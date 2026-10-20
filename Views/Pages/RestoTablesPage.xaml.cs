using System.Windows;
using System.Windows.Controls;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;
using AntiquePasabuyPOS.Views.Dialogs;

namespace AntiquePasabuyPOS.Views.Pages;

public partial class RestoTablesPage : Page
{
    private readonly AppDbContext _db;
    private readonly User _user;
    private readonly Action<string> _navigate;

    private List<RestoTable> _allTables = new();
    private string _activeFilter = "All";

    public RestoTablesPage(AppDbContext db, User user, Action<string> navigate)
    {
        InitializeComponent();
        _db = db;
        _user = user;
        _navigate = navigate;

        Loaded += async (_, _) => await LoadTablesAsync();
    }

    private async Task LoadTablesAsync()
    {
        try
        {
            // Sync running bills first with any active orders
            await RestaurantService.SyncAllTableBillsAsync();

            _allTables = await RestaurantService.GetTablesAsync();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading tables: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyFilter()
    {
        if (TablesCardsControl == null || _allTables == null) return;

        var filtered = _allTables.AsEnumerable();
        if (_activeFilter != "All")
        {
            if (Enum.TryParse<RestoTableStatus>(_activeFilter, out var targetStatus))
            {
                filtered = filtered.Where(t => t.Status == targetStatus);
            }
        }

        var list = filtered.ToList();
        TablesCardsControl.ItemsSource = list;

        // Update summary label
        var occupiedCount = _allTables.Count(t => t.Status == RestoTableStatus.Occupied || t.Status == RestoTableStatus.Billing);
        var totalBills = _allTables.Sum(t => t.CurrentBillAmount);
        if (TxtTableSummary != null)
        {
            TxtTableSummary.Text = $"Tables: {_allTables.Count} | Occupied: {occupiedCount} | Total Running Bills: ₱{totalBills:N2}";
        }
    }

    private void OnFilterStatusClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string filter)
        {
            _activeFilter = filter;

            if (TableFilterPillsPanel != null)
            {
                foreach (var child in TableFilterPillsPanel.Children)
                {
                    if (child is Button b)
                    {
                        b.Style = (b.Tag?.ToString() == filter)
                            ? (Style)FindResource("Pos3DPrimaryBtn")
                            : (Style)FindResource("Pos3DSecondaryBtn");
                    }
                }
            }

            ApplyFilter();
        }
    }

    private void OnTakeOrderClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is RestoTable table)
        {
            RestoSalesPage.PreSelectedTableNumber = table.TableNumber;
            _navigate("RestoSales");
        }
    }

    private void OnViewAddItemsClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is RestoTable table)
        {
            RestoSalesPage.PreSelectedTableNumber = table.TableNumber;
            _navigate("RestoSales");
        }
    }

    private void OnSettleBillClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is RestoTable table)
        {
            RestoSalesPage.PreSelectedTableNumber = table.TableNumber;
            RestoSalesPage.PreSelectForCheckout = true;
            _navigate("RestoSales");
        }
    }

    private void OnAddTableClick(object sender, RoutedEventArgs e)
    {
        var win = Window.GetWindow(this);
        var dlgCtx = new AppDbContext();
        var dlg = new RestoTableDialog(dlgCtx) { Owner = win };

        if (dlg.ShowDialog() == true)
        {
            _ = LoadTablesAsync();
        }
    }

    private async void OnEditTableClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is RestoTable tableRef)
        {
            var win = Window.GetWindow(this);

            var dlgCtx = new AppDbContext();
            var trackedTable = await dlgCtx.RestoTables.FindAsync(tableRef.Id);
            if (trackedTable == null)
            {
                dlgCtx.Dispose();
                MessageBox.Show("Table not found. It may have been deleted.", "Not Found",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dlg = new RestoTableDialog(dlgCtx, trackedTable) { Owner = win };

            if (dlg.ShowDialog() == true)
            {
                await LoadTablesAsync();
            }

            dlgCtx.Dispose();
        }
    }

    private async void OnDeleteTableClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is RestoTable table)
        {
            var res = MessageBox.Show(
                $"Delete table {table.TableNumber} — {table.Name}?\nThis cannot be undone.",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                var ok = await RestaurantService.DeleteTableAsync(table.Id);
                if (!ok)
                    MessageBox.Show("Could not delete the table. It may already be removed.",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                await LoadTablesAsync();
            }
        }
    }

    private async void OnToggleTableStatusClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is RestoTable table)
        {
            var nextStatus = table.Status == RestoTableStatus.Available
                ? RestoTableStatus.Occupied
                : RestoTableStatus.Available;

            await RestaurantService.UpdateTableStatusAsync(table.Id, nextStatus, null, 0m);
            await LoadTablesAsync();
        }
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await LoadTablesAsync();
}
