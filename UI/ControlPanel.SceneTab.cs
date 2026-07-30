// -----------------------------------------------------------------------------
// File: UI/ControlPanel.SceneTab.cs
// Purpose: Scene/file tab construction for ControlPanel.
// -----------------------------------------------------------------------------

namespace LightingShowcase.UI;

public sealed partial class ControlPanel
{
    /// <summary>Scene tab: file operations, built-in object insertion, and undo/redo.</summary>
    private void AddSceneTab(Control host)
    {
        int top = 14;
        AddHeading(host, "Scene files", top);
        top += 30;
        AddButton(host, OpenFileButton, "Open File", 14, top, 132);
        AddButton(host, InsertObjButton, "Insert Object", 156, top, 148);
        AddButton(host, SaveSceneButton, "Export Scene", 314, top, 150);

        top += 44;
        AddLabel(host, "Simplified open keep %", 14, top + 5, 150, 22);
        OpenSimplifiedKeepPercentBox.Left = 170;
        OpenSimplifiedKeepPercentBox.Top = top;
        OpenSimplifiedKeepPercentBox.Width = 58;
        OpenSimplifiedKeepPercentBox.Height = TextBoxHeight;
        OpenSimplifiedKeepPercentBox.Text = "35";
        host.Controls.Add(OpenSimplifiedKeepPercentBox);
        AddButton(host, OpenSimplifiedButton, "Open Simplified", 240, top - 4, 180);

        top += 50;
        AddButton(host, ClearSceneButton, "Clear Scene", 14, top, 132);
        AddButton(host, UndoButton, "Undo", 156, top, 96);
        AddButton(host, RedoButton, "Redo", 262, top, 96);
        AddDescription(host, "Shortcuts: Ctrl+O open, Ctrl+S save, Ctrl+Z undo, Ctrl+Y redo.", 14, top + 42, 450, 36);

        top += 94;
        AddHeading(host, "Ready-made objects", top);
        top += 32;
        AddLabel(host, "Object", 14, top + 7, 72, 24);
        ReadyMadeObjectComboBox.Left = 86;
        ReadyMadeObjectComboBox.Top = top;
        ReadyMadeObjectComboBox.Width = 250;
        ReadyMadeObjectComboBox.Height = 30;
        ReadyMadeObjectComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        host.Controls.Add(ReadyMadeObjectComboBox);
        AddButton(host, InsertReadyMadeButton, "Insert", 348, top - 3, 96);

        top += 56;
        AddHeading(host, "View actions", top);
        top += 32;
        AddButton(host, RealtimeViewButton, "Focus Edit", 14, top, 132);
        AddDescription(host, "Focus Edit frames the selected object or full scene. Raytraced rendering controls are in the Render tab.", 14, top + 42, 450, 44);

        top += 96;
        AddHeading(host, "Scene objects", top);
        top += 30;
        AddDescription(host, "Check boxes control visibility. Select one or more rows to edit, frame, hide, or show objects.", 14, top, 450, 38);
        top += 44;

        ObjectListView.Left = 14;
        ObjectListView.Top = top;
        ObjectListView.Width = 450;
        ObjectListView.Height = 210;
        ObjectListView.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        ObjectListView.View = View.Details;
        ObjectListView.CheckBoxes = true;
        ObjectListView.FullRowSelect = true;
        ObjectListView.HideSelection = false;
        ObjectListView.MultiSelect = true;
        ObjectListView.LabelEdit = true;
        ObjectListView.GridLines = false;
        ObjectListView.Columns.Add("Visible / object", 280);
        ObjectListView.Columns.Add("Triangles", 84, HorizontalAlignment.Right);
        ObjectListView.Columns.Add("Type", 72);
        host.Controls.Add(ObjectListView);

        top += 224;
        AddButton(host, ShowSelectedObjectsButton, "Show Selected", 14, top, 132);
        AddButton(host, HideSelectedObjectsButton, "Hide Selected", 156, top, 132);
        AddButton(host, ShowAllObjectsButton, "Show All", 298, top, 132);
    }
}
