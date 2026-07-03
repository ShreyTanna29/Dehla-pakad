using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using Photon.Pun;
using Photon.Realtime;

/// <summary>
/// Controls the in-game settings panel on the game table:
/// - Opens/closes via the gear button and the close (X) button (slide-in from the right).
/// - SOUND on/off (mutes AudioListener, persisted in PlayerPrefs).
/// - GAME SPEED visual cycle (SLOW / NORMAL / FAST), persisted in PlayerPrefs.
/// - EXIT GAME leaves the current match and returns to the Home screen.
/// Visuals reuse the existing wood/tan theme sprites; this script only drives behavior.
/// </summary>
public class InGameSettingsController : MonoBehaviour
{
    public static InGameSettingsController Instance;

    [Header("Panel")]
    public GameObject settingsPanel;        // wood panel root (the object that slides)
    public RectTransform panelRect;         // RectTransform of settingsPanel (for slide)
    public CanvasGroup panelCanvasGroup;    // optional, for fade/raycast control

    [Header("Open / Close Buttons")]
    public Button openButton;               // gear button on the table
    public Button closeButton;              // X button inside the panel
    public Button exitButton;               // EXIT GAME button

    [Header("Sound")]
    public Button soundOnButton;            // speaker (on) icon = unmute
    public Button soundOffButton;           // speaker-muted icon = mute
    public Image soundOnImage;              // highlighted when sound is ON
    public Image soundOffImage;             // highlighted when sound is OFF

    [Header("Game Speed (visual)")]
    public Button gameSpeedButton;          // pill button cycling SLOW/NORMAL/FAST
    public TMP_Text gameSpeedLabel;

    [Header("Exit Confirm Panel")]
    public GameObject confirmExitPanel;     // "ARE YOU SURE?" popup
    public Button confirmYesButton;         // EXIT, YES -> leave to home
    public Button confirmNoButton;          // NO, NO -> back to game
    [Tooltip("Optional label on the exit panel (e.g. AdsLoading).")]
    public TMP_Text exitAdStatusText;

    private const string PREF_MUTE = "SoundMuted";
    private const string PREF_SPEED = "GameSpeedIndex";
    private static readonly string[] SpeedNames = { "SLOW", "NORMAL", "FAST" };

    private static readonly Color ActiveTint = Color.white;
    private static readonly Color InactiveTint = new Color(1f, 1f, 1f, 0.35f);

    private bool isMuted;
    private int speedIndex;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(this); return; }

        isMuted = PlayerPrefs.GetInt(PREF_MUTE, 0) == 1;
        AudioListener.volume = isMuted ? 0f : 1f;
        speedIndex = Mathf.Clamp(PlayerPrefs.GetInt(PREF_SPEED, 1), 0, SpeedNames.Length - 1);
    }

    void Start()
    {
        Wire(openButton, OpenSettings);
        Wire(closeButton, CloseSettings);
        Wire(exitButton, OnExitGameClicked);
        Wire(soundOnButton, SetSoundOn);
        Wire(soundOffButton, SetSoundOff);
        Wire(gameSpeedButton, CycleGameSpeed);
        Wire(confirmYesButton, ConfirmExitYes);
        Wire(confirmNoButton, ConfirmExitNo);

        if (settingsPanel != null) settingsPanel.SetActive(false);
        if (confirmExitPanel != null) confirmExitPanel.SetActive(false);

        ApplySoundVisuals();
        ApplySpeedVisuals();
    }

    static void Wire(Button b, UnityEngine.Events.UnityAction call)
    {
        if (b == null) return;
        b.onClick.RemoveListener(call);
        b.onClick.AddListener(call);
    }

    public void OpenSettings()
    {
        if (settingsPanel == null) return;

        settingsPanel.SetActive(true);
        settingsPanel.transform.SetAsLastSibling();

        if (openButton != null)
            openButton.interactable = true;

        if (panelRect != null)
        {
            float w = panelRect.rect.width;
            panelRect.DOKill();
            panelRect.anchoredPosition = new Vector2(w, panelRect.anchoredPosition.y);
            panelRect.DOAnchorPosX(0f, 0.3f).SetEase(Ease.OutCubic).SetUpdate(true);
        }

        if (panelCanvasGroup != null)
        {
            panelCanvasGroup.DOKill();
            panelCanvasGroup.alpha = 1f;
            panelCanvasGroup.interactable = true;
            panelCanvasGroup.blocksRaycasts = true;
        }

        isMuted = PlayerPrefs.GetInt(PREF_MUTE, 0) == 1;
        ApplySoundVisuals();
        ApplySpeedVisuals();
    }

    public void CloseSettings()
    {
        if (settingsPanel == null) return;

        if (panelRect != null)
        {
            float w = panelRect.rect.width;
            panelRect.DOKill();
            panelRect.DOAnchorPosX(w, 0.25f).SetEase(Ease.InCubic).SetUpdate(true)
                .OnComplete(() =>
                {
                    if (settingsPanel != null) settingsPanel.SetActive(false);
                    if (panelCanvasGroup != null) panelCanvasGroup.alpha = 0f;
                });
        }
        else
        {
            settingsPanel.SetActive(false);
        }

        if (panelCanvasGroup != null)
        {
            panelCanvasGroup.DOKill();
            panelCanvasGroup.interactable = false;
            panelCanvasGroup.blocksRaycasts = false;
            panelCanvasGroup.alpha = 0f;
        }
    }

    public void SetSoundOn() { SetMuted(false); }
    public void SetSoundOff() { SetMuted(true); }

    void SetMuted(bool muted)
    {
        isMuted = muted;
        PlayerPrefs.SetInt(PREF_MUTE, isMuted ? 1 : 0);
        PlayerPrefs.Save();
        AudioListener.volume = isMuted ? 0f : 1f;
        ApplySoundVisuals();
    }

    void ApplySoundVisuals()
    {
        bool soundOn = !isMuted;
        if (soundOnImage != null) soundOnImage.color = soundOn ? ActiveTint : InactiveTint;
        if (soundOffImage != null) soundOffImage.color = soundOn ? InactiveTint : ActiveTint;
    }

    public void CycleGameSpeed()
    {
        speedIndex = (speedIndex + 1) % SpeedNames.Length;
        PlayerPrefs.SetInt(PREF_SPEED, speedIndex);
        PlayerPrefs.Save();
        ApplySpeedVisuals();
    }

    void ApplySpeedVisuals()
    {
        if (gameSpeedLabel != null) gameSpeedLabel.text = SpeedNames[speedIndex];
    }

    // EXIT GAME tapped -> show the "ARE YOU SURE?" confirmation instead of leaving directly.
    void OnExitGameClicked()
    {
        if (confirmExitPanel == null)
        {
            // No confirm UI assigned: fall back to leaving directly.
            DoExitToHome();
            return;
        }

        confirmExitPanel.SetActive(true);
        confirmExitPanel.transform.SetAsLastSibling();

        var cg = confirmExitPanel.GetComponent<CanvasGroup>();
        if (cg != null)
        {
            cg.DOKill();
            cg.alpha = 0f;
            cg.interactable = true;
            cg.blocksRaycasts = true;
            cg.DOFade(1f, 0.2f).SetUpdate(true);
        }

        confirmExitPanel.transform.localScale = Vector3.one * 0.85f;
        confirmExitPanel.transform.DOKill();
        confirmExitPanel.transform.DOScale(1f, 0.25f).SetEase(Ease.OutBack).SetUpdate(true);

        if (AdsManager.Instance != null)
            AdsManager.Instance.PreloadAds();

        UpdateExitAdStatusLabel();
    }

    void UpdateExitAdStatusLabel()
    {
        if (exitAdStatusText == null && confirmExitPanel != null)
        {
            Transform t = confirmExitPanel.transform.Find("AdsLoading");
            if (t != null) exitAdStatusText = t.GetComponent<TMP_Text>();
        }

        if (exitAdStatusText == null) return;

        bool interstitialReady = AdsManager.Instance != null && AdsManager.Instance.IsInterstitialReady();
        bool rewardedReady = AdsManager.Instance != null && AdsManager.Instance.IsRewardedAdReady();
        exitAdStatusText.text = interstitialReady || rewardedReady
            ? "Ad ready"
            : "Loading ad…";
    }

    // EXIT, YES -> show fullscreen ad (if any), then leave match and go Home.
    public void ConfirmExitYes()
    {
        if (confirmExitPanel != null) confirmExitPanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(false);

        if (AdsManager.Instance != null)
            AdsManager.Instance.ShowBestEffortFullscreenAd(DoExitToHome);
        else
            DoExitToHome();
    }

    // NO, NO -> dismiss confirmation, stay in the game.
    public void ConfirmExitNo()
    {
        if (confirmExitPanel == null) return;

        var cg = confirmExitPanel.GetComponent<CanvasGroup>();
        if (cg != null)
        {
            cg.interactable = false;
            cg.blocksRaycasts = false;
        }

        confirmExitPanel.transform.DOKill();
        confirmExitPanel.transform.DOScale(0.85f, 0.2f).SetEase(Ease.InBack).SetUpdate(true)
            .OnComplete(() => { if (confirmExitPanel != null) confirmExitPanel.SetActive(false); });
    }

    void DoExitToHome()
    {
        DismissAllPanels();

        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.LeaveRoomAndCleanup();
            return;
        }

        GameFlowState.SetPhase(GameFlowPhase.Home);
        bool leaving = PhotonNetwork.NetworkClientState == ClientState.Leaving;
        if (PhotonNetwork.InRoom && !leaving)
            PhotonNetwork.LeaveRoom();
        else if (ModeManager.Instance != null)
            ModeManager.Instance.ReturnToHomeClean();
    }

    /// <summary>Closes in-game settings and exit confirmation popups.</summary>
    public void DismissAllPanels()
    {
        if (settingsPanel != null)
        {
            settingsPanel.SetActive(false);
            if (panelCanvasGroup != null)
            {
                panelCanvasGroup.DOKill();
                panelCanvasGroup.alpha = 0f;
                panelCanvasGroup.interactable = false;
                panelCanvasGroup.blocksRaycasts = false;
            }
        }

        if (confirmExitPanel != null)
        {
            confirmExitPanel.SetActive(false);
            var cg = confirmExitPanel.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.DOKill();
                cg.alpha = 0f;
                cg.interactable = false;
                cg.blocksRaycasts = false;
            }
        }
    }

    /// <summary>
    /// Entry point for the Android hardware Back button while in a match: shows the same
    /// "Exit Game?" confirmation as the gear menu instead of letting the OS quit the app.
    /// </summary>
    public void RequestExitFromBack()
    {
        if (confirmExitPanel != null && confirmExitPanel.activeSelf) return; // already asking
        OnExitGameClicked();
    }
}
