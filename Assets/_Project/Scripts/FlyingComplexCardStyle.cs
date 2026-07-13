using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// On <c>FlyingComplexCard</c>: shows ClassicCard (red) or ModernCard (blue) based on
/// the inventory card-back selection from <see cref="CardBackStyle"/>.
/// </summary>
[DisallowMultipleComponent]
public class FlyingComplexCardStyle : MonoBehaviour
{
    [SerializeField] GameObject classicCard;
    [SerializeField] GameObject modernCard;

    void Awake()
    {
        AutoWire();
    }

    void OnEnable()
    {
        ApplySelectedStyle();
        CardBackStyle.OnChanged += ApplySelectedStyle;
    }

    void OnDisable()
    {
        CardBackStyle.OnChanged -= ApplySelectedStyle;
    }

    void AutoWire()
    {
        if (classicCard == null)
        {
            Transform t = transform.Find("ClassicCard");
            if (t != null) classicCard = t.gameObject;
        }

        if (modernCard == null)
        {
            Transform t = transform.Find("ModernCard");
            if (t != null) modernCard = t.gameObject;
        }
    }

    public void ApplySelectedStyle()
    {
        AutoWire();
        bool modern = CardBackStyle.IsModernSelected;

        if (classicCard != null) classicCard.SetActive(!modern);
        if (modernCard != null) modernCard.SetActive(modern);

        // Keep a root Image (if any) in sync for code that samples GetComponent<Image>().
        Image rootImage = GetComponent<Image>();
        Sprite back = CardBackStyle.GetBackSprite();
        if (rootImage != null && back != null)
            rootImage.sprite = back;
    }
}
