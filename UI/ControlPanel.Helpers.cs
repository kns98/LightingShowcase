// -----------------------------------------------------------------------------
// File: UI/ControlPanel.Helpers.cs
// Purpose: Shared WinForms helper methods for ControlPanel tab builders.
// -----------------------------------------------------------------------------

namespace LightingShowcase.UI;

public sealed partial class ControlPanel
{
/// <summary>Adds a section heading to a tab page.</summary>
private void AddHeading(Control host, string text, int top)
{
    host.Controls.Add(new Label
    {
        Text = text,
        ForeColor = Color.White,
        Left = 14,
        Top = top,
        Width = 450,
        Height = 22,
        Font = HeadingFont
    });
}

/// <summary>Adds a normal themed label to a tab page.</summary>
private void AddLabel(Control host, string text, int left, int top, int width, int height)
{
    host.Controls.Add(new Label
    {
        Text = text,
        ForeColor = Color.Gainsboro,
        Left = left,
        Top = top,
        Width = width,
        Height = height
    });
}

/// <summary>Adds muted help text to a tab page.</summary>
private void AddDescription(Control host, string text, int left, int top, int width, int height)
{
    host.Controls.Add(new Label
    {
        Text = text,
        ForeColor = Color.Gainsboro,
        Left = left,
        Top = top,
        Width = width,
        Height = height
    });
}

/// <summary>Adds a brightness slider row with a live value label.</summary>
private void AddLightSlider(Control host, string name, TrackBar slider, Label valueLabel, int top)
{
    AddLabel(host, name, 14, top + 4, 62, 22);
    slider.Left = 82;
    slider.Top = top;
    slider.Width = 290;
    slider.Height = 34;
    slider.Minimum = 0;
    slider.Maximum = 100;
    slider.TickFrequency = 10;
    slider.Value = 70;
    host.Controls.Add(slider);

    valueLabel.Left = 384;
    valueLabel.Top = top + 4;
    valueLabel.Width = 80;
    valueLabel.Height = 22;
    valueLabel.ForeColor = Color.White;
    valueLabel.Text = "70%";
    host.Controls.Add(valueLabel);
}

/// <summary>Adds a labeled X/Y/Z vector row plus an optional action button.</summary>
private void AddVectorRow(Control host, string label, TextBox xBox, TextBox yBox, TextBox zBox, Button button, string buttonText, int top, bool includeButton = true)
{
    AddLabel(host, label, 14, top + 5, 56, 22);
    AddAxisBox(host, xBox, "X", 70, top);
    AddAxisBox(host, yBox, "Y", 160, top);
    AddAxisBox(host, zBox, "Z", 250, top);
    if (includeButton)
        AddButton(host, button, buttonText, 360, top - 3, 88);
}

/// <summary>Adds one axis label and numeric textbox.</summary>
private void AddAxisBox(Control host, TextBox box, string axis, int left, int top)
{
    AddLabel(host, axis, left, top + 5, 14, 22);
    AddTextBox(host, box, IsAxisLabel(axis) ? "0" : string.Empty, left + 16, top);
}

/// <summary>Returns true when the label is a recognized axis token.</summary>
private static bool IsAxisLabel(string axis) => axis is "X" or "Y" or "Z";

/// <summary>Adds a numeric textbox with shared sizing.</summary>
private void AddTextBox(Control host, TextBox box, string text, int left, int top)
{
    box.Left = left;
    box.Top = top;
    box.Width = 66;
    box.Height = TextBoxHeight;
    box.Text = text;
    host.Controls.Add(box);
}

/// <summary>Adds a standard button to the requested tab page.</summary>
private void AddButton(Control host, Button button, string text, int left, int top, int width)
{
    button.Text = text;
    button.Left = left;
    button.Top = top;
    button.Width = width;
    button.Height = ButtonHeight;
    button.FlatStyle = FlatStyle.System;
    button.Font = ButtonFont;
    host.Controls.Add(button);
}
}
