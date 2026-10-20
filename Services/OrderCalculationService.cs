using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;

namespace AntiquePasabuyPOS.Services;

/// <summary>
/// Single source of truth calculation engine for all orders across the system (Phase 9).
/// Enforces standard Pasabuy Fee, Delivery Fee, Discounts, Grand Total, and Balance Due.
/// </summary>
public static class OrderCalculationService
{
    public const string PasabuyFeeLabel = "Pasabuy Fee";

    /// <summary>
    /// Computes the items subtotal based on actual price (if bought) or estimated price.
    /// Excludes unavailable and cancelled items to ensure accurate grand totals and balances.
    /// </summary>
    public static decimal CalculateItemsSubtotal(IEnumerable<OrderItem>? items)
    {
        if (items == null) return 0m;
        return items
            .Where(i => i.Status != ItemStatus.UNAVAILABLE && i.Status != ItemStatus.CANCELLED && !string.IsNullOrWhiteSpace(i.ProductName) && i.ProductName.Trim() != "-")
            .Sum(i => (i.ActualPrice ?? i.EstimatedPrice) * (decimal)i.Qty);
    }

    /// <summary>
    /// Computes the grand total for an order:
    /// Items Subtotal + Pasabuy Fee + Delivery Fee - Discount.
    /// Never returns negative amount.
    /// </summary>
    public static decimal CalculateGrandTotal(decimal itemsSubtotal, decimal pasabuyFee, decimal deliveryFee, decimal discount)
    {
        var total = itemsSubtotal + pasabuyFee + deliveryFee - discount;
        return Math.Max(0m, total);
    }

    /// <summary>
    /// Computes the grand total directly from an Order instance.
    /// </summary>
    public static decimal CalculateGrandTotal(Order order)
    {
        if (order == null) return 0m;
        var subtotal = CalculateItemsSubtotal(order.Items);
        return CalculateGrandTotal(subtotal, order.PasabuyFee, order.DeliveryFee, order.Discount);
    }

    /// <summary>
    /// Computes the total payments collected for an order.
    /// </summary>
    public static decimal CalculateTotalPaid(IEnumerable<Payment>? payments)
    {
        if (payments == null) return 0m;
        return payments.Sum(p => p.Amount);
    }

    /// <summary>
    /// Computes the balance due: Grand Total - Total Paid.
    /// Never returns negative balance (change tendered is handled at point of payment).
    /// </summary>
    public static decimal CalculateBalance(decimal grandTotal, decimal totalPaid)
    {
        return Math.Max(0m, grandTotal - totalPaid);
    }

    /// <summary>
    /// Determines the canonical payment status (Phase 11): UNPAID, PARTIAL, PAID.
    /// </summary>
    public static PaymentStatus DeterminePaymentStatus(decimal grandTotal, decimal totalPaid)
    {
        if (totalPaid <= 0m) return PaymentStatus.UNPAID;
        if (totalPaid < grandTotal) return PaymentStatus.PARTIAL;
        return PaymentStatus.PAID;
    }

    /// <summary>
    /// Resolves the official Pasabuy Fee for an order subtotal using configured rate tiers.
    /// </summary>
    public static decimal ResolvePasabuyFee(decimal itemsSubtotal, AppDbContext? db = null)
    {
        return PasabuyFeeService.CalculateServiceFee(itemsSubtotal, db);
    }
}
