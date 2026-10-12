using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }

    public DbSet<WorkShift> WorkShifts => Set<WorkShift>();

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

        // ── WorkShift (UC45) — ánh xạ sang bảng WorkShifts đã có sẵn ──
        var shift = builder.Entity<WorkShift>();
        shift.ToTable("WorkShifts");
        shift.HasKey(x => x.Id);
        shift.Property(x => x.StartTimeUtc).HasColumnType("datetime2(0)");
        shift.Property(x => x.EndTimeUtc).HasColumnType("datetime2(0)");
        shift.Property(x => x.StartingCash).HasColumnType("decimal(18,2)");
        shift.Property(x => x.EndingCash).HasColumnType("decimal(18,2)");
        shift.Property(x => x.Status).HasMaxLength(20);
        shift.Property(x => x.Notes).HasMaxLength(500);
        shift.HasOne(x => x.Staff)
             .WithMany()
             .HasForeignKey(x => x.StaffId)
             .OnDelete(DeleteBehavior.Restrict);
    }
}
