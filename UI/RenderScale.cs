// -----------------------------------------------------------------------------
// File: UI/RenderScale.cs
// Purpose: Render scale presets.
//
// Formats and parses render-scale values used by the raytrace preview resolution selector.
// This comment is intentionally kept in source code so future maintainers can
// understand the role of this file without opening external documentation.
// -----------------------------------------------------------------------------

namespace LightingShowcase.UI;

/// <summary>Utility for parsing and formatting raytrace render-scale presets.</summary>
public static class RenderScale
{
    public static readonly double[] Values = { 0.125, 0.25, 0.33, 0.50, 0.67, 1.0, 2.0, 3.0, 4.0, 5.0 };
    public static readonly string[] Labels = { "12.5%", "25%", "33%", "50%", "67%", "100%", "200%", "300%", "400%", "500%" };

    /// <summary>Implements the index of operation for this file's subsystem.</summary>
    public static int IndexOf(double scale)
    {
        for (int i = 0; i < Values.Length; i++)
            if (System.Math.Abs(Values[i] - scale) < 0.001) return i;
        return 3;
    }

    /// <summary>Implements the clamp index operation for this file's subsystem.</summary>
    public static int ClampIndex(int index)
    {
        return System.Math.Max(0, System.Math.Min(Values.Length - 1, index));
    }
}
