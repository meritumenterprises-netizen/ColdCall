CREATE OR ALTER VIEW dbo.vFirmyGeografia AS
SELECT f.Id AS FirmaId,f.PowiatId,f.GminaId,f.MiejscowoscId,
 COALESCE(p.Nazwa,CONVERT(nvarchar(max),f.Detail_Powiat)) AS Powiat,
 COALESCE(g.Nazwa,CONVERT(nvarchar(max),f.Detail_Gmina)) AS Gmina,
 COALESCE(m.Nazwa,CONVERT(nvarchar(max),f.Detail_Miejscowosc)) AS Miejscowosc
FROM dbo.Firmy f
LEFT JOIN dbo.Powiat p ON p.Id=f.PowiatId
LEFT JOIN dbo.Gmina g ON g.Id=f.GminaId
LEFT JOIN dbo.Miejscowosc m ON m.Id=f.MiejscowoscId;
