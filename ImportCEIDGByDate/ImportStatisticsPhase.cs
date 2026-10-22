namespace ImportCEIDGByDate
{
    public class ImportStatisticsPhase
    {
        public string PhaseName { get; set; }
        public string Description { get; set; }
        public ImportPhasesEnum Phase { get; set; }
        public bool IsCurrentPhase { get; set; }
        public DateTime Start { get; set; }
        public DateTime? End { get; set; }
        public string FinishResult { get; set; }
        public int EntriesCount { get; set; }

        public bool PhaseSkipped { get; set; }

        public bool IsCriticalError { get; set; } 
        public string CriticalErrorMessage { get; set; }

        public double EntriesRatePerSecond
        {
            get
            {
                if (End.HasValue)
                {
                    var seconds = (End.Value - Start).TotalSeconds;
                    if (seconds > 0)
                    {
                        return EntriesCount / seconds;
                    }
                }
                return 0;
            }
        }

        public double EntriesRatePerSecondCurrent
        {
            get
            {
                if (End.HasValue == false)
                {
                    var seconds = (DateTime.Now - Start).TotalSeconds;
                    if (seconds > 0)
                    {
                        return EntriesCount / seconds;
                    }
                }
                else
                {
                    return this.EntriesRatePerSecond;
                }
                return 0;
            }
        }

        public string Summary
        {
            get
            {
                if (IsCriticalError)
                {
                    return $"\tPhase: {PhaseName}, Description: {Description}, Start: {Start}, End: {(End.HasValue ? End.Value.ToString() : "In Progress")}, Entries Count: {EntriesCount}, Entries Rate Per Second: {EntriesRatePerSecondCurrent:F2}, IsCriticalError={IsCriticalError}, Critical Error Message={CriticalErrorMessage}";
                }
                else
                {
                    return $"\tPhase: {PhaseName}, Description: {Description}, Start: {Start}, End: {(End.HasValue ? End.Value.ToString() : "In Progress")}, Entries Count: {EntriesCount}, Entries Rate Per Second: {EntriesRatePerSecondCurrent:F2}, Skipped={PhaseSkipped}";
                }
            }
        }
    }
}
