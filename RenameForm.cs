using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace SolidWorksTeamRenameTool
{
    internal sealed class RenameForm : Form
    {
        private sealed class GridRow
        {
            public RenameTask Task { get; set; }
            public string Status { get; set; }
            public string Kind { get; set; }
            public int Level { get; set; }
            public string ParentPath { get; set; }
            public string DirectoryName { get; set; }
            public string CurrentSegment { get; set; }
            public string OldName { get; set; }
            public string NewName { get; set; }
            public int DuplicateCount { get; set; }
            public string Reason { get; set; }
            public string FullReason { get; set; }
        }

        private object _swApp;
        private readonly V2RuleConfig _config = V2RuleConfig.CreateDefault();
        private readonly List<RenameTask> _tasks = new List<RenameTask>();
        private readonly List<GridRow> _gridRows = new List<GridRow>();

        private object _activeModel;
        private List<ComponentNode> _componentNodes;
        private bool _syncing;
        private bool _bindingGrid;
        private bool _previewIsCurrent;
        private bool _paramsExpanded;
        private bool _settingsDirty;
        private V2RuleTarget _activeRule = V2RuleTarget.Assembly;
        private bool _showingProjectCode;
        private int _selectedIndex;
        private int _nextFieldId = 100;
        private string _activeTemplate = "prefix";

        private readonly Button _readButton = new GtkTabButton();
        private readonly Button _previewButton = new GtkTabButton();
        private readonly Button _executeButton = new GtkTabButton();
        private readonly Button _diagnosticButton = new GtkButton();
        private readonly CheckBox _autoConfirmDialogsBox = new CheckBox();
        private readonly CheckBox _confirmOverwriteBox = new CheckBox();

        private readonly Button _ruleModeButton = new GtkButton();
        private readonly Button _replaceModeButton = new GtkButton();
        private readonly TextBox _findTextBox = new TextBox();
        private readonly TextBox _replaceTextBox = new TextBox();
        private readonly TextBox _projectCodeBox = new TextBox();

        private readonly CheckBox _skipReadonlyBox = new CheckBox();
        private readonly CheckBox _skipDuplicateBox = new CheckBox();
        private readonly CheckBox _skipEnglishStartBox = new CheckBox();
        private readonly CheckBox _skipChineseStartBox = new CheckBox();
        private readonly CheckBox _skipNumberStartBox = new CheckBox();
        private readonly CheckBox _skipSymbolStartBox = new CheckBox();
        private readonly CheckBox _skipPathKeywordsBox = new CheckBox();
        private readonly TextBox _pathKeywordsBox = new TextBox();
        private readonly TextBox _logBox = new TextBox();

        private readonly Button _prefixTemplateButton = new GtkButton();
        private readonly Button _hierarchyTemplateButton = new GtkButton();
        private readonly Button _drawingTemplateButton = new GtkButton();
        private readonly Button _assemblyTabButton = new GtkButton();
        private readonly Button _partTabButton = new GtkButton();
        private readonly Button _projectTabButton = new GtkButton();
        private readonly Button _aboutButton = new GtkButton();
        private readonly Button _toggleParamsButton = new GtkButton();

        private readonly FlowLayoutPanel _fieldChainPanel = new FlowLayoutPanel();
        private readonly Label _exampleLabel = new Label();
        private readonly Panel _editorPanel = new Panel();
        private readonly TextBox _joinerBox = new TextBox();
        private readonly ComboBox _typeBox = new ComboBox();
        private readonly Label _param1Label = new Label();
        private readonly TextBox _param1Box = new TextBox();
        private readonly Label _param2Label = new Label();
        private readonly TextBox _param2Box = new TextBox();
        private readonly ComboBox _scopeBox = new ComboBox();
        private readonly Button _moveLeftButton = new GtkButton();
        private readonly Button _moveRightButton = new GtkButton();
        private readonly Button _addFieldButton = new GtkButton();
        private readonly Button _deleteFieldButton = new GtkButton();
        private Panel _param1Cell;
        private Panel _param2Cell;

        private readonly DataGridView _grid = new DataGridView();
        private readonly Label _summaryLabel = new Label();
        private readonly ProgressBar _busyBar = new ProgressBar();
        private SplitContainer _workspaceSplit;
        private SplitContainer _rightSplit;
        private TableLayoutPanel _ruleLayout;
        private RowStyle _editorRowStyle;

        private static readonly Color AppBackColor = GtkTheme.App;
        private static readonly Color SurfaceColor = GtkTheme.Surface;
        private static readonly Color SoftSurfaceColor = Color.FromArgb(239, 242, 245);
        private static readonly Color BorderColor = GtkTheme.Border;
        private static readonly Color AccentColor = GtkTheme.Accent;
        private static readonly Color AccentDarkColor = Color.FromArgb(28, 94, 134);
        private static readonly Color TextColor = GtkTheme.Text;
        private static readonly Color MutedTextColor = GtkTheme.Muted;

        private const int SwDocAssembly = 2;

        private const string AppVersion = "V2.1";

        public RenameForm(object swApp)
        {
            _swApp = swApp;
            BuildUi();
            BindStaticLists();
            LoadSavedSettings();
            LoadConfigToUi();
            RenderAll();
            _settingsDirty = false;
            FormClosing += RenameForm_FormClosing;
            Shown += delegate
            {
                TopMost = true;
                Activate();
                TopMost = false;
            };
            if (_swApp == null)
            {
                AppendLog("未检测到 SolidWorks。当前可检查界面和配置；读取装配、生成预览、执行重命名需要在装有 SolidWorks 的电脑上使用。");
            }
        }

        private void BuildUi()
        {
            Text = "层级命名工具";
            Width = 1400;
            Height = 780;
            MinimumSize = new Size(1100, 640);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
            BackColor = AppBackColor;

            TableLayoutPanel shell = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = AppBackColor,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            Controls.Add(shell);

            BuildToolbar(shell);

            _workspaceSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterWidth = 12,
                FixedPanel = FixedPanel.Panel1,
                IsSplitterFixed = true,
                BackColor = AppBackColor
            };
            shell.Controls.Add(_workspaceSplit, 0, 1);
            _workspaceSplit.Panel1.Padding = new Padding(12, 12, 0, 12);
            _workspaceSplit.Panel2.Padding = new Padding(0, 12, 12, 12);

            Panel sidebarCard = new GtkFrame
            {
                Dock = DockStyle.Fill,
                BackColor = SurfaceColor,
                Padding = new Padding(10)
            };
            _workspaceSplit.Panel1.Controls.Add(sidebarCard);

            Panel sidebar = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = Padding.Empty,
                BackColor = SurfaceColor
            };
            sidebarCard.Controls.Add(sidebar);

            _rightSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 12,
                IsSplitterFixed = true,
                BackColor = AppBackColor
            };
            _workspaceSplit.Panel2.Controls.Add(_rightSplit);

            BuildSidebar(sidebar);
            BuildRulePanel(_rightSplit.Panel1);
            BuildListPanel(_rightSplit.Panel2);

            Resize += delegate { ApplyLayout(); };
            Shown += delegate { ApplyLayout(); };
        }

        private void BuildToolbar(TableLayoutPanel shell)
        {
            Panel toolbar = new GtkToolbar
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(238, 241, 243),
                Padding = new Padding(8, 6, 8, 5)
            };
            shell.Controls.Add(toolbar, 0, 0);

            FlowLayoutPanel tools = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = toolbar.BackColor
            };
            toolbar.Controls.Add(tools);

            AddToolbarButton(tools, _readButton, "读取装配", ReadButton_Click);
            AddToolbarButton(tools, _previewButton, "生成预览", PreviewButton_Click);
            AddToolbarButton(tools, _executeButton, "执行重命名", ExecuteButton_Click);
            _executeButton.Enabled = false;

            _aboutButton.Dock = DockStyle.Right;
            _aboutButton.Text = AppVersion;
            _aboutButton.Width = 64;
            _aboutButton.Click += delegate { ShowAbout(); };
            StyleSecondaryButton(_aboutButton);
            toolbar.Controls.Add(_aboutButton);
        }

        private void BuildSidebar(Panel sidebar)
        {
            GroupBox mode = CreateGroup("重命名方式", 86);
            _ruleModeButton.Text = "规则命名";
            _replaceModeButton.Text = "查找替换原文件名";
            _ruleModeButton.Location = new Point(10, 25);
            _replaceModeButton.Location = new Point(150, 25);
            _ruleModeButton.Size = new Size(132, 31);
            _replaceModeButton.Size = new Size(146, 31);
            StyleSecondaryButton(_ruleModeButton);
            StyleSecondaryButton(_replaceModeButton);
            _ruleModeButton.Click += delegate { SetMode(V2RenameMode.Rule); };
            _replaceModeButton.Click += delegate { SetMode(V2RenameMode.FindReplace); };
            mode.Controls.AddRange(new Control[] { _ruleModeButton, _replaceModeButton });
            sidebar.Controls.Add(mode);

            GroupBox replace = CreateGroup("查找替换", 82);
            AddLabel(replace, "查找", 10, 24, 120);
            PlaceText(_findTextBox, replace, 10, 42, 132);
            AddLabel(replace, "替换为", 160, 24, 120);
            PlaceText(_replaceTextBox, replace, 160, 42, 136);
            sidebar.Controls.Add(replace);

            GroupBox filters = CreateGroup("筛选跳过", 210);
            PlaceCheck(_skipReadonlyBox, filters, "跳过只读文件", 10, 24, 132);
            PlaceCheck(_skipDuplicateBox, filters, "合并重复引用", 160, 24, 132);
            PlaceCheck(_skipEnglishStartBox, filters, "跳过英文开头", 10, 58, 132);
            PlaceCheck(_skipChineseStartBox, filters, "跳过中文开头", 160, 58, 132);
            PlaceCheck(_skipNumberStartBox, filters, "跳过数字开头", 10, 92, 132);
            PlaceCheck(_skipSymbolStartBox, filters, "跳过非中英数字开头", 160, 92, 156);
            PlaceCheck(_skipPathKeywordsBox, filters, "跳过路径关键词", 10, 126, 180);
            PlaceText(_pathKeywordsBox, filters, 10, 158, 286);
            sidebar.Controls.Add(filters);

            GroupBox operations = CreateGroup("操作选项", 124);
            PlaceCheck(_autoConfirmDialogsBox, operations, "自动确认 SW 弹窗", 10, 24, 180);
            PlaceCheck(_confirmOverwriteBox, operations, "同名覆盖前提示", 10, 54, 190);
            _confirmOverwriteBox.Checked = true;
            _diagnosticButton.Text = "高级诊断";
            _diagnosticButton.Location = new Point(10, 84);
            _diagnosticButton.Size = new Size(132, 30);
            _diagnosticButton.Enabled = false;
            _diagnosticButton.Click += DiagnosticButton_Click;
            StyleSecondaryButton(_diagnosticButton);
            operations.Controls.Add(_diagnosticButton);
            sidebar.Controls.Add(operations);

            GroupBox log = CreateGroup("操作日志", 138);
            _logBox.Location = new Point(10, 24);
            _logBox.Size = new Size(286, 96);
            _logBox.Multiline = true;
            _logBox.ReadOnly = true;
            _logBox.ScrollBars = ScrollBars.Vertical;
            _logBox.BorderStyle = BorderStyle.FixedSingle;
            _logBox.BackColor = SurfaceColor;
            _logBox.ForeColor = TextColor;
            _logBox.Text = "等待操作。";
            log.Controls.Add(_logBox);
            sidebar.Controls.Add(log);

            int y = 12;
            foreach (Control control in new Control[] { mode, replace, filters, operations, log })
            {
                control.Left = 10;
                control.Top = y;
                control.Width = 306;
                y += control.Height + 10;
            }
        }

        private void BuildRulePanel(Control host)
        {
            Panel panel = new GtkFrame
            {
                Dock = DockStyle.Fill,
                BackColor = SurfaceColor,
                Padding = new Padding(12)
            };
            host.Controls.Add(panel);

            _ruleLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                BackColor = SurfaceColor
            };
            _ruleLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _ruleLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            _ruleLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            _ruleLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 88F));
            _ruleLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            _editorRowStyle = new RowStyle(SizeType.Absolute, 0F);
            _ruleLayout.RowStyles.Add(_editorRowStyle);
            panel.Controls.Add(_ruleLayout);

            TableLayoutPanel header = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = SurfaceColor,
                Margin = Padding.Empty
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _ruleLayout.Controls.Add(header, 0, 0);

            Label title = new Label
            {
                Text = "命名规则",
                Font = new Font(Font.FontFamily, 12.5F, FontStyle.Bold),
                ForeColor = AccentDarkColor,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = false
            };
            header.Controls.Add(title, 0, 0);

            FlowLayoutPanel headerActions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = SurfaceColor,
                Margin = Padding.Empty
            };
            header.Controls.Add(headerActions, 1, 0);

            _prefixTemplateButton.Text = "层级前缀";
            _hierarchyTemplateButton.Text = "层级编码";
            _drawingTemplateButton.Text = "图号式编码";
            _prefixTemplateButton.Size = new Size(112, 30);
            _hierarchyTemplateButton.Size = new Size(112, 30);
            _drawingTemplateButton.Size = new Size(112, 30);
            _prefixTemplateButton.Margin = new Padding(8, 2, 0, 0);
            _hierarchyTemplateButton.Margin = new Padding(8, 2, 0, 0);
            _drawingTemplateButton.Margin = new Padding(8, 2, 0, 0);
            StyleSecondaryButton(_prefixTemplateButton);
            StyleSecondaryButton(_hierarchyTemplateButton);
            StyleSecondaryButton(_drawingTemplateButton);
            _prefixTemplateButton.Click += delegate { ApplyTemplate("prefix"); };
            _hierarchyTemplateButton.Click += delegate { ApplyTemplate("hierarchy"); };
            _drawingTemplateButton.Click += delegate { ApplyTemplate("drawing"); };
            headerActions.Controls.Add(_drawingTemplateButton);
            headerActions.Controls.Add(_hierarchyTemplateButton);
            headerActions.Controls.Add(_prefixTemplateButton);

            _toggleParamsButton.Text = "展开字段参数";
            _toggleParamsButton.Size = new Size(126, 30);
            _toggleParamsButton.Margin = new Padding(0, 3, 8, 3);
            _toggleParamsButton.Click += delegate
            {
                _paramsExpanded = !_paramsExpanded;
                MarkSettingsDirty();
                RenderAll();
                ApplyLayout();
            };
            StyleToggleButton(_toggleParamsButton);

            _projectTabButton.Text = "项目号";
            _assemblyTabButton.Text = "装配体规则";
            _partTabButton.Text = "零件规则";
            _projectTabButton.Size = new Size(84, 30);
            _assemblyTabButton.Size = new Size(104, 30);
            _partTabButton.Size = new Size(104, 30);
            _projectTabButton.Margin = new Padding(0, 3, 8, 3);
            _assemblyTabButton.Margin = new Padding(0, 3, 8, 3);
            _partTabButton.Margin = new Padding(0, 3, 8, 3);
            _projectTabButton.Click += delegate { _showingProjectCode = true; _activeRule = V2RuleTarget.Assembly; _selectedIndex = 0; MarkSettingsDirty(); RenderAll(); };
            _assemblyTabButton.Click += delegate { _showingProjectCode = false; _activeRule = V2RuleTarget.Assembly; _selectedIndex = 0; MarkSettingsDirty(); RenderAll(); };
            _partTabButton.Click += delegate { _showingProjectCode = false; _activeRule = V2RuleTarget.Part; _selectedIndex = 0; MarkSettingsDirty(); RenderAll(); };
            StyleSecondaryButton(_projectTabButton);
            StyleSecondaryButton(_assemblyTabButton);
            StyleSecondaryButton(_partTabButton);

            FlowLayoutPanel ruleActions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = SurfaceColor,
                Margin = Padding.Empty
            };
            ruleActions.Controls.AddRange(new Control[] { _projectTabButton, _assemblyTabButton, _partTabButton, _toggleParamsButton });
            _ruleLayout.Controls.Add(ruleActions, 0, 1);

            _fieldChainPanel.Dock = DockStyle.Fill;
            _fieldChainPanel.Margin = new Padding(0, 4, 0, 4);
            _fieldChainPanel.AutoScroll = true;
            _fieldChainPanel.WrapContents = false;
            _fieldChainPanel.BackColor = SoftSurfaceColor;
            _fieldChainPanel.BorderStyle = BorderStyle.FixedSingle;
            _ruleLayout.Controls.Add(_fieldChainPanel, 0, 2);

            _exampleLabel.Dock = DockStyle.Fill;
            _exampleLabel.Margin = new Padding(0, 2, 0, 4);
            _exampleLabel.BorderStyle = BorderStyle.FixedSingle;
            _exampleLabel.BackColor = SurfaceColor;
            _exampleLabel.ForeColor = AccentDarkColor;
            _exampleLabel.Font = new Font("Consolas", 9.5F, FontStyle.Bold);
            _exampleLabel.TextAlign = ContentAlignment.MiddleLeft;
            _ruleLayout.Controls.Add(_exampleLabel, 0, 3);

            _editorPanel.Dock = DockStyle.Fill;
            _editorPanel.Margin = new Padding(0, 6, 0, 0);
            _editorPanel.BackColor = SurfaceColor;
            _ruleLayout.Controls.Add(_editorPanel, 0, 4);
            BuildEditorPanel(_editorPanel);

            _projectCodeBox.TextChanged += ProjectCodeChanged;
        }

        private void BuildEditorPanel(Panel panel)
        {
            panel.Controls.Clear();
            panel.BorderStyle = BorderStyle.FixedSingle;
            panel.Padding = new Padding(10, 6, 10, 6);

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 5,
                RowCount = 1,
                BackColor = SurfaceColor,
                Margin = Padding.Empty
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 236F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            panel.Controls.Add(layout);

            layout.Controls.Add(CreateEditorCell("前连接符", _joinerBox), 0, 0);
            layout.Controls.Add(CreateEditorCell("字段类型", _typeBox), 1, 0);
            _param1Cell = CreateEditorCell(_param1Label, _param1Box);
            _param2Cell = CreateEditorCell(_param2Label, _param2Box, _scopeBox);
            layout.Controls.Add(_param1Cell, 2, 0);
            layout.Controls.Add(_param2Cell, 3, 0);

            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = SurfaceColor
            };
            AddSmallButton(buttons, _moveLeftButton, "前移", delegate { MoveSelected(-1); });
            AddSmallButton(buttons, _moveRightButton, "后移", delegate { MoveSelected(1); });
            AddSmallButton(buttons, _addFieldButton, "新增", delegate { InsertField(SelectedIndex() + 1); });
            AddSmallButton(buttons, _deleteFieldButton, "删除", delegate { DeleteSelected(); });
            layout.Controls.Add(buttons, 4, 0);
        }

        private void BuildListPanel(Control host)
        {
            Panel card = new GtkFrame
            {
                Dock = DockStyle.Fill,
                BackColor = SurfaceColor,
                Padding = new Padding(12)
            };
            host.Controls.Add(card);

            TableLayoutPanel panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = SurfaceColor,
                Padding = Padding.Empty,
                ColumnCount = 1,
                RowCount = 3
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 4F));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            card.Controls.Add(panel);

            TableLayoutPanel header = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = SurfaceColor,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            panel.Controls.Add(header, 0, 0);

            Label title = new Label
            {
                Text = "零件预览列表",
                Font = new Font(Font.FontFamily, 12.5F, FontStyle.Bold),
                ForeColor = AccentDarkColor,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = false
            };
            header.Controls.Add(title, 0, 0);

            _summaryLabel.Dock = DockStyle.Fill;
            _summaryLabel.ForeColor = MutedTextColor;
            _summaryLabel.TextAlign = ContentAlignment.MiddleRight;
            header.Controls.Add(_summaryLabel, 1, 0);

            _busyBar.Dock = DockStyle.Fill;
            _busyBar.Margin = Padding.Empty;
            _busyBar.Style = ProgressBarStyle.Marquee;
            _busyBar.Visible = false;
            panel.Controls.Add(_busyBar, 0, 1);

            _grid.Dock = DockStyle.Fill;
            _grid.Margin = new Padding(0, 8, 0, 0);
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.ReadOnly = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.MultiSelect = false;
            _grid.RowHeadersVisible = false;
            _grid.AllowUserToResizeRows = false;
            _grid.AllowUserToResizeColumns = false;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.ScrollBars = ScrollBars.Vertical;
            _grid.BackgroundColor = SurfaceColor;
            _grid.BorderStyle = BorderStyle.FixedSingle;
            _grid.EnableHeadersVisualStyles = false;
            _grid.ColumnHeadersDefaultCellStyle.BackColor = SoftSurfaceColor;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = TextColor;
            _grid.ColumnHeadersDefaultCellStyle.Font = new Font(Font.FontFamily, 9F, FontStyle.Bold);
            _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            _grid.GridColor = BorderColor;
            _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(205, 231, 247);
            _grid.DefaultCellStyle.SelectionForeColor = TextColor;
            _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 251, 252);
            _grid.ColumnHeadersHeight = 28;
            _grid.RowTemplate.Height = 24;
            _grid.EditMode = DataGridViewEditMode.EditOnEnter;
            _grid.CurrentCellDirtyStateChanged += Grid_CurrentCellDirtyStateChanged;
            _grid.CellBeginEdit += Grid_CellBeginEdit;
            _grid.EditingControlShowing += Grid_EditingControlShowing;
            _grid.CellValueChanged += Grid_CellValueChanged;
            _grid.CellToolTipTextNeeded += Grid_CellToolTipTextNeeded;
            _grid.DataError += delegate { };
            _grid.CellFormatting += Grid_CellFormatting;
            panel.Controls.Add(_grid, 0, 2);

            EnsureGridColumns();
            ConfigureGridColumns();
            _summaryLabel.Text = "尚未读取装配体。";
        }

        private void BindStaticLists()
        {
            _typeBox.Items.AddRange(new object[] { "固定文本", "父组件名称", "层级字母", "层级数字", "同级序号", "全局流水", "自定义属性", "原文件名" });
            _scopeBox.Items.AddRange(new object[] { "全部对象", "仅装配体" });
            _autoConfirmDialogsBox.CheckedChanged += delegate { MarkSettingsDirty(); };
            _confirmOverwriteBox.CheckedChanged += delegate { MarkSettingsDirty(); };

            foreach (Control control in AllControls(this))
            {
                if (control == _logBox || control == _autoConfirmDialogsBox || control == _confirmOverwriteBox)
                {
                    continue;
                }

                TextBox textBox = control as TextBox;
                if (textBox != null)
                {
                    textBox.TextChanged += AnyConfig_Changed;
                }
                ComboBox comboBox = control as ComboBox;
                if (comboBox != null)
                {
                    comboBox.SelectedIndexChanged += AnyConfig_Changed;
                }
                CheckBox checkBox = control as CheckBox;
                if (checkBox != null)
                {
                    checkBox.CheckedChanged += AnyConfig_Changed;
                }
            }
        }

        private static IEnumerable<Control> AllControls(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (Control nested in AllControls(child))
                {
                    yield return nested;
                }
            }
        }

        private void LoadConfigToUi()
        {
            _syncing = true;
            _findTextBox.Text = _config.FindText;
            _replaceTextBox.Text = _config.ReplaceText;
            _projectCodeBox.Text = _config.ProjectCode ?? string.Empty;
            _skipReadonlyBox.Checked = _config.Filters.SkipReadonlyFiles;
            _skipDuplicateBox.Checked = _config.Filters.SkipDuplicateReferences;
            _skipEnglishStartBox.Checked = _config.Filters.SkipEnglishStart;
            _skipChineseStartBox.Checked = _config.Filters.SkipChineseStart;
            _skipNumberStartBox.Checked = _config.Filters.SkipNumberStart;
            _skipSymbolStartBox.Checked = _config.Filters.SkipSymbolStart;
            _skipPathKeywordsBox.Checked = _config.Filters.SkipPathKeywords;
            _pathKeywordsBox.Text = string.Join(",", _config.Filters.PathKeywords.ToArray());
            _syncing = false;
        }

        private void LoadSavedSettings()
        {
            UserSettings settings;
            string error;
            if (!UserSettingsStore.TryLoad(out settings, out error))
            {
                if (!string.IsNullOrWhiteSpace(error))
                {
                    AppendLog("配置文件读取失败，已使用默认规则。");
                }
                V2RuleConfig.Normalize(_config);
                return;
            }

            ApplyUserSettings(settings);
        }

        private void ApplyUserSettings(UserSettings settings)
        {
            if (settings == null || settings.Config == null)
            {
                V2RuleConfig.Normalize(_config);
                return;
            }

            CopyConfig(settings.Config, _config);
            _activeTemplate = string.IsNullOrWhiteSpace(settings.ActiveTemplate) ? "prefix" : settings.ActiveTemplate;
            _activeRule = settings.ActiveRule;
            _paramsExpanded = settings.ParamsExpanded;
            _autoConfirmDialogsBox.Checked = settings.AutoConfirmDialogs;
            _confirmOverwriteBox.Checked = settings.ConfirmOverwrite;
            _selectedIndex = 0;
            UpdateNextFieldId();
        }

        private void CopyConfig(V2RuleConfig source, V2RuleConfig target)
        {
            target.Mode = source.Mode;
            target.FindText = source.FindText ?? string.Empty;
            target.ReplaceText = source.ReplaceText ?? string.Empty;
            target.ProjectCode = source.ProjectCode ?? string.Empty;
            target.Filters.SkipReadonlyFiles = source.Filters.SkipReadonlyFiles;
            target.Filters.SkipDuplicateReferences = source.Filters.SkipDuplicateReferences;
            target.Filters.SkipEnglishStart = source.Filters.SkipEnglishStart;
            target.Filters.SkipChineseStart = source.Filters.SkipChineseStart;
            target.Filters.SkipNumberStart = source.Filters.SkipNumberStart;
            target.Filters.SkipSymbolStart = source.Filters.SkipSymbolStart;
            target.Filters.SkipPathKeywords = source.Filters.SkipPathKeywords;
            target.Filters.PathKeywords.Clear();
            target.Filters.PathKeywords.AddRange(source.Filters.PathKeywords);
            target.AssemblyFields.Clear();
            target.PartFields.Clear();
            target.AssemblyFields.AddRange(source.AssemblyFields.Select(f => f.Clone()));
            target.PartFields.AddRange(source.PartFields.Select(f => f.Clone()));
            V2RuleConfig.Normalize(target);
        }

        private void SaveUserSettings()
        {
            SaveUiToConfig();
            UserSettings settings = new UserSettings
            {
                Config = _config.Clone(),
                ActiveTemplate = _activeTemplate,
                ActiveRule = _activeRule,
                ParamsExpanded = _paramsExpanded,
                AutoConfirmDialogs = _autoConfirmDialogsBox.Checked,
                ConfirmOverwrite = _confirmOverwriteBox.Checked
            };
            UserSettingsStore.Save(settings);
            _settingsDirty = false;
        }

        private void RenameForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_settingsDirty)
            {
                return;
            }

            try
            {
                SaveUserSettings();
            }
            catch (Exception ex)
            {
                AppendLog("配置文件保存失败：" + ex.Message);
            }
        }

        private void MarkSettingsDirty()
        {
            if (!_syncing)
            {
                _settingsDirty = true;
            }
        }

        private void UpdateNextFieldId()
        {
            int maxId = 0;
            foreach (V2RuleField field in _config.AssemblyFields.Concat(_config.PartFields))
            {
                if (field != null)
                {
                    maxId = Math.Max(maxId, field.Id);
                }
            }
            _nextFieldId = Math.Max(_nextFieldId, maxId + 1);
        }

        private void SaveUiToConfig()
        {
            if (_syncing)
            {
                return;
            }

            _config.FindText = _findTextBox.Text ?? string.Empty;
            _config.ReplaceText = _replaceTextBox.Text ?? string.Empty;
            _config.ProjectCode = _projectCodeBox.Text ?? string.Empty;
            _config.Filters.SkipReadonlyFiles = _skipReadonlyBox.Checked;
            _config.Filters.SkipDuplicateReferences = _skipDuplicateBox.Checked;
            _config.Filters.SkipEnglishStart = _skipEnglishStartBox.Checked;
            _config.Filters.SkipChineseStart = _skipChineseStartBox.Checked;
            _config.Filters.SkipNumberStart = _skipNumberStartBox.Checked;
            _config.Filters.SkipSymbolStart = _skipSymbolStartBox.Checked;
            _config.Filters.SkipPathKeywords = _skipPathKeywordsBox.Checked;
            _config.Filters.PathKeywords.Clear();
            _config.Filters.PathKeywords.AddRange(SplitKeywords(_pathKeywordsBox.Text));
        }

        private static IEnumerable<string> SplitKeywords(string text)
        {
            return (text ?? string.Empty)
                .Split(new[] { ',', '，', ';', '；', '、', '|', '/', '\\', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0);
        }

        private void AnyConfig_Changed(object sender, EventArgs e)
        {
            if (_syncing)
            {
                return;
            }

            if (sender == _joinerBox || sender == _typeBox || sender == _param1Box || sender == _param2Box || sender == _scopeBox)
            {
                UpdateSelectedFromEditor();
            }
            else
            {
                SaveUiToConfig();
            }

            MarkSettingsDirty();
            RenderAll();
            MarkPreviewDirty();
        }

        private void ProjectCodeChanged(object sender, EventArgs e)
        {
            if (_syncing)
            {
                return;
            }

            _config.ProjectCode = _projectCodeBox.Text ?? string.Empty;
            MarkSettingsDirty();
            MarkPreviewDirty();
            RenderExample();
        }

        private void SetMode(V2RenameMode mode)
        {
            _config.Mode = mode;
            MarkSettingsDirty();
            RenderAll();
            MarkPreviewDirty();
        }

        private void ApplyTemplate(string name)
        {
            _config.ApplyTemplate(name);
            V2RuleConfig.Normalize(_config);
            _activeTemplate = name;
            _showingProjectCode = false;
            _activeRule = V2RuleTarget.Assembly;
            _selectedIndex = 0;
            MarkSettingsDirty();
            RenderAll();
            MarkPreviewDirty();
        }

        private void RenderAll()
        {
            RenderModeButtons();
            RenderTemplateButtons();
            RenderRuleTabs();
            RenderFieldChain();
            RenderEditor();
            RenderExample();
            if (_editorRowStyle != null)
            {
                _editorRowStyle.Height = (_paramsExpanded && !_showingProjectCode) ? 78F : 0F;
            }
            _editorPanel.Visible = _paramsExpanded && !_showingProjectCode;
            _toggleParamsButton.Text = _paramsExpanded ? "▲ 收起字段参数" : "▼ 展开字段参数";
            _toggleParamsButton.Enabled = !_showingProjectCode;
            StyleToggleButton(_toggleParamsButton);
            _findTextBox.Enabled = _config.Mode == V2RenameMode.FindReplace;
            _replaceTextBox.Enabled = _config.Mode == V2RenameMode.FindReplace;
            if (_ruleLayout != null)
            {
                _ruleLayout.PerformLayout();
            }
        }

        private void ShowAbout()
        {
            MessageBox.Show(
                "层级命名工具 " + AppVersion + "\n\n" +
                "开源-非商业使用声明：\n\n" +
                "1. 你可以免费使用、复制、修改和分发本软件的源代码。\n" +
                "2. 本软件可用于学习、研究和个人用途。\n" +
                "3. 未经授权，禁止将本软件或其衍生版本用于任何商业用途，\n" +
                "   包括但不限于出售、出租、付费服务或商业培训。\n\n" +
                "本软件按“现状”提供，不附带任何明示或暗示的担保。\n" +
                "作者不对因使用本软件造成的任何损失承担责任。",
                "关于 层级命名工具",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void RenderModeButtons()
        {
            SetActiveButton(_ruleModeButton, _config.Mode == V2RenameMode.Rule);
            SetActiveButton(_replaceModeButton, _config.Mode == V2RenameMode.FindReplace);
        }

        private void RenderTemplateButtons()
        {
            SetActiveButton(_prefixTemplateButton, _activeTemplate == "prefix");
            SetActiveButton(_hierarchyTemplateButton, _activeTemplate == "hierarchy");
            SetActiveButton(_drawingTemplateButton, _activeTemplate == "drawing");
        }

        private void RenderRuleTabs()
        {
            SetActiveButton(_projectTabButton, _showingProjectCode);
            SetActiveButton(_assemblyTabButton, !_showingProjectCode && _activeRule == V2RuleTarget.Assembly);
            SetActiveButton(_partTabButton, !_showingProjectCode && _activeRule == V2RuleTarget.Part);
        }

        private void RenderFieldChain()
        {
            _fieldChainPanel.Controls.Clear();

            if (_showingProjectCode)
            {
                Panel projectChip = new Panel
                {
                    Size = new Size(210, 56),
                    Margin = new Padding(6, 8, 0, 6),
                    BackColor = SurfaceColor,
                    BorderStyle = BorderStyle.FixedSingle
                };
                Label projectLabel = new Label
                {
                    Text = "项目号",
                    AutoSize = true,
                    Location = new Point(12, 19),
                    ForeColor = TextColor
                };
                _projectCodeBox.Location = new Point(70, 15);
                _projectCodeBox.Size = new Size(128, 26);
                projectChip.Controls.Add(projectLabel);
                projectChip.Controls.Add(_projectCodeBox);
                _fieldChainPanel.Controls.Add(projectChip);
                return;
            }

            List<V2RuleField> fields = ActiveFields();
            if (fields.Count == 0)
            {
                Label empty = new Label
                {
                    Text = "当前没有字段，展开字段参数后点击新增创建字段。",
                    AutoSize = false,
                    Size = new Size(340, 52),
                    Margin = new Padding(10, 12, 0, 0),
                    TextAlign = ContentAlignment.MiddleLeft,
                    ForeColor = MutedTextColor
                };
                _fieldChainPanel.Controls.Add(empty);
                return;
            }

            for (int i = 0; i < fields.Count; i++)
            {
                V2RuleField field = fields[i];
                Button chip = new Button
                {
                    Tag = i,
                    Text = FieldChipText(field),
                    Size = new Size(148, 56),
                    Margin = new Padding(6, 8, 0, 6),
                    TextAlign = ContentAlignment.MiddleCenter,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = i == _selectedIndex ? AccentColor : SurfaceColor,
                    ForeColor = i == _selectedIndex ? Color.White : TextColor
                };
                chip.FlatAppearance.BorderColor = i == _selectedIndex ? AccentColor : BorderColor;
                chip.FlatAppearance.BorderSize = 1;
                chip.Click += delegate
                {
                    _selectedIndex = Convert.ToInt32(chip.Tag);
                    RenderAll();
                };
                _fieldChainPanel.Controls.Add(chip);
            }
        }

        private void RenderEditor()
        {
            if (_showingProjectCode)
            {
                _selectedIndex = -1;
                SetEditorEnabled(false);
                return;
            }

            List<V2RuleField> fields = ActiveFields();
            if (fields.Count == 0)
            {
                _selectedIndex = -1;
                SetEditorEnabled(false);
                return;
            }

            if (_selectedIndex < 0 || _selectedIndex >= fields.Count)
            {
                _selectedIndex = 0;
            }

            V2RuleField field = fields[_selectedIndex];
            _syncing = true;
            _joinerBox.Text = field.Joiner ?? string.Empty;
            _typeBox.SelectedIndex = (int)field.Type;
            _param1Box.Text = field.Param1 ?? string.Empty;
            _param2Box.Text = field.Param2 ?? string.Empty;
            _scopeBox.SelectedItem = ScopeDisplay(field.Param2);

            ConfigureParameterControls(field.Type);
            _syncing = false;
            SetEditorEnabled(true);
        }

        private void ConfigureParameterControls(V2FieldType type)
        {
            if (_param1Cell != null) _param1Cell.Visible = true;
            if (_param2Cell != null) _param2Cell.Visible = true;
            _param1Box.Visible = true;
            _param2Box.Visible = true;
            _scopeBox.Visible = false;
            _param1Box.Enabled = true;
            _param2Box.Enabled = true;

            if (type == V2FieldType.FixedText)
            {
                _param1Label.Text = "文本";
                _param2Label.Text = string.Empty;
                if (_param2Cell != null) _param2Cell.Visible = false;
            }
            else if (type == V2FieldType.ParentName)
            {
                _param1Label.Text = string.Empty;
                _param2Label.Text = string.Empty;
                if (_param1Cell != null) _param1Cell.Visible = false;
                if (_param2Cell != null) _param2Cell.Visible = false;
            }
            else if (type == V2FieldType.LevelLetter)
            {
                _param1Label.Text = "起始字母";
                _param2Label.Text = "统计范围";
                _param2Box.Visible = false;
                _scopeBox.Visible = true;
            }
            else if (type == V2FieldType.LevelNumber)
            {
                _param1Label.Text = "起始数字";
                _param2Label.Text = "位数";
            }
            else if (type == V2FieldType.SiblingIndex)
            {
                _param1Label.Text = "起始数字";
                _param2Label.Text = "位数";
            }
            else if (type == V2FieldType.GlobalSeq)
            {
                _param1Label.Text = "起始数字";
                _param2Label.Text = "位数";
            }
            else if (type == V2FieldType.CustomProperty)
            {
                _param1Label.Text = "属性名";
                _param2Label.Text = "默认值";
            }
            else if (type == V2FieldType.OriginalName)
            {
                _param1Label.Text = string.Empty;
                _param2Label.Text = string.Empty;
                if (_param1Cell != null) _param1Cell.Visible = false;
                if (_param2Cell != null) _param2Cell.Visible = false;
            }
        }

        private void RenderExample()
        {
            if (_showingProjectCode)
            {
                _exampleLabel.Text = "项目号：" + (_config.ProjectCode ?? string.Empty);
                return;
            }

            if (_config.Mode == V2RenameMode.FindReplace)
            {
                string sample = "旧项目-零件.SLDPRT";
                string next = string.IsNullOrEmpty(_config.FindText) ? sample : sample.Replace(_config.FindText, _config.ReplaceText ?? string.Empty);
                _exampleLabel.Text = next;
                return;
            }

            string example = string.Join(string.Empty, ActiveFields().Select(PreviewField).Where(s => !string.IsNullOrEmpty(s)).ToArray());
            _exampleLabel.Text = (string.IsNullOrWhiteSpace(example) ? "原文件名" : example) + ".SLDPRT";
        }

        private string PreviewField(V2RuleField field)
        {
            string value = FieldValuePreview(field);
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return (field.Joiner ?? string.Empty) + value;
        }

        private string FieldChipText(V2RuleField field)
        {
            if (field == null)
            {
                return string.Empty;
            }

            string type = FieldTypeLabel(field.Type);
            if (field.Type == V2FieldType.FixedText)
            {
                return type + "：" + (string.IsNullOrWhiteSpace(field.Param1) ? "项目号" : field.Param1);
            }
            if (field.Type == V2FieldType.ParentName)
            {
                return type + "：父级新名";
            }
            if (field.Type == V2FieldType.LevelLetter)
            {
                return type + "：" + (string.IsNullOrWhiteSpace(field.Param1) ? "A" : field.Param1);
            }
            if (field.Type == V2FieldType.LevelNumber)
            {
                return type + "：" + FieldValuePreview(field);
            }
            if (field.Type == V2FieldType.SiblingIndex)
            {
                return type + "：" + FieldValuePreview(field);
            }
            if (field.Type == V2FieldType.GlobalSeq)
            {
                return type + "：" + FieldValuePreview(field);
            }
            if (field.Type == V2FieldType.CustomProperty)
            {
                return type + "：" + (string.IsNullOrWhiteSpace(field.Param1) ? "图号" : field.Param1);
            }
            if (field.Type == V2FieldType.OriginalName)
            {
                return type + "：保留";
            }
            return type;
        }

        private string FieldValuePreview(V2RuleField field)
        {
            if (field.Type == V2FieldType.FixedText) return string.IsNullOrWhiteSpace(field.Param1) ? "文本" : field.Param1;
            if (field.Type == V2FieldType.ParentName) return string.IsNullOrWhiteSpace(_config.ProjectCode) ? "项目号" : _config.ProjectCode;
            if (field.Type == V2FieldType.LevelLetter) return string.IsNullOrWhiteSpace(field.Param1) ? "A" : field.Param1;
            if (field.Type == V2FieldType.LevelNumber) return PreviewNumber(field.Param1, field.Param2, 1, 2);
            if (field.Type == V2FieldType.SiblingIndex) return PreviewNumber(field.Param1, field.Param2, 1, 3);
            if (field.Type == V2FieldType.GlobalSeq) return PreviewNumber(field.Param1, field.Param2, 1, 3);
            if (field.Type == V2FieldType.CustomProperty) return string.IsNullOrWhiteSpace(field.Param1) ? "属性" : field.Param1;
            if (field.Type == V2FieldType.OriginalName) return "原文件名";
            return string.Empty;
        }

        private static string PreviewNumber(string startText, string widthText, int fallbackStart, int fallbackWidth)
        {
            int start;
            int width;
            if (!int.TryParse(startText, out start))
            {
                start = fallbackStart;
            }
            if (!int.TryParse(widthText, out width))
            {
                width = fallbackWidth;
            }
            width = Math.Max(1, Math.Min(12, width));
            return start.ToString(new string('0', width));
        }

        private void UpdateSelectedFromEditor()
        {
            List<V2RuleField> fields = ActiveFields();
            if (_selectedIndex < 0 || _selectedIndex >= fields.Count)
            {
                return;
            }

            V2RuleField field = fields[_selectedIndex];
            field.Joiner = _joinerBox.Text ?? string.Empty;
            V2FieldType selectedType = (V2FieldType)Math.Max(0, _typeBox.SelectedIndex);
            if (field.Type != selectedType)
            {
                V2RuleConfig.ResetFieldForType(field, selectedType);
                return;
            }

            field.Name = FieldTypeLabel(field.Type);
            if (field.Type == V2FieldType.OriginalName)
            {
                field.Param1 = string.Empty;
                field.Param2 = string.Empty;
            }
            else
            {
                field.Param1 = _param1Box.Text ?? string.Empty;
                field.Param2 = field.Type == V2FieldType.LevelLetter ? ScopeDisplay(Convert.ToString(_scopeBox.SelectedItem ?? "仅装配体")) : (_param2Box.Text ?? string.Empty);
            }
        }

        private void SetEditorEnabled(bool enabled)
        {
            foreach (Control control in AllControls(_editorPanel))
            {
                TextBox textBox = control as TextBox;
                ComboBox comboBox = control as ComboBox;
                Button button = control as Button;
                if (textBox != null || comboBox != null || button != null)
                {
                    control.Enabled = enabled;
                }
            }
            _addFieldButton.Enabled = true;
            _deleteFieldButton.Enabled = enabled && ActiveFields().Count > 0;
            _moveLeftButton.Enabled = enabled && _selectedIndex > 0;
            _moveRightButton.Enabled = enabled && _selectedIndex >= 0 && _selectedIndex < ActiveFields().Count - 1;
        }

        private List<V2RuleField> ActiveFields()
        {
            return _activeRule == V2RuleTarget.Assembly ? _config.AssemblyFields : _config.PartFields;
        }

        private int SelectedIndex()
        {
            return _selectedIndex < 0 ? ActiveFields().Count - 1 : _selectedIndex;
        }

        private void InsertField(int index)
        {
            List<V2RuleField> fields = ActiveFields();
            int safeIndex = Math.Max(0, Math.Min(fields.Count, index));
            V2RuleField field = V2RuleConfig.DefaultField(_nextFieldId++, V2FieldType.FixedText, "-");
            fields.Insert(safeIndex, field);
            _selectedIndex = safeIndex;
            MarkSettingsDirty();
            RenderAll();
            MarkPreviewDirty();
        }

        private void DeleteSelected()
        {
            List<V2RuleField> fields = ActiveFields();
            if (_selectedIndex < 0 || _selectedIndex >= fields.Count)
            {
                return;
            }

            fields.RemoveAt(_selectedIndex);
            _selectedIndex = fields.Count == 0 ? -1 : Math.Max(0, Math.Min(_selectedIndex, fields.Count - 1));
            MarkSettingsDirty();
            RenderAll();
            MarkPreviewDirty();
        }

        private void MoveSelected(int direction)
        {
            List<V2RuleField> fields = ActiveFields();
            int target = _selectedIndex + direction;
            if (_selectedIndex < 0 || target < 0 || target >= fields.Count)
            {
                return;
            }

            V2RuleField temp = fields[_selectedIndex];
            fields[_selectedIndex] = fields[target];
            fields[target] = temp;
            _selectedIndex = target;
            MarkSettingsDirty();
            RenderAll();
            MarkPreviewDirty();
        }

        private void ReadButton_Click(object sender, EventArgs e)
        {
            if (!EnsureSolidWorksConnected())
            {
                MessageBox.Show("没有连接到正在运行的 SolidWorks。请先打开 SolidWorks；如果已经打开，请确认工具和 SolidWorks 使用相同权限运行。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            object active = GetCurrentAssemblyDoc();
            if (active == null)
            {
                MessageBox.Show("没有找到已打开的 SolidWorks 装配体。请先在 SolidWorks 中打开 .SLDASM 装配体。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _activeModel = active;
            _previewIsCurrent = false;
            BeginBusy();
            SetCommandInProgress(true);
            try
            {
                AssemblyComponentReader reader = new AssemblyComponentReader();
                _tasks.Clear();
                _tasks.AddRange(reader.Build(active, i => { _summaryLabel.Text = "已读取 " + i + " 个组件"; _summaryLabel.Refresh(); }));
                _componentNodes = reader.GetNodes();
                BindGrid();
                AppendLog("读取完成。");
                _previewButton.Enabled = true;
                _executeButton.Enabled = false;
                _diagnosticButton.Enabled = false;
            }
            finally
            {
                SetCommandInProgress(false);
                EndBusy();
            }
        }

        private void PreviewButton_Click(object sender, EventArgs e)
        {
            if (!EnsureSolidWorksConnected())
            {
                MessageBox.Show("没有连接到正在运行的 SolidWorks，无法生成真实装配预览。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (_activeModel == null)
            {
                _activeModel = GetCurrentAssemblyDoc();
            }

            if (_activeModel == null)
            {
                MessageBox.Show("没有找到已打开的 SolidWorks 装配体。请先读取当前装配体。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            SaveUiToConfig();
            BeginBusy();
            SetCommandInProgress(true);
            try
            {
                V2RenamePlanner planner = new V2RenamePlanner(_config);
                _tasks.Clear();
                if (_componentNodes == null)
                {
                    _tasks.AddRange(planner.Build(_activeModel, i => { _summaryLabel.Text = "正在计算 " + i + " 个组件"; _summaryLabel.Refresh(); }));
                }
                else
                {
                    _tasks.AddRange(planner.BuildFromNodes(_componentNodes, _activeModel, i => { _summaryLabel.Text = "正在计算 " + i + " 个组件"; _summaryLabel.Refresh(); }));
                }
                BindGrid();
                _previewIsCurrent = true;
                _previewButton.Enabled = false;
                _executeButton.Enabled = CanExecutePreview();
                _diagnosticButton.Enabled = _previewIsCurrent && _tasks.Count > 0;
                AppendLog("预览完成。");
            }
            finally
            {
                SetCommandInProgress(false);
                EndBusy();
            }
        }

        private void ExecuteButton_Click(object sender, EventArgs e)
        {
            if (!CanExecutePreview())
            {
                MessageBox.Show("请先处理冲突或错误行：冲突行选择修改确认覆盖，或选择跳过。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int overwriteCount = _tasks.Count(t => t.Status == RenameStatus.Pending && t.UserConfirmedOverwriteTarget);
            if (_confirmOverwriteBox.Checked && overwriteCount > 0)
            {
                DialogResult result = MessageBox.Show(
                    "本次将把 " + overwriteCount + " 个已有同名目标文件移到回收站，然后继续重命名。\r\n\r\n是否继续执行？",
                    Text,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (result != DialogResult.Yes)
                {
                    AppendLog("已取消执行：用户未确认覆盖同名文件。");
                    return;
                }
            }

            DialogAutoConfirmer confirmer = null;
            bool oldTopMost = TopMost;
            try
            {
                TopMost = true;
                Activate();
                if (_autoConfirmDialogsBox.Checked)
                {
                    confirmer = DialogAutoConfirmer.Start();
                }

                BeginBusy();
                SetCommandInProgress(true);
                RenameExecutor executor = new RenameExecutor(_swApp);
                executor.Execute(_tasks);
                BindGrid();
                _executeButton.Enabled = CanExecutePreview();
                _diagnosticButton.Enabled = _previewIsCurrent && _tasks.Count > 0;
                AppendExecutionSummary();
                string logPath = WriteExecutionDiagnostics(BuildFormalExecutionLog());
                if (!string.IsNullOrWhiteSpace(logPath))
                {
                    AppendLog("执行日志：" + logPath);
                }
            }
            finally
            {
                SetCommandInProgress(false);
                if (confirmer != null)
                {
                    confirmer.Dispose();
                }
                TopMost = oldTopMost;
                Activate();
                EndBusy();
            }
        }

        private void DiagnosticButton_Click(object sender, EventArgs e)
        {
            if (!_previewIsCurrent || _tasks.Count == 0)
            {
                MessageBox.Show("请先生成预览，再运行高级诊断。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!EnsureSolidWorksConnected())
            {
                MessageBox.Show("没有连接到正在运行的 SolidWorks，无法运行高级诊断。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DialogResult confirm = MessageBox.Show(
                "高级诊断会真实调用 SolidWorks 改名接口，可能改变当前打开项目。\r\n\r\n请只在复制出来的测试项目中运行。是否继续？",
                Text,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes)
            {
                return;
            }

            bool oldTopMost = TopMost;
            TopMost = true;
            Activate();
            BeginBusy();
            try
            {
                RenameDiagnosticRunner runner = new RenameDiagnosticRunner(_swApp);
                RenameDiagnosticResult result = runner.Run(_tasks);
                AppendLog("高级诊断完成。");
                AppendLog("诊断日志：" + result.TextPath);
                AppendLog("诊断表格：" + result.CsvPath);
            }
            catch (Exception ex)
            {
                AppendLog("高级诊断失败：" + ex.Message);
                MessageBox.Show("高级诊断失败：" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                TopMost = oldTopMost;
                Activate();
                EndBusy();
            }
        }

        private void AppendExecutionSummary()
        {
            int completed = _tasks.Count(t => t.Status == RenameStatus.Renamed || t.Status == RenameStatus.Saved);
            int errors = _tasks.Count(t => t.Status == RenameStatus.Error);
            int skipped = _tasks.Count(t => t.Status == RenameStatus.Skipped);
            int pending = _tasks.Count(t => t.Status == RenameStatus.Pending);

            string text = "执行结果：完成=" + completed + "，错误=" + errors + "，跳过=" + skipped;
            if (pending > 0)
            {
                text += "，未执行=" + pending;
            }

            if (errors > 0)
            {
                text += "。请查看零件列表“原因”列。";
            }
            else
            {
                text += "。";
            }

            AppendLog(text);
        }

        private string WriteExecutionDiagnostics(IList<string> diagnostics)
        {
            if (diagnostics == null || diagnostics.Count == 0)
            {
                return string.Empty;
            }

            try
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SolidWorksTeamRenameTool");
                Directory.CreateDirectory(folder);

                string path = Path.Combine(folder, "last-execution-log.txt");
                File.WriteAllLines(path, diagnostics.ToArray(), Encoding.UTF8);
                return path;
            }
            catch (Exception ex)
            {
                AppendLog("执行诊断日志保存失败：" + ex.Message);
                return string.Empty;
            }
        }

        private IList<string> BuildFormalExecutionLog()
        {
            var lines = new List<string>();
            lines.Add(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " 层级命名工具执行摘要");

            int completed = _tasks.Count(t => t.Status == RenameStatus.Renamed || t.Status == RenameStatus.Saved);
            int errors = _tasks.Count(t => t.Status == RenameStatus.Error);
            int skipped = _tasks.Count(t => t.Status == RenameStatus.Skipped);
            int pending = _tasks.Count(t => t.Status == RenameStatus.Pending);
            lines.Add("完成=" + completed + "，错误=" + errors + "，跳过=" + skipped + "，未执行=" + pending);
            lines.Add("状态,类型,层级,原文件名,新文件名,重复数,原因");

            foreach (RenameTask task in _tasks)
            {
                lines.Add(string.Join(",",
                    CsvLog(StatusText(task)),
                    CsvLog(KindText(task.Kind)),
                    CsvLog(task.Level.ToString()),
                    CsvLog(Path.GetFileName(task.OldPath ?? string.Empty)),
                    CsvLog(string.IsNullOrWhiteSpace(task.NewFileName) ? Path.GetFileName(task.NewPath ?? string.Empty) : task.NewFileName),
                    CsvLog(Math.Max(1, task.DuplicateCount).ToString()),
                    CsvLog(task.Reason)));
            }

            return lines;
        }

        private static string CsvLog(string value)
        {
            string s = value ?? string.Empty;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        private void BindGrid()
        {
            _bindingGrid = true;
            _gridRows.Clear();
            foreach (RenameTask task in _tasks)
            {
                _gridRows.Add(new GridRow
                {
                    Task = task,
                    Status = StatusText(task),
                    Kind = KindText(task.Kind),
                    Level = task.Level,
                    ParentPath = task.ParentPath,
                    DirectoryName = LastDirectoryName(task.OldPath),
                    CurrentSegment = task.CurrentSegment,
                    OldName = Path.GetFileName(task.OldPath ?? string.Empty),
                    NewName = string.IsNullOrWhiteSpace(task.NewFileName) ? Path.GetFileName(task.NewPath ?? string.Empty) : task.NewFileName,
                    DuplicateCount = Math.Max(1, task.DuplicateCount),
                    Reason = CompactText(task.Reason, 18),
                    FullReason = task.Reason
                });
            }

            _grid.DataSource = null;
            EnsureGridColumns();
            _grid.DataSource = _gridRows;
            ConfigureGridColumns();
            UpdateSummary();
            _bindingGrid = false;
        }

        private void EnsureGridColumns()
        {
            _grid.AutoGenerateColumns = false;
            if (_grid.Columns.Count > 0)
            {
                return;
            }

            DataGridViewComboBoxColumn statusColumn = new DataGridViewComboBoxColumn
            {
                Name = "Status",
                HeaderText = "状态",
                DataPropertyName = "Status",
                FlatStyle = FlatStyle.Popup
            };
            statusColumn.Items.AddRange(new object[] { "修改", "跳过", "冲突", "错误", "已读取", "完成" });
            _grid.Columns.Add(statusColumn);
            _grid.Columns.Add("Kind", "类型");
            _grid.Columns.Add("Level", "层级");
            _grid.Columns.Add("DirectoryName", "目录");
            _grid.Columns.Add("CurrentSegment", "当前段");
            _grid.Columns.Add("OldName", "原文件名");
            _grid.Columns.Add("NewName", "新文件名");
            _grid.Columns.Add("DuplicateCount", "重复数");
            _grid.Columns.Add("Reason", "原因");

            foreach (DataGridViewColumn column in _grid.Columns)
            {
                column.DataPropertyName = column.Name;
                column.SortMode = DataGridViewColumnSortMode.NotSortable;
                column.ReadOnly = column.Name != "Status";
            }
        }

        private void ConfigureGridColumns()
        {
            SetColumn("Status", 46, 7);
            SetColumn("Kind", 48, 7);
            SetColumn("Level", 36, 5);
            SetColumn("DirectoryName", 74, 10);
            SetColumn("CurrentSegment", 56, 7);
            SetColumn("OldName", 120, 22);
            SetColumn("NewName", 150, 28);
            SetColumn("DuplicateCount", 48, 6);
            SetColumn("Reason", 82, 10);
        }

        private void SetColumn(string name, int minWidth, float fillWeight)
        {
            if (!_grid.Columns.Contains(name))
            {
                return;
            }

            DataGridViewColumn column = _grid.Columns[name];
            column.MinimumWidth = minWidth;
            column.FillWeight = fillWeight;
        }

        private void Grid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _gridRows.Count)
            {
                return;
            }

            RenameStatus status = _gridRows[e.RowIndex].Task.Status;
            DataGridViewRow row = _grid.Rows[e.RowIndex];
            if (status == RenameStatus.Pending)
            {
                row.DefaultCellStyle.BackColor = Color.FromArgb(232, 248, 235);
                row.DefaultCellStyle.ForeColor = Color.FromArgb(18, 98, 49);
            }
            else if (status == RenameStatus.Conflict || status == RenameStatus.Error)
            {
                row.DefaultCellStyle.BackColor = Color.FromArgb(255, 238, 238);
                row.DefaultCellStyle.ForeColor = Color.FromArgb(150, 45, 45);
            }
            else if (status == RenameStatus.Skipped)
            {
                row.DefaultCellStyle.BackColor = Color.FromArgb(248, 248, 248);
                row.DefaultCellStyle.ForeColor = Color.FromArgb(116, 71, 71);
            }
            else
            {
                row.DefaultCellStyle.BackColor = SurfaceColor;
                row.DefaultCellStyle.ForeColor = TextColor;
            }
        }

        private void Grid_CellToolTipTextNeeded(object sender, DataGridViewCellToolTipTextNeededEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _gridRows.Count || e.ColumnIndex < 0)
            {
                return;
            }

            RenameTask task = _gridRows[e.RowIndex].Task;
            string columnName = _grid.Columns[e.ColumnIndex].Name;
            if (columnName == "DirectoryName" || columnName == "OldName")
            {
                e.ToolTipText = task.OldPath ?? string.Empty;
            }
            else if (columnName == "NewName")
            {
                e.ToolTipText = task.NewPath ?? string.Empty;
            }
            else if (columnName == "Reason")
            {
                e.ToolTipText = task.Reason ?? string.Empty;
            }
            else if (columnName == "CurrentSegment")
            {
                e.ToolTipText = string.IsNullOrWhiteSpace(task.ParentPath) ? (task.CurrentSegment ?? string.Empty) : "父级：" + task.ParentPath;
            }
        }

        private void Grid_CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            if (_grid.IsCurrentCellDirty)
            {
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        }

        private void Grid_CellBeginEdit(object sender, DataGridViewCellCancelEventArgs e)
        {
            if (e.RowIndex < 0 || !_grid.Columns[e.ColumnIndex].Name.Equals("Status", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (e.RowIndex >= _gridRows.Count || !CanEditStatus(_gridRows[e.RowIndex].Task))
            {
                e.Cancel = true;
            }
        }

        private void Grid_EditingControlShowing(object sender, DataGridViewEditingControlShowingEventArgs e)
        {
            if (_grid.CurrentCell == null || !_grid.Columns[_grid.CurrentCell.ColumnIndex].Name.Equals("Status", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ComboBox combo = e.Control as ComboBox;
            if (combo == null)
            {
                return;
            }

            combo.Items.Clear();
            combo.Items.Add(StatusText(RenameStatus.Pending));
            combo.Items.Add(StatusText(RenameStatus.Skipped));
        }

        private void Grid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_bindingGrid || e.RowIndex < 0 || e.RowIndex >= _gridRows.Count)
            {
                return;
            }

            if (!_grid.Columns[e.ColumnIndex].Name.Equals("Status", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            GridRow row = _gridRows[e.RowIndex];
            RenameTask task = row.Task;
            string status = Convert.ToString(_grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value ?? string.Empty);

            if (!CanEditStatus(task))
            {
                row.Status = StatusText(task);
                _grid.InvalidateRow(e.RowIndex);
                return;
            }

            if (status == StatusText(RenameStatus.Pending))
            {
                SetTaskToPendingFromGrid(e.RowIndex, task);
            }
            else if (status == StatusText(RenameStatus.Skipped))
            {
                task.Status = RenameStatus.Skipped;
                task.SkippedByParent = false;
                task.UserConfirmedOverwriteTarget = false;
                task.Reason = "用户手动跳过。";
            }
            else
            {
                row.Status = StatusText(task);
                _grid.InvalidateRow(e.RowIndex);
                return;
            }

            row.Status = StatusText(task);
            row.Reason = task.Reason;
            RefreshRowsFromTasks();
            UpdateSummary();
            _executeButton.Enabled = CanExecutePreview();
            _grid.InvalidateRow(e.RowIndex);
        }

        private void SetTaskToPendingFromGrid(int rowIndex, RenameTask task)
        {
            if (HasActiveDuplicateTarget(task))
            {
                task.Status = RenameStatus.Conflict;
                task.PreviewDuplicateTargetConflict = true;
                task.SkippedByParent = false;
                task.UserConfirmedOverwriteTarget = false;
                task.Reason = "本次预览还有其他文件使用相同新文件名，请先将重复项改为跳过。";
                return;
            }

            string oldReason = task.Reason;
            task.Status = RenameStatus.Pending;
            task.PreviewDuplicateTargetConflict = false;
            task.SkippedByParent = false;

            if (task.TargetExistsConflict)
            {
                task.UserConfirmedOverwriteTarget = true;
                task.Reason = "用户确认覆盖已有同名目标文件，执行前将移到回收站。";
                return;
            }

            task.UserConfirmedOverwriteTarget = false;
            task.Reason = string.IsNullOrWhiteSpace(oldReason) ? "用户手动改为修改。" : "用户手动改为修改。原原因：" + oldReason;
        }

        private bool HasActiveDuplicateTarget(RenameTask task)
        {
            string target = SafeFullPath(task == null ? null : task.NewPath);
            if (string.IsNullOrWhiteSpace(target))
            {
                return false;
            }

            foreach (RenameTask other in _tasks)
            {
                if (ReferenceEquals(other, task) || other.Status == RenameStatus.Skipped || other.Status == RenameStatus.Error)
                {
                    continue;
                }

                string otherTarget = SafeFullPath(other.NewPath);
                if (string.Equals(target, otherTarget, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private bool CanEditStatus(RenameTask task)
        {
            if (!_previewIsCurrent || task == null)
            {
                return false;
            }

            if (task.SkippedByParent)
            {
                return false;
            }

            if (task.Status != RenameStatus.Pending && task.Status != RenameStatus.Skipped && task.Status != RenameStatus.Conflict)
            {
                return false;
            }

            if (task.Kind == RenameKind.Skip || task.Kind == RenameKind.Error)
            {
                return false;
            }

            if (task.IsVirtual)
            {
                if (task.Component == null)
                {
                    return false;
                }
            }
            else if (task.IsTop)
            {
                if (task.Model == null)
                {
                    return false;
                }
            }
            else if (task.Component == null || string.IsNullOrWhiteSpace(task.OldPath) || string.IsNullOrWhiteSpace(task.NewPath))
            {
                return false;
            }

            string reason = task.Reason ?? string.Empty;
            if (reason.IndexOf("只读", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("无法获取 ModelDoc2", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("不是 SLDASM/SLDPRT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("虚拟或未保存", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("压缩组件", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("无效组件", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            return true;
        }

        private static string SafeFullPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(path);
            }
            catch
            {
                return path;
            }
        }

        private static string LastDirectoryName(string path)
        {
            try
            {
                string directory = Path.GetDirectoryName(path ?? string.Empty);
                if (string.IsNullOrWhiteSpace(directory))
                {
                    return string.Empty;
                }

                return Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string CompactText(string text, int maxLength)
        {
            string value = (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            if (maxLength < 4 || value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(0, maxLength - 3) + "...";
        }

        private void RefreshRowsFromTasks()
        {
            foreach (GridRow gridRow in _gridRows)
            {
                RenameTask task = gridRow.Task;
                gridRow.Status = StatusText(task);
                gridRow.DirectoryName = LastDirectoryName(task.OldPath);
                gridRow.Reason = CompactText(task.Reason, 18);
                gridRow.FullReason = task.Reason;
            }
            _grid.Refresh();
        }

        private void UpdateSummary()
        {
            int pending = _tasks.Count(t => t.Status == RenameStatus.Pending);
            int skipped = _tasks.Count(t => t.Status == RenameStatus.Skipped);
            int conflicts = _tasks.Count(t => t.Status == RenameStatus.Conflict);
            int errors = _tasks.Count(t => t.Status == RenameStatus.Error);
            _summaryLabel.Text = "待执行=" + pending + "，跳过=" + skipped + "，冲突=" + conflicts + "，错误=" + errors;
        }

        private bool CanExecutePreview()
        {
            return _previewIsCurrent &&
                _tasks.Any(t => t.Status == RenameStatus.Pending) &&
                !_tasks.Any(t => t.Status == RenameStatus.Conflict || t.Status == RenameStatus.Error);
        }

        private void MarkPreviewDirty()
        {
            _previewIsCurrent = false;
            _previewButton.Enabled = true;
            _executeButton.Enabled = false;
        }

        private object GetCurrentAssemblyDoc()
        {
            EnsureSolidWorksConnected();
            object active = GetActiveDoc(_swApp);
            if (active == null)
            {
                AppendLog("未读取：SolidWorks 当前没有活动文档。");
                return null;
            }

            if (!LooksLikeAssemblyDocument(active))
            {
                AppendLog("未读取：当前活动文档不是装配体，或无法识别为 .SLDASM。");
                return null;
            }

            return active;
        }

        private bool EnsureSolidWorksConnected()
        {
            if (_swApp != null)
            {
                return true;
            }

            _swApp = GetRunningSolidWorks();
            if (_swApp != null)
            {
                AppendLog("已连接到 SolidWorks。");
                return true;
            }

            AppendLog("未检测到正在运行的 SolidWorks。");
            return false;
        }

        private void SetCommandInProgress(bool value)
        {
            if (_swApp == null)
            {
                return;
            }

            try
            {
                _swApp.GetType().InvokeMember(
                    "CommandInProgress",
                    System.Reflection.BindingFlags.SetProperty,
                    null,
                    _swApp,
                    new object[] { value });
            }
            catch
            {
            }
        }

        private void BeginBusy()
        {
            _busyBar.Visible = true;
            UseWaitCursor = true;
            _readButton.Enabled = false;
            _previewButton.Enabled = false;
            _executeButton.Enabled = false;
            Refresh();
        }

        private void EndBusy()
        {
            UseWaitCursor = false;
            Cursor = Cursors.Default;
            _grid.Cursor = Cursors.Default;
            _busyBar.Visible = false;
            _readButton.Enabled = true;
            _previewButton.Enabled = !_previewIsCurrent;
            _executeButton.Enabled = CanExecutePreview();
            Refresh();
        }

        private void AppendLog(string text)
        {
            if (string.IsNullOrWhiteSpace(_logBox.Text) || _logBox.Text == "等待操作。")
            {
                _logBox.Text = text;
            }
            else
            {
                _logBox.AppendText(Environment.NewLine + text);
            }
        }

        private void ApplyLayout()
        {
            SetSafeSplitterDistance(_workspaceSplit, 366, 350, 620);
            SetSafeSplitterDistance(_rightSplit, _paramsExpanded ? 300 : 224, _paramsExpanded ? 292 : 214, 260);
        }

        private static void SetSafeSplitterDistance(SplitContainer split, int preferred, int panel1Min, int panel2Min)
        {
            if (split == null)
            {
                return;
            }

            int length = split.Orientation == Orientation.Vertical ? split.Width : split.Height;
            int max = length - split.SplitterWidth - panel2Min;
            int min = panel1Min;
            if (max < min)
            {
                return;
            }

            try
            {
                split.SplitterDistance = Math.Max(min, Math.Min(preferred, max));
            }
            catch
            {
            }
        }

        private static string StatusText(RenameStatus status)
        {
            if (status == RenameStatus.Pending) return "修改";
            if (status == RenameStatus.Loaded) return "已读取";
            if (status == RenameStatus.Skipped) return "跳过";
            if (status == RenameStatus.Conflict) return "冲突";
            if (status == RenameStatus.Renamed || status == RenameStatus.Saved) return "完成";
            if (status == RenameStatus.Error) return "错误";
            return status.ToString();
        }

        private static string StatusText(RenameTask task)
        {
            return task == null ? string.Empty : StatusText(task.Status);
        }

        private static string KindText(RenameKind kind)
        {
            if (kind == RenameKind.TopAssembly) return "总装";
            if (kind == RenameKind.Assembly) return "装配体";
            if (kind == RenameKind.Part) return "零件";
            if (kind == RenameKind.Skip) return "跳过";
            if (kind == RenameKind.Error) return "错误";
            return kind.ToString();
        }

        private static string FieldTypeLabel(V2FieldType type)
        {
            return V2RuleConfig.FieldTypeLabel(type);
        }

        private static string ScopeDisplay(string value)
        {
            return V2RuleConfig.NormalizeScopeText(value);
        }

        private static int GetDocumentType(object model)
        {
            if (model == null) return 0;
            object type2 = TryInvoke(model, "GetType2");
            try { return type2 == null ? 0 : Convert.ToInt32(type2); } catch { }
            object value = TryInvoke(model, "GetType");
            try { return value == null ? 0 : Convert.ToInt32(value); } catch { return 0; }
        }

        private static object GetActiveDoc(object swApp)
        {
            object active = TryGetProperty(swApp, "ActiveDoc");
            if (active != null) return active;
            active = TryGetProperty(swApp, "IActiveDoc2");
            if (active != null) return active;
            active = TryInvoke(swApp, "IActiveDoc2");
            if (active != null) return active;
            return TryInvoke(swApp, "ActiveDoc");
        }

        private static object TryInvoke(object target, string name, params object[] args)
        {
            if (target == null) return null;
            try { return target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, args); } catch { return null; }
        }

        private static object TryGetProperty(object target, string name)
        {
            if (target == null) return null;
            try { return target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null); } catch { return null; }
        }

        private static string TryGetString(object target, string methodName)
        {
            object value = TryInvoke(target, methodName);
            return value == null ? string.Empty : Convert.ToString(value);
        }

        private static bool LooksLikeAssemblyDocument(object model)
        {
            if (model == null)
            {
                return false;
            }

            int docType = GetDocumentType(model);
            if (docType == SwDocAssembly)
            {
                return true;
            }

            string path = TryGetString(model, "GetPathName");
            if (path.EndsWith(".SLDASM", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string title = TryGetString(model, "GetTitle");
            if (title.EndsWith(".SLDASM", StringComparison.OrdinalIgnoreCase) ||
                title.EndsWith(".SLDASM*", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            object configuration = TryGetProperty(TryGetProperty(model, "ConfigurationManager"), "ActiveConfiguration");
            if (configuration == null)
            {
                configuration = TryInvoke(TryGetProperty(model, "ConfigurationManager"), "ActiveConfiguration");
            }

            object root = TryInvoke(configuration, "GetRootComponent3", true);
            return root != null;
        }

        private static object GetRunningSolidWorks()
        {
            foreach (string progId in new[] { "SldWorks.Application", "SolidWorks.Application" })
            {
                try
                {
                    object app = Marshal.GetActiveObject(progId);
                    if (app != null)
                    {
                        return app;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static Panel CreateEditorCell(string labelText, Control input)
        {
            Label label = new Label { Text = labelText };
            return CreateEditorCell(label, input, null);
        }

        private static Panel CreateEditorCell(Label label, Control input)
        {
            return CreateEditorCell(label, input, null);
        }

        private static Panel CreateEditorCell(Label label, Control input, Control alternateInput)
        {
            Panel panel = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 10, 0),
                BackColor = SurfaceColor
            };

            label.Dock = DockStyle.Top;
            label.Height = 18;
            label.ForeColor = MutedTextColor;
            label.TextAlign = ContentAlignment.BottomLeft;
            panel.Controls.Add(label);

            StyleEditorInput(input);
            panel.Controls.Add(input);

            if (alternateInput != null)
            {
                StyleEditorInput(alternateInput);
                panel.Controls.Add(alternateInput);
            }

            return panel;
        }

        private static void StyleEditorInput(Control input)
        {
            input.Dock = DockStyle.Bottom;
            input.Height = 25;
            TextBox textBox = input as TextBox;
            if (textBox != null)
            {
                textBox.BorderStyle = BorderStyle.FixedSingle;
                textBox.BackColor = SurfaceColor;
                textBox.ForeColor = TextColor;
            }

            ComboBox comboBox = input as ComboBox;
            if (comboBox != null)
            {
                comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
                comboBox.FlatStyle = FlatStyle.Standard;
                comboBox.BackColor = SurfaceColor;
                comboBox.ForeColor = TextColor;
            }
        }

        private static GroupBox CreateGroup(string title, int height)
        {
            return new GtkGroupBox
            {
                Text = title,
                Height = height,
                BackColor = SurfaceColor,
                ForeColor = TextColor,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular)
            };
        }

        private static void AddLabel(Control parent, string text, int left, int top, int width)
        {
            parent.Controls.Add(new Label { Text = text, Location = new Point(left, top), Size = new Size(width, 18), ForeColor = MutedTextColor });
        }

        private static void PlaceText(TextBox box, Control parent, int left, int top, int width)
        {
            box.Location = new Point(left, top);
            box.Size = new Size(width, 25);
            box.BorderStyle = BorderStyle.FixedSingle;
            box.BackColor = SurfaceColor;
            box.ForeColor = TextColor;
            parent.Controls.Add(box);
        }

        private static void PlaceCombo(ComboBox box, Control parent, int left, int top, int width)
        {
            box.Location = new Point(left, top);
            box.Size = new Size(width, 25);
            box.DropDownStyle = ComboBoxStyle.DropDownList;
            box.FlatStyle = FlatStyle.Standard;
            box.BackColor = SurfaceColor;
            box.ForeColor = TextColor;
            parent.Controls.Add(box);
        }

        private static void PlaceCheck(CheckBox box, Control parent, string text, int left, int top, int width)
        {
            box.Text = text;
            box.Location = new Point(left, top);
            box.Size = new Size(width, 24);
            box.BackColor = SurfaceColor;
            box.ForeColor = TextColor;
            parent.Controls.Add(box);
        }

        private static void AddTableButton(TableLayoutPanel parent, Button button, string text, bool primary, int column)
        {
            button.Text = text;
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(column == 0 ? 0 : 4, 0, column == 2 ? 0 : 4, 0);
            if (primary) StylePrimaryButton(button); else StyleSecondaryButton(button);
            parent.Controls.Add(button, column, 0);
        }

        private static void AddToolbarButton(FlowLayoutPanel parent, Button button, string text, EventHandler handler)
        {
            button.Text = text;
            button.Size = new Size(112, 32);
            button.Margin = new Padding(0, 0, 2, 0);
            button.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.Padding = Padding.Empty;
            button.Click += handler;
            parent.Controls.Add(button);
        }

        private static void AddSmallButton(FlowLayoutPanel parent, Button button, string text, EventHandler handler)
        {
            button.Text = text;
            button.Size = new Size(48, 28);
            button.Margin = new Padding(0, 0, 6, 0);
            StyleSecondaryButton(button);
            button.Click += handler;
            parent.Controls.Add(button);
        }

        private static void StylePrimaryButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            GtkButton gtk = button as GtkButton;
            if (gtk != null)
            {
                gtk.Active = false;
                gtk.Subtle = false;
            }
            button.BackColor = SoftSurfaceColor;
            button.ForeColor = TextColor;
            button.FlatAppearance.BorderColor = BorderColor;
            button.FlatAppearance.BorderSize = 1;
            button.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.Padding = Padding.Empty;
        }

        private static void StyleSecondaryButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            GtkButton gtk = button as GtkButton;
            if (gtk != null)
            {
                gtk.Active = false;
                gtk.Subtle = true;
            }
            button.BackColor = SurfaceColor;
            button.ForeColor = TextColor;
            button.FlatAppearance.BorderColor = BorderColor;
            button.FlatAppearance.BorderSize = 1;
            button.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular);
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.Padding = Padding.Empty;
        }

        private static void StyleToggleButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            GtkButton gtk = button as GtkButton;
            if (gtk != null)
            {
                gtk.Active = false;
                gtk.Subtle = true;
            }
            button.BackColor = SoftSurfaceColor;
            button.ForeColor = MutedTextColor;
            button.FlatAppearance.BorderColor = BorderColor;
            button.FlatAppearance.BorderSize = 1;
            button.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular);
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.Padding = Padding.Empty;
        }

        private static void SetActiveButton(Button button, bool active)
        {
            GtkButton gtk = button as GtkButton;
            if (gtk != null)
            {
                gtk.Active = active;
                gtk.Subtle = !active;
                gtk.Invalidate();
            }
            button.BackColor = active ? AccentColor : SurfaceColor;
            button.ForeColor = active ? Color.White : TextColor;
            button.FlatAppearance.BorderColor = active ? AccentColor : BorderColor;
            button.Font = new Font("Microsoft YaHei UI", 8.5F, active ? FontStyle.Bold : FontStyle.Regular);
        }
    }
}
