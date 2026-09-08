using Xunit;

namespace Winnow.Covers.Tests;

/// <summary>
/// Who owns decoded pixels, and when they are freed. A decoded layer is native
/// memory behind a finalizer, so before TASK-152.2 an evicted cover's pixels
/// came back only when a gen-2 collection ran; now the decoded-memory LRU takes
/// the first hold and eviction releases it. These tests pin the hold arithmetic
/// that decides the difference between "freed at once" and "freed while a tile
/// is still drawing it".
///
/// <para>The payload has null layers: nothing here looks inside the art, and
/// constructing a real Avalonia bitmap would need the rendering platform this
/// project deliberately does not start.</para>
/// </summary>
public class CoverArtOwnershipTests
{
    private static CoverArt Art(Action<Action> post) => new(null!, null, post);

    [Fact]
    public void The_last_hold_going_frees_the_layers_exactly_once()
    {
        var frees = 0;
        var art = Art(_ => frees++);

        Assert.Equal(1, art.Holds);

        // The LRU's own hold. Releasing it with nothing else holding frees.
        art.ReleaseHold();

        Assert.Equal(0, art.Holds);
        Assert.Equal(1, frees);

        // A second release is a no-op, not a second free.
        art.ReleaseHold();
        Assert.Equal(1, frees);
    }

    /// <summary>
    /// Eviction while a surface is drawing the art. The LRU lets go, the lease
    /// does not, and the pixels stay valid until the lease is disposed — which
    /// is the whole reason a smaller cache budget does not blank the wall.
    /// </summary>
    [Fact]
    public void A_second_hold_outlives_the_cache_and_frees_when_it_goes()
    {
        var frees = 0;
        var art = Art(_ => frees++);

        Assert.True(art.TryHold());
        Assert.Equal(2, art.Holds);

        art.ReleaseHold();

        Assert.Equal(1, art.Holds);
        Assert.Equal(0, frees);

        art.ReleaseHold();

        Assert.Equal(0, art.Holds);
        Assert.Equal(1, frees);
    }

    /// <summary>
    /// The race the hold count exists for: the cache hands art over, evicts it
    /// before the consumer holds it, and the consumer must be told to ask again
    /// rather than draw pixels that are on their way out.
    /// </summary>
    [Fact]
    public void Art_whose_holds_are_gone_refuses_a_new_hold()
    {
        var art = Art(_ => { });
        art.ReleaseHold();

        Assert.False(art.TryHold());
        Assert.Equal(0, art.Holds);
    }

    [Fact]
    public void Layers_report_what_was_decoded_and_what_it_can_answer()
    {
        var vividOnly = new CoverArt(null!, null);

        Assert.Equal(CoverLayers.Vivid, vividOnly.Layers);
        Assert.True(vividOnly.Satisfies(CoverLayers.Vivid));

        // A vivid-only decode cannot answer a two-layer request: the surface
        // that asked draws the floor variant under a translucent vivid layer.
        Assert.False(vividOnly.Satisfies(CoverLayers.VividAndFloor));
    }
}
