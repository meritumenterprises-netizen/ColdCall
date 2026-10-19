IF COL_LENGTH(N'ColdCall.SearchProfiles', N'LastSavedAtUtc') IS NULL
BEGIN
    ALTER TABLE ColdCall.SearchProfiles ADD LastSavedAtUtc datetime2 NULL;
END;
