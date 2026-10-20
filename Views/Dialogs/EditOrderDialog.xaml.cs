using System.Windows;
using System.Windows.Controls;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;
using Microsoft.EntityFrameworkCore;

namespace AntiquePasabuyPOS.Views.Dialogs;

public partial class EditOrderDialog : Window
{
    private readonly AppDbContext _db;
    private readonly Order _order;

    public EditOrderDialog(AppDbContext db, Order order)
    {
        InitializeComponent();
        _db = db;
        _order = order;
        Loaded += (_, _) => PopulateData();

        // Wire live recalc on fee field
        ServiceFeeBox.TextChanged  += (_, _) => RecalcGrandTotalPreview();
    }

    private void PopulateData()
    {
        TitleLabel.Text = $"Edit Order {_order.OrderNo}";

        var customers = _db.Customers.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name).ToList();
        customers.Insert(0, new Customer { Id = 0, Name = "-- Select Customer --" });
        CustomerCombo.ItemsSource = customers;
        CustomerCombo.SelectedValue = _order.CustomerId ?? 0;

        if (_order.DeliveryType == DeliveryType.Delivery)
            DeliveryRadio.IsChecked = true;
        else
            PickupRadio.IsChecked = true;

        AddressBox.Text = _order.Address ?? "";

        // Buying Day
        foreach (ComboBoxItem item in BuyingDayCombo.Items)
        {
            if (string.Equals(item.Content?.ToString(), _order.BuyingDay, StringComparison.OrdinalIgnoreCase))
            {
                BuyingDayCombo.SelectedItem = item;
                break;
            }
        }
        if (BuyingDayCombo.SelectedItem == null) BuyingDayCombo.SelectedIndex = 0;

        // Stores
        var stores = _db.Stores.AsNoTracking().Where(s => s.IsActive).Select(s => s.Name).Distinct().ToList();
        if (!stores.Contains("Robinsons Antique")) stores.Insert(0, "Robinsons Antique");
        StoreCombo.ItemsSource = stores;
        StoreCombo.Text = string.IsNullOrWhiteSpace(_order.PreferredStore) ? "Robinsons Antique" : _order.PreferredStore;

        // Payment Info
        var lastPayment = _order.Payments?.OrderByDescending(p => p.PaymentDate ?? p.CreatedAt).FirstOrDefault();
        var isPaid = _order.PaymentStatus == PaymentStatus.PAID;
        PaymentStatusCombo.SelectedIndex = isPaid ? 0 : 1;
        PaidDatePanel.Visibility = isPaid ? Visibility.Visible : Visibility.Collapsed;
        PaidDatePicker.SelectedDate = _order.PaidDate ?? DateTime.Today;

        var methodStr = lastPayment != null ? lastPayment.Method.ToString() : "GCash";
        foreach (ComboBoxItem item in PaymentMethodCombo.Items)
        {
            var content = item.Content?.ToString() ?? "";
            if (content.Equals(methodStr, StringComparison.OrdinalIgnoreCase) ||
                (content == "On Hand" && methodStr == "Cash") ||
                (content == "Bank" && methodStr == "BankTransfer") ||
                (content.StartsWith("COD", StringComparison.OrdinalIgnoreCase) && methodStr == "COD"))
            {
                PaymentMethodCombo.SelectedItem = item;
                break;
            }
        }
        if (PaymentMethodCombo.SelectedItem == null) PaymentMethodCombo.SelectedIndex = 0;

        ServiceFeeBox.Text = _order.ServiceFee.ToString("F2");
        NotesBox.Text = _order.Notes ?? "";
        RecalcGrandTotalPreview();
    }

    private void OnPaymentStatusChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PaidDatePanel == null || PaymentStatusCombo == null) return;
        var isPaid = (PaymentStatusCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() == "Paid";
        PaidDatePanel.Visibility = isPaid ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Live Grand Total Preview ──────────────────────────────────────────────

    private void RecalcGrandTotalPreview()
    {
        if (GrandTotalPreview is null) return;
        decimal.TryParse(ServiceFeeBox.Text, out var svc);
        var sub   = _order.ItemsSubtotal;
        var total = sub + (svc >= 0 ? svc : 0);
        GrandTotalPreview.Text = $"₱{(total > 0 ? total : 0m):N2}";
    }

    private void OnAutoCalcServiceFee(object sender, RoutedEventArgs e)
    {
        var fee = BusinessProfileService.IsPasabuyFeeEnabled(_db)
            ? PasabuyFeeService.CalculateServiceFee(_order.ItemsSubtotal, _db)
            : 0m;
        ServiceFeeBox.Text = fee.ToString("F2");
    }

    private void OnTypeChanged(object sender, RoutedEventArgs e)
    {
        if (AddressBox == null) return;
        AddressBox.IsEnabled = DeliveryRadio.IsChecked == true;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        ErrorLabel.Text = "";

        if (!decimal.TryParse(ServiceFeeBox.Text, out var svcFee) || svcFee < 0)
        {
            ErrorLabel.Text = "Please enter a valid non-negative Pasabuy Fee.";
            return;
        }

        var custId = CustomerCombo.SelectedValue is int cid && cid > 0 ? cid : (int?)null;
        if (!custId.HasValue)
        {
            ErrorLabel.Text = "Please select a customer for this order.";
            CustomerCombo.Focus();
            return;
        }
        var delivType = DeliveryRadio.IsChecked == true ? DeliveryType.Delivery : DeliveryType.Pickup;
        var address = string.IsNullOrWhiteSpace(AddressBox.Text) ? null : AddressBox.Text.Trim();
        var notes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim();
        var buyingDay = (BuyingDayCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Wednesday";
        var prefStore = string.IsNullOrWhiteSpace(StoreCombo.Text) ? "Robinsons Antique" : StoreCombo.Text.Trim();

        var svc = new OrderService(_db);
        var (ok, msg) = svc.Update(_order.Id, custId, delivType, address, notes, svcFee, 0m, 0m, buyingDay, prefStore);
        if (!ok)
        {
            ErrorLabel.Text = msg;
            return;
        }

        // Handle Payment Status and Method Update
        var isPaid = (PaymentStatusCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() == "Paid";
        var methodChoice = (PaymentMethodCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "GCash";
        var payMethod = methodChoice switch
        {
            "Bank"    => PaymentMethod.BankTransfer,
            "On Hand" => PaymentMethod.Cash,
            "Maya"    => PaymentMethod.Maya,
            var s when s.StartsWith("COD", StringComparison.OrdinalIgnoreCase) => PaymentMethod.COD,
            "Other"   => PaymentMethod.Other,
            _         => PaymentMethod.GCash
        };
        var paidDate = PaidDatePicker.SelectedDate ?? DateTime.Today;

        var existingPayments = _db.Payments.Where(p => p.OrderId == _order.Id).ToList();
        var orderEntity = _db.Orders.Include(o => o.Items).FirstOrDefault(o => o.Id == _order.Id);

        if (orderEntity != null)
        {
            var grandTotal = orderEntity.GrandTotal;
            if (isPaid)
            {
                if (existingPayments.Count == 0)
                {
                    _db.Payments.Add(new Payment
                    {
                        OrderId = _order.Id,
                        Amount = grandTotal,
                        Method = payMethod,
                        PaymentDate = paidDate,
                        CreatedAt = paidDate,
                        Notes = "Recorded in Order Edit"
                    });
                }
                else
                {
                    var p = existingPayments.First();
                    p.Amount = grandTotal;
                    p.Method = payMethod;
                    p.PaymentDate = paidDate;
                }
            }
            else
            {
                // Unpaid: remove existing payments
                _db.Payments.RemoveRange(existingPayments);
                if (payMethod == PaymentMethod.COD)
                {
                    _db.Payments.Add(new Payment
                    {
                        OrderId = _order.Id,
                        Amount = 0m,
                        Method = PaymentMethod.COD,
                        Reference = "COD-COUNTER",
                        PaymentDate = DateTime.Today,
                        CreatedAt = DateTime.Now,
                        Notes = "Cash on Delivery"
                    });
                }
            }
            _db.SaveChanges();
            OrderEventBus.NotifyPaymentRecorded(_order.Id, isPaid ? grandTotal : 0m);
        }

        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
