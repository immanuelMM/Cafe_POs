using CafePos.Domain;

namespace CafePos.Application.Features.Inventory;

public interface IRecipeRepository
{
    Task<bool> VariationExistsAsync(Guid variationId, CancellationToken ct);

    /// <summary>Recipe lines of a variation with their ingredient (read-only), by ingredient name.</summary>
    Task<IReadOnlyList<RecipeLine>> ListLinesAsync(Guid variationId, CancellationToken ct);

    /// <summary>Recipe lines of several variations (read-only), without the ingredient.</summary>
    Task<IReadOnlyList<RecipeLine>> ListLinesForVariationsAsync(IReadOnlyCollection<Guid> variationIds, CancellationToken ct);

    /// <summary>Tracked lines of a variation, for replacement.</summary>
    Task<IReadOnlyList<RecipeLine>> GetLinesForUpdateAsync(Guid variationId, CancellationToken ct);

    void AddLine(RecipeLine line);

    void RemoveLines(IEnumerable<RecipeLine> lines);
}
