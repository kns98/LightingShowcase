// -----------------------------------------------------------------------------
// File: UI/ControlPanel.RenderTab.cs
// Purpose: Render, lighting, and cinema playback tab construction.
// -----------------------------------------------------------------------------

namespace LightingShowcase.UI;

public sealed partial class ControlPanel
{
    /// <summary>Render tab: camera playback, raytrace scale, and full scene light controls.</summary>
    private void AddRenderTab(Control host)
    {
        int top = 14;
        AddHeading(host, "Cinema playback", top);
        top += 30;
        AddButton(host, PlayPauseButton, "Pause", 14, top, 108);
        AddButton(host, RestartButton, "Restart", 132, top, 96);
        AddButton(host, ManualButton, "Manual", 240, top, 100);
        AddButton(host, DemoButton, "Timeline", 350, top, 108);
        AddDescription(host, "Space toggles timeline playback. Manual pauses/free-controls the camera; Timeline plays the camera path in the selected raster preview.", 14, top + 42, 450, 44);

        top += 100;
        AddHeading(host, "Render preview", top);
        top += 32;
        AddButton(host, RaytraceViewButton, "Render Tab", 14, top, 132);
        AddDescription(host, "Opens the Render tab next to Helix and starts the selected backend. Shadow/Vulkan Raster are orbitable; CPU/Vulkan GPU are still-image render modes.", 156, top - 2, 308, 56);

        top += 58;
        AddLabel(host, "Renderer", 14, top + 5, 72, 24);
        RenderBackendComboBox.Left = 96;
        RenderBackendComboBox.Top = top;
        RenderBackendComboBox.Width = 170;
        RenderBackendComboBox.Height = 30;
        RenderBackendComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        RenderBackendComboBox.Items.AddRange(new object[] { "Shadow Raster Preview", "Vulkan Raster Preview", "CPU", "Vulkan GPU", "Vulkan Diagnostic" });
        RenderBackendComboBox.SelectedIndex = 0;
        host.Controls.Add(RenderBackendComboBox);
        AddDescription(host, "Shadow Raster is the CPU z-buffer preview. Vulkan Raster uses the GPU graphics pipeline. CPU/Vulkan GPU are still-render modes.", 14, top + 40, 450, 54);

        top += 86;
        AddLabel(host, "Scale", 14, top + 5, 60, 24);
        ScaleComboBox.Left = 82;
        ScaleComboBox.Top = top;
        ScaleComboBox.Width = 136;
        ScaleComboBox.Height = 30;
        ScaleComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        ScaleComboBox.Items.AddRange(RenderScale.Labels);
        ScaleComboBox.SelectedIndex = 3;
        host.Controls.Add(ScaleComboBox);
        AddDescription(host, "Lower scale renders faster while editing. Higher scale gives a sharper preview.", 14, top + 40, 450, 32);

        top += 78;
        AddLabel(host, "Bounces", 14, top + 5, 60, 24);
        BounceComboBox.Left = 82;
        BounceComboBox.Top = top;
        BounceComboBox.Width = 136;
        BounceComboBox.Height = 30;
        BounceComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        BounceComboBox.Items.AddRange(new object[] { "None", "1", "2", "3", "4", "5", "6", "8" });
        BounceComboBox.SelectedIndex = 0;
        host.Controls.Add(BounceComboBox);
        AddDescription(host, "None keeps the current direct raytracer. Higher values enable slower progressive path-traced indirect bounces.", 14, top + 40, 450, 44);

        top += 86;
        AddLabel(host, "Max samples", 14, top + 5, 82, 24);
        MaxSamplesComboBox.Left = 106;
        MaxSamplesComboBox.Top = top;
        MaxSamplesComboBox.Width = 112;
        MaxSamplesComboBox.Height = 30;
        MaxSamplesComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        MaxSamplesComboBox.Items.AddRange(new object[] { "Auto", "16", "32", "64", "128", "256", "512", "Unlimited" });
        MaxSamplesComboBox.SelectedIndex = 0;
        host.Controls.Add(MaxSamplesComboBox);
        AddDescription(host, "Auto preserves the current adaptive sample count. Fixed values stop progressive refinement after that many samples.", 14, top + 40, 450, 44);

        top += 100;
        AddHeading(host, "Scene lighting", top);
        top += 30;

        LightListBox.Left = 14;
        LightListBox.Top = top;
        LightListBox.Width = 450;
        LightListBox.Height = 112;
        LightListBox.IntegralHeight = false;
        host.Controls.Add(LightListBox);

        top += 124;
        AddLabel(host, "Id", 14, top + 5, 32, 22);
        LightIdBox.Left = 48;
        LightIdBox.Top = top;
        LightIdBox.Width = 150;
        LightIdBox.Height = TextBoxHeight;
        LightIdBox.Text = "light";
        host.Controls.Add(LightIdBox);

        AddLabel(host, "Kind", 210, top + 5, 42, 22);
        LightKindComboBox.Left = 254;
        LightKindComboBox.Top = top;
        LightKindComboBox.Width = 110;
        LightKindComboBox.Height = 30;
        LightKindComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        LightKindComboBox.Items.AddRange(new object[] { "Point", "Directional", "Spot" });
        LightKindComboBox.SelectedIndex = 0;
        host.Controls.Add(LightKindComboBox);

        LightEnabledBox.Left = 378;
        LightEnabledBox.Top = top + 2;
        LightEnabledBox.Width = 86;
        LightEnabledBox.Height = 24;
        LightEnabledBox.Text = "Enabled";
        LightEnabledBox.ForeColor = Color.White;
        LightEnabledBox.Checked = true;
        host.Controls.Add(LightEnabledBox);

        top += 38;
        AddVectorRow(host, "Pos", LightPosXBox, LightPosYBox, LightPosZBox, new Button(), string.Empty, top, includeButton: false);
        top += 38;
        AddVectorRow(host, "Dir", LightDirXBox, LightDirYBox, LightDirZBox, new Button(), string.Empty, top, includeButton: false);
        top += 38;
        AddVectorRow(host, "RGB", LightColorRBox, LightColorGBox, LightColorBBox, new Button(), string.Empty, top, includeButton: false);

        top += 38;
        AddLabel(host, "Intensity", 14, top + 5, 64, 22);
        LightIntensityBox.Left = 82;
        LightIntensityBox.Top = top;
        LightIntensityBox.Width = 66;
        LightIntensityBox.Height = TextBoxHeight;
        LightIntensityBox.Text = "3.0";
        host.Controls.Add(LightIntensityBox);

        AddLabel(host, "Range", 160, top + 5, 50, 22);
        LightRangeBox.Left = 214;
        LightRangeBox.Top = top;
        LightRangeBox.Width = 58;
        LightRangeBox.Height = TextBoxHeight;
        LightRangeBox.Text = "0";
        host.Controls.Add(LightRangeBox);

        AddLabel(host, "Cone", 286, top + 5, 42, 22);
        LightInnerConeBox.Left = 330;
        LightInnerConeBox.Top = top;
        LightInnerConeBox.Width = 58;
        LightInnerConeBox.Height = TextBoxHeight;
        LightInnerConeBox.Text = "0";
        host.Controls.Add(LightInnerConeBox);
        LightOuterConeBox.Left = 398;
        LightOuterConeBox.Top = top;
        LightOuterConeBox.Width = 58;
        LightOuterConeBox.Height = TextBoxHeight;
        LightOuterConeBox.Text = "45";
        host.Controls.Add(LightOuterConeBox);

        top += 42;
        AddButton(host, AddLightButton, "Add Light", 14, top, 132);
        AddButton(host, ApplyLightButton, "Apply Edit", 158, top, 132);
        AddButton(host, RemoveLightButton, "Remove", 302, top, 132);

        top += 42;
        AddButton(host, ConvertSelectionToLightButton, "Object → Light", 14, top, 156);
        AddDescription(host, "Object → Light creates a point light at the selected object's center and makes that mesh emissive so the lamp/bulb stays visible. Directional and spot lights use Dir. Range 0 means unlimited.", 184, top - 2, 280, 70);
    }
}
