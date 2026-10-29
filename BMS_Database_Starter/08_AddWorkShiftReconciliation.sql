-- TV5 (Hùng): ca làm và đối soát tiền mặt. Nâng cấp cộng thêm, chạy lại an toàn.
-- Chạy SAU BilliardDB_Full.sql / 05 / 06 / 07. Không xóa dữ liệu.
--
-- 1. WorkShifts: lưu snapshot lúc đóng ca để đối soát không đổi về sau.
--      CashCollected  = tổng tiền mặt (PaymentMethod = 'Cash') gắn với ca
--      ExpectedCash   = StartingCash + CashCollected
--      EndingCash     = tiền nhân viên đếm thực tế (đã có sẵn)
--      Chênh lệch     = EndingCash - ExpectedCash (tính ở code)
--      ClosedById     = người đóng ca (chính nhân viên, hoặc Admin đóng hộ)
--    Mỗi nhân viên chỉ có tối đa 1 ca Active (unique filtered index).
-- 2. PaymentTransactions: WorkShiftId + ReceivedById để TV3 gắn giao dịch vào ca.
--    Cả hai NULL được: giao dịch VNPay/IPN không có nhân viên thu.
USE BilliardDB;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID('dbo.WorkShifts', 'U') IS NULL
    THROW 50801, 'WorkShifts missing. Run BilliardDB_Full.sql first.', 1;
IF OBJECT_ID('dbo.PaymentTransactions', 'U') IS NULL
    THROW 50802, 'PaymentTransactions missing. Run BilliardDB_Full.sql first.', 1;

-- ── WorkShifts ──
IF COL_LENGTH('dbo.WorkShifts', 'CashCollected') IS NULL
    ALTER TABLE dbo.WorkShifts ADD CashCollected decimal(18,2) NULL;
IF COL_LENGTH('dbo.WorkShifts', 'ExpectedCash') IS NULL
    ALTER TABLE dbo.WorkShifts ADD ExpectedCash decimal(18,2) NULL;
IF COL_LENGTH('dbo.WorkShifts', 'ClosedById') IS NULL
    ALTER TABLE dbo.WorkShifts ADD ClosedById nvarchar(450) NULL;
COMMIT TRANSACTION;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.FK_Shift_ClosedBy', 'F') IS NULL
    ALTER TABLE dbo.WorkShifts ADD CONSTRAINT FK_Shift_ClosedBy
        FOREIGN KEY (ClosedById) REFERENCES dbo.AspNetUsers(Id);

IF OBJECT_ID('dbo.CK_Shift_StartCash', 'C') IS NULL
    ALTER TABLE dbo.WorkShifts WITH CHECK ADD CONSTRAINT CK_Shift_StartCash
        CHECK (StartingCash >= 0);
IF OBJECT_ID('dbo.CK_Shift_EndCash', 'C') IS NULL
    ALTER TABLE dbo.WorkShifts WITH CHECK ADD CONSTRAINT CK_Shift_EndCash
        CHECK (EndingCash IS NULL OR EndingCash >= 0);

-- Ca cũ đã Closed nhưng thiếu dữ liệu đóng ca thì CHECK dưới đây sẽ báo lỗi.
-- Dữ liệu demo hiện tại không có WorkShifts nên không bị ảnh hưởng.
IF OBJECT_ID('dbo.CK_Shift_Lifecycle', 'C') IS NULL
    ALTER TABLE dbo.WorkShifts WITH CHECK ADD CONSTRAINT CK_Shift_Lifecycle CHECK (
        (Status = 'Active' AND EndTimeUtc IS NULL AND EndingCash IS NULL) OR
        (Status = 'Closed' AND EndTimeUtc IS NOT NULL AND EndingCash IS NOT NULL));

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_WorkShifts_ActiveStaff'
               AND object_id = OBJECT_ID('dbo.WorkShifts'))
    CREATE UNIQUE INDEX UX_WorkShifts_ActiveStaff ON dbo.WorkShifts(StaffId)
        WHERE Status = 'Active';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WorkShifts_Start'
               AND object_id = OBJECT_ID('dbo.WorkShifts'))
    CREATE INDEX IX_WorkShifts_Start ON dbo.WorkShifts(StartTimeUtc) INCLUDE (StaffId, Status);

-- ── PaymentTransactions ──
IF COL_LENGTH('dbo.PaymentTransactions', 'WorkShiftId') IS NULL
    ALTER TABLE dbo.PaymentTransactions ADD WorkShiftId int NULL;
IF COL_LENGTH('dbo.PaymentTransactions', 'ReceivedById') IS NULL
    ALTER TABLE dbo.PaymentTransactions ADD ReceivedById nvarchar(450) NULL;
COMMIT TRANSACTION;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.FK_Payment_WorkShift', 'F') IS NULL
    ALTER TABLE dbo.PaymentTransactions ADD CONSTRAINT FK_Payment_WorkShift
        FOREIGN KEY (WorkShiftId) REFERENCES dbo.WorkShifts(Id);
IF OBJECT_ID('dbo.FK_Payment_ReceivedBy', 'F') IS NULL
    ALTER TABLE dbo.PaymentTransactions ADD CONSTRAINT FK_Payment_ReceivedBy
        FOREIGN KEY (ReceivedById) REFERENCES dbo.AspNetUsers(Id);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Payments_WorkShift'
               AND object_id = OBJECT_ID('dbo.PaymentTransactions'))
    CREATE INDEX IX_Payments_WorkShift ON dbo.PaymentTransactions(WorkShiftId, PaymentMethod)
        INCLUDE (AmountPaid) WHERE WorkShiftId IS NOT NULL;
COMMIT TRANSACTION;
GO

-- Kiểm tra nhanh
SELECT c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE, c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE (c.TABLE_NAME = 'WorkShifts' AND c.COLUMN_NAME IN ('CashCollected','ExpectedCash','ClosedById'))
   OR (c.TABLE_NAME = 'PaymentTransactions' AND c.COLUMN_NAME IN ('WorkShiftId','ReceivedById'))
ORDER BY c.TABLE_NAME, c.COLUMN_NAME;
