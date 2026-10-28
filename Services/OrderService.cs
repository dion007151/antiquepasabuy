using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using Microsoft.EntityFrameworkCore;

namespace AntiquePasabuyPOS.Services;

public class OrderService(AppDbContext db)
{
    public static readonly Dictionary<OrderStatus, List<OrderStatus>> ValidTransitions = new()
    {
        [OrderStatus.WAITING_CONFIRMATION] = [OrderStatus.READY_TO_BUY, OrderStatus.BUYING_NOW, OrderStatus.BOUGHT, OrderStatus.COMPLETED, OrderStatus.CANCELLED],
        [OrderStatus.READY_TO_BUY]         = [OrderStatus.WAITING_CONFIRMATION, OrderStatus.BUYING_NOW, OrderStatus.BOUGHT, OrderStatus.COMPLETED, OrderStatus.CANCELLED],
        [OrderStatus.BUYING_NOW]           = [OrderStatus.READY_TO_BUY, OrderStatus.WAITING_CONFIRMATION, OrderStatus.BOUGHT, OrderStatus.COMPLETED, OrderStatus.CANCELLED],
        [OrderStatus.BOUGHT]               = [OrderStatus.BUYING_NOW, OrderStatus.COMPLETED, OrderStatus.CANCELLED],
        [OrderStatus.COMPLETED]            = [OrderStatus.CANCELLED],
        [OrderStatus.CANCELLED]            = [],
    };

    public List<Order> GetAll(OrderStatus? filter = null, int? limit = null, string? buyingDay = null, string? businessId = null)
    {
        var targetBusiness = businessId ?? (BusinessProfileService.IsAngelitos ? null : "ANTIQUE_PASABUY");
        var q = db.Orders
            .AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.Masterlist)
            .Include(o => o.Items)
            .Include(o => o.Payments)
            .Include(o => o.Delivery)
            .AsQueryable();

        if (!string.IsNullOrEmpty(targetBusiness))
        {
            q = q.Where(o => o.BusinessId == targetBusiness || string.IsNullOrEmpty(o.BusinessId));
        }

        if (filter.HasValue) q = q.Where(o => o.Status == filter.Value);
        if (!string.IsNullOrWhiteSpace(buyingDay) && buyingDay != "All")
            q = q.Where(o => o.BuyingDay == buyingDay);

        q = q.OrderByDescending(o => o.CreatedAt);
        if (limit.HasValue) q = q.Take(limit.Value);
        return q.ToList();
    }

    public Order? GetById(int id)
        => db.Orders.AsNoTracking()
                    .Include(o => o.Customer).Include(o => o.Masterlist).Include(o => o.Items)
                    .Include(o => o.Payments).Include(o => o.Delivery)
                    .FirstOrDefault(o => o.Id == id);

    public Order Create(
        int? customerId,
        List<(string name, double qty, string unit, decimal price, string store, string? notes, ProductFeeType feeType, decimal? manualFee)> items,
        DeliveryType type = DeliveryType.Pickup,
        string? address = null,
        string? notes = null,
        decimal serviceFee = 50m,
        decimal deliveryFee = 0m,
        decimal discount = 0m,
        int? createdBy = null,
        OrderStatus status = OrderStatus.WAITING_CONFIRMATION,
        string buyingDay = "Wednesday",
        string? preferredStore = null,
        string? batchCode = null)
    {
        var no = GetNextOrderNo(batchCode);
        var activeMl = db.Masterlists.FirstOrDefault(m => m.Status == "Active");
        var order = new Order
        {
            BusinessId = BusinessProfileService.ActiveProfile.Id,
            OrderNo = no,
            CustomerId = customerId,
            MasterlistId = activeMl?.Id,
            Status = status,
            DeliveryType = type,
            Address = address,
            Notes = notes,
            BuyingDay = string.IsNullOrWhiteSpace(buyingDay) ? "Wednesday" : buyingDay,
            PreferredStore = preferredStore,
            BatchCode = string.IsNullOrWhiteSpace(batchCode) ? null : batchCode.Trim().ToUpperInvariant(),
            ServiceFee = serviceFee,
            DeliveryFee = deliveryFee,
            Discount = discount,
            CreatedBy = createdBy,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        db.Orders.Add(order);
        db.SaveChanges();

        foreach (var (name, qty, unit, price, store, itemNotes, itemFeeType, itemManualFee) in items)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Trim() == "-" || qty <= 0) continue;
            var cleanName = name.Trim();
            var prodMatch = db.Products.AsNoTracking().FirstOrDefault(p => p.Name.ToLower() == cleanName.ToLower());
            var cost = prodMatch?.CostPrice;

            var finalFeeType = itemFeeType != ProductFeeType.STANDARD
                ? itemFeeType
                : (prodMatch?.FeeType ?? ProductFeeType.STANDARD);

            var finalManualFee = itemManualFee.HasValue
                ? itemManualFee
                : prodMatch?.ManualFeeAmount;

            var cleanStore = string.IsNullOrWhiteSpace(store) ? "General" : store.Trim();
            var isFromStock = (prodMatch != null && prodMatch.IsStockItem)
                           || string.Equals(cleanStore, "On hand", StringComparison.OrdinalIgnoreCase);

            if (isFromStock && cleanStore == "General")
                cleanStore = "On hand";

            var orderItem = new OrderItem
            {
                OrderId = order.Id,
                ProductName = cleanName,
                Qty = qty,
                Unit = string.IsNullOrWhiteSpace(unit) ? "pc" : unit.Trim(),
                EstimatedPrice = price,
                ActualPrice = isFromStock ? price : null,
                PurchasedQty = isFromStock ? qty : null,
                PurchaseDate = isFromStock ? DateTime.Now : null,
                CostPrice = cost,
                Store = cleanStore,
                Notes = itemNotes,
                Status = isFromStock ? ItemStatus.BOUGHT_FROM_STOCK : ItemStatus.Pending,
                FeeType = finalFeeType,
                ManualFeeAmount = finalManualFee,
                IsFeeExempt = finalFeeType == ProductFeeType.NONE,
                Source = ItemSource.STORE
            };
            order.Items.Add(orderItem);
            db.OrderItems.Add(orderItem);
        }
        db.SaveChanges();

        // Immediately deduct inventory for all items fulfilled from on-hand stock
        var stockSvc = new StockService(db);
        foreach (var item in order.Items.Where(i => i.Status == ItemStatus.BOUGHT_FROM_STOCK))
        {
            stockSvc.RecordSaleFromStock(item.ProductName, item.Qty, order.OrderNo, item.Id);
        }

        // Advance order progress if stock items were fulfilled
        if (order.Items.Any(i => i.Status == ItemStatus.BOUGHT_FROM_STOCK))
        {
            new BuyingStateService(db).EvaluateOrderBuyingProgress(order.Id);
        }

        OrderEventBus.NotifyOrderCreated(order.Id);
        OrderEventBus.NotifyDataRefreshed();

        return order;
    }

    public Order Create(
        int? customerId,
        List<(string name, double qty, string unit, decimal price, string store, string? notes)> items,
        DeliveryType type = DeliveryType.Pickup,
        string? address = null,
        string? notes = null,
        decimal serviceFee = 50m,
        decimal deliveryFee = 0m,
        decimal discount = 0m,
        int? createdBy = null,
        OrderStatus status = OrderStatus.WAITING_CONFIRMATION,
        string buyingDay = "Wednesday",
        string? preferredStore = null,
        string? batchCode = null)
    {
        var itemsWithFees = items.Select(i => (i.name, i.qty, i.unit, i.price, i.store, i.notes, ProductFeeType.STANDARD, (decimal?)null)).ToList();
        return Create(customerId, itemsWithFees, type, address, notes, serviceFee, deliveryFee, discount, createdBy, status, buyingDay, preferredStore, batchCode);
    }

    public Order Create(
        int? customerId,
        List<(string name, double qty, string unit, decimal price, string store)> items,
        DeliveryType type = DeliveryType.Pickup,
        string? address = null,
        string? notes = null,
        decimal serviceFee = 50m,
        decimal deliveryFee = 0m,
        decimal discount = 0m,
        int? createdBy = null,
        OrderStatus status = OrderStatus.WAITING_CONFIRMATION,
        string buyingDay = "Wednesday",
        string? preferredStore = null,
        string? batchCode = null)
    {
        var mapped = items.Select(i => (i.name, i.qty, i.unit, i.price, i.store, (string?)null, ProductFeeType.STANDARD, (decimal?)null)).ToList();
        return Create(customerId, mapped, type, address, notes, serviceFee, deliveryFee, discount, createdBy, status, buyingDay, preferredStore, batchCode);
    }

    // Overload for backward compatibility with 4-tuple items
    public Order Create(
        int? customerId,
        List<(string name, double qty, string unit, decimal price)> items,
        DeliveryType type = DeliveryType.Pickup,
        string? address = null,
        string? notes = null,
        decimal serviceFee = 50m,
        decimal deliveryFee = 0m,
        decimal discount = 0m,
        int? createdBy = null,
        OrderStatus status = OrderStatus.Pending)
    {
        var mapped = items.Select(i => (i.name, i.qty, i.unit, i.price, "General")).ToList();
        return Create(customerId, mapped, type, address, notes, serviceFee, deliveryFee, discount, createdBy, status);
    }

    public (bool ok, string msg) Transition(int orderId, OrderStatus newStatus, int? userId = null)
    {
        var svc = new OrderStatusService(db);
        return svc.Transition(orderId, newStatus, userId);
    }

    public (bool ok, string msg) AdvanceSmartAction(int orderId, int? userId = null)
    {
        var o = db.Orders.AsNoTracking().Include(x => x.Delivery).FirstOrDefault(x => x.Id == orderId);
        if (o is null) return (false, "Order not found.");
        var info = NextActionService.GetNextAction(o);
        if (!info.TargetStatus.HasValue)
        {
            return info.ActionKey switch
            {
                "collect_payment" => (false,
                    $"Please record payment for the remaining balance of ₱{o.Balance:N2} before completing this order.\n\nUse the 'Mark As Paid' button to record payment."),
                "view" when o.Status == OrderStatus.COMPLETED => (true,
                    $"Order {o.OrderNo} is already completed."),
                "view" when o.Status == OrderStatus.CANCELLED => (false,
                    $"Order {o.OrderNo} is cancelled and cannot be advanced."),
                _ => (false, "This order has no further actions required.")
            };
        }

        var statusSvc = new OrderStatusService(db);
        return statusSvc.Transition(orderId, info.TargetStatus.Value, userId, $"Advanced via '{info.Label}'");
    }

    public (bool ok, string msg) ExecuteNextAction(int orderId, int? userId = null) => AdvanceSmartAction(orderId, userId);

    public (bool ok, string msg) Cancel(int orderId, string? reason = null, int? userId = null)
    {
        var statusSvc = new OrderStatusService(db);
        return statusSvc.Transition(orderId, OrderStatus.CANCELLED, userId, reason ?? "User requested cancellation");
    }

    public (bool ok, string msg) Update(int orderId, int? customerId, DeliveryType type,
        string? address, string? notes, decimal serviceFee, decimal deliveryFee, decimal discount,
        string? buyingDay = null, string? preferredStore = null)
    {
        var o = db.Orders.Find(orderId);
        if (o is null) return (false, "Order not found.");
        o.CustomerId = customerId;
        o.DeliveryType = type;
        o.Address = address;
        o.Notes = notes;
        o.ServiceFee = serviceFee;
        o.DeliveryFee = deliveryFee;
        o.Discount = discount;
        if (!string.IsNullOrWhiteSpace(buyingDay)) o.BuyingDay = buyingDay;
        if (preferredStore != null) o.PreferredStore = preferredStore;
        o.UpdatedAt = DateTime.Now;
        db.SaveChanges();
        return (true, "Order updated.");
    }

    public (bool ok, string msg) Update(int orderId, int? customerId, DeliveryType type,
        string? address, string? notes, decimal serviceFee, decimal deliveryFee, decimal discount)
        => Update(orderId, customerId, type, address, notes, serviceFee, deliveryFee, discount, null, null);

    // ── BUYING LIST / PURCHASING ACTIONS ──────────────────────────────────────────

    public List<OrderItem> GetBuyingItems(string? storeFilter = null, ItemStatus? statusFilter = null, string? buyingDayFilter = null)
    {
        var q = db.OrderItems
            .AsNoTracking()
            .Include(i => i.Order)
                .ThenInclude(o => o!.Customer)
            .Include(i => i.Order)
                .ThenInclude(o => o!.Masterlist)
            .Where(i => i.Order != null && (i.Order.BusinessId == "ANTIQUE_PASABUY" || string.IsNullOrEmpty(i.Order.BusinessId))
                        && i.Order.Status != OrderStatus.CANCELLED
                        && i.Status != ItemStatus.Cancelled
                        && !string.IsNullOrWhiteSpace(i.ProductName)
                        && i.ProductName.Trim() != "-")
            .AsQueryable();

        if (statusFilter.HasValue)
        {
            q = q.Where(i => i.Status == statusFilter.Value);
            // When filtering for Pending items to buy, strictly exclude already BOUGHT or COMPLETED orders
            if (statusFilter.Value == ItemStatus.Pending)
            {
                q = q.Where(i => i.Order!.Status == OrderStatus.WAITING_CONFIRMATION
                              || i.Order!.Status == OrderStatus.READY_TO_BUY
                              || i.Order!.Status == OrderStatus.BUYING_NOW);
            }
        }
        else
        {
            // Default shopping checklist view: only pending items from orders currently waiting to buy or buying
            q = q.Where(i => i.Status == ItemStatus.Pending
                          && (i.Order!.Status == OrderStatus.WAITING_CONFIRMATION
                              || i.Order!.Status == OrderStatus.READY_TO_BUY
                              || i.Order!.Status == OrderStatus.BUYING_NOW));
        }

        if (!string.IsNullOrWhiteSpace(storeFilter) && storeFilter != "All Stores")
            q = q.Where(i => i.Store == storeFilter);

        if (!string.IsNullOrWhiteSpace(buyingDayFilter) && buyingDayFilter != "All Days")
            q = q.Where(i => i.Order!.BuyingDay == buyingDayFilter);

        return q.OrderBy(i => i.Store).ThenBy(i => i.ProductName).ToList();
    }

    public (bool ok, string msg) MarkItemPurchased(int itemId, double purchasedQty, decimal actualPrice, string? receiptNo = null, string? store = null, string? notes = null)
    {
        var svc = new BuyingStateService(db);
        return svc.MarkItemPurchased(itemId, purchasedQty, actualPrice, receiptNo, store, notes);
    }

    public (bool ok, string msg) MarkItemUnavailable(int itemId, string reason, string? notes = null)
    {
        var svc = new BuyingStateService(db);
        return svc.MarkItemUnavailable(itemId, reason, notes);
    }

    public (bool ok, string msg) MarkItemBoughtFromStock(int itemId, decimal? actualPrice = null)
    {
        var svc = new BuyingStateService(db);
        return svc.MarkItemBoughtFromStock(itemId, actualPrice);
    }

    public (bool ok, string msg) ResetItemToPending(int itemId)
    {
        var svc = new BuyingStateService(db);
        return svc.ResetItemToPending(itemId);
    }

    public PasabuyDashboardStats GetPasabuyStats()
    {
        var today = DateTime.Today;

        // Fetch non-cancelled orders with their items and payments to compute accurate stats using Calc
        var orders = db.Orders.AsNoTracking()
            .Where(o => (o.BusinessId == "ANTIQUE_PASABUY" || string.IsNullOrEmpty(o.BusinessId))
                        && o.Status != OrderStatus.CANCELLED)
            .Include(o => o.Items)
            .Include(o => o.Payments)
            .ToList();

        var todaySales = db.Payments.AsNoTracking()
            .Where(p => p.CreatedAt >= today && (p.Order == null || p.Order.BusinessId == "ANTIQUE_PASABUY" || string.IsNullOrEmpty(p.Order.BusinessId)))
            .Select(p => p.Amount)
            .ToList()
            .Sum();

        var todayDateOnly = DateOnly.FromDateTime(today);
        var todayExp = db.Expenses.AsNoTracking()
            .Where(e => e.ExpenseDate == todayDateOnly)
            .Select(e => e.Amount)
            .ToList()
            .Sum();

        // Single Source of Truth: Calc calculations
        var totalOrdersCount       = orders.Count;
        var todayOrdersCount       = orders.Count(o => o.CreatedAt >= today);
        var forBuyingOrdersCount   = Calc.OrdersWaitingToBuyCount(orders);
        var boughtNotFinishedCount = Calc.OrdersBoughtNotFinishedCount(orders);
        var activeOrdersCount      = Calc.ActiveOrdersCount(orders);
        var totalItemsToBuy        = Calc.ItemsToBuyCount(orders);
        var totalUnpaidBalance     = Calc.OutstandingBalanceSum(orders);

        var unpaidOrdersCount = orders.Count(o => Calc.BalanceDue(o) > 0m);
        var completedOrdersCount = orders.Count(o => o.Status == OrderStatus.COMPLETED);
        var cancelledOrdersCount = db.Orders.AsNoTracking().Count(o => o.Status == OrderStatus.CANCELLED && (o.BusinessId == "ANTIQUE_PASABUY" || string.IsNullOrEmpty(o.BusinessId)));

        var forDeliveryCount = orders.Count(o => o.DeliveryType == DeliveryType.DELIVERY && o.Status != OrderStatus.COMPLETED);

        var activeItems = orders
            .SelectMany(o => o.Items ?? Enumerable.Empty<OrderItem>())
            .Where(i => !string.IsNullOrWhiteSpace(i.ProductName) && i.ProductName.Trim() != "-")
            .ToList();

        var purchasedItemsCount = activeItems.Count(i => i.Status == ItemStatus.PURCHASED || i.Status == ItemStatus.BOUGHT_FROM_STOCK);
        var unavailItemsCount   = activeItems.Count(i => i.Status == ItemStatus.UNAVAILABLE);
        var totalItemsToPurchase = activeItems.Count;

        var todayBatchesCount = activeItems
            .Where(i => i.Status == ItemStatus.PENDING)
            .Select(i => string.IsNullOrWhiteSpace(i.Store) ? "General" : i.Store)
            .Distinct()
            .Count();

        var allUnavailCount = db.OrderItems.AsNoTracking()
            .Count(i => i.Status == ItemStatus.UNAVAILABLE && i.Order != null && (i.Order.BusinessId == "ANTIQUE_PASABUY" || string.IsNullOrEmpty(i.Order.BusinessId)));

        // Schedule
        var scheduleStr = SeedData.GetSetting(db, "buying_schedule_days", "Wednesday, Thursday");
        var nextDay = ComputeNextBuyingDay(scheduleStr);

        return new PasabuyDashboardStats
        {
            NextBuyingDay             = nextDay,
            TotalSalesToday           = todaySales,
            TodayOrdersCount          = todayOrdersCount,
            PendingOrdersCount        = forBuyingOrdersCount,
            ForBuyingOrdersCount      = forBuyingOrdersCount,
            BuyingOrdersCount         = 0,
            PartialOrdersCount        = 0,
            BoughtOrdersCount         = boughtNotFinishedCount,
            CompletedOrdersCount      = completedOrdersCount,
            UnavailableOrdersCount    = 0,
            CancelledOrdersCount      = cancelledOrdersCount,
            TodayBuyingBatchesCount   = todayBatchesCount,
            TotalItemsToBuy           = totalItemsToBuy,
            AllUnavailableItemsCount  = allUnavailCount,
            OrdersToBuyCount          = forBuyingOrdersCount,
            PurchasedOrdersCount      = completedOrdersCount,
            ForDeliveryCount          = forDeliveryCount,
            ReadyForPickupCount       = 0,
            TotalItemsToPurchase      = totalItemsToPurchase,
            PurchasedItemsCount       = purchasedItemsCount,
            PendingItemsCount         = totalItemsToBuy,
            UnavailableItemsCount     = unavailItemsCount,
            UnpaidOrdersCount         = unpaidOrdersCount,
            TotalUnpaidBalance        = totalUnpaidBalance,
            ActiveOrdersCount         = activeOrdersCount,
            BoughtNotFinishedCount    = boughtNotFinishedCount,
            ItemsNeedingPurchaseCount = totalItemsToBuy,
            ExpensesToday             = todayExp,
            TotalOrdersCount          = totalOrdersCount
        };
    }

    public DashboardStats GetTodayStats()
    {
        var s = GetPasabuyStats();
        return new DashboardStats
        {
            TotalSales    = s.TotalSalesToday,
            OrderCount    = s.TodayOrdersCount,
            PendingCount  = s.PendingOrdersCount,
            ShoppingCount = s.OrdersToBuyCount,
            Expenses      = s.ExpensesToday
        };
    }

    public bool Delete(int id)
    {
        var order = db.Orders.Include(o => o.Items).Include(o => o.Payments).Include(o => o.Delivery).FirstOrDefault(o => o.Id == id);
        if (order is null) return false;
        db.Orders.Remove(order);
        db.SaveChanges();
        return true;
    }

    public int DeleteBulk(IEnumerable<int> ids)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return 0;
        var orders = db.Orders.Include(o => o.Items).Include(o => o.Payments).Include(o => o.Delivery)
                              .Where(o => list.Contains(o.Id)).ToList();
        if (orders.Count == 0) return 0;
        db.Orders.RemoveRange(orders);
        db.SaveChanges();
        return orders.Count;
    }

    public static string ComputeNextBuyingDay(string scheduleDays = "Wednesday, Thursday")
    {
        var days = scheduleDays.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => d.Trim())
            .ToList();

        if (days.Count == 0) return "Wednesday / Thursday";

        var today = DateTime.Today.DayOfWeek;
        // Map day names to DayOfWeek
        var dayOfWeeks = new List<DayOfWeek>();
        foreach (var d in days)
        {
            if (Enum.TryParse<DayOfWeek>(d, true, out var dow))
                dayOfWeeks.Add(dow);
        }

        if (dayOfWeeks.Count == 0) return string.Join(" / ", days);

        // Find the next upcoming day in the schedule
        for (int i = 0; i <= 7; i++)
        {
            var checkDay = today + i;
            if ((int)checkDay >= 7) checkDay -= 7;
            if (dayOfWeeks.Contains(checkDay))
            {
                if (i == 0) return $"Today ({checkDay})";
                if (i == 1) return $"Tomorrow ({checkDay})";
                return checkDay.ToString();
            }
        }

        return string.Join(" / ", days);
    }

    private string GetNextOrderNo(string? batchCode = null)
    {
        // Batch-style numbering: <batch>-001, <batch>-002, …
        // Uses the MAX existing sequence for the batch to be safe against
        // out-of-order inserts or gaps created by cancelled orders.
        if (!string.IsNullOrWhiteSpace(batchCode))
        {
            var batch = batchCode.Trim().ToUpperInvariant();

            // Collect all OrderNo values for this batch and find the highest sequence.
            var allInBatch = db.Orders
                .Where(o => o.BatchCode == batch)
                .Select(o => o.OrderNo)
                .ToList();

            if (allInBatch.Count == 0)
                return $"{batch}-001";

            int maxSeq = 0;
            foreach (var no in allInBatch)
            {
                var parts = no.Split('-');
                if (parts.Length >= 2 && int.TryParse(parts[^1], out int seq) && seq > maxSeq)
                    maxSeq = seq;
            }

            return $"{batch}-{maxSeq + 1:D3}";
        }

        // Automatic PO number: PO-000001, PO-000002, ...
        var last = db.Orders.OrderByDescending(o => o.Id).FirstOrDefault();
        if (last is null) return "PO-000001";
        if (last.OrderNo.StartsWith("PO-") && int.TryParse(last.OrderNo[3..], out int n))
            return $"PO-{n + 1:D6}";
        if (last.OrderNo.StartsWith("PB-") && int.TryParse(last.OrderNo[3..], out int m))
            return $"PO-{m + 1:D6}";
        if (last.OrderNo.StartsWith("ORD-"))
        {
            var parts = last.OrderNo.Split('-');
            if (parts.Length >= 2 && int.TryParse(parts[^1], out int p))
                return $"PO-{p + 1:D6}";
        }
        return $"PO-{last.Id + 1:D6}";
    }
}

public record PasabuyDashboardStats
{
    public string  NextBuyingDay          { get; init; } = "Wednesday";
    public decimal TotalSalesToday        { get; init; }
    public int     TodayOrdersCount       { get; init; }
    public int     TotalOrdersCount       { get; init; }
    public int     PendingOrdersCount     { get; init; }
    public int     ForBuyingOrdersCount   { get; init; }
    public int     BuyingOrdersCount      { get; init; }
    public int     PartialOrdersCount     { get; init; }
    public int     BoughtOrdersCount      { get; init; }
    public int     CompletedOrdersCount   { get; init; }
    public int     UnavailableOrdersCount { get; init; }
    public int     CancelledOrdersCount   { get; init; }
    public int     TodayBuyingBatchesCount { get; init; }
    public int     TotalItemsToBuy        { get; init; }
    public int     AllUnavailableItemsCount { get; init; }
    public int     OrdersToBuyCount       { get; init; }
    public int     PurchasedOrdersCount   { get; init; }
    public int     ForDeliveryCount       { get; init; }
    public int     ReadyForPickupCount    { get; init; }
    public int     TotalItemsToPurchase   { get; init; }
    public int     PurchasedItemsCount    { get; init; }
    public int     PendingItemsCount      { get; init; }
    public int     UnavailableItemsCount  { get; init; }
    public int     UnpaidOrdersCount      { get; init; }
    public decimal TotalUnpaidBalance     { get; init; }
    public int     ActiveOrdersCount      { get; init; }
    public int     BoughtNotFinishedCount { get; init; }
    public int     ItemsNeedingPurchaseCount { get; init; }
    public decimal ExpensesToday          { get; init; }
    public decimal NetProfitToday         => TotalSalesToday - ExpensesToday;
}

public record DashboardStats
{
    public decimal TotalSales    { get; init; }
    public int     OrderCount    { get; init; }
    public int     PendingCount  { get; init; }
    public int     ShoppingCount { get; init; }
    public decimal Expenses      { get; init; }
    public decimal NetProfit     => TotalSales - Expenses;
}
