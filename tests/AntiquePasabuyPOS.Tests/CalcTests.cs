using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;

namespace AntiquePasabuyPOS.Tests;

public class CalcTests
{
    // ── 1. Fee on Subtotal (Excel Approximate Match Lookup: Min Amount Lower Bound) ────
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.5, 50)]
    [InlineData(99, 50)]
    [InlineData(99.5, 50)]
    [InlineData(100, 75)]
    [InlineData(139, 75)]
    [InlineData(199.99, 75)]
    [InlineData(200, 100)]
    [InlineData(270, 100)]
    [InlineData(299.5, 100)]
    [InlineData(327, 150)]
    [InlineData(999.5, 150)]
    [InlineData(1395, 250)]
    [InlineData(1499.5, 250)]
    [InlineData(1878.5, 300)]
    [InlineData(1999.5, 300)]
    [InlineData(2269.5, 350)]
    [InlineData(2499.5, 350)]
    [InlineData(2500, 375)]
    [InlineData(2995, 449.25)]
    public void PasabuyFee_LowerBoundExcelLookup_ReturnsExpectedFee(double subtotalDbl, double expectedFeeDbl)
    {
        var subtotal = (decimal)subtotalDbl;
        var expectedFee = (decimal)expectedFeeDbl;

        var fee = Calc.PasabuyFee(subtotal);

        Assert.Equal(expectedFee, fee);
    }

    // ── 2. Decimals in Quantity and Price ──────────────────────────────────────────────
    [Fact]
    public void ItemAmount_DecimalsInQtyAndPrice_ComputesAndRoundsCorrectly()
    {
        // 0.5 x 1995 = 997.50
        var amount1 = Calc.ItemAmount(0.5, 1995m);
        Assert.Equal(997.50m, amount1);

        // 18.75 x 20 = 375.00
        var amount2 = Calc.ItemAmount(20, 18.75m);
        Assert.Equal(375.00m, amount2);
    }

    // ── 3. One Item Not Available: Recalculates Subtotal, Fee, Total ───────────────────
    [Fact]
    public void Order_WhenItemMarkedUnavailable_RecalculatesSubtotalFeeAndTotal()
    {
        var item1 = new OrderItem { ProductName = "Item 1", Qty = 1, EstimatedPrice = 140m, ActualPrice = 140m, Status = ItemStatus.PURCHASED };
        var item2 = new OrderItem { ProductName = "Item 2", Qty = 1, EstimatedPrice = 135m, Status = ItemStatus.PENDING };
        var items = new List<OrderItem> { item1, item2 };

        // Before: 140 + 135 = 275 subtotal -> fee 100 -> total 375
        var subtotalBefore = Calc.OrderSubtotal(items);
        var feeBefore = Calc.PasabuyFee(subtotalBefore);
        var totalBefore = Calc.OrderTotal(subtotalBefore, feeBefore);
        Assert.Equal(275m, subtotalBefore);
        Assert.Equal(100m, feeBefore);
        Assert.Equal(375m, totalBefore);

        // After: Item 2 marked UNAVAILABLE
        item2.Status = ItemStatus.UNAVAILABLE;
        var subtotalAfter = Calc.OrderSubtotal(items);
        var feeAfter = Calc.PasabuyFee(subtotalAfter);
        var totalAfter = Calc.OrderTotal(subtotalAfter, feeAfter);

        Assert.Equal(140m, subtotalAfter);
        Assert.Equal(75m, feeAfter);
        Assert.Equal(215m, totalAfter);
    }

    // ── 4. All Items Not Available or Reorder: Subtotal 0, Fee 0, Total 0 ──────────────
    [Fact]
    public void Order_WhenAllItemsUnavailableOrReorder_YieldsAllZeroes()
    {
        var items = new List<OrderItem>
        {
            new OrderItem { ProductName = "Item 1", Qty = 2, EstimatedPrice = 500m, Status = ItemStatus.UNAVAILABLE },
            new OrderItem { ProductName = "Item 2", Qty = 1, EstimatedPrice = 250m, Status = ItemStatus.REORDER }
        };

        var subtotal = Calc.OrderSubtotal(items);
        var fee = Calc.PasabuyFee(subtotal);
        var total = Calc.OrderTotal(subtotal, fee);

        Assert.Equal(0m, subtotal);
        Assert.Equal(0m, fee);
        Assert.Equal(0m, total);
    }

    // ── 5. Reorder Item Adds 0 to Subtotal ─────────────────────────────────────────────
    [Fact]
    public void Order_ReorderItem_ContributesZeroToSubtotal()
    {
        var items = new List<OrderItem>
        {
            new OrderItem { ProductName = "Active Item", Qty = 1, EstimatedPrice = 150m, Status = ItemStatus.PENDING },
            new OrderItem { ProductName = "Reorder Item", Qty = 3, EstimatedPrice = 200m, Status = ItemStatus.REORDER }
        };

        var subtotal = Calc.OrderSubtotal(items);
        Assert.Equal(150m, subtotal);
    }

    // ── 6. Fee-Exempt Product Adds Nothing to Fee Subtotal ─────────────────────────────
    [Fact]
    public void Order_FeeExemptItem_ContributesToSubtotalButNotFeeSubtotal()
    {
        var items = new List<OrderItem>
        {
            new OrderItem { ProductName = "Standard Item", Qty = 1, EstimatedPrice = 200m, Status = ItemStatus.PENDING, IsFeeExempt = false },
            new OrderItem { ProductName = "Exempt Item", Qty = 1, EstimatedPrice = 500m, Status = ItemStatus.PENDING, IsFeeExempt = true }
        };

        var orderSubtotal = Calc.OrderSubtotal(items);
        var feeSubtotal = Calc.FeeSubtotal(items);
        var fee = Calc.PasabuyFee(feeSubtotal);

        Assert.Equal(700m, orderSubtotal);
        Assert.Equal(200m, feeSubtotal);
        Assert.Equal(100m, fee); // Fee on 200 is 100, not fee on 700
    }

    // ── 7. Cancelled Order Items Excluded from Counts and Reports ──────────────────────
    [Fact]
    public void Calc_CancelledOrders_ExcludedFromCountsAndReports()
    {
        var order1 = new Order
        {
            Id = 1,
            Status = OrderStatus.WAITING_CONFIRMATION,
            Items = new List<OrderItem>
            {
                new OrderItem { ProductName = "Valid Item", Qty = 1, EstimatedPrice = 100m, Status = ItemStatus.PENDING }
            }
        };

        var cancelledOrder = new Order
        {
            Id = 2,
            Status = OrderStatus.CANCELLED,
            Items = new List<OrderItem>
            {
                new OrderItem { ProductName = "Cancelled Item 1", Qty = 5, EstimatedPrice = 500m, Status = ItemStatus.PENDING },
                new OrderItem { ProductName = "Cancelled Item 2", Qty = 2, EstimatedPrice = 200m, Status = ItemStatus.PURCHASED }
            }
        };

        var orders = new List<Order> { order1, cancelledOrder };

        Assert.Equal(1, Calc.ItemsToBuyCount(orders));
        Assert.Equal(1, Calc.OrdersWaitingToBuyCount(orders));
        Assert.Equal(1, Calc.ActiveOrdersCount(orders));
        Assert.Equal(100m, Calc.PurchaseBudgetSum(orders));
        Assert.Equal(75m, Calc.PasabuyFeesSum(orders));
    }

    // ── 8. Fee Override Note Validation & Adjusted Flag ────────────────────────────────
    [Fact]
    public void PasabuyFee_ManualOverride_RequiresNoteAndSetsAdjustedFlag()
    {
        // Empty or null note must throw ArgumentException
        Assert.Throws<ArgumentException>(() =>
        {
            Calc.PasabuyFee(500m, null, 120m, "", out _);
        });

        Assert.Throws<ArgumentException>(() =>
        {
            Calc.PasabuyFee(500m, null, 120m, "   ", out _);
        });

        // Valid override replaces fee and sets isAdjusted = true
        var fee = Calc.PasabuyFee(500m, null, 120m, "Special customer discount applied", out bool isAdjusted);
        Assert.Equal(120m, fee);
        Assert.True(isAdjusted);
    }

    // ── 9. Payment Status, Balance Due, Overpayment, and Zero Total ───────────────────
    [Fact]
    public void PaymentStatus_And_Balance_CalculatesAccurately()
    {
        // Partial Payment: total 500, paid 200 -> balance 300, status PARTIAL
        Assert.Equal(300m, Calc.BalanceDue(500m, 200m));
        Assert.Equal(0m, Calc.OverpaidAmount(500m, 200m));
        Assert.Equal(PaymentStatus.PARTIAL, Calc.PaymentStatus(500m, 200m));

        // Full Payment: total 500, paid 500 -> balance 0, status PAID
        Assert.Equal(0m, Calc.BalanceDue(500m, 500m));
        Assert.Equal(0m, Calc.OverpaidAmount(500m, 500m));
        Assert.Equal(PaymentStatus.PAID, Calc.PaymentStatus(500m, 500m));

        // Overpayment: total 500, paid 650 -> balance 0, overpaid 150, status PAID
        Assert.Equal(0m, Calc.BalanceDue(500m, 650m));
        Assert.Equal(150m, Calc.OverpaidAmount(500m, 650m));
        Assert.Equal(PaymentStatus.PAID, Calc.PaymentStatus(500m, 650m));

        // Zero-Total Order: not shown as Unpaid -> status PAID, balance 0
        Assert.Equal(0m, Calc.BalanceDue(0m, 0m));
        Assert.Equal(PaymentStatus.PAID, Calc.PaymentStatus(0m, 0m));
    }

    // ── 10. Rule 7 Counts on Small Sample Dataset with Known Answers ──────────────────
    [Fact]
    public void Rule7_SampleDataset_ProducesExactKnownCounts()
    {
        // Setup 4 orders:
        // Order 1: WAITING_CONFIRMATION with 2 pending items
        var o1 = new Order
        {
            Id = 1,
            Status = OrderStatus.WAITING_CONFIRMATION,
            Items = new List<OrderItem>
            {
                new OrderItem { ProductName = "Item A", Qty = 1, EstimatedPrice = 100m, Status = ItemStatus.PENDING },
                new OrderItem { ProductName = "Item B", Qty = 1, EstimatedPrice = 200m, Status = ItemStatus.PENDING }
            },
            Payments = new List<Payment> { new Payment { Amount = 100m } }
        };

        // Order 2: BOUGHT with 1 purchased item (not completed)
        var o2 = new Order
        {
            Id = 2,
            Status = OrderStatus.BOUGHT,
            Items = new List<OrderItem>
            {
                new OrderItem { ProductName = "Item C", Qty = 1, EstimatedPrice = 150m, ActualPrice = 150m, Status = ItemStatus.PURCHASED }
            },
            Payments = new List<Payment>()
        };

        // Order 3: COMPLETED with 1 purchased item
        var o3 = new Order
        {
            Id = 3,
            Status = OrderStatus.COMPLETED,
            Items = new List<OrderItem>
            {
                new OrderItem { ProductName = "Item D", Qty = 1, EstimatedPrice = 300m, ActualPrice = 300m, Status = ItemStatus.PURCHASED }
            },
            Payments = new List<Payment> { new Payment { Amount = 450m } }
        };

        // Order 4: CANCELLED
        var o4 = new Order
        {
            Id = 4,
            Status = OrderStatus.CANCELLED,
            Items = new List<OrderItem>
            {
                new OrderItem { ProductName = "Item E", Qty = 1, EstimatedPrice = 500m, Status = ItemStatus.PENDING }
            }
        };

        var orders = new List<Order> { o1, o2, o3, o4 };

        // Test every Rule 7 count:
        Assert.Equal(2, Calc.ItemsToBuyCount(orders));               // Item A and Item B
        Assert.Equal(1, Calc.OrdersWaitingToBuyCount(orders));       // Order 1
        Assert.Equal(1, Calc.OrdersBoughtNotFinishedCount(orders));   // Order 2
        Assert.Equal(2, Calc.ActiveOrdersCount(orders));             // Order 1 and Order 2
        
        // Outstanding balance:
        // o1: subtotal 300, fee 150, total 450, paid 100 -> bal 350
        // o2: subtotal 150, fee 75, total 225, paid 0 -> bal 225
        // o3: subtotal 300, fee 150, total 450, paid 450 -> bal 0
        // Sum across all non-cancelled orders = 350 + 225 + 0 = 575
        Assert.Equal(575m, Calc.OutstandingBalanceSum(orders));

        // Shopping summary on pending items:
        var shopping = Calc.ShoppingSummary(orders);
        Assert.Equal(1, shopping.OrdersCount);
        Assert.Equal(2, shopping.PendingItemsCount);
        Assert.Equal(300m, shopping.EstimatedCapital); // 100 + 200
    }

    // ── 11. MidpointRounding.AwayFromZero and 2-Decimal Precision ─────────────────────
    [Fact]
    public void RoundMoney_AwayFromZeroAndMaxTwoDecimals()
    {
        // 0.125 rounds to 0.13 away from zero (default banker's rounding would round to 0.12)
        var rounded = Calc.RoundMoney(0.125m);
        Assert.Equal(0.13m, rounded);

        // Verify other half-up roundings
        Assert.Equal(1.01m, Calc.RoundMoney(1.005m));
        Assert.Equal(1.02m, Calc.RoundMoney(1.015m));
        Assert.Equal(1.03m, Calc.RoundMoney(1.025m));

        // Ensure never more than 2 decimal places in calculations
        var result = Calc.ItemAmount(0.333333, 99.99m);
        Assert.Equal(33.33m, result);
        Assert.Equal(2, BitConverter.GetBytes(decimal.GetBits(result)[3])[2]); // Scale is <= 2
    }
}
