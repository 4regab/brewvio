namespace Brewvio.Models;

// A sellable product with a recipe (list of ingredient requirements).
public class MenuItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<RecipeIngredient> Recipe { get; set; } = new List<RecipeIngredient>();
}
