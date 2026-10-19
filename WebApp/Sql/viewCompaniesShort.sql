USE [Firmy_Temp];
GO

CREATE OR ALTER VIEW [ColdCall].[viewCompaniesShort]
AS
SELECT
    c.[Id],
    c.[Name],
    c.[Nip],
    CONCAT_WS(N' ',
        a.Miejscowosc,
        CASE WHEN a.PostalCode IS NOT NULL THEN CONCAT(a.PostalCode, N',') END,
        a.Street,
        NULLIF(CONCAT(a.BuildingNumber,
            CASE WHEN a.ApartmentNumber IS NOT NULL THEN CONCAT(N'/', a.ApartmentNumber) END), N'')
    ) AS [FullAddress],
    c.[Phone],
    c.[Email],
    c.[Website] AS [Www],
    c.[StartedAt] AS [DataAktywnosci],
    (SELECT s.[Nazwa] FROM [ColdCall].[CompanyStatus] AS s WHERE s.[StatusId] = c.[StatusDzialalnosciId]) AS [StatusDzialalnosci],
    b.[TypDzialalnosci] AS [TypDzialalnosci]
FROM [ColdCall].[Companies] AS c
LEFT JOIN [ColdCall].[Wojewodztwa] AS w ON w.[Id] = c.[WojewodztwoId]
LEFT JOIN [ColdCall].[Miejscowosci] AS m ON m.[Id] = c.[MiejscowoscId]
LEFT JOIN [ColdCall].[BusinessType] AS b ON b.[TypDzialalnosciId] = c.[BusinessType]
CROSS APPLY (VALUES (
    NULLIF(TRIM(m.[Name]), N''),
    NULLIF(TRIM(c.[PostalCode]), N''),
    NULLIF(TRIM(c.[Street]), N''),
    NULLIF(TRIM(c.[BuildingNumber]), N''),
    NULLIF(TRIM(c.[ApartmentNumber]), N'')
)) AS a(Miejscowosc, PostalCode, Street, BuildingNumber, ApartmentNumber);
GO
