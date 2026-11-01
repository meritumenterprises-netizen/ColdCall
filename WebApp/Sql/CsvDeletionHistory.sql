USE [Firmy_temp];
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'ColdCall.CsvDeletionHistory', N'U') IS NULL
BEGIN
    CREATE TABLE ColdCall.CsvDeletionHistory
    (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_CsvDeletionHistory PRIMARY KEY,
        UserId int NOT NULL,
        ExportId int NOT NULL,
        DeletedAtUtc datetime2(7) NOT NULL,
        QuotaDate date NOT NULL
    );
    CREATE INDEX IX_CsvDeletionHistory_UserId_QuotaDate ON ColdCall.CsvDeletionHistory(UserId, QuotaDate);
END;
COMMIT TRANSACTION;
GO
