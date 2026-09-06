using Winnow.Core.Queries;
using Xunit;

namespace Winnow.Tests;

public sealed class BucketThresholdValidationTests
{
    [Theory]
    [InlineData(0, 6000, 6, 7)]
    [InlineData(-1, 6000, 6, 7)]
    [InlineData(120, 0, 6, 7)]
    [InlineData(120, -1, 6, 7)]
    [InlineData(120, 120, 6, 7)]
    [InlineData(121, 120, 6, 7)]
    [InlineData(120, 6000, 0, 7)]
    [InlineData(120, 6000, -1, 7)]
    [InlineData(120, 6000, 6, 0)]
    [InlineData(120, 6000, 6, -1)]
    public void Invalid_thresholds_are_rejected(long bounced, long retired, int months, int days)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new BucketThresholds(bounced, retired, months, days));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Record_copies_cannot_bypass_invariants(int change)
    {
        var original = BucketThresholds.Default;
        Assert.Throws<ArgumentOutOfRangeException>(() => change switch
        {
            0 => original with { BouncedFloorMinutes = 0 },
            1 => original with { RetiredFloorMinutes = -1 },
            2 => original with { BouncedFloorMinutes = original.RetiredFloorMinutes },
            3 => original with { RetiredFloorMinutes = original.BouncedFloorMinutes },
            4 => original with { StaleWindowMonths = 0 },
            _ => original with { UpdateCorrelationWindowDays = -1 },
        });
        Assert.Equal(120, original.BouncedFloorMinutes);
        Assert.Equal(6000, original.RetiredFloorMinutes);
    }

    [Fact]
    public void Smallest_valid_ranges_keep_distinct_buckets()
    {
        var thresholds = new BucketThresholds(1, 2, 1, 1);
        Assert.Equal(LibraryBuckets.NeverPlayed, LibraryBucketRules.Classify(0, null, null, thresholds));
        Assert.Equal(LibraryBuckets.Bounced, LibraryBucketRules.Classify(1, null, null, thresholds));
        Assert.Equal(LibraryBuckets.Retired, LibraryBucketRules.Classify(2, null, null, thresholds));
        Assert.Equal(3, (thresholds with { RetiredFloorMinutes = 3 }).RetiredFloorMinutes);
    }
}
