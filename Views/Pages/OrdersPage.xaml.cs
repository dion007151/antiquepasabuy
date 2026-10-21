using System.ComponentModel;
using System.Runtime.CompilerServices;
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

public class OrderRowVm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? prop = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));

    public Order Order { get; }
    public List<Order> GroupedOrders { get; }
    public bool IsGroup => GroupedOrders.Count > 1;
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

    public OrderRowVm(Order order, Action? onSelectionChanged = null)
    {
        Order = order;
        GroupedOrders = new List<Order> { order };
        OnSelectionChanged = onSelectionChanged;
    }

    public OrderRowVm(List<Order> orders, Action? onSelectionChanged = null)
    {
        Order = orders.First();
        GroupedOrders = orders;
        OnSelectionChanged = onSelectionChanged;
    }

    public int Id => Order.Id;
    public string OrderNo => IsGroup
        ? $"{Order.OrderNo} (+{GroupedOrders.Count - 1})"
        : Order.OrderNo;
    public Customer? Customer => Order.Customer;
    public DateTime CreatedAt => Order.CreatedAt;
    public string DateDisplay => Order.CreatedAt.ToString("MMM dd, yyyy");
    public string TimeDisplay => Order.CreatedAt.ToString("hh:mm tt");
    public string BuyingDay => IsGroup
        ? string.Join(", ", GroupedOrders.Select(o => o.BuyingDay).Where(d => !string.IsNullOrWhiteSpace(d)).Distinct())
        : Order.BuyingDay;
    public DeliveryType DeliveryType => Order.DeliveryType;
    public OrderStatus Status => Order.Status;
    public ICollection<OrderItem> Items => Order.Items;
    public int ItemCount => IsGroup
        ? GroupedOrders.Sum(o => o.Items?.Count(i => !string.IsNullOrWhiteSpace(i.ProductName) && i.ProductName.Trim() != "-") ?? 0)
        : Order.Items?.Count(i => !string.IsNullOrWhiteSpace(i.ProductName) && i.ProductName.Trim() != "-") ?? 0;
    public decimal GrandTotal => IsGroup
        ? GroupedOrders.Sum(o => o.GrandTotal)
        : Order.GrandTotal;
    public ICollection<Payment> Payments => Order.Payments;
    public decimal TotalPaid => IsGroup
        ? GroupedOrders.Sum(o => o.TotalPaid)
        : Order.TotalPaid;
    public decimal Balance => IsGroup
        ? GroupedOrders.Sum(o => o.Balance)
        : Order.Balance;
    public string BalanceDisplay => Balance <= 0 ? "\u20b10.00" : $"\u20b1{Balance:N2}";
    private static readonly SolidColorBrush PaidGreenBrush = FreezeBrush(new SolidColorBrush(Color.FromRgb(22, 163, 74)));
    private static readonly SolidColorBrush UnpaidRedBrush = FreezeBrush(new SolidColorBrush(Color.FromRgb(220, 38, 38)));
    private static SolidColorBrush FreezeBrush(SolidColorBrush b) { b.Freeze(); return b; }

    public Brush BalanceBrush => Balance <= 0 ? PaidGreenBrush : UnpaidRedBrush;
    public PaymentStatus PaymentStatus => IsGroup
        ? (GroupedOrders.All(o => o.PaymentStatus == PaymentStatus.PAID)
            ? PaymentStatus.PAID
            : GroupedOrders.Any(o => o.TotalPaid > 0)
                ? PaymentStatus.PARTIAL
                : Order.PaymentStatus)
        : Order.PaymentStatus;
    public DateTime? PaidDate => IsGroup
        ? (GroupedOrders.Any(o => o.PaidDate.HasValue)
            ? GroupedOrders.Where(o => o.PaidDate.HasValue).Max(o => o.PaidDate)
            : null)
        : Order.PaidDate;
    public string PaidDateDisplay => IsGroup
        ? (PaidDate.HasValue ? PaidDate.Value.ToString("MMM dd, yyyy") : "\u2014")
        : Order.PaidDateDisplay;
    public string Store => IsGroup
        ? string.Join(", ", GroupedOrders.Select(o => o.PreferredStore).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct())
        : (Order.PreferredStore ?? "Robinsons Antique");
    public decimal PasabuyFee => IsGroup
        ? GroupedOrders.Sum(o => o.PasabuyFee)
        : Order.PasabuyFee;
    public decimal ServiceFee => PasabuyFee;
    public decimal EstimatedTotal => IsGroup
        ? GroupedOrders.Sum(o => o.EstimatedItemsTotal)
        : Order.EstimatedItemsTotal;
    public decimal ActualTotal => IsGroup
        ? GroupedOrders.Sum(o => o.ActualItemsTotal)
        : Order.ActualItemsTotal;
    public string PaymentMethodDisplay => IsGroup
        ? string.Join(", ", GroupedOrders.Select(o => o.PaymentMethodDisplay).Distinct())
        : Order.PaymentMethodDisplay;

    public string PaymentTooltip => PaymentStatus switch
    {
        PaymentStatus.PAID => $"Paid in full ({PaymentMethodDisplay}): \u20b1{TotalPaid:N2}" + (PaidDate.HasValue ? $" on {PaidDate.Value:MMM dd, yyyy}" : ""),
        PaymentStatus.PARTIAL => $"Partly Paid: \u20b1{TotalPaid:N2} | Balance: \u20b1{Balance:N2}",
        _ => $"Not Paid \u2014 Balance due: \u20b1{Balance:N2}"
    };
}

public partial class OrdersPage : Page
{
    private readonly AppDbContext _db;
    private readonly User _user;
    private readonly Action<string>? _navigate;
    private string _activeFilter = "all";
    private string _activePaymentFilter = "all";
    private List<Order> _allOrders = [];
    private List<OrderRowVm> _allRowVms = [];
    private List<OrderRowVm> _visibleRowVms = [];

    public OrdersPage(AppDbContext db, User user, Action<string>? navigate = null, Action? onSuccess = null, OrderStatus? initialFilter = null, string? initialPaymentFilter = null)
    {
        InitializeComponent();
        _db = db;
        _user = user;
        _navigate = navigate;

        if (initialFilter.HasValue)
        {
            _activeFilter = initialFilter.Value switch
            {
                OrderStatus.WAITING_CONFIRMATION => "waiting_confirmation",
                OrderStatus.READY_TO_BUY         => "for_buying",
                OrderStatus.BUYING_NOW           => "buying",
                OrderStatus.BOUGHT               => "bought",
                OrderStatus.COMPLETED            => "completed",
                OrderStatus.CANCELLED            => "cancelled",
                _ => "all"
            };
        }

        if (!string.IsNullOrWhiteSpace(initialPaymentFilter))
        {
            _activePaymentFilter = initialPaymentFilter.ToLowerInvariant();
        }

        Loaded += (_, _) =>
        {
            PageSubheading.Text = $"Track customer orders and update status • Today: {DateTime.Now:dddd, MMM dd, yyyy}";
            if (_activeFilter is "waiting_confirmation" or "waiting" or "waiting_to_buy")
            {
                PageHeading.Text = "Waiting to Buy Orders";
                PageSubheading.Text = "Orders waiting for scheduled shopping day";
            }
            HighlightTab(_activeFilter);
            OrderEventBus.OrderStatusChanged += OnExternalStatusChanged;
            OrderEventBus.OrderCreated += OnExternalOrderCreated;
            OrderEventBus.PaymentRecorded += OnExternalPayment;
            OrderEventBus.DataRefreshed += OnExternalRefresh;

            if (_allRowVms.Count > 0)
            {
                // Instant navigation: cached orders are already visible with 0ms delay!
                ApplyFilter();
                Dispatcher.InvokeAsync(LoadData, System.Windows.Threading.DispatcherPriority.Background);
            }
            else
            {
                LoadData();
            }
        };

        Unloaded += (_, _) =>
        {
            OrderEventBus.OrderStatusChanged -= OnExternalStatusChanged;
            OrderEventBus.OrderCreated -= OnExternalOrderCreated;
            OrderEventBus.PaymentRecorded -= OnExternalPayment;
            OrderEventBus.DataRefreshed -= OnExternalRefresh;
        };
    }

    private void OnExternalStatusChanged(int id, OrderStatus s) => Dispatcher.InvokeAsync(() => LoadData());
    private void OnExternalOrderCreated(int id) => Dispatcher.InvokeAsync(() => LoadData());
    private void OnExternalPayment(int id, decimal amt) => Dispatcher.InvokeAsync(() => LoadData());
    private void OnExternalRefresh() => Dispatcher.InvokeAsync(() => LoadData());

    private void HighlightTab(string tag)
    {
        var tabs = new[] { TabAll, TabForBuying, TabBuying, TabBought, TabCompleted, TabCancelled };
        var target = tag switch
        {
            "waiting_to_buy" or "waiting_confirmation" or "waiting" or "for_buying" => TabForBuying,
            "buying"    => TabBuying,
            "bought"    => TabBought,
            "completed" => TabCompleted,
            "cancelled" => TabCancelled,
            _           => TabAll
        };

        foreach (var t in tabs)
        {
            if (t == null) continue;
            NavProps.SetIsActive(t, t == target);
            t.Foreground = t == target ? Brushes.White : new SolidColorBrush(Color.FromRgb(71, 85, 105));
        }
    }

    private bool _isLoadingOrders = false;

    private async void LoadData()
    {
        if (_isLoadingOrders) return;
        _isLoadingOrders = true;

        try
        {
            List<Order> orders = [];
            // Run DB query on background thread with isolated context to avoid blocking the UI
            await Task.Run(() =>
            {
                using var bgDb = new AppDbContext();
                var svc = new OrderService(bgDb);
                orders = svc.GetAll();
            });

            _allOrders = orders;
            _allRowVms = _allOrders
                .Select(o => new OrderRowVm(o, UpdateSelectionState))
                .OrderByDescending(vm => vm.CreatedAt)
                .ToList();

            ApplyFilter();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OrdersPage] LoadData error: {ex.Message}");
        }
        finally
        {
            _isLoadingOrders = false;
        }
    }

    private void ApplyFilter()
    {
        if (SearchBox is null || OrdersGrid is null || _allRowVms is null) return;
        var term = SearchBox.Text?.Trim().ToLower() ?? "";

        var filtered = _allRowVms.AsEnumerable();

        // 1. PO Status Quick Tab Filters: [ ALL ] [ WAITING TO BUY ] [ BUYING ] [ BOUGHT ] [ ORDER FINISHED ] [ CANCELLED ]
        filtered = _activeFilter switch
        {
            "waiting_to_buy" or "for_buying" or "waiting_confirmation" or "waiting" => filtered.Where(r => r.Status == OrderStatus.READY_TO_BUY || r.Status == OrderStatus.WAITING_CONFIRMATION),
            "buying"     => filtered.Where(r => r.Status == OrderStatus.BUYING_NOW),
            "bought"     => filtered.Where(r => r.Status == OrderStatus.BOUGHT),
            "completed"  => filtered.Where(r => r.Status == OrderStatus.COMPLETED),
            "cancelled"  => filtered.Where(r => r.Status == OrderStatus.CANCELLED),
            _            => filtered
        };

        // 2. Payment Status Quick Tab Filters: [ ALL ] [ PAID ] [ UNPAID ]
        filtered = _activePaymentFilter switch
        {
            "paid"   => filtered.Where(r => r.PaymentStatus == PaymentStatus.PAID),
            "unpaid" => filtered.Where(r => r.PaymentStatus != PaymentStatus.PAID),
            _        => filtered
        };

        // 3. Search Text
        if (!string.IsNullOrEmpty(term))
        {
            filtered = filtered.Where(r =>
                (r.OrderNo?.ToLower().Contains(term) ?? false) ||
                (r.Customer?.Name?.ToLower().Contains(term) ?? false) ||
                (r.BuyingDay?.ToLower().Contains(term) ?? false) ||
                r.Status.ToString().ToLower().Contains(term) ||
                r.PaymentStatus.ToString().ToLower().Contains(term));
        }

        _visibleRowVms = filtered.ToList();
        // Force full DataGrid rebind (fixes virtualized panel not re-rendering updated status badges)
        OrdersGrid.ItemsSource = null;
        OrdersGrid.ItemsSource = _visibleRowVms;
        if (EmptyStatePanel != null)
        {
            EmptyStatePanel.Visibility = _visibleRowVms.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        UpdateSelectionState();
    }

    private void OnTabClick(object s, RoutedEventArgs e)
    {
        if (s is not Button btn) return;
        _activeFilter = btn.Tag?.ToString() ?? "all";
        HighlightTab(_activeFilter);
        ApplyFilter();
    }



    private void OnSearch(object s, TextChangedEventArgs e) => ApplyFilter();

    private void OnClearSearch(object s, RoutedEventArgs e)
    {
        if (SearchBox is not null)
        {
            SearchBox.Text = "";
            SearchBox.Focus();
        }
    }

    private void OnRefreshClick(object s, RoutedEventArgs e) => LoadData();

    private void OnNewOrder(object s, RoutedEventArgs e) => _navigate?.Invoke("NewOrder");

    private void OnOrderDoubleClick(object s, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (OrdersGrid.SelectedItem is OrderRowVm vm)
            ShowOrderDetail(vm.Id);
        else if (OrdersGrid.SelectedItem is Order o)
            ShowOrderDetail(o.Id);
    }

    // ── SELECTION CONTROLS ──────────────────────────────────────────────────────
    private void OnHeaderSelectAllClick(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb)
        {
            var target = cb.IsChecked == true;
            foreach (var r in _visibleRowVms)
                r.IsSelected = target;
            UpdateSelectionState();
        }
    }

    private void OnRowCheckBoxClick(object sender, RoutedEventArgs e) => UpdateSelectionState();

    private void OnSelectAllClick(object sender, RoutedEventArgs e)
    {
        foreach (var r in _visibleRowVms)
            r.IsSelected = true;
        UpdateSelectionState();
    }

    private void OnClearSelectionClick(object sender, RoutedEventArgs e)
    {
        foreach (var r in _allRowVms)
            r.IsSelected = false;
        UpdateSelectionState();
    }

    private void UpdateSelectionState()
    {
        var selected = _visibleRowVms.Where(r => r.IsSelected).ToList();
        var count = selected.Count;
        var totalAmount = selected.Sum(r => r.GrandTotal);

        if (SelectedCountLabel != null)
            SelectedCountLabel.Text = count > 0 ? $"{count} selected (\u20b1{totalAmount:N2})" : "0 selected";

        var hasSelection = count > 0;
        if (BulkSelectionPanel != null)
            BulkSelectionPanel.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
        if (BulkStatusBtn != null) BulkStatusBtn.IsEnabled = hasSelection;
        if (BulkPrintBtn != null) BulkPrintBtn.IsEnabled = hasSelection;
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

    // ── BULK OPERATIONS: UPDATE STATUS, PRINT, DELETE ────────────────────────────
    private void OnBulkUpdateStatusClick(object sender, RoutedEventArgs e)
    {
        var selected = _visibleRowVms.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0) return;

        var menu = new ContextMenu();
        var statuses = new (string label, OrderStatus status)[]
        {
            ("Waiting to Buy", OrderStatus.WAITING_CONFIRMATION),
            ("Buying",         OrderStatus.BUYING_NOW),
            ("Bought",         OrderStatus.BOUGHT),
            ("Order Finished", OrderStatus.COMPLETED),
            ("Cancelled",      OrderStatus.CANCELLED)
        };

        foreach (var (lbl, st) in statuses)
        {
            var item = new MenuItem { Header = lbl };
            var targetStatus = st;
            item.Click += (_, _) => BulkSetStatus(selected, targetStatus);
            menu.Items.Add(item);
        }

        if (sender is Button btn)
        {
            menu.PlacementTarget = btn;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void BulkSetStatus(List<OrderRowVm> selected, OrderStatus targetStatus)
    {
        var statusSvc = new OrderStatusService(_db);
        int successCount = 0;
        int totalCount = 0;
        foreach (var vm in selected)
        {
            foreach (var order in vm.GroupedOrders)
            {
                totalCount++;
                var (ok, _) = statusSvc.Transition(order.Id, targetStatus, _user?.Id, $"Bulk status update to {targetStatus}");
                if (ok) successCount++;
            }
        }

        MessageBox.Show($"Updated {successCount} of {totalCount} order(s) to {targetStatus}.",
            "Bulk Status Update", MessageBoxButton.OK, MessageBoxImage.Information);
        LoadData();
    }

    private void OnBulkPrintClick(object sender, RoutedEventArgs e)
    {
        var selected = _visibleRowVms.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0) return;

        var savedPrinter = SeedData.GetSetting(_db, "default_printer", "");
        var bizName = SeedData.GetSetting(_db, "business_name", "ANTIQUE PASABUY POS");
        var footer = SeedData.GetSetting(_db, "receipt_footer", "Thank you for using Antique Pasabuy POS!");
        var svc = new OrderService(_db);

        int printedCount = 0;
        int totalCount = 0;
        foreach (var vm in selected)
        {
            foreach (var order in vm.GroupedOrders)
            {
                totalCount++;
                var fullOrder = svc.GetById(order.Id);
                if (fullOrder != null)
                {
                    var (ok, _) = PrinterService.PrintReceipt(fullOrder, bizName, footer, savedPrinter, showDialog: false);
                    if (ok) printedCount++;
                }
            }
        }

        MessageBox.Show($"Sent {printedCount} of {totalCount} receipt(s) to printer.",
            "Batch Print", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnViewOrder(object s, RoutedEventArgs e)
    {
        if (s is Button btn && btn.Tag is int id)
            ShowOrderDetail(id);
    }

    // ── KEYBOARD SHORTCUTS ──────────────────────────────────────────────────────
    private void OnPageKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.P && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            e.Handled = true;
            var dlg = new PrintPreviewDialog(_allOrders, "All Purchase Orders") { Owner = Window.GetWindow(this) };
            dlg.ShowDialog();
        }
    }

    // ── TOP PRINT MENU ───────────────────────────────────────────────────────────
    private void OnPrintMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        var menu = new ContextMenu();

        // 1. Print All POs
        var printAllItem = new MenuItem
        {
            Header = "Print All Purchase Orders",
            InputGestureText = "Ctrl+P",
            Icon = new MaterialDesignThemes.Wpf.PackIcon { Kind = MaterialDesignThemes.Wpf.PackIconKind.PrinterOutline, Width = 16, Height = 16 }
        };
        printAllItem.Click += (_, _) =>
        {
            menu.IsOpen = false;
            Dispatcher.BeginInvoke(() =>
            {
                var dlg = new PrintPreviewDialog(_allOrders, "All Purchase Orders") { Owner = Window.GetWindow(this) };
                dlg.ShowDialog();
            });
        };
        menu.Items.Add(printAllItem);

        // 2. Print Today's POs
        var todayOrders = _allOrders.Where(o => o.CreatedAt.Date == DateTime.Today).ToList();
        var printTodayItem = new MenuItem
        {
            Header = $"Print Today's Purchase Orders ({todayOrders.Count} orders - {DateTime.Today:MMM dd, yyyy})",
            Icon = new MaterialDesignThemes.Wpf.PackIcon { Kind = MaterialDesignThemes.Wpf.PackIconKind.CalendarToday, Width = 16, Height = 16 }
        };
        printTodayItem.Click += (_, _) =>
        {
            menu.IsOpen = false;
            Dispatcher.BeginInvoke(() =>
            {
                if (todayOrders.Count == 0)
                {
                    MessageBox.Show("There are no purchase orders recorded for today.", "No Orders Today", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                var dlg = new PrintPreviewDialog(todayOrders, $"Today's Purchase Orders ({DateTime.Today:MMM dd, yyyy})") { Owner = Window.GetWindow(this) };
                dlg.ShowDialog();
            });
        };
        menu.Items.Add(printTodayItem);

        // 2. Print Filtered POs
        var printFilteredItem = new MenuItem
        {
            Header = $"Print Filtered POs ({_visibleRowVms.Count} orders)",
            Icon = new MaterialDesignThemes.Wpf.PackIcon { Kind = MaterialDesignThemes.Wpf.PackIconKind.FilterOutline, Width = 16, Height = 16 }
        };
        printFilteredItem.Click += (_, _) =>
        {
            menu.IsOpen = false;
            Dispatcher.BeginInvoke(() =>
            {
                var list = _visibleRowVms.SelectMany(r => r.GroupedOrders).ToList();
                var dlg = new PrintPreviewDialog(list, $"Purchase Orders ({_activeFilter.ToUpper()})") { Owner = Window.GetWindow(this) };
                dlg.ShowDialog();
            });
        };
        menu.Items.Add(printFilteredItem);

        menu.Items.Add(new Separator());

        // 3. Print Shopping Checklist (All)
        var printChecklistItem = new MenuItem
        {
            Header = "Print Shopping Checklist (All Stores & Days)",
            Icon = new MaterialDesignThemes.Wpf.PackIcon { Kind = MaterialDesignThemes.Wpf.PackIconKind.ClipboardCheckOutline, Width = 16, Height = 16 }
        };
        printChecklistItem.Click += (_, _) =>
        {
            menu.IsOpen = false;
            Dispatcher.BeginInvoke(() =>
            {
                var svc = new OrderService(_db);
                var items = svc.GetBuyingItems();
                if (items.Count == 0)
                {
                    MessageBox.Show("There are no pending items on the shopping checklist.", "Nothing to Print", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                var dlg = new PrintPreviewDialog(items, "All Stores", "All Days", "Shopping Checklist — All Stores") { Owner = Window.GetWindow(this) };
                dlg.ShowDialog();
            });
        };
        menu.Items.Add(printChecklistItem);

        // 4. Print By Store (Submenu)
        var printByStoreMenu = new MenuItem
        {
            Header = "Print Shopping Checklist By Store ►",
            Icon = new MaterialDesignThemes.Wpf.PackIcon { Kind = MaterialDesignThemes.Wpf.PackIconKind.StoreOutline, Width = 16, Height = 16 }
        };
        var stores = _db.Stores.Where(s => s.IsActive).OrderBy(s => s.Name).Select(s => s.Name).ToList();
        if (stores.Count == 0) stores = new List<string> { "SM", "Robinsons", "General" };
        foreach (var store in stores)
        {
            var storeItem = new MenuItem
            {
                Header = store,
                Icon = new MaterialDesignThemes.Wpf.PackIcon { Kind = MaterialDesignThemes.Wpf.PackIconKind.StorefrontOutline, Width = 14, Height = 14 }
            };
            var curStore = store;
            storeItem.Click += (_, _) =>
            {
                menu.IsOpen = false;
                Dispatcher.BeginInvoke(() =>
                {
                    var svc = new OrderService(_db);
                    var items = svc.GetBuyingItems(storeFilter: curStore);
                    if (items.Count == 0)
                    {
                        MessageBox.Show($"No pending items for store: {curStore}.", "Nothing to Print", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    var dlg = new PrintPreviewDialog(items, curStore, "All Days", $"Shopping Checklist — {curStore}") { Owner = Window.GetWindow(this) };
                    dlg.ShowDialog();
                });
            };
            printByStoreMenu.Items.Add(storeItem);
        }
        menu.Items.Add(printByStoreMenu);

        // 5. Print By Shopping Day (Submenu)
        var printByDayMenu = new MenuItem
        {
            Header = "Print Shopping Checklist By Shopping Day ►",
            Icon = new MaterialDesignThemes.Wpf.PackIcon { Kind = MaterialDesignThemes.Wpf.PackIconKind.CalendarClock, Width = 16, Height = 16 }
        };
        var scheduleSetting = SeedData.GetSetting(_db, "buying_schedule_days", "Wednesday, Thursday");
        var days = scheduleSetting.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (days.Count == 0) days = new List<string> { "Wednesday", "Thursday", "Saturday" };
        foreach (var day in days)
        {
            var dayItem = new MenuItem
            {
                Header = day,
                Icon = new MaterialDesignThemes.Wpf.PackIcon { Kind = MaterialDesignThemes.Wpf.PackIconKind.CalendarMonthOutline, Width = 14, Height = 14 }
            };
            var curDay = day;
            dayItem.Click += (_, _) =>
            {
                menu.IsOpen = false;
                Dispatcher.BeginInvoke(() =>
                {
                    var svc = new OrderService(_db);
                    var items = svc.GetBuyingItems(buyingDayFilter: curDay);
                    if (items.Count == 0)
                    {
                        MessageBox.Show($"No pending items for shopping day: {curDay}.", "Nothing to Print", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    var dlg = new PrintPreviewDialog(items, "All Stores", curDay, $"Shopping Checklist — {curDay}") { Owner = Window.GetWindow(this) };
                    dlg.ShowDialog();
                });
            };
            printByDayMenu.Items.Add(dayItem);
        }
        menu.Items.Add(printByDayMenu);

        menu.PlacementTarget = btn;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    // ── MORE MENU [ ⋮ ] ──────────────────────────────────────────────────────────
    private void OnMoreMenuClick(object s, RoutedEventArgs e)
    {
        if (s is not Button btn || btn.Tag is not int id) return;

        var vm = _visibleRowVms.FirstOrDefault(r => r.Id == id);
        if (vm == null) return;
        var order = vm.Order;

        var menu = new ContextMenu();

        // 1. View Details
        var viewItem = new MenuItem
        {
            Header = "View Details",
            Icon = new MaterialDesignThemes.Wpf.PackIcon { Kind = MaterialDesignThemes.Wpf.PackIconKind.EyeOutline, Width = 16, Height = 16 }
        };
        viewItem.Click += (_, _) =>
        {
            menu.IsOpen = false;
            Dispatcher.BeginInvoke(() => ShowOrderDetail(vm.Id));
        };
        menu.Items.Add(viewItem);

        // 2. Status-specific Primary Actions:
        // WAITING TO BUY: Start Buying
        if (vm.Status is OrderStatus.WAITING_CONFIRMATION or OrderStatus.READY_TO_BUY)
        {
            var startBuyingItem = new MenuItem
            {
                Header = "Start Buying",
                Icon = new MaterialDesignThemes.Wpf.PackIcon { Kind = MaterialDesignThemes.Wpf.PackIconKind.CartArrowDown, Width = 16, Height = 16 }
            };
            startBuyingItem.Click += (_, _) =>
            {
                menu.IsOpen = false;
                Dispatcher.BeginInvoke(() => TransitionVmOrders(vm, OrderStatus.BUYING_NOW, "Started buying"));
            };
            menu.Items.Add(startBuyingItem);
        }
        // BUYING NOW: Mark as Bought
        else if (vm.Status == OrderStatus.BUYING_NOW)
        {
            var boughtItem = new MenuItem
            {
                Header = "Mark as Bought",
                Icon = new MaterialDesignThemes.Wpf.PackIcon { Kind = MaterialDesignThemes.Wpf.PackIconKind.BasketCheckOutline, Width = 16, Height = 16 }
            };
            boughtItem.Click += (_, _) =>
            {
                menu.IsOpen = false;
                Dispatcher.BeginInvoke(() => TransitionVmOrders(vm, OrderStatus.BOUGHT, "All items bought at store"));
            };
            menu.Items.Add(boughtItem);
        }
        // BOUGHT: Order Finished (Hand over to customer)
        else if (vm.Status == OrderStatus.BOUGHT)
        {
            var completeItem = new MenuItem
            {
                Header = "Finish Order (Delivered / Handed Over)",
                Icon = new MaterialDesignThemes.Wpf.PackIcon { Kind = MaterialDesignThemes.Wpf.PackIconKind.CheckDecagram, Width = 16, Height = 16 }
            };
            completeItem.Click += (_, _) =>
            {
                menu.IsOpen = false;
                Dispatcher.BeginInvoke(() => TransitionVmOrders(vm, OrderStatus.COMPLETED, "Completed delivery / pickup handover"));
            };
            menu.Items.Add(completeItem);
        }

        // 3. Edit Order: Available for WAITING_CONFIRMATION, READY_TO_BUY, BUYING_NOW, BOUGHT
        if (vm.Status != OrderStatus.COMPLETED && vm.Status != OrderStatus.CANCELLED)
        {
            var editItem = new MenuItem
            {
                Header = "Edit Order",
                Icon = new MaterialDesignThemes.Wpf.PackIcon { Kind = MaterialDesignThemes.Wpf.PackIconKind.PencilOutline, Width = 16, Height = 16 }
            };
            editItem.Click += (_, _) =>
            {
                menu.IsOpen = false;
                Dispatcher.BeginInvoke(() =>
                {
                    var dlg = new EditOrderDialog(_db, order) { Owner = Window.GetWindow(this) };
                    if (dlg.ShowDialog() == true) LoadData();
                });
            };
            menu.Items.Add(editItem);

            // 4. Cancel Order: Available for WAITING_CONFIRMATION, READY_TO_BUY, BUYING_NOW
            var cancelItem = new MenuItem
            {
                Header = "Cancel Order",
                Icon = new MaterialDesignThemes.Wpf.PackIcon { Kind = MaterialDesignThemes.Wpf.PackIconKind.CloseCircleOutline, Width = 16, Height = 16 }
            };
            cancelItem.Click += (_, _) =>
            {
                menu.IsOpen = false;
                Dispatcher.BeginInvoke(() => CancelOrder(order));
            };
            menu.Items.Add(cancelItem);
        }

        menu.Items.Add(new Separator());

        // 5. Print: Available for ALL statuses
        var printPOItem = new MenuItem
        {
            Header = "Print",
            Icon = new MaterialDesignThemes.Wpf.PackIcon { Kind = MaterialDesignThemes.Wpf.PackIconKind.Printer, Width = 16, Height = 16 }
        };
        printPOItem.Click += (_, _) =>
        {
            menu.IsOpen = false;
            Dispatcher.BeginInvoke(() => PrintOrder(order));
        };
        menu.Items.Add(printPOItem);

        // 6. Print Shopping Checklist: Available for READY_TO_BUY, BUYING_NOW, BOUGHT, COMPLETED
        if (vm.Status is OrderStatus.READY_TO_BUY or OrderStatus.BUYING_NOW or OrderStatus.BOUGHT or OrderStatus.COMPLETED)
        {
            var printChecklistItem = new MenuItem
            {
                Header = "Print Shopping Checklist",
                Icon = new MaterialDesignThemes.Wpf.PackIcon { Kind = MaterialDesignThemes.Wpf.PackIconKind.ClipboardTextOutline, Width = 16, Height = 16 }
            };
            printChecklistItem.Click += (_, _) =>
            {
                menu.IsOpen = false;
                Dispatcher.BeginInvoke(() => PrintOrderChecklist(order));
            };
            menu.Items.Add(printChecklistItem);
        }



        menu.PlacementTarget = btn;
        menu.IsOpen = true;
    }

    public void OnStatusBadgeClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not OrderRowVm vm) return;

        var menu = new ContextMenu();
        var statuses = new (string label, OrderStatus status)[]
        {
            ("Waiting to Buy", OrderStatus.WAITING_CONFIRMATION),
            ("Buying",         OrderStatus.BUYING_NOW),
            ("Bought",         OrderStatus.BOUGHT),
            ("Order Finished", OrderStatus.COMPLETED),
            ("Cancelled",      OrderStatus.CANCELLED)
        };

        foreach (var (lbl, st) in statuses)
        {
            bool isCurrent = st == OrderStatus.WAITING_CONFIRMATION
                ? (vm.Status == OrderStatus.WAITING_CONFIRMATION || vm.Status == OrderStatus.READY_TO_BUY)
                : vm.Status == st;

            var item = new MenuItem
            {
                Header = lbl,
                IsChecked = isCurrent,
                FontWeight = isCurrent ? FontWeights.Bold : FontWeights.Normal
            };
            var targetStatus = st;
            item.Click += (_, _) =>
            {
                menu.IsOpen = false;
                TransitionVmOrders(vm, targetStatus, $"Status changed to {lbl} via Status badge");
            };
            menu.Items.Add(item);
        }

        menu.PlacementTarget = btn;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void TransitionVmOrders(OrderRowVm vm, OrderStatus targetStatus, string reason)
    {
        // Detach all tracked entities before status transition to prevent EF Core tracking conflicts
        foreach (var entry in _db.ChangeTracker.Entries().ToList())
            entry.State = EntityState.Detached;

        var statusSvc = new OrderStatusService(_db);
        var (ok, msg) = statusSvc.Transition(vm.Id, targetStatus, _user?.Id, reason);
        if (!ok)
        {
            MessageBox.Show(msg, "Status Update Notice", MessageBoxButton.OK, MessageBoxImage.Warning);
            LoadData();
            return;
        }

        // If the new status would be hidden by the current tab filter,
        // switch to "All" so the user can see the updated badge
        bool willBeHidden = _activeFilter switch
        {
            "for_buying"            => targetStatus != OrderStatus.READY_TO_BUY && targetStatus != OrderStatus.WAITING_CONFIRMATION,
            "buying"                => targetStatus != OrderStatus.BUYING_NOW,
            "bought"                => targetStatus != OrderStatus.BOUGHT,
            "completed"             => targetStatus != OrderStatus.COMPLETED,
            "cancelled"             => targetStatus != OrderStatus.CANCELLED,
            "waiting_confirmation"  => targetStatus != OrderStatus.WAITING_CONFIRMATION,
            _                       => false   // "all" — never hidden
        };

        if (willBeHidden)
        {
            _activeFilter = "all";
            HighlightTab("all");
        }

        // Reload fresh data from DB and force full grid rebind
        LoadData();
    }

    private void PrintOrder(Order order)
    {
        var orderService = new OrderService(_db);
        var fullOrder = orderService.GetById(order.Id) ?? order;
        var savedPrinter = SeedData.GetSetting(_db, "default_printer", "");
        var bizName = SeedData.GetSetting(_db, "business_name", "ANTIQUE PASABUY POS");
        var footer = SeedData.GetSetting(_db, "receipt_footer", "Thank you for using Antique Pasabuy POS!");
        var dlg = new PrintPreviewDialog(fullOrder, bizName, footer, savedPrinter)
        {
            Owner = Window.GetWindow(this)
        };
        dlg.ShowDialog();
    }

    private void PrintOrderChecklist(Order order)
    {
        var orderService = new OrderService(_db);
        var fullOrder = orderService.GetById(order.Id) ?? order;
        var savedPrinter = SeedData.GetSetting(_db, "default_printer", "");
        var items = fullOrder.Items.ToList();
        var dlg = new PrintPreviewDialog(items, fullOrder.PreferredStore ?? "Stores", fullOrder.BuyingDay, $"Shopping Checklist — PO {fullOrder.OrderNo}", savedPrinter)
        {
            Owner = Window.GetWindow(this)
        };
        dlg.ShowDialog();
    }

    private void CancelOrder(Order order)
    {
        var res = MessageBox.Show(
            $"Are you sure you want to cancel Order {order.OrderNo}?",
            "Cancel Order", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (res == MessageBoxResult.Yes)
        {
            var svc = new OrderService(_db);
            var (ok, msg) = svc.Cancel(order.Id, "Cancelled via quick menu", _user.Id);
            MessageBox.Show(msg, ok ? "Cancelled" : "Error", MessageBoxButton.OK,
                ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
            if (ok) LoadData();
        }
    }



    private void ShowOrderDetail(int orderId)
    {
        var svc = new OrderService(_db);
        var order = svc.GetById(orderId);
        if (order is null) return;
        var dlg = new Dialogs.OrderDetailDialog(_db, order, _user);
        dlg.Owner = Window.GetWindow(this);
        dlg.ShowDialog();
        LoadData();
    }
}

