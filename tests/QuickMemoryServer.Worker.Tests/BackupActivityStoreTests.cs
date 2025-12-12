using System;
using System.Linq;
using QuickMemoryServer.Worker.Services;
using Xunit;

namespace QuickMemoryServer.Worker.Tests;

public class BackupActivityStoreTests
{
    [Fact]
    public void Latest_Returns_Newest_First_And_Respects_Take()
    {
        var store = new BackupActivityStore(capacity: 10);
        for (var i = 0; i < 5; i++)
        {
            store.Record(new BackupActivity(
                DateTime.UtcNow.AddMinutes(i),
                "endpoint",
                BackupMode.Differential,
                BackupActivityStatus.Success,
                $"msg-{i}",
                10,
                null,
                "inst"));
        }

        var latest = store.Latest(take: 3);
        Assert.Equal(3, latest.Count);
        Assert.True(latest[0].TimestampUtc > latest[1].TimestampUtc);
        Assert.Equal("msg-4", latest[0].Message);
        Assert.Equal("msg-3", latest[1].Message);
    }

    [Fact]
    public void Latest_Filters_By_After()
    {
        var store = new BackupActivityStore(capacity: 10);
        var t0 = DateTime.UtcNow;
        store.Record(new BackupActivity(t0.AddSeconds(-10), "a", BackupMode.Differential, BackupActivityStatus.Success, "old", 1, null, "i"));
        store.Record(new BackupActivity(t0.AddSeconds(5), "a", BackupMode.Differential, BackupActivityStatus.Success, "new", 1, null, "i"));

        var latest = store.Latest(take: 5, after: t0);
        Assert.Single(latest);
        Assert.Equal("new", latest[0].Message);
    }

    [Fact]
    public void Capacity_Drops_Oldest()
    {
        var store = new BackupActivityStore(capacity: 2);
        store.Record(new BackupActivity(DateTime.UtcNow.AddSeconds(-2), "a", BackupMode.Differential, BackupActivityStatus.Success, "first", 1, null, "i"));
        store.Record(new BackupActivity(DateTime.UtcNow.AddSeconds(-1), "a", BackupMode.Differential, BackupActivityStatus.Success, "second", 1, null, "i"));
        store.Record(new BackupActivity(DateTime.UtcNow, "a", BackupMode.Differential, BackupActivityStatus.Success, "third", 1, null, "i"));

        var latest = store.Latest(take: 5);
        Assert.Equal(2, latest.Count);
        Assert.Equal("third", latest[0].Message);
        Assert.Equal("second", latest[1].Message);
        Assert.DoesNotContain(latest, x => x.Message == "first");
    }
}
