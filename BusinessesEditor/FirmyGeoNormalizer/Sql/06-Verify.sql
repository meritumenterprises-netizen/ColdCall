IF EXISTS(SELECT 1 FROM #Source s JOIN #Mapping x ON x.FirmaId=s.Id
 WHERE (s.P IS NOT NULL AND x.PowiatId IS NULL)
 OR (s.G IS NOT NULL AND x.GminaId IS NULL)
 OR (s.M IS NOT NULL AND x.MiejscowoscId IS NULL))
 THROW 51007,N'Brak referencji mimo niepustej nazwy. Migracja zostanie wycofana.',1;
IF EXISTS(SELECT FirmaId,PowiatId,GminaId,MiejscowoscId FROM #Mapping EXCEPT SELECT Id,PowiatId,GminaId,MiejscowoscId FROM dbo.Firmy)
 THROW 51008,N'Weryfikacja zapisanych referencji nie powiodła się.',1;
IF (SELECT COUNT_BIG(*) FROM #Source)<>(SELECT COUNT_BIG(*) FROM dbo.Firmy)
 THROW 51009,N'Liczba firm zmieniła się podczas migracji.',1;
