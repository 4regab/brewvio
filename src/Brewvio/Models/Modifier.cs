namespace Brewvio.Models;

// An optional add-on/choice (e.g., Oat Milk, Vanilla Syrup) that can adjust an item's price.
public class Modifier
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string GroupName { get; set; } = "";      // e.g., "Milk", "Syrup", "Size"
    public decimal PriceDelta { get; set; }
    public bool IsActive { get; set; } = true;
}
