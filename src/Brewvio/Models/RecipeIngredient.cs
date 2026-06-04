namespace Brewvio.Models;

// Join row: how much of an ingredient one menu item consumes (drives auto-deduction & costing).
public class RecipeIngredient
{
    public int Id { get; set; }
    public int MenuItemId { get; set; }
    public MenuItem MenuItem { get; set; } = null!;
    public int IngredientId { get; set; }
    public Ingredient Ingredient { get; set; } = null!;
    public decimal Quantity { get; set; }            // amount of ingredient per one menu item
}
