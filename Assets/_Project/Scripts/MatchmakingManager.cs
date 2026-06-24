using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Photon.Pun;

// NOTE: The standalone matchmaking panel (fake scrolling profiles + spinner + status
// messages) has been removed. Online matchmaking now reuses the shared seat panel
// (PlayWithFriendsManager) which shows the live countdown timer and seats real players
// as they join. This manager is now a thin coordinator that drives that seat panel and
// preserves the public API used by ModeManager / DeckManager / NetworkManager.
public class MatchmakingManager : MonoBehaviourPunCallbacks
{
    public static MatchmakingManager Instance;

    // Global user-cancel flag to block pending Photon callbacks from restarting matchmaking.
    public bool WasCancelledByUser { get; private set; }

    // Kept for backward compatibility: NetworkManager null-checks this reference.
    // The old standalone matchmaking panel is removed, so this stays null at runtime.
    [HideInInspector] public CanvasGroup matchmakingPanel;

    [Header("Profile Avatars (fallback pool for other systems)")]
    [Tooltip("Fallback avatar sprite pool. Primary pool is PlayerProfileManager.profileSprites.")]
    public List<Sprite> profileSprites = new List<Sprite>();

    private bool isSearching = false;
    private bool isMatchFoundRoutineRunning = false;

    // Static fallback pool consumed by ResultManager / PlayerProfileSync / etc.
    public static List<Sprite> GlobalProfileSprites = new List<Sprite>();

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            GlobalProfileSprites = profileSprites;
        }
        else Destroy(gameObject);
    }

    // Resolves the shared seat panel (PlayWithFriendsManager), activating it if needed
    // so its Instance is available even though it starts inactive in the scene.
    PlayWithFriendsManager EnsureSeatPanel()
    {
        GameObject panel = null;
        if (ModeManager.Instance != null) panel = ModeManager.Instance.panelPlayWithFriends;
        if (panel == null && PlayWithFriendsManager.Instance != null)
            panel = PlayWithFriendsManager.Instance.gameObject;
        if (panel != null && !panel.activeSelf) panel.SetActive(true);
        return PlayWithFriendsManager.Instance;
    }

    void HideSeatLobby()
    {
        if (PlayWithFriendsManager.Instance != null)
            PlayWithFriendsManager.Instance.HideLobby();
    }

    public void StartSearching()
    {
        if (isSearching) return;
        isSearching = true;
        WasCancelledByUser = false;

        Debug.Log("🔍 Matchmaking started (seat lobby).");

        PlayWithFriendsManager pwf = EnsureSeatPanel();
        if (pwf != null) pwf.ShowOnlineMatchmakingLobby();
        else Debug.LogWarning("[Matchmaking] Seat panel (PlayWithFriendsManager) not found.");
    }

    // Driven by DeckManager's countdown RPC: live player count + seconds remaining.
    public void UpdateMatchmakingStatus(int playersFound, int countdown)
    {
        if (!isSearching) return;
        if (PlayWithFriendsManager.Instance != null)
            PlayWithFriendsManager.Instance.UpdateOnlineTimer(playersFound, countdown);
    }

    public void StopSearching(bool isMatchFound)
    {
        // Private friends rooms manage their own flow.
        if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null
            && !PhotonNetwork.CurrentRoom.IsVisible && !PhotonNetwork.OfflineMode && !isMatchFound)
        {
            Debug.Log("[Matchmaking] Private Room detected, bypassing exit logic.");
            return;
        }

        if (!isSearching && !isMatchFound && !WasCancelledByUser) return;
        if (isMatchFound && isMatchFoundRoutineRunning) return;

        isSearching = false;

        if (isMatchFound)
        {
            isMatchFoundRoutineRunning = true;
            StartCoroutine(MatchFoundRoutine());
        }
        else
        {
            Debug.Log("[Matchmaking] Stopped/Cancelled -> Home Screen");
            ReturnToHome();
        }
    }

    IEnumerator MatchFoundRoutine()
    {
        bool isOffline = PhotonNetwork.OfflineMode;

        HideSeatLobby();

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.ShowLoading("Loading game...");

        yield return new WaitForSeconds(NetworkManager.GameStartLoadingDelaySeconds);

        if (isOffline)
        {
            Debug.Log("🤖 Instant Bot Match.");
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.ShowGameScene();
            if (PhotonNetwork.IsMasterClient && DeckManager.Instance != null)
                DeckManager.Instance.StartFullDealingSequence();
            isMatchFoundRoutineRunning = false;
            yield break;
        }

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.ShowGameScene();

        yield return new WaitForSeconds(0.2f);

        if (PhotonNetwork.IsMasterClient && DeckManager.Instance != null)
            DeckManager.Instance.StartFullDealingSequence();

        isMatchFoundRoutineRunning = false;
    }

    public void OnClick_Cancel() => OnCancelClicked();

    public void OnCancelClicked()
    {
        Debug.Log("[Cancel] Matchmaking cancel clicked -> Home Screen");
        WasCancelledByUser = true;
        isSearching = false;
        isMatchFoundRoutineRunning = false;

        if (ModeManager.Instance != null)
            ModeManager.Instance.CancelPendingMatchmaking();

        ReturnToHome();
    }

    void ReturnToHome()
    {
        HideSeatLobby();

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
        if (!isSearching) return;
        PlayWithFriendsManager pwf = EnsureSeatPanel();
        if (pwf != null) pwf.ShowOnlineMatchmakingLobby();
    }
}
