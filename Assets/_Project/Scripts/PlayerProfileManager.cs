using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;
using TMPro;
using System.Collections.Generic;
using Firebase.Database;
using Firebase.Extensions;
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
    private bool _isEditingExistingProfile;

    private const string PREFS_USERNAME = "PlayerUsername";
    private const string PREFS_AVATAR_INDEX = "PlayerAvatarIndex";
    private const string FirebaseDatabaseUrl = "https://dehla-pakad-a7859-default-rtdb.firebaseio.com/";

    // Photon custom-property key used to sync each player's chosen avatar to all clients.
    public const string PROP_AVATAR = "av";

    /// <summary>The avatar index the local user selected during profile setup (-1 if none).</summary>
    public static int GetSavedAvatarIndex() => PlayerPrefs.GetInt(PREFS_AVATAR_INDEX, -1);

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
        if (profileSetupErrorText != null)
            profileSetupErrorText.text = message;
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
        Debug.Log($"[ProfileManager] Checking profile for {userId}...");

        FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).GetReference("users").Child(userId)
            .GetValueAsync().ContinueWithOnMainThread(task =>
            {
                if (NetworkManager.Instance != null)
                    NetworkManager.Instance.HideLoading();

                // Give this account a short public UID (PUBG / Free Fire style) if it doesn't have one yet.
                GameUidService.EnsureGameUid(userId, _ =>
                {
                    RefreshUidDisplays();
                    if (PlayWithFriendsManager.Instance != null)
                        PlayWithFriendsManager.Instance.DisplayMyID();
                });

                if (task.IsFaulted)
                {
                    Debug.LogError("[Firebase DB] Failed to fetch user data: " + task.Exception);
                    ShowProfileSetupForNewUser(null);
                    return;
                }

                DataSnapshot snapshot = task.Result;
                if (TryReadCompleteProfile(snapshot, out string username, out int avatarIndex))
                {
                    Debug.Log($"[Firebase DB] Existing profile found for user: {username}");
                    CacheProfileLocally(username, avatarIndex);
                    TransitionToHome();
                }
                else
                {
                    Debug.Log("[Firebase DB] New Google account — profile setup required once.");
                    ShowProfileSetupForNewUser(snapshot);
                }
            });
    }

    void ShowProfileSetupForNewUser(DataSnapshot snapshot)
    {
        ShowProfileSetup(false);
        PrefillProfileSetupFromSnapshot(snapshot);
    }

    void CacheProfileLocally(string username, int avatarIndex)
    {
        PlayerPrefs.SetString(PREFS_USERNAME, username);
        PlayerPrefs.SetInt(PREFS_AVATAR_INDEX, avatarIndex);
        PlayerPrefs.Save();
    }

    bool TryReadCompleteProfile(DataSnapshot snapshot, out string username, out int avatarIndex)
    {
        username = null;
        avatarIndex = -1;

        if (snapshot == null || !snapshot.Exists)
            return false;

        if (!snapshot.HasChild("username") || !snapshot.HasChild("avatarIndex"))
            return false;

        username = snapshot.Child("username").Value?.ToString()?.Trim();
        if (!IsValidUsername(username, out _))
            return false;

        string avatarStr = snapshot.Child("avatarIndex").Value?.ToString();
        if (!int.TryParse(avatarStr, out avatarIndex))
            return false;

        if (profileSprites == null || avatarIndex < 0 || avatarIndex >= profileSprites.Length)
            return false;

        return true;
    }

    [ContextMenu("Dev: Clear All Firebase Users")]
    public void DevClearAllFirebaseUsers()
    {
        FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference
            .Child("users").RemoveValueAsync().ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted)
                    Debug.LogError("[Firebase DB] Failed to clear users: " + task.Exception);
                else
                    Debug.Log("[Firebase DB] All user profiles removed from database.");
            });
    }

    void PrefillProfileSetupFromSnapshot(DataSnapshot snapshot)
    {
        if (snapshot == null || !snapshot.Exists)
            return;

        if (snapshot.HasChild("username"))
        {
            string username = snapshot.Child("username").Value?.ToString();
            if (IsValidUsername(username, out _))
            {
                if (inputPlayerName != null)
                    inputPlayerName.text = username.Trim();
            }
        }

        if (snapshot.HasChild("avatarIndex"))
        {
            string avatarStr = snapshot.Child("avatarIndex").Value?.ToString();
            if (int.TryParse(avatarStr, out int avatarIndex)
                && profileSprites != null
                && avatarIndex >= 0
                && avatarIndex < profileSprites.Length)
            {
                SelectAvatar(avatarIndex);
            }
        }
    }

    private void ShowProfileSetup(bool isEditing)
    {
        Debug.Log($"[ProfileManager] Opening Profile Setup (isEditing={isEditing})");
        _isEditingExistingProfile = isEditing;
        ResolveUiReferences();

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
    }

    public static bool IsValidUsername(string username, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(username))
        {
            error = "Name cannot be empty.";
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
        
        Debug.Log("[ProfileManager] Local data cleared. Signed out.");
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

        if (string.IsNullOrEmpty(userId))
        {
            UpdateProfileUI();
            FinishProfileSave();
            return;
        }

        var userRef = FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference
            .Child("users").Child(userId);

        var profileData = new Dictionary<string, object>
        {
            { "username", username },
            { "avatarIndex", avatarIndex }
        };

        userRef.UpdateChildrenAsync(profileData).ContinueWithOnMainThread(task =>
        {
            if (task.IsCompleted && !task.IsFaulted)
            {
                Debug.Log("Profile saved to Firebase.");
                UpdateProfileUI();
                FinishProfileSave();
            }
            else
            {
                Debug.LogError("Failed to save profile: " + task.Exception);
                UpdateProfileUI();
                FinishProfileSave();
            }
        });
    }

    void FinishProfileSave()
    {
        if (_isEditingExistingProfile)
            TransitionEditComplete();
        else
            TransitionSetupToHome();
    }

    public void UpdateProfileUI()
    {
        ResolveUiReferences();

        string username = PlayerPrefs.GetString(PREFS_USERNAME, string.Empty);
        int avatarIndex = PlayerPrefs.GetInt(PREFS_AVATAR_INDEX, 0);

        if (textHomeProfileName != null)
            textHomeProfileName.text = username;

        if (imgHomeProfileAvatar != null && profileSprites != null && profileSprites.Length > avatarIndex)
        {
            imgHomeProfileAvatar.sprite = profileSprites[avatarIndex];
            imgHomeProfileAvatar.preserveAspect = true;
        }

        if (textProfileName != null)
            textProfileName.text = username;

        if (imgCurrentAvatar != null && profileSprites != null && profileSprites.Length > avatarIndex)
            imgCurrentAvatar.sprite = profileSprites[avatarIndex];

        if (!string.IsNullOrEmpty(username))
            PhotonNetwork.NickName = username;

        PublishAvatarToNetwork(avatarIndex);
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

        if (panelPlayerProfile != null)
            panelPlayerProfile.SetActive(false);

        GameObject home = ResolveHomePanel();
        if (home != null)
            home.SetActive(true);

        UpdateProfileUI();
        WireHomeProfileAvatarClick();

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.UpdateUIState(true);

        if (PlayerProfileSync.Instance != null)
            PlayerProfileSync.Instance.UpdateAllNames();
    }

    private void TransitionSetupToHome()
    {
        GoogleLogin.NotifyLoginFlowComplete();
        UpdateProfileUI();

        GameObject home = ResolveHomePanel();
        if (home != null)
        {
            home.SetActive(true);
            UnityEngine.CanvasGroup homeCG = home.GetComponent<UnityEngine.CanvasGroup>();
            if (homeCG == null) homeCG = home.AddComponent<UnityEngine.CanvasGroup>();
            homeCG.alpha = 0f;
            homeCG.DOFade(1f, 0.4f);
        }

        if (panelProfileSetup != null)
        {
            UnityEngine.CanvasGroup setupCG = panelProfileSetup.GetComponent<UnityEngine.CanvasGroup>();
            if (setupCG != null)
            {
                setupCG.DOFade(0f, 0.4f).OnComplete(() =>
                {
                    panelProfileSetup.SetActive(false);
                    if (NetworkManager.Instance != null)
                        NetworkManager.Instance.UpdateUIState(true);
                });
            }
            else
            {
                panelProfileSetup.SetActive(false);
                if (NetworkManager.Instance != null)
                    NetworkManager.Instance.UpdateUIState(true);
            }
        }
        else if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.UpdateUIState(true);
        }

        if (GoogleLogin.Instance != null && GoogleLogin.Instance.loginPanel != null)
            GoogleLogin.Instance.loginPanel.SetActive(false);

        WireHomeProfileAvatarClick();
    }

    public void OpenPlayerProfile()
    {
        if (panelPlayerProfile == null)
        {
            Debug.LogWarning("[Profile] Panel_PlayerProfile is not assigned.");
            return;
        }

        UpdateProfileUI();
        panelPlayerProfile.SetActive(true);
        panelPlayerProfile.transform.SetAsLastSibling();

        CanvasGroup cg = panelPlayerProfile.GetComponent<CanvasGroup>();
        if (cg == null) cg = panelPlayerProfile.AddComponent<CanvasGroup>();
        cg.alpha = 1f;
        cg.interactable = true;
        cg.blocksRaycasts = true;

        if (textTotalMatches != null) textTotalMatches.text = "Total Matches: 0";
        if (textWins != null) textWins.text = "Wins: 0";
        if (textWinRatio != null) textWinRatio.text = "Win Ratio: 0%";
        if (textTotalKOT != null) textTotalKOT.text = "Total KOTs: 0";
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
