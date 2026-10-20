using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;
using MaterialDesignThemes.Wpf;

namespace AntiquePasabuyPOS.Views.Dialogs;

public partial class CollectPaymentDialog : Window
{
    private readonly AppDbContext _db;
    private readonly Order _order;
    private readonly User? _user;
    private readonly decimal _balanceDue;
    private string? _proofFilePath;
    private readonly Payment? _existingPayment;

    public CollectPaymentDialog(AppDbContext db, Order order, User? user = null)
    {
        InitializeComponent();
        _db = db;
        _order = order;
        _user = user;

        _existingPayment = _order.Payments?.OrderByDescending(p => p.CreatedAt).FirstOrDefault();
        _balanceDue = Math.Max(0m, _order.Balance);

        // Header info
        OrderNoText.Text = $"PO #{_order.OrderNo}";
        CustomerText.Text = $"Customer: {(_order.Customer?.Name ?? "Walk-in")}";
        OrderStatusBadge.Text = _order.Status.ToString().ToUpper();

        // Balance info
        GrandTotalText.Text = $"Total: ₱{_order.GrandTotal:N2}";
        TotalPaidText.Text = $"Paid: ₱{_order.TotalPaid:N2}";

        // If order is already paid, mode is "Edit Payment"
        if (_order.PaymentStatus == PaymentStatus.PAID && _existingPayment != null)
        {
            Title = "Edit Payment";
            DialogHeaderTitle.Text = "Edit Payment Recording";
            BalanceDueTitleText.Text = "RECORDED PAYMENT";
            BalanceDueText.Text = $"₱{_existingPayment.Amount:N2}";
            ConfirmBtnText.Text = "SAVE CHANGES";

            AmountBox.Text = _existingPayment.Amount.ToString("F2");
            PaymentDatePicker.SelectedDate = _existingPayment.PaymentDate ?? _existingPayment.CreatedAt;
            ReferenceBox.Text = _existingPayment.Reference ?? "";

            // Select method in MethodCombo
            var targetContent = _existingPayment.Method switch
            {
                PaymentMethod.GCash => "GCash",
                PaymentMethod.BankTransfer => "Bank Transfer",
                PaymentMethod.Maya => "Maya",
                _ => "Cash / On Hand"
            };
            foreach (ComboBoxItem item in MethodCombo.Items)
            {
                if (item.Content?.ToString() == targetContent)
                {
                    MethodCombo.SelectedItem = item;
                    break;
                }
            }

            if (!string.IsNullOrWhiteSpace(_existingPayment.PaymentProofPath) && System.IO.File.Exists(_existingPayment.PaymentProofPath))
            {
                _proofFilePath = _existingPayment.PaymentProofPath;
                ProofFileLabel.Text = System.IO.Path.GetFileName(_existingPayment.PaymentProofPath);
                ClearProofBtn.Visibility = Visibility.Visible;
            }
        }
        else
        {
            BalanceDueText.Text = $"₱{_balanceDue:N2}";
            AmountBox.Text = _balanceDue.ToString("F2");
            PaymentDatePicker.SelectedDate = DateTime.Today;
        }

        Loaded += (_, _) =>
        {
            AmountBox.Focus();
            AmountBox.SelectAll();
            UpdateSettlementFeedback();
        };
    }

    private void OnPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;

        if (tag == "EXACT")
        {
            AmountBox.Text = (_order.PaymentStatus == PaymentStatus.PAID && _existingPayment != null)
                ? _existingPayment.Amount.ToString("F2")
                : _balanceDue.ToString("F2");
        }
        else if (decimal.TryParse(tag, out var val))
        {
            AmountBox.Text = val.ToString("F2");
        }

        AmountBox.Focus();
        AmountBox.SelectAll();
    }

    private void OnAmountChanged(object sender, TextChangedEventArgs e) => UpdateSettlementFeedback();

    private void OnMethodSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;

        var isCod = IsCodSelected();

        // Disable / clear amount when COD is chosen
        AmountBox.IsEnabled = !isCod;
        if (isCod)
        {
            AmountBox.Text = "";
        }
        else if (string.IsNullOrWhiteSpace(AmountBox.Text))
        {
            // Re-fill the default when switching back from COD
            AmountBox.Text = _balanceDue.ToString("F2");
        }

        // Show/hide quick presets row (Row 2)
        var presetsRow = (RowDefinition)((Grid)AmountBox.Parent.GetType()
            .GetProperty("Parent")
            ?.GetValue(AmountBox.Parent)! ?? new Grid()).RowDefinitions[2];
        // simpler: just collapse the WrapPanel
        BtnExactDue.Visibility = isCod ? Visibility.Collapsed : Visibility.Visible;

        UpdateSettlementFeedback();
    }

    private string GetSelectedMethodName()
    {
        if (MethodCombo.SelectedItem is ComboBoxItem item && item.Content is string s)
            return s;
        return "Cash";
    }

    private bool IsCodSelected()
        => GetSelectedMethodName().StartsWith("COD", StringComparison.OrdinalIgnoreCase);

    private void UpdateSettlementFeedback()
    {
        if (SettlementCard == null || SettlementDetailText == null || SettlementStatusLabel == null) return;

        var method = GetSelectedMethodName();
        var isCash = method.StartsWith("Cash");
        var isCod  = IsCodSelected();

        // ── COD: no amount to record now ─────────────────────────────────────
        if (isCod)
        {
            SettlementCard.Background   = new SolidColorBrush(Color.FromRgb(254, 243, 199));
            SettlementCard.BorderBrush  = new SolidColorBrush(Color.FromRgb(252, 211, 77));
            SettlementIcon.Kind         = PackIconKind.TruckDeliveryOutline;
            SettlementIcon.Foreground   = new SolidColorBrush(Color.FromRgb(180, 83, 9));
            SettlementStatusLabel.Text  = "COD — CASH ON DELIVERY";
            SettlementDetailText.Text   = "Payment will be collected by the rider upon delivery.";
            SettlementDetailText.Foreground = new SolidColorBrush(Color.FromRgb(180, 83, 9));
            return;
        }

        if (!decimal.TryParse(AmountBox.Text, out var tendered) || tendered <= 0)
        {
            SettlementCard.Background = new SolidColorBrush(Color.FromRgb(248, 250, 252));
            SettlementCard.BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240));
            SettlementIcon.Kind = PackIconKind.AlertCircleOutline;
            SettlementIcon.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            SettlementStatusLabel.Text = "Please enter a valid amount";
            SettlementDetailText.Text = "A positive payment amount is required.";
            SettlementDetailText.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            return;
        }

        if (_order.PaymentStatus == PaymentStatus.PAID && _existingPayment != null)
        {
            SettlementCard.Background = new SolidColorBrush(Color.FromRgb(240, 253, 244));
            SettlementCard.BorderBrush = new SolidColorBrush(Color.FromRgb(134, 239, 172));
            SettlementIcon.Kind = PackIconKind.CheckCircleOutline;
            SettlementIcon.Foreground = new SolidColorBrush(Color.FromRgb(22, 163, 74));
            SettlementStatusLabel.Text = $"{method.ToUpper()} PAYMENT RECORDING";
            SettlementDetailText.Text = $"Recording ₱{tendered:N2} via {method}.";
            SettlementDetailText.Foreground = new SolidColorBrush(Color.FromRgb(22, 163, 74));
            return;
        }

        if (isCash)
        {
            if (tendered > _balanceDue)
            {
                var change = tendered - _balanceDue;
                SettlementCard.Background = new SolidColorBrush(Color.FromRgb(236, 253, 245));
                SettlementCard.BorderBrush = new SolidColorBrush(Color.FromRgb(110, 231, 183));
                SettlementIcon.Kind = PackIconKind.CashRefund;
                SettlementIcon.Foreground = new SolidColorBrush(Color.FromRgb(5, 150, 105));
                SettlementStatusLabel.Text = "CHANGE TO RETURN TO CUSTOMER";
                SettlementDetailText.Text = $"₱{change:N2} Change Due (Balance settled in full)";
                SettlementDetailText.Foreground = new SolidColorBrush(Color.FromRgb(5, 150, 105));
            }
            else if (Math.Abs(tendered - _balanceDue) < 0.001m)
            {
                SettlementCard.Background = new SolidColorBrush(Color.FromRgb(240, 253, 244));
                SettlementCard.BorderBrush = new SolidColorBrush(Color.FromRgb(134, 239, 172));
                SettlementIcon.Kind = PackIconKind.CheckCircleOutline;
                SettlementIcon.Foreground = new SolidColorBrush(Color.FromRgb(22, 163, 74));
                SettlementStatusLabel.Text = "Exact payment tendered";
                SettlementDetailText.Text = "Balance will be settled in full (₱0.00 change).";
                SettlementDetailText.Foreground = new SolidColorBrush(Color.FromRgb(22, 163, 74));
            }
            else
            {
                var remaining = _balanceDue - tendered;
                SettlementCard.Background = new SolidColorBrush(Color.FromRgb(254, 243, 199));
                SettlementCard.BorderBrush = new SolidColorBrush(Color.FromRgb(252, 211, 77));
                SettlementIcon.Kind = PackIconKind.InformationOutline;
                SettlementIcon.Foreground = new SolidColorBrush(Color.FromRgb(217, 119, 6));
                SettlementStatusLabel.Text = "PARTIAL PAYMENT RECORDED";
                SettlementDetailText.Text = $"₱{remaining:N2} remaining balance will remain unpaid.";
                SettlementDetailText.Foreground = new SolidColorBrush(Color.FromRgb(180, 83, 9));
            }
        }
        else
        {
            var isFull = tendered >= _balanceDue;
            SettlementCard.Background = new SolidColorBrush(Color.FromRgb(239, 246, 255));
            SettlementCard.BorderBrush = new SolidColorBrush(Color.FromRgb(147, 197, 253));
            SettlementIcon.Kind = PackIconKind.CreditCardCheckOutline;
            SettlementIcon.Foreground = new SolidColorBrush(Color.FromRgb(37, 99, 235));
            SettlementStatusLabel.Text = $"{method.ToUpper()} PAYMENT";
            SettlementDetailText.Text = isFull
                ? $"Settling full balance of ₱{_balanceDue:N2} via {method}."
                : $"Recording partial payment of ₱{tendered:N2} via {method}.";
            SettlementDetailText.Foreground = new SolidColorBrush(Color.FromRgb(37, 99, 235));
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnConfirmPaymentClick(object sender, RoutedEventArgs e)
    {
        if (!IsCodSelected())
        {
            if (!decimal.TryParse(AmountBox.Text, out var tendered) || tendered <= 0)
            {
                MessageBox.Show("Please enter a valid positive payment amount.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                AmountBox.Focus();
                return;
            }
        }

        var tenderedAmt = IsCodSelected() ? 0m
            : decimal.TryParse(AmountBox.Text, out var t) ? t : 0m;
        var methodName = GetSelectedMethodName();
        var method = methodName switch
        {
            "GCash"                  => PaymentMethod.GCash,
            "Bank Transfer"          => PaymentMethod.BankTransfer,
            "Maya"                   => PaymentMethod.Maya,
            var s when s.StartsWith("COD") => PaymentMethod.COD,
            _                        => PaymentMethod.Cash
        };

        var reference = ReferenceBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(reference))
        {
            reference = $"{methodName.ToUpper()}-COUNTER";
        }

        var paymentDate = PaymentDatePicker.SelectedDate ?? DateTime.Today;

        try
        {
            var paymentSvc = new PaymentService(_db);

            if (_order.PaymentStatus == PaymentStatus.PAID && _existingPayment != null)
            {
                var (ok, msg) = paymentSvc.UpdatePayment(_existingPayment.Id, tenderedAmt, method, reference, paymentDate, _proofFilePath, _user?.Id);
                if (!ok)
                {
                    MessageBox.Show(msg, "Error Updating Payment", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            else
            {
                var (ok, msg, changeDue) = paymentSvc.RecordPayment(_order.Id, tenderedAmt, method, reference, null, _user?.Id, paymentDate, _proofFilePath);
                if (!ok)
                {
                    MessageBox.Show(msg, "Error Recording Payment", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            // Refresh order for printing if requested
            var svc = new OrderService(_db);
            var updatedOrder = svc.GetById(_order.Id) ?? _order;

            // Check if receipt print requested
            if (PrintReceiptCheckBox.IsChecked == true)
            {
                var savedPrinter = SeedData.GetSetting(_db, "default_printer", "");
                var bizName = SeedData.GetSetting(_db, "business_name", "ANTIQUE PASABUY POS");
                var footer = SeedData.GetSetting(_db, "receipt_footer", "Thank you for trusting Antique Pasabuy!");

                var preview = new PrintPreviewDialog(updatedOrder, bizName, footer, savedPrinter)
                {
                    Owner = Window.GetWindow(this)
                };
                preview.ShowDialog();
            }

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to record payment: {ex.Message}", "Error Recording Payment",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnBrowseProofClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select Payment Proof / Screenshot",
            Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All Files|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            _proofFilePath = dlg.FileName;
            ProofFileLabel.Text = System.IO.Path.GetFileName(dlg.FileName);
            ClearProofBtn.Visibility = Visibility.Visible;
        }
    }

    private void OnClearProofClick(object sender, RoutedEventArgs e)
    {
        _proofFilePath = null;
        ProofFileLabel.Text = "No file selected";
        ClearProofBtn.Visibility = Visibility.Collapsed;
    }
}
