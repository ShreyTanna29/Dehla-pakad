using System;
using UnityEngine;
using UnityEngine.Purchasing;

/// <summary>
/// Handles real-money coin purchases through Unity In-App Purchasing.
/// Six consumable products follow a 1 unit = 1 coin convention; the coin amount is parsed from the
/// product id suffix (e.g. <c>coins_500</c> grants 500 coins). On a successful purchase the coins are
/// credited via <see cref="CurrencyAndInventoryManager.AddCoins"/>.
///
/// Implements <see cref="IDetailedStoreListener"/> (which inherits <see cref="IStoreListener"/>) so the
/// non-obsolete failure callback is used while still providing <c>ProcessPurchase</c>.
///
/// Standalone manager: it does not modify any existing gameplay scripts.
/// </summary>
public class ShopIAPManager : MonoBehaviour, IDetailedStoreListener
{
    public static ShopIAPManager Instance { get; private set; }

    // Consumable product IDs — must match the product ids configured in the store dashboards.
    public const string PRODUCT_COINS_50 = "coins_50";
    public const string PRODUCT_COINS_100 = "coins_100";
    public const string PRODUCT_COINS_500 = "coins_500";
    public const string PRODUCT_COINS_1000 = "coins_1000";
    public const string PRODUCT_COINS_1500 = "coins_1500";
    public const string PRODUCT_COINS_2000 = "coins_2000";

    private static readonly string[] CoinProductIds =
    {
        PRODUCT_COINS_50,
        PRODUCT_COINS_100,
        PRODUCT_COINS_500,
        PRODUCT_COINS_1000,
        PRODUCT_COINS_1500,
        PRODUCT_COINS_2000
    };

    private IStoreController _controller;
    private IExtensionProvider _extensions;

    /// <summary>Raised after a successful purchase, with the product id and granted coin amount.</summary>
    public event Action<string, int> OnPurchaseSucceeded;
    /// <summary>Raised when a purchase fails, with the product id and a human-readable reason.</summary>
    public event Action<string, string> OnPurchaseFailedEvent;

    public bool IsInitialized => _controller != null && _extensions != null;

    #region Lifecycle

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (!IsInitialized)
            InitializePurchasing();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    #endregion

    #region Initialization

    private void InitializePurchasing()
    {
        var module = StandardPurchasingModule.Instance();
        var builder = ConfigurationBuilder.Instance(module);

        foreach (string id in CoinProductIds)
            builder.AddProduct(id, ProductType.Consumable);

        Debug.Log("[IAP] Initializing Unity Purchasing...");
        UnityPurchasing.Initialize(this, builder);
    }

    public void OnInitialized(IStoreController controller, IExtensionProvider extensions)
    {
        _controller = controller;
        _extensions = extensions;
        Debug.Log("[IAP] Initialized successfully. Products available: " + controller.products.all.Length);
    }

    public void OnInitializeFailed(InitializationFailureReason error)
    {
        OnInitializeFailed(error, null);
    }

    public void OnInitializeFailed(InitializationFailureReason error, string message)
    {
        Debug.LogError($"[IAP] Initialization failed: {error} {(string.IsNullOrEmpty(message) ? "" : "- " + message)}");
    }

    #endregion

    #region Purchasing

    /// <summary>Starts a purchase flow for one of the coin products.</summary>
    public void BuyCoins(string productId)
    {
        if (!IsInitialized)
        {
            Debug.LogWarning("[IAP] Not initialized yet — retrying initialization.");
            InitializePurchasing();
            return;
        }

        Product product = _controller.products.WithID(productId);
        if (product == null)
        {
            Debug.LogError("[IAP] Unknown product id: " + productId);
            OnPurchaseFailedEvent?.Invoke(productId, "Unknown product");
            return;
        }

        if (!product.availableToPurchase)
        {
            Debug.LogError("[IAP] Product not available to purchase: " + productId);
            OnPurchaseFailedEvent?.Invoke(productId, "Not available");
            return;
        }

        Debug.Log("[IAP] Initiating purchase: " + productId);
        _controller.InitiatePurchase(product);
    }

    public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs args)
    {
        string productId = args.purchasedProduct.definition.id;
        int coinAmount = ParseCoinAmount(productId);

        if (coinAmount > 0)
        {
            if (CurrencyAndInventoryManager.Instance != null)
            {
                CurrencyAndInventoryManager.Instance.AddCoins(coinAmount);
                Debug.Log($"[IAP] Purchase complete: {productId} -> +{coinAmount} coins.");
                OnPurchaseSucceeded?.Invoke(productId, coinAmount);
            }
            else
            {
                Debug.LogError("[IAP] CurrencyAndInventoryManager.Instance is null — coins not credited!");
            }
        }
        else
        {
            Debug.LogWarning("[IAP] Could not parse coin amount from product id: " + productId);
        }

        // Consumables are fulfilled immediately.
        return PurchaseProcessingResult.Complete;
    }

    // IDetailedStoreListener — preferred failure callback (non-obsolete).
    public void OnPurchaseFailed(Product product, PurchaseFailureDescription failureDescription)
    {
        string id = product != null ? product.definition.id : "unknown";
        Debug.LogError($"[IAP] Purchase failed: {id} | {failureDescription.reason} | {failureDescription.message}");
        OnPurchaseFailedEvent?.Invoke(id, failureDescription.reason.ToString());
    }

    // IStoreListener — legacy overload, required by the interface but obsolete. Kept minimal.
#pragma warning disable 618
    public void OnPurchaseFailed(Product product, PurchaseFailureReason failureReason)
    {
        string id = product != null ? product.definition.id : "unknown";
        Debug.LogError($"[IAP] Purchase failed: {id} | {failureReason}");
        OnPurchaseFailedEvent?.Invoke(id, failureReason.ToString());
    }
#pragma warning restore 618

    #endregion

    #region Helpers

    /// <summary>Parses the trailing integer from a product id (e.g. "coins_500" -> 500).</summary>
    private static int ParseCoinAmount(string productId)
    {
        if (string.IsNullOrEmpty(productId)) return 0;

        int underscore = productId.LastIndexOf('_');
        if (underscore < 0 || underscore >= productId.Length - 1) return 0;

        string suffix = productId.Substring(underscore + 1);
        return int.TryParse(suffix, out int amount) ? amount : 0;
    }

    #endregion
}