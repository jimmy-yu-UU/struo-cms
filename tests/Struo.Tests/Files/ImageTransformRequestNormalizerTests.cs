using AwesomeAssertions;
using Struo.Application.Files;
using Xunit;

namespace Struo.Tests.Files;

public class ImageTransformRequestNormalizerTests
{
    private static FileStorageOptions.ImageTransformOptions Defaults(
        Action<FileStorageOptions.ImageTransformOptions>? tweak = null)
    {
        var o = new FileStorageOptions.ImageTransformOptions();
        o.ApplyCollectionDefaults();
        tweak?.Invoke(o);
        return o;
    }

    private static ImageTransformRequest Ok(
        int? w, int? h, string? format = null, string? fit = null, int? quality = null,
        FileStorageOptions.ImageTransformOptions? o = null)
    {
        ImageTransformRequestNormalizer.TryNormalize(w, h, format, fit, quality, o ?? Defaults(), out var req, out var error)
            .Should().BeTrue(error);
        return req;
    }

    [Theory]
    [InlineData(100, 128)]
    [InlineData(64, 64)]
    [InlineData(65, 128)]
    [InlineData(1, 64)]
    [InlineData(0, 64)]
    [InlineData(-5, 64)]
    [InlineData(4096, 4096)]
    [InlineData(5000, 4096)]
    [InlineData(999999, 4096)]
    public void Width_snaps_up_to_the_next_step_and_never_exceeds_the_last(int requested, int expected) =>
        Ok(requested, null).Width.Should().Be(expected);

    [Fact]
    public void Height_only_snaps_up()
    {
        var r = Ok(null, 300);
        r.Width.Should().BeNull();
        r.Height.Should().Be(320);
    }

    [Fact]
    public void Inside_with_both_dimensions_snaps_each_independently()
    {
        var r = Ok(200, 150);
        (r.Width, r.Height, r.Fit).Should().Be((256, 256, "inside"));
    }

    [Theory]
    [InlineData(1000, 500, 1024, 512)]
    [InlineData(300, 200, 320, 216)]
    [InlineData(300, 250, 320, 264)]
    [InlineData(333, 100, 480, 144)]
    [InlineData(100, 100, 128, 128)]
    [InlineData(400, 800, 480, 960)]
    public void Cover_snaps_the_width_and_derives_the_height_from_the_requested_ratio(
        int w, int h, int expectedW, int expectedH)
    {
        var r = Ok(w, h, fit: "cover");
        (r.Width, r.Height, r.Fit).Should().Be((expectedW, expectedH, "cover"));
    }

    [Fact]
    public void Cover_with_one_dimension_snaps_that_dimension_only()
    {
        var wOnly = Ok(300, null, fit: "cover");
        (wOnly.Width, wOnly.Height).Should().Be((320, null));
        var hOnly = Ok(null, 300, fit: "cover");
        (hOnly.Width, hOnly.Height).Should().Be((null, 320));
    }

    [Fact]
    public void Cover_derived_height_is_clamped_to_the_configured_maximum_and_at_least_one_unit()
    {
        Ok(100, 100000, fit: "cover").Height.Should().Be(4096);
        Ok(999999, 1, fit: "cover").Height.Should().Be(8);
    }

    [Fact]
    public void Snapped_values_never_exceed_the_configured_maximum()
    {
        var o = Defaults(x => { x.MaxWidth = 1000; x.MaxHeight = 700; });
        var r = Ok(900, 650, o: o);
        (r.Width, r.Height).Should().Be((1000, 700));
    }

    [Theory]
    [InlineData(null, "inside")]
    [InlineData("", "inside")]
    [InlineData("inside", "inside")]
    [InlineData("COVER", "cover")]
    public void Fit_is_inside_or_cover(string? fit, string expected) =>
        Ok(100, null, fit: fit).Fit.Should().Be(expected);

    [Theory]
    [InlineData("garbage")]
    [InlineData("contain")]
    [InlineData("fill")]
    public void Unknown_fit_is_rejected(string fit)
    {
        ImageTransformRequestNormalizer.TryNormalize(100, null, null, fit, null, Defaults(), out _, out var error)
            .Should().BeFalse();
        error.Should().Contain("fit");
    }

    [Theory]
    [InlineData(null, 82)]
    [InlineData(5, 10)]
    [InlineData(14, 10)]
    [InlineData(15, 20)]
    [InlineData(82, 80)]
    [InlineData(85, 90)]
    [InlineData(100, 100)]
    [InlineData(250, 100)]
    [InlineData(-3, 10)]
    public void Quality_rounds_to_a_multiple_of_ten_within_10_to_100(int? requested, int expected) =>
        Ok(100, null, quality: requested).Quality.Should().Be(expected);

    [Fact]
    public void Format_is_lowercased_and_checked_against_the_allowlist()
    {
        Ok(100, null, format: "WEBP").Format.Should().Be("webp");
        ImageTransformRequestNormalizer.TryNormalize(100, null, "bmp", null, null, Defaults(), out _, out var error)
            .Should().BeFalse();
        error.Should().Contain("bmp");
    }

    [Fact]
    public void Format_only_request_keeps_both_dimensions_unset()
    {
        var r = Ok(null, null, format: "png");
        (r.Width, r.Height, r.Format).Should().Be((null, null, "png"));
    }

    [Fact]
    public void Custom_steps_are_used_in_ascending_order()
    {
        var o = Defaults(x => x.SizeSteps = [500, 100]);
        Ok(101, null, o: o).Width.Should().Be(500);
        Ok(50, null, o: o).Width.Should().Be(100);
        Ok(900, null, o: o).Width.Should().Be(500);
    }
}
