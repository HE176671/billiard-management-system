using Bms.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Services;

/// <summary>Serialize a bank transaction across invoice and wallet webhook handlers.</summary>
public static class BankTransactionGuard
{
    public static async Task LockAsync(ApplicationDbContext db, string transactionId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(transactionId) || transactionId.Length > 100)
            throw new ArgumentException("Mã giao dịch ngân hàng không hợp lệ.");
        var resource = "BMS-BANK-" + transactionId.ToUpperInvariant();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive',
                @LockOwner='Transaction', @LockTimeout=10000;
            IF @result < 0 THROW 51000, 'Cannot lock bank transaction', 1;
            """, ct);
    }
}
