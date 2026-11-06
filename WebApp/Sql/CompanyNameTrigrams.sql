SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
SET NOCOUNT ON;
SET XACT_ABORT ON;
-- EF writers must use UseSqlOutputClause(false) on Companies because of the trigger.
DECLARE @BuildLock int;
EXEC @BuildLock = sys.sp_getapplock @Resource = N'ColdCall.CompanyNameGramBuild',
    @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0;
IF @BuildLock < 0 THROW 50001, 'Another company name index build is running.', 1;
GO
IF OBJECT_ID(N'ColdCall.CompanyNameGramPositions') IS NULL
BEGIN
    CREATE TABLE ColdCall.CompanyNameGramPositions (Position smallint NOT NULL PRIMARY KEY);
    INSERT ColdCall.CompanyNameGramPositions
    SELECT TOP (1000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL))
    FROM sys.all_objects a CROSS JOIN sys.all_objects b;
END;
IF OBJECT_ID(N'ColdCall.CompanyNameGrams') IS NULL
BEGIN
    CREATE TABLE ColdCall.CompanyNameGrams
    (
        CompanyId int NOT NULL,
        Gram nvarchar(3) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
        CONSTRAINT PK_CompanyNameGrams PRIMARY KEY CLUSTERED (CompanyId, Gram)
            WITH (DATA_COMPRESSION = PAGE)
    );
END;
IF OBJECT_ID(N'ColdCall.CompanyNameGramFrequency') IS NULL
    CREATE TABLE ColdCall.CompanyNameGramFrequency
    (
        Gram nvarchar(3) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL PRIMARY KEY,
        CompanyCount bigint NOT NULL
    );
IF OBJECT_ID(N'ColdCall.CompanyNameGramBuild') IS NULL
BEGIN
    CREATE TABLE ColdCall.CompanyNameGramBuild
    (
        Id int NOT NULL PRIMARY KEY CHECK (Id = 1),
        LastCompanyId int NOT NULL,
        IsReady bit NOT NULL
    );
    INSERT ColdCall.CompanyNameGramBuild VALUES (1, 0, 0);
END;
GO
IF OBJECT_ID(N'ColdCall.CompanyNameGramExceptions') IS NULL
    CREATE TABLE ColdCall.CompanyNameGramExceptions
    (
        CompanyId int NOT NULL PRIMARY KEY,
        Name nvarchar(1000) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL
    );
GO
CREATE OR ALTER TRIGGER ColdCall.TR_Companies_NameGrams
ON ColdCall.Companies AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
       AND NOT UPDATE(Name) AND NOT UPDATE(Id)
        RETURN;
    DELETE g FROM ColdCall.CompanyNameGrams g JOIN deleted d ON d.Id = g.CompanyId;
    DELETE e FROM ColdCall.CompanyNameGramExceptions e JOIN deleted d ON d.Id = e.CompanyId;
    INSERT ColdCall.CompanyNameGrams (CompanyId, Gram)
    SELECT DISTINCT i.Id, CONVERT(nvarchar(3), SUBSTRING(i.Name, p.Position, 3))
    FROM inserted i JOIN ColdCall.CompanyNameGramPositions p
        ON p.Position <= DATALENGTH(i.Name) / 2 - 2;
    INSERT ColdCall.CompanyNameGramExceptions (CompanyId, Name)
    SELECT Id, Name FROM inserted WHERE Name COLLATE Latin1_General_100_BIN2 LIKE (N'%[^ -~' + NCHAR(260) + NCHAR(262) + NCHAR(280) + NCHAR(321) + NCHAR(323) + NCHAR(211) + NCHAR(346) + NCHAR(377) + NCHAR(379) + NCHAR(261) + NCHAR(263) + NCHAR(281) + NCHAR(322) + NCHAR(324) + NCHAR(243) + NCHAR(347) + NCHAR(378) + NCHAR(380) + N']%');
END;
GO
BEGIN TRANSACTION;
DROP FUNCTION IF EXISTS ColdCall.CompanyNameCandidates;
GO
CREATE FUNCTION ColdCall.CompanyNameCandidates(@Name nvarchar(1000))
RETURNS @Matches TABLE (CompanyId int NOT NULL PRIMARY KEY)
AS
BEGIN
    DECLARE @Pattern nvarchar(4000) = N'%' + REPLACE(REPLACE(REPLACE(REPLACE(
        @Name, N'\', N'\\'), N'%', N'\%'), N'_', N'\_'), N'[', N'\[') + N'%';
    DECLARE @Grams TABLE (Gram nvarchar(3) NOT NULL PRIMARY KEY, CompanyCount bigint NOT NULL);
    INSERT @Grams (Gram, CompanyCount)
    SELECT TOP (3) CONVERT(nvarchar(3), SUBSTRING(@Name, p.Position, 3)),
        MIN(COALESCE(f.CompanyCount, 0))
    FROM ColdCall.CompanyNameGramPositions p
    LEFT JOIN ColdCall.CompanyNameGramFrequency f
        ON f.Gram = CONVERT(nvarchar(3), SUBSTRING(@Name, p.Position, 3))
    WHERE p.Position <= DATALENGTH(@Name) / 2 - 2
    GROUP BY CONVERT(nvarchar(3), SUBSTRING(@Name, p.Position, 3))
    ORDER BY MIN(COALESCE(f.CompanyCount, 0)), CONVERT(nvarchar(3), SUBSTRING(@Name, p.Position, 3));
    DECLARE @First nvarchar(3), @Second nvarchar(3), @Third nvarchar(3);
    SELECT TOP (1) @First = Gram FROM @Grams ORDER BY CompanyCount, Gram;
    SELECT TOP (1) @Second = Gram FROM @Grams WHERE Gram <> @First ORDER BY CompanyCount, Gram;
    SELECT TOP (1) @Third = Gram FROM @Grams WHERE Gram <> @First AND Gram <> @Second ORDER BY CompanyCount, Gram;
    INSERT @Matches (CompanyId)
    SELECT c.Id FROM (
        SELECT CompanyId FROM ColdCall.CompanyNameGrams WHERE Gram = @First
        INTERSECT
        SELECT CompanyId FROM ColdCall.CompanyNameGrams WHERE Gram = COALESCE(@Second, @First)
        INTERSECT
        SELECT CompanyId FROM ColdCall.CompanyNameGrams WHERE Gram = COALESCE(@Third, @First)
    ) g
    JOIN ColdCall.Companies c WITH (FORCESEEK) ON c.Id = g.CompanyId
    WHERE c.Name LIKE @Pattern ESCAPE N'\';
    INSERT @Matches (CompanyId)
    SELECT e.CompanyId FROM ColdCall.CompanyNameGramExceptions e
    WHERE e.Name LIKE @Pattern ESCAPE N'\'
        AND NOT EXISTS (SELECT 1 FROM @Matches m WHERE m.CompanyId = e.CompanyId);
    RETURN;
END;
GO
COMMIT;
GO
-- Resumable backfill. Row locks serialize each batch with concurrent name changes.
UPDATE ColdCall.CompanyNameGramBuild SET IsReady = 0 WHERE Id = 1;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'ColdCall.CompanyNameGrams')
    AND name = N'IX_CompanyNameGrams_Gram_CompanyId' AND is_disabled = 0)
    ALTER INDEX IX_CompanyNameGrams_Gram_CompanyId ON ColdCall.CompanyNameGrams DISABLE;
EXEC sys.sp_autostats N'ColdCall.CompanyNameGrams', 'OFF';
DECLARE @Last int, @Next int, @Total int;
SELECT @Last = LastCompanyId FROM ColdCall.CompanyNameGramBuild WHERE Id = 1;
SELECT @Total = MAX(Id) FROM ColdCall.Companies;
WHILE 1 = 1
BEGIN
    BEGIN TRANSACTION;
    SELECT TOP (5000) Id, Name INTO #Batch
    FROM ColdCall.Companies WITH (UPDLOCK, ROWLOCK)
    WHERE Id > @Last ORDER BY Id;
    IF NOT EXISTS (SELECT 1 FROM #Batch)
    BEGIN
        DROP TABLE #Batch;
        COMMIT;
        BREAK;
    END;
    SELECT @Next = MAX(Id) FROM #Batch;
    INSERT ColdCall.CompanyNameGrams (CompanyId, Gram)
    SELECT DISTINCT b.Id, CONVERT(nvarchar(3), SUBSTRING(b.Name, p.Position, 3))
    FROM #Batch b JOIN ColdCall.CompanyNameGramPositions p
        ON p.Position <= DATALENGTH(b.Name) / 2 - 2
    WHERE NOT EXISTS (
        SELECT 1 FROM ColdCall.CompanyNameGrams g WITH (FORCESEEK)
        WHERE g.CompanyId = b.Id AND g.Gram = CONVERT(nvarchar(3), SUBSTRING(b.Name, p.Position, 3))
    );
    UPDATE ColdCall.CompanyNameGramBuild SET LastCompanyId = @Next WHERE Id = 1;
    DROP TABLE #Batch;
    COMMIT;
    SET @Last = @Next;
    RAISERROR ('Name index: company ID %d / %d', 0, 1, @Last, @Total) WITH NOWAIT;
END;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'ColdCall.CompanyNameGrams')
    AND name = N'IX_CompanyNameGrams_Gram_CompanyId')
    ALTER INDEX IX_CompanyNameGrams_Gram_CompanyId ON ColdCall.CompanyNameGrams
        REBUILD WITH (DATA_COMPRESSION = PAGE, SORT_IN_TEMPDB = ON);
ELSE
    CREATE UNIQUE INDEX IX_CompanyNameGrams_Gram_CompanyId
        ON ColdCall.CompanyNameGrams (Gram, CompanyId)
        WITH (DATA_COMPRESSION = PAGE, SORT_IN_TEMPDB = ON);
-- Frequencies only choose a selective gram; stale estimates never change results.
-- Unusual Unicode can expand or compose under the original SQL collation.
-- Keep those names in a small exact-match fallback, not a lossy trigram-only filter.
BEGIN TRANSACTION;
INSERT ColdCall.CompanyNameGramExceptions (CompanyId, Name)
SELECT c.Id, c.Name FROM ColdCall.Companies c WITH (UPDLOCK, ROWLOCK, INDEX(1))
WHERE c.Name COLLATE Latin1_General_100_BIN2 LIKE (N'%[^ -~' + NCHAR(260) + NCHAR(262) + NCHAR(280) + NCHAR(321) + NCHAR(323) + NCHAR(211) + NCHAR(346) + NCHAR(377) + NCHAR(379) + NCHAR(261) + NCHAR(263) + NCHAR(281) + NCHAR(322) + NCHAR(324) + NCHAR(243) + NCHAR(347) + NCHAR(378) + NCHAR(380) + N']%')
    AND NOT EXISTS (SELECT 1 FROM ColdCall.CompanyNameGramExceptions e WHERE e.CompanyId = c.Id);
COMMIT;
DELETE FROM ColdCall.CompanyNameGramFrequency;
INSERT ColdCall.CompanyNameGramFrequency (Gram, CompanyCount)
SELECT Gram, COUNT_BIG(*) FROM ColdCall.CompanyNameGrams GROUP BY Gram;
UPDATE STATISTICS ColdCall.CompanyNameGrams;
EXEC sys.sp_autostats N'ColdCall.CompanyNameGrams', 'ON';
UPDATE ColdCall.CompanyNameGramBuild SET IsReady = 1 WHERE Id = 1;
GO
