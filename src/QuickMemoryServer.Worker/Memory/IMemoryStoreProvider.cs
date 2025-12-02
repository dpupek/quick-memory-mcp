using System.Collections.Generic;

namespace QuickMemoryServer.Worker.Memory;

public interface IMemoryStoreProvider
{
    IReadOnlyDictionary<string, IMemoryStore> Stores { get; }
}
