using UnityEngine;



/// <summary>Central menu UI state — all main panel transitions should go through here.</summary>

public enum UIState

{

    Login,

    Home,

    Modes,

    JoinTable,

    CreatingRoom,

    RoomLobby,

    OnlineMatchmaking,

    LoadingGame,

    Game,

    SettingsPopup,

    ExitConfirmPopup

}



public enum UiFlowKind

{

    None,

    OnlineMatchmaking,

    PlayFriendsCreate,

    PlayFriendsJoin,

    PlayFriendsLobby,

    InGame,

    ReturningHome

}



public static class UiFlowManager

{

    public static UIState Current { get; private set; } = UIState.Home;

    public static UiFlowKind Flow { get; private set; } = UiFlowKind.None;



    static int _navigationToken;

    static int _joinAttemptToken;

    static bool _returningHome;



    public static int NavigationToken => _navigationToken;

    public static int JoinAttemptToken => _joinAttemptToken;

    public static bool IsReturningHome => _returningHome;



    public static void SetUIState(UIState state)

    {

        if (Current != state)

            Debug.Log($"[UI] SetUIState: {Current} → {state}");

        Current = state;

    }



    public static void BumpNavigation()

    {

        _navigationToken++;

        _joinAttemptToken++;

        if (!_returningHome)

            Flow = UiFlowKind.None;

        Debug.Log($"[UI] Navigation bump → token={_navigationToken}");

    }



    public static void MarkReturningHome()

    {

        _returningHome = true;

        Flow = UiFlowKind.ReturningHome;

        Debug.Log("[UI] MarkReturningHome — late Photon callbacks ignored until Home is ready.");

    }



    public static void CompleteReturnToHome()

    {

        _returningHome = false;

        Flow = UiFlowKind.None;

        SetUIState(UIState.Home);

    }



    public static int BeginPinJoinAttempt()

    {

        _returningHome = false;

        _joinAttemptToken++;

        Flow = UiFlowKind.PlayFriendsJoin;

        SetUIState(UIState.JoinTable);

        Debug.Log($"[UI] Current flow set to PlayFriendsJoin | join token={_joinAttemptToken}");

        return _joinAttemptToken;

    }



    public static void BeginOnlineMatchmaking()

    {

        _returningHome = false;

        Flow = UiFlowKind.OnlineMatchmaking;

        SetUIState(UIState.OnlineMatchmaking);

        Debug.Log("[UI] Current flow set to OnlineMatchmaking");

    }



    public static void MarkPlayFriendsCreate()

    {

        _returningHome = false;

        Flow = UiFlowKind.PlayFriendsCreate;

        Debug.Log("[UI] Current flow set to PlayFriendsCreate");

    }



    public static void MarkInGame()

    {

        _returningHome = false;

        Flow = UiFlowKind.InGame;

        SetUIState(UIState.Game);

        Debug.Log("[UI] Current flow set to InGame");

    }



    public static bool IsOnlineMatchmakingFlow() => Flow == UiFlowKind.OnlineMatchmaking;



    public static bool IsPlayFriendsJoinFlow() => Flow == UiFlowKind.PlayFriendsJoin;



    public static bool IsPlayFriendsLobbyFlow() =>

        Flow == UiFlowKind.PlayFriendsLobby || Flow == UiFlowKind.PlayFriendsCreate;



    public static void MarkPlayFriendsLobby()

    {

        _returningHome = false;

        Flow = UiFlowKind.PlayFriendsLobby;

        SetUIState(UIState.RoomLobby);

        Debug.Log("[UI] Current flow set to PlayFriendsLobby");

    }



    public static bool IsJoinAttemptCurrent(int token) => token == _joinAttemptToken;



    public static bool ShouldAcceptPhotonUiCallback()

    {

        if (_returningHome || Flow == UiFlowKind.ReturningHome)

            return false;

        if (Flow == UiFlowKind.OnlineMatchmaking

            || Flow == UiFlowKind.PlayFriendsJoin

            || Flow == UiFlowKind.PlayFriendsLobby

            || Flow == UiFlowKind.PlayFriendsCreate)

            return true;

        if (Current == UIState.OnlineMatchmaking

            || Current == UIState.JoinTable

            || Current == UIState.RoomLobby

            || Current == UIState.Modes)

            return true;

        if (GameFlowState.Current == GameFlowPhase.Matchmaking

            || GameFlowState.Current == GameFlowPhase.ModeSelection

            || GameFlowState.Current == GameFlowPhase.InRoom)

            return true;

        if (Current == UIState.Home)

            return false;

        if (GameFlowState.Current == GameFlowPhase.Home)

            return false;

        return true;

    }



    public static void ShowHomeOnly() => ReturnToHomeClean();



    public static void ShowModesOnly()

    {

        _returningHome = false;

        SetUIState(UIState.Modes);

        if (ModeManager.Instance != null)

            ModeManager.Instance.ShowModesScreenOnly();

        ValidatePanelState();

    }



    public static void ShowJoinTableOverModes()

    {

        _returningHome = false;

        Flow = UiFlowKind.PlayFriendsJoin;

        SetUIState(UIState.JoinTable);

        if (ModeManager.Instance != null)

        {

            ModeManager.Instance.MarkFriendsPinJoinFlow();

            ModeManager.Instance.ShowModesScreenOnly();

            ModeManager.Instance.ShowJoinTablePanel();

        }

        ValidatePanelState();

    }



    public static void ShowRoomLobbyOnly()

    {

        MarkPlayFriendsLobby();

        if (ModeManager.Instance != null)

        {

            ModeManager.Instance.HideJoinTablePanel();

            ModeManager.Instance.ShowPlayWithFriendsPanel();

        }

        HideAllOverlays();

        ValidatePanelState();

    }



    public static void ShowMatchmakingOnly()

    {

        BeginOnlineMatchmaking();

        if (MatchmakingManager.Instance != null)

            MatchmakingManager.Instance.ShowMatchmakingPanel();

        ValidatePanelState();

    }



    public static void ShowLoadingOnly(string message)

    {

        SetUIState(UIState.LoadingGame);

        HideAllOverlays();

        if (NetworkManager.Instance != null)

            NetworkManager.Instance.ShowLoading(message ?? "Loading...");

    }



    public static void ShowGameOnly()

    {

        MarkInGame();

        HideAllOverlays();

        if (NetworkManager.Instance != null)

            NetworkManager.Instance.ShowGameScene(showLoadingOverlay: false);

        ValidatePanelState();

    }



    public static void ReturnToHomeClean()

    {

        MarkReturningHome();

        BumpNavigation();

        Flow = UiFlowKind.ReturningHome;

        SetUIState(UIState.Home);

        if (ModeManager.Instance != null)

            ModeManager.Instance.ReturnToHomeCleanInternal();

        else

            CompleteReturnToHome();

        ValidatePanelState();

    }



    public static void ShowModesForPlayFriends()

    {

        BumpNavigation();

        Flow = UiFlowKind.PlayFriendsCreate;

        SetUIState(UIState.Modes);

        if (ModeManager.Instance != null)

            ModeManager.Instance.ShowModesForPlayFriendsInternal();

        ValidatePanelState();

    }



    public static void ShowJoinTable()

    {

        SetUIState(UIState.JoinTable);

        if (ModeManager.Instance != null)

            ModeManager.Instance.ShowJoinTablePanel();

        ValidatePanelState();

    }



    public static void HandlePinJoinFailed(short returnCode, string message)

    {

        if (!ShouldAcceptPhotonUiCallback())

        {

            Debug.LogWarning($"[UI] Ignoring stale OnJoinRoomFailed ({returnCode}) — user already left join flow.");

            return;

        }



        Debug.LogWarning($"[UI] HandlePinJoinFailed | code={returnCode} | {message}");

        Flow = UiFlowKind.PlayFriendsJoin;

        SetUIState(UIState.JoinTable);



        if (PlayWithFriendsManager.Instance != null)

            PlayWithFriendsManager.Instance.ApplyPinJoinFailureUi(returnCode, message);



        ValidatePanelState();

    }



    public static void HideAllOverlays()

    {

        if (NetworkManager.Instance == null) return;

        NetworkManager.Instance.ForceClearBlackOverlay();

        NetworkManager.Instance.HideLoadingInstant();

        NetworkManager.Instance.ClearUiInputBlockers();

    }



    public static void HideAllPopupsAndOverlays() => HideAllOverlays();



    public static void ValidatePanelState()

    {

        if (ModeManager.Instance != null)

            ModeManager.Instance.ValidateMenuPanels();

    }

}


