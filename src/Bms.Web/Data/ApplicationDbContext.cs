using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }

    // Custom business tables (Hung)
    public DbSet<TableType>              TableTypes              => Set<TableType>();
    public DbSet<PricingConfig>          PricingConfigs          => Set<PricingConfig>();
    public DbSet<PaymentTransaction>     PaymentTransactions     => Set<PaymentTransaction>();
    public DbSet<BilliardTable>          BilliardTables          => Set<BilliardTable>();
    public DbSet<WorkShift>              WorkShifts              => Set<WorkShift>();

    // Loyalty & CRM tables (Dung - TV2)
    public DbSet<MembershipTier>         MembershipTiers         => Set<MembershipTier>();
    public DbSet<PointTransaction>       PointTransactions       => Set<PointTransaction>();
    public DbSet<Review>                 Reviews                 => Set<Review>();
    public DbSet<Notification>           Notifications           => Set<Notification>();
    public DbSet<RewardItemEntity>       RewardItems             => Set<RewardItemEntity>();
    public DbSet<CustomerVoucher>        CustomerVouchers        => Set<CustomerVoucher>();
    public DbSet<PointAdjustmentRequest> PointAdjustmentRequests => Set<PointAdjustmentRequest>();
    public DbSet<CustomerNote>           CustomerNotes           => Set<CustomerNote>();
    public DbSet<Campaign>               Campaigns               => Set<Campaign>();
    public DbSet<CustomerAuditLog>       CustomerAuditLogs       => Set<CustomerAuditLog>();

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

        // ---- Loyalty & CRM (TV2) ----
        user.Property(x => x.TotalSpend).HasColumnType("decimal(18,2)");
        user.Property(x => x.LastPaidDate).HasColumnType("datetime2");
        user.Property(x => x.DateOfBirth).HasColumnType("date");
        user.Property(x => x.MarketingOptIn).HasDefaultValue(true);
        user.Property(x => x.AccountSource).HasColumnType("varchar(20)").HasDefaultValue("Self").IsRequired();
        user.Property(x => x.ClaimedAtUtc).HasColumnType("datetime2(0)");
        user.Property(x => x.TierAchievedAtUtc).HasColumnType("datetime2(0)");
        user.Property(x => x.TierReviewAtUtc).HasColumnType("datetime2(0)");
        user.Property(x => x.TierGraceUntilUtc).HasColumnType("datetime2(0)");
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
            e.Property(x => x.BirthdayVoucherValue).HasColumnType("decimal(18,2)");
        });

        // PointTransactions (TV2)
        var pt = builder.Entity<PointTransaction>();
        pt.ToTable("PointTransactions");
        pt.Property(x => x.CustomerId).HasMaxLength(450).IsRequired();
        pt.Property(x => x.TransactionType).HasColumnType("varchar(20)").IsRequired();
        pt.Property(x => x.CreatedAt).HasColumnType("datetime2");
        pt.Property(x => x.ReferenceCode).HasColumnType("varchar(50)");
        pt.Property(x => x.Reason).HasMaxLength(300);
        pt.Property(x => x.CreatedById).HasMaxLength(450);
        pt.Property(x => x.SpendAmount).HasColumnType("decimal(18,2)");
        pt.HasIndex(x => new { x.ReferenceCode, x.TransactionType })
            .HasDatabaseName("UX_PointTxn_Earn_Ref")
            .IsUnique()
            .HasFilter("[ReferenceCode] IS NOT NULL");
        pt.HasOne(x => x.Customer).WithMany(u => u.PointTransactions)
            .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        pt.HasOne(x => x.CreatedBy).WithMany()
            .HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);

        // RewardItems (TV2)
        builder.Entity<RewardItemEntity>(e =>
        {
            e.ToTable("RewardItems");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasColumnType("varchar(30)").IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Description).HasMaxLength(300);
            e.Property(x => x.RewardType).HasColumnType("varchar(20)").IsRequired();
            e.Property(x => x.DiscountValue).HasColumnType("decimal(18,2)");
            e.HasOne(x => x.MinTier).WithMany().HasForeignKey(x => x.MinTierId).OnDelete(DeleteBehavior.Restrict);
        });

        // Campaigns (TV2)
        builder.Entity<Campaign>(e =>
        {
            e.ToTable("Campaigns");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.CampaignType).HasColumnType("varchar(20)").IsRequired();
            e.Property(x => x.TargetSegment).HasColumnType("varchar(20)");
            e.Property(x => x.MessageTitle).HasMaxLength(200).IsRequired();
            e.Property(x => x.MessageContent).HasMaxLength(1000).IsRequired();
            e.Property(x => x.Status).HasColumnType("varchar(20)").IsRequired();
            e.Property(x => x.CreatedById).HasMaxLength(450).IsRequired();
            e.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(0)");
            e.Property(x => x.SentAtUtc).HasColumnType("datetime2(0)");
            e.HasOne(x => x.TargetTier).WithMany().HasForeignKey(x => x.TargetTierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.RewardItem).WithMany().HasForeignKey(x => x.RewardItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
        });

        // CustomerVouchers (TV2)
        builder.Entity<CustomerVoucher>(e =>
        {
            e.ToTable("CustomerVouchers");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasColumnType("varchar(20)").IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.CustomerId).HasMaxLength(450).IsRequired();
            e.Property(x => x.Title).HasMaxLength(150).IsRequired();
            e.Property(x => x.VoucherType).HasColumnType("varchar(20)").IsRequired();
            e.Property(x => x.DiscountValue).HasColumnType("decimal(18,2)");
            e.Property(x => x.Status).HasColumnType("varchar(20)").IsRequired();
            e.Property(x => x.IssuedAtUtc).HasColumnType("datetime2(0)");
            e.Property(x => x.ExpiresAtUtc).HasColumnType("datetime2(0)");
            e.Property(x => x.UsedAtUtc).HasColumnType("datetime2(0)");
            e.Property(x => x.UsedById).HasMaxLength(450);
            e.Property(x => x.UsedReferenceCode).HasColumnType("varchar(50)");
            e.HasIndex(x => new { x.CustomerId, x.Status }).HasDatabaseName("IX_Voucher_Customer_Status");
            e.HasOne(x => x.Customer).WithMany(u => u.Vouchers).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.RewardItem).WithMany().HasForeignKey(x => x.RewardItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Campaign).WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.UsedBy).WithMany().HasForeignKey(x => x.UsedById).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.PointTransaction).WithMany().HasForeignKey(x => x.PointTransactionId).OnDelete(DeleteBehavior.Restrict);
        });

        // PointAdjustmentRequests (TV2)
        builder.Entity<PointAdjustmentRequest>(e =>
        {
            e.ToTable("PointAdjustmentRequests");
            e.HasKey(x => x.Id);
            e.Property(x => x.CustomerId).HasMaxLength(450).IsRequired();
            e.Property(x => x.Reason).HasMaxLength(300).IsRequired();
            e.Property(x => x.Status).HasColumnType("varchar(20)").IsRequired();
            e.Property(x => x.RequestedById).HasMaxLength(450).IsRequired();
            e.Property(x => x.RequestedAtUtc).HasColumnType("datetime2(0)");
            e.Property(x => x.ReviewedById).HasMaxLength(450);
            e.Property(x => x.ReviewedAtUtc).HasColumnType("datetime2(0)");
            e.Property(x => x.ReviewNote).HasMaxLength(300);
            e.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.RequestedBy).WithMany().HasForeignKey(x => x.RequestedById).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ReviewedBy).WithMany().HasForeignKey(x => x.ReviewedById).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.PointTransaction).WithMany().HasForeignKey(x => x.PointTransactionId).OnDelete(DeleteBehavior.Restrict);
        });

        // CustomerNotes (TV2)
        builder.Entity<CustomerNote>(e =>
        {
            e.ToTable("CustomerNotes");
            e.HasKey(x => x.Id);
            e.Property(x => x.CustomerId).HasMaxLength(450).IsRequired();
            e.Property(x => x.Content).HasMaxLength(500).IsRequired();
            e.Property(x => x.CreatedById).HasMaxLength(450).IsRequired();
            e.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(0)");
            e.HasOne(x => x.Customer).WithMany(u => u.Notes).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
        });

        // CustomerAuditLogs (TV2)
        builder.Entity<CustomerAuditLog>(e =>
        {
            e.ToTable("CustomerAuditLogs");
            e.HasKey(x => x.Id);
            e.Property(x => x.CustomerId).HasMaxLength(450).IsRequired();
            e.Property(x => x.Action).HasColumnType("varchar(30)").IsRequired();
            e.Property(x => x.Detail).HasMaxLength(1000);
            e.Property(x => x.ActorId).HasMaxLength(450);
            e.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(0)");
            e.HasOne(x => x.Customer).WithMany(u => u.AuditLogs).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Actor).WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        });

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
