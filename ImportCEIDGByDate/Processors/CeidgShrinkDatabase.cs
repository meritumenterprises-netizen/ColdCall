using ImportCEIDGByDate.Data;
using ImportCEIDGByDate.Utils;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace ImportCEIDGByDate.Processors
{
    public class CeidgShrinkDatabase
    {
        public Statistics Statistics { get; set; }
        public GlobalSettings GlobalSettings { get; set; }
        public FirmyTempDbContext FirmyTempDbContext { get; set; }

        public CeidgShrinkDatabase(Statistics statistics, GlobalSettings globalSettings, FirmyTempDbContext firmyTempDbContext)
        {
            Statistics = statistics;
            GlobalSettings = globalSettings;
            FirmyTempDbContext = firmyTempDbContext;
        }

        public async Task ShrinkDatabase()
        {
            if (GlobalSettings.UpdateSQLStatistics)
            {
                Stopwatch sw = Stopwatch.StartNew();
                Statistics.StartNewPhase(ImportPhasesEnum.ShrinkDatabase, "Shrinking database");
                Console.WriteLine($"Shrinking database");
                var previousTimeout = FirmyTempDbContext.Database.GetCommandTimeout();
                try
                {
                    FirmyTempDbContext.Database.SetCommandTimeout(TimeSpan.FromHours(1));
                    await FirmyTempDbContext.Database.ExecuteSqlRawAsync("DBCC SHRINKFILE (N'Firmy', TRUNCATEONLY);");
                    await FirmyTempDbContext.Database.ExecuteSqlRawAsync("DBCC SHRINKFILE (N'Firmy_log', 2048);");
                }
                finally
                {
                    FirmyTempDbContext.Database.SetCommandTimeout(previousTimeout);
                }
                Statistics.FinishCurrentPhase("");
                sw.Stop();
                Console.WriteLine($"Finished shrinking database, duration {sw.Elapsed.AsString()}");
            }
        }

    }
}
