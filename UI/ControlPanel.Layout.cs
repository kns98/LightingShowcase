// -----------------------------------------------------------------------------
// File: UI/ControlPanel.Layout.cs
// Purpose: Top-level tab host layout for ControlPanel.
// -----------------------------------------------------------------------------

namespace LightingShowcase.UI;

public sealed partial class ControlPanel
{
    /// <summary>Adds the fixed title area above the tabs.</summary>
    private void AddTitle()
    {
        Controls.Add(new Label
        {
            Text = "Lighting showcase",
            ForeColor = Color.White,
            Font = new Font(Font.FontFamily, 12, FontStyle.Bold),
            Left = 14,
            Top = 12,
            Width = 470,
            Height = 24
        });
    }

    /// <summary>Adds brief usage help above the tabs.</summary>
    private void AddHelp()
    {
        Controls.Add(new Label
        {
            Text = "Left: Helix editor. Right: raytraced preview follows the Helix camera." + Environment.NewLine +
                   "Use navigation tools when mouse-button or trackpad combos are awkward.",
            ForeColor = Color.Gainsboro,
            Left = 14,
            Top = 42,
            Width = 488,
            Height = 46
        });
    }

    /// <summary>Creates all tabs and places each feature group in its own page.</summary>
    private void AddTabs()
    {
        Tabs.Left = 10;
        Tabs.Top = 96;
        Tabs.Width = 500;
        Tabs.Height = Math.Max(300, Height - 106);
        Tabs.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        Tabs.Appearance = TabAppearance.Normal;

        TabPage renderTab = CreateTab("Render");
        TabPage sceneTab = CreateTab("Scene");
        TabPage navigateTab = CreateTab("Navigate");
        TabPage selectionTab = CreateTab("Selection");
        TabPage timelineTab = CreateTab("Timeline");
        TabPage statusTab = CreateTab("Status");

        // Render is intentionally first because it now owns the cinema playback
        // controls and the raytrace controls that are used most during preview.
        Tabs.TabPages.Add(renderTab);
        Tabs.TabPages.Add(sceneTab);
        Tabs.TabPages.Add(navigateTab);
        Tabs.TabPages.Add(selectionTab);
        Tabs.TabPages.Add(timelineTab);
        Tabs.TabPages.Add(statusTab);
        Controls.Add(Tabs);

        AddRenderTab(renderTab);
        AddSceneTab(sceneTab);
        AddNavigateTab(navigateTab);
        AddSelectionTab(selectionTab);
        AddTimelineTab(timelineTab);
        AddStatusTab(statusTab);
    }

    /// <summary>Creates a scrollable tab page with the same dark theme as the panel.</summary>
    private TabPage CreateTab(string title)
    {
        return new TabPage(title)
        {
            BackColor = Color.FromArgb(36, 36, 42),
            ForeColor = Color.White,
            AutoScroll = true,
            Padding = new Padding(8)
        };
    }
}
