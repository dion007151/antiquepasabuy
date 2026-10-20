using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using Microsoft.EntityFrameworkCore;

namespace AntiquePasabuyPOS.Services;

/// <summary>
/// Service governing fulfillment operations: separation of Pickup vs Delivery,
/// strict query filtering (Phase 12, 13), driver assignment, and completion.
/// </summary>
public class FulfillmentService(AppDbContext db)
{
    /// <summary>
    /// Returns orders strictly matching the active fulfillment tab.
    /// Eliminates the screenshot bug where unready/buying orders leaked into pickup tabs.
    /// </summary>
    public List<Order> GetFulfillmentOrders(string tab, string? searchQuery = null)
    {
        var q = db.Orders
            .AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.Delivery)
            .Include(o => o.Items)
            .Include(o => o.Payments)
            .Where(o => o.Status != OrderStatus.CANCELLED)
            .AsQueryable();

        // Strict tab isolation (Phase 13)
        q = tab.ToLowerInvariant() switch
        {
            "for_delivery" => q.Where(o => o.DeliveryType == DeliveryType.DELIVERY &&
                                          (o.Status == OrderStatus.FOR_DELIVERY || o.Status == OrderStatus.READY || o.Status == OrderStatus.PENDING || o.Status == OrderStatus.FOR_BUYING || o.Status == OrderStatus.BUYING || o.Status == OrderStatus.BOUGHT)),

            "out_for_delivery" => q.Where(o => o.DeliveryType == DeliveryType.DELIVERY &&
                                               o.Status == OrderStatus.OUT_FOR_DELIVERY),

            "delivered" => q.Where(o => o.DeliveryType == DeliveryType.DELIVERY &&
                                       (o.Status == OrderStatus.DELIVERED ||
                                       (o.Status == OrderStatus.COMPLETED && o.Delivery != null && o.Delivery.Status == DeliveryStatus.DELIVERED))),

            "ready_for_pickup" or "pickup" => q.Where(o => o.DeliveryType == DeliveryType.PICKUP &&
                                                          (o.Status == OrderStatus.READY_FOR_PICKUP || o.Status == OrderStatus.READY || o.Status == OrderStatus.PENDING || o.Status == OrderStatus.FOR_BUYING || o.Status == OrderStatus.BUYING || o.Status == OrderStatus.BOUGHT)),

            "picked_up" => q.Where(o => o.DeliveryType == DeliveryType.PICKUP &&
                                       (o.Status == OrderStatus.PICKED_UP ||
                                       (o.Status == OrderStatus.COMPLETED && o.DeliveryType == DeliveryType.PICKUP))),

            "completed" => q.Where(o => o.Status == OrderStatus.COMPLETED),

            _ => q
        };

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            var search = searchQuery.Trim().ToLowerInvariant();
            q = q.Where(o =>
                (o.OrderNo != null && o.OrderNo.ToLower().Contains(search)) ||
                (o.Customer != null && o.Customer.Name.ToLower().Contains(search)) ||
                (o.Address != null && o.Address.ToLower().Contains(search)) ||
                (o.Delivery != null && o.Delivery.RiderName != null && o.Delivery.RiderName.ToLower().Contains(search)));
        }

        return q.OrderByDescending(o => o.CreatedAt).ToList();
    }

    /// <summary>
    /// Dispatches an order out for delivery with an assigned rider name and contact.
    /// </summary>
    public (bool ok, string msg) AssignRiderAndDispatch(int orderId, string riderName, string? riderPhone = null, int? userId = null)
    {
        var statusSvc = new OrderStatusService(db);
        var order = db.Orders.Include(o => o.Delivery).FirstOrDefault(o => o.Id == orderId);
        if (order is null) return (false, "Order not found.");
        if (order.DeliveryType != DeliveryType.DELIVERY) return (false, "Only delivery orders can be assigned a rider.");

        order.Delivery ??= new Delivery { OrderId = orderId, CreatedAt = DateTime.Now };
        order.Delivery.RiderName = riderName.Trim();
        if (!string.IsNullOrWhiteSpace(riderPhone)) order.Delivery.RiderPhone = riderPhone.Trim();
        order.Delivery.Status = DeliveryStatus.OUT_FOR_DELIVERY;
        order.Delivery.PickedUpAt = DateTime.Now;

        db.SaveChanges();

        // Advance status to OUT_FOR_DELIVERY
        return statusSvc.Transition(orderId, OrderStatus.OUT_FOR_DELIVERY, userId, $"Dispatched to rider {riderName}");
    }

    /// <summary>
    /// Marks a delivery order as DELIVERED.
    /// </summary>
    public (bool ok, string msg) MarkDelivered(int orderId, int? userId = null)
    {
        var statusSvc = new OrderStatusService(db);
        var order = db.Orders.Include(o => o.Delivery).FirstOrDefault(o => o.Id == orderId);
        if (order is null) return (false, "Order not found.");

        if (order.Delivery != null)
        {
            order.Delivery.Status = DeliveryStatus.DELIVERED;
            order.Delivery.DeliveredAt = DateTime.Now;
        }

        db.SaveChanges();
        return statusSvc.Transition(orderId, OrderStatus.DELIVERED, userId);
    }

    /// <summary>
    /// Marks a pickup order as PICKED_UP by the customer.
    /// </summary>
    public (bool ok, string msg) MarkPickedUp(int orderId, int? userId = null)
    {
        var statusSvc = new OrderStatusService(db);
        var order = db.Orders.FirstOrDefault(o => o.Id == orderId);
        if (order is null) return (false, "Order not found.");
        if (order.DeliveryType != DeliveryType.PICKUP) return (false, "Only pickup orders can be marked picked up.");

        return statusSvc.Transition(orderId, OrderStatus.PICKED_UP, userId);
    }
}
