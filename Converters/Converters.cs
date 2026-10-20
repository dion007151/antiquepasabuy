using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using AntiquePasabuyPOS.Models;

namespace AntiquePasabuyPOS.Converters;

public class OrderStatusColorConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        if (v is OrderStatus os)
        {
            return os switch
            {
                OrderStatus.WAITING_CONFIRMATION => new SolidColorBrush(Color.FromRgb(79, 70, 229)),   // Indigo (#4F46E5) - For Buying
                OrderStatus.READY_TO_BUY         => new SolidColorBrush(Color.FromRgb(79, 70, 229)),   // Indigo (#4F46E5) - For Buying
                OrderStatus.BUYING_NOW           => new SolidColorBrush(Color.FromRgb(124, 58, 237)),  // Purple (#7C3AED) - Buying
                OrderStatus.BOUGHT               => new SolidColorBrush(Color.FromRgb(5, 150, 105)),   // Emerald (#059669) - Bought
                OrderStatus.COMPLETED            => new SolidColorBrush(Color.FromRgb(22, 163, 74)),   // Green (#16A34A) - Completed
                OrderStatus.CANCELLED            => new SolidColorBrush(Color.FromRgb(220, 38, 38)),   // Red (#DC2626) - Cancelled
                _                                => new SolidColorBrush(Color.FromRgb(79, 70, 229))
            };
        }

        var statusStr = v?.ToString()?.ToUpperInvariant() ?? "";
        return statusStr switch
        {
            "WAITING_CONFIRMATION" or "WAITING CONFIRMATION" or "PENDING" or "DRAFT" or "CONFIRMED" => new SolidColorBrush(Color.FromRgb(79, 70, 229)),   // Indigo (#4F46E5) - For Buying
            "READY_TO_BUY" or "READY TO BUY" or "FOR_BUYING" or "READY" or "SHOPPING"               => new SolidColorBrush(Color.FromRgb(79, 70, 229)),   // Indigo (#4F46E5) - For Buying
            "BUYING_NOW" or "BUYING NOW" or "BUYING" or "PURCHASING" or "PARTIAL"                   => new SolidColorBrush(Color.FromRgb(124, 58, 237)),  // Purple (#7C3AED) - Buying
            "BOUGHT" or "PURCHASED"                                                                 => new SolidColorBrush(Color.FromRgb(5, 150, 105)),   // Emerald (#059669) - Bought
            "COMPLETED" or "COMPLETE" or "DELIVERED" or "PICKED_UP" or "PICKED UP" or "FOR_DELIVERY" or "FOR DELIVERY" or "OUT_FOR_DELIVERY" or "OUT FOR DELIVERY" or "READY_FOR_PICKUP" or "READY FOR PICKUP" => new SolidColorBrush(Color.FromRgb(22, 163, 74)), // Green (#16A34A) - Completed
            "CANCELLED" or "UNAVAILABLE"                                                            => new SolidColorBrush(Color.FromRgb(220, 38, 38)),   // Red (#DC2626) - Cancelled
            _                                                                                       => new SolidColorBrush(Color.FromRgb(79, 70, 229))    // Default: Indigo
        };
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

public class OrderStatusTextConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        if (v is OrderStatus os)
        {
            return os switch
            {
                OrderStatus.WAITING_CONFIRMATION => "Waiting to Buy",
                OrderStatus.READY_TO_BUY         => "Waiting to Buy",
                OrderStatus.BUYING_NOW           => "Buying",
                OrderStatus.BOUGHT               => "Bought",
                OrderStatus.COMPLETED            => "Order Finished",
                OrderStatus.CANCELLED            => "Cancelled",
                _                                => "Waiting to Buy"
            };
        }

        var statusStr = v?.ToString()?.ToUpperInvariant() ?? "";
        return statusStr switch
        {
            "WAITING_CONFIRMATION" or "WAITING CONFIRMATION" or "PENDING" or "DRAFT" or "CONFIRMED" => "Waiting to Buy",
            "READY_TO_BUY" or "READY TO BUY" or "FOR_BUYING" or "FOR BUYING" or "READY" or "SHOPPING" or "WAITING TO BUY" => "Waiting to Buy",
            "BUYING_NOW" or "BUYING NOW" or "BUYING" or "PURCHASING"                                 => "Buying",
            "BOUGHT" or "PURCHASED"                                                                 => "Bought",
            "COMPLETED" or "COMPLETE" or "DELIVERED" or "PICKED_UP" or "PICKED UP" or "ORDER FINISHED" or "ORDER_FINISHED" => "Order Finished",
            "CANCELLED" or "UNAVAILABLE"                                                            => "Cancelled",
            _                                                                                       => "Waiting to Buy"
        };
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

public class OrderStatusTooltipConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        if (v is OrderStatus os)
        {
            return os switch
            {
                OrderStatus.WAITING_CONFIRMATION => "Waiting to Buy — queued for scheduled shopping day",
                OrderStatus.READY_TO_BUY         => "Waiting to Buy — queued for scheduled shopping day",
                OrderStatus.BUYING_NOW           => "Buying — items are currently being purchased at the store",
                OrderStatus.BOUGHT               => "Bought — items purchased by shopper and ready for handover",
                OrderStatus.COMPLETED            => "Order Finished — order handed over or delivered",
                OrderStatus.CANCELLED            => "Cancelled — order was cancelled",
                _                                => "Order status"
            };
        }

        var statusStr = v?.ToString()?.ToUpperInvariant() ?? "";
        return statusStr switch
        {
            "WAITING_CONFIRMATION" or "WAITING CONFIRMATION" or "PENDING" or "DRAFT" or "CONFIRMED" => "Waiting to Buy — queued for scheduled shopping day",
            "READY_TO_BUY" or "READY TO BUY" or "FOR_BUYING" or "READY" or "SHOPPING" or "WAITING TO BUY" => "Waiting to Buy — queued for scheduled shopping day",
            "BUYING_NOW" or "BUYING NOW" or "BUYING" or "PURCHASING"                                 => "Buying — items are currently being purchased at the store",
            "BOUGHT" or "PURCHASED"                                                                 => "Bought — items purchased by shopper and ready for handover",
            "COMPLETED" or "COMPLETE" or "DELIVERED" or "PICKED_UP" or "PICKED UP" or "ORDER FINISHED" => "Order Finished — order handed over or delivered",
            "CANCELLED" or "UNAVAILABLE"                                                            => "Cancelled — order was cancelled",
            _                                                                                       => "Order status"
        };
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

public class ItemStatusColorConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        var statusStr = v?.ToString()?.ToUpperInvariant() ?? "";
        return statusStr switch
        {
            "PURCHASED" or "BOUGHT" => new SolidColorBrush(Color.FromRgb(22, 163, 74)),   // Green
            "UNAVAILABLE"           => new SolidColorBrush(Color.FromRgb(100, 116, 139)), // Muted slate gray
            "CANCELLED"             => new SolidColorBrush(Color.FromRgb(220, 38, 38)),   // Red
            _                       => new SolidColorBrush(Color.FromRgb(217, 119, 6))    // Amber
        };
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

public class DifferenceColorConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        if (v is decimal diff)
        {
            if (diff > 0) return new SolidColorBrush(Color.FromRgb(220, 38, 38)); // Higher than estimated (Red)
            if (diff < 0) return new SolidColorBrush(Color.FromRgb(22, 163, 74)); // Saved money / lower (Green)
            return new SolidColorBrush(Color.FromRgb(100, 116, 139));             // Exact / Zero (Gray)
        }
        return new SolidColorBrush(Color.FromRgb(100, 116, 139));
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

public class CategoryColorConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        var cat = v?.ToString()?.Trim() ?? "";
        return cat switch
        {
            "Fastfood"  => new SolidColorBrush(Color.FromRgb(234, 88, 12)),   // Warm Amber / Orange
            "Groceries" => new SolidColorBrush(Color.FromRgb(5, 150, 105)),   // Emerald Green
            "Pharmacy"  => new SolidColorBrush(Color.FromRgb(2, 132, 199)),   // Sky Blue
            "Bakery"    => new SolidColorBrush(Color.FromRgb(217, 70, 239)),  // Fuchsia Pink
            _           => new SolidColorBrush(Color.FromRgb(79, 70, 229))    // Indigo
        };
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

public class CategoryLightColorConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        var cat = v?.ToString()?.Trim() ?? "";
        return cat switch
        {
            "Fastfood"  => new SolidColorBrush(Color.FromRgb(255, 237, 213)), // Amber-100
            "Groceries" => new SolidColorBrush(Color.FromRgb(209, 250, 229)), // Emerald-100
            "Pharmacy"  => new SolidColorBrush(Color.FromRgb(224, 242, 254)), // Sky-100
            "Bakery"    => new SolidColorBrush(Color.FromRgb(252, 231, 243)), // Pink-100
            _           => new SolidColorBrush(Color.FromRgb(238, 242, 255))  // Indigo-100
        };
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

// ── Payment Method Converters ─────────────────────────────────────────────────

/// <summary>Converts ICollection&lt;Payment&gt; → display text label.</summary>
public class PaymentMethodTextConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        if (v is not IEnumerable<Payment> payments) return "Unpaid";
        var list = payments.ToList();
        if (list.Count == 0) return "Unpaid";
        var method = list.OrderByDescending(x => x.CreatedAt).First().Method;
        return method switch
        {
            PaymentMethod.GCash        => "GCash",
            PaymentMethod.BankTransfer => "Bank Transfer",
            PaymentMethod.Maya         => "Maya",
            PaymentMethod.Other        => "Other",
            _                          => "Cash / On Hand"
        };
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

/// <summary>Converts ICollection&lt;Payment&gt; → badge background colour.</summary>
public class PaymentMethodBgConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        if (v is not IEnumerable<Payment> payments) return new SolidColorBrush(Color.FromRgb(254, 243, 199)); // Amber-100
        var list = payments.ToList();
        if (list.Count == 0) return new SolidColorBrush(Color.FromRgb(254, 243, 199)); // Amber-100
        var method = list.OrderByDescending(x => x.CreatedAt).First().Method;
        return method switch
        {
            PaymentMethod.GCash        => new SolidColorBrush(Color.FromRgb(219, 234, 254)), // Blue-100
            PaymentMethod.BankTransfer => new SolidColorBrush(Color.FromRgb(209, 250, 229)), // Emerald-100
            PaymentMethod.Maya         => new SolidColorBrush(Color.FromRgb(243, 232, 255)), // Violet-100
            _                          => new SolidColorBrush(Color.FromRgb(220, 252, 231))  // Green-100
        };
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

/// <summary>Converts ICollection&lt;Payment&gt; → badge foreground colour.</summary>
public class PaymentMethodFgConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        if (v is not IEnumerable<Payment> payments) return new SolidColorBrush(Color.FromRgb(180, 83, 9));  // Amber-700
        var list = payments.ToList();
        if (list.Count == 0) return new SolidColorBrush(Color.FromRgb(180, 83, 9));  // Amber-700
        var method = list.OrderByDescending(x => x.CreatedAt).First().Method;
        return method switch
        {
            PaymentMethod.GCash        => new SolidColorBrush(Color.FromRgb(29,  78, 216)), // Blue-700
            PaymentMethod.BankTransfer => new SolidColorBrush(Color.FromRgb(4,  120, 87)),  // Emerald-700
            PaymentMethod.Maya         => new SolidColorBrush(Color.FromRgb(109, 40, 217)), // Violet-700
            _                          => new SolidColorBrush(Color.FromRgb(21, 128, 61))   // Green-700
        };
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

// ── Pasabuy Fee Exempt Converters ─────────────────────────────────────────────

/// <summary>Converts IsFeeExempt (bool) → display text for the Pasabuy Fee column.</summary>
public class FeeExemptTextConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        if (v is bool exempt)
            return exempt ? "NO FEE" : "WITH FEE";
        return "WITH FEE";
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

/// <summary>Converts IsFeeExempt (bool) → badge background color for the Pasabuy Fee column.</summary>
public class FeeExemptColorConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        if (v is bool exempt)
            return exempt
                ? new SolidColorBrush(Color.FromRgb(100, 116, 139))  // Slate — No Pasabuy Fee
                : new SolidColorBrush(Color.FromRgb(5, 150, 105));    // Emerald — With Pasabuy Fee
        return new SolidColorBrush(Color.FromRgb(5, 150, 105));
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

// ── Payment Status Converters ─────────────────────────────────────────────────

public class PaymentStatusColorConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        var statusStr = v?.ToString()?.ToUpperInvariant() ?? "";
        return statusStr switch
        {
            "PAID"                                  => new SolidColorBrush(Color.FromRgb(22, 163, 74)),  // Emerald Green (#16A34A)
            "PARTIAL" or "PARTLY PAID" or "PARTLY" => new SolidColorBrush(Color.FromRgb(217, 119, 6)),  // Amber (#D97706)
            "UNPAID" or "NOT PAID"                  => new SolidColorBrush(Color.FromRgb(220, 38, 38)),  // Red (#DC2626)
            _                                       => new SolidColorBrush(Color.FromRgb(107, 114, 128)) // Neutral Slate (#6B7280)
        };
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

public class PaymentStatusTextConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        var statusStr = v?.ToString()?.ToUpperInvariant() ?? "";
        return statusStr switch
        {
            "PAID"                                  => "Paid",
            "PARTIAL" or "PARTLY PAID" or "PARTLY" => "Partly Paid",
            "UNPAID" or "NOT PAID"                  => "Not Paid",
            _                                       => "Not Paid"
        };
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

public class PaymentStatusTooltipConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        var statusStr = v?.ToString()?.ToUpperInvariant() ?? "";
        return statusStr switch
        {
            "PAID"                                  => "Paid — purchase order is fully paid",
            "PARTIAL" or "PARTLY PAID" or "PARTLY" => "Partly Paid — partial payment received, remaining balance due",
            "UNPAID" or "NOT PAID"                  => "Not Paid — customer has not paid",
            _                                       => "Payment status"
        };
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

