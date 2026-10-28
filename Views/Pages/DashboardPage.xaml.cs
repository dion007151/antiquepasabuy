using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;
using AntiquePasabuyPOS.Helpers;

namespace AntiquePasabuyPOS.Views.Pages;

public partial class DashboardPage : Page
{
    private readonly AppDbContext _db;
    private readonly User _user;
    private readonly Action<string>? _navigate;

    private bool _hasLoadedOnce = false;
    private bool _isLoadingDashboard = false;

    public DashboardPage(AppDbContext db, User user, Action<string>? navigate = null, Action? onSuccess = null)
    {
        InitializeComponent();
        _db = db;
        _user = user;
        _navigate = navigate;
        Loaded += (_, _) =>
        {
            if (_hasLoadedOnce)
            {
                // Instant navigation: cached dashboard is already visible with 0ms delay!
                // Silently refresh stats in background at idle priority
                Dispatcher.InvokeAsync(LoadData, System.Windows.Threading.DispatcherPriority.Background);
            }
            else
            {
                _hasLoadedOnce = true;
                LoadData();
            }
            OrderEventBus.OrderCreated += OnExternalOrderCreated;
            OrderEventBus.OrderStatusChanged += OnExternalStatusChanged;
            OrderEventBus.ItemStatusChanged += OnExternalItemStatusChanged;
            OrderEventBus.PaymentRecorded += OnExternalPayment;
            OrderEventBus.DataRefreshed += OnExternalRefresh;
        };
        Unloaded += (_, _) =>
        {
            OrderEventBus.OrderCreated -= OnExternalOrderCreated;
            OrderEventBus.OrderStatusChanged -= OnExternalStatusChanged;
            OrderEventBus.ItemStatusChanged -= OnExternalItemStatusChanged;
            OrderEventBus.PaymentRecorded -= OnExternalPayment;
            OrderEventBus.DataRefreshed -= OnExternalRefresh;
        };
        GreetingLabel.Text = $"{AppHelper.GetGreeting()}, {user.FullName.Split(' ')[0]}";
    }

    private void OnExternalOrderCreated(int id) => Dispatcher.InvokeAsync(LoadData);
    private void OnExternalStatusChanged(int id, OrderStatus s) => Dispatcher.InvokeAsync(LoadData);
    private void OnExternalItemStatusChanged(int id, ItemStatus s) => Dispatcher.InvokeAsync(LoadData);
    private void OnExternalPayment(int id, decimal amt) => Dispatcher.InvokeAsync(LoadData);
    private void OnExternalRefresh() => Dispatcher.InvokeAsync(LoadData);

    private async void LoadData()
    {
        if (_isLoadingDashboard) return;
        _isLoadingDashboard = true;

        try
        {
            PasabuyDashboardStats? stats = null;
            List<Order> recent = [];

            // Execute database statistics and recent orders on background thread to ensure 60fps UI smoothness
            await Task.Run(() =>
            {
                using var bgDb = new AppDbContext();
                var svc = new OrderService(bgDb);
                stats = svc.GetPasabuyStats();
                recent = svc.GetAll(limit: 20, businessId: "ANTIQUE_PASABUY")
                    .Where(o => o.Status != OrderStatus.CANCELLED && o.Status != OrderStatus.Cancelled)
                    .Take(10)
                    .ToList();
            });

            if (stats == null) return;

            // 1. Next Buying Day, Orders & Total Items To Buy
            NextBuyingDayLabel.Text = string.IsNullOrWhiteSpace(stats.NextBuyingDay) ? "Scheduled" : stats.NextBuyingDay;
            if (stats.ForBuyingOrdersCount > 0)
                OrdersToBuyBadge.Text = $"{stats.ForBuyingOrdersCount} {(stats.ForBuyingOrdersCount == 1 ? "To Buy" : "To Buy")}";
            else if (stats.TotalOrdersCount > 0)
                OrdersToBuyBadge.Text = $"{stats.TotalOrdersCount} {(stats.TotalOrdersCount == 1 ? "Order" : "Orders")}";
            else
                OrdersToBuyBadge.Text = "0 Orders";

            ItemsToBuyBadge.Text = $"{stats.TotalItemsToBuy} {(stats.TotalItemsToBuy == 1 ? "Item To Buy" : "Items To Buy")}";
            if (UnpaidBalanceBadge != null) UnpaidBalanceBadge.Text = $"₱{stats.TotalUnpaidBalance:N2}";

            // 2. Banner CTA: When items to buy > 0, primary CTA = Open Shopping List
            if (BannerCtaBtn != null && BannerCtaText != null && BannerCtaIcon != null)
            {
                if (stats.TotalItemsToBuy > 0)
                {
                    BannerCtaText.Text = "OPEN SHOPPING LIST";
                    BannerCtaIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.ShoppingOutline;
                    BannerCtaBtn.ToolTip = "Open Shopping workspace to print checklist and shop store-by-store";
                }
                else
                {
                    BannerCtaText.Text = "VIEW PURCHASE ORDERS";
                    BannerCtaIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.ClipboardListOutline;
                    BannerCtaBtn.ToolTip = "Open purchase orders list";
                }
            }

            // 3. Operational Summary - Standardized 4 Cards
            if (ActiveOrdersLabel != null)
            {
                if (stats.ActiveOrdersCount > 0)
                {
                    ActiveOrdersLabel.Text = stats.ActiveOrdersCount.ToString();
                    if (ActiveOrdersSubLabel != null) ActiveOrdersSubLabel.Text = "In progress / fulfillment";
                }
                else if (stats.TotalOrdersCount > 0)
                {
                    ActiveOrdersLabel.Text = stats.TotalOrdersCount.ToString();
                    if (ActiveOrdersSubLabel != null)
                        ActiveOrdersSubLabel.Text = stats.CompletedOrdersCount > 0
                            ? $"{stats.CompletedOrdersCount} completed / fulfilled"
                            : $"{stats.TotalOrdersCount} total orders";
                }
                else
                {
                    ActiveOrdersLabel.Text = "0";
                    if (ActiveOrdersSubLabel != null) ActiveOrdersSubLabel.Text = "No orders yet";
                }
            }
            if (OutstandingBalanceLabel != null) OutstandingBalanceLabel.Text = $"₱{stats.TotalUnpaidBalance:N2}";
            if (UnpaidOrdersCountSubLabel != null) UnpaidOrdersCountSubLabel.Text = $"{stats.UnpaidOrdersCount} unpaid / partial orders";
            if (ForBuyingOrdersLabel != null) ForBuyingOrdersLabel.Text = stats.ForBuyingOrdersCount.ToString();
            if (ItemsToBuySubLabel != null) ItemsToBuySubLabel.Text = $"{stats.TotalItemsToBuy} items queued";
            if (BoughtNotFinishedLabel != null) BoughtNotFinishedLabel.Text = stats.BoughtNotFinishedCount.ToString();

            // 4. Shopping Alerts Content
            if (ShoppingAlertText != null && ShoppingAlertSubtext != null)
            {
                if (stats.TotalItemsToBuy > 0)
                {
                    ShoppingAlertText.Text = $"{stats.TotalItemsToBuy} items across {stats.ForBuyingOrdersCount} orders waiting to be bought.";
                    ShoppingAlertSubtext.Text = $"Scheduled for {NextBuyingDayLabel.Text}. Click 'Shopping Page' to begin.";
                }
                else if (stats.BoughtNotFinishedCount > 0)
                {
                    ShoppingAlertText.Text = $"{stats.BoughtNotFinishedCount} orders bought and awaiting fulfillment/dispatch.";
                    ShoppingAlertSubtext.Text = "Review fulfillment in Orders Masterlist.";
                }
                else if (stats.TotalUnpaidBalance > 0)
                {
                    ShoppingAlertText.Text = $"Outstanding customer receivables: ₱{stats.TotalUnpaidBalance:N2}.";
                    ShoppingAlertSubtext.Text = "Follow up with customers on unpaid purchase orders.";
                }
                else
                {
                    ShoppingAlertText.Text = "All shopping tasks and customer balances are up to date.";
                    ShoppingAlertSubtext.Text = "No urgent alerts requiring immediate action.";
                }
            }

            // 5. Recent Orders Table & Empty State
            RecentOrdersGrid.ItemsSource = recent;
            if (recent.Count == 0)
            {
                RecentOrdersEmptyState.Visibility = Visibility.Visible;
                RecentOrdersGrid.Visibility = Visibility.Collapsed;
            }
            else
            {
                RecentOrdersEmptyState.Visibility = Visibility.Collapsed;
                RecentOrdersGrid.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DashboardPage] LoadData error: {ex.Message}");
        }
        finally
        {
            _isLoadingDashboard = false;
        }
    }

    // Primary Action Handlers
    private void OnNewOrderClick(object sender, RoutedEventArgs e) => _navigate?.Invoke("NewOrder");
    private void OnBannerCtaClick(object sender, RoutedEventArgs e)
    {
        var svc = new OrderService(_db);
        var stats = svc.GetPasabuyStats();
        if (stats.TotalItemsToBuy > 0)
        {
            _navigate?.Invoke("Buying");
        }
        else
        {
            _navigate?.Invoke("Masterlist");
        }
    }
    private void OnOpenBuyingList(object sender, RoutedEventArgs e) => _navigate?.Invoke("Buying");
    private void OnViewAllOrders(object sender, RoutedEventArgs e) => _navigate?.Invoke("Masterlist");
    private void OnCreateOrderEmptyClick(object sender, RoutedEventArgs e) => _navigate?.Invoke("NewOrder");
}
