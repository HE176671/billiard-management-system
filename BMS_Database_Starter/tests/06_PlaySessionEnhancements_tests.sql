/* Integration test script for 06_PlaySessionEnhancements.sql.
   Database: BilliardDB. SQL Server 2019+.
   ALL TEST ROWS ARE ROLLED BACK. No existing business data is modified.
   Identity counters may advance, which is normal behavior in SQL Server.

   STRUCTURE:
     - Part 1: Automated Unit & Integration Tests (self-contained batches with clean ROLLBACK).
     - Part 2: Read-only summary table and data integrity verification.
     - Part 3: Commented instructions for MANUAL concurrency testing using two SSMS query windows.
*/
USE [BilliardDB];
GO
SET NOCOUNT ON;
SET XACT_ABORT OFF; -- Allow TRY/CATCH blocks in harness to process expected procedure exceptions.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
GO

-- Table variable to record automated test results
DECLARE @Results TABLE (
    TestId int IDENTITY(1,1) PRIMARY KEY,
    Category varchar(30),
    TestName nvarchar(200),
    Status varchar(10), -- PASS, FAIL, SKIP
    Expected nvarchar(200),
    Actual nvarchar(200),
    Details nvarchar(500)
);

-- Find active staff and customer accounts for testing
DECLARE @TestStaffId nvarchar(450), @TestCustomerId nvarchar(450);
SELECT TOP (1) @TestStaffId = u.Id
  FROM dbo.AspNetUsers u
  JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
  JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
 WHERE u.IsActive = 1 AND r.Name IN (N'Staff', N'Admin')
 ORDER BY u.Id;
IF @TestStaffId IS NULL
    SELECT TOP (1) @TestStaffId = Id FROM dbo.AspNetUsers WHERE Id IN (N'demo-staff-01', N'demo-admin') AND IsActive = 1;

SELECT TOP (1) @TestCustomerId = u.Id
  FROM dbo.AspNetUsers u
  JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
  JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
 WHERE u.IsActive = 1 AND r.Name = N'Customer'
 ORDER BY u.Id;
IF @TestCustomerId IS NULL
    SELECT TOP (1) @TestCustomerId = Id FROM dbo.AspNetUsers WHERE Id IN (N'demo-customer-01', N'demo-customer-02') AND IsActive = 1;

-- Count rows before tests
DECLARE @CountSessionsBefore int = (SELECT COUNT(*) FROM dbo.PlaySessions);
DECLARE @CountSegmentsBefore int = (SELECT COUNT(*) FROM dbo.PlaySessionTableSegments);
DECLARE @CountTablesBefore int = (SELECT COUNT(*) FROM dbo.BilliardTables);
DECLARE @CountTypesBefore int = (SELECT COUNT(*) FROM dbo.TableTypes);
DECLARE @CountBookingsBefore int = (SELECT COUNT(*) FROM dbo.Bookings);

-------------------------------------------------------------------------------
-- GROUP 1: fn_CeilTo15Min (Pure function unit tests)
-------------------------------------------------------------------------------
-- 1.1: 09:00:00
DECLARE @t1 datetime2(0) = '2026-10-08T09:00:00';
DECLARE @r1 datetime2(0) = dbo.fn_CeilTo15Min(@t1);
IF @r1 = '2026-10-08T09:00:00'
    INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
    VALUES ('fn_CeilTo15Min', N'09:00:00 giữ nguyên mốc', 'PASS', '09:00:00', CONVERT(varchar(30), @r1, 126), N'Đúng mốc 15 phút chẵn');
ELSE
    INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
    VALUES ('fn_CeilTo15Min', N'09:00:00 giữ nguyên mốc', 'FAIL', '09:00:00', CONVERT(varchar(30), @r1, 126), N'Không giữ nguyên mốc chẵn');

-- 1.2: 09:00:01
DECLARE @t2 datetime2(0) = '2026-10-08T09:00:01';
DECLARE @r2 datetime2(0) = dbo.fn_CeilTo15Min(@t2);
IF @r2 = '2026-10-08T09:15:00'
    INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
    VALUES ('fn_CeilTo15Min', N'09:00:01 làm tròn lên 09:15:00', 'PASS', '09:15:00', CONVERT(varchar(30), @r2, 126), N'Làm tròn lên block kế tiếp');
ELSE
    INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
    VALUES ('fn_CeilTo15Min', N'09:00:01 làm tròn lên 09:15:00', 'FAIL', '09:15:00', CONVERT(varchar(30), @r2, 126), N'Làm tròn sai');

-- 1.3: 08:50:00
DECLARE @t3 datetime2(0) = '2026-10-08T08:50:00';
DECLARE @r3 datetime2(0) = dbo.fn_CeilTo15Min(@t3);
IF @r3 = '2026-10-08T09:00:00'
    INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
    VALUES ('fn_CeilTo15Min', N'08:50:00 làm tròn lên 09:00:00', 'PASS', '09:00:00', CONVERT(varchar(30), @r3, 126), N'Làm tròn lên đầu giờ');
ELSE
    INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
    VALUES ('fn_CeilTo15Min', N'08:50:00 làm tròn lên 09:00:00', 'FAIL', '09:00:00', CONVERT(varchar(30), @r3, 126), N'Làm tròn sai');

-- 1.4: 09:14:59
DECLARE @t4 datetime2(0) = '2026-10-08T09:14:59';
DECLARE @r4 datetime2(0) = dbo.fn_CeilTo15Min(@t4);
IF @r4 = '2026-10-08T09:15:00'
    INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
    VALUES ('fn_CeilTo15Min', N'09:14:59 làm tròn lên 09:15:00', 'PASS', '09:15:00', CONVERT(varchar(30), @r4, 126), N'Làm tròn cận trên');
ELSE
    INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
    VALUES ('fn_CeilTo15Min', N'09:14:59 làm tròn lên 09:15:00', 'FAIL', '09:15:00', CONVERT(varchar(30), @r4, 126), N'Làm tròn sai');

-- 1.5: 09:15:00
DECLARE @t5 datetime2(0) = '2026-10-08T09:15:00';
DECLARE @r5 datetime2(0) = dbo.fn_CeilTo15Min(@t5);
IF @r5 = '2026-10-08T09:15:00'
    INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
    VALUES ('fn_CeilTo15Min', N'09:15:00 giữ nguyên mốc', 'PASS', '09:15:00', CONVERT(varchar(30), @r5, 126), N'Đúng mốc 15 phút chẵn');
ELSE
    INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
    VALUES ('fn_CeilTo15Min', N'09:15:00 giữ nguyên mốc', 'FAIL', '09:15:00', CONVERT(varchar(30), @r5, 126), N'Không giữ nguyên mốc chẵn');

-------------------------------------------------------------------------------
-- GROUP 2: fn_CalcPlaytimeAmount (Fixed time tests from PLAN_V2 Section 3)
-- Each test uses its OWN transaction and DISTINCT ZZ- table to avoid UX_PlaySessions_ActiveTable conflict.
-------------------------------------------------------------------------------

-- 2.1: PLAN_V2 Example 1: 09:00 -> 10:00 (100.000)
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT INTO dbo.TableTypes (Name, HourlyRate) VALUES (N'ZZ_Type_21', 100000);
    DECLARE @TT_21 int = SCOPE_IDENTITY();
    INSERT INTO dbo.BilliardTables (TableCode, TableTypeId, Status) VALUES (N'ZZ-T21', @TT_21, 'InUse');
    DECLARE @T_21 int = SCOPE_IDENTITY();

    INSERT INTO dbo.PlaySessions (TableId, OpenedById, StartAtUtc, HourlyRateSnapshot, SessionMode, BillingStartAtUtc)
    VALUES (@T_21, @TestStaffId, '2026-10-08T09:00:00', 100000, 'Open', '2026-10-08T09:00:00');
    DECLARE @S_21 int = SCOPE_IDENTITY();
    INSERT INTO dbo.PlaySessionTableSegments (SessionId, TableId, HourlyRateSnapshot, StartAtUtc, EndAtUtc)
    VALUES (@S_21, @T_21, 100000, '2026-10-08T09:00:00', '2026-10-08T10:00:00');

    DECLARE @Amt_21 decimal(18,2) = dbo.fn_CalcPlaytimeAmount(@S_21, '2026-10-08T10:00:00');
    IF @Amt_21 = 100000
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('fn_CalcPlaytimeAmount', N'PLAN_V2 Ví dụ 1: 09:00 - 10:00 (100k)', 'PASS', '100000.00', CAST(@Amt_21 AS varchar(30)), N'Đúng 100.000 VND');
    ELSE
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('fn_CalcPlaytimeAmount', N'PLAN_V2 Ví dụ 1: 09:00 - 10:00 (100k)', 'FAIL', '100000.00', CAST(@Amt_21 AS varchar(30)), N'Lệch giá trị tính tiền');

    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
    VALUES ('fn_CalcPlaytimeAmount', N'PLAN_V2 Ví dụ 1: 09:00 - 10:00 (100k)', 'FAIL', '100000.00', 'ERROR',
            N'Lỗi: [' + CAST(ERROR_NUMBER() AS nvarchar(20)) + N'] ' + ERROR_MESSAGE() + N' (Dòng ' + CAST(ERROR_LINE() AS nvarchar(20)) + N')');
END CATCH;

-- 2.2: PLAN_V2 Example 2: 08:50 -> 10:07 (Billed 09:00 -> 10:15 = 1.25h * 100k = 125.000)
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT INTO dbo.TableTypes (Name, HourlyRate) VALUES (N'ZZ_Type_22', 100000);
    DECLARE @TT_22 int = SCOPE_IDENTITY();
    INSERT INTO dbo.BilliardTables (TableCode, TableTypeId, Status) VALUES (N'ZZ-T22', @TT_22, 'InUse');
    DECLARE @T_22 int = SCOPE_IDENTITY();

    INSERT INTO dbo.PlaySessions (TableId, OpenedById, StartAtUtc, HourlyRateSnapshot, SessionMode, BillingStartAtUtc)
    VALUES (@T_22, @TestStaffId, '2026-10-08T08:50:00', 100000, 'Open', '2026-10-08T09:00:00');
    DECLARE @S_22 int = SCOPE_IDENTITY();
    INSERT INTO dbo.PlaySessionTableSegments (SessionId, TableId, HourlyRateSnapshot, StartAtUtc, EndAtUtc)
    VALUES (@S_22, @T_22, 100000, '2026-10-08T08:50:00', '2026-10-08T10:07:00');

    DECLARE @Amt_22 decimal(18,2) = dbo.fn_CalcPlaytimeAmount(@S_22, '2026-10-08T10:07:00');
    IF @Amt_22 = 125000
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('fn_CalcPlaytimeAmount', N'PLAN_V2 Ví dụ 2: 08:50 - 10:07 (125k)', 'PASS', '125000.00', CAST(@Amt_22 AS varchar(30)), N'Đúng 125.000 VND (09:00-10:15 = 1.25h)');
    ELSE
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('fn_CalcPlaytimeAmount', N'PLAN_V2 Ví dụ 2: 08:50 - 10:07 (125k)', 'FAIL', '125000.00', CAST(@Amt_22 AS varchar(30)), N'Lệch giá trị tính tiền');

    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
    VALUES ('fn_CalcPlaytimeAmount', N'PLAN_V2 Ví dụ 2: 08:50 - 10:07 (125k)', 'FAIL', '125000.00', 'ERROR',
            N'Lỗi: [' + CAST(ERROR_NUMBER() AS nvarchar(20)) + N'] ' + ERROR_MESSAGE() + N' (Dòng ' + CAST(ERROR_LINE() AS nvarchar(20)) + N')');
END CATCH;

-- 2.3: PLAN_V2 Example 3: 09:07 -> 09:40 (Billed 09:15 -> 09:45 = 0.5h * 100k = 50.000)
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT INTO dbo.TableTypes (Name, HourlyRate) VALUES (N'ZZ_Type_23', 100000);
    DECLARE @TT_23 int = SCOPE_IDENTITY();
    INSERT INTO dbo.BilliardTables (TableCode, TableTypeId, Status) VALUES (N'ZZ-T23', @TT_23, 'InUse');
    DECLARE @T_23 int = SCOPE_IDENTITY();

    INSERT INTO dbo.PlaySessions (TableId, OpenedById, StartAtUtc, HourlyRateSnapshot, SessionMode, BillingStartAtUtc)
    VALUES (@T_23, @TestStaffId, '2026-10-08T09:07:00', 100000, 'Open', '2026-10-08T09:15:00');
    DECLARE @S_23 int = SCOPE_IDENTITY();
    INSERT INTO dbo.PlaySessionTableSegments (SessionId, TableId, HourlyRateSnapshot, StartAtUtc, EndAtUtc)
    VALUES (@S_23, @T_23, 100000, '2026-10-08T09:07:00', '2026-10-08T09:40:00');

    DECLARE @Amt_23 decimal(18,2) = dbo.fn_CalcPlaytimeAmount(@S_23, '2026-10-08T09:40:00');
    IF @Amt_23 = 50000
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('fn_CalcPlaytimeAmount', N'PLAN_V2 Ví dụ 3: 09:07 - 09:40 (50k)', 'PASS', '50000.00', CAST(@Amt_23 AS varchar(30)), N'Đúng 50.000 VND (09:15-09:45 = 0.5h)');
    ELSE
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('fn_CalcPlaytimeAmount', N'PLAN_V2 Ví dụ 3: 09:07 - 09:40 (50k)', 'FAIL', '50000.00', CAST(@Amt_23 AS varchar(30)), N'Lệch giá trị tính tiền');

    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
    VALUES ('fn_CalcPlaytimeAmount', N'PLAN_V2 Ví dụ 3: 09:07 - 09:40 (50k)', 'FAIL', '50000.00', 'ERROR',
            N'Lỗi: [' + CAST(ERROR_NUMBER() AS nvarchar(20)) + N'] ' + ERROR_MESSAGE() + N' (Dòng ' + CAST(ERROR_LINE() AS nvarchar(20)) + N')');
END CATCH;

-- 2.4: PLAN_V2 Example 4: 09:07 -> 09:10 (Billed 09:15 -> 09:15 = 0s => Minimum 1 block = 25.000 & BillingEnd = BillingStart + 15 min)
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT INTO dbo.TableTypes (Name, HourlyRate) VALUES (N'ZZ_Type_24', 100000);
    DECLARE @TT_24 int = SCOPE_IDENTITY();
    INSERT INTO dbo.BilliardTables (TableCode, TableTypeId, Status) VALUES (N'ZZ-T24', @TT_24, 'InUse');
    DECLARE @T_24 int = SCOPE_IDENTITY();

    INSERT INTO dbo.PlaySessions (TableId, OpenedById, StartAtUtc, HourlyRateSnapshot, SessionMode, BillingStartAtUtc)
    VALUES (@T_24, @TestStaffId, '2026-10-08T09:07:00', 100000, 'Open', '2026-10-08T09:15:00');
    DECLARE @S_24 int = SCOPE_IDENTITY();
    INSERT INTO dbo.PlaySessionTableSegments (SessionId, TableId, HourlyRateSnapshot, StartAtUtc, EndAtUtc)
    VALUES (@S_24, @T_24, 100000, '2026-10-08T09:07:00', '2026-10-08T09:10:00');

    DECLARE @Amt_24 decimal(18,2) = dbo.fn_CalcPlaytimeAmount(@S_24, '2026-10-08T09:10:00');
    DECLARE @BillStart_24 datetime2(0) = dbo.fn_CeilTo15Min('2026-10-08T09:07:00'); -- 09:15
    DECLARE @BillEnd_24 datetime2(0) = dbo.fn_CeilTo15Min('2026-10-08T09:10:00');   -- 09:15
    IF @BillEnd_24 <= @BillStart_24 SET @BillEnd_24 = DATEADD(minute, 15, @BillStart_24); -- 09:30

    IF @Amt_24 = 25000 AND @BillEnd_24 = '2026-10-08T09:30:00'
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('fn_CalcPlaytimeAmount', N'PLAN_V2 Ví dụ 4: 09:07 - 09:10 tối thiểu 1 block (25k)', 'PASS', '25000.00 / 09:30', CAST(@Amt_24 AS varchar(20)) + ' / ' + CONVERT(varchar(20), @BillEnd_24, 108), N'Đúng 25.000 VND và BillingEndAtUtc = BillingStart + 15m (09:30)');
    ELSE
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('fn_CalcPlaytimeAmount', N'PLAN_V2 Ví dụ 4: 09:07 - 09:10 tối thiểu 1 block (25k)', 'FAIL', '25000.00 / 09:30', CAST(@Amt_24 AS varchar(20)) + ' / ' + CONVERT(varchar(20), @BillEnd_24, 108), N'Lệch giá trị hoặc BillingEnd');

    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
    VALUES ('fn_CalcPlaytimeAmount', N'PLAN_V2 Ví dụ 4: 09:07 - 09:10 tối thiểu 1 block (25k)', 'FAIL', '25000.00', 'ERROR',
            N'Lỗi: [' + CAST(ERROR_NUMBER() AS nvarchar(20)) + N'] ' + ERROR_MESSAGE() + N' (Dòng ' + CAST(ERROR_LINE() AS nvarchar(20)) + N')');
END CATCH;

-- 2.5: PLAN_V2 Example 5: Chuyển bàn đổi giá 09:00 Pool 100k, 09:30 Carom 120k, 10:00 đóng => 50k + 60k = 110.000
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT INTO dbo.TableTypes (Name, HourlyRate) VALUES (N'ZZ_Pool_25', 100000), (N'ZZ_Carom_25', 120000);
    DECLARE @TT_P25 int = (SELECT Id FROM dbo.TableTypes WHERE Name = N'ZZ_Pool_25');
    DECLARE @TT_C25 int = (SELECT Id FROM dbo.TableTypes WHERE Name = N'ZZ_Carom_25');
    INSERT INTO dbo.BilliardTables (TableCode, TableTypeId, Status) VALUES (N'ZZ-T25A', @TT_P25, 'Available'), (N'ZZ-T25B', @TT_C25, 'InUse');
    DECLARE @T_25A int = (SELECT Id FROM dbo.BilliardTables WHERE TableCode = N'ZZ-T25A');
    DECLARE @T_25B int = (SELECT Id FROM dbo.BilliardTables WHERE TableCode = N'ZZ-T25B');

    INSERT INTO dbo.PlaySessions (TableId, OpenedById, StartAtUtc, HourlyRateSnapshot, SessionMode, BillingStartAtUtc)
    VALUES (@T_25B, @TestStaffId, '2026-10-08T09:00:00', 100000, 'Open', '2026-10-08T09:00:00');
    DECLARE @S_25 int = SCOPE_IDENTITY();
    INSERT INTO dbo.PlaySessionTableSegments (SessionId, TableId, HourlyRateSnapshot, StartAtUtc, EndAtUtc)
    VALUES (@S_25, @T_25A, 100000, '2026-10-08T09:00:00', '2026-10-08T09:30:00'),
           (@S_25, @T_25B, 120000, '2026-10-08T09:30:00', '2026-10-08T10:00:00');

    DECLARE @Amt_25 decimal(18,2) = dbo.fn_CalcPlaytimeAmount(@S_25, '2026-10-08T10:00:00');
    IF @Amt_25 = 110000
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('fn_CalcPlaytimeAmount', N'PLAN_V2 Ví dụ 5: Chuyển bàn đổi giá (110k)', 'PASS', '110000.00', CAST(@Amt_25 AS varchar(30)), N'Đúng 110.000 VND (50.000 Pool + 60.000 Carom)');
    ELSE
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('fn_CalcPlaytimeAmount', N'PLAN_V2 Ví dụ 5: Chuyển bàn đổi giá (110k)', 'FAIL', '110000.00', CAST(@Amt_25 AS varchar(30)), N'Lệch giá trị tính tiền');

    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
    VALUES ('fn_CalcPlaytimeAmount', N'PLAN_V2 Ví dụ 5: Chuyển bàn đổi giá (110k)', 'FAIL', '110000.00', 'ERROR',
            N'Lỗi: [' + CAST(ERROR_NUMBER() AS nvarchar(20)) + N'] ' + ERROR_MESSAGE() + N' (Dòng ' + CAST(ERROR_LINE() AS nvarchar(20)) + N')');
END CATCH;

-- 2.6: Consistency: Tổng tiền tính độc lập từng đoạn bàn bằng PlaytimeAmount
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT INTO dbo.TableTypes (Name, HourlyRate) VALUES (N'ZZ_Pool_26', 100000), (N'ZZ_Carom_26', 120000);
    DECLARE @TT_P26 int = (SELECT Id FROM dbo.TableTypes WHERE Name = N'ZZ_Pool_26');
    DECLARE @TT_C26 int = (SELECT Id FROM dbo.TableTypes WHERE Name = N'ZZ_Carom_26');
    INSERT INTO dbo.BilliardTables (TableCode, TableTypeId, Status) VALUES (N'ZZ-T26A', @TT_P26, 'Available'), (N'ZZ-T26B', @TT_C26, 'InUse');
    DECLARE @T_26A int = (SELECT Id FROM dbo.BilliardTables WHERE TableCode = N'ZZ-T26A');
    DECLARE @T_26B int = (SELECT Id FROM dbo.BilliardTables WHERE TableCode = N'ZZ-T26B');

    INSERT INTO dbo.PlaySessions (TableId, OpenedById, StartAtUtc, HourlyRateSnapshot, SessionMode, BillingStartAtUtc)
    VALUES (@T_26B, @TestStaffId, '2026-10-08T09:00:00', 100000, 'Open', '2026-10-08T09:00:00');
    DECLARE @S_26 int = SCOPE_IDENTITY();
    INSERT INTO dbo.PlaySessionTableSegments (SessionId, TableId, HourlyRateSnapshot, StartAtUtc, EndAtUtc)
    VALUES (@S_26, @T_26A, 100000, '2026-10-08T09:00:00', '2026-10-08T09:30:00'),
           (@S_26, @T_26B, 120000, '2026-10-08T09:30:00', '2026-10-08T10:00:00');

    DECLARE @SumSegments decimal(18,2) = 0;
    SELECT @SumSegments = SUM(ROUND(CONVERT(decimal(18,2), DATEDIFF_BIG(second, dbo.fn_CeilTo15Min(StartAtUtc), dbo.fn_CeilTo15Min(EndAtUtc))) * HourlyRateSnapshot / 3600.0, 0))
      FROM dbo.PlaySessionTableSegments WHERE SessionId = @S_26;
    DECLARE @SessionTotal decimal(18,2) = dbo.fn_CalcPlaytimeAmount(@S_26, '2026-10-08T10:00:00');

    IF @SumSegments = @SessionTotal AND @SessionTotal = 110000
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('Consistency', N'Tổng tiền các đoạn bàn khớp PlaytimeAmount', 'PASS', '110000.00', CAST(@SumSegments AS varchar(30)), N'Tổng các đoạn = PlaytimeAmount = 110.000 VND');
    ELSE
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('Consistency', N'Tổng tiền các đoạn bàn khớp PlaytimeAmount', 'FAIL', CAST(@SessionTotal AS varchar(30)), CAST(@SumSegments AS varchar(30)), N'Lệch giữa tổng đoạn và PlaytimeAmount');

    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
    VALUES ('Consistency', N'Tổng tiền các đoạn bàn khớp PlaytimeAmount', 'FAIL', '110000.00', 'ERROR',
            N'Lỗi: [' + CAST(ERROR_NUMBER() AS nvarchar(20)) + N'] ' + ERROR_MESSAGE() + N' (Dòng ' + CAST(ERROR_LINE() AS nvarchar(20)) + N')');
END CATCH;

-- 2.7: Consistency: Phiên đã Closed trả đúng PlaytimeAmount đã lưu (bất kể @AtUtc truyền vào)
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT INTO dbo.TableTypes (Name, HourlyRate) VALUES (N'ZZ_Type_27', 100000);
    DECLARE @TT_27 int = SCOPE_IDENTITY();
    INSERT INTO dbo.BilliardTables (TableCode, TableTypeId, Status) VALUES (N'ZZ-T27', @TT_27, 'AwaitingPayment');
    DECLARE @T_27 int = SCOPE_IDENTITY();

    INSERT INTO dbo.PlaySessions (TableId, OpenedById, ClosedById, StartAtUtc, EndAtUtc, HourlyRateSnapshot, PlaytimeAmount, Status, SessionMode, BillingStartAtUtc, BillingEndAtUtc)
    VALUES (@T_27, @TestStaffId, @TestStaffId, '2026-10-08T09:00:00', '2026-10-08T10:00:00', 100000, 100000, 'Closed', 'Open', '2026-10-08T09:00:00', '2026-10-08T10:00:00');
    DECLARE @S_27 int = SCOPE_IDENTITY();
    INSERT INTO dbo.PlaySessionTableSegments (SessionId, TableId, HourlyRateSnapshot, StartAtUtc, EndAtUtc)
    VALUES (@S_27, @T_27, 100000, '2026-10-08T09:00:00', '2026-10-08T10:00:00');

    DECLARE @Amt_27Closed decimal(18,2) = dbo.fn_CalcPlaytimeAmount(@S_27, SYSUTCDATETIME());
    IF @Amt_27Closed = 100000
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('Consistency', N'Phiên Closed trả đúng PlaytimeAmount đã lưu', 'PASS', '100000.00', CAST(@Amt_27Closed AS varchar(30)), N'Khớp hoàn toàn 100.000 VND (bỏ qua @AtUtc)');
    ELSE
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('Consistency', N'Phiên Closed trả đúng PlaytimeAmount đã lưu', 'FAIL', '100000.00', CAST(@Amt_27Closed AS varchar(30)), N'Lệch giá trị');

    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
    VALUES ('Consistency', N'Phiên Closed trả đúng PlaytimeAmount đã lưu', 'FAIL', '100000.00', 'ERROR',
            N'Lỗi: [' + CAST(ERROR_NUMBER() AS nvarchar(20)) + N'] ' + ERROR_MESSAGE() + N' (Dòng ' + CAST(ERROR_LINE() AS nvarchar(20)) + N')');
END CATCH;

-------------------------------------------------------------------------------
-- GROUP 3: Stored Procedure Real Flows (Each test has its own Transaction + clean ROLLBACK)
-------------------------------------------------------------------------------

-- 3.1: usp_OpenSession 'Open' mode
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT INTO dbo.TableTypes (Name, HourlyRate) VALUES (N'ZZ_Pool_31', 100000);
    DECLARE @TT_31 int = SCOPE_IDENTITY();
    INSERT INTO dbo.BilliardTables (TableCode, TableTypeId, Status) VALUES (N'ZZ-31', @TT_31, 'Available');
    DECLARE @T_31 int = SCOPE_IDENTITY();

    DECLARE @OutOpen TABLE (SessionId int, BillingStartAtUtc datetime2(0), PlannedEndAtUtc datetime2(0), SessionMode varchar(10));
    INSERT INTO @OutOpen EXEC dbo.usp_OpenSession @TableId = @T_31, @StaffId = @TestStaffId, @SessionMode = 'Open';

    DECLARE @S_31 int, @Mode_31 varchar(10), @Planned_31 datetime2(0);
    SELECT @S_31 = SessionId, @Mode_31 = SessionMode, @Planned_31 = PlannedEndAtUtc FROM @OutOpen;

    IF @S_31 IS NOT NULL AND @Mode_31 = 'Open' AND @Planned_31 IS NULL
       AND EXISTS (SELECT 1 FROM dbo.BilliardTables WHERE Id = @T_31 AND Status = 'InUse')
       AND EXISTS (SELECT 1 FROM dbo.PlaySessionTableSegments WHERE SessionId = @S_31 AND TableId = @T_31 AND EndAtUtc IS NULL)
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('usp_OpenSession', N'Mở phiên Open thành công', 'PASS', 'Open / InUse / 1 open segment', 'Open / InUse / 1 open segment', N'Tạo phiên Open, bàn InUse, tạo segment đầu tiên');
    ELSE
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('usp_OpenSession', N'Mở phiên Open thành công', 'FAIL', 'Open / InUse / 1 open segment', 'Mismatch', N'Không đúng trạng thái');

    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
    VALUES ('usp_OpenSession', N'Mở phiên Open thành công', 'FAIL', 'Open / InUse', CAST(ERROR_NUMBER() AS varchar(20)),
            CASE WHEN ERROR_NUMBER() = 3915
                 THEN N'Lỗi 3915: Có thể procedure đã ném lỗi bị che bởi INSERT-EXEC, hãy gọi bằng EXEC thường để xem lỗi thật'
                 ELSE N'Lỗi: [' + CAST(ERROR_NUMBER() AS nvarchar(20)) + N'] ' + ERROR_MESSAGE() + N' (Dòng ' + CAST(ERROR_LINE() AS nvarchar(20)) + N')'
            END);
END CATCH;

-- 3.2: usp_OpenSession 'Timed' mode
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT INTO dbo.TableTypes (Name, HourlyRate) VALUES (N'ZZ_Pool_32', 100000);
    DECLARE @TT_32 int = SCOPE_IDENTITY();
    INSERT INTO dbo.BilliardTables (TableCode, TableTypeId, Status) VALUES (N'ZZ-32', @TT_32, 'Available');
    DECLARE @T_32 int = SCOPE_IDENTITY();

    DECLARE @OutTimed TABLE (SessionId int, BillingStartAtUtc datetime2(0), PlannedEndAtUtc datetime2(0), SessionMode varchar(10));
    INSERT INTO @OutTimed EXEC dbo.usp_OpenSession @TableId = @T_32, @StaffId = @TestStaffId, @SessionMode = 'Timed', @PlannedMinutes = 60;

    DECLARE @S_32 int, @Mode_32 varchar(10), @Planned_32 datetime2(0), @BillStart_32 datetime2(0);
    SELECT @S_32 = SessionId, @Mode_32 = SessionMode, @Planned_32 = PlannedEndAtUtc, @BillStart_32 = BillingStartAtUtc FROM @OutTimed;

    IF @S_32 IS NOT NULL AND @Mode_32 = 'Timed' AND @Planned_32 IS NOT NULL
       AND @Planned_32 = DATEADD(minute, 60, @BillStart_32)
       AND EXISTS (SELECT 1 FROM dbo.PlaySessions WHERE Id = @S_32 AND PlannedEndAtUtc = @Planned_32 AND BillingStartAtUtc = @BillStart_32)
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('usp_OpenSession', N'Mở phiên Timed thành công', 'PASS', 'Timed / PlannedEnd != NULL', 'Timed / PlannedEnd != NULL', N'Tạo phiên Timed, PlannedEndAtUtc chính xác');
    ELSE
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('usp_OpenSession', N'Mở phiên Timed thành công', 'FAIL', 'Timed / PlannedEnd != NULL', 'Mismatch', N'Không tạo được phiên Timed đúng');

    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
    VALUES ('usp_OpenSession', N'Mở phiên Timed thành công', 'FAIL', 'Timed / PlannedEnd != NULL', CAST(ERROR_NUMBER() AS varchar(20)),
            CASE WHEN ERROR_NUMBER() = 3915
                 THEN N'Lỗi 3915: Có thể procedure đã ném lỗi bị che bởi INSERT-EXEC, hãy gọi bằng EXEC thường để xem lỗi thật'
                 ELSE N'Lỗi: [' + CAST(ERROR_NUMBER() AS nvarchar(20)) + N'] ' + ERROR_MESSAGE() + N' (Dòng ' + CAST(ERROR_LINE() AS nvarchar(20)) + N')'
            END);
END CATCH;

-- 3.3: usp_OpenSession Error 51409 (Invalid SessionMode)
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT INTO dbo.TableTypes (Name, HourlyRate) VALUES (N'ZZ_Pool_33', 100000);
    DECLARE @TT_33 int = SCOPE_IDENTITY();
    INSERT INTO dbo.BilliardTables (TableCode, TableTypeId, Status) VALUES (N'ZZ-33', @TT_33, 'Available');
    DECLARE @T_33 int = SCOPE_IDENTITY();

    DECLARE @Err33 int = 0;
    BEGIN TRY
        EXEC dbo.usp_OpenSession @TableId = @T_33, @StaffId = @TestStaffId, @SessionMode = 'InvalidMode';
    END TRY
    BEGIN CATCH
        SET @Err33 = ERROR_NUMBER();
    END CATCH;

    IF @Err33 = 51409
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('usp_OpenSession', N'Bắt lỗi 51409 khi SessionMode sai', 'PASS', '51409', CAST(@Err33 AS varchar(20)), N'Bắt đúng lỗi 51409');
    ELSE
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('usp_OpenSession', N'Bắt lỗi 51409 khi SessionMode sai', 'FAIL', '51409', CAST(@Err33 AS varchar(20)), N'Mã lỗi không khớp');

    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
    VALUES ('usp_OpenSession', N'Bắt lỗi 51409 khi SessionMode sai', 'FAIL', '51409', 'ERROR',
            N'Lỗi: [' + CAST(ERROR_NUMBER() AS nvarchar(20)) + N'] ' + ERROR_MESSAGE() + N' (Dòng ' + CAST(ERROR_LINE() AS nvarchar(20)) + N')');
END CATCH;

-- 3.4: usp_OpenSession Error 51410 (Invalid PlannedMinutes)
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT INTO dbo.TableTypes (Name, HourlyRate) VALUES (N'ZZ_Pool_34', 100000);
    DECLARE @TT_34 int = SCOPE_IDENTITY();
    INSERT INTO dbo.BilliardTables (TableCode, TableTypeId, Status) VALUES (N'ZZ-34', @TT_34, 'Available');
    DECLARE @T_34 int = SCOPE_IDENTITY();

    DECLARE @Err34 int = 0;
    BEGIN TRY
        EXEC dbo.usp_OpenSession @TableId = @T_34, @StaffId = @TestStaffId, @SessionMode = 'Timed', @PlannedMinutes = NULL;
    END TRY
    BEGIN CATCH
        SET @Err34 = ERROR_NUMBER();
    END CATCH;

    IF @Err34 = 51410
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('usp_OpenSession', N'Bắt lỗi 51410 khi số phút không hợp lệ', 'PASS', '51410', CAST(@Err34 AS varchar(20)), N'Bắt đúng lỗi 51410');
    ELSE
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('usp_OpenSession', N'Bắt lỗi 51410 khi số phút không hợp lệ', 'FAIL', '51410', CAST(@Err34 AS varchar(20)), N'Mã lỗi không khớp');

    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
    VALUES ('usp_OpenSession', N'Bắt lỗi 51410 khi số phút không hợp lệ', 'FAIL', '51410', 'ERROR',
            N'Lỗi: [' + CAST(ERROR_NUMBER() AS nvarchar(20)) + N'] ' + ERROR_MESSAGE() + N' (Dòng ' + CAST(ERROR_LINE() AS nvarchar(20)) + N')');
END CATCH;

-- 3.5: usp_ExtendSession Success
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT INTO dbo.TableTypes (Name, HourlyRate) VALUES (N'ZZ_Pool_35', 100000);
    DECLARE @TT_35 int = SCOPE_IDENTITY();
    INSERT INTO dbo.BilliardTables (TableCode, TableTypeId, Status) VALUES (N'ZZ-35', @TT_35, 'Available');
    DECLARE @T_35 int = SCOPE_IDENTITY();

    DECLARE @OutTimed35 TABLE (SessionId int, BillingStartAtUtc datetime2(0), PlannedEndAtUtc datetime2(0), SessionMode varchar(10));
    INSERT INTO @OutTimed35 EXEC dbo.usp_OpenSession @TableId = @T_35, @StaffId = @TestStaffId, @SessionMode = 'Timed', @PlannedMinutes = 60;
    DECLARE @S_35 int = (SELECT SessionId FROM @OutTimed35);
    DECLARE @PlannedBefore datetime2(0) = (SELECT PlannedEndAtUtc FROM @OutTimed35);

    DECLARE @OutExt35 TABLE (SessionId int, PlannedEndAtUtc datetime2(0));
    INSERT INTO @OutExt35 EXEC dbo.usp_ExtendSession @SessionId = @S_35, @AddMinutes = 30, @StaffId = @TestStaffId;
    DECLARE @PlannedAfter datetime2(0) = (SELECT PlannedEndAtUtc FROM @OutExt35);

    IF @PlannedAfter = DATEADD(minute, 30, @PlannedBefore)
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('usp_ExtendSession', N'Gia hạn phiên Timed thành công', 'PASS', CONVERT(varchar(30), DATEADD(minute, 30, @PlannedBefore), 126), CONVERT(varchar(30), @PlannedAfter, 126), N'PlannedEndAtUtc tăng đúng 30 phút');
    ELSE
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('usp_ExtendSession', N'Gia hạn phiên Timed thành công', 'FAIL', CONVERT(varchar(30), DATEADD(minute, 30, @PlannedBefore), 126), CONVERT(varchar(30), @PlannedAfter, 126), N'Giờ gia hạn không khớp');

    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
    VALUES ('usp_ExtendSession', N'Gia hạn phiên Timed thành công', 'FAIL', '+30m', CAST(ERROR_NUMBER() AS varchar(20)),
            CASE WHEN ERROR_NUMBER() = 3915
                 THEN N'Lỗi 3915: Có thể procedure đã ném lỗi bị che bởi INSERT-EXEC, hãy gọi bằng EXEC thường để xem lỗi thật'
                 ELSE N'Lỗi: [' + CAST(ERROR_NUMBER() AS nvarchar(20)) + N'] ' + ERROR_MESSAGE() + N' (Dòng ' + CAST(ERROR_LINE() AS nvarchar(20)) + N')'
            END);
END CATCH;

-- 3.6: usp_ExtendSession Error 51602 (Cannot extend Open session)
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT INTO dbo.TableTypes (Name, HourlyRate) VALUES (N'ZZ_Pool_36', 100000);
    DECLARE @TT_36 int = SCOPE_IDENTITY();
    INSERT INTO dbo.BilliardTables (TableCode, TableTypeId, Status) VALUES (N'ZZ-36', @TT_36, 'Available');
    DECLARE @T_36 int = SCOPE_IDENTITY();

    DECLARE @OutOpen36 TABLE (SessionId int, BillingStartAtUtc datetime2(0), PlannedEndAtUtc datetime2(0), SessionMode varchar(10));
    INSERT INTO @OutOpen36 EXEC dbo.usp_OpenSession @TableId = @T_36, @StaffId = @TestStaffId, @SessionMode = 'Open';
    DECLARE @S_36 int = (SELECT SessionId FROM @OutOpen36);

    DECLARE @Err36 int = 0;
    BEGIN TRY
        EXEC dbo.usp_ExtendSession @SessionId = @S_36, @AddMinutes = 30, @StaffId = @TestStaffId;
    END TRY
    BEGIN CATCH
        SET @Err36 = ERROR_NUMBER();
    END CATCH;

    IF @Err36 = 51602
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('usp_ExtendSession', N'Bắt lỗi 51602 khi gia hạn phiên Open', 'PASS', '51602', CAST(@Err36 AS varchar(20)), N'Bắt đúng lỗi 51602');
    ELSE
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('usp_ExtendSession', N'Bắt lỗi 51602 khi gia hạn phiên Open', 'FAIL', '51602', CAST(@Err36 AS varchar(20)), N'Mã lỗi không khớp');

    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
    VALUES ('usp_ExtendSession', N'Bắt lỗi 51602 khi gia hạn phiên Open', 'FAIL', '51602', 'ERROR',
            N'Lỗi: [' + CAST(ERROR_NUMBER() AS nvarchar(20)) + N'] ' + ERROR_MESSAGE() + N' (Dòng ' + CAST(ERROR_LINE() AS nvarchar(20)) + N')');
END CATCH;

-- 3.7: usp_ExtendSession Error 51603 (Invalid AddMinutes)
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT INTO dbo.TableTypes (Name, HourlyRate) VALUES (N'ZZ_Pool_37', 100000);
    DECLARE @TT_37 int = SCOPE_IDENTITY();
    INSERT INTO dbo.BilliardTables (TableCode, TableTypeId, Status) VALUES (N'ZZ-37', @TT_37, 'Available');
    DECLARE @T_37 int = SCOPE_IDENTITY();

    DECLARE @OutTimed37 TABLE (SessionId int, BillingStartAtUtc datetime2(0), PlannedEndAtUtc datetime2(0), SessionMode varchar(10));
    INSERT INTO @OutTimed37 EXEC dbo.usp_OpenSession @TableId = @T_37, @StaffId = @TestStaffId, @SessionMode = 'Timed', @PlannedMinutes = 60;
    DECLARE @S_37 int = (SELECT SessionId FROM @OutTimed37);

    DECLARE @Err37 int = 0;
    BEGIN TRY
        EXEC dbo.usp_ExtendSession @SessionId = @S_37, @AddMinutes = 10, @StaffId = @TestStaffId;
    END TRY
    BEGIN CATCH
        SET @Err37 = ERROR_NUMBER();
    END CATCH;

    IF @Err37 = 51603
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('usp_ExtendSession', N'Bắt lỗi 51603 khi số phút gia hạn sai', 'PASS', '51603', CAST(@Err37 AS varchar(20)), N'Bắt đúng lỗi 51603');
    ELSE
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('usp_ExtendSession', N'Bắt lỗi 51603 khi số phút gia hạn sai', 'FAIL', '51603', CAST(@Err37 AS varchar(20)), N'Mã lỗi không khớp');

    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
    VALUES ('usp_ExtendSession', N'Bắt lỗi 51603 khi số phút gia hạn sai', 'FAIL', '51603', 'ERROR',
            N'Lỗi: [' + CAST(ERROR_NUMBER() AS nvarchar(20)) + N'] ' + ERROR_MESSAGE() + N' (Dòng ' + CAST(ERROR_LINE() AS nvarchar(20)) + N')');
END CATCH;

-- 3.8: usp_TransferSession Success
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT INTO dbo.TableTypes (Name, HourlyRate) VALUES (N'ZZ_Pool_38', 100000), (N'ZZ_Carom_38', 120000);
    DECLARE @TT_P38 int = (SELECT Id FROM dbo.TableTypes WHERE Name = N'ZZ_Pool_38');
    DECLARE @TT_C38 int = (SELECT Id FROM dbo.TableTypes WHERE Name = N'ZZ_Carom_38');
    INSERT INTO dbo.BilliardTables (TableCode, TableTypeId, Status) VALUES (N'ZZ-38A', @TT_P38, 'Available'), (N'ZZ-38B', @TT_C38, 'Available');
    DECLARE @T_38A int = (SELECT Id FROM dbo.BilliardTables WHERE TableCode = N'ZZ-38A');
    DECLARE @T_38B int = (SELECT Id FROM dbo.BilliardTables WHERE TableCode = N'ZZ-38B');

    DECLARE @OutOpen38 TABLE (SessionId int, BillingStartAtUtc datetime2(0), PlannedEndAtUtc datetime2(0), SessionMode varchar(10));
    INSERT INTO @OutOpen38 EXEC dbo.usp_OpenSession @TableId = @T_38A, @StaffId = @TestStaffId, @SessionMode = 'Open';
    DECLARE @S_38 int = (SELECT SessionId FROM @OutOpen38);

    DECLARE @OutTransfer38 TABLE (SessionId int, OldTableId int, NewTableId int, NewTableCode nvarchar(20), NewHourlyRate decimal(18,2), TransferAtUtc datetime2(0));
    INSERT INTO @OutTransfer38 EXEC dbo.usp_TransferSession @SessionId = @S_38, @NewTableId = @T_38B, @StaffId = @TestStaffId;

    DECLARE @OldT_Res int, @NewT_Res int;
    SELECT @OldT_Res = OldTableId, @NewT_Res = NewTableId FROM @OutTransfer38;

    IF @OldT_Res = @T_38A AND @NewT_Res = @T_38B
       AND EXISTS (SELECT 1 FROM dbo.BilliardTables WHERE Id = @T_38A AND Status = 'Available')
       AND EXISTS (SELECT 1 FROM dbo.BilliardTables WHERE Id = @T_38B AND Status = 'InUse')
       AND EXISTS (SELECT 1 FROM dbo.PlaySessions WHERE Id = @S_38 AND TableId = @T_38B)
       AND (SELECT COUNT(*) FROM dbo.PlaySessionTableSegments WHERE SessionId = @S_38) = 2
       AND (SELECT COUNT(*) FROM dbo.PlaySessionTableSegments WHERE SessionId = @S_38 AND EndAtUtc IS NULL) = 1
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('usp_TransferSession', N'Chuyển bàn thành công', 'PASS', 'Old Avail / New InUse / Segments=2', 'Old Avail / New InUse / Segments=2', N'Bàn cũ Available, bàn mới InUse, đóng segment cũ, mở segment mới');
    ELSE
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('usp_TransferSession', N'Chuyển bàn thành công', 'FAIL', 'Old Avail / New InUse / Segments=2', 'Mismatch', N'Trạng thái chuyển bàn không đúng');

    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
    VALUES ('usp_TransferSession', N'Chuyển bàn thành công', 'FAIL', 'Success', CAST(ERROR_NUMBER() AS varchar(20)),
            CASE WHEN ERROR_NUMBER() = 3915
                 THEN N'Lỗi 3915: Có thể procedure đã ném lỗi bị che bởi INSERT-EXEC, hãy gọi bằng EXEC thường để xem lỗi thật'
                 ELSE N'Lỗi: [' + CAST(ERROR_NUMBER() AS nvarchar(20)) + N'] ' + ERROR_MESSAGE() + N' (Dòng ' + CAST(ERROR_LINE() AS nvarchar(20)) + N')'
            END);
END CATCH;

-- 3.9: usp_TransferSession Error 51613 (Target table not Available)
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT INTO dbo.TableTypes (Name, HourlyRate) VALUES (N'ZZ_Pool_39', 100000);
    DECLARE @TT_39 int = SCOPE_IDENTITY();
    INSERT INTO dbo.BilliardTables (TableCode, TableTypeId, Status) VALUES (N'ZZ-39A', @TT_39, 'Available'), (N'ZZ-39B', @TT_39, 'Maintenance');
    DECLARE @T_39A int = (SELECT Id FROM dbo.BilliardTables WHERE TableCode = N'ZZ-39A');
    DECLARE @T_39B int = (SELECT Id FROM dbo.BilliardTables WHERE TableCode = N'ZZ-39B');

    DECLARE @OutOpen39 TABLE (SessionId int, BillingStartAtUtc datetime2(0), PlannedEndAtUtc datetime2(0), SessionMode varchar(10));
    INSERT INTO @OutOpen39 EXEC dbo.usp_OpenSession @TableId = @T_39A, @StaffId = @TestStaffId, @SessionMode = 'Open';
    DECLARE @S_39 int = (SELECT SessionId FROM @OutOpen39);

    DECLARE @Err39 int = 0;
    BEGIN TRY
        EXEC dbo.usp_TransferSession @SessionId = @S_39, @NewTableId = @T_39B, @StaffId = @TestStaffId;
    END TRY
    BEGIN CATCH
        SET @Err39 = ERROR_NUMBER();
    END CATCH;

    IF @Err39 = 51613
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('usp_TransferSession', N'Bắt lỗi 51613 khi bàn đích không Available', 'PASS', '51613', CAST(@Err39 AS varchar(20)), N'Bắt đúng lỗi 51613');
    ELSE
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('usp_TransferSession', N'Bắt lỗi 51613 khi bàn đích không Available', 'FAIL', '51613', CAST(@Err39 AS varchar(20)), N'Mã lỗi không khớp');

    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
    VALUES ('usp_TransferSession', N'Bắt lỗi 51613 khi bàn đích không Available', 'FAIL', '51613', 'ERROR',
            N'Lỗi: [' + CAST(ERROR_NUMBER() AS nvarchar(20)) + N'] ' + ERROR_MESSAGE() + N' (Dòng ' + CAST(ERROR_LINE() AS nvarchar(20)) + N')');
END CATCH;

-- 3.10: usp_TransferSession Error 51614 (Target is same table)
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT INTO dbo.TableTypes (Name, HourlyRate) VALUES (N'ZZ_Pool_310', 100000);
    DECLARE @TT_310 int = SCOPE_IDENTITY();
    INSERT INTO dbo.BilliardTables (TableCode, TableTypeId, Status) VALUES (N'ZZ-310', @TT_310, 'Available');
    DECLARE @T_310 int = SCOPE_IDENTITY();

    DECLARE @OutOpen310 TABLE (SessionId int, BillingStartAtUtc datetime2(0), PlannedEndAtUtc datetime2(0), SessionMode varchar(10));
    INSERT INTO @OutOpen310 EXEC dbo.usp_OpenSession @TableId = @T_310, @StaffId = @TestStaffId, @SessionMode = 'Open';
    DECLARE @S_310 int = (SELECT SessionId FROM @OutOpen310);

    DECLARE @Err310 int = 0;
    BEGIN TRY
        EXEC dbo.usp_TransferSession @SessionId = @S_310, @NewTableId = @T_310, @StaffId = @TestStaffId;
    END TRY
    BEGIN CATCH
        SET @Err310 = ERROR_NUMBER();
    END CATCH;

    IF @Err310 = 51614
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('usp_TransferSession', N'Bắt lỗi 51614 khi chuyển vào chính bàn đang chơi', 'PASS', '51614', CAST(@Err310 AS varchar(20)), N'Bắt đúng lỗi 51614');
    ELSE
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('usp_TransferSession', N'Bắt lỗi 51614 khi chuyển vào chính bàn đang chơi', 'FAIL', '51614', CAST(@Err310 AS varchar(20)), N'Mã lỗi không khớp');

    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
    VALUES ('usp_TransferSession', N'Bắt lỗi 51614 khi chuyển vào chính bàn đang chơi', 'FAIL', '51614', 'ERROR',
            N'Lỗi: [' + CAST(ERROR_NUMBER() AS nvarchar(20)) + N'] ' + ERROR_MESSAGE() + N' (Dòng ' + CAST(ERROR_LINE() AS nvarchar(20)) + N')');
END CATCH;

-- 3.11: usp_TransferSession Error 51615 (Target table is reserved by a current booking)
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT INTO dbo.TableTypes (Name, HourlyRate) VALUES (N'ZZ_Pool_311', 100000);
    DECLARE @TT_311 int = SCOPE_IDENTITY();
    INSERT INTO dbo.BilliardTables (TableCode, TableTypeId, Status) VALUES (N'ZZ-311A', @TT_311, 'Available'), (N'ZZ-311B', @TT_311, 'Available');
    DECLARE @T_311A int = (SELECT Id FROM dbo.BilliardTables WHERE TableCode = N'ZZ-311A');
    DECLARE @T_311B int = (SELECT Id FROM dbo.BilliardTables WHERE TableCode = N'ZZ-311B');

    -- Create active booking on table B
    DECLARE @Now311 datetime2(0) = SYSUTCDATETIME();
    INSERT INTO dbo.Bookings (CustomerId, TableId, StartAtUtc, EndAtUtc, Status)
    VALUES (@TestCustomerId, @T_311B, DATEADD(minute, -10, @Now311), DATEADD(minute, 50, @Now311), 'CheckedIn');

    DECLARE @OutOpen311 TABLE (SessionId int, BillingStartAtUtc datetime2(0), PlannedEndAtUtc datetime2(0), SessionMode varchar(10));
    INSERT INTO @OutOpen311 EXEC dbo.usp_OpenSession @TableId = @T_311A, @StaffId = @TestStaffId, @SessionMode = 'Open';
    DECLARE @S_311 int = (SELECT SessionId FROM @OutOpen311);

    DECLARE @Err311 int = 0;
    BEGIN TRY
        EXEC dbo.usp_TransferSession @SessionId = @S_311, @NewTableId = @T_311B, @StaffId = @TestStaffId;
    END TRY
    BEGIN CATCH
        SET @Err311 = ERROR_NUMBER();
    END CATCH;

    IF @Err311 = 51615
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('usp_TransferSession', N'Bắt lỗi 51615 khi bàn đích đang giữ chỗ', 'PASS', '51615', CAST(@Err311 AS varchar(20)), N'Bắt đúng lỗi 51615');
    ELSE
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('usp_TransferSession', N'Bắt lỗi 51615 khi bàn đích đang giữ chỗ', 'FAIL', '51615', CAST(@Err311 AS varchar(20)), N'Mã lỗi không khớp');

    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
    VALUES ('usp_TransferSession', N'Bắt lỗi 51615 khi bàn đích đang giữ chỗ', 'FAIL', '51615', 'ERROR',
            N'Lỗi: [' + CAST(ERROR_NUMBER() AS nvarchar(20)) + N'] ' + ERROR_MESSAGE() + N' (Dòng ' + CAST(ERROR_LINE() AS nvarchar(20)) + N')');
END CATCH;

-- 3.12: usp_TransferSession Error 51616 (Cannot transfer session linked to booking)
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT INTO dbo.TableTypes (Name, HourlyRate) VALUES (N'ZZ_Pool_312', 100000);
    DECLARE @TT_312 int = SCOPE_IDENTITY();
    INSERT INTO dbo.BilliardTables (TableCode, TableTypeId, Status) VALUES (N'ZZ-312A', @TT_312, 'Available'), (N'ZZ-312B', @TT_312, 'Available');
    DECLARE @T_312A int = (SELECT Id FROM dbo.BilliardTables WHERE TableCode = N'ZZ-312A');
    DECLARE @T_312B int = (SELECT Id FROM dbo.BilliardTables WHERE TableCode = N'ZZ-312B');

    DECLARE @Now312 datetime2(0) = SYSUTCDATETIME();
    INSERT INTO dbo.Bookings (CustomerId, TableId, StartAtUtc, EndAtUtc, Status)
    VALUES (@TestCustomerId, @T_312A, DATEADD(minute, -10, @Now312), DATEADD(minute, 50, @Now312), 'CheckedIn');
    DECLARE @Bk_312 int = SCOPE_IDENTITY();

    DECLARE @OutOpen312 TABLE (SessionId int, BillingStartAtUtc datetime2(0), PlannedEndAtUtc datetime2(0), SessionMode varchar(10));
    INSERT INTO @OutOpen312 EXEC dbo.usp_OpenSession @TableId = @T_312A, @StaffId = @TestStaffId, @BookingId = @Bk_312, @CustomerId = @TestCustomerId;
    DECLARE @S_312 int = (SELECT SessionId FROM @OutOpen312);

    DECLARE @Err312 int = 0;
    BEGIN TRY
        EXEC dbo.usp_TransferSession @SessionId = @S_312, @NewTableId = @T_312B, @StaffId = @TestStaffId;
    END TRY
    BEGIN CATCH
        SET @Err312 = ERROR_NUMBER();
    END CATCH;

    IF @Err312 = 51616
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('usp_TransferSession', N'Bắt lỗi 51616 khi chuyển phiên có BookingId', 'PASS', '51616', CAST(@Err312 AS varchar(20)), N'Bắt đúng lỗi 51616');
    ELSE
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('usp_TransferSession', N'Bắt lỗi 51616 khi chuyển phiên có BookingId', 'FAIL', '51616', CAST(@Err312 AS varchar(20)), N'Mã lỗi không khớp');

    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
    VALUES ('usp_TransferSession', N'Bắt lỗi 51616 khi chuyển phiên có BookingId', 'FAIL', '51616', 'ERROR',
            N'Lỗi: [' + CAST(ERROR_NUMBER() AS nvarchar(20)) + N'] ' + ERROR_MESSAGE() + N' (Dòng ' + CAST(ERROR_LINE() AS nvarchar(20)) + N')');
END CATCH;

-- 3.13: usp_CloseSession after transfer
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT INTO dbo.TableTypes (Name, HourlyRate) VALUES (N'ZZ_Pool_313', 100000), (N'ZZ_Carom_313', 120000);
    DECLARE @TT_P313 int = (SELECT Id FROM dbo.TableTypes WHERE Name = N'ZZ_Pool_313');
    DECLARE @TT_C313 int = (SELECT Id FROM dbo.TableTypes WHERE Name = N'ZZ_Carom_313');
    INSERT INTO dbo.BilliardTables (TableCode, TableTypeId, Status) VALUES (N'ZZ-313A', @TT_P313, 'Available'), (N'ZZ-313B', @TT_C313, 'Available');
    DECLARE @T_313A int = (SELECT Id FROM dbo.BilliardTables WHERE TableCode = N'ZZ-313A');
    DECLARE @T_313B int = (SELECT Id FROM dbo.BilliardTables WHERE TableCode = N'ZZ-313B');

    DECLARE @OutOpen313 TABLE (SessionId int, BillingStartAtUtc datetime2(0), PlannedEndAtUtc datetime2(0), SessionMode varchar(10));
    INSERT INTO @OutOpen313 EXEC dbo.usp_OpenSession @TableId = @T_313A, @StaffId = @TestStaffId, @SessionMode = 'Open';
    DECLARE @S_313 int = (SELECT SessionId FROM @OutOpen313);

    -- Transfer to table B (capturing result set to prevent leaking)
    DECLARE @DummyTransfer313 TABLE (SessionId int, OldTableId int, NewTableId int, NewTableCode nvarchar(20), NewHourlyRate decimal(18,2), TransferAtUtc datetime2(0));
    INSERT INTO @DummyTransfer313 EXEC dbo.usp_TransferSession @SessionId = @S_313, @NewTableId = @T_313B, @StaffId = @TestStaffId;

    -- Close session
    DECLARE @OutClose313 TABLE (
        Id int, StartAtUtc datetime2(0), EndAtUtc datetime2(0),
        HourlyRateSnapshot decimal(18,2), PlaytimeAmount decimal(18,2), Status varchar(20),
        BillingStartAtUtc datetime2(0), BillingEndAtUtc datetime2(0), SessionMode varchar(10)
    );
    INSERT INTO @OutClose313 EXEC dbo.usp_CloseSession @SessionId = @S_313, @StaffId = @TestStaffId;

    DECLARE @Cl_Status varchar(20), @Cl_Amount decimal(18,2);
    SELECT @Cl_Status = Status, @Cl_Amount = PlaytimeAmount FROM @OutClose313;

    IF @Cl_Status = 'Closed' AND @Cl_Amount IS NOT NULL
       AND EXISTS (SELECT 1 FROM dbo.BilliardTables WHERE Id = @T_313B AND Status = 'AwaitingPayment')
       AND NOT EXISTS (SELECT 1 FROM dbo.PlaySessionTableSegments WHERE SessionId = @S_313 AND EndAtUtc IS NULL)
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('usp_CloseSession', N'Đóng phiên sau khi chuyển bàn', 'PASS', 'Closed / AwaitingPayment / Segments closed', 'Closed / AwaitingPayment / Segments closed', N'Phiên Closed, bàn hiện tại AwaitingPayment, đóng mọi segment');
    ELSE
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('usp_CloseSession', N'Đóng phiên sau khi chuyển bàn', 'FAIL', 'Closed / AwaitingPayment / Segments closed', 'Mismatch', N'Trạng thái đóng bàn không đúng');

    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
    VALUES ('usp_CloseSession', N'Đóng phiên sau khi chuyển bàn', 'FAIL', 'Closed', CAST(ERROR_NUMBER() AS varchar(20)),
            CASE WHEN ERROR_NUMBER() = 3915
                 THEN N'Lỗi 3915: Có thể procedure đã ném lỗi bị che bởi INSERT-EXEC, hãy gọi bằng EXEC thường để xem lỗi thật'
                 ELSE N'Lỗi: [' + CAST(ERROR_NUMBER() AS nvarchar(20)) + N'] ' + ERROR_MESSAGE() + N' (Dòng ' + CAST(ERROR_LINE() AS nvarchar(20)) + N')'
            END);
END CATCH;

-- 3.14: Backward compatibility test for EXEC usp_OpenSession @TableId, @StaffId
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT INTO dbo.TableTypes (Name, HourlyRate) VALUES (N'ZZ_Pool_314', 100000);
    DECLARE @TT_314 int = SCOPE_IDENTITY();
    INSERT INTO dbo.BilliardTables (TableCode, TableTypeId, Status) VALUES (N'ZZ-314', @TT_314, 'Available');
    DECLARE @T_314 int = SCOPE_IDENTITY();

    DECLARE @OutCompatOpen TABLE (SessionId int, BillingStartAtUtc datetime2(0), PlannedEndAtUtc datetime2(0), SessionMode varchar(10));
    -- Calling with only 2 parameters as in V1
    INSERT INTO @OutCompatOpen EXEC dbo.usp_OpenSession @TableId = @T_314, @StaffId = @TestStaffId;
    DECLARE @S_314 int = (SELECT SessionId FROM @OutCompatOpen);

    DECLARE @OutCompatClose TABLE (
        Id int, StartAtUtc datetime2(0), EndAtUtc datetime2(0),
        HourlyRateSnapshot decimal(18,2), PlaytimeAmount decimal(18,2), Status varchar(20),
        BillingStartAtUtc datetime2(0), BillingEndAtUtc datetime2(0), SessionMode varchar(10)
    );
    -- Calling with only 2 parameters as in V1
    INSERT INTO @OutCompatClose EXEC dbo.usp_CloseSession @SessionId = @S_314, @StaffId = @TestStaffId;

    IF (SELECT Status FROM @OutCompatClose) = 'Closed'
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('Compatibility', N'Tương thích ngược lệnh gọi Open/Close 2 tham số', 'PASS', 'Closed', 'Closed', N'EXEC usp_OpenSession & usp_CloseSession 2 tham số chạy hoàn hảo');
    ELSE
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('Compatibility', N'Tương thích ngược lệnh gọi Open/Close 2 tham số', 'FAIL', 'Closed', 'Mismatch', N'Không đóng được phiên');

    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
    VALUES ('Compatibility', N'Tương thích ngược lệnh gọi Open/Close 2 tham số', 'FAIL', 'Closed', CAST(ERROR_NUMBER() AS varchar(20)),
            CASE WHEN ERROR_NUMBER() = 3915
                 THEN N'Lỗi 3915: Có thể procedure đã ném lỗi bị che bởi INSERT-EXEC, hãy gọi bằng EXEC thường để xem lỗi thật'
                 ELSE N'Lỗi: [' + CAST(ERROR_NUMBER() AS nvarchar(20)) + N'] ' + ERROR_MESSAGE() + N' (Dòng ' + CAST(ERROR_LINE() AS nvarchar(20)) + N')'
            END);
END CATCH;

-- 3.15: Mở Timed 60 phút: PlannedEndAtUtc = BillingStartAtUtc + 60m và DATEDIFF(MINUTE, BillingStartAtUtc, PlannedEndAtUtc) = 60
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT INTO dbo.TableTypes (Name, HourlyRate) VALUES (N'ZZ_Pool_315', 100000);
    DECLARE @TT_315 int = SCOPE_IDENTITY();
    INSERT INTO dbo.BilliardTables (TableCode, TableTypeId, Status) VALUES (N'ZZ-315', @TT_315, 'Available');
    DECLARE @T_315 int = SCOPE_IDENTITY();

    DECLARE @OutTimed315 TABLE (SessionId int, BillingStartAtUtc datetime2(0), PlannedEndAtUtc datetime2(0), SessionMode varchar(10));
    INSERT INTO @OutTimed315 EXEC dbo.usp_OpenSession @TableId = @T_315, @StaffId = @TestStaffId, @SessionMode = 'Timed', @PlannedMinutes = 60;

    DECLARE @S_315 int, @BillStart_315 datetime2(0), @Planned_315 datetime2(0);
    SELECT @S_315 = SessionId, @BillStart_315 = BillingStartAtUtc, @Planned_315 = PlannedEndAtUtc FROM @OutTimed315;

    IF @S_315 IS NOT NULL
       AND @Planned_315 = DATEADD(minute, 60, @BillStart_315)
       AND DATEDIFF(minute, @BillStart_315, @Planned_315) = 60
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('usp_OpenSession', N'Timed 60 phút: PlannedEnd = BillingStart + 60m', 'PASS', 'Diff = 60m', 'Diff = 60m', N'PlannedEndAtUtc bằng BillingStartAtUtc + 60 phút và khoảng cách đúng 60 phút');
    ELSE
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('usp_OpenSession', N'Timed 60 phút: PlannedEnd = BillingStart + 60m', 'FAIL', 'Diff = 60m', 'Mismatch', N'PlannedEndAtUtc không khớp BillingStartAtUtc + 60 phút');

    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
    VALUES ('usp_OpenSession', N'Timed 60 phút: PlannedEnd = BillingStart + 60m', 'FAIL', 'Diff = 60m', CAST(ERROR_NUMBER() AS varchar(20)),
            CASE WHEN ERROR_NUMBER() = 3915
                 THEN N'Lỗi 3915: Có thể procedure đã ném lỗi bị che bởi INSERT-EXEC, hãy gọi bằng EXEC thường để xem lỗi thật'
                 ELSE N'Lỗi: [' + CAST(ERROR_NUMBER() AS nvarchar(20)) + N'] ' + ERROR_MESSAGE() + N' (Dòng ' + CAST(ERROR_LINE() AS nvarchar(20)) + N')'
            END);
END CATCH;

-- 3.16: PlannedEndAtUtc luôn nằm đúng mốc 15 phút (bằng fn_CeilTo15Min của chính nó)
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT INTO dbo.TableTypes (Name, HourlyRate) VALUES (N'ZZ_Pool_316', 100000);
    DECLARE @TT_316 int = SCOPE_IDENTITY();
    INSERT INTO dbo.BilliardTables (TableCode, TableTypeId, Status) VALUES (N'ZZ-316', @TT_316, 'Available');
    DECLARE @T_316 int = SCOPE_IDENTITY();

    DECLARE @OutTimed316 TABLE (SessionId int, BillingStartAtUtc datetime2(0), PlannedEndAtUtc datetime2(0), SessionMode varchar(10));
    INSERT INTO @OutTimed316 EXEC dbo.usp_OpenSession @TableId = @T_316, @StaffId = @TestStaffId, @SessionMode = 'Timed', @PlannedMinutes = 45;

    DECLARE @S_316 int, @Planned_316 datetime2(0);
    SELECT @S_316 = SessionId, @Planned_316 = PlannedEndAtUtc FROM @OutTimed316;

    IF @S_316 IS NOT NULL
       AND @Planned_316 IS NOT NULL
       AND @Planned_316 = dbo.fn_CeilTo15Min(@Planned_316)
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('usp_OpenSession', N'PlannedEndAtUtc luôn đúng mốc 15 phút', 'PASS', 'On 15m mark', 'On 15m mark', N'PlannedEndAtUtc bằng fn_CeilTo15Min của chính nó');
    ELSE
        INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
        VALUES ('usp_OpenSession', N'PlannedEndAtUtc luôn đúng mốc 15 phút', 'FAIL', 'On 15m mark', 'Mismatch', N'PlannedEndAtUtc không nằm trên mốc 15 phút');

    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT INTO @Results (Category, TestName, Status, Expected, Actual, Details)
    VALUES ('usp_OpenSession', N'PlannedEndAtUtc luôn đúng mốc 15 phút', 'FAIL', 'On 15m mark', CAST(ERROR_NUMBER() AS varchar(20)),
            CASE WHEN ERROR_NUMBER() = 3915
                 THEN N'Lỗi 3915: Có thể procedure đã ném lỗi bị che bởi INSERT-EXEC, hãy gọi bằng EXEC thường để xem lỗi thật'
                 ELSE N'Lỗi: [' + CAST(ERROR_NUMBER() AS nvarchar(20)) + N'] ' + ERROR_MESSAGE() + N' (Dòng ' + CAST(ERROR_LINE() AS nvarchar(20)) + N')'
            END);
END CATCH;

-------------------------------------------------------------------------------
-- PART 2: SUMMARY REPORT & DATA INTEGRITY VERIFICATION
-------------------------------------------------------------------------------
-- 1. Detailed Results Table
SELECT TestId, Category, TestName, Status, Expected, Actual, Details
  FROM @Results
 ORDER BY TestId;

-- 2. Single Consolidated Summary Table
SELECT
    (SELECT COUNT(*) FROM @Results) AS TotalTests,
    (SELECT COUNT(*) FROM @Results WHERE Status = 'PASS') AS PassCount,
    (SELECT COUNT(*) FROM @Results WHERE Status = 'FAIL') AS FailCount,
    (SELECT COUNT(*) FROM @Results WHERE Status = 'SKIP') AS SkipCount,
    COALESCE((
        SELECT STRING_AGG(CAST(TestId AS nvarchar(10)) + N': ' + TestName + N' (' + Status + N')', N'; ')
          FROM @Results WHERE Status IN ('FAIL', 'SKIP')
    ), N'None (All tests passed)') AS FailedOrSkippedList;

-- 3. Data Integrity and Transaction Verification Table
SELECT
    CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.BilliardTables WHERE TableCode LIKE N'ZZ-%')
              AND NOT EXISTS (SELECT 1 FROM dbo.TableTypes WHERE Name LIKE N'ZZ_%')
         THEN N'PASS (No ZZ- data remaining)'
         ELSE N'FAIL (ZZ- data leaked)'
    END AS Verification_NoZZData,

    CASE WHEN @@TRANCOUNT = 0
         THEN N'PASS (@@TRANCOUNT = 0)'
         ELSE N'FAIL (@@TRANCOUNT = ' + CAST(@@TRANCOUNT AS nvarchar(10)) + N')'
    END AS Verification_TranCount,

    CASE WHEN (SELECT COUNT(*) FROM dbo.PlaySessions) = @CountSessionsBefore
         THEN N'PASS (' + CAST(@CountSessionsBefore AS nvarchar(10)) + N' rows)'
         ELSE N'FAIL (Before: ' + CAST(@CountSessionsBefore AS nvarchar(10)) + N', After: ' + CAST((SELECT COUNT(*) FROM dbo.PlaySessions) AS nvarchar(10)) + N')'
    END AS Verification_PlaySessionsCount,

    CASE WHEN (SELECT COUNT(*) FROM dbo.PlaySessionTableSegments) = @CountSegmentsBefore
         THEN N'PASS (' + CAST(@CountSegmentsBefore AS nvarchar(10)) + N' rows)'
         ELSE N'FAIL (Before: ' + CAST(@CountSegmentsBefore AS nvarchar(10)) + N', After: ' + CAST((SELECT COUNT(*) FROM dbo.PlaySessionTableSegments) AS nvarchar(10)) + N')'
    END AS Verification_SegmentsCount;
GO

/*
===============================================================================
PART 3: HƯỚNG DẪN KIỂM THỬ ĐỒNG THỜI BẰNG TAY (MANUAL CONCURRENCY TESTING)
===============================================================================
Mục này KHÔNG chạy tự động. Dùng 2 cửa sổ Query trong SSMS (Window A và Window B)
kết nối vào BilliardDB để kiểm tra hành vi khóa giao dịch và concurrency.

Chuẩn bị dữ liệu mẫu cho kiểm thử đồng thời (chạy 1 lần ở Window A):
-------------------------------------------------------------------------------
USE BilliardDB;
GO
DECLARE @Staff nvarchar(450) = (SELECT TOP 1 Id FROM AspNetUsers WHERE IsActive = 1);
INSERT INTO dbo.TableTypes (Name, HourlyRate) VALUES (N'ZZ_Conc_Pool', 100000), (N'ZZ_Conc_Carom', 120000);
DECLARE @PId int = (SELECT Id FROM dbo.TableTypes WHERE Name = N'ZZ_Conc_Pool');
DECLARE @CId int = (SELECT Id FROM dbo.TableTypes WHERE Name = N'ZZ_Conc_Carom');
INSERT INTO dbo.BilliardTables (TableCode, TableTypeId, Status)
VALUES (N'ZZ-C01', @PId, 'Available'), (N'ZZ-C02', @CId, 'Available'), (N'ZZ-C03', @PId, 'Available');
PRINT N'Đã tạo bàn ZZ-C01, ZZ-C02, ZZ-C03';
GO

-------------------------------------------------------------------------------
KỊCH BẢN A: Cửa sổ A chuyển bàn chưa COMMIT, Cửa sổ B đóng cùng phiên
-------------------------------------------------------------------------------
1. Mở phiên trên bàn ZZ-C01:
   DECLARE @Staff nvarchar(450) = (SELECT TOP 1 Id FROM AspNetUsers WHERE IsActive = 1);
   DECLARE @T1 int = (SELECT Id FROM BilliardTables WHERE TableCode = N'ZZ-C01');
   EXEC usp_OpenSession @TableId = @T1, @StaffId = @Staff, @SessionMode = 'Timed', @PlannedMinutes = 60;
   -- Lấy SessionId vừa tạo (ví dụ @SId = 123)

2. Cửa sổ A (Transfer):
   BEGIN TRANSACTION;
   DECLARE @Staff nvarchar(450) = (SELECT TOP 1 Id FROM AspNetUsers WHERE IsActive = 1);
   DECLARE @T2 int = (SELECT Id FROM BilliardTables WHERE TableCode = N'ZZ-C02');
   EXEC usp_TransferSession @SessionId = <SessionId_123>, @NewTableId = @T2, @StaffId = @Staff;
   -- KHÔNG gõ COMMIT vội, giữ transaction mở!

3. Cửa sổ B (Close):
   DECLARE @Staff nvarchar(450) = (SELECT TOP 1 Id FROM AspNetUsers WHERE IsActive = 1);
   EXEC usp_CloseSession @SessionId = <SessionId_123>, @StaffId = @Staff;
   -- Kết quả mong đợi ở B: Cửa sổ B bị CHỜ (Blocked do A đang giữ UPDLOCK trên PlaySessions).

4. Cửa sổ A:
   COMMIT TRANSACTION;
   -- Kết quả mong đợi: Ngay khi A COMMIT, Cửa sổ B hết chờ và thực thi đóng phiên thành công
   -- (hoặc cập nhật đúng bàn đích ZZ-C02 sang AwaitingPayment).

-------------------------------------------------------------------------------
KỊCH BẢN B: Hai cửa sổ cùng chuyển 2 phiên khác nhau vào CÙNG 1 bàn đích
-------------------------------------------------------------------------------
1. Mở 2 phiên: Phiên S1 trên ZZ-C01, Phiên S2 trên ZZ-C03 (Bàn đích là ZZ-C02 đang Available).
2. Cửa sổ A:
   BEGIN TRANSACTION;
   EXEC usp_TransferSession @SessionId = <S1>, @NewTableId = <Id_ZZ-C02>, @StaffId = @Staff;
   -- Giữ chưa COMMIT.

3. Cửa sổ B:
   EXEC usp_TransferSession @SessionId = <S2>, @NewTableId = <Id_ZZ-C02>, @StaffId = @Staff;
   -- B bị block chờ khóa trên ZZ-C02.

4. Cửa sổ A:
   COMMIT TRANSACTION;
   -- Kết quả mong đợi: A thành công chuyển S1 vào ZZ-C02.
   -- B hết chờ và nhận mã lỗi 51613 (Target table does not exist or is not Available).

-------------------------------------------------------------------------------
KỊCH BẢN C: Hai cửa sổ cùng gia hạn 1 phiên Timed
-------------------------------------------------------------------------------
1. Phiên Timed S1 có PlannedEndAtUtc ban đầu = T0 (= BillingStartAtUtc + số phút đăng ký ban đầu).
2. Cửa sổ A:
   BEGIN TRANSACTION;
   EXEC usp_ExtendSession @SessionId = <S1>, @AddMinutes = 30, @StaffId = @Staff;
   -- Giữ chưa COMMIT.

3. Cửa sổ B:
   EXEC usp_ExtendSession @SessionId = <S1>, @AddMinutes = 30, @StaffId = @Staff;
   -- B bị block chờ A.

4. Cửa sổ A:
   COMMIT TRANSACTION;
   -- Kết quả mong đợi: A tăng thêm 30 phút. B tiếp tục chạy sau A và tăng thêm 30 phút nữa.
   -- Sau hai lần gia hạn 30 phút, tổng cộng PlannedEndAtUtc tăng đúng 60 phút so với ban đầu
   -- (= BillingStartAtUtc + số phút ban đầu + 60 phút), không bị mất cập nhật (lost update).

-------------------------------------------------------------------------------
DỌN DẸP DỮ LIỆU SAU KIỂM THỬ BẰNG TAY (Chạy ở Window A):
-------------------------------------------------------------------------------
USE BilliardDB;
GO
DELETE s FROM dbo.PlaySessionTableSegments s
JOIN dbo.BilliardTables t ON s.TableId = t.Id WHERE t.TableCode LIKE N'ZZ-C%';
DELETE s FROM dbo.PlaySessions s
JOIN dbo.BilliardTables t ON s.TableId = t.Id WHERE t.TableCode LIKE N'ZZ-C%';
DELETE FROM dbo.BilliardTables WHERE TableCode LIKE N'ZZ-C%';
DELETE FROM dbo.TableTypes WHERE Name LIKE N'ZZ_Conc%';
PRINT N'Đã dọn dẹp sạch toàn bộ dữ liệu kiểm thử ZZ-C%';
GO
===============================================================================
*/
