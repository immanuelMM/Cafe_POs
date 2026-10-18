using CafePos.Api.Domain;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CafePos.Api.Data;

/// <summary>Resolves the active location for the current request (from the JWT location_id claim).</summary>
public interface ICurrentLocation
{
    Guid? LocationId { get; }
    Guid? UserId { get; }
}

public class CafeDbContext : IdentityUserContext<ApplicationUser, Guid>
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
    public DbSet<StaffAssignment> StaffAssignments => Set<StaffAssignment>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Item> Items => Set<Item>();
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
        // Money default: decimal(18,2). Quantity columns override to (18,3) below.
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
        configurationBuilder.Properties<decimal?>().HavePrecision(18, 2);
        configurationBuilder.Properties<string>().HaveMaxLength(500);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<ApplicationUser>().Property(u => u.DisplayName).HasMaxLength(200);

        b.Entity<Location>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.VatRatePercent).HasPrecision(5, 2);
            e.Property(x => x.Name).HasMaxLength(200);
        });

        b.Entity<StaffAssignment>(e =>
        {
            e.HasKey(x => new { x.UserId, x.LocationId });
            e.Property(x => x.Role).HasMaxLength(50);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
            e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId);
        });

        b.Entity<RefreshToken>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.TokenHash).HasMaxLength(100);
        });

        b.Entity<Category>(e => { e.HasKey(x => x.Id); e.Property(x => x.Name).HasMaxLength(200); });

        b.Entity<Item>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Variations).WithOne(v => v.Item).HasForeignKey(v => v.ItemId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.ModifierLists).WithOne().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ItemVariation>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Sku).IsUnique();
            e.Property(x => x.Sku).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(200);
        });

        b.Entity<ModifierList>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasMany(x => x.Modifiers).WithOne().HasForeignKey(m => m.ModifierListId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Modifier>(e => { e.HasKey(x => x.Id); e.Property(x => x.Name).HasMaxLength(200); });

        b.Entity<ItemModifierList>(e =>
        {
            e.HasKey(x => new { x.ItemId, x.ModifierListId });
            e.HasOne(x => x.ModifierList).WithMany().HasForeignKey(x => x.ModifierListId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Ingredient>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Unit).HasMaxLength(20);
            e.Property(x => x.LowStockThreshold).HasPrecision(18, 3);
        });

        b.Entity<RecipeLine>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ItemVariationId, x.IngredientId }).IsUnique();
            e.Property(x => x.Quantity).HasPrecision(18, 3);
            e.HasOne(x => x.Ingredient).WithMany().HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ItemVariation>().WithMany(v => v.Recipe).HasForeignKey(x => x.ItemVariationId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<IngredientStock>(e =>
        {
            e.HasKey(x => new { x.LocationId, x.IngredientId });
            e.Property(x => x.QuantityOnHand).HasPrecision(18, 3);
            e.HasOne(x => x.Ingredient).WithMany().HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<StockMovement>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.LocationId, x.IngredientId, x.CreatedAt });
            e.Property(x => x.QuantityDelta).HasPrecision(18, 3);
            e.Property(x => x.Reason).HasConversion<string>().HasMaxLength(20);
            e.HasOne<Ingredient>().WithMany().HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Order>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.LocationId, x.CreatedAt });
            e.HasIndex(x => new { x.LocationId, x.OrderNumber }).IsUnique();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.VatRatePercent).HasPrecision(5, 2);
            e.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Payments).WithOne().HasForeignKey(p => p.OrderId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<OrderItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ItemName).HasMaxLength(200);
            e.HasMany(x => x.Modifiers).WithOne().HasForeignKey(m => m.OrderItemId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<OrderItemModifier>(e => e.HasKey(x => x.Id));

        b.Entity<Payment>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Method).HasMaxLength(30);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        });

        b.Entity<IdempotencyRecord>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.LocationId, x.Key }).IsUnique();
            e.Property(x => x.Key).HasMaxLength(200);
            e.Property(x => x.ResponseJson).HasMaxLength(100000);
        });

        // Client/app-generated Guid keys: tell EF they are never store-generated so graph-discovered new rows are Added, not Modified.
        foreach (var et in b.Model.GetEntityTypes())
        {
            var pk = et.FindPrimaryKey();
            if (pk is { Properties.Count: 1 } && pk.Properties[0].ClrType == typeof(Guid) && et.ClrType != typeof(ApplicationUser))
                pk.Properties[0].ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
        }

        // Multi-location isolation: every ILocationScoped entity gets a global query filter
        // on LocationId == CurrentLocationId (plan 4.1), plus an index on LocationId.
        b.Entity<IngredientStock>().HasQueryFilter(x => x.LocationId == CurrentLocationId);
        b.Entity<StockMovement>().HasQueryFilter(x => x.LocationId == CurrentLocationId);
        b.Entity<Order>().HasQueryFilter(x => x.LocationId == CurrentLocationId);
        b.Entity<OrderItem>().HasQueryFilter(x => x.LocationId == CurrentLocationId);
        b.Entity<OrderItemModifier>().HasQueryFilter(x => x.LocationId == CurrentLocationId);
        b.Entity<Payment>().HasQueryFilter(x => x.LocationId == CurrentLocationId);
        b.Entity<IdempotencyRecord>().HasQueryFilter(x => x.LocationId == CurrentLocationId);
        b.Entity<OrderItem>().HasIndex(x => x.LocationId);
        b.Entity<OrderItemModifier>().HasIndex(x => x.LocationId);
        b.Entity<Payment>().HasIndex(x => x.LocationId);
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
