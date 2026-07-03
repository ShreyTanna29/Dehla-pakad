using UnityEngine;

public struct HandLayoutConfig
{
    public float prefabCardWidth;
    public float spacing;
}

public static class HandLayoutHelper
{
    public const int CardsPerRow = 13;

    static float? _lockedTwoTaashSpacing;

    static float? _lockedOneTaashSpacing;

    public static void ResetTwoTaashSpacingCache()
    {
        _lockedTwoTaashSpacing = null;
        _lockedOneTaashSpacing = null;
    }

    public static float GetPrefabCardWidth(GameObject cardPrefab) => GetPrefabCardSize(cardPrefab).x;

    public static Vector2 GetPrefabCardSize(GameObject cardPrefab)
    {
        if (cardPrefab == null) return new Vector2(100f, 140f);
        RectTransform rt = cardPrefab.GetComponent<RectTransform>();
        if (rt == null) return new Vector2(100f, 140f);
        return rt.sizeDelta;
    }

    public static void LogCardSizeIntegrity(GameObject cardPrefab, RectTransform runtimeCard, string context) { }

    public static HandLayoutConfig GetLayout(int cardCount, float availableWidthPx, float prefabCardWidth)
    {
        if (cardCount <= 0) cardCount = 1;
        if (prefabCardWidth <= 0f) prefabCardWidth = 100f;

        float areaWidth = availableWidthPx > 1f ? availableWidthPx * 0.98f : availableWidthPx;
        bool is2Taash = TaashRules.IsTwoTaashMode;

        if (!is2Taash)
            _lockedTwoTaashSpacing = null;
        else
            _lockedOneTaashSpacing = null;

        float spacing;
        if (cardCount == 1)
        {
            spacing = 0f;
        }
        else
        {
            float fitSpacing = (areaWidth - cardCount * prefabCardWidth) / (cardCount - 1);
            int fullHandCount = TaashRules.CardsPerPlayer;
            spacing = ResolveSpacing(fitSpacing, prefabCardWidth, cardCount, fullHandCount, is2Taash);
        }

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

    public static float GetRowY(int row, bool twoRowHand)
    {
        if (!twoRowHand) return 0f;
        return row == 0 ? 50f : -70f;
    }

    static float ResolveSpacing(float fitSpacing, float prefabCardWidth, int cardCount, int fullHandCount, bool is2Taash)
    {
        if (is2Taash)
        {
            if (cardCount >= fullHandCount)
            {
                float resolved = ComputeHandSpacing(fitSpacing, prefabCardWidth);
                _lockedTwoTaashSpacing = resolved;
                return resolved;
            }

            if (_lockedTwoTaashSpacing.HasValue)
                return _lockedTwoTaashSpacing.Value;

            return ComputeHandSpacing(fitSpacing, prefabCardWidth);
        }

        if (cardCount >= fullHandCount)
        {
            float resolved = ComputeHandSpacing(fitSpacing, prefabCardWidth);
            _lockedOneTaashSpacing = resolved;
            return resolved;
        }

        if (_lockedOneTaashSpacing.HasValue)
            return _lockedOneTaashSpacing.Value;

        return ComputeHandSpacing(fitSpacing, prefabCardWidth);
    }

    static float ComputeHandSpacing(float fitSpacing, float prefabCardWidth)
    {
        const float spacingBoost = 30f;
        float preferredMaxSpacing = 4f;
        float maxOverlap = prefabCardWidth * 0.72f;
        float minSpacing = -maxOverlap;
        return Mathf.Clamp(fitSpacing, minSpacing, preferredMaxSpacing) + spacingBoost;
    }
}
