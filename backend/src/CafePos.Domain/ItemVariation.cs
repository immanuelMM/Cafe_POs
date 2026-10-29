namespace CafePos.Domain;

public class ItemVariation
{
    public Guid Id { get; set; }
    public Guid ItemId { get; set; }
    public string Name { get; set; } = "";
    public string Sku { get; set; } = "";
    /// <summary>VAT-inclusive selling price in PHP.</summary>
    public decimal Price { get; set; }
    public bool Active { get; set; } = true;
    public Item? Item { get; set; }
    public List<RecipeLine> Recipe { get; set; } = new();
}
