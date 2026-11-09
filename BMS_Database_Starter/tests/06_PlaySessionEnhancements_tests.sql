/* Integration test script for 06_PlaySessionEnhancements.sql (Minute Billing, Combo Billing & Table Transfers).
   Database: BilliardDB. SQL Server 2019+.
   ALL TEST ROWS ARE ROLLED BACK. No existing business data is modified.
*/
USE [BilliardDB];
GO
SET NOCOUNT ON;
SET XACT_ABORT OFF;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
GO

DECLARE @Results TABLE (
    TestId int IDENTITY(1,1) PRIMARY KEY,
    Category varchar(30),
    TestName nvarchar(200),
    Status varchar(10),
    Expected nvarchar(200),
    Actual nvarchar(200),
    Details nvarchar(500)
);

DECLARE @TestStaffId nvarchar(450), @TestCustomerId nvarchar(450);
SELECT TOP (1) @TestStaffId = u.Id
  FROM dbo.AspNetUsers u
  JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
  JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
 WHERE u.IsActive = 1 AND r.Name IN (N'Staff', N'Admin')
 ORDER BY u.Id;

SELECT TOP (1) @TestCustomerId = u.Id
  FROM dbo.AspNetUsers u
  JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
  JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
 WHERE u.IsActive = 1 AND r.Name = N'Customer'
 ORDER BY u.Id;

-------------------------------------------------------------------------------
-- GROUP 1: fn_CalcPlaytimeAmount (Minute-based billing without Combo)
-------------------------------------------------------------------------------
-- 1.1: 30 minutes at 120.000/h => 60.000 VND
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT INTO dbo.TableTypes (Name, HourlyRate) VALUES (N'ZZ_Type_M1', 120000);
    DECLARE @TT_1 int = SCOPE_IDENTITY();
    INSERT INTO dbo.BilliardTables (TableCode, TableTypeId, Status) VALUES (N'ZZ-M1', @TT_1, 'InUse');
    DECLARE @T_1 int = SCOPE_IDENTITY();

    INSERT INTO dbo.PlaySessions (TableId, OpenedById, StartAtUtc, HourlyRateSnapshot)
    VALUES (@T_1, @TestStaffId, '2026-10-08T09:00:00', 120000);
    DECLARE @S_1 int = SCOPE_IDENTITY();
    INSERT INTO dbo.PlaySessionTableSegments (SessionId, TableId, HourlyRateSnapshot, StartAtUtc, EndAtUtc)
    VALUES (@S_1, @T_1, 120000, '2026-10-08T09:00:00', '2026-10-08T09:30:00');

    DECLARE @Amt_1 decimal(18,2) = dbo.fn_CalcPlaytimeAmount(@S_1, '2026-10-08T09:30:00');
    IF @Amt_1 = 60000
        INSERT INTO @Results VALUES ('MinuteBilling', N'30 phút giá 120k/h = 60.000đ', 'PASS', '60000.00', CAST(@Amt_1 AS varchar(30)), N'Tính đúng theo số phút thực tế');
    ELSE
        INSERT INTO @Results VALUES ('MinuteBilling', N'30 phút giá 120k/h = 60.000đ', 'FAIL', '60000.00', CAST(@Amt_1 AS varchar(30)), N'Lệch giá trị');
    ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT INTO @Results VALUES ('MinuteBilling', N'30 phút giá 120k/h = 60.000đ', 'FAIL', '60000.00', 'ERROR', ERROR_MESSAGE());
END CATCH;

-- 1.2: 17 minutes at 120.000/h => 34.000 VND (no 15-min block rounding!)
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT INTO dbo.TableTypes (Name, HourlyRate) VALUES (N'ZZ_Type_M2', 120000);
    DECLARE @TT_2 int = SCOPE_IDENTITY();
    INSERT INTO dbo.BilliardTables (TableCode, TableTypeId, Status) VALUES (N'ZZ-M2', @TT_2, 'InUse');
    DECLARE @T_2 int = SCOPE_IDENTITY();

    INSERT INTO dbo.PlaySessions (TableId, OpenedById, StartAtUtc, HourlyRateSnapshot)
    VALUES (@T_2, @TestStaffId, '2026-10-08T09:00:00', 120000);
    DECLARE @S_2 int = SCOPE_IDENTITY();
    INSERT INTO dbo.PlaySessionTableSegments (SessionId, TableId, HourlyRateSnapshot, StartAtUtc, EndAtUtc)
    VALUES (@S_2, @T_2, 120000, '2026-10-08T09:00:00', '2026-10-08T09:17:00');

    DECLARE @Amt_2 decimal(18,2) = dbo.fn_CalcPlaytimeAmount(@S_2, '2026-10-08T09:17:00');
    IF @Amt_2 = 34000
        INSERT INTO @Results VALUES ('MinuteBilling', N'17 phút giá 120k/h = 34.000đ', 'PASS', '34000.00', CAST(@Amt_2 AS varchar(30)), N'Không làm tròn 15p, tính đúng 17 phút');
    ELSE
        INSERT INTO @Results VALUES ('MinuteBilling', N'17 phút giá 120k/h = 34.000đ', 'FAIL', '34000.00', CAST(@Amt_2 AS varchar(30)), N'Lệch giá trị');
    ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT INTO @Results VALUES ('MinuteBilling', N'17 phút giá 120k/h = 34.000đ', 'FAIL', '34000.00', 'ERROR', ERROR_MESSAGE());
END CATCH;

-- 1.3: 1 minute at 100.000/h => actual 1 minute = 1.667 => rounded to 2.000 VND (no 15-min minimum)
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT INTO dbo.TableTypes (Name, HourlyRate) VALUES (N'ZZ_Type_M3', 100000);
    DECLARE @TT_3 int = SCOPE_IDENTITY();
    INSERT INTO dbo.BilliardTables (TableCode, TableTypeId, Status) VALUES (N'ZZ-M3', @TT_3, 'InUse');
    DECLARE @T_3 int = SCOPE_IDENTITY();

    INSERT INTO dbo.PlaySessions (TableId, OpenedById, StartAtUtc, HourlyRateSnapshot)
    VALUES (@T_3, @TestStaffId, '2026-10-08T09:00:00', 100000);
    DECLARE @S_3 int = SCOPE_IDENTITY();
    INSERT INTO dbo.PlaySessionTableSegments (SessionId, TableId, HourlyRateSnapshot, StartAtUtc, EndAtUtc)
    VALUES (@S_3, @T_3, 100000, '2026-10-08T09:00:00', '2026-10-08T09:01:00');

    DECLARE @Amt_3 decimal(18,2) = dbo.fn_CalcPlaytimeAmount(@S_3, '2026-10-08T09:01:00');
    IF @Amt_3 = 2000
        INSERT INTO @Results VALUES ('MinuteBilling', N'1 phút giá 100k/h => Tính đúng 1 phút = 2.000đ', 'PASS', '2000.00', CAST(@Amt_3 AS varchar(30)), N'Chơi bao lâu tính bấy nhiêu, không ép tối thiểu 15p');
    ELSE
        INSERT INTO @Results VALUES ('MinuteBilling', N'1 phút giá 100k/h => Tính đúng 1 phút = 2.000đ', 'FAIL', '2000.00', CAST(@Amt_3 AS varchar(30)), N'Lệch giá trị');
    ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT INTO @Results VALUES ('MinuteBilling', N'1 phút giá 100k/h => Tính đúng 1 phút = 2.000đ', 'FAIL', '2000.00', 'ERROR', ERROR_MESSAGE());
END CATCH;

-------------------------------------------------------------------------------
-- GROUP 2: Combo Billing & FIFO Overtime Across Table Transfers
-------------------------------------------------------------------------------
-- 2.1: Within Combo window (Combo 3h = 250k, played 90m) => 250.000 VND (no hourly fee)
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT INTO dbo.TableTypes (Name, HourlyRate) VALUES (N'ZZ_Type_C1', 100000);
    DECLARE @TT_C1 int = SCOPE_IDENTITY();
    INSERT INTO dbo.BilliardTables (TableCode, TableTypeId, Status) VALUES (N'ZZ-C1', @TT_C1, 'InUse');
    DECLARE @T_C1 int = SCOPE_IDENTITY();

    INSERT INTO dbo.Combos (Name, Price, PlaytimeHours, IsActive)
    VALUES (N'ZZ_Combo_3h', 250000, 3.0, 1);
    DECLARE @Combo_1 int = SCOPE_IDENTITY();

    INSERT INTO dbo.PlaySessions (TableId, OpenedById, StartAtUtc, HourlyRateSnapshot)
    VALUES (@T_C1, @TestStaffId, '2026-10-08T09:00:00', 100000);
    DECLARE @S_C1 int = SCOPE_IDENTITY();
    INSERT INTO dbo.PlaySessionTableSegments (SessionId, TableId, HourlyRateSnapshot, StartAtUtc, EndAtUtc)
    VALUES (@S_C1, @T_C1, 100000, '2026-10-08T09:00:00', NULL);

    EXEC dbo.usp_AddSessionCombo @SessionId = @S_C1, @ComboId = @Combo_1, @Quantity = 1, @StaffId = @TestStaffId;

    DECLARE @Amt_C1 decimal(18,2) = dbo.fn_CalcPlaytimeAmount(@S_C1, '2026-10-08T10:30:00');
    IF @Amt_C1 = 250000
        INSERT INTO @Results VALUES ('ComboBilling', N'Trong giờ Combo 3h (chơi 90p): chỉ tính tiền gói 250.000đ', 'PASS', '250000.00', CAST(@Amt_C1 AS varchar(30)), N'Không tính tiền giờ trong khoảng thời gian Combo');
    ELSE
        INSERT INTO @Results VALUES ('ComboBilling', N'Trong giờ Combo 3h (chơi 90p): chỉ tính tiền gói 250.000đ', 'FAIL', '250000.00', CAST(@Amt_C1 AS varchar(30)), N'Lệch giá trị');
    ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT INTO @Results VALUES ('ComboBilling', N'Trong giờ Combo 3h (chơi 90p): chỉ tính tiền gói 250.000đ', 'FAIL', '250000.00', 'ERROR', ERROR_MESSAGE());
END CATCH;

-- 2.2: Combo FIFO across table transfer + Overtime (Combo 2h = 120m = 180k; Seg 1: 90m @ 100k/h, Seg 2: 45m @ 120k/h => 15m overtime @ 120k/h = 30k => Total 210k)
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT INTO dbo.TableTypes (Name, HourlyRate) VALUES (N'ZZ_Type_C2A', 100000);
    DECLARE @TT_C2A int = SCOPE_IDENTITY();
    INSERT INTO dbo.TableTypes (Name, HourlyRate) VALUES (N'ZZ_Type_C2B', 120000);
    DECLARE @TT_C2B int = SCOPE_IDENTITY();

    INSERT INTO dbo.BilliardTables (TableCode, TableTypeId, Status) VALUES (N'ZZ-C2A', @TT_C2A, 'Available');
    DECLARE @T_C2A int = SCOPE_IDENTITY();
    INSERT INTO dbo.BilliardTables (TableCode, TableTypeId, Status) VALUES (N'ZZ-C2B', @TT_C2B, 'InUse');
    DECLARE @T_C2B int = SCOPE_IDENTITY();

    INSERT INTO dbo.Combos (Name, Price, PlaytimeHours, IsActive)
    VALUES (N'ZZ_Combo_2h', 180000, 2.0, 1);
    DECLARE @Combo_2 int = SCOPE_IDENTITY();

    INSERT INTO dbo.PlaySessions (TableId, OpenedById, StartAtUtc, HourlyRateSnapshot)
    VALUES (@T_C2B, @TestStaffId, '2026-10-08T09:00:00', 100000);
    DECLARE @S_C2 int = SCOPE_IDENTITY();

    -- Seg 1: 90m at 100k/h (covered by 120m combo, 30m remaining)
    INSERT INTO dbo.PlaySessionTableSegments (SessionId, TableId, HourlyRateSnapshot, StartAtUtc, EndAtUtc)
    VALUES (@S_C2, @T_C2A, 100000, '2026-10-08T09:00:00', '2026-10-08T10:30:00');
    -- Seg 2: 45m at 120k/h (first 30m covered by combo, remaining 15m billed at 120k/h = 30.000)
    INSERT INTO dbo.PlaySessionTableSegments (SessionId, TableId, HourlyRateSnapshot, StartAtUtc, EndAtUtc)
    VALUES (@S_C2, @T_C2B, 120000, '2026-10-08T10:30:00', '2026-10-08T11:15:00');

    EXEC dbo.usp_AddSessionCombo @SessionId = @S_C2, @ComboId = @Combo_2, @Quantity = 1, @StaffId = @TestStaffId;

    DECLARE @Amt_C2 decimal(18,2) = dbo.fn_CalcPlaytimeAmount(@S_C2, '2026-10-08T11:15:00');
    IF @Amt_C2 = 210000
        INSERT INTO @Results VALUES ('ComboBilling', N'Vượt giờ Combo + Chuyển bàn FIFO (180k + 15p*120k/h = 210.000đ)', 'PASS', '210000.00', CAST(@Amt_C2 AS varchar(30)), N'Trừ giờ Combo theo FIFO từng chặng và tính vượt giờ đúng đơn giá');
    ELSE
        INSERT INTO @Results VALUES ('ComboBilling', N'Vượt giờ Combo + Chuyển bàn FIFO (180k + 15p*120k/h = 210.000đ)', 'FAIL', '210000.00', CAST(@Amt_C2 AS varchar(30)), N'Lệch giá trị');
    ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT INTO @Results VALUES ('ComboBilling', N'Vượt giờ Combo + Chuyển bàn FIFO (180k + 15p*120k/h = 210.000đ)', 'FAIL', '210000.00', 'ERROR', ERROR_MESSAGE());
END CATCH;

-------------------------------------------------------------------------------
-- GROUP 3: Standardized Table Statuses (Available, InUse, Maintenance) & CloseSession
-------------------------------------------------------------------------------
-- 3.1: CK_Table_Status rejects 'AwaitingPayment' and 'Inactive'
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT INTO dbo.TableTypes (Name, HourlyRate) VALUES (N'ZZ_Type_ST1', 100000);
    DECLARE @TT_ST1 int = SCOPE_IDENTITY();
    INSERT INTO dbo.BilliardTables (TableCode, TableTypeId, Status) VALUES (N'ZZ-ST1', @TT_ST1, 'AwaitingPayment');
    ROLLBACK TRANSACTION;
    INSERT INTO @Results VALUES ('TableStatus', N'CK_Table_Status chặn trạng thái AwaitingPayment', 'FAIL', 'REJECTED', 'ALLOWED', N'Ràng buộc không chặn AwaitingPayment');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT INTO @Results VALUES ('TableStatus', N'CK_Table_Status chặn trạng thái AwaitingPayment', 'PASS', 'REJECTED', 'REJECTED', N'Chỉ cho phép Available, InUse, Maintenance');
END CATCH;

-- 3.2: usp_CloseSession closes session and immediately returns table Status to 'Available'
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT INTO dbo.TableTypes (Name, HourlyRate) VALUES (N'ZZ_Type_ST2', 120000);
    DECLARE @TT_ST2 int = SCOPE_IDENTITY();
    INSERT INTO dbo.BilliardTables (TableCode, TableTypeId, Status) VALUES (N'ZZ-ST2', @TT_ST2, 'Available');
    DECLARE @T_ST2 int = SCOPE_IDENTITY();

    EXEC dbo.usp_OpenSession @TableId = @T_ST2, @StaffId = @TestStaffId;

    DECLARE @OpenedSessionId int = (SELECT TOP (1) Id FROM dbo.PlaySessions WHERE TableId = @T_ST2 AND Status = 'Active' ORDER BY Id DESC);
    DECLARE @StatusWhilePlaying varchar(20) = (SELECT Status FROM dbo.BilliardTables WHERE Id = @T_ST2);

    EXEC dbo.usp_CloseSession @SessionId = @OpenedSessionId, @StaffId = @TestStaffId;

    DECLARE @StatusAfterClose varchar(20) = (SELECT Status FROM dbo.BilliardTables WHERE Id = @T_ST2);
    DECLARE @SessionStatusAfterClose varchar(20) = (SELECT Status FROM dbo.PlaySessions WHERE Id = @OpenedSessionId);

    IF @StatusWhilePlaying = 'InUse' AND @StatusAfterClose = 'Available' AND @SessionStatusAfterClose = 'Closed'
        INSERT INTO @Results VALUES ('TableStatus', N'usp_CloseSession chuyển bàn về Available & đóng phiên Closed', 'PASS', 'InUse->Available / Closed', @StatusWhilePlaying + '->' + @StatusAfterClose + ' / ' + @SessionStatusAfterClose, N'Không qua trạng thái AwaitingPayment');
    ELSE
        INSERT INTO @Results VALUES ('TableStatus', N'usp_CloseSession chuyển bàn về Available & đóng phiên Closed', 'FAIL', 'InUse->Available / Closed', @StatusWhilePlaying + '->' + @StatusAfterClose + ' / ' + @SessionStatusAfterClose, N'Sai trạng thái sau khi đóng phiên');
    ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT INTO @Results VALUES ('TableStatus', N'usp_CloseSession chuyển bàn về Available & đóng phiên Closed', 'FAIL', 'InUse->Available / Closed', 'ERROR', ERROR_MESSAGE());
END CATCH;

SELECT * FROM @Results;
GO
