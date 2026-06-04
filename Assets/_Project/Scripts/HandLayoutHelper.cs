using UnityEngine;

/// <summary>
/// Call Break style hand layout: prefab size unchanged, spacing + X position only.
/// </summary>
public struct HandLayoutConfig
{
    public float prefabCardWidth;
    public float spacing;
}

public static class HandLayoutHelper
{
    static float? _lockedTwoTaashSpacing;

    public static void ResetTwoTaashSpacingCache()
    {
        _lockedTwoTaashSpacing = null;
    }

    public static float GetPrefabCardWidth(GameObject cardPrefab) => GetPrefabCardSize(cardPrefab).x;

    public static Vector2 GetPrefabCardSize(GameObject cardPrefab)
    {
        if (cardPrefab == null) return new Vector2(100f, 140f);
        RectTransform rt = cardPrefab.GetComponent<RectTransform>();
        if (rt == null) return new Vector2(100f, 140f);
        return rt.sizeDelta;
    }

    /// <summary>
    /// Verifies runtime hand cards match prefab Inspector size (no script should resize cards).
    /// </summary>
    public static void LogCardSizeIntegrity(GameObject cardPrefab, RectTransform runtimeCard, string context)
    {
        Vector2 prefabSize = GetPrefabCardSize(cardPrefab);
        Vector3 prefabScale = cardPrefab != null ? cardPrefab.transform.localScale : Vector3.one;

        float runtimeW = runtimeCard != null ? runtimeCard.sizeDelta.x : 0f;
        float runtimeH = runtimeCard != null ? runtimeCard.sizeDelta.y : 0f;
        Vector3 runtimeScale = runtimeCard != null ? runtimeCard.localScale : Vector3.zero;

        bool sizeMatch = runtimeCard != null &&
                         Mathf.Approximately(runtimeW, prefabSize.x) &&
                         Mathf.Approximately(runtimeH, prefabSize.y);
        bool scaleMatch = runtimeCard != null &&
                          Mathf.Approximately(runtimeScale.x, prefabScale.x) &&
                          Mathf.Approximately(runtimeScale.y, prefabScale.y) &&
                          Mathf.Approximately(runtimeScale.z, prefabScale.z);

        Debug.Log(
            $"[CardSize] {context}\n" +
            $"[CardSize] Prefab Width: {prefabSize.x} | Prefab Height: {prefabSize.y} | Prefab Scale: {prefabScale}\n" +
            $"[CardSize] Runtime Width: {runtimeW} | Runtime Height: {runtimeH} | Runtime Scale: {runtimeScale}\n" +
            $"[CardSize] Match: size={sizeMatch} scale={scaleMatch}");
    }

    public static HandLayoutConfig GetLayout(int cardCount, float availableWidthPx, float prefabCardWidth)
    {
        if (cardCount <= 0) cardCount = 1;
        if (prefabCardWidth <= 0f) prefabCardWidth = 100f;

        float areaWidth = availableWidthPx > 1f ? availableWidthPx * 0.98f : availableWidthPx;
        bool is2Taash = TaashRules.IsTwoTaashMode;

        if (!is2Taash)
            _lockedTwoTaashSpacing = null;

        float spacing;
        if (cardCount == 1)
        {
            spacing = 0f;
        }
        else
        {
            float fitSpacing = (areaWidth - cardCount * prefabCardWidth) / (cardCount - 1);

            if (is2Taash)
            {
                int fullHandCount = TaashRules.CardsPerPlayer;
                bool usedLockedSpacing = false;

                if (cardCount >= fullHandCount)
                {
                    spacing = ComputeTwoTaashSpacing(fitSpacing, prefabCardWidth);
                    _lockedTwoTaashSpacing = spacing;
                }
                else if (_lockedTwoTaashSpacing.HasValue)
                {
                    spacing = _lockedTwoTaashSpacing.Value;
                    usedLockedSpacing = true;
                }
                else
                {
                    spacing = ComputeTwoTaashSpacing(fitSpacing, prefabCardWidth);
                }

                if (usedLockedSpacing)
                {
                    Debug.Log(
                        $"[HandLayout] 2 Taash locked spacing: {spacing} (cardCount={cardCount}, locked at {fullHandCount} cards)");
                }
            }
            else
            {
                // 1 Taash: ~50% of the 2 Taash spacing math, capped to a tight overlapping hand.
                float twoTaashSpacing = Mathf.Clamp(fitSpacing, -prefabCardWidth * 0.72f, 4f);
                float halfTwoTaashSpacing = twoTaashSpacing * 0.5f;

                float maxOverlap = prefabCardWidth * 0.58f;
                float minSpacing = -maxOverlap;
                float compactMaxSpacing = -prefabCardWidth * 0.28f;

                spacing = Mathf.Clamp(halfTwoTaashSpacing, minSpacing, compactMaxSpacing);
            }
        }

        float totalHandWidth = cardCount * prefabCardWidth + (cardCount - 1) * spacing;
        Debug.Log(
            $"[HandLayout] Mode: {(is2Taash ? "2 Taash" : "1 Taash")}\n" +
            $"[HandLayout] Card Count: {cardCount}\n" +
            $"[HandLayout] Spacing Used: {spacing}\n" +
            $"[HandLayout] Hand Width: {availableWidthPx}\n" +
            $"[HandLayout] Total Hand Width: {totalHandWidth}");

        return new HandLayoutConfig
        {
            prefabCardWidth = prefabCardWidth,
            spacing = spacing
        };
    }

    public static float GetHandAreaWidth(RectTransform handArea)
    {
        if (handArea == null) return 0f;
        return handArea.rect.width;
    }

    public static float ComputeStartX(HandLayoutConfig layout, int cardCount)
    {
        float totalWidth = cardCount * layout.prefabCardWidth + (cardCount - 1) * layout.spacing;
        return -totalWidth * 0.5f + layout.prefabCardWidth * 0.5f;
    }

    static float ComputeTwoTaashSpacing(float fitSpacing, float prefabCardWidth)
    {
        float preferredMaxSpacing = 4f;
        float maxOverlap = prefabCardWidth * 0.72f;
        float minSpacing = -maxOverlap;
        return Mathf.Clamp(fitSpacing, minSpacing, preferredMaxSpacing);
    }
}
