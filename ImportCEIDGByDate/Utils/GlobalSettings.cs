using System;
using System.Collections.Generic;
using System.Text;

namespace ImportCEIDGByDate.Utils
{
    public class GlobalSettings
    {
        public int GlobalDegreeOfParallelism = 4;
        public bool TruncateImportOriginAtTheBeginning { get; set; } = true;
        public bool TruncateImportTargetAtTheBeginning { get; set; } = true;
        public bool TruncateImportOriginAtTheEnd { get; set; } = true;
        public bool TruncateImportTargetAtTheEnd { get; set; } = true;

        public bool OverwriteOldFiles { get; set; } = true;
        public bool OverwriteNewFiles { get; set; } = true;

        public bool SkipDownloadingOldFilesIfExist { get; set; } = false;
        public bool SkipDownloadingNewFilesIfExist { get; set; } = false;
        public bool SkipXmlImports { get; set; } = false;

        public bool SkipDownloadOldFiles { get; set; } = false;
        public bool SkipDownloadNewFiles { get; set; } = false;
        public bool SkipUnpackOldFiles { get; set; } = false;
        public bool SkipUnpackNewFiles { get; set; } = false;

        public bool SkipImportOldFiles { get; set; } = false;
        public bool SkipImportNewFiles { get; set; } = false;
        public bool SkipUpdateImportTargetMetadata { get; set; } = false;

        public bool SkipUpdateExistingCompaniesFromOrigin { get; set; } = false;
        public bool SkipAddNewCompaniesFromImportToTemp { get; set; } = false;
        public bool SkipUpdateExistingCompaniesFromImportToTemp { get; set; } = false;
        public bool SkipImportDeferredAddressesAsync { get; set; } = false;

        public bool SkipProcessingAddresses { get; set; }

        public bool UpdateSQLStatistics { get; set; } = true;
        public bool ShrinkDatabase { get; set; } = true;
    }
}
