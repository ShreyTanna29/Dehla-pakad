using UnityEngine;
using TMPro;
using UnityEngine.UI;
using Firebase.Database;
using Firebase.Extensions;
using Photon.Pun;

public class FirebaseSearchManager : MonoBehaviour
{
    private const string FirebaseDatabaseUrl = "https://dehla-pakad-a7859-default-rtdb.firebaseio.com/";

    [Header("Search UI References")]
    public TMP_InputField searchInputField;
    public Transform searchResultsContainer;
    public GameObject searchResultRowPrefab;

    DatabaseReference dbReference;

    void Start()
    {
        dbReference = FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference;
    }

    public void UI_SearchPlayersByName()
    {
        if (FriendsPanelUIController.Instance != null)
        {
            FriendsPanelUIController.Instance.SearchPlayersByName();
            return;
        }

        if (searchInputField == null || searchResultsContainer == null) return;

        string searchText = searchInputField.text.Trim();
        if (string.IsNullOrEmpty(searchText)) return;

        foreach (Transform child in searchResultsContainer)
            Destroy(child.gameObject);

        Debug.Log($"[Firebase Search] Looking for users starting with: {searchText}");

        dbReference.Child("users").OrderByChild("username")
            .StartAt(searchText)
            .EndAt(searchText + "\uf8ff")
            .GetValueAsync().ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted || task.IsCanceled)
                {
                    Debug.LogError("Firebase Search Failed.");
                    return;
                }

                DataSnapshot snapshot = task.Result;
                if (!snapshot.Exists) return;

                string localUserId = PhotonNetwork.LocalPlayer?.UserId
                    ?? PhotonNetwork.AuthValues?.UserId;

                foreach (DataSnapshot userSnapshot in snapshot.Children)
                {
                    string foundUserId = userSnapshot.Key;

                    if (!userSnapshot.Child("username").Exists) continue;

                    string foundUsername = userSnapshot.Child("username").Value?.ToString();
                    if (string.IsNullOrEmpty(foundUsername)) continue;

                    if (!string.IsNullOrEmpty(localUserId) && foundUserId == localUserId) continue;

                    SpawnSearchResultRow(foundUserId, foundUsername);
                }
            });
    }

    void SpawnSearchResultRow(string id, string name)
    {
        if (searchResultRowPrefab == null || searchResultsContainer == null) return;

        GameObject row = Instantiate(searchResultRowPrefab, searchResultsContainer);

        TMP_Text nameText = row.GetComponentInChildren<TMP_Text>();
        if (nameText != null) nameText.text = name;

        Button addBtn = row.GetComponentInChildren<Button>();
        if (addBtn != null)
        {
            addBtn.onClick.RemoveAllListeners();
            addBtn.onClick.AddListener(() =>
            {
                if (PlayWithFriendsManager.Instance != null)
                {
                    PlayWithFriendsManager.Instance.SendFriendRequest(id, name);
                    Debug.Log($"[Friend System] Friend request sent to {name}!");

                    // Give immediate feedback on the row instead of removing it.
                    TMP_Text label = addBtn.GetComponentInChildren<TMP_Text>();
                    if (label != null) label.text = "Sent";
                    addBtn.interactable = false;
                }
            });
        }
    }
}
