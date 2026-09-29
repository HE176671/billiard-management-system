using Bms.Web.Data;
using Microsoft.AspNetCore.Identity;

namespace Bms.Web.Data;

/// <summary>
/// Chỉ chạy trong môi trường Development.
/// Đặt mật khẩu cho admin.demo nếu PasswordHash còn NULL.
/// Mật khẩu lấy từ cấu hình — không viết cứng trong source code.
/// </summary>
public static class DbInitializer
{
    public static async Task SeedDevelopmentPasswordsAsync(
        UserManager<ApplicationUser> userManager,
        IConfiguration config,
        ILogger logger)
    {
        var adminPassword = config["DevSeed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(adminPassword))
        {
            logger.LogWarning(
                "DevSeed:AdminPassword chưa được cấu hình. " +
                "Chạy: dotnet user-secrets set \"DevSeed:AdminPassword\" \"<mật_khẩu>\"");
            return;
        }

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
    }
}
