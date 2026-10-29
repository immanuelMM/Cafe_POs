namespace CafePos.Domain;

/// <summary>Entities carrying a LocationId are filtered by EF global query filters.</summary>
public interface ILocationScoped { Guid LocationId { get; set; } }
