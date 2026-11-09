/*
    18_WF5_AuditLog_SingleShift.sql  (TV5 - Hung)
    - Tao bang dbo.AuditLogs: nhat ky thao tac nhay cam (doi gia, kiem ke, lech tien ca, huy mon/hoa don...).
    - BR-23: ca quan chi co MOT ca dang mo -> unique filtered index tren WorkShifts(Status) WHERE Status = 'Active'.
    Chay sau 08_AddWorkShiftReconciliation.sql. Chay lai nhieu lan van an toan.
    Web cung tu ap dung script nay khi khoi dong (Data/Wf5SchemaUpgrader.cs).
*/
USE BilliardDB;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID('dbo.AuditLogs', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AuditLogs (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AuditLogs PRIMARY KEY,
        ActionType varchar(50) NOT NULL,
        EntityType varchar(50) NOT NULL,
        EntityId nvarchar(100) NULL,
        Description nvarchar(1000) NOT NULL,
        OldValue nvarchar(1000) NULL,
        NewValue nvarchar(1000) NULL,
        Reason nvarchar(500) NULL,
        ActorId nvarchar(450) NULL,
        ActorName nvarchar(200) NULL,
        CreatedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_AuditLogs_Created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_AuditLogs_Actor FOREIGN KEY (ActorId) REFERENCES dbo.AspNetUsers(Id)
    );
    CREATE INDEX IX_AuditLogs_Created ON dbo.AuditLogs(CreatedAtUtc DESC);
    CREATE INDEX IX_AuditLogs_Action_Created ON dbo.AuditLogs(ActionType, CreatedAtUtc DESC);
    CREATE INDEX IX_AuditLogs_Actor_Created ON dbo.AuditLogs(ActorId, CreatedAtUtc DESC);
END;
GO

-- BR-23: chi mot ca dang mo cho ca quan.
IF (SELECT COUNT(*) FROM dbo.WorkShifts WHERE Status = 'Active') > 1
    PRINT N'Dang co nhieu ca mo cung luc. Dong bot ca roi chay lai script de tao UX_WorkShifts_SingleActive.';
ELSE IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_WorkShifts_SingleActive' AND object_id = OBJECT_ID('dbo.WorkShifts'))
    CREATE UNIQUE INDEX UX_WorkShifts_SingleActive ON dbo.WorkShifts(Status) WHERE Status = 'Active';
GO
