using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;
using TMPro;
using System.Collections.Generic;
using Firebase;
using Firebase.Auth;
using Firebase.Firestore;
using Firebase.Extensions;
using Google;
using Photon.Pun;
using DG.Tweening;

[DefaultExecutionOrder(-150)]
public class PlayerProfileManager : MonoBehaviour
{
    public static PlayerProfileManager Instance;

    [Header("UI Panels")]
    public GameObject panelProfileSetup;
    public GameObject panelHome;

    [Header("Profile Setup References")]
    public UnityEngine.UI.Button[] avatarButtons;
    public Sprite[] profileSprites;
    public TMPro.TMP_InputField inputPlayerName;

    [FormerlySerializedAs("buttonSaveProfile")]
    public UnityEngine.UI.Button btnEnterGame;

    public TMPro.TMP_Text profileSetupErrorText;

    [Header("Home Profile UI")]
    [FormerlySerializedAs("homeProfileAvatar")]
    public UnityEngine.UI.Image imgHomeProfileAvatar;

    [FormerlySerializedAs("homeProfileName")]
    public TMPro.TMP_Text textHomeProfileName;

    [Header("Profile View UI (Stats)")]
    public GameObject panelPlayerProfile;

    [Tooltip("Keeps the entire Player Profile panel exactly as authored in the Editor — no runtime text, image, layout, or stat overrides.")]
    [SerializeField] private bool preserveManualProfileLayout = true;

    public TMPro.TMP_Text textProfileName;
    public TMPro.TMP_Text textCoins;
    public UnityEngine.UI.Image imgCurrentAvatar;
    public UnityEngine.UI.Button btnCloseProfile;
    public UnityEngine.UI.Button buttonEditProfile;

    [Header("Stat Texts")]
    public TMPro.TMP_Text textTotalMatches;
    public TMPro.TMP_Text textWins;
    public TMPro.TMP_Text textWinRatio;
    public TMPro.TMP_Text textTotalKOT;

    private int selectedAvatarIndex = -1;
    private string _pendingProfileUserId;
    private string _pendingDefaultUsername;
    private bool _isEditingExistingProfile;

    private const string PREFS_USERNAME = "PlayerUsername";
    private const string PREFS_AVATAR_INDEX = "PlayerAvatarIndex";

    // Photon custom-property key used to sync each player's chosen avatar to all clients.
    public const string PROP_AVATAR = "av";

    /// <summary>The avatar index the local user selected during profile setup (-1 if none).</summary>
    public static int GetSavedAvatarIndex() => PlayerPrefs.GetInt(PREFS_AVATAR_INDEX, -1);

    /// <summary>Username saved during profile setup (PlayerPrefs + Firebase).</summary>
    public static string GetSavedUsername() => PlayerPrefs.GetString(PREFS_USERNAME, string.Empty).Trim();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        ResolveUiReferences();
        HideUntilLoginComplete();
    }

    private void Start()
    {
        ResolveUiReferences();

        if (btnEnterGame != null)
            btnEnterGame.onClick.AddListener(OnClick_EnterGame);

        if (btnCloseProfile != null)
            btnCloseProfile.onClick.AddListener(() => panelPlayerProfile.SetActive(false));

        if (buttonEditProfile != null)
            buttonEditProfile.onClick.AddListener(OnEditProfileClicked);

        if (inputPlayerName != null)
        {
            inputPlayerName.characterLimit = 0;
            inputPlayerName.onValidateInput += ValidateUsernameCharacter;
            inputPlayerName.onValueChanged.AddListener(_ => ClearProfileSetupError());
        }

        EnsureProfileSetupErrorText();
        SetupAvatarButtons();
        WireHomeProfileAvatarClick();
    }

    void WireHomeProfileAvatarClick()
    {
        ResolveUiReferences();
        if (imgHomeProfileAvatar == null)
        {
            Debug.LogWarning("[Profile] Home Profile_Image not found — profile click disabled.");
            return;
        }

        Button btn = imgHomeProfileAvatar.GetComponent<Button>();
        if (btn == null)
        {
            btn = imgHomeProfileAvatar.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.ColorTint;
        }

        if (btn.targetGraphic == null)
            btn.targetGraphic = imgHomeProfileAvatar;

        btn.interactable = true;
        btn.onClick.RemoveListener(OnHomeProfileAvatarClicked);
        btn.onClick.AddListener(OnHomeProfileAvatarClicked);
    }

    void OnHomeProfileAvatarClicked()
    {
        OpenPlayerProfile();
    }

    char ValidateUsernameCharacter(string text, int charIndex, char addedChar)
    {
        return char.IsLetterOrDigit(addedChar) ? addedChar : '\0';
    }

    void ClearProfileSetupError()
    {
        if (profileSetupErrorText != null)
            profileSetupErrorText.text = string.Empty;
    }

    void ShowProfileSetupError(string message)
    {
        Debug.LogWarning("[Profile] " + message);
        EnsureProfileSetupErrorText();
        if (profileSetupErrorText != null)
        {
            profileSetupErrorText.text = message;
            profileSetupErrorText.gameObject.SetActive(true);
            return;
        }

        if (inputPlayerName != null && inputPlayerName.placeholder is TMP_Text placeholder)
            placeholder.text = message;
    }

    void EnsureProfileSetupErrorText()
    {
        if (profileSetupErrorText != null) return;
        if (panelProfileSetup == null) return;

        foreach (TMP_Text label in panelProfileSetup.GetComponentsInChildren<TMP_Text>(true))
        {
            if (label == null) continue;
            if (label.gameObject.name == "Txt_ProfileSetupError" || label.gameObject.name == "ProfileSetupError")
            {
                profileSetupErrorText = label;
                return;
            }
        }

        Transform anchor = inputPlayerName != null ? inputPlayerName.transform : panelProfileSetup.transform;
        GameObject go = new GameObject("Txt_ProfileSetupError", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(anchor.parent != null ? anchor.parent : panelProfileSetup.transform, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        RectTransform anchorRt = anchor as RectTransform;
        if (anchorRt != null)
        {
            rt.anchorMin = anchorRt.anchorMin;
            rt.anchorMax = anchorRt.anchorMax;
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(anchorRt.sizeDelta.x, 36f);
            rt.anchoredPosition = anchorRt.anchoredPosition + new Vector2(0f, -anchorRt.sizeDelta.y * 0.5f - 28f);
        }
        else
        {
            rt.sizeDelta = new Vector2(600f, 36f);
            rt.anchoredPosition = new Vector2(0f, -220f);
        }

        profileSetupErrorText = go.GetComponent<TextMeshProUGUI>();
        profileSetupErrorText.fontSize = 22f;
        profileSetupErrorText.color = new Color(0.95f, 0.25f, 0.15f, 1f);
        profileSetupErrorText.alignment = TextAlignmentOptions.Center;
        profileSetupErrorText.fontStyle = FontStyles.Bold;
        profileSetupErrorText.raycastTarget = false;
        profileSetupErrorText.text = string.Empty;
    }

    void SyncProfilePanelIdentity()
    {
        if (panelPlayerProfile == null) return;

        PlayerProfilePanelBinder binder = panelPlayerProfile.GetComponentInChildren<PlayerProfilePanelBinder>(true);
        if (binder != null)
            binder.SyncIdentityFields();
        else
        {
            string username = PlayerPrefs.GetString(PREFS_USERNAME, string.Empty);
            if (string.IsNullOrEmpty(username)) return;
            foreach (TMP_Text label in panelPlayerProfile.GetComponentsInChildren<TMP_Text>(true))
            {
                if (label.gameObject.name == "Side_Name" || label.gameObject.name == "Text_ProfileName")
                    label.text = username;
            }
        }
    }

    public void HideUntilLoginComplete()
    {
        ResolveUiReferences();

        if (panelProfileSetup != null)
            panelProfileSetup.SetActive(false);

        if (panelPlayerProfile != null)
            panelPlayerProfile.SetActive(false);

        GameObject home = ResolveHomePanel();
        if (home != null)
            home.SetActive(false);

        ClearProfileSetupError();
        ClearHomeProfileDisplay();
    }

    void ClearHomeProfileDisplay()
    {
        if (textHomeProfileName != null)
            textHomeProfileName.text = string.Empty;

        if (GoogleLogin.Instance != null)
        {
            if (GoogleLogin.Instance.profileNameText != null)
                GoogleLogin.Instance.profileNameText.text = string.Empty;
        }
    }

    void ResolveUiReferences()
    {
        if (panelHome == null)
            panelHome = ResolveHomePanel();

        if (textHomeProfileName == null && GoogleLogin.Instance != null && GoogleLogin.Instance.profileNameText != null)
            textHomeProfileName = GoogleLogin.Instance.profileNameText;

        ResolveHomeProfileAvatar();
    }

    void ResolveHomeProfileAvatar()
    {
        GameObject home = ResolveHomePanel();
        if (home != null)
        {
            foreach (Image img in home.GetComponentsInChildren<Image>(true))
            {
                if (img.gameObject.name == "Profile_Image")
                {
                    imgHomeProfileAvatar = img;
                    return;
                }
            }
        }

        if (imgHomeProfileAvatar == null && UiSafeLookup.TryGetImage("Profile_Image", out Image profileImg))
            imgHomeProfileAvatar = profileImg;
    }

    GameObject ResolveHomePanel()
    {
        if (panelHome != null) return panelHome;
        if (GoogleLogin.Instance != null && GoogleLogin.Instance.homePanel != null)
            return GoogleLogin.Instance.homePanel;
        if (ModeManager.Instance != null && ModeManager.Instance.panelHomeScreen != null)
            return ModeManager.Instance.panelHomeScreen;
        return null;
    }

    private void SetupAvatarButtons()
    {
        if (avatarButtons == null || avatarButtons.Length == 0) return;

        for (int i = 0; i < avatarButtons.Length; i++)
        {
            int index = i;
            avatarButtons[i].onClick.AddListener(() => SelectAvatar(index));

            if (avatarButtons[i].GetComponent<UnityEngine.UI.Outline>() == null)
            {
                UnityEngine.UI.Outline outline = avatarButtons[i].gameObject.AddComponent<UnityEngine.UI.Outline>();
                outline.effectColor = Color.yellow;
                outline.effectDistance = new Vector2(5, 5);
                outline.enabled = false;
            }
        }
    }

    public void SelectAvatar(int index)
    {
        selectedAvatarIndex = index;

        for (int i = 0; i < avatarButtons.Length; i++)
        {
            UnityEngine.UI.Outline outline = avatarButtons[i].GetComponent<UnityEngine.UI.Outline>();
            if (outline != null) outline.enabled = (i == index);

            float targetScale = (i == index) ? 1.15f : 1.0f;
            avatarButtons[i].transform.DOScale(targetScale, 0.25f).SetEase(Ease.OutBack);
        }
    }

    public void CheckAndLoadUserProfile(string userId, string defaultName)
    {
        _pendingProfileUserId = userId;
        _pendingDefaultUsername = defaultName;
        Debug.Log($"[ProfileManager] Checking profile for {userId}...");

        FirestoreUsersService.GetUser(userId, snapshot =>
        {
            // Give this account a short public UID (PUBG / Free Fire style) if it doesn't have one yet.
            GameUidService.EnsureGameUid(userId, _ =>
            {
                RefreshUidDisplays();
                if (PlayWithFriendsManager.Instance != null)
                    PlayWithFriendsManager.Instance.DisplayMyID();
            });

            if (snapshot != null && TryReadCompleteProfile(snapshot, out string username, out int avatarIndex))
            {
                Debug.Log($"[Firestore] Existing profile found for user: {username}");
                CacheProfileLocally(username, avatarIndex);
                TransitionToHome();
                return;
            }

            // Cloud miss (new Auth uid / permission blip) but this device already finished setup.
            if (TryReadCompleteLocalProfile(out string localName, out int localAvatar))
            {
                Debug.Log($"[Profile] Cloud profile missing for {userId}; restoring local profile '{localName}' and re-syncing.");
                CacheProfileLocally(localName, localAvatar);
                ResyncLocalProfileToCloud(userId, localName, localAvatar);
                TransitionToHome();
                return;
            }

            if (snapshot == null)
                Debug.LogWarning("[Firestore] No cloud profile yet (missing doc or rules not published). Using local setup.");
            else
                Debug.Log("[Firestore] New account — profile setup required once.");

            ShowProfileSetupForNewUser(snapshot);
        });
    }

    bool TryReadCompleteLocalProfile(out string username, out int avatarIndex)
    {
        username = PlayerPrefs.GetString(PREFS_USERNAME, string.Empty).Trim();
        avatarIndex = PlayerPrefs.GetInt(PREFS_AVATAR_INDEX, -1);

        if (!IsValidUsername(username, out _))
            return false;
        if (profileSprites == null || avatarIndex < 0 || avatarIndex >= profileSprites.Length)
            return false;
        return true;
    }

    void ResyncLocalProfileToCloud(string userId, string username, int avatarIndex)
    {
        if (string.IsNullOrEmpty(userId)) return;
        if (FirebaseAuth.DefaultInstance?.CurrentUser == null) return;

        var profileData = new Dictionary<string, object>
        {
            { FirestoreUsersService.FieldUsername, username },
            { FirestoreUsersService.FieldAvatarId, avatarIndex },
            { FirestoreUsersService.FieldIsBot, false },
            { FirestoreUsersService.FieldIsActiveNow, true }
        };

        FirestoreUsersService.MergeUser(userId, profileData, ok =>
        {
            if (ok)
                Debug.Log($"[Firestore] Re-synced local profile to users/{userId}");
        });
    }

    void ShowProfileSetupForNewUser(DocumentSnapshot snapshot)
    {
        ShowProfileSetup(false);
        PrefillProfileSetupFromSnapshot(snapshot);

        if (inputPlayerName != null && string.IsNullOrWhiteSpace(inputPlayerName.text)
            && !string.IsNullOrWhiteSpace(_pendingDefaultUsername)
            && IsValidUsername(_pendingDefaultUsername, out _))
        {
            inputPlayerName.text = _pendingDefaultUsername.Trim();
        }
    }

    /// <summary>Derives a default username from the Google account email (local-part, max 15 chars).</summary>
    public static string GenerateDefaultUsername(string email)
    {
        if (string.IsNullOrEmpty(email)) return "GuestPlayer";

        string name = email.Split('@')[0];

        if (name.Length > 15)
            name = name.Substring(0, 15);

        // Keep only letters/digits so it passes username validation.
        var cleaned = new System.Text.StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c))
                cleaned.Append(c);
        }

        if (cleaned.Length < 2)
            return "GuestPlayer";

        return cleaned.ToString();
    }

    void CacheProfileLocally(string username, int avatarIndex)
    {
        PlayerPrefs.SetString(PREFS_USERNAME, username);
        PlayerPrefs.SetInt(PREFS_AVATAR_INDEX, avatarIndex);
        PlayerPrefs.Save();
    }

    bool TryReadCompleteProfile(DocumentSnapshot snapshot, out string username, out int avatarIndex)
    {
        username = null;
        avatarIndex = -1;

        if (snapshot == null || !snapshot.Exists)
            return false;

        username = FirestoreUsersService.ResolveUsername(snapshot)?.Trim();
        // Real player docs must have an explicit username field (doc id is the Auth UID).
        if (!snapshot.ContainsField(FirestoreUsersService.FieldUsername)
            || !IsValidUsername(username, out _))
            return false;

        if (!FirestoreUsersService.TryGetAvatarId(snapshot, out avatarIndex))
            return false;

        if (profileSprites == null || avatarIndex < 0 || avatarIndex >= profileSprites.Length)
            return false;

        return true;
    }

    [ContextMenu("Dev: Clear All Firebase Users")]
    public void DevClearAllFirebaseUsers()
    {
        Debug.LogWarning("[Firestore] Dev clear-all-users is not supported as a single wipe. Delete docs in the Firebase console.");
    }

    void PrefillProfileSetupFromSnapshot(DocumentSnapshot snapshot)
    {
        if (snapshot == null || !snapshot.Exists)
            return;

        string username = FirestoreUsersService.ResolveUsername(snapshot);
        if (IsValidUsername(username, out _))
        {
            if (inputPlayerName != null)
                inputPlayerName.text = username.Trim();
        }

        if (FirestoreUsersService.TryGetAvatarId(snapshot, out int avatarIndex)
            && profileSprites != null
            && avatarIndex >= 0
            && avatarIndex < profileSprites.Length)
        {
            SelectAvatar(avatarIndex);
        }
    }

    private void ShowProfileSetup(bool isEditing)
    {
        Debug.Log($"[ProfileManager] Opening Profile Setup (isEditing={isEditing})");
        _isEditingExistingProfile = isEditing;
        ResolveUiReferences();

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.EndLoginTransitionLoading();

        if (panelProfileSetup != null)
        {
            panelProfileSetup.SetActive(true);
            panelProfileSetup.transform.SetAsLastSibling();
        }

        if (isEditing)
        {
            if (panelPlayerProfile != null)
                panelPlayerProfile.SetActive(false);
        }
        else
        {
            GameObject home = ResolveHomePanel();
            if (home != null)
                home.SetActive(false);

            if (NetworkManager.Instance != null)
                NetworkManager.Instance.HideHomeUntilLogin();

            if (GoogleLogin.Instance != null && GoogleLogin.Instance.loginPanel != null)
                GoogleLogin.Instance.loginPanel.SetActive(false);
        }

        if (!isEditing)
        {
            selectedAvatarIndex = -1;
            if (inputPlayerName != null)
                inputPlayerName.text = string.Empty;
        }

        if (btnEnterGame != null)
        {
            var btnText = btnEnterGame.GetComponentInChildren<TMP_Text>();
            if (btnText != null) btnText.text = isEditing ? "UPDATE PROFILE" : "ENTER GAME";
        }

        if (panelProfileSetup != null)
        {
            UnityEngine.CanvasGroup cg = panelProfileSetup.GetComponent<UnityEngine.CanvasGroup>();
            if (cg == null) cg = panelProfileSetup.AddComponent<UnityEngine.CanvasGroup>();
            cg.alpha = 1f;
            cg.interactable = true;
            cg.blocksRaycasts = true;
        }

        EnsureProfileSetupErrorText();
        ClearProfileSetupError();
    }

    public static bool IsValidUsername(string username, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(username))
        {
            error = "Please enter a username.";
            return false;
        }

        username = username.Trim();
        if (username.Length < 2)
        {
            error = "Name must be at least 2 characters.";
            return false;
        }

        for (int i = 0; i < username.Length; i++)
        {
            if (!char.IsLetterOrDigit(username[i]))
            {
                error = "Special characters are not allowed. Use only letters and numbers.";
                return false;
            }
        }

        return true;
    }

    /// <summary>Call this to clear everything and start fresh.</summary>
    public void ClearAllDataAndSignOut()
    {
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();
        if (GoogleLogin.Instance != null)
            GoogleLogin.Instance.SignOut();
        else if (FirebaseAuth.DefaultInstance != null)
            FirebaseAuth.DefaultInstance.SignOut();
        
        Debug.Log("[ProfileManager] Local data cleared. Signed out.");
    }

    /// <summary>Deletes the Firebase account, clears local data, and returns to the login screen.</summary>
    public void DeleteAccount(System.Action<bool, string> onComplete = null)
    {
#if !UNITY_EDITOR
        // ---- Real device (Android) flow: deletes DB data + the Firebase Auth user,
        // re-authenticating via Google first if Firebase requires a recent login. ----
        var user = FirebaseAuth.DefaultInstance?.CurrentUser;
        if (user == null)
        {
            ClearAllDataAndSignOut();
            onComplete?.Invoke(true, null);
            return;
        }

        string uid = user.UserId;
        DeleteFirebaseUserData(uid, () => AttemptFirebaseAccountDelete(user, false, onComplete));
#else
        // ---- Editor: simulate delete, then sign out via the centralized logout flow. ----
        Debug.Log("[ProfileManager] Account delete simulated in Editor");
        string uid = FirebaseAuth.DefaultInstance?.CurrentUser?.UserId;
        if (!string.IsNullOrEmpty(uid))
            DeleteFirebaseUserData(uid, () => FinishEditorDeleteSimulation(onComplete));
        else
            FinishEditorDeleteSimulation(onComplete);
#endif
    }

    void FinishEditorDeleteSimulation(System.Action<bool, string> onComplete)
    {
        if (LogoutManager.Instance != null)
        {
            LogoutManager.Instance.Logout();
            onComplete?.Invoke(true, null);
            return;
        }

        ClearAllDataAndSignOut();
        onComplete?.Invoke(true, null);
    }

    void DeleteFirebaseUserData(string uid, System.Action onDone)
    {
        if (string.IsNullOrEmpty(uid))
        {
            onDone?.Invoke();
            return;
        }

        FirestoreUsersService.DeleteUser(uid, _ => onDone?.Invoke());
    }

    void AttemptFirebaseAccountDelete(FirebaseUser user, bool alreadyReauthed, System.Action<bool, string> onComplete)
    {
        user.DeleteAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsCompleted && !task.IsFaulted && !task.IsCanceled)
            {
                ClearAllDataAndSignOut();
                onComplete?.Invoke(true, null);
                return;
            }

            string msg = task.Exception?.GetBaseException()?.Message ?? "Could not delete account.";
            bool needsReauth = !alreadyReauthed && NeedsRecentLogin(task.Exception);

            if (needsReauth)
            {
                ReauthenticateGoogleUser(user, reauthOk =>
                {
                    if (!reauthOk)
                    {
                        onComplete?.Invoke(false, "Please sign in again, then try deleting your account.");
                        return;
                    }
                    AttemptFirebaseAccountDelete(user, true, onComplete);
                });
                return;
            }

            Debug.LogWarning("[ProfileManager] Delete account failed: " + msg);
            onComplete?.Invoke(false, msg);
        });
    }

    static bool NeedsRecentLogin(System.AggregateException ex)
    {
        if (ex == null) return false;
        foreach (var inner in ex.Flatten().InnerExceptions)
        {
            if (inner is FirebaseException fex && (AuthError)fex.ErrorCode == AuthError.RequiresRecentLogin)
                return true;
            if (inner.Message != null && inner.Message.IndexOf("RECENT_LOGIN", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    void ReauthenticateGoogleUser(FirebaseUser user, System.Action<bool> onDone)
    {
        // const string webClientId = "42510352079-sjbojvc7ho51477f16hr7vd6catvlscj.apps.googleusercontent.com";
        const string webClientId = "391594214961-sl1o3653ias0johhdv653ndgg0gjfhtn.apps.googleusercontent.com";
        var config = new GoogleSignInConfiguration
        {
            WebClientId = webClientId,
            RequestIdToken = true,
            RequestEmail = true
        };
        GoogleSignIn.Configuration = config;

        GoogleSignIn.DefaultInstance.SignIn().ContinueWithOnMainThread(signTask =>
        {
            if (signTask.IsFaulted || signTask.IsCanceled || signTask.Result == null
                || string.IsNullOrEmpty(signTask.Result.IdToken))
            {
                onDone?.Invoke(false);
                return;
            }

            var credential = GoogleAuthProvider.GetCredential(signTask.Result.IdToken, null);
            user.ReauthenticateAsync(credential).ContinueWithOnMainThread(reauthTask =>
                onDone?.Invoke(reauthTask.IsCompleted && !reauthTask.IsFaulted && !reauthTask.IsCanceled));
        });
    }

    private void OnClick_EnterGame()
    {
        string username = inputPlayerName != null ? inputPlayerName.text.Trim() : string.Empty;

        if (!IsValidUsername(username, out string error))
        {
            ShowProfileSetupError(error);
            return;
        }

        if (selectedAvatarIndex == -1)
        {
            ShowProfileSetupError("Please select a profile avatar.");
            return;
        }

        ClearProfileSetupError();

        SaveProfileToFirebase(username, selectedAvatarIndex);
    }

    private void SaveProfileToFirebase(string username, int avatarIndex)
    {
        string userId = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser?.UserId;
        if (string.IsNullOrEmpty(userId))
            userId = _pendingProfileUserId;

        PlayerPrefs.SetString(PREFS_USERNAME, username);
        PlayerPrefs.SetInt(PREFS_AVATAR_INDEX, avatarIndex);
        PlayerPrefs.Save();

        bool hasAuth = Firebase.Auth.FirebaseAuth.DefaultInstance?.CurrentUser != null;
        if (string.IsNullOrEmpty(userId) || !hasAuth)
        {
            if (!hasAuth)
                Debug.LogWarning("[Profile] No Firebase Auth session — saved locally only.");
            UpdateProfileUI();
            FinishProfileSave();
            return;
        }

        var profileData = new Dictionary<string, object>
        {
            { FirestoreUsersService.FieldUsername, username },
            { FirestoreUsersService.FieldAvatarId, avatarIndex },
            { FirestoreUsersService.FieldIsBot, false },
            { FirestoreUsersService.FieldIsActiveNow, true }
        };

        // Only stamp createdAt on first-time profile setup, not on later edits.
        if (!_isEditingExistingProfile)
            profileData[FirestoreUsersService.FieldCreatedAt] = FieldValue.ServerTimestamp;

        FirestoreUsersService.MergeUser(userId, profileData, ok =>
        {
            if (ok)
                Debug.Log($"[Firestore] Profile saved under users/{userId} (username={username}, avatar_id={avatarIndex}).");
            else
                Debug.LogWarning("[Profile] Cloud save skipped/failed — continuing with local profile. Publish firestore.rules if this persists.");

            UpdateProfileUI();
            FinishProfileSave();
        });
    }

    void FinishProfileSave()
    {
        if (_isEditingExistingProfile)
            TransitionEditComplete();
        else
            TransitionToHome();
    }

    public void UpdateProfileUI()
    {
        ResolveUiReferences();

        string username = PlayerPrefs.GetString(PREFS_USERNAME, string.Empty);
        int avatarIndex = PlayerPrefs.GetInt(PREFS_AVATAR_INDEX, 0);

        // Home screen name/avatar is dynamic player identity and must always reflect the
        // logged-in user. It is intentionally NOT gated by preserveManualProfileLayout, which
        // only protects the Player Profile (stats) panel. ClearHomeProfileDisplay() blanks the
        // home name at login, so it must be restored here or it stays empty.
        if (!string.IsNullOrEmpty(username))
        {
            if (textHomeProfileName != null)
                textHomeProfileName.text = username;

            if (GoogleLogin.Instance != null && GoogleLogin.Instance.profileNameText != null)
                GoogleLogin.Instance.profileNameText.text = username;
        }

        if (imgHomeProfileAvatar != null && profileSprites != null && profileSprites.Length > avatarIndex)
        {
            imgHomeProfileAvatar.sprite = profileSprites[avatarIndex];
            imgHomeProfileAvatar.preserveAspect = true;
        }

        if (!preserveManualProfileLayout)
        {
            if (textProfileName != null)
                textProfileName.text = username;

            if (imgCurrentAvatar != null && profileSprites != null && profileSprites.Length > avatarIndex)
                imgCurrentAvatar.sprite = profileSprites[avatarIndex];
        }

        if (!string.IsNullOrEmpty(username))
            PhotonNetwork.NickName = username;

        PublishAvatarToNetwork(avatarIndex);
        SyncProfilePanelIdentity();

        if (!preserveManualProfileLayout)
            RefreshUidDisplays();
    }

    /// <summary>
    /// Shows the player's short UID on the Profile screen and Home screen, each tappable to copy.
    /// </summary>
    public void RefreshUidDisplays()
    {
        string uid = GameUidService.LocalGameUid;

        // Profile screen — add a UID line inside the InfoColumn (vertical layout) under the name.
        if (textProfileName != null && textProfileName.transform.parent != null)
        {
            TMP_Text profileUid = UidUI.EnsureChildLabel(
                textProfileName.transform.parent, "Text_ProfileUID", textProfileName, Vector2.zero);
            if (profileUid != null)
            {
                profileUid.transform.SetSiblingIndex(textProfileName.transform.GetSiblingIndex() + 1);
                EnsureLayoutHeight(profileUid, 30f);
                UidUI.BindCopyLabel(profileUid, uid);
            }
        }

        // Home screen — place a compact UID line just below the name/coins block.
        if (textHomeProfileName != null && textHomeProfileName.transform.parent != null)
        {
            TMP_Text homeUid = UidUI.EnsureChildLabel(
                textHomeProfileName.transform.parent, "Text_HomeUID", textHomeProfileName,
                new Vector2(0f, -118f));
            if (homeUid != null)
            {
                RectTransform nameRt = textHomeProfileName.rectTransform;
                RectTransform rt = homeUid.rectTransform;
                rt.anchorMin = nameRt.anchorMin;
                rt.anchorMax = nameRt.anchorMax;
                rt.pivot = nameRt.pivot;
                rt.sizeDelta = new Vector2(280f, 34f);
                rt.anchoredPosition = nameRt.anchoredPosition + new Vector2(20f, -118f);
                homeUid.alignment = TextAlignmentOptions.Left;
                homeUid.fontSize = Mathf.Max(16f, textHomeProfileName.fontSize * 0.5f);
            }
            UidUI.BindCopyLabel(homeUid, uid);
        }
    }

    static void EnsureLayoutHeight(TMP_Text label, float height)
    {
        if (label == null) return;
        UnityEngine.UI.LayoutElement le = label.GetComponent<UnityEngine.UI.LayoutElement>();
        if (le == null) le = label.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        le.preferredHeight = height;
        le.minHeight = height;
    }

    /// <summary>
    /// Syncs the local player's chosen avatar index to all clients via a Photon custom
    /// property so each seat in the game table shows the player's actual selected avatar.
    /// </summary>
    void PublishAvatarToNetwork(int avatarIndex)
    {
        if (avatarIndex < 0) return;
        if (!PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode) return;

        object current = null;
        if (PhotonNetwork.LocalPlayer.CustomProperties != null)
            PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue(PROP_AVATAR, out current);

        if (current is int ci && ci == avatarIndex) return; // already up to date

        var props = new ExitGames.Client.Photon.Hashtable { { PROP_AVATAR, avatarIndex } };
        PhotonNetwork.LocalPlayer.SetCustomProperties(props);
    }

    private void TransitionToHome()
    {
        if (GoogleLogin.Instance != null)
            GoogleLogin.Instance.OpenHomeAfterLoginReady(ApplyTransitionToHome);
        else
            ApplyTransitionToHome();
    }

    private void ApplyTransitionToHome()
    {
        GoogleLogin.NotifyLoginFlowComplete();

        if (panelProfileSetup != null)
            panelProfileSetup.SetActive(false);

        if (panelPlayerProfile != null)
            panelPlayerProfile.SetActive(false);

        GameObject home = ResolveHomePanel();
        if (home != null)
            home.SetActive(true);

        if (GoogleLogin.Instance != null && GoogleLogin.Instance.loginPanel != null)
            GoogleLogin.Instance.loginPanel.SetActive(false);

        UpdateProfileUI();
        WireHomeProfileAvatarClick();

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.UpdateUIState(true);

        if (PlayerProfileSync.Instance != null)
            PlayerProfileSync.Instance.UpdateAllNames();

        if (PlayWithFriendsManager.Instance != null)
            PlayWithFriendsManager.Instance.EnsureFriendServicesStarted();
    }

    void TransitionEditComplete()
    {
        _isEditingExistingProfile = false;

        if (panelProfileSetup != null)
            panelProfileSetup.SetActive(false);

        // Refresh the cached/home display first.
        UpdateProfileUI();
        WireHomeProfileAvatarClick();

        // Return to the Player Profile panel (NOT Home) so the user sees the updated profile.
        // Re-activating the panel fires its binder/tab OnEnable which re-reads name, avatar and email.
        if (panelPlayerProfile != null)
        {
            panelPlayerProfile.SetActive(true);
            panelPlayerProfile.transform.SetAsLastSibling();

            CanvasGroup cg = panelPlayerProfile.GetComponent<CanvasGroup>();
            if (cg == null) cg = panelPlayerProfile.AddComponent<CanvasGroup>();
            cg.alpha = 1f;
            cg.interactable = true;
            cg.blocksRaycasts = true;
        }

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.UpdateUIState(true);

        if (PlayerProfileSync.Instance != null)
            PlayerProfileSync.Instance.UpdateAllNames();
    }

    private void TransitionSetupToHome()
    {
        // Keep as a thin wrapper — new-profile save must open Home the same way as returning users.
        TransitionToHome();
    }

    public void OpenPlayerProfile()
    {
        if (panelPlayerProfile == null)
        {
            Debug.LogWarning("[Profile] Panel_PlayerProfile is not assigned.");
            return;
        }

        if (!preserveManualProfileLayout)
        {
            UpdateProfileUI();

            if (textTotalMatches != null) textTotalMatches.text = "0";
            if (textWins != null) textWins.text = "0";
            if (textWinRatio != null) textWinRatio.text = "0%";
            if (textTotalKOT != null) textTotalKOT.text = "0";

            CanvasGroup cg = panelPlayerProfile.GetComponent<CanvasGroup>();
            if (cg == null) cg = panelPlayerProfile.AddComponent<CanvasGroup>();
            cg.alpha = 1f;
            cg.interactable = true;
            cg.blocksRaycasts = true;
        }

        SyncProfilePanelIdentity();
        panelPlayerProfile.SetActive(true);
        panelPlayerProfile.transform.SetAsLastSibling();
    }

    public void OnEditProfileClicked()
    {
        if (panelPlayerProfile != null)
            panelPlayerProfile.SetActive(false);

        ShowProfileSetup(true);

        if (inputPlayerName != null)
            inputPlayerName.text = PlayerPrefs.GetString(PREFS_USERNAME, string.Empty);

        int existingIndex = PlayerPrefs.GetInt(PREFS_AVATAR_INDEX, -1);
        if (existingIndex != -1)
            SelectAvatar(existingIndex);
    }
}
