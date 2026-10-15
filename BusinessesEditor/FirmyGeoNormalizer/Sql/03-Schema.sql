IF OBJECT_ID(N'dbo.FirmyGeoNormalizerState',N'U') IS NULL
BEGIN
 IF OBJECT_ID(N'dbo.Powiat') IS NOT NULL OR OBJECT_ID(N'dbo.Gmina') IS NOT NULL OR OBJECT_ID(N'dbo.Miejscowosc') IS NOT NULL
 OR OBJECT_ID(N'dbo.vFirmyGeografia') IS NOT NULL
 OR COL_LENGTH(N'dbo.Firmy',N'PowiatId') IS NOT NULL OR COL_LENGTH(N'dbo.Firmy',N'GminaId') IS NOT NULL OR COL_LENGTH(N'dbo.Firmy',N'MiejscowoscId') IS NOT NULL
 THROW 51005,N'Istnieją obiekty nieutworzone przez tę aplikację. Wymagane dopasowanie schematu.',1;
 EXEC sys.sp_executesql N'
 CREATE TABLE dbo.FirmyGeoNormalizerState(Id int NOT NULL PRIMARY KEY,SchemaVersion int NOT NULL,CreatedAt datetime2 NOT NULL DEFAULT sysdatetime());
 INSERT dbo.FirmyGeoNormalizerState(Id,SchemaVersion) VALUES(1,3);
 CREATE TABLE dbo.Powiat(Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Geo_Powiat PRIMARY KEY,Nazwa nvarchar(200) NOT NULL,CONSTRAINT UQ_Geo_Powiat UNIQUE(Nazwa));
 CREATE TABLE dbo.Gmina(Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Geo_Gmina PRIMARY KEY,Nazwa nvarchar(200) NOT NULL,CONSTRAINT UQ_Geo_Gmina UNIQUE(Nazwa));
 CREATE TABLE dbo.Miejscowosc(Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Geo_Miejscowosc PRIMARY KEY,Nazwa nvarchar(200) NOT NULL,CONSTRAINT UQ_Geo_Miejscowosc UNIQUE(Nazwa));
 ALTER TABLE dbo.Firmy ADD PowiatId int NULL,GminaId int NULL,MiejscowoscId int NULL;';
END;
DECLARE @version int;
EXEC sys.sp_executesql N'SELECT @v=SchemaVersion FROM dbo.FirmyGeoNormalizerState WHERE Id=1',N'@v int OUTPUT',@v=@version OUTPUT;
IF @version IN (1,2)
BEGIN
 -- Remove only the unwanted hierarchy from dictionaries created by previous versions.
 EXEC sys.sp_executesql N'
 ALTER TABLE dbo.Miejscowosc DROP CONSTRAINT FK_Geo_Miejscowosc_Gmina;
 ALTER TABLE dbo.Gmina DROP CONSTRAINT FK_Geo_Gmina_Powiat;
 ALTER TABLE dbo.Miejscowosc DROP CONSTRAINT UQ_Geo_Miejscowosc;
 ALTER TABLE dbo.Gmina DROP CONSTRAINT UQ_Geo_Gmina;
 ALTER TABLE dbo.Powiat DROP CONSTRAINT UQ_Geo_Powiat;
 ALTER TABLE dbo.Miejscowosc DROP COLUMN GminaId;
 ALTER TABLE dbo.Gmina DROP COLUMN PowiatId;';
 IF COL_LENGTH(N'dbo.Powiat',N'WojewodztwoId') IS NOT NULL
  EXEC sys.sp_executesql N'ALTER TABLE dbo.Powiat DROP COLUMN WojewodztwoId;';
 IF COL_LENGTH(N'dbo.Powiat',N'Wojewodztwo') IS NOT NULL
  EXEC sys.sp_executesql N'ALTER TABLE dbo.Powiat DROP COLUMN Wojewodztwo;';
 -- Canonical ID = minimum existing ID for each name; rewire references before removing duplicates.
 EXEC sys.sp_executesql N'
 SELECT Id,MIN(Id) OVER(PARTITION BY Nazwa) AS TargetId INTO #P FROM dbo.Powiat;
 SELECT Id,MIN(Id) OVER(PARTITION BY Nazwa) AS TargetId INTO #G FROM dbo.Gmina;
 SELECT Id,MIN(Id) OVER(PARTITION BY Nazwa) AS TargetId INTO #M FROM dbo.Miejscowosc;
 UPDATE f SET PowiatId=x.TargetId FROM dbo.Firmy f JOIN #P x ON x.Id=f.PowiatId WHERE x.Id<>x.TargetId;
 UPDATE f SET GminaId=x.TargetId FROM dbo.Firmy f JOIN #G x ON x.Id=f.GminaId WHERE x.Id<>x.TargetId;
 UPDATE f SET MiejscowoscId=x.TargetId FROM dbo.Firmy f JOIN #M x ON x.Id=f.MiejscowoscId WHERE x.Id<>x.TargetId;
 DELETE p FROM dbo.Powiat p JOIN #P x ON x.Id=p.Id WHERE x.Id<>x.TargetId;
 DELETE g FROM dbo.Gmina g JOIN #G x ON x.Id=g.Id WHERE x.Id<>x.TargetId;
 DELETE m FROM dbo.Miejscowosc m JOIN #M x ON x.Id=m.Id WHERE x.Id<>x.TargetId;
 ALTER TABLE dbo.Powiat ADD CONSTRAINT UQ_Geo_Powiat UNIQUE(Nazwa);
 ALTER TABLE dbo.Gmina ADD CONSTRAINT UQ_Geo_Gmina UNIQUE(Nazwa);
 ALTER TABLE dbo.Miejscowosc ADD CONSTRAINT UQ_Geo_Miejscowosc UNIQUE(Nazwa);
 UPDATE dbo.FirmyGeoNormalizerState SET SchemaVersion=3 WHERE Id=1;';
END
ELSE IF @version IS NULL OR @version<>3
 THROW 51006,N'Nieobsługiwana wersja schematu.',1;
