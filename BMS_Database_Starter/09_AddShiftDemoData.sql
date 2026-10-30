-- DU LIEU GIA LAP cho trang Ca lam / Quan ly ca, KHONG phai giao dich thuc.
-- Can chay sau 08_AddWorkShiftReconciliation.sql (hoac chay web 1 lan de tu nang cap).
-- Chay lai an toan: neu da co ca demo thi bo qua, khong ghi de du lieu.
-- Tao 4 ca DA DONG trong 4 ngay gan nhat (gio Viet Nam), de trong cho nhan vien tu Mo ca khi demo:
--   Ca 1: khop tien        Ca 2: thieu 10.000 d (co VNPay, khong tinh vao ket)
--   Ca 3: thua 20.000 d    Ca 4: khop tien, khong co giao dich
USE BilliardDB;
GO
IF COL_LENGTH('dbo.WorkShifts', 'ExpectedCash') IS NULL OR COL_LENGTH('dbo.PaymentTransactions', 'WorkShiftId') IS NULL
BEGIN
    RAISERROR(N'Thieu cot ca lam. Hay chay 08_AddWorkShiftReconciliation.sql (hoac chay web 1 lan) truoc.', 16, 1);
    SET NOEXEC ON;
END;
GO
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS (SELECT 1 FROM dbo.WorkShifts WHERE Notes LIKE N'[[]DEMO]%')
    BEGIN
        PRINT N'Da co du lieu ca demo, bo qua.';
        COMMIT TRANSACTION;
        RETURN;
    END;

    -- Nhan vien: uu tien tai khoan mau, neu khong co thi lay Staff bat ky.
    DECLARE @Staff1 nvarchar(450) = (SELECT Id FROM dbo.AspNetUsers WHERE Id = N'demo-staff-01');
    DECLARE @Staff2 nvarchar(450) = (SELECT Id FROM dbo.AspNetUsers WHERE Id = N'demo-staff-02');
    DECLARE @AnyStaff nvarchar(450) = (SELECT TOP(1) u.Id FROM dbo.AspNetUsers u
        JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
        WHERE r.NormalizedName = N'STAFF' ORDER BY u.Id);
    SET @Staff1 = COALESCE(@Staff1, @AnyStaff);
    SET @Staff2 = COALESCE(@Staff2, @Staff1);
    DECLARE @TableId int = (SELECT TOP(1) Id FROM dbo.BilliardTables ORDER BY Id);
    IF @Staff1 IS NULL OR @TableId IS NULL
        THROW 50901, 'Can it nhat 1 tai khoan Staff va 1 ban bida.', 1;

    DECLARE @Today date = CAST(DATEADD(hour, 7, SYSUTCDATETIME()) AS date);

    -- Ca: so thu tu, nhan vien, so ngay truoc hom nay, gio bat dau/ket thuc (gio VN), tien dau ca, tien dem thuc te, ghi chu
    DECLARE @Shifts TABLE (No int, StaffId nvarchar(450), DaysAgo int, StartH int, EndH int,
        StartCash decimal(18,2), Counted decimal(18,2), Note nvarchar(500));
    INSERT @Shifts VALUES
        (1, @Staff1, 4,  8, 14, 500000,  880000, N'[DEMO] Ca sáng'),
        (2, @Staff2, 3, 14, 22, 500000,  680000, N'[DEMO] Ca chiều | Đóng ca: Thiếu 10.000đ do thối nhầm tiền cho khách'),
        (3, @Staff1, 2,  8, 14, 300000,  520000, N'[DEMO] Ca sáng | Đóng ca: Thừa 20.000đ, khách không lấy tiền thối'),
        (4, @Staff1, 1, 14, 22, 500000,  500000, N'[DEMO] Ca chiều vắng khách');

    -- Giao dich trong ca: so ca, phuong thuc, so tien, phut sau khi mo ca
    DECLARE @Payments TABLE (ShiftNo int, Method varchar(20), Amount decimal(18,2), AtMinute int);
    INSERT @Payments VALUES
        (1, 'Cash', 180000, 90), (1, 'Cash', 200000, 240),
        (2, 'Cash', 190000, 120), (2, 'VNPay', 190000, 300),
        (3, 'Cash', 200000, 150);

    DECLARE @No int = 1;
    WHILE @No <= 4
    BEGIN
        DECLARE @StaffId nvarchar(450), @DaysAgo int, @StartH int, @EndH int,
                @StartCash decimal(18,2), @Counted decimal(18,2), @Note nvarchar(500);
        SELECT @StaffId = StaffId, @DaysAgo = DaysAgo, @StartH = StartH, @EndH = EndH,
               @StartCash = StartCash, @Counted = Counted, @Note = Note
        FROM @Shifts WHERE No = @No;

        DECLARE @Day datetime2(0) = CAST(DATEADD(day, -@DaysAgo, @Today) AS datetime2(0));
        DECLARE @StartUtc datetime2(0) = DATEADD(hour, @StartH - 7, @Day);
        DECLARE @EndUtc datetime2(0) = DATEADD(hour, @EndH - 7, @Day);
        DECLARE @Cash decimal(18,2) = ISNULL((SELECT SUM(Amount) FROM @Payments
                                              WHERE ShiftNo = @No AND Method = 'Cash'), 0);

        -- Tao ca o trang thai Closed kem snapshot doi soat (giong luc dong ca tren web).
        INSERT dbo.WorkShifts(StaffId, StartTimeUtc, EndTimeUtc, StartingCash, EndingCash,
                              CashCollected, ExpectedCash, Status, Notes, ClosedById)
        VALUES (@StaffId, @StartUtc, @EndUtc, @StartCash, @Counted,
                @Cash, @StartCash + @Cash, 'Closed', @Note, @StaffId);
        DECLARE @ShiftId int = CONVERT(int, SCOPE_IDENTITY());

        -- Moi giao dich: 1 phien choi da dong -> 1 hoa don Paid -> 1 thanh toan gan voi ca.
        DECLARE @P int = 0, @Count int = (SELECT COUNT(*) FROM @Payments WHERE ShiftNo = @No);
        WHILE @P < @Count
        BEGIN
            DECLARE @Method varchar(20), @Amount decimal(18,2), @AtMinute int;
            SELECT @Method = Method, @Amount = Amount, @AtMinute = AtMinute
            FROM (SELECT *, ROW_NUMBER() OVER (ORDER BY AtMinute) - 1 AS Rn FROM @Payments WHERE ShiftNo = @No) x
            WHERE Rn = @P;

            DECLARE @PayAt datetime2(0) = DATEADD(minute, @AtMinute, @StartUtc);
            INSERT dbo.PlaySessions(TableId, CustomerId, OpenedById, ClosedById, StartAtUtc, EndAtUtc,
                                    HourlyRateSnapshot, PlaytimeAmount, Status)
            VALUES (@TableId, NULL, @StaffId, @StaffId, DATEADD(minute, -60, @PayAt), DATEADD(minute, -5, @PayAt),
                    @Amount, @Amount, 'Closed');
            DECLARE @SessionId int = CONVERT(int, SCOPE_IDENTITY());
            INSERT dbo.Invoices(SessionId, CustomerId, TotalPlaytime, TotalFandB, DiscountAmount, Status, CreatedAtUtc)
            VALUES (@SessionId, NULL, @Amount, 0, 0, 'Paid', DATEADD(minute, -5, @PayAt));
            DECLARE @InvoiceId int = CONVERT(int, SCOPE_IDENTITY());
            INSERT dbo.PaymentTransactions(InvoiceId, PaymentMethod, AmountPaid, GatewayRefCode,
                                           TransactionTimeUtc, WorkShiftId, ReceivedById)
            VALUES (@InvoiceId, @Method, @Amount, CONCAT(N'BMS-DEMO-SHIFT-', @ShiftId, N'-', @P + 1),
                    @PayAt, @ShiftId, CASE WHEN @Method = 'Cash' THEN @StaffId END);
            SET @P += 1;
        END;
        SET @No += 1;
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
SET NOEXEC OFF;
GO
-- Kiem tra nhanh
SELECT s.Id, u.FullName, CONVERT(varchar(16), DATEADD(hour, 7, s.StartTimeUtc), 120) AS BatDau_VN,
       s.StartingCash, s.CashCollected, s.ExpectedCash, s.EndingCash,
       s.EndingCash - s.ExpectedCash AS ChenhLech, s.Status
FROM dbo.WorkShifts s JOIN dbo.AspNetUsers u ON u.Id = s.StaffId
WHERE s.Notes LIKE N'[[]DEMO]%' ORDER BY s.StartTimeUtc;
