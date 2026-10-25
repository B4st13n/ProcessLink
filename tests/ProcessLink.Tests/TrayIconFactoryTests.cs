using Xunit;

namespace ProcessLink.Tests;

public sealed class TrayIconFactoryTests
{
    [Fact]
    public void CreateIcon_ReturnsUsableIcon()
    {
        using var icon = TrayIconFactory.CreateIcon();

        Assert.NotNull(icon);
        Assert.True(icon.Width > 0);
        Assert.True(icon.Height > 0);
    }

    [Fact]
    public void CreateImageSource_ReturnsFrozenBitmapSource()
    {
        var source = TrayIconFactory.CreateImageSource();

        Assert.NotNull(source);
        Assert.True(source.IsFrozen);
        Assert.True(source.Width > 0);
        Assert.True(source.Height > 0);
    }
}
