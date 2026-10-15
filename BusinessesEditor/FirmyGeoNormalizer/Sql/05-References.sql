SELECT s.Id AS FirmaId,p.Id AS PowiatId,g.Id AS GminaId,m.Id AS MiejscowoscId
INTO #Mapping FROM #Source s
LEFT JOIN dbo.Powiat p ON p.Nazwa=s.P
LEFT JOIN dbo.Gmina g ON g.Nazwa=s.G
LEFT JOIN dbo.Miejscowosc m ON m.Nazwa=s.M;
CREATE UNIQUE CLUSTERED INDEX IX_GeoMapping_Id ON #Mapping(FirmaId);
UPDATE f SET PowiatId=x.PowiatId,GminaId=x.GminaId,MiejscowoscId=x.MiejscowoscId
FROM dbo.Firmy f JOIN #Mapping x ON x.FirmaId=f.Id
WHERE EXISTS(SELECT f.PowiatId,f.GminaId,f.MiejscowoscId EXCEPT SELECT x.PowiatId,x.GminaId,x.MiejscowoscId);
SELECT @@ROWCOUNT AS ZaktualizowaneFirmy;
