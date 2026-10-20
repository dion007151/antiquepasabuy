using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;
using AntiquePasabuyPOS.Views.Dialogs;

namespace AntiquePasabuyPOS.Views.Pages;

public class CustomerRowVm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? prop = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));

    public Customer Customer { get; }
    public Action? OnSelectionChanged { get; set; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
                OnSelectionChanged?.Invoke();
            }
        }
    }

    public CustomerRowVm(Customer c, Action? onSelectionChanged = null)
    {
        Customer = c;
        OnSelectionChanged = onSelectionChanged;
    }

    public int Id => Customer.Id;
    public string Code => Customer.Code;
    public string Name => Customer.Name;
    public string Phone => Customer.Phone;
    public string Address => Customer.Address;
    public ICollection<Order> Orders => Customer.Orders;
    public string InitialsDisplay => Customer.InitialsDisplay;
}


public partial class CustomersPage : Page
{
    private readonly AppDbContext _db;
    private readonly User _user;
    private readonly Action<string>? _navigate;
    private List<CustomerRowVm> _all = [];
    private List<CustomerRowVm> _visible = [];

    private bool _isLoadingCustomers = false;

    public CustomersPage(AppDbContext db, User user, Action<string>? navigate = null, Action? onSuccess = null)
    {
        InitializeComponent();
        _db = db;
        _user = user;
        _navigate = navigate;
        Loaded += (_, _) =>
        {
            if (_all.Count > 0)
            {
                // Instant navigation: cached customer list is visible with 0ms delay!
                ApplySearch();
                Dispatcher.InvokeAsync(LoadData, System.Windows.Threading.DispatcherPriority.Background);
            }
            else
            {
                LoadData();
            }
        };
    }

    private async void LoadData()
    {
        if (_isLoadingCustomers) return;
        _isLoadingCustomers = true;
        try
        {
            List<Customer> list = [];
            await Task.Run(() =>
            {
                using var bgDb = new AppDbContext();
                var svc = new CustomerService(bgDb);
                list = svc.GetAll(businessId: BusinessProfileService.ANTIQUE_PASABUY);
            });

            _all = list.Select(c => new CustomerRowVm(c, UpdateSelectionState)).ToList();
            ApplySearch();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CustomersPage] LoadData error: {ex.Message}");
        }
        finally
        {
            _isLoadingCustomers = false;
        }
    }

    private void ApplySearch()
    {
        if (SearchBox is null || CustomersGrid is null || _all is null) return;
        var term = SearchBox.Text?.Trim().ToLower() ?? "";
        _visible = string.IsNullOrEmpty(term) ? _all
            : _all.Where(c => (c.Name?.ToLower().Contains(term) ?? false) ||
                              (c.Phone?.Contains(term) ?? false) ||
                              (c.Address?.ToLower().Contains(term) ?? false) ||
                              (c.Code?.ToLower().Contains(term) ?? false)).ToList();
        CustomersGrid.ItemsSource = _visible;
        UpdateSelectionState();
    }

    // ══ SELECTION CONTROLS ════════════════════════════════════════════════════
    private void OnHeaderSelectAllClick(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb)
        {
            var target = cb.IsChecked == true;
            foreach (var r in _visible) r.IsSelected = target;
            UpdateSelectionState();
        }
    }

    private void OnRowCheckBoxClick(object sender, RoutedEventArgs e) => UpdateSelectionState();

    private void OnSelectAllClick(object sender, RoutedEventArgs e)
    {
        foreach (var r in _visible) r.IsSelected = true;
        UpdateSelectionState();
    }

    private void OnClearSelectionClick(object sender, RoutedEventArgs e)
    {
        foreach (var r in _all) r.IsSelected = false;
        UpdateSelectionState();
    }

    private void UpdateSelectionState()
    {
        var count = _visible.Count(r => r.IsSelected);
        if (SelectedCountLabel != null)
            SelectedCountLabel.Text = $"{count} selected";
        if (BulkDeleteBtn != null)
            BulkDeleteBtn.IsEnabled = count > 0;
    }

    private void OnBulkDeleteClick(object sender, RoutedEventArgs e)
    {
        var selected = _visible.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0) return;

        var res = MessageBox.Show(
            $"Are you sure you want to delete {selected.Count} selected customer(s)?\n\nThis cannot be undone.",
            "Confirm Bulk Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (res != MessageBoxResult.Yes) return;

        var svc = new CustomerService(_db);
        int deletedCount = 0;
        foreach (var vm in selected)
        {
            var (ok, _) = svc.Delete(vm.Id);
            if (ok) deletedCount++;
        }

        MessageBox.Show($"Successfully deleted {deletedCount} of {selected.Count} customer(s).",
            "Bulk Delete Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        LoadData();
    }

    private void OnClearSearch(object s, RoutedEventArgs e)
    {
        if (SearchBox is not null)
        {
            SearchBox.Text = "";
            SearchBox.Focus();
        }
    }

    private void OnSearch(object s, TextChangedEventArgs e) => ApplySearch();

    private void OnAddCustomer(object s, RoutedEventArgs e)
    {
        var dlg = new CustomerDialog(_db, businessId: BusinessProfileService.ANTIQUE_PASABUY);
        dlg.Owner = Window.GetWindow(this);
        if (dlg.ShowDialog() == true) LoadData();
    }

    private void OnViewCustomer(object s, RoutedEventArgs e)
    {
        if (s is not Button btn || btn.Tag is not int id) return;
        var svc = new CustomerService(_db);
        var c = svc.GetById(id);
        if (c is null) return;

        var dlg = new CustomerDetailsDialog(_db, c);
        dlg.Owner = Window.GetWindow(this);
        var res = dlg.ShowDialog();
        if (dlg.StartNewOrder)
        {
            _navigate?.Invoke("NewOrder");
        }
        else
        {
            LoadData();
        }
    }

    private void OnMoreMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu != null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.IsOpen = true;
        }
    }

    private Customer? GetCustomerFromSender(object sender)
    {
        if (sender is FrameworkElement fe)
        {
            // If sender is MenuItem inside ContextMenu
            if (fe.Parent is ContextMenu cm && cm.PlacementTarget is Button btn && btn.Tag is int id)
            {
                var svc = new CustomerService(_db);
                return svc.GetById(id);
            }
            if (fe.Tag is int directId)
            {
                var svc = new CustomerService(_db);
                return svc.GetById(directId);
            }
        }
        return null;
    }

    private void OnEditCustomerFromMenu(object sender, RoutedEventArgs e)
    {
        var c = GetCustomerFromSender(sender);
        if (c is null) return;
        var dlg = new CustomerDialog(_db, c);
        dlg.Owner = Window.GetWindow(this);
        if (dlg.ShowDialog() == true) LoadData();
    }

    private void OnDeleteCustomerFromMenu(object sender, RoutedEventArgs e)
    {
        var c = GetCustomerFromSender(sender);
        if (c is null) return;

        var res = MessageBox.Show(
            $"Are you sure you want to delete customer '{c.Name}'?",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (res == MessageBoxResult.Yes)
        {
            var svc = new CustomerService(_db);
            var (ok, msg) = svc.Delete(c.Id);
            MessageBox.Show(msg, ok ? "Customer Deleted" : "Error",
                MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
            if (ok) LoadData();
        }
    }
}
