USE [Firmy_Temp];
GO

SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'ColdCall.SearchProfileCriteria', N'U') IS NULL
    BEGIN
        CREATE TABLE [ColdCall].[SearchProfileCriteria] (
            [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_SearchProfileCriteria] PRIMARY KEY,
            [SearchProfileId] int NOT NULL,
            [SortColumn] nvarchar(32) NOT NULL CONSTRAINT [DF_SearchProfileCriteria_SortColumn] DEFAULT N'dataAktywnosci',
            [SortDirection] varchar(4) NOT NULL CONSTRAINT [DF_SearchProfileCriteria_SortDirection] DEFAULT 'desc',
            [Address] nvarchar(1000) NOT NULL CONSTRAINT [DF_SearchProfileCriteria_Address] DEFAULT N'',
            [HasPhone] bit NULL,
            [HasEmail] bit NULL,
            [HasWebsite] bit NULL,
            [DateRangeEnabled] bit NOT NULL CONSTRAINT [DF_SearchProfileCriteria_DateRangeEnabled] DEFAULT 0,
            [DateFrom] date NULL,
            [DateTo] date NULL,
            [RecordLimit] int NOT NULL CONSTRAINT [DF_SearchProfileCriteria_RecordLimit] DEFAULT 1000,
            CONSTRAINT [FK_SearchProfileCriteria_SearchProfiles_SearchProfileId]
                FOREIGN KEY ([SearchProfileId]) REFERENCES [ColdCall].[SearchProfiles] ([Id]) ON DELETE CASCADE,
            CONSTRAINT [CK_SearchProfileCriteria_SortColumn] CHECK ([SortColumn] IN (N'name',N'nip',N'fullAddress',N'phone',N'email',N'www',N'dataAktywnosci')),
            CONSTRAINT [CK_SearchProfileCriteria_SortDirection] CHECK ([SortDirection] IN ('asc','desc')),
            CONSTRAINT [CK_SearchProfileCriteria_RecordLimit] CHECK ([RecordLimit] BETWEEN 1 AND 10000),
            CONSTRAINT [CK_SearchProfileCriteria_Dates] CHECK (
                ([DateRangeEnabled] = 0 AND [DateFrom] IS NULL AND [DateTo] IS NULL) OR
                ([DateRangeEnabled] = 1 AND [DateFrom] IS NOT NULL AND [DateTo] IS NOT NULL AND [DateTo] > [DateFrom]))
        );
        CREATE UNIQUE INDEX [IX_SearchProfileCriteria_SearchProfileId]
            ON [ColdCall].[SearchProfileCriteria] ([SearchProfileId]);
    END;
    IF COL_LENGTH(N'ColdCall.SearchProfileCriteria', N'CompanyName') IS NULL
        ALTER TABLE [ColdCall].[SearchProfileCriteria] ADD [CompanyName] nvarchar(1000) NOT NULL
            CONSTRAINT [DF_SearchProfileCriteria_CompanyName] DEFAULT N'' WITH VALUES;
    IF COL_LENGTH(N'ColdCall.SearchProfileCriteria', N'Name') IS NULL
        ALTER TABLE [ColdCall].[SearchProfileCriteria] ADD [Name] nvarchar(200) NOT NULL
            CONSTRAINT [DF_SearchProfileCriteria_Name] DEFAULT N'' WITH VALUES;
    INSERT INTO [ColdCall].[SearchProfileCriteria] ([SearchProfileId])
    SELECT p.[Id] FROM [ColdCall].[SearchProfiles] p
    WHERE NOT EXISTS (SELECT 1 FROM [ColdCall].[SearchProfileCriteria] c WHERE c.[SearchProfileId] = p.[Id]);
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
