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
-- Descending contact order keeps Id ascending for stable paging.
-- The ascending single-column indexes cannot supply that mixed ordering.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'ColdCall.Companies') AND name=N'IX_Companies_PhoneDesc_Id')
    CREATE NONCLUSTERED INDEX [IX_Companies_PhoneDesc_Id]
        ON [ColdCall].[Companies] ([Phone] DESC, [Id] ASC) INCLUDE ([StatusDzialalnosciId]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'ColdCall.Companies') AND name=N'IX_Companies_EmailDesc_Id')
    CREATE NONCLUSTERED INDEX [IX_Companies_EmailDesc_Id]
        ON [ColdCall].[Companies] ([Email] DESC, [Id] ASC) INCLUDE ([StatusDzialalnosciId]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'ColdCall.Companies') AND name=N'IX_Companies_WebsiteDesc_Id')
    CREATE NONCLUSTERED INDEX [IX_Companies_WebsiteDesc_Id]
        ON [ColdCall].[Companies] ([Website] DESC, [Id] ASC) INCLUDE ([StatusDzialalnosciId]);
GO
