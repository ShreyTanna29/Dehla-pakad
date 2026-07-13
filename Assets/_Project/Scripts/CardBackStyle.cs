using System;
using UnityEngine;

/// <summary>
/// Resolves the equipped inventory card-back style (Classic red / Modern blue).
/// Selection is stored in PlayerPrefs (<c>InvSelected_Cards</c>) and optionally mirrored to
/// <see cref="CurrencyAndInventoryManager"/> under category "Cards".
/// </summary>
public static class CardBackStyle
{
    public const string Category = "Cards";
    public const string ClassicId = "card_classic";
    public const string ModernId = "card_modern";
    public const string PrefKey = "InvSelected_Cards";

    public static event Action OnChanged;

    public static string SelectedId
    {
        get
        {
            string id = PlayerPrefs.GetString(PrefKey, ClassicId);
            if (string.IsNullOrEmpty(id)) return ClassicId;
            // Legacy sample catalog id.
            if (id == "card_blue") return ModernId;
            return id;
        }
    }

    public static bool IsModernSelected => SelectedId == ModernId;

    public static void Select(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) itemId = ClassicId;
        if (itemId == "card_blue") itemId = ModernId;

        PlayerPrefs.SetString(PrefKey, itemId);
        PlayerPrefs.Save();

        if (CurrencyAndInventoryManager.Instance != null)
        {
            if (!CurrencyAndInventoryManager.Instance.HasItem(itemId))
                CurrencyAndInventoryManager.Instance.BuyItem(itemId, 0, Category);
            else
                CurrencyAndInventoryManager.Instance.EquipItem(itemId, Category);
        }

        OnChanged?.Invoke();
    }

    public static Sprite GetBackSprite()
    {
        PlayWithFriendsManager pwm = PlayWithFriendsManager.Instance;
        if (IsModernSelected)
        {
            if (pwm != null && pwm.modernCardBackSprite != null)
                return pwm.modernCardBackSprite;
        }
        else
        {
            if (pwm != null && pwm.classicCardBackSprite != null)
                return pwm.classicCardBackSprite;
        }

        if (GameManager.Instance != null && GameManager.Instance.cardBackSprite != null)
            return GameManager.Instance.cardBackSprite;

        return null;
    }
}
