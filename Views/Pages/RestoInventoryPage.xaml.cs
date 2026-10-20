using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;
using AntiquePasabuyPOS.Views.Dialogs;

namespace AntiquePasabuyPOS.Views.Pages;

public partial class RestoInventoryPage : Page
{
    private readonly AppDbContext _db;
    private readonly User _user;
    private readonly Action<string> _navigate;
    private List<RestoInventoryItem> _items = new();

    public RestoInventoryPage(AppDbContext db, User user, Action<string> navigate)
    {
        InitializeComponent();
        _db = db;
        _user = user;
        _navigate = navigate;

        Loaded += async (_, _) => await LoadInventoryAsync();
    }

    private async Task LoadInventoryAsync()
    {
        try
        {
            _items = await RestaurantService.GetInventoryItemsAsync();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading inventory: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyFilter()
    {
        if (InventoryGrid == null || _items == null) return;
        var search = TxtSearchInventory?.Text?.Trim().ToLower() ?? "";
        InventoryGrid.ItemsSource = _items
            .Where(i => string.IsNullOrEmpty(search) ||
                        (i.Name != null && i.Name.ToLower().Contains(search)) ||
                        (i.ItemCode != null && i.ItemCode.ToLower().Contains(search)) ||
                        (i.Category != null && i.Category.ToLower().Contains(search)))
            .ToList();
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void OnRestockClick(object sender, RoutedEventArgs e)
    {
        OpenRestockDialog();
    }

    private void OnRestockRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is RestoInventoryItem item)
        {
            OpenRestockDialog(item);
        }
    }

    private void OnGridRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (InventoryGrid.SelectedItem is RestoInventoryItem item)
        {
            OpenRestockDialog(item);
        }
    }

    private void OpenRestockDialog(RestoInventoryItem? preselected = null)
    {
        var win = Window.GetWindow(this);
        var dlg = new RestoRestockDialog(new AppDbContext(), _items, preselected)
        {
            Owner = win
        };

        if (dlg.ShowDialog() == true)
        {
            _ = LoadInventoryAsync();
        }
    }

    private async void OnDeleteRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is RestoInventoryItem item)
        {
            var res = MessageBox.Show($"Are you sure you want to remove item '{item.Name}' from inventory?",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                await RestaurantService.DeleteInventoryItemAsync(item.Id);
                await LoadInventoryAsync();
            }
        }
    }
}
