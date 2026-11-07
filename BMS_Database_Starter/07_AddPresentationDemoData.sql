-- DU LIEU GIA LAP cho buoi bao cao 13/10/2026, KHONG phai giao dich thuc.
-- Additive, transactional, repeatable. Does not reset or overwrite existing data.
-- Run with sqlcmd -f 65001 or open the UTF-8 file directly in SSMS.
USE BilliardDB;
GO
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
GO
BEGIN TRY
    BEGIN TRANSACTION;

    -- 1. HANG THANH VIEN: only insert missing names, never change existing rules.
    DECLARE @Tiers TABLE (TierName nvarchar(50), MinPoints int, DiscountPercent decimal(5,2));
    INSERT @Tiers VALUES (N'Đồng',0,0),(N'Bạc',500,5),(N'Vàng',1500,10),(N'Kim Cương',3000,15);
    IF EXISTS (SELECT 1 FROM @Tiers d JOIN dbo.MembershipTiers t ON t.TierName=d.TierName
               WHERE t.MinPoints<>d.MinPoints OR t.DiscountPercent<>d.DiscountPercent)
        THROW 50010, 'Demo tier names already have different rules. No data has been changed.', 1;
    IF EXISTS (SELECT 1 FROM @Tiers d JOIN dbo.MembershipTiers t ON t.MinPoints=d.MinPoints AND t.TierName<>d.TierName)
        THROW 50011, 'Demo point thresholds conflict with existing tiers. No data has been changed.', 1;
    INSERT dbo.MembershipTiers(TierName,MinPoints,DiscountPercent)
    SELECT d.TierName,d.MinPoints,d.DiscountPercent FROM @Tiers d
    WHERE NOT EXISTS (SELECT 1 FROM dbo.MembershipTiers t WHERE t.TierName=d.TierName);

    -- 2. KHACH HANG: independent demo accounts; no password is stored in SQL.
    DECLARE @Customers TABLE (Id nvarchar(450), UserName nvarchar(256), FullName nvarchar(100),
        Points int, TierName nvarchar(50), IsActive bit);
    INSERT @Customers VALUES
        (N'presentation-customer-dong',N'demo.report.dong',N'Nguyễn Minh An — Demo',120,N'Đồng',1),
        (N'presentation-customer-bac',N'demo.report.bac',N'Trần Ngọc Mai — Demo',650,N'Bạc',1),
        (N'presentation-customer-vang',N'demo.report.vang',N'Lê Hoàng Nam — Demo',1800,N'Vàng',1),
        (N'presentation-customer-diamond',N'demo.report.diamond',N'Phạm Thu Hà — Demo',4000,N'Kim Cương',0);
    DECLARE @CustomerRole nvarchar(450) = (SELECT Id FROM dbo.AspNetRoles WHERE NormalizedName=N'CUSTOMER');
    IF @CustomerRole IS NULL THROW 50012, 'Customer role missing.', 1;
    IF EXISTS (SELECT 1 FROM @Customers d JOIN dbo.AspNetUsers u ON
        u.Id=d.Id OR u.NormalizedUserName=UPPER(d.UserName) OR u.NormalizedEmail=UPPER(d.UserName+N'@example.invalid')
        WHERE u.Id<>d.Id OR u.UserName<>d.UserName OR ISNULL(u.Email,N'')<>d.UserName+N'@example.invalid')
        THROW 50013, 'Demo customer identity conflicts with existing accounts.', 1;
    INSERT dbo.AspNetUsers(Id,UserName,NormalizedUserName,Email,NormalizedEmail,FullName,
        SecurityStamp,ConcurrencyStamp,RewardPoints,TierId,IsActive,CreatedAtUtc)
    SELECT d.Id,d.UserName,UPPER(d.UserName),d.UserName+N'@example.invalid',UPPER(d.UserName+N'@example.invalid'),
        d.FullName,CONVERT(nvarchar(36),NEWID()),CONVERT(nvarchar(36),NEWID()),d.Points,t.Id,d.IsActive,'2026-09-20T03:00:00'
    FROM @Customers d JOIN dbo.MembershipTiers t ON t.TierName=d.TierName
    WHERE NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers u WHERE u.Id=d.Id);
    INSERT dbo.AspNetUserRoles(UserId,RoleId)
    SELECT d.Id,@CustomerRole FROM @Customers d
    WHERE NOT EXISTS (SELECT 1 FROM dbo.AspNetUserRoles ur WHERE ur.UserId=d.Id AND ur.RoleId=@CustomerRole);

    -- 3. BANG GIA: three adjacent slots for Pool and Carom, no overlap.
    DECLARE @Pricing TABLE (TypeName nvarchar(50), StartHour time, EndHour time, Rate decimal(18,2));
    INSERT @Pricing VALUES
        (N'Pool','08:00','12:00',80000),(N'Pool','12:00','18:00',100000),(N'Pool','18:00','23:59',120000),
        (N'Carom','08:00','12:00',100000),(N'Carom','12:00','18:00',120000),(N'Carom','18:00','23:59',140000);
    IF EXISTS (SELECT 1 FROM @Pricing d WHERE NOT EXISTS (SELECT 1 FROM dbo.TableTypes t WHERE t.Name=d.TypeName))
        THROW 50014, 'Expected Pool and Carom table types are missing.', 1;
    IF EXISTS (SELECT 1 FROM @Pricing d JOIN dbo.TableTypes t ON t.Name=d.TypeName
        JOIN dbo.PricingConfigs p ON p.TableTypeId=t.Id AND p.StartHour<d.EndHour AND d.StartHour<p.EndHour
        WHERE p.StartHour<>d.StartHour OR p.EndHour<>d.EndHour OR p.HourlyRate<>d.Rate)
        THROW 50015, 'Demo pricing overlaps with existing pricing. Existing rules have not been changed.', 1;
    INSERT dbo.PricingConfigs(TableTypeId,StartHour,EndHour,HourlyRate)
    SELECT t.Id,d.StartHour,d.EndHour,d.Rate FROM @Pricing d JOIN dbo.TableTypes t ON t.Name=d.TypeName
    WHERE NOT EXISTS (SELECT 1 FROM dbo.PricingConfigs p WHERE p.TableTypeId=t.Id
        AND p.StartHour=d.StartHour AND p.EndHour=d.EndHour AND p.HourlyRate=d.Rate);

    -- 4. PHIEN + HOA DON + THANH TOAN: synthetic historical/demo sessions.
    -- Reuse an available table as history only; never change its current status.
    DECLARE @TableId int = (SELECT TOP(1) b.Id FROM dbo.BilliardTables b JOIN dbo.TableTypes t ON t.Id=b.TableTypeId
        WHERE t.Name=N'Pool' AND b.Status='Available' ORDER BY b.Id);
    DECLARE @StaffId nvarchar(450) = (SELECT TOP(1) u.Id FROM dbo.AspNetUsers u
        JOIN dbo.AspNetUserRoles ur ON ur.UserId=u.Id JOIN dbo.AspNetRoles r ON r.Id=ur.RoleId
        WHERE r.NormalizedName=N'STAFF' AND u.IsActive=1 ORDER BY u.Id);
    IF @TableId IS NULL OR @StaffId IS NULL THROW 50016, 'An available Pool table and active Staff account are required.', 1;
    DECLARE @Sales TABLE (No int, LocalDay date, CustomerId nvarchar(450), Discount decimal(18,2), Method varchar(20), Split bit);
    INSERT @Sales VALUES
        (1,'2026-09-28',N'presentation-customer-dong',0,'Cash',0),
        (2,'2026-10-05',N'presentation-customer-bac',10000,'VNPay',0),
        (3,'2026-10-10',N'presentation-customer-vang',20000,'Transfer',0),
        (4,'2026-10-12',N'presentation-customer-dong',0,'Cash',0),
        (5,'2026-10-12',N'presentation-customer-bac',10000,'Cash',1),
        (6,'2026-10-13',N'presentation-customer-vang',20000,'VNPay',0),
        (7,'2026-10-13',N'presentation-customer-diamond',30000,'SePay',0),
        (8,'2026-10-13',N'presentation-customer-dong',0,NULL,0);
    DECLARE @No int=1;
    WHILE @No<=8
    BEGIN
        DECLARE @Ref nvarchar(100)=CONCAT(N'BMS-PRESENTATION-20261013-',@No);
        DECLARE @Day date, @CustomerId nvarchar(450), @Discount decimal(18,2), @Method varchar(20), @Split bit;
        SELECT @Day=LocalDay,@CustomerId=CustomerId,@Discount=Discount,@Method=Method,@Split=Split FROM @Sales WHERE No=@No;
        -- Stable date/customer signature also identifies the unpaid demo invoice.
        DECLARE @Start datetime2(0)=DATEADD(hour,5+2*(@No%3),CAST(@Day AS datetime2(0)));
        DECLARE @End datetime2(0)=DATEADD(hour,2,@Start);
        IF NOT EXISTS (SELECT 1 FROM dbo.Invoices i JOIN dbo.PlaySessions s ON s.Id=i.SessionId
            WHERE i.CustomerId=@CustomerId AND s.StartAtUtc=@Start AND s.EndAtUtc=@End)
        BEGIN
            INSERT dbo.PlaySessions(TableId,CustomerId,OpenedById,ClosedById,StartAtUtc,EndAtUtc,HourlyRateSnapshot,PlaytimeAmount,Status)
            VALUES(@TableId,@CustomerId,@StaffId,@StaffId,@Start,@End,100000,200000,'Closed');
            DECLARE @SessionId int=CONVERT(int,SCOPE_IDENTITY());
            INSERT dbo.Invoices(SessionId,CustomerId,TotalPlaytime,TotalFandB,DiscountAmount,Status,CreatedAtUtc)
            VALUES(@SessionId,@CustomerId,200000,0,@Discount,CASE WHEN @Method IS NULL THEN 'Unpaid' ELSE 'Paid' END,@End);
            DECLARE @InvoiceId int=CONVERT(int,SCOPE_IDENTITY());
            DECLARE @Amount decimal(18,2)=200000-@Discount;
            IF @Method IS NOT NULL
            BEGIN
                INSERT dbo.PaymentTransactions(InvoiceId,PaymentMethod,AmountPaid,GatewayRefCode,TransactionTimeUtc)
                VALUES(@InvoiceId,@Method,CASE WHEN @Split=1 THEN @Amount/2 ELSE @Amount END,@Ref,DATEADD(minute,5,@End));
                IF @Split=1
                    INSERT dbo.PaymentTransactions(InvoiceId,PaymentMethod,AmountPaid,GatewayRefCode,TransactionTimeUtc)
                    VALUES(@InvoiceId,'VNPay',@Amount/2,@Ref+N'-PART2',DATEADD(minute,10,@End));
            END;
        END;
        SET @No+=1;
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
-- Demo-only summary; pre-existing payments are not included.
SELECT CONVERT(date,DATEADD(hour,7,TransactionTimeUtc)) AS VietnamDay,
    SUM(AmountPaid) AS DemoRevenueVND,COUNT(*) AS Transactions,COUNT(DISTINCT InvoiceId) AS PaidInvoices
FROM dbo.PaymentTransactions WHERE GatewayRefCode LIKE N'BMS-PRESENTATION-20261013-%'
GROUP BY CONVERT(date,DATEADD(hour,7,TransactionTimeUtc)) ORDER BY VietnamDay;
SELECT COUNT(*) AS DemoInvoices, SUM(CASE WHEN Status='Unpaid' THEN 1 ELSE 0 END) AS UnpaidInvoices
FROM dbo.Invoices WHERE CustomerId LIKE N'presentation-customer-%';
