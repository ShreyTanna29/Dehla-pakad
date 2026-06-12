using System.Collections.Generic;
using System.Linq;
using Firebase.Database;
using Firebase.Extensions;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Mockup-style Friends panel: My Friends tab (list + game INVITE) and Invite tab (search/add + requests).
/// Wire tab buttons, containers, and row prefabs in Inspector after Unity AI builds the UI.
/// </summary>
public class FriendsPanelUIController : MonoBehaviour
{
    public static FriendsPanelUIController Instance;

    const string FirebaseDatabaseUrl = "https://dehla-pakad-a7859-default-rtdb.firebaseio.com/";

    public enum PanelTab
    {
        MyFriends,
        Invite
    }

    [Header("Tabs")]
    public Button myFriendsTabButton;
    public Button inviteTabButton;
    public GameObject myFriendsTabContent;
    public GameObject inviteTabContent;
    public Image myFriendsTabHighlight;
    public Image inviteTabHighlight;
    public Color activeTabColor = Color.white;
    public Color inactiveTabColor = new Color(0.55f, 0.55f, 0.55f, 1f);

    [Header("My Friends Tab")]
    public Transform myFriendsListContainer;
    public GameObject friendListRowPrefab;

    [Header("Invite Tab — Search & Add")]
    public TMP_InputField searchInputField;
    public TMP_InputField addByIdInputField;
    public Button searchButton;
    public Button addByIdButton;
    public Transform searchResultsContainer;
    public GameObject searchResultRowPrefab;

    [Header("Invite Tab — Friend Requests")]
    public GameObject requestsSection;
    public Transform requestsListContainer;
    public GameObject requestRowPrefab;
    public TMP_Text requestsCountLabel;

    [Header("Invite Button Colors")]
    public Color inviteAvailableColor = new Color(0.2f, 0.75f, 0.25f, 1f);
    public Color inviteSentColor = new Color(0.45f, 0.45f, 0.45f, 1f);

    PanelTab _activeTab = PanelTab.MyFriends;
    DatabaseReference _usersDb;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this)
        {
            Destroy(this);
            return;
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        _usersDb = FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference;
        WireButtons();
        ShowTab(PanelTab.MyFriends);
    }

    void WireButtons()
    {
        if (myFriendsTabButton != null)
        {
            myFriendsTabButton.onClick.RemoveAllListeners();
            myFriendsTabButton.onClick.AddListener(() => ShowTab(PanelTab.MyFriends));
        }

        if (inviteTabButton != null)
        {
            inviteTabButton.onClick.RemoveAllListeners();
            inviteTabButton.onClick.AddListener(() => ShowTab(PanelTab.Invite));
        }

        if (searchButton != null)
        {
            searchButton.onClick.RemoveAllListeners();
            searchButton.onClick.AddListener(SearchPlayersByName);
        }

        if (addByIdButton != null)
        {
            addByIdButton.onClick.RemoveAllListeners();
            addByIdButton.onClick.AddListener(AddFriendById);
        }
    }

    public void ShowTab(PanelTab tab)
    {
        _activeTab = tab;

        if (myFriendsTabContent != null)
            myFriendsTabContent.SetActive(tab == PanelTab.MyFriends);
        if (inviteTabContent != null)
            inviteTabContent.SetActive(tab == PanelTab.Invite);

        if (myFriendsTabHighlight != null)
            myFriendsTabHighlight.color = tab == PanelTab.MyFriends ? activeTabColor : inactiveTabColor;
        if (inviteTabHighlight != null)
            inviteTabHighlight.color = tab == PanelTab.Invite ? activeTabColor : inactiveTabColor;

        RefreshAll();
    }

    /// <summary>Rebuild both tab lists from PlayWithFriendsManager data.</summary>
    public void RefreshAll()
    {
        RefreshMyFriendsList();
        RefreshRequestsList();
        UpdateRequestsBadge();
    }

    void RefreshMyFriendsList()
    {
        if (myFriendsListContainer == null || friendListRowPrefab == null) return;
        if (PlayWithFriendsManager.Instance == null) return;

        ClearContainer(myFriendsListContainer);

        foreach (string friendId in PlayWithFriendsManager.Instance.MyFriends)
        {
            if (string.IsNullOrEmpty(friendId)) continue;

            string displayName = PlayWithFriendsManager.Instance.GetFriendDisplayName(friendId);
            FriendInfo photonInfo = PlayWithFriendsManager.Instance.GetFriendPhotonInfo(friendId);
            bool inviteSent = PlayWithFriendsManager.Instance.IsGameInviteSent(friendId);

            SpawnMyFriendRow(friendId, displayName, photonInfo, inviteSent);
        }
    }

    void SpawnMyFriendRow(string friendId, string displayName, FriendInfo photonInfo, bool inviteSent)
    {
        GameObject row = Instantiate(friendListRowPrefab, myFriendsListContainer);

        TMP_Text label = FindPrimaryLabel(row.transform);
        string status = GetOnlineStatusText(photonInfo);
        if (label != null)
        {
            label.text = $"{displayName}\n<size=16>{status}</size>";
            label.color = photonInfo != null && photonInfo.IsOnline ? Color.white : new Color(0.75f, 0.75f, 0.75f);
        }

        Button inviteBtn = FindNamedButton(row.transform, "InviteButton");
        if (inviteBtn == null)
        {
            Button[] buttons = row.GetComponentsInChildren<Button>(true);
            inviteBtn = buttons.Length > 0 ? buttons[buttons.Length - 1] : null;
        }

        if (inviteBtn == null) return;

        TMP_Text inviteLabel = inviteBtn.GetComponentInChildren<TMP_Text>();
        Image inviteImg = inviteBtn.GetComponent<Image>();

        inviteBtn.onClick.RemoveAllListeners();

        if (inviteSent)
        {
            if (inviteLabel != null) inviteLabel.text = "INVITED";
            if (inviteImg != null) inviteImg.color = inviteSentColor;
            inviteBtn.interactable = false;
            return;
        }

        if (inviteLabel != null) inviteLabel.text = "INVITE";
        if (inviteImg != null) inviteImg.color = inviteAvailableColor;
        inviteBtn.interactable = true;
        inviteBtn.onClick.AddListener(() =>
        {
            PlayWithFriendsManager.Instance.InviteFriendToGame(friendId, displayName);
            PlayWithFriendsManager.Instance.MarkGameInviteSent(friendId);
            RefreshMyFriendsList();
        });
    }

    void RefreshRequestsList()
    {
        if (requestsListContainer == null) return;
        if (PlayWithFriendsManager.Instance == null) return;

        ClearContainer(requestsListContainer);

        IReadOnlyDictionary<string, string> requests = PlayWithFriendsManager.Instance.IncomingRequests;
        bool hasRequests = requests != null && requests.Count > 0;

        if (requestsSection != null)
            requestsSection.SetActive(hasRequests);

        if (!hasRequests) return;

        GameObject prefab = requestRowPrefab != null ? requestRowPrefab : friendListRowPrefab;
        if (prefab == null) return;

        foreach (KeyValuePair<string, string> kvp in requests)
        {
            if (string.IsNullOrEmpty(kvp.Key)) continue;
            SpawnRequestRow(prefab, kvp.Key, kvp.Value);
        }
    }

    void SpawnRequestRow(GameObject prefab, string fromId, string fromName)
    {
        GameObject row = Instantiate(prefab, requestsListContainer);

        TMP_Text label = FindPrimaryLabel(row.transform);
        if (label != null)
            label.text = $"{fromName}\n<size=16><color=#FFD479>Friend request</color></size>";

        Button acceptBtn = FindNamedButton(row.transform, "AcceptButton");
        Button declineBtn = FindNamedButton(row.transform, "DeclineButton");

        if (acceptBtn == null || declineBtn == null)
        {
            Button[] buttons = row.GetComponentsInChildren<Button>(true);
            if (buttons.Length >= 2)
            {
                acceptBtn = acceptBtn ?? buttons[0];
                declineBtn = declineBtn ?? buttons[1];
            }
        }

        if (acceptBtn != null)
        {
            acceptBtn.onClick.RemoveAllListeners();
            acceptBtn.onClick.AddListener(() =>
            {
                PlayWithFriendsManager.Instance.AcceptFriendRequest(fromId, fromName);
                RefreshAll();
            });
        }

        if (declineBtn != null)
        {
            declineBtn.onClick.RemoveAllListeners();
            declineBtn.onClick.AddListener(() =>
            {
                PlayWithFriendsManager.Instance.DeclineFriendRequest(fromId);
                RefreshAll();
            });
        }
    }

    void UpdateRequestsBadge()
    {
        if (requestsCountLabel == null || PlayWithFriendsManager.Instance == null) return;

        int count = PlayWithFriendsManager.Instance.IncomingRequests.Count;
        requestsCountLabel.gameObject.SetActive(count > 0);
        requestsCountLabel.text = count > 0 ? count.ToString() : "";
    }

    public void SearchPlayersByName()
    {
        if (searchInputField == null || searchResultsContainer == null || _usersDb == null) return;

        string searchText = searchInputField.text.Trim();
        if (string.IsNullOrEmpty(searchText)) return;

        ClearContainer(searchResultsContainer);
        ShowTab(PanelTab.Invite);

        _usersDb.Child("users").OrderByChild("username")
            .StartAt(searchText)
            .EndAt(searchText + "\uf8ff")
            .GetValueAsync().ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted || task.IsCanceled)
                {
                    Debug.LogError("[FriendsPanel] Search failed.");
                    return;
                }

                DataSnapshot snapshot = task.Result;
                if (!snapshot.Exists) return;

                string localUserId = PhotonNetwork.AuthValues?.UserId ?? PhotonNetwork.LocalPlayer?.UserId;

                foreach (DataSnapshot userSnapshot in snapshot.Children)
                {
                    string foundUserId = userSnapshot.Key;
                    if (!userSnapshot.Child("username").Exists) continue;

                    string foundUsername = userSnapshot.Child("username").Value?.ToString();
                    if (string.IsNullOrEmpty(foundUsername)) continue;
                    if (!string.IsNullOrEmpty(localUserId) && foundUserId == localUserId) continue;

                    SpawnSearchRow(foundUserId, foundUsername);
                }
            });
    }

    void SpawnSearchRow(string userId, string displayName)
    {
        if (searchResultRowPrefab == null || searchResultsContainer == null) return;

        GameObject row = Instantiate(searchResultRowPrefab, searchResultsContainer);

        TMP_Text nameText = FindPrimaryLabel(row.transform);
        if (nameText != null) nameText.text = displayName;

        Button addBtn = FindNamedButton(row.transform, "AddButton");
        if (addBtn == null)
            addBtn = row.GetComponentInChildren<Button>();

        if (addBtn == null) return;

        bool alreadyFriend = PlayWithFriendsManager.Instance != null
            && PlayWithFriendsManager.Instance.MyFriends.Contains(userId);

        TMP_Text btnLabel = addBtn.GetComponentInChildren<TMP_Text>();
        if (alreadyFriend)
        {
            if (btnLabel != null) btnLabel.text = "Added";
            addBtn.interactable = false;
            return;
        }

        addBtn.onClick.RemoveAllListeners();
        addBtn.onClick.AddListener(() =>
        {
            if (PlayWithFriendsManager.Instance == null) return;
            PlayWithFriendsManager.Instance.SendFriendRequest(userId, displayName);
            if (btnLabel != null) btnLabel.text = "Sent";
            addBtn.interactable = false;
        });
    }

    public void AddFriendById()
    {
        if (addByIdInputField == null || PlayWithFriendsManager.Instance == null) return;

        string id = addByIdInputField.text.Trim();
        if (string.IsNullOrEmpty(id)) return;

        PlayWithFriendsManager.Instance.SendFriendRequest(id, null);
        addByIdInputField.text = "";
        ShowTab(PanelTab.Invite);
    }

    static string GetOnlineStatusText(FriendInfo info)
    {
        if (info == null) return "Offline";
        if (info.IsInRoom) return "In Game";
        if (info.IsOnline) return "Online";
        return "Offline";
    }

    static void ClearContainer(Transform container)
    {
        if (container == null) return;
        for (int i = container.childCount - 1; i >= 0; i--)
            Destroy(container.GetChild(i).gameObject);
    }

    static TMP_Text FindPrimaryLabel(Transform root)
    {
        TMP_Text[] labels = root.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < labels.Length; i++)
        {
            if (labels[i].GetComponentInParent<Button>() == null)
                return labels[i];
        }
        return labels.Length > 0 ? labels[0] : null;
    }

    static Button FindNamedButton(Transform root, string childName)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == childName)
                return t.GetComponent<Button>();
        }
        return null;
    }
}
