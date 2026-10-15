IF OBJECT_ID(N'dbo.FK_Geo_Firmy_Powiat',N'F') IS NULL
 ALTER TABLE dbo.Firmy WITH CHECK ADD CONSTRAINT FK_Geo_Firmy_Powiat FOREIGN KEY(PowiatId) REFERENCES dbo.Powiat(Id);
IF OBJECT_ID(N'dbo.FK_Geo_Firmy_Gmina',N'F') IS NULL
 ALTER TABLE dbo.Firmy WITH CHECK ADD CONSTRAINT FK_Geo_Firmy_Gmina FOREIGN KEY(GminaId) REFERENCES dbo.Gmina(Id);
IF OBJECT_ID(N'dbo.FK_Geo_Firmy_Miejscowosc',N'F') IS NULL
 ALTER TABLE dbo.Firmy WITH CHECK ADD CONSTRAINT FK_Geo_Firmy_Miejscowosc FOREIGN KEY(MiejscowoscId) REFERENCES dbo.Miejscowosc(Id);
ALTER TABLE dbo.Firmy CHECK CONSTRAINT FK_Geo_Firmy_Powiat,FK_Geo_Firmy_Gmina,FK_Geo_Firmy_Miejscowosc;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Firmy') AND name=N'IX_Geo_Firmy_PowiatId')
 CREATE INDEX IX_Geo_Firmy_PowiatId ON dbo.Firmy(PowiatId);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Firmy') AND name=N'IX_Geo_Firmy_GminaId')
 CREATE INDEX IX_Geo_Firmy_GminaId ON dbo.Firmy(GminaId);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Firmy') AND name=N'IX_Geo_Firmy_MiejscowoscId')
 CREATE INDEX IX_Geo_Firmy_MiejscowoscId ON dbo.Firmy(MiejscowoscId);
