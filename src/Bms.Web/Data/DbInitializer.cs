using System.Text.RegularExpressions;
using Bms.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Data;

/// <summary>
/// Chỉ chạy trong môi trường Development.
/// - Mở khóa và đặt mật khẩu demo (mặc định 123456) cho admin.demo, staff.demo01 mỗi lần chạy.
/// - Nạp dữ liệu ca làm mẫu (09_AddShiftDemoData.sql) nếu database chưa có.
/// </summary>
public static class DbInitializer
{
    private static readonly string[] DemoAccounts = { "admin.demo", "staff.demo01" };

    public static async Task SeedDevelopmentPasswordsAsync(
        UserManager<ApplicationUser> userManager,
        IConfiguration config,
        ILogger logger)
    {
        // Mật khẩu demo dùng chung; đổi bằng user-secrets "DevSeed:DemoPassword" nếu muốn.
        var password = config["DevSeed:DemoPassword"];
        if (string.IsNullOrWhiteSpace(password)) password = "123456";

        foreach (var userName in DemoAccounts)
        {
            var user = await userManager.FindByNameAsync(userName);
            if (user == null)
            {
                logger.LogWarning("Không tìm thấy {UserName} — bỏ qua.", userName);
                continue;
            }

            // Mở khóa: tài khoản bị khóa tay (IsActive) hoặc bị khóa do nhập sai mật khẩu nhiều lần.
            user.IsActive = true;
            await userManager.SetLockoutEndDateAsync(user, null);
            await userManager.ResetAccessFailedCountAsync(user);

            // Đặt lại mật khẩu demo để buổi báo cáo luôn đăng nhập được.
            if (await userManager.HasPasswordAsync(user))
                await userManager.RemovePasswordAsync(user);
            var result = await userManager.AddPasswordAsync(user, password);
            if (result.Succeeded)
                logger.LogInformation("Đã mở khóa {UserName} và đặt mật khẩu demo.", userName);
            else
                logger.LogError("Đặt mật khẩu {UserName} thất bại: {Errors}", userName,
                    string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }

    /// <summary>
    /// Chạy BMS_Database_Starter/09_AddShiftDemoData.sql một lần nếu chưa có ca demo.
    /// Script tự bỏ qua khi đã có dữ liệu, nên chạy lại vẫn an toàn.
    /// </summary>
    public static async Task SeedShiftDemoDataAsync(ApplicationDbContext db, string contentRoot, ILogger logger)
    {
        var hasDemo = await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS Value FROM dbo.WorkShifts WHERE Notes LIKE N'[[]DEMO]%'").SingleAsync();
        if (hasDemo > 0) return;

        var script = FindScript(contentRoot, "09_AddShiftDemoData.sql");
        if (script == null)
        {
            logger.LogWarning("Không tìm thấy 09_AddShiftDemoData.sql — bỏ qua dữ liệu ca mẫu.");
            return;
        }

        // Tách theo dòng "GO" như SSMS; bỏ lệnh USE vì đã kết nối đúng database.
        var batches = Regex.Split(await File.ReadAllTextAsync(script), @"^\s*GO\s*$",
                RegexOptions.Multiline | RegexOptions.IgnoreCase)
            .Select(b => Regex.Replace(b, @"^\s*USE\s+\w+\s*;?\s*$", "", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            .Where(b => b.Split('\n').Any(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith("--")));

        // Giữ một kết nối cho mọi batch để SET NOEXEC trong script có tác dụng.
        await db.Database.OpenConnectionAsync();
        try
        {
            foreach (var batch in batches)
                await db.Database.ExecuteSqlRawAsync(batch);
            logger.LogInformation("Đã nạp dữ liệu ca làm mẫu từ {Script}.", Path.GetFileName(script));
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static string? FindScript(string startDir, string fileName)
    {
        for (var dir = new DirectoryInfo(startDir); dir != null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "BMS_Database_Starter", fileName);
            if (File.Exists(path)) return path;
        }
        return null;
    }
}
