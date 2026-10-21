using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using Microsoft.EntityFrameworkCore;

namespace AntiquePasabuyPOS.Services;

/// <summary>
/// Central state machine owning all legal order status transitions (Phase 3).
/// Enforces business rules, prevents invalid workflow jumps, and logs status audits.
/// </summary>
public class OrderStatusService(AppDbContext db)
{
    /// <summary>
    /// Strict transition map according to the canonical state machine.
    /// </summary>
    public static readonly Dictionary<OrderStatus, HashSet<OrderStatus>> LegalTransitions = new()
    {
        // ── 5-Status Canonical Machine ──────────────────────────────────────────
        // FOR_BUYING → BUYING → BOUGHT → COMPLETED
        // Any non-terminal → CANCELLED

        [OrderStatus.WAITING_CONFIRMATION] = new()
        {
            OrderStatus.READY_TO_BUY,
            OrderStatus.BUYING_NOW,
            OrderStatus.BOUGHT,
            OrderStatus.COMPLETED,
            OrderStatus.CANCELLED
        },

        [OrderStatus.READY_TO_BUY] = new()
        {
            OrderStatus.WAITING_CONFIRMATION,
            OrderStatus.BUYING_NOW,
            OrderStatus.BOUGHT,
            OrderStatus.COMPLETED,
            OrderStatus.CANCELLED
        },

        [OrderStatus.BUYING_NOW] = new()
        {
            OrderStatus.READY_TO_BUY,
            OrderStatus.WAITING_CONFIRMATION,
            OrderStatus.BOUGHT,
            OrderStatus.COMPLETED,
            OrderStatus.CANCELLED
        },

        [OrderStatus.BOUGHT] = new()
        {
            OrderStatus.READY_TO_BUY,
            OrderStatus.BUYING_NOW,
            OrderStatus.COMPLETED,
            OrderStatus.CANCELLED
        },

        [OrderStatus.COMPLETED] = new()
        {
            OrderStatus.BOUGHT,
            OrderStatus.BUYING_NOW,
            OrderStatus.READY_TO_BUY,
            OrderStatus.CANCELLED
        },

        [OrderStatus.CANCELLED] = new()
        {
            OrderStatus.WAITING_CONFIRMATION
        }
    };

    /// <summary>
    /// Checks whether a transition between two statuses is permitted.
    /// </summary>
    public static bool CanTransition(OrderStatus current, OrderStatus target, DeliveryType? deliveryType = null)
    {
        if (current == target) return true;
        if (!LegalTransitions.TryGetValue(current, out var allowed) || !allowed.Contains(target))
            return false;
        return true;
    }

    /// <summary>
    /// Executes a validated status transition inside a database transaction.
    /// </summary>
    public (bool ok, string msg) Transition(int orderId, OrderStatus newStatus, int? userId = null, string? reason = null)
    {
        using var tx = db.Database.BeginTransaction();
        try
        {
            // Detach ALL pre-tracked entries from the ChangeTracker to prevent EF Core duplicate
            // key tracking conflicts when the caller (e.g. OrderDetailDialog, OrdersPage) has
            // already attached entities via the same DbContext instance.
            foreach (var entry in db.ChangeTracker.Entries().ToList())
                entry.State = Microsoft.EntityFrameworkCore.EntityState.Detached;

            // Load Order + Delivery + Items — freshly from DB
            var order = db.Orders
                .Include(o => o.Delivery)
                .Include(o => o.Items)
                .FirstOrDefault(o => o.Id == orderId);

            if (order is null)
                return (false, "Order not found.");

            if (!CanTransition(order.Status, newStatus, order.DeliveryType))
            {
                return (false, $"Invalid transition from {FormatStatus(order.Status)} to {FormatStatus(newStatus)}.");
            }

            var previous = order.Status;
            order.Status = newStatus;
            order.UpdatedAt = DateTime.Now;

            // Auto-create delivery entity for delivery orders if not present yet
            if (order.Delivery == null && order.DeliveryType == DeliveryType.DELIVERY)
            {
                order.Delivery = new Delivery
                {
                    OrderId = order.Id,
                    Status = DeliveryStatus.PENDING,
                    CreatedAt = DateTime.Now
                };
                db.Deliveries.Add(order.Delivery);
            }

            // Sync delivery entity status when relevant
            if (order.Delivery != null)
            {
                if (newStatus == OrderStatus.FOR_DELIVERY)
                    order.Delivery.Status = DeliveryStatus.PENDING;
                else if (newStatus == OrderStatus.OUT_FOR_DELIVERY)
                {
                    order.Delivery.Status = DeliveryStatus.OUT_FOR_DELIVERY;
                    order.Delivery.PickedUpAt ??= DateTime.Now;
                }
                else if (newStatus is OrderStatus.DELIVERED or OrderStatus.COMPLETED)
                {
                    order.Delivery.Status = DeliveryStatus.DELIVERED;
                    order.Delivery.DeliveredAt ??= DateTime.Now;
                }
            }

            // Restore inventory for any Bought from stock items if order is cancelled
            if (newStatus == OrderStatus.CANCELLED && order.Items != null)
            {
                var stockSvc = new StockService(db);
                foreach (var item in order.Items.Where(i => i.Status == ItemStatus.BOUGHT_FROM_STOCK))
                {
                    stockSvc.RestoreStock(item.ProductName, item.PurchasedQty ?? item.Qty, order.OrderNo, item.Id);
                }
            }

            // Append cancellation reason to notes if provided
            if (newStatus == OrderStatus.CANCELLED && !string.IsNullOrWhiteSpace(reason))
            {
                order.Notes = string.IsNullOrWhiteSpace(order.Notes)
                    ? $"[CANCELLED: {reason}]"
                    : $"{order.Notes} | [CANCELLED: {reason}]";
            }

            // Record audit log
            db.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Action = "order_status_change",
                Details = $"Order {order.OrderNo}: {previous} -> {newStatus}{(string.IsNullOrWhiteSpace(reason) ? "" : $" ({reason})")}",
                CreatedAt = DateTime.Now
            });

            db.SaveChanges();
            tx.Commit();

            // Notify all screens
            OrderEventBus.NotifyOrderStatusChanged(orderId, newStatus);

            return (true, $"Order {order.OrderNo} is now {FormatStatus(newStatus)}.");
        }
        catch (Exception ex)
        {
            tx.Rollback();
            return (false, $"Unable to update status: {ex.Message}");
        }
    }

    /// <summary>
    /// Formats canonical enum into clean, shop-friendly text.
    /// </summary>
    public static string FormatStatus(OrderStatus status) => status switch
    {
        OrderStatus.WAITING_CONFIRMATION => "Waiting to Buy",
        OrderStatus.READY_TO_BUY         => "Waiting to Buy",
        OrderStatus.BUYING_NOW           => "Buying",
        OrderStatus.BOUGHT               => "Bought",
        OrderStatus.COMPLETED            => "Order Finished",
        OrderStatus.CANCELLED            => "Cancelled",
        _                                => "Waiting to Buy"
    };

    /// <summary>
    /// Formats payment status enum into clean display text.
    /// </summary>
    public static string FormatPaymentStatus(PaymentStatus status) => status switch
    {
        PaymentStatus.PAID    => "Paid",
        PaymentStatus.PARTIAL => "Partly Paid",
        PaymentStatus.UNPAID  => "Not Paid",
        _                     => "Not Paid"
    };
}
