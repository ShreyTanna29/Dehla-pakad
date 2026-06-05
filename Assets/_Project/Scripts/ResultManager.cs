using UnityEngine;
using TMPro;
using UnityEngine.UI;
using DG.Tweening;
using Photon.Pun;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class ResultManager : MonoBehaviourPunCallbacks
{
    public static ResultManager Instance;

    [Header("UI Root")]
    public CanvasGroup resultPanel;
    [Tooltip("Optional root to find Panel_Winning without GameObject.Find (e.g. game canvas).")]
    public Transform resultPanelSearchRoot;
    public TMP_FontAsset customFont;

    [Header("Optional — wired in scene or auto-built")]
    public TMP_Text titleText;
    public TMP_Text descriptionText;
    public Button homeButton;
    public Button restartButton;
    public Transform scoreboardContainer;

    [System.Serializable]
    public class PlayerResult
    {
        public string name;
        public int actorNumber;
        public int bid; // Restored
        public int tricksWon;
        public int dehlasCollected;
        public bool isCompleted;
        public float score;
        public int rank;
    }

    private PlayerResult[] playerResults = new PlayerResult[4];
    private Transform _builtRoot;
    private Image _dimOverlay;
    private readonly List<GameObject> _dynamicRows = new List<GameObject>();
    private bool _isShowingResult;
    private static bool _resultPanelResolveWarned;

    // Professional Theme Colors
    static readonly Color PanelBgColor = new Color(0.25f, 0.15f, 0.05f, 0.95f); // Wooden Dark
    static readonly Color FrameColor = new Color(0.45f, 0.28f, 0.15f, 1f);     // Wooden Frame
    static readonly Color RowBgColor = new Color(0f, 0f, 0f, 0.35f);           // Semi-transparent rows
    static readonly Color WinnerGoldColor = new Color(1f, 0.84f, 0f, 1f);      // Gold highlight
    static readonly Color TextWhiteColor = Color.white;
    static readonly Color TextGoldColor = new Color(1f, 0.92f, 0.5f, 1f);

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        for (int i = 0; i < 4; i++)
            playerResults[i] = new PlayerResult { name = GetInitialPlayerName(i) };

        HideResultPanelImmediate();
        WireButtons();
    }

    void WireButtons()
    {
        if (homeButton != null)
        {
            homeButton.onClick.RemoveAllListeners();
            homeButton.onClick.AddListener(OnHomeClicked);
        }
        if (restartButton != null)
        {
            restartButton.onClick.RemoveAllListeners();
            restartButton.onClick.AddListener(OnRestartClicked);
        }
    }

    void HideResultPanelImmediate()
    {
        _isShowingResult = false;
        if (!ResolveResultPanel()) return;
        resultPanel.DOKill();
        resultPanel.alpha = 0;
        resultPanel.interactable = false;
        resultPanel.blocksRaycasts = false;
        resultPanel.gameObject.SetActive(false);
        if (_dimOverlay != null)
            _dimOverlay.color = new Color(0f, 0f, 0f, 0f);
    }

    bool ResolveResultPanel()
    {
        if (resultPanel != null) return true;

        Transform root = resultPanelSearchRoot;
        if (root == null)
        {
            Canvas canvas = Object.FindAnyObjectByType<Canvas>();
            if (canvas != null)
                root = canvas.transform.root;
        }

        if (root != null)
        {
            UiSafeLookup.SetSearchRoot(root);
            if (UiSafeLookup.TryGet("Panel_Winning", out GameObject panelGo) && panelGo != null)
            {
                resultPanel = panelGo.GetComponent<CanvasGroup>();
                if (resultPanel == null)
                    resultPanel = panelGo.AddComponent<CanvasGroup>();
                Debug.Log("[ResultManager] Resolved Panel_Winning under canvas hierarchy.");
                return true;
            }
        }

        if (!_resultPanelResolveWarned)
        {
            _resultPanelResolveWarned = true;
            Debug.LogWarning("[ResultManager] resultPanel not found — assign resultPanel or Panel_Winning under resultPanelSearchRoot.");
        }
        return false;
    }

    void EnsurePanelHierarchyActive()
    {
        if (resultPanel == null) return;

        Transform t = resultPanel.transform;
        while (t != null)
        {
            if (!t.gameObject.activeSelf)
                t.gameObject.SetActive(true);
            t = t.parent;
        }

        Canvas rootCanvas = resultPanel.GetComponentInParent<Canvas>();
        if (rootCanvas != null)
            rootCanvas.gameObject.SetActive(true);

        resultPanel.transform.SetAsLastSibling();
    }

    void EnsureDimOverlay()
    {
        if (resultPanel == null) return;

        Transform existing = resultPanel.transform.Find("Overlay");
        if (existing != null)
        {
            _dimOverlay = existing.GetComponent<Image>();
            if (_dimOverlay == null)
                _dimOverlay = existing.gameObject.AddComponent<Image>();
        }
        else
        {
            GameObject overlayGo = new GameObject("Overlay", typeof(RectTransform), typeof(Image));
            overlayGo.transform.SetParent(resultPanel.transform, false);
            overlayGo.transform.SetAsFirstSibling();
            RectTransform rt = overlayGo.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            _dimOverlay = overlayGo.GetComponent<Image>();
            _dimOverlay.raycastTarget = true;
        }

        _dimOverlay.color = new Color(0f, 0f, 0f, 0.55f);
        _dimOverlay.raycastTarget = true;
    }

    string GetInitialPlayerName(int i) => i == 0 ? "You" : "Dehla_AI_" + i;

    public void SetBid(int seatIndex, int bidValue)
    {
        if (seatIndex >= 0 && seatIndex < 4)
            playerResults[seatIndex].bid = bidValue;
    }

    public void OnTrickWon(int winnerSeatIndex, int dehlaCount)
    {
        if (winnerSeatIndex < 0 || winnerSeatIndex >= 4) return;
        playerResults[winnerSeatIndex].tricksWon++;
        playerResults[winnerSeatIndex].dehlasCollected += dehlaCount;

        if (PhotonNetwork.IsMasterClient)
            SyncScoresToRoomProperties();
    }

    void SyncScoresToRoomProperties()
    {
        if (!PhotonNetwork.InRoom) return;
        int[] tricks = new int[4];
        int[] dehlas = new int[4];
        for (int i = 0; i < 4; i++)
        {
            tricks[i] = playerResults[i].tricksWon;
            dehlas[i] = playerResults[i].dehlasCollected;
        }
        PhotonNetwork.CurrentRoom.SetCustomProperties(
            new ExitGames.Client.Photon.Hashtable { { "SW", tricks }, { "DL", dehlas } });
    }

    public override void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable propertiesThatChanged)
    {
        if (propertiesThatChanged == null) return;
        if (propertiesThatChanged.ContainsKey("SW") &&
            PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("SW", out object tricksObj))
        {
            int[] tricks = (int[])tricksObj;
            for (int i = 0; i < 4 && i < tricks.Length; i++)
                playerResults[i].tricksWon = tricks[i];
        }
        if (propertiesThatChanged.ContainsKey("DL") &&
            PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("DL", out object dehlaObj))
        {
            int[] dehlas = (int[])dehlaObj;
            for (int i = 0; i < 4 && i < dehlas.Length; i++)
                playerResults[i].dehlasCollected = dehlas[i];
        }
    }

    [ContextMenu("Show Test Result")]
    public void ShowResult()
    {
        if (_isShowingResult)
        {
            Debug.LogWarning("[Result] ShowResult ignored — already showing.");
            return;
        }
        if (!ResolveResultPanel())
        {
            Debug.LogError("[Result] ShowResult aborted — result panel reference missing.");
            return;
        }

        _isShowingResult = true;
        Debug.Log("Result Panel Opening");
        EnsurePanelHierarchyActive();
        EnsureDimOverlay();

        RefreshPlayerNamesAndActors();
        CalculateScores();
        AssignRanks();

        PlayerResult winner = playerResults.OrderBy(p => p.rank).FirstOrDefault();
        if (winner != null)
            Debug.Log($"Winner Determined: {winner.name} (Rank #{winner.rank}, Score {winner.score})");
        else
            Debug.Log("Winner Determined: (none)");

        BuildResultPanelUI();
        Debug.Log("Result Data Loaded");

        resultPanel.gameObject.SetActive(true);
        resultPanel.DOKill();
        resultPanel.alpha = 0;
        resultPanel.interactable = true;
        resultPanel.blocksRaycasts = true;
        Debug.Log("Result Panel Opened");

        if (_dimOverlay != null)
        {
            Color c = _dimOverlay.color;
            c.a = 0f;
            _dimOverlay.color = c;
            _dimOverlay.DOFade(0.55f, 0.35f).SetUpdate(true);
        }

        resultPanel.DOFade(1f, 0.4f).SetUpdate(true).OnComplete(() =>
        {
            resultPanel.alpha = 1f;
        });

        Transform frame = resultPanel.transform.Find("MainFrame");
        if (frame != null)
        {
            frame.DOKill();
            frame.localScale = Vector3.one * 0.92f;
            frame.DOScale(1f, 0.5f).SetEase(Ease.OutBack).SetUpdate(true);
            frame.SetAsLastSibling();
        }

        StartCoroutine(AnimateRowsRoutine());

        if (TurnManager.Instance != null)
            TurnManager.Instance.StopTimer();
    }

    System.Collections.IEnumerator AnimateRowsRoutine()
    {
        foreach (var row in _dynamicRows)
        {
            if (row == null) continue;
            row.transform.localScale = new Vector3(1, 0, 1);
            row.GetComponent<CanvasGroup>().alpha = 0;
        }

        yield return new WaitForSecondsRealtime(0.5f);

        for (int i = 0; i < _dynamicRows.Count; i++)
        {
            var row = _dynamicRows[i];
            if (row == null) continue;
            
            row.GetComponent<CanvasGroup>().DOFade(1f, 0.3f).SetUpdate(true);
            row.transform.DOScale(Vector3.one, 0.4f).SetEase(Ease.OutBack).SetUpdate(true);
            
            yield return new WaitForSecondsRealtime(0.15f);
        }
    }

    void RefreshPlayerNamesAndActors()
    {
        for (int seat = 0; seat < 4; seat++)
        {
            playerResults[seat].name = GetSeatDisplayName(seat);
            playerResults[seat].actorNumber = GetActorNumberBySeat(seat);
        }
    }

    int GetActorNumberBySeat(int seatIndex)
    {
        if (PlayerHand.LocalInstance == null) return seatIndex; 
        
        // tableTurnOrder is indexed by visual seat (0-3).
        var field = typeof(PlayerHand).GetField("tableTurnOrder", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field == null) return -1;
        
        var turnOrder = (List<int>)field.GetValue(PlayerHand.LocalInstance);
        if (turnOrder != null && seatIndex < turnOrder.Count)
            return turnOrder[seatIndex];
            
        return -1;
    }

    string GetSeatDisplayName(int seatIndex)
    {
        if (seatIndex == 0)
            return PhotonNetwork.NickName ?? "You";

        if (PlayerProfileSync.Instance != null)
        {
            switch (seatIndex)
            {
                case 1 when PlayerProfileSync.Instance.txtLeftName != null:
                    return CleanName(PlayerProfileSync.Instance.txtLeftName.text);
                case 2 when PlayerProfileSync.Instance.txtTopName != null:
                    return CleanName(PlayerProfileSync.Instance.txtTopName.text);
                case 3 when PlayerProfileSync.Instance.txtRightName != null:
                    return CleanName(PlayerProfileSync.Instance.txtRightName.text);
            }
        }
        return "Player " + (seatIndex + 1);
    }

    static string CleanName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "Player";
        return raw.Split('\n')[0].Trim();
    }

    void CalculateScores()
    {
        // Simple scoring based on Dehlas and tricks
        foreach (var p in playerResults)
        {
            p.score = (p.tricksWon * 10) + (p.dehlasCollected * 20);
            p.isCompleted = true;
        }
    }

    void AssignRanks()
    {
        var sorted = playerResults.OrderByDescending(p => p.score).ToList();
        for (int i = 0; i < sorted.Count; i++)
            sorted[i].rank = i + 1;
    }

    void BuildResultPanelUI()
    {
        ClearDynamicUI();

        if (resultPanel == null) return;
        
        // Ensure MainFrame exists
        Transform mainFrame = resultPanel.transform.Find("MainFrame");
        if (mainFrame == null)
        {
            mainFrame = CreateRect("MainFrame", resultPanel.transform, Vector2.zero, new Vector2(800, 1000)).transform;
        }
        else
        {
            // Clear previous children except specific ones if needed, but easier to just use containers
            foreach (Transform child in mainFrame)
            {
                if (child.name != "ButtonsContainer" && child.name != "Title" && child.name != "Description")
                    Destroy(child.gameObject);
            }
        }

        AddImage(mainFrame.gameObject, FrameColor);
        var shadow = mainFrame.gameObject.GetComponent<Shadow>();
        if (shadow == null) shadow = mainFrame.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0, 0, 0, 0.5f);
        shadow.effectDistance = new Vector2(5, -5);

        PlayerResult winner = playerResults.OrderBy(p => p.rank).First();

        // Header Section
        var headerGo = CreateRect("Header", mainFrame, new Vector2(0, 350), new Vector2(750, 250));
        var vlgHeader = headerGo.AddComponent<VerticalLayoutGroup>();
        vlgHeader.childAlignment = TextAnchor.MiddleCenter;
        vlgHeader.spacing = 10;
        vlgHeader.childControlHeight = vlgHeader.childControlWidth = false;

        AddTmp(headerGo.transform, "WINNER", WinnerGoldColor, 64, TextAlignmentOptions.Center, FontStyles.Bold);
        
        // Winner Avatar
        var avatarFrame = CreateRect("WinnerAvatarFrame", headerGo.transform, Vector2.zero, new Vector2(120, 120));
        var avatarImgGo = CreateRect("AvatarImage", avatarFrame.transform, Vector2.zero, new Vector2(110, 110));
        var img = AddImage(avatarImgGo, Color.white);
        img.sprite = GetAvatarSprite(winner.actorNumber);
        img.preserveAspect = true;
        AddImage(avatarFrame.gameObject, WinnerGoldColor); // Gold border

        AddTmp(headerGo.transform, winner.name.ToUpper(), TextWhiteColor, 36, TextAlignmentOptions.Center, FontStyles.Bold);

        // Scoreboard Section
        var scoreboardGo = CreateRect("Scoreboard", mainFrame, new Vector2(0, -50), new Vector2(700, 500));
        var vlgScore = scoreboardGo.AddComponent<VerticalLayoutGroup>();
        vlgScore.spacing = 15;
        vlgScore.padding = new RectOffset(10, 10, 10, 10);
        vlgScore.childControlWidth = true;
        vlgScore.childForceExpandHeight = false;

        foreach (var p in playerResults.OrderBy(x => x.rank))
        {
            CreateScoreRow(scoreboardGo.transform, p);
        }

        Transform btnContainer = mainFrame.Find("ButtonsContainer");
        if (btnContainer == null)
        {
            btnContainer = CreateRect("ButtonsContainer", mainFrame, new Vector2(0, -400), new Vector2(600, 100)).transform;
            var hlg = btnContainer.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 40;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = hlg.childControlHeight = true;
        }
        btnContainer.SetAsLastSibling();

        EnsureButton(ref restartButton, btnContainer, "RestartButton", "PLAY AGAIN", new Color(0.1f, 0.6f, 0.2f), OnRestartClicked);
        EnsureButton(ref homeButton, btnContainer, "HomeButton", "HOME", new Color(0.6f, 0.2f, 0.1f), OnHomeClicked);
    }

    void CreateScoreRow(Transform parent, PlayerResult p)
    {
        bool isWinner = p.rank == 1;
        Color bg = isWinner ? new Color(0.5f, 0.4f, 0.1f, 0.6f) : RowBgColor;
        
        var row = new GameObject("PlayerRow", typeof(RectTransform), typeof(CanvasGroup), typeof(Image), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(parent, false);
        var le = row.AddComponent<LayoutElement>();
        le.preferredHeight = 80;
        
        var img = row.GetComponent<Image>();
        img.color = bg;
        
        var hlg = row.GetComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(20, 20, 5, 5);
        hlg.spacing = 20;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = false;
        hlg.childForceExpandWidth = false;

        // Rank
        var rankText = AddTmp(row.transform, $"#{p.rank}", isWinner ? WinnerGoldColor : TextWhiteColor, 32, TextAlignmentOptions.Left, FontStyles.Bold);
        rankText.rectTransform.sizeDelta = new Vector2(60, 50);

        // Avatar
        var avatarFrame = CreateRect("AvatarFrame", row.transform, Vector2.zero, new Vector2(60, 60));
        var avatarImgGo = CreateRect("Avatar", avatarFrame.transform, Vector2.zero, new Vector2(56, 56));
        var aImg = AddImage(avatarImgGo, Color.white);
        aImg.sprite = GetAvatarSprite(p.actorNumber);
        aImg.preserveAspect = true;
        if (isWinner) AddImage(avatarFrame.gameObject, WinnerGoldColor);

        // Name
        var nameText = AddTmp(row.transform, p.name, isWinner ? WinnerGoldColor : TextWhiteColor, 28, TextAlignmentOptions.Left);
        nameText.rectTransform.sizeDelta = new Vector2(200, 50);
        nameText.overflowMode = TextOverflowModes.Ellipsis;

        // Score
        AddTmp(row.transform, $"Score: {p.score}", TextGoldColor, 26, TextAlignmentOptions.Right).rectTransform.sizeDelta = new Vector2(140, 50);

        // Dehlas
        AddTmp(row.transform, $"Dehlas: {p.dehlasCollected}", TextWhiteColor, 26, TextAlignmentOptions.Right).rectTransform.sizeDelta = new Vector2(120, 50);

        _dynamicRows.Add(row);
    }

    Sprite GetAvatarSprite(int actorNumber)
    {
        List<Sprite> pool = MatchmakingManager.GlobalProfileSprites;
        if (pool == null || pool.Count == 0) return null;
        int spriteIndex = Mathf.Abs(actorNumber) % pool.Count;
        return pool[spriteIndex];
    }

    GameObject CreateRect(string name, Transform parent, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        return go;
    }

    static Image AddImage(GameObject go, Color c)
    {
        var img = go.GetComponent<Image>();
        if (img == null) img = go.AddComponent<Image>();
        img.color = c;
        return img;
    }

    TextMeshProUGUI AddTmp(Transform parent, string text, Color color, int size, TextAlignmentOptions align, FontStyles style = FontStyles.Normal)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(200, 50); // Default size, usually overridden by layout
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.color = color;
        tmp.fontSize = size;
        tmp.alignment = align;
        tmp.fontStyle = style;
        tmp.raycastTarget = false;
        if (customFont != null) tmp.font = customFont;
        return tmp;
    }

    void EnsureButton(ref Button btn, Transform parent, string goName, string label, Color bgColor, UnityEngine.Events.UnityAction action)
    {
        if (btn == null)
        {
            var go = CreateRect(goName, parent, Vector2.zero, new Vector2(250, 80));
            AddImage(go, bgColor);
            btn = go.AddComponent<Button>();
            btn.targetGraphic = go.GetComponent<Image>();
            AddTmp(go.transform, label, Color.white, 28, TextAlignmentOptions.Center, FontStyles.Bold);
        }
        else
        {
            btn.transform.SetParent(parent, false);
            var img = btn.GetComponent<Image>();
            if (img != null) img.color = bgColor;
            var tmp = btn.GetComponentInChildren<TMP_Text>();
            if (tmp != null) tmp.text = label;
        }

        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(action);
        
        var colors = btn.colors;
        colors.highlightedColor = bgColor * 1.2f;
        colors.pressedColor = bgColor * 0.8f;
        btn.colors = colors;
    }

    void ClearDynamicUI()
    {
        foreach (var go in _dynamicRows)
            if (go != null) Destroy(go);
        _dynamicRows.Clear();
    }

    void OnHomeClicked()
    {
        Debug.Log("[UI] Button Clicked: Home (from results)");
        HideResultPanelImmediate();
        ResetMatchStats();

        if (PhotonNetwork.InRoom)
            PhotonNetwork.LeaveRoom();
        else if (PhotonNetwork.OfflineMode)
        {
            PhotonNetwork.LeaveRoom();
            PhotonNetwork.OfflineMode = false;
        }

        if (DeckManager.Instance != null)
            DeckManager.Instance.ResetMatchState();

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.ReturnToHomeScreen();
    }

    void OnRestartClicked()
    {
        Debug.Log("[UI] Button Clicked: Play Again");
        HideResultPanelImmediate();
        ResetMatchStats();

        if (DeckManager.Instance != null)
            DeckManager.Instance.ResetMatchState();

        GameFlowState.SetPhase(GameFlowPhase.InRoom);

        if (PhotonNetwork.OfflineMode)
        {
            if (DeckManager.Instance != null && PhotonNetwork.IsMasterClient)
                DeckManager.Instance.FillBotsAndStart();
        }
        else if (PhotonNetwork.IsMasterClient && DeckManager.Instance != null)
        {
            DeckManager.Instance.FillBotsAndStart();
        }
    }

    void ResetMatchStats()
    {
        for (int i = 0; i < 4; i++)
        {
            playerResults[i].tricksWon = 0;
            playerResults[i].dehlasCollected = 0;
            playerResults[i].score = 0;
            playerResults[i].isCompleted = false;
            playerResults[i].rank = 0;
        }
    }
}
