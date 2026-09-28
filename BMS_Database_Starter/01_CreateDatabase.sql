/* BMS Starter v1 - SQL Server 2019+.
   Run the WHOLE file in SSMS. Uses a NEW database BilliardDB.
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
    COMMIT;
    PRINT N'Created 13 tables successfully. Next: 02_BusinessProcedures.sql';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;
GO
