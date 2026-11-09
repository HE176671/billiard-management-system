using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Data;

/// <summary>
/// TV5: tự áp dụng script 18_WF5_AuditLog_SingleShift.sql khi web khởi động.
/// Chỉ tạo bảng/index nếu chưa có, không xóa dữ liệu.
/// </summary>
public static class Wf5SchemaUpgrader
{
    private const string AuditLogBatch = """
        SET ANSI_NULLS ON;
        SET QUOTED_IDENTIFIER ON;
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
        """;

    // BR-23: cả quán chỉ có một ca đang mở.
    private const string SingleShiftBatch = """
        SET ANSI_NULLS ON;
        SET QUOTED_IDENTIFIER ON;
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_WorkShifts_SingleActive' AND object_id = OBJECT_ID('dbo.WorkShifts'))
           AND (SELECT COUNT(*) FROM dbo.WorkShifts WHERE Status = 'Active') <= 1
            CREATE UNIQUE INDEX UX_WorkShifts_SingleActive ON dbo.WorkShifts(Status) WHERE Status = 'Active';
        """;

    public static async Task ApplyAsync(ApplicationDbContext db, ILogger logger)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(AuditLogBatch);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Không tạo được bảng AuditLogs. Hãy chạy tay BMS_Database_Starter/18_WF5_AuditLog_SingleShift.sql.");
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync(SingleShiftBatch);
            var active = await db.Database
                .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM dbo.WorkShifts WHERE Status = 'Active'")
                .SingleAsync();
            if (active > 1)
                logger.LogWarning(
                    "Đang có {Count} ca mở cùng lúc (trái BR-23). Hãy đóng bớt ca ở màn Quản lý ca rồi khởi động lại để tạo ràng buộc 1 ca.",
                    active);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Không tạo được ràng buộc một ca đang mở (UX_WorkShifts_SingleActive).");
        }
    }
}
