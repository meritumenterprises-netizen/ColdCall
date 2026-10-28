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
        DownloadOldFiles,
        DownloadOldFile,
        DownloadNewFiles,
        DownloadNewFile,
        UnpackOldFiles,
        UnpackNewFiles,
        ImportOldFilesToTable,
        ImportOldFileToTable,
        ImportNewFilesToTable,
        ImportNewFileToTable,
        FinishedImportingOldFiles,
        FinishedImportingNewFiles,
        UpdateImportTargetMetadata,
        UpdateCompanyXml,
        UpdateCompanyXmlFromImportOrigin,
        UpdateCompanyXmlFromImportTarget,
        ImportNewCompanies,
        ImportDeferredAddressesAsync,
        UpdateExistingCompaniesFromOrigin,
        UpdateExistingCompaniesFromTarget,
        UpdateAdresyForChangedCompaniesFromTarget
    }
}
