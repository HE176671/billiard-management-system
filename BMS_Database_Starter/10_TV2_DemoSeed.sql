/* =====================================================================
   10_TV2_DemoSeed.sql  (TV2 — Dữ liệu thực tế cho CRM Dashboard & Demo 5 phút)
   Idempotent, additive seed:
   - Tạo ~30 hội viên với LastPaidDate & bút toán Earn có SpendAmount rải đều trong 12 tháng
     để mọi phân khúc RFM (Champion, Loyal, AtRisk, Lost, New, Regular) đều có dữ liệu thật.
   - Tạo 2 khách có sinh nhật hôm nay / ngày mai (hạng Silver/Gold).
   - Tạo 1 khách Gold sắp đến hạn xét lại hạng (TierReviewAtUtc).
   Run: sqlcmd -S "localhost\SQLEXPRESS" -d BilliardDB -E -C -i BMS_Database_Starter\10_TV2_DemoSeed.sql
   ===================================================================== */
USE BilliardDB;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

DECLARE @CustomerRoleId nvarchar(450) = (SELECT TOP 1 Id FROM dbo.AspNetRoles WHERE NormalizedName = N'CUSTOMER');
DECLARE @MemberTierId int = (SELECT TOP 1 Id FROM dbo.MembershipTiers WHERE SpendThreshold = 0 ORDER BY Id);
DECLARE @SilverTierId int = (SELECT TOP 1 Id FROM dbo.MembershipTiers WHERE TierName IN (N'Silver', N'Bạc') ORDER BY SpendThreshold);
DECLARE @GoldTierId   int = (SELECT TOP 1 Id FROM dbo.MembershipTiers WHERE TierName IN (N'Gold', N'Vàng') ORDER BY SpendThreshold DESC);

IF @SilverTierId IS NULL SET @SilverTierId = @MemberTierId;
IF @GoldTierId IS NULL   SET @GoldTierId   = @SilverTierId;

DECLARE @NowUtc datetime2(0) = SYSUTCDATETIME();
DECLARE @VnToday date = CONVERT(date, DATEADD(hour, 7, @NowUtc));
DECLARE @VnTomorrow date = DATEADD(day, 1, @VnToday);
DECLARE @BdayToday date = DATEFROMPARTS(1996, MONTH(@VnToday), DAY(@VnToday));
DECLARE @BdayTomorrow date = DATEFROMPARTS(1998, MONTH(@VnTomorrow), DAY(@VnTomorrow));

DECLARE @SeedUsers TABLE (
    Seq int,
    Id nvarchar(450),
    Phone nvarchar(20),
    FullName nvarchar(100),
    CardCode nvarchar(20),
    TierId int,
    CreatedDaysAgo int,
    LastPaidDaysAgo int NULL,
    TxCount int,
    SpendPerTx decimal(18,2),
    Dob date NULL,
    AccountSource varchar(20),
    ReviewOffsetDays int
);

-- Nhóm 1: Champion (R <= 14, F >= 8) — 5 khách
INSERT INTO @SeedUsers VALUES
(1,  N'tv2-crm-01', N'0912000101', N'Nguyễn Minh Tuấn',   N'MB900101', @GoldTierId,   240, 2,  9, 650000, @BdayToday,    'Self',    300),
(2,  N'tv2-crm-02', N'0912000102', N'Trần Quốc Bảo',      N'MB900102', @GoldTierId,   220, 4,  8, 700000, '1992-05-14',  'Self',    280),
(3,  N'tv2-crm-03', N'0912000103', N'Lê Hoàng Nam',       N'MB900103', @GoldTierId,   200, 6,  8, 680000, '1991-08-22',  'Counter', 250),
(4,  N'tv2-crm-04', N'0912000104', N'Phạm Đức Anh',       N'MB900104', @GoldTierId,   190, 9,  8, 750000, '1989-11-03',  'Self',    210),
(5,  N'tv2-crm-05', N'0912000105', N'Hoàng Gia Huy',      N'MB900105', @SilverTierId, 180, 11, 8, 420000, @BdayTomorrow, 'Counter', 190);

-- Nhóm 2: Loyal (R <= 30, F >= 3) — 6 khách
INSERT INTO @SeedUsers VALUES
(6,  N'tv2-crm-06', N'0912000106', N'Đỗ Văn Hùng',        N'MB900106', @SilverTierId, 160, 5,  5, 480000, '1995-03-19',  'Self',    200),
(7,  N'tv2-crm-07', N'0912000107', N'Vũ Thanh Tùng',      N'MB900107', @SilverTierId, 150, 12, 4, 550000, '1996-04-14',  'Counter', 180),
(8,  N'tv2-crm-08', N'0912000108', N'Nguyễn Tuấn Kiệt',   N'MB900108', @SilverTierId, 140, 18, 4, 520000, '1997-10-20',  'Self',    170),
(9,  N'tv2-crm-09', N'0912000109', N'Bùi Quang Hải',      N'MB900109', @MemberTierId, 130, 21, 3, 380000, '1995-10-02',  'Counter', 160),
(10, N'tv2-crm-10', N'0912000110', N'Phan Minh Đức',      N'MB900110', @MemberTierId, 120, 25, 3, 400000, '1996-04-11',  'Self',    150),
(11, N'tv2-crm-11', N'0912000111', N'Đoàn Nhật Minh',     N'MB900111', @SilverTierId, 110, 27, 5, 450000, '1999-04-19',  'Self',    140);

-- Nhóm 3: AtRisk (30 < R <= 90, F >= 2) — 6 khách
INSERT INTO @SeedUsers VALUES
(12, N'tv2-crm-12', N'0912000112', N'Nguyễn Thành Đạt',   N'MB900112', @GoldTierId,   300, 42, 4, 1300000, '1995-01-21', 'Self',   120),
(13, N'tv2-crm-13', N'0912000113', N'Lương Thế Vinh',     N'MB900113', @SilverTierId, 280, 48, 3, 850000,  '1995-04-28', 'Self',   100),
(14, N'tv2-crm-14', N'0912000114', N'Nguyễn Hữu Phước',   N'MB900114', @SilverTierId, 260, 55, 3, 720000,  '1995-05-16', 'Counter', 90),
(15, N'tv2-crm-15', N'0912000115', N'Trịnh Văn Toàn',     N'MB900115', @SilverTierId, 250, 63, 3, 690000,  '1996-04-12', 'Self',    80),
(16, N'tv2-crm-16', N'0912000116', N'Hà Trọng Nghĩa',     N'MB900116', @MemberTierId, 210, 72, 2, 540000,  '1997-09-22', 'Counter', 70),
(17, N'tv2-crm-17', N'0912000117', N'Ngô Ngọc Hải',       N'MB900117', @GoldTierId,   360, 81, 2, 950000,  '1993-05-15', 'Self',    -1);

-- Nhóm 4: Lost (R > 90 hoặc chưa từng thanh toán & tài khoản > 30 ngày) — 5 khách
INSERT INTO @SeedUsers VALUES
(18, N'tv2-crm-18', N'0912000118', N'Đặng Minh Khôi',     N'MB900118', @MemberTierId, 290, 110, 2, 350000, '1993-08-13', 'Self',    60),
(19, N'tv2-crm-19', N'0912000119', N'Trần Mạnh Cường',    N'MB900119', @MemberTierId, 270, 135, 1, 420000, '1991-12-20', 'Counter', 50),
(20, N'tv2-crm-20', N'0912000120', N'Nguyễn Trọng Hoàng', N'MB900120', @MemberTierId, 250, 166, 2, 300000, '1989-04-14', 'Self',    40),
(21, N'tv2-crm-21', N'0912000121', N'Lê Quang Vinh',      N'MB900121', @MemberTierId, 240, 195, 1, 500000, '1985-12-10', 'Self',    30),
(22, N'tv2-crm-22', N'0912000122', N'Phạm Thành Lương',   N'MB900122', @MemberTierId, 180, NULL,0, 0,      '1988-09-10', 'Counter', 60);

-- Nhóm 5: New (Tạo <= 30 ngày và F <= 1) — 4 khách
INSERT INTO @SeedUsers VALUES
(23, N'tv2-crm-23', N'0912000123', N'Trương Gia Bảo',     N'MB900123', @MemberTierId, 5,   5,   1, 280000, '2003-05-11', 'Counter', 360),
(24, N'tv2-crm-24', N'0912000124', N'Nguyễn Đình Phong',  N'MB900124', @MemberTierId, 10,  10,  1, 320000, '2004-08-19', 'Counter', 355),
(25, N'tv2-crm-25', N'0912000125', N'Bùi Anh Quân',       N'MB900125', @MemberTierId, 14,  NULL,0, 0,      '2003-02-24', 'Counter', 350),
(26, N'tv2-crm-26', N'0912000126', N'Nguyễn Thái Sơn',    N'MB900126', @MemberTierId, 20,  18,  1, 240000, '2003-07-13', 'Self',    345);

-- Nhóm 6: Regular (Tài khoản > 30 ngày, R <= 90 nhưng F thấp) — 4 khách
INSERT INTO @SeedUsers VALUES
(27, N'tv2-crm-27', N'0912000127', N'Hồ Tấn Phát',        N'MB900127', @MemberTierId, 95,  14,  2, 360000, '1997-11-06', 'Self',    270),
(28, N'tv2-crm-28', N'0912000128', N'Nguyễn Thành Chung', N'MB900128', @MemberTierId, 85,  22,  2, 340000, '1997-09-08', 'Counter', 280),
(29, N'tv2-crm-29', N'0912000129', N'Phạm Tuấn Hải',      N'MB900129', @MemberTierId, 75,  38,  1, 460000, '1998-05-19', 'Self',    290),
(30, N'tv2-crm-30', N'0912000130', N'Nguyễn Hoàng Long',  N'MB900130', @MemberTierId, 65,  19,  2, 410000, '1998-01-11', 'Self',    300);

DECLARE @s int = 1;
WHILE @s <= 30
BEGIN
    DECLARE @Uid nvarchar(450), @Phone nvarchar(20), @Name nvarchar(100), @Card nvarchar(20),
            @Tid int, @CreatedAgo int, @LastAgo int, @TxCnt int, @SpendTx decimal(18,2),
            @Dob date, @Src varchar(20), @RevOff int;

    SELECT @Uid = Id, @Phone = Phone, @Name = FullName, @Card = CardCode, @Tid = TierId,
           @CreatedAgo = CreatedDaysAgo, @LastAgo = LastPaidDaysAgo, @TxCnt = TxCount,
           @SpendTx = SpendPerTx, @Dob = Dob, @Src = AccountSource, @RevOff = ReviewOffsetDays
    FROM @SeedUsers WHERE Seq = @s;

    IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers WHERE Id = @Uid OR PhoneNumber = @Phone OR EmployeeCode = @Card)
    BEGIN
        DECLARE @CreatedAt datetime2(0) = DATEADD(day, -@CreatedAgo, @NowUtc);
        DECLARE @LastPaid datetime2(0) = CASE WHEN @LastAgo IS NOT NULL THEN DATEADD(day, -@LastAgo, @NowUtc) ELSE NULL END;
        DECLARE @ReviewAt datetime2(0) = DATEADD(day, @RevOff, @NowUtc);
        DECLARE @TotalSp decimal(18,2) = @TxCnt * @SpendTx;
        DECLARE @PtsPerTx int = CONVERT(int, FLOOR(@SpendTx / 20000.0));
        DECLARE @TotPts int = CASE WHEN @TxCnt > 0 THEN @TxCnt * @PtsPerTx ELSE 10 END;
        DECLARE @Email nvarchar(256) = CASE WHEN @Src = 'Counter' THEN @Phone + N'@noemail.bms.local' ELSE @Uid + N'@demo.bms.local' END;

        INSERT INTO dbo.AspNetUsers (
            Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed,
            PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount,
            SecurityStamp, ConcurrencyStamp, FullName, IsActive, CreatedAtUtc, EmployeeCode,
            RewardPoints, TierId, TotalSpend, LastPaidDate, DateOfBirth, MarketingOptIn,
            AccountSource, TierAchievedAtUtc, TierReviewAtUtc
        )
        VALUES (
            @Uid, @Phone, UPPER(@Phone), @Email, UPPER(@Email), 1,
            @Phone, 1, 0, 1, 0,
            CONVERT(nvarchar(36), NEWID()), CONVERT(nvarchar(36), NEWID()), @Name, 1, @CreatedAt, @Card,
            @TotPts, @Tid, @TotalSp, @LastPaid, @Dob, 1,
            @Src, @CreatedAt, @ReviewAt
        );

        IF @CustomerRoleId IS NOT NULL
            INSERT INTO dbo.AspNetUserRoles (UserId, RoleId) VALUES (@Uid, @CustomerRoleId);

        -- Tạo các bút toán Earn rải trong 12 tháng để biểu đồ 6 tháng & RFM có số liệu thực
        IF @TxCnt > 0 AND @LastPaid IS NOT NULL
        BEGIN
            DECLARE @k int = 1;
            DECLARE @RunningBal int = 0;
            WHILE @k <= @TxCnt
            BEGIN
                SET @RunningBal += @PtsPerTx;
                DECLARE @TxDate datetime2(0) = DATEADD(day, -((@TxCnt - @k) * 14), @LastPaid);
                DECLARE @Ref varchar(50) = CONCAT('SEED10-', @Uid, '-', @k);

                INSERT INTO dbo.PointTransactions (
                    CustomerId, Amount, BalanceAfter, TransactionType, ReferenceCode,
                    Reason, SpendAmount, CreatedAt
                )
                VALUES (
                    @Uid, @PtsPerTx, @RunningBal, 'Earn', @Ref,
                    N'Thanh toán hóa đơn bàn bida', @SpendTx, @TxDate
                );
                SET @k += 1;
            END;
        END
        ELSE
        BEGIN
            INSERT INTO dbo.PointTransactions (
                CustomerId, Amount, BalanceAfter, TransactionType, ReferenceCode,
                Reason, SpendAmount, CreatedAt
            )
            VALUES (
                @Uid, 10, 10, 'Bonus', CONCAT('WELCOME-', @Uid),
                N'Điểm thưởng chào mừng hội viên mới', NULL, @CreatedAt
            );
        END;
    END
    ELSE
    BEGIN
        -- Đảm bảo sửa lại họ tên và lý do chuẩn Unicode nếu trước đó chạy thiếu cờ UTF-8
        UPDATE dbo.AspNetUsers SET FullName = @Name WHERE Id = @Uid OR PhoneNumber = @Phone;
        UPDATE dbo.PointTransactions SET Reason = N'Thanh toán hóa đơn bàn bida' WHERE CustomerId = @Uid AND ReferenceCode LIKE 'SEED10-%';
        UPDATE dbo.PointTransactions SET Reason = N'Điểm thưởng chào mừng hội viên mới' WHERE CustomerId = @Uid AND ReferenceCode LIKE 'WELCOME-%';
    END;

    SET @s += 1;
END;

-- Đổi tên các tài khoản seed mẫu ban đầu (nếu có) sang họ tên tự nhiên
UPDATE dbo.AspNetUsers SET FullName = N'Phạm Minh Quân' WHERE PhoneNumber = N'0900000004' OR FullName LIKE N'Khách mẫu 01%';
UPDATE dbo.AspNetUsers SET FullName = N'Trần Bảo Ngọc'  WHERE PhoneNumber = N'0900000005' OR FullName LIKE N'Khách mẫu 02%';
UPDATE dbo.AspNetUsers SET FullName = N'Lê Đức Mạnh'    WHERE PhoneNumber = N'0900000006' OR FullName LIKE N'Khách mẫu 03%';

COMMIT TRANSACTION;
GO

