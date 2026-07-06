using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Controls the "JOIN TABLE" panel embedded in the Modes screen.
/// Reuses the existing Play-with-Friends join logic: takes the PIN typed here
/// and forwards it to PlayWithFriendsManager.JoinRoomWithPINText.
/// </summary>
public class JoinTablePanelController : MonoBehaviour
{
    [Header("References")]
    public TMP_InputField pinInput;
    public Button joinButton;

    void Start()
    {
        if (joinButton != null)
        {
            joinButton.onClick.RemoveListener(OnJoinClicked);
            joinButton.onClick.AddListener(OnJoinClicked);
        }
    }

    public void OnJoinClicked()
    {
        if (PlayWithFriendsManager.Instance == null)
        {
            Debug.LogWarning("[JoinTable] PlayWithFriendsManager.Instance missing — cannot join.");
            return;
        }

        if (PlayWithFriendsManager.Instance.IsJoinInProgress)
        {
            Debug.Log("[JoinTable] Join ignored — already joining.");
            return;
        }

        string pin = pinInput != null ? pinInput.text : null;
        PlayWithFriendsManager.Instance.JoinRoomWithPINText(pin);
    }

    public void SetJoinInteractable(bool interactable)
    {
        if (joinButton != null)
            joinButton.interactable = interactable;
    }
}
