/* =====================================================================
   06_TV2_AlignLoyaltySchema.sql  (TV2 - Loyalty / Reviews / Notifications)
   ===================================================================== */
USE BilliardDB;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

-- 1) Reviews.AdminReply -------------------------------------------------
IF COL_LENGTH('dbo.Reviews', 'AdminReply') IS NULL
    ALTER TABLE dbo.Reviews ADD AdminReply nvarchar(max) NULL;
GO

BEGIN TRAN;

-- 2) MembershipTiers ----------------------------------------------------
-- Member: 0 d, giam 0%, x1 | Silver: 2.000.000 d, giam 5%, x1 | Gold: 5.000.000 d, giam 10%, x2
UPDATE dbo.MembershipTiers SET TierName = N'Member', SpendThreshold = 0,       DiscountPercent = 0,  PointMultiplier = 1 WHERE Id = 1;
UPDATE dbo.MembershipTiers SET TierName = N'Silver', SpendThreshold = 2000000, DiscountPercent = 5,  PointMultiplier = 1 WHERE Id = 2;
UPDATE dbo.MembershipTiers SET TierName = N'Gold',   SpendThreshold = 5000000, DiscountPercent = 10, PointMultiplier = 2 WHERE Id = 3;

-- Xep lai hang cho khach hang (va moi user dang co TierId) theo TotalSpend, TRUOC khi xoa hang cu
UPDATE u
SET u.TierId = (SELECT TOP 1 t.Id FROM dbo.MembershipTiers t
                WHERE t.Id IN (1, 2, 3) AND t.SpendThreshold <= u.TotalSpend
                ORDER BY t.SpendThreshold DESC)
FROM dbo.AspNetUsers u
WHERE u.TierId IS NOT NULL
   OR EXISTS (SELECT 1 FROM dbo.AspNetUserRoles ur
              JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
              WHERE ur.UserId = u.Id AND r.Name = N'Customer');

-- Hang thu 4 (Kim Cuong) khong thuoc spec -> xoa
IF EXISTS (SELECT 1 FROM dbo.MembershipTiers WHERE Id = 4)
    DELETE FROM dbo.MembershipTiers WHERE Id = 4;

COMMIT TRAN;
GO

SELECT Id, TierName, SpendThreshold, DiscountPercent, PointMultiplier FROM dbo.MembershipTiers ORDER BY SpendThreshold;
SELECT UserName, TotalSpend, TierId FROM dbo.AspNetUsers WHERE TierId IS NOT NULL;
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Reviews' ORDER BY ORDINAL_POSITION;
GO
