using System.Windows;
using System.Windows.Controls;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;

namespace AntiquePasabuyPOS.Views.Pages;

public partial class RestoDashboardPage : Page
{
    private readonly AppDbContext _db;
    private readonly User _user;
    private readonly Action<string> _navigate;

    public RestoDashboardPage(AppDbContext db, User user, Action<string> navigate)
    {
        InitializeComponent();
        _db = db;
        _user = user;
        _navigate = navigate;

        Loaded += async (_, _) => await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        try
        {
            var today = DateTime.Today;
            var sales = await RestaurantService.GetSalesAsync(today, today.AddDays(1).AddTicks(-1));
            var totalToday = sales.Sum(s => s.TotalAmount);
            TxtTodaySales.Text = $"₱{totalToday:N2}";
            TxtSalesCount.Text = $"{sales.Count} sales recorded today";

            var tables = await RestaurantService.GetTablesAsync();
            var occupied = tables.Count(t => t.Status == RestoTableStatus.Occupied);
            TxtOccupiedTables.Text = $"{occupied} / {tables.Count}";
            TxtAvailableTables.Text = $"{tables.Count - occupied} tables available";
            TablesItemsControl.ItemsSource = tables;

            var kitchenOrders = await RestaurantService.GetActiveKitchenOrdersAsync();
            TxtKitchenOrders.Text = kitchenOrders.Count.ToString();

            var menuItems = await RestaurantService.GetMenuItemsAsync();
            TxtMenuItemsCount.Text = menuItems.Count.ToString();

            var allSales = await RestaurantService.GetSalesAsync();
            RecentSalesGrid.ItemsSource = sales.Count > 0 ? sales.Take(8).ToList() : allSales.Take(8).ToList();
            if (sales.Count == 0 && allSales.Count > 0)
            {
                TxtSalesCount.Text = $"{allSales.Count} total sales on record";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"RestoDashboard load error: {ex.Message}");
        }
    }

    private void OnNewSaleClick(object sender, RoutedEventArgs e) => _navigate("RestoSales");
    private void OnViewTablesClick(object sender, RoutedEventArgs e) => _navigate("RestoTables");
}
