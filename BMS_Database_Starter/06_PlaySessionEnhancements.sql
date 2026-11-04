/* BMS V2 - Booking & Play Session Enhancements (06). SQL Server 2019+, database BilliardDB.
   Additive and re-runnable (idempotent) upgrade.
   Implements:
     1. PlaySessionTableSegments table for tracking table transfers.
     2. Minute-based billing via dbo.fn_CalcPlaytimeAmount (records StartAtUtc and EndAtUtc).
     3. Automatic 15-minute No-Show expiration via dbo.usp_AutoExpireBookings.
     4. Booking rules: max 15 days in advance, 15-minute hold, 5-minute buffer before booking start,
        check-in workflow, and staff table assignment after check-in.
*/
USE [BilliardDB];
GO
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
GO

-- Step 1. Guard: wrong database or missing base objects.
SET NOEXEC OFF;
IF DB_NAME() <> N'BilliardDB'
   OR OBJECT_ID(N'dbo.PlaySessions', N'U') IS NULL
   OR OBJECT_ID(N'dbo.BilliardTables', N'U') IS NULL
   OR OBJECT_ID(N'dbo.TableTypes', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Bookings', N'U') IS NULL
   OR OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NULL
   OR OBJECT_ID(N'dbo.AspNetUserRoles', N'U') IS NULL
   OR OBJECT_ID(N'dbo.AspNetRoles', N'U') IS NULL
BEGIN
    RAISERROR(N'06_PlaySessionEnhancements: wrong database or base objects are missing. Nothing was changed.', 16, 1);
    SET NOEXEC ON;
END
GO

-- Step 2. Table PlaySessionTableSegments: one row per table a session used (for table transfers).
IF OBJECT_ID(N'dbo.PlaySessionTableSegments', N'U') IS NULL
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
    PRINT N'Created dbo.PlaySessionTableSegments.';
END
ELSE PRINT N'dbo.PlaySessionTableSegments already exists.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_SessionSegments_OpenPerSession' AND object_id = OBJECT_ID(N'dbo.PlaySessionTableSegments'))
BEGIN
    CREATE UNIQUE INDEX UX_SessionSegments_OpenPerSession
        ON dbo.PlaySessionTableSegments(SessionId) WHERE EndAtUtc IS NULL;
    PRINT N'Created UX_SessionSegments_OpenPerSession.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SessionSegments_Session_Start' AND object_id = OBJECT_ID(N'dbo.PlaySessionTableSegments'))
BEGIN
    CREATE INDEX IX_SessionSegments_Session_Start
        ON dbo.PlaySessionTableSegments(SessionId, StartAtUtc);
    PRINT N'Created IX_SessionSegments_Session_Start.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SessionSegments_Table' AND object_id = OBJECT_ID(N'dbo.PlaySessionTableSegments'))
BEGIN
    CREATE INDEX IX_SessionSegments_Table ON dbo.PlaySessionTableSegments(TableId);
    PRINT N'Created IX_SessionSegments_Table.';
END
GO

-- Backfill segments for any session without a segment.
SET XACT_ABORT ON;
IF EXISTS (SELECT 1 FROM dbo.PlaySessions s WHERE NOT EXISTS (SELECT 1 FROM dbo.PlaySessionTableSegments g WHERE g.SessionId = s.Id))
BEGIN
    BEGIN TRY
        BEGIN TRANSACTION;
        INSERT dbo.PlaySessionTableSegments(SessionId, TableId, HourlyRateSnapshot, StartAtUtc, EndAtUtc)
        SELECT s.Id, s.TableId, s.HourlyRateSnapshot, s.StartAtUtc, s.EndAtUtc
          FROM dbo.PlaySessions s
         WHERE NOT EXISTS (SELECT 1 FROM dbo.PlaySessionTableSegments g WHERE g.SessionId = s.Id);
        COMMIT;
        PRINT N'Backfilled PlaySessionTableSegments.';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END
GO

-- Step 3. dbo.fn_CalcPlaytimeAmount: Minute-based billing (records only StartAtUtc and EndAtUtc).
/* dbo.fn_CalcPlaytimeAmount(@SessionId, @AtUtc)
   Calculates total playtime charge in VND following real-world billiard club rules:
   1. Minimum playtime block (from 0s up to 15 minutes): billed at minimum 15 minutes right from the start.
   2. Over 15 minutes: billed by actual minutes played per table segment ((seconds + 59) / 60).
   3. Cash rounding: rounded to the nearest 1,000 VND (ROUND(Amount, -3)).
*/
CREATE OR ALTER FUNCTION dbo.fn_CalcPlaytimeAmount (@SessionId int, @AtUtc datetime2(0))
RETURNS decimal(18,2)
AS
BEGIN
    IF @SessionId IS NULL OR @AtUtc IS NULL RETURN NULL;
    DECLARE @SegmentCount int,
            @TotalMinutes bigint,
            @RawAmount decimal(18,2),
            @LastRate decimal(18,2),
            @RoundedAmount decimal(18,2);

    SELECT @SegmentCount = COUNT(*),
           @TotalMinutes = SUM(x.BilledMinutes),
           @RawAmount = SUM(CONVERT(decimal(18,2), x.BilledMinutes) * g.HourlyRateSnapshot / 60.0)
      FROM dbo.PlaySessionTableSegments g
     CROSS APPLY (
         SELECT CASE
                    WHEN COALESCE(g.EndAtUtc, @AtUtc) > g.StartAtUtc
                    THEN (DATEDIFF_BIG(second, g.StartAtUtc, COALESCE(g.EndAtUtc, @AtUtc)) + 59) / 60
                    ELSE CONVERT(bigint, 0)
                END AS BilledMinutes
     ) x
     WHERE g.SessionId = @SessionId;

    IF @SegmentCount = 0 RETURN NULL;

    SELECT TOP (1) @LastRate = g.HourlyRateSnapshot
      FROM dbo.PlaySessionTableSegments g
     WHERE g.SessionId = @SessionId
     ORDER BY g.StartAtUtc DESC, g.Id DESC;

    -- Rule 1: Minimum charge of 15 minutes right from the start of the session
    IF @TotalMinutes < 15
        SET @RawAmount = @RawAmount + (CONVERT(decimal(18,2), 15 - @TotalMinutes) * @LastRate / 60.0 ) + 5000;

    -- Rule 2: Round to the nearest 1,000 VND for practical cash/QR payment
    SET @RoundedAmount = ROUND(@RawAmount, -3);
    IF @RoundedAmount < 1000
        SET @RoundedAmount = 1000;

    RETURN CONVERT(decimal(18,2), @RoundedAmount);
END;
GO

-- Step 4. dbo.usp_AutoExpireBookings: Automatically transition Confirmed bookings > 15 minutes past StartAtUtc to NoShow.
CREATE OR ALTER PROCEDURE dbo.usp_AutoExpireBookings
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Now datetime2(0) = SYSUTCDATETIME();
    UPDATE dbo.Bookings
       SET Status = 'NoShow'
     WHERE Status = 'Confirmed'
       AND @Now > DATEADD(minute, 15, StartAtUtc);
    SELECT @@ROWCOUNT AS ExpiredCount;
END;
GO

-- Step 5. dbo.usp_CreateBooking:
--   1. Must be an active Customer account (51102).
--   2. StartAtUtc must be in the future (51101) and at most 15 days ahead (51105).
--   3. EndAtUtc defaults to StartAtUtc + 2 hours if omitted (used for reservation window).
--   4. Table must be bookable (not Maintenance/Inactive, 51103) and have no overlapping booking
--      or active session within 5 minutes of StartAtUtc (51104).
CREATE OR ALTER PROCEDURE dbo.usp_CreateBooking
    @CustomerId nvarchar(450),
    @TableId int,
    @StartAtUtc datetime2(0),
    @EndAtUtc datetime2(0) = NULL,
    @Notes nvarchar(300) = NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @Now datetime2(0) = SYSUTCDATETIME();

    IF @EndAtUtc IS NULL
        SET @EndAtUtc = DATEADD(hour, 2, @StartAtUtc);

    IF @StartAtUtc IS NULL OR @StartAtUtc <= @Now OR @EndAtUtc <= @StartAtUtc
        THROW 51101, N'Choose a future start time and an end time after it.', 1;

    IF @StartAtUtc > DATEADD(day, 15, @Now)
        THROW 51105, N'Bookings can only be made up to 15 days in advance.', 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers u
        JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
        JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
        WHERE u.Id = @CustomerId AND u.IsActive = 1 AND r.Name = N'Customer')
        THROW 51102, N'An active Customer account is required.', 1;

    BEGIN TRY
        BEGIN TRANSACTION;
        UPDATE dbo.Bookings
           SET Status = 'NoShow'
         WHERE Status = 'Confirmed'
           AND @Now > DATEADD(minute, 15, StartAtUtc);

        DECLARE @TableStatus varchar(20);
        SELECT @TableStatus = Status FROM dbo.BilliardTables WITH (UPDLOCK, HOLDLOCK) WHERE Id = @TableId;
        IF @TableStatus IS NULL OR @TableStatus IN ('Maintenance','Inactive')
            THROW 51103, N'Table is not bookable.', 1;

        IF @TableStatus = 'InUse' AND @StartAtUtc < DATEADD(minute, 5, @Now)
            THROW 51104, N'Table is currently in use and requires at least 5 minutes buffer before booking time.', 1;

        IF EXISTS (SELECT 1 FROM dbo.Bookings
            WHERE TableId = @TableId AND Status IN ('Confirmed','CheckedIn')
              AND StartAtUtc < @EndAtUtc AND EndAtUtc > @StartAtUtc)
            THROW 51104, N'The requested time overlaps an existing booking.', 1;

        INSERT dbo.Bookings(CustomerId, TableId, StartAtUtc, EndAtUtc, Notes)
        VALUES (@CustomerId, @TableId, @StartAtUtc, @EndAtUtc, @Notes);
        DECLARE @Id int = CONVERT(int, SCOPE_IDENTITY());
        COMMIT;
        SELECT @Id AS BookingId;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END;
GO

-- Step 6. dbo.usp_CancelBooking:
CREATE OR ALTER PROCEDURE dbo.usp_CancelBooking
    @BookingId int, @ActorId nvarchar(450), @Reason nvarchar(300) = NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @Now datetime2(0) = SYSUTCDATETIME();
        UPDATE dbo.Bookings
           SET Status = 'NoShow'
         WHERE Status = 'Confirmed'
           AND @Now > DATEADD(minute, 15, StartAtUtc);

        DECLARE @TableId int, @LockedId int, @CustomerId nvarchar(450), @Status varchar(20), @Start datetime2(0);
        SELECT @TableId = TableId FROM dbo.Bookings WHERE Id = @BookingId;
        SELECT @LockedId = Id FROM dbo.BilliardTables WITH (UPDLOCK, HOLDLOCK) WHERE Id = @TableId;
        SELECT @CustomerId = CustomerId, @Status = Status, @Start = StartAtUtc
          FROM dbo.Bookings WITH (UPDLOCK) WHERE Id = @BookingId;

        IF @Status IS NULL OR @Status <> 'Confirmed'
            THROW 51201, N'Only a Confirmed booking can be cancelled.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers WHERE Id = @ActorId AND IsActive = 1)
            THROW 51202, N'Active account required.', 1;

        DECLARE @IsStaff bit = 0;
        IF EXISTS (SELECT 1 FROM dbo.AspNetUserRoles ur JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
            WHERE ur.UserId = @ActorId AND r.Name IN (N'Admin',N'Staff')) SET @IsStaff = 1;
        IF @IsStaff = 0 AND (@CustomerId <> @ActorId OR @Start <= @Now)
            THROW 51203, N'Customers can cancel only their own upcoming bookings.', 1;

        UPDATE dbo.Bookings SET Status = 'Cancelled', CancelledAtUtc = @Now,
            CancelledById = @ActorId, CancellationReason = @Reason WHERE Id = @BookingId;
        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END;
GO

-- Step 7. dbo.usp_CheckInBooking:
--   Handles check-in scenarios:
--     51300: Booking not found
--     51301: Active Staff or Admin required
--     51302: Booking is Cancelled
--     51306: Booking was already CheckedIn or Completed
--     51307: Customer arrived > 15 minutes late (NoShow)
--     51303: Customer arrived early (> 5m before start) and @AllowEarly = 0
CREATE OR ALTER PROCEDURE dbo.usp_CheckInBooking
    @BookingId int,
    @StaffId nvarchar(450),
    @AllowEarly bit = 0
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers u JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
        JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
        WHERE u.Id = @StaffId AND u.IsActive = 1 AND r.Name IN (N'Staff',N'Admin'))
        THROW 51301, N'Active Staff or Admin required.', 1;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @Now datetime2(0) = SYSUTCDATETIME();

        UPDATE dbo.Bookings
           SET Status = 'NoShow'
         WHERE Status = 'Confirmed'
           AND @Now > DATEADD(minute, 15, StartAtUtc);

        DECLARE @TableId int, @LockedId int, @Status varchar(20), @Start datetime2(0);
        SELECT @TableId = TableId FROM dbo.Bookings WHERE Id = @BookingId;
        IF @TableId IS NULL
            THROW 51300, N'Booking not found.', 1;

        SELECT @LockedId = Id FROM dbo.BilliardTables WITH (UPDLOCK, HOLDLOCK) WHERE Id = @TableId;
        SELECT @Status = Status, @Start = StartAtUtc
          FROM dbo.Bookings WITH (UPDLOCK) WHERE Id = @BookingId;

        IF @Status = 'Cancelled'
            THROW 51302, N'Booking has been cancelled. Cannot check in.', 1;
        IF @Status IN ('CheckedIn', 'Completed')
            THROW 51306, N'Booking has already been checked in.', 1;
        IF @Status = 'NoShow' OR @Now > DATEADD(minute, 15, @Start)
            THROW 51307, N'Booking exceeded the 15-minute hold window (No-Show).', 1;

        IF @AllowEarly = 0 AND @Now < DATEADD(minute, -5, @Start)
            THROW 51303, N'Outside the check-in window (customer arrived early; confirm early service if table is ready).', 1;

        UPDATE dbo.Bookings
           SET Status = 'CheckedIn',
               CheckedInAtUtc = @Now,
               CheckedInById = @StaffId
         WHERE Id = @BookingId;

        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END;
GO

-- Step 8. dbo.usp_OpenSession:
--   - Unlimited playtime (minute-based billing).
--   - Staff can open a table for a CheckedIn booking (either the originally booked table or another
--     Available table selected by staff after check-in).
--   - Enforces the 5-minute pre-booking rule: a table cannot be opened for walk-in customers if
--     another booking starts within 5 minutes (StartAtUtc - 5 min <= Now).
CREATE OR ALTER PROCEDURE dbo.usp_OpenSession
    @TableId int,
    @StaffId nvarchar(450),
    @BookingId int = NULL,
    @CustomerId nvarchar(450) = NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers u JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
        JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
        WHERE u.Id = @StaffId AND u.IsActive = 1 AND r.Name IN (N'Staff',N'Admin'))
        THROW 51401, N'Active Staff or Admin required.', 1;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @Now datetime2(0) = SYSUTCDATETIME();

        UPDATE dbo.Bookings
           SET Status = 'NoShow'
         WHERE Status = 'Confirmed'
           AND @Now > DATEADD(minute, 15, StartAtUtc);

        DECLARE @TableStatus varchar(20), @Rate decimal(18,2);
        SELECT @TableStatus = t.Status, @Rate = ty.HourlyRate
          FROM dbo.BilliardTables t WITH (UPDLOCK, HOLDLOCK)
          JOIN dbo.TableTypes ty ON ty.Id = t.TableTypeId WHERE t.Id = @TableId;

        IF @TableStatus IS NULL OR @TableStatus <> 'Available'
            THROW 51402, N'Table is not Available.', 1;
        IF EXISTS (SELECT 1 FROM dbo.PlaySessions WHERE TableId = @TableId AND Status = 'Active')
            THROW 51403, N'Table already has an Active session.', 1;

        IF @BookingId IS NOT NULL
        BEGIN
            DECLARE @BookingStatus varchar(20), @BookingTable int, @BookingCustomer nvarchar(450);
            SELECT @BookingStatus = Status, @BookingTable = TableId, @BookingCustomer = CustomerId
              FROM dbo.Bookings WITH (UPDLOCK) WHERE Id = @BookingId;

            IF @BookingStatus IS NULL OR @BookingStatus <> 'CheckedIn'
                THROW 51404, N'Use a checked-in booking to open a booking session.', 1;
            IF @CustomerId IS NOT NULL AND @CustomerId <> @BookingCustomer
                THROW 51405, N'Customer does not match the booking.', 1;
            SET @CustomerId = @BookingCustomer;
            IF EXISTS (SELECT 1 FROM dbo.PlaySessions WHERE BookingId = @BookingId)
                THROW 51406, N'This booking already has a session.', 1;

            IF EXISTS (SELECT 1 FROM dbo.Bookings WHERE TableId = @TableId AND Id <> @BookingId
                AND ((Status = 'CheckedIn' AND NOT EXISTS (SELECT 1 FROM dbo.PlaySessions s WHERE s.BookingId = Bookings.Id))
                     OR (Status = 'Confirmed' AND DATEADD(minute, -5, StartAtUtc) <= @Now AND DATEADD(minute, 15, StartAtUtc) >= @Now)))
                THROW 51407, N'Table is reserved for another current booking.', 1;

            IF @BookingTable <> @TableId
                UPDATE dbo.Bookings SET TableId = @TableId WHERE Id = @BookingId;
        END
        ELSE
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.Bookings WHERE TableId = @TableId
                AND ((Status = 'CheckedIn' AND NOT EXISTS (SELECT 1 FROM dbo.PlaySessions s WHERE s.BookingId = Bookings.Id))
                     OR (Status = 'Confirmed' AND DATEADD(minute, -5, StartAtUtc) <= @Now AND DATEADD(minute, 15, StartAtUtc) >= @Now)))
                THROW 51407, N'Table is reserved for a current or upcoming booking (within 5 minutes).', 1;
        END

        IF @CustomerId IS NOT NULL AND NOT EXISTS (
            SELECT 1 FROM dbo.AspNetUsers u JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
            JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
            WHERE u.Id = @CustomerId AND u.IsActive = 1 AND r.Name = N'Customer')
            THROW 51408, N'Customer account is inactive or invalid.', 1;

        INSERT dbo.PlaySessions(TableId, BookingId, CustomerId, OpenedById, StartAtUtc, HourlyRateSnapshot)
            VALUES (@TableId, @BookingId, @CustomerId, @StaffId, @Now, @Rate);
        DECLARE @Id int = CONVERT(int, SCOPE_IDENTITY());

        INSERT dbo.PlaySessionTableSegments(SessionId, TableId, HourlyRateSnapshot, StartAtUtc, EndAtUtc)
            VALUES (@Id, @TableId, @Rate, @Now, NULL);

        UPDATE dbo.BilliardTables SET Status = 'InUse' WHERE Id = @TableId;
        COMMIT;
        SELECT @Id AS SessionId, @Now AS StartAtUtc;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END;
GO

-- Step 9. dbo.usp_CloseSession:
--   Closes the open segment, calculates playtime amount by minute via fn_CalcPlaytimeAmount,
--   updates PlaySessions, sets table to AwaitingPayment, and marks linked booking as Completed.
CREATE OR ALTER PROCEDURE dbo.usp_CloseSession
    @SessionId int, @StaffId nvarchar(450)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers u JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
        JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
        WHERE u.Id = @StaffId AND u.IsActive = 1 AND r.Name IN (N'Staff',N'Admin'))
        THROW 51501, N'Active Staff or Admin required.', 1;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @TableId int, @LockedId int, @Status varchar(20),
            @BookingId int, @Now datetime2(0) = SYSUTCDATETIME(),
            @Amount decimal(18,2);

        SELECT @Status = Status, @TableId = TableId, @BookingId = BookingId
          FROM dbo.PlaySessions WITH (UPDLOCK, HOLDLOCK) WHERE Id = @SessionId;
        IF @Status IS NULL OR @Status <> 'Active'
            THROW 51502, N'Only an Active session can be closed.', 1;

        SELECT @LockedId = Id FROM dbo.BilliardTables WITH (UPDLOCK, HOLDLOCK) WHERE Id = @TableId;

        UPDATE dbo.PlaySessionTableSegments SET EndAtUtc = @Now
         WHERE SessionId = @SessionId AND EndAtUtc IS NULL;
        IF @@ROWCOUNT = 0
            THROW 51699, N'Active session has no open table segment.', 1;

        SET @Amount = dbo.fn_CalcPlaytimeAmount(@SessionId, @Now);

        UPDATE dbo.PlaySessions
           SET EndAtUtc = @Now,
               ClosedById = @StaffId,
               Status = 'Closed',
               PlaytimeAmount = @Amount
         WHERE Id = @SessionId;

        UPDATE dbo.BilliardTables SET Status = 'AwaitingPayment' WHERE Id = @TableId;
        IF @BookingId IS NOT NULL
            UPDATE dbo.Bookings SET Status = 'Completed' WHERE Id = @BookingId;

        COMMIT;
        SELECT Id, StartAtUtc, EndAtUtc, HourlyRateSnapshot, PlaytimeAmount, Status
          FROM dbo.PlaySessions WHERE Id = @SessionId;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END;
GO

-- Step 10. dbo.usp_TransferSession:
--   Moves an Active session to another Available table (e.g. vacating a table 5 minutes before a booking).
CREATE OR ALTER PROCEDURE dbo.usp_TransferSession
    @SessionId int, @NewTableId int, @StaffId nvarchar(450)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers u JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
        JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
        WHERE u.Id = @StaffId AND u.IsActive = 1 AND r.Name IN (N'Staff',N'Admin'))
        THROW 51611, N'Active Staff or Admin required.', 1;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @Now datetime2(0) = SYSUTCDATETIME(), @Status varchar(20), @OldTableId int,
            @BookingId int, @FirstId int, @SecondId int, @LockedId int,
            @NewStatus varchar(20), @NewRate decimal(18,2), @NewCode nvarchar(20);

        UPDATE dbo.Bookings
           SET Status = 'NoShow'
         WHERE Status = 'Confirmed'
           AND @Now > DATEADD(minute, 15, StartAtUtc);

        SELECT @Status = Status, @OldTableId = TableId, @BookingId = BookingId
          FROM dbo.PlaySessions WITH (UPDLOCK, HOLDLOCK) WHERE Id = @SessionId;
        IF @Status IS NULL OR @Status <> 'Active'
            THROW 51612, N'Only an Active session can be transferred.', 1;
        IF @NewTableId = @OldTableId
            THROW 51614, N'Target table is the current table of the session.', 1;

        SET @FirstId = CASE WHEN @OldTableId < @NewTableId THEN @OldTableId ELSE @NewTableId END;
        SET @SecondId = CASE WHEN @OldTableId < @NewTableId THEN @NewTableId ELSE @OldTableId END;
        SELECT @LockedId = Id FROM dbo.BilliardTables WITH (UPDLOCK, HOLDLOCK) WHERE Id = @FirstId;
        IF @SecondId IS NOT NULL
            SELECT @LockedId = Id FROM dbo.BilliardTables WITH (UPDLOCK, HOLDLOCK) WHERE Id = @SecondId;

        SELECT @NewStatus = t.Status, @NewRate = ty.HourlyRate, @NewCode = t.TableCode
          FROM dbo.BilliardTables t JOIN dbo.TableTypes ty ON ty.Id = t.TableTypeId
         WHERE t.Id = @NewTableId;
        IF @NewStatus IS NULL OR @NewStatus <> 'Available'
            THROW 51613, N'Target table does not exist or is not Available.', 1;
        IF EXISTS (SELECT 1 FROM dbo.Bookings WHERE TableId = @NewTableId
            AND ((Status = 'CheckedIn' AND NOT EXISTS (SELECT 1 FROM dbo.PlaySessions s WHERE s.BookingId = Bookings.Id))
                 OR (Status = 'Confirmed' AND DATEADD(minute, -5, StartAtUtc) <= @Now AND DATEADD(minute, 15, StartAtUtc) >= @Now)))
            THROW 51615, N'Target table is reserved for a current or upcoming booking (within 5 minutes).', 1;

        UPDATE dbo.PlaySessionTableSegments SET EndAtUtc = @Now
         WHERE SessionId = @SessionId AND EndAtUtc IS NULL;
        IF @@ROWCOUNT = 0
            THROW 51699, N'Active session has no open table segment.', 1;

        INSERT dbo.PlaySessionTableSegments(SessionId, TableId, HourlyRateSnapshot, StartAtUtc, EndAtUtc)
            VALUES (@SessionId, @NewTableId, @NewRate, @Now, NULL);

        UPDATE dbo.PlaySessions SET TableId = @NewTableId WHERE Id = @SessionId;
        IF @BookingId IS NOT NULL
            UPDATE dbo.Bookings SET TableId = @NewTableId WHERE Id = @BookingId;

        UPDATE dbo.BilliardTables SET Status = 'Available' WHERE Id = @OldTableId;
        UPDATE dbo.BilliardTables SET Status = 'InUse' WHERE Id = @NewTableId;
        COMMIT;
        SELECT @SessionId AS SessionId, @OldTableId AS OldTableId, @NewTableId AS NewTableId,
               @NewCode AS NewTableCode, @NewRate AS NewHourlyRate, @Now AS TransferAtUtc;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END;
GO

-- Step 11. Read-only verification.
SET NOEXEC OFF;
GO
SELECT DB_NAME() AS DatabaseName,
    (SELECT COUNT(*) FROM sys.objects WHERE schema_id = SCHEMA_ID(N'dbo')
        AND name = N'fn_CalcPlaytimeAmount') AS CalcFunction_Expected1,
    (SELECT COUNT(*) FROM sys.procedures WHERE schema_id = SCHEMA_ID(N'dbo')
        AND name IN (N'usp_AutoExpireBookings', N'usp_CreateBooking', N'usp_CancelBooking', N'usp_CheckInBooking', N'usp_OpenSession', N'usp_CloseSession', N'usp_TransferSession')) AS Procedures_Expected7,
    (SELECT COUNT(*) FROM dbo.PlaySessions s WHERE s.Status = 'Active'
        AND (SELECT COUNT(*) FROM dbo.PlaySessionTableSegments g
              WHERE g.SessionId = s.Id AND g.EndAtUtc IS NULL) <> 1) AS ActiveWithoutOneOpenSegment_Expected0,
    (SELECT COUNT(*) FROM dbo.PlaySessions s WHERE s.Status = 'Closed'
        AND EXISTS (SELECT 1 FROM dbo.PlaySessionTableSegments g
                     WHERE g.SessionId = s.Id AND g.EndAtUtc IS NULL)) AS ClosedWithOpenSegment_Expected0;
PRINT N'06_PlaySessionEnhancements finished successfully.';
GO
