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

    [Header("Leaderboard Theme (assign in scene)")]
    [Tooltip("Rounded wooden board sprite used for the panel background (e.g. BG_Buttons).")]
    public Sprite woodBoardSprite;
    [Tooltip("Settings gear icon sprite (e.g. settings_button).")]
    public Sprite gearButtonSprite;

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

    const int RoundsPerMatch = 3;
    const int KotDehlasOneTaash = 4;
    const int KotDehlasTwoTaash = 8;
    readonly int[][] _roundDehlas = new int[RoundsPerMatch][];
    int _currentRoundIndex;

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

        for (int r = 0; r < RoundsPerMatch; r++)
            _roundDehlas[r] = new int[4];

        HideResultPanelImmediate();
        WireButtons();
    }

    void WireButtons()
    {
        if (homeButton != null)
        {
            EnableButtonVisuals(homeButton);
            homeButton.onClick.RemoveAllListeners();
            homeButton.onClick.AddListener(OnHomeClicked);
        }
        if (restartButton != null)
        {
            EnableButtonVisuals(restartButton);
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

    public void CloseResult()
    {
        HideResultPanelImmediate();
    }

    void EnsurePlayerResults()
    {
        if (playerResults == null || playerResults.Length < 4)
            playerResults = new PlayerResult[4];

        for (int i = 0; i < 4; i++)
        {
            if (playerResults[i] == null)
                playerResults[i] = new PlayerResult { name = GetInitialPlayerName(i) };
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
        EnsurePlayerResults();
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

        FinalizeCurrentRoundScores();
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

    void FinalizeCurrentRoundScores()
    {
        if (_currentRoundIndex < 0 || _currentRoundIndex >= RoundsPerMatch) return;

        for (int seat = 0; seat < 4; seat++)
            _roundDehlas[_currentRoundIndex][seat] = playerResults[seat].dehlasCollected;

        Debug.Log($"[Result] Round R{_currentRoundIndex + 1} finalized: " +
                  string.Join(", ", Enumerable.Range(0, 4).Select(i => $"{playerResults[i].name}={_roundDehlas[_currentRoundIndex][i]}")));
    }

    static int GetKotThreshold()
    {
        return TaashRules.IsTwoTaashMode ? KotDehlasTwoTaash : KotDehlasOneTaash;
    }

    static string FormatDehlaScore(int dehlas)
    {
        int kotThreshold = GetKotThreshold();
        return dehlas == kotThreshold ? $"{dehlas} (KOT)" : dehlas.ToString();
    }

    static int SumRound(int[] roundScores)
    {
        int total = 0;
        for (int i = 0; i < roundScores.Length; i++)
            total += roundScores[i];
        return total;
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
        EnsurePlayerResults();
        var sorted = playerResults.OrderByDescending(p => p.score).ToList();
        for (int i = 0; i < sorted.Count; i++)
            sorted[i].rank = i + 1;
    }

    void BuildResultPanelUI()
    {
        ClearDynamicUI();

        if (resultPanel == null) return;
        
        // Load Wood Board background safely
#if UNITY_EDITOR
        if (woodBoardSprite == null)
        {
            woodBoardSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/Sprites/Images/BG_Buttons.png");
        }
#endif

        // Ensure MainFrame exists and set its size & sprite to match the wooden board style
        Transform mainFrame = resultPanel.transform.Find("MainFrame");
        if (mainFrame == null)
        {
            mainFrame = CreateRect("MainFrame", resultPanel.transform, Vector2.zero, new Vector2(1200, 780)).transform;
        }
        else
        {
            ClearDynamicMainFrameContent(mainFrame);
        }

        // Apply Wood Background to MainFrame
        var mainFrameImage = AddImage(mainFrame.gameObject, Color.white);
        if (woodBoardSprite != null)
        {
            mainFrameImage.sprite = woodBoardSprite;
            mainFrameImage.type = Image.Type.Simple;
        }
        else
        {
            // Fallback to high-quality dark theme if sprite is missing
            mainFrameImage.color = PanelBgColor;
        }

        var rectMf = mainFrame.GetComponent<RectTransform>();
        if (rectMf != null)
        {
            rectMf.sizeDelta = new Vector2(1200, 780);
            rectMf.anchoredPosition = Vector2.zero;
        }

        var shadow = mainFrame.gameObject.GetComponent<Shadow>();
        if (shadow == null) shadow = mainFrame.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0, 0, 0, 0.4f);
        shadow.effectDistance = new Vector2(6, -6);

        // Layout constants (shared so separators / dashed lines align with the rows)
        float headerH = 150f;
        float rowH = 60f;
        float rowSpacing = 14f;
        float sideColWidth = 130f;
        float playerColWidth = 160f;
        float colSpacing = 15f;
        float tableCenterY = 90f;
        float tableContentH = headerH + (4f * rowH) + (4f * rowSpacing); // 446
        float tableWidth = (sideColWidth * 2f) + (playerColWidth * 4f) + (colSpacing * 5f); // 975
        float tableTopEdge = tableCenterY + (tableContentH / 2f);

        // 1. Create Round Close Button (X) at Top Right of the board
        CreateCloseButton(mainFrame);

        // 2. Setup the Scorecard Grid (Table)
        // Scorecard Table holds all columns horizontally
        var scorecardTableGo = CreateRect("ScorecardTable", mainFrame, new Vector2(0, tableCenterY), new Vector2(tableWidth, tableContentH));
        var hlg = scorecardTableGo.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = colSpacing;
        hlg.childAlignment = TextAnchor.UpperCenter;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        // Round scores come from actual dehlas captured each chaal (no fake/demo data).
        int[] r1_scores = _roundDehlas[0];
        int[] r2_scores = _roundDehlas[1];
        int[] r3_scores = _roundDehlas[2];

        int r1_total = SumRound(r1_scores);
        int r2_total = SumRound(r2_scores);
        int r3_total = SumRound(r3_scores);

        // Player totals
        int[] playerTotals = new int[4];
        for (int i = 0; i < 4; i++)
        {
            playerTotals[i] = r1_scores[i] + r2_scores[i] + r3_scores[i];
        }

        int grandTotal = r1_total + r2_total + r3_total;

        // Find winner based on grand totals
        int winningTotal = playerTotals.Max();
        int winnerIndex = System.Array.IndexOf(playerTotals, winningTotal);

        // --- Column 1: ROUNDS ---
        var roundsCol = CreateColumn("RoundsColumn", scorecardTableGo.transform, sideColWidth, tableContentH, rowSpacing);
        CreateLabelCell("ROUNDS", roundsCol.transform, sideColWidth, headerH, true);
        CreateValueCell("R1", roundsCol.transform, sideColWidth, rowH, true);
        CreateValueCell("R2", roundsCol.transform, sideColWidth, rowH, true);
        CreateValueCell("R3", roundsCol.transform, sideColWidth, rowH, true);
        CreateValueCell("TOTAL", roundsCol.transform, sideColWidth, rowH, true);
        _dynamicRows.Add(roundsCol);

        // --- Columns 2-5: PLAYERS ---
        for (int seat = 0; seat < 4; seat++)
        {
            var pCol = CreateColumn($"PlayerColumn_{seat}", scorecardTableGo.transform, playerColWidth, tableContentH, rowSpacing);
            
            // Player Header Cell with Avatar and Name Box
            CreatePlayerHeaderCell(seat, pCol.transform, playerColWidth, headerH);
            
            // R1 score
            CreateValueCell(FormatDehlaScore(r1_scores[seat]), pCol.transform, playerColWidth, rowH, false,
                isCurrentRound: _currentRoundIndex == 0);

            // R2 score
            CreateValueCell(FormatDehlaScore(r2_scores[seat]), pCol.transform, playerColWidth, rowH, false,
                isCurrentRound: _currentRoundIndex == 1);

            // R3 score
            CreateValueCell(FormatDehlaScore(r3_scores[seat]), pCol.transform, playerColWidth, rowH, false,
                isCurrentRound: _currentRoundIndex == 2);
            
            // Total score
            bool isWinner = (seat == winnerIndex);
            CreateValueCell(playerTotals[seat].ToString(), pCol.transform, playerColWidth, rowH, true, isWinner: isWinner);
            
            _dynamicRows.Add(pCol);
        }

        // --- Column 6: TOTAL ---
        var totalCol = CreateColumn("TotalColumn", scorecardTableGo.transform, sideColWidth, tableContentH, rowSpacing);
        CreateLabelCell("TOTAL", totalCol.transform, sideColWidth, headerH, true);
        CreateValueCell(r1_total.ToString(), totalCol.transform, sideColWidth, rowH, true,
            isCurrentRound: _currentRoundIndex == 0);
        CreateValueCell(r2_total.ToString(), totalCol.transform, sideColWidth, rowH, true,
            isCurrentRound: _currentRoundIndex == 1);
        CreateValueCell(r3_total.ToString(), totalCol.transform, sideColWidth, rowH, true,
            isCurrentRound: _currentRoundIndex == 2);
        CreateValueCell(grandTotal.ToString(), totalCol.transform, sideColWidth, rowH, true, isWinner: true);
        _dynamicRows.Add(totalCol);

        // 3. Draw Vertical Column Dividers under MainFrame (computed from column widths)
        float[] sepX = new float[] { -350f, -175f, 0f, 175f, 350f };
        for (int i = 0; i < sepX.Length; i++)
        {
            var line = CreateRect($"SeparatorLine_{i}", mainFrame, new Vector2(sepX[i], tableCenterY), new Vector2(2, tableContentH - 20f));
            AddImage(line, new Color(1f, 1f, 1f, 0.18f));
        }

        // 4. Draw Horizontal Dashed Divider Lines under MainFrame (computed to sit in the row gaps)
        float gap1 = tableTopEdge - headerH - (rowSpacing / 2f);
        float[] dashedY = new float[]
        {
            gap1,
            gap1 - rowH - rowSpacing,
            gap1 - (rowH + rowSpacing) * 2f,
            gap1 - (rowH + rowSpacing) * 3f
        };
        for (int i = 0; i < dashedY.Length; i++)
        {
            var lineGo = CreateRect($"DashedLine_{i}", mainFrame, new Vector2(0, dashedY[i]), new Vector2(tableWidth, 20));
            var txt = AddTmp(lineGo.transform, ". . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . .", new Color(1f, 1f, 1f, 0.25f), 18, TextAlignmentOptions.Center, FontStyles.Bold);
            txt.rectTransform.anchoredPosition = Vector2.zero;
        }

        EnsureSceneButtons(mainFrame);
    }

    void ClearDynamicMainFrameContent(Transform mainFrame)
    {
        if (mainFrame == null) return;

        var toDestroy = new List<GameObject>();
        foreach (Transform child in mainFrame)
        {
            string childName = child.name;
            if (childName == "ScorecardTable" || childName == "CloseButton")
                toDestroy.Add(child.gameObject);
            else if (childName.StartsWith("SeparatorLine_") || childName.StartsWith("DashedLine_"))
                toDestroy.Add(child.gameObject);
        }

        foreach (GameObject go in toDestroy)
            DestroyObjectSafe(go);
    }

    void EnsureSceneButtons(Transform mainFrame)
    {
        Transform btnContainer = mainFrame.Find("ButtonsContainer");
        if (btnContainer == null)
        {
            var btnContainerGo = CreateRect("ButtonsContainer", mainFrame, new Vector2(0, -300), new Vector2(700, 90));
            var hlgBtn = btnContainerGo.AddComponent<HorizontalLayoutGroup>();
            hlgBtn.spacing = 60;
            hlgBtn.childAlignment = TextAnchor.MiddleCenter;
            hlgBtn.childControlWidth = false;
            hlgBtn.childControlHeight = false;
            btnContainer = btnContainerGo.transform;
        }

        WireSceneButton(ref restartButton, btnContainer, "RestartButton", OnRestartClicked);
        WireSceneButton(ref homeButton, btnContainer, "HomeButton", OnHomeClicked);
    }

    void WireSceneButton(ref Button btn, Transform container, string childName, UnityEngine.Events.UnityAction action)
    {
        if (btn == null)
        {
            Transform existing = container.Find(childName);
            if (existing != null)
                btn = existing.GetComponent<Button>();
        }

        if (btn == null)
        {
            Color fallbackColor = childName == "RestartButton"
                ? new Color(0.12f, 0.65f, 0.28f)
                : new Color(0.85f, 0.35f, 0.15f);
            string label = childName == "RestartButton" ? "PLAY AGAIN" : "HOME";
            EnsureFallbackButton(ref btn, container, childName, label, fallbackColor, action);
            return;
        }

        if (btn.transform.parent != container)
            btn.transform.SetParent(container, false);

        EnableButtonVisuals(btn);
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(action);
    }

    static void EnableButtonVisuals(Button btn)
    {
        if (btn == null) return;
        btn.enabled = true;
        btn.interactable = true;
        btn.gameObject.SetActive(true);

        var img = btn.GetComponent<Image>();
        if (img != null)
            img.enabled = true;
    }

    void EnsureFallbackButton(ref Button btn, Transform parent, string goName, string label, Color bgColor, UnityEngine.Events.UnityAction action)
    {
        var go = CreateRect(goName, parent, Vector2.zero, new Vector2(280, 90));
        AddImage(go, bgColor);
        btn = go.AddComponent<Button>();
        btn.targetGraphic = go.GetComponent<Image>();
        var newTmp = AddTmp(go.transform, label, Color.white, 30, TextAlignmentOptions.Center, FontStyles.Bold);
        newTmp.rectTransform.sizeDelta = new Vector2(280, 90);

        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(action);

        var colors = btn.colors;
        colors.highlightedColor = bgColor * 1.2f;
        colors.pressedColor = bgColor * 0.8f;
        btn.colors = colors;
    }

    GameObject CreateColumn(string name, Transform parent, float width, float contentHeight, float spacing)
    {
        var col = CreateRect(name, parent, Vector2.zero, new Vector2(width, contentHeight));
        col.AddComponent<CanvasGroup>(); // For fading/animation
        var vlg = col.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.spacing = spacing;
        vlg.childControlWidth = false;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = false;
        vlg.childForceExpandHeight = false;
        return col;
    }

    void CreateLabelCell(string text, Transform parent, float width, float height, bool isBold)
    {
        var cell = CreateRect("LabelCell", parent, Vector2.zero, new Vector2(width, height));
        // Push label towards the bottom so it aligns with name boxes
        var txt = AddTmp(cell.transform, text, Color.white, 28, TextAlignmentOptions.Bottom, isBold ? FontStyles.Bold : FontStyles.Normal);
        txt.rectTransform.anchoredPosition = new Vector2(0, -25f);
        txt.rectTransform.sizeDelta = new Vector2(width, height);
    }

    void CreatePlayerHeaderCell(int seatIndex, Transform parent, float width, float height)
    {
        var cell = CreateRect("PlayerHeaderCell", parent, Vector2.zero, new Vector2(width, height));

        // 1. Avatar Border/Frame (Circle)
        var avatarFrameGo = CreateRect("AvatarFrame", cell.transform, new Vector2(0, 35f), new Vector2(75, 75));
        var borderImg = AddImage(avatarFrameGo, new Color(0.35f, 0.22f, 0.12f, 0.85f));
        Sprite circleSprite = null;
#if UNITY_EDITOR
        circleSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/2D Cards Game Art Pack/Sprites/Characters/frame_circle.png");
#endif
        if (circleSprite != null) borderImg.sprite = circleSprite;

        // 2. Avatar Inside Image
        var avatarImgGo = CreateRect("AvatarImage", avatarFrameGo.transform, Vector2.zero, new Vector2(68, 68));
        var avatarImg = AddImage(avatarImgGo, Color.white);
        avatarImg.sprite = GetAvatarSprite(GetActorNumberBySeat(seatIndex));
        avatarImg.preserveAspect = true;

        // 3. Name BG Box (Brown Rounded)
        var nameBoxGo = CreateRect("NameBox", cell.transform, new Vector2(0, -25f), new Vector2(140, 32));
        var nameBoxImg = AddImage(nameBoxGo, new Color(0.35f, 0.22f, 0.12f, 1f));
        if (woodBoardSprite != null)
        {
            nameBoxImg.sprite = woodBoardSprite;
            nameBoxImg.type = Image.Type.Simple;
        }

        // 4. Name Text
        string name = GetSeatDisplayName(seatIndex);
        var nameTxt = AddTmp(nameBoxGo.transform, name, Color.white, 16, TextAlignmentOptions.Center, FontStyles.Bold);
        nameTxt.rectTransform.anchoredPosition = Vector2.zero;
        nameTxt.rectTransform.sizeDelta = new Vector2(130, 26);
        nameTxt.overflowMode = TextOverflowModes.Ellipsis;
    }

    void CreateValueCell(string text, Transform parent, float width, float height, bool isBold, bool isWinner = false, bool isCurrentRound = false)
    {
        var cell = CreateRect("ValueCell", parent, Vector2.zero, new Vector2(width, height));
        
        // Define colors
        Color textColor = Color.white; // Default readable white on wood
        int fontSize = 28;
        FontStyles style = isBold ? FontStyles.Bold : FontStyles.Normal;

        if (isWinner)
        {
            textColor = new Color(1f, 0.82f, 0.2f, 1f); // Golden Highlight for Winner
            style = FontStyles.Bold;
            fontSize = 30;
        }
        else if (isCurrentRound)
        {
            textColor = new Color(0.45f, 1f, 0.5f, 1f); // Bright green for current active round
            style = FontStyles.Bold;
        }

        var txt = AddTmp(cell.transform, text, textColor, fontSize, TextAlignmentOptions.Center, style);
        txt.rectTransform.anchoredPosition = Vector2.zero;
        txt.rectTransform.sizeDelta = new Vector2(width, height);
    }

    void CreateCloseButton(Transform parent)
    {
        var btnGo = CreateRect("CloseButton", parent, new Vector2(540, 330), new Vector2(64, 64));
        
        var bgImg = AddImage(btnGo, new Color(0.35f, 0.22f, 0.12f, 1f));
        Sprite circleSprite = null;
#if UNITY_EDITOR
        circleSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/2D Cards Game Art Pack/Sprites/Characters/frame_circle.png");
#endif
        if (circleSprite != null) bgImg.sprite = circleSprite;
        
        var btn = btnGo.AddComponent<Button>();
        btn.targetGraphic = bgImg;
        btn.onClick.AddListener(CloseResult);

        var txt = AddTmp(btnGo.transform, "X", Color.white, 26, TextAlignmentOptions.Center, FontStyles.Bold);
        txt.rectTransform.anchoredPosition = Vector2.zero;
        txt.rectTransform.sizeDelta = new Vector2(50, 50);
        
        var colors = btn.colors;
        colors.normalColor = new Color(0.35f, 0.22f, 0.12f, 1f);
        colors.highlightedColor = new Color(0.5f, 0.3f, 0.15f, 1f);
        colors.pressedColor = new Color(0.2f, 0.1f, 0.05f, 1f);
        btn.colors = colors;
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

    void ClearDynamicUI()
    {
        foreach (var go in _dynamicRows)
            if (go != null) DestroyObjectSafe(go);
        _dynamicRows.Clear();
    }

    void DestroyObjectSafe(GameObject go)
    {
        if (go == null) return;
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            DestroyImmediate(go);
            return;
        }
#endif
        Destroy(go);
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
        _currentRoundIndex = 0;
        for (int r = 0; r < RoundsPerMatch; r++)
        {
            for (int i = 0; i < 4; i++)
                _roundDehlas[r][i] = 0;
        }

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
