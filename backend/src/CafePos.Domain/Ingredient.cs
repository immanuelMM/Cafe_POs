namespace CafePos.Domain;

public class Ingredient
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "each";
    public decimal LowStockThreshold { get; set; }
    public bool Active { get; set; } = true;
}
