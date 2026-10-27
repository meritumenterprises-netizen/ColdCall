-- Avoid a full address-table scan for every imported company.
SET LOCK_TIMEOUT 3000;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'ColdCall.Adresy')
      AND name = N'IX_Adresy_PrngId'
)
    CREATE NONCLUSTERED INDEX IX_Adresy_PrngId
        ON ColdCall.Adresy (PrngId)
        WITH (MAXDOP = 2);
