/* BMS Starter v1 - SQL Server 2019+.
   Expanded 2026-10-01: 24 business tables, 5 procedures.
   Run the WHOLE file in SSMS only for a NEW database BilliardDB.
   No DROP/TRUNCATE; refuses to initialize a database that already has user tables.
   Identity schema V1, string keys, 128-character login/token provider keys.
   Schema owner is SQL scripts (not EF migrations) for this starter.
*/
USE [master];
GO
IF DB_ID(N'BilliardDB') IS NULL
    EXEC(N'CREATE DATABASE [BilliardDB]');
GO
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
    IF EXISTS (SELECT 1 FROM sys.tables WHERE is_ms_shipped = 0)
        THROW 51000, N'Database already contains tables. Stop: do not rerun initialization.', 1;

        CREATE TABLE dbo.MembershipTiers (
        Id int IDENTITY NOT NULL CONSTRAINT PK_MembershipTiers PRIMARY KEY,
        TierName nvarchar(50) NOT NULL CONSTRAINT UQ_MembershipTiers_Name UNIQUE,
        MinPoints int NOT NULL CONSTRAINT DF_MembershipTiers_MinPoints DEFAULT 0,
        DiscountPercent decimal(5,2) NOT NULL CONSTRAINT DF_MembershipTiers_Discount DEFAULT 0,
        CONSTRAINT CK_MembershipTiers_Name CHECK (LEN(LTRIM(RTRIM(TierName))) > 0),
        CONSTRAINT CK_MembershipTiers_Discount CHECK (DiscountPercent >= 0 AND DiscountPercent <= 100),
        CONSTRAINT CK_MembershipTiers_MinPoints CHECK (MinPoints >= 0)
    );

    CREATE TABLE dbo.AspNetRoles (
        Id nvarchar(450) NOT NULL CONSTRAINT PK_AspNetRoles PRIMARY KEY,
        Name nvarchar(256) NULL,
        NormalizedName nvarchar(256) NULL,
        ConcurrencyStamp nvarchar(max) NULL
    );
    CREATE UNIQUE INDEX RoleNameIndex ON dbo.AspNetRoles(NormalizedName)
        WHERE NormalizedName IS NOT NULL;

    CREATE TABLE dbo.AspNetUsers (
        Id nvarchar(450) NOT NULL CONSTRAINT PK_AspNetUsers PRIMARY KEY,
        UserName nvarchar(256) NULL,
        NormalizedUserName nvarchar(256) NULL,
        Email nvarchar(256) NULL,
        NormalizedEmail nvarchar(256) NULL,
        EmailConfirmed bit NOT NULL CONSTRAINT DF_User_EmailConfirmed DEFAULT 0,
        PasswordHash nvarchar(max) NULL,
        SecurityStamp nvarchar(max) NULL,
        ConcurrencyStamp nvarchar(max) NULL,
        PhoneNumber nvarchar(20) NULL,
        PhoneNumberConfirmed bit NOT NULL CONSTRAINT DF_User_PhoneConfirmed DEFAULT 0,
        TwoFactorEnabled bit NOT NULL CONSTRAINT DF_User_2FA DEFAULT 0,
        LockoutEnd datetimeoffset(7) NULL,
        LockoutEnabled bit NOT NULL CONSTRAINT DF_User_Lockout DEFAULT 1,
        AccessFailedCount int NOT NULL CONSTRAINT DF_User_Failed DEFAULT 0,
        FullName nvarchar(100) NOT NULL,
        IsActive bit NOT NULL CONSTRAINT DF_User_IsActive DEFAULT 1,
        CreatedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_User_Created DEFAULT SYSUTCDATETIME(),
        EmployeeCode nvarchar(20) NULL,
        RewardPoints int NOT NULL CONSTRAINT DF_User_Points DEFAULT 0,
        TierId int NULL,
        CONSTRAINT FK_User_MembershipTier FOREIGN KEY (TierId) REFERENCES dbo.MembershipTiers(Id),
        HireDate date NULL,
        CONSTRAINT CK_User_FullName CHECK (LEN(LTRIM(RTRIM(FullName))) > 0),
        CONSTRAINT CK_User_Phone CHECK (PhoneNumber IS NULL OR LEN(LTRIM(RTRIM(PhoneNumber))) > 0),
        CONSTRAINT CK_User_EmployeeCode CHECK (EmployeeCode IS NULL OR LEN(LTRIM(RTRIM(EmployeeCode))) > 0),
        CONSTRAINT CK_User_Failed CHECK (AccessFailedCount >= 0)
    );
    CREATE UNIQUE INDEX UserNameIndex ON dbo.AspNetUsers(NormalizedUserName)
        WHERE NormalizedUserName IS NOT NULL;
    -- Unique email is a BMS rule; set Identity User.RequireUniqueEmail = true.
    CREATE UNIQUE INDEX EmailIndex ON dbo.AspNetUsers(NormalizedEmail)
        WHERE NormalizedEmail IS NOT NULL;
    CREATE UNIQUE INDEX UX_User_Phone ON dbo.AspNetUsers(PhoneNumber)
        WHERE PhoneNumber IS NOT NULL;
    CREATE UNIQUE INDEX UX_User_EmployeeCode ON dbo.AspNetUsers(EmployeeCode)
        WHERE EmployeeCode IS NOT NULL;

    CREATE TABLE dbo.AspNetUserRoles (
        UserId nvarchar(450) NOT NULL,
        RoleId nvarchar(450) NOT NULL,
        CONSTRAINT PK_AspNetUserRoles PRIMARY KEY (UserId, RoleId),
        CONSTRAINT FK_UserRoles_User FOREIGN KEY (UserId) REFERENCES dbo.AspNetUsers(Id) ON DELETE CASCADE,
        CONSTRAINT FK_UserRoles_Role FOREIGN KEY (RoleId) REFERENCES dbo.AspNetRoles(Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_AspNetUserRoles_RoleId ON dbo.AspNetUserRoles(RoleId);
    CREATE TABLE dbo.AspNetUserClaims (
        Id int IDENTITY NOT NULL CONSTRAINT PK_AspNetUserClaims PRIMARY KEY,
        UserId nvarchar(450) NOT NULL,
        ClaimType nvarchar(max) NULL,
        ClaimValue nvarchar(max) NULL,
        CONSTRAINT FK_UserClaims_User FOREIGN KEY (UserId) REFERENCES dbo.AspNetUsers(Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_AspNetUserClaims_UserId ON dbo.AspNetUserClaims(UserId);
    CREATE TABLE dbo.AspNetRoleClaims (
        Id int IDENTITY NOT NULL CONSTRAINT PK_AspNetRoleClaims PRIMARY KEY,
        RoleId nvarchar(450) NOT NULL,
        ClaimType nvarchar(max) NULL,
        ClaimValue nvarchar(max) NULL,
        CONSTRAINT FK_RoleClaims_Role FOREIGN KEY (RoleId) REFERENCES dbo.AspNetRoles(Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_AspNetRoleClaims_RoleId ON dbo.AspNetRoleClaims(RoleId);
    CREATE TABLE dbo.AspNetUserLogins (
        LoginProvider nvarchar(128) NOT NULL,
        ProviderKey nvarchar(128) NOT NULL,
        ProviderDisplayName nvarchar(max) NULL,
        UserId nvarchar(450) NOT NULL,
        CONSTRAINT PK_AspNetUserLogins PRIMARY KEY (LoginProvider, ProviderKey),
        CONSTRAINT FK_UserLogins_User FOREIGN KEY (UserId) REFERENCES dbo.AspNetUsers(Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_AspNetUserLogins_UserId ON dbo.AspNetUserLogins(UserId);
    CREATE TABLE dbo.AspNetUserTokens (
        UserId nvarchar(450) NOT NULL,
        LoginProvider nvarchar(128) NOT NULL,
        Name nvarchar(128) NOT NULL,
        Value nvarchar(max) NULL,
        CONSTRAINT PK_AspNetUserTokens PRIMARY KEY (UserId, LoginProvider, Name),
        CONSTRAINT FK_UserTokens_User FOREIGN KEY (UserId) REFERENCES dbo.AspNetUsers(Id) ON DELETE CASCADE
    );

    CREATE TABLE dbo.TableTypes (
        Id int IDENTITY NOT NULL CONSTRAINT PK_TableTypes PRIMARY KEY,
        Name nvarchar(50) NOT NULL CONSTRAINT UQ_TableTypes_Name UNIQUE,
        HourlyRate decimal(18,2) NOT NULL,
        CONSTRAINT CK_TableTypes_Name CHECK (LEN(LTRIM(RTRIM(Name))) > 0),
        CONSTRAINT CK_TableTypes_Rate CHECK (HourlyRate > 0)
    );
    CREATE TABLE dbo.BilliardTables (
        Id int IDENTITY NOT NULL CONSTRAINT PK_BilliardTables PRIMARY KEY,
        TableCode nvarchar(20) NOT NULL CONSTRAINT UQ_BilliardTables_Code UNIQUE,
        TableTypeId int NOT NULL,
        FloorNumber int NOT NULL CONSTRAINT DF_Table_Floor DEFAULT 1,
        Status varchar(20) NOT NULL CONSTRAINT DF_Table_Status DEFAULT 'Available',
        RowVersion rowversion NOT NULL,
        CONSTRAINT FK_Table_Type FOREIGN KEY (TableTypeId) REFERENCES dbo.TableTypes(Id),
        CONSTRAINT CK_Table_Code CHECK (LEN(LTRIM(RTRIM(TableCode))) > 0),
        CONSTRAINT CK_Table_Floor CHECK (FloorNumber >= 0),
        CONSTRAINT CK_Table_Status CHECK (Status IN ('Available','InUse','AwaitingPayment','Maintenance','Inactive'))
    );
    CREATE INDEX IX_BilliardTables_Type_Status ON dbo.BilliardTables(TableTypeId, Status);

    CREATE TABLE dbo.Bookings (
        Id int IDENTITY NOT NULL CONSTRAINT PK_Bookings PRIMARY KEY,
        CustomerId nvarchar(450) NOT NULL,
        TableId int NOT NULL,
        StartAtUtc datetime2(0) NOT NULL,
        EndAtUtc datetime2(0) NOT NULL,
        Status varchar(20) NOT NULL CONSTRAINT DF_Booking_Status DEFAULT 'Confirmed',
        CreatedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_Booking_Created DEFAULT SYSUTCDATETIME(),
        CheckedInAtUtc datetime2(0) NULL,
        CheckedInById nvarchar(450) NULL,
        CancelledAtUtc datetime2(0) NULL,
        CancelledById nvarchar(450) NULL,
        CancellationReason nvarchar(300) NULL,
        Notes nvarchar(300) NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT FK_Booking_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.AspNetUsers(Id),
        CONSTRAINT FK_Booking_Table FOREIGN KEY (TableId) REFERENCES dbo.BilliardTables(Id),
        CONSTRAINT FK_Booking_CheckinStaff FOREIGN KEY (CheckedInById) REFERENCES dbo.AspNetUsers(Id),
        CONSTRAINT FK_Booking_CancelledBy FOREIGN KEY (CancelledById) REFERENCES dbo.AspNetUsers(Id),
        CONSTRAINT CK_Booking_Times CHECK (EndAtUtc > StartAtUtc),
        CONSTRAINT CK_Booking_Status CHECK (Status IN ('Confirmed','CheckedIn','Completed','Cancelled','NoShow')),
        CONSTRAINT CK_Booking_Checkin CHECK (
            (CheckedInAtUtc IS NULL AND CheckedInById IS NULL) OR
            (CheckedInAtUtc IS NOT NULL AND CheckedInById IS NOT NULL)),
        CONSTRAINT CK_Booking_Cancel CHECK (
            (Status = 'Cancelled' AND CancelledAtUtc IS NOT NULL AND CancelledById IS NOT NULL) OR
            (Status <> 'Cancelled' AND CancelledAtUtc IS NULL AND CancelledById IS NULL))
    );
    CREATE INDEX IX_Bookings_Table_Time ON dbo.Bookings(TableId, StartAtUtc, EndAtUtc) INCLUDE (Status);
    CREATE INDEX IX_Bookings_Customer ON dbo.Bookings(CustomerId, StartAtUtc);

    CREATE TABLE dbo.PlaySessions (
        Id int IDENTITY NOT NULL CONSTRAINT PK_PlaySessions PRIMARY KEY,
        TableId int NOT NULL,
        BookingId int NULL,
        CustomerId nvarchar(450) NULL,
        OpenedById nvarchar(450) NOT NULL,
        ClosedById nvarchar(450) NULL,
        StartAtUtc datetime2(0) NOT NULL,
        EndAtUtc datetime2(0) NULL,
        HourlyRateSnapshot decimal(18,2) NOT NULL,
        PlaytimeAmount decimal(18,2) NULL,
        Status varchar(20) NOT NULL CONSTRAINT DF_Session_Status DEFAULT 'Active',
        RowVersion rowversion NOT NULL,
        CONSTRAINT FK_Session_Table FOREIGN KEY (TableId) REFERENCES dbo.BilliardTables(Id),
        CONSTRAINT FK_Session_Booking FOREIGN KEY (BookingId) REFERENCES dbo.Bookings(Id),
        CONSTRAINT FK_Session_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.AspNetUsers(Id),
        CONSTRAINT FK_Session_OpenedBy FOREIGN KEY (OpenedById) REFERENCES dbo.AspNetUsers(Id),
        CONSTRAINT FK_Session_ClosedBy FOREIGN KEY (ClosedById) REFERENCES dbo.AspNetUsers(Id),
        CONSTRAINT CK_Session_Status CHECK (Status IN ('Active','Closed')),
        CONSTRAINT CK_Session_Rate CHECK (HourlyRateSnapshot > 0),
        CONSTRAINT CK_Session_Lifecycle CHECK (
            (Status = 'Active' AND EndAtUtc IS NULL AND ClosedById IS NULL AND PlaytimeAmount IS NULL) OR
            (Status = 'Closed' AND EndAtUtc IS NOT NULL AND EndAtUtc >= StartAtUtc
                AND ClosedById IS NOT NULL AND PlaytimeAmount IS NOT NULL AND PlaytimeAmount >= 0))
    );
    CREATE UNIQUE INDEX UX_PlaySessions_ActiveTable ON dbo.PlaySessions(TableId) WHERE Status = 'Active';
    CREATE UNIQUE INDEX UX_PlaySessions_Booking ON dbo.PlaySessions(BookingId) WHERE BookingId IS NOT NULL;
    CREATE INDEX IX_PlaySessions_Table_Start ON dbo.PlaySessions(TableId, StartAtUtc);

    CREATE TABLE dbo.ProductCategories (
        Id int IDENTITY NOT NULL CONSTRAINT PK_ProductCategories PRIMARY KEY,
        Name nvarchar(60) NOT NULL CONSTRAINT UQ_ProductCategories_Name UNIQUE,
        CONSTRAINT CK_Category_Name CHECK (LEN(LTRIM(RTRIM(Name))) > 0)
    );
    CREATE TABLE dbo.Products (
        Id int IDENTITY NOT NULL CONSTRAINT PK_Products PRIMARY KEY,
        CategoryId int NOT NULL,
        Name nvarchar(100) NOT NULL CONSTRAINT UQ_Products_Name UNIQUE,
        Price decimal(18,2) NOT NULL,
        StockQuantity int NOT NULL CONSTRAINT DF_Product_Stock DEFAULT 0,
        IsActive bit NOT NULL CONSTRAINT DF_Product_Active DEFAULT 1,
        RowVersion rowversion NOT NULL,
        CONSTRAINT FK_Product_Category FOREIGN KEY (CategoryId) REFERENCES dbo.ProductCategories(Id),
        CONSTRAINT CK_Product_Name CHECK (LEN(LTRIM(RTRIM(Name))) > 0),
        CONSTRAINT CK_Product_Price CHECK (Price > 0),
        CONSTRAINT CK_Product_Stock CHECK (StockQuantity >= 0)
    );
    CREATE INDEX IX_Products_Category_Active ON dbo.Products(CategoryId, IsActive);

    CREATE TABLE dbo.PricingConfigs (
        Id int IDENTITY NOT NULL CONSTRAINT PK_PricingConfigs PRIMARY KEY,
        TableTypeId int NOT NULL,
        HourlyRate decimal(18,2) NOT NULL,
        StartHour time NOT NULL,
        EndHour time NOT NULL,
        CONSTRAINT FK_PricingConfig_TableType FOREIGN KEY (TableTypeId) REFERENCES dbo.TableTypes(Id),
        CONSTRAINT CK_PricingConfig_Hours CHECK (StartHour < EndHour),
        CONSTRAINT CK_PricingConfig_Rate CHECK (HourlyRate > 0)
    );

    CREATE TABLE dbo.Orders (
        Id int IDENTITY NOT NULL CONSTRAINT PK_Orders PRIMARY KEY,
        SessionId int NOT NULL,
        OrderTimeUtc datetime2(0) NOT NULL CONSTRAINT DF_Order_Time DEFAULT SYSUTCDATETIME(),
        Status varchar(20) NOT NULL CONSTRAINT DF_Order_Status DEFAULT 'Pending',
        CONSTRAINT FK_Order_Session FOREIGN KEY (SessionId) REFERENCES dbo.PlaySessions(Id),
        CONSTRAINT CK_Order_Status CHECK (Status IN ('Pending', 'Preparing', 'Served', 'Cancelled'))
    );
    CREATE INDEX IX_Orders_Session ON dbo.Orders(SessionId);

    CREATE TABLE dbo.OrderDetails (
        Id int IDENTITY NOT NULL CONSTRAINT PK_OrderDetails PRIMARY KEY,
        OrderId int NOT NULL,
        ProductId int NOT NULL,
        Quantity int NOT NULL,
        UnitPrice decimal(18,2) NOT NULL,
        Subtotal AS (Quantity * UnitPrice) PERSISTED,
        CONSTRAINT FK_OrderDetail_Order FOREIGN KEY (OrderId) REFERENCES dbo.Orders(Id) ON DELETE CASCADE,
        CONSTRAINT FK_OrderDetail_Product FOREIGN KEY (ProductId) REFERENCES dbo.Products(Id),
        CONSTRAINT CK_OrderDetail_Qty CHECK (Quantity > 0),
        CONSTRAINT CK_OrderDetail_Price CHECK (UnitPrice >= 0)
    );

    CREATE TABLE dbo.Invoices (
        Id int IDENTITY NOT NULL CONSTRAINT PK_Invoices PRIMARY KEY,
        SessionId int NOT NULL CONSTRAINT UQ_Invoice_Session UNIQUE,
        CustomerId nvarchar(450) NULL,
        TotalPlaytime decimal(18,2) NOT NULL CONSTRAINT DF_Invoice_Playtime DEFAULT 0,
        TotalFandB decimal(18,2) NOT NULL CONSTRAINT DF_Invoice_FandB DEFAULT 0,
        DiscountAmount decimal(18,2) NOT NULL CONSTRAINT DF_Invoice_Discount DEFAULT 0,
        FinalAmount AS (TotalPlaytime + TotalFandB - DiscountAmount) PERSISTED,
        Status varchar(20) NOT NULL CONSTRAINT DF_Invoice_Status DEFAULT 'Unpaid',
        CreatedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_Invoice_Created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_Invoice_Session FOREIGN KEY (SessionId) REFERENCES dbo.PlaySessions(Id),
        CONSTRAINT FK_Invoice_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.AspNetUsers(Id),
        CONSTRAINT CK_Invoice_Status CHECK (Status IN ('Unpaid', 'Paid'))
    );

    CREATE TABLE dbo.PaymentTransactions (
        Id int IDENTITY NOT NULL CONSTRAINT PK_PaymentTransactions PRIMARY KEY,
        InvoiceId int NOT NULL,
        PaymentMethod varchar(20) NOT NULL,
        AmountPaid decimal(18,2) NOT NULL,
        GatewayRefCode nvarchar(100) NULL,
        TransactionTimeUtc datetime2(0) NOT NULL CONSTRAINT DF_Payment_Time DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_Payment_Invoice FOREIGN KEY (InvoiceId) REFERENCES dbo.Invoices(Id) ON DELETE CASCADE,
        CONSTRAINT CK_Payment_Method CHECK (PaymentMethod IN ('Cash', 'VNPay', 'SePay', 'Transfer')),
        CONSTRAINT CK_Payment_Amount CHECK (AmountPaid > 0)
    );

    CREATE TABLE dbo.Combos (
        Id int IDENTITY NOT NULL CONSTRAINT PK_Combos PRIMARY KEY,
        Name nvarchar(100) NOT NULL CONSTRAINT UQ_Combos_Name UNIQUE,
        Price decimal(18,2) NOT NULL,
        PlaytimeHours decimal(5,2) NOT NULL,
        IsActive bit NOT NULL CONSTRAINT DF_Combo_Active DEFAULT 1,
        CONSTRAINT CK_Combo_Name CHECK (LEN(LTRIM(RTRIM(Name))) > 0),
        CONSTRAINT CK_Combo_Price CHECK (Price >= 0),
        CONSTRAINT CK_Combo_Playtime CHECK (PlaytimeHours > 0)
    );

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

    CREATE TABLE dbo.WorkShifts (
        Id int IDENTITY NOT NULL CONSTRAINT PK_WorkShifts PRIMARY KEY,
        StaffId nvarchar(450) NOT NULL,
        StartTimeUtc datetime2(0) NOT NULL CONSTRAINT DF_Shift_Start DEFAULT SYSUTCDATETIME(),
        EndTimeUtc datetime2(0) NULL,
        StartingCash decimal(18,2) NOT NULL CONSTRAINT DF_Shift_StartCash DEFAULT 0,
        EndingCash decimal(18,2) NULL,
        Status varchar(20) NOT NULL CONSTRAINT DF_Shift_Status DEFAULT 'Active',
        Notes nvarchar(500) NULL,
        CONSTRAINT FK_Shift_Staff FOREIGN KEY (StaffId) REFERENCES dbo.AspNetUsers(Id),
        CONSTRAINT CK_Shift_Status CHECK (Status IN ('Active', 'Closed')),
        CONSTRAINT CK_Shift_Time CHECK (EndTimeUtc IS NULL OR EndTimeUtc >= StartTimeUtc)
    );

    -- Bảng đánh giá dịch vụ sau phiên chơi (UC17)
    CREATE TABLE dbo.Reviews (
        Id           int IDENTITY NOT NULL CONSTRAINT PK_Reviews PRIMARY KEY,
        SessionId    int NOT NULL CONSTRAINT UQ_Review_Session UNIQUE,
        CustomerId   nvarchar(450) NOT NULL,
        Rating       tinyint NOT NULL,
        Comment      nvarchar(500) NULL,
        CreatedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_Review_Created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_Review_Session  FOREIGN KEY (SessionId)  REFERENCES dbo.PlaySessions(Id),
        CONSTRAINT FK_Review_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.AspNetUsers(Id),
        CONSTRAINT CK_Review_Rating   CHECK (Rating BETWEEN 1 AND 5)
    );
    CREATE INDEX IX_Reviews_Customer ON dbo.Reviews(CustomerId);

    -- Seed dữ liệu hạng thành viên mặc định
    SET IDENTITY_INSERT dbo.MembershipTiers ON;
    INSERT INTO dbo.MembershipTiers (Id, TierName, MinPoints, DiscountPercent) VALUES
        (1, N'Đồng',       0,    0.00),
        (2, N'Bạc',        500,  5.00),
        (3, N'Vàng',       2000, 10.00),
        (4, N'Kim Cương',  5000, 15.00);
    SET IDENTITY_INSERT dbo.MembershipTiers OFF;

    COMMIT;
    PRINT N'Created full 26 tables + seeded MembershipTiers successfully. Procedures follow in this file.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;
GO


GO

-----------------------------------------------------------

/* Run after 01. These procedures serialize booking/session changes per table.
   The web app must authorize the caller and derive user IDs from its login session.
   Never trust CustomerId/StaffId/ActorId supplied in a browser form.
   CRUD for products is separate; Identity accounts must use UserManager.
*/
USE [BilliardDB];
GO
CREATE OR ALTER PROCEDURE dbo.usp_CreateBooking
    @CustomerId nvarchar(450), @TableId int,
    @StartAtUtc datetime2(0), @EndAtUtc datetime2(0),
    @Notes nvarchar(300) = NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    IF @StartAtUtc IS NULL OR @EndAtUtc IS NULL OR @StartAtUtc <= SYSUTCDATETIME() OR @EndAtUtc <= @StartAtUtc
        THROW 51101, N'Choose a future start time and an end time after it.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers u
        JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
        JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
        WHERE u.Id = @CustomerId AND u.IsActive = 1 AND r.Name = N'Customer')
        THROW 51102, N'An active Customer account is required.', 1;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @TableStatus varchar(20);
        SELECT @TableStatus = Status FROM dbo.BilliardTables WITH (UPDLOCK, HOLDLOCK) WHERE Id = @TableId;
        IF @TableStatus IS NULL OR @TableStatus IN ('Maintenance','Inactive')
            THROW 51103, N'Table is not bookable.', 1;
        IF EXISTS (SELECT 1 FROM dbo.Bookings
            WHERE TableId = @TableId AND Status IN ('Confirmed','CheckedIn')
              AND StartAtUtc < @EndAtUtc AND EndAtUtc > @StartAtUtc)
            THROW 51104, N'The requested time overlaps an existing booking.', 1;
        INSERT dbo.Bookings(CustomerId, TableId, StartAtUtc, EndAtUtc, Notes)
        VALUES (@CustomerId, @TableId, @StartAtUtc, @EndAtUtc, @Notes);
        DECLARE @Id int = CONVERT(int, SCOPE_IDENTITY());
        COMMIT;
        SELECT @Id AS BookingId;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_CancelBooking
    @BookingId int, @ActorId nvarchar(450), @Reason nvarchar(300) = NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @TableId int, @LockedId int, @CustomerId nvarchar(450), @Status varchar(20), @Start datetime2(0);
        SELECT @TableId = TableId FROM dbo.Bookings WHERE Id = @BookingId;
        SELECT @LockedId = Id FROM dbo.BilliardTables WITH (UPDLOCK, HOLDLOCK) WHERE Id = @TableId;
        SELECT @CustomerId = CustomerId, @Status = Status, @Start = StartAtUtc
          FROM dbo.Bookings WITH (UPDLOCK) WHERE Id = @BookingId;
        IF @Status IS NULL OR @Status <> 'Confirmed'
            THROW 51201, N'Only a Confirmed booking can be cancelled.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers WHERE Id = @ActorId AND IsActive = 1)
            THROW 51202, N'Active account required.', 1;
        DECLARE @IsStaff bit = 0;
        IF EXISTS (SELECT 1 FROM dbo.AspNetUserRoles ur JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
            WHERE ur.UserId = @ActorId AND r.Name IN (N'Admin',N'Staff')) SET @IsStaff = 1;
        IF @IsStaff = 0 AND (@CustomerId <> @ActorId OR @Start <= SYSUTCDATETIME())
            THROW 51203, N'Customers can cancel only their own upcoming bookings.', 1;
        UPDATE dbo.Bookings SET Status = 'Cancelled', CancelledAtUtc = SYSUTCDATETIME(),
            CancelledById = @ActorId, CancellationReason = @Reason WHERE Id = @BookingId;
        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_CheckInBooking
    @BookingId int, @StaffId nvarchar(450)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers u JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
        JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
        WHERE u.Id = @StaffId AND u.IsActive = 1 AND r.Name IN (N'Staff',N'Admin'))
        THROW 51301, N'Active Staff or Admin required.', 1;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @TableId int, @TableStatus varchar(20), @Status varchar(20), @Start datetime2(0), @End datetime2(0);
        SELECT @TableId = TableId FROM dbo.Bookings WHERE Id = @BookingId;
        SELECT @TableStatus = Status FROM dbo.BilliardTables WITH (UPDLOCK, HOLDLOCK) WHERE Id = @TableId;
        SELECT @Status = Status, @Start = StartAtUtc, @End = EndAtUtc
          FROM dbo.Bookings WITH (UPDLOCK) WHERE Id = @BookingId;
        IF @Status IS NULL OR @Status <> 'Confirmed'
            THROW 51302, N'Only a Confirmed booking can be checked in.', 1;
        -- Initial proposal: check-in from 15 minutes before start until booked end.
        IF SYSUTCDATETIME() < DATEADD(minute,-15,@Start) OR SYSUTCDATETIME() >= @End
            THROW 51303, N'Outside the check-in window.', 1;
        IF @TableStatus <> 'Available'
            THROW 51304, N'Table is not ready for check-in.', 1;
        IF EXISTS (SELECT 1 FROM dbo.Bookings WHERE TableId = @TableId AND Status = 'CheckedIn')
            THROW 51305, N'Another customer has already checked in for this table.', 1;
        UPDATE dbo.Bookings SET Status = 'CheckedIn', CheckedInAtUtc = SYSUTCDATETIME(),
            CheckedInById = @StaffId WHERE Id = @BookingId;
        -- Check-in does NOT create a play session. The Staff session screen opens it.
        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_OpenSession
    @TableId int, @StaffId nvarchar(450),
    @BookingId int = NULL, @CustomerId nvarchar(450) = NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers u JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
        JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
        WHERE u.Id = @StaffId AND u.IsActive = 1 AND r.Name IN (N'Staff',N'Admin'))
        THROW 51401, N'Active Staff or Admin required.', 1;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @TableStatus varchar(20), @Rate decimal(18,2), @Now datetime2(0) = SYSUTCDATETIME();
        SELECT @TableStatus = t.Status, @Rate = ty.HourlyRate
          FROM dbo.BilliardTables t WITH (UPDLOCK, HOLDLOCK)
          JOIN dbo.TableTypes ty ON ty.Id = t.TableTypeId WHERE t.Id = @TableId;
        IF @TableStatus IS NULL OR @TableStatus <> 'Available'
            THROW 51402, N'Table is not Available.', 1;
        IF EXISTS (SELECT 1 FROM dbo.PlaySessions WHERE TableId = @TableId AND Status = 'Active')
            THROW 51403, N'Table already has an Active session.', 1;
        IF @BookingId IS NOT NULL
        BEGIN
            DECLARE @BookingStatus varchar(20), @BookingTable int, @BookingEnd datetime2(0), @BookingCustomer nvarchar(450);
            SELECT @BookingStatus = Status, @BookingTable = TableId, @BookingEnd = EndAtUtc, @BookingCustomer = CustomerId
              FROM dbo.Bookings WHERE Id = @BookingId;
            IF @BookingStatus IS NULL OR @BookingStatus <> 'CheckedIn' OR @BookingTable <> @TableId OR @BookingEnd <= @Now
                THROW 51404, N'Use a checked-in, unexpired booking for this table.', 1;
            IF @CustomerId IS NOT NULL AND @CustomerId <> @BookingCustomer
                THROW 51405, N'Customer does not match the booking.', 1;
            SET @CustomerId = @BookingCustomer;
            IF EXISTS (SELECT 1 FROM dbo.PlaySessions WHERE BookingId = @BookingId)
                THROW 51406, N'This booking already has a session.', 1;
        END
        ELSE IF EXISTS (SELECT 1 FROM dbo.Bookings WHERE TableId = @TableId
            AND (Status = 'CheckedIn' OR (Status = 'Confirmed' AND StartAtUtc <= @Now AND EndAtUtc > @Now)))
            THROW 51407, N'Table is reserved for a current booking.', 1;
        IF @CustomerId IS NOT NULL AND NOT EXISTS (
            SELECT 1 FROM dbo.AspNetUsers u JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
            JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
            WHERE u.Id = @CustomerId AND u.IsActive = 1 AND r.Name = N'Customer')
            THROW 51408, N'Customer account is inactive or invalid.', 1;
        INSERT dbo.PlaySessions(TableId, BookingId, CustomerId, OpenedById, StartAtUtc, HourlyRateSnapshot)
            VALUES (@TableId, @BookingId, @CustomerId, @StaffId, @Now, @Rate);
        DECLARE @Id int = CONVERT(int,SCOPE_IDENTITY());
        UPDATE dbo.BilliardTables SET Status = 'InUse' WHERE Id = @TableId;
        COMMIT;
        SELECT @Id AS SessionId;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_CloseSession
    @SessionId int, @StaffId nvarchar(450)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers u JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
        JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
        WHERE u.Id = @StaffId AND u.IsActive = 1 AND r.Name IN (N'Staff',N'Admin'))
        THROW 51501, N'Active Staff or Admin required.', 1;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @TableId int, @LockedId int, @Status varchar(20), @Start datetime2(0),
            @Rate decimal(18,2), @BookingId int, @Now datetime2(0) = SYSUTCDATETIME();
        SELECT @TableId = TableId FROM dbo.PlaySessions WHERE Id = @SessionId;
        SELECT @LockedId = Id FROM dbo.BilliardTables WITH (UPDLOCK, HOLDLOCK) WHERE Id = @TableId;
        SELECT @Status = Status, @Start = StartAtUtc, @Rate = HourlyRateSnapshot, @BookingId = BookingId
          FROM dbo.PlaySessions WITH (UPDLOCK) WHERE Id = @SessionId;
        IF @Status IS NULL OR @Status <> 'Active'
            THROW 51502, N'Only an Active session can be closed.', 1;
        -- Fixed rate, actual seconds, rounded to whole VND; no minimum charge in v1.
        UPDATE dbo.PlaySessions SET EndAtUtc = @Now, ClosedById = @StaffId, Status = 'Closed',
            PlaytimeAmount = ROUND(CONVERT(decimal(18,2),DATEDIFF_BIG(second,@Start,@Now)) * @Rate / 3600.0, 0)
            WHERE Id = @SessionId;
        UPDATE dbo.BilliardTables SET Status = 'AwaitingPayment' WHERE Id = @TableId;
        IF @BookingId IS NOT NULL UPDATE dbo.Bookings SET Status = 'Completed' WHERE Id = @BookingId;
        COMMIT;
        SELECT Id, StartAtUtc, EndAtUtc, HourlyRateSnapshot, PlaytimeAmount, Status
          FROM dbo.PlaySessions WHERE Id = @SessionId;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END;
GO
PRINT N'Created 5 procedures. Demo data follows in this file.';


GO

-----------------------------------------------------------

/* Fictional local-development fixtures. ALL PasswordHash values are NULL.
   These rows cannot sign in with a password until UserManager sets one.
   Runs once after schema and procedures above. No existing data is deleted or reset.
*/
USE [BilliardDB];
GO
SET NOCOUNT ON; SET XACT_ABORT ON;
-- Required for writes to tables with filtered indexes, including sqlcmd sessions.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
BEGIN TRY
    BEGIN TRANSACTION;
    IF EXISTS (SELECT 1 FROM dbo.AspNetRoles) OR EXISTS (SELECT 1 FROM dbo.AspNetUsers)
       OR EXISTS (SELECT 1 FROM dbo.TableTypes) OR EXISTS (SELECT 1 FROM dbo.ProductCategories)
        THROW 51600, N'Seed requires empty starter tables. Existing data will not be overwritten.', 1;
    DECLARE @Now datetime2(0) = SYSUTCDATETIME();
    DECLARE @Tomorrow datetime2(0) = DATEADD(day,1,CONVERT(datetime2(0),CONVERT(date,@Now)));
    INSERT dbo.AspNetRoles(Id, Name, NormalizedName, ConcurrencyStamp) VALUES
        (N'role-admin',N'Admin',N'ADMIN',CONVERT(nvarchar(36),NEWID())),
        (N'role-staff',N'Staff',N'STAFF',CONVERT(nvarchar(36),NEWID())),
        (N'role-customer',N'Customer',N'CUSTOMER',CONVERT(nvarchar(36),NEWID()));
    INSERT dbo.AspNetUsers(Id,UserName,NormalizedUserName,Email,NormalizedEmail,FullName,
        PhoneNumber,IsActive,EmployeeCode,HireDate,SecurityStamp,ConcurrencyStamp)
    VALUES
        (N'demo-admin',N'admin.demo',N'ADMIN.DEMO',N'admin@example.test',N'ADMIN@EXAMPLE.TEST',N'Quản trị mẫu',N'0900000001',1,N'AD001','2026-01-01',NEWID(),NEWID()),
        (N'demo-staff-01',N'staff.demo01',N'STAFF.DEMO01',N'staff01@example.test',N'STAFF01@EXAMPLE.TEST',N'Nhân viên mẫu 01',N'0900000002',1,N'NV001','2026-01-02',NEWID(),NEWID()),
        (N'demo-staff-02',N'staff.demo02',N'STAFF.DEMO02',N'staff02@example.test',N'STAFF02@EXAMPLE.TEST',N'Nhân viên mẫu 02',N'0900000003',0,N'NV002','2026-02-01',NEWID(),NEWID()),
        (N'demo-customer-01',N'customer.demo01',N'CUSTOMER.DEMO01',N'customer01@example.test',N'CUSTOMER01@EXAMPLE.TEST',N'Khách mẫu 01',N'0900000004',1,NULL,NULL,NEWID(),NEWID()),
        (N'demo-customer-02',N'customer.demo02',N'CUSTOMER.DEMO02',N'customer02@example.test',N'CUSTOMER02@EXAMPLE.TEST',N'Khách mẫu 02',N'0900000005',1,NULL,NULL,NEWID(),NEWID());
    INSERT dbo.AspNetUserRoles(UserId,RoleId) VALUES
        (N'demo-admin',N'role-admin'),(N'demo-staff-01',N'role-staff'),(N'demo-staff-02',N'role-staff'),
        (N'demo-customer-01',N'role-customer'),(N'demo-customer-02',N'role-customer');
    INSERT dbo.TableTypes(Name,HourlyRate) VALUES (N'Pool',100000),(N'Carom',120000);
    DECLARE @Pool int = (SELECT Id FROM dbo.TableTypes WHERE Name=N'Pool');
    DECLARE @Carom int = (SELECT Id FROM dbo.TableTypes WHERE Name=N'Carom');
    INSERT dbo.BilliardTables(TableCode,TableTypeId,FloorNumber,Status) VALUES
        (N'B01',@Pool,1,'InUse'),(N'B02',@Pool,1,'Available'),
        (N'B03',@Pool,1,'Available'),(N'B04',@Carom,2,'AwaitingPayment'),
        (N'B05',@Carom,2,'Maintenance'),(N'B06',@Carom,2,'Available');
    DECLARE @B01 int=(SELECT Id FROM dbo.BilliardTables WHERE TableCode=N'B01');
    DECLARE @B02 int=(SELECT Id FROM dbo.BilliardTables WHERE TableCode=N'B02');
    DECLARE @B03 int=(SELECT Id FROM dbo.BilliardTables WHERE TableCode=N'B03');
    DECLARE @B04 int=(SELECT Id FROM dbo.BilliardTables WHERE TableCode=N'B04');
    DECLARE @B06 int=(SELECT Id FROM dbo.BilliardTables WHERE TableCode=N'B06');
    INSERT dbo.Bookings(CustomerId,TableId,StartAtUtc,EndAtUtc,Notes) VALUES
        (N'demo-customer-01',@B02,DATEADD(hour,6,@Tomorrow),DATEADD(hour,8,@Tomorrow),N'13:00–15:00 giờ Việt Nam'),
        (N'demo-customer-02',@B03,DATEADD(hour,8,@Tomorrow),DATEADD(hour,10,@Tomorrow),N'15:00–17:00 giờ Việt Nam');
    INSERT dbo.Bookings(CustomerId,TableId,StartAtUtc,EndAtUtc,Status,CheckedInAtUtc,CheckedInById,Notes)
    VALUES (N'demo-customer-02',@B06,DATEADD(minute,-10,@Now),DATEADD(minute,110,@Now),'CheckedIn',@Now,N'demo-staff-01',N'Khách đã đến; chờ nhân viên mở bàn.');
    INSERT dbo.Bookings(CustomerId,TableId,StartAtUtc,EndAtUtc,Status,CancelledAtUtc,CancelledById,CancellationReason)
    VALUES (N'demo-customer-01',@B02,DATEADD(day,2,@Tomorrow),DATEADD(hour,2,DATEADD(day,2,@Tomorrow)),
        'Cancelled',@Now,N'demo-customer-01',N'Dữ liệu mẫu: khách đổi kế hoạch.');
    INSERT dbo.PlaySessions(TableId,CustomerId,OpenedById,StartAtUtc,HourlyRateSnapshot)
    VALUES (@B01,N'demo-customer-01',N'demo-staff-01',DATEADD(minute,-90,@Now),100000);
    INSERT dbo.PlaySessions(TableId,CustomerId,OpenedById,ClosedById,StartAtUtc,EndAtUtc,HourlyRateSnapshot,PlaytimeAmount,Status)
    VALUES (@B04,NULL,N'demo-staff-01',N'demo-staff-01',DATEADD(hour,-3,@Now),DATEADD(hour,-1,@Now),120000,240000,'Closed');
    INSERT dbo.ProductCategories(Name) VALUES (N'Đồ uống'),(N'Đồ ăn');
    DECLARE @Drink int=(SELECT Id FROM dbo.ProductCategories WHERE Name=N'Đồ uống');
    DECLARE @Food int=(SELECT Id FROM dbo.ProductCategories WHERE Name=N'Đồ ăn');
    INSERT dbo.Products(CategoryId,Name,Price,StockQuantity,IsActive) VALUES
        (@Drink,N'Nước suối',15000,50,1),(@Drink,N'Trà đào',39000,20,1),
        (@Drink,N'Nước ngọt',20000,0,1),(@Drink,N'Cà phê lon',25000,10,0),
        (@Food,N'Khoai tây chiên',45000,15,1),(@Food,N'Xúc xích',30000,20,1);
    COMMIT;
    PRINT N'Demo data created. Password login is NOT configured. Read-only verification follows in this file.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;
GO


GO

-----------------------------------------------------------

/* READ ONLY. Run after schema, procedures and seed in SSMS. */
USE [BilliardDB];
GO
-- SSMS creates dbo.sysdiagrams when saving database diagrams; exclude that support table.
SELECT DB_NAME() AS DatabaseName,
    (SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped=0
     AND NOT (schema_id=SCHEMA_ID(N'dbo') AND name=N'sysdiagrams')) AS TableCount_Expected24,
    (SELECT COUNT(*) FROM sys.procedures WHERE name IN
     ('usp_CreateBooking','usp_CancelBooking','usp_CheckInBooking','usp_OpenSession','usp_CloseSession')) AS ProcedureCount_Expected5;
SELECT N'Roles' AS Item, COUNT(*) AS Actual, 3 AS ExpectedAfterSeed FROM dbo.AspNetRoles
UNION ALL SELECT N'Users', COUNT(*),5 FROM dbo.AspNetUsers
UNION ALL SELECT N'Table types',COUNT(*),2 FROM dbo.TableTypes
UNION ALL SELECT N'Tables',COUNT(*),6 FROM dbo.BilliardTables
UNION ALL SELECT N'Bookings',COUNT(*),4 FROM dbo.Bookings
UNION ALL SELECT N'Play sessions',COUNT(*),2 FROM dbo.PlaySessions
UNION ALL SELECT N'Categories',COUNT(*),2 FROM dbo.ProductCategories
UNION ALL SELECT N'Products',COUNT(*),6 FROM dbo.Products
UNION ALL SELECT N'MembershipTiers',COUNT(*),0 FROM dbo.MembershipTiers
UNION ALL SELECT N'PricingConfigs',COUNT(*),0 FROM dbo.PricingConfigs
UNION ALL SELECT N'Orders',COUNT(*),0 FROM dbo.Orders
UNION ALL SELECT N'OrderDetails',COUNT(*),0 FROM dbo.OrderDetails
UNION ALL SELECT N'Invoices',COUNT(*),0 FROM dbo.Invoices
UNION ALL SELECT N'PaymentTransactions',COUNT(*),0 FROM dbo.PaymentTransactions
UNION ALL SELECT N'Combos',COUNT(*),0 FROM dbo.Combos
UNION ALL SELECT N'WorkShifts',COUNT(*),0 FROM dbo.WorkShifts
UNION ALL SELECT N'ComboItems',COUNT(*),0 FROM dbo.ComboItems
UNION ALL SELECT N'SessionCombos',COUNT(*),0 FROM dbo.SessionCombos
UNION ALL SELECT N'SessionComboItems',COUNT(*),0 FROM dbo.SessionComboItems;

-- Screen 5: Staff listing; show inactive accounts too.
SELECT u.Id,u.EmployeeCode,u.FullName,u.UserName,u.Email,u.PhoneNumber,u.HireDate,u.IsActive
FROM dbo.AspNetUsers u JOIN dbo.AspNetUserRoles ur ON ur.UserId=u.Id
JOIN dbo.AspNetRoles r ON r.Id=ur.RoleId WHERE r.Name=N'Staff' ORDER BY u.EmployeeCode;

-- Table screen: current physical state, independent of future reservations.
SELECT t.Id,t.TableCode,ty.Name AS TableType,ty.HourlyRate,t.Status,s.Id AS ActiveSessionId,s.StartAtUtc
FROM dbo.BilliardTables t JOIN dbo.TableTypes ty ON ty.Id=t.TableTypeId
LEFT JOIN dbo.PlaySessions s ON s.TableId=t.Id AND s.Status='Active' ORDER BY t.TableCode;

-- Booking screen (UTC storage -> Vietnam UTC+7 display).
SELECT b.Id,u.FullName,u.PhoneNumber,t.TableCode,b.Status,b.StartAtUtc,b.EndAtUtc,
    DATEADD(hour,7,b.StartAtUtc) AS StartVietnamTime,DATEADD(hour,7,b.EndAtUtc) AS EndVietnamTime
FROM dbo.Bookings b JOIN dbo.AspNetUsers u ON u.Id=b.CustomerId
JOIN dbo.BilliardTables t ON t.Id=b.TableId ORDER BY b.StartAtUtc;

SELECT p.Id,p.Name,c.Name AS Category,p.Price,p.StockQuantity,p.IsActive,
    CASE WHEN p.IsActive=0 THEN 'Discontinued' WHEN p.StockQuantity=0 THEN 'OutOfStock' ELSE 'Available' END AS DisplayStatus
FROM dbo.Products p JOIN dbo.ProductCategories c ON c.Id=p.CategoryId;

-- The following 3 queries should return ZERO rows.
SELECT a.Id AS FirstBooking,b.Id AS SecondBooking,a.TableId
FROM dbo.Bookings a JOIN dbo.Bookings b ON a.TableId=b.TableId AND a.Id<b.Id
WHERE a.Status IN ('Confirmed','CheckedIn') AND b.Status IN ('Confirmed','CheckedIn')
  AND a.StartAtUtc<b.EndAtUtc AND a.EndAtUtc>b.StartAtUtc;

SELECT t.Id,t.Status,s.Id AS ActiveSession
FROM dbo.BilliardTables t LEFT JOIN dbo.PlaySessions s ON s.TableId=t.Id AND s.Status='Active'
WHERE (t.Status='InUse' AND s.Id IS NULL) OR (t.Status<>'InUse' AND s.Id IS NOT NULL);

SELECT Id,Name,Price,StockQuantity FROM dbo.Products WHERE Price<=0 OR StockQuantity<0;

-- Expected 0 initially: no password was written by the SQL seed.
SELECT COUNT(*) AS UsersWithPassword_Expected0 FROM dbo.AspNetUsers WHERE PasswordHash IS NOT NULL;


GO

-----------------------------------------------------------




