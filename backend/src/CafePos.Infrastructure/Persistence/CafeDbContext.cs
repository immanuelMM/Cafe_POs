using CafePos.Application.Common;
using CafePos.Domain;
using CafePos.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CafePos.Infrastructure.Persistence;

/// <summary>
/// The EF Core context and unit of work. Per-entity mapping lives in <c>Configurations/</c>; the multi-location global query
/// filters and the SaveChanges write guard stay here because they depend on the current location.
/// </summary>
public class CafeDbContext : IdentityUserContext<ApplicationUser, Guid>, IUnitOfWork
{
    private readonly ICurrentLocation? _current;

    public CafeDbContext(DbContextOptions<CafeDbContext> options, ICurrentLocation? current = null) : base(options)
    {
        _current = current;
    }

    /// <summary>
    /// Referenced by the global query filters. Guid.Empty (no location, for example an anonymous request) matches nothing.
    /// </summary>
    public Guid CurrentLocationId => _current?.LocationId ?? Guid.Empty;

    public DbSet<Location> Locations => Set<Location>();
    public DbSet<BrandSettings> BrandSettings => Set<BrandSettings>();
    public DbSet<BrandDesign> BrandDesigns => Set<BrandDesign>();
    public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();
    public DbSet<StaffAssignment> StaffAssignments => Set<StaffAssignment>();
    public DbSet<ItemVariationLocationOverride> ItemVariationLocationOverrides => Set<ItemVariationLocationOverride>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<ItemImage> ItemImages => Set<ItemImage>();
    public DbSet<ItemVariation> ItemVariations => Set<ItemVariation>();
    public DbSet<ModifierList> ModifierLists => Set<ModifierList>();
    public DbSet<Modifier> Modifiers => Set<Modifier>();
    public DbSet<ItemModifierList> ItemModifierLists => Set<ItemModifierList>();
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<RecipeLine> RecipeLines => Set<RecipeLine>();
    public DbSet<IngredientStock> IngredientStocks => Set<IngredientStock>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderItemModifier> OrderItemModifiers => Set<OrderItemModifier>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Money default: decimal(18,2). Quantity columns override to (18,3) in their configuration.
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
        configurationBuilder.Properties<decimal?>().HavePrecision(18, 2);
        configurationBuilder.Properties<string>().HaveMaxLength(500);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.ApplyConfigurationsFromAssembly(typeof(CafeDbContext).Assembly);

        // Client/app-generated Guid keys: tell EF they are never store-generated so graph-discovered new rows are Added, not Modified.
        foreach (var et in b.Model.GetEntityTypes())
        {
            var pk = et.FindPrimaryKey();
            if (pk is { Properties.Count: 1 } && pk.Properties[0].ClrType == typeof(Guid) && et.ClrType != typeof(ApplicationUser))
                pk.Properties[0].ValueGenerated = ValueGenerated.Never;
        }

        // Multi-location isolation: every ILocationScoped entity gets a global query filter
        // on LocationId == CurrentLocationId (plan 4.1), plus an index on LocationId.
        // StaffAssignment is filtered too (not ILocationScoped: an Owner/Admin must be able to write assignments for the target
        // location). Auth flows and Owner-gated code read it with an explicit IgnoreQueryFilters() inside the repositories.
        b.Entity<StaffAssignment>().HasQueryFilter(x => x.LocationId == CurrentLocationId);
        b.Entity<IngredientStock>().HasQueryFilter(x => x.LocationId == CurrentLocationId);
        b.Entity<StockMovement>().HasQueryFilter(x => x.LocationId == CurrentLocationId);
        b.Entity<TimeEntry>().HasQueryFilter(x => x.LocationId == CurrentLocationId);
        b.Entity<Order>().HasQueryFilter(x => x.LocationId == CurrentLocationId);
        b.Entity<OrderItem>().HasQueryFilter(x => x.LocationId == CurrentLocationId);
        b.Entity<OrderItemModifier>().HasQueryFilter(x => x.LocationId == CurrentLocationId);
        b.Entity<Payment>().HasQueryFilter(x => x.LocationId == CurrentLocationId);
        b.Entity<IdempotencyRecord>().HasQueryFilter(x => x.LocationId == CurrentLocationId);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        EnforceInvariants();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        EnforceInvariants();
        return base.SaveChanges();
    }

    // ---- IUnitOfWork ----
    async Task<IUnitOfWorkTransaction> IUnitOfWork.BeginTransactionAsync(CancellationToken ct) =>
        new EfTransaction(await Database.BeginTransactionAsync(ct));

    void IUnitOfWork.ClearTracking() => ChangeTracker.Clear();

    private sealed class EfTransaction(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction inner) : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken ct = default) => inner.CommitAsync(ct);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    /// <summary>
    /// Stamps LocationId on new location-scoped rows, refuses writes to another location,
    /// and keeps StockMovement append-only.
    /// </summary>
    private void EnforceInvariants()
    {
        var current = _current?.LocationId;
        foreach (var entry in ChangeTracker.Entries())
        {
            var changed = entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted;
            if (!changed) continue;

            if (entry.Entity is StockMovement && entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("StockMovement is an append-only ledger; post an offsetting movement instead.");

            if (entry.Entity is ILocationScoped scoped && current is not null)
            {
                if (entry.State == EntityState.Added && scoped.LocationId == Guid.Empty)
                    scoped.LocationId = current.Value;
                if (scoped.LocationId != current.Value)
                    throw new InvalidOperationException("Cross-location write blocked.");
            }
        }
    }
}
