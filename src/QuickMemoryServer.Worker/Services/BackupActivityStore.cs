using System.Collections.Concurrent;

namespace QuickMemoryServer.Worker.Services;

public enum BackupActivityStatus
{
    Success,
    Failure,
    Skipped
}

public sealed record BackupActivity(
    DateTime TimestampUtc,
    string Endpoint,
    BackupMode Mode,
    BackupActivityStatus Status,
    string Message,
    double? DurationMs,
    string? InitiatedBy,
    string? InstanceId);

public sealed class BackupActivityStore
{
    private readonly ConcurrentQueue<BackupActivity> _events = new();
    private readonly int _capacity;

    public BackupActivityStore(int capacity = 200)
    {
        _capacity = capacity;
    }

    public void Record(BackupActivity activity)
    {
        _events.Enqueue(activity);
        while (_events.Count > _capacity && _events.TryDequeue(out _))
        {
        }
    }

    public IReadOnlyList<BackupActivity> Latest(int take = 50, DateTime? after = null)
    {
        var snapshot = _events.ToArray();
        var filtered = after is null
            ? snapshot
            : snapshot.Where(e => e.TimestampUtc > after.Value).ToArray();

        return filtered
            .OrderByDescending(e => e.TimestampUtc)
            .Take(take)
            .ToArray();
    }
}
