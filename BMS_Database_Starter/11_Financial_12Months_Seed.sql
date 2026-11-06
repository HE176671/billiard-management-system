/* ============================================================================
   11_Financial_12Months_Seed.sql
   PHÂN HỆ QUẢN TRỊ TÀI CHÍNH, BÁO CÁO LÃI/LỖ (P&L) & KIỂM TOÁN THẤT THOÁT (12 THÁNG)
   - Tạo bảng dbo.OperatingExpenses (Chi phí Bảo trì bàn/gậy/bi, Nhập bia/nước, Điện nước, Lương)
   - Bổ sung các mặt hàng đồ uống chủ lực (Bò Húc, Bia Tiger, Nước tăng lực Sting) nếu chưa có
   - Seed dữ liệu vận hành 12 tháng năm 2026 (Tháng 01 -> Tháng 12):
       + Tháng 3, 4, 5: Cao điểm doanh thu giờ chơi
       + Tháng 4, 5, 6: Cao điểm tỷ lệ bán nước giải khát & bia
       + Khung giờ Đêm (18:00 - 02:00) chiếm ~74% doanh thu so với Ban ngày (08:00 - 18:00)
       + Cả tháng Lãi (T1, T3, T4, T5, T6, T7, T11, T12) và tháng Lỗ (T2, T8, T9, T10)
       + 5 kịch bản Sai lệch dữ liệu / Thất thoát doanh thu (LEAK-01 -> LEAK-05) để kiểm toán
   - Idempotent (chạy lại nhiều lần an toàn, không nhân đôi dữ liệu, không xóa dữ liệu cũ)
   ============================================================================ */
USE [BilliardDB];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
GO

-- 1. TẠO BẢNG SỔ CÁI CHI PHÍ VẬN HÀNH (dbo.OperatingExpenses) NẾU CHƯA CÓ
IF OBJECT_ID('dbo.OperatingExpenses', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OperatingExpenses (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_OperatingExpenses PRIMARY KEY,
        ExpenseCode varchar(50) NOT NULL CONSTRAINT UQ_OperatingExpenses_Code UNIQUE,
        ExpenseDate date NOT NULL,
        Category varchar(30) NOT NULL
            CONSTRAINT CK_OperatingExpenses_Category CHECK (Category IN ('Maintenance', 'InventoryCOGS', 'Utilities', 'Payroll')),
        Title nvarchar(150) NOT NULL,
        Amount decimal(18,2) NOT NULL
            CONSTRAINT CK_OperatingExpenses_Amount CHECK (Amount > 0),
        TableId int NULL,
        ShiftPeriod varchar(20) NOT NULL CONSTRAINT DF_OperatingExpenses_Shift DEFAULT 'All'
            CONSTRAINT CK_OperatingExpenses_Shift CHECK (ShiftPeriod IN ('Day', 'Night', 'All')),
        Notes nvarchar(500) NULL,
        CreatedById nvarchar(450) NULL,
        CreatedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_OperatingExpenses_Created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_OperatingExpenses_Table FOREIGN KEY (TableId) REFERENCES dbo.BilliardTables(Id) ON DELETE NO ACTION,
        CONSTRAINT FK_OperatingExpenses_Actor FOREIGN KEY (CreatedById) REFERENCES dbo.AspNetUsers(Id) ON DELETE NO ACTION
    );
    CREATE INDEX IX_OperatingExpenses_Date_Category ON dbo.OperatingExpenses(ExpenseDate, Category) INCLUDE (Amount, ShiftPeriod);
END;
GO

BEGIN TRANSACTION;

-- 2. ĐẢM BẢO CÓ ĐỦ SẢN PHẨM ĐỒ UỐNG / BIA CHỦ LỰC ĐỂ PHÂN TÍCH MÙA VỤ (T4-T5-T6)
DECLARE @DrinkCatId int = (SELECT TOP 1 Id FROM dbo.ProductCategories WHERE Name = N'Đồ uống' ORDER BY Id);
IF @DrinkCatId IS NULL
BEGIN
    INSERT INTO dbo.ProductCategories(Name) VALUES (N'Đồ uống');
    SET @DrinkCatId = SCOPE_IDENTITY();
END;

IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Name = N'Bò Húc (RedBull)')
    INSERT INTO dbo.Products(CategoryId, Name, Price, StockQuantity, IsActive) VALUES (@DrinkCatId, N'Bò Húc (RedBull)', 25000, 180, 1);
IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Name = N'Bia Tiger Crystal')
    INSERT INTO dbo.Products(CategoryId, Name, Price, StockQuantity, IsActive) VALUES (@DrinkCatId, N'Bia Tiger Crystal', 32000, 240, 1);
IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Name = N'Sting Dâu Tăng Lực')
    INSERT INTO dbo.Products(CategoryId, Name, Price, StockQuantity, IsActive) VALUES (@DrinkCatId, N'Sting Dâu Tăng Lực', 20000, 150, 1);

DECLARE @ProdRedBull int = (SELECT TOP 1 Id FROM dbo.Products WHERE Name = N'Bò Húc (RedBull)');
DECLARE @ProdTiger   int = (SELECT TOP 1 Id FROM dbo.Products WHERE Name = N'Bia Tiger Crystal');
DECLARE @ProdSting   int = (SELECT TOP 1 Id FROM dbo.Products WHERE Name = N'Sting Dâu Tăng Lực');
DECLARE @ProdPeach   int = ISNULL((SELECT TOP 1 Id FROM dbo.Products WHERE Name = N'Trà đào'), @ProdRedBull);
DECLARE @ProdWater   int = ISNULL((SELECT TOP 1 Id FROM dbo.Products WHERE Name = N'Nước suối'), @ProdSting);

-- Lấy danh sách bàn & nhân viên có sẵn
DECLARE @Table1 int = (SELECT TOP 1 Id FROM dbo.BilliardTables ORDER BY Id);
DECLARE @Table2 int = ISNULL((SELECT TOP 1 Id FROM dbo.BilliardTables WHERE Id > @Table1 ORDER BY Id), @Table1);
DECLARE @Table3 int = ISNULL((SELECT TOP 1 Id FROM dbo.BilliardTables WHERE Id > @Table2 ORDER BY Id), @Table1);

DECLARE @AdminId nvarchar(450) = (SELECT TOP 1 u.Id FROM dbo.AspNetUsers u JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId WHERE r.NormalizedName = 'ADMIN' ORDER BY u.Id);
DECLARE @Staff1  nvarchar(450) = ISNULL((SELECT TOP 1 u.Id FROM dbo.AspNetUsers u JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId WHERE r.NormalizedName = 'STAFF' AND u.IsActive = 1 ORDER BY u.Id), @AdminId);
DECLARE @Staff2  nvarchar(450) = ISNULL((SELECT TOP 1 u.Id FROM dbo.AspNetUsers u JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId WHERE r.NormalizedName = 'STAFF' AND u.Id <> @Staff1 ORDER BY u.Id), @Staff1);
DECLARE @Cust1   nvarchar(450) = (SELECT TOP 1 u.Id FROM dbo.AspNetUsers u JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId WHERE r.NormalizedName = 'CUSTOMER' ORDER BY u.Id);

-- 3. BẢNG CẤU HÌNH KỊCH BẢN TÀI CHÍNH 12 THÁNG (2026-01 -> 2026-12)
-- Phản ánh đúng:
--   + T3, T4, T5: Doanh thu giờ chơi cao nhất năm
--   + T4, T5, T6: Doanh thu bán nước/bia cao nhất năm
--   + Khung giờ Đêm (NightPlay / NightFnb) luôn cao gấp ~3 lần Ban ngày (DayPlay / DayFnb)
--   + Chi phí Bảo trì (MaintCost), Giá vốn nước (CogsCost), Điện nước (UtilCost), Lương (DayPayroll + NightPayroll)
--   + Các tháng Lỗ: T2 (Nghỉ Tết), T8 (Đại tu bàn/gậy), T9 (Mùa mưa + Thất thoát), T10 (Thay bi/đèn + Dư nhân sự ca ngày)
DECLARE @MonthlyPlan TABLE (
    MonthNo int PRIMARY KEY,
    DayPlay decimal(18,2),
    NightPlay decimal(18,2),
    DayFnb decimal(18,2),
    NightFnb decimal(18,2),
    MaintCost decimal(18,2),
    CogsCost decimal(18,2),
    UtilCost decimal(18,2),
    DayPayroll decimal(18,2),
    NightPayroll decimal(18,2),
    MaintTitle nvarchar(150)
);

INSERT INTO @MonthlyPlan VALUES
-- Tháng | Giờ Ngày | Giờ Đêm  | Nước Ngày| Nước Đêm | Bảo Trì  | Giá Vốn F&B| Điện Nước | Lương Ngày| Lương Đêm | Nội dung bảo trì
(1,  12500000, 36000000,  4200000, 15000000,  4500000,  8500000,  9000000, 16000000, 16000000, N'Thay lơ Mỹ, đầu cơ Kamui & cân chỉnh bàn đầu năm'),
(2,   8000000, 23000000,  2800000,  8700000,  3800000,  5500000,  7500000, 18000000, 18000000, N'Bảo dưỡng định kỳ trước Tết & vệ sinh toàn bộ nỉ bàn'),
(3,  17400000, 51000000,  5600000, 19000000,  4200000, 11000000,  9500000, 16000000, 15500000, N'Thay 10 đầu cơ & đánh bóng bộ bi Aramith giải CLB'),
(4,  18200000, 53000000,  9300000, 27500000,  5000000, 15800000, 12500000, 16000000, 15200000, N'Bảo trì hệ thống lạnh & thay lơ/đầu cơ cao điểm T4'),
(5,  17500000, 51500000, 10000000, 29500000,  5500000, 17000000, 13500000, 16000000, 15000000, N'Căng lại vải nỉ 2 bàn Pool & bổ sung gậy phá nhảy'),
(6,  13500000, 40500000,  9200000, 28000000,  6000000, 16500000, 14500000, 16000000, 15500000, N'Bơm gas 4 máy lạnh công suất lớn & thay 8 đầu cơ'),
(7,  11200000, 35000000,  5300000, 17500000,  5200000, 10200000, 14000000, 16000000, 16100000, N'Sửa 4 gậy bida cong & thay bóng cái bàn Carom'),
(8,   9500000, 30000000,  4000000, 13000000, 22500000,  7800000, 12000000, 16000000, 15900000, N'ĐẠI TU: Thay vải nỉ Andy 6 bàn + mua 12 cơ mới + sửa điều hòa'),
(9,   9000000, 29000000,  3400000, 12000000,  6800000,  7200000,  9800000, 16000000, 15200000, N'Thay băng cao su bàn B04 & xử lý chống ẩm gậy mùa mưa'),
(10, 10500000, 32000000,  4200000, 13800000, 11800000,  8000000,  9000000, 16000000, 15000000, N'Thay 3 bộ bi Aramith Pro & nâng cấp đèn LED bàn VIP'),
(11, 13800000, 40000000,  5500000, 18000000,  4500000, 10500000,  9500000, 16000000, 16000000, N'Bảo dưỡng định kỳ gậy/đầu cơ & vệ sinh nỉ bàn T11'),
(12, 16000000, 45000000,  6900000, 21500000,  4800000, 12200000,  9800000, 16000000, 15200000, N'Bảo dưỡng tổng thể bàn & phụ kiện phục vụ lễ cuối năm');

DECLARE @m int = 1;
WHILE @m <= 12
BEGIN
    DECLARE @DayPlay decimal(18,2), @NightPlay decimal(18,2), @DayFnb decimal(18,2), @NightFnb decimal(18,2),
            @MaintCost decimal(18,2), @CogsCost decimal(18,2), @UtilCost decimal(18,2),
            @DayPayroll decimal(18,2), @NightPayroll decimal(18,2), @MaintTitle nvarchar(150);

    SELECT @DayPlay = DayPlay, @NightPlay = NightPlay, @DayFnb = DayFnb, @NightFnb = NightFnb,
           @MaintCost = MaintCost, @CogsCost = CogsCost, @UtilCost = UtilCost,
           @DayPayroll = DayPayroll, @NightPayroll = NightPayroll, @MaintTitle = MaintTitle
    FROM @MonthlyPlan WHERE MonthNo = @m;

    DECLARE @ExpDate date = DATEFROMPARTS(2026, @m, 15);
    DECLARE @CodeMaint varchar(50) = CONCAT('FIN2026-M', FORMAT(@m, '00'), '-MAINT');
    DECLARE @CodeCogs  varchar(50) = CONCAT('FIN2026-M', FORMAT(@m, '00'), '-COGS');
    DECLARE @CodeUtil  varchar(50) = CONCAT('FIN2026-M', FORMAT(@m, '00'), '-UTIL');
    DECLARE @CodePayD  varchar(50) = CONCAT('FIN2026-M', FORMAT(@m, '00'), '-PAY-DAY');
    DECLARE @CodePayN  varchar(50) = CONCAT('FIN2026-M', FORMAT(@m, '00'), '-PAY-NIGHT');

    -- A. GHI NHẬN 5 BÚT TOÁN CHI PHÍ VẬN HÀNH (OPEX & COGS) CHO THÁNG @m
    IF NOT EXISTS (SELECT 1 FROM dbo.OperatingExpenses WHERE ExpenseCode = @CodeMaint)
        INSERT INTO dbo.OperatingExpenses (ExpenseCode, ExpenseDate, Category, Title, Amount, TableId, ShiftPeriod, Notes, CreatedById)
        VALUES (@CodeMaint, @ExpDate, 'Maintenance', @MaintTitle, @MaintCost, @Table1, 'All', N'Chi phí bảo trì bàn, vải nỉ, gậy, đầu cơ, bi bida', @AdminId);

    IF NOT EXISTS (SELECT 1 FROM dbo.OperatingExpenses WHERE ExpenseCode = @CodeCogs)
        INSERT INTO dbo.OperatingExpenses (ExpenseCode, ExpenseDate, Category, Title, Amount, TableId, ShiftPeriod, Notes, CreatedById)
        VALUES (@CodeCogs, @ExpDate, 'InventoryCOGS', CONCAT(N'Nhập kho Bia Tiger, Bò Húc, Trà Đào, Nước ngọt tháng ', @m, N'/2026'), @CogsCost, NULL, 'All', N'Giá vốn hàng bán F&B (nhập thùng đại lý + đá + hao hụt)', @AdminId);

    IF NOT EXISTS (SELECT 1 FROM dbo.OperatingExpenses WHERE ExpenseCode = @CodeUtil)
        INSERT INTO dbo.OperatingExpenses (ExpenseCode, ExpenseDate, Category, Title, Amount, TableId, ShiftPeriod, Notes, CreatedById)
        VALUES (@CodeUtil, @ExpDate, 'Utilities', CONCAT(N'Hóa đơn Điện điều hòa, Đèn bàn, Nước & Internet tháng ', @m, N'/2026'), @UtilCost, NULL, 'All', N'Chi phí điện 3 pha máy lạnh + chiếu sáng bàn bida + nước sinh hoạt', @AdminId);

    IF NOT EXISTS (SELECT 1 FROM dbo.OperatingExpenses WHERE ExpenseCode = @CodePayD)
        INSERT INTO dbo.OperatingExpenses (ExpenseCode, ExpenseDate, Category, Title, Amount, TableId, ShiftPeriod, Notes, CreatedById)
        VALUES (@CodePayD, @ExpDate, 'Payroll', CONCAT(N'Quỹ lương nhân viên Ca Ban Ngày (08:00–18:00) tháng ', @m, N'/2026'), @DayPayroll, NULL, 'Day', N'Lương cứng + phụ cấp nhân sự trực khung giờ sáng & chiều', @AdminId);

    IF NOT EXISTS (SELECT 1 FROM dbo.OperatingExpenses WHERE ExpenseCode = @CodePayN)
        INSERT INTO dbo.OperatingExpenses (ExpenseCode, ExpenseDate, Category, Title, Amount, TableId, ShiftPeriod, Notes, CreatedById)
        VALUES (@CodePayN, @ExpDate, 'Payroll', CONCAT(N'Quỹ lương nhân viên Ca Đêm (18:00–02:00) tháng ', @m, N'/2026'), @NightPayroll, NULL, 'Night', N'Lương cứng + phụ cấp trực đêm cho thu ngân & phục vụ bàn', @AdminId);

    -- B. TẠO CA LÀM VIỆC, PHIÊN CHƠI, ĐƠN ĐỒ UỐNG, HÓA ĐƠN & THANH TOÁN ĐẠI DIỆN CHO THÁNG @m
    -- Chia mỗi tháng thành 2 cụm phiên chuẩn: Cụm Ca Ngày (10:00–14:00 VN = 03:00–07:00 UTC) và Cụm Ca Đêm (20:00–23:50 VN = 13:00–16:50 UTC)
    DECLARE @RefDay   nvarchar(100) = CONCAT(N'FIN2026-M', FORMAT(@m, '00'), N'-DAY');
    DECLARE @RefNight nvarchar(100) = CONCAT(N'FIN2026-M', FORMAT(@m, '00'), N'-NIGHT');

    IF NOT EXISTS (SELECT 1 FROM dbo.PaymentTransactions WHERE GatewayRefCode = @RefDay)
    BEGIN
        DECLARE @DayStartUtc datetime2(0) = DATETIME2FROMPARTS(2026, @m, 10, 3, 0, 0, 0, 0); -- 10:00 VN
        DECLARE @DayEndUtc   datetime2(0) = DATETIME2FROMPARTS(2026, @m, 10, 7, 0, 0, 0, 0); -- 14:00 VN

        INSERT INTO dbo.WorkShifts (StaffId, StartTimeUtc, EndTimeUtc, StartingCash, EndingCash, CashCollected, ExpectedCash, ClosedById, Status, Notes)
        VALUES (@Staff1, @DayStartUtc, @DayEndUtc, 2000000, 2000000 + (@DayPlay + @DayFnb) * 0.5, (@DayPlay + @DayFnb) * 0.5, 2000000 + (@DayPlay + @DayFnb) * 0.5, @Staff1, 'Closed', CONCAT(N'Ca ban ngày tháng ', @m, N'/2026 khớp quỹ'));
        DECLARE @ShiftDayId int = CONVERT(int, SCOPE_IDENTITY());

        INSERT INTO dbo.PlaySessions (TableId, CustomerId, OpenedById, ClosedById, StartAtUtc, EndAtUtc, HourlyRateSnapshot, PlaytimeAmount, Status)
        VALUES (@Table1, @Cust1, @Staff1, @Staff1, @DayStartUtc, @DayEndUtc, 100000, @DayPlay, 'Closed');
        DECLARE @SessDayId int = CONVERT(int, SCOPE_IDENTITY());

        INSERT INTO dbo.Orders (SessionId, OrderTimeUtc, Status)
        VALUES (@SessDayId, DATEADD(minute, 30, @DayStartUtc), 'Served');
        DECLARE @OrdDayId int = CONVERT(int, SCOPE_IDENTITY());

        -- Phân bổ đồ uống Ca Ngày (Trà đào + Nước suối + Bò húc)
        DECLARE @DayRedBullQty int = CONVERT(int, ROUND((@DayFnb * 0.60) / 25000.0, 0));
        DECLARE @DayRemainAmt decimal(18,2) = @DayFnb - (@DayRedBullQty * 25000);
        IF @DayRedBullQty > 0
            INSERT INTO dbo.OrderDetails (OrderId, ProductId, Quantity, UnitPrice) VALUES (@OrdDayId, @ProdRedBull, @DayRedBullQty, 25000);
        IF @DayRemainAmt > 0
            INSERT INTO dbo.OrderDetails (OrderId, ProductId, Quantity, UnitPrice) VALUES (@OrdDayId, @ProdPeach, 1, @DayRemainAmt);

        INSERT INTO dbo.Invoices (SessionId, CustomerId, TotalPlaytime, TotalFandB, DiscountAmount, Status, CreatedAtUtc)
        VALUES (@SessDayId, @Cust1, @DayPlay, @DayFnb, 0, 'Paid', @DayEndUtc);
        DECLARE @InvDayId int = CONVERT(int, SCOPE_IDENTITY());

        INSERT INTO dbo.PaymentTransactions (InvoiceId, PaymentMethod, AmountPaid, GatewayRefCode, TransactionTimeUtc, WorkShiftId, ReceivedById)
        VALUES
            (@InvDayId, 'Cash',  (@DayPlay + @DayFnb) * 0.5, @RefDay, DATEADD(minute, 2, @DayEndUtc), @ShiftDayId, @Staff1),
            (@InvDayId, 'VNPay', (@DayPlay + @DayFnb) * 0.5, CONCAT(@RefDay, N'-VNP'), DATEADD(minute, 3, @DayEndUtc), NULL, NULL);
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.PaymentTransactions WHERE GatewayRefCode = @RefNight)
    BEGIN
        DECLARE @NightStartUtc datetime2(0) = DATETIME2FROMPARTS(2026, @m, 18, 13, 0, 0, 0, 0); -- 20:00 VN
        DECLARE @NightEndUtc   datetime2(0) = DATETIME2FROMPARTS(2026, @m, 18, 16, 50, 0, 0, 0); -- 23:50 VN

        INSERT INTO dbo.WorkShifts (StaffId, StartTimeUtc, EndTimeUtc, StartingCash, EndingCash, CashCollected, ExpectedCash, ClosedById, Status, Notes)
        VALUES (@Staff2, @NightStartUtc, @NightEndUtc, 3000000, 3000000 + (@NightPlay + @NightFnb) * 0.6, (@NightPlay + @NightFnb) * 0.6, 3000000 + (@NightPlay + @NightFnb) * 0.6, @Staff2, 'Closed', CONCAT(N'Ca đêm cao điểm tháng ', @m, N'/2026'));
        DECLARE @ShiftNightId int = CONVERT(int, SCOPE_IDENTITY());

        INSERT INTO dbo.PlaySessions (TableId, CustomerId, OpenedById, ClosedById, StartAtUtc, EndAtUtc, HourlyRateSnapshot, PlaytimeAmount, Status)
        VALUES (@Table2, @Cust1, @Staff2, @Staff2, @NightStartUtc, @NightEndUtc, 120000, @NightPlay, 'Closed');
        DECLARE @SessNightId int = CONVERT(int, SCOPE_IDENTITY());

        INSERT INTO dbo.Orders (SessionId, OrderTimeUtc, Status)
        VALUES (@SessNightId, DATEADD(minute, 45, @NightStartUtc), 'Served');
        DECLARE @OrdNightId int = CONVERT(int, SCOPE_IDENTITY());

        -- Phân bổ đồ uống Ca Đêm (Tập trung mạnh vào Bia Tiger & Bò Húc, đặc biệt bùng nổ vào T4, T5, T6)
        DECLARE @NightTigerQty   int = CONVERT(int, ROUND((@NightFnb * 0.50) / 32000.0, 0));
        DECLARE @NightRedBullQty int = CONVERT(int, ROUND((@NightFnb * 0.35) / 25000.0, 0));
        DECLARE @NightRemainAmt  decimal(18,2) = @NightFnb - (@NightTigerQty * 32000) - (@NightRedBullQty * 25000);

        IF @NightTigerQty > 0
            INSERT INTO dbo.OrderDetails (OrderId, ProductId, Quantity, UnitPrice) VALUES (@OrdNightId, @ProdTiger, @NightTigerQty, 32000);
        IF @NightRedBullQty > 0
            INSERT INTO dbo.OrderDetails (OrderId, ProductId, Quantity, UnitPrice) VALUES (@OrdNightId, @ProdRedBull, @NightRedBullQty, 25000);
        IF @NightRemainAmt > 0
            INSERT INTO dbo.OrderDetails (OrderId, ProductId, Quantity, UnitPrice) VALUES (@OrdNightId, @ProdSting, 1, @NightRemainAmt);

        INSERT INTO dbo.Invoices (SessionId, CustomerId, TotalPlaytime, TotalFandB, DiscountAmount, Status, CreatedAtUtc)
        VALUES (@SessNightId, @Cust1, @NightPlay, @NightFnb, 0, 'Paid', @NightEndUtc);
        DECLARE @InvNightId int = CONVERT(int, SCOPE_IDENTITY());

        INSERT INTO dbo.PaymentTransactions (InvoiceId, PaymentMethod, AmountPaid, GatewayRefCode, TransactionTimeUtc, WorkShiftId, ReceivedById)
        VALUES
            (@InvNightId, 'Cash',     (@NightPlay + @NightFnb) * 0.6, @RefNight, DATEADD(minute, 2, @NightEndUtc), @ShiftNightId, @Staff2),
            (@InvNightId, 'Transfer', (@NightPlay + @NightFnb) * 0.4, CONCAT(@RefNight, N'-TRF'), DATEADD(minute, 4, @NightEndUtc), NULL, @Staff2);
    END;

    SET @m += 1;
END;

-- ============================================================================
-- 4. CÀI CẮM 5 KỊCH BẢN SAI LỆCH DỮ LIỆU / THẤT THOÁT DOANH THU (LEAKAGE CASES)
-- Nhằm kiểm chứng khả năng "Truy tìm thất thoát ở đâu, bàn nào, ca nào, do ai"
-- ============================================================================

-- [LEAK-01] Phiên chơi đã đóng (Closed) nhưng KHÔNG LẬP HÓA ĐƠN (Thu tiền bỏ túi ngoài sổ) — Tháng 8/2026
IF NOT EXISTS (SELECT 1 FROM dbo.PlaySessions WHERE StartAtUtc = '2026-08-22T14:00:00' AND HourlyRateSnapshot = 120000 AND PlaytimeAmount = 1440000)
BEGIN
    INSERT INTO dbo.PlaySessions (TableId, CustomerId, OpenedById, ClosedById, StartAtUtc, EndAtUtc, HourlyRateSnapshot, PlaytimeAmount, Status)
    VALUES (@Table3, NULL, @Staff2, @Staff2, '2026-08-22T14:00:00', '2026-08-22T17:00:00', 120000, 1440000, 'Closed');
END;

-- [LEAK-02] Hóa đơn lập nhưng treo trạng thái UNPAID quá hạn (Khách đã rời quán / nhân viên giữ tiền mặt không bấm thu) — Tháng 9/2026
IF NOT EXISTS (SELECT 1 FROM dbo.PlaySessions WHERE StartAtUtc = '2026-09-14T15:00:00' AND PlaytimeAmount = 1800000)
BEGIN
    INSERT INTO dbo.PlaySessions (TableId, CustomerId, OpenedById, ClosedById, StartAtUtc, EndAtUtc, HourlyRateSnapshot, PlaytimeAmount, Status)
    VALUES (@Table1, NULL, @Staff1, @Staff1, '2026-09-14T15:00:00', '2026-09-14T18:00:00', 100000, 1800000, 'Closed');
    DECLARE @LeakSess2 int = CONVERT(int, SCOPE_IDENTITY());

    INSERT INTO dbo.Invoices (SessionId, CustomerId, TotalPlaytime, TotalFandB, DiscountAmount, Status, CreatedAtUtc)
    VALUES (@LeakSess2, NULL, 1800000, 450000, 0, 'Unpaid', '2026-09-14T18:00:00');
END;

-- [LEAK-03] Gian lận sửa giảm tiền giờ & xóa đồ uống khi lên Hóa đơn (PlaytimeAmount=2.4tr, Order=960k nhưng Invoice chỉ tính 1.2tr tiền giờ & 320k nước) — Tháng 9/2026
IF NOT EXISTS (SELECT 1 FROM dbo.PaymentTransactions WHERE GatewayRefCode = N'LEAK2026-M09-UNDERBILL')
BEGIN
    INSERT INTO dbo.PlaySessions (TableId, CustomerId, OpenedById, ClosedById, StartAtUtc, EndAtUtc, HourlyRateSnapshot, PlaytimeAmount, Status)
    VALUES (@Table2, @Cust1, @Staff2, @Staff2, '2026-09-25T13:00:00', '2026-09-25T16:30:00', 120000, 2400000, 'Closed');
    DECLARE @LeakSess3 int = CONVERT(int, SCOPE_IDENTITY());

    INSERT INTO dbo.Orders (SessionId, OrderTimeUtc, Status)
    VALUES (@LeakSess3, '2026-09-25T14:00:00', 'Served');
    DECLARE @LeakOrd3 int = CONVERT(int, SCOPE_IDENTITY());

    INSERT INTO dbo.OrderDetails (OrderId, ProductId, Quantity, UnitPrice)
    VALUES (@LeakOrd3, @ProdTiger, 30, 32000); -- 960.000đ bia đã Served

    -- Hóa đơn bị sửa thấp hơn thực tế (mất 1.200.000đ tiền giờ + 640.000đ tiền bia = 1.840.000đ)
    INSERT INTO dbo.Invoices (SessionId, CustomerId, TotalPlaytime, TotalFandB, DiscountAmount, Status, CreatedAtUtc)
    VALUES (@LeakSess3, @Cust1, 1200000, 320000, 0, 'Paid', '2026-09-25T16:35:00');
    DECLARE @LeakInv3 int = CONVERT(int, SCOPE_IDENTITY());

    INSERT INTO dbo.PaymentTransactions (InvoiceId, PaymentMethod, AmountPaid, GatewayRefCode, TransactionTimeUtc, WorkShiftId, ReceivedById)
    VALUES (@LeakInv3, 'Cash', 1520000, N'LEAK2026-M09-UNDERBILL', '2026-09-25T16:36:00', NULL, @Staff2);
END;

-- [LEAK-04] Thụt két tiền mặt khi kết ca (ExpectedCash = 12.500.000đ nhưng EndingCash chỉ có 9.800.000đ -> Thiếu 2.700.000đ) — Tháng 2 & Tháng 9/2026
IF NOT EXISTS (SELECT 1 FROM dbo.WorkShifts WHERE StartTimeUtc = '2026-02-19T14:00:00' AND StaffId = @Staff2)
BEGIN
    INSERT INTO dbo.WorkShifts (StaffId, StartTimeUtc, EndTimeUtc, StartingCash, EndingCash, CashCollected, ExpectedCash, ClosedById, Status, Notes)
    VALUES (@Staff2, '2026-02-19T14:00:00', '2026-02-19T18:00:00', 2000000, 7400000, 8200000, 10200000, @Staff2, 'Closed', N'Đóng ca tối mùng 5 Tết: thiếu 2.800.000đ tiền mặt chưa rõ nguyên nhân');
END;

IF NOT EXISTS (SELECT 1 FROM dbo.WorkShifts WHERE StartTimeUtc = '2026-09-28T14:00:00' AND StaffId = @Staff1)
BEGIN
    INSERT INTO dbo.WorkShifts (StaffId, StartTimeUtc, EndTimeUtc, StartingCash, EndingCash, CashCollected, ExpectedCash, ClosedById, Status, Notes)
    VALUES (@Staff1, '2026-09-28T14:00:00', '2026-09-28T18:00:00', 2500000, 9800000, 10000000, 12500000, @Staff1, 'Closed', N'Kết ca đêm 28/09: lệch thiếu 2.700.000đ so với doanh thu máy');
END;

-- [LEAK-05] Áp dụng giảm giá khống (DiscountAmount = 950.000đ cho hóa đơn khách vãng lai không có thẻ thành viên / voucher) — Tháng 5/2026
IF NOT EXISTS (SELECT 1 FROM dbo.PaymentTransactions WHERE GatewayRefCode = N'LEAK2026-M05-FAKEDISC')
BEGIN
    INSERT INTO dbo.PlaySessions (TableId, CustomerId, OpenedById, ClosedById, StartAtUtc, EndAtUtc, HourlyRateSnapshot, PlaytimeAmount, Status)
    VALUES (@Table3, NULL, @Staff1, @Staff1, '2026-05-20T14:00:00', '2026-05-20T17:00:00', 120000, 1600000, 'Closed');
    DECLARE @LeakSess5 int = CONVERT(int, SCOPE_IDENTITY());

    INSERT INTO dbo.Invoices (SessionId, CustomerId, TotalPlaytime, TotalFandB, DiscountAmount, Status, CreatedAtUtc)
    VALUES (@LeakSess5, NULL, 1600000, 500000, 950000, 'Paid', '2026-05-20T17:05:00');
    DECLARE @LeakInv5 int = CONVERT(int, SCOPE_IDENTITY());

    INSERT INTO dbo.PaymentTransactions (InvoiceId, PaymentMethod, AmountPaid, GatewayRefCode, TransactionTimeUtc, WorkShiftId, ReceivedById)
    VALUES (@LeakInv5, 'Cash', 1150000, N'LEAK2026-M05-FAKEDISC', '2026-05-20T17:06:00', NULL, @Staff1);
END;

COMMIT TRANSACTION;
GO
PRINT N'11_Financial_12Months_Seed.sql applied successfully.';
GO
