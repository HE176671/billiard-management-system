/* Integration check after 05_AddComboRelations.sql, on localhost / BilliardDB.
   All test rows are rolled back. No existing business rows are edited.
   Identity counters can advance even after rollback; gaps are normal.
   This checks schema/snapshot storage, NOT the unimplemented purchase service.
*/
USE [BilliardDB];
GO
SET NOCOUNT ON;
SET XACT_ABORT OFF; -- Expected CHECK/FK failures must allow rollback at the end.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;

DECLARE @BeforeProducts int = (SELECT COUNT(*) FROM dbo.Products);
DECLARE @BeforeCombos int = (SELECT COUNT(*) FROM dbo.Combos);
DECLARE @BeforeItems int = (SELECT COUNT(*) FROM dbo.ComboItems);
DECLARE @BeforePurchases int = (SELECT COUNT(*) FROM dbo.SessionCombos);
DECLARE @BeforePurchasedItems int = (SELECT COUNT(*) FROM dbo.SessionComboItems);
DECLARE @SessionId int, @CreatedById nvarchar(450), @CategoryId int;
SELECT TOP (1) @SessionId = Id, @CreatedById = OpenedById
FROM dbo.PlaySessions WHERE Status = 'Active' ORDER BY Id;
SELECT TOP (1) @CategoryId = Id FROM dbo.ProductCategories ORDER BY Id;
IF @SessionId IS NULL OR @CategoryId IS NULL
    THROW 51800, N'Test requires an existing active session and product category.', 1;

DECLARE @Tag nvarchar(36) = CONVERT(nvarchar(36), NEWID());
DECLARE @WaterId int, @FoodId int, @ComboId int, @PurchaseId int;
DECLARE @MissingProductId int = -2147483648;
IF EXISTS (SELECT 1 FROM dbo.Products WHERE Id = @MissingProductId)
    THROW 51801, N'Test sentinel product ID is already in use.', 1;

BEGIN TRY
    BEGIN TRANSACTION;
    INSERT dbo.Products(CategoryId, Name, Price, StockQuantity)
    VALUES (@CategoryId, N'TEST water ' + @Tag, 10000, 20);
    SET @WaterId = CONVERT(int, SCOPE_IDENTITY());
    INSERT dbo.Products(CategoryId, Name, Price, StockQuantity)
    VALUES (@CategoryId, N'TEST food ' + @Tag, 30000, 20);
    SET @FoodId = CONVERT(int, SCOPE_IDENTITY());
    INSERT dbo.Combos(Name, Price, PlaytimeHours)
    VALUES (N'TEST combo ' + @Tag, 200000, 2);
    SET @ComboId = CONVERT(int, SCOPE_IDENTITY());
    INSERT dbo.ComboItems(ComboId, ProductId, Quantity)
    VALUES (@ComboId, @WaterId, 2), (@ComboId, @FoodId, 1);

    INSERT dbo.SessionCombos(SessionId, ComboId, Quantity, ComboNameSnapshot,
                            UnitPriceSnapshot, PlaytimeHoursSnapshot, CreatedById)
    SELECT @SessionId, Id, 2, Name, Price, PlaytimeHours, @CreatedById
    FROM dbo.Combos WHERE Id = @ComboId;
    SET @PurchaseId = CONVERT(int, SCOPE_IDENTITY());
    INSERT dbo.SessionComboItems(SessionComboId, ProductId, ProductNameSnapshot, QuantityPerCombo)
    SELECT @PurchaseId, p.Id, p.Name, ci.Quantity
    FROM dbo.ComboItems ci JOIN dbo.Products p ON p.Id = ci.ProductId
    WHERE ci.ComboId = @ComboId;

    -- Change only this transaction's temporary catalog rows.
    UPDATE dbo.Combos SET Name = N'TEST changed ' + @Tag, Price = 300000, PlaytimeHours = 3
    WHERE Id = @ComboId;
    UPDATE dbo.ComboItems SET Quantity = 9 WHERE ComboId = @ComboId AND ProductId = @WaterId;
    UPDATE dbo.Products SET Name = N'TEST renamed water ' + @Tag WHERE Id = @WaterId;

    IF NOT EXISTS (SELECT 1 FROM dbo.SessionCombos WHERE Id = @PurchaseId
                   AND ComboNameSnapshot = N'TEST combo ' + @Tag
                   AND Quantity * UnitPriceSnapshot = 400000
                   AND Quantity * PlaytimeHoursSnapshot = 4)
        THROW 51802, N'Purchased price/name/playtime snapshots changed unexpectedly.', 1;
    IF (SELECT COUNT(*) FROM dbo.SessionComboItems WHERE SessionComboId = @PurchaseId) <> 2
       OR NOT EXISTS (SELECT 1 FROM dbo.SessionComboItems WHERE SessionComboId = @PurchaseId
                       AND ProductId = @WaterId AND QuantityPerCombo = 2
                       AND ProductNameSnapshot = N'TEST water ' + @Tag)
       OR NOT EXISTS (SELECT 1 FROM dbo.SessionComboItems WHERE SessionComboId = @PurchaseId
                       AND ProductId = @FoodId AND QuantityPerCombo = 1)
        THROW 51803, N'Purchased food/drink snapshots changed unexpectedly.', 1;

    DECLARE @Rejected bit = 0;
    BEGIN TRY
        INSERT dbo.ComboItems(ComboId, ProductId, Quantity)
        VALUES (@ComboId, @MissingProductId, 1);
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 547 THROW;
        SET @Rejected = 1;
    END CATCH;
    IF @Rejected = 0 THROW 51804, N'Missing product was accepted.', 1;

    SET @Rejected = 0;
    BEGIN TRY
        UPDATE dbo.ComboItems SET Quantity = 0 WHERE ComboId = @ComboId AND ProductId = @WaterId;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 547 THROW;
        SET @Rejected = 1;
    END CATCH;
    IF @Rejected = 0 THROW 51805, N'Zero quantity was accepted.', 1;

    SET @Rejected = 0;
    BEGIN TRY
        DELETE dbo.Combos WHERE Id = @ComboId;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 547 THROW;
        SET @Rejected = 1;
    END CATCH;
    IF @Rejected = 0 THROW 51806, N'Referenced combo was deleted.', 1;

    ROLLBACK;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;

IF @BeforeProducts <> (SELECT COUNT(*) FROM dbo.Products)
   OR @BeforeCombos <> (SELECT COUNT(*) FROM dbo.Combos)
   OR @BeforeItems <> (SELECT COUNT(*) FROM dbo.ComboItems)
   OR @BeforePurchases <> (SELECT COUNT(*) FROM dbo.SessionCombos)
   OR @BeforePurchasedItems <> (SELECT COUNT(*) FROM dbo.SessionComboItems)
    THROW 51807, N'Row counts differ after rollback; inspect concurrent writes.', 1;

PRINT N'PASS: catalog edits preserve purchase snapshots; invalid FK/quantity and referenced deletion rejected; all test rows rolled back.';
GO
