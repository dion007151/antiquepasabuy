using System.Linq;
using System.Windows;
using System.Windows.Media;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;
using Microsoft.EntityFrameworkCore;

namespace AntiquePasabuyPOS.Views.Dialogs;

public partial class OrderDetailDialog : Window
{
    private readonly AppDbContext _db;
    private Order _order;

    private readonly User? _user;

    public OrderDetailDialog(AppDbContext db, Order order, User? user = null)
    {
        InitializeComponent();
        _db = db;
        _user = user;
        var svc = new OrderService(_db);
        _order = svc.GetById(order.Id) ?? order;
        PopulateOrder();
    }

    private void PopulateOrder()
    {
        OrderNoLabel.Text = _order.OrderNo;
        DateLabel.Text = $"Created: {_order.CreatedAt:MMM dd, yyyy  hh:mm tt}";
        StatusLabel.Text = OrderStatusService.FormatStatus(_order.Status);
        StatusBadge.Background = GetStatusBrush(_order.Status);

        // Cancel/edit button visibility — hide entirely for terminal states
        var isTerminal = _order.Status is OrderStatus.COMPLETED or OrderStatus.CANCELLED;
        CancelOrderBtn.Visibility = isTerminal ? Visibility.Collapsed : Visibility.Visible;

        // Context-aware next action button — show for non-terminal states with a valid action
        var next = NextActionService.GetNextAction(_order);
        var hasValidNextStatus = next.HasAction && next.TargetStatus.HasValue;
        if (hasValidNextStatus && NextActionBtn != null && NextActionText != null)
        {
            NextActionBtn.Visibility = Visibility.Visible;
            NextActionText.Text = next.ActionLabel;
        }
        else if (NextActionBtn != null)
        {
            NextActionBtn.Visibility = Visibility.Collapsed;
        }

        CustNameLabel.Text = _order.Customer?.Name ?? "Walk-in Customer";
        CustPhoneLabel.Text = string.IsNullOrWhiteSpace(_order.Customer?.Phone)
            ? "No phone on record"
            : $"Phone: {_order.Customer.Phone}";

        DeliveryTypeLabel.Text = _order.DeliveryType.ToString();
        AddressLabel.Text = string.IsNullOrWhiteSpace(_order.Address)
            ? "Address: (None specified)"
            : $"Address: {_order.Address}";

        NotesLabel.Text = string.IsNullOrWhiteSpace(_order.Notes)
            ? "No special notes or instructions."
            : _order.Notes;

        // Ensure any items missing CostPrice (Original Cost) resolve it from matching Product catalog
        if (_order.Items != null)
        {
            var needsDbSave = false;
            foreach (var item in _order.Items)
            {
                if (item.CostPrice == null || item.CostPrice == 0)
                {
                    var pMatch = _db.Products.AsNoTracking()
                        .FirstOrDefault(p => p.Name.ToLower() == item.ProductName.Trim().ToLower());
                    if (pMatch?.CostPrice != null && pMatch.CostPrice > 0)
                    {
                        item.CostPrice = pMatch.CostPrice;
                        needsDbSave = true;
                    }
                }
                // If ActualPrice was previously assigned the CostPrice mistakenly, revert it so NowSellPrice uses EstimatedPrice
                if (item.ActualPrice.HasValue && item.CostPrice.HasValue && item.ActualPrice == item.CostPrice && item.EstimatedPrice > 0 && item.EstimatedPrice != item.CostPrice)
                {
                    item.ActualPrice = null;
                    needsDbSave = true;
                }
            }
            if (needsDbSave)
            {
                try { _db.SaveChanges(); } catch { }
            }
        }

        // Strictly bind non-empty valid order items to avoid stray/empty artifact rows
        ItemsGrid.ItemsSource = _order.Items?
            .Where(i => !string.IsNullOrWhiteSpace(i.ProductName) && i.ProductName.Trim() != "-")
            .ToList();

        SubtotalVal.Text = $"₱{_order.ItemsSubtotal:N2}";
        SvcFeeVal.Text = $"₱{_order.ServiceFee:N2}";
        GrandTotalVal.Text = $"₱{_order.GrandTotal:N2}";

        var totalPaid = _order.TotalPaid;
        var balance = _order.GrandTotal - totalPaid;
        TotalPaidVal.Text = $"₱{totalPaid:N2}";
        BalanceVal.Text = $"₱{(balance > 0 ? balance : 0m):N2}";
        // ── Payment Information ──────────────────────────────────────────
        var lastPayment = _order.Payments?.OrderByDescending(p => p.CreatedAt).FirstOrDefault();
        var paymentStatus = _order.PaymentStatus;

        var isCod = _order.IsCOD;
        PaymentStatusLabel.Text = _order.PaymentStatusDisplay;
        PaymentStatusBadge.Background = (paymentStatus == PaymentStatus.PAID)
            ? new SolidColorBrush(Color.FromRgb(220, 252, 231))
            : isCod
                ? new SolidColorBrush(Color.FromRgb(254, 243, 199))
                : (paymentStatus == PaymentStatus.PARTIAL)
                    ? new SolidColorBrush(Color.FromRgb(254, 243, 199))
                    : new SolidColorBrush(Color.FromRgb(254, 226, 226));

        PaymentStatusLabel.Foreground = (paymentStatus == PaymentStatus.PAID)
            ? new SolidColorBrush(Color.FromRgb(21, 128, 61))
            : isCod
                ? new SolidColorBrush(Color.FromRgb(180, 83, 9))
                : (paymentStatus == PaymentStatus.PARTIAL)
                    ? new SolidColorBrush(Color.FromRgb(180, 83, 9))
                    : new SolidColorBrush(Color.FromRgb(185, 28, 28));

        if (lastPayment != null)
        {
            PaymentMethodLabel.Text = lastPayment.Method switch
            {
                PaymentMethod.GCash => "GCash",
                PaymentMethod.BankTransfer => "Bank Transfer",
                PaymentMethod.Maya => "Maya",
                PaymentMethod.COD => "COD (Cash on Delivery)",
                _ => "Cash / On Hand"
            };
            AmountPaidLabel.Text = $"₱{totalPaid:N2}";
            PaymentDateLabel.Text = (lastPayment.PaymentDate ?? lastPayment.CreatedAt).ToString("MMM dd, yyyy  hh:mm tt");
            PaymentRefLabel.Text = string.IsNullOrWhiteSpace(lastPayment.Reference) ? "—" : lastPayment.Reference;

            if (!string.IsNullOrWhiteSpace(lastPayment.PaymentProofPath) && System.IO.File.Exists(lastPayment.PaymentProofPath))
            {
                PaymentProofPanel.Visibility = Visibility.Visible;
                try
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(lastPayment.PaymentProofPath);
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    PaymentProofImage.Source = bmp;
                }
                catch { PaymentProofPanel.Visibility = Visibility.Collapsed; }
            }
        }
        else
        {
            PaymentMethodLabel.Text = "—";
            AmountPaidLabel.Text = "₱0.00";
            PaymentDateLabel.Text = "—";
            PaymentRefLabel.Text = "—";
        }

        // Payment button: "MARK AS PAID" for unpaid, "EDIT PAYMENT" if already paid
        if (paymentStatus == PaymentStatus.PAID)
        {
            MarkAsPaidBtnText.Text = "EDIT PAYMENT";
            MarkAsPaidBtnIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.Pencil;
        }
        else
        {
            MarkAsPaidBtnText.Text = "MARK AS PAID";
            MarkAsPaidBtnIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.CheckCircle;
        }
        CollectPaymentBtn.Visibility = _order.Status == OrderStatus.CANCELLED
            ? Visibility.Collapsed : Visibility.Visible;

        // ── Fulfillment Status ───────────────────────────────────────────
        FulfillmentTypeLabel.Text = _order.IsPickup ? "Customer Pickup" : "Delivery";
        var fulfillmentText = GetFulfillmentStatus();
        FulfillmentStatusLabel.Text = fulfillmentText;
        var (fulfillBg, fulfillFg) = fulfillmentText switch
        {
            "PICKED UP" or "DELIVERED" => (Color.FromRgb(220, 252, 231), Color.FromRgb(21, 128, 61)),
            "OUT FOR DELIVERY" => (Color.FromRgb(219, 234, 254), Color.FromRgb(29, 78, 216)),
            _ => (Color.FromRgb(241, 245, 249), Color.FromRgb(71, 85, 105))
        };
        FulfillmentStatusBadge.Background = new SolidColorBrush(fulfillBg);
        FulfillmentStatusLabel.Foreground = new SolidColorBrush(fulfillFg);
    }

    private static Brush GetStatusBrush(OrderStatus status) => status switch
    {
        // ── 5 Canonical Statuses ─────────────────────────────────────────────
        OrderStatus.WAITING_CONFIRMATION => new SolidColorBrush(Color.FromRgb(79, 70, 229)),   // Indigo (For Buying)
        OrderStatus.READY_TO_BUY         => new SolidColorBrush(Color.FromRgb(79, 70, 229)),   // Indigo (For Buying)
        OrderStatus.BUYING_NOW           => new SolidColorBrush(Color.FromRgb(124, 58, 237)),  // Purple (Buying)
        OrderStatus.BOUGHT               => new SolidColorBrush(Color.FromRgb(5, 150, 105)),   // Emerald (Bought)
        OrderStatus.COMPLETED            => new SolidColorBrush(Color.FromRgb(22, 163, 74)),   // Green (Completed)
        OrderStatus.CANCELLED            => new SolidColorBrush(Color.FromRgb(220, 38, 38)),   // Red (Cancelled)
        _                                => new SolidColorBrush(Color.FromRgb(79, 70, 229))    // Default Indigo
    };

    private string GetFulfillmentStatus()
    {
        if (_order.IsPickup)
        {
            if (_order.Status == OrderStatus.COMPLETED) return "PICKED UP";
            if (_order.Status == OrderStatus.BOUGHT)    return "READY FOR PICKUP";
            if (_order.Status == OrderStatus.CANCELLED) return "CANCELLED";
            return "PENDING PICKUP";
        }

        var delivery = _order.Delivery;
        if (delivery != null)
        {
            return delivery.Status switch
            {
                DeliveryStatus.DELIVERED        => "DELIVERED",
                DeliveryStatus.OUT_FOR_DELIVERY => "OUT FOR DELIVERY",
                DeliveryStatus.FAILED           => "DELIVERY FAILED",
                _                               => "PENDING DELIVERY"
            };
        }

        if (_order.Status == OrderStatus.COMPLETED) return "DELIVERED";
        if (_order.Status == OrderStatus.BOUGHT)    return "READY FOR DELIVERY";
        return "PENDING DELIVERY";
    }

    private void OnReceiptPreview(object sender, RoutedEventArgs e)
    {
        ReloadOrder();
        var savedPrinter = SeedData.GetSetting(_db, "default_printer", "");
        var bizName = SeedData.GetSetting(_db, "business_name", "ANTIQUE PASABUY POS");
        var footer = SeedData.GetSetting(_db, "receipt_footer", "Thank you for using Antique Pasabuy POS!");
        var dlg = new PrintPreviewDialog(_order, bizName, footer, savedPrinter)
        {
            Owner = this
        };
        dlg.ShowDialog();
    }

    private void OnQuickPrint(object sender, RoutedEventArgs e)
    {
        ReloadOrder();
        var savedPrinter = SeedData.GetSetting(_db, "default_printer", "");
        var bizName = SeedData.GetSetting(_db, "business_name", "ANTIQUE PASABUY POS");
        var footer = SeedData.GetSetting(_db, "receipt_footer", "Thank you for using Antique Pasabuy POS!");
        var (ok, msg) = PrinterService.PrintReceipt(_order, bizName, footer, savedPrinter, showDialog: string.IsNullOrEmpty(savedPrinter));
        StatusMessage.Text = msg;
        StatusMessage.Foreground = ok ? new SolidColorBrush(Color.FromRgb(13, 148, 136)) : new SolidColorBrush(Color.FromRgb(220, 38, 38));
    }

    private void OnNextActionClick(object sender, RoutedEventArgs e)
    {
        var svc = new OrderService(_db);
        var (ok, msg) = svc.ExecuteNextAction(_order.Id, _user?.Id);
        if (!ok)
        {
            MessageBox.Show(msg, "Action Notice", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            StatusMessage.Text = msg;
            StatusMessage.Foreground = new SolidColorBrush(Color.FromRgb(13, 148, 136));
            ReloadOrder();
        }
    }

    private void OnCollectPayment(object sender, RoutedEventArgs e)
    {
        var dlg = new CollectPaymentDialog(_db, _order, _user)
        {
            Owner = this
        };
        if (dlg.ShowDialog() == true)
        {
            ReloadOrder();
            StatusMessage.Text = "Payment recorded successfully.";
            StatusMessage.Foreground = new SolidColorBrush(Color.FromRgb(13, 148, 136));
        }
    }

    private void OnEditOrder(object sender, RoutedEventArgs e)
    {
        var dlg = new EditOrderDialog(_db, _order);
        dlg.Owner = this;
        if (dlg.ShowDialog() == true)
        {
            ReloadOrder();
            StatusMessage.Text = "Order updated successfully.";
            StatusMessage.Foreground = new SolidColorBrush(Color.FromRgb(13, 148, 136));
        }
    }

    private void OnCancelOrder(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show(
            $"Are you sure you want to cancel Order {_order.OrderNo}?",
            "Cancel Order Confirmation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;

        var svc = new OrderService(_db);
        var (ok, msg) = svc.Cancel(_order.Id, "Cancelled by user request", _user?.Id);
        StatusMessage.Text = msg;
        StatusMessage.Foreground = ok ? new SolidColorBrush(Color.FromRgb(220, 38, 38)) : new SolidColorBrush(Color.FromRgb(220, 38, 38));
        if (ok)
        {
            ReloadOrder();
            MessageBox.Show($"Order {_order.OrderNo} has been marked as Cancelled.", "Order Cancelled", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }


    private void ReloadOrder()
    {
        // Detach all tracked entries so GetById returns a fresh copy from DB
        foreach (var entry in _db.ChangeTracker.Entries().ToList())
            entry.State = EntityState.Detached;

        var svc = new OrderService(_db);
        var refreshed = svc.GetById(_order.Id);
        if (refreshed != null)
        {
            _order = refreshed;
            PopulateOrder();
        }
    }

    private void OnAutoRecalcFee(object sender, RoutedEventArgs e)
    {
        var autoFee = BusinessProfileService.IsPasabuyFeeEnabled(_db)
            ? PasabuyFeeService.CalculateServiceFee(_order.ItemsSubtotal, _db)
            : 0m;
        var svc = new OrderService(_db);
        var (ok, msg) = svc.Update(_order.Id, _order.CustomerId, _order.DeliveryType, _order.Address, _order.Notes, autoFee, 0m, 0m);
        if (ok)
        {
            ReloadOrder();
            StatusMessage.Text = $"Pasabuy fee auto-calculated: ₱{autoFee:N2}";
            StatusMessage.Foreground = new SolidColorBrush(Color.FromRgb(13, 148, 136));
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
