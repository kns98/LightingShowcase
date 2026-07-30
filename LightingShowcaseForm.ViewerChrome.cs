// -----------------------------------------------------------------------------
// File: LightingShowcaseForm.ViewerChrome.cs
// Purpose: Online-3D-Viewer-style chrome around the Helix realtime viewport.
// -----------------------------------------------------------------------------

using LightingShowcase.SceneGraph;
using LightingShowcase.Math3D;
using LightingShowcase.UI;

namespace LightingShowcase;

public sealed partial class LightingShowcaseForm
{
    /// <summary>Builds a white, inspector-oriented shell around the Helix viewport.</summary>
    private void ConfigureEditorViewerChrome()
    {
        editorViewerShell.Dock = DockStyle.Fill;
        editorViewerShell.BackColor = Color.White;

        editorToolbar.Dock = DockStyle.Top;
        editorToolbar.Height = 56;
        editorToolbar.BackColor = Color.White;
        editorToolbar.Padding = new Padding(10, 8, 10, 8);
        editorToolbar.Paint += (_, e) =>
        {
            using Pen pen = new(Color.FromArgb(225, 230, 235));
            e.Graphics.DrawLine(pen, 0, editorToolbar.Height - 1, editorToolbar.Width, editorToolbar.Height - 1);
        };

        editorTitleLabel.Text = "scene";
        editorTitleLabel.Dock = DockStyle.Fill;
        editorTitleLabel.TextAlign = ContentAlignment.MiddleCenter;
        editorTitleLabel.Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 11.0f, FontStyle.Regular);
        editorTitleLabel.ForeColor = Color.FromArgb(20, 35, 45);
        editorToolbar.Controls.Add(editorTitleLabel);

        editorToolbarButtons.Dock = DockStyle.Left;
        editorToolbarButtons.Width = 760;
        editorToolbarButtons.FlowDirection = FlowDirection.LeftToRight;
        editorToolbarButtons.WrapContents = false;
        editorToolbarButtons.BackColor = Color.Transparent;
        editorToolbarButtons.Padding = new Padding(0);
        editorToolbar.Controls.Add(editorToolbarButtons);
        editorToolbarButtons.BringToFront();

        AddViewerToolbarButton("Open", () => OpenFileFromDialog());
        AddViewerToolbarButton("Save", () => SaveSceneFromDialog());
        AddViewerToolbarButton("Select", () => SelectNavigationTool(ViewportNavigationTool.SelectEdit));
        AddViewerToolbarButton("Rect", () => SelectNavigationTool(ViewportNavigationTool.RectangleSelect));
        AddViewerToolbarButton("Lasso", () => SelectNavigationTool(ViewportNavigationTool.LassoSelect));
        AddViewerToolbarButton("Orbit", () => SelectNavigationTool(ViewportNavigationTool.Orbit));
        AddViewerToolbarButton("Pan", () => SelectNavigationTool(ViewportNavigationTool.Pan));
        AddViewerToolbarButton("Frame", () => FocusEditView());
        AddViewerToolbarButton("Front", () => SetEditorStandardView("front"));
        AddViewerToolbarButton("Right", () => SetEditorStandardView("right"));
        AddViewerToolbarButton("Top", () => SetEditorStandardView("top"));
        AddViewerToolbarButton("Details", () => ToggleFloatingEditorDetailsPanel());
        AddViewerToolbarButton("Del", () => DeleteSelectedGroup());

        editorDetailsPanel.Dock = DockStyle.Right;
        editorDetailsPanel.Width = 430;
        editorDetailsPanel.MinimumSize = new Size(360, 0);
        editorDetailsPanel.BackColor = Color.White;
        editorDetailsPanel.Padding = new Padding(16, 12, 14, 10);
        editorDetailsPanel.BorderStyle = BorderStyle.FixedSingle;
        editorDetailsPanel.AutoScroll = true;
        editorDetailsPanel.Paint += (_, e) =>
        {
            using Pen pen = new(Color.FromArgb(225, 230, 235));
            e.Graphics.DrawRectangle(pen, 0, 0, editorDetailsPanel.Width - 1, editorDetailsPanel.Height - 1);
        };

        editorDetailsHeadingLabel.Text = "Details";
        editorDetailsHeadingLabel.Dock = DockStyle.Top;
        editorDetailsHeadingLabel.Height = 38;
        editorDetailsHeadingLabel.Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 11.0f, FontStyle.Bold);
        editorDetailsHeadingLabel.ForeColor = Color.Black;
        editorDetailsPanel.Controls.Add(editorDetailsHeadingLabel);

        editorDetailsCloseButton.Text = "×";
        editorDetailsCloseButton.Width = 26;
        editorDetailsCloseButton.Height = 24;
        editorDetailsCloseButton.Left = editorDetailsPanel.Width - editorDetailsCloseButton.Width - 8;
        editorDetailsCloseButton.Top = 8;
        editorDetailsCloseButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        editorDetailsCloseButton.FlatStyle = FlatStyle.Flat;
        editorDetailsCloseButton.BackColor = Color.White;
        editorDetailsCloseButton.ForeColor = Color.FromArgb(70, 80, 90);
        editorDetailsCloseButton.FlatAppearance.BorderColor = Color.FromArgb(215, 222, 230);
        editorDetailsCloseButton.Click += (_, _) => HideFloatingEditorDetailsPanel();
        editorDetailsPanel.Controls.Add(editorDetailsCloseButton);
        editorDetailsCloseButton.BringToFront();

        editorDetailsApplyButton.Text = "Apply edits";
        editorDetailsApplyButton.Dock = DockStyle.Bottom;
        editorDetailsApplyButton.Height = 34;
        editorDetailsApplyButton.Margin = new Padding(0, 6, 0, 0);
        editorDetailsApplyButton.FlatStyle = FlatStyle.Flat;
        editorDetailsApplyButton.BackColor = Color.White;
        editorDetailsApplyButton.ForeColor = Color.FromArgb(45, 55, 65);
        editorDetailsApplyButton.FlatAppearance.BorderColor = Color.FromArgb(205, 212, 220);
        editorDetailsApplyButton.Click += (_, _) => ApplyEditorDetailsEdits();
        editorDetailsPanel.Controls.Add(editorDetailsApplyButton);

        Panel detailsRows = new()
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.White,
            Padding = new Padding(0, 0, 4, 0)
        };
        editorDetailsPanel.Controls.Add(detailsRows);
        detailsRows.SendToBack();
        editorDetailsHeadingLabel.BringToFront();
        editorDetailsCloseButton.BringToFront();
        editorDetailsApplyButton.BringToFront();
        int top = 6;
        AddViewerDetailRow(detailsRows, "Kind:", editorKindLabel, ref top);
        AddViewerEditableVectorRow(detailsRows, "Location:", editorPositionXTextBox, editorPositionYTextBox, editorPositionZTextBox, ref top);
        AddViewerDetailRow(detailsRows, "Bounds min:", editorBoundsMinLabel, ref top);
        AddViewerDetailRow(detailsRows, "Bounds max:", editorBoundsMaxLabel, ref top);
        AddViewerDetailRow(detailsRows, "Pivot:", editorPivotLabel, ref top);
        AddViewerEditableVectorRow(detailsRows, "Delta move:", editorDeltaMoveXTextBox, editorDeltaMoveYTextBox, editorDeltaMoveZTextBox, ref top);
        AddViewerEditableVectorRow(detailsRows, "Delta rotate:", editorDeltaRotateXTextBox, editorDeltaRotateYTextBox, editorDeltaRotateZTextBox, ref top);
        AddViewerEditableVectorRow(detailsRows, "Delta scale:", editorDeltaScaleXTextBox, editorDeltaScaleYTextBox, editorDeltaScaleZTextBox, ref top);
        AddViewerEditableDetailRow(detailsRows, "Size X:", editorSizeXTextBox, ref top);
        AddViewerEditableDetailRow(detailsRows, "Size Y:", editorSizeYTextBox, ref top);
        AddViewerEditableDetailRow(detailsRows, "Size Z:", editorSizeZTextBox, ref top);
        AddViewerDetailRow(detailsRows, "Vertices:", editorVerticesLabel, ref top);
        AddViewerDetailRow(detailsRows, "Triangles:", editorTrianglesLabel, ref top);
        AddViewerDetailRow(detailsRows, "Params:", editorPrimitiveParametersLabel, ref top);
        editorPrimitiveParametersPanel.Left = 0;
        editorPrimitiveParametersPanel.Top = top;
        editorPrimitiveParametersPanel.Width = Math.Max(250, detailsRows.Width - 8);
        editorPrimitiveParametersPanel.Height = 180;
        editorPrimitiveParametersPanel.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        editorPrimitiveParametersPanel.BackColor = Color.White;
        detailsRows.Controls.Add(editorPrimitiveParametersPanel);
        top += editorPrimitiveParametersPanel.Height + 8;

        editorSelectionHintLabel.Dock = DockStyle.Bottom;
        editorSelectionHintLabel.Height = 44;
        editorSelectionHintLabel.Text = "Select an object to inspect absolute location, pending deltas, and primitive parameters.";
        editorSelectionHintLabel.ForeColor = Color.FromArgb(90, 100, 110);
        editorSelectionHintLabel.Padding = new Padding(0, 6, 0, 0);
        editorSelectionHintLabel.Visible = false;
        editorDetailsPanel.Controls.Add(editorSelectionHintLabel);
        editorSelectionHintLabel.BringToFront();
        editorDetailsApplyButton.BringToFront();

        editorCenterPanel.Dock = DockStyle.Fill;
        editorCenterPanel.BackColor = Color.White;
        editorCenterPanel.Padding = new Padding(0);
        helixViewport.Dock = DockStyle.Fill;
        editorCenterPanel.Controls.Add(helixViewport);

        editorViewerShell.Controls.Add(editorCenterPanel);
        editorViewerShell.Controls.Add(editorDetailsPanel);
        editorViewerShell.Controls.Add(editorToolbar);
        editorDetailsPanel.BringToFront();
    }


    /// <summary>Legacy toggle support; Details is now a right-docked inspector.</summary>
    private void ConfigureFloatingEditorDetailsToggle()
    {
        editorDetailsToggleButton.Visible = false;
    }

    private void PositionFloatingEditorDetailsPanel()
    {
        // The Details inspector is docked right. No floating overlay positioning is needed.
    }

    private void ToggleFloatingEditorDetailsPanel()
    {
        editorDetailsPanel.Visible = !editorDetailsPanel.Visible;
    }

    private void HideFloatingEditorDetailsPanel()
    {
        editorDetailsPanel.Visible = false;
    }

    private void AddViewerToolbarButton(string text, Action action)
    {
        Button button = new()
        {
            Text = text,
            Width = text.Length <= 4 ? 54 : 66,
            Height = 34,
            Margin = new Padding(0, 0, 6, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(45, 55, 65),
            Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 8.5f, FontStyle.Regular)
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(215, 222, 230);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(238, 245, 252);
        button.Click += (_, _) => action();
        editorToolbarButtons.Controls.Add(button);
    }

    private static void AddViewerDetailRow(Control host, string caption, Label valueLabel, ref int top)
    {
        Label label = new()
        {
            Text = caption,
            Left = 0,
            Top = top,
            Width = 96,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(20, 30, 38),
            Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 9.5f, FontStyle.Regular)
        };
        valueLabel.Left = 104;
        valueLabel.Top = top;
        valueLabel.Width = Math.Max(120, host.Width - 112);
        valueLabel.Height = 24;
        valueLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        valueLabel.AutoEllipsis = true;
        valueLabel.TextAlign = ContentAlignment.MiddleLeft;
        valueLabel.ForeColor = Color.FromArgb(50, 60, 70);
        valueLabel.Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 9.5f, FontStyle.Regular);
        host.Controls.Add(label);
        host.Controls.Add(valueLabel);
        top += 30;
    }


    private void AddViewerEditableDetailRow(Control host, string labelText, TextBox textBox, ref int top)
    {
        Label label = new() { Text = labelText };
        label.Left = 0;
        label.Top = top + 4;
        label.Width = 104;
        label.Height = 24;
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.ForeColor = Color.FromArgb(95, 105, 115);
        label.Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 8.75f, FontStyle.Regular);
        ConfigurePlainEditorTextBox(textBox, 108, top, Math.Max(120, host.Width - 112));
        textBox.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        host.Controls.Add(label);
        host.Controls.Add(textBox);
        top += 30;
    }

    private void AddViewerEditableVectorRow(Control host, string labelText, TextBox xTextBox, TextBox yTextBox, TextBox zTextBox, ref int top)
    {
        Label label = new() { Text = labelText };
        label.Left = 0;
        label.Top = top + 4;
        label.Width = 104;
        label.Height = 24;
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.ForeColor = Color.FromArgb(95, 105, 115);
        label.Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 8.75f, FontStyle.Regular);

        int fieldLeft = 108;
        int gap = 5;
        int available = Math.Max(156, host.Width - fieldLeft - 4);
        int fieldWidth = Math.Max(48, (available - (gap * 2)) / 3);

        ConfigureVectorTextBox(xTextBox, fieldLeft, top, fieldWidth);
        ConfigureVectorTextBox(yTextBox, fieldLeft + fieldWidth + gap, top, fieldWidth);
        ConfigureVectorTextBox(zTextBox, fieldLeft + ((fieldWidth + gap) * 2), top, fieldWidth);

        host.Controls.Add(label);
        host.Controls.Add(xTextBox);
        host.Controls.Add(yTextBox);
        host.Controls.Add(zTextBox);
        top += 30;
    }

    private void ConfigureVectorTextBox(TextBox textBox, int left, int top, int width)
    {
        ConfigurePlainEditorTextBox(textBox, left, top, width);
        textBox.Anchor = AnchorStyles.Left | AnchorStyles.Top;
    }

    /// <summary>Configures an unrestricted editor TextBox. Validation happens only when Apply edits is clicked.</summary>
    private void ConfigurePlainEditorTextBox(TextBox textBox, int left, int top, int width)
    {
        textBox.Left = left;
        textBox.Top = top;
        textBox.Width = width;
        textBox.Height = 24;
        textBox.BorderStyle = BorderStyle.FixedSingle;
        textBox.ForeColor = Color.FromArgb(35, 45, 55);
        textBox.Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 9.0f, FontStyle.Regular);
        textBox.ReadOnly = false;
        textBox.Multiline = false;
        textBox.ShortcutsEnabled = true;
        textBox.TabStop = true;
        textBox.AcceptsReturn = false;
        textBox.AcceptsTab = false;
        textBox.ImeMode = ImeMode.NoControl;
        textBox.MouseDown += (_, _) => textBox.Focus();
        textBox.Enter += (_, _) => textBox.SelectAll();
        textBox.TextChanged += (_, _) =>
        {
            if (refreshingEditorDetailsFields)
                return;

            if (textBox.Tag is string tag && string.Equals(tag, InspectorProgrammaticTextTag, StringComparison.Ordinal))
                return;

            textBox.Tag = InspectorDirtyTextTag;
        };
    }

    /// <summary>Refreshes the left mesh list while preserving current selection where possible.</summary>
    private void RefreshEditorMeshList()
    {
        refreshingEditorMeshList = true;
        try
        {
            editorTitleLabel.Text = sceneDocument.Title;
            panel.ObjectListView.BeginUpdate();
            try
            {
                panel.ObjectListView.Items.Clear();
                foreach (SceneObjectInfo info in sceneDocument.GetObjectInfos())
                    AddEditorMeshListItem(info);
            }
            finally
            {
                panel.ObjectListView.EndUpdate();
            }
            RefreshEditorViewerSelection();
        }
        finally
        {
            refreshingEditorMeshList = false;
        }
    }

    private void AddEditorMeshListItem(SceneObjectInfo info)
    {
        ListViewItem item = new(info.Name)
        {
            Tag = info.Id,
            Checked = info.Visible,
            IndentCount = Math.Max(0, info.Depth)
        };
        item.SubItems.Add(info.TriangleCount.ToString("N0", CultureInfo.InvariantCulture));
        item.SubItems.Add(info.Kind);
        if (!info.Visible)
            item.ForeColor = Color.FromArgb(145, 145, 145);
        panel.ObjectListView.Items.Add(item);
    }

    private void ApplyObjectListVisibilityChange(ListViewItem? item)
    {
        if (refreshingEditorMeshList || item == null || item.Tag is not int id)
            return;

        SceneObjectGroup? group = sceneDocument.FindObject(id);
        if (group == null || group.Visible == item.Checked)
            return;

        CaptureUndoState();
        sceneDocument.SetObjectVisibility(id, item.Checked);
        lastLoadMessage = $"{(item.Checked ? "Shown" : "Hidden")} object: {group.Name}";
        helixViewport.SelectGroups(selectedGroupIds, scene);
        MarkRenderDirty();
        UpdateSelectionUi();
        UpdateStatus();
    }

    private void SetSelectedObjectVisibility(bool visible)
    {
        List<int> ids = panel.ObjectListView.SelectedItems.Cast<ListViewItem>()
            .Select(i => i.Tag)
            .OfType<int>()
            .Distinct()
            .ToList();
        if (ids.Count == 0 && selectedGroupIds.Count > 0)
            ids = selectedGroupIds.ToList();
        if (ids.Count == 0)
            return;

        CaptureUndoState();
        int changed = sceneDocument.SetObjectsVisibility(ids, visible);
        lastLoadMessage = $"{(visible ? "Shown" : "Hidden")} {changed} selected object(s)";
        MarkRenderDirty();
        UpdateSelectionUi();
        UpdateStatus();
    }

    private void ShowAllObjects()
    {
        int hiddenCount = scene.ObjectGroups
            .SelectMany(g => g.SelfAndDescendants())
            .Count(g => !g.Visible);
        if (hiddenCount == 0)
            return;

        CaptureUndoState();
        int restored = sceneDocument.ShowAllObjects();
        lastLoadMessage = $"Shown all objects ({restored} restored)";
        MarkRenderDirty();
        UpdateSelectionUi();
        UpdateStatus();
    }

    private void RefreshEditorViewerSelection()
    {
        refreshingEditorMeshList = true;
        try
        {
            foreach (ListViewItem? item in panel.ObjectListView.Items)
            {
                if (item == null || item.Tag is not int id)
                    continue;
                SceneObjectGroup? group = scene.GroupById(id);
                item.Selected = selectedGroupIds.Contains(id);
                if (group != null)
                {
                    item.Checked = group.Visible;
                    item.ForeColor = group.Visible ? Color.FromArgb(30, 35, 42) : Color.FromArgb(145, 145, 145);
                }
            }
        }
        finally
        {
            refreshingEditorMeshList = false;
        }
        RefreshEditorDetails();
    }

    private void SelectFromEditorMeshList()
    {
        if (refreshingEditorMeshList)
            return;

        List<int> ids = panel.ObjectListView.SelectedItems.Cast<ListViewItem>()
            .Select(i => i.Tag)
            .OfType<int>()
            .ToList();

        if (ids.Count == 0)
        {
            ClearSelection();
            return;
        }

        selectedGroupIds.Clear();
        foreach (int id in ids)
        {
            if (scene.GroupById(id)?.IsSelectable == true)
                selectedGroupIds.Add(id);
        }
        selectedGroupId = selectedGroupIds.Count == 0 ? -1 : selectedGroupIds.Last();
        UpdateSelectionUi();
        helixViewport.SelectGroups(selectedGroupIds, scene);
        UpdateStatus();
        Invalidate();
    }

    private void RefreshEditorDetails()
    {
        refreshingEditorDetailsFields = true;
        try
        {
            SceneObjectGroup? group = SelectedGroup;
            bool singleSelection = group != null && selectedGroupIds.Count == 1;
            SetEditorDetailsEditable(singleSelection);
            editorSelectionHintLabel.Visible = !singleSelection;
            if (!singleSelection || group == null)
            {
                string multipleText = selectedGroupIds.Count > 1 ? selectedGroupIds.Count.ToString(CultureInfo.InvariantCulture) + " objects" : "-";
                editorKindLabel.Text = selectedGroupIds.Count > 1 ? "multiple" : "-";
                editorVerticesLabel.Text = selectedGroupIds.Count > 1 ? "multiple" : "-";
                editorTrianglesLabel.Text = multipleText;
                SetCleanText(editorSizeXTextBox, "-");
                SetCleanText(editorSizeYTextBox, "-");
                SetCleanText(editorSizeZTextBox, "-");
                SetVecFields(editorPositionXTextBox, editorPositionYTextBox, editorPositionZTextBox, null);
                editorBoundsMinLabel.Text = "-";
                editorBoundsMaxLabel.Text = "-";
                editorPivotLabel.Text = "-";
                SetVecFields(editorDeltaMoveXTextBox, editorDeltaMoveYTextBox, editorDeltaMoveZTextBox, null);
                SetVecFields(editorDeltaRotateXTextBox, editorDeltaRotateYTextBox, editorDeltaRotateZTextBox, null);
                SetVecFields(editorDeltaScaleXTextBox, editorDeltaScaleYTextBox, editorDeltaScaleZTextBox, null);
                editorPrimitiveParametersLabel.Text = "-";
                RebuildPrimitiveParameterRows(null, new Vec3(0, 0, 0));
                return;
            }

            List<Triangle> triangles = group.BuildWorldTriangles(includeHidden: true).ToList();
            editorTrianglesLabel.Text = triangles.Count.ToString("N0", CultureInfo.InvariantCulture);
            editorVerticesLabel.Text = CountApproximateUniqueVertices(triangles).ToString("N0", CultureInfo.InvariantCulture);
            Aabb bounds = group.GetWorldBounds(includeHidden: true);
            Vec3 size = bounds.Max - bounds.Min;
            Vec3 center = (bounds.Min + bounds.Max) * 0.5;
            editorKindLabel.Text = DescribeSelectedObjectKind(group, triangles.Count);
            SetVecFields(editorPositionXTextBox, editorPositionYTextBox, editorPositionZTextBox, center);
            editorBoundsMinLabel.Text = FormatVec(bounds.Min);
            editorBoundsMaxLabel.Text = FormatVec(bounds.Max);
            editorPivotLabel.Text = FormatVec(group.TransformPoint(group.Pivot));
            SetVecFields(editorDeltaMoveXTextBox, editorDeltaMoveYTextBox, editorDeltaMoveZTextBox, group.Position);
            SetVecFieldsDegrees(editorDeltaRotateXTextBox, editorDeltaRotateYTextBox, editorDeltaRotateZTextBox, group.Rotation);
            SetVecFields(editorDeltaScaleXTextBox, editorDeltaScaleYTextBox, editorDeltaScaleZTextBox, group.Scale);
            SetCleanText(editorSizeXTextBox, FormatNumber(size.X));
            SetCleanText(editorSizeYTextBox, FormatNumber(size.Y));
            SetCleanText(editorSizeZTextBox, FormatNumber(size.Z));
            if (!string.IsNullOrWhiteSpace(group.PrimitiveKind) && group.PrimitiveParameters.Count == 0 && group.Children.Count == 0)
                ObjectLibraryRegistry.StoreDefaultPrimitiveParametersFromShadow(group);
            bool isParametric = group.HasParametricPrimitive && group.Children.Count == 0;
            SetVectorTextBoxesEnabled(singleSelection && !isParametric, editorPositionXTextBox, editorPositionYTextBox, editorPositionZTextBox);
            editorSizeXTextBox.Enabled = singleSelection && !isParametric;
            editorSizeYTextBox.Enabled = singleSelection && !isParametric;
            editorSizeZTextBox.Enabled = singleSelection && !isParametric;
            editorPrimitiveParametersLabel.Text = DescribePrimitiveParameters(group, size, center, triangles.Count);
            RebuildPrimitiveParameterRows(group, size);
        }
        finally
        {
            refreshingEditorDetailsFields = false;
        }
    }

    private void SetEditorDetailsEditable(bool editable)
    {
        SetVectorTextBoxesEnabled(editable, editorPositionXTextBox, editorPositionYTextBox, editorPositionZTextBox);
        SetVectorTextBoxesEnabled(editable, editorDeltaMoveXTextBox, editorDeltaMoveYTextBox, editorDeltaMoveZTextBox);
        SetVectorTextBoxesEnabled(editable, editorDeltaRotateXTextBox, editorDeltaRotateYTextBox, editorDeltaRotateZTextBox);
        SetVectorTextBoxesEnabled(editable, editorDeltaScaleXTextBox, editorDeltaScaleYTextBox, editorDeltaScaleZTextBox);
        editorSizeXTextBox.Enabled = editable;
        editorSizeYTextBox.Enabled = editable;
        editorSizeZTextBox.Enabled = editable;
        editorDetailsApplyButton.Enabled = editable;
        foreach (TextBox textBox in editorPrimitiveParameterTextBoxes.Values)
            textBox.Enabled = editable;
    }

    private static void SetVectorTextBoxesEnabled(bool enabled, params TextBox[] textBoxes)
    {
        foreach (TextBox textBox in textBoxes)
            textBox.Enabled = enabled;
    }

    private void RefreshEditorDeltaTransformDetails(SceneObjectGroup group)
    {
        if (selectedGroupIds.Count != 1 || group.Id != selectedGroupId)
            return;

        SetVecFields(editorDeltaMoveXTextBox, editorDeltaMoveYTextBox, editorDeltaMoveZTextBox, group.Position);
        SetVecFieldsDegrees(editorDeltaRotateXTextBox, editorDeltaRotateYTextBox, editorDeltaRotateZTextBox, group.Rotation);
        SetVecFields(editorDeltaScaleXTextBox, editorDeltaScaleYTextBox, editorDeltaScaleZTextBox, group.Scale);
    }



    /// <summary>Rebuilds the editable primitive parameter section for the selected object's authored type.</summary>
    private void RebuildPrimitiveParameterRows(SceneObjectGroup? group, Vec3 size)
    {
        editorPrimitiveParametersPanel.Controls.Clear();
        editorPrimitiveParameterTextBoxes.Clear();

        if (group == null || selectedGroupIds.Count != 1)
            return;

        List<(string Key, string Label, double Value)> parameters = GetPrimitiveParameterValues(group, size);
        if (parameters.Count == 0)
        {
            Label note = new()
            {
                Text = string.IsNullOrWhiteSpace(group.PrimitiveKind)
                    ? "Imported/raw mesh: no creation parameters were stored. Use Location, Size, and Delta Transform above."
                    : "This grouped object uses Location, Size, and Delta Transform above.",
                Left = 104,
                Top = 0,
                Width = Math.Max(160, editorPrimitiveParametersPanel.Width - 112),
                Height = 42,
                ForeColor = Color.FromArgb(90, 100, 110),
                Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 8.5f, FontStyle.Regular)
            };
            editorPrimitiveParametersPanel.Controls.Add(note);
            return;
        }

        int top = 0;
        foreach ((string key, string labelText, double value) in parameters)
        {
            Label label = new()
            {
                Text = labelText + ":",
                Left = 0,
                Top = top + 4,
                Width = 104,
                Height = 24,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(95, 105, 115),
                Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 8.75f, FontStyle.Regular)
            };
            TextBox textBox = new();
            ConfigurePlainEditorTextBox(textBox, 108, top, Math.Max(120, editorPrimitiveParametersPanel.Width - 116));
            textBox.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            textBox.Text = FormatNumber(value);
            textBox.Enabled = group != null && selectedGroupIds.Count == 1;
            editorPrimitiveParametersPanel.Controls.Add(label);
            editorPrimitiveParametersPanel.Controls.Add(textBox);
            editorPrimitiveParameterTextBoxes[key] = textBox;
            top += 30;
        }

        editorPrimitiveParametersPanel.Height = Math.Max(70, top + 8);
    }

    private static string HumanizePrimitiveParameterLabel(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return "Parameter";

        string spaced = System.Text.RegularExpressions.Regex.Replace(key, "(?<!^)([A-Z])", " $1");
        return char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }

    private static List<(string Key, string Label, double Value)> GetPrimitiveParameterValues(SceneObjectGroup group, Vec3 size)
    {
        List<(string Key, string Label, double Value)> values = new();
        if (group.Children.Count > 0)
            return values;

        if (ScenePrimitiveRegistry.Find(group.PrimitiveKind ?? group.PrimitiveSourceName) is not ISceneObjectDefinition definition)
            return values;

        if (group.PrimitiveParameters.Count == 0)
            ObjectLibraryRegistry.StoreDefaultPrimitiveParametersFromShadow(group);

        Aabb bounds = group.GetWorldBounds(includeHidden: true);
        Vec3 center = (bounds.Min + bounds.Max) * 0.5;
        Dictionary<string, double> defaults = definition.CreateParametersFromBounds(bounds);

        void Add(string key, string label, double fallback)
        {
            double value = group.PrimitiveParameters.TryGetValue(key, out double stored) && double.IsFinite(stored) ? stored : fallback;
            values.Add((key, label, value));
        }

        Add("originX", "Point X", center.X);
        Add("originY", "Point Y", center.Y);
        Add("originZ", "Point Z", center.Z);

        foreach (string key in defaults.Keys.Concat(group.PrimitiveParameters.Keys)
                     .Where(k => !string.Equals(k, "originX", StringComparison.OrdinalIgnoreCase) &&
                                 !string.Equals(k, "originY", StringComparison.OrdinalIgnoreCase) &&
                                 !string.Equals(k, "originZ", StringComparison.OrdinalIgnoreCase))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            double fallback = defaults.TryGetValue(key, out double defaultValue) ? defaultValue : 0.0;
            Add(key, HumanizePrimitiveParameterLabel(key), fallback);
        }

        return values;
    }


    private bool TryApplyPrimitiveParametersToGroup(SceneObjectGroup group, out bool parsed)
    {
        parsed = false;
        if (editorPrimitiveParameterTextBoxes.Count == 0 || string.IsNullOrWhiteSpace(group.PrimitiveKind) || group.Children.Count > 0)
            return false;

        bool changed = false;
        foreach (KeyValuePair<string, TextBox> entry in editorPrimitiveParameterTextBoxes)
        {
            if (!IsInspectorFieldDirty(entry.Value) || !TryParseFreeNumber(entry.Value.Text, out double value))
                continue;

            parsed = true;
            if (!entry.Key.StartsWith("origin", StringComparison.OrdinalIgnoreCase) && value <= 1e-8)
                value = 1e-8;

            if (!group.PrimitiveParameters.TryGetValue(entry.Key, out double oldValue) || Math.Abs(oldValue - value) > 1e-8)
            {
                group.PrimitiveParameters[entry.Key] = value;
                changed = true;
            }
        }

        if (!changed)
            return false;

        return scene.RebuildPrimitiveShadowGeometry(group);
    }

    private bool HasAnyDirtyParsablePrimitiveParameterText()
    {
        foreach (TextBox textBox in editorPrimitiveParameterTextBoxes.Values)
        {
            if (IsInspectorFieldDirty(textBox) && TryParseFreeNumber(textBox.Text, out _))
                return true;
        }
        return false;
    }

    private static string NormalizeInspectorPrimitiveKind(SceneObjectGroup group)
    {
        string raw = !string.IsNullOrWhiteSpace(group.PrimitiveKind)
            ? group.PrimitiveKind!
            : group.PrimitiveSourceName ?? string.Empty;
        return raw.Trim()
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("/", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
    }

    private static bool ApplyDesiredWorldSize(SceneObjectGroup group, Vec3 desiredSize)
    {
        Aabb currentBounds = group.GetWorldBounds(includeHidden: true);
        Vec3 currentSize = currentBounds.Max - currentBounds.Min;
        Vec3 factor = new(
            SafeResizeFactor(desiredSize.X, currentSize.X),
            SafeResizeFactor(desiredSize.Y, currentSize.Y),
            SafeResizeFactor(desiredSize.Z, currentSize.Z));

        if (NearlyEqual(factor, new Vec3(1, 1, 1)))
            return false;

        group.Scale = new(group.Scale.X * factor.X, group.Scale.Y * factor.Y, group.Scale.Z * factor.Z);
        return true;
    }


    private static void SetCleanText(TextBox textBox, string text)
    {
        textBox.Tag = InspectorProgrammaticTextTag;
        textBox.Text = text;
        textBox.Tag = null;
    }

    private static bool IsInspectorFieldDirty(TextBox textBox) =>
        textBox.Tag is string tag && string.Equals(tag, InspectorDirtyTextTag, StringComparison.Ordinal);

    private static bool IsAnyInspectorFieldDirty(params TextBox[] textBoxes) =>
        textBoxes.Any(IsInspectorFieldDirty);

    private static void MarkInspectorFieldsClean(params TextBox[] textBoxes)
    {
        foreach (TextBox textBox in textBoxes)
            textBox.Tag = null;
    }

    private static void SetVecFields(TextBox xTextBox, TextBox yTextBox, TextBox zTextBox, Vec3? value)
    {
        if (!value.HasValue)
        {
            SetCleanText(xTextBox, "-");
            SetCleanText(yTextBox, "-");
            SetCleanText(zTextBox, "-");
            return;
        }

        SetCleanText(xTextBox, FormatNumber(value.Value.X));
        SetCleanText(yTextBox, FormatNumber(value.Value.Y));
        SetCleanText(zTextBox, FormatNumber(value.Value.Z));
    }

    private static void SetVecFieldsDegrees(TextBox xTextBox, TextBox yTextBox, TextBox zTextBox, Vec3 radians)
    {
        SetCleanText(xTextBox, FormatNumber(radians.X * 180.0 / Math.PI));
        SetCleanText(yTextBox, FormatNumber(radians.Y * 180.0 / Math.PI));
        SetCleanText(zTextBox, FormatNumber(radians.Z * 180.0 / Math.PI));
    }

    private static Vec3? TryParseVec3Fields(TextBox xTextBox, TextBox yTextBox, TextBox zTextBox, out bool parsed)
    {
        parsed = false;
        bool xHasValue = HasNumericText(xTextBox.Text);
        bool yHasValue = HasNumericText(yTextBox.Text);
        bool zHasValue = HasNumericText(zTextBox.Text);
        if (!xHasValue && !yHasValue && !zHasValue)
            return null;

        // Treat an incomplete row as not parsed instead of fighting the user's
        // typing. This allows temporary input like '-', '.', '-.', and '1e'.
        if (!xHasValue || !yHasValue || !zHasValue)
            return null;

        if (!TryParseFreeNumber(xTextBox.Text, out double x)) return null;
        if (!TryParseFreeNumber(yTextBox.Text, out double y)) return null;
        if (!TryParseFreeNumber(zTextBox.Text, out double z)) return null;

        parsed = true;
        return new Vec3(x, y, z);
    }

    private static bool HasNumericText(string text)
    {
        string trimmed = text.Trim();
        return trimmed.Length > 0 && trimmed != "-";
    }

    private static bool TryParseFreeNumber(string text, out double value)
    {
        string cleaned = text.Trim()
            .Replace("°", string.Empty, StringComparison.Ordinal)
            .Replace("−", "-", StringComparison.Ordinal);

        if (double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value))
            return true;

        return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.CurrentCulture, out value) && double.IsFinite(value);
    }

    /// <summary>Applies editable values from the floating Details panel to the single selected object or group.</summary>
    private void ApplyEditorDetailsEdits()
    {
        if (refreshingEditorDetailsFields)
            return;

        SceneObjectGroup? group = SelectedGroup;
        if (group == null || selectedGroupIds.Count != 1)
            return;

        bool anyParsed = false;
        bool changed = false;

        bool locationDirty = IsAnyInspectorFieldDirty(editorPositionXTextBox, editorPositionYTextBox, editorPositionZTextBox);
        bool moveDirty = IsAnyInspectorFieldDirty(editorDeltaMoveXTextBox, editorDeltaMoveYTextBox, editorDeltaMoveZTextBox);
        bool rotateDirty = IsAnyInspectorFieldDirty(editorDeltaRotateXTextBox, editorDeltaRotateYTextBox, editorDeltaRotateZTextBox);
        bool scaleDirty = IsAnyInspectorFieldDirty(editorDeltaScaleXTextBox, editorDeltaScaleYTextBox, editorDeltaScaleZTextBox);
        bool sizeXDirty = IsInspectorFieldDirty(editorSizeXTextBox);
        bool sizeYDirty = IsInspectorFieldDirty(editorSizeYTextBox);
        bool sizeZDirty = IsInspectorFieldDirty(editorSizeZTextBox);

        bool locationParsed = false;
        bool moveParsed = false;
        bool rotateParsed = false;
        bool scaleParsed = false;
        bool sizeXParsed = false;
        bool sizeYParsed = false;
        bool sizeZParsed = false;

        Vec3? desiredLocation = locationDirty ? TryParseVec3Fields(editorPositionXTextBox, editorPositionYTextBox, editorPositionZTextBox, out locationParsed) : null;
        Vec3? desiredMove = moveDirty ? TryParseVec3Fields(editorDeltaMoveXTextBox, editorDeltaMoveYTextBox, editorDeltaMoveZTextBox, out moveParsed) : null;
        Vec3? desiredRotateDegrees = rotateDirty ? TryParseVec3Fields(editorDeltaRotateXTextBox, editorDeltaRotateYTextBox, editorDeltaRotateZTextBox, out rotateParsed) : null;
        Vec3? desiredScale = scaleDirty ? TryParseVec3Fields(editorDeltaScaleXTextBox, editorDeltaScaleYTextBox, editorDeltaScaleZTextBox, out scaleParsed) : null;
        double? desiredSizeX = sizeXDirty ? TryParsePositiveNumber(editorSizeXTextBox.Text, out sizeXParsed) : null;
        double? desiredSizeY = sizeYDirty ? TryParsePositiveNumber(editorSizeYTextBox.Text, out sizeYParsed) : null;
        double? desiredSizeZ = sizeZDirty ? TryParsePositiveNumber(editorSizeZTextBox.Text, out sizeZParsed) : null;
        bool primitiveParametersParsed = HasAnyDirtyParsablePrimitiveParameterText();
        anyParsed = locationParsed || moveParsed || rotateParsed || scaleParsed || sizeXParsed || sizeYParsed || sizeZParsed || primitiveParametersParsed;

        if (!anyParsed)
        {
            lastLoadMessage = "No valid Details edits to apply. Enter numbers in the X/Y/Z text boxes, then click Apply edits.";
            UpdateStatus();
            RefreshEditorDetails();
            return;
        }

        CaptureUndoState();

        changed |= TryApplyPrimitiveParametersToGroup(group, out _);

        if (desiredMove.HasValue && !NearlyEqual(group.Position, desiredMove.Value))
        {
            group.Position = desiredMove.Value;
            changed = true;
        }

        if (desiredRotateDegrees.HasValue)
        {
            Vec3 radians = new(
                desiredRotateDegrees.Value.X * Math.PI / 180.0,
                desiredRotateDegrees.Value.Y * Math.PI / 180.0,
                desiredRotateDegrees.Value.Z * Math.PI / 180.0);
            if (!NearlyEqual(group.Rotation, radians))
            {
                group.Rotation = radians;
                changed = true;
            }
        }

        if (desiredScale.HasValue && IsUsableScale(desiredScale.Value) && !NearlyEqual(group.Scale, desiredScale.Value))
        {
            group.Scale = desiredScale.Value;
            changed = true;
        }

        if (!(group.HasParametricPrimitive && group.Children.Count == 0) && (desiredSizeX.HasValue || desiredSizeY.HasValue || desiredSizeZ.HasValue))
        {
            Aabb currentBounds = group.GetWorldBounds(includeHidden: true);
            Vec3 currentSize = currentBounds.Max - currentBounds.Min;
            Vec3 desiredSize = new(
                desiredSizeX ?? currentSize.X,
                desiredSizeY ?? currentSize.Y,
                desiredSizeZ ?? currentSize.Z);

            changed |= ApplyDesiredWorldSize(group, desiredSize);
        }

        if (!(group.HasParametricPrimitive && group.Children.Count == 0) && desiredLocation.HasValue)
        {
            Aabb currentBounds = group.GetWorldBounds(includeHidden: true);
            Vec3 currentCenter = (currentBounds.Min + currentBounds.Max) * 0.5;
            Vec3 delta = desiredLocation.Value - currentCenter;
            if (delta.Length() > 1e-8)
            {
                group.Position += delta;
                changed = true;
            }
        }

        if (!changed)
        {
            lastLoadMessage = $"Details unchanged for {group.Name}";
            RefreshEditorDetails();
            UpdateStatus();
            return;
        }

        if (group.Children.Count == 0)
            group.RecalculatePivot();
        scene.RebuildWorldGeometry();
        helixViewport.SelectGroups(selectedGroupIds, scene);
        MarkRenderDirty();
        lastLoadMessage = $"Updated Details values for {group.Name}";
        RefreshEditorDetails();
        UpdateSelectionUi();
        UpdateStatus();
    }

    private static bool TryParseVec3(string text, out bool parsed)
    {
        parsed = TryParseVec3Value(text).HasValue;
        return parsed;
    }

    private static Vec3? TryParseVec3Value(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Trim() == "-")
            return null;

        string cleaned = text.Replace("°", string.Empty, StringComparison.Ordinal)
            .Replace("(", string.Empty, StringComparison.Ordinal)
            .Replace(")", string.Empty, StringComparison.Ordinal)
            .Replace(";", ",", StringComparison.Ordinal);
        string[] parts = cleaned.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
            return null;

        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)) return null;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y)) return null;
        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double z)) return null;
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z)) return null;
        return new Vec3(x, y, z);
    }

    private static double? TryParsePositiveNumber(string text, out bool parsed)
    {
        parsed = false;
        if (string.IsNullOrWhiteSpace(text) || text.Trim() == "-")
            return null;

        if (!TryParseFreeNumber(text, out double value))
            return null;
        if (!double.IsFinite(value) || value <= 1e-8)
            return null;

        parsed = true;
        return value;
    }

    private static bool IsUsableScale(Vec3 scale) =>
        double.IsFinite(scale.X) && double.IsFinite(scale.Y) && double.IsFinite(scale.Z) &&
        Math.Abs(scale.X) > 1e-8 && Math.Abs(scale.Y) > 1e-8 && Math.Abs(scale.Z) > 1e-8;

    private static double SafeResizeFactor(double desired, double current) =>
        double.IsFinite(desired) && desired > 1e-8 && double.IsFinite(current) && current > 1e-8
            ? desired / current
            : 1.0;

    private static bool NearlyEqual(Vec3 a, Vec3 b) =>
        Math.Abs(a.X - b.X) < 1e-8 && Math.Abs(a.Y - b.Y) < 1e-8 && Math.Abs(a.Z - b.Z) < 1e-8;


    private static string DescribeSelectedObjectKind(SceneObjectGroup group, int triangleCount)
    {
        if (group.Children.Count > 0)
            return $"group ({group.Children.Count} children)";
        if (!string.IsNullOrWhiteSpace(group.PrimitiveKind))
            return group.PrimitiveKind!;
        return triangleCount == 1 ? "triangle" : "mesh";
    }

    private static string DescribePrimitiveParameters(SceneObjectGroup group, Vec3 size, Vec3 center, int triangleCount)
    {
        string source = !string.IsNullOrWhiteSpace(group.PrimitiveSourceName)
            ? group.PrimitiveSourceName!
            : !string.IsNullOrWhiteSpace(group.PrimitiveKind)
                ? group.PrimitiveKind!
                : string.Empty;

        if (group.Children.Count > 0)
            return $"group location/size are editable above; children={group.Children.Count}";

        if (!string.IsNullOrWhiteSpace(source))
        {
            string kind = group.PrimitiveKind ?? source;
            PrimitiveGizmoEditMetadata metadata = group.GetGizmoEditMetadata();
            return $"source={source}; kind={kind}; gizmo={metadata.ScaleRule}; move={ (metadata.MoveUpdatesOrigin ? "origin" : "transform") }; triangles are regenerated shadow mesh";
        }

        if (triangleCount == 1)
            return "single triangle mesh; size/location/delta transform editable";

        return "imported mesh; editable as mesh size/location/delta transform";
    }

    private static string FormatVec(Vec3 value) => $"{FormatNumber(value.X)}, {FormatNumber(value.Y)}, {FormatNumber(value.Z)}";

    private static string FormatVecDegrees(Vec3 radians) =>
        $"{FormatNumber(radians.X * 180.0 / Math.PI)}°, {FormatNumber(radians.Y * 180.0 / Math.PI)}°, {FormatNumber(radians.Z * 180.0 / Math.PI)}°";

    private static string FormatNumber(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static int CountApproximateUniqueVertices(IEnumerable<Triangle> triangles)
    {
        HashSet<(long X, long Y, long Z)> vertices = new();
        foreach (Triangle tri in triangles)
        {
            AddVertex(vertices, tri.A);
            AddVertex(vertices, tri.B);
            AddVertex(vertices, tri.C);
        }
        return vertices.Count;
    }

    private static void AddVertex(HashSet<(long X, long Y, long Z)> vertices, Vec3 point)
    {
        const double scale = 100000.0;
        vertices.Add(((long)Math.Round(point.X * scale), (long)Math.Round(point.Y * scale), (long)Math.Round(point.Z * scale)));
    }
}
