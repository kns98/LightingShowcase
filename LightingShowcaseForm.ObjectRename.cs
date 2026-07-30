// -----------------------------------------------------------------------------
// File: LightingShowcaseForm.ObjectRename.cs
// Purpose: Editable object names from the Scene objects list.
// -----------------------------------------------------------------------------

namespace LightingShowcase;

public sealed partial class LightingShowcaseForm
{
    /// <summary>Applies an in-place ListView label edit to the backing scene object name.</summary>
    private void RenameObjectFromListEdit(LabelEditEventArgs e)
    {
        if (e.Item < 0 || e.Item >= panel.ObjectListView.Items.Count)
            return;

        string? proposed = e.Label;
        if (proposed == null)
            return;

        proposed = proposed.Trim();
        if (string.IsNullOrWhiteSpace(proposed))
        {
            e.CancelEdit = true;
            return;
        }

        ListViewItem item = panel.ObjectListView.Items[e.Item];
        if (item.Tag is not int id)
            return;

        SceneGraph.SceneObjectGroup? group = sceneDocument.FindObject(id);
        if (group == null)
            return;

        if (string.Equals(group.Name, proposed, StringComparison.Ordinal))
            return;

        CaptureUndoState();
        sceneDocument.RenameObject(id, proposed);
        lastLoadMessage = $"Renamed object to {proposed}";
        RefreshEditorMeshList();
        UpdateSelectionUi();
        UpdateStatus();
    }
}
