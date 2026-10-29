using CafePos.Application.Common;
using CafePos.Domain;

namespace CafePos.Application.Features.Inventory;

public sealed class RecipeService(IRecipeRepository recipes, IIngredientRepository ingredients, IUnitOfWork uow)
{
    public async Task<Result<RecipeDto>> GetAsync(Guid variationId, CancellationToken ct)
    {
        if (!await recipes.VariationExistsAsync(variationId, ct)) return Error.NotFound("Variation");
        return await LoadAsync(variationId, ct);
    }

    /// <summary>Replaces the whole recipe (global across locations). An empty list clears it.</summary>
    public async Task<Result<RecipeDto>> SetAsync(Guid variationId, SetRecipeRequest req, CancellationToken ct)
    {
        if (!await recipes.VariationExistsAsync(variationId, ct)) return Error.NotFound("Variation");
        var ids = req.Lines.Select(l => l.IngredientId).ToList();
        if (ids.Distinct().Count() != ids.Count) return Error.Unprocessable("Duplicate ingredient", "Each ingredient may appear once per recipe.");
        if (await ingredients.CountExistingAsync(ids, ct) != ids.Count) return Error.Unprocessable("Invalid ingredient", "One or more ingredients do not exist.");

        recipes.RemoveLines(await recipes.GetLinesForUpdateAsync(variationId, ct));
        await uow.SaveChangesAsync(ct); // flush deletes first: unique (variation, ingredient)
        foreach (var l in req.Lines)
            recipes.AddLine(new RecipeLine { Id = Guid.NewGuid(), ItemVariationId = variationId, IngredientId = l.IngredientId, Quantity = l.Quantity });
        await uow.SaveChangesAsync(ct);
        return await LoadAsync(variationId, ct);
    }

    private async Task<RecipeDto> LoadAsync(Guid variationId, CancellationToken ct)
    {
        var lines = await recipes.ListLinesAsync(variationId, ct);
        return new RecipeDto(variationId, lines.Select(r => new RecipeLineDto(r.IngredientId, r.Ingredient!.Name, r.Ingredient.Unit, r.Quantity)).ToList());
    }
}
