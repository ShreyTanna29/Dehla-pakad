using System.Collections;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Central round-flow coordinator. Ensures the leaderboard fully finishes before the next deal begins.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

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
    /// Starts the strict leaderboard-then-deal sequence for the next round.
    /// Only the authoritative (master / offline) client triggers the networked deal RPC.
    /// </summary>
    public void BeginRoundEndSequence(bool authoritative)
    {
        if (_roundEndSequence != null)
            StopCoroutine(_roundEndSequence);

        _roundEndSequence = StartCoroutine(HandleRoundEndSequence(authoritative));
    }

    /// <summary>
    /// Blocks dealing until the leaderboard has been visible for the full display window.
    /// </summary>
    public IEnumerator HandleRoundEndSequence(bool authoritative)
    {
        ResolveLeaderboardPanel();

        // Leaderboard is already shown by ResultManager.ShowRoundLeaderboard — do not re-open here.

        // Task 13: the inter-round leaderboard must stay visible for a full 5 seconds. Use a hard
        // fallback if the serialized field was accidentally left at 0/negative, and use REALTIME so
        // the window is unaffected by any Time.timeScale changes (pauses) elsewhere.
        float displaySeconds = leaderboardDisplaySeconds > 0f ? leaderboardDisplaySeconds : 5f;
        yield return new WaitForSecondsRealtime(displaySeconds);

        if (leaderboardPanel != null)
            leaderboardPanel.SetActive(false);

        if (authoritative)
            DealCardsHorizontally();

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
