namespace CafePos.Domain;

public class Modifier
{
    public Guid Id { get; set; }
    public Guid ModifierListId { get; set; }
    public string Name { get; set; } = "";
    public decimal PriceDelta { get; set; }
    public bool Active { get; set; } = true;
}
