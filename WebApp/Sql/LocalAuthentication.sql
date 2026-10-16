SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
BEGIN TRANSACTION;
IF OBJECT_ID(N'ColdCall.UserCredentials', N'U') IS NULL
BEGIN
    CREATE TABLE ColdCall.UserCredentials (
        UserId int NOT NULL CONSTRAINT PK_UserCredentials PRIMARY KEY,
        TrialOffered bit NOT NULL CONSTRAINT DF_UserCredentials_TrialOffered DEFAULT(0),
        UserName nvarchar(256) NULL, NormalizedUserName nvarchar(256) NULL,
        Email nvarchar(256) NULL, NormalizedEmail nvarchar(256) NULL,
        EmailConfirmed bit NOT NULL, PasswordHash nvarchar(max) NULL,
        SecurityStamp nvarchar(max) NULL, ConcurrencyStamp nvarchar(max) NULL,
        PhoneNumber nvarchar(max) NULL, PhoneNumberConfirmed bit NOT NULL,
        TwoFactorEnabled bit NOT NULL, LockoutEnd datetimeoffset NULL,
        LockoutEnabled bit NOT NULL, AccessFailedCount int NOT NULL,
        CONSTRAINT FK_UserCredentials_Users_UserId FOREIGN KEY(UserId) REFERENCES ColdCall.Users(Id) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX UX_UserCredentials_NormalizedUserName ON ColdCall.UserCredentials(NormalizedUserName) WHERE NormalizedUserName IS NOT NULL;
    CREATE INDEX EmailIndex ON ColdCall.UserCredentials(NormalizedEmail);
END;
IF OBJECT_ID(N'ColdCall.UserCredentialClaims', N'U') IS NULL
BEGIN
    CREATE TABLE ColdCall.UserCredentialClaims (
        Id int IDENTITY NOT NULL CONSTRAINT PK_UserCredentialClaims PRIMARY KEY,
        UserId int NOT NULL, ClaimType nvarchar(max) NULL, ClaimValue nvarchar(max) NULL,
        CONSTRAINT FK_UserCredentialClaims_UserCredentials_UserId FOREIGN KEY(UserId) REFERENCES ColdCall.UserCredentials(UserId) ON DELETE CASCADE
    );
    CREATE INDEX IX_UserCredentialClaims_UserId ON ColdCall.UserCredentialClaims(UserId);
END;
IF OBJECT_ID(N'ColdCall.UserCredentialLogins', N'U') IS NULL
BEGIN
    CREATE TABLE ColdCall.UserCredentialLogins (
        LoginProvider nvarchar(128) NOT NULL, ProviderKey nvarchar(128) NOT NULL,
        ProviderDisplayName nvarchar(max) NULL, UserId int NOT NULL,
        CONSTRAINT PK_UserCredentialLogins PRIMARY KEY(LoginProvider,ProviderKey),
        CONSTRAINT FK_UserCredentialLogins_UserCredentials_UserId FOREIGN KEY(UserId) REFERENCES ColdCall.UserCredentials(UserId) ON DELETE CASCADE
    );
    CREATE INDEX IX_UserCredentialLogins_UserId ON ColdCall.UserCredentialLogins(UserId);
END;
IF OBJECT_ID(N'ColdCall.UserCredentialTokens', N'U') IS NULL
BEGIN
    CREATE TABLE ColdCall.UserCredentialTokens (
        UserId int NOT NULL, LoginProvider nvarchar(128) NOT NULL, Name nvarchar(128) NOT NULL, Value nvarchar(max) NULL,
        CONSTRAINT PK_UserCredentialTokens PRIMARY KEY(UserId,LoginProvider,Name),
        CONSTRAINT FK_UserCredentialTokens_UserCredentials_UserId FOREIGN KEY(UserId) REFERENCES ColdCall.UserCredentials(UserId) ON DELETE CASCADE
    );
END;
COMMIT TRANSACTION;
