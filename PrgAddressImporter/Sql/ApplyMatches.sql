SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @Saved TABLE(Id int NOT NULL);
-- Recheck both eligibility and the original company address while holding update locks.
SELECT M.CompanyId,P.* INTO #Eligible
FROM #Matches M
JOIN #Candidates K ON K.CompanyId=M.CompanyId
JOIN ColdCall.Companies C WITH(UPDLOCK,HOLDLOCK) ON C.Id=M.CompanyId
JOIN dbo.PrgAdresy P ON P.Id=M.PrgId
WHERE M.MatchCount=1 AND C.AddressIsMissing=1 AND C.BlockedByRODO=0
    AND NOT EXISTS (SELECT 1 FROM ColdCall.CompanyAddress WITH(UPDLOCK,HOLDLOCK) WHERE CompanyId=C.Id)
    AND ISNULL(C.Street,'')=K.OriginalStreet AND ISNULL(C.BuildingNumber,'')=K.OriginalNumber
    AND ISNULL(C.MiejscowoscId,-1)=K.CityId AND ISNULL(C.WojewodztwoId,-1)=K.WojId AND ISNULL(C.PowiatId,-1)=K.PowId
    AND P.Wojewodztwo IS NOT NULL AND P.Powiat IS NOT NULL AND P.Gmina IS NOT NULL
    AND LEN(P.Ulica)<=200 AND LEN(P.NrBudynku)<=50
    AND NOT EXISTS
    (
        SELECT 1 FROM ColdCall.Adresy A
        JOIN ColdCall.Wojewodztwa W ON W.Id=A.WojewodztwoId
        JOIN ColdCall.Powiaty PW ON PW.Id=A.PowiatId
        JOIN ColdCall.Gminy G ON G.Id=A.GminaId
        JOIN ColdCall.Miejscowosci M ON M.Id=A.MiejscowoscId
        WHERE A.PrngId=P.SourceId AND
            (A.Latitude<>CONVERT(decimal(9,6),P.Latitude) OR A.Longitude<>CONVERT(decimal(9,6),P.Longitude)
             OR ISNULL(A.Ulica,'')<>P.Ulica OR ISNULL(A.NrUlicy,'')<>P.NrBudynku
             OR W.Name<>P.Wojewodztwo OR PW.Name<>P.Powiat OR G.Name<>P.Gmina OR M.Name<>P.Miejscowosc)
    );

INSERT ColdCall.Wojewodztwa(Name)
SELECT DISTINCT Wojewodztwo FROM #Eligible E WHERE NOT EXISTS
    (SELECT 1 FROM ColdCall.Wojewodztwa WITH(UPDLOCK,HOLDLOCK) WHERE Name=E.Wojewodztwo);
INSERT ColdCall.Powiaty(Name)
SELECT DISTINCT Powiat FROM #Eligible E WHERE NOT EXISTS
    (SELECT 1 FROM ColdCall.Powiaty WITH(UPDLOCK,HOLDLOCK) WHERE Name=E.Powiat);
INSERT ColdCall.Gminy(Name)
SELECT DISTINCT Gmina FROM #Eligible E WHERE NOT EXISTS
    (SELECT 1 FROM ColdCall.Gminy WITH(UPDLOCK,HOLDLOCK) WHERE Name=E.Gmina);
INSERT ColdCall.Miejscowosci(Name)
SELECT DISTINCT Miejscowosc FROM #Eligible E WHERE NOT EXISTS
    (SELECT 1 FROM ColdCall.Miejscowosci WITH(UPDLOCK,HOLDLOCK) WHERE Name=E.Miejscowosc);

INSERT ColdCall.Adresy(WojewodztwoId,PowiatId,GminaId,MiejscowoscId,Ulica,NrUlicy,Latitude,Longitude,Location,PrngId)
SELECT W.Id,P.Id,G.Id,M.Id,NULLIF(E.Ulica,''),E.NrBudynku,
    CONVERT(decimal(9,6),E.Latitude),CONVERT(decimal(9,6),E.Longitude),
    geography::Point(CONVERT(float,E.Latitude),CONVERT(float,E.Longitude),4326),E.SourceId
FROM dbo.PrgAdresy E
JOIN ColdCall.Wojewodztwa W ON W.Name=E.Wojewodztwo
JOIN ColdCall.Powiaty P ON P.Name=E.Powiat
JOIN ColdCall.Gminy G ON G.Name=E.Gmina
JOIN ColdCall.Miejscowosci M ON M.Name=E.Miejscowosc
WHERE EXISTS(SELECT 1 FROM #Eligible K WHERE K.Id=E.Id)
    AND NOT EXISTS(SELECT 1 FROM ColdCall.Adresy WITH(UPDLOCK,HOLDLOCK) WHERE PrngId=E.SourceId);

INSERT ColdCall.CompanyAddress(CompanyId,AddressId)
SELECT E.CompanyId,A.Id FROM #Eligible E JOIN ColdCall.Adresy A ON A.PrngId=E.SourceId
WHERE NOT EXISTS(SELECT 1 FROM ColdCall.CompanyAddress WITH(UPDLOCK,HOLDLOCK) WHERE CompanyId=E.CompanyId);

UPDATE C SET AddressIsMissing=0
OUTPUT inserted.Id INTO @Saved(Id)
FROM ColdCall.Companies C JOIN #Eligible E ON E.CompanyId=C.Id
WHERE EXISTS(SELECT 1 FROM ColdCall.CompanyAddress CA JOIN ColdCall.Adresy A ON A.Id=CA.AddressId
             WHERE CA.CompanyId=C.Id AND A.PrngId=E.SourceId);
COMMIT;
SELECT M.CompanyId,CONVERT(bit,CASE WHEN S.Id IS NULL THEN 0 ELSE 1 END) AS Saved
FROM #Matches M LEFT JOIN @Saved S ON S.Id=M.CompanyId
WHERE M.MatchCount=1 ORDER BY M.CompanyId;
DROP TABLE #Eligible;
