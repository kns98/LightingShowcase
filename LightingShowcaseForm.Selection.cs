// -----------------------------------------------------------------------------
// File: LightingShowcaseForm.Selection.cs
// Purpose: Selection and object editing.
//
// Tracks the selected object, updates transform/material controls, applies gizmo changes, and performs object operations.
// This comment is intentionally kept in source code so future maintainers can
// understand the role of this file without opening external documentation.
// -----------------------------------------------------------------------------

using System.Globalization;
using LightingShowcase.CameraSystem;
using LightingShowcase.Math3D;
using LightingShowcase.Rendering;
using LightingShowcase.SceneGraph;
using LightingShowcase.UI;

namespace LightingShowcase;

public sealed partial class LightingShowcaseForm
{
    private SceneObjectGroup? SelectedGroup => scene.GroupById(selectedGroupId);

    /// <summary>Selects or shift-toggles a group picked from the Helix viewport.</summary>
    private void OnHelixGroupPicked(int groupId)
    {
        bool additive = ModifierKeys.HasFlag(Keys.Shift);
        SelectGroup(groupId, additive);
    }

    /// <summary>Applies rectangle/lasso selection results from the Helix viewport without creating undo history.</summary>
    private void OnHelixGroupsMarqueeSelected(IReadOnlyCollection<int> groupIds, ViewportSelectionCombineMode combineMode)
    {
        HashSet<int> validIds = groupIds
            .Select(id => scene.GroupById(id))
            .Where(group => group?.IsSelectable == true)
            .Select(group => group!.Id)
            .ToHashSet();

        switch (combineMode)
        {
            case ViewportSelectionCombineMode.Add:
                foreach (int id in validIds)
                    selectedGroupIds.Add(id);
                break;

            case ViewportSelectionCombineMode.Subtract:
                foreach (int id in validIds)
                    selectedGroupIds.Remove(id);
                break;

            case ViewportSelectionCombineMode.Toggle:
                foreach (int id in validIds)
                {
                    if (!selectedGroupIds.Add(id))
                        selectedGroupIds.Remove(id);
                }
                break;

            default:
                selectedGroupIds.Clear();
                foreach (int id in validIds)
                    selectedGroupIds.Add(id);
                break;
        }

        selectedGroupId = selectedGroupIds.Count == 0 ? -1 : selectedGroupIds.Last();
        UpdateSelectionUi();
        helixViewport.SelectGroups(selectedGroupIds, scene);
        UpdateStatus();
        Invalidate();
    }

    /// <summary>Selects a single group, or toggles it into the multi-selection set.</summary>
    private void SelectGroup(int groupId, bool additive = false)
    {
        SceneObjectGroup? group = scene.GroupById(groupId);
        if (group?.IsSelectable != true)
        {
            ClearSelection();
            return;
        }

        if (additive)
        {
            if (!selectedGroupIds.Add(groupId))
                selectedGroupIds.Remove(groupId);
            selectedGroupId = selectedGroupIds.Count == 0 ? -1 : selectedGroupIds.Last();
        }
        else
        {
            selectedGroupIds.Clear();
            selectedGroupIds.Add(groupId);
            selectedGroupId = groupId;
        }

        UpdateSelectionUi();
        helixViewport.SelectGroups(selectedGroupIds, scene);
        UpdateStatus();
        Invalidate();
    }

    /// <summary>Clears selection and updates dependent UI/render state.</summary>
    private void ClearSelection()
    {
        selectedGroupId = -1;
        selectedGroupIds.Clear();
        UpdateSelectionUi();
        helixViewport.SelectGroups(selectedGroupIds, scene);
        UpdateStatus();
        Invalidate();
    }

    /// <summary>Attempts to pick selection and reports failure without crashing the UI.</summary>
    private bool TryPickSelection(MouseEventArgs e)
    {
        if (frame == null || e.Button != MouseButtons.Left || panel.Bounds.Contains(e.Location))
            return false;

        int rx = Math.Clamp((int)(e.X / (double)Math.Max(1, ClientSize.Width) * frame.Width), 0, frame.Width - 1);
        int ry = Math.Clamp((int)(e.Y / (double)Math.Max(1, ClientSize.Height) * frame.Height), 0, frame.Height - 1);
        CameraBasis basis = camera.GetBasis();
        Vec3 direction = RayTracer.RayDirection(rx, ry, frame.Width, frame.Height, basis);
        Hit? hit = scene.Intersect(new Ray(camera.Position, direction));

        if (hit?.GroupId > 0)
        {
            SelectGroup(hit.GroupId, ModifierKeys.HasFlag(Keys.Shift));
            return true;
        }

        ClearSelection();
        return false;
    }

    /// <summary>Applies selected move to the active scene/editor state.</summary>
    private void ApplySelectedMove()
    {
        SceneObjectGroup? group = SelectedGroup;
        if (group == null) return;

        Vec3 requestedDelta = ReadVector(panel.MoveXBox, panel.MoveYBox, panel.MoveZBox, Vec3.Zero);
        if (!IsFinite(requestedDelta) || requestedDelta.Length() <= 1e-12)
            return;

        Vec3 worldDelta = requestedDelta;
        if (panel.ReferenceCameraBox.Checked)
        {
            CameraBasis basis = camera.GetBasis();
            worldDelta = basis.Right * requestedDelta.X + basis.Up * requestedDelta.Y + basis.Forward * requestedDelta.Z;
        }

        if (!IsFinite(worldDelta) || worldDelta.Length() <= 1e-12)
            return;

        CaptureUndoState();
        group.Position += worldDelta;
        CommitGroupTransformForCurrentEdit(group);
        scene.RebuildWorldGeometry();

        // Move values are deltas, not absolute coordinates.  Resetting them after a
        // successful move prevents a second Apply click/Enter press from pushing the
        // selected group repeatedly until it appears to vanish from the view.
        ResetMoveInputs();
        MarkRenderDirty();
        UpdateSelectionUi();
        UpdateStatus();
    }

    /// <summary>Applies selected rotation to the active scene/editor state.</summary>
    private void ApplySelectedRotation()
    {
        SceneObjectGroup? group = SelectedGroup;
        if (group == null) return;
        Vec3 degrees = ReadVector(panel.RotXBox, panel.RotYBox, panel.RotZBox, Vec3.Zero);
        if (!IsFinite(degrees)) return;
        double toRad = Math.PI / 180.0;
        CaptureUndoState();
        Vec3 scaleBeforeRotate = group.Scale;
        group.Rotation += degrees * toRad;
        group.Scale = scaleBeforeRotate;
        CommitGroupTransformForCurrentEdit(group);
        scene.RebuildWorldGeometry();
        MarkRenderDirty();
        UpdateSelectionUi();
        UpdateStatus();
    }

    /// <summary>Applies selected scale to the active scene/editor state.</summary>
    private void ApplySelectedScale()
    {
        SceneObjectGroup? group = SelectedGroup;
        if (group == null) return;
        Vec3 factor = ReadVector(panel.ScaleXBox, panel.ScaleYBox, panel.ScaleZBox, new Vec3(1, 1, 1));
        if (!IsFinite(factor)) return;
        factor = new Vec3(ClampScale(factor.X), ClampScale(factor.Y), ClampScale(factor.Z));
        CaptureUndoState();
        group.Scale = new Vec3(group.Scale.X * factor.X, group.Scale.Y * factor.Y, group.Scale.Z * factor.Z);
        CommitGroupTransformForCurrentEdit(group);
        scene.RebuildWorldGeometry();
        MarkRenderDirty();
        UpdateSelectionUi();
        UpdateStatus();
    }


    /// <summary>Applies an incremental transform emitted by the Helix viewport gizmo.</summary>
    private void ApplyGizmoDelta(GizmoDelta delta)
    {
        if (delta.GroupId != selectedGroupId)
            return;

        SceneObjectGroup? group = SelectedGroup;
        if (group == null)
            return;

        if (!gizmoUndoCaptured)
        {
            CaptureUndoState();
            gizmoUndoCaptured = true;
        }

        Vec3 axis = delta.Axis switch
        {
            GizmoAxis.X => new Vec3(1, 0, 0),
            GizmoAxis.Y => new Vec3(0, 1, 0),
            _ => new Vec3(0, 0, 1)
        };

        bool rebuiltParametricShadow = false;
        bool liveParametricEdit = group.HasParametricPrimitive && group.Children.Count == 0;

        switch (delta.Operation)
        {
            case GizmoOperation.Move:
                if (liveParametricEdit)
                    rebuiltParametricShadow = group.ApplyParametricMoveDelta(axis * delta.Amount);
                else
                    group.Position += axis * delta.Amount;
                break;

            case GizmoOperation.Rotate:
                Vec3 scaleBeforeRotate = group.Scale;
                group.Rotation += axis * delta.Amount;
                group.Scale = scaleBeforeRotate;
                break;

            case GizmoOperation.Scale:
                double factor = ClampScale(1.0 + delta.Amount);
                if (liveParametricEdit)
                    rebuiltParametricShadow = group.ApplyParametricScaleDelta(AxisName(delta.Axis), factor);
                else
                {
                    group.Scale = delta.Axis switch
                    {
                        GizmoAxis.X => new Vec3(ClampScale(group.Scale.X * factor), group.Scale.Y, group.Scale.Z),
                        GizmoAxis.Y => new Vec3(group.Scale.X, ClampScale(group.Scale.Y * factor), group.Scale.Z),
                        _ => new Vec3(group.Scale.X, group.Scale.Y, ClampScale(group.Scale.Z * factor))
                    };
                }
                break;
        }

        if (rebuiltParametricShadow)
        {
            scene.RebuildPrimitiveShadowGeometry(group);
            scene.RebuildWorldGeometry();
            helixViewport.RebuildGroupGeometry(group.Id, scene);
        }
        else
        {
            helixViewport.PreviewGroupTransform(group.Id, scene);
        }

        RefreshEditorDeltaTransformDetails(group);
        UpdateSelectionUi();
    }

    /// <summary>Implements the complete gizmo drag operation for this file's subsystem.</summary>
    private static char AxisName(GizmoAxis axis) => axis switch
    {
        GizmoAxis.X => 'X',
        GizmoAxis.Y => 'Y',
        _ => 'Z'
    };

    private void CompleteGizmoDrag()
    {
        SceneObjectGroup? group = SelectedGroup;
        if (group == null)
        {
            gizmoUndoCaptured = false;
            return;
        }

        CommitGroupTransformForCurrentEdit(group);
        gizmoUndoCaptured = false;
        scene.RebuildWorldGeometry();
        MarkRenderDirty();
        UpdateSelectionUi();
        UpdateStatus();
    }


    /// <summary>Commits pending transforms using object-specific metadata when the object is parametric.</summary>
    private void CommitGroupTransformForCurrentEdit(SceneObjectGroup group)
    {
        // Recursive groups must keep their transform as node metadata.  Baking a
        // parent group's rotation/scale into child meshes after marquee-select +
        // Group can make children inherit the parent transform and then be
        // transformed again through their own node transform on the next rebuild.
        // The visible symptom is that grouped objects lose their proportions or
        // appear skewed after rotating the group.
        //
        // Treat compound groups like normal scene nodes: their Position/Rotation/
        // Scale remain on the parent, and child geometry stays untouched until an
        // explicit destructive operation such as Ungroup needs to flatten it.
        if (group.Children.Count > 0)
        {
            // A compound group's pivot is established when the group is created
            // or when its membership changes. Do not recalculate it while the
            // user is rotating: the axis-aligned bounds center can move during
            // rotation and make the group appear to wobble or stretch.
            return;
        }

        if (group.HasParametricPrimitive)
        {
            bool rebuilt = false;
            if (group.ApplyPendingTransformToPrimitiveParameters())
                rebuilt = scene.RebuildPrimitiveShadowGeometry(group);

            // Rotation remains as transform metadata for parametric objects; move/scale
            // are absorbed into authored parameters so, for example, scaling a sphere
            // edits radius instead of baking scaled triangles.
            if (!rebuilt)
                group.RecalculatePivot();
            return;
        }

        group.BakeCurrentTransform();
    }

    /// <summary>Deletes the currently selected object from the scene.</summary>
    private void DeleteSelectedGroup()
    {
        if (selectedGroupIds.Count == 0) return;
        CaptureUndoState();
        foreach (int id in selectedGroupIds.ToList())
            scene.DeleteGroup(id);
        selectedGroupId = -1;
        selectedGroupIds.Clear();
        helixViewport.SelectGroups(selectedGroupIds, scene);
        UpdateSelectionUi();
        MarkRenderDirty();
        UpdateStatus();
    }

    /// <summary>Clones the selected object and selects the duplicate.</summary>
    private void DuplicateSelectedGroup()
    {
        if (selectedGroupId <= 0) return;

        try
        {
            CaptureUndoState();
            SceneObjectGroup duplicate = scene.DuplicateGroup(selectedGroupId);
            selectedGroupId = duplicate.Id;
            selectedGroupIds.Clear();
            selectedGroupIds.Add(duplicate.Id);
            lastLoadMessage = $"Duplicated {duplicate.Name}";
            MarkRenderDirty();
            UpdateSelectionUi();
            helixViewport.SelectGroups(selectedGroupIds, scene);
            UpdateStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Duplicate failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }


    /// <summary>Groups the current multi-selection into a recursive parent group.</summary>
    private void GroupSelectedGroups()
    {
        if (selectedGroupIds.Count < 2) return;

        try
        {
            CaptureUndoState();
            SceneObjectGroup group = scene.GroupSelectedObjects(selectedGroupIds, $"Group {DateTime.Now:HHmmss}");
            selectedGroupIds.Clear();
            selectedGroupIds.Add(group.Id);
            selectedGroupId = group.Id;
            lastLoadMessage = $"Grouped {group.Children.Count} objects";
            MarkRenderDirty();
            UpdateSelectionUi();
            helixViewport.SelectGroups(selectedGroupIds, scene);
            UpdateStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Group failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Ungroups the active recursive group so its children become selectable objects again.</summary>
    private void UngroupSelectedGroup()
    {
        if (selectedGroupId <= 0) return;

        try
        {
            CaptureUndoState();
            IReadOnlyList<SceneObjectGroup> children = scene.Ungroup(selectedGroupId);
            selectedGroupIds.Clear();
            foreach (SceneObjectGroup child in children)
                selectedGroupIds.Add(child.Id);
            selectedGroupId = selectedGroupIds.Count == 0 ? -1 : selectedGroupIds.Last();
            lastLoadMessage = $"Ungrouped into {children.Count} objects";
            MarkRenderDirty();
            UpdateSelectionUi();
            helixViewport.SelectGroups(selectedGroupIds, scene);
            UpdateStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Ungroup failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }


    /// <summary>Simplifies the selected object to a user-chosen percentage of its current triangles.</summary>
    private void SimplifySelectedGroup()
    {
        SceneObjectGroup? group = SelectedGroup;
        if (group == null) return;

        if (!double.TryParse(panel.SimplifyKeepPercentBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double keepPercent) ||
            keepPercent <= 0.0 || keepPercent >= 100.0 || !double.IsFinite(keepPercent))
        {
            MessageBox.Show(this, "Enter a keep percentage between 1 and 99. For example: 50 keeps about half the triangles; 10 keeps about one tenth.", "Invalid simplify amount", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        int before = group.CountLocalTrianglesRecursively();
        if (before <= 3)
            return;

        try
        {
            CaptureUndoState();
            int removed = group.SimplifyGeometry(keepPercent / 100.0);
            scene.RebuildWorldGeometry();
            int after = group.CountLocalTrianglesRecursively();
            lastLoadMessage = $"Simplified {group.Name}: {before} -> {after} triangles ({removed} removed)";
            MarkRenderDirty();
            UpdateSelectionUi();
            helixViewport.SelectGroups(selectedGroupIds, scene);
            UpdateStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Simplify failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Implements the change selected color operation for this file's subsystem.</summary>
    private void ChangeSelectedColor()
    {
        SceneObjectGroup? group = SelectedGroup;
        if (group == null) return;

        using ColorDialog dialog = new() { FullOpen = true, Color = Color.White };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        Vec3 color = new(dialog.Color.R / 255.0, dialog.Color.G / 255.0, dialog.Color.B / 255.0);
        CaptureUndoState();
        Material? existing = group.LocalTriangles.FirstOrDefault()?.Material;
        group.ApplyColor(new Material(color, existing?.Emission ?? 0.0, existing?.LightId));
        scene.RebuildWorldGeometry();
        MarkRenderDirty();
        UpdateSelectionUi();
        UpdateStatus();
    }


    /// <summary>Implements the assign selected bitmap texture operation for this file's subsystem.</summary>
    private void AssignSelectedBitmapTexture()
    {
        SceneObjectGroup? group = SelectedGroup;
        if (group == null) return;

        using OpenFileDialog dialog = new()
        {
            Title = "Assign bitmap texture to selected object",
            Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            CaptureUndoState();
            group.ApplyTexture(TextureMap.FromFile(dialog.FileName), ReadTextureTileSizeOrDefault(), forceBoxProjection: false);
            scene.RebuildWorldGeometry();
            MarkRenderDirty();
            UpdateSelectionUi();
            UpdateStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Texture load failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Implements the assign selected checker texture operation for this file's subsystem.</summary>
    private void AssignSelectedCheckerTexture()
    {
        SceneObjectGroup? group = SelectedGroup;
        if (group == null) return;

        CaptureUndoState();
        group.ApplyTexture(TextureMap.CreateChecker(), ReadTextureTileSizeOrDefault(), forceBoxProjection: true);
        scene.RebuildWorldGeometry();
        MarkRenderDirty();
        UpdateSelectionUi();
        UpdateStatus();
    }



    /// <summary>Reads the texture tile size from the UI; invalid values fall back to the default quarter scene unit tile.</summary>
    private double ReadTextureTileSizeOrDefault()
    {
        return double.TryParse(panel.TextureTileSizeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double tileWorldUnits) &&
            tileWorldUnits > 0.0 && double.IsFinite(tileWorldUnits)
            ? tileWorldUnits
            : 0.25;
    }

    /// <summary>Reprojects selected textured object UVs using scene-unit tiling instead of one texture across the whole object.</summary>
    private void RetileSelectedTexture()
    {
        SceneObjectGroup? group = SelectedGroup;
        if (group == null) return;

        if (!double.TryParse(panel.TextureTileSizeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double tileWorldUnits) ||
            tileWorldUnits <= 0.0 || !double.IsFinite(tileWorldUnits))
        {
            MessageBox.Show(this, "Enter a positive tile size such as 0.25. Smaller values create more texture repeats.", "Invalid tile size", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        bool hasTexture = group.SelfAndDescendants()
            .SelectMany(g => g.LocalTriangles)
            .Any(t => t.Material.Texture != null);
        if (!hasTexture)
            return;

        CaptureUndoState();
        group.RetileTexture(tileWorldUnits);
        scene.RebuildWorldGeometry();
        MarkRenderDirty();
        UpdateSelectionUi();
        UpdateStatus();
    }

    /// <summary>Clears selected texture and updates dependent UI/render state.</summary>
    private void ClearSelectedTexture()
    {
        SceneObjectGroup? group = SelectedGroup;
        if (group == null) return;

        CaptureUndoState();
        group.ClearTexture();
        scene.RebuildWorldGeometry();
        MarkRenderDirty();
        UpdateSelectionUi();
        UpdateStatus();
    }


    /// <summary>Updates the material library description label for the selected preset.</summary>
    private void UpdateSelectedMaterialPresetInfo()
    {
        if (panel.MaterialLibraryComboBox.SelectedItem is MaterialPreset preset)
        {
            Material material = preset.Material;
            panel.MaterialPresetInfoLabel.Text = $"{preset.Summary}  M {material.Metallic:0.##}, R {material.Roughness:0.##}, T {material.Transmission:0.##}, A {material.Alpha:0.##}, E {material.Emission:0.##}";
        }
        else
        {
            panel.MaterialPresetInfoLabel.Text = "Choose a preset to load color, metalness, roughness, alpha, transmission, and emission.";
        }
    }

    /// <summary>Applies the selected material library preset to the selected object while preserving imported texture maps where possible.</summary>
    private void ApplySelectedMaterialPreset()
    {
        SceneObjectGroup? group = SelectedGroup;
        if (group == null || panel.MaterialLibraryComboBox.SelectedItem is not MaterialPreset preset)
            return;

        Material presetMaterial = preset.Material;
        CaptureUndoState();
        group.ApplyMaterialProperties(material => new Material(
            presetMaterial.Color,
            presetMaterial.Emission,
            material.LightId,
            panel.MaterialUseBaseTextureBox.Checked ? material.Texture : null,
            presetMaterial.EmissionColor,
            panel.MaterialUseEmissiveTextureBox.Checked ? material.EmissiveTexture : null,
            presetMaterial.Alpha,
            presetMaterial.AlphaBlend,
            presetMaterial.Metallic,
            presetMaterial.Roughness,
            presetMaterial.Transmission,
            panel.MaterialUseMetallicRoughnessTextureBox.Checked ? material.MetallicRoughnessTexture : null,
            panel.MaterialUseNormalTextureBox.Checked ? material.NormalTexture : null,
            material.OcclusionTexture,
            material.NormalScale,
            material.OcclusionStrength,
            presetMaterial.AlphaMode,
            presetMaterial.AlphaCutoff,
            material.DoubleSided,
            material.TransmissionTexture,
            material.Ior,
            material.Thickness,
            material.AttenuationColor,
            material.AttenuationDistance,
            material.Clearcoat,
            material.ClearcoatRoughness,
            material.ClearcoatUsesTransmissionTexture));

        scene.RebuildWorldGeometry();
        MarkRenderDirty();
        UpdateSelectionUi();
        helixViewport.SelectGroups(selectedGroupIds, scene);
        UpdateStatus();
    }

    /// <summary>Applies numeric glTF/PBR material properties from the Selection tab to the selected object.</summary>
    private void ApplySelectedMaterialProperties()
    {
        SceneObjectGroup? group = SelectedGroup;
        if (group == null) return;

        if (!TryReadUnitInterval(panel.MaterialAlphaBox, "Alpha", out double alpha) ||
            !TryReadUnitInterval(panel.MaterialTransmissionBox, "Transmission", out double transmission) ||
            !TryReadUnitInterval(panel.MaterialMetallicBox, "Metallic", out double metallic) ||
            !TryReadUnitInterval(panel.MaterialRoughnessBox, "Roughness", out double roughness) ||
            !TryReadNonNegative(panel.MaterialEmissionBox, "Emission", out double emission) ||
            !TryReadUnitInterval(panel.MaterialEmissionRBox, "Emission R", out double emissionR) ||
            !TryReadUnitInterval(panel.MaterialEmissionGBox, "Emission G", out double emissionG) ||
            !TryReadUnitInterval(panel.MaterialEmissionBBox, "Emission B", out double emissionB))
        {
            return;
        }

        bool alphaBlend = panel.MaterialAlphaBlendBox.Checked;
        bool keepBaseTexture = panel.MaterialUseBaseTextureBox.Checked;
        bool keepEmissiveTexture = panel.MaterialUseEmissiveTextureBox.Checked;
        bool keepMetallicRoughnessTexture = panel.MaterialUseMetallicRoughnessTextureBox.Checked;
        bool keepNormalTexture = panel.MaterialUseNormalTextureBox.Checked;
        Vec3 emissionColor = new(emissionR, emissionG, emissionB);

        CaptureUndoState();
        group.ApplyMaterialProperties(material => new Material(
            material.Color,
            emission,
            material.LightId,
            keepBaseTexture ? material.Texture : null,
            emissionColor,
            keepEmissiveTexture ? material.EmissiveTexture : null,
            alpha,
            alphaBlend,
            metallic,
            roughness,
            transmission,
            keepMetallicRoughnessTexture ? material.MetallicRoughnessTexture : null,
            keepNormalTexture ? material.NormalTexture : null,
            material.OcclusionTexture,
            material.NormalScale,
            material.OcclusionStrength,
            alphaBlend ? MaterialAlphaMode.Blend : material.AlphaMode == MaterialAlphaMode.Mask ? MaterialAlphaMode.Mask : MaterialAlphaMode.Opaque,
            material.AlphaCutoff,
            material.DoubleSided,
            material.TransmissionTexture,
            material.Ior,
            material.Thickness,
            material.AttenuationColor,
            material.AttenuationDistance,
            material.Clearcoat,
            material.ClearcoatRoughness,
            material.ClearcoatUsesTransmissionTexture));

        scene.RebuildWorldGeometry();
        MarkRenderDirty();
        UpdateSelectionUi();
        helixViewport.SelectGroups(selectedGroupIds, scene);
        UpdateStatus();
    }

    /// <summary>Reads a floating point value constrained to [0, 1], showing a friendly validation message if invalid.</summary>
    private bool TryReadUnitInterval(TextBox box, string label, out double value)
    {
        if (double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
            double.IsFinite(value) && value >= 0.0 && value <= 1.0)
            return true;

        MessageBox.Show(this, $"{label} must be a number from 0 to 1.", "Invalid material value", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        value = 0.0;
        return false;
    }

    /// <summary>Reads a non-negative floating point value, showing a friendly validation message if invalid.</summary>
    private bool TryReadNonNegative(TextBox box, string label, out double value)
    {
        if (double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
            double.IsFinite(value) && value >= 0.0)
            return true;

        MessageBox.Show(this, $"{label} must be a non-negative number.", "Invalid material value", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        value = 0.0;
        return false;
    }

    /// <summary>Updates the glTF/PBR material controls to match the selected object's first material.</summary>
    private void UpdateMaterialControls(SceneObjectGroup? group, bool enabled)
    {
        Material? material = enabled ? group?.FirstMaterialOrDefault() : null;
        if (material == null)
        {
            panel.MaterialInfoLabel.Text = "Material: none";
            panel.MaterialAlphaBox.Text = "1";
            panel.MaterialTransmissionBox.Text = "0";
            panel.MaterialMetallicBox.Text = "0";
            panel.MaterialRoughnessBox.Text = "0.72";
            panel.MaterialEmissionBox.Text = "0";
            panel.MaterialEmissionRBox.Text = "1";
            panel.MaterialEmissionGBox.Text = "1";
            panel.MaterialEmissionBBox.Text = "1";
            panel.MaterialAlphaBlendBox.Checked = false;
            panel.MaterialUseBaseTextureBox.Checked = false;
            panel.MaterialUseEmissiveTextureBox.Checked = false;
            panel.MaterialUseMetallicRoughnessTextureBox.Checked = false;
            panel.MaterialUseNormalTextureBox.Checked = false;
        }
        else
        {
            panel.MaterialInfoLabel.Text = $"Material: alpha {material.Alpha:0.###}, transmission {material.Transmission:0.###}, metallic {material.Metallic:0.###}, roughness {material.Roughness:0.###}";
            panel.MaterialAlphaBox.Text = material.Alpha.ToString("0.###", CultureInfo.InvariantCulture);
            panel.MaterialTransmissionBox.Text = material.Transmission.ToString("0.###", CultureInfo.InvariantCulture);
            panel.MaterialMetallicBox.Text = material.Metallic.ToString("0.###", CultureInfo.InvariantCulture);
            panel.MaterialRoughnessBox.Text = material.Roughness.ToString("0.###", CultureInfo.InvariantCulture);
            panel.MaterialEmissionBox.Text = material.Emission.ToString("0.###", CultureInfo.InvariantCulture);
            panel.MaterialEmissionRBox.Text = material.EmissionColor.X.ToString("0.###", CultureInfo.InvariantCulture);
            panel.MaterialEmissionGBox.Text = material.EmissionColor.Y.ToString("0.###", CultureInfo.InvariantCulture);
            panel.MaterialEmissionBBox.Text = material.EmissionColor.Z.ToString("0.###", CultureInfo.InvariantCulture);
            panel.MaterialAlphaBlendBox.Checked = material.AlphaBlend;
            panel.MaterialUseBaseTextureBox.Checked = material.Texture != null;
            panel.MaterialUseEmissiveTextureBox.Checked = material.EmissiveTexture != null;
            panel.MaterialUseMetallicRoughnessTextureBox.Checked = material.MetallicRoughnessTexture != null;
            panel.MaterialUseNormalTextureBox.Checked = material.NormalTexture != null;
        }

        panel.MaterialAlphaBox.Enabled = enabled;
        panel.MaterialTransmissionBox.Enabled = enabled;
        panel.MaterialMetallicBox.Enabled = enabled;
        panel.MaterialRoughnessBox.Enabled = enabled;
        panel.MaterialEmissionBox.Enabled = enabled;
        panel.MaterialEmissionRBox.Enabled = enabled;
        panel.MaterialEmissionGBox.Enabled = enabled;
        panel.MaterialEmissionBBox.Enabled = enabled;
        panel.MaterialAlphaBlendBox.Enabled = enabled;
        panel.MaterialUseBaseTextureBox.Enabled = enabled && material?.Texture != null;
        panel.MaterialUseEmissiveTextureBox.Enabled = enabled && material?.EmissiveTexture != null;
        panel.MaterialUseMetallicRoughnessTextureBox.Enabled = enabled && material?.MetallicRoughnessTexture != null;
        panel.MaterialUseNormalTextureBox.Enabled = enabled && material?.NormalTexture != null;
        panel.ApplyMaterialPropertiesButton.Enabled = enabled;
        panel.MaterialLibraryComboBox.Enabled = enabled;
        panel.ApplyMaterialPresetButton.Enabled = enabled && panel.MaterialLibraryComboBox.SelectedItem is MaterialPreset;
    }

    /// <summary>Updates selection ui from the current application state.</summary>
    private void UpdateSelectionUi()
    {
        SceneObjectGroup? group = SelectedGroup;
        bool hasSelection = selectedGroupIds.Count > 0;
        bool singleEnabled = group != null && selectedGroupIds.Count <= 1;

        if (!hasSelection || group == null)
        {
            panel.SelectionLabel.Text = "Selection: none";
            panel.TextureInfoLabel.Text = "Texture: none";
            ReplaceTexturePreviewImage(null);
            UpdateMaterialControls(null, enabled: false);
        }
        else if (selectedGroupIds.Count > 1)
        {
            panel.SelectionLabel.Text = $"Selection: {selectedGroupIds.Count} objects selected. Use Group to make a recursive group.";
            panel.TextureInfoLabel.Text = "Texture: multiple selection";
            ReplaceTexturePreviewImage(null);
            UpdateMaterialControls(group, enabled: false);
        }
        else
        {
            Aabb bounds = group.GetWorldBounds(includeHidden: true);
            Vec3 center = (bounds.Min + bounds.Max) * 0.5;
            string childInfo = group.Children.Count > 0 ? $"  Children: {group.Children.Count}" : string.Empty;
            int localTriangleCount = group.CountLocalTrianglesRecursively();
            panel.SelectionLabel.Text = $"Selection: {group.Name}  C({center.X:0.##},{center.Y:0.##},{center.Z:0.##})  Tris: {localTriangleCount}{childInfo}";

            TextureMap? texture = group.SelfAndDescendants()
                .SelectMany(g => g.LocalTriangles)
                .Select(t => t.Material.Texture)
                .FirstOrDefault(t => t != null);
            if (texture == null)
            {
                panel.TextureInfoLabel.Text = "Texture: none";
                ReplaceTexturePreviewImage(null);
            }
            else
            {
                panel.TextureInfoLabel.Text = $"Texture: {texture.Name} ({texture.Width}x{texture.Height})";
                ReplaceTexturePreviewImage(texture.CreatePreviewBitmap());
            }

            UpdateMaterialControls(group, enabled: singleEnabled);
        }

        panel.ApplyMoveButton.Enabled = singleEnabled;
        panel.ApplyRotateButton.Enabled = singleEnabled;
        panel.ApplyScaleButton.Enabled = singleEnabled;
        panel.DeleteSelectionButton.Enabled = hasSelection;
        panel.ShowSelectedObjectsButton.Enabled = hasSelection;
        panel.HideSelectedObjectsButton.Enabled = hasSelection;
        panel.DuplicateSelectionButton.Enabled = singleEnabled;
        panel.GroupSelectionButton.Enabled = selectedGroupIds.Count >= 2;
        panel.UngroupSelectionButton.Enabled = selectedGroupIds.Count == 1 && group != null && scene.CanUngroup(group.Id);
        panel.ColorSelectionButton.Enabled = singleEnabled;
        panel.TextureSelectionButton.Enabled = singleEnabled;
        panel.SampleTextureSelectionButton.Enabled = singleEnabled;
        panel.ClearTextureSelectionButton.Enabled = singleEnabled;
        panel.RetileTextureButton.Enabled = singleEnabled;
        panel.TextureTileSizeBox.Enabled = singleEnabled;
        panel.SimplifySelectionButton.Enabled = singleEnabled && group != null && group.CountLocalTrianglesRecursively() > 3;
        panel.SimplifyKeepPercentBox.Enabled = singleEnabled && group != null && group.CountLocalTrianglesRecursively() > 3;
        panel.ConvertSelectionToLightButton.Enabled = hasSelection;
        RefreshEditorViewerSelection();
    }

    /// <summary>Implements the replace texture preview image operation for this file's subsystem.</summary>
    private void ReplaceTexturePreviewImage(Image? image)
    {
        Image? old = panel.TexturePreviewBox.Image;
        panel.TexturePreviewBox.Image = image;
        if (old != null && !ReferenceEquals(old, image))
            SafeDisposeImage(old);
    }

    /// <summary>Draws simple raytrace-preview outlines around all selected objects.</summary>
    private void DrawSelectionOverlay(Graphics graphics)
    {
        foreach (int id in selectedGroupIds)
        {
            SceneObjectGroup? group = scene.GroupById(id);
            if (group == null) continue;
            DrawGroupBounds(graphics, group);
        }
    }

    private void DrawGroupBounds(Graphics graphics, SceneObjectGroup group)
    {
        Aabb b = group.GetWorldBounds(includeHidden: true);
        Vec3 min = b.Min, max = b.Max;
        Vec3[] corners =
        {
            new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z), new(max.X, max.Y, min.Z), new(min.X, max.Y, min.Z),
            new(min.X, min.Y, max.Z), new(max.X, min.Y, max.Z), new(max.X, max.Y, max.Z), new(min.X, max.Y, max.Z)
        };

        PointF?[] p = corners.Select(ProjectToScreen).ToArray();
        using Pen pen = new(Color.Gold, 2.0f);
        DrawEdge(graphics, pen, p, 0, 1); DrawEdge(graphics, pen, p, 1, 2); DrawEdge(graphics, pen, p, 2, 3); DrawEdge(graphics, pen, p, 3, 0);
        DrawEdge(graphics, pen, p, 4, 5); DrawEdge(graphics, pen, p, 5, 6); DrawEdge(graphics, pen, p, 6, 7); DrawEdge(graphics, pen, p, 7, 4);
        DrawEdge(graphics, pen, p, 0, 4); DrawEdge(graphics, pen, p, 1, 5); DrawEdge(graphics, pen, p, 2, 6); DrawEdge(graphics, pen, p, 3, 7);
    }

    /// <summary>Implements the project to screen operation for this file's subsystem.</summary>
    private PointF? ProjectToScreen(Vec3 point)
    {
        CameraBasis basis = camera.GetBasis();
        Vec3 rel = point - camera.Position;
        double z = rel.Dot(basis.Forward);
        if (z <= 0.01) return null;

        double x = rel.Dot(basis.Right);
        double y = rel.Dot(basis.Up);
        double fov = Math.Tan((72.0 * Math.PI / 180.0) / 2.0);
        double aspect = ClientSize.Width / (double)Math.Max(1, ClientSize.Height);
        float sx = (float)((x / (z * fov * aspect) + 1.0) * 0.5 * ClientSize.Width);
        float sy = (float)((1.0 - y / (z * fov)) * 0.5 * ClientSize.Height);
        return new PointF(sx, sy);
    }

    /// <summary>Implements the draw edge operation for this file's subsystem.</summary>
    private static void DrawEdge(Graphics graphics, Pen pen, PointF?[] points, int a, int b)
    {
        if (points[a].HasValue && points[b].HasValue)
            graphics.DrawLine(pen, points[a]!.Value, points[b]!.Value);
    }


    /// <summary>Resets move inputs to a safe default value.</summary>
    private void ResetMoveInputs()
    {
        panel.MoveXBox.Text = "0";
        panel.MoveYBox.Text = "0";
        panel.MoveZBox.Text = "0";
    }

    /// <summary>Implements the is finite operation for this file's subsystem.</summary>
    private static bool IsFinite(Vec3 value) =>
        double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);

    /// <summary>Reads vector from user input or serialized data.</summary>
    private static Vec3 ReadVector(TextBox x, TextBox y, TextBox z, Vec3 fallback) => new(
        ReadDouble(x.Text, fallback.X),
        ReadDouble(y.Text, fallback.Y),
        ReadDouble(z.Text, fallback.Z));

    /// <summary>Reads double from user input or serialized data.</summary>
    private static double ReadDouble(string text, double fallback) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : fallback;

    /// <summary>Implements the clamp scale operation for this file's subsystem.</summary>
    private static double ClampScale(double value) => Math.Clamp(value, 0.02, 50.0);
}
