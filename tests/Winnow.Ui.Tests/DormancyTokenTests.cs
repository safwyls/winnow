using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Winnow.App.Services;
using Winnow.Covers;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class DormancyTokenTests
{
    [AvaloniaFact]
    public void Xaml_resources_procedural_art_and_disk_renderer_share_the_transform_endpoint()
    {
        var resources = new ResourceInclude(new Uri("avares://Winnow/"))
            { Source = new Uri("avares://Winnow/Themes/tokens.axaml") }.Loaded;
        var options = new CoverCacheOptions();
        Assert.Equal(DormancyStyle.BrightnessFloor, Assert.IsType<double>(resources["DormancyBrightFloor"]));
        Assert.Equal(DormancyStyle.SaturationFloor, Assert.IsType<double>(resources["DormancySatFloor"]));
        Assert.Equal(DormancyStyle.HueDegrees, Assert.IsType<double>(resources["DormancyHueDegrees"]));
        Assert.Equal((float)Dormancy.BrightFloor, options.FloorBrightness);
        Assert.Equal((float)Dormancy.SatFloor, options.FloorSaturation);
        Assert.Equal(CoverImaging.DefaultHueDegrees, options.FloorHueDegrees);
    }
}
