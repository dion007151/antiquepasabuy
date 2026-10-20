using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;
using AntiquePasabuyPOS.Views.Dialogs;
using Microsoft.EntityFrameworkCore;

namespace AntiquePasabuyPOS.Views.Pages;

public partial class DeliveryPage : Page
{
    private readonly AppDbContext _db;
    private readonly User _user;
    private readonly Action<string>? _navigate;
    private string _activeTab = "for_delivery";
    private List<DeliveryRowVm> _allRows = [];

    public DeliveryPage(AppDbContext db, User user, Action<string>? navigate = null, Action? onSuccess = null, string? initialTab = null)
    {
        InitializeComponent();
        _db = db;
        _user = user;
        _navigate = navigate;

        if (!string.IsNullOrWhiteSpace(initialTab))
        {
            _activeTab = initialTab.ToLower() switch
            {
                "pickup" or "ready_pickup" or "ready_for_pickup" => "pickup",
                "picked_up"                                      => "picked_up",
                "completed"                                      => "completed",
                "out" or "out_for_delivery"                      => "out_for_delivery",
                "delivered"                                      => "delivered",
                _                                                => "for_delivery"
            };
        }

        Loaded += (_, _) =>
        {
            HighlightActiveTab();
            OrderEventBus.OrderStatusChanged += OnExternalStatusChanged;
            OrderEventBus.DataRefreshed += OnExternalRefresh;
            LoadData();
        };

        Unloaded += (_, _) =>
        {
            OrderEventBus.OrderStatusChanged -= OnExternalStatusChanged;
            OrderEventBus.DataRefreshed -= OnExternalRefresh;
        };
    }

    private void OnExternalStatusChanged(int orderId, OrderStatus newStatus) => Dispatcher.InvokeAsync(LoadData);
    private void OnExternalRefresh() => Dispatcher.InvokeAsync(LoadData);

    private void HighlightActiveTab()
    {
        var tabs = new[] { TabForDelivery, TabOutForDelivery, TabDelivered, TabPickup, TabPickedUp, TabCompleted };
        var target = _activeTab switch
        {
            "out_for_delivery" => TabOutForDelivery,
            "delivered"        => TabDelivered,
            "pickup"           => TabPickup,
            "picked_up"        => TabPickedUp,
            "completed"        => TabCompleted,
            _                  => TabForDelivery
        };

        foreach (var t in tabs)
        {
            if (t == null) continue;
            NavProps.SetIsActive(t, t == target);
            t.Foreground = t == target ? Brushes.White : new SolidColorBrush(Color.FromRgb(71, 85, 105));
        }
    }

    private void OnTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        _activeTab = btn.Tag?.ToString() ?? "for_delivery";
        HighlightActiveTab();
        ApplyFilter();
    }

    private void LoadData()
    {
        // 1. Ensure any Delivery-type orders have a Delivery record created if missing
        var missingDeliveries = _db.Orders
            .Where(o => o.Status != OrderStatus.CANCELLED && o.DeliveryType == DeliveryType.DELIVERY && o.Delivery == null)
            .ToList();

        if (missingDeliveries.Count > 0)
        {
            foreach (var ord in missingDeliveries)
            {
                var del = new Delivery
                {
                    OrderId = ord.Id,
                    Status = ord.Status == OrderStatus.DELIVERED || ord.Status == OrderStatus.COMPLETED
                        ? DeliveryStatus.DELIVERED
                        : (ord.Status == OrderStatus.OUT_FOR_DELIVERY ? DeliveryStatus.OUT_FOR_DELIVERY : DeliveryStatus.PENDING),
                    CreatedAt = ord.CreatedAt
                };
                _db.Deliveries.Add(del);
            }
            _db.SaveChanges();
        }

        // 2. Fetch all orders with Customer and Delivery (AsNoTracking to prevent any tracking locks)
        var orders = _db.Orders
            .AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.Delivery)
            .Where(o => o.Status != OrderStatus.CANCELLED)
            .OrderByDescending(o => o.CreatedAt)
            .ToList();

        _allRows = orders.Select(o => new DeliveryRowVm(o)).ToList();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var q = SearchBox?.Text?.Trim().ToLower() ?? "";
        if (ClearSearchBtn != null)
            ClearSearchBtn.Visibility = string.IsNullOrEmpty(q) ? Visibility.Collapsed : Visibility.Visible;

        var filtered = _allRows.AsEnumerable();

        // Strict tab isolation:
        filtered = _activeTab switch
        {
            "for_delivery"     => filtered.Where(r => !r.IsPickup && (r.Order.Status == OrderStatus.FOR_DELIVERY || r.Order.Status == OrderStatus.READY || r.Order.Status == OrderStatus.PENDING || r.Order.Status == OrderStatus.FOR_BUYING || r.Order.Status == OrderStatus.BUYING || r.Order.Status == OrderStatus.BOUGHT)),
            "out_for_delivery" => filtered.Where(r => !r.IsPickup && (r.Order.Status == OrderStatus.OUT_FOR_DELIVERY || r.Delivery?.Status == DeliveryStatus.OUT_FOR_DELIVERY)),
            "delivered"        => filtered.Where(r => !r.IsPickup && (r.Order.Status == OrderStatus.DELIVERED || r.Delivery?.Status == DeliveryStatus.DELIVERED)),
            "pickup"           => filtered.Where(r => r.IsPickup && (r.Order.Status == OrderStatus.READY_FOR_PICKUP || r.Order.Status == OrderStatus.READY || r.Order.Status == OrderStatus.PENDING || r.Order.Status == OrderStatus.FOR_BUYING || r.Order.Status == OrderStatus.BUYING || r.Order.Status == OrderStatus.BOUGHT)),
            "picked_up"        => filtered.Where(r => r.IsPickup && (r.Order.Status == OrderStatus.PICKED_UP || r.Order.Status == OrderStatus.DELIVERED)),
            "completed"        => filtered.Where(r => r.Order.Status == OrderStatus.COMPLETED),
            _                  => filtered
        };

        if (!string.IsNullOrEmpty(q))
        {
            filtered = filtered.Where(r =>
                (r.Order.OrderNo?.ToLower().Contains(q) ?? false) ||
                (r.Order.Customer?.Name?.ToLower().Contains(q) ?? false) ||
                (r.DestinationAddress.ToLower().Contains(q)) ||
                (r.RiderDisplay.ToLower().Contains(q)) ||
                r.StatusText.ToLower().Contains(q));
        }

        var list = filtered.ToList();
        MainGrid.ItemsSource = list;
        if (EmptyStatePanel != null)
            EmptyStatePanel.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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

    // ══ ASSIGN DRIVER MODAL & HANDLERS ═══════════════════════════════════════
    private bool PromptAssignDriver(DeliveryRowVm row)
    {
        if (row.Order == null) return false;
        var del = _db.Deliveries.FirstOrDefault(d => d.OrderId == row.Order.Id);
        if (del == null)
        {
            del = new Delivery { OrderId = row.Order.Id, CreatedAt = DateTime.Now, Status = DeliveryStatus.PENDING };
            _db.Deliveries.Add(del);
            _db.SaveChanges();
        }

        var existingDrivers = _db.Deliveries
            .Where(d => !string.IsNullOrEmpty(d.RiderName))
            .Select(d => d.RiderName!)
            .Distinct()
            .ToList();

        var dlg = new AssignDriverDialog(
            orderNo: row.Order.OrderNo,
            customerName: row.Order.Customer?.Name,
            currentDriver: del.RiderName,
            currentPhone: del.RiderPhone,
            currentNotes: del.Notes,
            existingDrivers: existingDrivers)
        {
            Owner = Window.GetWindow(this)
        };

        if (dlg.ShowDialog() == true)
        {
            del.RiderName = dlg.DriverName;
            del.RiderPhone = dlg.DriverPhone;
            if (!string.IsNullOrWhiteSpace(dlg.Notes))
            {
                del.Notes = dlg.Notes;
            }

            _db.AuditLogs.Add(new AuditLog
            {
                UserId = _user.Id,
                Action = "assign_driver",
                Details = $"Order {row.Order.OrderNo} assigned to rider {del.RiderName}"
            });

            _db.SaveChanges();
            LoadData();
            return true;
        }

        return false;
    }

    private void OnAssignDriverClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not DeliveryRowVm row) return;
        PromptAssignDriver(row);
    }

    // ══ ACTION 1: [ OUT FOR DELIVERY ] ══════════════════════════════════════
    private void OnOutForDeliveryClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not DeliveryRowVm row || row.Order == null) return;

        var del = _db.Deliveries.FirstOrDefault(d => d.OrderId == row.Order.Id);
        if (del == null)
        {
            del = new Delivery { OrderId = row.Order.Id, CreatedAt = DateTime.Now, Status = DeliveryStatus.PENDING };
            _db.Deliveries.Add(del);
            _db.SaveChanges();
        }

        // If no rider assigned yet, prompt user to assign before dispatching
        if (string.IsNullOrWhiteSpace(del.RiderName))
        {
            var assigned = PromptAssignDriver(row);
            if (!assigned) return; // User cancelled
            del = _db.Deliveries.FirstOrDefault(d => d.OrderId == row.Order.Id);
            if (del == null || string.IsNullOrWhiteSpace(del.RiderName)) return;
        }

        var svc = new FulfillmentService(_db);
        var (ok, msg) = svc.AssignRiderAndDispatch(row.Order.Id, del.RiderName, del.RiderPhone, _user.Id);
        if (ok)
        {
            LoadData();
            MessageBox.Show($"Order {row.Order.OrderNo} is now Out for Delivery with {del.RiderName}!",
                "Rider Dispatched", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(msg, "Dispatch Notice", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ══ ACTION 2: [ MARK DELIVERED ] ════════════════════════════════════════
    private void OnMarkDeliveredClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not DeliveryRowVm row || row.Order == null) return;

        var order = row.Order;
        var hasBalance = order.PaymentStatus != PaymentStatus.PAID && order.Balance > 0m;
        bool collectedCodCash = false;

        if (hasBalance)
        {
            var res = MessageBox.Show(
                $"Order {order.OrderNo} has a COD balance of ₱{order.Balance:N2} to collect.\n\n" +
                $"Did the rider successfully deliver the package and COLLECT ₱{order.Balance:N2} CASH from {order.Customer?.Name ?? "the customer"}?\n\n" +
                "• Click YES to confirm delivery AND record COD cash payment.\n" +
                "• Click NO to mark delivered without recording payment.\n" +
                "• Click CANCEL to abort.",
                "Confirm COD Delivery & Collection",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (res == MessageBoxResult.Cancel) return;

            if (res == MessageBoxResult.Yes)
            {
                collectedCodCash = true;
                var paySvc = new PaymentService(_db);
                var riderName = row.Delivery?.RiderName ?? "Rider";
                var (payOk, payMsg, _) = paySvc.RecordPayment(order.Id, order.Balance, PaymentMethod.Cash, $"COD-DELIVERY-{order.OrderNo}", $"COD collected by {riderName} upon delivery", _user.Id);
                if (!payOk)
                {
                    MessageBox.Show($"Could not record COD payment: {payMsg}", "Payment Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
        else
        {
            var res = MessageBox.Show(
                $"Confirm Order {order.OrderNo} has been delivered to {order.Customer?.Name ?? "customer"}?",
                "Confirm Delivery", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (res != MessageBoxResult.Yes) return;
        }

        var svc = new FulfillmentService(_db);
        var (ok, msg) = svc.MarkDelivered(order.Id, _user.Id);
        if (ok)
        {
            LoadData();
            var finalMsg = collectedCodCash
                ? $"Order {order.OrderNo} marked DELIVERED and COD cash payment recorded!"
                : msg;
            MessageBox.Show(finalMsg, "Delivered", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(msg, "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ══ ACTION 3: [ MARK PICKED UP ] ════════════════════════════════════════
    private void OnMarkPickedUpClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not DeliveryRowVm row || row.Order == null) return;

        var svc = new FulfillmentService(_db);
        var (ok, msg) = svc.MarkPickedUp(row.Order.Id, _user.Id);
        if (ok)
        {
            LoadData();
            MessageBox.Show(msg, "Picked Up", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(msg, "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ══ ACTION 4: [ COMPLETE ORDER ] ════════════════════════════════════════
    private void OnCompleteOrderClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not DeliveryRowVm row || row.Order == null) return;

        var svc = new OrderStatusService(_db);
        var (ok, msg) = svc.Transition(row.Order.Id, OrderStatus.COMPLETED, _user.Id);
        if (ok)
        {
            LoadData();
            MessageBox.Show($"Order {row.Order.OrderNo} is now Completed!", "Order Completed", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(msg, "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

public class DeliveryRowVm
{
    public Order Order { get; }
    public Delivery? Delivery => Order.Delivery;

    public bool IsPickup => Order.DeliveryType == DeliveryType.PICKUP;
    public string DestinationAddress => IsPickup ? "Store Counter (Customer Pickup)" : (Order.Address ?? "No address provided");
    public string RiderDisplay => string.IsNullOrWhiteSpace(Delivery?.RiderName) ? (IsPickup ? "Counter Pickup" : "Unassigned") : Delivery.RiderName;
    public bool CanAssignDriver => !IsPickup && !IsFinished;
    public string AssignButtonLabel => string.IsNullOrWhiteSpace(Delivery?.RiderName) ? "Assign" : "Change";
    public FontWeight RiderFontWeight => string.IsNullOrWhiteSpace(Delivery?.RiderName) ? FontWeights.Normal : FontWeights.SemiBold;
    public Brush RiderForegroundBrush => string.IsNullOrWhiteSpace(Delivery?.RiderName)
        ? new SolidColorBrush(Color.FromRgb(148, 163, 184))
        : new SolidColorBrush(Color.FromRgb(16, 42, 67));

    // Single Obvious Next Action Rules
    public bool ShowOutForDeliveryBtn => !IsPickup && (Order.Status == OrderStatus.FOR_DELIVERY || Order.Status == OrderStatus.READY || Order.Status == OrderStatus.PENDING);
    public bool ShowMarkDeliveredBtn  => !IsPickup && (Order.Status == OrderStatus.OUT_FOR_DELIVERY || Order.Status == OrderStatus.FOR_DELIVERY);
    public bool ShowMarkPickedUpBtn   => IsPickup && (Order.Status == OrderStatus.READY_FOR_PICKUP || Order.Status == OrderStatus.READY || Order.Status == OrderStatus.PENDING);
    public bool ShowCompleteOrderBtn  => Order.Status == OrderStatus.DELIVERED || Order.Status == OrderStatus.PICKED_UP;
    public bool IsFinished            => Order.Status == OrderStatus.COMPLETED;

    // Payment & COD tracking for Rider delivery
    public bool IsPaid => Order.PaymentStatus == PaymentStatus.PAID;
    public bool IsCod => Order.IsCOD || (!IsPaid && !IsPickup);
    public bool HasCodBalance => !IsPaid && Order.Balance > 0m;
    public string PaymentBadgeText => IsPaid ? "PAID" : (IsCod ? "COD" : "UNPAID");
    public Brush PaymentBadgeBgBrush => IsPaid
        ? new SolidColorBrush(Color.FromRgb(220, 252, 231))
        : (IsCod
            ? new SolidColorBrush(Color.FromRgb(254, 243, 199))
            : new SolidColorBrush(Color.FromRgb(254, 226, 226)));
    public Brush PaymentBadgeFgBrush => IsPaid
        ? new SolidColorBrush(Color.FromRgb(21, 128, 61))
        : (IsCod
            ? new SolidColorBrush(Color.FromRgb(180, 83, 9))
            : new SolidColorBrush(Color.FromRgb(185, 28, 28)));
    public string CodAmountText => $"Collect: ₱{Order.Balance:N2}";
    public Brush CodAmountFgBrush => new SolidColorBrush(Color.FromRgb(180, 83, 9));

    public string StatusText
    {
        get
        {
            // Use string comparison to avoid CS8510 unreachable pattern errors
            // caused by enum aliases sharing the same underlying int values.
            var s = Order.Status.ToString().ToUpperInvariant();
            if (IsPickup)
            {
                return s switch
                {
                    "COMPLETED"                    => "COMPLETED",
                    "BUYING_NOW" or "BUYING"        => "PURCHASING",
                    "READY_TO_BUY" or "FOR_BUYING" => "READY TO BUY",
                    "CANCELLED"                    => "CANCELLED",
                    _                              => "PENDING"
                };
            }
            return s switch
            {
                "COMPLETED"                    => "COMPLETED",
                "BUYING_NOW" or "BUYING"        => "PURCHASING",
                "READY_TO_BUY" or "FOR_BUYING" => "READY TO BUY",
                "CANCELLED"                    => "CANCELLED",
                _                              => "PENDING"
            };
        }
    }

    public Brush StatusBgBrush => StatusText switch
    {
        "DELIVERED" or "COMPLETED" => new SolidColorBrush(Color.FromRgb(209, 250, 229)),
        "OUT FOR DELIVERY" => new SolidColorBrush(Color.FromRgb(224, 231, 255)),
        "READY FOR PICKUP" or "READY" => new SolidColorBrush(Color.FromRgb(254, 243, 199)),
        "PENDING" => new SolidColorBrush(Color.FromRgb(254, 243, 199)),
        "FOR BUYING" or "BUYING" => new SolidColorBrush(Color.FromRgb(237, 233, 254)),
        _ => new SolidColorBrush(Color.FromRgb(241, 245, 249))
    };

    public Brush StatusFgBrush => StatusText switch
    {
        "DELIVERED" or "COMPLETED" => new SolidColorBrush(Color.FromRgb(6, 95, 70)),
        "OUT FOR DELIVERY" => new SolidColorBrush(Color.FromRgb(67, 56, 202)),
        "READY FOR PICKUP" or "READY" => new SolidColorBrush(Color.FromRgb(180, 83, 9)),
        "PENDING" => new SolidColorBrush(Color.FromRgb(180, 83, 9)),
        "FOR BUYING" or "BUYING" => new SolidColorBrush(Color.FromRgb(109, 40, 217)),
        _ => new SolidColorBrush(Color.FromRgb(71, 85, 105))
    };

    public DeliveryRowVm(Order order)
    {
        Order = order;
    }
}
