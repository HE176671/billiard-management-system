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

        IF COL_LENGTH('dbo.Reviews', 'AdminReply') IS NULL
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
                PointCost int NOT NULL CONSTRAINT CK_RewardItems_Cost CHECK (PointCost > 0),
                RewardType varchar(20) NOT NULL CONSTRAINT CK_RewardItems_Type CHECK (RewardType IN ('Gift', 'FreeHour', 'BillDiscount')),
                DiscountValue decimal(18,2) NULL,
                ValidDays int NOT NULL CONSTRAINT DF_RewardItems_ValidDays DEFAULT 30,
                MinTierId int NULL,
                IsActive bit NOT NULL CONSTRAINT DF_RewardItems_IsActive DEFAULT 1,
                CONSTRAINT FK_RewardItems_MinTier FOREIGN KEY (MinTierId) REFERENCES dbo.MembershipTiers(Id) ON DELETE NO ACTION
            );
        END;

        DECLARE @GoldTierId int = (SELECT TOP 1 Id FROM dbo.MembershipTiers WHERE TierName IN (N'Gold', N'Vàng') ORDER BY SpendThreshold DESC, Id DESC);
        IF NOT EXISTS (SELECT 1 FROM dbo.RewardItems WHERE Code = 'DRINK')
            INSERT INTO dbo.RewardItems (Code, Name, Description, PointCost, RewardType, DiscountValue, ValidDays, MinTierId, IsActive)
            VALUES ('DRINK', N'Nước uống miễn phí', N'1 lon nước ngọt / nước suối / Bò húc tự chọn tại quầy', 20, 'Gift', 20000, 30, NULL, 1);
        IF NOT EXISTS (SELECT 1 FROM dbo.RewardItems WHERE Code = 'SNACK')
            INSERT INTO dbo.RewardItems (Code, Name, Description, PointCost, RewardType, DiscountValue, ValidDays, MinTierId, IsActive)
            VALUES ('SNACK', N'Đồ ăn nhẹ', N'1 đĩa trái cây / hướng dương / khô gà', 50, 'Gift', 50000, 30, NULL, 1);
        IF NOT EXISTS (SELECT 1 FROM dbo.RewardItems WHERE Code = 'FREEHOUR')
            INSERT INTO dbo.RewardItems (Code, Name, Description, PointCost, RewardType, DiscountValue, ValidDays, MinTierId, IsActive)
            VALUES ('FREEHOUR', N'1 giờ chơi miễn phí', N'Giảm tối đa 1 giờ chơi (tối đa 80.000đ) trực tiếp trên hóa đơn', 80, 'FreeHour', 80000, 30, NULL, 1);
        IF NOT EXISTS (SELECT 1 FROM dbo.RewardItems WHERE Code = 'VIP_TABLE')
            INSERT INTO dbo.RewardItems (Code, Name, Description, PointCost, RewardType, DiscountValue, ValidDays, MinTierId, IsActive)
            VALUES ('VIP_TABLE', N'Voucher giảm 150.000đ bàn VIP', N'Ưu đãi đặc quyền dành cho hội viên hạng Gold trở lên', 150, 'BillDiscount', 150000, 30, @GoldTierId, 1);

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
            await db.Database.ExecuteSqlRawAsync(CrmLoyaltyV2Batches[0]);
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

                IF COL_LENGTH('dbo.Reviews', 'AdminReply') IS NULL
                    ALTER TABLE dbo.Reviews ADD AdminReply nvarchar(max) NULL;

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
    }
}
