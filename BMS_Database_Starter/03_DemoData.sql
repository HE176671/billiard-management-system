/* Fictional local-development fixtures. ALL PasswordHash values are NULL.
   These rows cannot sign in with a password until UserManager sets one.
   Run once after 01 and 02. No existing data is deleted or reset.
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
    PRINT N'Demo data created. Password login is NOT configured. Next: 04_Verify.sql';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;
GO
