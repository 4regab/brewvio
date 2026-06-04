namespace Brewvio.Models;

// An inventory stock item consumed by menu-item recipes.
public class Ingredient
{
    public int Id { get; set; }
    public string Code { get; set; } = "";          // short item code shown in the inventory table
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";       // e.g. Coffee, Dairy, Syrup, Supplies
    public string Unit { get; set; } = "";          // ml, g, pc, shot
    public decimal StockLevel { get; set; }          // current quantity on hand
    public decimal Threshold { get; set; }           // low-stock alert level
    public decimal CostPerUnit { get; set; }         // used for profitability/costing
    public ICollection<RecipeIngredient> RecipeIngredients { get; set; } = new List<RecipeIngredient>();
}
