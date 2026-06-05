using UnityEngine;
using Firebase;
using Firebase.Auth;
using Firebase.Extensions;
using Google;
using System.Threading.Tasks;
using Photon.Pun;
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

    private FirebaseAuth auth;
    private GoogleSignInConfiguration configuration;
    private bool isFirebaseReady = false;

    private const string WEB_CLIENT_ID = "297172491992-ndjbhrt0d7h5o8ndf01nvvl0fpl15sii.apps.googleusercontent.com";

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

                FirebaseUser currentUser = auth.CurrentUser;
                if (currentUser != null)
                {
                    OnFirebaseLoginFinished(Task.FromResult(currentUser));
                }
                else
                {
                    ShowLoginPanel();
                    if (GoogleSignIn.DefaultInstance != null)
                        GoogleSignIn.DefaultInstance.SignOut();
                    UpdateStatus("Ready to Login");
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

    public void SignInWithGoogle()
    {
        if (Application.internetReachability == NetworkReachability.NotReachable)
        {
            UpdateStatus("No Internet Connection!");
            return;
        }

#if UNITY_EDITOR
        Debug.LogWarning("⚠️ Editor Mode: Simulating Login...");
        SimulateLogin();
#else
        if (!isFirebaseReady)
        {
            UpdateStatus("Firebase Initializing...");
            InitializeFirebase(); // Attempt re-init
            return;
        }

        UpdateStatus("Signing in with Google...");
        Debug.Log("[Google] Starting Sign-In...");

        GoogleSignIn.DefaultInstance.SignIn().ContinueWithOnMainThread(OnGoogleLoginFinished);
#endif
    }

    private void OnGoogleLoginFinished(Task<GoogleSignInUser> task)
    {
        if (task.IsFaulted)
        {
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
                        
                        // Error 10 Help
                        if ((int)gEx.Status == 10) 
                        {
                            Debug.LogError("TIP: Error 10 usually means WebClientId is wrong OR SHA-1 is missing in Firebase Console.");
                        }
                    }
                }
            }
            UpdateStatus(errorMessage);
            return;
        }

        if (task.IsCanceled)
        {
            UpdateStatus("Login Canceled");
            Debug.LogWarning("[Google] Sign-In Canceled");
            return;
        }

        if (task.Result == null || string.IsNullOrEmpty(task.Result.IdToken))
        {
            UpdateStatus("Auth Token Missing!");
            Debug.LogError("❌ Google Result is null or IdToken is empty.");
            return;
        }

        Debug.Log("✅ Google Login Success. Authenticating with Firebase...");
        UpdateStatus("Firebase Auth...");
        
        Credential credential = GoogleAuthProvider.GetCredential(task.Result.IdToken, null);
        auth.SignInWithCredentialAsync(credential).ContinueWithOnMainThread(OnFirebaseLoginFinished);
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

        // Set Nickname for Photon
        string nick = !string.IsNullOrEmpty(user.DisplayName) ? user.DisplayName : 
                     (!string.IsNullOrEmpty(user.Email) ? user.Email.Split('@')[0] : "Player_" + Random.Range(1000, 9999));
        
        PhotonNetwork.NickName = nick;

        // Show Loading (GAME_LOGO) and then transition to home
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.ShowLoading("Loading Player Profile...");
            
            // Artificial delay to show logo as per user request
            Invoke(nameof(TransitionToHome), 1.5f);
        }
        else
        {
            TransitionToHome();
        }
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

    private void TransitionToHome()
    {
        ShowHomePanel();

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.HideLoading();

        if (PlayerProfileSync.Instance != null)
            PlayerProfileSync.Instance.UpdateAllNames();
            
        Debug.Log("🚀 Login Flow Complete. Nickname: " + PhotonNetwork.NickName);
    }

    private void SimulateLogin()
    {
        PhotonNetwork.NickName = "Roman Bhati"; 
        
        // Show Loading (GAME_LOGO) and then transition to home
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.ShowLoading("Simulating Login...");
            Invoke(nameof(TransitionToHome), 1.5f);
        }
        else
        {
            TransitionToHome();
        }
        UpdateStatus("Simulated Login Success");
    }

    public void SimulateLoginME()
    {
        PhotonNetwork.NickName = "Roman Bhati"; 
        
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.ShowLoading("Simulating Login...");
            Invoke(nameof(TransitionToHome), 1.5f);
        }
        else
        {
            TransitionToHome();
        }
        UpdateStatus("Simulated Login Success");
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
