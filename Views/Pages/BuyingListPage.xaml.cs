using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;
using Microsoft.EntityFrameworkCore;

namespace AntiquePasabuyPOS.Views.Pages;

public partial class BuyingListPage : Page
{
    private readonly AppDbContext _db;
    private readonly User _user;
    private readonly Action<string>? _navigate;
    private readonly ItemStatus? _initialStatus;
    private bool _initialized = false;
    private bool _isLoadingData = false;
    private bool _isInitializingFilters = false;
    private List<OrderItem> _currentRawItems = [];
    private List<BuyingItemRowVm> _allRowVms = [];
    private string _searchText = "";
    private bool _todayFilterActive = false;
    private bool _groupByStore = true;

    public BuyingListPage(AppDbContext db, User user, Action<string>? navigate = null, Action? onSuccess = null, ItemStatus? initialStatus = null)
    {
        InitializeComponent();
        _db = db;
        _user = user;
        _navigate = navigate;
        _initialStatus = initialStatus;

        Loaded += (_, _) =>
        {
            if (!_initialized)
            {
                InitFilters();
                _initialized = true;
                LoadData();
            }
            else
            {
                // Instant navigation: cached grid is already visible with 0ms delay!
                // Silently refresh in background at idle priority without blocking the UI thread
                Dispatcher.InvokeAsync(LoadData, System.Windows.Threading.DispatcherPriority.Background);
            }
            OrderEventBus.ItemStatusChanged += OnExternalItemChanged;
            OrderEventBus.OrderStatusChanged += OnExternalOrderChanged;
            OrderEventBus.DataRefreshed += OnExternalRefresh;
        };

        Unloaded += (_, _) =>
        {
            OrderEventBus.ItemStatusChanged -= OnExternalItemChanged;
            OrderEventBus.OrderStatusChanged -= OnExternalOrderChanged;
            OrderEventBus.DataRefreshed -= OnExternalRefresh;
        };
    }

    private void OnExternalItemChanged(int itemId, ItemStatus newStatus) => Dispatcher.InvokeAsync(LoadData);
    private void OnExternalOrderChanged(int orderId, OrderStatus newStatus) => Dispatcher.InvokeAsync(LoadData);
    private void OnExternalRefresh() => Dispatcher.InvokeAsync(LoadData);

    // F2 = Open camera barcode scanner
    private void OnPageKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.F2)
        {
            e.Handled = true;
            OpenCameraScanner();
        }
    }

    private void InitFilters()
    {
        _isInitializingFilters = true;
        try
        {
            // Set Header Title based on mode
            var todayStr = DateTime.Now.ToString("dddd, MMM dd, yyyy");
            var todayFormatted = DateTime.Today.ToString("MMM dd");
            TodayBtnText.Text = $"Today Only ({todayFormatted})";
            TodayBuyingBtn.ToolTip = $"Filter list to show items for today ({todayStr})";

            if (_initialStatus == ItemStatus.Purchased)
            {
                PageTitleText.Text = "Purchased Items Registry";
                PageSubtitleText.Text = $"Audit bought items & purchase prices • Today: {todayStr}";
                CompleteBuyingBtn.Visibility = Visibility.Collapsed;
            }
            else if (_initialStatus == ItemStatus.Unavailable)
            {
                PageTitleText.Text = "Unavailable Items (Action Required)";
                PageSubtitleText.Text = $"Resolve out-of-stock items • Today: {todayStr}";
                CompleteBuyingBtn.Visibility = Visibility.Collapsed;
            }
            else
            {
                PageSubtitleText.Text = $"Items to purchase for customer orders • Today: {todayStr}";
            }

            // Store filter
            var stores = new List<string> { "All Stores" };
            var dbStores = _db.Stores.Where(s => s.IsActive).OrderBy(s => s.Name).Select(s => s.Name).ToList();
            stores.AddRange(dbStores);
            StoreFilterCombo.ItemsSource = stores;
            StoreFilterCombo.SelectedIndex = 0;

            // Buying Day filter (if present)
            if (DayFilterCombo != null)
            {
                var scheduleSetting = SeedData.GetSetting(_db, "buying_schedule_days", "Wednesday, Thursday");
                var days = new List<string> { "All Days" };
                days.AddRange(scheduleSetting.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                DayFilterCombo.ItemsSource = days;
                DayFilterCombo.SelectedIndex = 0;
            }

            // Status filter: default to "To Buy" (pending items to purchase)
            var statusList = new List<string> { "To Buy", "Purchased", "Unavailable", "All Items" };
            StatusFilterCombo.ItemsSource = statusList;
            if (_initialStatus.HasValue)
            {
                var target = _initialStatus.Value switch
                {
                    ItemStatus.Purchased => "Purchased",
                    ItemStatus.Unavailable => "Unavailable",
                    _ => "To Buy"
                };
                var idx = statusList.IndexOf(target);
                StatusFilterCombo.SelectedIndex = idx >= 0 ? idx : 0;
            }
            else
            {
                StatusFilterCombo.SelectedIndex = 0;
            }
        }
        finally
        {
            _isInitializingFilters = false;
        }
    }

    private async void LoadData()
    {
        if (_isLoadingData) return;
        _isLoadingData = true;

        try
        {
            var storeFilter = StoreFilterCombo?.SelectedItem as string;
            var dayFilter = DayFilterCombo?.SelectedItem as string;
            var statusStr = StatusFilterCombo?.SelectedItem as string;
            var selectedDate = BuyingDatePicker?.SelectedDate;

            ItemStatus? statusFilter = null;
            if (_initialStatus.HasValue)
            {
                statusFilter = _initialStatus.Value;
            }
            else if (statusStr == "To Buy" || string.IsNullOrEmpty(statusStr)) statusFilter = ItemStatus.Pending;
            else if (statusStr == "Purchased") statusFilter = ItemStatus.Purchased;
            else if (statusStr == "Unavailable") statusFilter = ItemStatus.Unavailable;
            else if (statusStr == "All Items") statusFilter = null;

            List<OrderItem> rawItems = [];

            // Query in background thread using an isolated context to guarantee zero UI thread lag
            await Task.Run(() =>
            {
                using var bgDb = new AppDbContext();
                var svc = new OrderService(bgDb);
                rawItems = svc.GetBuyingItems(storeFilter, statusFilter, dayFilter);

                // Filter by selected date (from DatePicker or Today button)
                if (selectedDate.HasValue)
                {
                    var filterDate = selectedDate.Value.Date;
                    var dayOfWeekName = filterDate.ToString("dddd");

                    rawItems = rawItems.Where(i =>
                        i.Order != null &&
                        (
                            i.Order.CreatedAt.Date == filterDate ||
                            (string.Equals(i.Order.BuyingDay, dayOfWeekName, StringComparison.OrdinalIgnoreCase) &&
                             i.Order.CreatedAt.Date >= filterDate.AddDays(-6) &&
                             i.Order.CreatedAt.Date <= filterDate) ||
                            (i.PurchaseDate.HasValue && i.PurchaseDate.Value.Date == filterDate)
                        )
                    ).ToList();
                }
            });

            _currentRawItems = rawItems;

            // Update KPIs
            int _storesCount = rawItems.Select(i => i.Store)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase).Count();
            int _posCount = rawItems.Select(i => i.OrderId).Distinct().Count();
            double _totalQty = rawItems.Sum(i => i.Qty);
            decimal _estBudget = rawItems.Sum(i => (decimal)i.Qty * i.EstimatedPrice);

            if (StoresCountLabel != null)  StoresCountLabel.Text  = $"{_storesCount} store{(_storesCount != 1 ? "s" : "")}";
            if (POsCountLabel != null)     POsCountLabel.Text     = $"{_posCount} PO{(_posCount != 1 ? "s" : "")}";
            if (ItemsCountLabel != null)   ItemsCountLabel.Text   = $"{rawItems.Count} item{(rawItems.Count != 1 ? "s" : "")}";
            if (TotalQtyLabel != null)     TotalQtyLabel.Text     = $"{_totalQty:0.##} unit{(_totalQty != 1 ? "s" : "")}";
            if (EstBudgetLabel != null)    EstBudgetLabel.Text    = $"\u20b1{_estBudget:N2}";

            // Check if Complete Buying is fully ready or has items to act on
            var hasItems = rawItems.Count > 0;
            var completableOrders = rawItems
                .Select(i => i.Order)
                .Where(o => o != null && (o.Status == OrderStatus.READY_TO_BUY || o.Status == OrderStatus.BUYING_NOW || o.Status == OrderStatus.WAITING_CONFIRMATION))
                .Distinct()
                .Where(o => o!.Items.All(i => i.Status is ItemStatus.PURCHASED or ItemStatus.UNAVAILABLE or ItemStatus.CANCELLED))
                .ToList();

            if (CompleteBuyingBtn != null)
            {
                CompleteBuyingBtn.IsEnabled = hasItems;
                CompleteBuyingBtn.Opacity = (completableOrders.Count > 0 && !rawItems.Any(i => i.Status == ItemStatus.PENDING)) ? 1.0 : 0.85;
            }

            // Wrap rows into VMs with selection change notification
            _allRowVms = rawItems.Select(i => new BuyingItemRowVm(i) { OnSelectionChanged = UpdateSelectedSummary }).ToList();

            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_allRowVms);
            view.GroupDescriptions.Clear();
            if (_groupByStore)
            {
                view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(BuyingItemRowVm.StoreName)));
            }

            if (MainBuyingGrid != null)
            {
                MainBuyingGrid.ItemsSource = view;
            }

            // Apply in-memory search if already typed
            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                ApplySearchFilter();
            }
            else
            {
                if (EmptyStatePanel != null)
                {
                    EmptyStatePanel.Visibility = _allRowVms.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                    if (EmptyStateMainText != null && EmptyStateSubText != null)
                    {
                        if (statusStr == "Purchased")
                        {
                            EmptyStateMainText.Text = "No purchased items recorded yet.";
                            EmptyStateSubText.Text = "Items marked as purchased will appear here.";
                        }
                        else if (statusStr == "Unavailable")
                        {
                            EmptyStateMainText.Text = "No unavailable items.";
                            EmptyStateSubText.Text = "Items marked out of stock or unavailable will appear here.";
                        }
                        else
                        {
                            EmptyStateMainText.Text = "All shopping completed!";
                            EmptyStateSubText.Text = "There are no pending items on your shopping checklist.";
                        }
                    }
                }
            }
            UpdateSelectedSummary();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BuyingListPage] LoadData error: {ex.Message}");
        }
        finally
        {
            _isLoadingData = false;
        }
    }

    private void OnGoToOrdersClick(object sender, RoutedEventArgs e)
    {
        _navigate?.Invoke("Orders");
    }

    private void UpdateSelectedSummary()
    {
        var selected = _allRowVms.Where(r => r.IsSelected).ToList();
        var totalEst = selected.Sum(r => (decimal)r.Item.Qty * r.EstimatedPrice);
        SelectedSummaryLabel.Text = $"{selected.Count} of {_allRowVms.Count} selected (Est: ₱{totalEst:N2})";
        PrintBuyingListBtn.IsEnabled = _allRowVms.Count > 0;

        // Bulk action buttons active only when 1+ rows selected
        var hasSelection = selected.Count > 0;
        if (BulkPurchasedBtn != null)
        {
            BulkPurchasedBtn.IsEnabled = hasSelection;
            BulkPurchasedBtn.Opacity = hasSelection ? 1.0 : 0.5;
        }
        if (BulkUnavailableBtn != null)
        {
            BulkUnavailableBtn.IsEnabled = hasSelection;
            BulkUnavailableBtn.Opacity = hasSelection ? 1.0 : 0.5;
        }
        if (BulkDeleteBtn != null)
        {
            BulkDeleteBtn.IsEnabled = hasSelection;
            BulkDeleteBtn.Opacity = hasSelection ? 1.0 : 0.5;
        }
    }

    private void UpdateStatsAndCounters()
    {
        int _sc = _currentRawItems.Select(i => i.Store)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase).Count();
        int _pc = _currentRawItems.Select(i => i.OrderId).Distinct().Count();
        double _tq = _currentRawItems.Sum(i => i.Qty);
        decimal _eb = _currentRawItems.Sum(i => (decimal)i.Qty * i.EstimatedPrice);
        decimal _ac = _currentRawItems.Where(i => i.ActualPrice.HasValue)
            .Sum(i => (decimal)i.Qty * i.ActualPrice!.Value);

        if (StoresCountLabel != null)  StoresCountLabel.Text  = $"{_sc} store{(_sc != 1 ? "s" : "")}";
        if (POsCountLabel != null)     POsCountLabel.Text     = $"{_pc} PO{(_pc != 1 ? "s" : "")}";
        if (ItemsCountLabel != null)   ItemsCountLabel.Text   = $"{_currentRawItems.Count} item{(_currentRawItems.Count != 1 ? "s" : "")}";
        if (TotalQtyLabel != null)     TotalQtyLabel.Text     = $"{_tq:0.##} unit{(_tq != 1 ? "s" : "")}";
        if (EstBudgetLabel != null)    EstBudgetLabel.Text    = $"\u20b1{_eb:N2}";

        var completableOrders = _currentRawItems
            .Select(i => i.Order)
            .Where(o => o != null && (o.Status == OrderStatus.READY_TO_BUY || o.Status == OrderStatus.BUYING_NOW || o.Status == OrderStatus.WAITING_CONFIRMATION))
            .Distinct()
            .Where(o => o!.Items.All(i => i.Status is ItemStatus.PURCHASED or ItemStatus.UNAVAILABLE or ItemStatus.CANCELLED))
            .ToList();

        CompleteBuyingBtn.IsEnabled = completableOrders.Count > 0;
        CompleteBuyingBtn.Opacity = completableOrders.Count > 0 ? 1.0 : 0.5;

        UpdateSelectedSummary();
    }

    private void OnBulkDeleteSelectedClick(object sender, RoutedEventArgs e)
    {
        var selected = _allRowVms.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0) return;

        var confirm = MessageBox.Show(
            $"Are you sure you want to permanently delete the {selected.Count} selected items from their orders?",
            "Confirm Delete Items",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        var buyingSvc = new BuyingStateService(_db);
        var impactedOrderIds = new HashSet<int>();

        try
        {
            foreach (var row in selected)
            {
                var item = row.Item;
                impactedOrderIds.Add(item.OrderId);

                var dbItem = _db.OrderItems.Find(item.Id);
                if (dbItem != null)
                {
                    _db.OrderItems.Remove(dbItem);
                }
            }

            _db.SaveChanges();

            foreach (var orderId in impactedOrderIds)
            {
                buyingSvc.EvaluateOrderBuyingProgress(orderId);
            }

            LoadData();
            MessageBox.Show($"Successfully removed {selected.Count} items.", "Items Deleted", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error deleting items: {ex.Message}", "Delete Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnBulkMarkPurchasedClick(object sender, RoutedEventArgs e)
    {
        var selected = _allRowVms.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0) return;

        var selectedIds = selected.Select(r => r.Item.Id).ToList();
        var buyingSvc = new BuyingStateService(_db);

        var (ok, msg, count) = buyingSvc.BulkMarkItemsPurchased(selectedIds);
        if (!ok)
        {
            MessageBox.Show(msg, "Bulk Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        OrderEventBus.NotifyDataRefreshed();
        LoadData();
    }

    private void OnBulkMarkUnavailableClick(object sender, RoutedEventArgs e)
    {
        var selected = _allRowVms.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0) return;

        var selectedIds = selected.Select(r => r.Item.Id).ToList();
        var buyingSvc = new BuyingStateService(_db);

        var (ok, msg, count) = buyingSvc.BulkMarkItemsUnavailable(selectedIds, "Out of Stock");
        if (!ok)
        {
            MessageBox.Show(msg, "Bulk Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        OrderEventBus.NotifyDataRefreshed();
        LoadData();
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = SearchBox.Text?.Trim() ?? "";
        ApplySearchFilter();
    }

    private void OnClearSearch(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        _searchText = "";
        ApplySearchFilter();
    }

    private void ApplySearchFilter()
    {
        if (MainBuyingGrid == null || _allRowVms == null) return;
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_allRowVms);
        if (view == null) return;

        if (string.IsNullOrWhiteSpace(_searchText))
        {
            view.Filter = null;
        }
        else
        {
            var q = _searchText.ToLowerInvariant();
            view.Filter = obj =>
            {
                if (obj is BuyingItemRowVm r)
                {
                    return (!string.IsNullOrEmpty(r.ProductName) && r.ProductName.ToLowerInvariant().Contains(q)) ||
                           (!string.IsNullOrEmpty(r.StoreName) && r.StoreName.ToLowerInvariant().Contains(q)) ||
                           (!string.IsNullOrEmpty(r.CustomerDisplay) && r.CustomerDisplay.ToLowerInvariant().Contains(q)) ||
                           (!string.IsNullOrEmpty(r.Item.PurchaseNotes) && r.Item.PurchaseNotes.ToLowerInvariant().Contains(q)) ||
                           (r.Order?.OrderNo != null && r.Order.OrderNo.ToLowerInvariant().Contains(q));
                }
                return true;
            };
        }

        int visibleCount = 0;
        foreach (var _ in view) visibleCount++;
        if (EmptyStatePanel != null)
        {
            EmptyStatePanel.Visibility = visibleCount == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        UpdateSelectedSummary();
    }

    private void OnSelectAllClick(object sender, RoutedEventArgs e)
    {
        foreach (var r in _allRowVms) r.IsSelected = true;
        UpdateSelectedSummary();
    }

    private void OnDeselectAllClick(object sender, RoutedEventArgs e)
    {
        foreach (var r in _allRowVms) r.IsSelected = false;
        UpdateSelectedSummary();
    }

    private void OnSelectPendingClick(object sender, RoutedEventArgs e)
    {
        foreach (var r in _allRowVms) r.IsSelected = r.IsPending;
        UpdateSelectedSummary();
    }

    private void OnFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initialized && !_isInitializingFilters) LoadData();
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => LoadData();

    // ══ TOP ACTION 2: [ TODAY'S BUYING ] TOGGLE & DATE PICKER ═══════════════
    private void OnTodayBuyingToggle(object sender, RoutedEventArgs e)
    {
        if (_todayFilterActive)
        {
            _todayFilterActive = false;
            if (BuyingDatePicker != null) BuyingDatePicker.SelectedDate = null;
        }
        else
        {
            _todayFilterActive = true;
            if (BuyingDatePicker != null) BuyingDatePicker.SelectedDate = DateTime.Today;
        }
        NavProps.SetIsActive(TodayBuyingBtn, _todayFilterActive);
        LoadData();
    }

    private void OnBuyingDateChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_initialized) return;

        var selected = BuyingDatePicker?.SelectedDate;
        if (ClearDateBtn != null)
            ClearDateBtn.Visibility = selected.HasValue ? Visibility.Visible : Visibility.Collapsed;

        _todayFilterActive = selected.HasValue && selected.Value.Date == DateTime.Today;
        NavProps.SetIsActive(TodayBuyingBtn, _todayFilterActive);

        LoadData();
    }

    private void OnClearDateFilter(object sender, RoutedEventArgs e)
    {
        if (BuyingDatePicker != null)
            BuyingDatePicker.SelectedDate = null;
    }

    // ══ TOP ACTION 3: [ GROUP BY STORE ] TOGGLE ══════════════════════════════
    private void OnGroupByStoreToggle(object sender, RoutedEventArgs e)
    {
        _groupByStore = !_groupByStore;
        NavProps.SetIsActive(GroupByStoreBtn, _groupByStore);
        if (MainBuyingGrid != null && _allRowVms != null)
        {
            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_allRowVms);
            if (view != null)
            {
                view.GroupDescriptions.Clear();
                if (_groupByStore)
                {
                    view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(BuyingItemRowVm.StoreName)));
                }
            }
        }
    }

    // ══ TOP ACTION 3.5: [ START BUYING ] — READY TO BUY → BUYING NOW ═══════
    private void OnStartBuyingClick(object sender, RoutedEventArgs e)
    {
        // Gather distinct Waiting to Buy orders visible in the current filter
        var readyOrders = _currentRawItems
            .Select(i => i.Order)
            .Where(o => o != null && (o.Status == OrderStatus.WAITING_CONFIRMATION || o.Status == OrderStatus.READY_TO_BUY))
            .DistinctBy(o => o!.Id)
            .ToList();

        if (readyOrders.Count == 0)
        {
            MessageBox.Show(
                "No Waiting to Buy orders found in the current filter.\n" +
                "Create orders or set orders to Waiting to Buy status first.",
                "Start Buying", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var statusSvc = new OrderStatusService(_db);
        int started = 0;
        var errors = new System.Collections.Generic.List<string>();

        foreach (var order in readyOrders)
        {
            var (ok, msg) = statusSvc.Transition(order!.Id, OrderStatus.BUYING_NOW);
            if (ok) started++;
            else errors.Add(msg);
        }

        var resultMsg = started > 0
            ? $"Started for {started} order{(started != 1 ? "s" : "")}.\nStatus changed to Buying."
            : "Could not start buying for any orders.";
        if (errors.Count > 0)
            resultMsg += $"\n\nNote: {string.Join("; ", errors.Take(3))}";

        MessageBox.Show(resultMsg, "Start Buying", MessageBoxButton.OK,
            started > 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);

        LoadData();
    }

    // ══ TOP ACTION 4: [ PRINT BUYING LIST ] ═══════════════════════════════════
    private void OnPrintBuyingListClick(object sender, RoutedEventArgs e)
    {
        var selectedRows = _allRowVms.Where(r => r.IsSelected).ToList();
        List<OrderItem> itemsToPrint;

        if (selectedRows.Count > 0)
        {
            itemsToPrint = selectedRows.Select(r => r.Item).ToList();
        }
        else if (_currentRawItems.Count > 0)
        {
            itemsToPrint = _currentRawItems;
        }
        else
        {
            MessageBox.Show("All shopping items have already been purchased. There are no pending checklist items to print.", "Print Checklist",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Unless user is explicitly viewing Purchased history, only print pending items from active orders
        var statusStr = StatusFilterCombo.SelectedItem as string;
        if (statusStr != "Purchased" && _initialStatus != ItemStatus.Purchased)
        {
            itemsToPrint = itemsToPrint
                .Where(i => i.Status == ItemStatus.Pending &&
                            i.Order?.Status != OrderStatus.BOUGHT &&
                            i.Order?.Status != OrderStatus.COMPLETED &&
                            i.Order?.Status != OrderStatus.CANCELLED)
                .ToList();

            if (itemsToPrint.Count == 0)
            {
                MessageBox.Show("All items have already been purchased or completed. There are no pending checklist items to print.", "Print Checklist",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }

        var store = StoreFilterCombo.SelectedItem as string ?? "All Stores";
        var printDate = BuyingDatePicker?.SelectedDate ?? DateTime.Today;
        var day = printDate.ToString("dddd");

        var savedPrinter = SeedData.GetSetting(_db, "default_printer", "");
        var dlg = new Dialogs.PrintPreviewDialog(
            itemsToPrint,
            storeName: store,
            buyingDay: day,
            title: $"Buying List Print Preview ({itemsToPrint.Count} items)",
            targetPrinter: savedPrinter,
            printDate: printDate);
        dlg.Owner = Window.GetWindow(this);
        dlg.ShowDialog();
    }

    // ══ TOP ACTION 5: [ COMPLETE BUYING ] ═════════════════════════════════════
    private void OnCompleteBuyingClick(object sender, RoutedEventArgs e)
    {
        var pendingCount = _currentRawItems.Count(i => i.Status != ItemStatus.Purchased && i.Status != ItemStatus.Unavailable && i.Status != ItemStatus.Cancelled);
        if (pendingCount > 0)
        {
            MessageBox.Show(
                $"Cannot finish buying yet.\n\n{pendingCount} item{(pendingCount != 1 ? "s remain" : " remains")} that {(pendingCount != 1 ? "are" : "is")} not marked as Bought or Not available.\n\nPlease mark all items as Bought or Not available first.",
                "Finish Buying Blocked",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var buyingSvc = new BuyingStateService(_db);
        var (ok, msg, count) = buyingSvc.CompleteBuyingForReadyOrders();
        MessageBox.Show(msg, "Finish Buying", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        LoadData();
    }

    // ══ TOP MORE MENU [ ••• ] ════════════════════════════════════════════════
    private void OnMoreActionsClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var menu = new ContextMenu();

        var printItem = new MenuItem { Header = "Print Buying Sheet" };
        printItem.Click += (_, _) => OnPrintBuyingListClick(null!, null!);
        menu.Items.Add(printItem);

        var exportItem = new MenuItem { Header = "Export CSV" };
        exportItem.Click += (_, _) => OnExportCsv(null!, null!);
        menu.Items.Add(exportItem);

        menu.Items.Add(new Separator());

        var refreshItem = new MenuItem { Header = "Refresh Data" };
        refreshItem.Click += (_, _) => LoadData();
        menu.Items.Add(refreshItem);

        menu.PlacementTarget = btn;
        menu.IsOpen = true;
    }

    private void OnPrintBuyingSheet(object sender, RoutedEventArgs e) => OnPrintBuyingListClick(sender, e);

    private void OnExportCsv(object sender, RoutedEventArgs e)
    {
        var itemsToExport = _allRowVms.Any(r => r.IsSelected)
            ? _allRowVms.Where(r => r.IsSelected).Select(r => r.Item).ToList()
            : _currentRawItems;

        if (itemsToExport.Count == 0)
        {
            MessageBox.Show("No buying items to export.", "Export CSV", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv",
                FileName = $"AntiquePasabuy_BuyingList_{DateTime.Now:yyyyMMdd_HHmm}.csv"
            };
            if (dlg.ShowDialog() == true)
            {
                var sb = new System.Text.StringBuilder();

                // Business Metadata Header Block
                sb.AppendLine("\"ANTIQUE PASABUY POS — BUYING & PROCUREMENT MANIFEST\"");
                sb.AppendLine($"\"Export Date:\",\"{DateTime.Now:yyyy-MM-dd hh:mm tt}\"");
                sb.AppendLine($"\"Total Items:\",\"{itemsToExport.Count}\"");
                sb.AppendLine();

                sb.AppendLine("Store,Order No,Customer Name,Product Item,Quantity,Unit,Estimated Price,Actual Price,Price Diff,Item Status,Notes");
                decimal sumEst = 0, sumAct = 0;
                double sumQty = 0;

                foreach (var i in itemsToExport)
                {
                    sumQty += i.Qty;
                    sumEst += i.EstimatedPrice;
                    if (i.ActualPrice.HasValue) sumAct += i.ActualPrice.Value;

                    var diff = i.ActualPrice.HasValue ? (i.ActualPrice.Value - i.EstimatedPrice).ToString("F2") : "0.00";
                    sb.AppendLine($"\"{i.Store}\",\"{i.Order?.OrderNo}\",\"{i.Order?.Customer?.Name}\",\"{i.ProductName}\",{i.Qty:0.##},\"{i.Unit}\",{i.EstimatedPrice:F2},{(i.ActualPrice.HasValue ? i.ActualPrice.Value.ToString("F2") : "")},{diff},\"{i.Status}\",\"{i.PurchaseNotes}\"");
                }

                // Summary Totals Row
                sb.AppendLine();
                var totalDiff = sumAct > 0 ? (sumAct - sumEst).ToString("F2") : "0.00";
                sb.AppendLine($"\"TOTALS\",,,,\"{sumQty:0.##}\",,\"{sumEst:F2}\",\"{(sumAct > 0 ? sumAct.ToString("F2") : "")}\",\"{totalDiff}\",,");

                // Write with UTF-8 BOM for Microsoft Excel compatibility
                System.IO.File.WriteAllText(dlg.FileName, sb.ToString(), new System.Text.UTF8Encoding(true));
                MessageBox.Show($"Exported {itemsToExport.Count} items to {dlg.FileName} successfully!", "Export Buying List", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ══ ROW ACTION: [ PURCHASED ] ════════════════════════════════════════════
    private void OnMarkPurchasedClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is BuyingItemRowVm row)
        {
            var item = row.Item;

            var dialog = new Window
            {
                Title = $"Bought — {item.ProductName}",
                Width = 380,
                Height = 360,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                Background = Brushes.White
            };

            var stack = new StackPanel { Margin = new Thickness(24) };
            stack.Children.Add(new TextBlock { Text = item.ProductName, FontWeight = FontWeights.Bold, FontSize = 15, Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59)), Margin = new Thickness(0, 0, 0, 12) });

            stack.Children.Add(new TextBlock { Text = "Purchased Quantity", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)) });
            var qtyBox = new TextBox { Text = item.Qty.ToString("0.#"), Margin = new Thickness(0, 4, 0, 10), Padding = new Thickness(6, 4, 6, 4) };
            stack.Children.Add(qtyBox);

            stack.Children.Add(new TextBlock { Text = $"Actual Price (₱) — Est: ₱{item.EstimatedPrice:N2}", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)) });
            var priceBox = new TextBox { Text = (item.ActualPrice ?? item.EstimatedPrice).ToString("F2"), Margin = new Thickness(0, 4, 0, 10), Padding = new Thickness(6, 4, 6, 4), FontWeight = FontWeights.Bold };
            dialog.Loaded += (_, _) => { priceBox.Focus(); priceBox.SelectAll(); };
            stack.Children.Add(priceBox);

            stack.Children.Add(new TextBlock { Text = "Store Purchased From", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)) });
            var storeBox = new TextBox { Text = item.Store, Margin = new Thickness(0, 4, 0, 10), Padding = new Thickness(6, 4, 6, 4) };
            stack.Children.Add(storeBox);

            stack.Children.Add(new TextBlock { Text = "Receipt # / Notes (Optional)", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)) });
            var receiptBox = new TextBox { Text = item.ReceiptNo ?? "", Margin = new Thickness(0, 4, 0, 16), Padding = new Thickness(6, 4, 6, 4) };
            stack.Children.Add(receiptBox);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancelBtn = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(14, 6, 14, 6) };
            cancelBtn.Click += (_, _) => dialog.Close();

            var saveBtn = new Button
            {
                Content = "Save Purchase",
                Background = new SolidColorBrush(Color.FromRgb(5, 150, 105)),
                Foreground = Brushes.White,
                Padding = new Thickness(16, 6, 16, 6),
                FontWeight = FontWeights.Bold
            };
            saveBtn.Click += (_, _) =>
            {
                if (!double.TryParse(qtyBox.Text.Trim(), out var pQty) || pQty <= 0)
                {
                    MessageBox.Show("Please enter a valid purchased quantity.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (!decimal.TryParse(priceBox.Text.Trim(), out var actualP) || actualP < 0)
                {
                    MessageBox.Show("Please enter a valid actual price.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var svc = new OrderService(_db);
                var (ok, msg) = svc.MarkItemPurchased(item.Id, pQty, actualP, receiptBox.Text, storeBox.Text);
                if (ok)
                {
                    dialog.Close();
                    LoadData();
                }
                else
                {
                    MessageBox.Show(msg, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            btnPanel.Children.Add(cancelBtn);
            btnPanel.Children.Add(saveBtn);
            stack.Children.Add(btnPanel);

            dialog.Content = stack;
            dialog.ShowDialog();
        }
    }

    // ══ ROW ACTION: [ UNAVAILABLE ] ══════════════════════════════════════════
    private void OnMarkUnavailableClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is BuyingItemRowVm row)
        {
            var item = row.Item;

            var dialog = new Window
            {
                Title = $"Mark Unavailable — {item.ProductName}",
                Width = 380,
                Height = 320,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                Background = Brushes.White
            };

            var stack = new StackPanel { Margin = new Thickness(24) };
            stack.Children.Add(new TextBlock { Text = item.ProductName, FontWeight = FontWeights.Bold, FontSize = 15, Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59)), Margin = new Thickness(0, 0, 0, 12) });

            stack.Children.Add(new TextBlock { Text = "Reason for Unavailability", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)) });
            var reasonCombo = new ComboBox
            {
                ItemsSource = new List<string> { "Out of Stock", "Store Closed", "Wrong Product", "Price Changed", "Other" },
                SelectedIndex = 0,
                Margin = new Thickness(0, 4, 0, 12)
            };
            stack.Children.Add(reasonCombo);

            stack.Children.Add(new TextBlock { Text = "Notes / Customer Notification", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)) });
            var notesBox = new TextBox { Margin = new Thickness(0, 4, 0, 16), Padding = new Thickness(6, 4, 6, 4), Height = 55, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true };
            stack.Children.Add(notesBox);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancelBtn = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(14, 6, 14, 6) };
            cancelBtn.Click += (_, _) => dialog.Close();

            var confirmBtn = new Button
            {
                Content = "Mark Unavailable",
                Background = new SolidColorBrush(Color.FromRgb(220, 38, 38)),
                Foreground = Brushes.White,
                Padding = new Thickness(16, 6, 16, 6),
                FontWeight = FontWeights.Bold
            };
            confirmBtn.Click += (_, _) =>
            {
                var reason = reasonCombo.SelectedItem?.ToString() ?? "Out of Stock";
                var svc = new OrderService(_db);
                var (ok, msg) = svc.MarkItemUnavailable(item.Id, reason, notesBox.Text);
                if (ok)
                {
                    dialog.Close();
                    LoadData();
                }
                else
                {
                    MessageBox.Show(msg, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            btnPanel.Children.Add(cancelBtn);
            btnPanel.Children.Add(confirmBtn);
            stack.Children.Add(btnPanel);

            dialog.Content = stack;
            dialog.ShowDialog();
        }
    }

    // ══ CONTEXTUAL ACTION: [ RESOLVE ] (For Unavailable Items) ═══════════════
    private void OnResolveUnavailableClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not BuyingItemRowVm row) return;
        var item = row.Item;

        var menu = new ContextMenu();

        // 1. Try Another Store
        var tryStoreItem = new MenuItem { Header = "Try Another Store" };
        tryStoreItem.Click += (_, _) => TryAnotherStore(item);
        menu.Items.Add(tryStoreItem);

        // 2. Contact Customer
        var contactItem = new MenuItem { Header = "Contact Customer" };
        contactItem.Click += (_, _) => ContactCustomer(item);
        menu.Items.Add(contactItem);

        menu.Items.Add(new Separator());

        // 3. Remove Item
        var removeItem = new MenuItem { Header = "Remove Item From Order" };
        removeItem.Click += (_, _) => RemoveItem(item);
        menu.Items.Add(removeItem);

        menu.PlacementTarget = btn;
        menu.IsOpen = true;
    }

    private void TryAnotherStore(OrderItem item)
    {
        var stores = _db.Stores.Where(s => s.IsActive).OrderBy(s => s.Name).Select(s => s.Name).ToList();
        var dialog = new Window
        {
            Title = $"Switch Store — {item.ProductName}",
            Width = 360,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            Background = Brushes.White
        };

        var stack = new StackPanel { Margin = new Thickness(20) };
        stack.Children.Add(new TextBlock { Text = $"Current Store: {item.Store}", FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)), Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new TextBlock { Text = "Select Alternative Store", FontSize = 12, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });

        var combo = new ComboBox { ItemsSource = stores, SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 16) };
        stack.Children.Add(combo);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancelBtn = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0) };
        cancelBtn.Click += (_, _) => dialog.Close();

        var saveBtn = new Button { Content = "Confirm Reassignment", Background = new SolidColorBrush(Color.FromRgb(0, 53, 145)), Foreground = Brushes.White, FontWeight = FontWeights.Bold };
        saveBtn.Click += (_, _) =>
        {
            var newStore = combo.SelectedItem?.ToString() ?? "General";
            var dbItem = _db.OrderItems.Find(item.Id);
            if (dbItem != null)
            {
                dbItem.Store = newStore;
                dbItem.Status = ItemStatus.Pending; // Re-queues for buying!
                dbItem.UnavailableReason = null;
                _db.SaveChanges();
                dialog.Close();
                LoadData();
                MessageBox.Show($"Item re-queued under store '{newStore}'!", "Store Reassigned", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        };

        btnPanel.Children.Add(cancelBtn);
        btnPanel.Children.Add(saveBtn);
        stack.Children.Add(btnPanel);
        dialog.Content = stack;
        dialog.ShowDialog();
    }

    private void ContactCustomer(OrderItem item)
    {
        var cust = item.Order?.Customer;
        var custName = cust?.Name ?? "Walk-in";
        var phone = cust?.Phone ?? "No phone on file";
        var address = cust?.Address ?? "No address";
        var reason = item.UnavailableReason ?? "Out of Stock";

        MessageBox.Show(
            $"Customer: {custName}\nPhone: {phone}\nAddress: {address}\n\nItem: {item.ProductName}\nIssue: {reason}\nOrder: {item.Order?.OrderNo}",
            "Customer Contact Information",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void RemoveItem(OrderItem item)
    {
        var res = MessageBox.Show(
            $"Remove '{item.ProductName}' from Order {item.Order?.OrderNo}?",
            "Confirm Item Removal", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (res == MessageBoxResult.Yes)
        {
            var dbItem = _db.OrderItems.Find(item.Id);
            if (dbItem != null)
            {
                _db.OrderItems.Remove(dbItem);
                _db.SaveChanges();
                LoadData();
                MessageBox.Show($"Item removed from order.", "Item Removed", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }

    // ══ ROW ACTION: [ VIEW ORDER ] & [ RECEIPT ] ═════════════════════════════
    private void OnViewOrderClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is BuyingItemRowVm row && row.Order != null)
        {
            var dlg = new Dialogs.OrderDetailDialog(_db, row.Order, _user);
            dlg.Owner = Window.GetWindow(this);
            if (dlg.ShowDialog() == true) LoadData();
        }
    }

    private void OnViewPurchaseClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is BuyingItemRowVm row)
        {
            var item = row.Item;
            var actual = item.ActualPrice.HasValue ? $"₱{item.ActualPrice.Value:N2}" : "N/A";
            var est = $"₱{item.EstimatedPrice:N2}";
            var diff = item.PriceDifference.ToString("+₱0.00;-₱0.00;₱0.00");
            var receipt = string.IsNullOrWhiteSpace(item.ReceiptNo) ? "None" : item.ReceiptNo;
            var store = item.Store;
            var date = item.PurchaseDate.HasValue ? item.PurchaseDate.Value.ToString("yyyy-MM-dd hh:mm tt") : "N/A";

            MessageBox.Show(
                $"Purchase Details:\n\nProduct: {item.ProductName}\nStore: {store}\nQty: {item.Qty} {item.Unit}\nEst. Price: {est}\nAct. Price: {actual}\nVariance: {diff}\nReceipt #: {receipt}\nDate: {date}",
                "Purchase Audit Information",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // ══ BARCODE SCANNER ══════════════════════════════════════════════════════
    private void OnToggleBarcodeScanner(object sender, RoutedEventArgs e)
    {
        OpenCameraScanner();
    }

    private void OnCloseBarcodeScanner(object sender, RoutedEventArgs e)
    {
        // Legacy — no-op for old scanner bar (now uses camera dialog)
    }

    private void OnBarcodeInputChanged(object sender, TextChangedEventArgs e)
    {
        // Legacy — no-op
    }

    private void OnBarcodeInputKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Legacy — no-op
    }

    /// <summary>
    /// Opens the camera-based BarcodeScannerDialog in Buying List mode.
    /// When a barcode is detected, it matches against pending buying items and marks them purchased.
    /// </summary>
    private void OpenCameraScanner()
    {
        var dlg = new Dialogs.BarcodeScannerDialog(_db, OnBarcodeDetectedExtended, "Barcode Scanner — Buying List");
        dlg.Owner = Window.GetWindow(this);
        dlg.ShowDialog();
        // Reload after scanner closes in case items were marked purchased
        LoadData();
    }

    /// <summary>
    /// Called by BarcodeScannerDialog (BuyingList mode) each time a barcode is detected.
    /// Matches the barcode against pending OrderItems by:
    ///   1. Direct barcode field match on the linked Product
    ///   2. Detected/registered product name bidirectional matching
    ///   3. Token/word matching for brand and product keywords
    /// If matches are found, marks them Purchased using detected or user-entered actual price.
    /// </summary>
    private void OnBarcodeDetected(string barcode)
    {
        OnBarcodeDetectedExtended(barcode, null, null);
    }

    private void OnBarcodeDetectedExtended(string barcode, string? detectedProductName = null, decimal? detectedPrice = null)
    {
        if (string.IsNullOrWhiteSpace(barcode)) return;

        var cleanBarcode = barcode.Trim();
        var q = cleanBarcode.ToLowerInvariant();

        // 1. Try matching by barcode against registered Product in DB
        var matchedProduct = _db.Products.AsNoTracking()
            .FirstOrDefault(p => p.IsActive && p.Barcode != null && (p.Barcode == cleanBarcode || p.Barcode.ToLower() == q));

        var targetName = matchedProduct?.Name ?? detectedProductName;

        List<BuyingItemRowVm> matches = [];

        // Match using product name if available (from DB product or detected/entered name)
        if (!string.IsNullOrWhiteSpace(targetName))
        {
            var pName = targetName.Trim().ToLowerInvariant();
            // Match if either contains the other (e.g. "Century Tuna" vs "Century Tuna Flakes in Oil 155g")
            matches = _allRowVms
                .Where(r => r.IsPending && (
                    r.ProductName.ToLowerInvariant().Contains(pName) ||
                    pName.Contains(r.ProductName.ToLowerInvariant())
                ))
                .ToList();

            // If still no match, match by significant words/tokens (e.g. brand + product)
            if (matches.Count == 0)
            {
                var tokens = pName.Split([' ', ',', '-', '/', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
                    .Where(t => t.Length >= 4)
                    .ToList();

                if (tokens.Count > 0)
                {
                    matches = _allRowVms
                        .Where(r => r.IsPending && tokens.Any(t => r.ProductName.ToLowerInvariant().Contains(t)))
                        .ToList();
                }
            }
        }

        // If still no match, fallback to raw barcode string search in row product name
        if (matches.Count == 0)
        {
            matches = _allRowVms
                .Where(r => r.IsPending && r.ProductName.ToLowerInvariant().Contains(q))
                .ToList();
        }

        if (matches.Count == 0)
        {
            // Nothing matched in pending buying items
            return;
        }

        // Mark matched items purchased
        var svc = new OrderService(_db);
        var buyingSvc = new BuyingStateService(_db);
        var impactedOrderIds = new HashSet<int>();

        foreach (var row in matches)
        {
            var item = row.Item;
            // Use detected or entered price if available; fallback to actual or estimated price
            var actualPrice = (detectedPrice.HasValue && detectedPrice.Value > 0)
                ? detectedPrice.Value
                : (item.ActualPrice ?? item.EstimatedPrice);

            svc.MarkItemPurchased(item.Id, item.Qty, actualPrice);
            impactedOrderIds.Add(item.OrderId);
        }

        foreach (var orderId in impactedOrderIds)
            buyingSvc.EvaluateOrderBuyingProgress(orderId);

        // Refresh the buying list so the user sees the item as Purchased immediately
        Dispatcher.InvokeAsync(LoadData);
    }
}

public class StoreBuyingGroup
{
    public string StoreName { get; }
    public List<BuyingItemRowVm> Items { get; }
    public int ItemCount => Items.Count;
    public decimal TotalEstBudget => Items.Sum(i => (decimal)i.Item.Qty * i.EstimatedPrice);
    public int PurchasedCount => Items.Count(i => i.IsPurchased);
    public int PendingCount => Items.Count(i => i.IsPending);
    public string ProgressSummary => $"{PurchasedCount} of {ItemCount} acquired";

    public StoreBuyingGroup(string storeName, List<BuyingItemRowVm> items)
    {
        StoreName = storeName;
        Items = items;
    }
}

public class BuyingItemRowVm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public OrderItem Item { get; }
    public Action? OnSelectionChanged { get; set; }

    private bool _isSelected = true;
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

    private static readonly SolidColorBrush GrayBrush = Freeze(new SolidColorBrush(Color.FromRgb(100, 116, 139)));
    private static readonly SolidColorBrush RedBrush = Freeze(new SolidColorBrush(Color.FromRgb(220, 38, 38)));
    private static readonly SolidColorBrush GreenBrush = Freeze(new SolidColorBrush(Color.FromRgb(5, 150, 105)));

    private static readonly SolidColorBrush PurchasedBg = Freeze(new SolidColorBrush(Color.FromRgb(209, 250, 229)));
    private static readonly SolidColorBrush PurchasedFg = Freeze(new SolidColorBrush(Color.FromRgb(6, 95, 70)));
    private static readonly SolidColorBrush UnavailBg = Freeze(new SolidColorBrush(Color.FromRgb(254, 226, 226)));
    private static readonly SolidColorBrush UnavailFg = Freeze(new SolidColorBrush(Color.FromRgb(153, 27, 27)));
    private static readonly SolidColorBrush PartialBg = Freeze(new SolidColorBrush(Color.FromRgb(224, 231, 255)));
    private static readonly SolidColorBrush PartialFg = Freeze(new SolidColorBrush(Color.FromRgb(55, 48, 163)));
    private static readonly SolidColorBrush PendingBg = Freeze(new SolidColorBrush(Color.FromRgb(254, 243, 199)));
    private static readonly SolidColorBrush PendingFg = Freeze(new SolidColorBrush(Color.FromRgb(146, 64, 14)));

    private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

    public string StoreName => string.IsNullOrWhiteSpace(Item.Store) ? "General Store" : Item.Store;
    public string ProductName => Item.ProductName;
    public bool HasNotes => !string.IsNullOrWhiteSpace(Item.PurchaseNotes);
    public string NotesDisplay => HasNotes ? $"Note: {Item.PurchaseNotes}" : "";
    public string QtyDisplay => Item.PurchasedQty.HasValue ? $"{Item.PurchasedQty.Value:0.#} / {Item.Qty:0.#} {Item.Unit}" : $"{Item.Qty:0.#} {Item.Unit}";
    public string CustomerDisplay => $"{Item.Order?.Customer?.Name ?? "Walk-in"} ({Item.Order?.OrderNo})";
    public Order? Order => Item.Order;
    public DateTime? OrderDate => Item.Order?.CreatedAt;
    public string OrderDateDisplay => Item.Order != null ? Item.Order.CreatedAt.ToString("MMM dd, yyyy") : "—";
    public string OrderTimeDisplay => Item.Order != null ? Item.Order.CreatedAt.ToString("hh:mm tt") : "";
    public decimal EstimatedPrice => Item.EstimatedPrice;

    public bool IsPurchased => Item.Status == ItemStatus.Purchased;
    public bool IsUnavailable => Item.Status == ItemStatus.Unavailable;
    public bool IsPending => Item.Status == ItemStatus.Pending;

    public string ActualPriceDisplay => Item.ActualPrice.HasValue ? $"₱{Item.ActualPrice.Value:N2}" : "—";

    public string DiffDisplay
    {
        get
        {
            if (!Item.ActualPrice.HasValue) return "—";
            var diff = Item.ActualPrice.Value - Item.EstimatedPrice;
            if (diff > 0) return $"+₱{diff:N2}";
            if (diff < 0) return $"-₱{Math.Abs(diff):N2}";
            return "₱0.00";
        }
    }

    public Brush DiffBrush
    {
        get
        {
            if (!Item.ActualPrice.HasValue) return GrayBrush;
            var diff = Item.ActualPrice.Value - Item.EstimatedPrice;
            if (diff > 0) return RedBrush;
            if (diff < 0) return GreenBrush;
            return GrayBrush;
        }
    }

    public string StatusText => Item.Status switch
    {
        ItemStatus.Purchased => "PURCHASED",
        ItemStatus.Unavailable => $"UNAVAILABLE ({Item.UnavailableReason ?? "Out of Stock"})",
        _ => "PENDING"
    };

    public Brush StatusBgBrush => Item.Status switch
    {
        ItemStatus.Purchased => PurchasedBg,
        ItemStatus.Unavailable => UnavailBg,
        _ => PendingBg
    };

    public Brush StatusFgBrush => Item.Status switch
    {
        ItemStatus.Purchased => PurchasedFg,
        ItemStatus.Unavailable => UnavailFg,
        _ => PendingFg
    };

    public void RefreshVisuals()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBgBrush));
        OnPropertyChanged(nameof(StatusFgBrush));
        OnPropertyChanged(nameof(IsPurchased));
        OnPropertyChanged(nameof(IsUnavailable));
        OnPropertyChanged(nameof(IsPending));
        OnPropertyChanged(nameof(ActualPriceDisplay));
        OnPropertyChanged(nameof(DiffDisplay));
        OnPropertyChanged(nameof(DiffBrush));
        OnPropertyChanged(nameof(QtyDisplay));
    }

    public BuyingItemRowVm(OrderItem item)
    {
        Item = item;
    }
}
