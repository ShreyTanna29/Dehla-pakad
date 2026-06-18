using System.Collections;
using UnityEngine;
using Firebase;
using Firebase.Auth;
using Firebase.Database;
using Firebase.Extensions;
using Google;
using System.Threading.Tasks;
using Photon.Pun;
using Photon.Realtime;
using System.Collections.Generic;

/// <summary>
/// Handles Google Sign-In and Firebase Authentication.
/// Optimized for Android builds.
/// </summary>
[DefaultExecutionOrder(-200)]
public class GoogleLogin : MonoBehaviour
{
    public static GoogleLogin Instance;

    [Header("Panels")]
    public GameObject loginPanel;
    public GameObject homePanel;

    [Header("UI References")]
    public UnityEngine.UI.Button googleSignInButton;
    public TMPro.TMP_Text statusText;

    [Header("Profile UI Elements")]
    public TMPro.TMP_Text profileNameText;

    private FirebaseAuth auth;
    private GoogleSignInConfiguration configuration;

    private const string WEB_CLIENT_ID = "297172491992-ndjbhrt0d7h5o8ndf01nvvl0fpl15sii.apps.googleusercontent.com";
    private const string FirebaseDatabaseUrl = "https://dehla-pakad-a7859-default-rtdb.firebaseio.com/";
    private const float SimulatedLoginMinWait = 3.5f;
    private const float RealLoginMinWait = 2.5f;
    private const float PhotonReadyMaxWait = 12f;
    private bool isFirebaseReady = false;
    private bool _loginFlowStarted;
    public bool IsFirebaseReady { get { if(isFirebaseReady) {} return isFirebaseReady; } }

    /// <summary>True only after login + profile setup (or existing profile load) finished.</summary>
    public static bool HasCompletedLoginFlow { get; private set; }

    public static void NotifyLoginFlowComplete()
    {
        HasCompletedLoginFlow = true;
    }

    public static void ResetLoginFlow()
    {
        HasCompletedLoginFlow = false;
    }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        EnforceLoginScreen();
    }

    void EnforceLoginScreen()
    {
        ResetLoginFlow();
        ClearDisplayedProfileName();

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.HideHomeUntilLogin();

        if (homePanel != null)
            homePanel.SetActive(false);

        if (loginPanel != null)
        {
            loginPanel.SetActive(true);
            loginPanel.transform.SetAsLastSibling();
        }

        if (PlayerProfileManager.Instance != null)
            PlayerProfileManager.Instance.HideUntilLoginComplete();
    }

    void ClearDisplayedProfileName()
    {
        if (profileNameText != null)
            profileNameText.text = string.Empty;
    }

    void Start()
    {
        // Setup Google Sign-In Configuration
        configuration = new GoogleSignInConfiguration
        {
            WebClientId = WEB_CLIENT_ID,
            RequestIdToken = true,
            RequestEmail = true,
            UseGameSignIn = false // Better compatibility for general Google Login
        };

        if (googleSignInButton != null)
            googleSignInButton.interactable = false;

        InitializeFirebase();
    }

    private void InitializeFirebase()
    {
        UpdateStatus("Checking Dependencies...");
        Debug.Log("[Firebase] Checking Dependencies...");

        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            DependencyStatus status = task.Result;

            if (status == DependencyStatus.Available)
            {
                auth = FirebaseAuth.DefaultInstance;
                isFirebaseReady = true;
                GoogleSignIn.Configuration = configuration;

                if (googleSignInButton != null)
                    googleSignInButton.interactable = true;

                if (!_loginFlowStarted)
                    EnforceLoginScreen();
                UpdateStatus("Ready to Login");
            }
            else
            {
                string error = "Firebase Error: " + status;
                UpdateStatus(error);
                if (!_loginFlowStarted)
                    EnforceLoginScreen();
            }
        });
    }

    public void SignInWithGoogle() => OnGoogleLoginButtonClick();

    public void OnGoogleLoginButtonClick()
    {
        Debug.Log("Google Login Button Clicked! Starting explicit sign-in.");
        _loginFlowStarted = true;

        if (Application.internetReachability == NetworkReachability.NotReachable)
        {
            UpdateStatus("No Internet Connection!");
            return;
        }

#if UNITY_EDITOR
        Debug.LogWarning("Editor Mode: Simulating Login...");
        SimulateLogin();
#else
        if (!isFirebaseReady)
        {
            UpdateStatus("Firebase Initializing...");
            InitializeFirebase();
            return;
        }

        UpdateStatus("Signing in with Google...");
        GoogleSignIn.DefaultInstance.SignIn().ContinueWithOnMainThread(OnAuthenticationFinished);
#endif
    }

    private void OnAuthenticationFinished(Task<GoogleSignInUser> task)
    {
        if (task.IsFaulted)
        {
            Debug.LogError("Google Login Faulted.");
            string errorMessage = "Google Login Failed";
            if (task.Exception != null)
            {
                foreach (System.Exception ex in task.Exception.InnerExceptions)
                {
                    GoogleSignIn.SignInException gEx = ex as GoogleSignIn.SignInException;
                    if (gEx != null)
                    {
                        errorMessage = $"Error {gEx.Status}: {gEx.Message}";
                        Debug.LogError($"[Google Error] Code {gEx.Status}: {gEx.Message}");
                        if ((int)gEx.Status == 10)
                            Debug.LogError("TIP: Error 10 usually means WebClientId is wrong OR SHA-1 is missing in Firebase Console.");
                    }
                }
            }
            UpdateStatus(errorMessage);
            return;
        }

        if (task.IsCanceled)
        {
            Debug.LogWarning("Google Login Canceled.");
            UpdateStatus("Login Canceled");
            return;
        }

        GoogleSignInUser googleUser = task.Result;
        if (googleUser == null || string.IsNullOrEmpty(googleUser.IdToken))
        {
            UpdateStatus("Auth Token Missing!");
            Debug.LogError("Google Result is null or IdToken is empty.");
            return;
        }

        Debug.Log("Google Login Success. Authenticating with Firebase...");
        UpdateStatus("Firebase Auth...");

        Credential credential = GoogleAuthProvider.GetCredential(googleUser.IdToken, null);
        auth.SignInWithCredentialAsync(credential).ContinueWithOnMainThread(OnFirebaseLoginFinished);
    }

    void ApplyProfileName(string displayName)
    {
        if (string.IsNullOrEmpty(displayName)) return;

        PhotonNetwork.NickName = displayName;

        if (profileNameText != null)
        {
            profileNameText.text = displayName;
            Debug.Log($"[Profile] Name updated to: {displayName}");
        }
    }

    void SaveUserProfileToDatabase(string userId, string displayName)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(displayName)) return;

        FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference
            .Child("users").Child(userId).Child("username").SetValueAsync(displayName)
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted)
                    Debug.LogError("[Firebase DB] Username save failed: " + task.Exception);
                else
                    Debug.Log("[Firebase DB] Username saved for: " + userId);
            });
    }

    void ConnectPhotonAfterLogin(string photonUserId)
    {
        if (string.IsNullOrEmpty(photonUserId)) return;

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.ApplyPhotonAuthAndConnect(photonUserId);
        else if (NetworkManager.HasInternet())
        {
            PhotonNetwork.AuthValues = new AuthenticationValues(photonUserId);
            PhotonNetwork.ConnectUsingSettings();
        }
    }

    private void OnFirebaseLoginFinished(Task<FirebaseUser> task)
    {
        if (task.IsFaulted)
        {
            UpdateStatus("Firebase Error");
            Debug.LogError("❌ Firebase Auth Failed: " + task.Exception);
            ShowLoginPanel();
            return;
        }

        if (task.IsCanceled)
        {
            UpdateStatus("Firebase Canceled");
            ShowLoginPanel();
            return;
        }

        FirebaseUser user = task.Result;
        if (user == null)
        {
            ShowLoginPanel();
            return;
        }

        CompleteLogin(user);
    }

    private void CompleteLogin(FirebaseUser user)
    {
        Debug.Log("✅ Authenticated: " + user.DisplayName);
        UpdateStatus("Welcome, " + user.DisplayName);

        string photonUserId = user.UserId;
        ConnectPhotonAfterLogin(photonUserId);

        if (PlayerProfileManager.Instance != null)
        {
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.ShowLoading("Fetching Profile...");
                
            PlayerProfileManager.Instance.CheckAndLoadUserProfile(photonUserId, user.DisplayName);
        }
        else
        {
            Debug.LogError("PlayerProfileManager.Instance is null! Cannot open profile setup.");
            UpdateStatus("Profile setup unavailable.");
        }
    }

    void ShowLoginPanel() => EnforceLoginScreen();

    public void ShowHomePanel_Internal()
    {
        ShowHomePanel();
    }

    void ShowHomePanel()
    {
        if (loginPanel != null) loginPanel.SetActive(false);
        if (homePanel != null) homePanel.SetActive(true);
        
        // Ensure Home Panel is visible if we are fading
        var cg = homePanel.GetComponent<CanvasGroup>();
        if (cg != null) cg.alpha = 1f;

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.UpdateUIState(true);
    }

    void BeginHomeWhenReady(string loadingMessage, float minWaitSeconds)
    {
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.ShowLoading(loadingMessage);
            StartCoroutine(BeginHomeWhenReadyRoutine(minWaitSeconds));
        }
        else
        {
            TransitionToHome();
        }
    }

    IEnumerator BeginHomeWhenReadyRoutine(float minWaitSeconds)
    {
        yield return NetworkManager.Instance.WaitForPhotonReadyRoutine(
            minWaitSeconds,
            PhotonReadyMaxWait,
            msg =>
            {
                if (NetworkManager.Instance != null && NetworkManager.Instance.loadingText != null)
                    NetworkManager.Instance.loadingText.text = msg;
            });

        TransitionToHome();
    }

    private void TransitionToHome()
    {
        NotifyLoginFlowComplete();

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.HideLoading();

        ShowHomePanel();

        if (PlayerProfileSync.Instance != null)
            PlayerProfileSync.Instance.UpdateAllNames();

        if (PlayWithFriendsManager.Instance != null)
            PlayWithFriendsManager.Instance.EnsureFriendServicesStarted();

        Debug.Log("Login Flow Complete. Nickname: " + PhotonNetwork.NickName
            + $" | PhotonReady={PhotonNetwork.IsConnectedAndReady} | InLobby={PhotonNetwork.InLobby}");
    }

    const string SimulatedPhotonUserId = "simulate_editor_uid";

    private void SimulateLogin()
    {
        UpdateStatus("Signing in...");

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.ShowLoading("Signing in...");

        // Ensure we don't have stale local data if we want a fresh start
        // PlayerPrefs.DeleteKey("PlayerUsername"); 

        ConnectPhotonAfterLogin(SimulatedPhotonUserId);

        if (PlayerProfileManager.Instance != null)
        {
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.ShowLoading("Loading profile setup...");
            PlayerProfileManager.Instance.CheckAndLoadUserProfile(SimulatedPhotonUserId, null);
        }
        else
            UpdateStatus("Profile manager missing");
    }

    public void SimulateLoginME()
    {
        OnGoogleLoginButtonClick();
    }

    private void UpdateStatus(string message)
    {
        if (statusText != null) statusText.text = message;
    }

    public void SignOut()
    {
        _loginFlowStarted = false;
        ResetLoginFlow();

        if (auth != null) auth.SignOut();
        if (GoogleSignIn.DefaultInstance != null) GoogleSignIn.DefaultInstance.SignOut();

        if (PlayerProfileManager.Instance != null)
            PlayerProfileManager.Instance.HideUntilLoginComplete();

        ShowLoginPanel();
        UpdateStatus("Signed Out");
        Debug.Log("👋 User Signed Out");
    }
}
