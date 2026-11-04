IF COL_LENGTH(N'ColdCall.SearchProfiles', N'RecordCount') IS NULL
    ALTER TABLE [ColdCall].[SearchProfiles] ADD [RecordCount] int NULL;
