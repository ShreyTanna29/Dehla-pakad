using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// Small helpers to display the player's short UID and let the user copy it by tapping.
/// Used on the Profile screen, Home screen and the Friends panel header.
/// </summary>
public static class UidUI
{
    /// <summary>
    /// Sets the label to "UID: 1234567890" and turns its GameObject into a tap-to-copy button.
    /// Tapping copies the raw UID to the clipboard and briefly shows "Copied!".
    /// </summary>
    public static void BindCopyLabel(TMP_Text label, string uid, string prefix = "UID: ")
    {
        if (label == null) return;

        string baseText = string.IsNullOrEmpty(uid) ? prefix + "—" : prefix + uid;
        label.text = baseText;
        label.raycastTarget = true;

        Button btn = label.GetComponent<Button>();
        if (btn == null)
        {
            btn = label.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
        }

        btn.onClick.RemoveAllListeners();

        if (string.IsNullOrEmpty(uid))
        {
            btn.interactable = false;
            return;
        }

        btn.interactable = true;
        string captured = uid;
        btn.onClick.AddListener(() =>
        {
            GUIUtility.systemCopyBuffer = captured;
            label.text = "Copied!";
            DOVirtual.DelayedCall(1.4f, () =>
            {
                if (label != null) label.text = baseText;
            }, false);
        });
    }

    /// <summary>
    /// Finds (or creates once) a child TMP label under <paramref name="parent"/> used to show the UID.
    /// Style is copied from <paramref name="styleRef"/> so it matches the surrounding text.
    /// </summary>
    public static TMP_Text EnsureChildLabel(Transform parent, string objName, TMP_Text styleRef,
        Vector2 anchoredOffset)
    {
        if (parent == null) return null;

        Transform existing = parent.Find(objName);
        if (existing != null)
        {
            TMP_Text found = existing.GetComponent<TMP_Text>();
            if (found != null) return found;
        }

        var go = new GameObject(objName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();

        if (styleRef != null)
        {
            if (styleRef.font != null) tmp.font = styleRef.font;
            tmp.fontSize = Mathf.Max(16f, styleRef.fontSize * 0.6f);
            tmp.alignment = styleRef.alignment;

            RectTransform rt = tmp.rectTransform;
            RectTransform srt = styleRef.rectTransform;
            rt.anchorMin = srt.anchorMin;
            rt.anchorMax = srt.anchorMax;
            rt.pivot = srt.pivot;
            rt.sizeDelta = srt.sizeDelta;
            rt.anchoredPosition = srt.anchoredPosition + anchoredOffset;
        }

        tmp.color = new Color(1f, 0.92f, 0.7f, 0.95f);
        tmp.fontStyle = FontStyles.Bold;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        return tmp;
    }
}
