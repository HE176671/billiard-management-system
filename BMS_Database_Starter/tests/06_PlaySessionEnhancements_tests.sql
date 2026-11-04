/* Integration test script for 06_PlaySessionEnhancements.sql (Minute Billing & Booking Rules).
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
-- GROUP 1: fn_CalcPlaytimeAmount (Minute-based billing)
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

-- 1.3: 1 minute at 100.000/h => minimum 15 minutes = 25.000 VND right from the start
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
    IF @Amt_3 = 25000
        INSERT INTO @Results VALUES ('MinuteBilling', N'1 phút giá 100k/h => Tối thiểu 15p = 25.000đ', 'PASS', '25000.00', CAST(@Amt_3 AS varchar(30)), N'Tính tiền ngay từ phút đầu theo mức tối thiểu 15p');
    ELSE
        INSERT INTO @Results VALUES ('MinuteBilling', N'1 phút giá 100k/h => Tối thiểu 15p = 25.000đ', 'FAIL', '25000.00', CAST(@Amt_3 AS varchar(30)), N'Lệch giá trị');
    ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT INTO @Results VALUES ('MinuteBilling', N'1 phút giá 100k/h => Tối thiểu 15p = 25.000đ', 'FAIL', '25000.00', 'ERROR', ERROR_MESSAGE());
END CATCH;

-- 1.4: 4 minutes at 100.000/h (> 2m and < 15m) => minimum 15 minutes = 25.000 VND
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT INTO dbo.TableTypes (Name, HourlyRate) VALUES (N'ZZ_Type_M4', 100000);
    DECLARE @TT_4 int = SCOPE_IDENTITY();
    INSERT INTO dbo.BilliardTables (TableCode, TableTypeId, Status) VALUES (N'ZZ-M4', @TT_4, 'InUse');
    DECLARE @T_4 int = SCOPE_IDENTITY();

    INSERT INTO dbo.PlaySessions (TableId, OpenedById, StartAtUtc, HourlyRateSnapshot)
    VALUES (@T_4, @TestStaffId, '2026-10-08T09:00:00', 100000);
    DECLARE @S_4 int = SCOPE_IDENTITY();
    INSERT INTO dbo.PlaySessionTableSegments (SessionId, TableId, HourlyRateSnapshot, StartAtUtc, EndAtUtc)
    VALUES (@S_4, @T_4, 100000, '2026-10-08T09:00:00', '2026-10-08T09:03:51');

    DECLARE @Amt_4 decimal(18,2) = dbo.fn_CalcPlaytimeAmount(@S_4, '2026-10-08T09:03:51');
    IF @Amt_4 = 25000
        INSERT INTO @Results VALUES ('MinuteBilling', N'3p51s giá 100k/h => Tối thiểu 15p = 25.000đ', 'PASS', '25000.00', CAST(@Amt_4 AS varchar(30)), N'Áp dụng mức tối thiểu 15 phút đầu');
    ELSE
        INSERT INTO @Results VALUES ('MinuteBilling', N'3p51s giá 100k/h => Tối thiểu 15p = 25.000đ', 'FAIL', '25000.00', CAST(@Amt_4 AS varchar(30)), N'Lệch giá trị');
    ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT INTO @Results VALUES ('MinuteBilling', N'3p51s giá 100k/h => Tối thiểu 15p = 25.000đ', 'FAIL', '25000.00', 'ERROR', ERROR_MESSAGE());
END CATCH;

-------------------------------------------------------------------------------
-- GROUP 2: Booking Rules (Max 7 days, Auto No-Show > 15m, Check-in & Table Selection)
-------------------------------------------------------------------------------
-- 2.1: Booking > 15 days ahead is rejected with 51105
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT INTO dbo.TableTypes (Name, HourlyRate) VALUES (N'ZZ_Type_B1', 100000);
    DECLARE @TT_B1 int = SCOPE_IDENTITY();
    INSERT INTO dbo.BilliardTables (TableCode, TableTypeId, Status) VALUES (N'ZZ-B1', @TT_B1, 'Available');
    DECLARE @T_B1 int = SCOPE_IDENTITY();

    DECLARE @Future16Days datetime2(0) = DATEADD(day, 16, SYSUTCDATETIME());
    EXEC dbo.usp_CreateBooking @CustomerId = @TestCustomerId, @TableId = @T_B1, @StartAtUtc = @Future16Days;

    INSERT INTO @Results VALUES ('Booking', N'Chặn đặt bàn quá 15 ngày (51105)', 'FAIL', '51105', 'No Error', N'Không chặn khi đặt trước 16 ngày');
    ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    DECLARE @Err_B1 int = ERROR_NUMBER();
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    IF @Err_B1 = 51105
        INSERT INTO @Results VALUES ('Booking', N'Chặn đặt bàn quá 15 ngày (51105)', 'PASS', '51105', CAST(@Err_B1 AS varchar(20)), N'Chặn đúng giới hạn 15 ngày');
    ELSE
        INSERT INTO @Results VALUES ('Booking', N'Chặn đặt bàn quá 15 ngày (51105)', 'FAIL', '51105', CAST(@Err_B1 AS varchar(20)), ERROR_MESSAGE());
END CATCH;

-- 2.2: Auto-expire booking > 15 minutes late to NoShow
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT INTO dbo.TableTypes (Name, HourlyRate) VALUES (N'ZZ_Type_B2', 100000);
    DECLARE @TT_B2 int = SCOPE_IDENTITY();
    INSERT INTO dbo.BilliardTables (TableCode, TableTypeId, Status) VALUES (N'ZZ-B2', @TT_B2, 'Available');
    DECLARE @T_B2 int = SCOPE_IDENTITY();

    DECLARE @Past20Min datetime2(0) = DATEADD(minute, -20, SYSUTCDATETIME());
    INSERT INTO dbo.Bookings (CustomerId, TableId, StartAtUtc, EndAtUtc, Status)
    VALUES (@TestCustomerId, @T_B2, @Past20Min, DATEADD(hour, 2, @Past20Min), 'Confirmed');
    DECLARE @B_2 int = SCOPE_IDENTITY();

    EXEC dbo.usp_AutoExpireBookings;

    DECLARE @Status_B2 varchar(20) = (SELECT Status FROM dbo.Bookings WHERE Id = @B_2);
    IF @Status_B2 = 'NoShow'
        INSERT INTO @Results VALUES ('Booking', N'Tự động chuyển NoShow khi quá 15 phút', 'PASS', 'NoShow', @Status_B2, N'Quá 20 phút tự chuyển sang NoShow');
    ELSE
        INSERT INTO @Results VALUES ('Booking', N'Tự động chuyển NoShow khi quá 15 phút', 'FAIL', 'NoShow', @Status_B2, N'Không tự chuyển trạng thái');
    ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT INTO @Results VALUES ('Booking', N'Tự động chuyển NoShow khi quá 15 phút', 'FAIL', 'NoShow', 'ERROR', ERROR_MESSAGE());
END CATCH;

-- 2.3: Block walk-in open session within 5 minutes before a Confirmed booking (51407)
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT INTO dbo.TableTypes (Name, HourlyRate) VALUES (N'ZZ_Type_B3', 100000);
    DECLARE @TT_B3 int = SCOPE_IDENTITY();
    INSERT INTO dbo.BilliardTables (TableCode, TableTypeId, Status) VALUES (N'ZZ-B3', @TT_B3, 'Available');
    DECLARE @T_B3 int = SCOPE_IDENTITY();

    DECLARE @In3Min datetime2(0) = DATEADD(minute, 3, SYSUTCDATETIME());
    INSERT INTO dbo.Bookings (CustomerId, TableId, StartAtUtc, EndAtUtc, Status)
    VALUES (@TestCustomerId, @T_B3, @In3Min, DATEADD(hour, 2, @In3Min), 'Confirmed');

    EXEC dbo.usp_OpenSession @TableId = @T_B3, @StaffId = @TestStaffId;

    INSERT INTO @Results VALUES ('Booking', N'Chặn mở bàn khách lẻ khi còn < 5 phút tới giờ booking (51407)', 'FAIL', '51407', 'No Error', N'Không chặn');
    ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    DECLARE @Err_B3 int = ERROR_NUMBER();
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    IF @Err_B3 = 51407
        INSERT INTO @Results VALUES ('Booking', N'Chặn mở bàn khách lẻ khi còn < 5 phút tới giờ booking (51407)', 'PASS', '51407', CAST(@Err_B3 AS varchar(20)), N'Đảm bảo bàn trống trước giờ booking 5 phút');
    ELSE
        INSERT INTO @Results VALUES ('Booking', N'Chặn mở bàn khách lẻ khi còn < 5 phút tới giờ booking (51407)', 'FAIL', '51407', CAST(@Err_B3 AS varchar(20)), ERROR_MESSAGE());
END CATCH;

-- 2.4: Staff Check-in and assign another Available table to open session
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT INTO dbo.TableTypes (Name, HourlyRate) VALUES (N'ZZ_Type_B4', 100000);
    DECLARE @TT_B4 int = SCOPE_IDENTITY();
    INSERT INTO dbo.BilliardTables (TableCode, TableTypeId, Status) VALUES (N'ZZ-B4A', @TT_B4, 'InUse');
    DECLARE @T_B4A int = SCOPE_IDENTITY();
    INSERT INTO dbo.BilliardTables (TableCode, TableTypeId, Status) VALUES (N'ZZ-B4B', @TT_B4, 'Available');
    DECLARE @T_B4B int = SCOPE_IDENTITY();

    DECLARE @StartNow datetime2(0) = DATEADD(minute, -2, SYSUTCDATETIME());
    INSERT INTO dbo.Bookings (CustomerId, TableId, StartAtUtc, EndAtUtc, Status)
    VALUES (@TestCustomerId, @T_B4A, @StartNow, DATEADD(hour, 2, @StartNow), 'Confirmed');
    DECLARE @B_4 int = SCOPE_IDENTITY();

    -- Check-in succeeds even when T_B4A is still InUse
    EXEC dbo.usp_CheckInBooking @BookingId = @B_4, @StaffId = @TestStaffId;

    -- Staff assigns available table T_B4B to this CheckedIn booking
    EXEC dbo.usp_OpenSession @TableId = @T_B4B, @StaffId = @TestStaffId, @BookingId = @B_4;

    DECLARE @NewBookingTable int = (SELECT TableId FROM dbo.Bookings WHERE Id = @B_4);
    IF @NewBookingTable = @T_B4B
        INSERT INTO @Results VALUES ('Booking', N'Sau Check-in nhân viên chọn bàn trống khác và mở phiên', 'PASS', CAST(@T_B4B AS varchar(20)), CAST(@NewBookingTable AS varchar(20)), N'Check-in và phân bàn thành công');
    ELSE
        INSERT INTO @Results VALUES ('Booking', N'Sau Check-in nhân viên chọn bàn trống khác và mở phiên', 'FAIL', CAST(@T_B4B AS varchar(20)), CAST(@NewBookingTable AS varchar(20)), N'Không cập nhật bàn mới');
    ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT INTO @Results VALUES ('Booking', N'Sau Check-in nhân viên chọn bàn trống khác và mở phiên', 'FAIL', 'PASS', 'ERROR', ERROR_MESSAGE());
END CATCH;

SELECT * FROM @Results;
GO
