namespace CafePos.Domain;

public class ItemModifierList
{
    public Guid ItemId { get; set; }
    public Guid ModifierListId { get; set; }
    public ModifierList? ModifierList { get; set; }
}
