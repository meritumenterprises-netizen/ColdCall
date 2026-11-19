SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'ColdCall.SearchProfileMcp', N'U') IS NULL
    THROW 50000, 'ColdCall.SearchProfileMcp must exist before configuring cascade delete.', 1;

IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id = OBJECT_ID(N'ColdCall.SearchProfileMcp')
      AND name = N'FK_SearchProfileMcp_SearchProfiles'
      AND delete_referential_action = 1
      AND is_disabled = 0 AND is_not_trusted = 0
)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.foreign_keys
        WHERE parent_object_id = OBJECT_ID(N'ColdCall.SearchProfileMcp')
          AND name = N'FK_SearchProfileMcp_SearchProfiles')
        ALTER TABLE ColdCall.SearchProfileMcp DROP CONSTRAINT FK_SearchProfileMcp_SearchProfiles;

    ALTER TABLE ColdCall.SearchProfileMcp WITH CHECK
        ADD CONSTRAINT FK_SearchProfileMcp_SearchProfiles
        FOREIGN KEY (SearchProfileId) REFERENCES ColdCall.SearchProfiles(Id) ON DELETE CASCADE;
END;

COMMIT TRANSACTION;
