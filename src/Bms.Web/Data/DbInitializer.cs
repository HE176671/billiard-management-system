using System.Text.RegularExpressions;
using Bms.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Data;

/// <summary>
/// Chỉ chạy trong môi trường Development.
/// - Đặt mật khẩu cho tài khoản mẫu (admin.demo, staff.demo01) nếu PasswordHash còn NULL.
///   Mật khẩu lấy từ cấu hình DevSeed:AdminPassword — không viết cứng trong source code.
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
        var password = config["DevSeed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "DevSeed:AdminPassword chưa được cấu hình. " +
                "Chạy: dotnet user-secrets set \"DevSeed:AdminPassword\" \"<mật_khẩu>\"");
            return;
        }

        foreach (var userName in DemoAccounts)
        {
            var user = await userManager.FindByNameAsync(userName);
            if (user == null)
            {
                logger.LogWarning("Không tìm thấy {UserName} — bỏ qua.", userName);
                continue;
            }
            if (await userManager.HasPasswordAsync(user))
            {
                logger.LogInformation("{UserName} đã có mật khẩu, bỏ qua.", userName);
                continue;
            }

            var result = await userManager.AddPasswordAsync(user, password);
            if (result.Succeeded)
                logger.LogInformation("Đã đặt mật khẩu cho {UserName} (giống DevSeed:AdminPassword).", userName);
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
