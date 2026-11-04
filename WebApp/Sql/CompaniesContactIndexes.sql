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
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'ColdCall.Companies') AND name = N'IX_Companies_Phone')
    CREATE NONCLUSTERED INDEX [IX_Companies_Phone]
        ON [ColdCall].[Companies] ([Phone]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'ColdCall.Companies') AND name = N'IX_Companies_Email')
    CREATE NONCLUSTERED INDEX [IX_Companies_Email]
        ON [ColdCall].[Companies] ([Email]);
GO
