using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;

public class MatchmakingManager : MonoBehaviourPunCallbacks
{
    public static MatchmakingManager Instance;

    // Global user-cancel flag to block pending Photon callbacks from restarting matchmaking.
    public bool WasCancelledByUser { get; private set; }

    [Header("UI Panels")]
    public CanvasGroup matchmakingPanel;
    public TMP_Text titleText;
    public TMP_Text statusText;
    public Button cancelButton;
    public RectTransform spinner;
    public GameObject matchedPlayersContainer;
    public Sprite maskSprite;

    [Header("Profile Animation")]
    public RectTransform profileContainer;
    public GameObject profilePrefab;
    public List<Sprite> profileSprites = new List<Sprite>();
    public float spawnInterval = 0.4f;
    public float scrollDuration = 4.0f;

    [Header("Matchmaking Status Messages")]
    public string[] fakeStatusMessages = {
        "Looking for opponents...",
        "Matching skill level...",
        "Connecting players...",
        "Almost ready...",
        "Verifying connection...",
        "Preparing cards...",
        "Joining game room..."
    };

    private bool isSearching = false;
    private bool isMatchFoundRoutineRunning = false;
    private List<GameObject> profilePool = new List<GameObject>();
    private Coroutine statusCoroutine;
    private Coroutine spawnCoroutine;
    private Tween spinnerTween;

    public static List<Sprite> GlobalProfileSprites = new List<Sprite>();

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            GlobalProfileSprites = profileSprites;
        }
        else Destroy(gameObject);

        if (cancelButton != null)
        {
            // Replace any inspector/scene Cancel bindings with code path that sets the cancel flag.
            cancelButton.onClick.RemoveAllListeners();
            cancelButton.onClick.AddListener(OnCancelClicked);
        }

        if (matchmakingPanel != null)
        {
            matchmakingPanel.alpha = 0;
            matchmakingPanel.interactable = false;
            matchmakingPanel.blocksRaycasts = false;
            matchmakingPanel.gameObject.SetActive(false);
        }
    }

    public void StartSearching()
    {
        if (isSearching) return;
        isSearching = true;
        WasCancelledByUser = false;

        Debug.Log("🔍 Matchmaking Started...");

        if (matchmakingPanel != null)
        {
            matchmakingPanel.gameObject.SetActive(true);
            matchmakingPanel.alpha = 0; 
            matchmakingPanel.DOFade(1, 0.3f).SetUpdate(true);
            matchmakingPanel.interactable = true;
            matchmakingPanel.blocksRaycasts = true;
            
            matchmakingPanel.transform.localScale = Vector3.one * 0.8f;
            matchmakingPanel.transform.DOScale(1.0f, 0.4f).SetEase(Ease.OutBack).SetUpdate(true);
        }

        if (matchedPlayersContainer != null) matchedPlayersContainer.SetActive(false);
        if (profileContainer != null) profileContainer.gameObject.SetActive(true);
        if (cancelButton != null) cancelButton.gameObject.SetActive(true);

        titleText.text = "Finding Players...";
        statusText.text = "Searching for players...";

        if (spinner != null)
        {
            spinner.localScale = Vector3.one;
            spinnerTween = spinner.DORotate(new Vector3(0, 0, -360), 1.5f, RotateMode.FastBeyond360)
                .SetLoops(-1, LoopType.Incremental)
                .SetEase(Ease.Linear)
                .SetUpdate(true);
        }

        statusCoroutine = StartCoroutine(RotateStatusMessages());
        spawnCoroutine = StartCoroutine(SpawnProfilesRoutine());
    }

    public void UpdateMatchmakingStatus(int playersFound, int countdown)
    {
        if (!isSearching) return;
        
        titleText.text = "Searching...";
        statusText.text = $"Players Found: {playersFound}/4\nStarting in {countdown}s";
    }

    public void StopSearching(bool isMatchFound)
    {
        if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null && !PhotonNetwork.CurrentRoom.IsVisible && !isMatchFound)
        {
            Debug.Log("[Matchmaking] Private Room detected, bypassing exit logic.");
            return;
        }

        // For Bots mode or if we were searching, we want the transition
        if (!isSearching && !isMatchFound && !WasCancelledByUser) return;
        
        // Prevent multiple MatchFoundRoutine calls if already starting
        if (isMatchFound && isMatchFoundRoutineRunning) return;

        isSearching = false;
        if (statusCoroutine != null) StopCoroutine(statusCoroutine);
        if (spawnCoroutine != null) StopCoroutine(spawnCoroutine);
        if (spinnerTween != null) spinnerTween.Kill();

        if (isMatchFound)
        {
            isMatchFoundRoutineRunning = true;
            StartCoroutine(MatchFoundRoutine());
        }
        else
        {
            Debug.Log("[Matchmaking] Stopped/Cancelled -> Home Screen");

            HidePanelImmediate();

            if (ModeManager.Instance != null)
                ModeManager.Instance.CancelPendingMatchmaking();

            if (PhotonNetwork.InRoom)
                PhotonNetwork.LeaveRoom();
            else if (PhotonNetwork.InLobby)
                PhotonNetwork.LeaveLobby();

            if (PhotonNetwork.OfflineMode)
                PhotonNetwork.OfflineMode = false;

            GameFlowState.SetPhase(GameFlowPhase.Home, true);

            if (ModeManager.Instance != null)
            {
                if (ModeManager.Instance.panelModes != null)
                    ModeManager.Instance.panelModes.SetActive(false);

                if (ModeManager.Instance.panelHomeScreen != null)
                    ModeManager.Instance.panelHomeScreen.SetActive(true);

                ModeManager.Instance.ApplyHomeScreenButtonColors();
            }

            if (NetworkManager.Instance != null)
            {
                NetworkManager.Instance.HideLoading();
                NetworkManager.Instance.UpdateUIState(true);
            }

            Debug.Log("[Matchmaking] Cancel clicked -> returned to Home Screen");
        }
    }

    private void HidePanelImmediate()
    {
        if (statusCoroutine != null)
        {
            StopCoroutine(statusCoroutine);
            statusCoroutine = null;
        }

        if (spawnCoroutine != null)
        {
            StopCoroutine(spawnCoroutine);
            spawnCoroutine = null;
        }

        if (spinnerTween != null)
        {
            spinnerTween.Kill();
            spinnerTween = null;
        }

        if (matchmakingPanel != null)
        {
            matchmakingPanel.DOKill();
            matchmakingPanel.transform.DOKill();
            matchmakingPanel.alpha = 0f;
            matchmakingPanel.interactable = false;
            matchmakingPanel.blocksRaycasts = false;
            matchmakingPanel.gameObject.SetActive(false);
        }

        if (profileContainer != null)
            profileContainer.gameObject.SetActive(false);

        if (matchedPlayersContainer != null)
            matchedPlayersContainer.SetActive(false);

        if (cancelButton != null)
            cancelButton.gameObject.SetActive(false);
    }

    private void HidePanel()
    {
        if (matchmakingPanel != null)
        {
            matchmakingPanel.DOFade(0, 0.3f).SetUpdate(true).OnComplete(() => {
                matchmakingPanel.interactable = false;
                matchmakingPanel.blocksRaycasts = false;
                matchmakingPanel.gameObject.SetActive(false);
            });
        }
    }

    IEnumerator RotateStatusMessages()
    {
        int index = 0;
        while (isSearching)
        {
            yield return new WaitForSeconds(2.5f);
            if (index < fakeStatusMessages.Length)
            {
                statusText.DOFade(0, 0.3f).SetUpdate(true).OnComplete(() => {
                    statusText.text = fakeStatusMessages[index];
                    statusText.DOFade(1, 0.3f).SetUpdate(true);
                    index++;
                });
            }
        }
    }

    IEnumerator SpawnProfilesRoutine()
    {
        while (isSearching)
        {
            SpawnProfile();
            yield return new WaitForSeconds(Random.Range(spawnInterval * 0.8f, spawnInterval * 1.5f));
        }
    }

    void SpawnProfile()
    {
        GameObject profile = GetProfileFromPool();
        profile.SetActive(true);
        
        RectTransform rt = profile.GetComponent<RectTransform>();
        Image img = profile.GetComponentInChildren<Image>();
        CanvasGroup cg = profile.GetComponent<CanvasGroup>();

        if (profileSprites.Count > 0)
            img.sprite = profileSprites[Random.Range(0, profileSprites.Count)];

        float xPos = Random.Range(-profileContainer.rect.width * 0.45f, profileContainer.rect.width * 0.45f);
        rt.anchoredPosition = new Vector2(xPos, -profileContainer.rect.height * 0.6f);
        
        cg.alpha = 0;
        float randomScale = Random.Range(0.8f, 1.2f);
        rt.localScale = Vector3.one * randomScale * 0.5f;

        Sequence seq = DOTween.Sequence().SetUpdate(true);
        seq.Append(cg.DOFade(0.6f, 0.5f));
        seq.Join(rt.DOScale(randomScale, 0.5f));
        seq.Join(rt.DOAnchorPosY(profileContainer.rect.height * 0.6f, scrollDuration).SetEase(Ease.OutSine));
        seq.Insert(scrollDuration - 0.5f, cg.DOFade(0, 0.5f));
        seq.Insert(scrollDuration - 0.5f, rt.DOScale(0.5f, 0.5f));
        seq.OnComplete(() => {
            if (profile != null) profile.SetActive(false);
        });
    }

    GameObject GetProfileFromPool()
    {
        foreach (var p in profilePool)
        {
            if (p != null && !p.activeInHierarchy) return p;
        }
        GameObject newP = Instantiate(profilePrefab, profileContainer);
        profilePool.Add(newP);
        return newP;
    }

    /// <summary>
    /// Professional transition routine handling "Match Found" through to "Dealing Starts".
    /// </summary>
    IEnumerator MatchFoundRoutine()
    {
        bool isOffline = PhotonNetwork.OfflineMode;

        // --- FAST PATH FOR BOTS ---
        if (isOffline)
        {
            Debug.Log("🤖 Instant Bot Match: Bypassing matchmaking UI.");
            
            // Immediately hide matchmaking
            if (matchmakingPanel != null)
            {
                matchmakingPanel.alpha = 0;
                matchmakingPanel.interactable = false;
                matchmakingPanel.blocksRaycasts = false;
                matchmakingPanel.gameObject.SetActive(false);
            }

            // Immediately show game scene
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.ShowGameScene();

            // Immediately start dealing
            if (PhotonNetwork.IsMasterClient && DeckManager.Instance != null)
            {
                DeckManager.Instance.StartFullDealingSequence();
            }
            isMatchFoundRoutineRunning = false;
            yield break;
        }

        // --- STAGE 2: MATCH FOUND (Online Only) ---
        titleText.text = "Players Found!";
        statusText.text = "Initializing match...";

        AudioSource audioSource = GetComponent<AudioSource>();
        if (audioSource != null && audioSource.clip != null) audioSource.Play();

        if (cancelButton != null) cancelButton.gameObject.SetActive(false);
        if (spinner != null) spinner.DOScale(0, 0.3f).SetUpdate(true);
        if (profileContainer != null) profileContainer.gameObject.SetActive(false);

        if (matchedPlayersContainer != null)
        {
            matchedPlayersContainer.SetActive(true);
            matchedPlayersContainer.transform.localScale = Vector3.one; // Ensure visible
            matchedPlayersContainer.transform.DOScale(1.0f, 0.5f).SetEase(Ease.OutBack).SetUpdate(true);

            foreach (Transform child in matchedPlayersContainer.transform) Destroy(child.gameObject);
            for (int i = 0; i < 4; i++)
            {
                GameObject p = Instantiate(profilePrefab, matchedPlayersContainer.transform);
                p.SetActive(true);
                
                p.GetComponent<CanvasGroup>().alpha = 1;
                p.GetComponent<RectTransform>().localScale = Vector3.one;
                if (profileSprites.Count > 0)
                    p.GetComponentInChildren<Image>().sprite = profileSprites[Random.Range(0, profileSprites.Count)];

                p.transform.localScale = Vector3.zero;
                p.transform.DOScale(1.0f, 0.3f).SetDelay(i * 0.1f).SetEase(Ease.OutBack).SetUpdate(true);
            }
        }

        if (matchmakingPanel != null)
            matchmakingPanel.transform.DOPunchScale(Vector3.one * 0.05f, 0.5f, 10, 1).SetUpdate(true);

        yield return new WaitForSeconds(2.5f);

        // --- STAGE 3: TRANSITION (Fade Out UI, Fade In Scene) ---
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.UpdateUIState(false);
        }

        if (matchmakingPanel != null)
        {
            matchmakingPanel.DOFade(0, 0.8f).SetUpdate(true);
            matchmakingPanel.interactable = false;
            matchmakingPanel.blocksRaycasts = false;
        }

        yield return new WaitForSeconds(0.8f);

        if (matchmakingPanel != null) matchmakingPanel.gameObject.SetActive(false);

        // --- STAGE 4: GAME SCENE FULLY VISIBLE ---
        yield return new WaitForSeconds(0.5f); 

        // --- STAGE 5: START DEAL ANIMATION ---
        if (PhotonNetwork.IsMasterClient && DeckManager.Instance != null)
        {
            DeckManager.Instance.StartFullDealingSequence();
        }
        isMatchFoundRoutineRunning = false;
    }

    public void OnClick_Cancel()
    {
        OnCancelClicked();
    }

    public void OnCancelClicked()
    {
        Debug.Log("[Cancel] Matchmaking cancel clicked -> force Home Screen");
        WasCancelledByUser = true;
        isSearching = false;
        isMatchFoundRoutineRunning = false;

        if (ModeManager.Instance != null)
            ModeManager.Instance.CancelPendingMatchmaking();

        ForceReturnToHomeFromCancel();
    }

    private void ForceReturnToHomeFromCancel()
    {
        Debug.Log("[Cancel] ForceReturnToHomeFromCancel started");

        // Stop matchmaking coroutines
        if (statusCoroutine != null)
        {
            StopCoroutine(statusCoroutine);
            statusCoroutine = null;
        }

        if (spawnCoroutine != null)
        {
            StopCoroutine(spawnCoroutine);
            spawnCoroutine = null;
        }

        // Kill tweens
        if (spinnerTween != null)
        {
            spinnerTween.Kill();
            spinnerTween = null;
        }

        if (matchmakingPanel != null)
        {
            matchmakingPanel.DOKill();
            matchmakingPanel.transform.DOKill();
            matchmakingPanel.alpha = 0;
            matchmakingPanel.interactable = false;
            matchmakingPanel.blocksRaycasts = false;
            matchmakingPanel.gameObject.SetActive(false);
        }

        if (profileContainer != null)
            profileContainer.gameObject.SetActive(false);

        if (matchedPlayersContainer != null)
            matchedPlayersContainer.SetActive(false);

        if (cancelButton != null)
            cancelButton.gameObject.SetActive(false);

        // Stop Photon pending flow
        if (PhotonNetwork.InRoom)
            PhotonNetwork.LeaveRoom();
        else if (PhotonNetwork.InLobby)
            PhotonNetwork.LeaveLobby();

        if (PhotonNetwork.OfflineMode)
            PhotonNetwork.OfflineMode = false;

        // Force state Home
        GameFlowState.SetPhase(GameFlowPhase.Home, true);

        // Force correct panels
        if (ModeManager.Instance != null)
        {
            if (ModeManager.Instance.panelModes != null)
                ModeManager.Instance.panelModes.SetActive(false);

            if (ModeManager.Instance.panelHomeScreen != null)
                ModeManager.Instance.panelHomeScreen.SetActive(true);

            ModeManager.Instance.ApplyHomeScreenButtonColors();
        }

        // Hide network loading
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.HideLoading();
            NetworkManager.Instance.UpdateUIState(true);
        }

        // Hide game scene UI if currently visible
        GameObject panelGame = GameObject.Find("Panel_Game");
        if (panelGame == null) panelGame = GameObject.Find("[Panel_Game]");
        if (panelGame != null)
            panelGame.SetActive(false);

        GameObject gamePanel = GameObject.Find("GamePanel");
        if (gamePanel != null)
            gamePanel.SetActive(false);

        GameObject trumpUI = GameObject.Find("TrumpUI");
        if (trumpUI != null)
            trumpUI.SetActive(false);

        Debug.Log("[Cancel] Returned to Home Screen successfully");
    }

    void OnApplicationPause(bool paused)
    {
        if (!paused) RefreshUIAfterResume();
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus) RefreshUIAfterResume();
    }

    public void RefreshUIAfterResume()
    {
        if (!isSearching || matchmakingPanel == null) return;

        matchmakingPanel.DOKill();
        if (spinner != null) spinner.DOKill();
        if (statusText != null) statusText.DOKill();

        matchmakingPanel.gameObject.SetActive(true);
        matchmakingPanel.alpha = 1f;
        matchmakingPanel.interactable = true;
        matchmakingPanel.blocksRaycasts = true;
        matchmakingPanel.transform.localScale = Vector3.one;

        if (spinner != null)
        {
            spinner.localScale = Vector3.one;
            spinnerTween = spinner.DORotate(new Vector3(0, 0, -360), 1.5f, RotateMode.FastBeyond360)
                .SetLoops(-1, LoopType.Incremental)
                .SetEase(Ease.Linear)
                .SetUpdate(true);
        }

        if (titleText != null && string.IsNullOrEmpty(titleText.text))
            titleText.text = "Finding Players...";
        if (statusText != null)
        {
            statusText.alpha = 1f;
            if (string.IsNullOrEmpty(statusText.text))
                statusText.text = "Searching for players...";
        }

        if (isSearching && statusCoroutine == null)
            statusCoroutine = StartCoroutine(RotateStatusMessages());
        if (isSearching && spawnCoroutine == null)
            spawnCoroutine = StartCoroutine(SpawnProfilesRoutine());
    }
}
