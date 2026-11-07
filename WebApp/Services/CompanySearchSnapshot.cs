namespace ColdCall.Services;

public sealed class CompanySearchSnapshot(int[] ids, DateTime?[] importedOn, bool hasNotes, string orderKey = "")
{
    private Tuple<string, int[]>? _lastOrder;
    public int[]? GetOrder(string key) => key == orderKey
        ? Enumerable.Range(0, Ids.Length).ToArray()
        : Volatile.Read(ref _lastOrder) is { } order && order.Item1 == key ? order.Item2 : null;

    public void SetOrder(string key, int[] indices) =>
        Volatile.Write(ref _lastOrder, Tuple.Create(key, indices));

    public int[] Ids { get; } = ids;
    public DateTime?[] ImportedOn { get; } = importedOn;
    public bool HasNotes { get; } = hasNotes;
}
