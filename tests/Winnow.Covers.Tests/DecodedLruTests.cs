using Xunit;

namespace Winnow.Covers.Tests;

/// <summary>
/// <c>CoverCache.MaxDecodedBytes</c> is the only thing standing between a
/// 3,000-tile grid at 2x DPI and unbounded native memory, and until
/// <c>DecodedLru</c> was split out of <c>CoverCache</c> nothing could reach it:
/// admitting an entry meant constructing an Avalonia <c>WriteableBitmap</c>, and
/// these tests deliberately start no rendering platform.
///
/// <para>The payload here is a plain object rather than a <c>CoverArt</c> for
/// the same reason. What the eviction rule does is arithmetic over declared
/// costs; it never looks inside the value.</para>
/// </summary>
public class DecodedLruTests
{
    private sealed record Payload(string Name);

    private static DecodedLru<string, Payload> Lru(long maxBytes) => new(maxBytes);

    [Fact]
    public void An_admitted_entry_is_a_hit_and_counts_its_bytes()
    {
        var lru = Lru(1000);
        lru.Admit("a", new Payload("a"), 300);

        Assert.True(lru.TryGet("a", out var value));
        Assert.Equal("a", value.Name);
        Assert.Equal(300, lru.Bytes);
        Assert.Equal(1, lru.Count);

        lru.Clear();
    }

    [Fact]
    public void A_miss_changes_nothing()
    {
        var lru = Lru(1000);

        Assert.False(lru.TryGet("nobody", out _));
        Assert.Equal(0, lru.Bytes);
        Assert.Equal(0, lru.Count);
    }

    // ── The bound ────────────────────────────────────────────────────────────

    [Fact]
    public void Admitting_past_the_budget_evicts_until_it_holds()
    {
        var lru = Lru(1000);
        lru.Admit("a", new Payload("a"), 400);
        lru.Admit("b", new Payload("b"), 400);
        Assert.Equal(800, lru.Bytes);

        lru.Admit("c", new Payload("c"), 400);

        // 1200 would exceed 1000, so the oldest goes and the bound holds.
        Assert.Equal(800, lru.Bytes);
        Assert.Equal(2, lru.Count);
        Assert.False(lru.TryGet("a", out _));
        Assert.True(lru.TryGet("b", out _));
        Assert.True(lru.TryGet("c", out _));

        lru.Clear();
    }

    [Fact]
    public void Eviction_takes_as_many_as_the_budget_needs()
    {
        var lru = Lru(1000);
        for (var i = 0; i < 5; i++)
        {
            lru.Admit(i.ToString(), new Payload(i.ToString()), 200);
        }

        Assert.Equal(1000, lru.Bytes);

        // One 900-byte admission cannot coexist with more than one 200.
        lru.Admit("big", new Payload("big"), 900);

        Assert.True(lru.Bytes <= 1000);
        Assert.True(lru.TryGet("big", out _));
        Assert.False(lru.TryGet("0", out _));

        lru.Clear();
    }

    /// <summary>
    /// Least-RECENTLY-USED, not least-recently-admitted. A tile the user is
    /// looking at must not be evicted for one they scrolled past ten seconds
    /// ago, which is the difference between a cache and a queue.
    /// </summary>
    [Fact]
    public void A_read_promotes_an_entry_out_of_the_eviction_line()
    {
        var lru = Lru(1000);
        lru.Admit("a", new Payload("a"), 400);
        lru.Admit("b", new Payload("b"), 400);

        // Touch the oldest. Now "b" is the one at the back.
        Assert.True(lru.TryGet("a", out _));

        lru.Admit("c", new Payload("c"), 400);

        Assert.True(lru.TryGet("a", out _));
        Assert.False(lru.TryGet("b", out _));

        lru.Clear();
    }

    /// <summary>
    /// A single cover larger than the whole budget is still served. Evicting it
    /// to satisfy the bound would leave an empty cache in front of a tile that
    /// is actively asking for art — a guaranteed re-decode on every
    /// realization, forever.
    /// </summary>
    [Fact]
    public void The_last_entry_survives_even_when_it_alone_exceeds_the_budget()
    {
        var lru = Lru(100);
        lru.Admit("huge", new Payload("huge"), 5000);

        Assert.Equal(1, lru.Count);
        Assert.True(lru.TryGet("huge", out _));
        Assert.Equal(5000, lru.Bytes);

        lru.Clear();
    }

    /// <summary>
    /// Two decodes of one slot race only when the in-flight de-duplication
    /// upstream loses. The value already held is as good as the one arriving,
    /// and swapping it would invalidate whatever is already bound to it — so
    /// re-admission is a no-op, and above all must not double-count the bytes.
    /// </summary>
    [Fact]
    public void Re_admitting_a_held_key_is_a_no_op_and_does_not_double_count()
    {
        var lru = Lru(1000);
        var first = new Payload("first");
        lru.Admit("a", first, 400);
        lru.Admit("a", new Payload("second"), 400);

        Assert.Equal(400, lru.Bytes);
        Assert.Equal(1, lru.Count);
        Assert.True(lru.TryGet("a", out var held));
        Assert.Same(first, held);

        lru.Clear();
    }

    [Fact]
    public void Clear_empties_the_cache_and_hands_back_every_byte()
    {
        var lru = Lru(1000);
        lru.Admit("a", new Payload("a"), 400);
        lru.Admit("b", new Payload("b"), 400);

        lru.Clear();

        Assert.Equal(0, lru.Bytes);
        Assert.Equal(0, lru.Count);
        Assert.False(lru.TryGet("a", out _));
    }

    /// <summary>
    /// The grid reads from the UI thread during layout while decode tasks
    /// admit from the thread pool. The accounting must survive that: a torn
    /// <c>_bytes</c> is an eviction rule that stops running.
    /// </summary>
    [Fact]
    public async Task Concurrent_admissions_keep_the_accounting_consistent()
    {
        var lru = Lru(10_000);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
        {
            for (var i = 0; i < 200; i++)
            {
                var key = $"{worker}:{i}";
                lru.Admit(key, new Payload(key), 100);
                lru.TryGet(key, out _);
            }
        })));

        Assert.True(lru.Bytes <= 10_000, $"bound broken: {lru.Bytes} bytes held");
        Assert.Equal(lru.Count * 100, lru.Bytes);

        lru.Clear();
    }
}
