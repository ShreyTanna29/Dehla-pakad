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

        if (imgMyAvatar == null && UiSafeLookup.TryGetPath("You/You_Avatar", out GameObject myGo))
            imgMyAvatar = myGo.GetComponent<UnityEngine.UI.Image>();
        if (imgLeftAvatar == null && UiSafeLookup.TryGetPath("Opponent_Left/Playe2_Avatar", out GameObject leftGo))
            imgLeftAvatar = leftGo.GetComponent<UnityEngine.UI.Image>();
        if (imgTopAvatar == null && UiSafeLookup.TryGetPath("Opponent_Top/Player3_Avatar", out GameObject topGo))
            imgTopAvatar = topGo.GetComponent<UnityEngine.UI.Image>();
        if (imgRightAvatar == null && UiSafeLookup.TryGetPath("Opponent_Right/Playe4_Avatar", out GameObject rightGo))
            imgRightAvatar = rightGo.GetComponent<UnityEngine.UI.Image>();

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
    
    public override void OnPlayerEnteredRoom(Player newPlayer) { UpdateAllNames(); }
    public override void OnPlayerLeftRoom(Player otherPlayer) { UpdateAllNames(); }

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
            string myName = PhotonNetwork.LocalPlayer.NickName;
            if (myName.Length > 10) myName = myName.Substring(0, 10); 
            txtMyName.text = myName + " (Me)";
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
        if (img == null) return;
        List<Sprite> pool = MatchmakingManager.GlobalProfileSprites;
        if (pool == null || pool.Count == 0) return;
        int spriteIndex = Mathf.Abs(actorNumber) % pool.Count;
        img.sprite = pool[spriteIndex];
        img.preserveAspect = true;
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
