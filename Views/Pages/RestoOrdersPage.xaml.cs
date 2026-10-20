using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;
using AntiquePasabuyPOS.Views.Dialogs;

namespace AntiquePasabuyPOS.Views.Pages;

public class RestoOrderRowVm
{
    public int Id { get; set; }
    public string OrderNo { get; set; } = "";
    public string OrderType { get; set; } = "Dining"; // "Dining", "Bar", "Takeout"
    public string OrderTypeBadge { get; set; } = "🍽️ Dine-In";
    public string OrderTypeBackground { get; set; } = "#EFF6FF";
    public string OrderTypeForeground { get; set; } = "#1D4ED8";
    public string TableNumber { get; set; } = "";
    public string TableDisplay => string.IsNullOrWhiteSpace(TableNumber) ? "Counter" : (TableNumber.StartsWith("T", StringComparison.OrdinalIgnoreCase) || TableNumber.StartsWith("B", StringComparison.OrdinalIgnoreCase) ? TableNumber : $"Table {TableNumber}");
    public string CustomerName { get; set; } = "Walk-in Guest";
    public string ServerOrCashier { get; set; } = "";
    public string ItemsSummary { get; set; } = "";
    public int ItemsCount { get; set; }
    public decimal TotalAmount { get; set; }
    public string StatusBadge { get; set; } = "✅ Paid (Cash)";
    public string StatusBackground { get; set; } = "#DCFCE7";
    public string StatusForeground { get; set; } = "#16A34A";
    public bool IsActiveTab { get; set; }
    public bool IsSelected { get; set; }
    public DateTime CreatedAt { get; set; }
    public RestoSale? Sale { get; set; }
    public RestoOrder? ActiveOrder { get; set; }
}

public partial class RestoOrdersPage : Page
{
    private readonly AppDbContext _db;
    private readonly User _user;
    private readonly Action<string> _navigate;

    private List<RestoSale> _allSales = new();
    private List<RestoOrder> _activeOrders = new();
    private List<RestoOrderRowVm> _allRows = new();

    private string _activeCategoryFilter = "All";
    private string _activeDateFilter = "Today";

    public RestoOrdersPage(AppDbContext db, User user, Action<string> navigate)
    {
        InitializeComponent();
        _db = db;
        _user = user;
        _navigate = navigate;

        Loaded += async (_, _) => await LoadOrdersAsync();
    }

    private async Task LoadOrdersAsync()
    {
        try
        {
            _allSales = await RestaurantService.GetSalesAsync();
            _activeOrders = await RestaurantService.GetActiveKitchenOrdersAsync();

            BuildUnifiedRows();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading orders: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BuildUnifiedRows()
    {
        var rows = new List<RestoOrderRowVm>();

        // 1. Open Active Tabs / Kitchen Tickets
        foreach (var o in _activeOrders)
        {
            bool isBar = IsBarTable(o.TableNumber);
            string orderType = isBar ? "Bar" : (string.IsNullOrWhiteSpace(o.TableNumber) ? "Takeout" : "Dining");

            var itemsStr = string.Join(", ", o.Items.Select(i => $"{i.Quantity:0.#}x {i.ItemName}"));
            if (string.IsNullOrWhiteSpace(itemsStr)) itemsStr = "No items yet";

            rows.Add(new RestoOrderRowVm
            {
                Id = o.Id,
                OrderNo = o.OrderNo,
                OrderType = orderType,
                OrderTypeBadge = isBar ? "🍺 Bar Counter" : (string.IsNullOrWhiteSpace(o.TableNumber) ? "🥡 Takeout" : "🍽️ Dine-in Table"),
                OrderTypeBackground = isBar ? "#FEF3C7" : (string.IsNullOrWhiteSpace(o.TableNumber) ? "#F3F4F6" : "#EFF6FF"),
                OrderTypeForeground = isBar ? "#B45309" : (string.IsNullOrWhiteSpace(o.TableNumber) ? "#4B5563" : "#1D4ED8"),
                TableNumber = o.TableNumber ?? "",
                CustomerName = string.IsNullOrWhiteSpace(o.CustomerName) ? "Dining Guest" : o.CustomerName,
                ServerOrCashier = o.ServerName ?? "Server",
                ItemsSummary = itemsStr,
                ItemsCount = o.Items.Count,
                TotalAmount = o.TotalAmount,
                StatusBadge = "⚡ Active Tab",
                StatusBackground = "#FEF9C3",
                StatusForeground = "#B45309",
                IsActiveTab = true,
                CreatedAt = o.CreatedAt,
                ActiveOrder = o
            });
        }

        // 2. Completed Sales Transactions
        foreach (var s in _allSales)
        {
            bool isBar = IsBarTable(s.TableNumber);
            string orderType = isBar ? "Bar" : (string.IsNullOrWhiteSpace(s.TableNumber) ? "Takeout" : "Dining");

            var itemsStr = string.Join(", ", s.Items.Select(i => $"{i.Quantity:0.#}x {i.ItemName}"));
            if (string.IsNullOrWhiteSpace(itemsStr)) itemsStr = "General items";

            rows.Add(new RestoOrderRowVm
            {
                Id = s.Id,
                OrderNo = s.SaleNo,
                OrderType = orderType,
                OrderTypeBadge = isBar ? "🍺 Bar Counter" : (string.IsNullOrWhiteSpace(s.TableNumber) ? "🥡 Takeout" : "🍽️ Dine-in Table"),
                OrderTypeBackground = isBar ? "#FEF3C7" : (string.IsNullOrWhiteSpace(s.TableNumber) ? "#F3F4F6" : "#EFF6FF"),
                OrderTypeForeground = isBar ? "#B45309" : (string.IsNullOrWhiteSpace(s.TableNumber) ? "#4B5563" : "#1D4ED8"),
                TableNumber = s.TableNumber ?? "",
                CustomerName = string.IsNullOrWhiteSpace(s.CustomerName) ? "Walk-in Guest" : s.CustomerName,
                ServerOrCashier = s.CashierName ?? "Cashier",
                ItemsSummary = itemsStr,
                ItemsCount = s.Items.Count,
                TotalAmount = s.TotalAmount,
                StatusBadge = $"✅ Paid ({s.PaymentMethod})",
                StatusBackground = "#DCFCE7",
                StatusForeground = "#16A34A",
                IsActiveTab = false,
                CreatedAt = s.CreatedAt,
                Sale = s
            });
        }

        _allRows = rows.OrderByDescending(r => r.CreatedAt).ToList();
    }

    private static bool IsBarTable(string? table)
    {
        if (string.IsNullOrWhiteSpace(table)) return false;
        var t = table.Trim().ToUpperInvariant();
        return t.Contains("BAR") || t.StartsWith("B-") || t.StartsWith("B ");
    }

    private void ApplyFilter()
    {
        if (OrdersGrid == null || _allRows == null) return;

        var query = _allRows.AsEnumerable();
        var now = DateTime.Now;

        // 1. Category Segment Filter (All, Dining, Bar, Active, Completed)
        if (_activeCategoryFilter == "Dining")
            query = query.Where(r => r.OrderType == "Dining");
        else if (_activeCategoryFilter == "Bar")
            query = query.Where(r => r.OrderType == "Bar");
        else if (_activeCategoryFilter == "Active")
            query = query.Where(r => r.IsActiveTab);
        else if (_activeCategoryFilter == "Completed")
            query = query.Where(r => !r.IsActiveTab);

        // 2. Date Filter
        if (DpFrom?.SelectedDate != null || DpTo?.SelectedDate != null)
        {
            if (DpFrom?.SelectedDate != null)
            {
                var start = DpFrom.SelectedDate.Value.Date;
                query = query.Where(r => r.CreatedAt >= start);
            }
            if (DpTo?.SelectedDate != null)
            {
                var end = DpTo.SelectedDate.Value.Date.AddDays(1).AddTicks(-1);
                query = query.Where(r => r.CreatedAt <= end);
            }
        }
        else
        {
            query = _activeDateFilter switch
            {
                "Today" => query.Where(r => r.CreatedAt.Date == now.Date),
                "Yesterday" => query.Where(r => r.CreatedAt.Date == now.Date.AddDays(-1)),
                "ThisWeek" => query.Where(r => r.CreatedAt.Date >= now.Date.AddDays(-(int)now.DayOfWeek)),
                "ThisMonth" => query.Where(r => r.CreatedAt.Year == now.Year && r.CreatedAt.Month == now.Month),
                _ => query // "All"
            };
        }

        // 3. Search Box Filter
        var search = TxtSearchOrders?.Text?.Trim().ToLower() ?? "";
        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(r =>
                (r.OrderNo != null && r.OrderNo.ToLower().Contains(search)) ||
                (r.TableNumber != null && r.TableNumber.ToLower().Contains(search)) ||
                (r.CustomerName != null && r.CustomerName.ToLower().Contains(search)) ||
                (r.ServerOrCashier != null && r.ServerOrCashier.ToLower().Contains(search)) ||
                (r.ItemsSummary != null && r.ItemsSummary.ToLower().Contains(search)) ||
                (r.StatusBadge != null && r.StatusBadge.ToLower().Contains(search))
            );
        }

        var filtered = query.OrderByDescending(r => r.CreatedAt).ToList();
        OrdersGrid.ItemsSource = filtered;

        // Empty state visibility
        if (EmptyStateBorder != null)
        {
            EmptyStateBorder.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // 4. Update KPIs across all relevant orders for the active date range
        var dateQuery = _allRows.AsEnumerable();
        if (DpFrom?.SelectedDate != null || DpTo?.SelectedDate != null)
        {
            if (DpFrom?.SelectedDate != null)
            {
                var start = DpFrom.SelectedDate.Value.Date;
                dateQuery = dateQuery.Where(r => r.CreatedAt >= start);
            }
            if (DpTo?.SelectedDate != null)
            {
                var end = DpTo.SelectedDate.Value.Date.AddDays(1).AddTicks(-1);
                dateQuery = dateQuery.Where(r => r.CreatedAt <= end);
            }
        }
        else
        {
            dateQuery = _activeDateFilter switch
            {
                "Today" => dateQuery.Where(r => r.CreatedAt.Date == now.Date),
                "Yesterday" => dateQuery.Where(r => r.CreatedAt.Date == now.Date.AddDays(-1)),
                "ThisWeek" => dateQuery.Where(r => r.CreatedAt.Date >= now.Date.AddDays(-(int)now.DayOfWeek)),
                "ThisMonth" => dateQuery.Where(r => r.CreatedAt.Year == now.Year && r.CreatedAt.Month == now.Month),
                _ => dateQuery // "All"
            };
        }

        var kpiRows = dateQuery.ToList();
        var activeTabs = kpiRows.Where(r => r.IsActiveTab).ToList();
        var completedSales = kpiRows.Where(r => !r.IsActiveTab).ToList();

        if (TxtKpiTotalSales != null)
            TxtKpiTotalSales.Text = $"₱{completedSales.Sum(s => s.TotalAmount):N2}";
        if (TxtKpiOrderCount != null)
            TxtKpiOrderCount.Text = $"{completedSales.Count} completed sales";

        if (TxtKpiActiveTabs != null)
            TxtKpiActiveTabs.Text = $"₱{activeTabs.Sum(a => a.TotalAmount):N2}";
        if (TxtKpiActiveTabsCount != null)
            TxtKpiActiveTabsCount.Text = $"{activeTabs.Count} open tables/tabs";

        var dineInOrders = kpiRows.Where(r => r.OrderType == "Dining").ToList();
        if (TxtKpiDineInSales != null)
            TxtKpiDineInSales.Text = $"₱{dineInOrders.Sum(d => d.TotalAmount):N2}";
        if (TxtKpiDineInCount != null)
            TxtKpiDineInCount.Text = $"{dineInOrders.Count} dining orders";

        var barOrders = kpiRows.Where(r => r.OrderType == "Bar").ToList();
        if (TxtKpiBarSales != null)
            TxtKpiBarSales.Text = $"₱{barOrders.Sum(b => b.TotalAmount):N2}";
        if (TxtKpiBarCount != null)
            TxtKpiBarCount.Text = $"{barOrders.Count} bar tabs / drinks";

        UpdateBulkDeleteButtonVisibility();
    }

    private void OnCategoryTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            _activeCategoryFilter = tag;

            if (CategoryTabsPanel != null)
            {
                foreach (var child in CategoryTabsPanel.Children)
                {
                    if (child is Button b)
                    {
                        b.Style = (b.Tag?.ToString() == tag)
                            ? (Style)FindResource("Pos3DPrimaryBtn")
                            : (Style)FindResource("Pos3DSecondaryBtn");
                    }
                }
            }

            ApplyFilter();
        }
    }

    private void OnDatePillClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            _activeDateFilter = tag;

            // Clear custom date pickers when clicking a preset
            if (DpFrom != null) DpFrom.SelectedDate = null;
            if (DpTo != null) DpTo.SelectedDate = null;

            if (DatePillsPanel != null)
            {
                foreach (var child in DatePillsPanel.Children)
                {
                    if (child is Button b)
                    {
                        b.Style = (b.Tag?.ToString() == tag)
                            ? (Style)FindResource("Pos3DPrimaryBtn")
                            : (Style)FindResource("Pos3DSecondaryBtn");
                    }
                }
            }

            ApplyFilter();
        }
    }

    private void OnDateRangeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DpFrom?.SelectedDate != null || DpTo?.SelectedDate != null)
        {
            if (DatePillsPanel != null)
            {
                foreach (var child in DatePillsPanel.Children)
                {
                    if (child is Button b)
                        b.Style = (Style)FindResource("Pos3DSecondaryBtn");
                }
            }
        }
        ApplyFilter();
    }

    private void OnSearchOrdersChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void OnClearSearchClick(object sender, RoutedEventArgs e)
    {
        if (TxtSearchOrders != null)
            TxtSearchOrders.Text = "";
    }

    private async Task ShowOrderDetailDialogAsync(RestoOrderRowVm row)
    {
        var win = Window.GetWindow(this);
        if (row.Sale != null)
        {
            var dlg = new RestoOrderDetailDialog(row.Sale) { Owner = win };
            if (dlg.ShowDialog() == true) await LoadOrdersAsync();
        }
        else if (row.ActiveOrder != null)
        {
            // Build pseudo-sale so user can inspect active tab details & itemized receipt
            var pseudoSale = new RestoSale
            {
                Id = row.ActiveOrder.Id,
                SaleNo = row.ActiveOrder.OrderNo,
                TableNumber = row.ActiveOrder.TableNumber,
                CustomerName = row.ActiveOrder.CustomerName,
                CashierName = row.ActiveOrder.ServerName,
                PaymentMethod = "Open Tab (Unpaid)",
                PaymentStatus = RestoSaleStatus.Pending,
                Subtotal = row.ActiveOrder.TotalAmount,
                TotalAmount = row.ActiveOrder.TotalAmount,
                CreatedAt = row.ActiveOrder.CreatedAt,
                Items = row.ActiveOrder.Items.Select(i => new RestoSaleItem
                {
                    ItemName = i.ItemName,
                    Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice,
                    Subtotal = i.Subtotal
                }).ToList()
            };

            var dlg = new RestoOrderDetailDialog(pseudoSale) { Owner = win };
            if (dlg.ShowDialog() == true) await LoadOrdersAsync();
        }
    }

    private async void OnViewOrderDetailsClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is RestoOrderRowVm row)
        {
            await ShowOrderDetailDialogAsync(row);
        }
    }

    private async void OnOrdersGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (OrdersGrid?.SelectedItem is RestoOrderRowVm row)
        {
            await ShowOrderDetailDialogAsync(row);
        }
    }

    private async void OnDeleteOrderClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is RestoOrderRowVm row)
        {
            await ConfirmAndDeleteOrderAsync(row);
        }
    }

    private async void OnContextMenuViewReceipt(object sender, RoutedEventArgs e)
    {
        if (OrdersGrid?.SelectedItem is RestoOrderRowVm row)
        {
            await ShowOrderDetailDialogAsync(row);
        }
    }

    private async void OnContextMenuDeleteOrder(object sender, RoutedEventArgs e)
    {
        if (OrdersGrid?.SelectedItem is RestoOrderRowVm row)
        {
            await ConfirmAndDeleteOrderAsync(row);
        }
    }

    private async void OnOrdersGridKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && OrdersGrid?.SelectedItem is RestoOrderRowVm row)
        {
            e.Handled = true;
            await ConfirmAndDeleteOrderAsync(row);
        }
    }

    private async Task ConfirmAndDeleteOrderAsync(RestoOrderRowVm row)
    {
        var res = MessageBox.Show(
            $"Are you sure you want to permanently delete order {row.OrderNo}?\n\n" +
            $"• Guest / Table: {row.CustomerName} ({row.TableDisplay})\n" +
            $"• Amount:        ₱{row.TotalAmount:N2}\n" +
            $"• Status:        {row.StatusBadge}\n" +
            $"• Date:          {row.CreatedAt:MMM dd, yyyy  hh:mm tt}\n\n" +
            "This will delete the order record permanently.",
            "Delete Order",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (res == MessageBoxResult.Yes)
        {
            try
            {
                if (row.Sale != null)
                {
                    var ok = await RestaurantService.DeleteSaleAsync(row.Sale.Id);
                    if (ok)
                    {
                        MessageBox.Show($"Sale {row.OrderNo} has been deleted.", "Order Deleted", MessageBoxButton.OK, MessageBoxImage.Information);
                        await LoadOrdersAsync();
                    }
                }
                else if (row.ActiveOrder != null)
                {
                    var ok = await RestaurantService.DeleteOrderAsync(row.ActiveOrder.Id);
                    if (!ok) await RestaurantService.UpdateOrderStatusAsync(row.ActiveOrder.Id, RestoOrderStatus.Cancelled);
                    MessageBox.Show($"Active order {row.OrderNo} has been deleted.", "Order Deleted", MessageBoxButton.OK, MessageBoxImage.Information);
                    await LoadOrdersAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to delete order: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void OnHeaderSelectAllClick(object sender, RoutedEventArgs e)
    {
        if (HeaderSelectAll == null || OrdersGrid?.ItemsSource is not IEnumerable<RestoOrderRowVm> rows) return;
        bool isChecked = HeaderSelectAll.IsChecked == true;
        foreach (var r in rows)
        {
            r.IsSelected = isChecked;
        }
        OrdersGrid.Items.Refresh();
        UpdateBulkDeleteButtonVisibility();
    }

    private void OnRowCheckChanged(object sender, RoutedEventArgs e)
    {
        UpdateBulkDeleteButtonVisibility();
    }

    private void UpdateBulkDeleteButtonVisibility()
    {
        if (BtnDeleteSelected == null) return;
        var selectedCount = (OrdersGrid?.ItemsSource as IEnumerable<RestoOrderRowVm>)?.Count(r => r.IsSelected) ?? 0;
        if (selectedCount > 0)
        {
            BtnDeleteSelected.Visibility = Visibility.Visible;
            if (TxtDeleteSelectedLabel != null)
                TxtDeleteSelectedLabel.Text = $"Delete Selected ({selectedCount})";
        }
        else
        {
            BtnDeleteSelected.Visibility = Visibility.Collapsed;
        }
    }

    private async void OnDeleteSelectedClick(object sender, RoutedEventArgs e)
    {
        var selected = (OrdersGrid?.ItemsSource as IEnumerable<RestoOrderRowVm>)?.Where(r => r.IsSelected).ToList() ?? new();
        if (selected.Count == 0) return;

        var res = MessageBox.Show(
            $"Are you sure you want to permanently delete {selected.Count} selected order(s)/sale(s)?\n\nThis cannot be undone.",
            "Delete Selected Orders",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (res != MessageBoxResult.Yes) return;

        try
        {
            foreach (var row in selected)
            {
                if (row.Sale != null)
                {
                    await RestaurantService.DeleteSaleAsync(row.Sale.Id);
                }
                else if (row.ActiveOrder != null)
                {
                    await RestaurantService.DeleteOrderAsync(row.ActiveOrder.Id);
                }
            }

            MessageBox.Show($"{selected.Count} order(s) deleted successfully.", "Orders Deleted", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadOrdersAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error deleting selected orders: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnClearAllOrdersClick(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show(
            "Are you sure you want to permanently clear and delete ALL restaurant orders and sales records?\n\n" +
            "• All completed sales and receipts will be wiped.\n" +
            "• All open tabs and kitchen orders will be cleared.\n" +
            "• All dining and bar tables will be reset to Available.\n\n" +
            "This action cannot be undone. Do you want to proceed?",
            "Clear All Restaurant Orders",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (res != MessageBoxResult.Yes) return;

        try
        {
            using (var db = new AppDbContext())
            {
                db.RestoSaleItems.RemoveRange(db.RestoSaleItems);
                db.RestoSales.RemoveRange(db.RestoSales);
                db.RestoOrderItems.RemoveRange(db.RestoOrderItems);
                db.RestoOrders.RemoveRange(db.RestoOrders);
                foreach (var t in db.RestoTables)
                {
                    t.Status = RestoTableStatus.Available;
                    t.CurrentCustomerName = null;
                    t.CurrentBillAmount = 0;
                    t.OccupiedSince = null;
                }
                await db.SaveChangesAsync();
            }

            MessageBox.Show("All restaurant sales and orders have been successfully cleared.", "Orders Cleared", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadOrdersAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to clear orders: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnNewOrderClick(object sender, RoutedEventArgs e) => _navigate("RestoSales");

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await LoadOrdersAsync();
}
