using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }

    // ── Business tables ──
    public DbSet<PlaySession>     PlaySessions      => Set<PlaySession>();
    public DbSet<BilliardTable>   BilliardTables    => Set<BilliardTable>();
    public DbSet<TableType>       TableTypes        => Set<TableType>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<Product>         Products          => Set<Product>();
    public DbSet<Order>           Orders            => Set<Order>();
    public DbSet<OrderDetail>     OrderDetails      => Set<OrderDetail>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // ── Identity column config ──
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

        // ── PlaySessions ──
        var ps = builder.Entity<PlaySession>();
        ps.Property(x => x.StartAtUtc).HasColumnType("datetime2(0)");
        ps.Property(x => x.EndAtUtc).HasColumnType("datetime2(0)");
        ps.Property(x => x.HourlyRateSnapshot).HasColumnType("decimal(18,2)");
        ps.Property(x => x.PlaytimeAmount).HasColumnType("decimal(18,2)");
        ps.HasOne(x => x.Table).WithMany()
            .HasForeignKey(x => x.TableId)
            .OnDelete(DeleteBehavior.Restrict);
        // EF không dùng navigation cho OpenedById/ClosedById/BookingId/CustomerId
        // (đây là FK sang AspNetUsers và Bookings, identity đã xử lý riêng)
        // — chỉ đậnh nghĩa chúng là scalar column, không tạo navigation property
        ps.Property(x => x.OpenedById).HasMaxLength(450).IsRequired();
        ps.Property(x => x.ClosedById).HasMaxLength(450);
        ps.Property(x => x.BookingId);
        ps.Property(x => x.CustomerId).HasMaxLength(450);

        // ── Products & ProductCategories ──
        var pc = builder.Entity<ProductCategory>();
        pc.Property(x => x.Name).HasMaxLength(60).IsRequired();

        var p = builder.Entity<Product>();
        p.Property(x => x.Name).HasMaxLength(100).IsRequired();
        p.Property(x => x.Price).HasColumnType("decimal(18,2)");
        p.HasOne(x => x.Category).WithMany(c => c.Products)
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // ── Orders & OrderDetails ──
        var o = builder.Entity<Order>();
        o.Property(x => x.OrderTimeUtc).HasColumnType("datetime2(0)");
        o.Property(x => x.Status).HasMaxLength(20).IsRequired();
        o.HasOne(x => x.Session).WithMany(s => s.Orders)
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Restrict);

        var od = builder.Entity<OrderDetail>();
        od.Property(x => x.UnitPrice).HasColumnType("decimal(18,2)");
        od.Property(x => x.Subtotal).HasColumnType("decimal(18,2)").ValueGeneratedOnAddOrUpdate();
        od.HasOne(x => x.Order).WithMany(ord => ord.OrderDetails)
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        od.HasOne(x => x.Product).WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

