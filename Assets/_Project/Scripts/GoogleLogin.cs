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
    public bool IsFirebaseReady { get { if(isFirebaseReady) {} return isFirebaseReady; } }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        ShowLoginPanel();
        if (NetworkManager.Instance != null)
            NetworkManager.Instance.HideHomeUntilLogin();
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

                if (auth.CurrentUser != null)
                {
                    Debug.Log("[Login] Persistent User Found! Auto-login bypass...");
                    CompleteLogin(auth.CurrentUser);
                }
                else
                {
                    ShowLoginPanel();
                    UpdateStatus("Ready to Login");

                    GoogleSignIn.DefaultInstance.SignInSilently().ContinueWithOnMainThread(silentTask =>
                    {
                        if (!silentTask.IsFaulted && !silentTask.IsCanceled && silentTask.Result != null)
                        {
                            Credential credential = GoogleAuthProvider.GetCredential(silentTask.Result.IdToken, null);
                            auth.SignInWithCredentialAsync(credential).ContinueWithOnMainThread(OnFirebaseLoginFinished);
                        }
                    });
                }
            }
            else
            {
                string error = "Firebase Error: " + status;
                UpdateStatus(error);
                ShowLoginPanel();
            }
        });
    }

    public void SignInWithGoogle() => OnGoogleLoginButtonClick();

    public void OnGoogleLoginButtonClick()
    {
        Debug.Log("Google Login Button Clicked! Starting explicit sign-in.");

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

        string googleAccountName = googleUser.DisplayName;
        if (!string.IsNullOrEmpty(googleAccountName))
            ApplyProfileName(googleAccountName);

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

        string nick = !string.IsNullOrEmpty(user.DisplayName) ? user.DisplayName :
                     (!string.IsNullOrEmpty(user.Email) ? user.Email.Split('@')[0] : "Player_" + Random.Range(1000, 9999));

        string photonUserId = user.UserId;
        ApplyProfileName(nick);
        ConnectPhotonAfterLogin(photonUserId);
        SaveUserProfileToDatabase(photonUserId, nick);

        BeginHomeWhenReady("Loading Player Profile...", RealLoginMinWait);
    }

    void ShowLoginPanel()
    {
        if (loginPanel != null) loginPanel.SetActive(true);
        if (homePanel != null) homePanel.SetActive(false);
        if (NetworkManager.Instance != null)
            NetworkManager.Instance.HideHomeUntilLogin();
    }

    void ShowHomePanel()
    {
        if (loginPanel != null) loginPanel.SetActive(false);
        if (homePanel != null) homePanel.SetActive(true);
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
        ShowHomePanel();

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.HideLoading();

        if (PlayerProfileSync.Instance != null)
            PlayerProfileSync.Instance.UpdateAllNames();

        if (PlayWithFriendsManager.Instance != null)
            PlayWithFriendsManager.Instance.EnsureFriendServicesStarted();

        Debug.Log("Login Flow Complete. Nickname: " + PhotonNetwork.NickName
            + $" | PhotonReady={PhotonNetwork.IsConnectedAndReady} | InLobby={PhotonNetwork.InLobby}");
    }

    const string SimulatedPhotonUserId = "simulate_roman_bhati_uid";

    private void SimulateLogin()
    {
        ApplyProfileName("Roman Bhati");
        ConnectPhotonAfterLogin(SimulatedPhotonUserId);
        SaveUserProfileToDatabase(SimulatedPhotonUserId, "Roman Bhati");
        UpdateStatus("Simulated Login Success");
        BeginHomeWhenReady("Simulating Login...\nConnecting to server...", SimulatedLoginMinWait);
    }

    public void SimulateLoginME()
    {
        ApplyProfileName("Roman Bhati");
        ConnectPhotonAfterLogin(SimulatedPhotonUserId);
        SaveUserProfileToDatabase(SimulatedPhotonUserId, "Roman Bhati");
        UpdateStatus("Simulated Login Success");
        BeginHomeWhenReady("Simulating Login...\nConnecting to server...", SimulatedLoginMinWait);
    }

    private void UpdateStatus(string message)
    {
        if (statusText != null) statusText.text = message;
    }

    public void SignOut()
    {
        if (auth != null) auth.SignOut();
        if (GoogleSignIn.DefaultInstance != null) GoogleSignIn.DefaultInstance.SignOut();

        ShowLoginPanel();
        UpdateStatus("Signed Out");
        Debug.Log("👋 User Signed Out");
    }
}
