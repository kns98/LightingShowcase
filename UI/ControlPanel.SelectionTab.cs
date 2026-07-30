// -----------------------------------------------------------------------------
// File: UI/ControlPanel.SelectionTab.cs
// Purpose: Selection/material tab construction for ControlPanel.
// -----------------------------------------------------------------------------

namespace LightingShowcase.UI;

public sealed partial class ControlPanel
{
/// <summary>Selection tab: transform, material, texture, duplicate, and delete controls.</summary>
private void AddSelectionTab(Control host)
{
    int top = 14;
    SelectionLabel.Text = "Selection: none";
    SelectionLabel.ForeColor = Color.White;
    SelectionLabel.Left = 14;
    SelectionLabel.Top = top;
    SelectionLabel.Width = 450;
    SelectionLabel.Height = 30;
    host.Controls.Add(SelectionLabel);

    // Transform editing moved to the right-docked Details inspector.
    // Keep the legacy controls alive for compatibility with older handlers, but
    // do not place them on the original left control panel.
    MoveXBox.Text = "0"; MoveYBox.Text = "0"; MoveZBox.Text = "0";
    RotXBox.Text = "0"; RotYBox.Text = "0"; RotZBox.Text = "0";
    ScaleXBox.Text = "1"; ScaleYBox.Text = "1"; ScaleZBox.Text = "1";
    ReferenceCameraBox.Checked = false;

    top += 38;
    AddHeading(host, "Appearance", top);
    top += 32;
    AddButton(host, ColorSelectionButton, "Color", 14, top, 92);
    AddButton(host, TextureSelectionButton, "Bitmap", 116, top, 100);
    AddButton(host, SampleTextureSelectionButton, "Checker", 226, top, 100);
    AddButton(host, ClearTextureSelectionButton, "No Tex", 336, top, 92);

    top += 42;
    AddDescription(host, "Tile size", 14, top + 4, 70, 22);
    TextureTileSizeBox.Left = 86;
    TextureTileSizeBox.Top = top;
    TextureTileSizeBox.Width = 70;
    TextureTileSizeBox.Height = TextBoxHeight;
    TextureTileSizeBox.Text = "0.25";
    host.Controls.Add(TextureTileSizeBox);
    AddButton(host, RetileTextureButton, "Retile selected", 168, top - 4, 150);
    AddDescription(host, "Smaller = more repeats. Use this when a bitmap is stretched across a table, rug, wall, or imported model.", 328, top - 2, 135, 42);

    top += 58;
    AddHeading(host, "Material library", top);

    top += 28;
    MaterialLibraryComboBox.Left = 14;
    MaterialLibraryComboBox.Top = top;
    MaterialLibraryComboBox.Width = 300;
    MaterialLibraryComboBox.Height = TextBoxHeight;
    MaterialLibraryComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
    host.Controls.Add(MaterialLibraryComboBox);
    AddButton(host, ApplyMaterialPresetButton, "Apply preset", 326, top - 4, 136);

    top += 34;
    MaterialPresetInfoLabel.Text = "Choose a preset to load color, metalness, roughness, alpha, transmission, and emission.";
    MaterialPresetInfoLabel.ForeColor = Color.Gainsboro;
    MaterialPresetInfoLabel.Left = 14;
    MaterialPresetInfoLabel.Top = top;
    MaterialPresetInfoLabel.Width = 450;
    MaterialPresetInfoLabel.Height = 40;
    host.Controls.Add(MaterialPresetInfoLabel);

    top += 56;
    AddHeading(host, "glTF / PBR material", top);

    top += 28;
    MaterialInfoLabel.Text = "Material: none";
    MaterialInfoLabel.ForeColor = Color.Gainsboro;
    MaterialInfoLabel.Left = 14;
    MaterialInfoLabel.Top = top;
    MaterialInfoLabel.Width = 450;
    MaterialInfoLabel.Height = 22;
    host.Controls.Add(MaterialInfoLabel);

    top += 30;
    AddDescription(host, "Alpha", 14, top + 5, 48, 22);
    AddTextBox(host, MaterialAlphaBox, "1", 64, top);
    AddDescription(host, "Trans", 142, top + 5, 48, 22);
    AddTextBox(host, MaterialTransmissionBox, "0", 192, top);
    AddDescription(host, "Metal", 270, top + 5, 46, 22);
    AddTextBox(host, MaterialMetallicBox, "0", 318, top);
    MaterialAlphaBlendBox.Text = "Blend";
    MaterialAlphaBlendBox.ForeColor = Color.Gainsboro;
    MaterialAlphaBlendBox.Left = 394;
    MaterialAlphaBlendBox.Top = top + 3;
    MaterialAlphaBlendBox.Width = 74;
    MaterialAlphaBlendBox.Height = 24;
    host.Controls.Add(MaterialAlphaBlendBox);

    top += 38;
    AddDescription(host, "Rough", 14, top + 5, 48, 22);
    AddTextBox(host, MaterialRoughnessBox, "0.72", 64, top);
    AddDescription(host, "Emit", 142, top + 5, 48, 22);
    AddTextBox(host, MaterialEmissionBox, "0", 192, top);
    AddDescription(host, "E RGB", 270, top + 5, 48, 22);
    AddTextBox(host, MaterialEmissionRBox, "1", 318, top);
    AddTextBox(host, MaterialEmissionGBox, "1", 386, top);
    AddTextBox(host, MaterialEmissionBBox, "1", 454, top);
    MaterialEmissionBBox.Width = 42;

    top += 38;
    MaterialUseBaseTextureBox.Text = "Base tex";
    MaterialUseEmissiveTextureBox.Text = "Emissive";
    MaterialUseMetallicRoughnessTextureBox.Text = "MR tex";
    MaterialUseNormalTextureBox.Text = "Normal";
    CheckBox[] textureBoxes = { MaterialUseBaseTextureBox, MaterialUseEmissiveTextureBox, MaterialUseMetallicRoughnessTextureBox, MaterialUseNormalTextureBox };
    int[] lefts = { 14, 118, 222, 326 };
    for (int i = 0; i < textureBoxes.Length; i++)
    {
        textureBoxes[i].ForeColor = Color.Gainsboro;
        textureBoxes[i].Left = lefts[i];
        textureBoxes[i].Top = top;
        textureBoxes[i].Width = 96;
        textureBoxes[i].Height = 24;
        host.Controls.Add(textureBoxes[i]);
    }

    top += 34;
    AddButton(host, ApplyMaterialPropertiesButton, "Apply material", 14, top, 150);
    AddDescription(host, "Alpha/transmission affect glass. Metallic/roughness affect highlights. Uncheck texture boxes to ignore imported maps for the selected object.", 176, top - 2, 290, 44);

    top += 58;
    AddButton(host, DuplicateSelectionButton, "Duplicate", 14, top, 112);
    AddButton(host, DeleteSelectionButton, "Delete", 138, top, 90);
    AddButton(host, GroupSelectionButton, "Group", 240, top, 86);
    AddButton(host, UngroupSelectionButton, "Ungroup", 338, top, 112);

    top += 48;
    AddDescription(host, "Keep %", 14, top + 4, 62, 22);
    SimplifyKeepPercentBox.Left = 78;
    SimplifyKeepPercentBox.Top = top;
    SimplifyKeepPercentBox.Width = 60;
    SimplifyKeepPercentBox.Height = TextBoxHeight;
    SimplifyKeepPercentBox.Text = "50";
    host.Controls.Add(SimplifyKeepPercentBox);
    AddButton(host, SimplifySelectionButton, "Simplify selected", 150, top - 4, 156);
    AddDescription(host, "Keeps representative triangles across the whole object. Lower values improve editor speed but reduce detail.", 318, top - 2, 148, 42);

    top += 54;
    TextureInfoLabel.Text = "Texture: none";
    TextureInfoLabel.ForeColor = Color.Gainsboro;
    TextureInfoLabel.Left = 14;
    TextureInfoLabel.Top = top;
    TextureInfoLabel.Width = 450;
    TextureInfoLabel.Height = 22;
    host.Controls.Add(TextureInfoLabel);

    top += 30;
    TexturePreviewBox.Left = 14;
    TexturePreviewBox.Top = top;
    TexturePreviewBox.Width = 96;
    TexturePreviewBox.Height = 96;
    TexturePreviewBox.SizeMode = PictureBoxSizeMode.Zoom;
    TexturePreviewBox.BackColor = Color.FromArgb(30, 30, 36);
    TexturePreviewBox.BorderStyle = BorderStyle.FixedSingle;
    host.Controls.Add(TexturePreviewBox);

    AddDescription(host, "Use Bitmap to assign an image file, Checker for the built-in sample, or No Tex to clear it.", 122, top + 6, 330, 64);
}

}
