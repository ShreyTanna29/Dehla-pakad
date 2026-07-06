using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Debug overlay — does NOT block UI clicks (no GraphicRaycaster, no raycast on background).
/// </summary>
public class PhotonNetworkDebug : MonoBehaviour
{
    public static PhotonNetworkDebug Instance;

    [Header("Optional — assign in scene or auto-created")]
    public TMP_Text debugText;
    public Canvas debugCanvas;

    [Header("Settings")]
    [Tooltip("Keep OFF for release builds — this overlay rebuilds a status string every frame.")]
    public bool showOnAndroid = false;
    public bool showInEditor = false;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        EnsureOverlayExists();
    }

    void EnsureOverlayExists()
    {
        if (debugText != null) return;

        GameObject canvasGo = new GameObject("PhotonDebugCanvas");
        canvasGo.transform.SetParent(transform, false);

        debugCanvas = canvasGo.AddComponent<Canvas>();
        debugCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        debugCanvas.sortingOrder = 100;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

        var cg = canvasGo.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = false;
        cg.interactable = false;

        GameObject textGo = new GameObject("DebugText");
        textGo.transform.SetParent(canvasGo.transform, false);
        RectTransform rt = textGo.AddComponent<RectTransform>();
        // rt.anchorMin = new Vector2(0, 1);
        // rt.anchorMax = new Vector2(1, 1);
        // rt.pivot = new Vector2(0.5f, 1);
        // rt.sizeDelta = new Vector2(0, 280);
        // rt.anchoredPosition = Vector2.zero;

        debugText = textGo.AddComponent<TextMeshProUGUI>();
        debugText.fontSize = 20;
        debugText.alignment = TextAlignmentOptions.TopLeft;
        debugText.color = Color.white;
        debugText.raycastTarget = false;

        Image bg = textGo.AddComponent<Image>();
        bg.color = new Color(0, 0, 0, 0.5f);
        bg.raycastTarget = false;
    }

    void Update()
    {
        bool visible = Application.isEditor ? showInEditor : showOnAndroid;
        if (debugCanvas != null) debugCanvas.enabled = visible;
        if (!visible || debugText == null || NetworkManager.Instance == null) return;

        debugText.text = NetworkManager.GetDebugStatusBlock();
    }
}
