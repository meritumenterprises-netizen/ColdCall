USE [Firmy_Temp];
GO

-- Matches the grid's ORDER BY StartedAt DESC, Id ASC.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'ColdCall.Companies')
      AND name = N'IX_Companies_StartedAt_Id'
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Companies_StartedAt_Id]
    ON [ColdCall].[Companies] ([StartedAt] DESC, [Id] ASC)
    INCLUDE ([WojewodztwoId]);
END;
GO
