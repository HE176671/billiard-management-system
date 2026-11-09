using Bms.Web.Data;
using Microsoft.AspNetCore.Identity;

namespace Bms.Web.Data;

/// <summary>
/// Chỉ chạy trong môi trường Development.
/// Đặt mật khẩu cho các tài khoản nội bộ (Admin & Staff) nếu PasswordHash còn NULL.
/// Mật khẩu lấy từ cấu hình — không viết cứng trong source code.
/// </summary>
public static class DbInitializer
{
    public static async Task SeedDevelopmentPasswordsAsync(
        UserManager<ApplicationUser> userManager,
        IConfiguration config,
        ILogger logger)
    {
        var adminPassword = config["DevSeed:AdminPassword"] ?? "Admin@123";

        var admin = await userManager.FindByNameAsync("admin.demo");
        if (admin == null)
        {
            logger.LogError("Không tìm thấy admin.demo — hãy chạy 03_DemoData.sql trước.");
            return;
        }

        if (!await userManager.HasPasswordAsync(admin))
        {
            var result = await userManager.AddPasswordAsync(admin, adminPassword);
            if (result.Succeeded)
                logger.LogInformation("Đã đặt mật khẩu cho admin.demo.");
            else
                logger.LogError("Đặt mật khẩu admin.demo thất bại: {Errors}",
                    string.Join("; ", result.Errors.Select(e => e.Description)));
        }
        else
        {
            logger.LogInformation("admin.demo đã có mật khẩu, bỏ qua.");
        }

        // Đặt mật khẩu demo cho tài khoản Quản lý (Manager)
        var managerPassword = config["DevSeed:ManagerPassword"] ?? "Manager@123";
        var managerUser = await userManager.FindByNameAsync("manager.demo");
        if (managerUser != null)
        {
            if (!await userManager.HasPasswordAsync(managerUser))
            {
                var result = await userManager.AddPasswordAsync(managerUser, managerPassword);
                if (result.Succeeded)
                    logger.LogInformation("Đã đặt mật khẩu cho manager.demo.");
                else
                    logger.LogError("Đặt mật khẩu manager.demo thất bại: {Errors}",
                        string.Join("; ", result.Errors.Select(e => e.Description)));
            }
        }

        // Đặt mật khẩu demo cho tài khoản Nhân viên (Staff)
        var staffPassword = config["DevSeed:StaffPassword"] ?? "Staff@123";
        var demoStaff = new[] { "staff.demo01", "staff.demo02" };
        foreach (var staffUsername in demoStaff)
        {
            var staff = await userManager.FindByNameAsync(staffUsername);
            if (staff != null)
            {
                if (!await userManager.HasPasswordAsync(staff))
                {
                    var result = await userManager.AddPasswordAsync(staff, staffPassword);
                    if (result.Succeeded)
                        logger.LogInformation("Đã đặt mật khẩu cho {StaffUsername}.", staffUsername);
                    else
                        logger.LogError("Đặt mật khẩu {StaffUsername} thất bại: {Errors}", staffUsername,
                            string.Join("; ", result.Errors.Select(e => e.Description)));
                }
                else
                {
                    logger.LogInformation("{StaffUsername} đã có mật khẩu, bỏ qua.", staffUsername);
                }
            }
        }
    }
}
