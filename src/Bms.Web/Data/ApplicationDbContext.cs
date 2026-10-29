using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }

    // Custom business tables (Hung)
    public DbSet<TableType>           TableTypes           => Set<TableType>();
    public DbSet<PricingConfig>       PricingConfigs       => Set<PricingConfig>();
    public DbSet<PaymentTransaction>  PaymentTransactions  => Set<PaymentTransaction>();
    public DbSet<BilliardTable>       BilliardTables       => Set<BilliardTable>();
    public DbSet<WorkShift>           WorkShifts           => Set<WorkShift>();

    // Loyalty tables (Dung - TV2)
    public DbSet<MembershipTier>      MembershipTiers      => Set<MembershipTier>();
    public DbSet<PointTransaction>    PointTransactions    => Set<PointTransaction>();
    public DbSet<Review>              Reviews              => Set<Review>();
    public DbSet<Notification>        Notifications        => Set<Notification>();

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
        user.HasIndex(x => x.NormalizedEmail).HasDatabaseName("EmailIndex")
            .IsUnique().HasFilter("[NormalizedEmail] IS NOT NULL");
        user.HasIndex(x => x.PhoneNumber).HasDatabaseName("UX_User_Phone")
            .IsUnique().HasFilter("[PhoneNumber] IS NOT NULL");
        user.HasIndex(x => x.EmployeeCode).HasDatabaseName("UX_User_EmployeeCode")
            .IsUnique().HasFilter("[EmployeeCode] IS NOT NULL");

        // ---- Loyalty (TV2) ----
        user.Property(x => x.TotalSpend).HasColumnType("decimal(18,2)");
        user.Property(x => x.LastPaidDate).HasColumnType("datetime2");
        user.HasOne(x => x.Tier).WithMany(t => t.Users)
            .HasForeignKey(x => x.TierId).OnDelete(DeleteBehavior.SetNull);

        // Identity key length fix
        builder.Entity<IdentityUserLogin<string>>().Property(x => x.LoginProvider).HasMaxLength(128);
        builder.Entity<IdentityUserLogin<string>>().Property(x => x.ProviderKey).HasMaxLength(128);
        builder.Entity<IdentityUserToken<string>>().Property(x => x.LoginProvider).HasMaxLength(128);
        builder.Entity<IdentityUserToken<string>>().Property(x => x.Name).HasMaxLength(128);

        // MembershipTiers
        builder.Entity<MembershipTier>(e =>
        {
            e.ToTable("MembershipTiers");
            e.HasKey(x => x.Id);
            e.Property(x => x.TierName).HasMaxLength(50).IsRequired();
            e.Property(x => x.DiscountPercent).HasColumnType("decimal(5,2)");
            e.Property(x => x.SpendThreshold).HasColumnType("decimal(18,2)");
        });

        // PointTransactions (TV2)
        var pt = builder.Entity<PointTransaction>();
        pt.ToTable("PointTransactions");
        pt.Property(x => x.CustomerId).HasMaxLength(450).IsRequired();
        pt.Property(x => x.TransactionType).HasColumnType("varchar(20)").IsRequired();
        pt.Property(x => x.CreatedAt).HasColumnType("datetime2");
        pt.HasOne(x => x.Customer).WithMany(u => u.PointTransactions)
            .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);

        // Reviews (TV2)
        var review = builder.Entity<Review>();
        review.ToTable("Reviews");
        review.Property(x => x.CustomerId).HasMaxLength(450).IsRequired();
        review.Property(x => x.Rating).HasColumnType("tinyint");
        review.Property(x => x.Comment).HasMaxLength(500);
        review.Property(x => x.CreatedAtUtc).HasColumnType("datetime2");
        review.HasOne(x => x.Customer).WithMany(u => u.Reviews)
            .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);

        // Notifications (TV2)
        var noti = builder.Entity<Notification>();
        noti.ToTable("Notifications");
        noti.Property(x => x.UserId).HasMaxLength(450).IsRequired();
        noti.Property(x => x.Title).HasMaxLength(200).IsRequired();
        noti.Property(x => x.Type).HasColumnType("varchar(50)");
        noti.Property(x => x.CreatedAt).HasColumnType("datetime2");
        noti.HasOne(x => x.User).WithMany(u => u.Notifications)
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        // TableTypes (Hung)
        builder.Entity<TableType>(e =>
        {
            e.ToTable("TableTypes");
            e.Property(x => x.Name).HasMaxLength(50).IsRequired();
            e.Property(x => x.HourlyRate).HasColumnType("decimal(18,2)");
        });

        // PricingConfigs (Hung)
        builder.Entity<PricingConfig>(e =>
        {
            e.ToTable("PricingConfigs");
            e.Property(x => x.HourlyRate).HasColumnType("decimal(18,2)");
            e.HasOne(x => x.TableType).WithMany().HasForeignKey(x => x.TableTypeId);
        });

        // PaymentTransactions (Hung)
        builder.Entity<PaymentTransaction>(e =>
        {
            e.ToTable("PaymentTransactions");
            e.Property(x => x.AmountPaid).HasColumnType("decimal(18,2)");
            e.Property(x => x.PaymentMethod).HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.TransactionTimeUtc).HasColumnType("datetime2(0)");
            e.Property(x => x.ReceivedById).HasMaxLength(450);
            e.HasOne<WorkShift>().WithMany().HasForeignKey(x => x.WorkShiftId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ReceivedById).OnDelete(DeleteBehavior.Restrict);
        });

        // WorkShifts (Hung)
        builder.Entity<WorkShift>(e =>
        {
            e.ToTable("WorkShifts");
            e.Property(x => x.StaffId).HasMaxLength(450).IsRequired();
            e.Property(x => x.ClosedById).HasMaxLength(450);
            e.Property(x => x.StartTimeUtc).HasColumnType("datetime2(0)");
            e.Property(x => x.EndTimeUtc).HasColumnType("datetime2(0)");
            e.Property(x => x.StartingCash).HasColumnType("decimal(18,2)");
            e.Property(x => x.EndingCash).HasColumnType("decimal(18,2)");
            e.Property(x => x.CashCollected).HasColumnType("decimal(18,2)");
            e.Property(x => x.ExpectedCash).HasColumnType("decimal(18,2)");
            e.Property(x => x.Status).HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Ignore(x => x.CashDifference);
            e.HasOne(x => x.Staff).WithMany().HasForeignKey(x => x.StaffId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ClosedBy).WithMany().HasForeignKey(x => x.ClosedById).OnDelete(DeleteBehavior.Restrict);
        });

        // BilliardTables (Hung)
        builder.Entity<BilliardTable>(e =>
        {
            e.ToTable("BilliardTables");
            e.Property(x => x.TableCode).HasMaxLength(20);
            e.Property(x => x.Status).HasMaxLength(20).IsUnicode(false);
        });
    }
}
