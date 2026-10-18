namespace ClipNest.Core.Models;

/// <summary>Aspect-preserving decode dimensions, bounded in both directions.</summary>
public static class ImagePreviewSize
{
    public static (int Width, int Height) Fit(uint width, uint height, int maximum)
    {
        if (width == 0 || height == 0) throw new ArgumentOutOfRangeException(nameof(width));
        ArgumentOutOfRangeException.ThrowIfLessThan(maximum, 1);
        var scale = Math.Min(1d, maximum / (double)Math.Max(width, height));
        return (Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)));
    }
}
