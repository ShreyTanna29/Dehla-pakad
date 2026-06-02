using UnityEngine;

[RequireComponent(typeof(RectTransform))]
public class SafeArea : MonoBehaviour
{
    private RectTransform rectTransform;
    private Rect lastSafeArea = new Rect(0, 0, 0, 0);
    private Vector2 lastScreenSize = new Vector2(0, 0);
    private ScreenOrientation lastOrientation = ScreenOrientation.AutoRotation;

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        Refresh();
    }

    void Update()
    {
        Refresh();
    }

    void Refresh()
    {
        Rect safeArea = Screen.safeArea;

        if (safeArea != lastSafeArea || Screen.width != lastScreenSize.x || Screen.height != lastScreenSize.y || Screen.orientation != lastOrientation)
        {
            lastScreenSize.x = Screen.width;
            lastScreenSize.y = Screen.height;
            lastOrientation = Screen.orientation;
            ApplySafeArea(safeArea);
        }
    }

    void ApplySafeArea(Rect r)
    {
        lastSafeArea = r;

        // Check for valid screen size
        if (Screen.width == 0 || Screen.height == 0) return;

        // Convert safe area rectangle from pixels to normalized anchor coordinates (0..1)
        Vector2 anchorMin = r.position;
        Vector2 anchorMax = r.position + r.size;

        anchorMin.x /= Screen.width;
        anchorMin.y /= Screen.height;
        anchorMax.x /= Screen.width;
        anchorMax.y /= Screen.height;

        // Apply to my RectTransform
        // rectTransform.anchorMin = anchorMin;
        // rectTransform.anchorMax = anchorMax;

        Debug.LogFormat("Safe Area Logic Calculated: [{0}, {1}] to [{2}, {3}] (Not applied to RectTransform per request)", anchorMin.x, anchorMin.y, anchorMax.x, anchorMax.y);
    }
}