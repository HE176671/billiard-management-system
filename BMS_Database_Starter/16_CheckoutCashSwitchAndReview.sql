-- Apply after 15_CheckoutWorkflow.sql. Also applied automatically at startup.
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name='CK_PaymentIntent_Status'
    AND parent_object_id=OBJECT_ID('dbo.PaymentIntents') AND definition NOT LIKE '%Cancelled%')
    ALTER TABLE dbo.PaymentIntents DROP CONSTRAINT CK_PaymentIntent_Status;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name='CK_PaymentIntent_Status' AND parent_object_id=OBJECT_ID('dbo.PaymentIntents'))
    ALTER TABLE dbo.PaymentIntents ADD CONSTRAINT CK_PaymentIntent_Status CHECK (Status IN ('Pending','Confirmed','Expired','Failed','Cancelled'));
IF OBJECT_ID('dbo.PaymentMethodChanges','U') IS NULL
BEGIN
    CREATE TABLE dbo.PaymentMethodChanges (
        Id int IDENTITY PRIMARY KEY,
        InvoiceId int NOT NULL REFERENCES dbo.Invoices(Id),
        ReferenceCode varchar(50) NOT NULL UNIQUE REFERENCES dbo.PaymentIntents(ReferenceCode),
        FromMethod varchar(20) NOT NULL,
        ToMethod varchar(20) NOT NULL,
        Amount decimal(18,2) NOT NULL,
        CashReceived decimal(18,2) NOT NULL,
        Reason nvarchar(250) NOT NULL,
        ChangedById nvarchar(450) NOT NULL REFERENCES dbo.AspNetUsers(Id),
        ChangedAtUtc datetime2 NOT NULL,
        CONSTRAINT CK_PaymentChange_Amount CHECK (Amount > 0 AND CashReceived >= Amount)
    );
END;
IF OBJECT_ID('dbo.PaymentReconciliations','U') IS NULL
BEGIN
    CREATE TABLE dbo.PaymentReconciliations (
        Id int IDENTITY PRIMARY KEY,
        InvoiceId int NOT NULL REFERENCES dbo.Invoices(Id),
        ReferenceCode varchar(50) NOT NULL REFERENCES dbo.PaymentIntents(ReferenceCode),
        Amount decimal(18,2) NOT NULL CHECK (Amount > 0),
        Source varchar(20) NOT NULL CHECK (Source IN ('Manual','SePay')),
        Reason varchar(40) NOT NULL,
        Status varchar(20) NOT NULL CHECK (Status IN ('Open','Resolved')),
        ProviderTransactionId nvarchar(100) NULL,
        RequestId varchar(32) NULL,
        BankReceivedAtUtc datetime2 NULL,
        CreatedAtUtc datetime2 NOT NULL,
        CreatedById nvarchar(450) NULL REFERENCES dbo.AspNetUsers(Id),
        Notes nvarchar(1000) NOT NULL,
        Resolution varchar(40) NULL,
        ResolutionNotes nvarchar(1000) NULL,
        ResolvedById nvarchar(450) NULL REFERENCES dbo.AspNetUsers(Id),
        ResolvedAtUtc datetime2 NULL,
        CONSTRAINT CK_PaymentReview_Source CHECK (
            (Source='Manual' AND CreatedById IS NOT NULL AND RequestId IS NOT NULL AND BankReceivedAtUtc IS NOT NULL AND ProviderTransactionId IS NULL) OR
            (Source='SePay' AND ProviderTransactionId IS NOT NULL AND CreatedById IS NULL AND RequestId IS NULL)),
        CONSTRAINT CK_PaymentReview_Resolution CHECK (
            (Status='Open' AND Resolution IS NULL AND ResolvedById IS NULL AND ResolvedAtUtc IS NULL) OR
            (Status='Resolved' AND Resolution IS NOT NULL AND ResolutionNotes IS NOT NULL AND ResolvedById IS NOT NULL AND ResolvedAtUtc IS NOT NULL))
    );
    CREATE UNIQUE INDEX UX_PaymentReview_Provider ON dbo.PaymentReconciliations(ProviderTransactionId) WHERE ProviderTransactionId IS NOT NULL;
    CREATE UNIQUE INDEX UX_PaymentReview_Request ON dbo.PaymentReconciliations(RequestId) WHERE RequestId IS NOT NULL;
    CREATE INDEX IX_PaymentReview_Status ON dbo.PaymentReconciliations(Status, CreatedAtUtc);
    CREATE INDEX IX_PaymentReview_Invoice ON dbo.PaymentReconciliations(InvoiceId);
END;
