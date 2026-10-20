namespace AntiquePasabuyPOS.Models;
public class Payment
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order? Order { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; } = PaymentMethod.Cash;
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public DateTime? PaymentDate { get; set; }
    public string? PaymentProofPath { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
