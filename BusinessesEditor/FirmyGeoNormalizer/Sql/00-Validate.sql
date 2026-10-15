IF OBJECT_ID(N'dbo.Firmy',N'U') IS NULL THROW 51001,N'Nie znaleziono tabeli dbo.Firmy w wybranej bazie.',1;
DECLARE @missing nvarchar(2048)=N'';
SELECT @missing=@missing+CASE WHEN @missing=N'' THEN N'' ELSE N', ' END+v.Nazwa
FROM (VALUES(N'Id'),(N'Detail_Powiat'),(N'Detail_Gmina'),(N'Detail_Miejscowosc')) v(Nazwa)
WHERE NOT EXISTS(SELECT 1 FROM sys.columns c WHERE c.object_id=OBJECT_ID(N'dbo.Firmy') AND c.name=v.Nazwa);
IF @missing<>N''
BEGIN
 SET @missing=N'Brakujące kolumny w dbo.Firmy: '+@missing;
 THROW 51002,@missing,1;
END;
