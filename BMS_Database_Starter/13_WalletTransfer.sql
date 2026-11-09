-- Apply after 12_WalletPrepaid.sql.

        SET QUOTED_IDENTIFIER ON;
        SET ANSI_NULLS ON;
        IF COL_LENGTH('dbo.WalletTransactions','PaymentMethod') IS NULL
            ALTER TABLE dbo.WalletTransactions ADD PaymentMethod varchar(20) NOT NULL CONSTRAINT DF_Wallet_Method DEFAULT 'Cash' WITH VALUES;
        IF COL_LENGTH('dbo.WalletTransactions','ProviderTransactionId') IS NULL
            ALTER TABLE dbo.WalletTransactions ADD ProviderTransactionId nvarchar(100) NULL;
        
GO

        SET QUOTED_IDENTIFIER ON;
        SET ANSI_NULLS ON;
        UPDATE dbo.WalletTransactions SET PaymentMethod='Wallet' WHERE TransactionType='Payment' AND PaymentMethod='Cash';
        IF OBJECT_ID('dbo.WalletTopUpIntents','U') IS NULL
        BEGIN
            CREATE TABLE dbo.WalletTopUpIntents (
                Id int IDENTITY PRIMARY KEY,
                CustomerId nvarchar(450) NOT NULL REFERENCES dbo.AspNetUsers(Id),
                CreatedById nvarchar(450) NOT NULL REFERENCES dbo.AspNetUsers(Id),
                ReferenceCode varchar(50) NOT NULL UNIQUE,
                ExpectedAmount decimal(18,2) NOT NULL CHECK (ExpectedAmount >= 1000 AND ExpectedAmount <= 100000000),
                Status varchar(20) NOT NULL CHECK (Status IN ('Pending','Confirmed','Expired')),
                ProviderTransactionId nvarchar(100) NULL,
                CreatedAtUtc datetime2 NOT NULL,
                ExpiresAtUtc datetime2 NOT NULL,
                ConfirmedAtUtc datetime2 NULL
            );
            CREATE UNIQUE INDEX UX_WalletIntent_Bank ON dbo.WalletTopUpIntents(ProviderTransactionId) WHERE ProviderTransactionId IS NOT NULL;
            CREATE INDEX IX_WalletIntent_Customer ON dbo.WalletTopUpIntents(CustomerId,CreatedAtUtc);
        END;
        IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name='CK_WalletTransaction_Type' AND definition LIKE '%Transfer%')
        BEGIN
            ALTER TABLE dbo.WalletTransactions DROP CONSTRAINT CK_WalletTransaction_Type;
            ALTER TABLE dbo.WalletTransactions ADD CONSTRAINT CK_WalletTransaction_Type CHECK (
                (TransactionType='TopUp' AND Amount>0 AND InvoiceId IS NULL AND
                    ((PaymentMethod='Cash' AND WorkShiftId IS NOT NULL AND ProviderTransactionId IS NULL) OR
                     (PaymentMethod='Transfer' AND WorkShiftId IS NULL AND ProviderTransactionId IS NOT NULL))) OR
                (TransactionType='Payment' AND Amount<=0 AND InvoiceId IS NOT NULL AND PaymentMethod='Wallet'));
        END;
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_Wallet_Bank' AND object_id=OBJECT_ID('dbo.WalletTransactions'))
            CREATE UNIQUE INDEX UX_Wallet_Bank ON dbo.WalletTransactions(ProviderTransactionId) WHERE ProviderTransactionId IS NOT NULL;
        
