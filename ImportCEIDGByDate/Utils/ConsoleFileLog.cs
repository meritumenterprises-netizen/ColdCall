using System.Text;

namespace ImportCEIDGByDate.Utils;

public sealed class ConsoleFileLog : IDisposable
{
    private readonly TextWriter originalOut = Console.Out;
    private readonly TextWriter originalError = Console.Error;
    private readonly StreamWriter file;
    private readonly object gate = new();
    private bool disposed;

    public string FilePath { get; }

    public ConsoleFileLog(string directory)
    {
        Directory.CreateDirectory(directory);
        FilePath = Path.GetFullPath(Path.Combine(directory,
            $"import-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Environment.ProcessId}.log"));
        file = new StreamWriter(new FileStream(FilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read),
            new UTF8Encoding(false)) { AutoFlush = true };
        Console.SetOut(new TeeWriter(originalOut, file, gate));
        Console.SetError(new TeeWriter(originalError, file, gate));
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            file.Dispose();
            disposed = true;
        }
    }

    private sealed class TeeWriter(TextWriter console, TextWriter log, object gate) : TextWriter
    {
        public override Encoding Encoding => console.Encoding;

        public override void Write(char value)
        {
            lock (gate) { console.Write(value); log.Write(value); }
        }

        public override void Write(string? value)
        {
            lock (gate) { console.Write(value); log.Write(value); }
        }

        public override void Write(char[] buffer, int index, int count)
        {
            lock (gate) { console.Write(buffer, index, count); log.Write(buffer, index, count); }
        }

        public override void Write(ReadOnlySpan<char> buffer)
        {
            lock (gate) { console.Write(buffer); log.Write(buffer); }
        }

        public override void WriteLine(string? value)
        {
            lock (gate) { console.WriteLine(value); log.WriteLine(value); }
        }

        public override void WriteLine(ReadOnlySpan<char> buffer)
        {
            lock (gate) { console.WriteLine(buffer); log.WriteLine(buffer); }
        }

        public override void WriteLine()
        {
            lock (gate) { console.WriteLine(); log.WriteLine(); }
        }

        public override void Flush()
        {
            lock (gate) { console.Flush(); log.Flush(); }
        }
    }
}
