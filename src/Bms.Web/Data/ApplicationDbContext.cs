using Bms.Web.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }

    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Combo> Combos => Set<Combo>();
    public DbSet<ComboItem> ComboItems => Set<ComboItem>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        var user = builder.Entity<ApplicationUser>();
        user.Property(x => x.FullName).HasMaxLength(100).IsRequired();
        user.Property(x => x.PhoneNumber).HasMaxLength(20);
        user.Property(x => x.EmployeeCode).HasMaxLength(20);
        user.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(0)");
        user.Property(x => x.HireDate).HasColumnType("date");
        user.HasIndex(x => x.NormalizedEmail).HasDatabaseName("EmailIndex")
            .IsUnique().HasFilter("[NormalizedEmail] IS NOT NULL");
        user.HasIndex(x => x.PhoneNumber).HasDatabaseName("UX_User_Phone")
            .IsUnique().HasFilter("[PhoneNumber] IS NOT NULL");
        user.HasIndex(x => x.EmployeeCode).HasDatabaseName("UX_User_EmployeeCode")
            .IsUnique().HasFilter("[EmployeeCode] IS NOT NULL");
        builder.Entity<IdentityUserLogin<string>>().Property(x => x.LoginProvider).HasMaxLength(128);
        builder.Entity<IdentityUserLogin<string>>().Property(x => x.ProviderKey).HasMaxLength(128);
        builder.Entity<IdentityUserToken<string>>().Property(x => x.LoginProvider).HasMaxLength(128);
        builder.Entity<IdentityUserToken<string>>().Property(x => x.Name).HasMaxLength(128);

        // ── Module F&B Catalog, Inventory & Combos (UC41 & UC42) ──
        builder.Entity<ProductCategory>(e =>
        {
            e.ToTable("ProductCategories", "dbo");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(60).IsRequired();
            e.HasIndex(x => x.Name).HasDatabaseName("UQ_ProductCategories_Name").IsUnique();
        });

        builder.Entity<Product>(e =>
        {
            e.ToTable("Products", "dbo");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Price).HasColumnType("decimal(18,2)").IsRequired();
            e.Property(x => x.StockQuantity).HasDefaultValue(0).IsRequired();
            e.Property(x => x.IsActive).HasDefaultValue(true).IsRequired();
            e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();

            e.HasIndex(x => x.Name).HasDatabaseName("UQ_Products_Name").IsUnique();
            e.HasIndex(x => new { x.CategoryId, x.IsActive }).HasDatabaseName("IX_Products_Category_Active");

            e.HasOne(x => x.Category)
                .WithMany(c => c.Products)
                .HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Combo>(e =>
        {
            e.ToTable("Combos", "dbo");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Price).HasColumnType("decimal(18,2)").IsRequired();
            e.Property(x => x.PlaytimeHours).HasColumnType("decimal(5,2)").IsRequired();
            e.Property(x => x.IsActive).HasDefaultValue(true).IsRequired();

            e.HasIndex(x => x.Name).HasDatabaseName("UQ_Combos_Name").IsUnique();
        });

        builder.Entity<ComboItem>(e =>
        {
            e.ToTable("ComboItems", "dbo");
            e.HasKey(x => new { x.ComboId, x.ProductId });
            e.Property(x => x.Quantity).IsRequired();

            e.HasIndex(x => x.ProductId).HasDatabaseName("IX_ComboItems_ProductId");

            e.HasOne(x => x.Combo)
                .WithMany(c => c.ComboItems)
                .HasForeignKey(x => x.ComboId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Product)
                .WithMany(p => p.ComboItems)
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
