SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'ColdCall.SearchProfileCriteria', N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'ColdCall.CK_SearchProfileCriteria_Dates', N'C') IS NOT NULL
        ALTER TABLE ColdCall.SearchProfileCriteria DROP CONSTRAINT CK_SearchProfileCriteria_Dates;
    ALTER TABLE ColdCall.SearchProfileCriteria WITH CHECK
        ADD CONSTRAINT CK_SearchProfileCriteria_Dates CHECK (
            (DateRangeEnabled = 0 AND DateFrom IS NULL AND DateTo IS NULL) OR
            (DateRangeEnabled = 1 AND DateFrom IS NOT NULL AND DateTo IS NOT NULL AND DateTo >= DateFrom));
END;
COMMIT;
