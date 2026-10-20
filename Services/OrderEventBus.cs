using System.Windows;
using AntiquePasabuyPOS.Models;

namespace AntiquePasabuyPOS.Services;

/// <summary>
/// Centralized real-time event bus for cross-screen data synchronization (Phase 18).
/// Dispatches events onto the WPF Dispatcher thread so UI subscribers can safely refresh.
/// </summary>
public static class OrderEventBus
{
    public static event Action<int>? OrderCreated;
    public static event Action<int, OrderStatus>? OrderStatusChanged;
    public static event Action<int, ItemStatus>? ItemStatusChanged;
    public static event Action<int, decimal>? PaymentRecorded;
    public static event Action? DataRefreshed;

    public static void NotifyOrderCreated(int orderId) => Dispatch(() => OrderCreated?.Invoke(orderId));
    public static void NotifyOrderStatusChanged(int orderId, OrderStatus newStatus) => Dispatch(() => OrderStatusChanged?.Invoke(orderId, newStatus));
    public static void NotifyItemStatusChanged(int itemId, ItemStatus newStatus) => Dispatch(() => ItemStatusChanged?.Invoke(itemId, newStatus));
    public static void NotifyPaymentRecorded(int orderId, decimal amount) => Dispatch(() => PaymentRecorded?.Invoke(orderId, amount));
    public static void NotifyDataRefreshed() => Dispatch(() => DataRefreshed?.Invoke());

    private static void Dispatch(Action action)
    {
        if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.InvokeAsync(action);
        }
        else
        {
            action();
        }
    }
}
