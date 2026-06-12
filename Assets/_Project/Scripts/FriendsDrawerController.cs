using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public class FriendsDrawerController : MonoBehaviour
{
    public static FriendsDrawerController Instance;

    [Header("UI References")]
    public RectTransform friendListPanel;
    public Button inviteFriendsButton;
    public RectTransform arrowIcon;

    [Header("Animation Settings")]
    [SerializeField] private float openX = 0f;
    [SerializeField] private float closedX = 700f;
    [SerializeField] private float duration = 0.4f;

    private bool isOpen = false;

    void Awake()
    {
        if (Instance == null) Instance = this;
    }

    void Start()
    {
        if (friendListPanel != null)
            friendListPanel.anchoredPosition = new Vector2(closedX, friendListPanel.anchoredPosition.y);

        isOpen = false;

        if (inviteFriendsButton != null)
        {
            inviteFriendsButton.onClick.RemoveAllListeners();
            inviteFriendsButton.onClick.AddListener(ToggleDrawer);
        }
    }

    public void ToggleDrawer()
    {
        if (friendListPanel == null) return;

        if (DOTween.IsTweening(friendListPanel)) return;

        friendListPanel.DOKill();
        if (arrowIcon != null) arrowIcon.DOKill();

        if (isOpen)
        {
            friendListPanel.DOAnchorPosX(closedX, duration).SetEase(Ease.InQuad);

            if (arrowIcon != null) arrowIcon.DOLocalRotate(new Vector3(0, 0, 0), duration);

            isOpen = false;
            Debug.Log("[Drawer] Friendlist closed.");
        }
        else
        {
            friendListPanel.DOAnchorPosX(openX, duration).SetEase(Ease.OutBack);

            if (arrowIcon != null) arrowIcon.DOLocalRotate(new Vector3(0, 0, 180), duration);

            isOpen = true;
            Debug.Log("[Drawer] Friendlist opened. Refreshing status...");

            if (FriendsPanelUIController.Instance != null)
                FriendsPanelUIController.Instance.RefreshAll();
            else if (PlayWithFriendsManager.Instance != null)
            {
                PlayWithFriendsManager.Instance.RefreshFriendsListUI();
                PlayWithFriendsManager.Instance.CheckFriendsOnlineStatus();
            }
        }
    }

    public void OpenDrawer()
    {
        if (isOpen) return;
        ToggleDrawer();
    }
}
