namespace AntiquePasabuyPOS.Models;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Store { get; set; } = "General"; // e.g., SM, Robinsons, Mercury Drug
    public decimal Price { get; set; }
    public string Unit { get; set; } = "pc";
    public string? Description { get; set; }
    public string? ImagePath { get; set; }
    public string? Barcode { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // ── Inventory Tracking (RING CAKE Excel workflow) ─────────────────────────
    /// <summary>Whether this product is kept in on-hand stock (STOCK item) vs bought to order (STORE item).</summary>
    public bool IsStockItem { get; set; } = false;
    /// <summary>Whether this product is exempt from the Pasabuy Fee (kept for DB migration, superseded by FeeType).</summary>
    [Obsolete("Use FeeType instead. IsFeeExempt is kept only for schema-migration reference.")]
    public bool IsFeeExempt { get; set; } = false;
    /// <summary>
    /// How the Pasabuy fee is applied to this product.
    /// STANDARD = included in tier subtotal, NONE = no fee, MANUAL = flat fee per unit.
    /// Defaults to STANDARD.
    /// </summary>
    public ProductFeeType FeeType { get; set; } = ProductFeeType.STANDARD;
    /// <summary>Flat fee per unit when FeeType == MANUAL (e.g. ₱50 per item). Null otherwise.</summary>
    public decimal? ManualFeeAmount { get; set; }
    /// <summary>Actual cost price from store/wholesale (maps to S&R/SM column in Excel).</summary>
    public decimal? CostPrice { get; set; }
    /// <summary>Opening inventory count at the start of the batch.</summary>
    public double OpeningStock { get; set; } = 0;
    /// <summary>Current on-hand stock quantity (decremented as orders are fulfilled).</summary>
    public double StockQuantity { get; set; } = 0;
    /// <summary>Additional stocks received during the batch period.</summary>
    public double AdditionalStocks { get; set; } = 0;

    // Computed inventory reconciliation
    public double TotalStocks => OpeningStock + AdditionalStocks;
    public double SoldItems   => TotalStocks - StockQuantity;
    public double RemainingItems => StockQuantity;
}
