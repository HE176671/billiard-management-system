-- Additive upgrade for existing expanded BilliardDB; safe to run again.
USE BilliardDB;
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.MembershipTiers', 'U') IS NULL
    THROW 50001, 'MembershipTiers missing. Check the SQL Server instance and expanded schema.', 1;
IF COL_LENGTH('dbo.MembershipTiers', 'MinPoints') IS NULL
BEGIN
    ALTER TABLE dbo.MembershipTiers ADD MinPoints int NOT NULL
        CONSTRAINT DF_MembershipTiers_MinPoints DEFAULT 0 WITH VALUES;
    EXEC('ALTER TABLE dbo.MembershipTiers ADD CONSTRAINT CK_MembershipTiers_MinPoints CHECK (MinPoints >= 0)');
END;
COMMIT TRANSACTION;
GO
SELECT Id, TierName, MinPoints, DiscountPercent FROM dbo.MembershipTiers ORDER BY MinPoints, Id;
