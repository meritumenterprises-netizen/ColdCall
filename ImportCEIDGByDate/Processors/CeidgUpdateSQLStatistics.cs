using ImportCEIDGByDate.Data;
using ImportCEIDGByDate.Utils;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace ImportCEIDGByDate.Processors
{
    public class CeidgUpdateSQLStatistics
    {
        public Statistics Statistics { get; set; }
        public GlobalSettings GlobalSettings { get; set; }
        public FirmyTempDbContext FirmyTempDbContext { get; set; }

        public CeidgUpdateSQLStatistics (Statistics statistics, GlobalSettings globalSettings, FirmyTempDbContext firmyTempDbContext)
        {
            Statistics = statistics;
            GlobalSettings = globalSettings;
            FirmyTempDbContext = firmyTempDbContext;
        }

        public async Task UpdateSQLStatistics()
        {
            if (GlobalSettings.UpdateSQLStatistics)
            {
                Statistics.StartNewPhase(ImportPhasesEnum.UpdateSQLStatistics, "Updating SQL statistics for ColdCall.Companies");
                await FirmyTempDbContext.Database.ExecuteSqlRawAsync("UPDATE STATISTICS [ColdCall].[Companies] WITH FULLSCAN;");
                Statistics.FinishCurrentPhase("");
            }
        }
    }
}
