USE [Firmy_temp];
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;

-- Preview: all writes, including ALTER COLUMN, are rolled back.
-- Set @Apply=1 to persist. Set @MaxCompanies=NULL to process all companies.
-- Maintenance window required: stop importers and writes while running this script.
-- Table locks prevent concurrent writes and serialize duplicate detection.
DECLARE @Apply bit = 0;
DECLARE @MaxCompanies int = 10000;
DECLARE @Created int = 0, @Linked int = 0, @Skipped int = 0;

IF @@TRANCOUNT <> 0
    THROW 51000, N'Uruchom skrypt poza istniejącą transakcją.', 1;
IF @MaxCompanies IS NOT NULL AND @MaxCompanies < 1
    THROW 51001, N'Limit firm musi być dodatni lub NULL.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    -- Same lock order as the importer: Companies -> Adresy -> CompanyAddress.
    -- Acquire Companies before ALTER TABLE to avoid an opposite lock order.
    DECLARE @LockId int;
    SELECT TOP (1) @LockId = Id
    FROM ColdCall.Companies WITH (TABLOCKX, HOLDLOCK)
    OPTION (MAXDOP 1);
    SELECT TOP (1) @LockId = Id
    FROM ColdCall.Adresy WITH (TABLOCKX, HOLDLOCK)
    OPTION (MAXDOP 1);
    SELECT TOP (1) @LockId = CompanyAdresId
    FROM ColdCall.CompanyAddress WITH (TABLOCKX, HOLDLOCK)
    OPTION (MAXDOP 1);

    ALTER TABLE ColdCall.Adresy ALTER COLUMN Latitude decimal(9,6) NULL;
    ALTER TABLE ColdCall.Adresy ALTER COLUMN Longitude decimal(9,6) NULL;
    ALTER TABLE ColdCall.Adresy ALTER COLUMN Location geography NULL;

    -- The address table represents a building, not an apartment.
    -- PostalCode and ApartmentNumber have no destination columns in Adresy.
    SELECT TOP (ISNULL(@MaxCompanies, 2147483647))
        c.Id AS CompanyId, c.WojewodztwoId, c.PowiatId, c.GminaId, c.MiejscowoscId,
        NULLIF(TRIM(c.Street), N'') AS Ulica,
        NULLIF(TRIM(c.BuildingNumber), N'') AS NrUlicy
    INTO #Sources
    FROM ColdCall.Companies c WITH (UPDLOCK, HOLDLOCK)
    WHERE c.BlockedByRODO = 0
      AND c.WojewodztwoId IS NOT NULL AND c.PowiatId IS NOT NULL
      AND c.GminaId IS NOT NULL AND c.MiejscowoscId IS NOT NULL
    ORDER BY c.Id
    OPTION (MAXDOP 1);

    SELECT a.Id + 0 AS AddressId, a.WojewodztwoId, a.PowiatId, a.GminaId, a.MiejscowoscId,
        NULLIF(TRIM(a.Ulica), N'') AS Ulica,
        NULLIF(TRIM(a.NrUlicy), N'') AS NrUlicy,
        CONVERT(bit, CASE WHEN a.Latitude IS NOT NULL AND a.Longitude IS NOT NULL
            AND a.Location IS NOT NULL THEN 1 ELSE 0 END) AS HasGps
    INTO #Existing
    FROM ColdCall.Adresy a WITH (UPDLOCK, HOLDLOCK)
    OPTION (MAXDOP 1);

    -- Normalize recognized prefixes identically on both sides, without fuzzy matching.
    DECLARE @Pass int = 0;
    WHILE @Pass < 2
    BEGIN
        UPDATE s SET Ulica = NULLIF(TRIM(SUBSTRING(s.Ulica, DATALENGTH(p.Prefix) / 2 + 1, 2147483647)), N'')
        FROM #Sources s
        CROSS APPLY (
            SELECT TOP (1) v.Prefix
            FROM (VALUES (N'ULICA '),(N'UL. '),(N'UL.'),(N'UL:'),(N'UL '),
                (N'PLAC '),(N'PL. '),(N'PL.'),(N'PL:'),(N'PL '),
                (N'ALEJA '),(N'ALEJE '),(N'AL. '),(N'AL.'),(N'AL:'),(N'AL ')) v(Prefix)
            WHERE LEFT(UPPER(s.Ulica), DATALENGTH(v.Prefix) / 2) = v.Prefix
            ORDER BY DATALENGTH(v.Prefix) DESC
        ) p;
        UPDATE a SET Ulica = NULLIF(TRIM(SUBSTRING(a.Ulica, DATALENGTH(p.Prefix) / 2 + 1, 2147483647)), N'')
        FROM #Existing a
        CROSS APPLY (
            SELECT TOP (1) v.Prefix
            FROM (VALUES (N'ULICA '),(N'UL. '),(N'UL.'),(N'UL:'),(N'UL '),
                (N'PLAC '),(N'PL. '),(N'PL.'),(N'PL:'),(N'PL '),
                (N'ALEJA '),(N'ALEJE '),(N'AL. '),(N'AL.'),(N'AL:'),(N'AL ')) v(Prefix)
            WHERE LEFT(UPPER(a.Ulica), DATALENGTH(v.Prefix) / 2) = v.Prefix
            ORDER BY DATALENGTH(v.Prefix) DESC
        ) p;
        SET @Pass += 1;
    END;

    DELETE FROM #Sources
    WHERE NrUlicy IS NULL OR UPPER(NrUlicy) IN (N'-',N'--',N'N/A',N'B/N',N'BRAK')
       OR DATALENGTH(Ulica) > 400 OR DATALENGTH(NrUlicy) > 100;
    SET @Skipped = @@ROWCOUNT;

    SELECT CompanyId, WojewodztwoId, PowiatId, GminaId, MiejscowoscId,
        CONVERT(nvarchar(200), Ulica) AS Ulica,
        CONVERT(nvarchar(50), NrUlicy) AS NrUlicy,
        CONVERT(nvarchar(200), UPPER(ISNULL(Ulica, N''))) AS StreetKey,
        CONVERT(nvarchar(50), UPPER(ISNULL(NrUlicy, N''))) AS NumberKey
    INTO #Pending FROM #Sources
    OPTION (MAXDOP 1);

    CREATE UNIQUE CLUSTERED INDEX IX_Pending_Id ON #Pending(CompanyId);

    SELECT AddressId, WojewodztwoId, PowiatId, GminaId, MiejscowoscId,
        CONVERT(nvarchar(200), UPPER(ISNULL(Ulica, N''))) AS StreetKey,
        CONVERT(nvarchar(50), UPPER(ISNULL(NrUlicy, N''))) AS NumberKey, HasGps
    INTO #Known FROM #Existing
    OPTION (MAXDOP 1);

    CREATE INDEX IX_Known_Key ON #Known
        (WojewodztwoId, PowiatId, GminaId, MiejscowoscId, StreetKey, NumberKey)
        INCLUDE(AddressId, HasGps);

    -- One row per distinct building, even when many companies share it.
    SELECT WojewodztwoId, PowiatId, GminaId, MiejscowoscId, StreetKey, NumberKey,
        MIN(Ulica) AS Ulica, MIN(NrUlicy) AS NrUlicy
    INTO #Keys FROM #Pending
    GROUP BY WojewodztwoId, PowiatId, GminaId, MiejscowoscId, StreetKey, NumberKey
    OPTION (MAXDOP 1);

    INSERT ColdCall.Adresy
        (WojewodztwoId, PowiatId, GminaId, MiejscowoscId, Ulica, NrUlicy,
         Rodzaj, Latitude, Longitude, Location, PrngId)
    OUTPUT inserted.Id, inserted.WojewodztwoId, inserted.PowiatId,
        inserted.GminaId, inserted.MiejscowoscId,
        UPPER(ISNULL(inserted.Ulica, N'')), UPPER(ISNULL(inserted.NrUlicy, N'')),
        CONVERT(bit, 0)
    INTO #Known(AddressId, WojewodztwoId, PowiatId, GminaId, MiejscowoscId,
        StreetKey, NumberKey, HasGps)
    SELECT k.WojewodztwoId, k.PowiatId, k.GminaId, k.MiejscowoscId,
        k.Ulica, k.NrUlicy, N'adres z Companies, bez GPS', NULL, NULL, NULL, NULL
    FROM #Keys k
    WHERE NOT EXISTS (
        SELECT 1 FROM #Known a
        WHERE a.WojewodztwoId=k.WojewodztwoId AND a.PowiatId=k.PowiatId
          AND a.GminaId=k.GminaId AND a.MiejscowoscId=k.MiejscowoscId
          AND a.StreetKey=k.StreetKey AND a.NumberKey=k.NumberKey
    )
    OPTION (MAXDOP 1);
    SET @Created = @@ROWCOUNT;

    -- Prefer a matching address already linked to this company; otherwise GPS, then lowest Id.
    SELECT p.CompanyId, chosen.AddressId
    INTO #Bindings FROM #Pending p
    CROSS APPLY (
        SELECT TOP (1) a.AddressId
        FROM #Known a
        LEFT JOIN ColdCall.CompanyAddress ca WITH (UPDLOCK, HOLDLOCK)
            ON ca.CompanyId=p.CompanyId AND ca.AddressId=a.AddressId
        WHERE a.WojewodztwoId=p.WojewodztwoId AND a.PowiatId=p.PowiatId
          AND a.GminaId=p.GminaId AND a.MiejscowoscId=p.MiejscowoscId
          AND a.StreetKey=p.StreetKey AND a.NumberKey=p.NumberKey
        ORDER BY CASE WHEN ca.CompanyId IS NOT NULL THEN 0 ELSE 1 END,
            a.HasGps DESC, a.AddressId
    ) chosen
    OPTION (MAXDOP 1);

    INSERT ColdCall.CompanyAddress(CompanyId, AddressId)
    SELECT b.CompanyId, b.AddressId FROM #Bindings b
    WHERE NOT EXISTS (
        SELECT 1 FROM ColdCall.CompanyAddress ca WITH (UPDLOCK, HOLDLOCK)
        WHERE ca.CompanyId=b.CompanyId AND ca.AddressId=b.AddressId
    )
    OPTION (MAXDOP 1);
    SET @Linked = @@ROWCOUNT;

    -- AddressIsMissing and other Companies fields are deliberately left unchanged.
    IF @Apply = 1 COMMIT TRANSACTION;
    ELSE ROLLBACK TRANSACTION;

    SELECT @Apply AS Zapisano, @Created AS NoweAdresy,
        @Linked AS NowePowiazania, @Skipped AS PominieteNiekompletneLubZaDlugie;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

DROP TABLE IF EXISTS #Bindings, #Keys, #Known, #Pending, #Existing, #Sources;
