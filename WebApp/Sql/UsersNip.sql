USE [Firmy_temp];
GO
SET XACT_ABORT ON;
IF COL_LENGTH(N'ColdCall.Users', N'Nip') IS NULL
    ALTER TABLE ColdCall.Users ADD Nip nvarchar(10) NULL;
GO
