// -----------------------------------------------------------------------------
// File: UI/ControlPanel.NavigateTab.cs
// Purpose: Viewport navigation tab construction for ControlPanel.
// -----------------------------------------------------------------------------

namespace LightingShowcase.UI;

public sealed partial class ControlPanel
{
    /// <summary>Navigate tab: explicit viewport tools, standard views, and sensitivity tuning.</summary>
    private void AddNavigateTab(Control host)
    {
        int top = 14;
        AddHeading(host, "Viewport tool", top);
        top += 30;
        AddButton(host, SelectNavigationButton, "Select/Edit", 14, top, 132);
        AddButton(host, OrbitNavigationButton, "Orbit", 156, top, 112);
        AddButton(host, PanNavigationButton, "Pan", 280, top, 112);
        AddDescription(host, "The active tool controls what left-drag does in the Helix viewport.", 14, top + 42, 450, 34);

        top += 84;
        AddHeading(host, "Navigation feel", top);
        top += 32;
        AddNavigationSensitivitySlider(host, "Orbit", OrbitSensitivityTrackBar, OrbitSensitivityLabel, top, 125);
        top += 46;
        AddNavigationSensitivitySlider(host, "Pan", PanSensitivityTrackBar, PanSensitivityLabel, top, 125);
        top += 46;
        AddNavigationSensitivitySlider(host, "Zoom", ZoomSensitivityTrackBar, ZoomSensitivityLabel, top, 150);
        AddDescription(host, "Raise these when large scenes or trackpads feel too slow. The values apply immediately to mouse, trackpad, wheel, and keyboard navigation.", 14, top + 42, 450, 50);

        top += 104;
        AddHeading(host, "Standard views", top);
        top += 30;
        AddButton(host, FrontViewButton, "Front", 14, top, 96);
        AddButton(host, RightViewButton, "Right", 122, top, 96);
        AddButton(host, TopViewButton, "Top", 230, top, 96);
        AddButton(host, ResetViewButton, "Reset", 338, top, 96);
        AddDescription(host, "Shortcuts: F frames selection, Ctrl+R resets, 1/3/7 switch front/right/top.", 14, top + 42, 450, 38);

        top += 88;
        TrackpadNavigationBox.Text = "Trackpad navigation";
        TrackpadNavigationBox.ForeColor = Color.Gainsboro;
        TrackpadNavigationBox.Left = 14;
        TrackpadNavigationBox.Top = top;
        TrackpadNavigationBox.Width = 220;
        TrackpadNavigationBox.Height = 24;
        host.Controls.Add(TrackpadNavigationBox);
        AddDescription(host, "Trackpad mode keeps gentle two-finger scroll zoom and Shift/Ctrl drag behavior available.", 34, top + 30, 420, 44);
    }

    /// <summary>Adds one navigation sensitivity slider row.</summary>
    private void AddNavigationSensitivitySlider(Control host, string name, TrackBar slider, Label valueLabel, int top, int defaultPercent)
    {
        AddLabel(host, name, 14, top + 4, 62, 22);
        slider.Left = 82;
        slider.Top = top;
        slider.Width = 290;
        slider.Height = 34;
        slider.Minimum = 25;
        slider.Maximum = 300;
        slider.TickFrequency = 25;
        slider.SmallChange = 5;
        slider.LargeChange = 25;
        slider.Value = Math.Clamp(defaultPercent, slider.Minimum, slider.Maximum);
        host.Controls.Add(slider);

        valueLabel.Left = 384;
        valueLabel.Top = top + 4;
        valueLabel.Width = 80;
        valueLabel.Height = 22;
        valueLabel.ForeColor = Color.White;
        valueLabel.Text = slider.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";
        host.Controls.Add(valueLabel);
    }
}
