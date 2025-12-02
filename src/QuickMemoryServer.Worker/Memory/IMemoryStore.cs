using System.IO;
using QuickMemoryServer.Worker.Models;

namespace QuickMemoryServer.Worker.Memory;

public interface IMemoryStore
{
    string Name { get; }

    string Project { get; }

    ValueTask InitializeAsync(CancellationToken cancellationToken);

    IReadOnlyCollection<MemoryEntry> Snapshot();

    ValueTask PersistAsync(CancellationToken cancellationToken);

    MemoryEntry? FindEntry(string id);

    IEnumerable<string> Related(string id, int maxHops);

    ValueTask UpsertAsync(MemoryEntry entry, CancellationToken cancellationToken);

    ValueTask<bool> DeleteAsync(string id, bool force, CancellationToken cancellationToken);

    string StoragePath { get; }

    FileInfo? EntriesFileInfo { get; }
}
