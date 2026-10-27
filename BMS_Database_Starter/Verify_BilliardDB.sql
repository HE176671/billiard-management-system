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
