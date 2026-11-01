USE [Firmy_temp];
GO
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
SET LOCK_TIMEOUT 30000;
GO
BEGIN TRY
    BEGIN TRANSACTION;
    IF EXISTS (SELECT 1 FROM ColdCall.Companies WITH (TABLOCKX, HOLDLOCK) WHERE DATALENGTH(AddressSort) > 1600)
        THROW 50001, 'AddressSort exceeds 800 UTF-16 code units. No schema changes applied.', 1;

    DROP INDEX IF EXISTS IX_Companies_AddressSortPrefix_Id ON ColdCall.Companies;
    DROP INDEX IF EXISTS IX_Companies_AddressSortPrefixDesc_Id ON ColdCall.Companies;
    IF COL_LENGTH(N'ColdCall.Companies', N'AddressSortPrefix') IS NOT NULL
        EXEC(N'ALTER TABLE ColdCall.Companies DROP COLUMN AddressSortPrefix;');

    IF COL_LENGTH(N'ColdCall.Companies', N'AddressSort') <> 1600
        ALTER TABLE ColdCall.Companies ALTER COLUMN AddressSort nvarchar(800) NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'ColdCall.Companies') AND name=N'IX_Companies_AddressSort_Id')
        CREATE NONCLUSTERED INDEX IX_Companies_AddressSort_Id
            ON ColdCall.Companies (AddressSort ASC, Id ASC) INCLUDE (StatusDzialalnosciId);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'ColdCall.Companies') AND name=N'IX_Companies_AddressSortDesc_Id')
        CREATE NONCLUSTERED INDEX IX_Companies_AddressSortDesc_Id
            ON ColdCall.Companies (AddressSort DESC, Id ASC) INCLUDE (StatusDzialalnosciId);
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
