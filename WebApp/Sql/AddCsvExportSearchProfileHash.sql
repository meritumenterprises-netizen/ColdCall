IF COL_LENGTH(N'ColdCall.CSVExport', N'SearchProfileHash') IS NULL
    ALTER TABLE ColdCall.CSVExport ADD SearchProfileHash nvarchar(32) NULL;
