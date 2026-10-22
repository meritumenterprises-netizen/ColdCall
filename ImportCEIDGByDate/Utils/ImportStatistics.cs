using System;
using System.Collections.Generic;
using System.Text;

namespace ImportCEIDGByDate
{

    public class ImportStatistics
    {
        public DateTime ProcessStart { get; set; }
        public DateTime? ProcessEnd { get; set; }

        public string FinishMessage { get; set; }

        public string Summary
        {
            get
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"Import statistics: {Description}");
                sb.AppendLine($"Process started at: {ProcessStart}");
                if (ProcessEnd.HasValue)
                {
                    sb.AppendLine($"Process ended at: {ProcessEnd.Value}");
                }
                else
                {
                    sb.AppendLine($"Process is still running.");
                }
                sb.AppendLine($"Duration: {Duration:hh\\:mm\\:ss}");
                sb.AppendLine($"New date: {NewDate}");
                sb.AppendLine($"Old date: {OldDate}");
                sb.AppendLine($"Old files folder: {OldFilesFolder}");
                sb.AppendLine($"New files folder: {NewFilesFolder}");
                sb.AppendLine("Phases:");
                foreach (var phase in Phases)
                {
                    sb.AppendLine($"\tPhase: {phase.PhaseName}, Start: {phase.Start}, End: {(phase.End.HasValue ? phase.End.Value.ToString() : "Still running")}, Duration: {(phase.End.Value - phase.Start).ToString("mm:ss.fff")}, Entries count: {phase.EntriesCount}, Finish result: {phase.FinishResult}");
                }
                sb.AppendLine();
                sb.AppendLine($"==== SUMMARY ====");
                sb.AppendLine($"Duration: {this.ProcessEnd.Value.Subtract(ProcessStart).ToString("hh:mm:ss")}");
                return sb.ToString();
            }
        }

        public string Description { get; set; }

        public DateTime NewDate { get; set; }
        public DateTime OldDate { get; set; }

        public Dictionary<string, List<CeidgReport>> OldReports { get; set; } = new Dictionary<string, List<CeidgReport>>();
        public Dictionary<string, List<CeidgReport>> NewReports { get; set; } = new Dictionary<string, List<CeidgReport>>();

        public string OldFilesFolder { get; set; }
        public List<string> OldFiles { get; set; } = new List<string>(); 
        public string NewFilesFolder { get; set; }
        public string ReportFolder { get; set; }
        public List<string> NewFiles { get; set; } = new List<string>();

        public List<ImportStatisticsPhase> Phases { get; set; } = new List<ImportStatisticsPhase>();

        public ImportStatisticsPhase CurrentPhase
        {
            get
            {
                if (Phases.Any())
                {
                    return Phases.SingleOrDefault((p) => p.IsCurrentPhase);
                }
                return null;
            }
        }

        public void Init(string description, DateTime newDate, DateTime oldDate, string oldFilesFolder, string newFilesFolder)
        {
            this.Description = description;
            this.ProcessStart = DateTime.Now;
            this.ProcessEnd = null;
            this.NewDate = newDate;
            this.OldDate = oldDate;
            this.OldFilesFolder = oldFilesFolder;
            this.NewFilesFolder = newFilesFolder;
            this.Phases.Clear();
            this.StartNewPhase(ImportPhasesEnum.Initialization, "Initialization of program data");
        }

        public TimeSpan Duration
        {
            get
            {
                if (ProcessEnd.HasValue)
                {
                    return ProcessEnd.Value - ProcessStart;
                }
                else
                {
                    return DateTime.Now - ProcessStart;
                }
            }
        }

        public ImportStatisticsPhase StartNewPhase ( ImportPhasesEnum phase, string description)
        {
            //if (this.Phases.FirstOrDefault((p) => p.Phase == phase) != null)
            //{
            //    throw new InvalidOperationException($"Phase {phase} already exists.");
            //}

            if (CurrentPhase != null)
            {
                CurrentPhase.End = DateTime.Now;
                CurrentPhase.IsCurrentPhase = false;
            }
            var newPhase = new ImportStatisticsPhase()
            {
                PhaseName = phase.ToString(),
                Description = description,
                Phase = phase,
                IsCurrentPhase = true,
                Start = DateTime.Now,
                End = null,
                FinishResult = "",
                EntriesCount = 0
            };
            Phases.Add(newPhase);
            return newPhase;
        }

        public void FinishCurrentPhase(string comment, int? entriesCount = null)
        {
            if (CurrentPhase == null)
            {
                throw new InvalidOperationException("No current phase to finish.");
            }
            var currentPhase = CurrentPhase;
            currentPhase.End = DateTime.Now;
            currentPhase.IsCurrentPhase = false;
            currentPhase.FinishResult = comment;
            currentPhase.EntriesCount = entriesCount.HasValue ? entriesCount.Value: 0;
        }

        public void Finish(string comment)
        {
            this.Phases.Last().End = DateTime.Now;
            this.ProcessEnd = DateTime.Now;
            this.FinishMessage = comment;
        }
    }
}
