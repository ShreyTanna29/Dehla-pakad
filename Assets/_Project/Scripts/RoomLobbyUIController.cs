using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;

/// <summary>
/// Self-contained controller for three lobby concerns:
///   STEP 1 - Friend List collapse toggle
///   STEP 2 - Instant friend invites + host game start
///   STEP 3 - Host vs. Client room-lobby UI visibility
///
/// Wire the [SerializeField] panels in the Inspector and hook the public methods to your
/// Buttons' OnClick events. This delegates the networking work to the project's existing
/// systems (PlayWithFriendsManager / NetworkManager) so it stays consistent with the rest of
/// the game and does not duplicate match logic.
/// </summary>
public class RoomLobbyUIController : MonoBehaviourPunCallbacks
{
    // ---------------------------------------------------------------------
    // STEP 1 — Collapsible Friend List
    // ---------------------------------------------------------------------
    [Header("STEP 1 - Friend List")]
    [SerializeField] GameObject friendListPanel;

    /// <summary>Toggles the friend list panel on/off. Attach to the toggle button's OnClick.</summary>
    public void ToggleFriendList()
    {
        if (friendListPanel == null) return;
        // Active -> hide; inactive -> show. Single, branch-free toggle.
        friendListPanel.SetActive(!friendListPanel.activeSelf);
    }

    // ---------------------------------------------------------------------
    // STEP 2 — Instant Invites & Game Start
    // ---------------------------------------------------------------------

    /// <summary>
    /// Fires the friend invite IMMEDIATELY (no mode-selection gating). Delegates to the project's
    /// existing instant-invite flow, which writes the invite to Firebase the moment it's called.
    /// </summary>
    public void SendGameInvite(string friendId)
    {
        if (string.IsNullOrEmpty(friendId)) return;

        if (PlayWithFriendsManager.Instance != null)
            PlayWithFriendsManager.Instance.SendGameInvite(friendId); // instant, no waiting
        else
            Debug.LogWarning("[RoomLobby] SendGameInvite — PlayWithFriendsManager.Instance missing.");
    }

    /// <summary>
    /// Host-only game start. NOTE: this project is SINGLE-SCENE — there is no "GameScene" asset and
    /// PhotonNetwork.LoadLevel("GameScene") would load an empty default scene (the blue screen).
    /// AutomaticallySyncScene is therefore kept OFF, and we route through the proven start pipeline
    /// (NetworkManager.HostStartMatch -> ModeManager.StartGameFromModePanel) which RPCs every client
    /// into the gameplay UI together. This IS the "all players start together" equivalent here.
    /// </summary>
    public void HostStartGame()
    {
        if (!PhotonNetwork.IsMasterClient)
        {
            Debug.LogWarning("[RoomLobby] HostStartGame ignored — only the Master Client can start.");
            return;
        }

        // Defensive: ensure no stray scene auto-sync is active for this single-scene project.
        PhotonNetwork.AutomaticallySyncScene = false;

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.HostStartMatch();
        else
            Debug.LogError("[RoomLobby] HostStartGame — NetworkManager.Instance missing.");
    }

    // ---------------------------------------------------------------------
    // STEP 3 — Room Lobby UI (Host vs. Client visibility)
    // ---------------------------------------------------------------------
    [Header("STEP 3 - Room Lobby Panels")]
    [SerializeField] GameObject roomLobbyPanel;
    [SerializeField] GameObject backButton;
    [SerializeField] GameObject startButton;
    [SerializeField] GameObject modeSelectionPanel;

    /// <summary>Photon callback — refresh lobby visibility the moment we enter the room.</summary>
    public override void OnJoinedRoom()
    {
        UpdateRoomLobbyUI();
    }

    /// <summary>
    /// Applies role-based lobby visibility:
    ///   - Everyone: roomLobbyPanel + backButton ON.
    ///   - Host:     startButton + modeSelectionPanel ON.
    ///   - Client:   startButton + modeSelectionPanel OFF.
    /// </summary>
    public void UpdateRoomLobbyUI()
    {
        // 1) Shared UI — always visible to host AND client.
        if (roomLobbyPanel != null) roomLobbyPanel.SetActive(true);
        if (backButton != null) backButton.SetActive(true);

        // 2/3) Host-only controls.
        bool isHost = PhotonNetwork.IsMasterClient;
        if (startButton != null)
        {
            startButton.SetActive(isHost);
            if (isHost)
            {
                Button btn = startButton.GetComponent<Button>();
                if (btn != null) btn.interactable = true;
            }
        }
        if (modeSelectionPanel != null) modeSelectionPanel.SetActive(isHost);
    }

    /// <summary>Wire the seat-panel Back button to this.</summary>
    public void OnBackFromLobby()
    {
        if (NetworkManager.Instance != null)
            NetworkManager.Instance.LeaveRoomAndCleanup();
        else if (PlayWithFriendsManager.Instance != null)
            PlayWithFriendsManager.Instance.LeaveCurrentRoom();
    }
}
