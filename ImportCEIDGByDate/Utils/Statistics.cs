using ImportCEIDGByDate.Utils;
using System;
using System.Collections.Generic;
using System.Text;

namespace ImportCEIDGByDate
{

    public class Statistics
    {
        public DateTime ProcessStart { get; set; }
        public DateTime? ProcessEnd { get; set; }

        public string FinishMessage { get; set; }

        public string Summary
        {
            get
            {
                if (ProcessEnd.HasValue == false)
                {
                    ProcessEnd = DateTime.Now;
                }
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"Import statistics: {Description}");
                sb.AppendLine($"New date: {NewDate}");
                //sb.AppendLine($"Old date: {OldDate}");
                //sb.AppendLine($"Old files folder: {OldFilesFolder}");
                //foreach (var file in OldFiles)
                //{
                //    sb.AppendLine($"\t{Path.GetFileName(file)}");
                //}
                sb.AppendLine($"New files folder: {NewFilesFolder}");
                foreach (var file in NewFiles)
                {
                    sb.AppendLine($"\t{Path.GetFileName(file)}");
                }
                sb.AppendLine("Phases:");
                foreach (var phase in Phases)
                {
                    sb.AppendLine(phase.Summary());
                }
                sb.AppendLine();
                sb.AppendLine($"==== SUMMARY ====");
                this.ProcessEnd = DateTime.Now;
                sb.AppendLine($"Process started at: {ProcessStart}");
                if (ProcessEnd.HasValue)
                {
                    sb.AppendLine($"Process ended at: {ProcessEnd.Value}");
                }
                else
                {
                    sb.AppendLine($"Process is still running.");
                }
                sb.AppendLine($"Duration: {this.ProcessEnd.Value.Subtract(ProcessStart).AsString()}");
                return sb.ToString();
            }
        }

        public string Description { get; set; }

        public DateTime NewDate { get; set; }
        public DateTime OldDate { get; set; }

        public Dictionary<string, List<CeidgReport>> OldReports { get; set; } = new Dictionary<string, List<CeidgReport>>();
        public Dictionary<string, List<CeidgReport>> NewReports { get; set; } = new Dictionary<string, List<CeidgReport>>();

        public string NewFilesFolder { get; set; }

        public int NewFilesCount
        {
            get
            {
                return this.NewReports.Values.Sum(reports => reports.Count);
            }
        }


        public string ReportFolder { get; set; }
        
        public List<string> NewFiles { get; set; } = new List<string>();

        public List<StatisticsPhase> Phases { get; set; } = new List<StatisticsPhase>();

        public StatisticsPhase CurrentPhase
        {
            get
            {
                var parent = Phases.LastOrDefault();
                if (parent?.Children.Count > 0)
                {
                    return parent.Children.LastOrDefault();
                }
                return parent;
            }
        }

        public void Init(string description, DateTime newDate, string newFilesFolder)
        {
            this.Description = description;
            this.ProcessStart = DateTime.Now;
            this.ProcessEnd = null;
            this.NewDate = newDate;
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

        public StatisticsPhase StartNewPhase(ImportPhasesEnum phase, string description)
        {
            if (CurrentPhase != null)
            {
                var parent = Phases.Last();
                FinishCurrentPhase(parent.FinishResult, parent.EntriesCount);
            }
            var newPhase = new StatisticsPhase()
            {
                PhaseName = phase.ToString(),
                Description = description,
                Phase = phase,
                Start = DateTime.Now,
                End = null,
                FinishResult = "",
                EntriesCount = 0
            };
            Phases.Add(newPhase);
            return newPhase;
        }

        public StatisticsPhase StartNewChildPhase(ImportPhasesEnum phase, string description)
        {
            var parent = Phases.LastOrDefault();
            var child = new StatisticsPhase
            {
                Level = parent.Level + 1,
                PhaseName = phase.ToString(),
                Description = description,
                Phase = phase,
                Start = DateTime.Now,
                FinishResult = "",
                EntriesCount = 0
            };
            parent.Children.Add(child);
            return child;
        }

        public void FinishChildPhase(string comment, int? entriesCount = null)
        {
            var parent = Phases.LastOrDefault();
            var child = parent.Children.LastOrDefault();
            child.FinishResult = comment;
            child.End = DateTime.Now;
            child.EntriesCount = entriesCount ?? child.EntriesCount;
        }

        public void FinishCurrentPhase(string comment, int? entriesCount = null)
        {
            if (CurrentPhase == null)
            {
                throw new InvalidOperationException("No current phase to finish.");
            }
            var currentPhase = Phases.Last();
            var currentChild = currentPhase.Children.LastOrDefault();
            if (currentChild != null)
                FinishChildPhase(currentChild.FinishResult, currentChild.EntriesCount);
            currentPhase.End = DateTime.Now;
            currentPhase.FinishResult = comment;
            currentPhase.EntriesCount = entriesCount.HasValue ? entriesCount.Value : 0;
        }

        public void Finish(string comment)
        {
            if (CurrentPhase != null)
                FinishCurrentPhase(comment, Phases.Last().EntriesCount);
            this.Phases.Last().End = DateTime.Now;
            this.ProcessEnd = DateTime.Now;
            this.FinishMessage = comment;
        }

        public int ImportOriginRecordCount { get; set; }
        public int ImportTargetRecordCount { get; set; }
    }
}
