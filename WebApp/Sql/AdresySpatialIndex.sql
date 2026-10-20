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

-- Run explicitly during a maintenance window; creating a spatial index can block writes.
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes AS i
    JOIN sys.index_columns AS ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
    JOIN sys.columns AS c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
    WHERE i.object_id=OBJECT_ID(N'ColdCall.Adresy') AND i.type=4 AND c.name=N'Location'
)
BEGIN
    CREATE SPATIAL INDEX [SIX_Adresy_Location]
    ON [ColdCall].[Adresy] ([Location])
    USING GEOGRAPHY_AUTO_GRID;
END;
GO
