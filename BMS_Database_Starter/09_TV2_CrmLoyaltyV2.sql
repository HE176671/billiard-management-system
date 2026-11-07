/* =====================================================================
   09_TV2_CrmLoyaltyV2.sql  (TV2 — CRM & Loyalty V2 Upgrade)
   Idempotent, additive migration script. Safe to re-run at any time.
   Run: sqlcmd -S "localhost\SQLEXPRESS" -d BilliardDB -E -C -i BMS_Database_Starter\09_TV2_CrmLoyaltyV2.sql
   ===================================================================== */
USE BilliardDB;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

-- ═════════════════════════════════════════════════════════════════════
-- BATCH 1: Đảm bảo các cột/bảng nền tảng của TV2 và thêm cột mới V2
-- ═════════════════════════════════════════════════════════════════════
BEGIN TRANSACTION;

-- 1.1. AspNetUsers — cột TV2 cơ bản & cột V2
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

-- 1.2. MembershipTiers — cột TV2 cơ bản & cột quyền lợi V2
IF COL_LENGTH('dbo.MembershipTiers', 'SpendThreshold') IS NULL
    ALTER TABLE dbo.MembershipTiers ADD SpendThreshold decimal(18,2) NOT NULL CONSTRAINT DF_Tier_SpendThreshold DEFAULT 0;
IF COL_LENGTH('dbo.MembershipTiers', 'PointMultiplier') IS NULL
    ALTER TABLE dbo.MembershipTiers ADD PointMultiplier int NOT NULL CONSTRAINT DF_Tier_PointMultiplier DEFAULT 1;
IF COL_LENGTH('dbo.MembershipTiers', 'BirthdayVoucherValue') IS NULL
    ALTER TABLE dbo.MembershipTiers ADD BirthdayVoucherValue decimal(18,2) NOT NULL CONSTRAINT DF_Tier_BirthdayVoucher DEFAULT 0;
IF COL_LENGTH('dbo.MembershipTiers', 'SortOrder') IS NULL
    ALTER TABLE dbo.MembershipTiers ADD SortOrder int NOT NULL CONSTRAINT DF_Tier_SortOrder DEFAULT 0;

-- 1.3. PointTransactions — đảm bảo bảng tồn tại & thêm cột sổ cái V2
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

-- 1.4. Notifications & Reviews.AdminReply (đảm bảo tồn tại)
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

COMMIT TRANSACTION;
GO

-- ═════════════════════════════════════════════════════════════════════
-- BATCH 2: Ràng buộc CHECK, FK, Index và Backfill trên các bảng nâng cấp
-- ═════════════════════════════════════════════════════════════════════
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID('dbo.CK_User_AccountSource', 'C') IS NULL
    ALTER TABLE dbo.AspNetUsers WITH CHECK ADD CONSTRAINT CK_User_AccountSource
        CHECK (AccountSource IN ('Counter', 'Self'));

-- Cập nhật AccountSource='Counter' cho các tài khoản đã tạo tại quầy (@noemail.bms.local)
UPDATE dbo.AspNetUsers
SET AccountSource = 'Counter'
WHERE Email LIKE '%@noemail.bms.local' AND AccountSource <> 'Counter';

-- Tìm và xóa mọi CHECK constraint cũ trên cột PointTransactions.TransactionType
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

-- Backfill BalanceAfter cho dữ liệu cũ nếu đang bằng 0
;WITH Running AS (
    SELECT Id,
           SUM(Amount) OVER (PARTITION BY CustomerId ORDER BY CreatedAt, Id ROWS UNBOUNDED PRECEDING) AS CalcBalance
    FROM dbo.PointTransactions
)
UPDATE pt
SET pt.BalanceAfter = CASE WHEN r.CalcBalance < 0 THEN 0 ELSE r.CalcBalance END
FROM dbo.PointTransactions pt
JOIN Running r ON r.Id = pt.Id
WHERE pt.BalanceAfter = 0 AND pt.Amount <> 0;

-- Cập nhật BirthdayVoucherValue & SortOrder cho Member / Silver / Gold
UPDATE dbo.MembershipTiers SET BirthdayVoucherValue = 0,     SortOrder = 1 WHERE TierName IN (N'Member', N'Đồng') AND SortOrder = 0;
UPDATE dbo.MembershipTiers SET BirthdayVoucherValue = 30000, SortOrder = 2 WHERE TierName IN (N'Silver', N'Bạc')  AND SortOrder = 0;
UPDATE dbo.MembershipTiers SET BirthdayVoucherValue = 60000, SortOrder = 3 WHERE TierName IN (N'Gold',   N'Vàng') AND SortOrder = 0;

COMMIT TRANSACTION;
GO

-- ═════════════════════════════════════════════════════════════════════
-- BATCH 3: Tạo 6 bảng mới của TV2 CRM & Loyalty V2 + Seed RewardItems
-- ═════════════════════════════════════════════════════════════════════
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- 3.4.1. RewardItems (thay thế RewardCatalog hard-code)
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

-- Seed 4 phần quà chuẩn nếu chưa có
DECLARE @GoldTierId int = (SELECT TOP 1 Id FROM dbo.MembershipTiers WHERE TierName IN (N'Gold', N'Vàng') ORDER BY SpendThreshold DESC, Id DESC);

IF NOT EXISTS (SELECT 1 FROM dbo.RewardItems WHERE Code = 'DRINK')
    INSERT INTO dbo.RewardItems (Code, Name, Description, PointCost, RewardType, DiscountValue, ValidDays, MinTierId, IsActive)
    VALUES ('DRINK', N'Nước uống miễn phí', N'1 lon nước ngọt / nước suối / Bò húc tự chọn tại quầy', 20, 'Gift', 20000, 30, NULL, 1);
ELSE
    UPDATE dbo.RewardItems SET Name = N'Nước uống miễn phí', Description = N'1 lon nước ngọt / nước suối / Bò húc tự chọn tại quầy' WHERE Code = 'DRINK';

IF NOT EXISTS (SELECT 1 FROM dbo.RewardItems WHERE Code = 'SNACK')
    INSERT INTO dbo.RewardItems (Code, Name, Description, PointCost, RewardType, DiscountValue, ValidDays, MinTierId, IsActive)
    VALUES ('SNACK', N'Đồ ăn nhẹ', N'1 đĩa trái cây / hướng dương / khô gà', 50, 'Gift', 50000, 30, NULL, 1);
ELSE
    UPDATE dbo.RewardItems SET Name = N'Đồ ăn nhẹ', Description = N'1 đĩa trái cây / hướng dương / khô gà' WHERE Code = 'SNACK';

IF NOT EXISTS (SELECT 1 FROM dbo.RewardItems WHERE Code = 'FREEHOUR')
    INSERT INTO dbo.RewardItems (Code, Name, Description, PointCost, RewardType, DiscountValue, ValidDays, MinTierId, IsActive)
    VALUES ('FREEHOUR', N'1 giờ chơi miễn phí', N'Giảm tối đa 1 giờ chơi (tối đa 80.000đ) trực tiếp trên hóa đơn', 80, 'FreeHour', 80000, 30, NULL, 1);
ELSE
    UPDATE dbo.RewardItems SET Name = N'1 giờ chơi miễn phí', Description = N'Giảm tối đa 1 giờ chơi (tối đa 80.000đ) trực tiếp trên hóa đơn' WHERE Code = 'FREEHOUR';

IF NOT EXISTS (SELECT 1 FROM dbo.RewardItems WHERE Code = 'VIP_TABLE')
    INSERT INTO dbo.RewardItems (Code, Name, Description, PointCost, RewardType, DiscountValue, ValidDays, MinTierId, IsActive)
    VALUES ('VIP_TABLE', N'Voucher giảm 150.000đ bàn VIP', N'Ưu đãi đặc quyền dành cho hội viên hạng Gold trở lên', 150, 'BillDiscount', 150000, 30, @GoldTierId, 1);
ELSE
    UPDATE dbo.RewardItems SET Name = N'Voucher giảm 150.000đ bàn VIP', Description = N'Ưu đãi đặc quyền dành cho hội viên hạng Gold trở lên' WHERE Code = 'VIP_TABLE';

-- 3.4.2. Campaigns
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

-- 3.4.3. CustomerVouchers
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

-- 3.4.4. PointAdjustmentRequests (maker-checker)
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

-- 3.4.5. CustomerNotes
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

-- 3.4.6. CustomerAuditLogs
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

COMMIT TRANSACTION;
GO

