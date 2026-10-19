namespace Struo.Application.Files;

/// <summary>
/// Turns raw query values into the one canonical <see cref="ImageTransformRequest"/> the cache key and
/// the transformer see: sizes snap up to configured steps, quality rounds to a multiple of ten, fit is
/// <c>inside</c> or <c>cover</c>. The canonical form is what bounds the number of distinct variants.
/// </summary>
public static class ImageTransformRequestNormalizer
{
    public const string FitInside = "inside";
    public const string FitCover = "cover";

    /// <summary>Granularity, in pixels, of the height derived for <c>fit=cover</c>.</summary>
    public const int CoverHeightUnit = 8;

    public static bool TryNormalize(
        int? width, int? height, string? format, string? fit, int? quality,
        FileStorageOptions.ImageTransformOptions options,
        out ImageTransformRequest request, out string error)
    {
        request = default;
        error = "";

        var normalizedFormat = (format ?? "").ToLowerInvariant();
        if (format is not null && !options.AllowedFormats.Contains(normalizedFormat))
        {
            error = $"Unsupported format '{format}'.";
            return false;
        }

        var normalizedFit = string.IsNullOrEmpty(fit) ? FitInside : fit.ToLowerInvariant();
        if (normalizedFit is not (FitInside or FitCover))
        {
            error = $"Unsupported fit '{fit}' (expected '{FitInside}' or '{FitCover}').";
            return false;
        }

        var (w, h) = NormalizeSize(width, height, normalizedFit == FitCover, options);
        request = new ImageTransformRequest(
            w, h,
            format is null ? null : normalizedFormat,
            normalizedFit,
            quality is null ? Math.Clamp(options.DefaultQuality, 1, 100) : NormalizeQuality(quality.Value));
        return true;
    }

    private static (int? Width, int? Height) NormalizeSize(
        int? width, int? height, bool cover, FileStorageOptions.ImageTransformOptions options)
    {
        var w = width is null ? (int?)null : Snap(width.Value, options.MaxWidth, options.SizeSteps);

        // cover crops to a box, which needs both sides; the height then follows the requested aspect
        // ratio so snapping the width cannot distort it. The height rounds to a multiple of
        // CoverHeightUnit, which bounds the cover variants per width step.
        if (cover && width is not null && height is not null)
        {
            var ratio = (double)Math.Max(height.Value, 1) / Math.Max(width.Value, 1);
            var derived = (int)Math.Round(w!.Value * ratio, MidpointRounding.AwayFromZero);
            var unit = (int)Math.Round(derived / (double)CoverHeightUnit, MidpointRounding.AwayFromZero);
            return (w, Math.Min(Math.Max(unit, 1) * CoverHeightUnit, options.MaxHeight));
        }

        var h = height is null ? (int?)null : Snap(height.Value, options.MaxHeight, options.SizeSteps);
        return (w, h);
    }

    private static int Snap(int requested, int max, int[] steps)
    {
        var clamped = Math.Clamp(requested, 1, max);
        var snapped = clamped;
        if (steps.Length > 0)
        {
            var ordered = steps.Order().ToArray();
            snapped = ordered.FirstOrDefault(s => s >= clamped);
            if (snapped == 0) snapped = ordered[^1];
        }
        return Math.Min(snapped, max);
    }

    private static int NormalizeQuality(int quality) =>
        Math.Clamp((int)Math.Round(quality / 10.0, MidpointRounding.AwayFromZero) * 10, 10, 100);
}
