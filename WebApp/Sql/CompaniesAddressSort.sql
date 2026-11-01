USE [Firmy_temp];
GO
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
IF COL_LENGTH(N'ColdCall.Companies', N'AddressSort') IS NULL
    ALTER TABLE [ColdCall].[Companies] ADD [AddressSort] nvarchar(800) NULL;
GO
SET NOCOUNT ON;
DECLARE @AfterId int = 0;
DECLARE @Batch TABLE (Id int PRIMARY KEY);
WHILE 1 = 1
BEGIN
    DELETE FROM @Batch;
    INSERT INTO @Batch (Id)
        SELECT TOP (10000) Id FROM [ColdCall].[Companies]
        WHERE Id > @AfterId ORDER BY Id;
    IF @@ROWCOUNT = 0 BREAK;
    UPDATE c SET AddressSort = COALESCE(m.Name, N'') + N' ' + COALESCE(c.PostalCode, N'')
        + N' ' + COALESCE(c.Street, N'') + N' ' + COALESCE(c.BuildingNumber, N'')
        + CASE WHEN NULLIF(LTRIM(RTRIM(c.ApartmentNumber)), N'') IS NULL THEN N'' ELSE N'/' + LTRIM(RTRIM(c.ApartmentNumber)) END
    FROM [ColdCall].[Companies] c
    JOIN @Batch b ON b.Id = c.Id
    LEFT JOIN [ColdCall].[Miejscowosci] m ON m.Id = c.MiejscowoscId;
    SELECT @AfterId = MAX(Id) FROM @Batch;
END;
GO
