using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Data;

public static class ManualQrSchema
{
    public const string Sql = """
        IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('dbo.PaymentIntents') AND name='ExpiresAtUtc' AND is_nullable=0)
            ALTER TABLE dbo.PaymentIntents ALTER COLUMN ExpiresAtUtc datetime2(0) NULL;
        IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('dbo.WalletTopUpIntents') AND name='ExpiresAtUtc' AND is_nullable=0)
            ALTER TABLE dbo.WalletTopUpIntents ALTER COLUMN ExpiresAtUtc datetime2 NULL;
        IF COL_LENGTH('dbo.PaymentIntents','RequiresStaffConfirmation') IS NULL
            ALTER TABLE dbo.PaymentIntents ADD RequiresStaffConfirmation bit NOT NULL CONSTRAINT DF_PaymentIntent_StaffConfirmation DEFAULT 1 WITH VALUES;
        IF COL_LENGTH('dbo.WalletTopUpIntents','RequiresStaffConfirmation') IS NULL
            ALTER TABLE dbo.WalletTopUpIntents ADD RequiresStaffConfirmation bit NOT NULL CONSTRAINT DF_WalletIntent_StaffConfirmation DEFAULT 1 WITH VALUES;
        """;
    public const string PendingSql = """
        UPDATE i SET Status='Pending', ExpiresAtUtc=NULL, RequiresStaffConfirmation=1
        FROM dbo.PaymentIntents i
        WHERE i.Status IN ('Pending','Expired')
          AND NOT EXISTS (SELECT 1 FROM dbo.PaymentTransactions p WHERE p.InvoiceId=i.InvoiceId)
          AND NOT EXISTS (SELECT 1 FROM dbo.CheckoutDrafts d WHERE d.ReferenceCode=i.ReferenceCode AND d.Status IN ('Paid','Superseded'));
        UPDATE dbo.WalletTopUpIntents SET Status='Pending', ExpiresAtUtc=NULL, RequiresStaffConfirmation=1
        WHERE Status IN ('Pending','Expired');
        """;
    public static async Task ApplyAsync(ApplicationDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(Sql);
        await db.Database.ExecuteSqlRawAsync(PendingSql);
    }
}
