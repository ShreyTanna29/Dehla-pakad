using UnityEngine;

public struct HandLayoutConfig
{
    public float prefabCardWidth;
    public float spacing;
}

public static class HandLayoutHelper
{
    public static void ResetTwoTaashSpacingCache() { }

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

        int fullHandCount = TaashRules.CardsPerPlayer;
        float simulatedFitSpacing = (areaWidth - fullHandCount * prefabCardWidth) / (fullHandCount - 1);

        float spacing;
        if (cardCount == 1)
        {
            spacing = 0f;
        }
        else
        {
            float maxOverlap = -prefabCardWidth * 0.75f;
            float minOverlap = -prefabCardWidth * 0.35f;

            spacing = Mathf.Clamp(simulatedFitSpacing, maxOverlap, minOverlap);
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
}
