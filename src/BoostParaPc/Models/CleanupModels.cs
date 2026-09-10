namespace BoostParaPc.Models;

public sealed record CleanupItem(
    string Name,
    string Path,
    long SizeBytes,
    bool Selected = true);

public sealed record CleanupResult(
    long BytesFreed,
    int FilesRemoved,
    int Errors,
    IReadOnlyList<string> Details);
