namespace AntiquePasabuyPOS.Models;

/// <summary>
/// Audit trail for all inventory movements (adds, manual adjustments, sales, order cancellations).
/// </summary>
public class StockMovement
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    /// <summary>Positive when adding or restoring, negative when deducting.</summary>
    public double ChangeQty { get; set; }
    /// <summary>Stock balance immediately after this movement.</summary>
    public double NewQuantity { get; set; }
    /// <summary>Reason / description (e.g. "Added", "Adjusted: damaged", "Order OCT 1-002").</summary>
    public string Note { get; set; } = "";
    /// <summary>Linked order item if deducted/restored from an order; null for manual adds/adjustments.</summary>
    public int? OrderItemId { get; set; }

    // Navigation
    public Product? Product { get; set; }
    public OrderItem? OrderItem { get; set; }
}
