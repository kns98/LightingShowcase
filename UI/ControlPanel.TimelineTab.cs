// -----------------------------------------------------------------------------
// File: UI/ControlPanel.TimelineTab.cs
// Purpose: Timeline tab construction for ControlPanel.
// -----------------------------------------------------------------------------

namespace LightingShowcase.UI;

public sealed partial class ControlPanel
{
/// <summary>Timeline tab: keyframe list and camera key editing fields.</summary>
private void AddTimelineTab(Control host)
{
    int top = 14;
    AddHeading(host, "Camera timeline", top);
    top += 30;
    TimelineListBox.Left = 14;
    TimelineListBox.Top = top;
    TimelineListBox.Width = 450;
    TimelineListBox.Height = 150;
    host.Controls.Add(TimelineListBox);

    top += 166;
    AddLabel(host, "Time 0-1", 14, top + 6, 72, 22);
    AddTextBox(host, TimelineTimeBox, "0", 94, top);
    AddButton(host, TimelinePreviewButton, "Preview", 166, top - 3, 92);
    AddButton(host, TimelineAddCurrentButton, "Add Current", 266, top - 3, 126);
    AddButton(host, TimelineDeleteButton, "Delete", 402, top - 3, 64);

    top += 42;
    AddVectorRow(host, "Pos", TimelinePosXBox, TimelinePosYBox, TimelinePosZBox, TimelineApplyButton, "Apply", top);
    top += 40;
    AddVectorRow(host, "Target", TimelineTargetXBox, TimelineTargetYBox, TimelineTargetZBox, new Button(), string.Empty, top, includeButton: false);
    AddDescription(host, "Select a key, edit time/position/target, then Apply. Add Current stores the present Helix camera.", 14, top + 42, 450, 52);
}

}
