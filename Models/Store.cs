using System.ComponentModel.DataAnnotations.Schema;

namespace AntiquePasabuyPOS.Models;

public class Store
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Location { get; set; }
    [NotMapped]
    public string? Branch { get => Location; set => Location = value; }
    public string? Category { get; set; } // Department Store, Supermarket, Pharmacy, Bakery, Fast Food, Other
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
