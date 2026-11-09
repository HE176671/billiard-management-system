-- Apply after 13_WalletTransfer.sql. Safe to run again.
IF COL_LENGTH('dbo.WalletTopUpIntents','ConfirmationMethod') IS NULL
    ALTER TABLE dbo.WalletTopUpIntents ADD ConfirmationMethod varchar(20) NULL;
IF COL_LENGTH('dbo.WalletTopUpIntents','ConfirmedById') IS NULL
    ALTER TABLE dbo.WalletTopUpIntents ADD ConfirmedById nvarchar(450) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_WalletIntent_ConfirmedBy')
    ALTER TABLE dbo.WalletTopUpIntents ADD CONSTRAINT FK_WalletIntent_ConfirmedBy
        FOREIGN KEY (ConfirmedById) REFERENCES dbo.AspNetUsers(Id);
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name='CK_WalletIntent_Confirmation')
    ALTER TABLE dbo.WalletTopUpIntents ADD CONSTRAINT CK_WalletIntent_Confirmation CHECK (
        (ConfirmationMethod IS NULL AND ConfirmedById IS NULL) OR
        (ConfirmationMethod='SePay' AND ConfirmedById IS NULL) OR
        (ConfirmationMethod='Manual' AND ConfirmedById IS NOT NULL));
GO
