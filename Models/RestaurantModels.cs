using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Windows.Media;
using AntiquePasabuyPOS.Services;

namespace AntiquePasabuyPOS.Models;

/// <summary>
/// Status of a restaurant table.
/// </summary>
public enum RestoTableStatus
{
    Available,
    Occupied,
    Reserved,
    Billing
}

/// <summary>
/// Status of a restaurant order/ticket.
/// </summary>
public enum RestoOrderStatus
{
    Pending,
    Preparing,
    Ready,
    Served,
    Completed,
    Cancelled
}

/// <summary>
/// Status of a sale transaction.
/// </summary>
public enum RestoSaleStatus
{
    Paid,
    Pending,
    Voided
}

/// <summary>
/// Represents a dining or bar table in Angelito's Bar & Resto Grill.
/// </summary>
public class RestoTable
{
    public int Id { get; set; }
    [Required] public string TableNumber { get; set; } = "";
    public string Name { get; set; } = "";
    public int Capacity { get; set; } = 4;
    public string Section { get; set; } = "Main Dining"; // Main Dining, Bar, VIP, Patio
    public RestoTableStatus Status { get; set; } = RestoTableStatus.Available;
    public string? CurrentCustomerName { get; set; }
    public decimal CurrentBillAmount { get; set; } = 0m;
    public DateTime? OccupiedSince { get; set; }
    public string BusinessId { get; set; } = BusinessProfileService.ANGELITOS_RESTO;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// Represents a restaurant menu item for Angelito's.
/// </summary>
public class RestoMenuItem
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    [Required] public string Name { get; set; } = "";
    public string Category { get; set; } = "Grill"; // Appetizer, Sizzling, Grill, Pulutan, Drinks, Alcoholic, Rice, Dessert
    public string? Description { get; set; }
    public decimal Price { get; set; } = 0m;
    public decimal? CostPrice { get; set; }
    public bool IsAvailable { get; set; } = true;
    public string? ImagePath { get; set; }
    public string BusinessId { get; set; } = BusinessProfileService.ANGELITOS_RESTO;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [NotMapped]
    public ImageSource? Thumbnail => !string.IsNullOrWhiteSpace(ImagePath) ? ProductImageService.LoadBitmap(ImagePath, 0) : null;

    [NotMapped]
    public bool HasThumbnail => Thumbnail != null;

    [NotMapped]
    public bool HasNoThumbnail => !HasThumbnail;
}

/// <summary>
/// Represents an inventory item for Angelito's (meat, beverages, liquor, supplies).
/// </summary>
public class RestoInventoryItem
{
    public int Id { get; set; }
    public string ItemCode { get; set; } = "";
    [Required] public string Name { get; set; } = "";
    public string Category { get; set; } = "Meat"; // Meat, Beverage, Liquor, Produce, Seasoning, Supplies
    public decimal Quantity { get; set; } = 0m;
    public string Unit { get; set; } = "pc"; // kg, bottle, case, pc, pack
    public decimal MinStockLevel { get; set; } = 5m;
    public decimal? CostPrice { get; set; }
    public string BusinessId { get; set; } = BusinessProfileService.ANGELITOS_RESTO;
    public DateTime LastRestockedAt { get; set; } = DateTime.Now;

    public bool IsLowStock => Quantity <= MinStockLevel;

    /// <summary>Total value of this stock line (Quantity × CostPrice).</summary>
    [NotMapped]
    public decimal StockValue => Quantity * (CostPrice ?? 0m);

    /// <summary>Human-readable stock status label.</summary>
    [NotMapped]
    public string StockStatus => IsLowStock ? "LOW STOCK" : "IN STOCK";
}

/// <summary>
/// Staff member record for Angelito's Bar & Resto Grill.
/// </summary>
public class RestoStaff
{
    public int Id { get; set; }
    public string StaffCode { get; set; } = "";
    [Required] public string FullName { get; set; } = "";
    public string Role { get; set; } = "Server"; // Manager, Chef, Server, Bartender, Cashier
    public string ContactNumber { get; set; } = "";
    public string Shift { get; set; } = "Evening"; // Morning, Afternoon, Evening, Night
    public bool IsActive { get; set; } = true;
    public string BusinessId { get; set; } = BusinessProfileService.ANGELITOS_RESTO;
    public DateTime HireDate { get; set; } = DateTime.Now;
}

/// <summary>
/// An active restaurant order / kitchen ticket.
/// </summary>
public class RestoOrder
{
    public int Id { get; set; }
    public string OrderNo { get; set; } = "";
    public string? TableNumber { get; set; }
    public string? CustomerName { get; set; }
    public RestoOrderStatus Status { get; set; } = RestoOrderStatus.Pending;
    public decimal TotalAmount { get; set; } = 0m;
    public string? ServerName { get; set; }
    public string? Notes { get; set; }
    public string BusinessId { get; set; } = BusinessProfileService.ANGELITOS_RESTO;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public ICollection<RestoOrderItem> Items { get; set; } = new List<RestoOrderItem>();
}

/// <summary>
/// Individual ordered item within a RestoOrder.
/// </summary>
public class RestoOrderItem
{
    public int Id { get; set; }
    public int RestoOrderId { get; set; }
    public RestoOrder? RestoOrder { get; set; }
    public int? MenuItemId { get; set; }
    [Required] public string ItemName { get; set; } = "";
    public decimal Quantity { get; set; } = 1m;
    public decimal UnitPrice { get; set; } = 0m;
    public decimal Subtotal => Quantity * UnitPrice;
    public RestoOrderStatus Status { get; set; } = RestoOrderStatus.Pending; // Pending, Preparing, Ready, Served
    public string? SpecialInstructions { get; set; }
}

/// <summary>
/// Completed or recorded sales transaction for Angelito's Bar & Resto Grill.
/// </summary>
public class RestoSale
{
    public int Id { get; set; }
    public string SaleNo { get; set; } = "";
    public string? TableNumber { get; set; }
    public string? CustomerName { get; set; }
    public decimal Subtotal { get; set; } = 0m;
    public decimal Discount { get; set; } = 0m;
    public decimal Tax { get; set; } = 0m;
    public decimal TotalAmount { get; set; } = 0m;
    public decimal AmountPaid { get; set; } = 0m;
    public decimal Change { get; set; } = 0m;
    public string PaymentMethod { get; set; } = "Cash"; // Cash, GCash, Card, Maya
    public RestoSaleStatus PaymentStatus { get; set; } = RestoSaleStatus.Paid;
    public string? CashierName { get; set; }
    public string? Notes { get; set; }
    public string BusinessId { get; set; } = BusinessProfileService.ANGELITOS_RESTO;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public ICollection<RestoSaleItem> Items { get; set; } = new List<RestoSaleItem>();
}

/// <summary>
/// Line item in a completed restaurant sale transaction.
/// </summary>
public class RestoSaleItem
{
    public int Id { get; set; }
    public int RestoSaleId { get; set; }
    public RestoSale? RestoSale { get; set; }
    public int? MenuItemId { get; set; }
    [Required] public string ItemName { get; set; } = "";
    public string Category { get; set; } = "";
    public decimal UnitPrice { get; set; } = 0m;
    public decimal? CostPrice { get; set; }  // Original cost from RestoMenuItem (for profit tracking)
    public decimal Quantity { get; set; } = 1m;
    public decimal Subtotal { get; set; } = 0m;
    public string? Notes { get; set; }
}
