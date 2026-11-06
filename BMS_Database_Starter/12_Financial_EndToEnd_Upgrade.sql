/* ============================================================================
   BMS_Database_Starter / 12_Financial_EndToEnd_Upgrade.sql
   Nâng cấp Luồng Quản Trị Tài Chính & Kiểm Toán Nội Bộ End-to-End (7 Screens):
   1. Bổ sung cột Phê duyệt & Nhà cung cấp cho dbo.OperatingExpenses (Status, VendorName, ReviewedById, ReviewedAtUtc, ReviewNote, UpdatedAtUtc).
   2. Tạo bảng dbo.FinancialAuditCases (Quản lý vòng đời điều tra & xử lý thất thoát: Open -> Investigating -> Resolved).
   3. Tạo bảng dbo.MonthlyBudgets (Quản lý định mức ngân sách 4 luồng chi phí & kế hoạch hành động mùa vụ 12 tháng).
   4. Seed dữ liệu 12 tháng định mức ngân sách & kế hoạch tối ưu mùa vụ (2026-01 .. 2026-12).
   ============================================================================ */
SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- 1. Nâng cấp bảng dbo.OperatingExpenses
IF COL_LENGTH('dbo.OperatingExpenses', 'VendorName') IS NULL
    ALTER TABLE dbo.OperatingExpenses ADD VendorName nvarchar(150) NULL;

IF COL_LENGTH('dbo.OperatingExpenses', 'Status') IS NULL
    ALTER TABLE dbo.OperatingExpenses ADD Status varchar(20) NOT NULL
        CONSTRAINT DF_OperatingExpenses_Status DEFAULT 'Approved'
        CONSTRAINT CK_OperatingExpenses_Status CHECK (Status IN ('Pending', 'Approved', 'Rejected'));

IF COL_LENGTH('dbo.OperatingExpenses', 'ReviewedById') IS NULL
    ALTER TABLE dbo.OperatingExpenses ADD ReviewedById nvarchar(450) NULL;

IF COL_LENGTH('dbo.OperatingExpenses', 'ReviewedAtUtc') IS NULL
    ALTER TABLE dbo.OperatingExpenses ADD ReviewedAtUtc datetime2(0) NULL;

IF COL_LENGTH('dbo.OperatingExpenses', 'ReviewNote') IS NULL
    ALTER TABLE dbo.OperatingExpenses ADD ReviewNote nvarchar(500) NULL;

IF COL_LENGTH('dbo.OperatingExpenses', 'UpdatedAtUtc') IS NULL
    ALTER TABLE dbo.OperatingExpenses ADD UpdatedAtUtc datetime2(0) NULL;
GO

-- 2. Tạo bảng dbo.FinancialAuditCases (Hồ sơ điều tra & xử lý thất thoát End-to-End)
IF OBJECT_ID('dbo.FinancialAuditCases', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.FinancialAuditCases (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_FinancialAuditCases PRIMARY KEY,
        CaseCode varchar(64) NOT NULL CONSTRAINT UQ_FinancialAuditCases_Code UNIQUE,
        LeakageCode varchar(20) NOT NULL,
        RuleName nvarchar(150) NOT NULL,
        Severity varchar(20) NOT NULL
            CONSTRAINT CK_FinancialAuditCases_Severity CHECK (Severity IN ('Critical', 'High', 'Warning')),
        OccurredAtUtc datetime2(0) NOT NULL,
        TableOrShiftLabel nvarchar(150) NOT NULL,
        StaffName nvarchar(120) NOT NULL,
        ExpectedAmount decimal(18,2) NOT NULL,
        ActualAmount decimal(18,2) NOT NULL,
        LeakageAmount decimal(18,2) NOT NULL,
        RecoveredAmount decimal(18,2) NOT NULL CONSTRAINT DF_FinancialAuditCases_Recovered DEFAULT 0,
        Status varchar(24) NOT NULL CONSTRAINT DF_FinancialAuditCases_Status DEFAULT 'Open'
            CONSTRAINT CK_FinancialAuditCases_Status CHECK (Status IN ('Open', 'Investigating', 'Resolved')),
        ResolutionType varchar(32) NULL
            CONSTRAINT CK_FinancialAuditCases_ResType CHECK (ResolutionType IS NULL OR ResolutionType IN ('RecoveredCash', 'DeductedPayroll', 'Waived')),
        EvidenceDetail nvarchar(1000) NOT NULL,
        RecommendedAction nvarchar(600) NOT NULL,
        ProblemSolved nvarchar(600) NOT NULL,
        InvestigationNote nvarchar(1000) NULL,
        ResolvedById nvarchar(450) NULL,
        ResolvedAtUtc datetime2(0) NULL,
        CreatedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_FinancialAuditCases_Created DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_FinancialAuditCases_Status_Leakage ON dbo.FinancialAuditCases(Status, LeakageCode) INCLUDE (LeakageAmount, RecoveredAmount);
END;
GO

-- 3. Tạo bảng dbo.MonthlyBudgets (Định mức ngân sách 4 luồng chi phí & Kế hoạch mùa vụ 12 tháng)
IF OBJECT_ID('dbo.MonthlyBudgets', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MonthlyBudgets (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_MonthlyBudgets PRIMARY KEY,
        PeriodMonth varchar(7) NOT NULL CONSTRAINT UQ_MonthlyBudgets_Period UNIQUE,
        RevenueTarget decimal(18,2) NOT NULL,
        MaintenanceCap decimal(18,2) NOT NULL,
        InventoryCogsCap decimal(18,2) NOT NULL,
        UtilitiesCap decimal(18,2) NOT NULL,
        PayrollCap decimal(18,2) NOT NULL,
        StrategicFocus nvarchar(150) NOT NULL,
        ActionPlanSummary nvarchar(1000) NOT NULL,
        Status varchar(20) NOT NULL CONSTRAINT DF_MonthlyBudgets_Status DEFAULT 'Active'
            CONSTRAINT CK_MonthlyBudgets_Status CHECK (Status IN ('Draft', 'Active', 'Locked')),
        UpdatedById nvarchar(450) NULL,
        UpdatedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_MonthlyBudgets_Updated DEFAULT SYSUTCDATETIME()
    );
END;
GO

-- 4. Seed Idempotent 12 tháng Định mức Ngân sách & Kế hoạch Tối ưu Mùa vụ (2026-01 .. 2026-12)
MERGE dbo.MonthlyBudgets AS target
USING (VALUES
    ('2026-01', 38000000, 3500000, 9000000,  5500000, 15000000, N'Ổn định đầu năm & Tất niên', N'Duy trì 2 nhân sự ca đêm, kiểm tra định kỳ mặt bàn trước Tết, giữ tỷ lệ giá vốn nước dưới 25%.', 'Locked'),
    ('2026-02', 28000000, 9000000, 6500000,  5000000, 15500000, N'Bảo trì đại tu mặt bàn dịp Tết', N'Chấp nhận chi phí thay vải nỉ & cân chỉnh băng bàn định kỳ đầu năm; cắt giảm ca ngày trong tuần nghỉ Tết.', 'Locked'),
    ('2026-03', 48000000, 3500000, 11500000, 6200000, 16000000, N'Cao điểm Giờ chơi Mùa Xuân (T3-T5)', N'Tăng tốc lấp đầy bàn khung 18h-01h; áp dụng gói luyện cơ chiều; điều chuyển 1 nhân sự ca ngày sang ca đêm.', 'Active'),
    ('2026-04', 52000000, 3800000, 14500000, 7200000, 16500000, N'Cao điểm Kép: Giờ chơi & Bia/Nước (T4-T6)', N'Tăng trần ngân sách nhập Bia Heineken/Tiger, Bò húc, Trà đào thêm 30%; tung Combo 2 Giờ Bàn + Tháp Bia.', 'Active'),
    ('2026-05', 55000000, 4000000, 15500000, 7800000, 16500000, N'Đỉnh điểm Doanh thu Giờ chơi & Giải khát', N'Tối ưu hóa biên lợi nhuận F&B; kiểm soát chặt tồn kho bia tránh order lậu không lên hóa đơn (LEAK-02).', 'Active'),
    ('2026-06', 47000000, 3800000, 14000000, 8000000, 16000000, N'Cao điểm Nước giải khát Mùa Hè (T4-T6)', N'Đẩy mạnh bán nước đóng lon & trà nhiệt đới; kiểm soát tiền điện điều hòa 3 pha ban ngày khi vắng khách.', 'Active'),
    ('2026-07', 40000000, 3500000, 10000000, 7200000, 15000000, N'Giữ nhịp Mùa Hè & Cắt giảm Ca Ngày', N'Cắt giảm 1 nhân sự phục vụ ca ngày (08:00-16:00) do tỷ trọng doanh thu ca ngày chỉ đạt ~26%; tiết kiệm 4.5tr/tháng.', 'Active'),
    ('2026-08', 32000000, 7500000, 8000000,  7500000, 15000000, N'Kiểm soát Mùa Thấp Điểm Mưa Ngâu (Tháng Lỗ)', N'Giới hạn chi phí bảo trì phát sinh; tắt bớt cụm máy lạnh tầng 2 khung giờ 08:00-15:00; tung ưu đãi giờ vàng.', 'Active'),
    ('2026-09', 31000000, 7000000, 7800000,  6800000, 15000000, N'Tái cấu trúc Chi phí Tựu trường (Tháng Lỗ)', N'Khóa trần khuyến mãi thủ công <= 25% (chống LEAK-03); rà soát định biên nhân sự ca ngày.', 'Active'),
    ('2026-10', 33000000, 7200000, 8200000,  6500000, 15000000, N'Phục hồi Thu-Đông & Kiểm toán Két tiền mặt', N'Bắt buộc đối soát tiền mặt cuối ca 100% (chống LEAK-05); chuẩn bị giải đấu CLB cuối năm.', 'Active'),
    ('2026-11', 42000000, 3800000, 10500000, 6000000, 15500000, N'Tăng trưởng Giải đấu CLB Cuối năm', N'Đẩy mạnh gói hội viên VIP ca đêm; tăng nhập bia phục vụ giải đấu nội bộ.', 'Active'),
    ('2026-12', 46000000, 4200000, 12000000, 6200000, 16500000, N'Cao điểm Lễ Hội & Tất Niên Cuối Năm', N'Tối đa công suất bàn VIP ca đêm; tổng kết kiểm toán thất thoát cả năm và thưởng KPI thu ngân không lệch két.', 'Draft')
) AS src (PeriodMonth, RevenueTarget, MaintenanceCap, InventoryCogsCap, UtilitiesCap, PayrollCap, StrategicFocus, ActionPlanSummary, Status)
ON target.PeriodMonth = src.PeriodMonth
WHEN NOT MATCHED THEN
    INSERT (PeriodMonth, RevenueTarget, MaintenanceCap, InventoryCogsCap, UtilitiesCap, PayrollCap, StrategicFocus, ActionPlanSummary, Status)
    VALUES (src.PeriodMonth, src.RevenueTarget, src.MaintenanceCap, src.InventoryCogsCap, src.UtilitiesCap, src.PayrollCap, src.StrategicFocus, src.ActionPlanSummary, src.Status);
GO

-- Giữ nguyên 60 bút toán gốc ở trạng thái Approved và thêm 2 phiếu chi Pending mẫu để demo duyệt trực tiếp trên UI
UPDATE dbo.OperatingExpenses
SET Status = 'Approved'
WHERE ExpenseCode LIKE 'EXP-2026-%';

IF NOT EXISTS (SELECT 1 FROM dbo.OperatingExpenses WHERE ExpenseCode = 'EXP-E2E-PEND-01')
    INSERT INTO dbo.OperatingExpenses (ExpenseCode, ExpenseDate, Category, Title, Amount, ShiftPeriod, VendorName, Status, Notes)
    VALUES ('EXP-E2E-PEND-01', '2026-12-28', 'Maintenance', N'Đề xuất thay mới 4 cây cơ Carbon cao cấp bàn VIP', 3200000, 'Night', N'Nhà phân phối Thiết bị Bida Hoàng Gia', 'Pending', N'Phiếu chi chờ CFO phê duyệt trước khi xuất quỹ');

IF NOT EXISTS (SELECT 1 FROM dbo.OperatingExpenses WHERE ExpenseCode = 'EXP-E2E-PEND-02')
    INSERT INTO dbo.OperatingExpenses (ExpenseCode, ExpenseDate, Category, Title, Amount, ShiftPeriod, VendorName, Status, Notes)
    VALUES ('EXP-E2E-PEND-02', '2026-12-29', 'InventoryCOGS', N'Đề xuất nhập bổ sung 15 thùng Bia Heineken Silver phục vụ Tết Dương Lịch', 6450000, 'Night', N'Đại lý Bia & Nước Giải Khát Minh Phát', 'Pending', N'Nhập dự phòng cao điểm lễ cuối năm — Chờ CFO duyệt');

UPDATE dbo.OperatingExpenses
SET VendorName = CASE Category
    WHEN 'Maintenance' THEN N'Xưởng Phụ Kiện Bida ProCue'
    WHEN 'InventoryCOGS' THEN N'Đại lý Bia & Nước Giải Khát Minh Phát'
    WHEN 'Utilities' THEN N'EVN & Cấp Nước Đô Thị'
    WHEN 'Payroll' THEN N'Bảng Lương Nội Bộ CLB BMS'
    ELSE N'Nhà cung cấp nội bộ'
END
WHERE VendorName IS NULL;
GO
