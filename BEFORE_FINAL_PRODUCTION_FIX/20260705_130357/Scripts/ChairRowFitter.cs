using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Keeps a UI row (laid out at a fixed natural size by a HorizontalLayoutGroup +
/// ContentSizeFitter) looking identical on every device by uniformly scaling it so it
/// always fits inside its parent's width. Card sizes and spacing stay in the exact same
/// proportion on every aspect ratio — only the overall scale changes when space is tight,
/// so the cards never overlap and never clip off-screen.
/// </summary>
[RequireComponent(typeof(RectTransform))]
[ExecuteAlways]
[DisallowMultipleComponent]
public class ChairRowFitter : MonoBehaviour
{
    [Tooltip("Fraction of the parent's width the row is allowed to occupy (leaves side margins).")]
    [Range(0.5f, 1f)]
    [SerializeField] float maxWidthFraction = 0.92f;

    [Tooltip("Optional explicit natural width. <= 0 means auto-detect from the layout's preferred width.")]
    [SerializeField] float naturalWidth = 0f;

    RectTransform _rt;
    RectTransform _parentRt;
    float _lastParentWidth = -1f;

    void Awake() => Cache();
    void OnEnable() { Cache(); Fit(); }
    void OnRectTransformDimensionsChange() => Fit();

    void Update()
    {
        // The row's own rect doesn't change when the parent (canvas) resizes, so poll the
        // parent width and refit on change. Cheap and bullet-proof across orientation /
        // resolution / device-simulator changes.
        if (_parentRt == null) Cache();
        if (_parentRt == null) return;

        float w = _parentRt.rect.width;
        if (!Mathf.Approximately(w, _lastParentWidth))
            Fit();
    }

    void Cache()
    {
        if (_rt == null) _rt = GetComponent<RectTransform>();
        if (_rt != null) _parentRt = _rt.parent as RectTransform;
    }

    public void Fit()
    {
        if (_rt == null) Cache();
        if (_rt == null || _parentRt == null) return;

        float rowWidth = naturalWidth > 0f ? naturalWidth : LayoutUtility.GetPreferredWidth(_rt);
        if (rowWidth <= 0f) rowWidth = _rt.rect.width;
        if (rowWidth <= 0f) return;

        _lastParentWidth = _parentRt.rect.width;

        float available = _lastParentWidth * Mathf.Clamp01(maxWidthFraction);
        float scale = Mathf.Min(1f, available / rowWidth);
        _rt.localScale = new Vector3(scale, scale, 1f);
    }
}
