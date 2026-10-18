using ClipNest.Core.Models;
using FluentAssertions;
using Xunit;

namespace ClipNest.Core.Tests.Environment;

public class PreviewResourceTests
{
    [Theory]
    [InlineData(7680u, 4320u, 800, 800, 450)]
    [InlineData(100u, 100000u, 800, 1, 800)]
    [InlineData(100000u, 100u, 800, 800, 1)]
    [InlineData(50u, 30u, 800, 50, 30)]
    public void DecodeSize_BoundsBothDimensionsWithoutUpscaling(uint width, uint height, int limit, int expectedWidth, int expectedHeight)
        => ImagePreviewSize.Fit(width, height, limit).Should().Be((expectedWidth, expectedHeight));

    [Fact]
    public void Cache_EvictsLeastRecentlyUsedAndReplacesWithoutGrowing()
    {
        var cache = new BoundedCache<int, string>(2);
        cache.Add(1, "one"); cache.Add(2, "two");
        cache.TryGet(1, out _).Should().BeTrue();
        cache.Add(3, "three");
        cache.TryGet(2, out _).Should().BeFalse();
        cache.Add(1, "replacement");
        cache.Count.Should().Be(2);
        cache.TryGet(1, out var value).Should().BeTrue();
        value.Should().Be("replacement");
    }
}
