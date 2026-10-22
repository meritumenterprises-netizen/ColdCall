using System;
using System.Collections.Generic;
using System.Text;

namespace ImportCEIDGByDate
{
    public enum ImportPhasesEnum
    {
        Unknown = 0,
        Initialization,
        DownloadAndParseReport,
        DownloadOldFiles,
        DownloadNewFiles,
        DeduplicateOldFiles,
        DeduplicateNewFiles,
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
        ImportNewCompanies,
        UpdateExistingCompanies,
        UpdateAdresyForChangedCompanies
    }
}
