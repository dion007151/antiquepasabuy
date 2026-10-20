using System.Windows;
using System.Windows.Controls;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;

namespace AntiquePasabuyPOS.Views.Pages;

public partial class RestoKitchenPage : Page
{
    private readonly AppDbContext _db;
    private readonly User _user;
    private readonly Action<string> _navigate;

    public RestoKitchenPage(AppDbContext db, User user, Action<string> navigate)
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
            var active = await RestaurantService.GetActiveKitchenOrdersAsync();
            KitchenTicketsControl.ItemsSource = active;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading kitchen orders: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnAdvanceStatusClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is RestoOrder order)
        {
            var nextStatus = order.Status switch
            {
                RestoOrderStatus.Pending => RestoOrderStatus.Preparing,
                RestoOrderStatus.Preparing => RestoOrderStatus.Ready,
                RestoOrderStatus.Ready => RestoOrderStatus.Served,
                _ => RestoOrderStatus.Completed
            };

            await RestaurantService.UpdateOrderStatusAsync(order.Id, nextStatus);
            await LoadOrdersAsync();
        }
    }

    private async void OnCancelKitchenTicketClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is RestoOrder order)
        {
            var tableInfo = string.IsNullOrWhiteSpace(order.TableNumber) ? "" : $" (Table: {order.TableNumber})";
            var res = MessageBox.Show(
                $"Are you sure you want to cancel and delete kitchen ticket {order.OrderNo}{tableInfo}?\n\nThis will remove the ticket from the kitchen display.",
                "Cancel Kitchen Ticket",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (res == MessageBoxResult.Yes)
            {
                try
                {
                    await RestaurantService.DeleteOrderAsync(order.Id);
                    await LoadOrdersAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to delete ticket: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await LoadOrdersAsync();
}
