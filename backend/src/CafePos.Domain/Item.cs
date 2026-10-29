namespace CafePos.Domain;

public class Item
{
    public Guid Id { get; set; }
    public Guid CategoryId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool Active { get; set; } = true;
    /// <summary>Set when a photo is stored (in ItemImage); versions the image URL. Null = no photo.</summary>
    public DateTime? ImageUpdatedAt { get; set; }
    public Category? Category { get; set; }
    public List<ItemVariation> Variations { get; set; } = new();
    public List<ItemModifierList> ModifierLists { get; set; } = new();
}
