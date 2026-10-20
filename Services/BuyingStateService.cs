using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using Microsoft.EntityFrameworkCore;

namespace AntiquePasabuyPOS.Services;

/// <summary>
/// Service governing item-level buying states, multi-store buying sessions,
/// and automatic order status advancement based on item resolution (Phases 4, 5, 7, 8).
/// </summary>
public class BuyingStateService(AppDbContext db)
{
    /// <summary>
    /// Marks an order item as PURCHASED, records actual price, and advances order state if shopping has begun or finished.
    /// </summary>
    public (bool ok, string msg) MarkItemPurchased(int itemId, double purchasedQty, decimal actualPrice, string? receiptNo = null, string? store = null, string? notes = null, int? userId = null)
    {
        using var tx = db.Database.BeginTransaction();
        try
        {
            var item = db.OrderItems.Include(i => i.Order).FirstOrDefault(i => i.Id == itemId);
            if (item is null) return (false, "Order item not found.");

            item.PurchasedQty = purchasedQty;
            item.ActualPrice = actualPrice;
            item.Status = ItemStatus.PURCHASED;
            item.PurchaseDate = DateTime.Now;
            if (!string.IsNullOrWhiteSpace(receiptNo)) item.ReceiptNo = receiptNo.Trim();
            if (!string.IsNullOrWhiteSpace(store)) item.Store = store.Trim();
            if (!string.IsNullOrWhiteSpace(notes)) item.PurchaseNotes = notes.Trim();

            db.SaveChanges();

            // Evaluate order status based on sibling items
            EvaluateOrderBuyingProgress(item.OrderId);

            tx.Commit();
            OrderEventBus.NotifyItemStatusChanged(itemId, ItemStatus.PURCHASED);
            return (true, $"Marked '{item.ProductName}' as PURCHASED.");
        }
        catch (Exception ex)
        {
            tx.Rollback();
            return (false, $"Error updating item: {ex.Message}");
        }
    }

    /// <summary>
    /// Bulk marks multiple items as PURCHASED in a single atomic transaction.
    /// </summary>
    public (bool ok, string msg, int count) BulkMarkItemsPurchased(List<int> itemIds)
    {
        if (itemIds == null || itemIds.Count == 0) return (true, "No items selected.", 0);

        using var tx = db.Database.BeginTransaction();
        try
        {
            var items = db.OrderItems.Include(i => i.Order).Where(i => itemIds.Contains(i.Id)).ToList();
            if (items.Count == 0) return (false, "No matching items found.", 0);

            var impactedOrderIds = new HashSet<int>();
            foreach (var item in items)
            {
                item.Status = ItemStatus.PURCHASED;
                item.PurchaseDate = DateTime.Now;
                if (!item.ActualPrice.HasValue || item.ActualPrice.Value <= 0)
                    item.ActualPrice = item.EstimatedPrice;
                if (!item.PurchasedQty.HasValue || item.PurchasedQty.Value <= 0)
                    item.PurchasedQty = item.Qty;

                impactedOrderIds.Add(item.OrderId);
            }

            db.SaveChanges();

            foreach (var orderId in impactedOrderIds)
            {
                EvaluateOrderBuyingProgress(orderId);
            }

            tx.Commit();
            return (true, $"Successfully marked {items.Count} items as PURCHASED.", items.Count);
        }
        catch (Exception ex)
        {
            tx.Rollback();
            return (false, $"Error updating items: {ex.Message}", 0);
        }
    }

    /// <summary>
    /// Marks an order item as UNAVAILABLE with a reason.
    /// </summary>
    public (bool ok, string msg) MarkItemUnavailable(int itemId, string reason, string? notes = null, int? userId = null)
    {
        using var tx = db.Database.BeginTransaction();
        try
        {
            var item = db.OrderItems.Include(i => i.Order).FirstOrDefault(i => i.Id == itemId);
            if (item is null) return (false, "Order item not found.");

            item.Status = ItemStatus.UNAVAILABLE;
            item.UnavailableReason = string.IsNullOrWhiteSpace(reason) ? "Out of Stock" : reason.Trim();
            if (!string.IsNullOrWhiteSpace(notes)) item.PurchaseNotes = notes.Trim();
            item.PurchaseDate = DateTime.Now;

            db.SaveChanges();

            // Evaluate order status based on sibling items
            EvaluateOrderBuyingProgress(item.OrderId);

            tx.Commit();
            OrderEventBus.NotifyItemStatusChanged(itemId, ItemStatus.UNAVAILABLE);
            return (true, $"Marked '{item.ProductName}' as UNAVAILABLE ({item.UnavailableReason}).");
        }
        catch (Exception ex)
        {
            tx.Rollback();
            return (false, $"Error updating item: {ex.Message}");
        }
    }

    /// <summary>
    /// Bulk marks multiple items as UNAVAILABLE in a single atomic transaction.
    /// </summary>
    public (bool ok, string msg, int count) BulkMarkItemsUnavailable(List<int> itemIds, string reason = "Out of Stock", string? notes = null)
    {
        if (itemIds == null || itemIds.Count == 0) return (true, "No items selected.", 0);

        using var tx = db.Database.BeginTransaction();
        try
        {
            var items = db.OrderItems.Include(i => i.Order).Where(i => itemIds.Contains(i.Id)).ToList();
            if (items.Count == 0) return (false, "No matching items found.", 0);

            var impactedOrderIds = new HashSet<int>();
            foreach (var item in items)
            {
                item.Status = ItemStatus.UNAVAILABLE;
                item.UnavailableReason = string.IsNullOrWhiteSpace(reason) ? "Out of Stock" : reason.Trim();
                if (!string.IsNullOrWhiteSpace(notes)) item.PurchaseNotes = notes.Trim();
                item.PurchaseDate = DateTime.Now;

                impactedOrderIds.Add(item.OrderId);
            }

            db.SaveChanges();

            foreach (var orderId in impactedOrderIds)
            {
                EvaluateOrderBuyingProgress(orderId);
            }

            tx.Commit();
            return (true, $"Successfully marked {items.Count} items as UNAVAILABLE.", items.Count);
        }
        catch (Exception ex)
        {
            tx.Rollback();
            return (false, $"Error updating items: {ex.Message}", 0);
        }
    }

    /// <summary>
    /// Reverts an item back to PENDING.
    /// </summary>
    public (bool ok, string msg) ResetItemToPending(int itemId)
    {
        using var tx = db.Database.BeginTransaction();
        try
        {
            var item = db.OrderItems.Include(i => i.Order).FirstOrDefault(i => i.Id == itemId);
            if (item is null) return (false, "Order item not found.");

            item.Status = ItemStatus.PENDING;
            item.PurchasedQty = null;
            item.ActualPrice = null;
            item.ReceiptNo = null;
            item.UnavailableReason = null;

            db.SaveChanges();
            EvaluateOrderBuyingProgress(item.OrderId);

            tx.Commit();
            OrderEventBus.NotifyItemStatusChanged(itemId, ItemStatus.PENDING);
            return (true, $"Reset '{item.ProductName}' to PENDING.");
        }
        catch (Exception ex)
        {
            tx.Rollback();
            return (false, $"Error updating item: {ex.Message}");
        }
    }

    /// <summary>
    /// Evaluates all items in an order to determine whether the order should transition.
    /// Canonical 5-status workflow:
    ///   FOR_BUYING → BUYING (any item resolved) → BOUGHT (all items resolved) → COMPLETED (explicit handover)
    /// - If any item is purchased or unavailable and order is FOR_BUYING → BUYING
    /// - If all items are resolved (Purchased or Unavailable) and none Pending → BOUGHT
    ///   (shopper has finished at the store; waiting for customer handover/delivery)
    /// - COMPLETED is set explicitly by the encoder once order is handed over.
    /// </summary>
    public void EvaluateOrderBuyingProgress(int orderId)
    {
        var order = db.Orders.Include(o => o.Items).FirstOrDefault(o => o.Id == orderId);
        if (order is null || order.Status == OrderStatus.CANCELLED || order.Status == OrderStatus.COMPLETED) return;

        var items = order.Items.ToList();
        if (items.Count == 0) return;

        var activeItems = items.Where(i => i.Status != ItemStatus.CANCELLED).ToList();
        if (activeItems.Count == 0) return;

        var purchasedCount = activeItems.Count(i => i.Status == ItemStatus.PURCHASED);
        var unavailCount   = activeItems.Count(i => i.Status == ItemStatus.UNAVAILABLE);
        var pendingCount   = activeItems.Count(i => i.Status == ItemStatus.PENDING);

        OrderStatus targetStatus;

        if (pendingCount == 0 && (purchasedCount > 0 || unavailCount > 0))
        {
            // ── All items resolved (some purchased, some unavailable, none pending) ──
            // Transition to BOUGHT: shopper has finished shopping, items collected.
            // The encoder will manually mark as COMPLETED once order is handed over.
            targetStatus = OrderStatus.BOUGHT;
        }
        else if (purchasedCount > 0 || unavailCount > 0)
        {
            // ── Shopping in progress: at least one item resolved, more still pending ──
            targetStatus = OrderStatus.BUYING_NOW;
        }
        else
        {
            // ── No items resolved yet, stay in current state ──
            // Only advance FOR_BUYING→BUYING_NOW if shopper actually starts touching items
            return;
        }

        // Recalculate Pasabuy Service Fee dynamically based on active (non-unavailable) items
        var activeSubtotal = activeItems
            .Where(i => i.Status != ItemStatus.UNAVAILABLE)
            .Sum(i => (i.ActualPrice ?? i.EstimatedPrice) * (decimal)i.Qty);

        if (BusinessProfileService.IsPasabuyFeeEnabled(db))
        {
            order.ServiceFee = activeSubtotal > 0
                ? PasabuyFeeService.CalculateServiceFee(activeSubtotal, db)
                : 0m;
        }
        else
        {
            order.ServiceFee = 0m;
        }

        if (order.Status != targetStatus)
        {
            order.Status = targetStatus;
            order.UpdatedAt = DateTime.Now;
            db.SaveChanges();
            OrderEventBus.NotifyOrderStatusChanged(orderId, targetStatus);
        }
        else
        {
            order.UpdatedAt = DateTime.Now;
            db.SaveChanges();
        }
    }

    /// <summary>
    /// Starts a Buying Session for specified orders or day: advances all eligible PENDING orders to FOR_BUYING/BUYING.
    /// </summary>
    public (bool ok, string msg, int count) StartBuyingSession(string? buyingDay = null)
    {
        using var tx = db.Database.BeginTransaction();
        try
        {
            var q = db.Orders.Where(o => o.Status == OrderStatus.WAITING_CONFIRMATION || o.Status == OrderStatus.READY_TO_BUY || o.Status == OrderStatus.PENDING || o.Status == OrderStatus.FOR_BUYING);
            if (!string.IsNullOrWhiteSpace(buyingDay) && buyingDay != "All Days" && buyingDay != "All")
            {
                q = q.Where(o => o.BuyingDay == buyingDay);
            }

            var orders = q.ToList();
            var count = 0;
            foreach (var ord in orders)
            {
                ord.Status = OrderStatus.BUYING_NOW;
                ord.UpdatedAt = DateTime.Now;
                count++;
            }

            db.SaveChanges();
            tx.Commit();
            OrderEventBus.NotifyDataRefreshed();
            return (true, $"Started buying session for {count} orders.", count);
        }
        catch (Exception ex)
        {
            tx.Rollback();
            return (false, $"Error starting buying session: {ex.Message}", 0);
        }
    }

    /// <summary>
    /// Completes buying for all orders where all items are resolved: marks them as BOUGHT.
    /// </summary>
    public (bool ok, string msg, int count) CompleteBuyingForReadyOrders()
    {
        using var tx = db.Database.BeginTransaction();
        try
        {
            var activeOrders = db.Orders.Include(o => o.Items)
                .Where(o => o.Status == OrderStatus.WAITING_CONFIRMATION || o.Status == OrderStatus.READY_TO_BUY || o.Status == OrderStatus.BUYING_NOW)
                .ToList();

            var count = 0;
            foreach (var ord in activeOrders)
            {
                if (ord.Items.Count > 0 && ord.Items.All(i => i.Status is ItemStatus.PURCHASED or ItemStatus.UNAVAILABLE or ItemStatus.CANCELLED))
                {
                    ord.Status = OrderStatus.BOUGHT;
                    ord.UpdatedAt = DateTime.Now;
                    count++;
                }
            }

            db.SaveChanges();
            tx.Commit();
            OrderEventBus.NotifyDataRefreshed();
            return (true, $"Successfully finished buying for {count} order(s). They are now marked as BOUGHT.", count);
        }
        catch (Exception ex)
        {
            tx.Rollback();
            return (false, $"Error completing buying: {ex.Message}", 0);
        }
    }
}
