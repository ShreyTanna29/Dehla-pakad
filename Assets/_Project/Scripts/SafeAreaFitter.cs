using UnityEngine;

/// <summary>
/// Resizes its RectTransform to the device safe area so content never sits under a notch, punch-hole
/// or rounded corner. Re-applies when the safe area or orientation changes (foldables/rotation).
/// Attach to a full-screen container whose children should stay inside the safe area.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class SafeAreaFitter : MonoBehaviour
{
    [Tooltip("When enabled, keeps the RectTransform anchors/offsets exactly as set in the Editor.")]
    [SerializeField] private bool preserveManualAnchors;

    RectTransform _rt;
    Rect _lastSafe;
    Vector2Int _lastScreen;

    void Awake() { _rt = GetComponent<RectTransform>(); Apply(); }
    void OnEnable() { Apply(); }

    void Update()
    {
        if (preserveManualAnchors) return;
        if (Screen.safeArea != _lastSafe || Screen.width != _lastScreen.x || Screen.height != _lastScreen.y)
            Apply();
    }

    void Apply()
    {
        if (preserveManualAnchors) return;
        if (_rt == null) _rt = GetComponent<RectTransform>();
        if (Screen.width <= 0 || Screen.height <= 0) return;

        Rect safe = Screen.safeArea;
        _lastSafe = safe;
        _lastScreen = new Vector2Int(Screen.width, Screen.height);

        Vector2 anchorMin = safe.position;
        Vector2 anchorMax = safe.position + safe.size;
        anchorMin.x /= Screen.width;
        anchorMin.y /= Screen.height;
        anchorMax.x /= Screen.width;
        anchorMax.y /= Screen.height;

        if (float.IsNaN(anchorMin.x) || float.IsNaN(anchorMax.x)) return;

        _rt.anchorMin = anchorMin;
        _rt.anchorMax = anchorMax;
        _rt.offsetMin = Vector2.zero;
        _rt.offsetMax = Vector2.zero;
    }
}
