using GifMaker.Core;

namespace GifMaker.UnitTests.Core;

public sealed class ScreenRegionTests
{
    [Theory]
    [InlineData(0, 0, 1, 1)]
    [InlineData(-10, 20, 1920, 1080)]
    [InlineData(100, -50, 2, 2)]
    public void CreateAcceptsPositiveDimensions(int x, int y, int width, int height)
    {
        var result = ScreenRegion.Create(x, y, width, height);

        Assert.True(result.IsSuccess);
        var region = result.GetValueOrThrow();
        Assert.Equal(x, region.X);
        Assert.Equal(y, region.Y);
        Assert.Equal(width, region.Width);
        Assert.Equal(height, region.Height);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    public void CreateRejectsNonPositiveDimensions(int width, int height)
    {
        var result = ScreenRegion.Create(0, 0, width, height);

        Assert.False(result.IsSuccess);
        Assert.Equal("Region must have positive dimensions", result.Match(_ => "", error => error));
    }
}
