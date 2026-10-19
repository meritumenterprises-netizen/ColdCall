SET XACT_ABORT ON;
IF COL_LENGTH(N'ColdCall.SearchProfileCriteria', N'CompanyStatusIds') IS NULL
    ALTER TABLE ColdCall.SearchProfileCriteria ADD CompanyStatusIds nvarchar(1000) NULL;
