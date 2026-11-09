SET NOCOUNT ON;
SET XACT_ABORT OFF;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
IF DB_NAME() NOT LIKE '%Verification%' THROW 51000, 'Use a disposable Verification database', 1;
BEGIN TRANSACTION;
BEGIN TRY
    DECLARE @customer nvarchar(450) = (SELECT TOP 1 u.Id FROM dbo.AspNetUsers u JOIN dbo.AspNetUserRoles ur ON ur.UserId=u.Id JOIN dbo.AspNetRoles r ON r.Id=ur.RoleId WHERE r.Name='Customer');
    DECLARE @actor nvarchar(450) = (SELECT TOP 1 u.Id FROM dbo.AspNetUsers u JOIN dbo.AspNetUserRoles ur ON ur.UserId=u.Id JOIN dbo.AspNetRoles r ON r.Id=ur.RoleId WHERE r.Name IN ('Staff','Admin'));
    DECLARE @shift int = (SELECT TOP 1 Id FROM dbo.WorkShifts WHERE StaffId=@actor AND Status='Active');
    IF @shift IS NULL
    BEGIN
        INSERT dbo.WorkShifts(StaffId,StartTimeUtc,StartingCash,Status) VALUES(@actor,SYSUTCDATETIME(),0,'Active');
        SET @shift = SCOPE_IDENTITY();
    END;
    DECLARE @ref varchar(50) = 'NAP-' + REPLACE(CONVERT(varchar(36),NEWID()),'-','');
    UPDATE dbo.AspNetUsers SET WalletBalance=2000000 WHERE Id=@customer;
    INSERT dbo.WalletTransactions(CustomerId,TransactionType,Amount,BalanceAfter,ReferenceCode,CreatedById,WorkShiftId,CreatedAtUtc)
        VALUES(@customer,'TopUp',2000000,2000000,@ref,@actor,@shift,SYSUTCDATETIME());
    PRINT 'PASS: top-up ledger with valid customer/actor/shift';
    BEGIN TRY
        INSERT dbo.WalletTransactions(CustomerId,TransactionType,Amount,BalanceAfter,ReferenceCode,CreatedById,WorkShiftId,CreatedAtUtc)
            VALUES(@customer,'TopUp',2000000,4000000,@ref,@actor,@shift,SYSUTCDATETIME());
        THROW 51001,'Duplicate reference accepted',1;
    END TRY BEGIN CATCH
        IF ERROR_NUMBER() NOT IN (2601,2627) THROW;
        PRINT 'PASS: unique reference prevents duplicate wallet entry';
    END CATCH;
    BEGIN TRY
        UPDATE dbo.AspNetUsers SET WalletBalance=-1 WHERE Id=@customer;
        THROW 51002,'Negative wallet balance accepted',1;
    END TRY BEGIN CATCH
        IF ERROR_NUMBER()<>547 THROW;
        PRINT 'PASS: database rejects negative wallet balance';
    END CATCH;
    BEGIN TRY
        INSERT dbo.WalletTransactions(CustomerId,TransactionType,Amount,BalanceAfter,ReferenceCode,CreatedById,CreatedAtUtc)
            VALUES(@customer,'TopUp',100000,2100000,'INVALID-'+@ref,@actor,SYSUTCDATETIME());
        THROW 51003,'Top-up without shift accepted',1;
    END TRY BEGIN CATCH
        IF ERROR_NUMBER()<>547 THROW;
        PRINT 'PASS: top-up requires a shift';
    END CATCH;
    DECLARE @table int = (SELECT TOP 1 Id FROM dbo.BilliardTables);
    INSERT dbo.PlaySessions(TableId,CustomerId,OpenedById,ClosedById,StartAtUtc,EndAtUtc,HourlyRateSnapshot,PlaytimeAmount,Status)
        VALUES(@table,@customer,@actor,@actor,DATEADD(hour,-1,SYSUTCDATETIME()),SYSUTCDATETIME(),100000,100000,'Closed');
    DECLARE @session int=SCOPE_IDENTITY();
    INSERT dbo.Invoices(SessionId,CustomerId,TotalPlaytime,TotalFandB,DiscountAmount,Status)
        VALUES(@session,@customer,100000,0,5000,'Paid');
    DECLARE @invoice int=SCOPE_IDENTITY();
    INSERT dbo.PaymentTransactions(InvoiceId,PaymentMethod,AmountPaid,TransactionTimeUtc)
        VALUES(@invoice,'Wallet',95000,SYSUTCDATETIME());
    UPDATE dbo.AspNetUsers SET WalletBalance=WalletBalance-95000 WHERE Id=@customer AND WalletBalance>=95000;
    IF @@ROWCOUNT<>1 THROW 51004,'Valid debit failed',1;
    INSERT dbo.WalletTransactions(CustomerId,TransactionType,Amount,BalanceAfter,ReferenceCode,InvoiceId,CreatedById,CreatedAtUtc,PaymentMethod)
        VALUES(@customer,'Payment',-95000,1905000,'PAY-'+@ref,@invoice,@actor,SYSUTCDATETIME(),'Wallet');
    PRINT 'PASS: Wallet payment method and linked debit accepted';
    BEGIN TRY
        INSERT dbo.WalletTransactions(CustomerId,TransactionType,Amount,BalanceAfter,ReferenceCode,InvoiceId,CreatedById,CreatedAtUtc,PaymentMethod)
            VALUES(@customer,'Payment',-95000,1810000,'PAY2-'+@ref,@invoice,@actor,SYSUTCDATETIME(),'Wallet');
        THROW 51005,'Invoice debited twice',1;
    END TRY BEGIN CATCH
        IF ERROR_NUMBER() NOT IN (2601,2627) THROW;
        PRINT 'PASS: one wallet debit per invoice';
    END CATCH;
    UPDATE dbo.AspNetUsers SET WalletBalance=WalletBalance-3000000 WHERE Id=@customer AND WalletBalance>=3000000;
    IF @@ROWCOUNT<>0 THROW 51006,'Insufficient wallet debit accepted',1;
    IF (SELECT WalletBalance FROM dbo.AspNetUsers WHERE Id=@customer)<>1905000 THROW 51007,'Balance changed on rejected debit',1;
    PRINT 'PASS: conditional debit rejects insufficient funds';
    IF NOT EXISTS(SELECT 1 FROM dbo.MembershipTiers WHERE TierName='Silver' AND SpendThreshold=2000000)
        THROW 51008,'Silver configuration changed',1;
    IF NOT EXISTS(SELECT 1 FROM dbo.MembershipTiers WHERE TierName='Gold' AND SpendThreshold=5000000)
        THROW 51009,'Gold configuration changed',1;
    PRINT 'PASS: original tier configuration preserved';
    ROLLBACK;
    IF EXISTS(SELECT 1 FROM dbo.WalletTransactions WHERE ReferenceCode=@ref) THROW 51010,'Rollback failed',1;
    PRINT 'PASS: transaction rollback removes test wallet receipts';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK;
    THROW;
END CATCH;
