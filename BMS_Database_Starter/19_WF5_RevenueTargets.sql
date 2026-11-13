/*
    TV5 — UC40 Mục tiêu doanh thu tháng.
    - Bảng dbo.RevenueTargets: mỗi tháng tối đa 1 mục tiêu (unique Year + Month).
    - Doanh thu thực tế KHÔNG lưu ở đây, luôn tính lại từ PaymentTransactions.
    Chạy lại nhiều lần vẫn an toàn. Web cũng tự áp dụng khi khởi động (Data/Wf5SchemaUpgrader.cs).
*/
USE BilliardDB;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID('dbo.RevenueTargets', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RevenueTargets (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_RevenueTargets PRIMARY KEY,
        [Year] int NOT NULL,
        [Month] int NOT NULL,
        TargetAmount decimal(18,0) NOT NULL,
        Note nvarchar(300) NULL,
        CreatedById nvarchar(450) NULL,
        CreatedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_RevenueTargets_Created DEFAULT SYSUTCDATETIME(),
        UpdatedById nvarchar(450) NULL,
        UpdatedAtUtc datetime2(0) NULL,
        CONSTRAINT CK_RevenueTargets_Month CHECK ([Month] BETWEEN 1 AND 12),
        CONSTRAINT CK_RevenueTargets_Year CHECK ([Year] BETWEEN 2000 AND 9998),
        CONSTRAINT CK_RevenueTargets_Amount CHECK (TargetAmount > 0),
        CONSTRAINT FK_RevenueTargets_CreatedBy FOREIGN KEY (CreatedById) REFERENCES dbo.AspNetUsers(Id),
        CONSTRAINT FK_RevenueTargets_UpdatedBy FOREIGN KEY (UpdatedById) REFERENCES dbo.AspNetUsers(Id)
    );
    CREATE UNIQUE INDEX UX_RevenueTargets_Year_Month ON dbo.RevenueTargets([Year], [Month]);
END;
GO
