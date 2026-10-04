SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET NOCOUNT ON;

BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'ColdCall.CompanyPkds', N'U') IS NULL
        THROW 50001, 'ColdCall.CompanyPkds does not exist.', 1;

    IF COL_LENGTH(N'ColdCall.CompanyPkds', N'Id') IS NULL
        ALTER TABLE [ColdCall].[CompanyPkds] ADD [Id] int IDENTITY(1,1) NOT NULL;

    IF COLUMNPROPERTY(OBJECT_ID(N'ColdCall.CompanyPkds'), N'Id', 'IsIdentity') <> 1
        THROW 50002, 'CompanyPkds.Id must be an identity column.', 1;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'ColdCall.CompanyPkds')
          AND name = N'IX_CompanyPkds_CompanyId_PkdCodeId')
        CREATE UNIQUE NONCLUSTERED INDEX [IX_CompanyPkds_CompanyId_PkdCodeId]
        ON [ColdCall].[CompanyPkds] ([CompanyId] ASC, [PkdCodeId] ASC);

    DECLARE @pkName sysname, @pkIndex int;
    SELECT @pkName = name, @pkIndex = unique_index_id FROM sys.key_constraints
    WHERE parent_object_id = OBJECT_ID(N'ColdCall.CompanyPkds') AND type = 'PK';

    IF @pkName IS NOT NULL AND NOT EXISTS (
        SELECT 1 FROM sys.index_columns ic JOIN sys.columns c
        ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE ic.object_id = OBJECT_ID(N'ColdCall.CompanyPkds') AND ic.index_id = @pkIndex
          AND ic.key_ordinal = 1 AND c.name = N'Id'
          AND NOT EXISTS (SELECT 1 FROM sys.index_columns other
            WHERE other.object_id = ic.object_id AND other.index_id = ic.index_id AND other.key_ordinal > 1))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys
            WHERE referenced_object_id = OBJECT_ID(N'ColdCall.CompanyPkds') AND key_index_id = @pkIndex)
            THROW 50003, 'The old primary key is referenced by another table.', 1;
        DECLARE @dropSql nvarchar(max) = N'ALTER TABLE [ColdCall].[CompanyPkds] DROP CONSTRAINT ' + QUOTENAME(@pkName);
        EXEC(@dropSql);
        SET @pkName = NULL;
    END;

    IF @pkName IS NULL
        EXEC(N'ALTER TABLE [ColdCall].[CompanyPkds] ADD CONSTRAINT [PK_CompanyPkds] PRIMARY KEY CLUSTERED ([Id] ASC)');

    COMMIT TRANSACTION;
    PRINT 'CompanyPkds: Id IDENTITY primary key and unique CompanyId ASC, PkdCodeId ASC index ready.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
