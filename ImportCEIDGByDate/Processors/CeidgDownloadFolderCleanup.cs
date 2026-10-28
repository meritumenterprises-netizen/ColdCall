using ImportCEIDGByDate.Data;
using ImportCEIDGByDate.Data.FirmyTemp;
using ImportCEIDGByDate.Repositories;
using System;
using System.Collections.Generic;
using System.Text;
using System.Globalization;

namespace ImportCEIDGByDate.Processors
{
    public class CeidgDownloadFolderCleanup
    {
        public Statistics Statistics { get; set; }

        public CeidgDownloadFolderCleanup(Statistics statistics)
        {
            this.Statistics = statistics;
        }

        public void CleanUp()
        {
            Statistics.StartNewPhase(ImportPhasesEnum.DownloadFolderCleanup, "");

            Console.Write($"Cleaning up CEIDG download folders... ");

            var outputFolder = Directory.GetParent(Path.GetFullPath(Statistics.NewFilesFolder))!.FullName;
            var oldDate = DateOnly.FromDateTime(Statistics.OldDate);
            var newDate = DateOnly.FromDateTime(Statistics.NewDate);
            var outputFolders = Directory.EnumerateDirectories(outputFolder)
                .Where(folder => DateOnly.TryParseExact(Path.GetFileName(folder), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    && date != oldDate && date != newDate)
                .OrderBy(folder => folder, StringComparer.Ordinal)
                .ToList();

            Console.WriteLine($"Found {outputFolders.Count} output folders to delete");

            foreach (var folder in outputFolders)
            {
                Console.WriteLine($"Deleting folder {folder}");

                try
                {
                    Directory.Delete(folder, true);
                }
                catch
                {

                }
            }

            Console.WriteLine("Done.");
            Statistics.FinishCurrentPhase("");
        }
    }
}
