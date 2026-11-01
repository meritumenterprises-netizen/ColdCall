USE [Firmy_temp];
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH(N'ColdCall.Users', N'FirstName') IS NULL
    ALTER TABLE [ColdCall].[Users] ADD [FirstName] nvarchar(100) NULL;
IF COL_LENGTH(N'ColdCall.Users', N'LastName') IS NULL
    ALTER TABLE [ColdCall].[Users] ADD [LastName] nvarchar(100) NULL;
IF COL_LENGTH(N'ColdCall.Users', N'CompanyName') IS NULL
    ALTER TABLE [ColdCall].[Users] ADD [CompanyName] nvarchar(200) NULL;
IF COL_LENGTH(N'ColdCall.Users', N'PostalCode') IS NULL
    ALTER TABLE [ColdCall].[Users] ADD [PostalCode] nvarchar(20) NULL;
IF COL_LENGTH(N'ColdCall.Users', N'City') IS NULL
    ALTER TABLE [ColdCall].[Users] ADD [City] nvarchar(100) NULL;
IF COL_LENGTH(N'ColdCall.Users', N'Street') IS NULL
    ALTER TABLE [ColdCall].[Users] ADD [Street] nvarchar(200) NULL;
IF COL_LENGTH(N'ColdCall.Users', N'StreetNumber') IS NULL
    ALTER TABLE [ColdCall].[Users] ADD [StreetNumber] nvarchar(50) NULL;
COMMIT TRANSACTION;
GO
