SET XACT_ABORT ON;
SET LOCK_TIMEOUT 10000;
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'ColdCall.SearchProfileCriteria', N'U') IS NOT NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.check_constraints
                   WHERE parent_object_id = OBJECT_ID(N'ColdCall.SearchProfileCriteria')
                     AND name = N'CK_SearchProfileCriteria_RecordLimit')
            ALTER TABLE [ColdCall].[SearchProfileCriteria] DROP CONSTRAINT [CK_SearchProfileCriteria_RecordLimit];
        ALTER TABLE [ColdCall].[SearchProfileCriteria] WITH CHECK
            ADD CONSTRAINT [CK_SearchProfileCriteria_RecordLimit] CHECK ([RecordLimit] >= 1);
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
