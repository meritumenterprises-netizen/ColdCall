-- Optional performance indexes. Review and execute separately; the importer does not run this file.
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('ColdCall.CompanyAddress') AND name='IX_PrgImporter_CompanyAddress_CompanyId')
    CREATE INDEX IX_PrgImporter_CompanyAddress_CompanyId
        ON ColdCall.CompanyAddress(CompanyId) INCLUDE(AddressId);

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('ColdCall.Companies') AND name='IX_PrgImporter_Companies_MissingAddress')
    CREATE INDEX IX_PrgImporter_Companies_MissingAddress
        ON ColdCall.Companies(Id) INCLUDE(MiejscowoscId,WojewodztwoId,PowiatId)
        WHERE AddressIsMissing=1 AND BlockedByRODO=0;
