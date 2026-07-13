using System;
using System.Collections;
using UnityEngine;
using Firebase;
using Firebase.Auth;
using Firebase.Extensions;
using Google;
using System.Threading.Tasks;
using Photon.Pun;
using Photon.Realtime;
using System.Collections.Generic;
using DG.Tweening;

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

    // private const string WEB_CLIENT_ID = "42510352079-sjbojvc7ho51477f16hr7vd6catvlscj.apps.googleusercontent.com";
    private const string WEB_CLIENT_ID = "391594214961-sl1o3653ias0johhdv653ndgg0gjfhtn.apps.googleusercontent.com";
    private const float SimulatedLoginMinWait = 3f;
    private const float RealLoginMinWait = 2.5f;
    private const float PhotonReadyMaxWait = 12f;
    private const string LoginTransitionLoadingMessage = "Simulating login...";
    private bool isFirebaseReady = false;
    private bool _loginFlowStarted;
    private bool _pendingGuestLogin;
    private bool _pendingEditorAuth;
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
        {
            NetworkManager.Instance.HideHomeUntilLogin();
            // TASK 3 — clear any lingering loading overlay so a failed/cancelled login (or a logout)
            // never leaves the user stuck on a loading screen when we return to the login panel.
            NetworkManager.Instance.EndLoginTransitionLoading();
        }

        if (homePanel != null)
            homePanel.SetActive(false);

        ShowLoginPanelUI();

        if (PlayerProfileManager.Instance != null)
            PlayerProfileManager.Instance.HideUntilLoginComplete();
    }

    /// <summary>
    /// Login panel ko smoothly (fade-in) dikhata hai. Agar Firebase mein user pehle se
    /// logged-in hai (auto-login), to login panel bilkul aane nahi deta — flash se bachne ke liye.
    /// </summary>
    public void ShowLoginPanelUI()
    {
        // BUG FIX 1: Auto-login active hai to login panel ko aage mat aane do.
        if (Firebase.Auth.FirebaseAuth.DefaultInstance != null &&
            Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser != null)
        {
            Debug.Log("[Login UI] Auto-login active, preventing flash.");
            return;
        }

        if (loginPanel == null) return;

        bool wasHidden = !loginPanel.activeSelf;
        loginPanel.SetActive(true);
        loginPanel.transform.SetAsLastSibling();

        CanvasGroup cg = loginPanel.GetComponent<CanvasGroup>();
        if (cg == null) cg = loginPanel.AddComponent<CanvasGroup>();

        // BUG FIX 2: Purani atki hui animation kill karo taaki jhatka na lage.
        cg.DOKill();
        cg.interactable = true;
        cg.blocksRaycasts = true;

        // Sirf tab fade-in karo jab panel pehle hidden tha (dubara call par jhatka na ho).
        if (wasHidden || cg.alpha < 0.99f)
        {
            cg.alpha = 0f;
            cg.DOFade(1f, 0.4f).SetUpdate(true);
        }
    }

    void ClearDisplayedProfileName()
    {
        if (profileNameText != null)
            profileNameText.text = string.Empty;
    }

    void Start()
    {
        ResolveLoginButtons();

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

    void ResolveLoginButtons()
    {
        if (googleSignInButton == null && UiSafeLookup.TryGetButton("Button_GoogleLogin", out UnityEngine.UI.Button googleBtn))
            googleSignInButton = googleBtn;

        if (btnGuestLogin == null && UiSafeLookup.TryGetButton("Button_GuestLogin", out UnityEngine.UI.Button guestBtn))
            btnGuestLogin = guestBtn;
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

                if (_pendingEditorAuth)
                {
                    _pendingEditorAuth = false;
#if UNITY_EDITOR
                    EditorSignInAnonymouslyForFirestore();
#endif
                    return;
                }

                if (_pendingGuestLogin)
                {
                    _pendingGuestLogin = false;
                    BeginGuestSignIn();
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

    /// <summary>
    /// TASK 3 — Safe wrapper around the Google sign-in entry point.
    /// Google login can fail silently (cancelled picker, missing SHA-1 / Error 10,
    /// Firebase not ready, or an unexpected native exception). This wrapper guarantees
    /// that any *synchronous* failure is logged and surfaced to the user instead of
    /// disappearing. Asynchronous SDK failures are already handled inside
    /// OnAuthenticationFinished() via UpdateStatus().
    /// Hook your Google Sign-In button's OnClick to this method instead of OnGoogleLoginButtonClick.
    /// </summary>
    public void AttemptGoogleLogin()
    {
        try
        {
            OnGoogleLoginButtonClick();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[GoogleLogin] AttemptGoogleLogin failed: {ex}");
            UpdateStatus("Login Failed, try again.");
            _loginFlowStarted = false;
            SetLoginButtonsInteractable(true);
        }
    }

    void ShowLoginLoading(string message = null)
    {
        if (NetworkManager.Instance != null)
            NetworkManager.Instance.BeginLoginTransitionLoading(message ?? LoginTransitionLoadingMessage);
    }

    void EndLoginLoading()
    {
        if (NetworkManager.Instance != null)
            NetworkManager.Instance.EndLoginTransitionLoading();
    }

    public void OnGoogleLoginButtonClick()
    {
        Debug.Log("Google Login Button Clicked! Starting explicit sign-in.");
        _loginFlowStarted = true;
        SetLoginButtonsInteractable(false);

        if (Application.internetReachability == NetworkReachability.NotReachable)
        {
            UpdateStatus("No Internet Connection!");
            _loginFlowStarted = false;
            SetLoginButtonsInteractable(true);
            return;
        }

        // YAHAN SE HATA DIYA: ShowLoginLoading(); taaki background mein loading na aaye

#if UNITY_EDITOR
        ShowLoginLoading(); // Editor mein turant dikha do
        SimulateLogin();
#else
        if (!isFirebaseReady)
        {
            UpdateStatus("Firebase Initializing...");
            InitializeFirebase();
            _loginFlowStarted = false;
            SetLoginButtonsInteractable(true);
            return;
        }

        UpdateStatus("Choose Google Account...");
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
        // YAHAN ADD KIYA: Jab user account select kar le, TAB loading screen aaye
        ShowLoginLoading();

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
            _loginFlowStarted = false;
            SetLoginButtonsInteractable(true);
            EndLoginLoading();
            return;
        }

        if (task.IsCanceled)
        {
            Debug.LogWarning("Google Login Canceled.");
            UpdateStatus("Login Canceled");
            _loginFlowStarted = false;
            SetLoginButtonsInteractable(true);
            EndLoginLoading();
            return;
        }

        GoogleSignInUser googleUser = task.Result;
        if (googleUser == null || string.IsNullOrEmpty(googleUser.IdToken))
        {
            UpdateStatus("Auth Token Missing!");
            Debug.LogError("Google Result is null or IdToken is empty.");
            _loginFlowStarted = false;
            SetLoginButtonsInteractable(true);
            EndLoginLoading();
            return;
        }

        Debug.Log("Google Login Success. Authenticating with Firebase...");
        UpdateStatus("Firebase Auth...");

        Credential credential = GoogleAuthProvider.GetCredential(googleUser.IdToken, null);
        auth.SignInAndRetrieveDataWithCredentialAsync(credential).ContinueWithOnMainThread(OnFirebaseLoginFinished);
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

        FirestoreUsersService.MergeUser(userId, new Dictionary<string, object>
        {
            { FirestoreUsersService.FieldUsername, displayName },
            { FirestoreUsersService.FieldIsBot, false },
            { FirestoreUsersService.FieldIsActiveNow, true }
        }, ok =>
        {
            if (!ok)
                Debug.LogWarning("[Firestore] Username save failed for: " + userId);
            else
                Debug.Log("[Firestore] Username saved for: " + userId);
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

    static FirebaseUser GetUserFromAuthTask(Task<AuthResult> task)
    {
        if (task == null || task.IsFaulted || task.IsCanceled) return null;
        return task.Result != null ? task.Result.User : null;
    }

    private void OnFirebaseLoginFinished(Task<AuthResult> task)
    {
        if (task.IsFaulted)
        {
            UpdateStatus("Firebase Error");
            Debug.LogError("❌ Firebase Auth Failed: " + task.Exception);
            _loginFlowStarted = false;
            SetLoginButtonsInteractable(true);
            EndLoginLoading();
            ShowLoginPanel();
            return;
        }

        if (task.IsCanceled)
        {
            UpdateStatus("Firebase Canceled");
            _loginFlowStarted = false;
            SetLoginButtonsInteractable(true);
            EndLoginLoading();
            ShowLoginPanel();
            return;
        }

        FirebaseUser user = GetUserFromAuthTask(task);
        if (user == null)
        {
            _loginFlowStarted = false;
            SetLoginButtonsInteractable(true);
            EndLoginLoading();
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
#if UNITY_EDITOR
        // Editor "Google" login uses anonymous Auth so Firestore gets a real token,
        // but the UI should still behave like a linked Google session.
        if (!_editorSimGuest)
            isGuest = false;
#endif

        ShowLoginLoading();

        if (!string.IsNullOrEmpty(user.Email))
        {
            PlayerPrefs.SetString("PlayerEmail", user.Email);
            PlayerPrefs.Save();
        }

        string defaultName;
        if (isGuest)
        {
            PlayerPrefs.DeleteKey("PlayerEmail");
            PlayerPrefs.Save();
            defaultName = "Guest" + UnityEngine.Random.Range(1000, 9999);
        }
        else if (!string.IsNullOrWhiteSpace(user.DisplayName))
            defaultName = user.DisplayName.Trim();
        else
        {
            string email = user.Email;
#if UNITY_EDITOR
            if (string.IsNullOrEmpty(email))
                email = PlayerPrefs.GetString("PlayerEmail", "");
#endif
            defaultName = PlayerProfileManager.GenerateDefaultUsername(email);
        }

        Debug.Log($"✅ Authenticated: {(isGuest ? "Guest" : defaultName)} (anonymous={isGuest})");
        UpdateStatus(isGuest ? "Welcome, Guest" : "Welcome, " + defaultName);

        string photonUserId = user.UserId;
        ConnectPhotonAfterLogin(photonUserId);

        if (PlayerProfileManager.Instance != null)
        {
            // ShowLoading suppressed during login flow per user request.
            // if (NetworkManager.Instance != null)
            //     NetworkManager.Instance.ShowLoading("Fetching Profile...");

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
            if (u != null)
            {
#if UNITY_EDITOR
                // Editor Google login uses anonymous Auth for Firestore tokens, but is not a guest.
                if (u.IsAnonymous) return _editorSimGuest;
#endif
                return u.IsAnonymous;
            }
        }
        catch { /* Firebase not ready — fall through */ }

#if UNITY_EDITOR
        return _editorSimGuest;
#else
        return false;
#endif
    }

    /// <summary>Bound to the "Play as Guest" button. Signs in anonymously and reuses the profile flow.</summary>
    public void SignInAsGuest() => AttemptGuestLogin();

    public void AttemptGuestLogin()
    {
        try
        {
            BeginGuestSignIn();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[GoogleLogin] AttemptGuestLogin failed: {ex}");
            UpdateStatus("Guest Login Failed, try again.");
            _loginFlowStarted = false;
            _pendingGuestLogin = false;
            SetLoginButtonsInteractable(true);
        }
    }

    void BeginGuestSignIn()
    {
        Debug.Log("[LoginFlow] Guest Login Button Clicked! Starting anonymous sign-in.");
        _loginFlowStarted = true;
        SetLoginButtonsInteractable(false);

        if (Application.internetReachability == NetworkReachability.NotReachable)
        {
            UpdateStatus("No Internet Connection!");
            _loginFlowStarted = false;
            SetLoginButtonsInteractable(true);
            return;
        }

        ShowLoginLoading();

#if UNITY_EDITOR
        SimulateGuestLogin();
#else
        if (!isFirebaseReady || auth == null)
        {
            _pendingGuestLogin = true;
            UpdateStatus("Firebase Initializing...");
            InitializeFirebase();
            return;
        }

        UpdateStatus("Signing in as Guest...");
        // ShowLoading suppressed during login flow per user request.
        // if (NetworkManager.Instance != null)
        //     NetworkManager.Instance.ShowLoading("Signing in as Guest...");

        if (auth.CurrentUser != null && auth.CurrentUser.IsAnonymous)
        {
            Debug.Log($"[Auth] Reusing existing anonymous user {auth.CurrentUser.UserId}");
            CompleteLogin(auth.CurrentUser);
            return;
        }

        auth.SignInAnonymouslyAsync().ContinueWithOnMainThread(OnGuestSignInFinished);
#endif
    }

    void ResetGuestLoginFailure()
    {
        _loginFlowStarted = false;
        _pendingGuestLogin = false;
        SetLoginButtonsInteractable(true);
    }

    private void OnGuestSignInFinished(Task<AuthResult> task)
    {
        if (task.IsFaulted || task.IsCanceled)
        {
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.EndLoginTransitionLoading();

            Debug.LogError("❌ Guest Login Failed: " + task.Exception);
            UpdateStatus("Guest Login Failed");
            ResetGuestLoginFailure();
            ShowLoginPanel();
            return;
        }

        FirebaseUser user = GetUserFromAuthTask(task);
        if (user == null)
        {
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.EndLoginTransitionLoading();

            UpdateStatus("Guest Login Failed");
            ResetGuestLoginFailure();
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
        if (auth == null || auth.CurrentUser == null)
            return false;

        FirebaseUser user = auth.CurrentUser;
        Debug.Log($"[Auth] Auto-login: existing session found (anonymous={user.IsAnonymous}, uid={user.UserId}).");
        _loginFlowStarted = true;
#if UNITY_EDITOR
        _editorSimGuest = user.IsAnonymous && string.IsNullOrEmpty(PlayerPrefs.GetString("PlayerEmail", ""));
#endif
        CompleteLogin(user);
        return true;
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
        // ShowLoading suppressed during login flow per user request.
        // if (NetworkManager.Instance != null)
        //     NetworkManager.Instance.ShowLoading("Choose Google account...");

        StartGoogleSignInInteractive(forceAccountPicker: true, OnGoogleLinkAuthFinished);
#endif
    }

#if !UNITY_EDITOR
    private void OnGoogleLinkAuthFinished(Task<GoogleSignInUser> task)
    {
        if (task.IsCanceled)
        {
            if (NetworkManager.Instance != null) NetworkManager.Instance.EndLoginTransitionLoading();
            Debug.LogWarning("[Link] Google account picker cancelled.");
            UpdateStatus("Bind Cancelled");
            return;
        }

        if (task.IsFaulted)
        {
            if (NetworkManager.Instance != null) NetworkManager.Instance.EndLoginTransitionLoading();
            Debug.LogError("[Link] Google sign-in for binding failed: " + task.Exception);
            UpdateStatus("Bind Cancelled");
            return;
        }

        GoogleSignInUser googleUser = task.Result;
        if (googleUser == null || string.IsNullOrEmpty(googleUser.IdToken))
        {
            if (NetworkManager.Instance != null) NetworkManager.Instance.EndLoginTransitionLoading();
            UpdateStatus("Auth Token Missing!");
            return;
        }

        if (auth == null || auth.CurrentUser == null)
        {
            if (NetworkManager.Instance != null) NetworkManager.Instance.EndLoginTransitionLoading();
            return;
        }

        Credential credential = GoogleAuthProvider.GetCredential(googleUser.IdToken, null);
        auth.CurrentUser.LinkWithCredentialAsync(credential).ContinueWithOnMainThread(OnGoogleLinkFinished);
    }

    private void OnGoogleLinkFinished(Task<AuthResult> task)
    {
        if (NetworkManager.Instance != null) NetworkManager.Instance.EndLoginTransitionLoading();

        if (task.IsFaulted || task.IsCanceled)
        {
            string message = "Bind Failed";
            if (task.Exception != null)
            {
                foreach (System.Exception ex in task.Exception.Flatten().InnerExceptions)
                {
                    if (ex is FirebaseAccountLinkException linkEx &&
                        linkEx.ErrorCode == (int)AuthError.CredentialAlreadyInUse)
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

        FirebaseUser user = GetUserFromAuthTask(task);
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
    /// <summary>
    /// Editor login must use real Firebase Auth (anonymous). Fake UIDs have no auth token,
    /// so Firestore rejects every read/write with "Missing or insufficient permissions."
    /// </summary>
    void EditorSignInAnonymouslyForFirestore()
    {
        if (!isFirebaseReady || auth == null)
        {
            _pendingEditorAuth = true;
            UpdateStatus("Firebase Initializing...");
            InitializeFirebase();
            return;
        }

        // Reuse the existing Auth session. Calling SignInAnonymously again creates a NEW uid
        // and makes Firestore look like a brand-new account (profile setup every login).
        if (auth.CurrentUser != null)
        {
            Debug.Log($"[Auth] Reusing existing Firebase user {auth.CurrentUser.UserId}");
            CompleteLogin(auth.CurrentUser);
            return;
        }

        UpdateStatus(_editorSimGuest ? "Signing in as Guest..." : "Signing in...");
        auth.SignInAnonymouslyAsync().ContinueWithOnMainThread(OnGuestSignInFinished);
    }

    private void SimulateGuestLogin()
    {
        _editorSimGuest = true;
        PlayerPrefs.DeleteKey("PlayerEmail");
        PlayerPrefs.Save();
        EditorSignInAnonymouslyForFirestore();
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
            // ShowLoading suppressed during login flow per user request.
            // NetworkManager.Instance.ShowLoading(loadingMessage);
            StartCoroutine(BeginHomeWhenReadyRoutine(minWaitSeconds));
        }
        else
        {
            TransitionToHome();
        }
    }

    /// <summary>
    /// Waits for the login/simulating panel minimum display time (and Photon when available),
    /// then runs <paramref name="openHomePanels"/> — typically PlayerProfileManager home transition.
    /// </summary>
    public void OpenHomeAfterLoginReady(System.Action openHomePanels)
    {
        float minWait =
#if UNITY_EDITOR
            SimulatedLoginMinWait;
#else
            RealLoginMinWait;
#endif

        if (NetworkManager.Instance != null)
            StartCoroutine(OpenHomeAfterLoginReadyRoutine(minWait, openHomePanels));
        else
            StartCoroutine(OpenHomeAfterLoginReadySimpleRoutine(minWait, openHomePanels));
    }

    IEnumerator OpenHomeAfterLoginReadyRoutine(float minWaitSeconds, System.Action openHomePanels)
    {
        ShowLoginLoading();

        yield return NetworkManager.Instance.WaitForPhotonReadyRoutine(
            minWaitSeconds,
            PhotonReadyMaxWait,
            null);

        openHomePanels?.Invoke();
        yield return null;
        EndLoginLoading();
    }

    IEnumerator OpenHomeAfterLoginReadySimpleRoutine(float minWaitSeconds, System.Action openHomePanels)
    {
        yield return new WaitForSecondsRealtime(minWaitSeconds);
        openHomePanels?.Invoke();
        yield return null;
        EndLoginLoading();
    }

    IEnumerator BeginHomeWhenReadyRoutine(float minWaitSeconds)
    {
        yield return NetworkManager.Instance.WaitForPhotonReadyRoutine(
            minWaitSeconds,
            PhotonReadyMaxWait,
            null);

        TransitionToHome();
    }

    private void TransitionToHome()
    {
        NotifyLoginFlowComplete();
        ShowHomePanel();

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.EndLoginTransitionLoading();

        if (PlayerProfileSync.Instance != null)
            PlayerProfileSync.Instance.UpdateAllNames();

        if (PlayWithFriendsManager.Instance != null)
            PlayWithFriendsManager.Instance.EnsureFriendServicesStarted();

        Debug.Log("Login Flow Complete. Nickname: " + PhotonNetwork.NickName
            + $" | PhotonReady={PhotonNetwork.IsConnectedAndReady} | InLobby={PhotonNetwork.InLobby}");
    }

    const string SimulatedPhotonUserId = "simulate_editor_uid";

#if UNITY_EDITOR
    private void SimulateLogin()
    {
        _editorSimGuest = false;
        UpdateStatus("Signing in...");

        // Editor-only: no real Google account, so seed a placeholder email so the profile panel's
        // "Synced with" line can be verified. On device the real email from CompleteLogin is used.
        if (string.IsNullOrEmpty(PlayerPrefs.GetString("PlayerEmail", "")))
        {
            PlayerPrefs.SetString("PlayerEmail", "editor.test@gmail.com");
            PlayerPrefs.Save();
        }

        // Real anonymous Auth token is required for Firestore rules (request.auth != null).
        EditorSignInAnonymouslyForFirestore();
    }
#endif

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
        SetLoginButtonsInteractable(true);

        if (auth != null) auth.SignOut();
        if (GoogleSignIn.DefaultInstance != null) GoogleSignIn.DefaultInstance.SignOut();

        if (PlayerProfileManager.Instance != null)
            PlayerProfileManager.Instance.HideUntilLoginComplete();

        ShowLoginPanel();
        UpdateStatus("Signed Out");
        Debug.Log("👋 User Signed Out");
    }

    void SetLoginButtonsInteractable(bool interactable)
    {
        if (googleSignInButton != null) googleSignInButton.interactable = interactable;
        if (btnGuestLogin != null) btnGuestLogin.interactable = interactable;
    }
}
