/* =====================================================================
   11_WF2_FnbInventoryOrdering.sql (WF2 — F&B Menu, Inventory Stock, Combo & QR Ordering)
   Người phụ trách: Tùng (Software Engineer — Thành viên 4)
   Idempotent, additive migration script. Safe to re-run at any time.
   Run: sqlcmd -S "localhost" -d BilliardDB -E -C -i BMS_Database_Starter\11_WF2_FnbInventoryOrdering.sql
   ===================================================================== */
USE BilliardDB;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

-- ═════════════════════════════════════════════════════════════════════
-- BATCH 1: Tạo các bảng mới cho module F&B, Kho và Lịch sử giá (WF2)
-- ═════════════════════════════════════════════════════════════════════
BEGIN TRANSACTION;

-- 1.1. Bảng lịch sử thay đổi giá bán sản phẩm (BR-15)
IF OBJECT_ID('dbo.ProductPriceHistory', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProductPriceHistory (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProductPriceHistory PRIMARY KEY,
        ProductId int NOT NULL CONSTRAINT FK_ProductPriceHistory_Product FOREIGN KEY REFERENCES dbo.Products(Id),
        OldPrice decimal(18,2) NOT NULL,
        NewPrice decimal(18,2) NOT NULL,
        EffectiveDateUtc datetime2(0) NOT NULL CONSTRAINT DF_ProductPriceHistory_Date DEFAULT SYSUTCDATETIME(),
        ChangedById nvarchar(450) NULL CONSTRAINT FK_ProductPriceHistory_User FOREIGN KEY REFERENCES dbo.AspNetUsers(Id),
        Note nvarchar(255) NULL
    );
    CREATE INDEX IX_ProductPriceHistory_ProductId ON dbo.ProductPriceHistory(ProductId);
END;

-- 1.2. Bảng phiếu nhập kho F&B (UC-11)
IF OBJECT_ID('dbo.StockReceipts', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.StockReceipts (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_StockReceipts PRIMARY KEY,
        ReceiptCode nvarchar(50) NOT NULL CONSTRAINT UQ_StockReceipts_Code UNIQUE,
        SupplierName nvarchar(100) NOT NULL,
        TotalAmount decimal(18,2) NOT NULL CONSTRAINT DF_StockReceipts_Total DEFAULT 0,
        Note nvarchar(500) NULL,
        ReceiptDateUtc datetime2(0) NOT NULL CONSTRAINT DF_StockReceipts_Date DEFAULT SYSUTCDATETIME(),
        CreatedById nvarchar(450) NOT NULL CONSTRAINT FK_StockReceipts_User FOREIGN KEY REFERENCES dbo.AspNetUsers(Id)
    );
    CREATE INDEX IX_StockReceipts_Date ON dbo.StockReceipts(ReceiptDateUtc);
END;

-- 1.3. Bảng chi tiết phiếu nhập kho F&B (UC-11)
IF OBJECT_ID('dbo.StockReceiptDetails', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.StockReceiptDetails (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_StockReceiptDetails PRIMARY KEY,
        ReceiptId int NOT NULL CONSTRAINT FK_StockReceiptDetails_Receipt FOREIGN KEY REFERENCES dbo.StockReceipts(Id) ON DELETE CASCADE,
        ProductId int NOT NULL CONSTRAINT FK_StockReceiptDetails_Product FOREIGN KEY REFERENCES dbo.Products(Id),
        Quantity int NOT NULL CONSTRAINT CK_StockReceiptDetails_Qty CHECK (Quantity > 0),
        CostPrice decimal(18,2) NOT NULL CONSTRAINT CK_StockReceiptDetails_Cost CHECK (CostPrice >= 0),
        Subtotal AS (Quantity * CostPrice)
    );
    CREATE INDEX IX_StockReceiptDetails_ReceiptId ON dbo.StockReceiptDetails(ReceiptId);
    CREATE INDEX IX_StockReceiptDetails_ProductId ON dbo.StockReceiptDetails(ProductId);
END;

-- 1.4. Bảng thẻ kho ghi nhận biến động tồn kho (Stock Movements - UC-12)
IF OBJECT_ID('dbo.StockMovements', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.StockMovements (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_StockMovements PRIMARY KEY,
        ProductId int NOT NULL CONSTRAINT FK_StockMovements_Product FOREIGN KEY REFERENCES dbo.Products(Id),
        QuantityChange int NOT NULL,
        BalanceAfter int NOT NULL,
        MovementType varchar(30) NOT NULL,
        ReferenceType varchar(30) NULL,
        ReferenceId int NULL,
        Note nvarchar(300) NULL,
        CreatedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_StockMovements_Date DEFAULT SYSUTCDATETIME(),
        CreatedById nvarchar(450) NULL CONSTRAINT FK_StockMovements_User FOREIGN KEY REFERENCES dbo.AspNetUsers(Id)
    );
    CREATE INDEX IX_StockMovements_Product ON dbo.StockMovements(ProductId, CreatedAtUtc DESC);
    CREATE INDEX IX_StockMovements_Ref ON dbo.StockMovements(ReferenceType, ReferenceId);
END;

-- ═════════════════════════════════════════════════════════════════════
-- BATCH 2: Mở rộng các bảng hiện tại (Combos, Orders, OrderDetails, BilliardTables)
-- ═════════════════════════════════════════════════════════════════════

-- 2.1. Thêm TableTypeId vào Combos nếu áp dụng cho loại bàn cụ thể
IF COL_LENGTH('dbo.Combos', 'TableTypeId') IS NULL
BEGIN
    ALTER TABLE dbo.Combos ADD TableTypeId int NULL;
    ALTER TABLE dbo.Combos ADD CONSTRAINT FK_Combos_TableType FOREIGN KEY (TableTypeId) REFERENCES dbo.TableTypes(Id);
END;

-- 2.2. Mở rộng Orders: nguồn đơn (Staff/QR), mã phiên quét QR, lý do từ chối đơn
IF COL_LENGTH('dbo.Orders', 'OrderSource') IS NULL
    ALTER TABLE dbo.Orders ADD OrderSource varchar(20) NOT NULL CONSTRAINT DF_Orders_Source DEFAULT 'Staff';
IF COL_LENGTH('dbo.Orders', 'ClientSessionId') IS NULL
    ALTER TABLE dbo.Orders ADD ClientSessionId int NULL;
IF COL_LENGTH('dbo.Orders', 'RejectReason') IS NULL
    ALTER TABLE dbo.Orders ADD RejectReason nvarchar(300) NULL;
IF COL_LENGTH('dbo.Orders', 'Note') IS NULL
    ALTER TABLE dbo.Orders ADD Note nvarchar(300) NULL;
IF COL_LENGTH('dbo.Orders', 'CreatedById') IS NULL
BEGIN
    ALTER TABLE dbo.Orders ADD CreatedById nvarchar(450) NULL;
    ALTER TABLE dbo.Orders ADD CONSTRAINT FK_Orders_CreatedBy FOREIGN KEY (CreatedById) REFERENCES dbo.AspNetUsers(Id);
END;

-- Ràng buộc trạng thái đơn hàng F&B
IF EXISTS (SELECT * FROM sys.check_constraints WHERE name = 'CK_Order_Status')
BEGIN
    ALTER TABLE dbo.Orders DROP CONSTRAINT CK_Order_Status;
    ALTER TABLE dbo.Orders ADD CONSTRAINT CK_Order_Status CHECK (Status IN ('Pending', 'Preparing', 'Served', 'Completed', 'Cancelled', 'Rejected'));
END;

-- Trạng thái bàn: hỗ trợ AwaitingPayment khi khách yêu cầu thanh toán
IF EXISTS (SELECT * FROM sys.check_constraints WHERE name = 'CK_Table_Status')
BEGIN
    ALTER TABLE dbo.BilliardTables DROP CONSTRAINT CK_Table_Status;
    ALTER TABLE dbo.BilliardTables ADD CONSTRAINT CK_Table_Status CHECK (Status IN ('Available', 'InUse', 'AwaitingPayment', 'Maintenance'));
END;

-- 2.3. Mở rộng OrderDetails: huỷ từng món, lưu lý do và thời gian huỷ món
IF COL_LENGTH('dbo.OrderDetails', 'Status') IS NULL
    ALTER TABLE dbo.OrderDetails ADD Status varchar(20) NOT NULL CONSTRAINT DF_OrderDetails_Status DEFAULT 'Active';
IF COL_LENGTH('dbo.OrderDetails', 'CancelledQuantity') IS NULL
    ALTER TABLE dbo.OrderDetails ADD CancelledQuantity int NOT NULL CONSTRAINT DF_OrderDetails_CancelledQty DEFAULT 0;
IF COL_LENGTH('dbo.OrderDetails', 'CancelledAtUtc') IS NULL
    ALTER TABLE dbo.OrderDetails ADD CancelledAtUtc datetime2(0) NULL;
IF COL_LENGTH('dbo.OrderDetails', 'CancelledById') IS NULL
BEGIN
    ALTER TABLE dbo.OrderDetails ADD CancelledById nvarchar(450) NULL;
    ALTER TABLE dbo.OrderDetails ADD CONSTRAINT FK_OrderDetails_CancelledBy FOREIGN KEY (CancelledById) REFERENCES dbo.AspNetUsers(Id);
END;
IF COL_LENGTH('dbo.OrderDetails', 'CancelReason') IS NULL
    ALTER TABLE dbo.OrderDetails ADD CancelReason nvarchar(300) NULL;

COMMIT TRANSACTION;
GO

-- ═════════════════════════════════════════════════════════════════════
-- BATCH 3: Cập nhật Stored Procedure usp_AddSessionCombo (BR-07a)
-- Kiểm tra tồn kho và trừ thẻ kho nguyên tử cho món kèm Combo
-- ═════════════════════════════════════════════════════════════════════
CREATE OR ALTER PROCEDURE dbo.usp_AddSessionCombo
    @SessionId int,
    @ComboId int,
    @Quantity int = 1,
    @StaffId nvarchar(450)
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers u JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
        JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
        WHERE u.Id = @StaffId AND u.IsActive = 1 AND r.Name IN (N'Staff',N'Manager',N'Admin'))
        THROW 51711, N'Active Staff or Admin required.', 1;

    IF @Quantity IS NULL OR @Quantity <> 1
        THROW 51712, N'Each session can only apply 1 Combo package (Quantity = 1).', 1;

    DECLARE @TrancountAtStart int = @@TRANCOUNT;
    IF @TrancountAtStart = 0
        BEGIN TRANSACTION;
    ELSE
        SAVE TRANSACTION usp_AddSessionCombo_Savepoint;

    BEGIN TRY
        DECLARE @Now datetime2(0) = SYSUTCDATETIME(),
                @SessionStatus varchar(20),
                @ComboName nvarchar(100),
                @ComboPrice decimal(18,2),
                @ComboHours decimal(5,2),
                @ComboActive bit,
                @SessionComboId int,
                @EstimatedAmount decimal(18,2);

        SELECT @SessionStatus = Status
          FROM dbo.PlaySessions WITH (UPDLOCK, HOLDLOCK)
         WHERE Id = @SessionId;

        IF @SessionStatus IS NULL OR @SessionStatus <> 'Active'
            THROW 51713, N'Only an Active session can purchase a combo.', 1;

        IF EXISTS (SELECT 1 FROM dbo.SessionCombos WHERE SessionId = @SessionId)
            THROW 51715, N'This session already has an applied Combo package. Only 1 Combo is allowed per session.', 1;

        SELECT @ComboName = Name,
               @ComboPrice = Price,
               @ComboHours = PlaytimeHours,
               @ComboActive = IsActive
          FROM dbo.Combos WITH (UPDLOCK, HOLDLOCK)
         WHERE Id = @ComboId;

        IF @ComboName IS NULL OR @ComboActive <> 1
            THROW 51714, N'Selected combo does not exist or is no longer active.', 1;

        -- BR-07a: Kiểm tra tồn kho của tất cả món kèm trong gói Combo
        IF EXISTS (
            SELECT 1
            FROM dbo.ComboItems ci
            JOIN dbo.Products p WITH (UPDLOCK, ROWLOCK) ON p.Id = ci.ProductId
            WHERE ci.ComboId = @ComboId
              AND p.StockQuantity < (ci.Quantity * @Quantity)
        )
            THROW 51716, N'Món kèm trong gói Combo đã hết hàng trong kho. Không thể áp dụng Combo này!', 1;

        INSERT dbo.SessionCombos (
            SessionId, ComboId, Quantity,
            ComboNameSnapshot, UnitPriceSnapshot, PlaytimeHoursSnapshot,
            PurchasedAtUtc, CreatedById
        )
        VALUES (
            @SessionId, @ComboId, @Quantity,
            @ComboName, @ComboPrice, @ComboHours,
            @Now, @StaffId
        );

        SET @SessionComboId = CONVERT(int, SCOPE_IDENTITY());

        INSERT dbo.SessionComboItems (
            SessionComboId, ProductId, ProductNameSnapshot, QuantityPerCombo
        )
        SELECT @SessionComboId, ci.ProductId, p.Name, ci.Quantity
          FROM dbo.ComboItems ci
          JOIN dbo.Products p ON p.Id = ci.ProductId
         WHERE ci.ComboId = @ComboId;

        -- BR-07a: Trừ kho nguyên tử và ghi nhận thẻ kho StockMovements cho từng món kèm
        DECLARE @ItemProdId int, @ItemReqQty int, @NewBal int;
        DECLARE item_cursor CURSOR LOCAL FAST_FORWARD FOR
            SELECT ci.ProductId, ci.Quantity * @Quantity
            FROM dbo.ComboItems ci
            WHERE ci.ComboId = @ComboId;

        OPEN item_cursor;
        FETCH NEXT FROM item_cursor INTO @ItemProdId, @ItemReqQty;
        WHILE @@FETCH_STATUS = 0
        BEGIN
            UPDATE dbo.Products
            SET StockQuantity = StockQuantity - @ItemReqQty
            WHERE Id = @ItemProdId;

            SELECT @NewBal = StockQuantity FROM dbo.Products WHERE Id = @ItemProdId;

            INSERT INTO dbo.StockMovements (
                ProductId, QuantityChange, BalanceAfter, MovementType,
                ReferenceType, ReferenceId, Note, CreatedAtUtc, CreatedById
            )
            VALUES (
                @ItemProdId, -@ItemReqQty, @NewBal, 'ComboDeduct',
                'SessionCombo', @SessionComboId,
                N'Trừ kho món kèm theo gói Combo ' + @ComboName,
                @Now, @StaffId
            );

            FETCH NEXT FROM item_cursor INTO @ItemProdId, @ItemReqQty;
        END;
        CLOSE item_cursor;
        DEALLOCATE item_cursor;

        SET @EstimatedAmount = dbo.fn_CalcPlaytimeAmount(@SessionId, @Now);

        IF @TrancountAtStart = 0
            COMMIT;

        SELECT @SessionComboId AS SessionComboId,
               @SessionId AS SessionId,
               @ComboId AS ComboId,
               @Quantity AS Quantity,
               @ComboName AS ComboNameSnapshot,
               @ComboPrice AS UnitPriceSnapshot,
               @ComboHours AS PlaytimeHoursSnapshot,
               @Now AS PurchasedAtUtc,
               @EstimatedAmount AS EstimatedAmount;
    END TRY
    BEGIN CATCH
        IF @TrancountAtStart = 0
        BEGIN
            IF @@TRANCOUNT > 0 ROLLBACK;
        END;
        ELSE
        BEGIN
            IF XACT_STATE() <> -1
                ROLLBACK TRANSACTION usp_AddSessionCombo_Savepoint;
        END;
        THROW;
    END CATCH
END;
GO

PRINT N'✓ Script 11_WF2_FnbInventoryOrdering đã được áp dụng thành công!';
GO
