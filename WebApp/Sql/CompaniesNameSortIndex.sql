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
-- Reuse the duplicate ascending index for Name DESC, clustered Id ASC.
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'ColdCall.Companies') AND name=N'NonClusteredIndex-20261003-065101')
    CREATE NONCLUSTERED INDEX [NonClusteredIndex-20261003-065101]
        ON [ColdCall].[Companies] ([Name] DESC) INCLUDE ([StatusDzialalnosciId]) WITH (DROP_EXISTING=ON);
ELSE
    CREATE NONCLUSTERED INDEX [NonClusteredIndex-20261003-065101]
        ON [ColdCall].[Companies] ([Name] DESC) INCLUDE ([StatusDzialalnosciId]);
GO
