using System;
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
    public UnityEngine.UI.Button btnGuestLogin;
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

        // Guest / Anonymous login button. Bound here so it lives alongside the Google button
        // without touching the existing Google sign-in wiring.
        if (btnGuestLogin != null)
        {
            btnGuestLogin.interactable = false;
            btnGuestLogin.onClick.RemoveListener(SignInAsGuest);
            btnGuestLogin.onClick.AddListener(SignInAsGuest);
        }

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
                if (btnGuestLogin != null)
                    btnGuestLogin.interactable = true;

                // Auto-login: if a Firebase session already exists (Google OR Guest/Anonymous),
                // skip the login screen and go straight to profile loading.
                if (TryAutoLogin())
                {
                    UpdateStatus("Welcome back...");
                    return;
                }

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
        StartGoogleSignInInteractive(forceAccountPicker: true, OnAuthenticationFinished);
#endif
    }

    /// <summary>
    /// Starts an interactive Google sign-in and, when requested, clears any cached account so
    /// Android shows the full account picker instead of silently reusing the last Gmail.
    /// </summary>
    void StartGoogleSignInInteractive(bool forceAccountPicker, Action<Task<GoogleSignInUser>> onFinished)
    {
        configuration.AccountName = null;
        configuration.ForceTokenRefresh = forceAccountPicker;
        GoogleSignIn.Configuration = configuration;

        if (forceAccountPicker)
        {
            try
            {
                GoogleSignIn.DefaultInstance.SignOut();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[Google] SignOut before account picker: " + ex.Message);
            }
        }

        GoogleSignIn.DefaultInstance.SignIn().ContinueWithOnMainThread(task =>
        {
            configuration.ForceTokenRefresh = false;
            configuration.AccountName = null;
            GoogleSignIn.Configuration = configuration;
            onFinished?.Invoke(task);
        });
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
        // Anonymous (guest) accounts have no DisplayName, so seed a random guest name.
        // Google accounts keep their existing DisplayName-based flow unchanged.
        bool isGuest = user.IsAnonymous;
        string defaultName = isGuest
            ? "Guest_" + UnityEngine.Random.Range(1000, 9999)
            : user.DisplayName;

        Debug.Log($"✅ Authenticated: {(isGuest ? "Guest" : user.DisplayName)} (anonymous={isGuest})");
        UpdateStatus(isGuest ? "Welcome, Guest" : "Welcome, " + user.DisplayName);

        string photonUserId = user.UserId;
        ConnectPhotonAfterLogin(photonUserId);

        if (PlayerProfileManager.Instance != null)
        {
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.ShowLoading("Fetching Profile...");

            PlayerProfileManager.Instance.CheckAndLoadUserProfile(photonUserId, defaultName);
        }
        else
        {
            Debug.LogError("PlayerProfileManager.Instance is null! Cannot open profile setup.");
            UpdateStatus("Profile setup unavailable.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  GUEST (ANONYMOUS) LOGIN  —  added alongside Google login, does not modify it.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Editor-only flag so the profile UI can show the guest "Bind Google" option while simulating.</summary>
    private static bool _editorSimGuest;

    /// <summary>
    /// True when the current session is an anonymous/guest account. Used by the profile panel to
    /// show "Bind with Google" instead of an email. Works under the editor simulation too.
    /// </summary>
    public static bool IsGuestSession()
    {
        try
        {
            FirebaseUser u = FirebaseAuth.DefaultInstance != null ? FirebaseAuth.DefaultInstance.CurrentUser : null;
            if (u != null) return u.IsAnonymous;
        }
        catch { /* Firebase not ready — fall through */ }

#if UNITY_EDITOR
        return _editorSimGuest;
#else
        return false;
#endif
    }

    /// <summary>Bound to the "Play as Guest" button. Signs in anonymously and reuses the profile flow.</summary>
    public void SignInAsGuest()
    {
        Debug.Log("Guest Login Button Clicked! Starting anonymous sign-in.");
        _loginFlowStarted = true;

        if (Application.internetReachability == NetworkReachability.NotReachable)
        {
            UpdateStatus("No Internet Connection!");
            return;
        }

#if UNITY_EDITOR
        Debug.LogWarning("Editor Mode: Simulating Guest Login...");
        SimulateGuestLogin();
#else
        if (!isFirebaseReady || auth == null)
        {
            UpdateStatus("Firebase Initializing...");
            InitializeFirebase();
            return;
        }

        UpdateStatus("Signing in as Guest...");
        if (NetworkManager.Instance != null)
            NetworkManager.Instance.ShowLoading("Signing in as Guest...");

        auth.SignInAnonymouslyAsync().ContinueWithOnMainThread(OnGuestSignInFinished);
#endif
    }

    private void OnGuestSignInFinished(Task<FirebaseUser> task)
    {
        if (task.IsFaulted || task.IsCanceled)
        {
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.HideLoading();

            Debug.LogError("❌ Guest Login Failed: " + task.Exception);
            UpdateStatus("Guest Login Failed");
            ShowLoginPanel();
            return;
        }

        FirebaseUser user = task.Result;
        if (user == null)
        {
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.HideLoading();

            UpdateStatus("Guest Login Failed");
            ShowLoginPanel();
            return;
        }

        CompleteLogin(user);
    }

    /// <summary>
    /// Auto-login: if a Firebase session already exists (returning Google OR Guest user), bypass the
    /// login screen and go straight to <see cref="PlayerProfileManager.CheckAndLoadUserProfile"/>.
    /// </summary>
    private bool TryAutoLogin()
    {
#if UNITY_EDITOR
        return false; // Editor uses simulated login; no persisted Firebase session to restore.
#else
        if (auth == null || auth.CurrentUser == null)
            return false;

        FirebaseUser user = auth.CurrentUser;
        Debug.Log($"[Auth] Auto-login: existing session found (anonymous={user.IsAnonymous}).");
        _loginFlowStarted = true;
        CompleteLogin(user);
        return true;
#endif
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  GUEST → GOOGLE BINDING  —  upgrades an anonymous account to a Google account.
    //  Same Firebase UserId is preserved, so all profile/progress data carries over.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Called from the Player Profile panel when a guest taps "Bind with Google".</summary>
    public void LinkGuestWithGoogle()
    {
        if (Application.internetReachability == NetworkReachability.NotReachable)
        {
            UpdateStatus("No Internet Connection!");
            return;
        }

#if UNITY_EDITOR
        Debug.LogWarning("Editor Mode: Simulating Google bind...");
        SimulateLinkGoogle();
#else
        if (auth == null || auth.CurrentUser == null)
        {
            Debug.LogError("[Link] No current user to link.");
            return;
        }

        if (!auth.CurrentUser.IsAnonymous)
        {
            Debug.Log("[Link] Account already linked to Google.");
            RefreshProfileBinder();
            return;
        }

        UpdateStatus("Binding Google account...");
        if (NetworkManager.Instance != null)
            NetworkManager.Instance.ShowLoading("Choose Google account...");

        StartGoogleSignInInteractive(forceAccountPicker: true, OnGoogleLinkAuthFinished);
#endif
    }

#if !UNITY_EDITOR
    private void OnGoogleLinkAuthFinished(Task<GoogleSignInUser> task)
    {
        if (task.IsCanceled)
        {
            if (NetworkManager.Instance != null) NetworkManager.Instance.HideLoading();
            Debug.LogWarning("[Link] Google account picker cancelled.");
            UpdateStatus("Bind Cancelled");
            return;
        }

        if (task.IsFaulted)
        {
            if (NetworkManager.Instance != null) NetworkManager.Instance.HideLoading();
            Debug.LogError("[Link] Google sign-in for binding failed: " + task.Exception);
            UpdateStatus("Bind Cancelled");
            return;
        }

        GoogleSignInUser googleUser = task.Result;
        if (googleUser == null || string.IsNullOrEmpty(googleUser.IdToken))
        {
            if (NetworkManager.Instance != null) NetworkManager.Instance.HideLoading();
            UpdateStatus("Auth Token Missing!");
            return;
        }

        if (auth == null || auth.CurrentUser == null)
        {
            if (NetworkManager.Instance != null) NetworkManager.Instance.HideLoading();
            return;
        }

        Credential credential = GoogleAuthProvider.GetCredential(googleUser.IdToken, null);
        auth.CurrentUser.LinkWithCredentialAsync(credential).ContinueWithOnMainThread(OnGoogleLinkFinished);
    }

    private void OnGoogleLinkFinished(Task<FirebaseUser> task)
    {
        if (NetworkManager.Instance != null) NetworkManager.Instance.HideLoading();

        if (task.IsFaulted || task.IsCanceled)
        {
            string message = "Bind Failed";
            if (task.Exception != null)
            {
                foreach (System.Exception ex in task.Exception.Flatten().InnerExceptions)
                {
                    if (ex is FirebaseAccountLinkException linkEx &&
                        linkEx.UserInfo != null &&
                        linkEx.UserInfo.Reason == AuthError.CredentialAlreadyInUse)
                    {
                        message = "This Google account is already linked to another profile.";
                        break;
                    }
                }
            }

            Debug.LogError("[Link] LinkWithCredential failed: " + task.Exception);
            UpdateStatus(message);
            return;
        }

        FirebaseUser user = task.Result;
        string email = user != null ? user.Email : null;
        if (!string.IsNullOrEmpty(email))
        {
            PlayerPrefs.SetString("PlayerEmail", email);
            PlayerPrefs.Save();
        }

        if (user != null && !string.IsNullOrEmpty(user.DisplayName))
            ApplyProfileName(user.DisplayName);

        Debug.Log("[Link] Guest successfully bound to Google: " + email);
        UpdateStatus("Google account linked!");
        RefreshProfileBinder();
    }
#endif

#if UNITY_EDITOR
    const string SimulatedGuestUserId = "simulate_guest_uid";

    private void SimulateGuestLogin()
    {
        _editorSimGuest = true;
        UpdateStatus("Signing in as Guest...");

        // Clear any cached email so the profile shows the "Bind with Google" option in the editor.
        PlayerPrefs.DeleteKey("PlayerEmail");
        PlayerPrefs.Save();

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.ShowLoading("Signing in as Guest...");

        ConnectPhotonAfterLogin(SimulatedGuestUserId);

        if (PlayerProfileManager.Instance != null)
        {
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.ShowLoading("Loading profile setup...");

            PlayerProfileManager.Instance.CheckAndLoadUserProfile(
                SimulatedGuestUserId, "Guest_" + UnityEngine.Random.Range(1000, 9999));
        }
        else
        {
            UpdateStatus("Profile manager missing");
        }
    }

    private void SimulateLinkGoogle()
    {
        _editorSimGuest = false;
        PlayerPrefs.SetString("PlayerEmail", "linked.guest@gmail.com");
        PlayerPrefs.Save();
        UpdateStatus("Google account linked!");
        Debug.Log("[Link] (Editor) Simulated Google bind. Email = linked.guest@gmail.com");
        RefreshProfileBinder();
    }
#endif

    private void RefreshProfileBinder()
    {
        PlayerProfilePanelBinder binder =
            UnityEngine.Object.FindFirstObjectByType<PlayerProfilePanelBinder>(FindObjectsInactive.Include);
        if (binder != null)
            binder.Refresh();
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
        _editorSimGuest = false;
        UpdateStatus("Signing in...");

        // Editor-only: no real Google account, so seed a placeholder email so the profile panel's
        // "Synced with" line can be verified. On device the real email from CompleteLogin is used.
        if (string.IsNullOrEmpty(PlayerPrefs.GetString("PlayerEmail", "")))
            PlayerPrefs.SetString("PlayerEmail", "editor.test@gmail.com");

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
        _editorSimGuest = false;
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
