SET XACT_ABORT ON;
BEGIN TRANSACTION;
UPDATE P SET
    GmlId=B.GmlId, Snapshot=B.Snapshot, Terc=B.Terc, Simc=B.Simc,
    Miejscowosc=B.Miejscowosc, Ulic=B.Ulic, Ulica=B.Ulica, NrBudynku=B.NrBudynku,
    KodPocztowy=B.KodPocztowy, X=B.X, Y=B.Y, Srid=B.Srid,
    Latitude=B.Latitude, Longitude=B.Longitude, CityNorm=B.CityNorm,
    StreetNorm=B.StreetNorm, NumberNorm=B.NumberNorm, MatchKey=B.MatchKey,
    Wojewodztwo=NULL, Powiat=NULL, Gmina=NULL, WojNorm=NULL, PowNorm=NULL
FROM dbo.PrgAdresy P JOIN #PrgBatch B ON B.SourceId=P.SourceId;
INSERT dbo.PrgAdresy
    (SourceId,GmlId,Snapshot,Terc,Simc,Miejscowosc,Ulic,Ulica,NrBudynku,KodPocztowy,
     X,Y,Srid,Latitude,Longitude,CityNorm,StreetNorm,NumberNorm,MatchKey)
SELECT SourceId,GmlId,Snapshot,Terc,Simc,Miejscowosc,Ulic,Ulica,NrBudynku,KodPocztowy,
       X,Y,Srid,Latitude,Longitude,CityNorm,StreetNorm,NumberNorm,MatchKey
FROM #PrgBatch B WHERE NOT EXISTS
    (SELECT 1 FROM dbo.PrgAdresy P WITH (UPDLOCK,HOLDLOCK) WHERE P.SourceId=B.SourceId);
COMMIT;
TRUNCATE TABLE #PrgBatch;
