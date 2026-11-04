SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'ColdCall.StripePayments', N'U') IS NULL
BEGIN
    CREATE TABLE ColdCall.StripePayments (
        StripeSessionId nvarchar(255) NOT NULL CONSTRAINT PK_StripePayments PRIMARY KEY,
        AppUserId int NOT NULL,
        PlanDefinitionId int NOT NULL,
        PaidAtUtc datetime2 NOT NULL,
        AmountPaid decimal(18,2) NOT NULL,
        PDFInvoice image NULL,
        InvoiceId nvarchar(128) NULL,
        ClientId int NULL,
        InvoiceNumber int NULL,
        CONSTRAINT FK_StripePayments_Users FOREIGN KEY (AppUserId)
            REFERENCES ColdCall.Users(Id) ON DELETE CASCADE,
        CONSTRAINT FK_StripePayments_PlanDefinitions FOREIGN KEY (PlanDefinitionId)
            REFERENCES ColdCall.PlanDefinitions(Id)
    );
    CREATE INDEX IX_StripePayments_AppUserId ON ColdCall.StripePayments(AppUserId);
END;
IF COL_LENGTH(N'ColdCall.StripePayments', N'PDFInvoice') IS NULL
    ALTER TABLE ColdCall.StripePayments ADD PDFInvoice image NULL;
IF COL_LENGTH(N'ColdCall.StripePayments', N'InvoiceId') IS NULL
    ALTER TABLE ColdCall.StripePayments ADD InvoiceId nvarchar(128) NULL;
IF COL_LENGTH(N'ColdCall.StripePayments', N'ClientId') IS NULL
    ALTER TABLE ColdCall.StripePayments ADD ClientId int NULL;
IF COL_LENGTH(N'ColdCall.StripePayments', N'InvoiceNumber') IS NULL
    ALTER TABLE ColdCall.StripePayments ADD InvoiceNumber int NULL;
COMMIT TRANSACTION;
