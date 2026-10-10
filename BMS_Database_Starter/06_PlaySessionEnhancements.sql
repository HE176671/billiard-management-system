/* BMS V2 - Play session enhancements (06). SQL Server 2019+, database BilliardDB.
   Additive and re-runnable (idempotent) upgrade. Does not edit any existing .sql file.
   Run the WHOLE file in SSMS. Read the Messages tab; every step prints what it did.
   Run it when nobody is opening/closing tables: between the "add column" steps and the
   procedure replacement, the old usp_OpenSession would insert rows without BillingStartAtUtc.

   WHAT THIS FILE DOES (one GO batch per step so it can be re-run):
     PlaySessions         + SessionMode, PlannedEndAtUtc, BillingStartAtUtc, BillingEndAtUtc
                            + CK_Session_Mode, CK_Session_Planned, CK_Session_Billing
                            (CK_Session_Lifecycle is NOT touched)
     PlaySessionTableSegments  new table (one row per table a session used), filtered unique
                            index: at most one open segment (EndAtUtc IS NULL) per session
     dbo.fn_CeilTo15Min        round UP to a 15-minute mark (UTC)
     dbo.fn_CalcPlaytimeAmount playtime amount by 15-minute blocks (single source of truth)
     dbo.usp_OpenSession       (changed) + @SessionMode = 'Open', @PlannedMinutes = NULL
     dbo.usp_CloseSession      (changed) bills by block through fn_CalcPlaytimeAmount
     dbo.usp_ExtendSession     (new)
     dbo.usp_TransferSession   (new)

   NOTE ON PlaySessions.HourlyRateSnapshot (decision 8):
     It keeps the rate at the moment the session was OPENED. After a table transfer this column
     is NO LONGER the rate of the whole session. The real per-table rates live in
     dbo.PlaySessionTableSegments.HourlyRateSnapshot (one row per table the session used).
     usp_CloseSession still returns the session-level column unchanged (opening rate).

   BILLING RULE (PLAN_V2 section 3). ceil15(t) = epoch + CEILING(seconds(t - epoch) / 900) * 900,
     epoch = 2000-01-01 00:00:00. Segment i is billed in [ceil15(Start), ceil15(End)] at its own
     rate: ROUND(billed seconds * rate / 3600.0, 0). PlaytimeAmount = sum of the segments. If the
     total billed seconds is 0, one block of the LAST segment is billed: ROUND(900 * rate / 3600.0, 0),
     and BillingEndAtUtc = BillingStartAtUtc + 15 minutes.

   ERROR CODES.
     Open (V1, unchanged): 51401 51402 51403 51404 51405 51406 51407 51408
     Open (new):  51409 invalid session mode, 51410 invalid planned minutes
     Close (V1, unchanged): 51501 51502
     Extend: 51601 permission, 51602 session not Active or not Timed, 51603 invalid minutes
     Transfer: 51611 permission, 51612 session not Active, 51613 target table missing or not
               Available, 51614 target is the current table, 51615 target reserved by a booking,
               51616 session is linked to a booking
     Internal integrity: 51699 Active session has no open table segment
     This file only: 51910 / 51911 are raised if a step cannot continue; fix the data and re-run.

   LOCK ORDER (V2 rule). usp_CloseSession, usp_TransferSession and usp_ExtendSession lock the
     PlaySessions row first (UPDLOCK, HOLDLOCK). Close then locks the table; Transfer then locks
     both tables in ascending Id order. usp_OpenSession keeps the V1 order (see its comment).

   All times use SYSUTCDATETIME() (UTC, datetime2(0) like the existing columns). No time is ever
   taken from a client.
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

-- Step 1. Guard: wrong database or missing base objects. SET NOEXEC ON skips every later batch
-- (the last batch turns it OFF again), so nothing can run against the wrong database.
SET NOEXEC OFF;
IF DB_NAME() <> N'BilliardDB'
   OR OBJECT_ID(N'dbo.PlaySessions', N'U') IS NULL
   OR OBJECT_ID(N'dbo.BilliardTables', N'U') IS NULL
   OR OBJECT_ID(N'dbo.TableTypes', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Bookings', N'U') IS NULL
   OR OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NULL
   OR OBJECT_ID(N'dbo.AspNetUserRoles', N'U') IS NULL
   OR OBJECT_ID(N'dbo.AspNetRoles', N'U') IS NULL
   OR OBJECT_ID(N'dbo.usp_OpenSession', N'P') IS NULL
   OR OBJECT_ID(N'dbo.usp_CloseSession', N'P') IS NULL
BEGIN
    RAISERROR(N'06_PlaySessionEnhancements: wrong database or base objects are missing. Nothing was changed.', 16, 1);
    SET NOEXEC ON;
END
GO

-- Step 2a. PlaySessions.SessionMode (existing rows become 'Open' through the default).
IF COL_LENGTH(N'dbo.PlaySessions', N'SessionMode') IS NULL
BEGIN
    ALTER TABLE dbo.PlaySessions
        ADD SessionMode varchar(10) NOT NULL CONSTRAINT DF_Session_Mode DEFAULT 'Open';
    PRINT N'Added PlaySessions.SessionMode.';
END
ELSE PRINT N'PlaySessions.SessionMode already exists.';
GO

-- Step 2b. PlaySessions.PlannedEndAtUtc (NULL for Open sessions).
IF COL_LENGTH(N'dbo.PlaySessions', N'PlannedEndAtUtc') IS NULL
BEGIN
    ALTER TABLE dbo.PlaySessions ADD PlannedEndAtUtc datetime2(0) NULL;
    PRINT N'Added PlaySessions.PlannedEndAtUtc.';
END
ELSE PRINT N'PlaySessions.PlannedEndAtUtc already exists.';
GO

-- Step 2c. PlaySessions.BillingStartAtUtc (added NULL first; made NOT NULL after the backfill).
IF COL_LENGTH(N'dbo.PlaySessions', N'BillingStartAtUtc') IS NULL
BEGIN
    ALTER TABLE dbo.PlaySessions ADD BillingStartAtUtc datetime2(0) NULL;
    PRINT N'Added PlaySessions.BillingStartAtUtc (nullable until backfilled).';
END
ELSE PRINT N'PlaySessions.BillingStartAtUtc already exists.';
GO

-- Step 2d. PlaySessions.BillingEndAtUtc (required when Closed, enforced by CK_Session_Billing).
IF COL_LENGTH(N'dbo.PlaySessions', N'BillingEndAtUtc') IS NULL
BEGIN
    ALTER TABLE dbo.PlaySessions ADD BillingEndAtUtc datetime2(0) NULL;
    PRINT N'Added PlaySessions.BillingEndAtUtc.';
END
ELSE PRINT N'PlaySessions.BillingEndAtUtc already exists.';
GO

-- Step 3. dbo.fn_CeilTo15Min. Created BEFORE the backfill because the backfill uses it.
/* dbo.fn_CeilTo15Min(@t)
     @t      datetime2(0)  A point in time in UTC. Time zone +07:00 is a whole number of hours,
                           so a UTC 15-minute mark is also a Vietnam-time 15-minute mark.
     Returns datetime2(0)  The smallest 15-minute mark that is >= @t (a value already on a mark
                           is returned unchanged). NULL in, NULL out.
   Counts seconds from the fixed epoch 2000-01-01 00:00:00; valid for @t >= the epoch.
   ceil = ((seconds + 899) / 900) * 900 seconds, applied in minutes to stay inside int range. */
CREATE OR ALTER FUNCTION dbo.fn_CeilTo15Min (@t datetime2(0))
RETURNS datetime2(0)
AS
BEGIN
    DECLARE @Epoch datetime2(0) = CONVERT(datetime2(0), N'2000-01-01T00:00:00', 126);
    RETURN CASE WHEN @t IS NULL THEN NULL
                ELSE DATEADD(minute,
                             CONVERT(int, ((DATEDIFF_BIG(second, @Epoch, @t) + 899) / 900) * 15),
                             @Epoch)
           END;
END;
GO

-- Step 4a. Backfill BillingStartAtUtc.
--   Active sessions: ceil15(StartAtUtc) (what the billing function will charge from).
--   Closed sessions: StartAtUtc unchanged, so history does not change.
-- Only rows that are still NULL are touched, so a re-run changes nothing.
SET XACT_ABORT ON;
IF EXISTS (SELECT 1 FROM dbo.PlaySessions WHERE BillingStartAtUtc IS NULL)
BEGIN
    BEGIN TRY
        BEGIN TRANSACTION;
        UPDATE dbo.PlaySessions
           SET BillingStartAtUtc = CASE WHEN Status = 'Active' THEN dbo.fn_CeilTo15Min(StartAtUtc)
                                        ELSE StartAtUtc END
         WHERE BillingStartAtUtc IS NULL;
        COMMIT;
        PRINT N'Backfilled PlaySessions.BillingStartAtUtc.';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END
ELSE PRINT N'BillingStartAtUtc: nothing to backfill.';
GO

-- Step 4b. Backfill BillingEndAtUtc for Closed sessions (= EndAtUtc, history unchanged).
SET XACT_ABORT ON;
IF EXISTS (SELECT 1 FROM dbo.PlaySessions WHERE Status = 'Closed' AND BillingEndAtUtc IS NULL)
BEGIN
    BEGIN TRY
        BEGIN TRANSACTION;
        UPDATE dbo.PlaySessions SET BillingEndAtUtc = EndAtUtc
         WHERE Status = 'Closed' AND BillingEndAtUtc IS NULL;
        COMMIT;
        PRINT N'Backfilled PlaySessions.BillingEndAtUtc for Closed sessions.';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END
ELSE PRINT N'BillingEndAtUtc: nothing to backfill.';
GO

-- Step 5. BillingStartAtUtc becomes NOT NULL (no DEFAULT, decision 4).
IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID(N'dbo.PlaySessions') AND name = N'BillingStartAtUtc'
             AND is_nullable = 1)
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.PlaySessions WHERE BillingStartAtUtc IS NULL)
        THROW 51910, N'BillingStartAtUtc still has NULL rows (a session was created meanwhile). Re-run this file.', 1;
    ALTER TABLE dbo.PlaySessions ALTER COLUMN BillingStartAtUtc datetime2(0) NOT NULL;
    PRINT N'PlaySessions.BillingStartAtUtc is now NOT NULL.';
END
ELSE PRINT N'PlaySessions.BillingStartAtUtc is already NOT NULL.';
GO

-- Step 6a. CK_Session_Mode.
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = N'CK_Session_Mode' AND parent_object_id = OBJECT_ID(N'dbo.PlaySessions'))
BEGIN
    ALTER TABLE dbo.PlaySessions WITH CHECK
        ADD CONSTRAINT CK_Session_Mode CHECK (SessionMode IN ('Open','Timed'));
    PRINT N'Added CK_Session_Mode.';
END
ELSE PRINT N'CK_Session_Mode already exists.';
GO

-- Step 6b. CK_Session_Planned: Open has no planned end; Timed has one after the start.
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = N'CK_Session_Planned' AND parent_object_id = OBJECT_ID(N'dbo.PlaySessions'))
BEGIN
    ALTER TABLE dbo.PlaySessions WITH CHECK
        ADD CONSTRAINT CK_Session_Planned CHECK (
            (SessionMode = 'Open' AND PlannedEndAtUtc IS NULL) OR
            (SessionMode = 'Timed' AND PlannedEndAtUtc IS NOT NULL AND PlannedEndAtUtc > StartAtUtc));
    PRINT N'Added CK_Session_Planned.';
END
ELSE PRINT N'CK_Session_Planned already exists.';
GO

-- Step 6c. CK_Session_Billing: Active has no billing end; Closed has one, not before the start.
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = N'CK_Session_Billing' AND parent_object_id = OBJECT_ID(N'dbo.PlaySessions'))
BEGIN
    ALTER TABLE dbo.PlaySessions WITH CHECK
        ADD CONSTRAINT CK_Session_Billing CHECK (
            (Status = 'Active' AND BillingEndAtUtc IS NULL) OR
            (Status = 'Closed' AND BillingEndAtUtc IS NOT NULL AND BillingEndAtUtc >= BillingStartAtUtc));
    PRINT N'Added CK_Session_Billing.';
END
ELSE PRINT N'CK_Session_Billing already exists.';
GO

-- Step 7a. Table PlaySessionTableSegments: one row per table a session used.
-- Segments are contiguous: the EndAtUtc of the old segment equals the StartAtUtc of the next.
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

-- Step 7b. Filtered unique index: at most ONE open segment per session.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'UX_SessionSegments_OpenPerSession'
                 AND object_id = OBJECT_ID(N'dbo.PlaySessionTableSegments'))
BEGIN
    CREATE UNIQUE INDEX UX_SessionSegments_OpenPerSession
        ON dbo.PlaySessionTableSegments(SessionId) WHERE EndAtUtc IS NULL;
    PRINT N'Created UX_SessionSegments_OpenPerSession.';
END
ELSE PRINT N'UX_SessionSegments_OpenPerSession already exists.';
GO

-- Step 7c. Index for reading a session's segments in time order.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_SessionSegments_Session_Start'
                 AND object_id = OBJECT_ID(N'dbo.PlaySessionTableSegments'))
BEGIN
    CREATE INDEX IX_SessionSegments_Session_Start
        ON dbo.PlaySessionTableSegments(SessionId, StartAtUtc);
    PRINT N'Created IX_SessionSegments_Session_Start.';
END
ELSE PRINT N'IX_SessionSegments_Session_Start already exists.';
GO

-- Step 7d. Index on the foreign key to BilliardTables.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_SessionSegments_Table'
                 AND object_id = OBJECT_ID(N'dbo.PlaySessionTableSegments'))
BEGIN
    CREATE INDEX IX_SessionSegments_Table ON dbo.PlaySessionTableSegments(TableId);
    PRINT N'Created IX_SessionSegments_Table.';
END
ELSE PRINT N'IX_SessionSegments_Table already exists.';
GO

-- Step 8. Backfill: every session without a segment gets exactly one.
-- Closed session: closed segment (EndAtUtc = session EndAtUtc). Active session: open segment.
SET XACT_ABORT ON;
IF EXISTS (SELECT 1 FROM dbo.PlaySessions s
           WHERE NOT EXISTS (SELECT 1 FROM dbo.PlaySessionTableSegments g WHERE g.SessionId = s.Id))
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
ELSE PRINT N'PlaySessionTableSegments: nothing to backfill.';
GO

-- Step 9. dbo.fn_CalcPlaytimeAmount.
/* dbo.fn_CalcPlaytimeAmount(@SessionId, @AtUtc)
     @SessionId int           Play session Id.
     @AtUtc     datetime2(0)  "Now" in UTC. Used ONLY as the end of the segment that is still
                              open (EndAtUtc IS NULL); closed segments use their own EndAtUtc,
                              so for a Closed session the result does not depend on @AtUtc.
     Returns decimal(18,2)    Playtime amount in whole VND for the whole session, or NULL when
                              the session has no segment or an argument is NULL.
   Each segment is billed from ceil15(StartAtUtc) to ceil15(EndAtUtc or @AtUtc) at its own
   HourlyRateSnapshot: ROUND(seconds * rate / 3600.0, 0). Negative spans count as 0.
   If the total billed seconds is 0, one block of the LAST segment is billed:
   ROUND(900 * rate / 3600.0, 0). Used by usp_CloseSession and by the "estimated amount" screen
   so both show the same number. */
CREATE OR ALTER FUNCTION dbo.fn_CalcPlaytimeAmount (@SessionId int, @AtUtc datetime2(0))
RETURNS decimal(18,2)
AS
BEGIN
    IF @SessionId IS NULL OR @AtUtc IS NULL RETURN NULL;
    DECLARE @Epoch datetime2(0) = CONVERT(datetime2(0), N'2000-01-01T00:00:00', 126);
    DECLARE @SegmentCount int, @TotalSeconds bigint, @Amount decimal(18,2), @LastRate decimal(18,2);

    SELECT @SegmentCount = COUNT(*),
           @TotalSeconds = SUM(x.BilledSeconds),
           @Amount = SUM(ROUND(CONVERT(decimal(18,2), x.BilledSeconds) * g.HourlyRateSnapshot / 3600.0, 0))
      FROM dbo.PlaySessionTableSegments g
     CROSS APPLY (SELECT DATEDIFF_BIG(second, @Epoch, dbo.fn_CeilTo15Min(g.StartAtUtc)) AS S,
                         DATEDIFF_BIG(second, @Epoch, dbo.fn_CeilTo15Min(COALESCE(g.EndAtUtc, @AtUtc))) AS E) w
     CROSS APPLY (SELECT CASE WHEN w.E > w.S THEN w.E - w.S ELSE CONVERT(bigint, 0) END AS BilledSeconds) x
     WHERE g.SessionId = @SessionId;

    IF @SegmentCount = 0 RETURN NULL;
    IF @TotalSeconds > 0 RETURN @Amount;

    SELECT TOP (1) @LastRate = g.HourlyRateSnapshot
      FROM dbo.PlaySessionTableSegments g
     WHERE g.SessionId = @SessionId
     ORDER BY g.StartAtUtc DESC, g.Id DESC;
    RETURN CONVERT(decimal(18,2), ROUND(CONVERT(decimal(18,2), 900) * @LastRate / 3600.0, 0));
END;
GO

-- Step 10. usp_OpenSession (changed).
/* Signature: the V1 parameters keep their names and order; two optional parameters are
   appended, so EXEC usp_OpenSession @TableId, @StaffId (and the BookingId/CustomerId forms)
   behave as before. Checks 51401 to 51408 and their order are unchanged.
   New checks (right after 51401, before the transaction):
     51409 @SessionMode is not exactly 'Open' or 'Timed' (comparison is case-sensitive).
     51410 Timed without @PlannedMinutes, or @PlannedMinutes not a multiple of 15 in 15..720,
           or Open WITH @PlannedMinutes.
   Result set (read by column name): SessionId, BillingStartAtUtc, PlannedEndAtUtc, SessionMode.

   LOCK ORDER (accepted by the team). Lập luận nguyên văn:
   usp_OpenSession tạo dòng phiên mới nên chưa có dòng nào để khóa trước. Nó giữ nguyên thứ tự V1:
   khóa bàn, rồi chỉ đọc PlaySessions ở các kiểm tra 51403 và 51406, rồi INSERT. Hai lần đọc này có
   thể đụng dòng phiên đang bị Close hoặc Transfer giữ khóa, nhưng chỉ trong hai tình huống:
   - Bàn Available mà vẫn có phiên Active (dữ liệu sai).
   - Chuyển bàn của phiên có BookingId đồng thời với mở bàn theo booking đó. Tình huống này đã bị
     51616 chặn từ trước khi lấy khóa bàn. */
CREATE OR ALTER PROCEDURE dbo.usp_OpenSession
    @TableId int, @StaffId nvarchar(450),
    @BookingId int = NULL, @CustomerId nvarchar(450) = NULL,
    @SessionMode varchar(10) = 'Open', @PlannedMinutes int = NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers u JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
        JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
        WHERE u.Id = @StaffId AND u.IsActive = 1 AND r.Name IN (N'Staff',N'Admin'))
        THROW 51401, N'Active Staff or Admin required.', 1;
    IF @SessionMode IS NULL
       OR @SessionMode COLLATE Latin1_General_100_BIN2 NOT IN ('Open','Timed')
       OR DATALENGTH(@SessionMode) <> DATALENGTH(RTRIM(@SessionMode))
        THROW 51409, N'Session mode must be Open or Timed.', 1;
    IF (@SessionMode = 'Timed' AND (@PlannedMinutes IS NULL OR @PlannedMinutes < 15
                                    OR @PlannedMinutes > 720 OR @PlannedMinutes % 15 <> 0))
       OR (@SessionMode = 'Open' AND @PlannedMinutes IS NOT NULL)
        THROW 51410, N'Timed needs planned minutes (multiple of 15, from 15 to 720); Open must not have planned minutes.', 1;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @TableStatus varchar(20), @Rate decimal(18,2), @Now datetime2(0) = SYSUTCDATETIME();
        SELECT @TableStatus = t.Status, @Rate = ty.HourlyRate
          FROM dbo.BilliardTables t WITH (UPDLOCK, HOLDLOCK)
          JOIN dbo.TableTypes ty ON ty.Id = t.TableTypeId WHERE t.Id = @TableId;
        IF @TableStatus IS NULL OR @TableStatus <> 'Available'
            THROW 51402, N'Table is not Available.', 1;
        IF EXISTS (SELECT 1 FROM dbo.PlaySessions WHERE TableId = @TableId AND Status = 'Active')
            THROW 51403, N'Table already has an Active session.', 1;
        IF @BookingId IS NOT NULL
        BEGIN
            DECLARE @BookingStatus varchar(20), @BookingTable int, @BookingEnd datetime2(0), @BookingCustomer nvarchar(450);
            SELECT @BookingStatus = Status, @BookingTable = TableId, @BookingEnd = EndAtUtc, @BookingCustomer = CustomerId
              FROM dbo.Bookings WHERE Id = @BookingId;
            IF @BookingStatus IS NULL OR @BookingStatus <> 'CheckedIn' OR @BookingTable <> @TableId OR @BookingEnd <= @Now
                THROW 51404, N'Use a checked-in, unexpired booking for this table.', 1;
            IF @CustomerId IS NOT NULL AND @CustomerId <> @BookingCustomer
                THROW 51405, N'Customer does not match the booking.', 1;
            SET @CustomerId = @BookingCustomer;
            IF EXISTS (SELECT 1 FROM dbo.PlaySessions WHERE BookingId = @BookingId)
                THROW 51406, N'This booking already has a session.', 1;
        END
        ELSE IF EXISTS (SELECT 1 FROM dbo.Bookings WHERE TableId = @TableId
            AND (Status = 'CheckedIn' OR (Status = 'Confirmed' AND StartAtUtc <= @Now AND EndAtUtc > @Now)))
            THROW 51407, N'Table is reserved for a current booking.', 1;
        IF @CustomerId IS NOT NULL AND NOT EXISTS (
            SELECT 1 FROM dbo.AspNetUsers u JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
            JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
            WHERE u.Id = @CustomerId AND u.IsActive = 1 AND r.Name = N'Customer')
            THROW 51408, N'Customer account is inactive or invalid.', 1;
        DECLARE @BillingStart datetime2(0) = dbo.fn_CeilTo15Min(@Now);
        DECLARE @PlannedEnd datetime2(0) =
            CASE WHEN @SessionMode = 'Timed' THEN DATEADD(minute, @PlannedMinutes, @BillingStart) ELSE NULL END;
        INSERT dbo.PlaySessions(TableId, BookingId, CustomerId, OpenedById, StartAtUtc, HourlyRateSnapshot,
                                SessionMode, PlannedEndAtUtc, BillingStartAtUtc)
            VALUES (@TableId, @BookingId, @CustomerId, @StaffId, @Now, @Rate,
                    @SessionMode, @PlannedEnd, @BillingStart);
        DECLARE @Id int = CONVERT(int,SCOPE_IDENTITY());
        INSERT dbo.PlaySessionTableSegments(SessionId, TableId, HourlyRateSnapshot, StartAtUtc, EndAtUtc)
            VALUES (@Id, @TableId, @Rate, @Now, NULL);
        UPDATE dbo.BilliardTables SET Status = 'InUse' WHERE Id = @TableId;
        COMMIT;
        SELECT @Id AS SessionId, @BillingStart AS BillingStartAtUtc,
               @PlannedEnd AS PlannedEndAtUtc, @SessionMode AS SessionMode;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END;
GO

-- Step 11. usp_CloseSession (changed).
/* Parameters and error codes (51501, 51502) are unchanged. The session row is locked FIRST
   (UPDLOCK, HOLDLOCK) and TableId is read after that lock, then the table is locked: a transfer
   can change TableId, so reading it before the lock could close the wrong table.
   Closes the open segment, bills through dbo.fn_CalcPlaytimeAmount and writes
   BillingStartAtUtc = ceil15(StartAtUtc) again as a safeguard (matters only for sessions that
   were Active before V2). BillingEndAtUtc = ceil15(close time); if that is not after
   BillingStartAtUtc (minimum 1 block) it becomes BillingStartAtUtc + 15 minutes.
   Unchanged updates: session Closed, table AwaitingPayment, booking Completed.
   Result set (read by column name): the 6 V1 columns in the V1 order (Id, StartAtUtc,
   EndAtUtc, HourlyRateSnapshot, PlaytimeAmount, Status), then BillingStartAtUtc,
   BillingEndAtUtc, SessionMode. */
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
            @Amount decimal(18,2), @BillStart datetime2(0), @BillEnd datetime2(0);
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
        SET @BillStart = dbo.fn_CeilTo15Min(@Start);
        SET @BillEnd = dbo.fn_CeilTo15Min(@Now);
        IF @BillEnd <= @BillStart SET @BillEnd = DATEADD(minute, 15, @BillStart);
        UPDATE dbo.PlaySessions SET EndAtUtc = @Now, ClosedById = @StaffId, Status = 'Closed',
            PlaytimeAmount = @Amount, BillingStartAtUtc = @BillStart, BillingEndAtUtc = @BillEnd
            WHERE Id = @SessionId;
        UPDATE dbo.BilliardTables SET Status = 'AwaitingPayment' WHERE Id = @TableId;
        IF @BookingId IS NOT NULL UPDATE dbo.Bookings SET Status = 'Completed' WHERE Id = @BookingId;
        COMMIT;
        SELECT Id, StartAtUtc, EndAtUtc, HourlyRateSnapshot, PlaytimeAmount, Status,
               BillingStartAtUtc, BillingEndAtUtc, SessionMode
          FROM dbo.PlaySessions WHERE Id = @SessionId;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END;
GO

-- Step 12. usp_ExtendSession (new).
/* Adds time to an Active Timed session: PlannedEndAtUtc += @AddMinutes. Billing is NOT affected
   (billing always follows the real play time). Locks the PlaySessions row (UPDLOCK, HOLDLOCK),
   so two simultaneous extensions are applied one after the other and both are counted.
     @SessionId   int            Session to extend.
     @AddMinutes  int            Multiple of 15, from 15 to 240.
     @StaffId     nvarchar(450)  Active Staff or Admin.
   Errors: 51601 permission; 51603 invalid @AddMinutes; 51602 session missing, not Active or not
   Timed. Result set: SessionId, PlannedEndAtUtc (the new planned end, UTC). */
CREATE OR ALTER PROCEDURE dbo.usp_ExtendSession
    @SessionId int, @AddMinutes int, @StaffId nvarchar(450)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers u JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
        JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
        WHERE u.Id = @StaffId AND u.IsActive = 1 AND r.Name IN (N'Staff',N'Admin'))
        THROW 51601, N'Active Staff or Admin required.', 1;
    IF @AddMinutes IS NULL OR @AddMinutes < 15 OR @AddMinutes > 240 OR @AddMinutes % 15 <> 0
        THROW 51603, N'Extension must be a multiple of 15 minutes, from 15 to 240.', 1;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @Status varchar(20), @Mode varchar(10), @PlannedEnd datetime2(0);
        SELECT @Status = Status, @Mode = SessionMode, @PlannedEnd = PlannedEndAtUtc
          FROM dbo.PlaySessions WITH (UPDLOCK, HOLDLOCK) WHERE Id = @SessionId;
        IF @Status IS NULL OR @Status <> 'Active' OR @Mode <> 'Timed'
            THROW 51602, N'Only an Active Timed session can be extended.', 1;
        SET @PlannedEnd = DATEADD(minute, @AddMinutes, @PlannedEnd);
        UPDATE dbo.PlaySessions SET PlannedEndAtUtc = @PlannedEnd WHERE Id = @SessionId;
        COMMIT;
        SELECT @SessionId AS SessionId, @PlannedEnd AS PlannedEndAtUtc;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END;
GO

-- Step 13. usp_TransferSession (new).
/* Moves an Active session to another table WITHOUT resetting its time. The old segment is closed
   and the new one opened at the same instant, with the rate of the target table.
     @SessionId   int            Session to move.
     @NewTableId  int            Target table.
     @StaffId     nvarchar(450)  Active Staff or Admin.
   Check order: 51611 permission; 51612 session missing or not Active; 51616 session linked to a
   booking; 51614 target = current table; then lock the PlaySessions row, then BOTH tables in
   ascending Id (UPDLOCK, HOLDLOCK); 51613 target missing, not Available (or has an Active
   session); 51615 target reserved by a booking.
   51615 uses EXACTLY the reservation condition of 51407 in usp_OpenSession.
   51616: the 51407 condition counts a CheckedIn booking even when that booking already has a
   session (it stays CheckedIn until the session closes). After a transfer the old table would
   look reserved by the booking of the session that just left it, so booking sessions are refused.
   Effects: old segment closed, new segment opened, PlaySessions.TableId changed, old table
   Available, new table InUse. HourlyRateSnapshot of the session is not changed.
   Result set: SessionId, OldTableId, NewTableId, NewTableCode, NewHourlyRate, TransferAtUtc. */
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
        SELECT @Status = Status, @OldTableId = TableId, @BookingId = BookingId
          FROM dbo.PlaySessions WITH (UPDLOCK, HOLDLOCK) WHERE Id = @SessionId;
        IF @Status IS NULL OR @Status <> 'Active'
            THROW 51612, N'Only an Active session can be transferred.', 1;
        IF @BookingId IS NOT NULL
            THROW 51616, N'Sessions linked to a booking cannot be transferred.', 1;
        IF @NewTableId = @OldTableId
            THROW 51614, N'Target table is the current table of the session.', 1;
        -- Lock both tables in ascending Id order (a NULL target locks only the current table).
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
            AND (Status = 'CheckedIn' OR (Status = 'Confirmed' AND StartAtUtc <= @Now AND EndAtUtc > @Now)))
            THROW 51615, N'Target table is reserved for a current booking.', 1;
        UPDATE dbo.PlaySessionTableSegments SET EndAtUtc = @Now
         WHERE SessionId = @SessionId AND EndAtUtc IS NULL;
        IF @@ROWCOUNT = 0
            THROW 51699, N'Active session has no open table segment.', 1;
        INSERT dbo.PlaySessionTableSegments(SessionId, TableId, HourlyRateSnapshot, StartAtUtc, EndAtUtc)
            VALUES (@SessionId, @NewTableId, @NewRate, @Now, NULL);
        UPDATE dbo.PlaySessions SET TableId = @NewTableId WHERE Id = @SessionId;
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

-- Step 14. Read-only verification. Turns SET NOEXEC OFF (it was ON only if the guard failed).
SET NOEXEC OFF;
GO
SELECT DB_NAME() AS DatabaseName,
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.PlaySessions')
        AND name IN (N'SessionMode', N'PlannedEndAtUtc', N'BillingStartAtUtc', N'BillingEndAtUtc')) AS NewColumns_Expected4,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.PlaySessions')
        AND name IN (N'CK_Session_Lifecycle', N'CK_Session_Mode', N'CK_Session_Planned', N'CK_Session_Billing')) AS Checks_Expected4,
    (SELECT COUNT(*) FROM sys.objects WHERE schema_id = SCHEMA_ID(N'dbo')
        AND name IN (N'fn_CeilTo15Min', N'fn_CalcPlaytimeAmount')) AS Functions_Expected2,
    (SELECT COUNT(*) FROM sys.procedures WHERE schema_id = SCHEMA_ID(N'dbo')
        AND name IN (N'usp_OpenSession', N'usp_CloseSession', N'usp_ExtendSession', N'usp_TransferSession')) AS Procedures_Expected4,
    (SELECT COUNT(*) FROM dbo.PlaySessions) AS PlaySessions,
    (SELECT COUNT(*) FROM dbo.PlaySessionTableSegments) AS Segments,
    (SELECT COUNT(*) FROM dbo.PlaySessions s WHERE s.Status = 'Active'
        AND (SELECT COUNT(*) FROM dbo.PlaySessionTableSegments g
              WHERE g.SessionId = s.Id AND g.EndAtUtc IS NULL) <> 1) AS ActiveWithoutOneOpenSegment_Expected0,
    (SELECT COUNT(*) FROM dbo.PlaySessions s WHERE s.Status = 'Closed'
        AND EXISTS (SELECT 1 FROM dbo.PlaySessionTableSegments g
                     WHERE g.SessionId = s.Id AND g.EndAtUtc IS NULL)) AS ClosedWithOpenSegment_Expected0;
SELECT name AS ConstraintName, is_disabled, is_not_trusted
  FROM sys.check_constraints
 WHERE parent_object_id IN (OBJECT_ID(N'dbo.PlaySessions'), OBJECT_ID(N'dbo.PlaySessionTableSegments'))
 ORDER BY parent_object_id, name;
PRINT N'06_PlaySessionEnhancements finished. Check the result sets above: every Expected column must match.';
GO
