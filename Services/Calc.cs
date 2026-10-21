using System;
using System.Collections.Generic;
using System.Linq;
using AntiquePasabuyPOS.Models;

namespace AntiquePasabuyPOS.Services;

/// <summary>
/// Single source of truth calculation engine for all financial math, payment statuses,
/// order statuses, and dashboard/shopping/report counts across Dionix POS (Phase 1).
///
/// PURE FUNCTIONS ONLY: Zero UI, zero database, zero async calls.
/// Enforces MidpointRounding.AwayFromZero and 2-decimal money safety.
/// </summary>
public static class Calc
{
    // ── Helper: 2-Decimal Banker-Safe Money Rounding ─────────────────────────
    public static decimal RoundMoney(decimal amount)
        => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    // ── Rule 1: Item Amount = Qty x Price ─────────────────────────────────────
    /// <summary>
    /// Computes the total price for an item quantity and price.
    /// Qty accepts decimals (e.g. 0.5 for split items). Price accepts decimals (e.g. 18.75).
    /// Always rounds to 2 decimals using MidpointRounding.AwayFromZero.
    /// </summary>
    public static decimal ItemAmount(double qty, decimal price)
    {
        if (qty <= 0 || price <= 0m) return 0m;
        return RoundMoney((decimal)qty * price);
    }

    /// <summary>
    /// Resolves the unit price for an OrderItem: ActualPrice (if bought/resolved) or EstimatedPrice.
    /// </summary>
    public static decimal ResolveItemUnitPrice(OrderItem? item)
    {
        if (item is null) return 0m;
        return (item.ActualPrice.HasValue && item.ActualPrice.Value > 0)
            ? item.ActualPrice.Value
            : item.EstimatedPrice;
    }

    /// <summary>
    /// Computes the amount for an OrderItem.
    /// Excluded items (Unavailable, Reorder, Cancelled) contribute ₱0.00.
    /// </summary>
    public static decimal ItemAmount(OrderItem? item)
    {
        if (item is null || !IsCountedItem(item)) return 0m;
        var unitPrice = ResolveItemUnitPrice(item);
        return ItemAmount(item.Qty, unitPrice);
    }

    // ── Rule 2: Item Status & Subtotal Inclusion ──────────────────────────────
    /// <summary>
    /// Returns true if an item is counted in order subtotal and fee calculation.
    /// Counted: PENDING (estimate), PURCHASED / Bought, BOUGHT_FROM_STOCK.
    /// Not Counted (amount 0): UNAVAILABLE / Not available, REORDER, CANCELLED.
    /// </summary>
    public static bool IsCountedItem(OrderItem? item)
    {
        if (item is null) return false;
        if (string.IsNullOrWhiteSpace(item.ProductName) || item.ProductName.Trim() == "-") return false;

        return item.Status switch
        {
            ItemStatus.PENDING           => true,
            ItemStatus.PURCHASED         => true,
            ItemStatus.BOUGHT_FROM_STOCK => true,
            ItemStatus.UNAVAILABLE       => false,
            ItemStatus.REORDER           => false,
            ItemStatus.CANCELLED         => false,
            _                            => false
        };
    }

    /// <summary>
    /// Computes an order's items subtotal from its items.
    /// Automatically excludes Unavailable, Reorder, and Cancelled items.
    /// </summary>
    public static decimal OrderSubtotal(IEnumerable<OrderItem>? items)
    {
        if (items is null) return 0m;
        decimal sum = 0m;
        foreach (var item in items)
        {
            if (IsCountedItem(item))
            {
                sum += ItemAmount(item);
            }
        }
        return RoundMoney(sum);
    }

    /// <summary>
    /// Computes the fee-bearing (taxable) subtotal.
    /// Products with IsFeeExempt == true contribute nothing to this fee subtotal.
    /// </summary>
    public static decimal FeeSubtotal(IEnumerable<OrderItem>? items)
    {
        if (items is null) return 0m;
        decimal sum = 0m;
        foreach (var item in items)
        {
            if (IsCountedItem(item) && !item.IsFeeExempt)
            {
                sum += ItemAmount(item);
            }
        }
        return RoundMoney(sum);
    }

    // ── Rule 3: Pasabuy Fee & Tier Table ──────────────────────────────────────
    /// <summary>
    /// Computes the Pasabuy service fee using the lower-bound Excel approximate-match rule:
    /// - If subtotal <= 0: fee = 0
    /// - If subtotal >= 2500: fee = 15% of subtotal
    /// - Else: tier with highest MinAmount <= subtotal determines the fee.
    /// If a valid manual override and required note are provided, the override replaces computed fee and is flagged adjusted.
    /// </summary>
    public static decimal PasabuyFee(
        decimal feeSubtotal,
        IEnumerable<PasabuyRateTier>? feeTiers,
        decimal? manualOverride,
        string? overrideNote,
        out bool isAdjusted)
    {
        // 1. Manual Fee Override with mandatory note
        if (manualOverride.HasValue)
        {
            if (string.IsNullOrWhiteSpace(overrideNote))
            {
                throw new ArgumentException("A note is required when overriding the Pasabuy Fee.");
            }
            isAdjusted = true;
            return RoundMoney(Math.Max(0m, manualOverride.Value));
        }

        isAdjusted = false;

        // 2. Subtotal <= 0 gives fee 0
        if (feeSubtotal <= 0m)
        {
            return 0m;
        }

        // 3. Subtotal >= 2500 gives 15% of the subtotal
        if (feeSubtotal >= 2500m)
        {
            return RoundMoney(feeSubtotal * 0.15m);
        }

        // 4. Excel Approximate-Match Lookup on MinAmount (lower bound)
        // A subtotal gets the tier with the highest Min that is <= subtotal.
        // Ignore the Max value. Subtotal <= 0 gives fee 0. Subtotal >= 2500 gives 15%.
        var tiers = (feeTiers ?? PasabuyFeeService.GetDefaultTiers())
            .OrderBy(t => t.MinAmount)
            .ToList();

        PasabuyRateTier? matchedTier = null;
        foreach (var tier in tiers)
        {
            if (feeSubtotal >= tier.MinAmount)
            {
                matchedTier = tier;
            }
            else
            {
                break;
            }
        }

        // If subtotal is positive (0 < subtotal < first tier MinAmount, e.g. 0.50),
        // fallback to the base tier (₱50.00)
        if (matchedTier is null && feeSubtotal > 0m)
        {
            matchedTier = tiers.FirstOrDefault(t => t.MinAmount <= 1m) ?? tiers.FirstOrDefault();
        }

        if (matchedTier is not null)
        {
            if (matchedTier.FixedFee.HasValue)
            {
                return RoundMoney(matchedTier.FixedFee.Value);
            }
            if (matchedTier.PercentageFee.HasValue)
            {
                return RoundMoney(feeSubtotal * matchedTier.PercentageFee.Value);
            }
        }

        // 5. Standard fallbacks matching official rate table
        if (feeSubtotal >= 2000m) return 350.00m;
        if (feeSubtotal >= 1500m) return 300.00m;
        if (feeSubtotal >= 1000m) return 250.00m;
        if (feeSubtotal >= 300m)  return 150.00m;
        if (feeSubtotal >= 200m)  return 100.00m;
        if (feeSubtotal >= 100m)  return 75.00m;
        return 50.00m; // 0 < subtotal < 100
    }

    public static decimal PasabuyFee(
        decimal feeSubtotal,
        IEnumerable<PasabuyRateTier>? feeTiers = null)
    {
        return PasabuyFee(feeSubtotal, feeTiers, null, null, out _);
    }

    /// <summary>
    /// Computes the Pasabuy fee for a collection of order items.
    /// Fee-exempt items contribute nothing to the fee subtotal.
    /// </summary>
    public static decimal PasabuyFee(
        IEnumerable<OrderItem>? items,
        IEnumerable<PasabuyRateTier>? feeTiers,
        decimal? manualOverride,
        string? overrideNote,
        out bool isAdjusted)
    {
        var taxable = FeeSubtotal(items);
        return PasabuyFee(taxable, feeTiers, manualOverride, overrideNote, out isAdjusted);
    }

    public static decimal PasabuyFee(
        IEnumerable<OrderItem>? items,
        IEnumerable<PasabuyRateTier>? feeTiers = null)
    {
        return PasabuyFee(items, feeTiers, null, null, out _);
    }

    /// <summary>
    /// Computes the Pasabuy fee directly for an Order entity.
    /// Respects manual override if set on the order with a note.
    /// </summary>
    public static decimal PasabuyFee(Order? order, IEnumerable<PasabuyRateTier>? feeTiers = null)
    {
        if (order is null || order.Status == Models.OrderStatus.CANCELLED) return 0m;

        if (order.ManualFeeOverride.HasValue && (order.IsFeeAdjusted || !string.IsNullOrWhiteSpace(order.FeeOverrideNote)))
        {
            var note = !string.IsNullOrWhiteSpace(order.FeeOverrideNote) ? order.FeeOverrideNote : "Manual fee override";
            return PasabuyFee(order.Items, feeTiers, order.ManualFeeOverride.Value, note, out _);
        }

        return PasabuyFee(order.Items, feeTiers, null, null, out _);
    }

    // ── Rule 4: Total Bill = Counted Items Subtotal + Pasabuy Fee ─────────────
    /// <summary>
    /// Computes the total bill: Counted Items Subtotal + Pasabuy Fee.
    /// Delivery fee and discount are treated as 0 in this phase per Rule 4.
    /// </summary>
    public static decimal OrderTotal(decimal itemsSubtotal, decimal pasabuyFee)
    {
        return RoundMoney(Math.Max(0m, itemsSubtotal + pasabuyFee));
    }

    /// <summary>
    /// Computes the total bill directly for an Order instance.
    /// Cancelled orders have a total bill of ₱0.00.
    /// </summary>
    public static decimal OrderTotal(Order? order, IEnumerable<PasabuyRateTier>? feeTiers = null)
    {
        if (order is null || order.Status == Models.OrderStatus.CANCELLED) return 0m;
        var subtotal = OrderSubtotal(order.Items);
        var fee = PasabuyFee(order, feeTiers);
        return OrderTotal(subtotal, fee);
    }

    // ── Rule 5: Payments, Balance Due, Payment Status ─────────────────────────
    /// <summary>
    /// Computes the total paid as sum of all payment amounts.
    /// </summary>
    public static decimal TotalPaid(IEnumerable<Payment>? payments)
    {
        if (payments is null) return 0m;
        return RoundMoney(payments.Sum(p => p.Amount));
    }

    /// <summary>
    /// Balance due = total bill - total paid (never negative).
    /// </summary>
    public static decimal BalanceDue(decimal totalBill, decimal totalPaid)
    {
        return RoundMoney(Math.Max(0m, totalBill - totalPaid));
    }

    /// <summary>
    /// Balance due directly for an Order instance.
    /// </summary>
    public static decimal BalanceDue(Order? order, IEnumerable<PasabuyRateTier>? feeTiers = null)
    {
        if (order is null || order.Status == Models.OrderStatus.CANCELLED) return 0m;
        var total = OrderTotal(order, feeTiers);
        var paid = TotalPaid(order.Payments);
        return BalanceDue(total, paid);
    }

    /// <summary>
    /// Returns the overpaid amount when totalPaid > totalBill.
    /// </summary>
    public static decimal OverpaidAmount(decimal totalBill, decimal totalPaid)
    {
        return totalPaid > totalBill ? RoundMoney(totalPaid - totalBill) : 0m;
    }

    /// <summary>
    /// Derived payment status:
    /// - A zero-total order is NOT shown as Unpaid (returns PAID).
    /// - UNPAID: paid == 0 (and total > 0).
    /// - PARTIAL: 0 < paid < total.
    /// - PAID: paid >= total and total > 0 (or total == 0).
    /// </summary>
    public static PaymentStatus PaymentStatus(decimal totalBill, decimal totalPaid)
    {
        if (totalBill <= 0m) return Models.PaymentStatus.PAID;
        if (totalPaid <= 0m) return Models.PaymentStatus.UNPAID;
        if (totalPaid < totalBill) return Models.PaymentStatus.PARTIAL;
        return Models.PaymentStatus.PAID;
    }

    public static PaymentStatus PaymentStatus(Order? order, IEnumerable<PasabuyRateTier>? feeTiers = null)
    {
        if (order is null) return Models.PaymentStatus.PAID;
        var total = OrderTotal(order, feeTiers);
        var paid = TotalPaid(order.Payments);
        return PaymentStatus(total, paid);
    }

    // ── Rule 6: Order Status Derived from Items ──────────────────────────────
    /// <summary>
    /// Order status derived from items:
    /// - CANCELLED: order is cancelled.
    /// - WAITING_CONFIRMATION (Waiting to Buy): at least one Pending item.
    /// - BOUGHT: no Pending items, not Completed.
    /// - COMPLETED: marked completed (picked up or delivered).
    /// </summary>
    public static OrderStatus OrderStatus(bool isCancelled, bool isCompleted, IEnumerable<OrderItem>? items)
    {
        if (isCancelled) return Models.OrderStatus.CANCELLED;
        if (isCompleted) return Models.OrderStatus.COMPLETED;

        if (items is not null && items.Any(i => i.Status == ItemStatus.PENDING && !string.IsNullOrWhiteSpace(i.ProductName) && i.ProductName.Trim() != "-"))
        {
            return Models.OrderStatus.WAITING_CONFIRMATION;
        }

        return Models.OrderStatus.BOUGHT;
    }

    public static OrderStatus OrderStatus(Order? order)
    {
        if (order is null) return Models.OrderStatus.WAITING_CONFIRMATION;
        var isCancelled = order.Status == Models.OrderStatus.CANCELLED;
        var isCompleted = order.Status == Models.OrderStatus.COMPLETED;
        return OrderStatus(isCancelled, isCompleted, order.Items);
    }

    // ── Rule 7: Definitions of Every Count and Sum ────────────────────────────

    /// <summary>
    /// Items to buy = number of Pending items in non-cancelled orders.
    /// </summary>
    public static int ItemsToBuyCount(IEnumerable<Order>? orders)
    {
        if (orders is null) return 0;
        return orders
            .Where(o => o.Status != Models.OrderStatus.CANCELLED)
            .SelectMany(o => o.Items ?? Enumerable.Empty<OrderItem>())
            .Count(i => i.Status == ItemStatus.PENDING && !string.IsNullOrWhiteSpace(i.ProductName) && i.ProductName.Trim() != "-");
    }

    /// <summary>
    /// Orders waiting to buy = number of non-cancelled orders with at least one Pending item.
    /// </summary>
    public static int OrdersWaitingToBuyCount(IEnumerable<Order>? orders)
    {
        if (orders is null) return 0;
        return orders.Count(o => o.Status != Models.OrderStatus.CANCELLED &&
                                 o.Items != null &&
                                 o.Items.Any(i => i.Status == ItemStatus.PENDING && !string.IsNullOrWhiteSpace(i.ProductName) && i.ProductName.Trim() != "-"));
    }

    /// <summary>
    /// Orders bought, not finished = orders with no Pending items, not Completed, not Cancelled.
    /// </summary>
    public static int OrdersBoughtNotFinishedCount(IEnumerable<Order>? orders)
    {
        if (orders is null) return 0;
        return orders.Count(o => o.Status != Models.OrderStatus.CANCELLED &&
                                 o.Status != Models.OrderStatus.COMPLETED &&
                                 (o.Items == null || !o.Items.Any(i => i.Status == ItemStatus.PENDING && !string.IsNullOrWhiteSpace(i.ProductName) && i.ProductName.Trim() != "-")));
    }

    /// <summary>
    /// Active orders = orders that are not Completed and not Cancelled.
    /// </summary>
    public static int ActiveOrdersCount(IEnumerable<Order>? orders)
    {
        if (orders is null) return 0;
        return orders.Count(o => o.Status != Models.OrderStatus.CANCELLED && o.Status != Models.OrderStatus.COMPLETED);
    }

    /// <summary>
    /// Outstanding balance = sum of balance due over ALL non-cancelled orders (including Completed ones).
    /// </summary>
    public static decimal OutstandingBalanceSum(IEnumerable<Order>? orders, IEnumerable<PasabuyRateTier>? feeTiers = null)
    {
        if (orders is null) return 0m;
        decimal sum = 0m;
        foreach (var o in orders)
        {
            if (o.Status != Models.OrderStatus.CANCELLED)
            {
                sum += BalanceDue(o, feeTiers);
            }
        }
        return RoundMoney(sum);
    }

    // Alias for backward compatibility
    public static decimal OutstandingBalance(IEnumerable<Order>? orders, IEnumerable<PasabuyRateTier>? feeTiers = null)
        => OutstandingBalanceSum(orders, feeTiers);

    /// <summary>
    /// Estimated capital (Shopping) = sum of ItemAmount (qty x unit price) over Pending items only, in the current view.
    /// </summary>
    public static decimal EstimatedCapital(IEnumerable<OrderItem>? items)
    {
        if (items is null) return 0m;
        decimal sum = 0m;
        foreach (var item in items)
        {
            if (item.Status == ItemStatus.PENDING && !string.IsNullOrWhiteSpace(item.ProductName) && item.ProductName.Trim() != "-")
            {
                var price = ResolveItemUnitPrice(item);
                sum += ItemAmount(item.Qty, price);
            }
        }
        return RoundMoney(sum);
    }

    /// <summary>
    /// ShoppingSummary: returns (POsCount, PendingItemsCount, EstimatedCapital) for the given orders / items.
    /// Estimated capital is sum of ItemAmount (qty x unit price) of Pending items in the current view.
    /// </summary>
    public static (int OrdersCount, int PendingItemsCount, decimal EstimatedCapital) ShoppingSummary(
        IEnumerable<Order>? orders,
        IEnumerable<OrderItem>? pendingItems = null)
    {
        var nonCancelledOrders = (orders ?? Enumerable.Empty<Order>())
            .Where(o => o.Status != Models.OrderStatus.CANCELLED)
            .ToList();

        var ordersWaiting = nonCancelledOrders
            .Count(o => o.Items != null && o.Items.Any(i => i.Status == ItemStatus.PENDING && !string.IsNullOrWhiteSpace(i.ProductName) && i.ProductName.Trim() != "-"));

        var itemsList = pendingItems?.ToList()
            ?? nonCancelledOrders.SelectMany(o => o.Items ?? Enumerable.Empty<OrderItem>())
                                 .Where(i => i.Status == ItemStatus.PENDING && !string.IsNullOrWhiteSpace(i.ProductName) && i.ProductName.Trim() != "-")
                                 .ToList();

        var estCapital = EstimatedCapital(itemsList);
        return (ordersWaiting, itemsList.Count, estCapital);
    }

    /// <summary>
    /// Purchase budget (Reports) = sum of counted-items subtotals of non-cancelled orders.
    /// Uses OrderSubtotal with no separate math.
    /// </summary>
    public static decimal PurchaseBudgetSum(
        IEnumerable<Order>? orders,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        if (orders is null) return 0m;
        var filtered = orders.Where(o => o.Status != Models.OrderStatus.CANCELLED);
        if (startDate.HasValue)
            filtered = filtered.Where(o => o.CreatedAt >= startDate.Value);
        if (endDate.HasValue)
            filtered = filtered.Where(o => o.CreatedAt <= endDate.Value);

        decimal sum = 0m;
        foreach (var o in filtered)
        {
            sum += OrderSubtotal(o.Items);
        }
        return RoundMoney(sum);
    }

    public static decimal PurchaseBudget(IEnumerable<Order>? orders)
        => PurchaseBudgetSum(orders);

    /// <summary>
    /// Pasabuy fees (Reports) = sum of PasabuyFee of non-cancelled orders.
    /// Uses PasabuyFee with no separate math.
    /// </summary>
    public static decimal PasabuyFeesSum(
        IEnumerable<Order>? orders,
        DateTime? startDate = null,
        DateTime? endDate = null,
        IEnumerable<PasabuyRateTier>? feeTiers = null)
    {
        if (orders is null) return 0m;
        var filtered = orders.Where(o => o.Status != Models.OrderStatus.CANCELLED);
        if (startDate.HasValue)
            filtered = filtered.Where(o => o.CreatedAt >= startDate.Value);
        if (endDate.HasValue)
            filtered = filtered.Where(o => o.CreatedAt <= endDate.Value);

        decimal sum = 0m;
        foreach (var o in filtered)
        {
            sum += PasabuyFee(o, feeTiers);
        }
        return RoundMoney(sum);
    }

    public static decimal PasabuyFees(IEnumerable<Order>? orders, IEnumerable<PasabuyRateTier>? feeTiers = null)
        => PasabuyFeesSum(orders, null, null, feeTiers);
}
