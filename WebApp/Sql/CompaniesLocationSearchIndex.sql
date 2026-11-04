SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'ColdCall.Companies') AND name = N'IX_Companies_Location_Status')
    CREATE INDEX IX_Companies_Location_Status
    ON ColdCall.Companies (WojewodztwoId, PowiatId, GminaId, MiejscowoscId, StatusDzialalnosciId)
    INCLUDE (StartedAt);
