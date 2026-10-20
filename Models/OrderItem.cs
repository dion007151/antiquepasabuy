namespace AntiquePasabuyPOS.Models;

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order? Order { get; set; }
    public string ProductName { get; set; } = "";
    public double Qty { get; set; } = 1;
    public string Unit { get; set; } = "pc";
    public decimal EstimatedPrice { get; set; }
    public decimal? ActualPrice { get; set; }
    public ItemStatus Status { get; set; } = ItemStatus.Pending;
    public string? Notes { get; set; }

    // Pasabuy Store & Purchasing Tracking
    public string Store { get; set; } = "General"; // SM, Robinsons, Grocery, Pharmacy, etc.
    public ItemSource Source { get; set; } = ItemSource.STORE; // STORE (Bought at store) vs STOCK (from shop inventory)
    public decimal? CostPrice { get; set; } // Actual store purchase cost (matches S&R/SM in Excel)
    public bool IsFeeExempt { get; set; } = false; // True for retail items with ₱0 Pasabuy Fee
    public double? PurchasedQty { get; set; }
    public DateTime? PurchaseDate { get; set; }
    public string? ReceiptNo { get; set; }
    public string? UnavailableReason { get; set; } // Out of Stock, Store Closed, Wrong Product, Price Changed, Other
    public string? PurchaseNotes { get; set; }

    // Computed Original Cost & Now Sell Price & Subtotal
    public decimal? OriginalCost => (CostPrice.HasValue && CostPrice.Value > 0) ? CostPrice : null;
    public bool HasOriginalCost => OriginalCost.HasValue && OriginalCost.Value > 0;

    public decimal NowSellPrice => (ActualPrice.HasValue && ActualPrice.Value > 0) ? ActualPrice.Value : EstimatedPrice;
    public bool HasActualPrice => ActualPrice.HasValue && ActualPrice.Value > 0;
    public decimal PriceDifference => (ActualPrice.HasValue && ActualPrice.Value > 0) ? ActualPrice.Value - EstimatedPrice : 0m;
    public decimal Subtotal => NowSellPrice * (decimal)Qty;
    public bool HasNotes => !string.IsNullOrWhiteSpace(PurchaseNotes);
}
