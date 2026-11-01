USE [Firmy_temp];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
GO
IF COL_LENGTH(N'ColdCall.Companies', N'AddressSortPrefix') IS NULL
    ALTER TABLE [ColdCall].[Companies] ADD [AddressSortPrefix] AS
        (CONVERT(nvarchar(800), LEFT([AddressSort], 800))) PERSISTED;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'ColdCall.Companies')
               AND name = N'IX_Companies_AddressSortPrefix_Id')
    CREATE NONCLUSTERED INDEX [IX_Companies_AddressSortPrefix_Id]
        ON [ColdCall].[Companies] ([AddressSortPrefix], [Id])
        INCLUDE ([AddressSort], [StatusDzialalnosciId]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'ColdCall.Companies')
               AND name = N'IX_Companies_AddressSortPrefixDesc_Id')
    CREATE NONCLUSTERED INDEX [IX_Companies_AddressSortPrefixDesc_Id]
        ON [ColdCall].[Companies] ([AddressSortPrefix] DESC, [Id] ASC)
        INCLUDE ([AddressSort], [StatusDzialalnosciId]);
GO
