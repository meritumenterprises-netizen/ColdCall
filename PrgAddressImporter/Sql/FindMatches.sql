INSERT #Matches(CompanyId,MatchCount,PrgId)
SELECT C.CompanyId, COUNT_BIG(P.Id) AS MatchCount, MIN(P.Id) AS PrgId
FROM #Candidates C
LEFT JOIN dbo.PrgAdresy P ON P.MatchKey=C.MatchKey AND P.Snapshot=@snapshot
    AND P.CityNorm=C.CityNorm AND P.StreetNorm=C.StreetNorm AND P.NumberNorm=C.NumberNorm
    AND (C.WojNorm='' OR C.WojNorm=P.WojNorm)
    AND (C.PowNorm='' OR C.PowNorm=P.PowNorm)
GROUP BY C.CompanyId;
SELECT CompanyId, MatchCount, PrgId FROM #Matches ORDER BY CompanyId;
