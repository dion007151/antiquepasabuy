using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;
using AntiquePasabuyPOS.Views.Dialogs;

namespace AntiquePasabuyPOS.Views.Pages;

public partial class RestoMenuPage : Page
{
    private readonly AppDbContext _db;
    private readonly User _user;
    private readonly Action<string> _navigate;
    private List<RestoMenuItem> _menuItems = new();

    public RestoMenuPage(AppDbContext db, User user, Action<string> navigate)
    {
        InitializeComponent();
        _db = db;
        _user = user;
        _navigate = navigate;

        Loaded += async (_, _) => await LoadMenuAsync();
    }

    private async Task LoadMenuAsync()
    {
        try
        {
            _menuItems = await RestaurantService.GetMenuItemsAsync();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading menu: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyFilter()
    {
        if (MenuGrid == null || _menuItems == null) return;
        var search = TxtSearchMenu?.Text?.Trim().ToLower() ?? "";
        MenuGrid.ItemsSource = _menuItems
            .Where(m => string.IsNullOrEmpty(search) || 
                        (m.Name != null && m.Name.ToLower().Contains(search)) || 
                        (m.Code != null && m.Code.ToLower().Contains(search)) ||
                        (m.Category != null && m.Category.ToLower().Contains(search)))
            .ToList();
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void OnAddDishClick(object sender, RoutedEventArgs e)
    {
        var win = Window.GetWindow(this);
        var dlg = new RestoMenuDialog(new AppDbContext())
        {
            Owner = win
        };

        if (dlg.ShowDialog() == true)
        {
            _ = LoadMenuAsync();
        }
    }

    private void OnEditDishRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is RestoMenuItem item)
        {
            EditDish(item);
        }
    }

    private void OnGridRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (MenuGrid.SelectedItem is RestoMenuItem item)
        {
            EditDish(item);
        }
    }

    private async void EditDish(RestoMenuItem item)
    {
        var win = Window.GetWindow(this);
        var dlgCtx = new AppDbContext();
        var tracked = await dlgCtx.RestoMenuItems.FindAsync(item.Id);
        if (tracked == null)
        {
            dlgCtx.Dispose();
            MessageBox.Show("Dish not found. It may have been deleted.",
                "Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dlg = new RestoMenuDialog(dlgCtx, tracked) { Owner = win };
        if (dlg.ShowDialog() == true)
            await LoadMenuAsync();

        dlgCtx.Dispose();
    }

    private async void OnDeleteDishRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is RestoMenuItem item)
        {
            var res = MessageBox.Show($"Are you sure you want to remove '{item.Name}' from the menu?", 
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                await RestaurantService.DeleteMenuItemAsync(item.Id);
                if (!string.IsNullOrWhiteSpace(item.ImagePath))
                {
                    ProductImageService.DeleteProductImage(item.ImagePath);
                }
                await LoadMenuAsync();
            }
        }
    }
}
