using System;
using System.Collections.Generic;
using System.Text;

namespace ImportCEIDGByDate
{
    public enum ImportPhasesEnum
    {
        Unknown = 0,
        Initialization,
        DownloadFolderCleanup,
        DownloadAndParseReport,
        DownloadNewFiles,
        DownloadNewFile,
        UnpackNewFiles,
        ImportNewFilesToTargetImportTable,
        ImportNewFileToTargetImportTable,
        ContentXmlCleanup,
        UpdateImportTargetMetadata,
        UpdateCompanyXml,
        UpdateCompanyXmlFromImportTarget,
        ImportNewCompaniesToDatabase,
        ImportDeferredAddressesAsync,
        UpdateExistingCompaniesFromTargetInDatabase,
        UpdateAdresyForChangedCompaniesFromTarget,
        RemoveNIPsFromImportOriginThatArePresentInOriginTarget,
        UpdateSQLStatistics
    }
}
