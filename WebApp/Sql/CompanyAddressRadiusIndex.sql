SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET NOCOUNT ON;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'ColdCall.CompanyAddress')
        AND name = N'IX_CompanyAddress_AddressId_CompanyId'
)
    CREATE INDEX IX_CompanyAddress_AddressId_CompanyId
        ON ColdCall.CompanyAddress (AddressId, CompanyId);

