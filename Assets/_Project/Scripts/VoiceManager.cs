using System.Collections;
using Photon.Pun;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(PhotonView))]
public class VoiceManager : MonoBehaviourPun
{
    public static VoiceManager Instance { get; private set; }

    [Header("UI Slots (Drag & Drop Here)")]
    public GameObject voicePanel;
    public Button openVoiceButton;
    public Button invisibleBgButton;

    [Header("Audio Settings")]
    public AudioSource audioSource;
    public AudioClip[] voiceClips;

    AudioSource _sfxSource;
    GameObject _autoBlocker;

    void Awake()
    {
        Instance = this;
        EnsureSfxSource();
        PreloadClips();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Start()
    {
        if (openVoiceButton != null)
            openVoiceButton.onClick.AddListener(OpenPanel);
        if (invisibleBgButton != null)
            invisibleBgButton.onClick.AddListener(ClosePanel);

        ClosePanel();
    }

    void EnsureSfxSource()
    {
        AudioListener listener = FindAnyObjectByType<AudioListener>();
        if (listener != null)
        {
            Transform child = listener.transform.Find("VoiceSfx");
            if (child == null)
            {
                var go = new GameObject("VoiceSfx");
                go.transform.SetParent(listener.transform, false);
                _sfxSource = go.AddComponent<AudioSource>();
            }
            else
            {
                _sfxSource = child.GetComponent<AudioSource>();
                if (_sfxSource == null)
                    _sfxSource = child.gameObject.AddComponent<AudioSource>();
            }
        }

        if (_sfxSource == null)
        {
            if (audioSource == null)
                audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
                audioSource = gameObject.AddComponent<AudioSource>();
            _sfxSource = audioSource;
        }

        Configure2D(_sfxSource);
    }

    static void Configure2D(AudioSource src)
    {
        if (src == null) return;
        src.playOnAwake = false;
        src.spatialBlend = 0f;
        src.spatialize = false;
        src.volume = 1f;
        src.mute = false;
        src.loop = false;
        src.panStereo = 0f;
    }

    void PreloadClips()
    {
        if (voiceClips == null) return;
        foreach (AudioClip clip in voiceClips)
        {
            if (clip == null) continue;
            if (clip.loadState == AudioDataLoadState.Unloaded)
                clip.LoadAudioData();
        }
    }

    void EnsureAutoBlocker()
    {
        if (_autoBlocker != null || voicePanel == null) return;

        Transform parent = voicePanel.transform.parent;
        if (parent == null) return;

        _autoBlocker = new GameObject("VoiceBlocker_Auto");
        _autoBlocker.transform.SetParent(parent, false);
        _autoBlocker.transform.SetSiblingIndex(voicePanel.transform.GetSiblingIndex());

        RectTransform rt = _autoBlocker.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        Image img = _autoBlocker.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0f);
        img.raycastTarget = true;

        Button btn = _autoBlocker.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(ClosePanel);
    }

    public void OpenPanel()
    {
        EnsureAutoBlocker();

        if (_autoBlocker != null)
            _autoBlocker.SetActive(true);
        if (invisibleBgButton != null)
            invisibleBgButton.gameObject.SetActive(true);
        if (voicePanel != null)
        {
            voicePanel.SetActive(true);
            voicePanel.transform.SetAsLastSibling();
        }
    }

    public void ClosePanel()
    {
        if (voicePanel != null)
            voicePanel.SetActive(false);
        if (invisibleBgButton != null)
            invisibleBgButton.gameObject.SetActive(false);
        if (_autoBlocker != null)
            _autoBlocker.SetActive(false);
    }

    public void Click_SendVoice(int index)
    {
        ClosePanel();
        PlayVoiceLocal(index);

        if (!PhotonNetwork.InRoom) return;

        if (photonView != null && photonView.ViewID > 0)
            photonView.RPC(nameof(RPC_PlayVoice), RpcTarget.Others, index);
    }

    [PunRPC]
    public void RPC_PlayVoice(int index)
    {
        PlayVoiceLocal(index);
    }

    public void PlayVoiceLocal(int index)
    {
        StartCoroutine(PlayVoiceRoutine(index));
    }

    IEnumerator PlayVoiceRoutine(int index)
    {
        if (voiceClips == null || index < 0 || index >= voiceClips.Length)
            yield break;

        AudioClip clip = voiceClips[index];
        if (clip == null)
            yield break;

        EnsureSfxSource();
        Configure2D(_sfxSource);

        if (clip.loadState == AudioDataLoadState.Unloaded)
            clip.LoadAudioData();

        float wait = 2f;
        while (clip.loadState == AudioDataLoadState.Loading && wait > 0f)
        {
            wait -= Time.unscaledDeltaTime;
            yield return null;
        }

        _sfxSource.PlayOneShot(clip, 1f);
    }
}
