using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// Shared hover scale feedback (matches Play Online button).
/// </summary>
public static class UIButtonHoverUtility
{
    const float HoverScaleMultiplier = 1.1f;
    const float HoverTweenDuration = 0.15f;

    public static void SetupHoverScale(Button btn)
    {
        if (btn == null) return;

        Vector3 originalScale = btn.transform.localScale;

        ButtonEventHelper helper = btn.gameObject.GetComponent<ButtonEventHelper>();
        if (helper == null)
            helper = btn.gameObject.AddComponent<ButtonEventHelper>();

        helper.OnPointerEnterAction = () =>
        {
            if (btn.interactable)
                btn.transform.DOScale(originalScale * HoverScaleMultiplier, HoverTweenDuration).SetUpdate(true);
        };

        helper.OnPointerExitAction = () =>
        {
            btn.transform.DOScale(originalScale, HoverTweenDuration).SetUpdate(true);
        };
    }
}
