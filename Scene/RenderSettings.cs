// -----------------------------------------------------------------------------
// File: Scene/RenderSettings.cs
// Purpose: Renderer-independent settings stored with a scene/session.
// -----------------------------------------------------------------------------

using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

/// <summary>Neutral render options consumed by preview and final render adapters.</summary>
public sealed class RenderSettings
{
    public RenderMode Mode { get; set; } = RenderMode.Lit;
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public double Exposure { get; set; } = 1.0;
    public double AmbientStrength { get; set; } = 1.0;
    public Vec3 BackgroundTop { get; set; } = new(0.055, 0.060, 0.072);
    public Vec3 BackgroundBottom { get; set; } = new(0.010, 0.012, 0.016);
    public bool ShowGrid { get; set; } = true;
    public bool ShowLightIcons { get; set; } = true;
    public bool ShowCameraIcon { get; set; } = true;
    public bool UseShadows { get; set; } = true;
    public int PathBounceCount { get; set; } = 0;
    public RenderBackend Backend { get; set; } = RenderBackend.Cpu;

    public RenderSettings Clone() => new()
    {
        Mode = Mode,
        Width = Width,
        Height = Height,
        Exposure = Exposure,
        AmbientStrength = AmbientStrength,
        BackgroundTop = BackgroundTop,
        BackgroundBottom = BackgroundBottom,
        ShowGrid = ShowGrid,
        ShowLightIcons = ShowLightIcons,
        ShowCameraIcon = ShowCameraIcon,
        UseShadows = UseShadows,
        PathBounceCount = PathBounceCount,
        Backend = Backend
    };
}
