namespace MarketMakerSPBStandalone.Runtime;

/// <summary>
/// Exclusive process-level lease for a strategy state path. A stale .lock file is fine:
/// OS-level FileShare.None lock is released on process exit/crash.
/// </summary>
public sealed class InstanceLease : IDisposable
{
    private readonly FileStream _lockFile;
    public string Path { get; }

    public InstanceLease(string statePath)
    {
        if (string.IsNullOrWhiteSpace(statePath))
            throw new ArgumentException("State path required.", nameof(statePath));
        var full = System.IO.Path.GetFullPath(statePath) + ".lock";
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        Path = full;
        try
        {
            _lockFile = new FileStream(full, FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None, 64,
                FileOptions.WriteThrough);
        }
        catch (IOException error)
        {
            throw new IOException("Another Market Maker instance already holds the state-file lock: " + full, error);
        }
    }

    public void Dispose() => _lockFile.Dispose();
}
