using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }

    public DbSet<MembershipTier> MembershipTiers => Set<MembershipTier>();
    public DbSet<PointTransaction> PointTransactions => Set<PointTransaction>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Notification> Notifications => Set<Notification>();

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

        // ---- Loyalty (TV2): map vào schema đã có sẵn trên SQL Server, KHÔNG dùng migration ----
        user.Property(x => x.TotalSpend).HasColumnType("decimal(18,2)");
        user.Property(x => x.LastPaidDate).HasColumnType("datetime2");
        user.HasOne(x => x.Tier).WithMany(t => t.Users)
            .HasForeignKey(x => x.TierId).OnDelete(DeleteBehavior.SetNull);

        var tier = builder.Entity<MembershipTier>();
        tier.ToTable("MembershipTiers");
        tier.HasKey(x => x.Id);
        tier.Property(x => x.TierName).HasMaxLength(50).IsRequired();
        tier.Property(x => x.DiscountPercent).HasColumnType("decimal(5,2)");
        tier.Property(x => x.SpendThreshold).HasColumnType("decimal(18,2)");

        var pt = builder.Entity<PointTransaction>();
        pt.ToTable("PointTransactions");
        pt.Property(x => x.CustomerId).HasMaxLength(450).IsRequired();
        pt.Property(x => x.TransactionType).HasColumnType("varchar(20)").IsRequired();
        pt.Property(x => x.CreatedAt).HasColumnType("datetime2");
        pt.HasOne(x => x.Customer).WithMany(u => u.PointTransactions)
            .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);

        var review = builder.Entity<Review>();
        review.ToTable("Reviews");
        review.Property(x => x.CustomerId).HasMaxLength(450).IsRequired();
        review.Property(x => x.Rating).HasColumnType("tinyint");
        review.Property(x => x.Comment).HasMaxLength(500);
        review.Property(x => x.CreatedAtUtc).HasColumnType("datetime2");
        review.HasOne(x => x.Customer).WithMany(u => u.Reviews)
            .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);

        var noti = builder.Entity<Notification>();
        noti.ToTable("Notifications");
        noti.Property(x => x.UserId).HasMaxLength(450).IsRequired();
        noti.Property(x => x.Title).HasMaxLength(200).IsRequired();
        noti.Property(x => x.Type).HasColumnType("varchar(50)");
        noti.Property(x => x.CreatedAt).HasColumnType("datetime2");
        noti.HasOne(x => x.User).WithMany(u => u.Notifications)
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        builder.Entity<IdentityUserLogin<string>>().Property(x => x.LoginProvider).HasMaxLength(128);
        builder.Entity<IdentityUserLogin<string>>().Property(x => x.ProviderKey).HasMaxLength(128);
        builder.Entity<IdentityUserToken<string>>().Property(x => x.LoginProvider).HasMaxLength(128);
        builder.Entity<IdentityUserToken<string>>().Property(x => x.Name).HasMaxLength(128);
    }
}
