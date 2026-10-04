SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'ColdCall.Wojewodztwa')
        AND name = N'Name' AND (max_length <> 500 OR is_nullable <> 0 OR system_type_id <> 231))
        ALTER TABLE [ColdCall].[Wojewodztwa] ALTER COLUMN [Name] nvarchar(250) NOT NULL;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'ColdCall.Powiaty')
        AND name = N'Name' AND (max_length <> 500 OR is_nullable <> 0 OR system_type_id <> 231))
        ALTER TABLE [ColdCall].[Powiaty] ALTER COLUMN [Name] nvarchar(250) NOT NULL;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'ColdCall.Gminy')
        AND name = N'Name' AND (max_length <> 500 OR is_nullable <> 0 OR system_type_id <> 231))
        ALTER TABLE [ColdCall].[Gminy] ALTER COLUMN [Name] nvarchar(250) NOT NULL;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'ColdCall.Miejscowosci')
        AND name = N'Name' AND (max_length <> 500 OR is_nullable <> 0 OR system_type_id <> 231))
        ALTER TABLE [ColdCall].[Miejscowosci] ALTER COLUMN [Name] nvarchar(250) NOT NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'ColdCall.Wojewodztwa')
        AND name = N'UX_Wojewodztwa_Name')
        CREATE UNIQUE INDEX [UX_Wojewodztwa_Name] ON [ColdCall].[Wojewodztwa] ([Name]);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'ColdCall.Powiaty')
        AND name = N'UX_Powiaty_Name')
        CREATE UNIQUE INDEX [UX_Powiaty_Name] ON [ColdCall].[Powiaty] ([Name]);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'ColdCall.Gminy')
        AND name = N'UX_Gminy_Name')
        CREATE UNIQUE INDEX [UX_Gminy_Name] ON [ColdCall].[Gminy] ([Name]);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'ColdCall.Miejscowosci')
        AND name = N'UX_Miejscowosci_Name')
        CREATE UNIQUE INDEX [UX_Miejscowosci_Name] ON [ColdCall].[Miejscowosci] ([Name]);
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
