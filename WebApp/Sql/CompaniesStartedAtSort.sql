USE [Firmy_temp];
GO
DROP INDEX IF EXISTS [IX_Companies_ActivityStartedAt_Id] ON [ColdCall].[Companies];
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'ColdCall.Companies')
               AND name = N'IX_Companies_StartedAt_Id')
    CREATE NONCLUSTERED INDEX [IX_Companies_StartedAt_Id]
        ON [ColdCall].[Companies] ([StartedAt] DESC, [Id] ASC)
        INCLUDE ([WojewodztwoId]);
GO
