// -----------------------------------------------------------------------------
// File: UI/ControlPanel.StatusTab.cs
// Purpose: Status/progress tab construction for ControlPanel.
// -----------------------------------------------------------------------------

namespace LightingShowcase.UI;

public sealed partial class ControlPanel
{
/// <summary>Status tab: long operation progress and detailed status text.</summary>
private void AddStatusTab(Control host)
{
    int top = 14;
    AddHeading(host, "Loading progress", top);
    top += 32;
    LoadingLabel.Text = "Ready";
    LoadingLabel.ForeColor = Color.Gainsboro;
    LoadingLabel.Left = 14;
    LoadingLabel.Top = top;
    LoadingLabel.Width = 450;
    LoadingLabel.Height = 24;
    LoadingLabel.Visible = false;
    host.Controls.Add(LoadingLabel);

    LoadingProgressBar.Left = 14;
    LoadingProgressBar.Top = top + 30;
    LoadingProgressBar.Width = 450;
    LoadingProgressBar.Height = 22;
    LoadingProgressBar.Minimum = 0;
    LoadingProgressBar.Maximum = 100;
    LoadingProgressBar.Value = 0;
    LoadingProgressBar.Visible = false;
    host.Controls.Add(LoadingProgressBar);

    top += 78;
    AddHeading(host, "Status", top);
    top += 30;
    StatusLabel.Left = 14;
    StatusLabel.Top = top;
    StatusLabel.Width = 450;
    StatusLabel.Height = 190;
    StatusLabel.ForeColor = Color.White;
    StatusLabel.BackColor = Color.FromArgb(115, 0, 0, 0);
    StatusLabel.BorderStyle = BorderStyle.FixedSingle;
    StatusLabel.Padding = new Padding(8);
    StatusLabel.Text = "Ready.";
    host.Controls.Add(StatusLabel);
}

}
