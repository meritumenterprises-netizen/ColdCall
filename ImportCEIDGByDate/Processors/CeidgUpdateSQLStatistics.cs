using ImportCEIDGByDate.Data;
using ImportCEIDGByDate.Utils;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
                Stopwatch sw = Stopwatch.StartNew();
                Console.WriteLine($"Updating SQL statistics for ColdCall.Companies");
                Statistics.StartNewPhase(ImportPhasesEnum.UpdateSQLStatistics, "Updating SQL statistics for ColdCall.Companies");
                var previousTimeout = FirmyTempDbContext.Database.GetCommandTimeout();
                try
                {
                    FirmyTempDbContext.Database.SetCommandTimeout(TimeSpan.FromHours(1));
                    await FirmyTempDbContext.Database.ExecuteSqlRawAsync("UPDATE STATISTICS [ColdCall].[Companies] WITH FULLSCAN;");
                    await FirmyTempDbContext.Database.ExecuteSqlRawAsync("UPDATE STATISTICS ColdCall.CompanyNameGrams\r\n    IX_CompanyNameGrams_Gram_CompanyId\r\nWITH RESAMPLE;");
                }
                finally
                {
                    FirmyTempDbContext.Database.SetCommandTimeout(previousTimeout);
                }
                Statistics.FinishCurrentPhase("");
                sw.Stop();
                Console.WriteLine($"Finished updating SQL statistics for ColdCall.Companies, done in {sw.Elapsed.AsString()}");
            }
        }
    }
}
