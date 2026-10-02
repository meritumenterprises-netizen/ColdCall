SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'ColdCall.CompanyStatus', N'U') IS NULL
        CREATE TABLE [ColdCall].[CompanyStatus] (
            [StatusId] tinyint NOT NULL CONSTRAINT [PK_CompanyStatus] PRIMARY KEY,
            [Nazwa] nvarchar(100) NOT NULL
        );
    IF NOT EXISTS (SELECT 1 FROM [ColdCall].[CompanyStatus] WHERE [StatusId] = 1)
        INSERT INTO [ColdCall].[CompanyStatus] ([StatusId], [Nazwa]) VALUES (1, N'Aktywny');
    IF COL_LENGTH(N'ColdCall.Companies', N'StatusDzialalnosciId') IS NULL
        ALTER TABLE [ColdCall].[Companies] ADD [StatusDzialalnosciId] tinyint NOT NULL
            CONSTRAINT [DF_Companies_StatusDzialalnosciId] DEFAULT (1) WITH VALUES;

    -- Update legacy views before removing their source column.
    DECLARE @view nvarchar(max), @objectId int;
    DECLARE statusViews CURSOR LOCAL FAST_FORWARD FOR
        SELECT object_id, definition FROM sys.sql_modules
        WHERE object_id IN (OBJECT_ID(N'ColdCall.viewCompaniesShort'),
            OBJECT_ID(N'dbo.viewCompanies'), OBJECT_ID(N'dbo.viewCompaniesWithPKDs'))
          AND (definition LIKE N'%c.[[]StatusDzialalnosci]%'
            OR definition LIKE N'%ColdCall.Companies.StatusDzialalnosci,%');
    OPEN statusViews;
    FETCH NEXT FROM statusViews INTO @objectId, @view;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @view = REPLACE(@view, N'c.[StatusDzialalnosci]',
            N'(SELECT s.[Nazwa] FROM [ColdCall].[CompanyStatus] AS s WHERE s.[StatusId] = c.[StatusDzialalnosciId]) AS [StatusDzialalnosci]');
        SET @view = REPLACE(@view, N'ColdCall.Companies.StatusDzialalnosci,',
            N'(SELECT s.[Nazwa] FROM [ColdCall].[CompanyStatus] AS s WHERE s.[StatusId] = ColdCall.Companies.[StatusDzialalnosciId]) AS [StatusDzialalnosci],');
        SET @view = N'ALTER ' + SUBSTRING(@view, CHARINDEX(N'VIEW', UPPER(@view)), LEN(@view));
        EXEC sys.sp_executesql @view;
        FETCH NEXT FROM statusViews INTO @objectId, @view;
    END;
    CLOSE statusViews;
    DEALLOCATE statusViews;

    IF COL_LENGTH(N'ColdCall.Companies', N'StatusDzialalnosci') IS NOT NULL
    BEGIN
        DECLARE @default sysname, @drop nvarchar(max);
        SELECT @default = dc.name FROM sys.default_constraints AS dc
        JOIN sys.columns AS c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
        WHERE c.object_id = OBJECT_ID(N'ColdCall.Companies') AND c.name = N'StatusDzialalnosci';
        IF @default IS NOT NULL
        BEGIN
            SET @drop = N'ALTER TABLE [ColdCall].[Companies] DROP CONSTRAINT ' + QUOTENAME(@default);
            EXEC sys.sp_executesql @drop;
        END;
        ALTER TABLE [ColdCall].[Companies] DROP COLUMN [StatusDzialalnosci];
    END;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'ColdCall.Companies')
        AND name = N'FK_Companies_CompanyStatus_StatusDzialalnosciId')
        ALTER TABLE [ColdCall].[Companies] WITH CHECK ADD
            CONSTRAINT [FK_Companies_CompanyStatus_StatusDzialalnosciId]
            FOREIGN KEY ([StatusDzialalnosciId]) REFERENCES [ColdCall].[CompanyStatus] ([StatusId]);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'ColdCall.Companies')
        AND name = N'IX_Companies_StatusDzialalnosciId')
        CREATE INDEX [IX_Companies_StatusDzialalnosciId] ON [ColdCall].[Companies] ([StatusDzialalnosciId]);
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
