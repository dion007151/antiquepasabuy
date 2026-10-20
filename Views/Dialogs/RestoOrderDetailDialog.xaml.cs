using System.Windows;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;

namespace AntiquePasabuyPOS.Views.Dialogs;

public partial class RestoOrderDetailDialog : Window
{
    private readonly RestoSale _sale;

    public RestoOrderDetailDialog(RestoSale sale)
    {
        InitializeComponent();
        _sale = sale;

        TxtSaleNo.Text = sale.SaleNo;
        TxtStatus.Text = sale.PaymentStatus.ToString().ToUpper();
        TxtTimestamp.Text = $"Date & Time: {sale.CreatedAt:MMM dd, yyyy  hh:mm tt}";

        TxtTableInfo.Text = string.IsNullOrWhiteSpace(sale.TableNumber) ? "Takeout / Counter" : $"Table {sale.TableNumber}";
        TxtCustomerInfo.Text = $"Guest: {(string.IsNullOrWhiteSpace(sale.CustomerName) ? "Walk-in Guest" : sale.CustomerName)}";
        TxtCashierInfo.Text = $"Staff: {(string.IsNullOrWhiteSpace(sale.CashierName) ? "Cashier" : sale.CashierName)}";
        TxtPaymentMethod.Text = $"Payment: {sale.PaymentMethod}";

        ItemsGrid.ItemsSource = sale.Items;

        TxtSubtotal.Text = $"₱{sale.Subtotal:N2}";
        TxtDiscount.Text = $"₱{sale.Discount:N2}";
        TxtGrandTotal.Text = $"₱{sale.TotalAmount:N2}";
    }

    private async void OnDeleteOrderClick(object sender, RoutedEventArgs e)
    {
        var cust = string.IsNullOrWhiteSpace(_sale.CustomerName) ? "Walk-in Guest" : _sale.CustomerName;
        var tableInfo = string.IsNullOrWhiteSpace(_sale.TableNumber) ? "Takeout / Counter" : $"Table {_sale.TableNumber}";

        var res = MessageBox.Show(
            $"Are you sure you want to permanently delete order {_sale.SaleNo}?\n\n" +
            $"• Guest / Table: {cust} ({tableInfo})\n" +
            $"• Total:         ₱{_sale.TotalAmount:N2}\n" +
            $"• Payment:       {_sale.PaymentMethod}\n" +
            $"• Date:          {_sale.CreatedAt:MMM dd, yyyy  hh:mm tt}\n\n" +
            "This will delete the order record and all its items permanently.",
            "Delete Restaurant Order",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (res == MessageBoxResult.Yes)
        {
            try
            {
                var ok = await RestaurantService.DeleteSaleAsync(_sale.Id);
                if (ok)
                {
                    MessageBox.Show($"Order {_sale.SaleNo} has been deleted.", "Order Deleted", MessageBoxButton.OK, MessageBoxImage.Information);
                    DialogResult = true;
                    Close();
                }
                else
                {
                    MessageBox.Show("Order could not be found. It may have already been removed.", "Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to delete order: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
