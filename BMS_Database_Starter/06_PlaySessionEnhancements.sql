/* BMS V2 - Booking & Play Session Enhancements (06). SQL Server 2019+, database BilliardDB.
   Additive and re-runnable (idempotent) upgrade.
   Implements:
     1. Clean removal of legacy Timed mode / 15-minute block billing columns and objects if present.
     2. PlaySessionTableSegments table for tracking table transfers.
     3. Minute-based billing via dbo.fn_CalcPlaytimeAmount (only records StartAtUtc and EndAtUtc).
     4. Automatic 15-minute No-Show expiration via dbo.usp_AutoExpireBookings.
     5. Booking rules: max 7 days in advance, 15-minute hold, 5-minute buffer before booking start,
        check-in workflow handling all 8 business scenarios, and staff table assignment after check-in.
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

-- Step 2. Remove legacy Timed mode and 15-minute block billing objects/columns if they exist.
IF OBJECT_ID(N'dbo.usp_ExtendSession', N'P') IS NOT NULL
BEGIN
    DROP PROCEDURE dbo.usp_ExtendSession;
    PRINT N'Dropped legacy procedure dbo.usp_ExtendSession.';
END
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Session_Billing' AND parent_object_id = OBJECT_ID(N'dbo.PlaySessions'))
BEGIN
    ALTER TABLE dbo.PlaySessions DROP CONSTRAINT CK_Session_Billing;
    PRINT N'Dropped constraint CK_Session_Billing.';
END
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Session_Planned' AND parent_object_id = OBJECT_ID(N'dbo.PlaySessions'))
BEGIN
    ALTER TABLE dbo.PlaySessions DROP CONSTRAINT CK_Session_Planned;
    PRINT N'Dropped constraint CK_Session_Planned.';
END
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Session_Mode' AND parent_object_id = OBJECT_ID(N'dbo.PlaySessions'))
BEGIN
    ALTER TABLE dbo.PlaySessions DROP CONSTRAINT CK_Session_Mode;
    PRINT N'Dropped constraint CK_Session_Mode.';
END
GO

IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_Session_Mode' AND parent_object_id = OBJECT_ID(N'dbo.PlaySessions'))
BEGIN
    ALTER TABLE dbo.PlaySessions DROP CONSTRAINT DF_Session_Mode;
    PRINT N'Dropped default constraint DF_Session_Mode.';
END
GO

IF COL_LENGTH(N'dbo.PlaySessions', N'BillingEndAtUtc') IS NOT NULL
BEGIN
    ALTER TABLE dbo.PlaySessions DROP COLUMN BillingEndAtUtc;
    PRINT N'Dropped legacy column PlaySessions.BillingEndAtUtc.';
END
GO

IF COL_LENGTH(N'dbo.PlaySessions', N'BillingStartAtUtc') IS NOT NULL
BEGIN
    ALTER TABLE dbo.PlaySessions DROP COLUMN BillingStartAtUtc;
    PRINT N'Dropped legacy column PlaySessions.BillingStartAtUtc.';
END
GO

IF COL_LENGTH(N'dbo.PlaySessions', N'PlannedEndAtUtc') IS NOT NULL
BEGIN
    ALTER TABLE dbo.PlaySessions DROP COLUMN PlannedEndAtUtc;
    PRINT N'Dropped legacy column PlaySessions.PlannedEndAtUtc.';
END
GO

IF COL_LENGTH(N'dbo.PlaySessions', N'SessionMode') IS NOT NULL
BEGIN
    ALTER TABLE dbo.PlaySessions DROP COLUMN SessionMode;
    PRINT N'Dropped legacy column PlaySessions.SessionMode.';
END
GO

IF OBJECT_ID(N'dbo.fn_CeilTo15Min', N'FN') IS NOT NULL
BEGIN
    DROP FUNCTION dbo.fn_CeilTo15Min;
    PRINT N'Dropped legacy function dbo.fn_CeilTo15Min.';
END
GO

-- Step 3. Table PlaySessionTableSegments: one row per table a session used (for table transfers).
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

-- Step 4. dbo.fn_CalcPlaytimeAmount: Minute-based billing (records only StartAtUtc and EndAtUtc).
/* dbo.fn_CalcPlaytimeAmount(@SessionId, @AtUtc)
   Calculates total playtime charge in whole VND based on actual minutes played per table segment.
   Each segment's elapsed seconds are converted to minutes (rounded up to the nearest whole minute:
   (seconds + 59) / 60). If total minutes across all segments is 0 (e.g. closed in the same second),
   minimum 1 minute on the latest segment is billed.
   Formula per segment: ROUND(BilledMinutes * HourlyRateSnapshot / 60.0, 0).
*/
CREATE OR ALTER FUNCTION dbo.fn_CalcPlaytimeAmount (@SessionId int, @AtUtc datetime2(0))
RETURNS decimal(18,2)
AS
BEGIN
    IF @SessionId IS NULL OR @AtUtc IS NULL RETURN NULL;
    DECLARE @SegmentCount int, @TotalMinutes bigint, @Amount decimal(18,2), @LastRate decimal(18,2);

    SELECT @SegmentCount = COUNT(*),
           @TotalMinutes = SUM(x.BilledMinutes),
           @Amount = SUM(ROUND(CONVERT(decimal(18,2), x.BilledMinutes) * g.HourlyRateSnapshot / 60.0, 0))
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
    IF @TotalMinutes > 0 RETURN @Amount;

    SELECT TOP (1) @LastRate = g.HourlyRateSnapshot
      FROM dbo.PlaySessionTableSegments g
     WHERE g.SessionId = @SessionId
     ORDER BY g.StartAtUtc DESC, g.Id DESC;
    RETURN CONVERT(decimal(18,2), ROUND(CONVERT(decimal(18,2), 1) * @LastRate / 60.0, 0));
END;
GO

-- Step 5. dbo.usp_AutoExpireBookings: Automatically transition Confirmed bookings > 15 minutes past StartAtUtc to NoShow.
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

-- Step 6. dbo.usp_CreateBooking:
--   1. Must be an active Customer account (51102).
--   2. StartAtUtc must be in the future (51101) and at most 7 days ahead (51105).
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
        -- Expire overdue bookings first so abandoned slots become free
        UPDATE dbo.Bookings
           SET Status = 'NoShow'
         WHERE Status = 'Confirmed'
           AND @Now > DATEADD(minute, 15, StartAtUtc);

        DECLARE @TableStatus varchar(20);
        SELECT @TableStatus = Status FROM dbo.BilliardTables WITH (UPDLOCK, HOLDLOCK) WHERE Id = @TableId;
        IF @TableStatus IS NULL OR @TableStatus IN ('Maintenance','Inactive')
            THROW 51103, N'Table is not bookable.', 1;

        -- If table is currently InUse, booking start must be at least 5 minutes in the future to allow closing/transferring
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

-- Step 7. dbo.usp_CancelBooking:
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

-- Step 8. dbo.usp_CheckInBooking:
--   Handles check-in scenarios:
--     51300: Booking not found
--     51301: Active Staff or Admin required
--     51302: Booking is Cancelled
--     51306: Booking was already CheckedIn or Completed
--     51307: Customer arrived > 15 minutes late (NoShow)
--     51303: Customer arrived early (> 15m before start) and @AllowEarly = 0
--   Note: Check-in records customer arrival (Status = 'CheckedIn') even if the reserved table is
--   currently finishing up; staff then selects/assigns the table to open the play session.
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

        -- Auto-expire bookings > 15 minutes past StartAtUtc
        UPDATE dbo.Bookings
           SET Status = 'NoShow'
         WHERE Status = 'Confirmed'
           AND @Now > DATEADD(minute, 15, StartAtUtc);

        DECLARE @TableId int, @LockedId int, @Status varchar(20), @Start datetime2(0), @End datetime2(0);
        SELECT @TableId = TableId FROM dbo.Bookings WHERE Id = @BookingId;
        IF @TableId IS NULL
            THROW 51300, N'Booking not found.', 1;

        SELECT @LockedId = Id FROM dbo.BilliardTables WITH (UPDLOCK, HOLDLOCK) WHERE Id = @TableId;
        SELECT @Status = Status, @Start = StartAtUtc, @End = EndAtUtc
          FROM dbo.Bookings WITH (UPDLOCK) WHERE Id = @BookingId;

        IF @Status = 'Cancelled'
            THROW 51302, N'Booking has been cancelled. Cannot check in.', 1;
        IF @Status IN ('CheckedIn', 'Completed')
            THROW 51306, N'Booking has already been checked in.', 1;
        IF @Status = 'NoShow' OR @Now > DATEADD(minute, 15, @Start)
            THROW 51307, N'Booking exceeded the 15-minute hold window (No-Show).', 1;
        IF @Status <> 'Confirmed'
            THROW 51302, N'Only a Confirmed booking can be checked in.', 1;

        -- Early arrival check: if > 15 minutes before start and staff hasn't explicitly enabled @AllowEarly
        IF @AllowEarly = 0 AND @Now < DATEADD(minute, -15, @Start)
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

-- Step 9. dbo.usp_OpenSession:
--   - Unlimited playtime (no Timed mode / PlannedMinutes).
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

        -- Auto-expire overdue bookings (> 15m late) so tables are not blocked by no-shows
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

            -- Ensure target table is not reserved right now by a DIFFERENT booking (within 5-min buffer)
            IF EXISTS (SELECT 1 FROM dbo.Bookings WHERE TableId = @TableId AND Id <> @BookingId
                AND ((Status = 'CheckedIn' AND NOT EXISTS (SELECT 1 FROM dbo.PlaySessions s WHERE s.BookingId = Bookings.Id))
                     OR (Status = 'Confirmed' AND DATEADD(minute, -5, StartAtUtc) <= @Now AND DATEADD(minute, 15, StartAtUtc) >= @Now)))
                THROW 51407, N'Table is reserved for another current booking.', 1;

            -- If staff assigned a different available table after check-in, update Booking.TableId
            IF @BookingTable <> @TableId
                UPDATE dbo.Bookings SET TableId = @TableId WHERE Id = @BookingId;
        END
        ELSE
        BEGIN
            -- Walk-in session: block if table has a CheckedIn booking waiting or a Confirmed booking starting within 5 minutes
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

-- Step 10. dbo.usp_CloseSession:
--   Closes the open segment, calculates playtime amount by minute via fn_CalcPlaytimeAmount,
--   updates PlaySessions (StartAtUtc and EndAtUtc only), sets table to AwaitingPayment,
--   and marks linked booking as Completed.
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
        DECLARE @TableId int, @LockedId int, @Status varchar(20), @Start datetime2(0),
            @BookingId int, @Now datetime2(0) = SYSUTCDATETIME(),
            @Amount decimal(18,2);

        SELECT @Status = Status, @TableId = TableId, @Start = StartAtUtc, @BookingId = BookingId
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

-- Step 11. dbo.usp_TransferSession:
--   Moves an Active session to another Available table (e.g. when vacating a table 5 minutes before a booking).
--   Also supports sessions opened from a booking (updates Bookings.TableId to the new table).
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

        SET @FirstId = CASE WHEN @NewTableId IS NULL OR @OldTableId < @NewTableId
                            THEN @OldTableId ELSE @NewTableId END;
        SET @SecondId = CASE WHEN @NewTableId IS NULL THEN NULL
                             WHEN @OldTableId < @NewTableId THEN @NewTableId
                             ELSE @OldTableId END;
        SELECT @LockedId = Id FROM dbo.BilliardTables WITH (UPDLOCK, HOLDLOCK) WHERE Id = @FirstId;
        IF @SecondId IS NOT NULL
            SELECT @LockedId = Id FROM dbo.BilliardTables WITH (UPDLOCK, HOLDLOCK) WHERE Id = @SecondId;

        SELECT @NewStatus = t.Status, @NewRate = ty.HourlyRate, @NewCode = t.TableCode
          FROM dbo.BilliardTables t JOIN dbo.TableTypes ty ON ty.Id = t.TableTypeId
         WHERE t.Id = @NewTableId;
        IF @NewStatus IS NULL OR @NewStatus <> 'Available'
            THROW 51613, N'Target table does not exist or is not Available.', 1;
        IF EXISTS (SELECT 1 FROM dbo.PlaySessions WHERE TableId = @NewTableId AND Status = 'Active')
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

-- Step 12. Read-only verification.
SET NOEXEC OFF;
GO
SELECT DB_NAME() AS DatabaseName,
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.PlaySessions')
        AND name IN (N'SessionMode', N'PlannedEndAtUtc', N'BillingStartAtUtc', N'BillingEndAtUtc')) AS LegacyColumns_Expected0,
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
