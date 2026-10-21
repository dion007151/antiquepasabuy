using System.Windows;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using Microsoft.EntityFrameworkCore;

namespace AntiquePasabuyPOS.Views.Dialogs;

public partial class CustomerDetailsDialog : Window
{
    private readonly AppDbContext _db;
    public Customer Customer { get; private set; }
    public bool StartNewOrder { get; private set; }

    public CustomerDetailsDialog(AppDbContext db, Customer customer)
    {
        InitializeComponent();
        _db = db;
        Customer = customer;
        LoadCustomerData();
    }

    private void LoadCustomerData()
    {
        NameText.Text = Customer.Name;
        CodeText.Text = string.IsNullOrWhiteSpace(Customer.Code) ? $"CUST-{Customer.Id:D4}" : Customer.Code;
        PhoneText.Text = string.IsNullOrWhiteSpace(Customer.Phone) ? "No phone" : Customer.Phone;
        AddressText.Text = string.IsNullOrWhiteSpace(Customer.Address) ? "No address specified" : Customer.Address;

        var orders = _db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.Payments)
            .Where(o => o.CustomerId == Customer.Id)
            .OrderByDescending(o => o.CreatedAt)
            .ToList();

        TotalOrdersText.Text = orders.Count.ToString();
        TotalSpentText.Text = $"₱{orders.Sum(o => o.GrandTotal):N2}";
        TotalFeesText.Text = $"₱{orders.Sum(o => o.PasabuyFee):N2}";
        OrderCountBadge.Text = $"({orders.Count} {(orders.Count == 1 ? "order" : "orders")})";

        HistoryGrid.ItemsSource = orders;
    }

    private void OnNewOrderClick(object sender, RoutedEventArgs e)
    {
        StartNewOrder = true;
        DialogResult = true;
        Close();
    }

    private void OnEditClick(object sender, RoutedEventArgs e)
    {
        var dlg = new CustomerDialog(_db, Customer);
        dlg.Owner = this;
        if (dlg.ShowDialog() == true)
        {
            var refreshed = _db.Customers.Find(Customer.Id);
            if (refreshed != null)
            {
                Customer = refreshed;
                LoadCustomerData();
            }
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
