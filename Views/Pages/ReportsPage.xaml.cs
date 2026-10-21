using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace AntiquePasabuyPOS.Views.Pages;

public partial class ReportsPage : Page
{
    private readonly AppDbContext _db;
    private readonly User _user;
    private string _activeReport = "sales";

    // Cached current report records for export
    private List<dynamic> _currentData = [];

    public ReportsPage(AppDbContext db, User user, Action<string>? navigate = null, Action? onSuccess = null, string? initialReport = null)
    {
        InitializeComponent();
        _db = db;
        _user = user;
        if (!string.IsNullOrWhiteSpace(initialReport)) _activeReport = initialReport.ToLower();

        // Default to This Month so existing orders are always visible
        FromDate.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        ToDate.SelectedDate   = DateTime.Today;

        Loaded += (_, _) =>
        {
            SwitchReportTab(_activeReport);
        };
    }

    private void OnDateShortcutChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FromDate == null || ToDate == null || DateShortcutCombo?.SelectedItem is not ComboBoxItem item) return;

        var selected = item.Content?.ToString() ?? "This Month";
        switch (selected)
        {
            case "Today":
                FromDate.SelectedDate = DateTime.Today;
                ToDate.SelectedDate   = DateTime.Today;
                break;
            case "This Week":
                var dayOfWeek = (int)DateTime.Today.DayOfWeek;
                var mondayOffset = dayOfWeek == 0 ? -6 : 1 - dayOfWeek;
                FromDate.SelectedDate = DateTime.Today.AddDays(mondayOffset);
                ToDate.SelectedDate   = DateTime.Today;
                break;
            case "This Month":
                FromDate.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                ToDate.SelectedDate   = DateTime.Today;
                break;
            case "Custom":
                // Enable pickers — user sets range manually, then clicks Apply
                break;
        }

        OnGenerate(null!, null!);
    }

    private void OnReportTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            SwitchReportTab(tag);
        }
    }

    private void SwitchReportTab(string reportKey)
    {
        _activeReport = reportKey.ToLower();

        // Update button styles
        SetTabStyle(TabSales, _activeReport == "sales", Color.FromRgb(0, 53, 145));
        if (TabEstVsAct != null) SetTabStyle(TabEstVsAct, _activeReport == "estvsact", Color.FromRgb(0, 53, 145));
        SetTabStyle(TabTopStores, _activeReport == "topstores", Color.FromRgb(0, 53, 145));
        SetTabStyle(TabCustomers, _activeReport == "customers", Color.FromRgb(0, 53, 145));
        SetTabStyle(TabCancelled, _activeReport == "cancelled", Color.FromRgb(220, 38, 38));

        // Show Sales Performance Chart only on Sales Report tab
        SalesChart.Visibility = _activeReport == "sales" ? Visibility.Visible : Visibility.Collapsed;

        ConfigureColumns();
        OnGenerate(null!, null!);
    }

    private void SetTabStyle(Button btn, bool isActive, Color activeColor)
    {
        NavProps.SetIsActive(btn, isActive);
        btn.Foreground = isActive ? Brushes.White : new SolidColorBrush(Color.FromRgb(71, 85, 105));
    }

    private void ConfigureColumns()
    {
        ReportGrid.Columns.Clear();

        switch (_activeReport)
        {
            case "sales":
                ReportTitleLabel.Text = "Purchase Order Summary (Budget & Pasabuy Fees)";
                ReportGrid.Columns.Add(new DataGridTextColumn { Header = "PO #", Binding = new Binding("OrderNo"), Width = 110 });
                ReportGrid.Columns.Add(new DataGridTextColumn { Header = "Date", Binding = new Binding("CreatedAt") { StringFormat = "yyyy-MM-dd HH:mm" }, Width = 130 });
                ReportGrid.Columns.Add(new DataGridTextColumn { Header = "Customer", Binding = new Binding("CustomerName"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                ReportGrid.Columns.Add(new DataGridTextColumn { Header = "Shopping Day", Binding = new Binding("BuyingDay"), Width = 100 });
                ReportGrid.Columns.Add(new DataGridTextColumn { Header = "Purchase Budget", Binding = new Binding("ItemsSubtotal") { StringFormat = "₱{0:N2}" }, Width = 120 });
                ReportGrid.Columns.Add(new DataGridTextColumn { Header = "Pasabuy Fees", Binding = new Binding("PasabuyFee") { StringFormat = "₱{0:N2}" }, Width = 115 });
                ReportGrid.Columns.Add(new DataGridTextColumn { Header = "Delivery Fee", Binding = new Binding("DeliveryFee") { StringFormat = "₱{0:N2}" }, Width = 100 });
                ReportGrid.Columns.Add(new DataGridTextColumn { Header = "Grand Total", Binding = new Binding("GrandTotal") { StringFormat = "₱{0:N2}" }, Width = 120 });
                ReportGrid.Columns.Add(new DataGridTextColumn { Header = "Payment", Binding = new Binding("PaymentStatus"), Width = 95 });
                ReportGrid.Columns.Add(new DataGridTextColumn { Header = "Paid Date", Binding = new Binding("PaidDateDisplay"), Width = 105 });
                ReportGrid.Columns.Add(new DataGridTextColumn { Header = "Status", Binding = new Binding("Status"), Width = 150 });
                break;

            case "estvsact":
                ReportTitleLabel.Text = "Estimated vs. Actual Purchase Amount Report";
                ReportGrid.Columns.Add(new DataGridTextColumn { Header = "PO #", Binding = new Binding("OrderNo"), Width = 110 });
                ReportGrid.Columns.Add(new DataGridTextColumn { Header = "Customer", Binding = new Binding("CustomerName"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                ReportGrid.Columns.Add(new DataGridTextColumn { Header = "Store", Binding = new Binding("Store"), Width = 130 });
                ReportGrid.Columns.Add(new DataGridTextColumn { Header = "Item Name", Binding = new Binding("ProductName"), Width = new DataGridLength(1.3, DataGridLengthUnitType.Star) });
                ReportGrid.Columns.Add(new DataGridTextColumn { Header = "Qty", Binding = new Binding("QtyDisplay"), Width = 75 });
                ReportGrid.Columns.Add(new DataGridTextColumn { Header = "Estimated", Binding = new Binding("EstimatedTotal") { StringFormat = "₱{0:N2}" }, Width = 105 });
                ReportGrid.Columns.Add(new DataGridTextColumn { Header = "Actual", Binding = new Binding("ActualTotal") { StringFormat = "₱{0:N2}" }, Width = 105 });
                ReportGrid.Columns.Add(new DataGridTextColumn { Header = "Difference", Binding = new Binding("DiffDisplay"), Width = 105 });
                ReportGrid.Columns.Add(new DataGridTextColumn { Header = "Status", Binding = new Binding("ItemStatus"), Width = 110 });
                break;

            case "topstores":
                ReportTitleLabel.Text = "Top Stores & Most Requested Items Analysis";
                ReportGrid.Columns.Add(new DataGridTextColumn { Header = "Type", Binding = new Binding("CategoryType"), Width = 100 });
                ReportGrid.Columns.Add(new DataGridTextColumn { Header = "Store / Product Name", Binding = new Binding("Name"), Width = new DataGridLength(1.2, DataGridLengthUnitType.Star) });
                ReportGrid.Columns.Add(new DataGridTextColumn { Header = "Total Orders", Binding = new Binding("TotalOrders"), Width = 110 });
                ReportGrid.Columns.Add(new DataGridTextColumn { Header = "Total Units", Binding = new Binding("TotalQtyDisplay"), Width = 110 });
                ReportGrid.Columns.Add(new DataGridTextColumn { Header = "Estimated Volume", Binding = new Binding("EstimatedVolume") { StringFormat = "₱{0:N2}" }, Width = 140 });
                ReportGrid.Columns.Add(new DataGridTextColumn { Header = "Share / Note", Binding = new Binding("ShareNote"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                break;

            case "customers":
                ReportTitleLabel.Text = "Customer Purchasing Report";
                ReportGrid.Columns.Add(new DataGridTextColumn { Header = "Customer Name", Binding = new Binding("Name"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                ReportGrid.Columns.Add(new DataGridTextColumn { Header = "Phone", Binding = new Binding("Phone"), Width = 120 });
                ReportGrid.Columns.Add(new DataGridTextColumn { Header = "Address", Binding = new Binding("Address"), Width = new DataGridLength(1.2, DataGridLengthUnitType.Star) });
                ReportGrid.Columns.Add(new DataGridTextColumn { Header = "Total Orders", Binding = new Binding("OrdersCount"), Width = 95 });
                ReportGrid.Columns.Add(new DataGridTextColumn { Header = "Total Spent", Binding = new Binding("TotalSpent") { StringFormat = "₱{0:N2}" }, Width = 120 });
                ReportGrid.Columns.Add(new DataGridTextColumn { Header = "Pasabuy Fees Paid", Binding = new Binding("PasabuyFeesPaid") { StringFormat = "₱{0:N2}" }, Width = 130 });
                break;

            case "cancelled":
                ReportTitleLabel.Text = "Cancelled Orders Audit Report";
                ReportGrid.Columns.Add(new DataGridTextColumn { Header = "PO #", Binding = new Binding("OrderNo"), Width = 110 });
                ReportGrid.Columns.Add(new DataGridTextColumn { Header = "Order Date", Binding = new Binding("CreatedAt") { StringFormat = "yyyy-MM-dd HH:mm" }, Width = 130 });
                ReportGrid.Columns.Add(new DataGridTextColumn { Header = "Customer", Binding = new Binding("CustomerName"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                ReportGrid.Columns.Add(new DataGridTextColumn { Header = "Items Count", Binding = new Binding("ItemsCountDisplay"), Width = 100 });
                ReportGrid.Columns.Add(new DataGridTextColumn { Header = "Cancelled Value", Binding = new Binding("GrandTotal") { StringFormat = "₱{0:N2}" }, Width = 125 });
                ReportGrid.Columns.Add(new DataGridTextColumn { Header = "Delivery Type", Binding = new Binding("DeliveryTypeDisplay"), Width = 110 });
                ReportGrid.Columns.Add(new DataGridTextColumn { Header = "Cancellation Reason / Notes", Binding = new Binding("Notes"), Width = new DataGridLength(1.5, DataGridLengthUnitType.Star) });
                break;
        }
    }

    private void OnGenerate(object s, RoutedEventArgs e)
    {
        var from = (FromDate.SelectedDate ?? DateTime.Today).Date;
        var to = (ToDate.SelectedDate ?? DateTime.Today).Date.AddDays(1);

        switch (_activeReport)
        {
            case "sales":
                LoadSalesReport(from, to);
                break;
            case "estvsact":
                LoadEstVsActReport(from, to);
                break;
            case "topstores":
                LoadTopStoresReport(from, to);
                break;
            case "customers":
                LoadCustomerReport(from, to);
                break;
            case "cancelled":
                LoadCancelledOrdersReport(from, to);
                break;
        }
    }

    private void LoadSalesReport(DateTime from, DateTime to)
    {
        var orders = _db.Orders
            .AsNoTracking()
            .Where(o => (o.BusinessId == "ANTIQUE_PASABUY" || string.IsNullOrEmpty(o.BusinessId)) && o.CreatedAt >= from && o.CreatedAt < to && o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.CANCELLED)
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .Include(o => o.Payments)
            .OrderByDescending(o => o.CreatedAt)
            .ToList();

        var totalBudget = orders.Sum(o => o.ItemsSubtotal);
        var totalPasabuy = orders.Sum(o => o.PasabuyFee);
        var totalDelivery = orders.Sum(o => o.DeliveryFee);

        if (KpiCard1Title != null) KpiCard1Title.Text = "PURCHASE BUDGET";
        if (KpiCard2Title != null) KpiCard2Title.Text = "PASABUY FEES";
        if (KpiCard3Title != null) KpiCard3Title.Text = "DELIVERY FEES";
        if (KpiCard4Title != null) KpiCard4Title.Text = "TOTAL ORDERS";

        RevenueLabel.Text = $"₱{totalBudget:N2}";
        PasabuyFeeLabel.Text = $"₱{totalPasabuy:N2}";
        DeliveryFeeLabel.Text = $"₱{totalDelivery:N2}";
        OrdersLabel.Text = $"{orders.Count} orders";

        _currentData = orders.Select(o => (dynamic)new
        {
            o.OrderNo,
            o.CreatedAt,
            CustomerName = o.Customer?.Name ?? "Walk-in Customer",
            BuyingDay = o.BuyingDay ?? "Wednesday",
            o.ItemsSubtotal,
            PasabuyFee = o.PasabuyFee,
            DeliveryFee = o.DeliveryFee,
            o.GrandTotal,
            PaymentStatus = o.PaymentStatusDisplay,
            PaidDateDisplay = o.PaidDateDisplay,
            Status = OrderStatusService.FormatStatus(o.Status)
        }).ToList();

        ReportGrid.ItemsSource = _currentData;

        // Render Sales Performance Bar Chart with real filtered order dataset
        SalesChart.Visibility = Visibility.Visible;
        SalesChart.RenderChart(from, to, orders);
    }

    private void LoadEstVsActReport(DateTime from, DateTime to)
    {
        var items = _db.OrderItems
            .AsNoTracking()
            .Include(i => i.Order).ThenInclude(o => o!.Customer)
            .Where(i => i.Order != null && (i.Order.BusinessId == "ANTIQUE_PASABUY" || string.IsNullOrEmpty(i.Order.BusinessId))
                     && i.Order.CreatedAt >= from && i.Order.CreatedAt < to
                     && i.Order.Status != OrderStatus.Cancelled && i.Order.Status != OrderStatus.CANCELLED)
            .OrderByDescending(i => i.Order!.CreatedAt)
            .ToList();

        var totalEst = items.Sum(i => i.EstimatedPrice * (decimal)i.Qty);
        var totalAct = items.Where(i => i.ActualPrice.HasValue).Sum(i => i.ActualPrice!.Value * (decimal)i.Qty);
        var totalDiff = totalAct - items.Where(i => i.ActualPrice.HasValue).Sum(i => i.EstimatedPrice * (decimal)i.Qty);

        if (KpiCard1Title != null) KpiCard1Title.Text = "ESTIMATED TOTAL";
        if (KpiCard2Title != null) KpiCard2Title.Text = "ACTUAL PURCHASES";
        if (KpiCard3Title != null) KpiCard3Title.Text = "TOTAL VARIANCE";
        if (KpiCard4Title != null) KpiCard4Title.Text = "ITEMS EVALUATED";

        RevenueLabel.Text = $"₱{totalEst:N2}";
        PasabuyFeeLabel.Text = $"₱{totalAct:N2}";
        DeliveryFeeLabel.Text = $"{(totalDiff >= 0 ? "+" : "-")}₱{Math.Abs(totalDiff):N2}";
        OrdersLabel.Text = $"{items.Count} items";

        _currentData = items.Select(i =>
        {
            var estTotal = i.EstimatedPrice * (decimal)i.Qty;
            var actTotal = (i.ActualPrice ?? i.EstimatedPrice) * (decimal)i.Qty;
            var diff = i.ActualPrice.HasValue ? (i.ActualPrice.Value - i.EstimatedPrice) * (decimal)i.Qty : 0m;
            var diffStr = !i.ActualPrice.HasValue ? "—" : (diff > 0 ? $"+₱{diff:N2}" : $"-₱{Math.Abs(diff):N2}");

            return (dynamic)new
            {
                OrderNo = i.Order?.OrderNo ?? "—",
                CustomerName = i.Order?.Customer?.Name ?? "Walk-in Customer",
                Store = string.IsNullOrWhiteSpace(i.Store) ? "General" : i.Store,
                ProductName = i.ProductName,
                QtyDisplay = $"{i.Qty:0.#} {i.Unit}",
                EstimatedTotal = estTotal,
                ActualTotal = i.ActualPrice.HasValue ? actTotal : estTotal,
                DiffDisplay = diffStr,
                ItemStatus = i.Status.ToString()
            };
        }).ToList<dynamic>();

        ReportGrid.ItemsSource = _currentData;
    }

    private void LoadTopStoresReport(DateTime from, DateTime to)
    {
        var items = _db.OrderItems
            .AsNoTracking()
            .Include(i => i.Order)
            .Where(i => i.Order != null && (i.Order.BusinessId == "ANTIQUE_PASABUY" || string.IsNullOrEmpty(i.Order.BusinessId)) && i.Order.CreatedAt >= from && i.Order.CreatedAt < to 
                     && i.Order.Status != OrderStatus.Cancelled && i.Order.Status != OrderStatus.CANCELLED)
            .ToList();

        decimal totalStoreVolume = items.Sum(i => i.EstimatedPrice * (decimal)i.Qty);

        // Group by Stores (Top 5)
        var topStores = items
            .GroupBy(i => string.IsNullOrWhiteSpace(i.Store) ? "General Market" : i.Store)
            .Select(g =>
            {
                var vol = g.Sum(x => x.EstimatedPrice * (decimal)x.Qty);
                var ordersCount = g.Select(x => x.OrderId).Distinct().Count();
                var units = g.Sum(x => x.Qty);
                var pct = totalStoreVolume > 0 ? (vol / totalStoreVolume * 100) : 0;
                return (dynamic)new
                {
                    CategoryType = "STORE",
                    Name = g.Key,
                    TotalOrders = $"{ordersCount} orders",
                    TotalQtyDisplay = $"{units:0.##} units",
                    EstimatedVolume = vol,
                    ShareNote = $"{pct:F1}% of total volume"
                };
            })
            .OrderByDescending(s => (decimal)s.EstimatedVolume)
            .Take(10)
            .ToList();

        // Group by Top Products (Top 10)
        var topProducts = items
            .GroupBy(i => i.ProductName)
            .Select(g =>
            {
                var vol = g.Sum(x => x.EstimatedPrice * (decimal)x.Qty);
                var ordersCount = g.Select(x => x.OrderId).Distinct().Count();
                var units = g.Sum(x => x.Qty);
                var store = g.Select(x => x.Store).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "General";
                return (dynamic)new
                {
                    CategoryType = "PRODUCT",
                    Name = g.Key,
                    TotalOrders = $"{ordersCount} orders",
                    TotalQtyDisplay = $"{units:0.##} units",
                    EstimatedVolume = vol,
                    ShareNote = $"Bought at: {store}"
                };
            })
            .OrderByDescending(p => (decimal)p.EstimatedVolume)
            .Take(15)
            .ToList();

        var combined = new List<dynamic>();
        combined.AddRange(topStores);
        combined.AddRange(topProducts);

        if (KpiCard1Title != null) KpiCard1Title.Text = "TOTAL VOLUME";
        if (KpiCard2Title != null) KpiCard2Title.Text = "TOP STORE";
        if (KpiCard3Title != null) KpiCard3Title.Text = "TOP PRODUCT";
        if (KpiCard4Title != null) KpiCard4Title.Text = "TOTAL PRODUCTS";

        RevenueLabel.Text = $"₱{totalStoreVolume:N2}";
        PasabuyFeeLabel.Text = topStores.FirstOrDefault()?.Name ?? "None";
        DeliveryFeeLabel.Text = topProducts.FirstOrDefault()?.Name ?? "None";
        OrdersLabel.Text = $"{items.Select(i => i.ProductName).Distinct().Count()} products";

        _currentData = combined;
        ReportGrid.ItemsSource = _currentData;
    }

    private void LoadCustomerReport(DateTime from, DateTime to)
    {
        var orders = _db.Orders
            .AsNoTracking()
            .Where(o => (o.BusinessId == "ANTIQUE_PASABUY" || string.IsNullOrEmpty(o.BusinessId)) && o.CreatedAt >= from && o.CreatedAt < to && o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.CANCELLED)
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .Include(o => o.Payments)
            .ToList();

        var groups = orders
            .GroupBy(o => o.Customer != null ? o.Customer.Id : 0)
            .Select(g =>
            {
                var cust = g.First().Customer;
                return (dynamic)new
                {
                    Name = cust?.Name ?? "Walk-in Customers",
                    Phone = cust?.Phone ?? "—",
                    Address = cust?.Address ?? "—",
                    OrdersCount = g.Count(),
                    TotalSpent = g.Sum(o => o.GrandTotal),
                    PasabuyFeesPaid = g.Sum(o => o.PasabuyFee)
                };
            })
            .OrderByDescending(c => (decimal)c.TotalSpent)
            .ToList();

        RevenueLabel.Text = $"₱{orders.Sum(o => o.GrandTotal):N2}";
        PasabuyFeeLabel.Text = $"₱{orders.Sum(o => o.PasabuyFee):N2}";
        DeliveryFeeLabel.Text = $"{groups.Count} active";
        OrdersLabel.Text = $"{orders.Count} orders";

        _currentData = groups;
        ReportGrid.ItemsSource = _currentData;
    }

    private void LoadCancelledOrdersReport(DateTime from, DateTime to)
    {
        var orders = _db.Orders
            .AsNoTracking()
            .Where(o => (o.BusinessId == "ANTIQUE_PASABUY" || string.IsNullOrEmpty(o.BusinessId))
                     && o.CreatedAt >= from && o.CreatedAt < to
                     && (o.Status == OrderStatus.Cancelled || o.Status == OrderStatus.CANCELLED))
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .Include(o => o.Payments)
            .OrderByDescending(o => o.CreatedAt)
            .ToList();

        var totalCancelledValue = orders.Sum(o => o.GrandTotal);
        var totalLostPasabuy = orders.Sum(o => o.PasabuyFee);
        var totalImpactedItems = orders.Sum(o => o.Items?.Count ?? 0);

        if (KpiCard1Title != null) KpiCard1Title.Text = "TOTAL CANCELLED VALUE";
        if (KpiCard2Title != null) KpiCard2Title.Text = "LOST PASABUY REVENUE";
        if (KpiCard3Title != null) KpiCard3Title.Text = "IMPACTED ITEMS";
        if (KpiCard4Title != null) KpiCard4Title.Text = "CANCELLED ORDERS";

        RevenueLabel.Text = $"₱{totalCancelledValue:N2}";
        PasabuyFeeLabel.Text = $"₱{totalLostPasabuy:N2}";
        DeliveryFeeLabel.Text = $"{totalImpactedItems} items";
        OrdersLabel.Text = $"{orders.Count} cancelled";

        _currentData = orders.Select(o => (dynamic)new
        {
            o.OrderNo,
            o.CreatedAt,
            CustomerName = o.Customer?.Name ?? "Walk-in Customer",
            ItemsCount = o.Items?.Count ?? 0,
            ItemsCountDisplay = $"{o.Items?.Count ?? 0} item(s)",
            o.GrandTotal,
            DeliveryTypeDisplay = o.DeliveryType.ToString().ToUpper(),
            Notes = string.IsNullOrWhiteSpace(o.Notes) ? "No reason specified" : o.Notes
        }).ToList();

        ReportGrid.ItemsSource = _currentData;
    }

    private void OnExportExcel(object sender, RoutedEventArgs e)
    {
        if (_currentData.Count == 0)
        {
            MessageBox.Show("No report data available to export.", "Export Excel", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var dlg = new SaveFileDialog
            {
                Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                FileName = $"DionixPOS_{_activeReport}_Report_{DateTime.Now:yyyyMMdd}.xlsx"
            };

            if (dlg.ShowDialog() != true) return;

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add(_activeReport.ToUpper() + " REPORT");

            // ── Metadata block (rows 1–6) ──────────────────────────────────────
            var metaStyle = wb.Style;
            int mr = 1;
            ws.Cell(mr, 1).Value = "DIONIX POS — OFFICIAL BUSINESS REPORT";
            ws.Cell(mr, 1).Style.Font.Bold = true;
            ws.Cell(mr, 1).Style.Font.FontSize = 13;
            mr++;
            ws.Cell(mr, 1).Value = "Report Type:";  ws.Cell(mr, 2).Value = _activeReport.ToUpper() + " REPORT";
            mr++;
            ws.Cell(mr, 1).Value = "Date Range:";
            ws.Cell(mr, 2).Value = $"{FromDate.SelectedDate:yyyy-MM-dd}  to  {ToDate.SelectedDate:yyyy-MM-dd}";
            mr++;
            ws.Cell(mr, 1).Value = "Generated By:"; ws.Cell(mr, 2).Value = $"{_user.FullName} ({_user.Role})";
            mr++;
            ws.Cell(mr, 1).Value = "Export Date:";  ws.Cell(mr, 2).Value = DateTime.Now.ToString("yyyy-MM-dd hh:mm tt");
            mr++;
            ws.Cell(mr, 1).Value = "Total Records:"; ws.Cell(mr, 2).Value = _currentData.Count;
            mr++; // blank row
            mr++;

            // Style meta label column
            for (int ri = 1; ri < mr - 1; ri++)
            {
                ws.Cell(ri, 1).Style.Font.Bold = true;
                ws.Cell(ri, 1).Style.Font.FontColor = XLColor.FromHtml("#475569");
            }

            // ── Column headers ─────────────────────────────────────────────────
            int dataStart = mr;
            List<string> headers;
            List<bool> isCurrency = new();

            switch (_activeReport)
            {
                case "sales":
                    headers = ["Order No", "Date", "Customer", "Shopping Day",
                               "Purchase Budget", "Net Pasabuy Fee", "Delivery Fee", "Grand Total", "Status"];
                    isCurrency = [false, false, false, false, true, true, true, true, false];
                    break;
                case "topstores":
                    headers = ["Type", "Store / Product Name", "Total Orders", "Total Units",
                               "Estimated Volume", "Share / Note"];
                    isCurrency = [false, false, false, false, true, false];
                    break;
                case "customers":
                    headers = ["Customer Name", "Phone", "Address",
                               "Total Orders", "Total Spent", "Pasabuy Fees Paid"];
                    isCurrency = [false, false, false, false, true, true];
                    break;
                default: // cancelled
                    headers = ["Order No", "Order Date", "Customer", "Items Count", "Cancelled Value", "Delivery Type", "Cancellation Reason / Notes"];
                    isCurrency = [false, false, false, false, true, false, false];
                    break;
            }

            for (int ci = 0; ci < headers.Count; ci++)
            {
                var hCell = ws.Cell(dataStart, ci + 1);
                hCell.Value = headers[ci];
                hCell.Style.Font.Bold = true;
                hCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1E293B");
                hCell.Style.Font.FontColor = XLColor.White;
                hCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                hCell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                hCell.Style.Border.BottomBorderColor = XLColor.FromHtml("#94A3B8");
            }

            // ── Data rows ──────────────────────────────────────────────────────
            int row = dataStart + 1;
            decimal sumSub = 0, sumFee = 0, sumDel = 0, sumGrand = 0, sumSpent = 0, sumFees = 0, sumVol = 0, sumCancelled = 0;
            int sumOrders = 0;

            foreach (var r in _currentData)
            {
                List<object> vals;
                switch (_activeReport)
                {
                    case "sales":
                    {
                        decimal sub   = Convert.ToDecimal(r.ItemsSubtotal);
                        decimal fee   = Convert.ToDecimal(r.PasabuyFee);
                        decimal del   = Convert.ToDecimal(r.DeliveryFee);
                        decimal grand = Convert.ToDecimal(r.GrandTotal);
                        sumSub += sub; sumFee += fee; sumDel += del; sumGrand += grand;
                        vals = [r.OrderNo, ((DateTime)r.CreatedAt).ToString("yyyy-MM-dd HH:mm"),
                                r.CustomerName, r.BuyingDay, sub, fee, del, grand, r.Status];
                        break;
                    }
                    case "topstores":
                    {
                        decimal vol = Convert.ToDecimal(r.EstimatedVolume);
                        sumVol += vol;
                        vals = [r.CategoryType, r.Name, r.TotalOrders, r.TotalQtyDisplay,
                                vol, r.ShareNote];
                        break;
                    }
                    case "customers":
                    {
                        int    oc    = Convert.ToInt32(r.OrdersCount);
                        decimal sp   = Convert.ToDecimal(r.TotalSpent);
                        decimal fp   = Convert.ToDecimal(r.PasabuyFeesPaid);
                        sumOrders += oc; sumSpent += sp; sumFees += fp;
                        vals = [r.Name, r.Phone, r.Address, oc, sp, fp];
                        break;
                    }
                    default: // cancelled
                    {
                        decimal val = Convert.ToDecimal(r.GrandTotal);
                        sumCancelled += val;
                        vals = [r.OrderNo, ((DateTime)r.CreatedAt).ToString("yyyy-MM-dd HH:mm"),
                                r.CustomerName, r.ItemsCountDisplay, val, r.DeliveryTypeDisplay, r.Notes];
                        break;
                    }
                }

                bool shade = (row - dataStart) % 2 == 0;
                for (int ci = 0; ci < vals.Count; ci++)
                {
                    var cell = ws.Cell(row, ci + 1);
                    cell.Value = vals[ci] is decimal dv ? (XLCellValue)dv : (XLCellValue)(vals[ci]?.ToString() ?? "");
                    if (isCurrency.Count > ci && isCurrency[ci] && vals[ci] is decimal)
                        cell.Style.NumberFormat.Format = "₱#,##0.00";
                    if (shade)
                        cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");
                    cell.Style.Border.BottomBorder = XLBorderStyleValues.Hair;
                    cell.Style.Border.BottomBorderColor = XLColor.FromHtml("#CBD5E1");
                }
                row++;
            }

            // ── Totals row ─────────────────────────────────────────────────────
            int totalRow = row;
            ws.Row(totalRow).Style.Font.Bold = true;
            ws.Row(totalRow).Style.Fill.BackgroundColor = XLColor.FromHtml("#F1F5F9");

            switch (_activeReport)
            {
                case "sales":
                    ws.Cell(totalRow, 1).Value = "TOTALS";
                    ws.Cell(totalRow, 5).Value = sumSub;   ws.Cell(totalRow, 5).Style.NumberFormat.Format = "₱#,##0.00";
                    ws.Cell(totalRow, 6).Value = sumFee;   ws.Cell(totalRow, 6).Style.NumberFormat.Format = "₱#,##0.00";
                    ws.Cell(totalRow, 7).Value = sumDel;   ws.Cell(totalRow, 7).Style.NumberFormat.Format = "₱#,##0.00";
                    ws.Cell(totalRow, 8).Value = sumGrand; ws.Cell(totalRow, 8).Style.NumberFormat.Format = "₱#,##0.00";
                    break;
                case "topstores":
                    ws.Cell(totalRow, 1).Value = "TOTAL VOLUME";
                    ws.Cell(totalRow, 5).Value = sumVol;   ws.Cell(totalRow, 5).Style.NumberFormat.Format = "₱#,##0.00";
                    break;
                case "customers":
                    ws.Cell(totalRow, 1).Value = "TOTALS";
                    ws.Cell(totalRow, 4).Value = sumOrders;
                    ws.Cell(totalRow, 5).Value = sumSpent; ws.Cell(totalRow, 5).Style.NumberFormat.Format = "₱#,##0.00";
                    ws.Cell(totalRow, 6).Value = sumFees;  ws.Cell(totalRow, 6).Style.NumberFormat.Format = "₱#,##0.00";
                    break;
                default: // cancelled
                    ws.Cell(totalRow, 1).Value = "TOTAL CANCELLED";
                    ws.Cell(totalRow, 4).Value = $"{_currentData.Count} orders";
                    ws.Cell(totalRow, 5).Value = sumCancelled; ws.Cell(totalRow, 5).Style.NumberFormat.Format = "₱#,##0.00";
                    break;
            }

            // ── Formatting polish ──────────────────────────────────────────────
            ws.Columns().AdjustToContents();

            // Cap very wide columns at 55 chars
            foreach (var col in ws.ColumnsUsed())
                if (col.Width > 55) col.Width = 55;

            // Freeze header rows
            ws.SheetView.Freeze(dataStart, 0);

            wb.SaveAs(dlg.FileName);
            MessageBox.Show(
                $"Exported {_currentData.Count} records to:\n{dlg.FileName}",
                "Export Excel", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu != null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            btn.ContextMenu.IsOpen = true;
        }
    }

    private void OnExportPdf(object sender, RoutedEventArgs e)
    {
        if (_currentData.Count == 0)
        {
            MessageBox.Show("No report data available to export to PDF.", "Export PDF", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var dateRange = $"{FromDate.SelectedDate:yyyy-MM-dd}  to  {ToDate.SelectedDate:yyyy-MM-dd}";
            var summaryTotals = BuildSummaryTotals();
            var doc = PrinterService.CreateRichReportDocument(
                _activeReport,
                ReportTitleLabel.Text,
                dateRange,
                "All Stores",
                _currentData,
                summaryTotals,
                pageWidth: 1080);

            var (ok, msg) = PrinterService.PrintFlowDocument(
                doc,
                $"DIONIX POS - {_activeReport} Report",
                targetPrinter: "Microsoft Print to PDF",
                showDialog: false);  // Go directly to PDF — no printer picker

            if (ok)
                MessageBox.Show("PDF export completed successfully.", "Export PDF", MessageBoxButton.OK, MessageBoxImage.Information);
            else if (!msg.Contains("cancelled"))
                MessageBox.Show($"PDF export failed: {msg}", "Export PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"PDF export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnPrintReport(object sender, RoutedEventArgs e)
    {
        if (_currentData.Count == 0)
        {
            MessageBox.Show("No report data available to print.", "Print Report", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var dateRange = $"{FromDate.SelectedDate:yyyy-MM-dd}  to  {ToDate.SelectedDate:yyyy-MM-dd}";
            var summaryTotals = BuildSummaryTotals();
            var doc = PrinterService.CreateRichReportDocument(
                _activeReport,
                ReportTitleLabel.Text,
                dateRange,
                "All Stores",
                _currentData,
                summaryTotals,
                pageWidth: 1080);

            PrinterService.PrintFlowDocument(
                doc,
                $"DIONIX POS - {_activeReport} Report",
                targetPrinter: null,
                showDialog: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Print failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Builds a summary totals dictionary for the current active report
    /// to be passed into CreateRichReportDocument.
    /// </summary>
    private Dictionary<string, string> BuildSummaryTotals()
    {
        if (_activeReport == "sales")
        {
            decimal sumSub   = 0, sumFee = 0, sumDel = 0, sumGrand = 0;
            foreach (var r in _currentData)
            {
                sumSub   += Convert.ToDecimal(r.ItemsSubtotal);
                sumFee   += Convert.ToDecimal(r.PasabuyFee);
                sumDel   += Convert.ToDecimal(r.DeliveryFee);
                sumGrand += Convert.ToDecimal(r.GrandTotal);
            }
            return new Dictionary<string, string>
            {
                ["Items Subtotal"] = $"₱{sumSub:N2}",
                ["Pasabuy Fees"]   = $"₱{sumFee:N2}",
                ["Delivery Fees"]  = $"₱{sumDel:N2}",
                ["Grand Total"]    = $"₱{sumGrand:N2}"
            };
        }
        else if (_activeReport == "customers")
        {
            decimal sumSpent = 0, sumFees = 0;
            int    sumOrders = 0;
            foreach (var r in _currentData)
            {
                sumOrders += Convert.ToInt32(r.OrdersCount);
                sumSpent  += Convert.ToDecimal(r.TotalSpent);
                sumFees   += Convert.ToDecimal(r.PasabuyFeesPaid);
            }
            return new Dictionary<string, string>
            {
                ["Customers"]     = $"{_currentData.Count}",
                ["Total Orders"]  = $"{sumOrders}",
                ["Total Spent"]   = $"₱{sumSpent:N2}",
                ["Pasabuy Fees"]  = $"₱{sumFees:N2}"
            };
        }
        else if (_activeReport == "topstores")
        {
            decimal sumVol = 0;
            foreach (var r in _currentData)
                sumVol += Convert.ToDecimal(r.EstimatedVolume);
            return new Dictionary<string, string>
            {
                ["Total Top Records"] = $"{_currentData.Count}",
                ["Total Volume"]      = $"₱{sumVol:N2}"
            };
        }
        else // cancelled
        {
            decimal sumCancelled = 0;
            foreach (var r in _currentData)
                sumCancelled += Convert.ToDecimal(r.GrandTotal);

            return new Dictionary<string, string>
            {
                ["Total Cancelled Orders"] = $"{_currentData.Count}",
                ["Total Cancelled Value"]  = $"₱{sumCancelled:N2}"
            };
        }
    }

    private string BuildReportText()
    {
        var sb = new StringBuilder();
        const int W = 42;
        string Line(char c = '-') => new string(c, W);
        string Center(string s) => s.Length >= W ? s : s.PadLeft((W + s.Length) / 2).PadRight(W);
        string TwoCol(string left, string right)
        {
            var spaces = W - left.Length - right.Length;
            return spaces > 0 ? left + new string(' ', spaces) + right : $"{left} {right}";
        }

        sb.AppendLine(Line('='));
        sb.AppendLine(Center("DIONIX POS MANAGEMENT"));
        sb.AppendLine(Center($"{_activeReport.ToUpper()} REPORT"));
        sb.AppendLine(Line('='));
        sb.AppendLine(TwoCol("Period From:", FromDate.SelectedDate?.ToString("yyyy-MM-dd") ?? "—"));
        sb.AppendLine(TwoCol("Period To:", ToDate.SelectedDate?.ToString("yyyy-MM-dd") ?? "—"));
        sb.AppendLine(TwoCol("Printed On:", DateTime.Now.ToString("yyyy-MM-dd HH:mm")));
        sb.AppendLine(TwoCol("Total Records:", $"{_currentData.Count}"));
        sb.AppendLine(Line('-'));

        if (_activeReport == "sales")
        {
            sb.AppendLine(TwoCol("Total Revenue:", RevenueLabel.Text));
            sb.AppendLine(TwoCol("PASABUY FEES:", PasabuyFeeLabel.Text));
            sb.AppendLine(TwoCol("Delivery Fees:", DeliveryFeeLabel.Text));
            sb.AppendLine(Line('-'));
            foreach (var r in _currentData.Take(40))
            {
                sb.AppendLine(TwoCol($"{r.OrderNo} ({r.CustomerName})", $"₱{r.GrandTotal:N2}"));
            }
        }
        else if (_activeReport == "customers")
        {
            foreach (var r in _currentData.Take(40))
            {
                sb.AppendLine(TwoCol($"{r.Name} ({r.OrdersCount} ord)", $"₱{r.TotalSpent:N2}"));
            }
        }
        else if (_activeReport == "topstores")
        {
            foreach (var r in _currentData.Take(40))
            {
                sb.AppendLine(TwoCol($"{r.Name}", $"₱{Convert.ToDecimal(r.EstimatedVolume):N2}"));
            }
        }
        else // cancelled
        {
            foreach (var r in _currentData.Take(40))
            {
                sb.AppendLine(TwoCol($"{r.OrderNo}: {r.CustomerName}", $"₱{Convert.ToDecimal(r.GrandTotal):N2}"));
            }
        }

        sb.AppendLine(Line('='));
        sb.AppendLine(Center("End of Report"));
        sb.AppendLine();
        sb.AppendLine();

        return sb.ToString();
    }
}
