using System.Collections;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Central round-flow coordinator. Leaderboard shows while next-round dealing runs in the background.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("Global Card Assets")]
    public Sprite cardBackSprite;

    [Header("Round-End Sequence")]
    [Tooltip("Root of the inter-round leaderboard panel (e.g. Panel_Winning).")]
    [SerializeField] private GameObject leaderboardPanel;

    [SerializeField] private float leaderboardDisplaySeconds = 5f;

    Coroutine _roundEndSequence;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) Destroy(gameObject);
    }

    /// <summary>
    /// Starts inter-round flow: dealing begins immediately while leaderboard stays visible.
    /// Only the authoritative (master / offline) client triggers the networked deal RPC.
    /// </summary>
    public void BeginRoundEndSequence(bool authoritative)
    {
        if (_roundEndSequence != null)
            StopCoroutine(_roundEndSequence);

        _roundEndSequence = StartCoroutine(HandleRoundEndSequence(authoritative));
    }

    /// <summary>
    /// Deals next-round cards immediately behind the leaderboard, then auto-hides after display window.
    /// Player can also close the leaderboard anytime via Close button.
    /// </summary>
    public IEnumerator HandleRoundEndSequence(bool authoritative)
    {
        ResolveLeaderboardPanel();

        // Cards peeche turant distribute — leaderboard wait ki zaroorat nahi.
        if (authoritative)
            DealCardsHorizontally();

        float displaySeconds = leaderboardDisplaySeconds > 0f ? leaderboardDisplaySeconds : 5f;
        yield return new WaitForSecondsRealtime(displaySeconds);

        // Agar player ne pehle hi X se band kar diya to panel already inactive hoga.
        if (leaderboardPanel != null && leaderboardPanel.activeSelf)
            leaderboardPanel.SetActive(false);

        if (ResultManager.Instance != null)
            ResultManager.Instance.NotifyRoundEndSequenceComplete();

        _roundEndSequence = null;
    }

    /// <summary>
    /// Starts the horizontal card-deal animation for the new round (master / offline only).
    /// </summary>
    public void DealCardsHorizontally()
    {
        if (!PhotonNetwork.IsMasterClient && !PhotonNetwork.OfflineMode)
            return;

        int nextRound = ResultManager.Instance != null
            ? ResultManager.Instance.currentRound + 1
            : 1;

        if (DeckManager.Instance == null || DeckManager.Instance.photonView == null)
        {
            Debug.LogError("[GameManager] Next round NOT started — DeckManager/photonView missing.");
            return;
        }

        if (PhotonNetwork.IsMasterClient || PhotonNetwork.OfflineMode)
            DeckManager.Instance.ResetRoundStateForNextRound();

        Debug.Log($"[GameManager] Triggering next round deal (round {nextRound}) via RPC_BeginNextRound.");
        DeckManager.Instance.photonView.RPC(
            nameof(DeckManager.RPC_BeginNextRound),
            RpcTarget.AllBuffered,
            nextRound);
    }

    void ResolveLeaderboardPanel()
    {
        if (leaderboardPanel != null) return;

        if (ResultManager.Instance != null && ResultManager.Instance.resultPanel != null)
            leaderboardPanel = ResultManager.Instance.resultPanel.gameObject;
    }
}
