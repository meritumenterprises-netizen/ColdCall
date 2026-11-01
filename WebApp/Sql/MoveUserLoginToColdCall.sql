USE [Firmy_temp];
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.UserLogin', N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'ColdCall.UserLogin', N'U') IS NOT NULL
        THROW 50001, 'Both dbo.UserLogin and ColdCall.UserLogin exist. No changes applied.', 1;
    ALTER SCHEMA ColdCall TRANSFER dbo.UserLogin;
END;
COMMIT TRANSACTION;
GO
