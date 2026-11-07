SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET NOCOUNT ON;
SET LOCK_TIMEOUT 15000;

IF COL_LENGTH(N'ColdCall.Companies', N'ContactFlags') IS NULL
    ALTER TABLE ColdCall.Companies ADD ContactFlags AS
        CONVERT(tinyint, (CASE WHEN [Phone] IS NOT NULL AND LTRIM(RTRIM([Phone])) <> '' THEN 1 ELSE 0 END) + (CASE WHEN [Email] IS NOT NULL AND LTRIM(RTRIM([Email])) <> '' THEN 2 ELSE 0 END) + (CASE WHEN [Website] IS NOT NULL AND LTRIM(RTRIM([Website])) <> '' THEN 4 ELSE 0 END));

IF NOT EXISTS (SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'ColdCall.Companies')
      AND name = N'IX_Companies_ContactFlags_Status_StartedAt_Id')
    CREATE INDEX IX_Companies_ContactFlags_Status_StartedAt_Id
        ON ColdCall.Companies (ContactFlags, StatusDzialalnosciId, StartedAt DESC, Id)
        INCLUDE (ImportedOn)
        WITH (MAXDOP = 2);
