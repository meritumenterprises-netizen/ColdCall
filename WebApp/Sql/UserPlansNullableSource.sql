SET XACT_ABORT ON;
SET LOCK_TIMEOUT 10000;

IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'ColdCall.UserPlans')
      AND name = N'Source' AND is_nullable = 0
)
    ALTER TABLE [ColdCall].[UserPlans] ALTER COLUMN [Source] nvarchar(max) NULL;
