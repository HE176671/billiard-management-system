-- Run after scripts 12-14. Additive checkout workflow and manual confirmation audit.
IF OBJECT_ID('dbo.CheckoutDrafts','U') IS NULL
BEGIN
    CREATE TABLE dbo.CheckoutDrafts (
        ReferenceCode varchar(50) NOT NULL PRIMARY KEY,
        Status varchar(20) NOT NULL,
        ReplacementReference varchar(50) NULL,
        CreatedById nvarchar(450) NOT NULL REFERENCES dbo.AspNetUsers(Id),
        CustomerId nvarchar(450) NULL REFERENCES dbo.AspNetUsers(Id),
        SessionId int NULL REFERENCES dbo.PlaySessions(Id),
        InvoiceId int NULL REFERENCES dbo.Invoices(Id),
        IsManualEntry bit NOT NULL,
        PaymentMethod varchar(20) NOT NULL,
        PlaytimeAmount decimal(18,2) NOT NULL,
        FandBAmount decimal(18,2) NOT NULL,
        PointsToRedeem int NOT NULL,
        VoucherCode varchar(20) NULL,
        CashReceived decimal(18,2) NULL,
        FinalAmount decimal(18,2) NULL,
        CreatedAtUtc datetime2 NOT NULL,
        PaidAtUtc datetime2 NULL,
        CONSTRAINT CK_CheckoutDraft_Status CHECK (Status IN ('Draft','Pending','Paid','Superseded')),
        CONSTRAINT CK_CheckoutDraft_Method CHECK (PaymentMethod IN ('Cash','Wallet','VietQR'))
    );
    CREATE INDEX IX_CheckoutDrafts_Invoice ON dbo.CheckoutDrafts(InvoiceId);
END;
IF COL_LENGTH('dbo.PaymentIntents','ConfirmationMethod') IS NULL
    ALTER TABLE dbo.PaymentIntents ADD ConfirmationMethod varchar(20) NULL;
IF COL_LENGTH('dbo.PaymentIntents','ConfirmedById') IS NULL
    ALTER TABLE dbo.PaymentIntents ADD ConfirmedById nvarchar(450) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_PaymentIntent_ConfirmedBy')
    ALTER TABLE dbo.PaymentIntents ADD CONSTRAINT FK_PaymentIntent_ConfirmedBy
        FOREIGN KEY (ConfirmedById) REFERENCES dbo.AspNetUsers(Id);
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name='CK_PaymentIntent_Confirmation')
    ALTER TABLE dbo.PaymentIntents ADD CONSTRAINT CK_PaymentIntent_Confirmation CHECK (
        (ConfirmationMethod IS NULL AND ConfirmedById IS NULL) OR
        (ConfirmationMethod='SePay' AND ConfirmedById IS NULL) OR
        (ConfirmationMethod='Manual' AND ConfirmedById IS NOT NULL));
GO
