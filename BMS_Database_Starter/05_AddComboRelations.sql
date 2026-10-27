/* Additive upgrade for the existing 21-table BilliardDB.
   Target checked in this task: localhost, NOT localhost\SQLEXPRESS.
   Creates three empty tables. Does not reseed, delete or change existing rows.
   Run once. An already/partially applied upgrade is rejected for inspection.
*/
USE [BilliardDB];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;

BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dbo.Combos', N'U') IS NULL
       OR OBJECT_ID(N'dbo.Products', N'U') IS NULL
       OR OBJECT_ID(N'dbo.PlaySessions', N'U') IS NULL
       OR OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NULL
        THROW 51700, N'Required base tables are missing. Check server/database; do not rerun the full initializer.', 1;

    IF OBJECT_ID(N'dbo.ComboItems') IS NOT NULL
       OR OBJECT_ID(N'dbo.SessionCombos') IS NOT NULL
       OR OBJECT_ID(N'dbo.SessionComboItems') IS NOT NULL
        THROW 51701, N'Combo upgrade objects already exist. Stop and inspect; no existing tables will be replaced.', 1;

    -- BEGIN COMBO RELATIONS
    -- Catalog: which products are included in ONE unit of a combo.
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

    -- Purchase: preserve agreed name, price and playtime per combo unit.
    -- Multiple purchases per session are supported; this does not decide pricing policy.
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

    -- Purchase contents: copy included products at purchase time.
    -- Do not recalculate past purchases from the editable ComboItems catalog.
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
    -- END COMBO RELATIONS

    COMMIT;
    PRINT N'Added ComboItems, SessionCombos and SessionComboItems. Existing rows unchanged.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;
GO

SELECT DB_NAME() AS DatabaseName, name AS ComboTable
FROM sys.tables WHERE name IN (N'Combos', N'ComboItems', N'SessionCombos', N'SessionComboItems');
SELECT name AS ForeignKeyName, is_disabled, is_not_trusted
FROM sys.foreign_keys WHERE parent_object_id IN
    (OBJECT_ID(N'dbo.ComboItems'), OBJECT_ID(N'dbo.SessionCombos'), OBJECT_ID(N'dbo.SessionComboItems'));
