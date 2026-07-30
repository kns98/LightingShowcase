// -----------------------------------------------------------------------------
// File: LightingShowcaseForm.LightingUi.cs
// Purpose: Control-panel scene light editing.
//
// Keeps all add/edit/remove logic for SceneLight objects in one partial form file.
// The lighting tab edits the Scene.Lights collection directly, captures undo
// snapshots for every mutating action, and marks both previews dirty afterward.
// -----------------------------------------------------------------------------

using LightingShowcase.Lighting;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase;

public sealed partial class LightingShowcaseForm
{
    /// <summary>Refreshes the list of scene lights while preserving the previous selection when possible.</summary>
    private void RefreshLightList(string? preferredId = null)
    {
        int previousIndex = panel.LightListBox.SelectedIndex;
        panel.LightListBox.BeginUpdate();
        panel.LightListBox.Items.Clear();

        for (int i = 0; i < scene.Lights.Count; i++)
        {
            SceneLight light = scene.Lights[i];
            string enabled = light.Enabled ? "on" : "off";
            panel.LightListBox.Items.Add($"{i + 1:00}  {light.Id}  {enabled}  {light.Kind}  pos=({light.Position.X:0.##}, {light.Position.Y:0.##}, {light.Position.Z:0.##})  dir=({light.Direction.X:0.##}, {light.Direction.Y:0.##}, {light.Direction.Z:0.##})  rgb=({light.Color.X:0.##}, {light.Color.Y:0.##}, {light.Color.Z:0.##})  i={light.Intensity:0.##}  range={light.Range:0.##}");
        }

        panel.LightListBox.EndUpdate();

        int selectedIndex = -1;
        if (!string.IsNullOrWhiteSpace(preferredId))
            selectedIndex = scene.Lights.FindIndex(light => string.Equals(light.Id, preferredId, StringComparison.OrdinalIgnoreCase));
        if (selectedIndex < 0 && previousIndex >= 0 && previousIndex < scene.Lights.Count)
            selectedIndex = previousIndex;
        if (selectedIndex < 0 && scene.Lights.Count > 0)
            selectedIndex = 0;

        panel.LightListBox.SelectedIndex = selectedIndex;
        LoadSelectedLightIntoEditor(selectedIndex);
    }

    /// <summary>Loads the selected light into the editor fields.</summary>
    private void LoadSelectedLightIntoEditor(int index)
    {
        if (index < 0 || index >= scene.Lights.Count)
        {
            panel.LightIdBox.Text = MakeUniqueLightId("light");
            panel.LightKindComboBox.SelectedItem = SceneLightKind.Point.ToString();
            panel.LightPosXBox.Text = "0";
            panel.LightPosYBox.Text = "2";
            panel.LightPosZBox.Text = "3";
            panel.LightDirXBox.Text = "0";
            panel.LightDirYBox.Text = "0";
            panel.LightDirZBox.Text = "-1";
            panel.LightColorRBox.Text = "1";
            panel.LightColorGBox.Text = "1";
            panel.LightColorBBox.Text = "1";
            panel.LightIntensityBox.Text = "3";
            panel.LightRangeBox.Text = "0";
            panel.LightInnerConeBox.Text = "0";
            panel.LightOuterConeBox.Text = "45";
            panel.LightEnabledBox.Checked = true;
            panel.ApplyLightButton.Enabled = false;
            panel.RemoveLightButton.Enabled = false;
            helixViewport.SelectLight(null, scene);
            return;
        }

        SceneLight light = scene.Lights[index];
        panel.LightIdBox.Text = light.Id;
        panel.LightKindComboBox.SelectedItem = light.Kind.ToString();
        if (panel.LightKindComboBox.SelectedIndex < 0)
            panel.LightKindComboBox.SelectedIndex = 0;
        panel.LightPosXBox.Text = FormatCoord(light.Position.X);
        panel.LightPosYBox.Text = FormatCoord(light.Position.Y);
        panel.LightPosZBox.Text = FormatCoord(light.Position.Z);
        panel.LightDirXBox.Text = FormatCoord(light.Direction.X);
        panel.LightDirYBox.Text = FormatCoord(light.Direction.Y);
        panel.LightDirZBox.Text = FormatCoord(light.Direction.Z);
        panel.LightColorRBox.Text = FormatCoord(light.Color.X);
        panel.LightColorGBox.Text = FormatCoord(light.Color.Y);
        panel.LightColorBBox.Text = FormatCoord(light.Color.Z);
        panel.LightIntensityBox.Text = FormatCoord(light.Intensity);
        panel.LightRangeBox.Text = FormatCoord(light.Range);
        panel.LightInnerConeBox.Text = FormatCoord(RadiansToDegrees(light.InnerConeAngle));
        panel.LightOuterConeBox.Text = FormatCoord(RadiansToDegrees(light.OuterConeAngle));
        panel.LightEnabledBox.Checked = light.Enabled;
        panel.ApplyLightButton.Enabled = true;
        panel.RemoveLightButton.Enabled = true;
        helixViewport.SelectLight(light.Id, scene);
    }

    /// <summary>Selects a light picked directly from the Helix overlay marker.</summary>
    private void OnHelixLightPicked(string lightId)
    {
        int index = scene.Lights.FindIndex(light => string.Equals(light.Id, lightId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return;

        panel.Tabs.SelectedIndex = 0;
        panel.LightListBox.SelectedIndex = index;
        LoadSelectedLightIntoEditor(index);
        lastLoadMessage = $"Selected light: {scene.Lights[index].Id}";
        UpdateStatus();
    }

    /// <summary>Adds a new scene light from the current editor fields.</summary>
    private void AddLightFromEditor()
    {
        SceneLight light = ReadLightEditor(fallback: null, requireUniqueId: true);
        CaptureUndoState();
        scene.Lights.Add(light);
        lastLoadMessage = $"Added light: {light.Id}";
        RefreshLightList(light.Id);
        MarkRenderDirty();
        UpdateStatus();
    }

    /// <summary>Applies current editor fields to the selected scene light.</summary>
    private void ApplySelectedLightEdit()
    {
        int index = panel.LightListBox.SelectedIndex;
        if (index < 0 || index >= scene.Lights.Count)
            return;

        SceneLight previous = scene.Lights[index];
        SceneLight edited = ReadLightEditor(previous, requireUniqueId: false);
        CaptureUndoState();
        previous.Id = edited.Id;
        previous.Kind = edited.Kind;
        previous.Position = edited.Position;
        previous.Direction = edited.Direction;
        previous.Color = edited.Color;
        previous.Intensity = edited.Intensity;
        previous.Range = edited.Range;
        previous.InnerConeAngle = edited.InnerConeAngle;
        previous.OuterConeAngle = edited.OuterConeAngle;
        previous.Enabled = edited.Enabled;
        previous.CastsShadow = edited.CastsShadow;
        previous.IsImported = edited.IsImported;
        previous.IsDefault = edited.IsDefault;
        lastLoadMessage = $"Edited light: {previous.Id}";
        RefreshLightList(previous.Id);
        MarkRenderDirty();
        UpdateStatus();
    }

    /// <summary>Removes the selected scene light.</summary>
    private void RemoveSelectedLight()
    {
        int index = panel.LightListBox.SelectedIndex;
        if (index < 0 || index >= scene.Lights.Count)
            return;

        string id = scene.Lights[index].Id;
        CaptureUndoState();
        scene.Lights.RemoveAt(index);
        lastLoadMessage = $"Removed light: {id}";
        RefreshLightList();
        MarkRenderDirty();
        UpdateStatus();
    }

    /// <summary>Creates editable scene lights from the currently selected mesh objects.</summary>
    private void ConvertSelectedObjectsToLights()
    {
        List<SceneObjectGroup> groups = selectedGroupIds
            .Select(id => scene.GroupById(id))
            .Where(group => group?.IsSelectable == true)
            .Cast<SceneObjectGroup>()
            .Distinct()
            .ToList();

        if (groups.Count == 0)
            return;

        try
        {
            CaptureUndoState();
            List<string> createdIds = new();
            foreach (SceneObjectGroup group in groups)
            {
                SceneLight light = CreateLightFromObject(group);
                scene.Lights.Add(light);
                MakeObjectSurfaceEmissive(group, light);
                createdIds.Add(light.Id);
            }

            scene.RebuildWorldGeometry();
            string? preferredId = createdIds.LastOrDefault();
            lastLoadMessage = groups.Count == 1
                ? $"Converted {groups[0].Name} to light: {preferredId}"
                : $"Converted {groups.Count} objects to lights";
            RefreshLightList(preferredId);
            MarkRenderDirty();
            UpdateSelectionUi();
            helixViewport.SelectGroups(selectedGroupIds, scene);
            UpdateStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Convert to light failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private SceneLight CreateLightFromObject(SceneObjectGroup group)
    {
        Aabb bounds = group.GetWorldBounds(includeHidden: true);
        Vec3 center = (bounds.Min + bounds.Max) * 0.5;
        double radius = BoundsRadius(bounds);
        Material? material = group.FirstMaterialOrDefault();
        Vec3 color = EstimateLightColor(material);
        double surfaceEmission = material?.Emission ?? 0.0;
        double intensity = Math.Clamp(Math.Max(3.0, radius * radius * 8.0 + surfaceEmission * 3.0), 1.0, 5000.0);
        double range = Math.Clamp(radius * 8.0, 0.0, 10000.0);
        Vec3 direction = EstimateObjectForwardDirection(group);
        string id = MakeUniqueLightId("light-" + SanitizeLightId(group.Name));

        return new SceneLight(
            id,
            center,
            color,
            intensity,
            enabled: true,
            kind: SceneLightKind.Point,
            direction: direction,
            range: range,
            innerConeAngle: 0.0,
            outerConeAngle: Math.PI / 4.0,
            castsShadow: true,
            isImported: false,
            isDefault: false);
    }

    private static void MakeObjectSurfaceEmissive(SceneObjectGroup group, SceneLight light)
    {
        Vec3 emissionColor = NormalizeLightColor(light.Color);
        group.ApplyMaterialProperties(material => new Material(
            material.Color,
            Math.Max(material.Emission, 1.5),
            light.Id,
            material.Texture,
            emissionColor,
            material.EmissiveTexture,
            material.Alpha,
            material.AlphaBlend,
            material.Metallic,
            material.Roughness,
            material.Transmission,
            material.MetallicRoughnessTexture,
            material.NormalTexture));
    }

    private static Vec3 EstimateLightColor(Material? material)
    {
        if (material == null)
            return new Vec3(1.0, 0.92, 0.76);

        Vec3 sampled = material.Emission > 0.0
            ? material.SampleEmission(0.5, 0.5)
            : material.Sample(0.5, 0.5);
        return NormalizeLightColor(sampled);
    }

    private static Vec3 EstimateObjectForwardDirection(SceneObjectGroup group)
    {
        Vec3 origin = group.TransformPoint(group.Pivot);
        Vec3 forward = group.TransformPoint(group.Pivot + new Vec3(0, 0, -1)) - origin;
        if (forward.Length() < 1e-8)
            forward = new Vec3(0, 0, -1);
        return forward.Normalize();
    }

    private static string SanitizeLightId(string name)
    {
        string cleaned = new(name.Trim().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray());
        cleaned = string.Join('-', cleaned.Split('-', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(cleaned) ? "object" : cleaned;
    }

    private static double BoundsRadius(Aabb bounds)
    {
        Vec3 size = bounds.Max - bounds.Min;
        return Math.Max(0.25, size.Length() * 0.5);
    }

    private static Vec3 NormalizeLightColor(Vec3 color)
    {
        double max = Math.Max(color.X, Math.Max(color.Y, color.Z));
        if (!double.IsFinite(max) || max < 0.05)
            return new Vec3(1.0, 0.92, 0.76);
        return new Vec3(
            Math.Clamp(color.X / max, 0.0, 1.0),
            Math.Clamp(color.Y / max, 0.0, 1.0),
            Math.Clamp(color.Z / max, 0.0, 1.0));
    }

    /// <summary>Reads and validates one SceneLight from the lighting editor fields.</summary>
    private SceneLight ReadLightEditor(SceneLight? fallback, bool requireUniqueId)
    {
        string id = string.IsNullOrWhiteSpace(panel.LightIdBox.Text)
            ? MakeUniqueLightId("light")
            : panel.LightIdBox.Text.Trim();

        if (requireUniqueId || scene.Lights.Any(light => !ReferenceEquals(light, fallback) && string.Equals(light.Id, id, StringComparison.OrdinalIgnoreCase)))
            id = MakeUniqueLightId(id);

        SceneLightKind kind = ReadLightKind(fallback?.Kind ?? SceneLightKind.Point);
        Vec3 position = ReadVector(panel.LightPosXBox, panel.LightPosYBox, panel.LightPosZBox, fallback?.Position ?? new Vec3(0, 2, 3));
        Vec3 direction = ReadVector(panel.LightDirXBox, panel.LightDirYBox, panel.LightDirZBox, fallback?.Direction ?? new Vec3(0, 0, -1));
        Vec3 color = ClampColor(ReadVector(panel.LightColorRBox, panel.LightColorGBox, panel.LightColorBBox, fallback?.Color ?? new Vec3(1, 1, 1)));
        double intensity = Math.Clamp(ReadDouble(panel.LightIntensityBox.Text, fallback?.Intensity ?? 3.0), 0.0, 100000.0);
        double range = Math.Max(0.0, ReadDouble(panel.LightRangeBox.Text, fallback?.Range ?? 0.0));
        double innerCone = DegreesToRadians(Math.Clamp(ReadDouble(panel.LightInnerConeBox.Text, RadiansToDegrees(fallback?.InnerConeAngle ?? 0.0)), 0.0, 179.0));
        double outerCone = DegreesToRadians(Math.Clamp(ReadDouble(panel.LightOuterConeBox.Text, RadiansToDegrees(fallback?.OuterConeAngle ?? Math.PI / 4.0)), 0.0, 179.0));
        if (outerCone < innerCone)
            outerCone = innerCone;
        return new SceneLight(id, position, color, intensity, panel.LightEnabledBox.Checked, kind, direction, range, innerCone, outerCone, fallback?.CastsShadow ?? true, fallback?.IsImported ?? false, fallback?.IsDefault ?? false);
    }

    /// <summary>Creates a unique light id using the requested base id.</summary>
    private string MakeUniqueLightId(string baseId)
    {
        baseId = string.IsNullOrWhiteSpace(baseId) ? "light" : baseId.Trim();
        if (!scene.Lights.Any(light => string.Equals(light.Id, baseId, StringComparison.OrdinalIgnoreCase)))
            return baseId;

        for (int i = 2; i < 10000; i++)
        {
            string candidate = $"{baseId}-{i}";
            if (!scene.Lights.Any(light => string.Equals(light.Id, candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }

        return $"{baseId}-{Guid.NewGuid():N}"[..Math.Min(baseId.Length + 9, 32)];
    }

    /// <summary>Reads the selected light kind.</summary>
    private SceneLightKind ReadLightKind(SceneLightKind fallback)
    {
        string? text = panel.LightKindComboBox.SelectedItem?.ToString() ?? panel.LightKindComboBox.Text;
        return Enum.TryParse(text, ignoreCase: true, out SceneLightKind kind) ? kind : fallback;
    }

    /// <summary>Converts radians to degrees for UI display.</summary>
    private static double RadiansToDegrees(double radians) => radians * 180.0 / Math.PI;

    /// <summary>Converts degrees to radians for scene storage.</summary>
    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    /// <summary>Clamps RGB color components to the normalized range used by the ray tracer.</summary>
    private static Vec3 ClampColor(Vec3 color) => new(
        Math.Clamp(color.X, 0.0, 1.0),
        Math.Clamp(color.Y, 0.0, 1.0),
        Math.Clamp(color.Z, 0.0, 1.0));
}
