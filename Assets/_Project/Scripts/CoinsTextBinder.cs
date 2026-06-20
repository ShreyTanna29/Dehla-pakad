using UnityEngine;
using TMPro;

/// <summary>
/// Binds a <see cref="TMP_Text"/> to the live coin balance held by
/// <see cref="CurrencyAndInventoryManager"/>. Drop this on any label that should show coins
/// (Home stats, Player Profile, etc.). It subscribes to <c>OnCoinsChanged</c> and refreshes in
/// real time, so buying coins or spending them updates every bound label at once.
///
/// Resilient to initialization order: the currency manager lives on a DontDestroyOnLoad bootstrap
/// object that may be created after this label, so the binder keeps trying to subscribe until the
/// singleton exists.
///
/// Standalone helper — it does not modify any existing gameplay scripts.
/// </summary>
[DisallowMultipleComponent]
public class CoinsTextBinder : MonoBehaviour
{
    [Tooltip("Label to drive. Defaults to a TMP_Text on this GameObject.")]
    [SerializeField] private TMP_Text label;

    [Tooltip("Display format. Use {0} as the coin amount placeholder, e.g. \"Coins : {0}\".")]
    [SerializeField] private string format = "{0}";

    private bool _subscribed;

    private void Reset()
    {
        label = GetComponent<TMP_Text>();
    }

    private void OnEnable()
    {
        if (label == null) label = GetComponent<TMP_Text>();
        TrySubscribe();
        Refresh();
    }

    private void OnDisable() => Unsubscribe();
    private void OnDestroy() => Unsubscribe();

    private void Update()
    {
        // The manager may be created after this label; keep trying until it exists.
        if (!_subscribed)
        {
            TrySubscribe();
            if (_subscribed) Refresh();
        }
    }

    private void TrySubscribe()
    {
        var c = CurrencyAndInventoryManager.Instance;
        if (c == null || _subscribed) return;
        c.OnCoinsChanged += Refresh;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        var c = CurrencyAndInventoryManager.Instance;
        if (c != null) c.OnCoinsChanged -= Refresh;
        _subscribed = false;
    }

    private void Refresh()
    {
        if (label == null) return;
        int coins = CurrencyAndInventoryManager.Instance != null
            ? CurrencyAndInventoryManager.Instance.Coins
            : 0;
        label.text = string.Format(format, coins);
    }
}
