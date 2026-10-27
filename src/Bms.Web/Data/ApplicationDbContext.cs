using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }

    // Custom business tables
    public DbSet<MembershipTier>  MembershipTiers  => Set<MembershipTier>();
    public DbSet<TableType>       TableTypes        => Set<TableType>();
    public DbSet<PricingConfig>   PricingConfigs    => Set<PricingConfig>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<BilliardTable> BilliardTables => Set<BilliardTable>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // ApplicationUser extensions
        var user = builder.Entity<ApplicationUser>();
        user.Property(x => x.FullName).HasMaxLength(100).IsRequired();
        user.Property(x => x.PhoneNumber).HasMaxLength(20);
        user.Property(x => x.EmployeeCode).HasMaxLength(20);
        user.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(0)");
        user.Property(x => x.HireDate).HasColumnType("date");
        user.HasOne(x => x.Tier).WithMany().HasForeignKey(x => x.TierId).OnDelete(DeleteBehavior.Restrict);
        user.HasIndex(x => x.NormalizedEmail).HasDatabaseName("EmailIndex")
            .IsUnique().HasFilter("[NormalizedEmail] IS NOT NULL");
        user.HasIndex(x => x.PhoneNumber).HasDatabaseName("UX_User_Phone")
            .IsUnique().HasFilter("[PhoneNumber] IS NOT NULL");
        user.HasIndex(x => x.EmployeeCode).HasDatabaseName("UX_User_EmployeeCode")
            .IsUnique().HasFilter("[EmployeeCode] IS NOT NULL");

        // Identity key length fix
        builder.Entity<IdentityUserLogin<string>>().Property(x => x.LoginProvider).HasMaxLength(128);
        builder.Entity<IdentityUserLogin<string>>().Property(x => x.ProviderKey).HasMaxLength(128);
        builder.Entity<IdentityUserToken<string>>().Property(x => x.LoginProvider).HasMaxLength(128);
        builder.Entity<IdentityUserToken<string>>().Property(x => x.Name).HasMaxLength(128);

        // MembershipTiers
        builder.Entity<MembershipTier>(e =>
        {
            e.ToTable("MembershipTiers");
            e.Property(x => x.TierName).HasMaxLength(50).IsRequired();
            e.Property(x => x.DiscountPercent).HasColumnType("decimal(5,2)");
        });

        // TableTypes
        builder.Entity<TableType>(e =>
        {
            e.ToTable("TableTypes");
            e.Property(x => x.Name).HasMaxLength(50).IsRequired();
            e.Property(x => x.HourlyRate).HasColumnType("decimal(18,2)");
        });

        // PricingConfigs
        builder.Entity<PricingConfig>(e =>
        {
            e.ToTable("PricingConfigs");
            e.Property(x => x.HourlyRate).HasColumnType("decimal(18,2)");
            e.HasOne(x => x.TableType).WithMany().HasForeignKey(x => x.TableTypeId);
        });
        builder.Entity<PaymentTransaction>(e =>
        {
            e.ToTable("PaymentTransactions");
            e.Property(x => x.AmountPaid).HasColumnType("decimal(18,2)");
            e.Property(x => x.PaymentMethod).HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.TransactionTimeUtc).HasColumnType("datetime2(0)");
        });
        builder.Entity<BilliardTable>(e =>
        {
            e.ToTable("BilliardTables");
            e.Property(x => x.TableCode).HasMaxLength(20);
            e.Property(x => x.Status).HasMaxLength(20).IsUnicode(false);
        });
    }
}
