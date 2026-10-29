namespace CafePos.Domain;

public class ModifierList
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public int MinSelections { get; set; }
    public int MaxSelections { get; set; } = 1;
    public bool Active { get; set; } = true;
    public List<Modifier> Modifiers { get; set; } = new();
}
