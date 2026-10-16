USE [Firmy_temp];
GO
SET XACT_ABORT ON;
SET LOCK_TIMEOUT 15000;

BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dbo.CSVExport', N'U') IS NOT NULL
    BEGIN
        IF OBJECT_ID(N'ColdCall.CSVExport', N'U') IS NOT NULL
            THROW 50001, 'Both dbo.CSVExport and ColdCall.CSVExport exist; nothing was changed.', 1;
        ALTER SCHEMA [ColdCall] TRANSFER [dbo].[CSVExport];
    END
    ELSE IF OBJECT_ID(N'ColdCall.CSVExport', N'U') IS NULL
        THROW 50002, 'CSVExport table was not found.', 1;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
