namespace AntiquePasabuyPOS.Models;
public class Delivery
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order? Order { get; set; }
    public string? RiderName { get; set; }
    public string? RiderPhone { get; set; }
    public DeliveryStatus Status { get; set; } = DeliveryStatus.ForDelivery;
    public string? Notes { get; set; }
    public DateTime? PickedUpAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
