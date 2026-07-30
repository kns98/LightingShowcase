// -----------------------------------------------------------------------------
// File: LightingShowcaseForm.TimelineUi.cs
// Purpose: Camera timeline editing.
//
// Maintains the list of camera keyframes and maps timeline UI controls to DemoCameraPath data.
// This comment is intentionally kept in source code so future maintainers can
// understand the role of this file without opening external documentation.
// -----------------------------------------------------------------------------

using LightingShowcase.CameraSystem;
using LightingShowcase.Math3D;

namespace LightingShowcase;

public sealed partial class LightingShowcaseForm
{
    /// <summary>Compatibility hook retained because startup calls it after building controls.</summary>
    private void SyncLightControls()
    {
        // Default lights are normal SceneLight entries now. There are no hidden
        // per-id brightness multipliers to synchronize.
    }

    /// <summary>Implements the refresh timeline list operation for this file's subsystem.</summary>
    private void RefreshTimelineList(bool selectFirst = false)
    {
        int previousSelection = panel.TimelineListBox.SelectedIndex;
        panel.TimelineListBox.BeginUpdate();
        panel.TimelineListBox.Items.Clear();
        IReadOnlyList<CameraKey> keys = demoPath.Keys;
        for (int i = 0; i < keys.Count; i++)
        {
            CameraKey key = keys[i];
            panel.TimelineListBox.Items.Add($"{i + 1:00}  t={key.Time:0.000}  pos=({key.Position.X:0.##}, {key.Position.Y:0.##}, {key.Position.Z:0.##})  target=({key.Target.X:0.##}, {key.Target.Y:0.##}, {key.Target.Z:0.##})");
        }
        panel.TimelineListBox.EndUpdate();

        if (panel.TimelineListBox.Items.Count == 0)
            selectedTimelineIndex = -1;
        else if (selectFirst)
            selectedTimelineIndex = 0;
        else
            selectedTimelineIndex = Math.Max(0, Math.Min(previousSelection, panel.TimelineListBox.Items.Count - 1));

        panel.TimelineListBox.SelectedIndex = selectedTimelineIndex;
        LoadTimelineKeyIntoEditor(selectedTimelineIndex);
    }

    /// <summary>Implements the select timeline key operation for this file's subsystem.</summary>
    private void SelectTimelineKey(int index)
    {
        selectedTimelineIndex = index;
        LoadTimelineKeyIntoEditor(index);
    }

    /// <summary>Implements the load timeline key into editor operation for this file's subsystem.</summary>
    private void LoadTimelineKeyIntoEditor(int index)
    {
        if (index < 0 || index >= demoPath.Keys.Count)
            return;

        CameraKey key = demoPath.Keys[index];
        panel.TimelineTimeBox.Text = FormatCoord(key.Time);
        panel.TimelinePosXBox.Text = FormatCoord(key.Position.X);
        panel.TimelinePosYBox.Text = FormatCoord(key.Position.Y);
        panel.TimelinePosZBox.Text = FormatCoord(key.Position.Z);
        panel.TimelineTargetXBox.Text = FormatCoord(key.Target.X);
        panel.TimelineTargetYBox.Text = FormatCoord(key.Target.Y);
        panel.TimelineTargetZBox.Text = FormatCoord(key.Target.Z);
    }

    /// <summary>Applies timeline key edit to the active scene/editor state.</summary>
    private void ApplyTimelineKeyEdit()
    {
        if (selectedTimelineIndex < 0 || selectedTimelineIndex >= demoPath.Keys.Count)
            return;

        CameraKey previous = demoPath.Keys[selectedTimelineIndex];
        double time = ReadDouble(panel.TimelineTimeBox.Text, previous.Time);
        Vec3 position = ReadVector(panel.TimelinePosXBox, panel.TimelinePosYBox, panel.TimelinePosZBox, previous.Position);
        Vec3 target = ReadVector(panel.TimelineTargetXBox, panel.TimelineTargetYBox, panel.TimelineTargetZBox, previous.Target);
        demoPath.UpdateKey(selectedTimelineIndex, new CameraKey(time, position, target));
        RefreshTimelineList();
        demoPlaying = false;
        useDemoCamera = true;
        SetDemoCamera();
        MarkRaytraceDirty();
        UpdateStatus();
    }

    /// <summary>Adds or creates current camera to timeline for this subsystem.</summary>
    private void AddCurrentCameraToTimeline()
    {
        double time = ReadDouble(panel.TimelineTimeBox.Text, demoTime / DemoDuration);
        CameraBasis basis = camera.GetBasis();
        Vec3 position = camera.Position;
        Vec3 target = position + basis.Forward * 4.0;
        int newIndex = demoPath.AddKey(new CameraKey(time, position, target));
        RefreshTimelineList();
        if (newIndex >= 0 && newIndex < panel.TimelineListBox.Items.Count)
            panel.TimelineListBox.SelectedIndex = newIndex;
        lastLoadMessage = "Added current Helix camera to timeline";
        UpdateStatus();
    }

    /// <summary>Implements the delete selected timeline key operation for this file's subsystem.</summary>
    private void DeleteSelectedTimelineKey()
    {
        if (selectedTimelineIndex < 0 || selectedTimelineIndex >= demoPath.Keys.Count)
            return;
        demoPath.RemoveKey(selectedTimelineIndex);
        selectedTimelineIndex = Math.Min(selectedTimelineIndex, demoPath.Keys.Count - 1);
        RefreshTimelineList();
        lastLoadMessage = "Deleted selected timeline camera key";
        UpdateStatus();
    }

    /// <summary>Implements the preview selected timeline key operation for this file's subsystem.</summary>
    private void PreviewSelectedTimelineKey()
    {
        if (selectedTimelineIndex < 0 || selectedTimelineIndex >= demoPath.Keys.Count)
            return;

        CameraKey key = demoPath.Keys[selectedTimelineIndex];
        demoTime = key.Time * DemoDuration;
        camera.SetLookAt(key.Position, key.Target);
        useDemoCamera = true;
        demoPlaying = false;
        helixViewport.UpdateCamera(camera.Position, camera.GetBasis());
        MarkRaytraceDirty();
        UpdateStatus();
    }
}
