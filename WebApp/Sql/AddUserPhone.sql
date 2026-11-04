IF COL_LENGTH(N'ColdCall.Users', N'Phone') IS NULL
BEGIN
    ALTER TABLE [ColdCall].[Users] ADD [Phone] nvarchar(32) NULL;
END;
