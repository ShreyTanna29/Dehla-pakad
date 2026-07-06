using UnityEngine;
using UnityEngine.SceneManagement;
using Photon.Pun;

/// <summary>
/// TASK 1 (Robust Logout) + TASK 2 (Delete Account).
///
/// Centralized, robust logout / delete-account flow for Dehla Pakad.
/// This script does NOT re-implement Firebase/Google sign-out — that logic already lives,
/// tested, in GoogleLogin.SignOut() and PlayerProfileManager.DeleteAccount(). Instead this
/// manager fills the real robustness gaps:
///   * selectively clears ONLY account-specific PlayerPrefs (keeps device settings such as
///     mute / speed / game-modes),
///   * resets the in-memory static session state so a fresh login starts clean,
///   * routes the user back to the login screen (panel-based, with an optional scene-load path).
/// </summary>
[DefaultExecutionOrder(-140)]
public class LogoutManager : MonoBehaviour
{
    public static LogoutManager Instance { get; private set; }

    [Header("Delete Account Confirmation UI")]
    [Tooltip("Confirmation popup GameObject shown before deleting the account. " +
             "Wire its 'Yes/Confirm' button to ConfirmDeleteAccount() and its 'No/Cancel' button to CancelDeleteAccount().")]
    [SerializeField] private GameObject deleteConfirmPanel;

    [Header("Logout Destination")]
    [Tooltip("This project is single-scene (login is a panel, not a scene). Leave OFF to return to the " +
             "in-scene login panel via GoogleLogin.SignOut(). Only turn ON if you later add a dedicated " +
             "Login scene to Build Settings.")]
    [SerializeField] private bool loadLoginScene = false;
    [SerializeField] private string loginSceneName = "Login";

    // Account-specific keys cleared on logout. Device/gameplay preferences
    // (MutePref, speed, trick/trump/sar/logic modes) are intentionally PRESERVED.
    static readonly string[] AccountPrefKeys =
    {
        "PlayerEmail",          // GoogleLogin
        "PlayerUsername",       // PlayerProfileManager (PREFS_USERNAME)
        "PlayerAvatarIndex",    // PlayerProfileManager (PREFS_AVATAR_INDEX)
        "PlayerGameUid",        // GameUidService (PrefsGameUid)
        "PhotonUserId",         // NetworkManager / PlayWithFriendsManager
        "ActiveMatchRoomName",  // NetworkManager (PrefsActiveRoomName)
    };

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ============================================================
    // TASK 1 — ROBUST LOGOUT
    // ============================================================
    /// <summary>Hook this to your "Logout" button OnClick.</summary>
    public void Logout()
    {
        Debug.Log("[LogoutManager] Logout requested.");

        // 1) Reset in-memory session statics so a fresh login starts from a clean slate.
        ResetSessionState();

        // 2) Clear account-specific PlayerPrefs (device settings are preserved).
        ClearAccountPrefs();

        // 3) Sign out of Google + Firebase via the existing, battle-tested flow.
        if (GoogleLogin.Instance != null)
        {
            // GoogleLogin.SignOut() already performs:
            //   auth.SignOut(); GoogleSignIn.DefaultInstance.SignOut();
            //   PlayerProfileManager.HideUntilLoginComplete(); ShowLoginPanel(); ResetLoginFlow();
            GoogleLogin.Instance.SignOut();
        }
        else
        {
            // ---- Placeholder fallback if the GoogleLogin singleton is not present ----
            // Firebase.Auth.FirebaseAuth.DefaultInstance.SignOut();
            // Google.GoogleSignIn.DefaultInstance.SignOut();
            Debug.LogWarning("[LogoutManager] GoogleLogin.Instance missing — sign-out skipped (placeholder).");
        }

        // 4) Return to the login screen.
        if (loadLoginScene)
            SceneManager.LoadScene(loginSceneName);
        // else: GoogleLogin.SignOut() already re-showed the in-scene login panel.
    }

    void ResetSessionState()
    {
        // Phase machine back to Home (force, because logout can happen mid-game).
        GameFlowState.SetPhase(GameFlowPhase.Home, forceRecovery: true);

        // Clear stale match / matchmaking state.
        if (DeckManager.botActorNumbers != null) DeckManager.botActorNumbers.Clear();
        PlayWithFriendsManager.PendingJoinPin = null;

        // TASK 1 — disconnect Photon so a logged-out user is never left in a room/match.
        // PhotonNetwork.Disconnect() raises OnDisconnected with DisconnectByClientLogic, which
        // NetworkManager.OnDisconnected explicitly ignores (no auto-reconnect, no connection-lost
        // panel). We set the phase back to Home above first so any callbacks treat this as a clean
        // exit. This also covers the case where the previous account stayed connected to Photon.
        if (PhotonNetwork.IsConnected)
        {
            Debug.Log("[LogoutManager] Disconnecting Photon for logout.");
            PhotonNetwork.Disconnect();
        }

        // Reset gameplay input locks.
        CardInteract.canPlayCards = false;
        CardInteract.isPlayingCard = false;
    }

    void ClearAccountPrefs()
    {
        foreach (string key in AccountPrefKeys)
            PlayerPrefs.DeleteKey(key);
        PlayerPrefs.Save();
    }

    // ============================================================
    // TASK 2 — DELETE ACCOUNT
    // ============================================================
    /// <summary>
    /// Hook this to your "Delete Account" button. Shows the confirmation popup first.
    /// The actual deletion happens in ConfirmDeleteAccount() (wired to the popup's Yes button).
    /// </summary>
    public void DeleteAccount()
    {
        if (deleteConfirmPanel != null)
        {
            deleteConfirmPanel.SetActive(true);
            return;
        }

        Debug.LogWarning("[LogoutManager] No deleteConfirmPanel assigned — deleting without confirmation UI.");
        ConfirmDeleteAccount();
    }

    /// <summary>Hook to the confirmation popup's "No / Cancel" button.</summary>
    public void CancelDeleteAccount()
    {
        if (deleteConfirmPanel != null) deleteConfirmPanel.SetActive(false);
    }

    /// <summary>Hook to the confirmation popup's "Yes / Delete" button.</summary>
    public void ConfirmDeleteAccount()
    {
        if (deleteConfirmPanel != null) deleteConfirmPanel.SetActive(false);

        if (PlayerProfileManager.Instance == null)
        {
            Debug.LogError("[LogoutManager] PlayerProfileManager.Instance missing — cannot delete account.");
            return;
        }

        // The existing flow deletes the user's DB node + the Firebase Auth user
        // (re-authenticating via Google automatically if Firebase requires a recent login),
        // and then signs out. We reset local session state afterwards for safety.
        PlayerProfileManager.Instance.DeleteAccount((success, message) =>
        {
            Debug.Log($"[LogoutManager] DeleteAccount result: success={success}, message={message}");

            ResetSessionState();

            if (success && loadLoginScene)
                SceneManager.LoadScene(loginSceneName);
            // On success the existing flow already cleared prefs + signed out + showed the login panel.
        });
    }
}
