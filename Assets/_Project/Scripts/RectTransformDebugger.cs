using UnityEngine;
using System.Collections;

public class RectTransformDebugger : MonoBehaviour
{
    private RectTransform rt;

    void Start()
    {
        rt = GetComponent<RectTransform>();
        LogRectTransform("Start");
        StartCoroutine(LogAfterDelay(1f));
    }

    IEnumerator LogAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        LogRectTransform("After 1 Second");
    }

    void LogRectTransform(string label)
    {
        if (rt == null) return;

        Debug.Log($"[{label}] {gameObject.name} RectTransform:\n" +
                  $" - anchorMin: {rt.anchorMin}\n" +
                  $" - anchorMax: {rt.anchorMax}\n" +
                  $" - pivot: {rt.pivot}\n" +
                  $" - sizeDelta: {rt.sizeDelta}\n" +
                  $" - anchoredPosition: {rt.anchoredPosition}\n" +
                  $" - offsetMin (Left/Bottom): {rt.offsetMin}\n" +
                  $" - offsetMax (Right/Top): {rt.offsetMax}");
    }
}
