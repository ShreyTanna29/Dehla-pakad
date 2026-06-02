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
        // For Bots mode or if we were searching, we want the transition
        if (!isSearching && !isMatchFound) return;
        
        isSearching = false;

        if (statusCoroutine != null) StopCoroutine(statusCoroutine);
        if (spawnCoroutine != null) StopCoroutine(spawnCoroutine);
        if (spinnerTween != null) spinnerTween.Kill();

        if (isMatchFound)
        {
            StartCoroutine(MatchFoundRoutine());
        }
        else
        {
            HidePanel();
            if (PhotonNetwork.InRoom) PhotonNetwork.LeaveRoom();
            
            // Return to Mode Selection when cancelled
            if (ModeManager.Instance != null)
            {
                ModeManager.Instance.OpenModePanelFromHome();
            }
        }
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
            if (NetworkManager.Instance != null && NetworkManager.Instance.gameCanvasGroup != null)
            {
                NetworkManager.Instance.gameCanvasGroup.alpha = 1;
                NetworkManager.Instance.gameCanvasGroup.interactable = true;
                NetworkManager.Instance.gameCanvasGroup.blocksRaycasts = true;
            }

            // Immediately start dealing
            if (PhotonNetwork.IsMasterClient && DeckManager.Instance != null)
            {
                DeckManager.Instance.StartFullDealingSequence();
            }
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
            matchedPlayersContainer.transform.localScale = Vector3.zero;
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
        Debug.Log("🚀 Stage 3: Transitioning to Game Scene...");

        if (matchmakingPanel != null)
        {
            matchmakingPanel.DOFade(0, 0.8f).SetUpdate(true);
            matchmakingPanel.interactable = false;
            matchmakingPanel.blocksRaycasts = false;
        }

        if (NetworkManager.Instance != null && NetworkManager.Instance.gameCanvasGroup != null)
        {
            CanvasGroup gameCG = NetworkManager.Instance.gameCanvasGroup;
            gameCG.DOFade(1, 0.8f).SetUpdate(true);
            gameCG.interactable = true;
            gameCG.blocksRaycasts = true;
        }

        yield return new WaitForSeconds(0.8f);

        if (matchmakingPanel != null) matchmakingPanel.gameObject.SetActive(false);

        // --- STAGE 4: GAME SCENE FULLY VISIBLE ---
        Debug.Log("🚀 Stage 4: Game Scene Fully Visible.");
        yield return new WaitForSeconds(0.5f); 

        // --- STAGE 5: START DEAL ANIMATION ---
        if (PhotonNetwork.IsMasterClient && DeckManager.Instance != null)
        {
            Debug.Log("🃏 Stage 5: Master Client starting dealing sequence.");
            DeckManager.Instance.StartFullDealingSequence();
        }
    }

    public void OnClick_Cancel()
    {
        StopSearching(false);
    }
}
