using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Data;

/// <summary>
/// Tự áp dụng các nâng cấp schema cộng thêm khi web khởi động (tương đương script 08 và script 09),
/// để máy nào quên chạy script tay vẫn không bị lỗi "Invalid column name" / "Invalid object name".
/// Chỉ thêm bảng/cột/ràng buộc/index nếu chưa có, không xóa dữ liệu.
/// </summary>
public static class SchemaUpgrader
{
    private static readonly string[] WorkShiftBatches =
    {
        """
        IF COL_LENGTH('dbo.WorkShifts', 'CashCollected') IS NULL
            ALTER TABLE dbo.WorkShifts ADD CashCollected decimal(18,2) NULL;
        IF COL_LENGTH('dbo.WorkShifts', 'ExpectedCash') IS NULL
            ALTER TABLE dbo.WorkShifts ADD ExpectedCash decimal(18,2) NULL;
        IF COL_LENGTH('dbo.WorkShifts', 'ClosedById') IS NULL
            ALTER TABLE dbo.WorkShifts ADD ClosedById nvarchar(450) NULL;
        IF COL_LENGTH('dbo.PaymentTransactions', 'WorkShiftId') IS NULL
            ALTER TABLE dbo.PaymentTransactions ADD WorkShiftId int NULL;
        IF COL_LENGTH('dbo.PaymentTransactions', 'ReceivedById') IS NULL
            ALTER TABLE dbo.PaymentTransactions ADD ReceivedById nvarchar(450) NULL;
        """,
        """
        SET ANSI_NULLS ON;
        SET QUOTED_IDENTIFIER ON;
        IF OBJECT_ID('dbo.FK_Shift_ClosedBy', 'F') IS NULL
            ALTER TABLE dbo.WorkShifts ADD CONSTRAINT FK_Shift_ClosedBy
                FOREIGN KEY (ClosedById) REFERENCES dbo.AspNetUsers(Id);
        IF OBJECT_ID('dbo.CK_Shift_StartCash', 'C') IS NULL
            ALTER TABLE dbo.WorkShifts WITH CHECK ADD CONSTRAINT CK_Shift_StartCash CHECK (StartingCash >= 0);
        IF OBJECT_ID('dbo.CK_Shift_EndCash', 'C') IS NULL
            ALTER TABLE dbo.WorkShifts WITH CHECK ADD CONSTRAINT CK_Shift_EndCash CHECK (EndingCash IS NULL OR EndingCash >= 0);
        IF OBJECT_ID('dbo.CK_Shift_Lifecycle', 'C') IS NULL
            ALTER TABLE dbo.WorkShifts WITH CHECK ADD CONSTRAINT CK_Shift_Lifecycle CHECK (
                (Status = 'Active' AND EndTimeUtc IS NULL AND EndingCash IS NULL) OR
                (Status = 'Closed' AND EndTimeUtc IS NOT NULL AND EndingCash IS NOT NULL));
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_WorkShifts_ActiveStaff' AND object_id = OBJECT_ID('dbo.WorkShifts'))
            CREATE UNIQUE INDEX UX_WorkShifts_ActiveStaff ON dbo.WorkShifts(StaffId) WHERE Status = 'Active';
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WorkShifts_Start' AND object_id = OBJECT_ID('dbo.WorkShifts'))
            CREATE INDEX IX_WorkShifts_Start ON dbo.WorkShifts(StartTimeUtc) INCLUDE (StaffId, Status);
        IF OBJECT_ID('dbo.FK_Payment_WorkShift', 'F') IS NULL
            ALTER TABLE dbo.PaymentTransactions ADD CONSTRAINT FK_Payment_WorkShift
                FOREIGN KEY (WorkShiftId) REFERENCES dbo.WorkShifts(Id);
        IF OBJECT_ID('dbo.FK_Payment_ReceivedBy', 'F') IS NULL
            ALTER TABLE dbo.PaymentTransactions ADD CONSTRAINT FK_Payment_ReceivedBy
                FOREIGN KEY (ReceivedById) REFERENCES dbo.AspNetUsers(Id);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Payments_WorkShift' AND object_id = OBJECT_ID('dbo.PaymentTransactions'))
            CREATE INDEX IX_Payments_WorkShift ON dbo.PaymentTransactions(WorkShiftId, PaymentMethod)
                INCLUDE (AmountPaid) WHERE WorkShiftId IS NOT NULL;
        """
    };

    private static readonly string[] CrmLoyaltyV2Batches =
    {
        """
        SET ANSI_NULLS ON;
        SET QUOTED_IDENTIFIER ON;
        IF COL_LENGTH('dbo.AspNetUsers', 'TotalSpend') IS NULL
            ALTER TABLE dbo.AspNetUsers ADD TotalSpend decimal(18,2) NOT NULL CONSTRAINT DF_User_TotalSpend DEFAULT 0;
        IF COL_LENGTH('dbo.AspNetUsers', 'LastPaidDate') IS NULL
            ALTER TABLE dbo.AspNetUsers ADD LastPaidDate datetime2 NULL;
        IF COL_LENGTH('dbo.AspNetUsers', 'DateOfBirth') IS NULL
            ALTER TABLE dbo.AspNetUsers ADD DateOfBirth date NULL;
        IF COL_LENGTH('dbo.AspNetUsers', 'MarketingOptIn') IS NULL
            ALTER TABLE dbo.AspNetUsers ADD MarketingOptIn bit NOT NULL CONSTRAINT DF_User_MarketingOptIn DEFAULT 1;
        IF COL_LENGTH('dbo.AspNetUsers', 'AccountSource') IS NULL
            ALTER TABLE dbo.AspNetUsers ADD AccountSource varchar(20) NOT NULL CONSTRAINT DF_User_AccountSource DEFAULT 'Self';
        IF COL_LENGTH('dbo.AspNetUsers', 'ClaimedAtUtc') IS NULL
            ALTER TABLE dbo.AspNetUsers ADD ClaimedAtUtc datetime2(0) NULL;
        IF COL_LENGTH('dbo.AspNetUsers', 'TierAchievedAtUtc') IS NULL
            ALTER TABLE dbo.AspNetUsers ADD TierAchievedAtUtc datetime2(0) NULL;
        IF COL_LENGTH('dbo.AspNetUsers', 'TierReviewAtUtc') IS NULL
            ALTER TABLE dbo.AspNetUsers ADD TierReviewAtUtc datetime2(0) NULL;
        IF COL_LENGTH('dbo.AspNetUsers', 'TierGraceUntilUtc') IS NULL
            ALTER TABLE dbo.AspNetUsers ADD TierGraceUntilUtc datetime2(0) NULL;

        IF COL_LENGTH('dbo.MembershipTiers', 'SpendThreshold') IS NULL
            ALTER TABLE dbo.MembershipTiers ADD SpendThreshold decimal(18,2) NOT NULL CONSTRAINT DF_Tier_SpendThreshold DEFAULT 0;
        IF COL_LENGTH('dbo.MembershipTiers', 'PointMultiplier') IS NULL
            ALTER TABLE dbo.MembershipTiers ADD PointMultiplier int NOT NULL CONSTRAINT DF_Tier_PointMultiplier DEFAULT 1;
        IF COL_LENGTH('dbo.MembershipTiers', 'BirthdayVoucherValue') IS NULL
            ALTER TABLE dbo.MembershipTiers ADD BirthdayVoucherValue decimal(18,2) NOT NULL CONSTRAINT DF_Tier_BirthdayVoucher DEFAULT 0;
        IF COL_LENGTH('dbo.MembershipTiers', 'SortOrder') IS NULL
            ALTER TABLE dbo.MembershipTiers ADD SortOrder int NOT NULL CONSTRAINT DF_Tier_SortOrder DEFAULT 0;

        IF OBJECT_ID('dbo.PointTransactions', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.PointTransactions (
                Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PointTransactions PRIMARY KEY,
                CustomerId nvarchar(450) NOT NULL,
                Amount int NOT NULL,
                TransactionType varchar(20) NOT NULL,
                CreatedAt datetime2 NOT NULL CONSTRAINT DF_PointTxn_CreatedAt DEFAULT SYSUTCDATETIME(),
                CONSTRAINT FK_PointTxn_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.AspNetUsers(Id)
            );
        END;

        IF COL_LENGTH('dbo.PointTransactions', 'BalanceAfter') IS NULL
            ALTER TABLE dbo.PointTransactions ADD BalanceAfter int NOT NULL CONSTRAINT DF_PointTxn_BalanceAfter DEFAULT 0;
        IF COL_LENGTH('dbo.PointTransactions', 'InvoiceId') IS NULL
            ALTER TABLE dbo.PointTransactions ADD InvoiceId int NULL;
        IF COL_LENGTH('dbo.PointTransactions', 'ReferenceCode') IS NULL
            ALTER TABLE dbo.PointTransactions ADD ReferenceCode varchar(50) NULL;
        IF COL_LENGTH('dbo.PointTransactions', 'Reason') IS NULL
            ALTER TABLE dbo.PointTransactions ADD Reason nvarchar(300) NULL;
        IF COL_LENGTH('dbo.PointTransactions', 'CreatedById') IS NULL
            ALTER TABLE dbo.PointTransactions ADD CreatedById nvarchar(450) NULL;
        IF COL_LENGTH('dbo.PointTransactions', 'SpendAmount') IS NULL
            ALTER TABLE dbo.PointTransactions ADD SpendAmount decimal(18,2) NULL;

        IF OBJECT_ID('dbo.Notifications', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.Notifications (
                Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Notifications PRIMARY KEY,
                UserId nvarchar(450) NOT NULL,
                Title nvarchar(200) NOT NULL,
                Content nvarchar(max) NOT NULL,
                Type varchar(50) NULL,
                IsRead bit NOT NULL CONSTRAINT DF_Noti_IsRead DEFAULT 0,
                CreatedAt datetime2 NOT NULL CONSTRAINT DF_Noti_CreatedAt DEFAULT SYSUTCDATETIME(),
                CONSTRAINT FK_Noti_User FOREIGN KEY (UserId) REFERENCES dbo.AspNetUsers(Id) ON DELETE CASCADE
            );
        END;

        IF OBJECT_ID('dbo.Reviews', 'U') IS NOT NULL AND COL_LENGTH('dbo.Reviews', 'AdminReply') IS NULL
            ALTER TABLE dbo.Reviews ADD AdminReply nvarchar(max) NULL;
        """,
        """
        SET ANSI_NULLS ON;
        SET QUOTED_IDENTIFIER ON;
        IF OBJECT_ID('dbo.CK_User_AccountSource', 'C') IS NULL
            ALTER TABLE dbo.AspNetUsers WITH CHECK ADD CONSTRAINT CK_User_AccountSource
                CHECK (AccountSource IN ('Counter', 'Self'));

        DECLARE @dropSql nvarchar(max) = N'';
        SELECT @dropSql += N'ALTER TABLE dbo.PointTransactions DROP CONSTRAINT [' + cc.name + N'];'
        FROM sys.check_constraints cc
        JOIN sys.columns c ON c.object_id = cc.parent_object_id AND c.column_id = cc.parent_column_id
        WHERE cc.parent_object_id = OBJECT_ID('dbo.PointTransactions')
          AND c.name = 'TransactionType'
          AND cc.name <> 'CK_PointTxn_Type';
        IF LEN(@dropSql) > 0
            EXEC sp_executesql @dropSql;

        IF OBJECT_ID('dbo.CK_PointTxn_Type', 'C') IS NULL
            ALTER TABLE dbo.PointTransactions WITH CHECK ADD CONSTRAINT CK_PointTxn_Type
                CHECK (TransactionType IN ('Earn', 'Redeem', 'Expire', 'Adjust', 'Reverse', 'Bonus'));

        IF OBJECT_ID('dbo.FK_PointTxn_Invoice', 'F') IS NULL
            ALTER TABLE dbo.PointTransactions ADD CONSTRAINT FK_PointTxn_Invoice
                FOREIGN KEY (InvoiceId) REFERENCES dbo.Invoices(Id) ON DELETE NO ACTION;

        IF OBJECT_ID('dbo.FK_PointTxn_CreatedBy', 'F') IS NULL
            ALTER TABLE dbo.PointTransactions ADD CONSTRAINT FK_PointTxn_CreatedBy
                FOREIGN KEY (CreatedById) REFERENCES dbo.AspNetUsers(Id) ON DELETE NO ACTION;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PointTxn_Earn_Ref' AND object_id = OBJECT_ID('dbo.PointTransactions'))
            CREATE UNIQUE INDEX UX_PointTxn_Earn_Ref ON dbo.PointTransactions(ReferenceCode, TransactionType)
                WHERE ReferenceCode IS NOT NULL;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PointTxn_Customer_Created' AND object_id = OBJECT_ID('dbo.PointTransactions'))
            CREATE INDEX IX_PointTxn_Customer_Created ON dbo.PointTransactions(CustomerId, CreatedAt DESC, Id DESC);

        IF OBJECT_ID('dbo.RewardItems', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.RewardItems (
                Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_RewardItems PRIMARY KEY,
                Code varchar(30) NOT NULL CONSTRAINT UQ_RewardItems_Code UNIQUE,
                Name nvarchar(100) NOT NULL,
                Description nvarchar(300) NULL,
                PointCost int NOT NULL CONSTRAINT CK_RewardItems_Cost CHECK (PointCost >= 0),
                RewardType varchar(20) NOT NULL CONSTRAINT CK_RewardItems_Type CHECK (RewardType IN ('Gift', 'FreeHour', 'BillDiscount')),
                DiscountValue decimal(18,2) NULL CONSTRAINT CK_RewardItems_Discount CHECK (DiscountValue IS NULL OR DiscountValue >= 0),
                ValidDays int NOT NULL CONSTRAINT DF_RewardItems_ValidDays DEFAULT 7,
                MinTierId int NULL,
                IsActive bit NOT NULL CONSTRAINT DF_RewardItems_IsActive DEFAULT 1,
                CONSTRAINT FK_RewardItems_MinTier FOREIGN KEY (MinTierId) REFERENCES dbo.MembershipTiers(Id) ON DELETE NO ACTION
            );
        END;

        DECLARE @GoldTierId int = (SELECT TOP 1 Id FROM dbo.MembershipTiers WHERE TierName IN (N'Gold', N'Vàng') ORDER BY SpendThreshold DESC, Id DESC);
        IF NOT EXISTS (SELECT 1 FROM dbo.RewardItems WHERE Code = 'DRINK')
            INSERT INTO dbo.RewardItems (Code, Name, Description, PointCost, RewardType, DiscountValue, ValidDays, MinTierId, IsActive)
            VALUES ('DRINK', N'Nước uống miễn phí', N'1 lon nước ngọt / nước suối / Bò húc tự chọn tại quầy', 20, 'Gift', 20000, 7, NULL, 1);
        IF NOT EXISTS (SELECT 1 FROM dbo.RewardItems WHERE Code = 'SNACK')
            INSERT INTO dbo.RewardItems (Code, Name, Description, PointCost, RewardType, DiscountValue, ValidDays, MinTierId, IsActive)
            VALUES ('SNACK', N'Đồ ăn nhẹ', N'1 đĩa trái cây / hướng dương / khô gà', 50, 'Gift', 50000, 7, NULL, 1);
        IF NOT EXISTS (SELECT 1 FROM dbo.RewardItems WHERE Code = 'FREEHOUR')
            INSERT INTO dbo.RewardItems (Code, Name, Description, PointCost, RewardType, DiscountValue, ValidDays, MinTierId, IsActive)
            VALUES ('FREEHOUR', N'1 giờ chơi miễn phí', N'Giảm tối đa 1 giờ chơi (tối đa 80.000đ) trực tiếp trên hóa đơn', 80, 'FreeHour', 80000, 7, NULL, 1);
        IF NOT EXISTS (SELECT 1 FROM dbo.RewardItems WHERE Code = 'VIP_TABLE')
            INSERT INTO dbo.RewardItems (Code, Name, Description, PointCost, RewardType, DiscountValue, ValidDays, MinTierId, IsActive)
            VALUES ('VIP_TABLE', N'Voucher giảm 150.000đ bàn VIP', N'Ưu đãi đặc quyền dành cho hội viên hạng Gold trở lên', 150, 'BillDiscount', 150000, 7, @GoldTierId, 1);

        IF OBJECT_ID('dbo.Campaigns', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.Campaigns (
                Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Campaigns PRIMARY KEY,
                Name nvarchar(150) NOT NULL,
                CampaignType varchar(20) NOT NULL CONSTRAINT CK_Campaigns_Type CHECK (CampaignType IN ('Manual', 'Birthday', 'WinBack')),
                TargetSegment varchar(20) NULL,
                TargetTierId int NULL,
                RewardItemId int NULL,
                BonusPoints int NOT NULL CONSTRAINT DF_Campaigns_BonusPoints DEFAULT 0,
                MessageTitle nvarchar(200) NOT NULL,
                MessageContent nvarchar(1000) NOT NULL,
                Status varchar(20) NOT NULL CONSTRAINT DF_Campaigns_Status DEFAULT 'Draft'
                    CONSTRAINT CK_Campaigns_Status CHECK (Status IN ('Draft', 'Sent', 'Cancelled')),
                CreatedById nvarchar(450) NOT NULL,
                CreatedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_Campaigns_Created DEFAULT SYSUTCDATETIME(),
                SentAtUtc datetime2(0) NULL,
                RecipientCount int NOT NULL CONSTRAINT DF_Campaigns_Recipients DEFAULT 0,
                CONSTRAINT FK_Campaigns_TargetTier FOREIGN KEY (TargetTierId) REFERENCES dbo.MembershipTiers(Id) ON DELETE NO ACTION,
                CONSTRAINT FK_Campaigns_RewardItem FOREIGN KEY (RewardItemId) REFERENCES dbo.RewardItems(Id) ON DELETE NO ACTION,
                CONSTRAINT FK_Campaigns_CreatedBy FOREIGN KEY (CreatedById) REFERENCES dbo.AspNetUsers(Id) ON DELETE NO ACTION
            );
        END;

        IF OBJECT_ID('dbo.CustomerVouchers', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.CustomerVouchers (
                Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CustomerVouchers PRIMARY KEY,
                Code varchar(20) NOT NULL CONSTRAINT UQ_CustomerVouchers_Code UNIQUE,
                CustomerId nvarchar(450) NOT NULL,
                RewardItemId int NULL,
                CampaignId int NULL,
                Title nvarchar(150) NOT NULL,
                VoucherType varchar(20) NOT NULL CONSTRAINT CK_CustomerVouchers_Type CHECK (VoucherType IN ('Gift', 'FreeHour', 'BillDiscount')),
                DiscountValue decimal(18,2) NULL,
                Status varchar(20) NOT NULL CONSTRAINT DF_CustomerVouchers_Status DEFAULT 'Active'
                    CONSTRAINT CK_CustomerVouchers_Status CHECK (Status IN ('Active', 'Used', 'Expired', 'Cancelled')),
                IssuedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_CustomerVouchers_Issued DEFAULT SYSUTCDATETIME(),
                ExpiresAtUtc datetime2(0) NOT NULL,
                UsedAtUtc datetime2(0) NULL,
                UsedById nvarchar(450) NULL,
                UsedReferenceCode varchar(50) NULL,
                PointTransactionId int NULL,
                CONSTRAINT FK_Vouchers_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.AspNetUsers(Id) ON DELETE NO ACTION,
                CONSTRAINT FK_Vouchers_RewardItem FOREIGN KEY (RewardItemId) REFERENCES dbo.RewardItems(Id) ON DELETE NO ACTION,
                CONSTRAINT FK_Vouchers_Campaign FOREIGN KEY (CampaignId) REFERENCES dbo.Campaigns(Id) ON DELETE NO ACTION,
                CONSTRAINT FK_Vouchers_UsedBy FOREIGN KEY (UsedById) REFERENCES dbo.AspNetUsers(Id) ON DELETE NO ACTION,
                CONSTRAINT FK_Vouchers_PointTxn FOREIGN KEY (PointTransactionId) REFERENCES dbo.PointTransactions(Id) ON DELETE NO ACTION
            );
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Voucher_Customer_Status' AND object_id = OBJECT_ID('dbo.CustomerVouchers'))
            CREATE INDEX IX_Voucher_Customer_Status ON dbo.CustomerVouchers(CustomerId, Status);

        IF OBJECT_ID('dbo.PointAdjustmentRequests', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.PointAdjustmentRequests (
                Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PointAdjustmentRequests PRIMARY KEY,
                CustomerId nvarchar(450) NOT NULL,
                Points int NOT NULL CONSTRAINT CK_PointAdj_Points CHECK (Points <> 0),
                Reason nvarchar(300) NOT NULL,
                Status varchar(20) NOT NULL CONSTRAINT DF_PointAdj_Status DEFAULT 'Pending'
                    CONSTRAINT CK_PointAdj_Status CHECK (Status IN ('Pending', 'Approved', 'Rejected')),
                RequestedById nvarchar(450) NOT NULL,
                RequestedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_PointAdj_RequestedAt DEFAULT SYSUTCDATETIME(),
                ReviewedById nvarchar(450) NULL,
                ReviewedAtUtc datetime2(0) NULL,
                ReviewNote nvarchar(300) NULL,
                PointTransactionId int NULL,
                CONSTRAINT FK_PointAdj_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.AspNetUsers(Id) ON DELETE NO ACTION,
                CONSTRAINT FK_PointAdj_RequestedBy FOREIGN KEY (RequestedById) REFERENCES dbo.AspNetUsers(Id) ON DELETE NO ACTION,
                CONSTRAINT FK_PointAdj_ReviewedBy FOREIGN KEY (ReviewedById) REFERENCES dbo.AspNetUsers(Id) ON DELETE NO ACTION,
                CONSTRAINT FK_PointAdj_PointTxn FOREIGN KEY (PointTransactionId) REFERENCES dbo.PointTransactions(Id) ON DELETE NO ACTION
            );
        END;

        IF OBJECT_ID('dbo.CustomerNotes', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.CustomerNotes (
                Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CustomerNotes PRIMARY KEY,
                CustomerId nvarchar(450) NOT NULL,
                Content nvarchar(500) NOT NULL,
                IsPinned bit NOT NULL CONSTRAINT DF_CustomerNotes_IsPinned DEFAULT 0,
                CreatedById nvarchar(450) NOT NULL,
                CreatedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_CustomerNotes_Created DEFAULT SYSUTCDATETIME(),
                CONSTRAINT FK_CustomerNotes_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.AspNetUsers(Id) ON DELETE NO ACTION,
                CONSTRAINT FK_CustomerNotes_CreatedBy FOREIGN KEY (CreatedById) REFERENCES dbo.AspNetUsers(Id) ON DELETE NO ACTION
            );
        END;

        IF OBJECT_ID('dbo.CustomerAuditLogs', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.CustomerAuditLogs (
                Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_CustomerAuditLogs PRIMARY KEY,
                CustomerId nvarchar(450) NOT NULL,
                Action varchar(30) NOT NULL,
                Detail nvarchar(1000) NULL,
                ActorId nvarchar(450) NULL,
                CreatedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_CustomerAuditLogs_Created DEFAULT SYSUTCDATETIME(),
                CONSTRAINT FK_CustomerAuditLogs_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.AspNetUsers(Id) ON DELETE NO ACTION,
                CONSTRAINT FK_CustomerAuditLogs_Actor FOREIGN KEY (ActorId) REFERENCES dbo.AspNetUsers(Id) ON DELETE NO ACTION
            );
        END;
        """
    };

    public static async Task ApplyAsync(ApplicationDbContext db, ILogger logger)
    {
        // 1. Nâng cấp WorkShift (Script 08)
        var missingShift = await db.Database.SqlQueryRaw<int>("""
            SELECT CASE WHEN COL_LENGTH('dbo.WorkShifts', 'ExpectedCash') IS NULL
                          OR COL_LENGTH('dbo.WorkShifts', 'CashCollected') IS NULL
                          OR COL_LENGTH('dbo.WorkShifts', 'ClosedById') IS NULL
                          OR COL_LENGTH('dbo.PaymentTransactions', 'WorkShiftId') IS NULL
                          OR COL_LENGTH('dbo.PaymentTransactions', 'ReceivedById') IS NULL
                        THEN 1 ELSE 0 END AS Value
            """).SingleAsync();
        if (missingShift != 0)
        {
            logger.LogWarning("Database thiếu cột ca làm (script 08). Đang tự nâng cấp schema...");
            await db.Database.ExecuteSqlRawAsync(WorkShiftBatches[0]);
            try
            {
                await using var tx = await db.Database.BeginTransactionAsync();
                await db.Database.ExecuteSqlRawAsync(WorkShiftBatches[1]);
                await tx.CommitAsync();
                logger.LogInformation("Đã nâng cấp schema ca làm (tương đương 08_AddWorkShiftReconciliation.sql).");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Đã thêm cột ca làm nhưng chưa tạo được ràng buộc/index.");
            }
        }

        // 2. Nâng cấp CRM & Loyalty V2 (Script 09)
        var missingCrmV2 = await db.Database.SqlQueryRaw<int>("""
            SELECT CASE WHEN COL_LENGTH('dbo.AspNetUsers', 'AccountSource') IS NULL
                          OR COL_LENGTH('dbo.AspNetUsers', 'TierGraceUntilUtc') IS NULL
                          OR COL_LENGTH('dbo.MembershipTiers', 'BirthdayVoucherValue') IS NULL
                          OR COL_LENGTH('dbo.PointTransactions', 'BalanceAfter') IS NULL
                          OR COL_LENGTH('dbo.PointTransactions', 'SpendAmount') IS NULL
                          OR OBJECT_ID('dbo.RewardItems', 'U') IS NULL
                          OR OBJECT_ID('dbo.CustomerVouchers', 'U') IS NULL
                          OR OBJECT_ID('dbo.PointAdjustmentRequests', 'U') IS NULL
                          OR OBJECT_ID('dbo.CustomerNotes', 'U') IS NULL
                          OR OBJECT_ID('dbo.Campaigns', 'U') IS NULL
                          OR OBJECT_ID('dbo.CustomerAuditLogs', 'U') IS NULL
                        THEN 1 ELSE 0 END AS Value
            """).SingleAsync();
        if (missingCrmV2 != 0)
        {
            logger.LogWarning("Database thiếu bảng/cột CRM & Loyalty V2 (script 09). Đang tự nâng cấp schema...");
            // TV5: bắt lỗi riêng để database cũ thiếu bảng phụ vẫn chạy tiếp các bước nâng cấp sau
            // (trước đây lỗi ở đây làm bỏ qua bước 3-5: tài khoản manager.demo, bảng kho WF2...).
            try { await db.Database.ExecuteSqlRawAsync(CrmLoyaltyV2Batches[0]); }
            catch (Exception ex) { logger.LogWarning(ex, "Lỗi khi thêm cột CRM & Loyalty V2 (batch 0)."); }
            try
            {
                await using var tx = await db.Database.BeginTransactionAsync();
                await db.Database.ExecuteSqlRawAsync(CrmLoyaltyV2Batches[1]);
                await tx.CommitAsync();
                logger.LogInformation("Đã nâng cấp schema CRM & Loyalty V2 (tương đương 09_TV2_CrmLoyaltyV2.sql).");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Đã thêm cột CRM V2 nhưng gặp lỗi khi tạo ràng buộc/bảng phụ.");
            }
        }

        // 3. Đảm bảo Role Manager, bảng ComboItems, danh mục Gậy cho thuê & dữ liệu mẫu Combo/Review
        try
        {
            await db.Database.ExecuteSqlRawAsync("""
                SET ANSI_NULLS ON;
                SET QUOTED_IDENTIFIER ON;

                IF OBJECT_ID('dbo.Reviews', 'U') IS NOT NULL AND COL_LENGTH('dbo.Reviews', 'AdminReply') IS NULL
                    ALTER TABLE dbo.Reviews ADD AdminReply nvarchar(max) NULL;

                IF OBJECT_ID('dbo.RewardItems', 'U') IS NOT NULL
                BEGIN
                    UPDATE dbo.RewardItems SET ValidDays = 7 WHERE ValidDays <> 7;
                    UPDATE dbo.RewardItems SET PointCost = 0 WHERE PointCost < 0;
                    UPDATE dbo.RewardItems SET DiscountValue = 0 WHERE DiscountValue < 0;
                    IF OBJECT_ID('dbo.CK_RewardItems_Cost', 'C') IS NOT NULL
                        ALTER TABLE dbo.RewardItems DROP CONSTRAINT CK_RewardItems_Cost;
                    ALTER TABLE dbo.RewardItems ADD CONSTRAINT CK_RewardItems_Cost CHECK (PointCost >= 0);
                    IF OBJECT_ID('dbo.CK_RewardItems_Discount', 'C') IS NULL
                        ALTER TABLE dbo.RewardItems ADD CONSTRAINT CK_RewardItems_Discount CHECK (DiscountValue IS NULL OR DiscountValue >= 0);
                END;

                IF OBJECT_ID('dbo.ComboItems', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.ComboItems (
                        ComboId int NOT NULL,
                        ProductId int NOT NULL,
                        Quantity int NOT NULL,
                        CONSTRAINT PK_ComboItems PRIMARY KEY (ComboId, ProductId),
                        CONSTRAINT FK_ComboItems_Combo FOREIGN KEY (ComboId) REFERENCES dbo.Combos(Id) ON DELETE CASCADE,
                        CONSTRAINT FK_ComboItems_Product FOREIGN KEY (ProductId) REFERENCES dbo.Products(Id),
                        CONSTRAINT CK_ComboItems_Quantity CHECK (Quantity > 0)
                    );
                    CREATE INDEX IX_ComboItems_ProductId ON dbo.ComboItems(ProductId);
                END;

                IF OBJECT_ID('dbo.SessionCombos', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.SessionCombos (
                        Id int IDENTITY NOT NULL CONSTRAINT PK_SessionCombos PRIMARY KEY,
                        SessionId int NOT NULL,
                        ComboId int NOT NULL,
                        Quantity int NOT NULL,
                        ComboNameSnapshot nvarchar(100) NOT NULL,
                        UnitPriceSnapshot decimal(18,2) NOT NULL,
                        PlaytimeHoursSnapshot decimal(5,2) NOT NULL,
                        PurchasedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_SessionCombos_Purchased DEFAULT SYSUTCDATETIME(),
                        CreatedById nvarchar(450) NOT NULL,
                        CONSTRAINT FK_SessionCombos_Session FOREIGN KEY (SessionId) REFERENCES dbo.PlaySessions(Id),
                        CONSTRAINT FK_SessionCombos_Combo FOREIGN KEY (ComboId) REFERENCES dbo.Combos(Id),
                        CONSTRAINT FK_SessionCombos_CreatedBy FOREIGN KEY (CreatedById) REFERENCES dbo.AspNetUsers(Id),
                        CONSTRAINT CK_SessionCombos_Quantity CHECK (Quantity > 0),
                        CONSTRAINT CK_SessionCombos_Name CHECK (LEN(LTRIM(RTRIM(ComboNameSnapshot))) > 0),
                        CONSTRAINT CK_SessionCombos_Price CHECK (UnitPriceSnapshot >= 0),
                        CONSTRAINT CK_SessionCombos_Hours CHECK (PlaytimeHoursSnapshot > 0)
                    );
                    CREATE INDEX IX_SessionCombos_SessionId ON dbo.SessionCombos(SessionId);
                    CREATE INDEX IX_SessionCombos_ComboId ON dbo.SessionCombos(ComboId);
                    CREATE INDEX IX_SessionCombos_CreatedById ON dbo.SessionCombos(CreatedById);
                END;

                IF OBJECT_ID('dbo.SessionComboItems', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.SessionComboItems (
                        SessionComboId int NOT NULL,
                        ProductId int NOT NULL,
                        ProductNameSnapshot nvarchar(100) NOT NULL,
                        QuantityPerCombo int NOT NULL,
                        CONSTRAINT PK_SessionComboItems PRIMARY KEY (SessionComboId, ProductId),
                        CONSTRAINT FK_SessionComboItems_Purchase FOREIGN KEY (SessionComboId) REFERENCES dbo.SessionCombos(Id),
                        CONSTRAINT FK_SessionComboItems_Product FOREIGN KEY (ProductId) REFERENCES dbo.Products(Id),
                        CONSTRAINT CK_SessionComboItems_Name CHECK (LEN(LTRIM(RTRIM(ProductNameSnapshot))) > 0),
                        CONSTRAINT CK_SessionComboItems_Quantity CHECK (QuantityPerCombo > 0)
                    );
                    CREATE INDEX IX_SessionComboItems_ProductId ON dbo.SessionComboItems(ProductId);
                END;

                IF OBJECT_ID('dbo.PlaySessionTableSegments', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.PlaySessionTableSegments (
                        Id int IDENTITY NOT NULL CONSTRAINT PK_PlaySessionTableSegments PRIMARY KEY,
                        SessionId int NOT NULL,
                        TableId int NOT NULL,
                        HourlyRateSnapshot decimal(18,2) NOT NULL,
                        StartAtUtc datetime2(0) NOT NULL,
                        EndAtUtc datetime2(0) NULL,
                        CONSTRAINT FK_SessionSegment_Session FOREIGN KEY (SessionId) REFERENCES dbo.PlaySessions(Id),
                        CONSTRAINT FK_SessionSegment_Table FOREIGN KEY (TableId) REFERENCES dbo.BilliardTables(Id),
                        CONSTRAINT CK_SessionSegment_Rate CHECK (HourlyRateSnapshot > 0),
                        CONSTRAINT CK_SessionSegment_Times CHECK (EndAtUtc IS NULL OR EndAtUtc >= StartAtUtc)
                    );
                    CREATE UNIQUE INDEX UX_SessionSegments_OpenPerSession ON dbo.PlaySessionTableSegments(SessionId) WHERE EndAtUtc IS NULL;
                    CREATE INDEX IX_SessionSegments_Session_Start ON dbo.PlaySessionTableSegments(SessionId, StartAtUtc);
                    CREATE INDEX IX_SessionSegments_Table ON dbo.PlaySessionTableSegments(TableId);
                END;

                IF EXISTS (SELECT 1 FROM dbo.PlaySessions s WHERE NOT EXISTS (SELECT 1 FROM dbo.PlaySessionTableSegments g WHERE g.SessionId = s.Id))
                BEGIN
                    INSERT INTO dbo.PlaySessionTableSegments(SessionId, TableId, HourlyRateSnapshot, StartAtUtc, EndAtUtc)
                    SELECT s.Id, s.TableId, s.HourlyRateSnapshot, s.StartAtUtc, s.EndAtUtc
                    FROM dbo.PlaySessions s
                    WHERE NOT EXISTS (SELECT 1 FROM dbo.PlaySessionTableSegments g WHERE g.SessionId = s.Id);
                END;

                IF NOT EXISTS (SELECT 1 FROM dbo.AspNetRoles WHERE NormalizedName = N'MANAGER')
                BEGIN
                    INSERT INTO dbo.AspNetRoles(Id, Name, NormalizedName, ConcurrencyStamp)
                    VALUES (N'role-manager', N'Manager', N'MANAGER', CONVERT(nvarchar(36), NEWID()));
                END;

                IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers WHERE NormalizedUserName = N'MANAGER.DEMO')
                BEGIN
                    INSERT INTO dbo.AspNetUsers(
                        Id, UserName, NormalizedUserName, Email, NormalizedEmail, FullName,
                        PhoneNumber, IsActive, EmployeeCode, HireDate, SecurityStamp, ConcurrencyStamp,
                        EmailConfirmed, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount,
                        RewardPoints, TotalSpend, MarketingOptIn, AccountSource, CreatedAtUtc)
                    VALUES (
                        N'demo-manager', N'manager.demo', N'MANAGER.DEMO', N'manager@example.test', N'MANAGER@EXAMPLE.TEST',
                        N'Quản lý CLB Mẫu', N'0900000009', 1, N'MG001', '2026-01-01', NEWID(), NEWID(),
                        1, 1, 0, 1, 0,
                        0, 0, 1, 'Self', SYSUTCDATETIME());
                END;

                DECLARE @mgrRoleId nvarchar(450) = (SELECT TOP 1 Id FROM dbo.AspNetRoles WHERE NormalizedName = N'MANAGER');
                DECLARE @mgrUserId nvarchar(450) = (SELECT TOP 1 Id FROM dbo.AspNetUsers WHERE NormalizedUserName = N'MANAGER.DEMO');
                IF @mgrRoleId IS NOT NULL AND @mgrUserId IS NOT NULL
                   AND NOT EXISTS (SELECT 1 FROM dbo.AspNetUserRoles WHERE UserId = @mgrUserId AND RoleId = @mgrRoleId)
                BEGIN
                    INSERT INTO dbo.AspNetUserRoles(UserId, RoleId) VALUES (@mgrUserId, @mgrRoleId);
                END;

                -- Đồng bộ toàn bộ dữ liệu nghiệp vụ điều chỉnh điểm / chiến dịch từ Admin mẫu sang Manager mẫu
                IF @mgrUserId IS NOT NULL AND OBJECT_ID('dbo.PointAdjustmentRequests', 'U') IS NOT NULL
                BEGIN
                    UPDATE dbo.PointAdjustmentRequests
                    SET RequestedById = @mgrUserId
                    WHERE RequestedById = N'demo-admin'
                       OR RequestedById IN (
                           SELECT ur.UserId FROM dbo.AspNetUserRoles ur
                           JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
                           WHERE r.NormalizedName = N'ADMIN'
                       );

                    UPDATE dbo.PointAdjustmentRequests
                    SET ReviewedById = @mgrUserId
                    WHERE ReviewedById = N'demo-admin'
                       OR ReviewedById IN (
                           SELECT ur.UserId FROM dbo.AspNetUserRoles ur
                           JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
                           WHERE r.NormalizedName = N'ADMIN'
                       );

                    UPDATE dbo.PointAdjustmentRequests
                    SET ReviewNote = REPLACE(ReviewNote, N'Admin', N'Manager')
                    WHERE ReviewNote LIKE N'%Admin%';
                END;

                IF @mgrUserId IS NOT NULL AND OBJECT_ID('dbo.Campaigns', 'U') IS NOT NULL
                BEGIN
                    UPDATE dbo.Campaigns
                    SET CreatedById = @mgrUserId
                    WHERE CreatedById = N'demo-admin';
                END;

                -- Danh mục Gậy cho thuê & dữ liệu mẫu
                IF NOT EXISTS (SELECT 1 FROM dbo.ProductCategories WHERE Name = N'Gậy cho thuê')
                BEGIN
                    INSERT INTO dbo.ProductCategories(Name) VALUES (N'Gậy cho thuê');
                END;

                DECLARE @CueCatId int = (SELECT TOP 1 Id FROM dbo.ProductCategories WHERE Name = N'Gậy cho thuê');
                IF @CueCatId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Products WHERE CategoryId = @CueCatId)
                BEGIN
                    INSERT INTO dbo.Products(CategoryId, Name, Price, StockQuantity, IsActive) VALUES
                        (@CueCatId, N'Gậy Carbon Predator (Thuê theo lượt)', 50000, 10, 1),
                        (@CueCatId, N'Gậy Cá Nhân Fury (Thuê theo lượt)', 30000, 15, 1),
                        (@CueCatId, N'Gậy Nhảy / Phá Chuyên Dụng (Thuê theo lượt)', 25000, 8, 1);
                END;

                -- Dữ liệu mẫu Combo Giờ chơi tặng kèm Nước
                IF NOT EXISTS (SELECT 1 FROM dbo.Combos)
                BEGIN
                    INSERT INTO dbo.Combos(Name, Price, PlaytimeHours, IsActive) VALUES
                        (N'Combo 2 Giờ Chơi + Tặng 2 Trà Đào', 190000, 2.0, 1),
                        (N'Combo 3 Giờ Chơi + Tặng 2 Nước Ngọt & Khoai Tây', 280000, 3.0, 1);

                    DECLARE @Combo1 int = (SELECT TOP 1 Id FROM dbo.Combos WHERE Name LIKE N'Combo 2 Giờ%');
                    DECLARE @Combo2 int = (SELECT TOP 1 Id FROM dbo.Combos WHERE Name LIKE N'Combo 3 Giờ%');
                    DECLARE @ProdTraDao int = (SELECT TOP 1 Id FROM dbo.Products WHERE Name = N'Trà đào');
                    DECLARE @ProdNuocNgot int = (SELECT TOP 1 Id FROM dbo.Products WHERE Name = N'Nước ngọt');
                    DECLARE @ProdKhoai int = (SELECT TOP 1 Id FROM dbo.Products WHERE Name = N'Khoai tây chiên');

                    IF @Combo1 IS NOT NULL AND @ProdTraDao IS NOT NULL
                        INSERT INTO dbo.ComboItems(ComboId, ProductId, Quantity) VALUES (@Combo1, @ProdTraDao, 2);
                    IF @Combo2 IS NOT NULL AND @ProdNuocNgot IS NOT NULL
                        INSERT INTO dbo.ComboItems(ComboId, ProductId, Quantity) VALUES (@Combo2, @ProdNuocNgot, 2);
                    IF @Combo2 IS NOT NULL AND @ProdKhoai IS NOT NULL
                        INSERT INTO dbo.ComboItems(ComboId, ProductId, Quantity) VALUES (@Combo2, @ProdKhoai, 1);
                END;
                """);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Không thể tự khởi tạo dữ liệu mẫu Role Manager / Gậy cho thuê / Combo.");
        }

        // 4. Chuẩn hóa dữ liệu Gmail hội viên (sửa email sai định dạng / thiếu email -> @gmail.com) & đồng bộ Tổng mức chi tiêu
        try
        {
            await NormalizeCustomerDataAsync(db);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Không thể chuẩn hóa dữ liệu email hội viên.");
        }

        // 5. Nâng cấp WF2: Gọi món & Kho (Tùng)
        try
        {
            await db.Database.ExecuteSqlRawAsync("""
                SET ANSI_NULLS ON;
                SET QUOTED_IDENTIFIER ON;

                IF OBJECT_ID('dbo.ProductPriceHistory', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.ProductPriceHistory (
                        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProductPriceHistory PRIMARY KEY,
                        ProductId int NOT NULL CONSTRAINT FK_ProductPriceHistory_Product FOREIGN KEY REFERENCES dbo.Products(Id),
                        OldPrice decimal(18,2) NOT NULL,
                        NewPrice decimal(18,2) NOT NULL,
                        EffectiveDateUtc datetime2(0) NOT NULL CONSTRAINT DF_ProductPriceHistory_Date DEFAULT SYSUTCDATETIME(),
                        ChangedById nvarchar(450) NULL CONSTRAINT FK_ProductPriceHistory_User FOREIGN KEY REFERENCES dbo.AspNetUsers(Id),
                        Note nvarchar(255) NULL
                    );
                    CREATE INDEX IX_ProductPriceHistory_ProductId ON dbo.ProductPriceHistory(ProductId);
                END;

                IF OBJECT_ID('dbo.StockReceipts', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.StockReceipts (
                        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_StockReceipts PRIMARY KEY,
                        ReceiptCode nvarchar(50) NOT NULL CONSTRAINT UQ_StockReceipts_Code UNIQUE,
                        SupplierName nvarchar(100) NOT NULL,
                        TotalAmount decimal(18,2) NOT NULL CONSTRAINT DF_StockReceipts_Total DEFAULT 0,
                        Note nvarchar(500) NULL,
                        ReceiptDateUtc datetime2(0) NOT NULL CONSTRAINT DF_StockReceipts_Date DEFAULT SYSUTCDATETIME(),
                        CreatedById nvarchar(450) NOT NULL CONSTRAINT FK_StockReceipts_User FOREIGN KEY REFERENCES dbo.AspNetUsers(Id)
                    );
                    CREATE INDEX IX_StockReceipts_Date ON dbo.StockReceipts(ReceiptDateUtc);
                END;

                IF OBJECT_ID('dbo.StockReceiptDetails', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.StockReceiptDetails (
                        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_StockReceiptDetails PRIMARY KEY,
                        ReceiptId int NOT NULL CONSTRAINT FK_StockReceiptDetails_Receipt FOREIGN KEY REFERENCES dbo.StockReceipts(Id) ON DELETE CASCADE,
                        ProductId int NOT NULL CONSTRAINT FK_StockReceiptDetails_Product FOREIGN KEY REFERENCES dbo.Products(Id),
                        Quantity int NOT NULL CONSTRAINT CK_StockReceiptDetails_Qty CHECK (Quantity > 0),
                        CostPrice decimal(18,2) NOT NULL CONSTRAINT CK_StockReceiptDetails_Cost CHECK (CostPrice >= 0),
                        Subtotal AS (Quantity * CostPrice)
                    );
                    CREATE INDEX IX_StockReceiptDetails_ReceiptId ON dbo.StockReceiptDetails(ReceiptId);
                    CREATE INDEX IX_StockReceiptDetails_ProductId ON dbo.StockReceiptDetails(ProductId);
                END;

                IF OBJECT_ID('dbo.StockMovements', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.StockMovements (
                        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_StockMovements PRIMARY KEY,
                        ProductId int NOT NULL CONSTRAINT FK_StockMovements_Product FOREIGN KEY REFERENCES dbo.Products(Id),
                        QuantityChange int NOT NULL,
                        BalanceAfter int NOT NULL,
                        MovementType varchar(30) NOT NULL,
                        ReferenceType varchar(30) NULL,
                        ReferenceId int NULL,
                        Note nvarchar(300) NULL,
                        CreatedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_StockMovements_Date DEFAULT SYSUTCDATETIME(),
                        CreatedById nvarchar(450) NULL CONSTRAINT FK_StockMovements_User FOREIGN KEY REFERENCES dbo.AspNetUsers(Id)
                    );
                    CREATE INDEX IX_StockMovements_Product ON dbo.StockMovements(ProductId, CreatedAtUtc DESC);
                    CREATE INDEX IX_StockMovements_Ref ON dbo.StockMovements(ReferenceType, ReferenceId);
                END;

                IF COL_LENGTH('dbo.Combos', 'TableTypeId') IS NULL
                BEGIN
                    ALTER TABLE dbo.Combos ADD TableTypeId int NULL;
                    ALTER TABLE dbo.Combos ADD CONSTRAINT FK_Combos_TableType FOREIGN KEY (TableTypeId) REFERENCES dbo.TableTypes(Id);
                END;

                IF COL_LENGTH('dbo.Orders', 'OrderSource') IS NULL
                    ALTER TABLE dbo.Orders ADD OrderSource varchar(20) NOT NULL CONSTRAINT DF_Orders_Source DEFAULT 'Staff';
                IF COL_LENGTH('dbo.Orders', 'ClientSessionId') IS NULL
                    ALTER TABLE dbo.Orders ADD ClientSessionId int NULL;
                IF COL_LENGTH('dbo.Orders', 'RejectReason') IS NULL
                    ALTER TABLE dbo.Orders ADD RejectReason nvarchar(300) NULL;
                IF COL_LENGTH('dbo.Orders', 'Note') IS NULL
                    ALTER TABLE dbo.Orders ADD Note nvarchar(300) NULL;
                IF COL_LENGTH('dbo.Orders', 'CreatedById') IS NULL
                BEGIN
                    ALTER TABLE dbo.Orders ADD CreatedById nvarchar(450) NULL;
                    ALTER TABLE dbo.Orders ADD CONSTRAINT FK_Orders_CreatedBy FOREIGN KEY (CreatedById) REFERENCES dbo.AspNetUsers(Id);
                END;

                IF EXISTS (SELECT * FROM sys.check_constraints WHERE name = 'CK_Order_Status')
                BEGIN
                    ALTER TABLE dbo.Orders DROP CONSTRAINT CK_Order_Status;
                    ALTER TABLE dbo.Orders ADD CONSTRAINT CK_Order_Status CHECK (Status IN ('Pending', 'Preparing', 'Served', 'Completed', 'Cancelled', 'Rejected'));
                END;

                IF EXISTS (SELECT * FROM sys.check_constraints WHERE name = 'CK_Table_Status')
                BEGIN
                    ALTER TABLE dbo.BilliardTables DROP CONSTRAINT CK_Table_Status;
                    ALTER TABLE dbo.BilliardTables ADD CONSTRAINT CK_Table_Status CHECK (Status IN ('Available', 'InUse', 'AwaitingPayment', 'Maintenance'));
                END;

                IF COL_LENGTH('dbo.OrderDetails', 'Status') IS NULL
                    ALTER TABLE dbo.OrderDetails ADD Status varchar(20) NOT NULL CONSTRAINT DF_OrderDetails_Status DEFAULT 'Active';
                IF COL_LENGTH('dbo.OrderDetails', 'CancelledQuantity') IS NULL
                    ALTER TABLE dbo.OrderDetails ADD CancelledQuantity int NOT NULL CONSTRAINT DF_OrderDetails_CancelledQty DEFAULT 0;
                IF COL_LENGTH('dbo.OrderDetails', 'CancelledAtUtc') IS NULL
                    ALTER TABLE dbo.OrderDetails ADD CancelledAtUtc datetime2(0) NULL;
                IF COL_LENGTH('dbo.OrderDetails', 'CancelledById') IS NULL
                BEGIN
                    ALTER TABLE dbo.OrderDetails ADD CancelledById nvarchar(450) NULL;
                    ALTER TABLE dbo.OrderDetails ADD CONSTRAINT FK_OrderDetails_CancelledBy FOREIGN KEY (CancelledById) REFERENCES dbo.AspNetUsers(Id);
                END;
                IF COL_LENGTH('dbo.OrderDetails', 'CancelReason') IS NULL
                    ALTER TABLE dbo.OrderDetails ADD CancelReason nvarchar(300) NULL;
                """);

            await db.Database.ExecuteSqlRawAsync("""
                CREATE OR ALTER PROCEDURE dbo.usp_AddSessionCombo
                    @SessionId int,
                    @ComboId int,
                    @Quantity int = 1,
                    @StaffId nvarchar(450)
                AS
                BEGIN
                    SET NOCOUNT ON;
                    IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers u JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
                        JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
                        WHERE u.Id = @StaffId AND u.IsActive = 1 AND r.Name IN (N'Staff',N'Manager',N'Admin'))
                        THROW 51711, N'Active Staff or Admin required.', 1;

                    IF @Quantity IS NULL OR @Quantity <> 1
                        THROW 51712, N'Each session can only apply 1 Combo package (Quantity = 1).', 1;

                    DECLARE @TrancountAtStart int = @@TRANCOUNT;
                    IF @TrancountAtStart = 0
                        BEGIN TRANSACTION;
                    ELSE
                        SAVE TRANSACTION usp_AddSessionCombo_Savepoint;

                    BEGIN TRY
                        DECLARE @Now datetime2(0) = SYSUTCDATETIME(),
                                @SessionStatus varchar(20),
                                @ComboName nvarchar(100),
                                @ComboPrice decimal(18,2),
                                @ComboHours decimal(5,2),
                                @ComboActive bit,
                                @SessionComboId int,
                                @EstimatedAmount decimal(18,2);

                        SELECT @SessionStatus = Status
                          FROM dbo.PlaySessions WITH (UPDLOCK, HOLDLOCK)
                         WHERE Id = @SessionId;

                        IF @SessionStatus IS NULL OR @SessionStatus <> 'Active'
                            THROW 51713, N'Only an Active session can purchase a combo.', 1;

                        IF EXISTS (SELECT 1 FROM dbo.SessionCombos WHERE SessionId = @SessionId)
                            THROW 51715, N'This session already has an applied Combo package. Only 1 Combo is allowed per session.', 1;

                        SELECT @ComboName = Name,
                               @ComboPrice = Price,
                               @ComboHours = PlaytimeHours,
                               @ComboActive = IsActive
                          FROM dbo.Combos WITH (UPDLOCK, HOLDLOCK)
                         WHERE Id = @ComboId;

                        IF @ComboName IS NULL OR @ComboActive <> 1
                            THROW 51714, N'Selected combo does not exist or is no longer active.', 1;

                        -- BR-07a: Kiểm tra tồn kho của tất cả món kèm trong gói Combo
                        IF EXISTS (
                            SELECT 1
                            FROM dbo.ComboItems ci
                            JOIN dbo.Products p WITH (UPDLOCK, ROWLOCK) ON p.Id = ci.ProductId
                            WHERE ci.ComboId = @ComboId
                              AND p.StockQuantity < (ci.Quantity * @Quantity)
                        )
                            THROW 51716, N'Món kèm trong gói Combo đã hết hàng trong kho. Không thể áp dụng Combo này!', 1;

                        INSERT dbo.SessionCombos (
                            SessionId, ComboId, Quantity,
                            ComboNameSnapshot, UnitPriceSnapshot, PlaytimeHoursSnapshot,
                            PurchasedAtUtc, CreatedById
                        )
                        VALUES (
                            @SessionId, @ComboId, @Quantity,
                            @ComboName, @ComboPrice, @ComboHours,
                            @Now, @StaffId
                        );

                        SET @SessionComboId = CONVERT(int, SCOPE_IDENTITY());

                        INSERT dbo.SessionComboItems (
                            SessionComboId, ProductId, ProductNameSnapshot, QuantityPerCombo
                        )
                        SELECT @SessionComboId, ci.ProductId, p.Name, ci.Quantity
                          FROM dbo.ComboItems ci
                          JOIN dbo.Products p ON p.Id = ci.ProductId
                         WHERE ci.ComboId = @ComboId;

                        -- BR-07a: Trừ kho nguyên tử và ghi nhận thẻ kho StockMovements cho từng món kèm
                        DECLARE @ItemProdId int, @ItemReqQty int, @NewBal int;
                        DECLARE item_cursor CURSOR LOCAL FAST_FORWARD FOR
                            SELECT ci.ProductId, ci.Quantity * @Quantity
                            FROM dbo.ComboItems ci
                            WHERE ci.ComboId = @ComboId;

                        OPEN item_cursor;
                        FETCH NEXT FROM item_cursor INTO @ItemProdId, @ItemReqQty;
                        WHILE @@FETCH_STATUS = 0
                        BEGIN
                            UPDATE dbo.Products
                            SET StockQuantity = StockQuantity - @ItemReqQty
                            WHERE Id = @ItemProdId;

                            SELECT @NewBal = StockQuantity FROM dbo.Products WHERE Id = @ItemProdId;

                            INSERT INTO dbo.StockMovements (
                                ProductId, QuantityChange, BalanceAfter, MovementType,
                                ReferenceType, ReferenceId, Note, CreatedAtUtc, CreatedById
                            )
                            VALUES (
                                @ItemProdId, -@ItemReqQty, @NewBal, 'ComboDeduct',
                                'SessionCombo', @SessionComboId,
                                N'Trừ kho món kèm theo gói Combo ' + @ComboName,
                                @Now, @StaffId
                            );

                            FETCH NEXT FROM item_cursor INTO @ItemProdId, @ItemReqQty;
                        END;
                        CLOSE item_cursor;
                        DEALLOCATE item_cursor;

                        SET @EstimatedAmount = dbo.fn_CalcPlaytimeAmount(@SessionId, @Now);

                        IF @TrancountAtStart = 0
                            COMMIT;

                        SELECT @SessionComboId AS SessionComboId,
                               @SessionId AS SessionId,
                               @ComboId AS ComboId,
                               @Quantity AS Quantity,
                               @ComboName AS ComboNameSnapshot,
                               @ComboPrice AS UnitPriceSnapshot,
                               @ComboHours AS PlaytimeHoursSnapshot,
                               @Now AS PurchasedAtUtc,
                               @EstimatedAmount AS EstimatedAmount;
                    END TRY
                    BEGIN CATCH
                        IF @TrancountAtStart = 0
                        BEGIN
                            IF @@TRANCOUNT > 0 ROLLBACK;
                        END;
                        ELSE
                        BEGIN
                            IF XACT_STATE() <> -1
                                ROLLBACK TRANSACTION usp_AddSessionCombo_Savepoint;
                        END;
                        THROW;
                    END CATCH
                END;
                """);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Không thể tự nâng cấp schema WF2 (Gọi món & Kho).");
        }
    }

    private static async Task NormalizeCustomerDataAsync(ApplicationDbContext db)
    {
        var customerRoleId = await db.Roles.AsNoTracking()
            .Where(r => r.Name == "Customer")
            .Select(r => r.Id)
            .FirstOrDefaultAsync();
        if (string.IsNullOrEmpty(customerRoleId)) return;

        var customers = await db.Users
            .Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == customerRoleId))
            .ToListAsync();

        var earnTotals = await db.PointTransactions.AsNoTracking()
            .Where(t => t.TransactionType == "Earn" && t.SpendAmount != null)
            .GroupBy(t => t.CustomerId)
            .Select(g => new { CustomerId = g.Key, Total = g.Sum(t => t.SpendAmount ?? 0m) })
            .ToDictionaryAsync(x => x.CustomerId, x => x.Total);

        var strictGmailRx = new Regex(@"^[a-zA-Z0-9._%+\-]+@gmail\.com$", RegexOptions.IgnoreCase);
        var strictNameRx = new Regex(@"^[\p{L}\p{M}]+(\s+[\p{L}\p{M}]+)*$");
        var usedEmails = new HashSet<string>(
            await db.Users.AsNoTracking()
                .Where(u => u.Email != null)
                .Select(u => u.Email!.ToLowerInvariant())
                .ToListAsync(),
            StringComparer.OrdinalIgnoreCase);

        bool changed = false;
        int seq = 1;
        foreach (var u in customers)
        {
            // Sửa họ tên nếu chứa số/ký tự lạ từ dữ liệu test cũ
            if (string.IsNullOrWhiteSpace(u.FullName) || !strictNameRx.IsMatch(u.FullName.Trim()))
            {
                u.FullName = $"Nguyễn Minh Quân";
                changed = true;
            }

            // Chuẩn hóa Email sang @gmail.com nếu bị trống, dùng domain nội bộ (.local, .test) hoặc sai định dạng Gmail
            string currentEmail = (u.Email ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(currentEmail) || !strictGmailRx.IsMatch(currentEmail))
            {
                string slug = ToAsciiSlug(u.FullName);
                if (string.IsNullOrEmpty(slug)) slug = "hoivienbms";

                string phoneSuffix = string.Empty;
                if (!string.IsNullOrWhiteSpace(u.PhoneNumber) && u.PhoneNumber.Length >= 3)
                {
                    var digits = new string(u.PhoneNumber.Where(char.IsDigit).ToArray());
                    if (digits.Length >= 3) phoneSuffix = digits[^3..];
                }
                if (string.IsNullOrEmpty(phoneSuffix))
                {
                    phoneSuffix = (100 + seq).ToString();
                }

                string candidate = $"{slug}{phoneSuffix}@gmail.com";
                int dup = 1;
                while (usedEmails.Contains(candidate))
                {
                    dup++;
                    candidate = $"{slug}{phoneSuffix}.{dup}@gmail.com";
                }

                usedEmails.Add(candidate);
                u.Email = candidate;
                u.NormalizedEmail = candidate.ToUpperInvariant();
                changed = true;
            }

            // Đồng bộ Tổng mức chi tiêu (TotalSpend)
            if (earnTotals.TryGetValue(u.Id, out var calcTotal) && calcTotal > u.TotalSpend)
            {
                u.TotalSpend = calcTotal;
                changed = true;
            }

            seq++;
        }

        if (changed)
        {
            await db.SaveChangesAsync();
        }
    }

    private static string ToAsciiSlug(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        string normalized = input.Trim().Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (char c in normalized)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat != UnicodeCategory.NonSpacingMark && char.IsLetter(c) && c <= 'z')
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }
        return sb.ToString();
    }
}
