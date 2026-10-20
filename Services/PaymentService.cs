using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using Microsoft.EntityFrameworkCore;

namespace AntiquePasabuyPOS.Services;

/// <summary>
/// Service governing payment collection, transaction safety, balance calculation,
/// and payment status determination (Phase 11).
/// </summary>
public class PaymentService(AppDbContext db)
{
    /// <summary>
    /// Records a payment for an order transactionally, returning change due if cash tendered exceeds balance.
    /// </summary>
    public (bool ok, string msg, decimal changeDue) RecordPayment(
        int orderId,
        decimal amountTendered,
        PaymentMethod method,
        string? reference = null,
        string? notes = null,
        int? userId = null,
        DateTime? paymentDate = null,
        string? proofPath = null)
    {
        // COD is a ₱0 placeholder — rider collects cash upon delivery, so allow zero amount
        if (method != PaymentMethod.COD && amountTendered <= 0m)
            return (false, "Payment amount must be greater than zero.", 0m);

        using var tx = db.Database.BeginTransaction();
        try
        {
            var order = db.Orders.Include(o => o.Items).Include(o => o.Payments).FirstOrDefault(o => o.Id == orderId);
            if (order is null)
                return (false, "Order not found.", 0m);

            var grandTotal = OrderCalculationService.CalculateGrandTotal(order);
            var currentPaid = OrderCalculationService.CalculateTotalPaid(order.Payments);
            var balanceDue = OrderCalculationService.CalculateBalance(grandTotal, currentPaid);

            decimal actualPaymentAmount;
            decimal changeDue = 0m;

            if (method == PaymentMethod.COD)
            {
                // COD placeholder: record ₱0 — balance stays unpaid until rider returns with cash
                actualPaymentAmount = 0m;
            }
            else if (method == PaymentMethod.Cash && amountTendered > balanceDue && balanceDue > 0m)
            {
                actualPaymentAmount = balanceDue;
                changeDue = amountTendered - balanceDue;
            }
            else
            {
                actualPaymentAmount = amountTendered;
            }

            var payment = new Payment
            {
                OrderId = orderId,
                Amount = actualPaymentAmount,
                Method = method,
                Reference = string.IsNullOrWhiteSpace(reference) ? $"{method.ToString().ToUpper()}-COUNTER" : reference.Trim(),
                Notes = notes?.Trim(),
                PaymentDate = paymentDate ?? DateTime.Now,
                PaymentProofPath = string.IsNullOrWhiteSpace(proofPath) ? null : proofPath.Trim(),
                CreatedAt = DateTime.Now
            };

            db.Payments.Add(payment);
            order.UpdatedAt = DateTime.Now;

            // Audit log
            db.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Action = "payment_recorded",
                Details = $"Order {order.OrderNo}: Collected ₱{actualPaymentAmount:N2} via {method}. Ref: {payment.Reference}",
                CreatedAt = DateTime.Now
            });

            db.SaveChanges();
            tx.Commit();

            OrderEventBus.NotifyPaymentRecorded(orderId, actualPaymentAmount);
            OrderEventBus.NotifyDataRefreshed();

            var successMsg = method == PaymentMethod.COD
                ? $"Order {order.OrderNo} tagged as COD. Rider will collect ₱{order.GrandTotal - OrderCalculationService.CalculateTotalPaid(order.Payments):N2} upon delivery."
                : changeDue > 0m
                    ? $"Payment of ₱{actualPaymentAmount:N2} recorded. Change due: ₱{changeDue:N2}."
                    : $"Payment of ₱{actualPaymentAmount:N2} recorded successfully.";

            return (true, successMsg, changeDue);
        }
        catch (Exception ex)
        {
            tx.Rollback();
            return (false, $"Payment could not be saved: {ex.Message}", 0m);
        }
    }

    /// <summary>
    /// Updates an existing payment record (e.g. edit payment method, reference, date, proof, amount).
    /// </summary>
    public (bool ok, string msg) UpdatePayment(
        int paymentId,
        decimal amount,
        PaymentMethod method,
        string? reference = null,
        DateTime? paymentDate = null,
        string? proofPath = null,
        int? userId = null)
    {
        if (method != PaymentMethod.COD && amount <= 0m)
            return (false, "Payment amount must be greater than zero.");

        using var tx = db.Database.BeginTransaction();
        try
        {
            var payment = db.Payments.FirstOrDefault(p => p.Id == paymentId);
            if (payment == null)
                return (false, "Payment record not found.");

            payment.Amount = amount;
            payment.Method = method;
            payment.Reference = string.IsNullOrWhiteSpace(reference) ? $"{method.ToString().ToUpper()}-COUNTER" : reference.Trim();
            payment.PaymentDate = paymentDate ?? payment.PaymentDate ?? DateTime.Now;
            payment.PaymentProofPath = string.IsNullOrWhiteSpace(proofPath) ? null : proofPath.Trim();

            db.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Action = "payment_updated",
                Details = $"Payment #{payment.Id}: Updated to ₱{amount:N2} via {method}. Ref: {payment.Reference}",
                CreatedAt = DateTime.Now
            });

            db.SaveChanges();
            tx.Commit();

            OrderEventBus.NotifyPaymentRecorded(payment.OrderId, amount);
            OrderEventBus.NotifyDataRefreshed();
            return (true, "Payment updated successfully.");
        }
        catch (Exception ex)
        {
            tx.Rollback();
            return (false, $"Failed to update payment: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets all payments for an order.
    /// </summary>
    public List<Payment> GetOrderPayments(int orderId)
    {
        return db.Payments.Where(p => p.OrderId == orderId).OrderByDescending(p => p.CreatedAt).ToList();
    }
}
