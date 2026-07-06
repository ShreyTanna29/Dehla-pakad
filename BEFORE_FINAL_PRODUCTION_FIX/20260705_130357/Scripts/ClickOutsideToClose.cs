using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Task 5 — Reusable "click outside to close".
///
/// Attach this to a FULL-SCREEN, raycast-target overlay Image that sits BEHIND the panel
/// (same Canvas, lower sibling index than the panel so the panel renders on top and still
/// receives its own clicks). When the user presses anywhere that is NOT inside
/// <see cref="panelToClose"/>, the panel — and optionally this overlay — are deactivated.
///
/// Requires an EventSystem + a GraphicRaycaster on the Canvas (standard uGUI setup) and a
/// Graphic (e.g. an Image with Raycast Target enabled; alpha may be 0) on this GameObject so
/// it can receive pointer events. This mirrors the project's existing dim-overlay close pattern,
/// but as a drop-in component usable by any panel (Friends List, etc.).
/// </summary>
[RequireComponent(typeof(UnityEngine.UI.Graphic))]
public class ClickOutsideToClose : MonoBehaviour, IPointerDownHandler
{
    [Tooltip("The panel that closes when the user clicks outside of it.")]
    [SerializeField] private RectTransform panelToClose;

    [Tooltip("Also deactivate THIS overlay GameObject when the panel closes.")]
    [SerializeField] private bool deactivateSelfOnClose = true;

    /// <summary>Assign the target panel at runtime (e.g. from the controller that opens it).</summary>
    public void SetPanel(RectTransform panel) => panelToClose = panel;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (panelToClose == null || !panelToClose.gameObject.activeInHierarchy) return;

        // If the press landed inside the panel's rect, ignore it (let the panel handle the input).
        if (RectTransformUtility.RectangleContainsScreenPoint(
                panelToClose, eventData.position, eventData.pressEventCamera))
            return;

        panelToClose.gameObject.SetActive(false);
        if (deactivateSelfOnClose) gameObject.SetActive(false);
    }
}
