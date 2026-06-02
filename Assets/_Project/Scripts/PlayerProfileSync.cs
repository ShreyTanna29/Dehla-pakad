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

    [Header("Avatar Components")]
    private UnityEngine.UI.Image imgMyAvatar;
    private UnityEngine.UI.Image imgLeftAvatar;
    private UnityEngine.UI.Image imgTopAvatar;
    private UnityEngine.UI.Image imgRightAvatar;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        SetupAvatars();
    }

    void SetupAvatars()
    {
        imgMyAvatar = GameObject.Find("You/You_Avatar")?.GetComponent<UnityEngine.UI.Image>();
        imgLeftAvatar = GameObject.Find("Opponent_Left/Playe2_Avatar")?.GetComponent<UnityEngine.UI.Image>();
        imgTopAvatar = GameObject.Find("Opponent_Top/Player3_Avatar")?.GetComponent<UnityEngine.UI.Image>();
        imgRightAvatar = GameObject.Find("Opponent_Right/Playe4_Avatar")?.GetComponent<UnityEngine.UI.Image>();

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

    void Update()
    {
        if (PlayerHand.LocalInstance != null && txtTopName != null && txtTopName.text == "...")
        {
            UpdateAllNames();
        }
    }

    public void UpdateAllNames()
    {
        if (txtLeftName) txtLeftName.text = "...";
        if (txtTopName) txtTopName.text = "...";
        if (txtRightName) txtRightName.text = "...";

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
            SetSeatText(seatIndex, p.NickName);
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
