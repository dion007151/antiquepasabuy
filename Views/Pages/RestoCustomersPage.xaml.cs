using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;
using AntiquePasabuyPOS.Views.Dialogs;

namespace AntiquePasabuyPOS.Views.Pages;

public partial class RestoCustomersPage : Page
{
    private readonly AppDbContext _db;
    private readonly User _user;
    private readonly Action<string> _navigate;
    private List<Customer> _all = new();

    public RestoCustomersPage(AppDbContext db, User user, Action<string> navigate)
    {
        InitializeComponent();
        _db = db;
        _user = user;
        _navigate = navigate;

        Loaded += async (_, _) => await LoadCustomersAsync();
    }

    private async Task LoadCustomersAsync()
    {
        try
        {
            _all = await _db.Customers.AsNoTracking()
                .Where(c => c.IsActive && c.BusinessId == BusinessProfileService.ANGELITOS_RESTO)
                .OrderBy(c => c.Name)
                .ToListAsync();
            ApplySearch();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading customers: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplySearch()
    {
        var term = SearchBox?.Text?.Trim().ToLower() ?? "";
        var filtered = string.IsNullOrEmpty(term) ? _all
            : _all.Where(c =>
                (c.Name?.ToLower().Contains(term) ?? false) ||
                (c.Phone?.Contains(term) ?? false) ||
                (c.Code?.ToLower().Contains(term) ?? false) ||
                (c.Address?.ToLower().Contains(term) ?? false)).ToList();

        CustomersGrid.ItemsSource = filtered;
        TxtTotalCount.Text = _all.Count.ToString();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => ApplySearch();

    private void OnAddCustomerClick(object sender, RoutedEventArgs e)
    {
        var win = Window.GetWindow(this);
        var dlg = new CustomerDialog(new AppDbContext(), businessId: BusinessProfileService.ANGELITOS_RESTO) { Owner = win };
        if (dlg.ShowDialog() == true)
            _ = LoadCustomersAsync();
    }

    private async void OnEditCustomerClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Customer customer)
        {
            var win = Window.GetWindow(this);

            // Open a fresh isolated context for the dialog to avoid cross-context
            // tracking conflicts (the page's _db context doesn't own this entity).
            var dlgCtx = new AppDbContext();
            var fresh = await dlgCtx.Customers.FindAsync(customer.Id);
            if (fresh == null)
            {
                dlgCtx.Dispose();
                MessageBox.Show("Customer not found. They may have been deleted.",
                    "Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dlg = new CustomerDialog(dlgCtx, fresh) { Owner = win };
            if (dlg.ShowDialog() == true)
                await LoadCustomersAsync();

            dlgCtx.Dispose();
        }
    }

    private async void OnDeleteCustomerClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Customer customer)
        {
            var res = MessageBox.Show(
                $"Delete customer \"{customer.Name}\"?\nThis cannot be undone.",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (res != MessageBoxResult.Yes) return;

            try
            {
                var svc = new CustomerService(_db);
                var (ok, msg) = svc.Delete(customer.Id);
                if (!ok)
                    MessageBox.Show(msg, "Cannot Delete", MessageBoxButton.OK, MessageBoxImage.Warning);
                await LoadCustomersAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not delete customer: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

