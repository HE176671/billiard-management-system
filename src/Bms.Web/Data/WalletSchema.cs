using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Data;

public static class WalletSchema
{
    public const string Sql = """
        SET ANSI_NULLS ON;
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_PADDING ON;
        SET ANSI_WARNINGS ON;
        SET CONCAT_NULL_YIELDS_NULL ON;
        SET ARITHABORT ON;
        SET NUMERIC_ROUNDABORT OFF;
        IF COL_LENGTH('dbo.AspNetUsers', 'WalletBalance') IS NULL
            ALTER TABLE dbo.AspNetUsers ADD WalletBalance decimal(18,2) NOT NULL
                CONSTRAINT DF_User_WalletBalance DEFAULT 0 WITH VALUES;
        IF OBJECT_ID('dbo.WalletTransactions', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.WalletTransactions (
                Id int IDENTITY PRIMARY KEY,
                CustomerId nvarchar(450) NOT NULL REFERENCES dbo.AspNetUsers(Id),
                TransactionType varchar(20) NOT NULL,
                Amount decimal(18,2) NOT NULL,
                BalanceAfter decimal(18,2) NOT NULL CHECK (BalanceAfter >= 0),
                ReferenceCode varchar(50) NOT NULL UNIQUE,
                InvoiceId int NULL REFERENCES dbo.Invoices(Id),
                CreatedById nvarchar(450) NOT NULL REFERENCES dbo.AspNetUsers(Id),
                WorkShiftId int NULL REFERENCES dbo.WorkShifts(Id),
                CreatedAtUtc datetime2 NOT NULL,
                CONSTRAINT CK_WalletTransaction_Type CHECK (
                    (TransactionType = 'TopUp' AND Amount > 0 AND InvoiceId IS NULL AND WorkShiftId IS NOT NULL) OR
                    (TransactionType = 'Payment' AND Amount <= 0 AND InvoiceId IS NOT NULL))
            );
            CREATE INDEX IX_Wallet_Customer_Date ON dbo.WalletTransactions(CustomerId, CreatedAtUtc);
            CREATE UNIQUE INDEX UX_Wallet_Invoice ON dbo.WalletTransactions(InvoiceId) WHERE InvoiceId IS NOT NULL;
        END;
        IF OBJECT_ID('dbo.CK_User_WalletBalance', 'C') IS NULL
            EXEC('ALTER TABLE dbo.AspNetUsers ADD CONSTRAINT CK_User_WalletBalance CHECK (WalletBalance >= 0)');
        IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('dbo.PaymentTransactions') AND name='CK_Payment_Method' AND definition LIKE '%Wallet%')
        BEGIN
            IF OBJECT_ID('dbo.CK_Payment_Method', 'C') IS NOT NULL
                ALTER TABLE dbo.PaymentTransactions DROP CONSTRAINT CK_Payment_Method;
            ALTER TABLE dbo.PaymentTransactions ADD CONSTRAINT CK_Payment_Method
                CHECK (PaymentMethod IN ('Cash','Transfer','SePay','VNPay','Wallet'));
        END;
        IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name='CK_Payment_Amount' AND definition NOT LIKE '%>=%')
        BEGIN
            ALTER TABLE dbo.PaymentTransactions DROP CONSTRAINT CK_Payment_Amount;
            ALTER TABLE dbo.PaymentTransactions ADD CONSTRAINT CK_Payment_Amount CHECK (AmountPaid >= 0);
        END;
        """;

    public static async Task ApplyAsync(ApplicationDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(Sql);
        await WalletTransferSchema.ApplyAsync(db);
    }
}
