using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;
using AntiquePasabuyPOS.Views.Dialogs;
using Microsoft.EntityFrameworkCore;

namespace AntiquePasabuyPOS.Views.Pages;

public partial class PaymentsPage : Page
{
    private readonly AppDbContext _db;
    private readonly User _user;
    private readonly Action<string>? _navigate;
    private readonly string _initialTab;

    private List<Payment> _allPayments = [];
    private List<Order> _unpaidOrders = [];
    private string _activeTab = "unpaid";
    private Order? _selectedOrderForCollection = null;

    public PaymentsPage(AppDbContext db, User user, Action<string>? navigate = null, Action? onSuccess = null, string? initialFilter = null)
    {
        InitializeComponent();
        _db = db;
        _user = user;
        _navigate = navigate;
        _initialTab = string.IsNullOrWhiteSpace(initialFilter) ? "unpaid" : initialFilter.ToLower();

        Loaded += (_, _) =>
        {
            SetTab(_initialTab);
            LoadData();

            OrderEventBus.OrderCreated += OnExternalOrderCreated;
            OrderEventBus.OrderStatusChanged += OnExternalStatusChanged;
            OrderEventBus.PaymentRecorded += OnExternalPayment;
            OrderEventBus.DataRefreshed += OnExternalRefresh;
        };

        Unloaded += (_, _) =>
        {
            OrderEventBus.OrderCreated -= OnExternalOrderCreated;
            OrderEventBus.OrderStatusChanged -= OnExternalStatusChanged;
            OrderEventBus.PaymentRecorded -= OnExternalPayment;
            OrderEventBus.DataRefreshed -= OnExternalRefresh;
        };
    }

    private void OnExternalOrderCreated(int id) => Dispatcher.InvokeAsync(LoadData);
    private void OnExternalStatusChanged(int id, OrderStatus s) => Dispatcher.InvokeAsync(LoadData);
    private void OnExternalPayment(int id, decimal amt) => Dispatcher.InvokeAsync(LoadData);
    private void OnExternalRefresh() => Dispatcher.InvokeAsync(LoadData);

    private void SetTab(string tab)
    {
        _activeTab = tab;
        NavProps.SetIsActive(TabUnpaid, tab == "unpaid");
        NavProps.SetIsActive(TabToday,  tab == "today");
        NavProps.SetIsActive(TabAll,    tab == "all");

        TabUnpaid.Foreground = tab == "unpaid" ? Brushes.White : new SolidColorBrush(Color.FromRgb(71, 85, 105));
        TabToday.Foreground  = tab == "today"  ? Brushes.White : new SolidColorBrush(Color.FromRgb(71, 85, 105));
        TabAll.Foreground    = tab == "all"    ? Brushes.White : new SolidColorBrush(Color.FromRgb(71, 85, 105));

        UnpaidOrdersContainer.Visibility = tab == "unpaid" ? Visibility.Visible : Visibility.Collapsed;
        TransactionsContainer.Visibility = tab != "unpaid" ? Visibility.Visible : Visibility.Collapsed;

        ApplyFilter();
    }

    private void OnTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            SetTab(tag);
        }
    }

    private void LoadData()
    {
        // 1. Load Payments
        _allPayments = _db.Payments
            .AsNoTracking()
            .Include(p => p.Order!)
            .ThenInclude(o => o.Customer)
            .OrderByDescending(p => p.CreatedAt)
            .ToList();

        // 2. Load Unpaid Orders
        _unpaidOrders = _db.Orders
            .AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .Include(o => o.Payments)
            .Where(o => o.Status != OrderStatus.CANCELLED && o.Status != OrderStatus.Cancelled)
            .ToList()
            .Where(o => o.Balance > 0)
            .OrderByDescending(o => o.CreatedAt)
            .ToList();

        // 3. Update KPI Labels
        var today = DateTime.Today;
        var todayPayments = _allPayments.Where(p => p.CreatedAt >= today).ToList();
        var monthPayments = _allPayments.Where(p => p.CreatedAt >= new DateTime(today.Year, today.Month, 1)).ToList();

        var totalUnpaid = _unpaidOrders.Sum(o => o.Balance);
        UnpaidTotalLabel.Text = $"₱{totalUnpaid:N2}";
        UnpaidOrdersCountLabel.Text = $"{_unpaidOrders.Count} Orders pending payment";
        TabUnpaidText.Text = $"UNPAID ORDERS ({_unpaidOrders.Count})";

        TodayLabel.Text = $"₱{todayPayments.Sum(p => p.Amount):N2}";
        TodayCountLabel.Text = $"{todayPayments.Count} Payments recorded today";

        MonthLabel.Text = $"₱{monthPayments.Sum(p => p.Amount):N2}";
        TotalTransactionsLabel.Text = $"{_allPayments.Count} Total recorded transactions";

        // Setup modal order selector combo
        ModalOrderCombo.ItemsSource = _unpaidOrders;

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var q = SearchBox?.Text?.Trim().ToLower() ?? "";
        if (ClearSearchBtn != null)
            ClearSearchBtn.Visibility = string.IsNullOrEmpty(q) ? Visibility.Collapsed : Visibility.Visible;

        if (_activeTab == "unpaid")
        {
            var filtered = string.IsNullOrEmpty(q)
                ? _unpaidOrders
                : _unpaidOrders.Where(o =>
                    (o.OrderNo?.ToLower().Contains(q) ?? false) ||
                    (o.Customer?.Name?.ToLower().Contains(q) ?? false) ||
                    (o.Customer?.Phone?.Contains(q) ?? false) ||
                    o.Status.ToString().ToLower().Contains(q)).ToList();

            UnpaidOrdersGrid.ItemsSource = filtered;
            if (EmptyStatePanelUnpaid != null)
                EmptyStatePanelUnpaid.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            var payments = _activeTab == "today"
                ? _allPayments.Where(p => p.CreatedAt >= DateTime.Today).ToList()
                : _allPayments;

            var filtered = string.IsNullOrEmpty(q)
                ? payments
                : payments.Where(p =>
                    (p.Order?.OrderNo?.ToLower().Contains(q) ?? false) ||
                    (p.Order?.Customer?.Name?.ToLower().Contains(q) ?? false) ||
                    p.Method.ToString().ToLower().Contains(q) ||
                    (!string.IsNullOrEmpty(p.Reference) && p.Reference.ToLower().Contains(q))).ToList();

            MainGrid.ItemsSource = filtered;
            if (EmptyStatePanelTransactions != null)
                EmptyStatePanelTransactions.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void OnClearSearch(object sender, RoutedEventArgs e)
    {
        if (SearchBox != null)
        {
            SearchBox.Text = "";
            SearchBox.Focus();
        }
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => LoadData();

    private void OnCollectPaymentClick(object sender, RoutedEventArgs e)
    {
        if (_unpaidOrders.Count == 0)
        {
            MessageBox.Show("There are no unpaid orders currently requiring payment recording.", "No Unpaid Orders",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var order = _unpaidOrders.First();
        var fullOrder = new OrderService(_db).GetById(order.Id) ?? order;
        var dlg = new CollectPaymentDialog(_db, fullOrder, _user)
        {
            Owner = Window.GetWindow(this)
        };
        if (dlg.ShowDialog() == true)
        {
            LoadData();
        }
    }

    private void OnCollectRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int orderId)
        {
            var order = _unpaidOrders.FirstOrDefault(o => o.Id == orderId);
            if (order != null)
            {
                var fullOrder = new OrderService(_db).GetById(order.Id) ?? order;
                var dlg = new CollectPaymentDialog(_db, fullOrder, _user)
                {
                    Owner = Window.GetWindow(this)
                };
                if (dlg.ShowDialog() == true)
                {
                    LoadData();
                }
            }
        }
    }

    private void OpenCollectionModal(Order order)
    {
        _selectedOrderForCollection = order;
        ModalOrderNoText.Text = $"Order: {order.OrderNo}";
        ModalCustomerText.Text = $"Customer: {(order.Customer?.Name ?? "Walk-in")}";
        ModalBalanceText.Text = $"₱{order.Balance:N2}";
        ModalAmountBox.Text = order.Balance.ToString("F2");
        ModalReferenceBox.Text = "";
        ModalMethodCombo.SelectedIndex = 0;

        OrderSelectorPanel.Visibility = _unpaidOrders.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        ModalOrderCombo.SelectedItem = order;

        CollectionModal.Visibility = Visibility.Visible;
        ModalAmountBox.Focus();
        ModalAmountBox.SelectAll();
    }

    private void OnModalOrderSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ModalOrderCombo.SelectedItem is Order selected)
        {
            _selectedOrderForCollection = selected;
            ModalOrderNoText.Text = $"Order: {selected.OrderNo}";
            ModalCustomerText.Text = $"Customer: {(selected.Customer?.Name ?? "Walk-in")}";
            ModalBalanceText.Text = $"₱{selected.Balance:N2}";
            ModalAmountBox.Text = selected.Balance.ToString("F2");
        }
    }

    private void OnCancelCollection(object sender, RoutedEventArgs e)
    {
        CollectionModal.Visibility = Visibility.Collapsed;
        _selectedOrderForCollection = null;
    }

    private void OnConfirmPaymentClick(object sender, RoutedEventArgs e)
    {
        if (_selectedOrderForCollection == null)
        {
            MessageBox.Show("Please select an order to record payment for.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var methodStr = (ModalMethodCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Cash";
        var isCod = methodStr.StartsWith("COD", StringComparison.OrdinalIgnoreCase);

        if (!isCod && (!decimal.TryParse(ModalAmountBox.Text.Trim(), out var parsedAmt) || parsedAmt <= 0))
        {
            MessageBox.Show("Please enter a valid positive payment amount.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            ModalAmountBox.Focus();
            return;
        }

        var amount = isCod ? 0m : (decimal.TryParse(ModalAmountBox.Text.Trim(), out var a) ? a : 0m);
        var method = methodStr switch
        {
            "GCash"        => PaymentMethod.GCash,
            "Bank Transfer" => PaymentMethod.BankTransfer,
            "Maya"         => PaymentMethod.Maya,
            var s when s.StartsWith("COD") => PaymentMethod.COD,
            _              => PaymentMethod.Cash
        };

        var reference = ModalReferenceBox.Text.Trim();

        try
        {
            var paymentSvc = new PaymentService(_db);
            var (ok, msg, changeDue) = paymentSvc.RecordPayment(_selectedOrderForCollection.Id, amount, method, reference, null, _user.Id);
            if (!ok)
            {
                MessageBox.Show(msg, "Payment Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            CollectionModal.Visibility = Visibility.Collapsed;
            _selectedOrderForCollection = null;

            MessageBox.Show(msg, "Payment Confirmed", MessageBoxButton.OK, MessageBoxImage.Information);

            LoadData();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error recording payment: {ex.Message}", "Payment Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
