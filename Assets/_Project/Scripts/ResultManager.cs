using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using DG.Tweening;
using Photon.Pun;
using System.Collections.Generic;

public class ResultManager : MonoBehaviourPunCallbacks
{
    public static ResultManager Instance;

    [Header("UI Panels")]
    public CanvasGroup resultPanel;
    public TMP_FontAsset customFont;

    [Header("Team UI Containers")]
    public Transform teamYouContainer;
    public Transform teamOpponentsContainer;
    public TMP_Text titleText;
    public TMP_Text descriptionText;

    [Header("Buttons")]
    public Button homeButton;
    public Button restartButton;

    [System.Serializable]
    public class PlayerResult
    {
        public string name;
        public int bid;
        public int tricksWon;
        public bool isCompleted;
        public float score;
    }

    private PlayerResult[] playerResults = new PlayerResult[4];

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        for (int i = 0; i < 4; i++)
        {
            playerResults[i] = new PlayerResult { name = GetInitialPlayerName(i) };
        }

        if (resultPanel != null)
        {
            resultPanel.alpha = 0;
            resultPanel.interactable = false;
            resultPanel.blocksRaycasts = false;
            resultPanel.gameObject.SetActive(false);
        }

        if (homeButton != null) homeButton.onClick.AddListener(OnHomeClicked);
        if (restartButton != null) restartButton.onClick.AddListener(OnRestartClicked);
    }

    string GetInitialPlayerName(int i)
    {
        if (i == 0) return "You";
        return "Dehla_AI_" + i;
    }

    public void SetBid(int seatIndex, int bidValue)
    {
        if (seatIndex >= 0 && seatIndex < 4)
        {
            playerResults[seatIndex].bid = bidValue;
        }
    }

    public void OnTrickWon(int winnerSeatIndex, int dehlaCount)
    {
        if (winnerSeatIndex >= 0 && winnerSeatIndex < 4)
        {
            playerResults[winnerSeatIndex].tricksWon++;
            
            // 🚀 SYNC: Master client ensures everyone has the same score data
            if (PhotonNetwork.IsMasterClient)
            {
                SyncAllScores();
            }
        }
    }

    void SyncAllScores()
    {
        int[] tricks = new int[4];
        for (int i = 0; i < 4; i++) tricks[i] = playerResults[i].tricksWon;
        photonView.RPC("RPC_SyncScores", RpcTarget.Others, (object)tricks);
    }

    [PunRPC]
    void RPC_SyncScores(int[] tricks)
    {
        for (int i = 0; i < 4 && i < tricks.Length; i++)
        {
            playerResults[i].tricksWon = tricks[i];
        }
    }

    public void ShowResult()
    {
        CalculateScores();
        GenerateTeamUI();

        float teamYouScore = playerResults[0].score + playerResults[2].score;
        float teamOpponentScore = playerResults[1].score + playerResults[3].score;
        bool isVictory = teamYouScore >= teamOpponentScore;
        Debug.Log($"[ResultManager] Match Finished. Winner: {(isVictory ? "Team You" : "Team Opponents")}");

        if (resultPanel != null)
        {
            resultPanel.gameObject.SetActive(true);
            resultPanel.DOFade(1, 0.6f).SetUpdate(true);
            resultPanel.interactable = true;
            resultPanel.blocksRaycasts = true;
            resultPanel.transform.localScale = Vector3.one * 0.7f;
            resultPanel.transform.DOScale(1, 0.6f).SetEase(Ease.OutBack).SetUpdate(true);
        }
    }

    void CalculateScores()
    {
        foreach (var p in playerResults)
        {
            if (p.tricksWon >= p.bid)
            {
                p.isCompleted = true;
                p.score = p.bid + (p.tricksWon - p.bid) * 0.1f;
            }
            else
            {
                p.isCompleted = false;
                p.score = -p.bid;
            }
        }
    }

    void GenerateTeamUI()
    {
        // Clear containers
        ClearContainer(teamYouContainer);
        ClearContainer(teamOpponentsContainer);

        float teamYouScore = playerResults[0].score + playerResults[2].score;
        float teamOpponentScore = playerResults[1].score + playerResults[3].score;

        bool isVictory = teamYouScore >= teamOpponentScore;

        if (titleText != null)
        {
            titleText.text = isVictory ? "VICTORY!" : "DEFEAT";
            titleText.color = isVictory ? Color.yellow : Color.red;
        }

        if (descriptionText != null)
        {
            descriptionText.text = isVictory ? 
                $"Well played! Your team scored {teamYouScore:F1} points." : 
                $"Better luck next time! Opponents scored {teamOpponentScore:F1} points.";
        }

        // Add Players to Team You
        AddPlayerResultToUI(playerResults[0], teamYouContainer);
        AddPlayerResultToUI(playerResults[2], teamYouContainer);

        // Add Players to Team Opponents
        AddPlayerResultToUI(playerResults[1], teamOpponentsContainer);
        AddPlayerResultToUI(playerResults[3], teamOpponentsContainer);
    }

    void ClearContainer(Transform container)
    {
        if (container == null) return;
        foreach (Transform child in container)
        {
            if (child.name != "Header") Destroy(child.gameObject);
        }
    }

    void AddPlayerResultToUI(PlayerResult p, Transform container)
    {
        if (container == null) return;

        GameObject row = new GameObject("PlayerResult", typeof(RectTransform), typeof(VerticalLayoutGroup));
        row.transform.SetParent(container, false);
        var vlg = row.GetComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childControlHeight = true;
        vlg.childForceExpandHeight = false;

        Color nameColor = p.name == "You" ? Color.green : Color.white;
        AddText(row.transform, p.name.ToUpper(), nameColor, 32, TextAlignmentOptions.Center);
        
        string status = p.isCompleted ? "✔ COMPLETED" : "✘ FAILED";
        Color statusColor = p.isCompleted ? Color.green : Color.red;
        AddText(row.transform, status, statusColor, 24, TextAlignmentOptions.Center);

        AddText(row.transform, $"SCORE: {p.score:F1}", Color.white, 28, TextAlignmentOptions.Center);
        
        // Add spacing
        GameObject spacer = new GameObject("Spacer", typeof(RectTransform));
        spacer.transform.SetParent(row.transform, false);
        spacer.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 10);
    }

    void AddText(Transform parent, string content, Color color, int size, TextAlignmentOptions align)
    {
        GameObject textObj = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObj.transform.SetParent(parent, false);
        var tmp = textObj.GetComponent<TextMeshProUGUI>();
        tmp.text = content;
        tmp.color = color;
        tmp.fontSize = size;
        tmp.alignment = align;
        if (customFont != null) tmp.font = customFont;
    }

    void OnHomeClicked()
    {
        if (PhotonNetwork.IsConnected) PhotonNetwork.LeaveRoom();
        SceneManager.LoadScene(0); 
    }

    void OnRestartClicked()
    {
        if (PhotonNetwork.IsConnected) PhotonNetwork.LeaveRoom();
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
