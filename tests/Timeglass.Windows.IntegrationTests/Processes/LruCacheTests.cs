using Timeglass.Windows.Processes;

namespace Timeglass.Windows.IntegrationTests.Processes;

public class LruCacheTests
{
    [Fact]
    public void Returns_what_was_set()
    {
        var cache = new LruCache<int, string>(2);
        cache.Set(1, "one");

        cache.TryGet(1, out var value).ShouldBeTrue();
        value.ShouldBe("one");
        cache.TryGet(2, out _).ShouldBeFalse();
    }

    [Fact]
    public void Evicts_the_least_recently_used_entry_when_full()
    {
        var cache = new LruCache<int, string>(2);
        cache.Set(1, "one");
        cache.Set(2, "two");
        cache.TryGet(1, out _); // 2 is now the oldest

        cache.Set(3, "three");

        cache.Count.ShouldBe(2);
        cache.TryGet(1, out _).ShouldBeTrue();
        cache.TryGet(2, out _).ShouldBeFalse();
        cache.TryGet(3, out _).ShouldBeTrue();
    }

    [Fact]
    public void Replacing_a_key_does_not_evict()
    {
        var cache = new LruCache<int, string>(2);
        cache.Set(1, "one");
        cache.Set(2, "two");

        cache.Set(1, "uno");

        cache.Count.ShouldBe(2);
        cache.TryGet(1, out var value).ShouldBeTrue();
        value.ShouldBe("uno");
        cache.TryGet(2, out _).ShouldBeTrue();
    }

    [Fact]
    public void Uses_the_given_comparer()
    {
        var cache = new LruCache<string, int>(2, StringComparer.OrdinalIgnoreCase);
        cache.Set(@"C:\App.exe", 1);

        cache.TryGet(@"c:\app.EXE", out var value).ShouldBeTrue();
        value.ShouldBe(1);
    }

    [Fact]
    public void Capacity_must_be_positive()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new LruCache<int, int>(0));
    }
}
