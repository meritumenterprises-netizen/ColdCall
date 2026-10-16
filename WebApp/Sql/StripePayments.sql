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
        CONSTRAINT FK_StripePayments_Users FOREIGN KEY (AppUserId)
            REFERENCES ColdCall.Users(Id) ON DELETE CASCADE,
        CONSTRAINT FK_StripePayments_PlanDefinitions FOREIGN KEY (PlanDefinitionId)
            REFERENCES ColdCall.PlanDefinitions(Id)
    );
    CREATE INDEX IX_StripePayments_AppUserId ON ColdCall.StripePayments(AppUserId);
END;
COMMIT TRANSACTION;
