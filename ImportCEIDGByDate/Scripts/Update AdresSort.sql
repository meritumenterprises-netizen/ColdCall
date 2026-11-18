begin tran

UPDATE c
SET c.AddressSort =
    COALESCE(m.Name, N'') + N''
    + N' ' + COALESCE(c.PostalCode, N'')
    + N' ' + COALESCE(c.Street, N'')
    + N' ' + COALESCE(c.BuildingNumber, N'')
    + CASE
        WHEN NULLIF(LTRIM(RTRIM(c.ApartmentNumber)), N'') IS NULL
            THEN N''
        ELSE N'/' + LTRIM(RTRIM(c.ApartmentNumber))
      END
FROM ColdCall.Companies AS c
LEFT JOIN ColdCall.Miejscowosci AS m
    ON m.Id = c.MiejscowoscId
WHERE NULLIF(LTRIM(RTRIM(c.AddressSort)), N'') IS NULL;

SELECT @@ROWCOUNT AS Zaktualizowano;

-- rollback tran
commit tran