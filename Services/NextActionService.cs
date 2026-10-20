using AntiquePasabuyPOS.Models;

namespace AntiquePasabuyPOS.Services;

/// <summary>
/// Centralized Next Action service.
/// Maps every canonical PO status to its smart workflow action label, target status, and key.
///
/// 5-Status Workflow:
///   PENDING → READY_TO_BUY → BUYING_NOW → COMPLETED
///   Any non-terminal → CANCELLED
/// </summary>
public static class NextActionService
{
    public record SmartActionInfo(
        string Label,
        OrderStatus? TargetStatus,
        string ActionKey,
        string ToolTip,
        string ColorHex
    )
    {
        public bool HasAction => ActionKey != "view";
        public string ActionLabel => Label;
    }

    /// <summary>
    /// Computes the context-aware next action for any given order.
    /// Never returns blank or null.
    /// </summary>
    public static SmartActionInfo GetNextAction(Order order)
    {
        if (order == null)
            return new SmartActionInfo("VIEW", null, "view", "View details", "#64748B");

        return order.Status switch
        {
            // ── WAITING_CONFIRMATION / READY_TO_BUY: Waiting to Buy → Start Buying ──
            OrderStatus.WAITING_CONFIRMATION or OrderStatus.READY_TO_BUY =>
                new SmartActionInfo(
                    "START BUYING",
                    OrderStatus.BUYING_NOW,
                    "start_buying",
                    "Mark as actively being purchased at stores",
                    "#7C3AED"),

            // ── BUYING_NOW: buyer is at the store — mark as bought when done ──
            OrderStatus.BUYING_NOW =>
                new SmartActionInfo(
                    "MARK AS BOUGHT",
                    OrderStatus.BOUGHT,
                    "mark_bought",
                    "All items purchased at the store — move to Bought",
                    "#059669"),

            // ── BOUGHT: items in hand — complete once handed to customer ──────
            OrderStatus.BOUGHT =>
                new SmartActionInfo(
                    "FINISH ORDER",
                    OrderStatus.COMPLETED,
                    "finish_order",
                    "Order finished — handed over or delivered to customer",
                    "#16A34A"),

            // ── Terminal states ───────────────────────────────────────────────
            OrderStatus.COMPLETED =>
                new SmartActionInfo("VIEW", null, "view", "Order is fully completed", "#16A34A"),

            OrderStatus.CANCELLED =>
                new SmartActionInfo("VIEW", null, "view", "Order is cancelled", "#EF4444"),

            _ =>
                new SmartActionInfo("VIEW", null, "view", "View order", "#64748B")
        };
    }
}
