using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using TMPro; 
using UnityEngine.UI;
using System.Collections.Generic;

public class PlayerProfileSync : MonoBehaviourPunCallbacks
{
    public static PlayerProfileSync Instance;

    [Header("Player Name Texts")]
    public TMP_Text txtMyName;      
    public TMP_Text txtLeftName;    
    public TMP_Text txtTopName;     
    public TMP_Text txtRightName;   

    [Header("Avatar Settings")]
    public Sprite maskSprite;

    [Header("Avatar Components (optional — assign in Inspector)")]
    [SerializeField] UnityEngine.UI.Image imgMyAvatar;
    [SerializeField] UnityEngine.UI.Image imgLeftAvatar;
    [SerializeField] UnityEngine.UI.Image imgTopAvatar;
    [SerializeField] UnityEngine.UI.Image imgRightAvatar;
    [Tooltip("Root for in-game seat avatars (e.g. game Canvas). Avoids GameObject.Find.")]
    [SerializeField] Transform gameUiSearchRoot;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        SetupAvatars();
    }

    public void InitializeGameScene()
    {
        if (NetworkManager.Instance != null && NetworkManager.Instance.gameCanvasGroup != null)
            gameUiSearchRoot = NetworkManager.Instance.gameCanvasGroup.transform;

        SetupAvatars();
        PublishLocalAvatar();
        UpdateAllNames();
        Debug.Log("[GameInit] Player profiles initialized");
    }

    void SetupAvatars()
    {
        if (gameUiSearchRoot == null && NetworkManager.Instance != null && NetworkManager.Instance.gameCanvasGroup != null)
            gameUiSearchRoot = NetworkManager.Instance.gameCanvasGroup.transform;
        if (gameUiSearchRoot == null)
            gameUiSearchRoot = transform.root;
            
        UiSafeLookup.SetSearchRoot(gameUiSearchRoot);

        // More robust finding - try path then by name if path fails
        if (imgMyAvatar == null)
        {
            if (UiSafeLookup.TryGetPath("You/You_Avatar", out GameObject myGo))
                imgMyAvatar = myGo.GetComponent<UnityEngine.UI.Image>();
            else if (UiSafeLookup.TryGet("You_Avatar", out GameObject myGo2))
                imgMyAvatar = myGo2.GetComponent<UnityEngine.UI.Image>();
        }

        if (imgLeftAvatar == null)
        {
            if (UiSafeLookup.TryGetPath("Opponent_Left/Playe2_Avatar", out GameObject leftGo))
                imgLeftAvatar = leftGo.GetComponent<UnityEngine.UI.Image>();
            else if (UiSafeLookup.TryGet("Playe2_Avatar", out GameObject leftGo2))
                imgLeftAvatar = leftGo2.GetComponent<UnityEngine.UI.Image>();
        }

        if (imgTopAvatar == null)
        {
            if (UiSafeLookup.TryGetPath("Opponent_Top/Player3_Avatar", out GameObject topGo))
                imgTopAvatar = topGo.GetComponent<UnityEngine.UI.Image>();
            else if (UiSafeLookup.TryGet("Player3_Avatar", out GameObject topGo2))
                imgTopAvatar = topGo2.GetComponent<UnityEngine.UI.Image>();
            
            if (imgTopAvatar == null)
                Debug.LogWarning("[ProfileSync] Failed to find imgTopAvatar (Opponent_Top/Player3_Avatar)");
        }

        if (imgRightAvatar == null)
        {
            if (UiSafeLookup.TryGetPath("Opponent_Right/Playe4_Avatar", out GameObject rightGo))
                imgRightAvatar = rightGo.GetComponent<UnityEngine.UI.Image>();
            else if (UiSafeLookup.TryGet("Playe4_Avatar", out GameObject rightGo2))
                imgRightAvatar = rightGo2.GetComponent<UnityEngine.UI.Image>();
        }

        if (txtMyName == null && UiSafeLookup.TryGetPath("You/You_Name", out GameObject myNameGo))
            txtMyName = myNameGo.GetComponent<TMP_Text>();
        if (txtLeftName == null && UiSafeLookup.TryGet("Opponent_Left", out GameObject leftRoot))
            txtLeftName = leftRoot.GetComponentInChildren<TMP_Text>(true);
        if (txtTopName == null && UiSafeLookup.TryGet("Opponent_Top", out GameObject topRoot))
            txtTopName = topRoot.GetComponentInChildren<TMP_Text>(true);
        if (txtRightName == null && UiSafeLookup.TryGet("Opponent_Right", out GameObject rightRoot))
            txtRightName = rightRoot.GetComponentInChildren<TMP_Text>(true);

        ApplyMask(imgMyAvatar);
        ApplyMask(imgLeftAvatar);
        ApplyMask(imgTopAvatar);
        ApplyMask(imgRightAvatar);
    }

    void ApplyMask(UnityEngine.UI.Image img)
    {
        if (img == null) return;
        if (maskSprite != null) img.sprite = maskSprite;
        if (img.gameObject.GetComponent<UnityEngine.UI.Mask>() == null)
        {
            img.gameObject.AddComponent<UnityEngine.UI.Mask>();
        }
    }

    void Start() 
    { 
        if (string.IsNullOrEmpty(PhotonNetwork.NickName))
        {
            PhotonNetwork.NickName = "Player"; 
        }
        UpdateAllNames(); 
    }
    
    public override void OnJoinedRoom()
    {
        PublishLocalAvatar();
        UpdateAllNames();
    }

    public override void OnPlayerEnteredRoom(Player newPlayer) { UpdateAllNames(); }
    public override void OnPlayerLeftRoom(Player otherPlayer) { UpdateAllNames(); }

    public override void OnPlayerPropertiesUpdate(Player targetPlayer, ExitGames.Client.Photon.Hashtable changedProps)
    {
        if (changedProps != null && changedProps.ContainsKey(PlayerProfileManager.PROP_AVATAR))
            UpdateAllNames();
    }

    private float lastUpdateStatsTime = 0f;

    void Update()
    {
        if (Time.time - lastUpdateStatsTime > 1.0f)
        {
            lastUpdateStatsTime = Time.time;
            UpdateAllNames();
        }
    }

    public void UpdateAllNames()
    {
        if (txtLeftName == null || txtTopName == null || txtRightName == null)
        {
            SetupAvatars();
            if (txtLeftName == null || txtTopName == null || txtRightName == null)
                return;
        }

        if (txtMyName && PhotonNetwork.LocalPlayer != null)
        {
            string myName = GetLocalProfileDisplayName();
            txtMyName.text = myName;
            AssignAvatarSprite(imgMyAvatar, PhotonNetwork.LocalPlayer.ActorNumber);
        }

        foreach (Player p in PhotonNetwork.PlayerList)
        {
            if (p.IsLocal) continue;
            int seatIndex = GetSeatIndex(p.ActorNumber);
            
            string displayName = p.NickName;
            if (p.IsInactive)
            {
                if (DeckManager.Instance != null && DeckManager.Instance.IsActorBotControlled(p.ActorNumber))
                    continue;
                displayName += "\n(Disconnected)";
            }
            
            SetSeatText(seatIndex, displayName);
            AssignAvatarBySeat(seatIndex, p.ActorNumber);
        }

        if (DeckManager.Instance != null && DeckManager.botActorNumbers != null)
        {
            for (int i = 0; i < DeckManager.botActorNumbers.Count; i++)
            {
                int botActor = DeckManager.botActorNumbers[i];
                int seatIndex = GetSeatIndex(botActor);
                SetSeatText(seatIndex, "Dehla_AI_" + (i + 1));
                AssignAvatarBySeat(seatIndex, botActor);
            }
        }
    }

    void AssignAvatarBySeat(int seatIndex, int actorNumber)
    {
        if (seatIndex == 1) AssignAvatarSprite(imgLeftAvatar, actorNumber);
        else if (seatIndex == 2) AssignAvatarSprite(imgTopAvatar, actorNumber);
        else if (seatIndex == 3) AssignAvatarSprite(imgRightAvatar, actorNumber);
    }

    void AssignAvatarSprite(UnityEngine.UI.Image img, int actorNumber)
    {
        if (img == null)
        {
             Debug.LogWarning($"[ProfileSync] Cannot assign avatar for actor {actorNumber} - Image component is null");
             return;
        }

        Sprite[] pool = GetCanonicalPool();
        if (pool == null || pool.Length == 0) return;

        int spriteIndex = ResolveAvatarIndex(actorNumber);
        if (spriteIndex < 0 || spriteIndex >= pool.Length)
            spriteIndex = Mathf.Abs(actorNumber) % pool.Length; // fallback for bots / missing data

        img.sprite = pool[spriteIndex];
        img.preserveAspect = true;
        img.enabled = true; // Ensure it's enabled
    }

    // Returns the avatar index the player actually selected during profile setup.
    // The local player uses its saved PlayerPrefs choice directly; remote players use the
    // synced Photon custom property. Returns -1 when unknown (caller falls back).
    int ResolveAvatarIndex(int actorNumber)
    {
        if (PhotonNetwork.LocalPlayer != null && actorNumber == PhotonNetwork.LocalPlayer.ActorNumber)
        {
            int local = PlayerProfileManager.GetSavedAvatarIndex();
            if (local >= 0) return local;
        }

        Player p = GetPlayerByActor(actorNumber);
        if (p != null && p.CustomProperties != null &&
            p.CustomProperties.TryGetValue(PlayerProfileManager.PROP_AVATAR, out object val) && val != null)
        {
            if (val is int vi) return vi;
            if (int.TryParse(val.ToString(), out int parsed)) return parsed;
        }
        return -1;
    }

    Player GetPlayerByActor(int actorNumber)
    {
        if (!PhotonNetwork.IsConnectedAndReady && !PhotonNetwork.OfflineMode) return null;
        if (PhotonNetwork.CurrentRoom != null)
            return PhotonNetwork.CurrentRoom.GetPlayer(actorNumber);
        return null;
    }

    Sprite[] _cachedPool;

    // Use the exact sprite list the avatar index was chosen from (profile setup) so a
    // synced index maps to the same picture on every client. Falls back to the global pool.
    Sprite[] GetCanonicalPool()
    {
        if (PlayerProfileManager.Instance != null &&
            PlayerProfileManager.Instance.profileSprites != null &&
            PlayerProfileManager.Instance.profileSprites.Length > 0)
        {
            _cachedPool = PlayerProfileManager.Instance.profileSprites;
            return _cachedPool;
        }

        if (_cachedPool != null && _cachedPool.Length > 0) return _cachedPool;

        if (MatchmakingManager.GlobalProfileSprites != null && MatchmakingManager.GlobalProfileSprites.Count > 0)
            _cachedPool = MatchmakingManager.GlobalProfileSprites.ToArray();

        return _cachedPool;
    }

    // Make sure this client's chosen avatar is published to the room so others can see it.
    void PublishLocalAvatar()
    {
        int idx = PlayerProfileManager.GetSavedAvatarIndex();
        if (idx < 0) return;
        if (!PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode) return;

        object current = null;
        if (PhotonNetwork.LocalPlayer.CustomProperties != null)
            PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue(PlayerProfileManager.PROP_AVATAR, out current);
        if (current is int ci && ci == idx) return;

        var props = new ExitGames.Client.Photon.Hashtable { { PlayerProfileManager.PROP_AVATAR, idx } };
        PhotonNetwork.LocalPlayer.SetCustomProperties(props);
    }

    static string GetLocalProfileDisplayName()
    {
        string profileName = PlayerPrefs.GetString("PlayerUsername", string.Empty).Trim();
        if (!string.IsNullOrEmpty(profileName))
            return profileName;

        string nickName = PhotonNetwork.NickName;
        if (!string.IsNullOrEmpty(nickName))
            return nickName;

        return "Player";
    }

    private void SetSeatText(int seatIndex, string name)
    {
        if (name.Length > 10) name = name.Substring(0, 10);
        if (seatIndex == 1 && txtLeftName) txtLeftName.text = name;
        else if (seatIndex == 2 && txtTopName) txtTopName.text = name;
        else if (seatIndex == 3 && txtRightName) txtRightName.text = name;
    }

    public void ShowBotNames(System.Collections.Generic.List<int> botActors)
    {
        UpdateAllNames();
    }

    int GetSeatIndex(int targetActorNumber)
    {
        if (PlayerHand.LocalInstance != null)
        {
            return PlayerHand.LocalInstance.GetSeatIndex(targetActorNumber);
        }

        if (!PhotonNetwork.IsConnectedAndReady) return 0;
        int localActor = PhotonNetwork.LocalPlayer.ActorNumber;
        return (targetActorNumber - localActor + 4) % 4;
    }
}
