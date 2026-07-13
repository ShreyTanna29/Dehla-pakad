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
    public int maxTurnTime = 18; 
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

    void Start() => ResolveTimerUi();

    void ResolveTimerUi()
    {
        if (timerText == null && UiSafeLookup.TryGetPath("TimerText", out GameObject timerGo))
            timerText = timerGo.GetComponent<TMP_Text>();

        if (timerFillBar == null && UiSafeLookup.TryGetPath("TimerFill", out GameObject fillGo))
            timerFillBar = fillGo.GetComponent<Image>();
        else if (timerFillBar == null && timerText != null && timerText.transform.parent != null)
        {
            Transform fill = timerText.transform.parent.Find("TimerFill");
            if (fill != null) timerFillBar = fill.GetComponent<Image>();
        }
    }

    void EnsureTimerVisible()
    {
        ResolveTimerUi();

        // Always show the black turn timer panel in every mode (Bots / Online / Friends).
        if (UiSafeLookup.TryGet("Panel_Timer", out GameObject panelTimer) && panelTimer != null)
        {
            if (!panelTimer.activeSelf) panelTimer.SetActive(true);
            CanvasGroup panelCg = panelTimer.GetComponent<CanvasGroup>();
            if (panelCg != null)
            {
                panelCg.DOKill();
                panelCg.alpha = 1f;
                panelCg.interactable = false;
                panelCg.blocksRaycasts = false;
            }
        }

        if (timerText == null) return;

        Transform t = timerText.transform;
        while (t != null)
        {
            if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            var cg = t.GetComponent<CanvasGroup>();
            if (cg != null && cg.alpha < 0.05f) cg.alpha = 1f;
            t = t.parent;
        }

        timerText.gameObject.SetActive(true);
        // Only force the legacy horizontal bar visible when the circular timers are NOT in use,
        // otherwise it would re-enable the bar we intentionally disabled.
        if (timerFillBar != null && DynamicTimerSetup.Instance == null)
        {
            timerFillBar.gameObject.SetActive(true);
            if (timerFillBar.transform.parent != null)
                timerFillBar.transform.parent.gameObject.SetActive(true);
        }
    }

    void HideTimerUi()
    {
        ResolveTimerUi();
        if (UiSafeLookup.TryGet("Panel_Timer", out GameObject panelTimer) && panelTimer != null)
            panelTimer.SetActive(false);
        else if (timerText != null && timerText.transform.parent != null)
            timerText.transform.parent.gameObject.SetActive(false);

        if (timerFillBar != null && timerFillBar.transform.parent != null
            && DynamicTimerSetup.Instance == null)
            timerFillBar.transform.parent.gameObject.SetActive(false);
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
        if (DynamicTimerSetup.Instance != null) DynamicTimerSetup.Instance.HideAll();

        // Hide the standalone timer panel so the "YOUR TURN" text does not linger over the
        // menu/Modes screen after a game ends or the player backs out. Panel_Timer lives at the
        // root Canvas (a sibling of the game panel), so hiding the game panel does NOT hide it —
        // we must hide it explicitly here. EnsureTimerVisible() re-activates it automatically
        // when the next turn starts, so the in-game timer keeps working.
        HideTimerUi();
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

        if (currentTime > 0) yield break;

        // Brief wait if a trick resolve briefly overlaps the timer — do not soft-lock forever.
        float unlockWait = 2.5f;
        while (PlayerHand.IsTrickLocked && unlockWait > 0f)
        {
            yield return new WaitForSeconds(0.25f);
            unlockWait -= 0.25f;
        }
        if (PlayerHand.IsTrickLocked) yield break;

        int timedOutActor = currentActorTurn;
        if (timedOutActor < 1 || !PhotonNetwork.IsMasterClient || PlayerHand.LocalInstance == null)
            yield break;

        // Prefer local auto-play on the timed-out client first.
        if (DeckManager.Instance != null && DeckManager.Instance.IsActorBotControlled(timedOutActor))
        {
            PlayerHand.LocalInstance.ForceBotPlayImmediate(timedOutActor);
        }
        else
        {
            Player turnPlayer = PhotonNetwork.CurrentRoom != null
                ? PhotonNetwork.CurrentRoom.GetPlayer(timedOutActor)
                : null;
            bool fullyLeft = turnPlayer == null;
            bool inactive = turnPlayer != null && turnPlayer.IsInactive;

            if (fullyLeft && DeckManager.Instance != null)
            {
                DeckManager.Instance.MasterConvertAbsentPlayerToBot(timedOutActor);
                PlayerHand.LocalInstance.ForceBotPlayImmediate(timedOutActor);
            }
            else if (inactive)
            {
                // Still inside PlayerTTL — force-play without converting so reconnect can reclaim the seat.
                PlayerHand.LocalInstance.MasterForceTimeoutPlay(timedOutActor);
                StartCoroutine(MasterTimeoutRecovery(timedOutActor));
            }
            else
            {
                photonView.RPC("RPC_TimeUpAutoPlay", RpcTarget.All, timedOutActor);
                StartCoroutine(MasterTimeoutRecovery(timedOutActor));
            }
        }
    }

    IEnumerator MasterTimeoutRecovery(int actorNumber)
    {
        yield return new WaitForSeconds(1.35f);
        if (!PhotonNetwork.IsMasterClient || PlayerHand.LocalInstance == null) yield break;
        if (PlayerHand.IsTrickLocked) yield break;
        if (actorNumber != PlayerHand.LocalInstance.GetAuthoritativeTurnActor()) yield break;

        PlayerHand.LocalInstance.MasterForceTimeoutPlay(actorNumber);
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
        EnsureTimerVisible();

        float fillAmount = maxTurnTime > 0 ? (float)currentTime / maxTurnTime : 0f;

        // Circular per-seat timer (Callbreak style) takes priority when present. It is driven from the
        // authoritative, network-synced currentTime/currentActorTurn — never from a separate countdown.
        if (DynamicTimerSetup.Instance != null)
        {
            int seat = PlayerHand.LocalInstance != null
                ? PlayerHand.LocalInstance.GetSeatIndex(currentActorTurn)
                : -1;
            DynamicTimerSetup.Instance.ShowForSeat(seat, fillAmount);
        }
        else if (timerFillBar != null)
        {
            timerFillBar.fillAmount = fillAmount;

            // 🎨 COLOR CHANGE: Green to Red
            if (fillAmount > 0.6f) timerFillBar.color = Color.green;
            else if (fillAmount > 0.3f) timerFillBar.color = Color.yellow;
            else timerFillBar.color = Color.red;

            // Optional: Punch scale on tick
            if (timerFillBar.transform.parent != null)
                timerFillBar.transform.parent.DOPunchScale(new Vector3(0.05f, 0.05f, 0), 0.2f);
        }

        if (timerText != null)
        {
            int localActorNum = PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber : -1;
            if (localActorNum == currentActorTurn)
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

        if (PlayerHand.IsTrickLocked)
            return;

        if (PhotonNetwork.LocalPlayer == null || PhotonNetwork.LocalPlayer.ActorNumber != actorNumber)
            return;

        // Clear a stuck play-lock so timeout auto-play can still advance the turn.
        CardInteract.isPlayingCard = false;
        CardInteract.canPlayCards = true;

        // Do not gate timeout auto-play on CanAcceptPlayerInput — a stale phase/lock was soft-locking matches.
        if (GameFlowState.Current == GameFlowPhase.GameFinished)
            return;

        Debug.Log("⏳ Time Up! Forcefully auto-playing a valid card...");
        AutoPlayValidCard();
    }

    void AutoPlayValidCard()
    {
        if (PlayerHand.IsTrickLocked) return;

        PlayerHand myHand = PlayerHand.LocalInstance;
        if (myHand == null || myHand.myCards == null || myHand.myCards.Count == 0) return;

        // Never leave timeout blocked by a stale drag/play lock.
        CardInteract.isPlayingCard = false;

        int localActor = PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber : -1;
        List<CardData> legalCards = PlayerHand.GetValidCards(myHand.myCards, myHand.currentTrick, localActor);
        if (legalCards == null || legalCards.Count == 0) return;
        CardData cardToPlay = legalCards[0];

        // Prefer the local hand UI only — FindObjectsByType can hit opponent/table cards.
        GameObject cardUIObj = myHand.FindCardObjectInLocalHand(cardToPlay);

        Debug.Log("[TurnManager] Auto-playing card due to timeout.");
        CardInteract.canPlayCards = true;
        CardInteract.isPlayingCard = true;
        myHand.OnLocalPlayerPlayedCard(cardToPlay, cardUIObj);
    }
}