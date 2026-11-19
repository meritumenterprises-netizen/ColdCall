IF OBJECT_ID(N'ColdCall.SearchProfileCriteria', N'U') IS NULL
    THROW 50000, 'ColdCall.SearchProfileCriteria is missing. Initialize the application database first.', 1;

IF COL_LENGTH(N'ColdCall.SearchProfileCriteria', N'HasAddress') IS NULL
    ALTER TABLE ColdCall.SearchProfileCriteria ADD HasAddress bit NULL;
