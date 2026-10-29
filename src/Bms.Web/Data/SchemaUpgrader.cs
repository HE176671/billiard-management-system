using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Data;

/// <summary>
/// Tự áp dụng các nâng cấp schema cộng thêm khi web khởi động, để máy nào quên
/// chạy script tay vẫn không bị lỗi "Invalid column name".
/// Nội dung giống BMS_Database_Starter/08_AddWorkShiftReconciliation.sql: chỉ thêm
/// cột/ràng buộc/index nếu chưa có, không xóa hay sửa dữ liệu.
/// </summary>
public static class SchemaUpgrader
{
    // Mỗi phần tử là một batch (tương đương giữa hai lệnh GO trong script).
    private static readonly string[] WorkShiftBatches =
    {
        """
        IF COL_LENGTH('dbo.WorkShifts', 'CashCollected') IS NULL
            ALTER TABLE dbo.WorkShifts ADD CashCollected decimal(18,2) NULL;
        IF COL_LENGTH('dbo.WorkShifts', 'ExpectedCash') IS NULL
            ALTER TABLE dbo.WorkShifts ADD ExpectedCash decimal(18,2) NULL;
        IF COL_LENGTH('dbo.WorkShifts', 'ClosedById') IS NULL
            ALTER TABLE dbo.WorkShifts ADD ClosedById nvarchar(450) NULL;
        IF COL_LENGTH('dbo.PaymentTransactions', 'WorkShiftId') IS NULL
            ALTER TABLE dbo.PaymentTransactions ADD WorkShiftId int NULL;
        IF COL_LENGTH('dbo.PaymentTransactions', 'ReceivedById') IS NULL
            ALTER TABLE dbo.PaymentTransactions ADD ReceivedById nvarchar(450) NULL;
        """,
        """
        IF OBJECT_ID('dbo.FK_Shift_ClosedBy', 'F') IS NULL
            ALTER TABLE dbo.WorkShifts ADD CONSTRAINT FK_Shift_ClosedBy
                FOREIGN KEY (ClosedById) REFERENCES dbo.AspNetUsers(Id);
        IF OBJECT_ID('dbo.CK_Shift_StartCash', 'C') IS NULL
            ALTER TABLE dbo.WorkShifts WITH CHECK ADD CONSTRAINT CK_Shift_StartCash CHECK (StartingCash >= 0);
        IF OBJECT_ID('dbo.CK_Shift_EndCash', 'C') IS NULL
            ALTER TABLE dbo.WorkShifts WITH CHECK ADD CONSTRAINT CK_Shift_EndCash CHECK (EndingCash IS NULL OR EndingCash >= 0);
        IF OBJECT_ID('dbo.CK_Shift_Lifecycle', 'C') IS NULL
            ALTER TABLE dbo.WorkShifts WITH CHECK ADD CONSTRAINT CK_Shift_Lifecycle CHECK (
                (Status = 'Active' AND EndTimeUtc IS NULL AND EndingCash IS NULL) OR
                (Status = 'Closed' AND EndTimeUtc IS NOT NULL AND EndingCash IS NOT NULL));
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_WorkShifts_ActiveStaff' AND object_id = OBJECT_ID('dbo.WorkShifts'))
            CREATE UNIQUE INDEX UX_WorkShifts_ActiveStaff ON dbo.WorkShifts(StaffId) WHERE Status = 'Active';
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WorkShifts_Start' AND object_id = OBJECT_ID('dbo.WorkShifts'))
            CREATE INDEX IX_WorkShifts_Start ON dbo.WorkShifts(StartTimeUtc) INCLUDE (StaffId, Status);
        IF OBJECT_ID('dbo.FK_Payment_WorkShift', 'F') IS NULL
            ALTER TABLE dbo.PaymentTransactions ADD CONSTRAINT FK_Payment_WorkShift
                FOREIGN KEY (WorkShiftId) REFERENCES dbo.WorkShifts(Id);
        IF OBJECT_ID('dbo.FK_Payment_ReceivedBy', 'F') IS NULL
            ALTER TABLE dbo.PaymentTransactions ADD CONSTRAINT FK_Payment_ReceivedBy
                FOREIGN KEY (ReceivedById) REFERENCES dbo.AspNetUsers(Id);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Payments_WorkShift' AND object_id = OBJECT_ID('dbo.PaymentTransactions'))
            CREATE INDEX IX_Payments_WorkShift ON dbo.PaymentTransactions(WorkShiftId, PaymentMethod)
                INCLUDE (AmountPaid) WHERE WorkShiftId IS NOT NULL;
        """
    };

    public static async Task ApplyAsync(ApplicationDbContext db, ILogger logger)
    {
        var missing = await db.Database.SqlQueryRaw<int>("""
            SELECT CASE WHEN COL_LENGTH('dbo.WorkShifts', 'ExpectedCash') IS NULL
                          OR COL_LENGTH('dbo.WorkShifts', 'CashCollected') IS NULL
                          OR COL_LENGTH('dbo.WorkShifts', 'ClosedById') IS NULL
                          OR COL_LENGTH('dbo.PaymentTransactions', 'WorkShiftId') IS NULL
                          OR COL_LENGTH('dbo.PaymentTransactions', 'ReceivedById') IS NULL
                        THEN 1 ELSE 0 END AS Value
            """).SingleAsync();
        if (missing == 0) return;

        logger.LogWarning("Database thiếu cột ca làm (script 08). Đang tự nâng cấp schema...");
        // Thêm cột trước và lưu ngay: thiếu cột là web lỗi, nên phần này phải thành công.
        await db.Database.ExecuteSqlRawAsync(WorkShiftBatches[0]);
        try
        {
            // Ràng buộc/index có thể vướng dữ liệu cũ; lỗi thì web vẫn chạy được.
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync(WorkShiftBatches[1]);
            await tx.CommitAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Đã thêm cột ca làm nhưng chưa tạo được ràng buộc/index. Kiểm tra dữ liệu WorkShifts rồi chạy lại script 08.");
            return;
        }
        logger.LogInformation("Đã nâng cấp schema ca làm (tương đương 08_AddWorkShiftReconciliation.sql).");
    }
}
