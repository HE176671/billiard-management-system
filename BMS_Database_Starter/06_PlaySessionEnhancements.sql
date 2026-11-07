/* BMS V2 - Play Session & Combo Enhancements (06). SQL Server 2019+, database BilliardDB.
   Additive and re-runnable (idempotent) upgrade.
   Implements:
     1. Restores Booking procedures (usp_CreateBooking, usp_CancelBooking, usp_CheckInBooking)
        to the original starter specification and removes usp_AutoExpireBookings.
     2. PlaySessionTableSegments table for tracking table transfers.
     3. Minute-based & Combo-aware billing via dbo.fn_CalcPlaytimeAmount (FIFO combo minute deduction across table segments).
     4. Play session procedures: usp_OpenSession, usp_CloseSession, usp_TransferSession, and usp_AddSessionCombo.
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
   OR OBJECT_ID(N'dbo.Products', N'U') IS NULL
   OR OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NULL
   OR OBJECT_ID(N'dbo.AspNetUserRoles', N'U') IS NULL
   OR OBJECT_ID(N'dbo.AspNetRoles', N'U') IS NULL
BEGIN
    RAISERROR(N'06_PlaySessionEnhancements: wrong database or base objects are missing. Nothing was changed.', 16, 1);
    SET NOEXEC ON;
END
GO

-- Step 1b. Ensure Combo tables exist (Combos, ComboItems, SessionCombos, SessionComboItems).
IF OBJECT_ID(N'dbo.Combos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Combos (
        Id int IDENTITY NOT NULL CONSTRAINT PK_Combos PRIMARY KEY,
        Name nvarchar(100) NOT NULL CONSTRAINT UQ_Combos_Name UNIQUE,
        Price decimal(18,2) NOT NULL,
        PlaytimeHours decimal(5,2) NOT NULL,
        IsActive bit NOT NULL CONSTRAINT DF_Combo_Active DEFAULT 1,
        CONSTRAINT CK_Combo_Name CHECK (LEN(LTRIM(RTRIM(Name))) > 0),
        CONSTRAINT CK_Combo_Price CHECK (Price >= 0),
        CONSTRAINT CK_Combo_Playtime CHECK (PlaytimeHours > 0)
    );
    PRINT N'Created dbo.Combos.';
END
GO

IF OBJECT_ID(N'dbo.ComboItems', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ComboItems (
        ComboId int NOT NULL,
        ProductId int NOT NULL,
        Quantity int NOT NULL,
        CONSTRAINT PK_ComboItems PRIMARY KEY (ComboId, ProductId),
        CONSTRAINT FK_ComboItems_Combo FOREIGN KEY (ComboId) REFERENCES dbo.Combos(Id),
        CONSTRAINT FK_ComboItems_Product FOREIGN KEY (ProductId) REFERENCES dbo.Products(Id),
        CONSTRAINT CK_ComboItems_Quantity CHECK (Quantity > 0)
    );
    CREATE INDEX IX_ComboItems_ProductId ON dbo.ComboItems(ProductId);
    PRINT N'Created dbo.ComboItems.';
END
GO

IF OBJECT_ID(N'dbo.SessionCombos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SessionCombos (
        Id int IDENTITY NOT NULL CONSTRAINT PK_SessionCombos PRIMARY KEY,
        SessionId int NOT NULL,
        ComboId int NOT NULL,
        Quantity int NOT NULL,
        ComboNameSnapshot nvarchar(100) NOT NULL,
        UnitPriceSnapshot decimal(18,2) NOT NULL,
        PlaytimeHoursSnapshot decimal(5,2) NOT NULL,
        PurchasedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_SessionCombos_Purchased DEFAULT SYSUTCDATETIME(),
        CreatedById nvarchar(450) NOT NULL,
        CONSTRAINT FK_SessionCombos_Session FOREIGN KEY (SessionId) REFERENCES dbo.PlaySessions(Id),
        CONSTRAINT FK_SessionCombos_Combo FOREIGN KEY (ComboId) REFERENCES dbo.Combos(Id),
        CONSTRAINT FK_SessionCombos_CreatedBy FOREIGN KEY (CreatedById) REFERENCES dbo.AspNetUsers(Id),
        CONSTRAINT CK_SessionCombos_Quantity CHECK (Quantity > 0),
        CONSTRAINT CK_SessionCombos_Name CHECK (LEN(LTRIM(RTRIM(ComboNameSnapshot))) > 0),
        CONSTRAINT CK_SessionCombos_Price CHECK (UnitPriceSnapshot >= 0),
        CONSTRAINT CK_SessionCombos_Hours CHECK (PlaytimeHoursSnapshot > 0)
    );
    CREATE INDEX IX_SessionCombos_SessionId ON dbo.SessionCombos(SessionId);
    CREATE INDEX IX_SessionCombos_ComboId ON dbo.SessionCombos(ComboId);
    CREATE INDEX IX_SessionCombos_CreatedById ON dbo.SessionCombos(CreatedById);
    PRINT N'Created dbo.SessionCombos.';
END
GO

IF OBJECT_ID(N'dbo.SessionComboItems', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SessionComboItems (
        SessionComboId int NOT NULL,
        ProductId int NOT NULL,
        ProductNameSnapshot nvarchar(100) NOT NULL,
        QuantityPerCombo int NOT NULL,
        CONSTRAINT PK_SessionComboItems PRIMARY KEY (SessionComboId, ProductId),
        CONSTRAINT FK_SessionComboItems_Purchase FOREIGN KEY (SessionComboId) REFERENCES dbo.SessionCombos(Id),
        CONSTRAINT FK_SessionComboItems_Product FOREIGN KEY (ProductId) REFERENCES dbo.Products(Id),
        CONSTRAINT CK_SessionComboItems_Name CHECK (LEN(LTRIM(RTRIM(ProductNameSnapshot))) > 0),
        CONSTRAINT CK_SessionComboItems_Quantity CHECK (QuantityPerCombo > 0)
    );
    CREATE INDEX IX_SessionComboItems_ProductId ON dbo.SessionComboItems(ProductId);
    PRINT N'Created dbo.SessionComboItems.';
END
GO

-- Step 2. Remove custom auto-expire procedure and restore original starter Booking procedures.
DROP PROCEDURE IF EXISTS dbo.usp_AutoExpireBookings;
GO

CREATE OR ALTER PROCEDURE dbo.usp_CreateBooking
    @CustomerId nvarchar(450), @TableId int,
    @StartAtUtc datetime2(0), @EndAtUtc datetime2(0),
    @Notes nvarchar(300) = NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    IF @StartAtUtc IS NULL OR @EndAtUtc IS NULL OR @StartAtUtc <= SYSUTCDATETIME() OR @EndAtUtc <= @StartAtUtc
        THROW 51101, N'Choose a future start time and an end time after it.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers u
        JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
        JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
        WHERE u.Id = @CustomerId AND u.IsActive = 1 AND r.Name = N'Customer')
        THROW 51102, N'An active Customer account is required.', 1;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @TableStatus varchar(20);
        SELECT @TableStatus = Status FROM dbo.BilliardTables WITH (UPDLOCK, HOLDLOCK) WHERE Id = @TableId;
        IF @TableStatus IS NULL OR @TableStatus IN ('Maintenance','Inactive')
            THROW 51103, N'Table is not bookable.', 1;
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

CREATE OR ALTER PROCEDURE dbo.usp_CancelBooking
    @BookingId int, @ActorId nvarchar(450), @Reason nvarchar(300) = NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
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
            WHERE ur.UserId = @ActorId AND r.Name IN (N'Admin',N'Manager',N'Staff')) SET @IsStaff = 1;
        IF @IsStaff = 0 AND (@CustomerId <> @ActorId OR @Start <= SYSUTCDATETIME())
            THROW 51203, N'Customers can cancel only their own upcoming bookings.', 1;
        UPDATE dbo.Bookings SET Status = 'Cancelled', CancelledAtUtc = SYSUTCDATETIME(),
            CancelledById = @ActorId, CancellationReason = @Reason WHERE Id = @BookingId;
        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_CheckInBooking
    @BookingId int, @StaffId nvarchar(450)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers u JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
        JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
        WHERE u.Id = @StaffId AND u.IsActive = 1 AND r.Name IN (N'Staff',N'Manager',N'Admin'))
        THROW 51301, N'Active Staff or Admin required.', 1;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @TableId int, @TableStatus varchar(20), @Status varchar(20), @Start datetime2(0), @End datetime2(0);
        SELECT @TableId = TableId FROM dbo.Bookings WHERE Id = @BookingId;
        SELECT @TableStatus = Status FROM dbo.BilliardTables WITH (UPDLOCK, HOLDLOCK) WHERE Id = @TableId;
        SELECT @Status = Status, @Start = StartAtUtc, @End = EndAtUtc
          FROM dbo.Bookings WITH (UPDLOCK) WHERE Id = @BookingId;
        IF @Status IS NULL OR @Status <> 'Confirmed'
            THROW 51302, N'Only a Confirmed booking can be checked in.', 1;
        IF SYSUTCDATETIME() < DATEADD(minute,-15,@Start) OR SYSUTCDATETIME() >= @End
            THROW 51303, N'Outside the check-in window.', 1;
        IF @TableStatus <> 'Available'
            THROW 51304, N'Table is not ready for check-in.', 1;
        IF EXISTS (SELECT 1 FROM dbo.Bookings WHERE TableId = @TableId AND Status = 'CheckedIn')
            THROW 51305, N'Another customer has already checked in for this table.', 1;
        UPDATE dbo.Bookings SET Status = 'CheckedIn', CheckedInAtUtc = SYSUTCDATETIME(),
            CheckedInById = @StaffId WHERE Id = @BookingId;
        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END;
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

-- Step 4. dbo.fn_CalcPlaytimeAmount: Minute-based & Combo-aware billing.
/* dbo.fn_CalcPlaytimeAmount(@SessionId, @AtUtc)
   Calculates total table charge in VND (Combo package price + Overtime playtime charge):
   Case A - Session has NO purchased combos:
     1. Minimum playtime block (from 0s up to 15 minutes): billed at minimum 15 minutes right from the start.
     2. Over 15 minutes: billed by actual minutes played per table segment ((seconds + 59) / 60).
     3. Cash rounding: rounded to the nearest 1,000 VND (ROUND(Amount, -3), min 1,000 VND).
   Case B - Session HAS purchased combos (dbo.SessionCombos):
     1. Combo base charge = SUM(Quantity * UnitPriceSnapshot) is charged immediately.
     2. Total included combo minutes = ROUND(SUM(Quantity * PlaytimeHoursSnapshot * 60), 0).
     3. Included combo minutes are deducted from table segments in chronological order (FIFO).
     4. Any minutes played beyond the included combo minutes are billed at that segment's HourlyRateSnapshot
        (no 15-minute minimum for overtime), rounded to the nearest 1,000 VND.
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
            @RoundedAmount decimal(18,2),
            @ComboCount int = 0,
            @ComboAmount decimal(18,2) = 0,
            @ComboMinutes bigint = 0;

    SELECT @ComboCount = COUNT(*),
           @ComboAmount = COALESCE(SUM(CONVERT(decimal(18,2), sc.Quantity) * sc.UnitPriceSnapshot), 0),
           @ComboMinutes = COALESCE(CONVERT(bigint, ROUND(SUM(CONVERT(decimal(18,2), sc.Quantity) * sc.PlaytimeHoursSnapshot * 60.0), 0)), 0)
      FROM dbo.SessionCombos sc
     WHERE sc.SessionId = @SessionId;

    IF @ComboCount = 0
    BEGIN
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

        -- Rule 1: Minimum charge of 15 minutes right from the start of a non-combo session
        IF @TotalMinutes < 15
            SET @RawAmount = @RawAmount + (CONVERT(decimal(18,2), 15 - @TotalMinutes) * @LastRate / 60.0);

        -- Rule 2: Round to the nearest 1,000 VND for practical cash/QR payment
        SET @RoundedAmount = ROUND(@RawAmount, -3);
        IF @RoundedAmount < 1000
            SET @RoundedAmount = 1000;

        RETURN CONVERT(decimal(18,2), @RoundedAmount);
    END

    -- Combo-enabled calculation (FIFO deduction across table segments)
    DECLARE @SegTable TABLE (
        RowNum int IDENTITY(1,1) PRIMARY KEY,
        BilledMinutes bigint NOT NULL,
        HourlyRate decimal(18,2) NOT NULL
    );

    INSERT INTO @SegTable (BilledMinutes, HourlyRate)
    SELECT CASE
               WHEN COALESCE(g.EndAtUtc, @AtUtc) > g.StartAtUtc
               THEN (DATEDIFF_BIG(second, g.StartAtUtc, COALESCE(g.EndAtUtc, @AtUtc)) + 59) / 60
               ELSE CONVERT(bigint, 0)
           END,
           g.HourlyRateSnapshot
      FROM dbo.PlaySessionTableSegments g
     WHERE g.SessionId = @SessionId
     ORDER BY g.StartAtUtc ASC, g.Id ASC;

    IF NOT EXISTS (SELECT 1 FROM @SegTable) RETURN NULL;

    DECLARE @OvertimeRaw decimal(18,2) = 0,
            @OvertimeRounded decimal(18,2) = 0;

    ;WITH SegCumulative AS (
        SELECT s.RowNum,
               s.BilledMinutes,
               s.HourlyRate,
               SUM(s.BilledMinutes) OVER (ORDER BY s.RowNum ROWS UNBOUNDED PRECEDING) - s.BilledMinutes AS PrevMinutes,
               SUM(s.BilledMinutes) OVER (ORDER BY s.RowNum ROWS UNBOUNDED PRECEDING) AS EndMinutes
          FROM @SegTable s
    )
    SELECT @OvertimeRaw = COALESCE(SUM(
        CONVERT(decimal(18,2),
            CASE
                WHEN EndMinutes <= @ComboMinutes THEN CONVERT(bigint, 0)
                WHEN PrevMinutes >= @ComboMinutes THEN BilledMinutes
                ELSE EndMinutes - @ComboMinutes
            END
        ) * HourlyRate / 60.0
    ), 0)
      FROM SegCumulative;

    IF @OvertimeRaw > 0
    BEGIN
        SET @OvertimeRounded = ROUND(@OvertimeRaw, -3);
        IF @OvertimeRounded < 1000
            SET @OvertimeRounded = 1000;
    END

    RETURN CONVERT(decimal(18,2), @ComboAmount + @OvertimeRounded);
END;
GO

-- Step 5. dbo.usp_OpenSession:
--   Staff opens an Available table for walk-in or an optional Customer account.
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
        WHERE u.Id = @StaffId AND u.IsActive = 1 AND r.Name IN (N'Staff',N'Manager',N'Admin'))
        THROW 51401, N'Active Staff or Admin required.', 1;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @Now datetime2(0) = SYSUTCDATETIME();
        DECLARE @TableStatus varchar(20), @Rate decimal(18,2);

        SELECT @TableStatus = t.Status, @Rate = ty.HourlyRate
          FROM dbo.BilliardTables t WITH (UPDLOCK, HOLDLOCK)
          JOIN dbo.TableTypes ty ON ty.Id = t.TableTypeId WHERE t.Id = @TableId;

        IF @TableStatus IS NULL OR @TableStatus <> 'Available'
            THROW 51402, N'Table is not Available.', 1;
        IF EXISTS (SELECT 1 FROM dbo.PlaySessions WHERE TableId = @TableId AND Status = 'Active')
            THROW 51403, N'Table already has an Active session.', 1;

        IF @CustomerId IS NOT NULL AND NOT EXISTS (
            SELECT 1 FROM dbo.AspNetUsers u JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
            JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
            WHERE u.Id = @CustomerId AND u.IsActive = 1 AND r.Name = N'Customer')
            THROW 51408, N'Customer account is inactive or invalid.', 1;

        INSERT dbo.PlaySessions(TableId, BookingId, CustomerId, OpenedById, StartAtUtc, HourlyRateSnapshot)
            VALUES (@TableId, NULL, @CustomerId, @StaffId, @Now, @Rate);
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

-- Step 6. dbo.usp_CloseSession:
--   Closes the open segment, calculates total table amount (combo + overtime or minute billing) via fn_CalcPlaytimeAmount,
--   updates PlaySessions, and sets table to AwaitingPayment.
CREATE OR ALTER PROCEDURE dbo.usp_CloseSession
    @SessionId int, @StaffId nvarchar(450)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers u JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
        JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
        WHERE u.Id = @StaffId AND u.IsActive = 1 AND r.Name IN (N'Staff',N'Manager',N'Admin'))
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

-- Step 7. dbo.usp_TransferSession:
--   Moves an Active session to another Available table.
CREATE OR ALTER PROCEDURE dbo.usp_TransferSession
    @SessionId int, @NewTableId int, @StaffId nvarchar(450)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers u JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
        JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
        WHERE u.Id = @StaffId AND u.IsActive = 1 AND r.Name IN (N'Staff',N'Manager',N'Admin'))
        THROW 51611, N'Active Staff or Admin required.', 1;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @Now datetime2(0) = SYSUTCDATETIME(), @Status varchar(20), @OldTableId int,
            @BookingId int, @FirstId int, @SecondId int, @LockedId int,
            @NewStatus varchar(20), @NewRate decimal(18,2), @NewCode nvarchar(20);

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

-- Step 8. dbo.usp_AddSessionCombo:
--   Purchases a Combo for an Active session, snapshots combo name/price/hours into SessionCombos
--   and included products into SessionComboItems (without deducting Products.StockQuantity).
--   Error codes:
--     51711: Active Staff or Admin required
--     51712: Quantity must be > 0
--     51713: Session does not exist or is not Active
--     51714: Combo does not exist or is not Active
CREATE OR ALTER PROCEDURE dbo.usp_AddSessionCombo
    @SessionId int,
    @ComboId int,
    @Quantity int = 1,
    @StaffId nvarchar(450)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers u JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
        JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
        WHERE u.Id = @StaffId AND u.IsActive = 1 AND r.Name IN (N'Staff',N'Manager',N'Admin'))
        THROW 51711, N'Active Staff or Admin required.', 1;

    IF @Quantity IS NULL OR @Quantity <= 0
        THROW 51712, N'Combo quantity must be greater than zero.', 1;

    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @Now datetime2(0) = SYSUTCDATETIME(),
                @SessionStatus varchar(20),
                @ComboName nvarchar(100),
                @ComboPrice decimal(18,2),
                @ComboHours decimal(5,2),
                @ComboActive bit,
                @SessionComboId int,
                @EstimatedAmount decimal(18,2);

        SELECT @SessionStatus = Status
          FROM dbo.PlaySessions WITH (UPDLOCK, HOLDLOCK)
         WHERE Id = @SessionId;

        IF @SessionStatus IS NULL OR @SessionStatus <> 'Active'
            THROW 51713, N'Only an Active session can purchase a combo.', 1;

        SELECT @ComboName = Name,
               @ComboPrice = Price,
               @ComboHours = PlaytimeHours,
               @ComboActive = IsActive
          FROM dbo.Combos WITH (UPDLOCK, HOLDLOCK)
         WHERE Id = @ComboId;

        IF @ComboName IS NULL OR @ComboActive <> 1
            THROW 51714, N'Selected combo does not exist or is no longer active.', 1;

        INSERT dbo.SessionCombos (
            SessionId, ComboId, Quantity,
            ComboNameSnapshot, UnitPriceSnapshot, PlaytimeHoursSnapshot,
            PurchasedAtUtc, CreatedById
        )
        VALUES (
            @SessionId, @ComboId, @Quantity,
            @ComboName, @ComboPrice, @ComboHours,
            @Now, @StaffId
        );

        SET @SessionComboId = CONVERT(int, SCOPE_IDENTITY());

        INSERT dbo.SessionComboItems (
            SessionComboId, ProductId, ProductNameSnapshot, QuantityPerCombo
        )
        SELECT @SessionComboId, ci.ProductId, p.Name, ci.Quantity
          FROM dbo.ComboItems ci
          JOIN dbo.Products p ON p.Id = ci.ProductId
         WHERE ci.ComboId = @ComboId;

        SET @EstimatedAmount = dbo.fn_CalcPlaytimeAmount(@SessionId, @Now);

        COMMIT;
        SELECT @SessionComboId AS SessionComboId,
               @SessionId AS SessionId,
               @ComboId AS ComboId,
               @Quantity AS Quantity,
               @ComboName AS ComboNameSnapshot,
               @ComboPrice AS UnitPriceSnapshot,
               @ComboHours AS PlaytimeHoursSnapshot,
               @Now AS PurchasedAtUtc,
               @EstimatedAmount AS EstimatedAmount;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END;
GO

-- Step 9. Read-only verification.
SET NOEXEC OFF;
GO
SELECT DB_NAME() AS DatabaseName,
    (SELECT COUNT(*) FROM sys.objects WHERE schema_id = SCHEMA_ID(N'dbo')
        AND name = N'fn_CalcPlaytimeAmount') AS CalcFunction_Expected1,
    (SELECT COUNT(*) FROM sys.procedures WHERE schema_id = SCHEMA_ID(N'dbo')
        AND name IN (N'usp_CreateBooking', N'usp_CancelBooking', N'usp_CheckInBooking', N'usp_OpenSession', N'usp_CloseSession', N'usp_TransferSession', N'usp_AddSessionCombo')) AS Procedures_Expected7,
    (SELECT COUNT(*) FROM dbo.PlaySessions s WHERE s.Status = 'Active'
        AND (SELECT COUNT(*) FROM dbo.PlaySessionTableSegments g
              WHERE g.SessionId = s.Id AND g.EndAtUtc IS NULL) <> 1) AS ActiveWithoutOneOpenSegment_Expected0,
    (SELECT COUNT(*) FROM dbo.PlaySessions s WHERE s.Status = 'Closed'
        AND EXISTS (SELECT 1 FROM dbo.PlaySessionTableSegments g
                     WHERE g.SessionId = s.Id AND g.EndAtUtc IS NULL)) AS ClosedWithOpenSegment_Expected0;
PRINT N'06_PlaySessionEnhancements finished successfully.';
GO
