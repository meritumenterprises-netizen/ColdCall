namespace ImportCEIDGByDate;

public sealed class AddressImportProgress
{
    private readonly string label;
    private readonly TextWriter output;
    public int Total { get; }
    public int Processed { get; private set; }
    public int Saved { get; private set; }
    public int Skipped => Processed - Saved;
    public double Percent => Total == 0 ? 100 : 100d * Processed / Total;

    public AddressImportProgress(string label, int total, TextWriter? output = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(total);
        this.label = label;
        this.output = output ?? Console.Out;
        Total = total;
        Write();
    }

    public void Complete(bool addressSaved)
    {
        if (Processed >= Total) throw new InvalidOperationException("Address progress exceeds total.");
        Processed++;
        if (addressSaved) Saved++;
        if (Processed % 100 == 0 || Processed == Total) Write();
    }

    private void Write() => output.WriteLine(
        $"{label}: {Percent:F2}% ({Processed}/{Total}); zapisano adresow={Saved}; pominieto={Skipped}.");
}
