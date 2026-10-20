using System.ComponentModel.DataAnnotations;
namespace AntiquePasabuyPOS.Models;
public class Customer
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    [Required] public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Address { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public string BusinessId { get; set; } = "ANTIQUE_PASABUY";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public ICollection<Order> Orders { get; set; } = new List<Order>();

    /// <summary>Generates up to 2 initials from the customer name for avatar display.</summary>
    public string InitialsDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Name)) return "?";
            var parts = Name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1) return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpper();
            return $"{parts[0][0]}{parts[1][0]}".ToUpper();
        }
    }
}

