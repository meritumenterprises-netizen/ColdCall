using ImportCEIDGByDate.Utils;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace ImportCEIDGByDate
{
    public class StatisticsPhase
    {
        public int Level { get; set; }
        public List<StatisticsPhase> Children { get; set; } = new List<StatisticsPhase>();
        public string PhaseName { get; set; }
        public string Description { get; set; }
        public ImportPhasesEnum Phase { get; set; }
        public DateTime Start { get; set; }
        public DateTime? End { get; set; }
        public TimeSpan Duration
        {
            get
            {
                return End.HasValue ? End.Value.Subtract(Start) : DateTime.Now.Subtract(Start);
            }
        }
        public string FinishResult { get; set; }
        public int EntriesCount { get; set; }

        public bool PhaseSkipped { get; set; }

        public bool IsCriticalError { get; set; } 
        public string CriticalErrorMessage { get; set; }

        public string Summary()
        {
            StringBuilder sb = new StringBuilder();
            if (this.Children.Count == 0)
            {
                sb.Append($"Phase: {this.PhaseName}, Start: {this.Start}, End: {this.End.Value}, Duration: {(this.Duration).ToString(@"hh\:mm\:ss\.ffff")}, Entries count: {this.EntriesCount} ");

                if (this.EntriesCount > 0)
                {
                    double entriesPerSecond = (double)this.EntriesCount / Duration.TotalSeconds;
                    sb.Append($", Rate {entriesPerSecond:F2} entries/sec");
                }

                if (!string.IsNullOrEmpty(this.FinishResult))
                {
                    sb.Append($", Finish result: {this.FinishResult}");
                }
                 sb.AppendLine();

            }
            else
            {
                sb.AppendLine($"Phase: {this.PhaseName}, Start: {this.Start}");
                foreach (var subPhase in this.Children)
                {
                    if (!subPhase.End.HasValue)
                    {
                        subPhase.End = this.End.Value;
                    }
                    sb.Append($"\tPhase: {subPhase.PhaseName}, Start: {subPhase.Start}, End: {subPhase.End.Value}, Duration: {subPhase.Duration.AsString()}");
                    sb.Append($", Entries count: {subPhase.EntriesCount}, Entries/sec: {subPhase.EntriesRatePerSecond}");
                    sb.AppendLine();

                }
                sb.Append($"{this.PhaseName} End: {this.End.Value}, Duration: {(this.Duration).AsString()}");
                if (this.EntriesCount > 0)
                {
                    sb.Append($", Entries count: {this.EntriesCount}, Entries/sec: {EntriesRatePerSecond}");
                }
                if (!string.IsNullOrEmpty(this.FinishResult))
                {
                    sb.Append($", Finish result: {this.FinishResult}");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }
        public string EntriesRatePerSecond
        {
            get
            {
                if (End.HasValue)
                {
                    var seconds = (End.Value - Start).TotalSeconds;
                    if (seconds > 0)
                    {
                        return $"{(double)EntriesCount / seconds:F2}";
                    }
                }
                return $"{0:F2}";
            }
        }
    }
}
