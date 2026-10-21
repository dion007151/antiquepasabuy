using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;

namespace AntiquePasabuyPOS.Services;

/// <summary>
/// Thin wrapper that delegates all financial math to Calc (Phase 9 → Phase 2 upgrade).
/// CalculateGrandTotal(Order) calls Calc.OrderTotal so the Pasabuy fee is always
/// recalculated from live items (honouring IsFeeExempt and ManualFeeOverride) rather than
/// reading the stale stored ServiceFee field.
/// DeterminePaymentStatus delegates to Calc.PaymentStatus so a zero-total order is PAID,
/// not UNPAID.
/// </summary>
public static class OrderCalculationService
{
    public const string PasabuyFeeLabel = "Pasabuy Fee";

    /// <summary>Items subtotal — delegates to Calc.OrderSubtotal.</summary>
    public static decimal CalculateItemsSubtotal(IEnumerable<OrderItem>? items)
        => Calc.OrderSubtotal(items);

    /// <summary>Grand total from scalar components (legacy callers that already have the fee).</summary>
    public static decimal CalculateGrandTotal(decimal itemsSubtotal, decimal pasabuyFee, decimal deliveryFee, decimal discount)
        => Calc.RoundMoney(Math.Max(0m, itemsSubtotal + pasabuyFee + deliveryFee - discount));

    /// <summary>Grand total from an Order — delegates to Calc.OrderTotal.</summary>
    public static decimal CalculateGrandTotal(Order order)
    {
        if (order == null) return 0m;
        return Calc.OrderTotal(order);
    }

    /// <summary>Total paid — delegates to Calc.TotalPaid.</summary>
    public static decimal CalculateTotalPaid(IEnumerable<Payment>? payments)
        => Calc.TotalPaid(payments);

    /// <summary>Balance due (never negative) — delegates to Calc.BalanceDue.</summary>
    public static decimal CalculateBalance(decimal grandTotal, decimal totalPaid)
        => Calc.BalanceDue(grandTotal, totalPaid);

    /// <summary>Payment status — delegates to Calc.PaymentStatus.</summary>
    public static PaymentStatus DeterminePaymentStatus(decimal grandTotal, decimal totalPaid)
        => Calc.PaymentStatus(grandTotal, totalPaid);

    /// <summary>Pasabuy fee from a subtotal via configured rate tiers.</summary>
    public static decimal ResolvePasabuyFee(decimal itemsSubtotal, AppDbContext? db = null)
        => PasabuyFeeService.CalculateServiceFee(itemsSubtotal, db);
}
