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

    public bool IsSearching => isSearching;

    /// <summary>Clears user-cancel flag so a fresh Play Online search is not blocked.</summary>
    public void PrepareForNewOnlineSearch()
    {
        WasCancelledByUser = false;
        isMatchFoundRoutineRunning = false;
    }

    /// <summary>Clears online matchmaking timers/flags so PlayFriends is not blocked by stale online state.</summary>
    public void ResetMatchmakingState(bool cancelledByUser)
    {
        Debug.Log($"[Matchmaking] ResetMatchmakingState cancelled={cancelledByUser}");
        WasCancelledByUser = cancelledByUser;
        isSearching = false;
        isMatchFoundRoutineRunning = false;
        StopAllCoroutines();

        if (DeckManager.Instance != null)
            DeckManager.Instance.StopOnlineMatchmaking();

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.ForceClearBlackOverlay();

        // Only clear online UI flags when switching modes — not during user cancel (ReturnToHome handles that).
        if (!cancelledByUser && PlayWithFriendsManager.Instance != null)
            PlayWithFriendsManager.Instance.ClearOnlineModeOnly();
    }

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
        HideMatchmakingPanel();
    }

    /// <summary>Makes the online matchmaking seat panel fully visible above menu layers.</summary>
    public void ShowMatchmakingPanel()
    {
        Debug.Log("[UI] ShowMatchmakingPanel called");

        if (ModeManager.Instance != null)
        {
            ModeManager.Instance.HideJoinTablePanel();
            if (ModeManager.Instance.panelModes != null)
                ModeManager.SetPanelVisiblePublic(ModeManager.Instance.panelModes, false);
            if (ModeManager.Instance.panelHomeScreen != null)
                ModeManager.SetPanelVisiblePublic(ModeManager.Instance.panelHomeScreen, false);
        }

        PlayWithFriendsManager pwf = EnsureSeatPanel();
        if (pwf == null)
        {
            Debug.LogWarning("[Matchmaking] Seat panel (PlayWithFriendsManager) not found.");
            return;
        }

        pwf.ShowOnlineMatchmakingLobby();

        GameObject panel = pwf.gameObject;
        CanvasGroup cg = panel.GetComponent<CanvasGroup>();
        Debug.Log($"[UI] Matchmaking panel activeSelf={panel.activeSelf} activeInHierarchy={panel.activeInHierarchy}"
            + $" | parent activeInHierarchy={(panel.transform.parent != null && panel.transform.parent.gameObject.activeInHierarchy)}"
            + $" | CanvasGroup alpha={(cg != null ? cg.alpha.ToString("F2") : "n/a")}"
            + $" | siblingIndex={panel.transform.GetSiblingIndex()}");
    }

    /// <summary>Hides the online matchmaking seat panel without stopping Photon matchmaking.</summary>
    public void HideMatchmakingPanel()
    {
        if (PlayWithFriendsManager.Instance != null)
            PlayWithFriendsManager.Instance.HideLobby();
    }

    public void StartSearching()
    {
        if (isSearching) return;
        isSearching = true;
        PrepareForNewOnlineSearch();

        Debug.Log("🔍 Matchmaking started (seat lobby).");

        ShowMatchmakingPanel();
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
                NetworkManager.Instance.ShowGameScene(showLoadingOverlay: false);
            if (PhotonNetwork.IsMasterClient && DeckManager.Instance != null
                && DeckManager.Instance.IsMatchContextReadyForDealingPublic())
                DeckManager.Instance.StartFullDealingSequence();
            else if (!PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode)
                NetworkManager.Instance?.ReturnToHomeScreen();
            isMatchFoundRoutineRunning = false;
            yield break;
        }

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.ShowGameScene(showLoadingOverlay: false);

        yield return new WaitForSeconds(0.2f);

        if (DeckManager.Instance != null
            && DeckManager.Instance.IsMatchContextReadyForDealingPublic()
            && PhotonNetwork.IsMasterClient)
            DeckManager.Instance.StartFullDealingSequence();
        else if (!PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode)
            NetworkManager.Instance?.ReturnToHomeScreen();

        isMatchFoundRoutineRunning = false;
    }

    public void OnClick_Cancel() => OnCancelClicked();

    public void OnCancelClicked()
    {
        Debug.Log("[Cancel] Matchmaking cancel clicked -> Home Screen");
        WasCancelledByUser = true;
        isSearching = false;
        isMatchFoundRoutineRunning = false;
        StopAllCoroutines();

        if (DeckManager.Instance != null)
            DeckManager.Instance.StopOnlineMatchmaking();

        if (ModeManager.Instance != null)
            ModeManager.Instance.CancelPendingMatchmaking();

        ReturnToHome();
    }

    void ReturnToHome()
    {
        HideSeatLobby();

        if (ModeManager.Instance != null)
            ModeManager.Instance.CancelPendingMatchmaking();

        GameFlowState.SetPhase(GameFlowPhase.Home, forceRecovery: true);

        if (ModeManager.Instance != null)
            ModeManager.Instance.ReturnToHomeClean();

        if (PhotonNetwork.InRoom)
        {
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.LeaveRoomAndCleanup();
            else
                PhotonNetwork.LeaveRoom();
            return;
        }

        if (PhotonNetwork.InLobby)
            PhotonNetwork.LeaveLobby();

        if (PhotonNetwork.OfflineMode)
            PhotonNetwork.OfflineMode = false;

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.RefreshPlayOnlineButtonState();
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
        ShowMatchmakingPanel();
    }
}
