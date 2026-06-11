using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using TMPro; 
using UnityEngine.UI; 
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;

public class TurnManager : MonoBehaviourPunCallbacks
{
    public static TurnManager Instance;

    [Header("Timer Settings")]
    public int maxTurnTime = 15; 
    private int currentTime;
    private bool isTimerRunning = false;
    private bool isPaused = false;
    private int currentActorTurn = -1;

    [Header("UI Reference")]
    public TMP_Text timerText; 
    public UnityEngine.UI.Image timerFillBar; 

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public void SetPaused(bool paused)
    {
        isPaused = paused;
        if (paused)
        {
            if (timerText != null) timerText.text = "Waiting for Player...";
        }
        else
        {
            UpdateTimerUI();

            // 🚀 RESUME BOT THINKING: If the game was paused waiting for a human and now it's a bot's turn (or player timeout)
            if (PhotonNetwork.IsMasterClient && PlayerHand.LocalInstance != null && currentActorTurn != -1
                && DeckManager.Instance != null && DeckManager.Instance.IsDealingComplete)
            {
                PlayerHand.LocalInstance.TriggerBotTurnIfApplicable(currentActorTurn);
            }
        }
    }

    public void StartTurn(int actorNumber)
    {
        if (!GameStabilityAudit.CanStartTurn())
        {
            Debug.LogWarning($"[TurnManager] StartTurn blocked — actor={actorNumber} state={GameFlowState.Current}");
            return;
        }

        GameStabilityAudit.LogTurn("StartTurn", actorNumber,
            PlayerHand.LocalInstance != null && PlayerHand.LocalInstance.currentTrick != null
                ? PlayerHand.LocalInstance.currentTrick.Count
                : 0);

        currentActorTurn = actorNumber;
        currentTime = maxTurnTime; // Reset local time
        isPaused = false;
        
        if (PhotonNetwork.IsMasterClient)
        {
            photonView.RPC("RPC_SyncTimerTurn", RpcTarget.All, currentActorTurn, maxTurnTime);
            StopAllCoroutines();
            StartCoroutine(MasterTimerRoutine());
        }
    }

    public void StopTimer()
    {
        isTimerRunning = false;
        isPaused = false;
        StopAllCoroutines();
        if (timerText != null) timerText.text = "Wait...";
        if (timerFillBar != null) timerFillBar.fillAmount = 0;
    }

    // 🚀 MASTER SWITCH: If the previous master left mid-timer, the new one takes over.
    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        if (newMasterClient.IsLocal && isTimerRunning)
        {
            Debug.Log("[TurnManager] I am the new Master. Resuming timer routine.");
            StopAllCoroutines();
            StartCoroutine(MasterTimerRoutine());
        }
    }

    IEnumerator MasterTimerRoutine()
    {
        while (currentTime > 0)
        {
            if (!isPaused)
            {
                yield return new WaitForSeconds(1f);
                if (!isPaused) // Double check after wait
                {
                    currentTime--;
                    photonView.RPC("RPC_UpdateTime", RpcTarget.All, currentTime);
                }
            }
            else
            {
                yield return new WaitForSeconds(0.5f);
            }
        }

        if (currentTime <= 0)
        {
            if (PlayerHand.IsTrickLocked) yield break;

            if (PhotonNetwork.IsMasterClient && DeckManager.Instance != null
                && DeckManager.Instance.IsActorBotControlled(currentActorTurn)
                && PlayerHand.LocalInstance != null)
            {
                PlayerHand.LocalInstance.ForceBotPlayImmediate(currentActorTurn);
            }
            else if (!PlayerHand.IsTrickLocked)
            {
                photonView.RPC("RPC_TimeUpAutoPlay", RpcTarget.All, currentActorTurn);
            }
        }
    }

    [PunRPC]
    public void RPC_SyncTimerState(int actorNumber, int timeRemaining)
    {
        currentActorTurn = actorNumber;
        currentTime = timeRemaining;
        isTimerRunning = true;
        UpdateTimerUI();
        
        Debug.Log($"[TurnManager] Timer State Restored: Actor {actorNumber}, Time = {timeRemaining}s");
    }

    [PunRPC]
    void RPC_SyncTimerTurn(int actorNumber, int timeStarted)
    {
        currentActorTurn = actorNumber;
        currentTime = timeStarted;
        isTimerRunning = true;
        UpdateTimerUI();
    }

    [PunRPC]
    void RPC_UpdateTime(int timeRemaining)
    {
        currentTime = timeRemaining;
        UpdateTimerUI();
    }

    void UpdateTimerUI()
    {
        if (timerFillBar != null)
        {
            float fillAmount = (float)currentTime / maxTurnTime;
            timerFillBar.fillAmount = fillAmount;

            // 🎨 COLOR CHANGE: Green to Red
            if (fillAmount > 0.6f) timerFillBar.color = Color.green;
            else if (fillAmount > 0.3f) timerFillBar.color = Color.yellow;
            else timerFillBar.color = Color.red;

            // Optional: Punch scale on tick
            timerFillBar.transform.parent.DOPunchScale(new Vector3(0.05f, 0.05f, 0), 0.2f);
        }

        if (timerText != null)
        {
            if (PhotonNetwork.LocalPlayer.ActorNumber == currentActorTurn)
            {
                timerText.color = Color.white;
                timerText.text = $"YOUR TURN: {currentTime}s";
            }
            else
            {
                timerText.color = Color.white;
                timerText.text = $"PLAYER {currentActorTurn}: {currentTime}s";
            }
        }
    }

    [PunRPC]
    void RPC_TimeUpAutoPlay(int actorNumber)
    {
        isTimerRunning = false;

        if (!GameStabilityAudit.CanAcceptPlayerInput() || PlayerHand.IsTrickLocked)
            return;
        
        if (PhotonNetwork.LocalPlayer.ActorNumber == actorNumber)
        {
            Debug.Log("⏳ Time Up! Forcefully auto-playing a valid card...");
            AutoPlayValidCard();
        }
    }

    void AutoPlayValidCard()
    {
        if (PlayerHand.IsTrickLocked) return;

        PlayerHand myHand = PlayerHand.LocalInstance;
        if (myHand == null || myHand.myCards == null || myHand.myCards.Count == 0 || CardInteract.isPlayingCard) return;

        CardData cardToPlay = myHand.myCards[0]; 

        if (myHand.currentTrick != null && myHand.currentTrick.Count > 0)
        {
            CardSuit ledSuit = myHand.currentTrick[0].suit;
            List<CardData> matchingCards = myHand.myCards.FindAll(c => c.cardSuit == ledSuit);
            
            if (matchingCards.Count > 0)
            {
                cardToPlay = matchingCards[0]; 
            }
        }

        GameObject cardUIObj = null;
        
        CardDisplay[] allDisplays = Object.FindObjectsByType<CardDisplay>(FindObjectsSortMode.None);
        foreach (CardDisplay display in allDisplays)
        {
            if (display != null && display.myCardData.cardSuit == cardToPlay.cardSuit && display.myCardData.cardRank == cardToPlay.cardRank)
            {
                cardUIObj = display.gameObject;
                break;
            }
        }

        if (cardUIObj != null)
        {
            Debug.Log("[TurnManager] Auto-playing card due to timeout.");
            CardInteract.canPlayCards = true; 
            CardInteract.isPlayingCard = true; // Set lock
            myHand.OnLocalPlayerPlayedCard(cardToPlay, cardUIObj);
        }
    }
}