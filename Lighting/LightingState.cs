// -----------------------------------------------------------------------------
// File: Lighting/LightingState.cs
// Purpose: Legacy render-time lighting state.
//
// Default and imported lights are now represented only by editable SceneLight
// objects. This type remains as a small compatibility snapshot because the
// renderer pipeline still accepts a LightingState instance.
// -----------------------------------------------------------------------------

namespace LightingShowcase.Lighting;

/// <summary>Compatibility state consumed by the ray tracer pipeline.</summary>
public sealed class LightingState
{
    public string Label { get; private set; } = "Scene lights";

    /// <summary>Returns a neutral multiplier for legacy callers.</summary>
    public double GetLevel(string id) => 1.0;

    /// <summary>Retained for older scene/UI code; no hidden light multipliers are stored.</summary>
    public void SetLevel(string id, double level)
    {
        Label = "Scene lights";
    }

    /// <summary>Implements the clone operation for this file's subsystem.</summary>
    public LightingState Clone() => new();

    // Playback only animates the camera timeline. Lighting is manual through SceneLight objects.
    public void Evaluate(double timeSeconds, double duration)
    {
        Label = "Scene lights";
    }
}
