USE [Firmy_Temp];
GO

SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    IF COL_LENGTH(N'ColdCall.Companies', N'CreatedOn') IS NULL
    BEGIN
        ALTER TABLE [ColdCall].[Companies]
        ADD [CreatedOn] datetime NOT NULL
            CONSTRAINT [DF_Companies_CreatedOn] DEFAULT (DATEADD(day, -1, GETDATE())) WITH VALUES;

        ALTER TABLE [ColdCall].[Companies] DROP CONSTRAINT [DF_Companies_CreatedOn];
        ALTER TABLE [ColdCall].[Companies]
        ADD CONSTRAINT [DF_Companies_CreatedOn] DEFAULT (GETDATE()) FOR [CreatedOn];
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
