using System.Windows;
using System.Windows.Controls;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;

namespace AntiquePasabuyPOS.Views.Pages;

public partial class RestoReportsPage : Page
{
    private readonly AppDbContext _db;
    private readonly User _user;
    private readonly Action<string> _navigate;

    private List<RestoSale> _allSales = new();
    private List<RestoInventoryItem> _allInv = new();
    private string _activeDateFilter = "Today";

    public RestoReportsPage(AppDbContext db, User user, Action<string> navigate, string initialTab = "sales")
    {
        InitializeComponent();
        _db = db;
        _user = user;
        _navigate = navigate;

        Loaded += async (_, _) =>
        {
            if (initialTab == "inventory")
                ShowInventoryTab();
            else
                ShowSalesTab();

            await LoadDataAsync();
        };
    }

    private async Task LoadDataAsync()
    {
        try
        {
            _allSales = await RestaurantService.GetSalesAsync();
            ApplySalesFilter();

            _allInv = await RestaurantService.GetInventoryItemsAsync();
            ApplyInventoryFilter();

            // Populate inventory summary cards
            TxtInvItemCount.Text  = _allInv.Count.ToString();
            TxtInvLowCount.Text   = _allInv.Count(i => i.IsLowStock).ToString();
            TxtInvTotalValue.Text = $"₱{_allInv.Sum(i => i.StockValue):N2}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading reports: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplySalesFilter()
    {
        if (SalesGrid == null || _allSales == null) return;

        var query = _allSales.AsEnumerable();
        var now = DateTime.Now;

        query = _activeDateFilter switch
        {
            "Today" => query.Where(s => s.CreatedAt.Date == now.Date),
            "Yesterday" => query.Where(s => s.CreatedAt.Date == now.Date.AddDays(-1)),
            "ThisWeek" => query.Where(s => s.CreatedAt.Date >= now.Date.AddDays(-(int)now.DayOfWeek)),
            "ThisMonth" => query.Where(s => s.CreatedAt.Year == now.Year && s.CreatedAt.Month == now.Month),
            _ => query
        };

        var search = TxtSearchReports?.Text?.Trim().ToLower() ?? "";
        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(s =>
                (s.SaleNo != null && s.SaleNo.ToLower().Contains(search)) ||
                (s.TableNumber != null && s.TableNumber.ToLower().Contains(search)) ||
                (s.CustomerName != null && s.CustomerName.ToLower().Contains(search)) ||
                (s.PaymentMethod != null && s.PaymentMethod.ToLower().Contains(search)) ||
                (s.CashierName != null && s.CashierName.ToLower().Contains(search))
            );
        }

        var list = query.OrderByDescending(s => s.CreatedAt).ToList();
        SalesGrid.ItemsSource = list;

        var totalRev = list.Sum(s => s.TotalAmount);
        var cashRev  = list.Where(s => s.PaymentMethod == "Cash").Sum(s => s.TotalAmount);
        var digRev   = list.Where(s => s.PaymentMethod != "Cash").Sum(s => s.TotalAmount);
        var avgTicket = list.Count > 0 ? (totalRev / list.Count) : 0m;

        if (TxtTotalRevenue != null) TxtTotalRevenue.Text = $"₱{totalRev:N2}";
        if (TxtTotalOrdersCount != null) TxtTotalOrdersCount.Text = $"{list.Count} {(list.Count == 1 ? "order" : "orders")}";
        if (TxtAvgOrderValue != null) TxtAvgOrderValue.Text = $"₱{avgTicket:N2}";
        if (TxtCashSales != null) TxtCashSales.Text = $"₱{cashRev:N2}";
        if (TxtDigitalSales != null) TxtDigitalSales.Text = $"₱{digRev:N2}";
    }

    private void ApplyInventoryFilter()
    {
        if (InventoryReportGrid == null || _allInv == null) return;

        var search = TxtSearchInventory?.Text?.Trim().ToLower() ?? "";
        var query = _allInv.AsEnumerable();

        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(i =>
                (i.Name != null && i.Name.ToLower().Contains(search)) ||
                (i.ItemCode != null && i.ItemCode.ToLower().Contains(search)) ||
                (i.Category != null && i.Category.ToLower().Contains(search))
            );
        }

        InventoryReportGrid.ItemsSource = query.OrderBy(i => i.Name).ToList();
    }

    private void OnReportDatePillClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            _activeDateFilter = tag;

            if (ReportDatePillsPanel != null)
            {
                foreach (var child in ReportDatePillsPanel.Children)
                {
                    if (child is Button b)
                    {
                        b.Style = (b.Tag?.ToString() == tag)
                            ? (Style)FindResource("Pos3DPrimaryBtn")
                            : (Style)FindResource("Pos3DSecondaryBtn");
                    }
                }
            }

            ApplySalesFilter();
        }
    }

    private void OnSearchReportsChanged(object sender, TextChangedEventArgs e) => ApplySalesFilter();

    private void OnSearchInventoryChanged(object sender, TextChangedEventArgs e) => ApplyInventoryFilter();

    private void OnTabSalesClick(object sender, RoutedEventArgs e) => ShowSalesTab();
    private void OnTabInventoryClick(object sender, RoutedEventArgs e) => ShowInventoryTab();

    private void ShowSalesTab()
    {
        SalesReportsPanel.Visibility = Visibility.Visible;
        InventoryReportsPanel.Visibility = Visibility.Collapsed;
        BtnTabSales.Style = (Style)FindResource("Pos3DPrimaryBtn");
        BtnTabInventory.Style = (Style)FindResource("Pos3DSecondaryBtn");
    }

    private void ShowInventoryTab()
    {
        SalesReportsPanel.Visibility = Visibility.Collapsed;
        InventoryReportsPanel.Visibility = Visibility.Visible;
        BtnTabSales.Style = (Style)FindResource("Pos3DSecondaryBtn");
        BtnTabInventory.Style = (Style)FindResource("Pos3DPrimaryBtn");
    }
}
