using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace CafePos.Api.OpenApi;

/// <summary>
/// Keeps the order of paths and tags in the OpenAPI document stable. Controllers are discovered in assembly metadata order (the
/// order the compiler saw the source files), so moving or renaming a controller file would otherwise reshuffle the committed
/// snapshot (backend/openapi/v1.json) and the generated frontend types without any contract change. Paths are grouped by
/// controller in the order below; within a controller they keep the action declaration order. Unlisted controllers go last.
/// </summary>
internal sealed class ContractOrderDocumentTransformer : IOpenApiDocumentTransformer
{
    private static readonly string[] ControllerOrder =
    [
        "CafePos.Api", "Auth", "Branding", "Categories", "Items", "ModifierLists", "Inventory", "Locations",
        "Orders", "TimeClock", "Users", "VariationOverrides"
    ];

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var ordered = document.Paths
            .Select((entry, index) => (entry, index, rank: Rank(entry.Value.Operations?.Values.FirstOrDefault()?.Tags?.FirstOrDefault()?.Name)))
            .OrderBy(x => x.rank).ThenBy(x => x.index)
            .Select(x => x.entry)
            .ToList();
        document.Paths = new OpenApiPaths();
        foreach (var (path, item) in ordered) document.Paths.Add(path, item);

        if (document.Tags is { Count: > 1 } tags)
        {
            var sorted = tags.Select((tag, index) => (tag, index)).OrderBy(x => Rank(x.tag.Name)).ThenBy(x => x.index).Select(x => x.tag).ToList();
            tags.Clear();
            foreach (var tag in sorted) tags.Add(tag);
        }
        return Task.CompletedTask;
    }

    private static int Rank(string? controller)
    {
        var index = controller is null ? -1 : Array.IndexOf(ControllerOrder, controller);
        return index < 0 ? ControllerOrder.Length : index;
    }
}
