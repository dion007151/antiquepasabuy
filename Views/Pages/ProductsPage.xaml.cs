using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;

namespace AntiquePasabuyPOS.Views.Pages;

public class ProductRowVm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? prop = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));

    public Product Product { get; }
    public Action? OnSelectionChanged { get; set; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
                OnSelectionChanged?.Invoke();
            }
        }
    }

    public int Id => Product.Id;
    public string Name => Product.Name;
    public string Category => string.IsNullOrWhiteSpace(Product.Category) ? "General" : Product.Category;
    public string Store => string.IsNullOrWhiteSpace(Product.Store) ? "General" : Product.Store;
    public decimal Price => Product.Price;
    public string Unit => string.IsNullOrWhiteSpace(Product.Unit) ? "pc" : Product.Unit;
    public bool IsActive => Product.IsActive;
    public bool IsFeeExempt => Product.IsFeeExempt;
    public string? ImagePath => Product.ImagePath;

    private ImageSource? _thumbnail;
    private bool _thumbnailLoaded;
    public ImageSource? Thumbnail
    {
        get
        {
            if (!_thumbnailLoaded)
            {
                _thumbnailLoaded = true;
                _thumbnail = ProductImageService.LoadBitmap(Product.ImagePath, 0);
            }
            return _thumbnail;
        }
    }
    public bool HasImage => !string.IsNullOrWhiteSpace(Product.ImagePath) && Thumbnail != null;

    public ProductRowVm(Product p, Action? onSelectionChanged = null)
    {
        Product = p;
        OnSelectionChanged = onSelectionChanged;
    }
}

public partial class ProductsPage : Page
{
    private readonly AppDbContext _db;
    private readonly User _user;
    private List<ProductRowVm> _all = [];
    private List<ProductRowVm> _visible = [];
    private string _activeFilter = "all";

    private bool _isLoadingProducts = false;

    public ProductsPage(AppDbContext db, User user, Action<string>? navigate = null, Action? onSuccess = null)
    {
        InitializeComponent();
        _db = db;
        _user = user;
        Loaded += (_, _) =>
        {
            if (_all.Count > 0)
            {
                // Instant navigation: cached product catalog is visible with 0ms delay!
                ApplyFilter();
                Dispatcher.InvokeAsync(LoadData, System.Windows.Threading.DispatcherPriority.Background);
            }
            else
            {
                LoadData();
            }

            var window = Window.GetWindow(this);
            if (window != null)
            {
                window.PreviewKeyDown -= OnWindowPreviewKeyDown;
                window.PreviewKeyDown += OnWindowPreviewKeyDown;
            }
        };
        Unloaded += (_, _) =>
        {
            var window = Window.GetWindow(this);
            if (window != null)
            {
                window.PreviewKeyDown -= OnWindowPreviewKeyDown;
            }
        };
    }

    private async void LoadData()
    {
        if (_isLoadingProducts) return;
        _isLoadingProducts = true;
        try
        {
            List<Product> products = [];
            await Task.Run(() =>
            {
                using var bgDb = new AppDbContext();
                products = bgDb.Products.AsNoTracking().OrderBy(p => p.Name).ToList();
            });

            _all = products.Select(p => new ProductRowVm(p, UpdateSelectionState)).ToList();
            UpdateFilterCounts();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ProductsPage] LoadData error: {ex.Message}");
        }
        finally
        {
            _isLoadingProducts = false;
        }
    }

    private void UpdateFilterCounts()
    {
        var total   = _all.Count;
        var active  = _all.Count(p => p.IsActive);
        var inactive = _all.Count(p => !p.IsActive);
        var withFee = _all.Count(p => !p.IsFeeExempt);
        var noFee   = _all.Count(p => p.IsFeeExempt);

        FilterAllBtn.Content      = $"ALL ({total})";
        FilterActiveBtn.Content   = $"ACTIVE ({active})";
        FilterInactiveBtn.Content = $"INACTIVE ({inactive})";
        FilterWithFeeBtn.Content  = $"WITH PASABUY FEE ({withFee})";
        FilterNoFeeBtn.Content    = $"NO PASABUY FEE ({noFee})";
    }

    private void OnFilterClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            _activeFilter = tag.ToLower();

            // Update button styles
            SetButtonStyle(FilterAllBtn,      _activeFilter == "all");
            SetButtonStyle(FilterActiveBtn,   _activeFilter == "active");
            SetButtonStyle(FilterInactiveBtn, _activeFilter == "inactive");
            SetButtonStyle(FilterWithFeeBtn,  _activeFilter == "withfee");
            SetButtonStyle(FilterNoFeeBtn,    _activeFilter == "nofee");

            ApplyFilter();
        }
    }

    private void SetButtonStyle(Button btn, bool isActive)
    {
        NavProps.SetIsActive(btn, isActive);
        btn.Foreground = isActive ? Brushes.White : new SolidColorBrush(Color.FromRgb(71, 85, 105));
    }

    private void ApplyFilter()
    {
        if (SearchBox is null || MainGrid is null || _all is null) return;
        var t = SearchBox.Text?.Trim().ToLower() ?? "";

        var filtered = _all.AsEnumerable();

        filtered = _activeFilter switch
        {
            "active"   => filtered.Where(p => p.IsActive),
            "inactive" => filtered.Where(p => !p.IsActive),
            "withfee"  => filtered.Where(p => !p.IsFeeExempt),
            "nofee"    => filtered.Where(p => p.IsFeeExempt),
            _          => filtered
        };

        if (!string.IsNullOrEmpty(t))
        {
            filtered = filtered.Where(p =>
                (p.Name?.ToLower().Contains(t) ?? false) ||
                (p.Category?.ToLower().Contains(t) ?? false) ||
                (p.Store?.ToLower().Contains(t) ?? false) ||
                p.Price.ToString().Contains(t));
        }

        _visible = filtered.ToList();
        MainGrid.ItemsSource = _visible;
        UpdateSelectionState();
    }

    // ══ SELECTION CONTROLS ════════════════════════════════════════════════════
    private void OnHeaderSelectAllClick(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb)
        {
            var target = cb.IsChecked == true;
            foreach (var r in _visible) r.IsSelected = target;
            UpdateSelectionState();
        }
    }

    private void OnRowCheckBoxClick(object sender, RoutedEventArgs e) => UpdateSelectionState();

    private void OnSelectAllClick(object sender, RoutedEventArgs e)
    {
        foreach (var r in _visible) r.IsSelected = true;
        UpdateSelectionState();
    }

    private void OnClearSelectionClick(object sender, RoutedEventArgs e)
    {
        foreach (var r in _all) r.IsSelected = false;
        UpdateSelectionState();
    }

    private void UpdateSelectionState()
    {
        var count = _visible.Count(r => r.IsSelected);
        if (SelectedCountLabel != null)
            SelectedCountLabel.Text = $"{count} selected";
        if (BulkSelectionPanel != null)
            BulkSelectionPanel.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnBulkToggleActiveClick(object sender, RoutedEventArgs e)
    {
        var selected = _visible.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0) return;

        foreach (var vm in selected)
        {
            var p = _db.Products.Find(vm.Id);
            if (p != null)
            {
                p.IsActive = !p.IsActive;
            }
        }
        _db.SaveChanges();
        LoadData();
    }

    private void OnBulkMoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu != null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            btn.ContextMenu.IsOpen = true;
        }
    }

    private void OnBulkDeleteClick(object sender, RoutedEventArgs e)
    {
        var selected = _visible.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0) return;

        var res = MessageBox.Show(
            $"Are you sure you want to delete {selected.Count} selected product(s)?\n\nThis cannot be undone.",
            "Confirm Bulk Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (res != MessageBoxResult.Yes) return;

        int deletedCount = 0;
        foreach (var vm in selected)
        {
            var p = _db.Products.Find(vm.Id);
            if (p != null)
            {
                var imgPath = p.ImagePath;
                _db.Products.Remove(p);
                ProductImageService.DeleteProductImage(imgPath);
                deletedCount++;
            }
        }
        _db.SaveChanges();

        MessageBox.Show($"Successfully deleted {deletedCount} of {selected.Count} product(s).",
            "Bulk Delete Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        LoadData();
    }

    private void OnClearSearch(object s, RoutedEventArgs e)
    {
        if (SearchBox is not null)
        {
            SearchBox.Text = "";
            SearchBox.Focus();
        }
    }

    private void OnSearch(object s, TextChangedEventArgs e) => ApplyFilter();

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2)
        {
            OnScanBarcode(sender, e);
            e.Handled = true;
        }
    }

    private void OnScanBarcode(object sender, RoutedEventArgs e)
    {
        var dlg = new Dialogs.BarcodeScannerDialog(_db, (Product product) => OnBarcodeProductAdded(product))
        {
            Owner = Window.GetWindow(this),
            Title = "Barcode Scanner — Add & Manage Products"
        };
        dlg.ShowDialog();
        LoadData();
    }

    private void OnBarcodeProductAdded(Product product)
    {
        if (product is null) return;
        Dispatcher.Invoke(() =>
        {
            LoadData();
            // Automatically find and highlight the newly added or updated product in the catalog table
            var found = _all.FirstOrDefault(p => p.Id == product.Id || (!string.IsNullOrEmpty(p.Product.Barcode) && p.Product.Barcode == product.Barcode));
            if (found != null)
            {
                ApplyFilter();
                var item = _visible.FirstOrDefault(v => v.Id == found.Id);
                if (item != null)
                {
                    MainGrid.SelectedItem = item;
                    MainGrid.ScrollIntoView(item);
                }
            }
        });
    }

    private void OnAdd(object s, RoutedEventArgs e)
    {
        var dlg = new Dialogs.ProductDialog(_db);
        dlg.Owner = Window.GetWindow(this);
        if (dlg.ShowDialog() == true) LoadData();
    }

    private void OnEdit(object s, RoutedEventArgs e)
    {
        if (s is not Button btn || btn.Tag is not int id) return;
        var p = _db.Products.Find(id);
        if (p is null) return;
        var dlg = new Dialogs.ProductDialog(_db, p);
        dlg.Owner = Window.GetWindow(this);
        if (dlg.ShowDialog() == true) LoadData();
    }

    private void OnMoreMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu != null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.IsOpen = true;
        }
    }

    private Product? GetProductFromSender(object sender)
    {
        if (sender is FrameworkElement fe)
        {
            if (fe.Parent is ContextMenu cm && cm.PlacementTarget is Button btn && btn.Tag is int id)
            {
                return _db.Products.Find(id);
            }
            if (fe.Tag is int directId)
            {
                return _db.Products.Find(directId);
            }
        }
        return null;
    }

    private void OnToggleActive(object sender, RoutedEventArgs e)
    {
        var p = GetProductFromSender(sender);
        if (p is null) return;

        p.IsActive = !p.IsActive;
        _db.SaveChanges();
        LoadData();
    }

    private void OnDeleteFromMenu(object sender, RoutedEventArgs e)
    {
        var p = GetProductFromSender(sender);
        if (p is null) return;

        var res = MessageBox.Show(
            $"Are you sure you want to delete product '{p.Name}'?",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (res == MessageBoxResult.Yes)
        {
            var imgPath = p.ImagePath;
            _db.Products.Remove(p);
            _db.SaveChanges();
            ProductImageService.DeleteProductImage(imgPath);
            LoadData();
        }
    }
}
