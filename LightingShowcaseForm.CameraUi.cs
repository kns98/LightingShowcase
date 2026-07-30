// -----------------------------------------------------------------------------
// File: LightingShowcaseForm.CameraUi.cs
// Purpose: Camera and lighting controls.
//
// Maps UI sliders/text boxes for camera position/orientation and light settings into the active camera/lighting model.
// This comment is intentionally kept in source code so future maintainers can
// understand the role of this file without opening external documentation.
// -----------------------------------------------------------------------------

using System.Globalization;
using LightingShowcase.Math3D;

namespace LightingShowcase;

public sealed partial class LightingShowcaseForm
{
    /// <summary>Updates camera ui from the current application state.</summary>
    private void UpdateCameraUi(bool force = false)
    {
        // The old bottom camera position editor was removed. Camera editing now lives in
        // the clickable camera-timeline editor, while the Helix view remains the live editor camera.
    }

    /// <summary>Implements the format coord operation for this file's subsystem.</summary>
    private static string FormatCoord(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
