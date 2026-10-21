using AntiquePasabuyPOS.Services;

namespace AntiquePasabuyPOS.Models;

public class Order
{
    public int Id { get; set; }
    public string OrderNo { get; set; } = "";
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.WAITING_CONFIRMATION;
    public DeliveryType DeliveryType { get; set; } = DeliveryType.Pickup;
    public string? Address { get; set; }
    public string? Notes { get; set; }

    // Official Antique Pasabuy Fields
    public string? BatchCode { get; set; } // e.g., AUG2, JCO aug4
    public string BuyingDay { get; set; } = "Wednesday"; // Wednesday, Thursday, etc.
    public string? PreferredStore { get; set; } // SM, Robinsons, etc.
    public string BusinessId { get; set; } = "ANTIQUE_PASABUY";

    // Masterlist assignment — nullable so existing orders without a batch still work
    public int? MasterlistId { get; set; }
    public Masterlist? Masterlist { get; set; }


    // Official Pasabuy Fee (mapped directly to database ServiceFee field for zero data loss)
    public decimal ServiceFee { get; set; } = 50m;
    public decimal PasabuyFee
    {
        get
        {
            if (Items == null)
            {
                System.Diagnostics.Debug.WriteLine($"[Order {OrderNo}] Items collection is not loaded.");
                throw new InvalidOperationException($"[Order {OrderNo}] Items collection must be loaded before accessing PasabuyFee.");
            }
            return Calc.PasabuyFee(this);
        }
    }

    // Manual Fee Override (Rule 3)
    public decimal? ManualFeeOverride { get; set; }
    public string? FeeOverrideNote { get; set; }
    public bool IsFeeAdjusted { get; set; } = false;

    public decimal DeliveryFee { get; set; } = 0m;
    public decimal Discount { get; set; } = 0m;
    public int? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public Delivery? Delivery { get; set; }

    public decimal ItemsSubtotal
    {
        get
        {
            if (Items == null)
            {
                System.Diagnostics.Debug.WriteLine($"[Order {OrderNo}] Items collection is not loaded.");
                throw new InvalidOperationException($"[Order {OrderNo}] Items collection must be loaded before accessing ItemsSubtotal.");
            }
            return Calc.OrderSubtotal(Items);
        }
    }

    public decimal GrandTotal
    {
        get
        {
            if (Items == null)
            {
                System.Diagnostics.Debug.WriteLine($"[Order {OrderNo}] Items collection is not loaded.");
                throw new InvalidOperationException($"[Order {OrderNo}] Items collection must be loaded before accessing GrandTotal.");
            }
            return Calc.OrderTotal(this);
        }
    }

    public decimal TotalPaid
    {
        get
        {
            if (Payments == null)
            {
                System.Diagnostics.Debug.WriteLine($"[Order {OrderNo}] Payments collection is not loaded.");
                throw new InvalidOperationException($"[Order {OrderNo}] Payments collection must be loaded before accessing TotalPaid.");
            }
            return Calc.TotalPaid(Payments);
        }
    }

    public decimal Balance
    {
        get
        {
            if (Items == null || Payments == null)
            {
                System.Diagnostics.Debug.WriteLine($"[Order {OrderNo}] Items or Payments collection is not loaded.");
                throw new InvalidOperationException($"[Order {OrderNo}] Items and Payments collections must be loaded before accessing Balance.");
            }
            return Calc.BalanceDue(this);
        }
    }

    public PaymentStatus PaymentStatus
    {
        get
        {
            if (Items == null || Payments == null)
            {
                System.Diagnostics.Debug.WriteLine($"[Order {OrderNo}] Items or Payments collection is not loaded.");
                throw new InvalidOperationException($"[Order {OrderNo}] Items and Payments collections must be loaded before accessing PaymentStatus.");
            }
            return Calc.PaymentStatus(this);
        }
    }

    public DateTime? PaidDate => Payments != null && Payments.Any(p => p.Amount > 0)
        ? Payments.Where(p => p.Amount > 0)
                  .OrderByDescending(p => p.PaymentDate ?? p.CreatedAt)
                  .Select(p => (DateTime?)(p.PaymentDate ?? p.CreatedAt))
                  .FirstOrDefault()
        : null;

    public string PaidDateDisplay => PaymentStatus == PaymentStatus.PAID && PaidDate.HasValue
        ? PaidDate.Value.ToString("MMM dd, yyyy")
        : "—";

    public decimal EstimatedItemsTotal => Items?
        .Where(i => i.Status != ItemStatus.UNAVAILABLE && i.Status != ItemStatus.CANCELLED && !string.IsNullOrWhiteSpace(i.ProductName) && i.ProductName.Trim() != "-")
        .Sum(i => i.EstimatedPrice * (decimal)i.Qty) ?? 0m;

    public decimal ActualItemsTotal => Items?
        .Where(i => (i.Status == ItemStatus.PURCHASED || i.ActualPrice.HasValue) && !string.IsNullOrWhiteSpace(i.ProductName) && i.ProductName.Trim() != "-")
        .Sum(i => (i.ActualPrice ?? i.EstimatedPrice) * (decimal)i.Qty) ?? 0m;

    public string PaymentMethodDisplay => Payments != null && Payments.Any()
        ? string.Join(", ", Payments.Select(p => p.Method.ToString()).Distinct())
        : "Not Paid";

    public string StatusDisplay => Status switch
    {
        OrderStatus.WAITING_CONFIRMATION => "Waiting to Buy",
        OrderStatus.READY_TO_BUY         => "Waiting to Buy",
        OrderStatus.BUYING_NOW           => "Buying",
        OrderStatus.BOUGHT               => "Bought",
        OrderStatus.COMPLETED            => "Order Finished",
        OrderStatus.CANCELLED            => "Cancelled",
        _                                => "Waiting to Buy"
    };

    public bool IsCOD => Payments != null && Payments.Any(p => p.Method == PaymentMethod.COD);

    public string PaymentStatusDisplay => PaymentStatus switch
    {
        PaymentStatus.PAID    => "Paid",
        PaymentStatus.PARTIAL => IsCOD ? "Partly Paid (COD)" : "Partly Paid",
        PaymentStatus.UNPAID  => IsCOD ? "COD (Unpaid)" : "Not Paid",
        _                     => "Not Paid"
    };

    public bool IsPickup => DeliveryType == DeliveryType.PICKUP;
    public bool IsDelivery => DeliveryType == DeliveryType.DELIVERY;
}
