using QuickMemoryServer.Worker.Memory;
using QuickMemoryServer.Worker.Models;

namespace QuickMemoryServer.Worker.Services;

internal static class McpHelpers
{
    public static Dictionary<string, MemoryEntry> BuildEntryLookup(IEnumerable<MemoryStore> stores)
    {
        var map = new Dictionary<string, MemoryEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var store in stores)
        {
            foreach (var entry in store.Snapshot())
            {
                map[entry.Id] = entry;
            }
        }

        return map;
    }

    public static MemoryEntry? ResolveEntry(string id, IEnumerable<MemoryStore> preferredStores, MemoryRouter router)
    {
        foreach (var store in preferredStores)
        {
            var entry = store.FindEntry(id);
            if (entry is not null)
            {
                return entry;
            }
        }

        var project = id.Contains(':') ? id.Split(':', 2)[0] : null;
        if (!string.IsNullOrWhiteSpace(project))
        {
            if (router.TryResolveStore(project, out var projectStore))
            {
                return projectStore.FindEntry(id);
            }
        }

        return null;
    }
}
