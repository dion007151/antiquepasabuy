using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;
using AntiquePasabuyPOS.Views.Dialogs;
using Microsoft.EntityFrameworkCore;

namespace AntiquePasabuyPOS.Views.Pages;

// ─────────────────────────────────────────────────────────────────────────────
// VIEW MODELS
// ─────────────────────────────────────────────────────────────────────────────

public class OrderItemEntry : INotifyPropertyChanged
{
    private double _qty = 1;
    private decimal _price;

    public string Name  { get; set; } = "";
    public string Store { get; set; } = "SM";
    public bool HasStore => !string.IsNullOrWhiteSpace(Store);
    public string Unit  { get; set; } = "pc";
    public decimal? CostPrice { get; set; }
    public decimal? ActualPrice { get; set; }
    public bool IsFeeExempt { get; set; } = false;  // kept for backward compat
    public ProductFeeType FeeType { get; set; } = ProductFeeType.STANDARD;
    public decimal? ManualFeeAmount { get; set; }

    public bool IsStockItem { get; set; } = false;
    private double? _stockOnHand;
    public double? StockOnHand
    {
        get => _stockOnHand;
        set
        {
            if (_stockOnHand == value) return;
            _stockOnHand = value;
            OnPropertyChanged(nameof(StockOnHand));
            OnPropertyChanged(nameof(StockOnHandDisplay));
            OnPropertyChanged(nameof(HasStockWarning));
            OnPropertyChanged(nameof(StockWarningText));
        }
    }

    public string StockOnHandDisplay => IsStockItem && StockOnHand.HasValue ? $"{StockOnHand.Value:0.##} on hand" : "";

    public bool HasStockWarning => IsStockItem && StockOnHand.HasValue && Qty > StockOnHand.Value;
    public string StockWarningText => HasStockWarning ? $"Only {StockOnHand!.Value:0.##} on hand; remaining will need to be bought" : "";

    public decimal Price
    {
        get => _price;
        set
        {
            if (_price == value) return;
            _price = value;
            OnPropertyChanged(nameof(Price));
            OnPropertyChanged(nameof(Subtotal));
        }
    }

    private string? _notes;
    public string? Notes
    {
        get => _notes;
        set
        {
            if (_notes == value) return;
            _notes = value;
            OnPropertyChanged(nameof(Notes));
            OnPropertyChanged(nameof(HasNotes));
        }
    }
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);

    public double Qty
    {
        get => _qty;
        set
        {
            if (_qty == value) return;
            _qty = value;
            OnPropertyChanged(nameof(Qty));
            OnPropertyChanged(nameof(Subtotal));
            OnPropertyChanged(nameof(HasStockWarning));
            OnPropertyChanged(nameof(StockWarningText));
        }
    }

    public decimal Subtotal => Price * (decimal)Qty;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string prop) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}

public class ProductTileVm : INotifyPropertyChanged
{
    public Product Product  { get; }
    public int     Id       => Product.Id;
    public string  Name     => Product.Name;
    public string  Category => string.IsNullOrWhiteSpace(Product.Category) ? "General" : Product.Category;
    public string  Store    => string.IsNullOrWhiteSpace(Product.Store)    ? "General" : Product.Store;
    public decimal Price    => Product.Price;
    public string  Unit     => string.IsNullOrWhiteSpace(Product.Unit)     ? "pc"      : Product.Unit;
    public bool    IsActive => Product.IsActive;

    public bool   IsStockItem   => Product.IsStockItem;
    public double StockQuantity => Product.StockQuantity;
    public string StockDisplay  => IsStockItem ? $"{StockQuantity:0.##} on hand" : "";

    private ImageSource? _imageSource;
    private bool _imageLoaded = false;
    public ImageSource? ImageSource
    {
        get
        {
            if (!_imageLoaded)
            {
                _imageLoaded = true;
                _imageSource = ProductImageService.LoadBitmap(Product.ImagePath, 140);
            }
            return _imageSource;
        }
    }

    public bool HasImage   => !string.IsNullOrWhiteSpace(Product.ImagePath) && ImageSource != null;
    public bool HasNoImage => !HasImage;

    private int _cartQty;
    public int CartQty
    {
        get => _cartQty;
        set
        {
            if (_cartQty == value) return;
            _cartQty = value;
            OnPropertyChanged(nameof(CartQty));
            OnPropertyChanged(nameof(IsInCart));
            OnPropertyChanged(nameof(CartQtyDisplay));
            OnPropertyChanged(nameof(CartQtyBadge));
        }
    }

    public bool   IsInCart      => _cartQty > 0;
    public string CartQtyDisplay => $"[ {_cartQty} ]";
    public string CartQtyBadge   => $"{_cartQty}×";

    public ProductTileVm(Product product) => Product = product;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string prop) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}

// ─────────────────────────────────────────────────────────────────────────────
// PAGE
// ─────────────────────────────────────────────────────────────────────────────

public partial class NewOrderPage : Page
{
    private readonly AppDbContext  _db;
    private readonly User          _user;
    private readonly Action?       _onSuccess;

    private readonly ObservableCollection<OrderItemEntry> _items = [];
    private List<Product>      _allProducts  = [];
    private List<ProductTileVm> _productTiles = [];

    private string         _activeCategory       = "All";
    private bool           _showNotes            = false;
    private bool           _isRecalculating      = false;
    private bool           _isSubmittingOrder    = false;
    // Phase 2: last-used store for smart item defaults
    private string         _lastUsedStore        = "";
    private int?           _existingTodayPoId    = null;
    private string?        _existingTodayPoNo    = null;
    private bool           _isFeeManual          = false;  // true = user manually set the pasabuy fee
    private decimal?       _manualFeeOverride    = null;

    // ── Performance: cache fee tiers so DB is never hit during live recalc ──
    private List<AntiquePasabuyPOS.Models.PasabuyRateTier>? _cachedFeeTiers = null;

    /// <summary>Returns fee tiers from in-memory cache. Never hits DB after first call.</summary>
    private decimal CalcFee(decimal subtotal)
    {
        if (!BusinessProfileService.IsPasabuyFeeEnabled(_db)) return 0m;
        if (subtotal <= 0m) return 0m;
        _cachedFeeTiers ??= PasabuyFeeService.GetRateTiers(_db);  // loads once, reused every time
        return PasabuyFeeService.CalculateServiceFee(subtotal, null) is var _ // use cached path below
            ? CalculateFeeFromCachedTiers(subtotal)
            : 0m;
    }

    private decimal CalculateFeeFromCachedTiers(decimal subtotal)
    {
        if (_cachedFeeTiers == null || _cachedFeeTiers.Count == 0)
            return PasabuyFeeService.CalculateServiceFee(subtotal, null);
        var tiers = _cachedFeeTiers;
        for (int i = 0; i < tiers.Count; i++)
        {
            var tier = tiers[i];
            if (subtotal < tier.MinAmount) continue;
            bool matchesMax;
            if (i < tiers.Count - 1 && tiers[i + 1].MinAmount > tier.MinAmount)
                matchesMax = subtotal < tiers[i + 1].MinAmount;
            else if (tier.MaxAmount.HasValue)
            {
                decimal maxBoundary = tier.MaxAmount.Value % 1 == 0 ? tier.MaxAmount.Value + 0.9999m : tier.MaxAmount.Value;
                matchesMax = subtotal <= maxBoundary;
            }
            else
                matchesMax = true;
            if (!matchesMax) continue;
            if (tier.FixedFee.HasValue) return tier.FixedFee.Value;
            if (tier.PercentageFee.HasValue) return Math.Round(subtotal * tier.PercentageFee.Value, 2, MidpointRounding.AwayFromZero);
        }
        // Hardcoded safety fallback
        if (subtotal < 100m) return 50m;
        if (subtotal < 200m) return 75m;
        if (subtotal < 300m) return 100m;
        if (subtotal < 1000m) return 150m;
        if (subtotal < 1500m) return 250m;
        if (subtotal < 2000m) return 300m;
        if (subtotal < 2500m) return 350m;
        return Math.Round(subtotal * 0.15m, 2, MidpointRounding.AwayFromZero);
    }

    // Debounce timer — Change Due only updates 400ms after the user stops typing
    private readonly System.Windows.Threading.DispatcherTimer _changeDebounce = new()
    {
        Interval = TimeSpan.FromMilliseconds(400)
    };

    // ── Constructor ──────────────────────────────────────────────────────────
    public NewOrderPage(AppDbContext db, User user, Action<string>? navigate = null, Action? onSuccess = null)
    {
        InitializeComponent();
        _db        = db;
        _user      = user;
        _onSuccess = onSuccess;

        CartItemsControl.ItemsSource = _items;
        _items.CollectionChanged    += OnItemsCollectionChanged;

        // Wire debounce timer — fires once after 400ms of no input
        _changeDebounce.Tick += (_, _) =>
        {
            _changeDebounce.Stop();
            RecalcChangeDue();
        };

        Loaded += OnPageLoaded;
        RecalcTotal();
    }

    // ── Page Loaded ──────────────────────────────────────────────────────────

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        InitSchedule();
        AdjustProductColumns(ProductsBorder?.ActualWidth ?? 0);

        if (PasabuyFeeRow != null)
            PasabuyFeeRow.Visibility = BusinessProfileService.IsPasabuyFeeEnabled(_db) ? Visibility.Visible : Visibility.Collapsed;

        // ── Run all three DB operations in parallel — cuts startup from ~3× to ~1× load time ──
        List<AntiquePasabuyPOS.Models.Customer> customers = [];
        List<Product> products = [];
        var bizId = BusinessProfileService.ANTIQUE_PASABUY;

        await Task.WhenAll(
            // 1. Pre-cache fee tiers
            Task.Run(() =>
            {
                using var bgDb = new AntiquePasabuyPOS.Data.AppDbContext();
                _cachedFeeTiers = PasabuyFeeService.GetRateTiers(bgDb);
            }),
            // 2. Load customers
            Task.Run(() =>
            {
                using var bgDb = new AntiquePasabuyPOS.Data.AppDbContext();
                customers = new CustomerService(bgDb).GetAll(activeOnly: true, businessId: bizId);
            }),
            // 3. Load products
            Task.Run(() =>
            {
                using var bgDb = new AntiquePasabuyPOS.Data.AppDbContext();
                products = bgDb.Products.Where(p => p.IsActive).OrderBy(p => p.Name).ToList();
            })
        );

        // Apply all results to UI (already on UI thread after await)
        CustomerCombo.ItemsSource = customers;
        _allProducts  = products;
        _productTiles = products.Select(p => new ProductTileVm(p)).ToList();
        UpdateProductTileQuantities();

        BuildCategoryPills();
        ApplyProductFilter();
        InitQuickEntryStores();

        // Auto-focus the Rapid Item Entry box so encoder can immediately type without touching mouse
        QuickItemNameBox?.Focus();
    }

    // ── Data Loading ─────────────────────────────────────────────────────────

    private bool _isPayNow = false;

    private void InitSchedule()
    {
        // Buying Day dropdown
        var scheduleSetting = SeedData.GetSetting(_db, "buying_schedule_days", "Wednesday, Thursday");
        var days = scheduleSetting
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        BuyingDayCombo.ItemsSource = days;

        var nextDay  = OrderService.ComputeNextBuyingDay();
        var matchDay = days.FirstOrDefault(d => d.Equals(nextDay, StringComparison.OrdinalIgnoreCase))
                       ?? days.FirstOrDefault();
        BuyingDayCombo.SelectedItem = matchDay;

        // Fulfillment & Status defaults
        if (FulfillmentCombo != null)
        {
            FulfillmentCombo.ItemsSource = new[] { "Pickup", "Delivery" };
            FulfillmentCombo.SelectedIndex = 0;
        }

        if (PaymentMethodCombo != null)
        {
            PaymentMethodCombo.ItemsSource = new[] { "Cash / On Hand", "GCash", "Maya", "Bank Transfer", "COD (Cash on Delivery)" };
            PaymentMethodCombo.SelectedIndex = 0;
        }

        UpdatePayOptionButtons();
        // Always in Pay Now mode — payment panel is always visible
        _isPayNow = true;
        if (PayNowPanel != null) PayNowPanel.Visibility = System.Windows.Visibility.Collapsed; // hidden dummy

        // Always clear payment fields on new order — never carry over from previous session
        if (TenderedAmountBox != null) TenderedAmountBox.Text = string.Empty;
        if (PaymentRefBox    != null) PaymentRefBox.Text    = string.Empty;

        RecalcChangeDue();
    }

    private async Task LoadCustomersAsync()
    {
        List<AntiquePasabuyPOS.Models.Customer> customers = [];
        var bizId = BusinessProfileService.ANTIQUE_PASABUY;
        await Task.Run(() =>
        {
            using var bgDb = new AntiquePasabuyPOS.Data.AppDbContext();
            customers = new CustomerService(bgDb).GetAll(activeOnly: true, businessId: bizId);
        });
        CustomerCombo.ItemsSource = customers;
    }

    private void LoadCustomers()
    {
        CustomerCombo.ItemsSource = new CustomerService(_db).GetAll(activeOnly: true, businessId: BusinessProfileService.ANTIQUE_PASABUY);
    }

    private async Task LoadProductsAsync()
    {
        List<Product> products = [];
        await Task.Run(() =>
        {
            using var bgDb = new AntiquePasabuyPOS.Data.AppDbContext();
            products = bgDb.Products.Where(p => p.IsActive).OrderBy(p => p.Name).ToList();
        });
        _allProducts  = products;
        _productTiles = products.Select(p => new ProductTileVm(p)).ToList();
        UpdateProductTileQuantities();
    }

    private void LoadProducts()
    {
        _allProducts  = _db.Products.Where(p => p.IsActive).OrderBy(p => p.Name).ToList();
        _productTiles = _allProducts.Select(p => new ProductTileVm(p)).ToList();
        UpdateProductTileQuantities();
    }

    // ── Category Pills ───────────────────────────────────────────────────────

    private void BuildCategoryPills()
    {
        CategoryPanel.Children.Clear();
        var cats = new List<string> { "All" };
        var uniqueCats = _allProducts
            .Select(p => p.Category?.Trim())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => string.Equals(c, "foods", StringComparison.OrdinalIgnoreCase) ? "Food" : c!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c);
        cats.AddRange(uniqueCats);

        foreach (var cat in cats)
        {
            var btn = new Button
            {
                Content = cat,
                Tag     = cat,
                Style   = (Style)FindResource("CategoryPillStyle"),
            };
            btn.Click += OnCategoryPillClicked;
            NavProps.SetIsActive(btn, cat == _activeCategory);
            CategoryPanel.Children.Add(btn);
        }
    }

    private void OnCategoryPillClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string cat) return;
        _activeCategory = cat;
        foreach (Button child in CategoryPanel.Children)
            NavProps.SetIsActive(child, (child.Tag as string) == _activeCategory);
        ApplyProductFilter();
    }

    // ── Product Search ───────────────────────────────────────────────────────

    private void OnProductSearchChanged(object sender, TextChangedEventArgs e)
    {
        // Show/hide the clear (✕) button reactively
        ClearSearchBtn.Visibility = string.IsNullOrEmpty(ProductSearchBox.Text)
            ? Visibility.Collapsed
            : Visibility.Visible;
        ApplyProductFilter();
    }

    /// <summary>ESC in the search box clears it instantly.</summary>
    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ProductSearchBox.Text = "";
            e.Handled = true;
        }
    }

    private void OnClearProductSearch(object sender, RoutedEventArgs e)
    {
        ProductSearchBox.Text = "";
        ProductSearchBox.Focus();
    }

    private void ApplyProductFilter()
    {
        if (ProductsItemsControl is null) return;
        var term     = ProductSearchBox.Text?.Trim().ToLower() ?? "";
        var filtered = _productTiles.AsEnumerable();

        if (_activeCategory != "All")
            filtered = filtered.Where(p =>
                string.Equals(p.Category, _activeCategory, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(term))
            filtered = filtered.Where(p =>
                p.Name.ToLower().Contains(term) ||
                p.Category.ToLower().Contains(term));

        ProductsItemsControl.ItemsSource = filtered.ToList();
    }

    private void UpdateProductTileQuantities()
    {
        if (_productTiles is null) return;
        foreach (var tile in _productTiles)
        {
            var cartItem = _items.FirstOrDefault(i =>
                string.Equals(i.Name, tile.Name, StringComparison.OrdinalIgnoreCase));
            tile.CartQty = cartItem is not null ? (int)cartItem.Qty : 0;
        }
    }

    // ── Product Tile Click ───────────────────────────────────────────────────

    private void OnProductCardClicked(object sender, RoutedEventArgs e)
    {
        Product? prod = null;
        if (sender is Button btn)
        {
            if (btn.Tag is ProductTileVm vm) prod = vm.Product;
            else if (btn.Tag is Product p)   prod = p;
        }

        if (prod is null || !prod.IsActive) return;

        var existing = _items.FirstOrDefault(i =>
            string.Equals(i.Name, prod.Name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Qty += 1;   // PropertyChanged fires → OnItemPropertyChanged → RecalcTotal
        }
        else
        {
            var store = prod.IsStockItem ? "On hand" : (!string.IsNullOrWhiteSpace(prod.Store) ? prod.Store : "General");
            _items.Add(new OrderItemEntry
            {
                Name  = prod.Name,
                Qty   = 1,
                Unit  = !string.IsNullOrWhiteSpace(prod.Unit)  ? prod.Unit  : "pc",
                Price = prod.Price,
                Store = store,
                CostPrice = prod.CostPrice,
                FeeType = prod.FeeType,
                ManualFeeAmount = prod.ManualFeeAmount,
                IsFeeExempt = prod.FeeType == ProductFeeType.NONE || prod.IsFeeExempt,
                IsStockItem = prod.IsStockItem,
                StockOnHand = prod.StockQuantity
            });
            // Sync store label
            if (!string.IsNullOrWhiteSpace(store)) SyncPrimaryStoreLabel(store);
        }
    }

    // ── Barcode Scanner ───────────────────────────────────────────────────────

    private void OnScanBarcode(object sender, RoutedEventArgs e)
    {
        var dlg = new BarcodeScannerDialog(_db, OnBarcodeProductScanned)
        {
            Owner = Window.GetWindow(this)
        };
        dlg.ShowDialog();
    }

    private void OnBarcodeProductScanned(Product prod)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => OnBarcodeProductScanned(prod));
            return;
        }

        if (prod is null) return;

        var store = prod.IsStockItem ? "On hand" : (!string.IsNullOrWhiteSpace(prod.Store) ? prod.Store : "General");
        if (!string.IsNullOrWhiteSpace(store)) _lastUsedStore = store;

        var existing = _items.FirstOrDefault(i =>
            string.Equals(i.Name, prod.Name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Qty += 1;
        }
        else
        {
            _items.Add(new OrderItemEntry
            {
                Name  = prod.Name,
                Qty   = 1,
                Unit  = !string.IsNullOrWhiteSpace(prod.Unit)  ? prod.Unit  : "pc",
                Price = prod.Price,
                Store = store,
                CostPrice = prod.CostPrice,
                FeeType = prod.FeeType,
                ManualFeeAmount = prod.ManualFeeAmount,
                IsFeeExempt = prod.FeeType == ProductFeeType.NONE || prod.IsFeeExempt,
                IsStockItem = prod.IsStockItem,
                StockOnHand = prod.StockQuantity
            });
        }
    }

    // ── Cart Qty Controls ────────────────────────────────────────────────────

    private void OnIncreaseQty(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is OrderItemEntry item)
            item.Qty += 1;   // auto-recalc via PropertyChanged
    }

    private void OnDecreaseQty(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is OrderItemEntry item)
        {
            if (item.Qty > 1)
                item.Qty -= 1;   // auto-recalc via PropertyChanged
            else
                _items.Remove(item);   // auto-recalc via CollectionChanged
        }
    }

    private void OnRemoveCartItem(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is OrderItemEntry item)
            _items.Remove(item);
    }

    private void OnClearCart(object sender, RoutedEventArgs e)
    {
        if (_items.Count == 0) return;
        if (MessageBox.Show(
            "Clear all items from the purchase order ticket?", "Clear Purchase Order",
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _items.Clear();
            if (NotesBox != null) NotesBox.Text = "";
            RecalcTotal();
        }
    }

    // ── Rapid Keyboard Row Entry ───────────────────────────────────────────

    private void InitQuickEntryStores()
    {
        if (QuickStoreCombo == null) return;
        var stores = _db.Stores.Where(s => s.IsActive).OrderBy(s => s.Name).Select(s => s.Name).ToList();
        if (stores.Count == 0) stores = new List<string> { "SM", "Robinsons", "S&R", "Mercury Drug", "General" };
        if (!stores.Contains("On hand")) stores.Insert(0, "On hand");
        QuickStoreCombo.ItemsSource = stores;
        var defaultStore = !string.IsNullOrWhiteSpace(_lastUsedStore) && stores.Contains(_lastUsedStore)
            ? _lastUsedStore
            : stores.FirstOrDefault(s => s != "On hand") ?? "SM";
        QuickStoreCombo.SelectedItem = defaultStore;
        SyncPrimaryStoreLabel(defaultStore);
    }

    /// <summary>Syncs the primary store selection with the Quick Entry store.</summary>
    private void SyncPrimaryStoreLabel(string store)
    {
        _lastUsedStore = store;
        // Keep QuickStoreCombo in sync if called externally
        if (QuickStoreCombo != null && QuickStoreCombo.SelectedItem as string != store)
            QuickStoreCombo.SelectedItem = store;
    }

    private void OnQuickItemKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            SubmitQuickRow();
        }
    }

    private void OnQuickAddRowClick(object sender, RoutedEventArgs e)
    {
        SubmitQuickRow();
    }

    private void SubmitQuickRow()
    {
        var itemName = QuickItemNameBox?.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(itemName))
        {
            QuickItemNameBox?.Focus();
            return;
        }

        var store = QuickStoreCombo?.Text?.Trim() ?? QuickStoreCombo?.SelectedItem as string ?? "SM";
        if (string.IsNullOrWhiteSpace(store)) store = "SM";
        SyncPrimaryStoreLabel(store);

        var priceStr = QuickPriceBox?.Text?.Trim() ?? "0";
        if (!decimal.TryParse(priceStr, out var price) || price < 0)
        {
            MessageBox.Show("Please enter a valid price.", "Invalid Price", MessageBoxButton.OK, MessageBoxImage.Warning);
            QuickPriceBox?.Focus();
            QuickPriceBox?.SelectAll();
            return;
        }

        var qtyStr = QuickQtyBox?.Text?.Trim() ?? "1";
        if (!double.TryParse(qtyStr, out var qty) || qty <= 0)
        {
            qty = 1;
        }

        var matchedProd = _allProducts?.FirstOrDefault(p => string.Equals(p.Name, itemName, StringComparison.OrdinalIgnoreCase));
        var isStock = matchedProd?.IsStockItem ?? false;
        var stockOnHand = matchedProd?.StockQuantity;

        if (isStock && (string.IsNullOrWhiteSpace(store) || store == "SM" || store == "General"))
        {
            store = "On hand";
        }
        SyncPrimaryStoreLabel(store);

        var matchedCost = matchedProd?.CostPrice;

        var matchedFeeType = matchedProd?.FeeType ?? ProductFeeType.STANDARD;
        var matchedManualFee = matchedProd?.ManualFeeAmount;

        // Add directly to ticket items collection (triggers automatic fee recalc & ticket refresh)
        _items.Add(new OrderItemEntry
        {
            Name = itemName,
            Store = store,
            Price = price,
            Qty = qty,
            Unit = "pc",
            CostPrice = matchedCost,
            FeeType = matchedFeeType,
            ManualFeeAmount = matchedManualFee,
            IsFeeExempt = matchedFeeType == ProductFeeType.NONE || (matchedProd?.IsFeeExempt ?? false),
            IsStockItem = isStock,
            StockOnHand = stockOnHand
        });

        // Reset inputs for next rapid keyboard entry
        if (QuickItemNameBox != null) QuickItemNameBox.Text = string.Empty;
        if (QuickPriceBox != null) QuickPriceBox.Text = string.Empty;
        if (QuickQtyBox != null) QuickQtyBox.Text = "1";
        if (QuickStockHint != null) QuickStockHint.Visibility = Visibility.Collapsed;

        // Keep cursor on Item Name box so encoder never touches mouse
        QuickItemNameBox?.Focus();
    }

    private void OnQuickItemNameChanged(object sender, TextChangedEventArgs e)
    {
        var text = QuickItemNameBox?.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(text))
        {
            if (QuickStockHint != null) QuickStockHint.Visibility = Visibility.Collapsed;
            return;
        }

        var match = _allProducts?.FirstOrDefault(p => string.Equals(p.Name, text, StringComparison.OrdinalIgnoreCase));
        if (match != null && match.IsStockItem)
        {
            if (QuickStockHint != null)
            {
                QuickStockHint.Text = $"[{match.StockQuantity:0.##} on hand]";
                QuickStockHint.Visibility = Visibility.Visible;
            }
            if (QuickStoreCombo != null && (QuickStoreCombo.SelectedItem as string == "SM" || string.IsNullOrWhiteSpace(QuickStoreCombo.Text)))
            {
                QuickStoreCombo.SelectedItem = "On hand";
            }
            if (string.IsNullOrWhiteSpace(QuickPriceBox?.Text) || QuickPriceBox?.Text == "0" || QuickPriceBox?.Text == "0.00")
            {
                if (QuickPriceBox != null) QuickPriceBox.Text = match.Price.ToString("N2");
            }
        }
        else
        {
            if (QuickStockHint != null) QuickStockHint.Visibility = Visibility.Collapsed;
        }
    }

    // ── Custom / Manual Item ─────────────────────────────────────────────────

    private void OnAddCustomItem(object sender, RoutedEventArgs e)
    {
        var stores = _db.Stores.Where(s => s.IsActive).OrderBy(s => s.Name).Select(s => s.Name).ToList();
        // Phase 2: pre-fill last-used store so cashier doesn't have to select it every time
        var dlg = new Dialogs.AddItemDialog(stores, defaultStore: _lastUsedStore) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true && dlg.Result is not null)
        {
            // Track last-used store from custom item entry
            if (!string.IsNullOrWhiteSpace(dlg.Result.Store))
            {
                _lastUsedStore = dlg.Result.Store;
                if (QuickStoreCombo != null) QuickStoreCombo.SelectedItem = _lastUsedStore;
            }
            _items.Add(dlg.Result);   // auto-recalc via CollectionChanged
        }
    }

    // ── New Customer ─────────────────────────────────────────────────────────

    private void OnAddNewCustomer(object sender, RoutedEventArgs e)
    {
        var dlg = new Dialogs.CustomerDialog(_db) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
        {
            LoadCustomers();
            var latest = _db.Customers.OrderByDescending(c => c.Id).FirstOrDefault();
            if (latest is not null) CustomerCombo.SelectedValue = latest.Id;
        }
    }

    private async void OnCustomerSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ClearCustomerBtn != null)
            ClearCustomerBtn.Visibility = CustomerCombo.SelectedItem != null
                ? Visibility.Visible
                : Visibility.Collapsed;

        // Phase 2: Soft duplicate PO notice — check if customer already has PO today
        _existingTodayPoId = null;
        _existingTodayPoNo = null;

        if (DuplicatePoNotice != null)
        {
            var custId = CustomerCombo.SelectedValue is int cid ? cid : (int?)null;
            if (custId.HasValue)
            {
                // Run DB check on background thread — never block UI on customer select
                var cidVal = custId.Value;
                var todayPo = await Task.Run(() =>
                {
                    using var bgDb = new AntiquePasabuyPOS.Data.AppDbContext();
                    return bgDb.Orders
                        .Where(o => o.CustomerId == cidVal
                                 && o.CreatedAt.Date == DateTime.Today
                                 && o.Status != OrderStatus.CANCELLED)
                        .OrderByDescending(o => o.CreatedAt)
                        .Select(o => new { o.Id, o.OrderNo, o.Status })
                        .FirstOrDefault();
                });

                if (todayPo != null)
                {
                    _existingTodayPoId = todayPo.Id;
                    _existingTodayPoNo = todayPo.OrderNo;

                    DuplicatePoNotice.Visibility = Visibility.Visible;
                    if (DuplicatePoText != null)
                        DuplicatePoText.Text = $"Customer already has PO {todayPo.OrderNo} today ({todayPo.Status}).";
                    if (AppendToExistingPoBtn != null)
                        AppendToExistingPoBtn.Content = $"Append to {todayPo.OrderNo}";
                    return;
                }
            }
            DuplicatePoNotice.Visibility = Visibility.Collapsed;
        }
    }

    private void OnAppendToExistingPoClick(object sender, RoutedEventArgs e)
    {
        if (!_existingTodayPoId.HasValue)
        {
            MessageBox.Show("No existing PO found for this customer today.", "Append Order", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_items.Count == 0)
        {
            MessageBox.Show("Please add items to append to the customer's existing PO.", "Empty Cart", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show(
            $"Are you sure you want to append these {_items.Count} item(s) to existing {_existingTodayPoNo}?\n\nThis will merge into their order today and automatically recalculate their total and tiered Pasabuy fee.",
            "Confirm Append",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            var existingOrder = _db.Orders
                .Include(o => o.Items)
                .Include(o => o.Payments)
                .FirstOrDefault(o => o.Id == _existingTodayPoId.Value);

            if (existingOrder == null)
            {
                MessageBox.Show("The existing PO could not be found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            foreach (var item in _items)
            {
                if (string.IsNullOrWhiteSpace(item.Name) || item.Name.Trim() == "-" || item.Qty <= 0) continue;
                var cost = item.CostPrice ?? item.ActualPrice;
                if (!cost.HasValue)
                {
                    var pMatch = _db.Products.AsNoTracking().FirstOrDefault(p => p.Name.ToLower() == item.Name.Trim().ToLower());
                    cost = pMatch?.CostPrice;
                }
                existingOrder.Items.Add(new OrderItem
                {
                    OrderId        = existingOrder.Id,
                    ProductName    = item.Name,
                    Qty            = item.Qty,
                    Unit           = item.Unit,
                    EstimatedPrice = item.Price,
                    ActualPrice    = item.ActualPrice,
                    CostPrice      = cost,
                    Store          = string.IsNullOrWhiteSpace(item.Store) ? (existingOrder.PreferredStore ?? "General") : item.Store,
                    Notes          = item.Notes,
                    Status         = ItemStatus.Pending,
                    Source         = ItemSource.STORE
                });
            }

            // Recalculate tiered fee based on the newly expanded subtotal
            if (BusinessProfileService.IsPasabuyFeeEnabled(_db))
            {
                existingOrder.ServiceFee = PasabuyFeeService.CalculateServiceFee(existingOrder.ItemsSubtotal, _db);
            }
            else
            {
                existingOrder.ServiceFee = 0m;
            }
            existingOrder.UpdatedAt = DateTime.Now;

            _db.SaveChanges();
            OrderEventBus.NotifyDataRefreshed();

            MessageBox.Show(
                $"Successfully appended {_items.Count} item(s) to {existingOrder.OrderNo}!\n" +
                $"New Subtotal: ₱{existingOrder.ItemsSubtotal:N2}\n" +
                $"New Pasabuy Fee: ₱{existingOrder.ServiceFee:N2}\n" +
                $"New Grand Total: ₱{existingOrder.GrandTotal:N2}",
                "Appended Successfully",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            // Reset current cart
            _items.Clear();
            if (NotesBox != null) NotesBox.Text = string.Empty;
            _onSuccess?.Invoke();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to append items: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnClearCustomerSelection(object sender, RoutedEventArgs e)
    {
        CustomerCombo.SelectedItem = null;
        if (ClearCustomerBtn != null)
            ClearCustomerBtn.Visibility = Visibility.Collapsed;
    }

    // ── Notes Toggle ─────────────────────────────────────────────────────────

    private void OnToggleNotes(object sender, RoutedEventArgs e)
    {
        _showNotes            = !_showNotes;
        NotesBox.Visibility   = _showNotes ? Visibility.Visible  : Visibility.Collapsed;
        AddNoteBtn.Content    = _showNotes ? "− Hide note"       : "+ Add PO notes / special instructions";
        if (_showNotes) NotesBox.Focus();
    }

    // ── Item Property Change Subscriptions ──────────────────────────────────

    private void OnItemsCollectionChanged(
        object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // Unsubscribe removed items to avoid memory leaks
        if (e.OldItems != null)
            foreach (OrderItemEntry item in e.OldItems)
                item.PropertyChanged -= OnItemPropertyChanged;

        // Subscribe new items so Qty/Price changes auto-trigger recalc
        if (e.NewItems != null)
            foreach (OrderItemEntry item in e.NewItems)
                item.PropertyChanged += OnItemPropertyChanged;

        RecalcTotal();
    }

    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OrderItemEntry.Qty)
                           or nameof(OrderItemEntry.Price)
                           or nameof(OrderItemEntry.Subtotal))
        {
            RecalcTotal();
            // Efficiently sync only the one tile whose qty changed (avoids full-list loop)
            if (e.PropertyName == nameof(OrderItemEntry.Qty) && sender is OrderItemEntry changedItem)
            {
                var tile = _productTiles?.FirstOrDefault(t =>
                    string.Equals(t.Name, changedItem.Name, StringComparison.OrdinalIgnoreCase));
                if (tile != null)
                    tile.CartQty = (int)changedItem.Qty;
            }
        }
    }

    // ── Pasabuy Fee Override ─────────────────────────────────────────────────

    private void OnToggleFeeOverride(object sender, RoutedEventArgs e)
    {
        _isFeeManual = !_isFeeManual;
        if (FeeModeBadgeText != null)
            FeeModeBadgeText.Text = _isFeeManual ? "MANUAL" : "AUTO";
        if (FeeModeBadge != null)
            FeeModeBadge.Background = _isFeeManual
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(254, 243, 199))  // amber
                : (System.Windows.Media.Brush)FindResource("PosLightBlue");
        if (FeeModeBadgeText != null)
            FeeModeBadgeText.Foreground = _isFeeManual
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 83, 9))
                : (System.Windows.Media.Brush)FindResource("PosPrimary");
        if (ManualFeePanel != null)
            ManualFeePanel.Visibility = _isFeeManual ? Visibility.Visible : Visibility.Collapsed;
        if (!_isFeeManual)
        {
            // Revert to auto
            _manualFeeOverride = null;
            if (ManualFeeBox != null) ManualFeeBox.Text = string.Empty;
            RecalcTotal();
        }
    }

    private void OnManualFeeChanged(object sender, TextChangedEventArgs e) { /* live preview on Apply */ }

    private void OnApplyManualFee(object sender, RoutedEventArgs e)
    {
        var text = ManualFeeBox?.Text?.Trim() ?? "";
        if (decimal.TryParse(text, System.Globalization.NumberStyles.Number,
                             System.Globalization.CultureInfo.InvariantCulture, out var fee) && fee >= 0)
        {
            _manualFeeOverride = fee;
            RecalcTotal();
        }
        else
        {
            MessageBox.Show("Please enter a valid fee amount (e.g. 75 or 150).",
                "Invalid Fee", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RecalcTotal()
    {
        if (_isRecalculating) return;
        if (SubtotalLabel is null || GrandTotalLabel is null || _items is null)
            return;

        _isRecalculating = true;
        try
        {
            var count = _items.Count;
            var sub   = _items.Sum(i => i.Subtotal);
            var fee   = ComputeCurrentFee();
            var grand = sub + fee;

            SubtotalLabel.Text    = $"₱{sub:N2}";
            if (PasabuyFeeLabel != null)
                PasabuyFeeLabel.Text = $"₱{fee:N2}";
            GrandTotalLabel.Text  = $"₱{grand:N2}";

            if (CartCountBadge != null)
                CartCountBadge.Text = $"{count} {(count == 1 ? "item" : "items")}";
            if (TotalItemsCountLabel != null)
                TotalItemsCountLabel.Text = $"{count} {(count == 1 ? "item" : "items")}";
            if (EmptyCartPanel != null)
                EmptyCartPanel.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
            // NOTE: UpdateProductTileQuantities() is NOT called here to avoid looping all
            // tiles on every keystroke. It is called from OnItemsCollectionChanged instead.
        }
        finally
        {
            _isRecalculating = false;
        }
    }

    /// <summary>
    /// Computes current service fee: manual override (if active) > tier fee + manual item flat fees.
    /// </summary>
    private decimal ComputeCurrentFee()
    {
        if (_isFeeManual && _manualFeeOverride.HasValue)
            return _manualFeeOverride.Value;

        if (_items == null || _items.Count == 0)
            return 0m;

        var tierSubtotal = _items
            .Where(i => i.FeeType == ProductFeeType.STANDARD && !i.IsFeeExempt)
            .Sum(i => i.Subtotal);

        var manualFeeTotal = _items
            .Where(i => i.FeeType == ProductFeeType.MANUAL && i.ManualFeeAmount.HasValue && i.ManualFeeAmount.Value > 0m)
            .Sum(i => Calc.RoundMoney((i.ManualFeeAmount ?? 0m) * (decimal)i.Qty));

        var tierFee = tierSubtotal > 0m ? CalcFee(tierSubtotal) : 0m;
        return Calc.RoundMoney(tierFee + manualFeeTotal);
    }

    // ── Payment & Fulfillment Option Handlers ───────────────────────────────

    private void OnFulfillmentChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DeliveryAddressBox == null) return;
        var isDelivery = FulfillmentCombo.SelectedItem as string == "Delivery";
        DeliveryAddressBox.Visibility = isDelivery ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnPayOptionSelected(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            _isPayNow = tag == "now";
            UpdatePayOptionButtons();
        }
    }

    private void UpdatePayOptionButtons()
    {
        if (PayLaterBtn == null || PayNowBtn == null || PayNowPanel == null) return;

        NavProps.SetIsActive(PayLaterBtn, !_isPayNow);
        NavProps.SetIsActive(PayNowBtn, _isPayNow);

        PayLaterBtn.Foreground = !_isPayNow ? System.Windows.Media.Brushes.White : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(71, 85, 105));
        PayNowBtn.Foreground   = _isPayNow  ? System.Windows.Media.Brushes.White : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(71, 85, 105));

        PayNowPanel.Visibility = _isPayNow ? Visibility.Visible : Visibility.Collapsed;

        // Never auto-fill — let the cashier type the amount manually
        RecalcChangeDue();
    }

    private void OnPaymentMethodChanged(object sender, SelectionChangedEventArgs e)
    {
        var method = PaymentMethodCombo?.SelectedItem as string ?? "Cash";
        var isCash = method.StartsWith("Cash", StringComparison.OrdinalIgnoreCase);
        var isCod  = method.StartsWith("COD", StringComparison.OrdinalIgnoreCase);

        if (TenderedAmountBox != null)
        {
            TenderedAmountBox.IsEnabled = !isCod;
            if (isCod) TenderedAmountBox.Text = string.Empty;
        }

        if (QuickCashPanel != null)
            QuickCashPanel.Visibility = (isCash && !isCod) ? Visibility.Visible : Visibility.Collapsed;

        if (ChangeDueBorder != null)
            ChangeDueBorder.Visibility = (isCash && !isCod) ? Visibility.Visible : Visibility.Collapsed;

        if (isCod)
        {
            if (PaymentStatusHint != null)
            {
                PaymentStatusHint.Text = "COD (COLLECT ON DELIVERY)";
                PaymentStatusHint.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(217, 119, 6));
            }
        }
        else
        {
            RecalcChangeDue();
        }
    }

    private void OnTenderedAmountChanged(object sender, TextChangedEventArgs e)
    {
        // Restart the debounce timer — Change Due updates 400ms after typing stops
        _changeDebounce.Stop();
        _changeDebounce.Start();
    }

    private void OnQuickCashClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag && TenderedAmountBox != null)
        {
            var sub   = _items.Sum(i => i.Subtotal);
            var fee   = ComputeCurrentFee();
            var grand = sub + fee;

            if (tag == "exact")
            {
                TenderedAmountBox.Text = grand.ToString("F2");
            }
            else if (decimal.TryParse(tag, out var preset))
            {
                TenderedAmountBox.Text = preset.ToString("F2");
            }
            // Preset buttons update instantly — no delay needed
            _changeDebounce.Stop();
            RecalcChangeDue();
        }
    }

    private void RecalcChangeDue()
    {
        if (ChangeDueLabel == null || PaymentStatusHint == null || TenderedAmountBox == null || _items == null)
            return;

        var sub   = _items.Sum(i => i.Subtotal);
        var fee   = ComputeCurrentFee();
        var grand = sub + fee;

        // Use InvariantCulture so "100.50" always parses correctly regardless of system locale
        decimal.TryParse(TenderedAmountBox.Text,
            System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture,
            out var tendered);

        var hasInput = !string.IsNullOrWhiteSpace(TenderedAmountBox.Text);

        if (!hasInput || grand == 0m)
        {
            // Nothing typed yet — neutral awaiting state
            ChangeDueLabel.Text         = "₱0.00";
            ChangeDueLabel.Foreground   = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 116, 139));
            ChangeDueBorder.Background  = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(241, 245, 249));
            ChangeDueBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(203, 213, 225));
            PaymentStatusHint.Text      = "AWAITING";
            PaymentStatusHint.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 116, 139));
        }
        else if (tendered >= grand)
        {
            // Enough — show green change
            var change = tendered - grand;
            ChangeDueLabel.Text         = $"₱{change:N2}";
            ChangeDueLabel.Foreground   = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(5, 150, 105));
            ChangeDueBorder.Background  = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(236, 253, 245));
            ChangeDueBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129));
            PaymentStatusHint.Text      = change == 0m ? "EXACT" : "CHANGE";
            PaymentStatusHint.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(6, 95, 70));
        }
        else
        {
            // Short — show red shortage amount
            var shortage = grand - tendered;
            ChangeDueLabel.Text         = $"-₱{shortage:N2}";
            ChangeDueLabel.Foreground   = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38));
            ChangeDueBorder.Background  = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(254, 242, 242));
            ChangeDueBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(252, 165, 165));
            PaymentStatusHint.Text      = "SHORT";
            PaymentStatusHint.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(185, 28, 28));
        }
    }

    // ── Save Purchase Order ──────────────────────────────────────────────────

    private void OnPlaceOrder(object sender, RoutedEventArgs e)
    {
        if (_isSubmittingOrder) return;

        try
        {
            _isSubmittingOrder = true;  // Inside try so finally always resets it

            if (_items.Count == 0)
            {
                MessageBox.Show(
                    "Please add at least one item to the purchase order.",
                    "Empty Purchase Order",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var custId = CustomerCombo.SelectedValue is int cid && cid > 0 ? cid : (int?)null;
            var customerNameInput = CustomerCombo.Text?.Trim();

            // Customer name is strictly required: must select existing or type customer name
            if (!custId.HasValue && string.IsNullOrWhiteSpace(customerNameInput))
            {
                MessageBox.Show(
                    "Please select or write a customer name before placing the purchase order.",
                    "Customer Name Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                CustomerCombo.Focus();
                return;
            }

            if (!custId.HasValue && !string.IsNullOrWhiteSpace(customerNameInput))
            {
                var typedName = customerNameInput;
                var existing = _db.Customers.FirstOrDefault(c => c.Name.ToLower() == typedName.ToLower() && (c.BusinessId == BusinessProfileService.ANTIQUE_PASABUY || string.IsNullOrEmpty(c.BusinessId)));
                if (existing != null)
                {
                    custId = existing.Id;
                }
                else
                {
                    var createdCust = new CustomerService(_db).Create(typedName, businessId: BusinessProfileService.ANTIQUE_PASABUY);
                    custId = createdCust.Id;
                    LoadCustomers();
                }
            }
            var orderSvc     = new OrderService(_db);
            var selectedDay  = BuyingDayCombo.SelectedItem as string ?? "Wednesday";
            var notes        = string.IsNullOrWhiteSpace(NotesBox?.Text) ? null : NotesBox.Text.Trim();

            var isDelivery   = FulfillmentCombo.SelectedItem as string == "Delivery";
            var delivType    = isDelivery ? DeliveryType.Delivery : DeliveryType.Pickup;
            var address      = isDelivery ? DeliveryAddressBox?.Text?.Trim() : null;

            var initialStatus = OrderStatus.WAITING_CONFIRMATION;

            var validItems  = _items.Where(i => !string.IsNullOrWhiteSpace(i.Name) && i.Name.Trim() != "-" && i.Qty > 0).ToList();
            if (validItems.Count == 0)
            {
                MessageBox.Show("Please add at least one valid item with a non-zero quantity.", "Invalid Order", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var mappedItems  = validItems.Select(i => (i.Name, i.Qty, i.Unit, i.Price, i.Store, i.Notes, i.FeeType, i.ManualFeeAmount)).ToList();
            var subTotal     = validItems.Sum(i => i.Subtotal);
            var pasabuyFee   = ComputeCurrentFee();

            var activeBatch = SeedData.GetSetting(_db, "active_batch_code");
            var preferredStore = !string.IsNullOrWhiteSpace(_lastUsedStore)
                ? _lastUsedStore
                : (QuickStoreCombo?.SelectedItem as string ?? QuickStoreCombo?.Text?.Trim());

            // Use manual fee override if active
            if (_isFeeManual && _manualFeeOverride.HasValue)
                pasabuyFee = _manualFeeOverride.Value;

            var created = orderSvc.Create(
                customerId:     custId,
                items:          mappedItems,
                type:           delivType,
                address:        address,
                notes:          notes,
                serviceFee:     pasabuyFee,
                deliveryFee:    0m,
                discount:       0m,
                createdBy:      _user.Id,
                status:         initialStatus,
                buyingDay:      selectedDay,
                preferredStore: preferredStore,
                batchCode:      activeBatch
            );

            // Record Payment if COD or "Pay Now" was selected
            var methodStr = (PaymentMethodCombo?.SelectedItem as string) ?? "Cash";
            var isCod = methodStr.StartsWith("COD", StringComparison.OrdinalIgnoreCase);

            if (isCod)
            {
                var paySvc = new PaymentService(_db);
                paySvc.RecordPayment(created.Id, 0m, PaymentMethod.COD, "COD-ORDER", "Cash on Delivery (rider will collect)", _user.Id);
            }
            else if (TenderedAmountBox != null && decimal.TryParse(TenderedAmountBox.Text, out var tenderedAmt) && tenderedAmt > 0m)
            {
                PaymentMethod method = methodStr switch
                {
                    "GCash"         => PaymentMethod.GCash,
                    "Maya"          => PaymentMethod.Maya,
                    "Bank Transfer" => PaymentMethod.BankTransfer,
                    _               => PaymentMethod.Cash
                };

                var refNo = string.IsNullOrWhiteSpace(PaymentRefBox?.Text) ? null : PaymentRefBox.Text.Trim();
                var paySvc = new PaymentService(_db);
                var (payOk, payMsg, changeDue) = paySvc.RecordPayment(created.Id, tenderedAmt, method, refNo, "POS Ticket Direct Payment", _user.Id);

                if (payOk && changeDue > 0m)
                {
                    MessageBox.Show($"Payment recorded via {methodStr}.\nChange Due: ₱{changeDue:N2}", "Payment Tendered", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }

            // Fetch populated order for preview
            var fullOrder = orderSvc.GetById(created!.Id);
            var bizName   = SeedData.GetSetting(_db, "business_name",    "DIONIX POS - ANTIQUE PASABUY");
            var footer    = SeedData.GetSetting(_db, "receipt_footer",   "Thank you for trusting Antique Pasabuy!");

            if (fullOrder is not null)
            {
                var printRes = MessageBox.Show(
                    $"Purchase Order {created.OrderNo} saved successfully!\n" +
                    $"Status: {OrderStatusService.FormatStatus(created.Status)}\n" +
                    $"Estimated Total: ₱{created.GrandTotal:N2}\n\n" +
                    $"Print Purchase Order now?",
                    "Purchase Order Saved",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (printRes == MessageBoxResult.Yes)
                {
                    new PrintPreviewDialog(fullOrder, bizName, footer, targetPrinter: null)
                    {
                        Owner = Window.GetWindow(this)
                    }.ShowDialog();
                }
            }

            // Reset ticket for next order
            ResetTicket();
            _onSuccess?.Invoke();
        }
        finally
        {
            _isSubmittingOrder = false;
        }
    }

    /// <summary>Resets the ticket to a clean state after a successful order.</summary>
    private void ResetTicket()
    {
        _items.Clear();
        CustomerCombo.SelectedItem = null;
        CustomerCombo.Text = string.Empty;
        if (ClearCustomerBtn != null) ClearCustomerBtn.Visibility = Visibility.Collapsed;
        if (NotesBox != null) NotesBox.Text = "";
        if (DeliveryAddressBox != null) DeliveryAddressBox.Text = "";
        if (PaymentRefBox != null) PaymentRefBox.Text = "";
        if (TenderedAmountBox != null) TenderedAmountBox.Text = "";

        _isPayNow = false;
        UpdatePayOptionButtons();

        if (_showNotes)
        {
            _showNotes          = false;
            if (NotesBox != null) NotesBox.Visibility = Visibility.Collapsed;
            if (AddNoteBtn != null) AddNoteBtn.Content = "+ Add PO notes / special instructions";
        }

        // Refresh product stock on hand and catalog tiles
        LoadProducts();
        ApplyProductFilter();

        RecalcTotal();

        // Re-focus search for the next order
        ProductSearchBox?.Focus();
    }

    // ── Responsive Grid ──────────────────────────────────────────────────────

    private void OnProductsGridSizeChanged(object sender, SizeChangedEventArgs e)
    {
        AdjustProductColumns(e.NewSize.Width);
    }

    private void AdjustProductColumns(double width)
    {
        if (width <= 0) return;
        // Available width inside ProductsBorder (subtracting scrollbar ~20px and padding)
        double usableWidth = Math.Max(100, width - 26);
        // Ideal card width is ~150px-180px, minimum 135px
        int cols = (int)Math.Floor(usableWidth / 145.0);
        if (cols < 2) cols = 2;
        if (cols > 6) cols = 6;

        var grid = FindVisualChild<System.Windows.Controls.Primitives.UniformGrid>(ProductsItemsControl);
        if (grid != null && grid.Columns != cols)
            grid.Columns = cols;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null) return null;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) return typed;
            var sub = FindVisualChild<T>(child);
            if (sub != null) return sub;
        }
        return null;
    }

    // ── Needed reference for SizeChanged handler ─────────────────────────────
    // ProductsBorder is declared in XAML with SizeChanged="OnProductsGridSizeChanged"
    // The x:Name reference is resolved by the XAML designer, no code needed here.
}
