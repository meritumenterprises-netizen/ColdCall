SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.PrgImporterState','U') IS NULL
BEGIN
    IF OBJECT_ID('dbo.Terc','U') IS NOT NULL OR OBJECT_ID('dbo.PrgAdresy','U') IS NOT NULL
        THROW 50001, 'Terc/PrgAdresy already exist without importer metadata. Review their schema before importing.', 1;
    CREATE TABLE dbo.PrgImporterState
    (
        Id tinyint NOT NULL CONSTRAINT PK_PrgImporterState PRIMARY KEY CHECK (Id=1),
        SchemaVersion int NOT NULL,
        Snapshot varchar(64) NULL,
        Ready bit NOT NULL,
        CompletedOn datetime2 NULL
    );
    INSERT dbo.PrgImporterState(Id,SchemaVersion,Ready) VALUES(1,2,0);
    CREATE TABLE dbo.Terc
    (
        Code varchar(7) NOT NULL CONSTRAINT PK_PrgImporterTerc PRIMARY KEY,
        Woj varchar(2) NOT NULL, Pow varchar(2) NULL, Gmi varchar(2) NULL, Rodz varchar(1) NULL,
        Nazwa nvarchar(200) NOT NULL, NazDod nvarchar(100) NULL, StanNa date NOT NULL
    );
    CREATE TABLE dbo.PrgAdresy
    (
        Id bigint IDENTITY NOT NULL CONSTRAINT PK_PrgImporterAdresy PRIMARY KEY,
        SourceId varchar(50) NOT NULL,
        GmlId nvarchar(500) NOT NULL,
        Snapshot varchar(64) NOT NULL,
        Terc varchar(7) NOT NULL,
        WojCode AS CONVERT(varchar(2),SUBSTRING(Terc,1,2)) PERSISTED,
        PowCode AS CONVERT(varchar(2),SUBSTRING(Terc,3,2)) PERSISTED,
        GmiCode AS CONVERT(varchar(2),SUBSTRING(Terc,5,2)) PERSISTED,
        RodzCode AS CONVERT(varchar(1),SUBSTRING(Terc,7,1)) PERSISTED,
        Wojewodztwo nvarchar(200) NULL, Powiat nvarchar(200) NULL, Gmina nvarchar(200) NULL,
        Simc varchar(7) NOT NULL, Miejscowosc nvarchar(200) NOT NULL,
        Ulic varchar(32) NOT NULL, Ulica nvarchar(300) NOT NULL,
        NrBudynku nvarchar(50) NOT NULL, KodPocztowy varchar(20) NOT NULL,
        X decimal(15,3) NOT NULL, Y decimal(15,3) NOT NULL, Srid int NOT NULL,
        Latitude decimal(10,7) NOT NULL, Longitude decimal(10,7) NOT NULL,
        CityNorm nvarchar(200) NOT NULL, StreetNorm nvarchar(300) NOT NULL,
        NumberNorm nvarchar(50) NOT NULL, WojNorm nvarchar(200) NULL, PowNorm nvarchar(200) NULL,
        MatchKey binary(32) NOT NULL
    );
    CREATE UNIQUE INDEX UX_PrgAdresy_SourceId ON dbo.PrgAdresy(SourceId);
    CREATE INDEX IX_PrgAdresy_MatchKey ON dbo.PrgAdresy(MatchKey,Snapshot)
        INCLUDE(CityNorm,StreetNorm,NumberNorm,WojNorm,PowNorm);
END;
IF EXISTS (SELECT 1 FROM dbo.PrgImporterState WHERE Id=1 AND SchemaVersion=1)
BEGIN
    ALTER TABLE dbo.PrgAdresy ALTER COLUMN Ulic varchar(32) NOT NULL;
    UPDATE dbo.PrgImporterState SET SchemaVersion=2 WHERE Id=1;
END;
IF NOT EXISTS (SELECT 1 FROM dbo.PrgImporterState WHERE Id=1 AND SchemaVersion=2)
    THROW 50002, 'Unsupported importer schema version.', 1;
COMMIT;
