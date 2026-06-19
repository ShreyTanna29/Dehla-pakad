using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// Shows a brief themed message near the bottom of a panel. Used to give immediate feedback for
/// profile-panel buttons (Info, Share, Country/World, Get more items, etc.). Creates a single reused
/// toast object under the given panel.
/// </summary>
public static class ProfileToast
{
    public static void Show(Transform panel, string message, float seconds = 1.6f)
    {
        if (panel == null || string.IsNullOrEmpty(message)) return;

        Transform existing = panel.Find("__ProfileToast");
        GameObject go = existing != null ? existing.gameObject : null;
        if (go == null)
        {
            go = new GameObject("__ProfileToast", typeof(RectTransform));
            go.transform.SetParent(panel, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(640f, 76f);
            rt.anchoredPosition = new Vector2(0f, 60f);

            var bg = go.AddComponent<Image>();
            ColorUtility.TryParseHtmlString("#2B1A0C", out Color c);
            c.a = 0.96f;
            bg.color = c;

            var txtGo = new GameObject("Text", typeof(RectTransform));
            txtGo.transform.SetParent(go.transform, false);
            var trt = txtGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(20f, 8f); trt.offsetMax = new Vector2(-20f, -8f);
            var tmp = txtGo.AddComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 28f;
            ColorUtility.TryParseHtmlString("#FFE9C7", out Color tc);
            tmp.color = tc;
            tmp.raycastTarget = false;
            var font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            if (font != null) tmp.font = font;
        }

        go.transform.SetAsLastSibling();
        var label = go.GetComponentInChildren<TMP_Text>();
        if (label != null) label.text = message;

        var cg = go.GetComponent<CanvasGroup>();
        if (cg == null) cg = go.AddComponent<CanvasGroup>();
        cg.DOKill();
        cg.alpha = 0f;
        go.SetActive(true);
        cg.DOFade(1f, 0.18f).SetUpdate(true);
        DOVirtual.DelayedCall(seconds, () =>
        {
            if (cg != null) cg.DOFade(0f, 0.3f).SetUpdate(true).OnComplete(() => { if (go != null) go.SetActive(false); });
        }, false);
    }
}
