namespace AntiquePasabuyPOS.Models;

public enum OrderStatus
{
    WAITING_CONFIRMATION,
    READY_TO_BUY,
    BUYING_NOW,
    BOUGHT,
    COMPLETED,
    CANCELLED,

    // Backward compatibility aliases for existing DB records & code
    PENDING = WAITING_CONFIRMATION,
    Pending = WAITING_CONFIRMATION,
    WaitingConfirmation = WAITING_CONFIRMATION,
    WAITING = WAITING_CONFIRMATION,
    Waiting = WAITING_CONFIRMATION,
    Draft = WAITING_CONFIRMATION,
    Confirmed = WAITING_CONFIRMATION,
    FOR_BUYING = READY_TO_BUY,
    BUYING = BUYING_NOW,
    READY = READY_TO_BUY,
    PARTIAL = BUYING_NOW,
    FOR_DELIVERY = COMPLETED,
    OUT_FOR_DELIVERY = COMPLETED,
    DELIVERED = COMPLETED,
    READY_FOR_PICKUP = COMPLETED,
    PICKED_UP = COMPLETED,
    UNAVAILABLE = CANCELLED,

    ForBuying = READY_TO_BUY,
    ReadyToBuy = READY_TO_BUY,
    Purchasing = BUYING_NOW,
    Shopping = BUYING_NOW,
    BuyingNow = BUYING_NOW,
    Bought = BOUGHT,
    Purchased = BOUGHT,
    Ready = READY_TO_BUY,
    ForDelivery = COMPLETED,
    Delivered = COMPLETED,
    Completed = COMPLETED,
    Partial = BUYING_NOW,
    Unavailable = CANCELLED,
    Cancelled = CANCELLED
}

public enum ItemStatus
{
    PENDING,
    PURCHASED,
    BOUGHT_FROM_STOCK,
    UNAVAILABLE,
    REORDER,
    CANCELLED,

    // Backward compatibility aliases
    Pending = PENDING,
    Purchased = PURCHASED,
    Unavailable = UNAVAILABLE,
    NotAvailable = UNAVAILABLE,
    Cancelled = CANCELLED,
    Partial = PENDING,
    Bought = PURCHASED,
    BoughtFromStock = BOUGHT_FROM_STOCK,
    Reorder = REORDER
}

public enum PaymentStatus
{
    UNPAID,
    PARTIAL,
    PAID
}

public enum DeliveryStatus
{
    PENDING,
    FOR_DELIVERY = PENDING,
    OUT_FOR_DELIVERY,
    DELIVERED,
    FAILED,

    // Backward compatibility aliases
    Pending = PENDING,
    ForDelivery = FOR_DELIVERY,
    OutForDelivery = OUT_FOR_DELIVERY,
    Delivered = DELIVERED,
    Failed = FAILED
}

public enum DeliveryType
{
    PICKUP,
    DELIVERY,

    // Backward compatibility aliases
    Pickup = PICKUP,
    Delivery = DELIVERY
}

public enum PaymentMethod
{
    Cash,
    OnHand = Cash,
    GCash,
    Maya,
    BankTransfer,
    Bank = BankTransfer,
    COD,            // Cash on Delivery — payment collected by rider upon delivery
    Other
}

public enum UserRole
{
    Admin,
    Staff,
    Shopper
}

public enum ItemSource
{
    STORE,
    STOCK,

    // Aliases
    Store = STORE,
    Stock = STOCK,
    Bought = STORE
}
