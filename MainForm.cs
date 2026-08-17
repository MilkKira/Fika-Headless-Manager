using FikaHeadlessManager.Models;
using FikaHeadlessManager.Services;
using ReaLTaiizor.Controls;
using ReaLTaiizor.Util;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text.Json;
using Button = System.Windows.Forms.Button;
using Panel = System.Windows.Forms.Panel;

namespace FikaHeadlessManager;

/// <summary>
/// Hosts the ReaLTaiizor dashboard and coordinates all managed headless clients.
/// </summary>
public sealed class MainForm : Form
{
    private const int NonClientLeftButtonDown = 0x00A1;
    private const int CaptionHitTest = 0x0002;
    private static readonly Color AccentColor = Color.FromArgb(79, 70, 229);
    private static readonly Color AccentDarkColor = Color.FromArgb(55, 48, 163);
    private static readonly Color PageColor = Color.FromArgb(247, 248, 252);
    private static readonly Color BorderColor = Color.FromArgb(229, 231, 235);
    private static readonly Color MutedColor = Color.FromArgb(107, 114, 128);
    private static readonly Color SidebarColor = Color.FromArgb(17, 24, 39);

    private readonly ConfigurationStore _configurationStore = new();
    private readonly HeadlessManagerService _managerService;
    private readonly BindingList<ManagerProfile> _profiles = [];
    private readonly BindingSource _profileSource = new();
    private readonly Dictionary<Guid, Queue<ProcessLogEntry>> _logsByProfile = [];
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 5000 };

    private readonly Label _profileCountValue = CreateLabel("0", 22, FontStyle.Bold, Color.FromArgb(17, 24, 39));
    private readonly Label _activeCountValue = CreateLabel("0", 22, FontStyle.Bold, Color.FromArgb(17, 24, 39));
    private readonly Label _lastUpdatedLabel = CreateLabel(string.Empty, 10, FontStyle.Regular, MutedColor);
    private readonly FlowLayoutPanel _sidebarProfiles = new();
    private readonly DataGridView _profileGrid = new();

    private readonly Panel _editorOverlay = new();
    private readonly Panel _editorCard = new();
    private readonly Label _editorTitle = CreateLabel("添加实例", 20, FontStyle.Bold, Color.FromArgb(17, 24, 39));
    private readonly Label _editorError = CreateLabel(string.Empty, 10, FontStyle.Regular, Color.FromArgb(220, 38, 38));
    private readonly HopeTextBox _nameInput = CreateTextBox("实例名称");
    private readonly HopeTextBox _directoryInput = CreateTextBox("SPT 安装目录");
    private readonly HopeTextBox _profileInput = CreateTextBox("配置文件 ID / 令牌");
    private readonly HopeTextBox _backendInput = CreateTextBox("后端地址");
    private readonly HopeTextBox _titleInput = CreateTextBox("窗口标题（可选）");
    private readonly HopeCheckBox _autoRestartInput = CreateCheckBox("自动重启");
    private readonly HopeCheckBox _extraLoggingInput = CreateCheckBox("额外文件日志");

    private readonly Panel _logOverlay = new();
    private readonly Panel _logCard = new();
    private readonly Label _logTitle = CreateLabel("实时输出", 19, FontStyle.Bold, Color.FromArgb(17, 24, 39));
    private readonly Label _logSubtitle = CreateLabel(string.Empty, 10, FontStyle.Regular, MutedColor);
    private readonly HopeComboBox _logCategoryFilter = CreateComboBox();
    private readonly HopeComboBox _logLevelFilter = CreateComboBox();
    private readonly RichTextBox _logOutput = new();

    private ManagerProfile? _editingProfile;
    private Guid? _viewedLogProfileId;
    private bool _allowClose;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainForm"/> class.
    /// </summary>
    public MainForm()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = PageColor;
        ClientSize = new Size(1440, 900);
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        FormBorderStyle = FormBorderStyle.None;
        Icon = LoadApplicationIcon();
        MinimumSize = new Size(1080, 720);
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Fika 无头管理器";

        var titleBar = BuildTitleBar();

        var contentHost = new Panel
        {
            BackColor = PageColor,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        var windowLayout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            RowCount = 2
        };
        windowLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        windowLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        windowLayout.Controls.Add(titleBar, 0, 0);
        windowLayout.Controls.Add(contentHost, 0, 1);
        Controls.Add(windowLayout);
        contentHost.Controls.Add(BuildDashboard());
        BuildEditorOverlay(contentHost);
        BuildLogOverlay(contentHost);

        _profileSource.DataSource = _profiles;
        _profileGrid.DataSource = _profileSource;
        _refreshTimer.Tick += (_, _) => RefreshDashboard();

        _managerService = new HeadlessManagerService();
        _managerService.StateChanged += ManagerService_StateChanged;
        _managerService.LogReceived += ManagerService_LogReceived;

        Load += MainForm_Load;
        FormClosing += MainForm_FormClosing;
        Resize += (_, _) => PositionOverlayCards();
    }

    private Control BuildTitleBar()
    {
        var titleBar = new TableLayoutPanel
        {
            BackColor = AccentColor,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 1
        };
        titleBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        titleBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));

        var dragArea = new BufferedPanel
        {
            BackColor = AccentColor,
            Cursor = Cursors.Default,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };
        var icon = new PictureBox
        {
            Image = Icon?.ToBitmap(),
            Location = new Point(10, 8),
            Size = new Size(24, 24),
            SizeMode = PictureBoxSizeMode.StretchImage
        };
        var title = CreateLabel("Fika 无头管理器 · ReaLTaiizor", 9, FontStyle.Regular, Color.White);
        title.Location = new Point(43, 9);
        dragArea.Controls.Add(icon);
        dragArea.Controls.Add(title);
        AttachTitleBarDrag(dragArea);
        AttachTitleBarDrag(icon);
        AttachTitleBarDrag(title);
        titleBar.Controls.Add(dragArea, 0, 0);

        var controls = new TableLayoutPanel
        {
            BackColor = AccentColor,
            ColumnCount = 3,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 1
        };
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
        var minimizeButton = CreateWindowButton("—");
        minimizeButton.Click += (_, _) => WindowState = FormWindowState.Minimized;
        var maximizeButton = CreateWindowButton("□");
        maximizeButton.Click += (_, _) => ToggleWindowState();
        var closeButton = CreateWindowButton("×", isCloseButton: true);
        closeButton.Click += (_, _) => Close();
        controls.Controls.Add(minimizeButton, 0, 0);
        controls.Controls.Add(maximizeButton, 1, 0);
        controls.Controls.Add(closeButton, 2, 0);
        titleBar.Controls.Add(controls, 1, 0);
        return titleBar;
    }

    private void AttachTitleBarDrag(Control control)
    {
        control.MouseDown += (_, eventArgs) => BeginWindowDrag(eventArgs);
        control.DoubleClick += (_, _) => ToggleWindowState();
    }

    private static Button CreateWindowButton(string text, bool isCloseButton = false)
    {
        var button = new Button
        {
            BackColor = AccentColor,
            Cursor = Cursors.Hand,
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Symbol", 11F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(165, 180, 252),
            Margin = Padding.Empty,
            TabStop = false,
            Text = text,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseDownBackColor = isCloseButton
            ? Color.FromArgb(185, 28, 28)
            : Color.FromArgb(55, 48, 163);
        button.FlatAppearance.MouseOverBackColor = isCloseButton
            ? Color.FromArgb(220, 38, 38)
            : Color.FromArgb(67, 56, 202);
        return button;
    }

    private void ToggleWindowState() =>
        WindowState = WindowState == FormWindowState.Maximized
            ? FormWindowState.Normal
            : FormWindowState.Maximized;

    private Control BuildDashboard()
    {
        var root = new TableLayoutPanel
        {
            BackColor = PageColor,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            RowCount = 1
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(BuildSidebar(), 0, 0);
        root.Controls.Add(BuildMainArea(), 1, 0);
        return root;
    }

    private Control BuildSidebar()
    {
        var sidebar = new TableLayoutPanel
        {
            BackColor = SidebarColor,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 24, 18, 18),
            RowCount = 5
        };
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 128));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));

        var brand = new Panel { Dock = DockStyle.Fill };
        var brandBadge = CreateRoundedPanel(Color.FromArgb(67, 56, 202), 10);
        brandBadge.Bounds = new Rectangle(0, 0, 40, 40);
        brandBadge.Controls.Add(CreateCenteredLabel("FH", 11, FontStyle.Bold, Color.White));
        var brandTitle = CreateLabel("无头控制中心", 10, FontStyle.Bold, Color.White);
        brandTitle.Location = new Point(52, 3);
        var brandCaption = CreateLabel("无头管理器", 9, FontStyle.Regular, Color.FromArgb(140, 150, 168));
        brandCaption.Location = new Point(52, 25);
        brand.Controls.Add(brandBadge);
        brand.Controls.Add(brandTitle);
        brand.Controls.Add(brandCaption);
        sidebar.Controls.Add(brand, 0, 0);

        var selectedNavigation = CreateRoundedPanel(Color.FromArgb(40, 48, 68), 8);
        selectedNavigation.Dock = DockStyle.Fill;
        selectedNavigation.Margin = new Padding(0, 0, 0, 4);
        selectedNavigation.Controls.Add(CreateCenteredLabel("▦   无头主机总览", 10, FontStyle.Bold, Color.White));
        sidebar.Controls.Add(selectedNavigation, 0, 1);

        var actions = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Fill, RowCount = 2 };
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        var addButton = CreateButton("＋ 添加无头实例", HopeButtonType.Primary);
        addButton.Margin = new Padding(0, 10, 0, 5);
        addButton.Click += (_, _) => ShowAddEditor();
        var startAllButton = CreateButton("全部启动", HopeButtonType.Info);
        startAllButton.Margin = new Padding(0, 5, 0, 8);
        startAllButton.Click += async (_, _) => await StartAllAsync();
        actions.Controls.Add(addButton, 0, 0);
        actions.Controls.Add(startAllButton, 0, 1);
        sidebar.Controls.Add(actions, 0, 2);

        var managedHost = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Fill, RowCount = 2 };
        managedHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        managedHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var managedLabel = CreateLabel("已管理无头主机", 9, FontStyle.Bold, Color.FromArgb(107, 114, 128));
        managedLabel.Dock = DockStyle.Fill;
        managedLabel.TextAlign = ContentAlignment.MiddleLeft;
        managedHost.Controls.Add(managedLabel, 0, 0);
        _sidebarProfiles.AutoScroll = true;
        _sidebarProfiles.BackColor = SidebarColor;
        _sidebarProfiles.Dock = DockStyle.Fill;
        _sidebarProfiles.FlowDirection = FlowDirection.TopDown;
        _sidebarProfiles.Padding = new Padding(4, 4, 0, 4);
        _sidebarProfiles.WrapContents = false;
        managedHost.Controls.Add(_sidebarProfiles, 0, 1);
        sidebar.Controls.Add(managedHost, 0, 3);

        var hiddenNotice = CreateRoundedPanel(Color.FromArgb(31, 41, 55), 8);
        hiddenNotice.Dock = DockStyle.Fill;
        hiddenNotice.Margin = new Padding(0, 10, 0, 0);
        var noticeTitle = CreateLabel("控制台隐藏", 9, FontStyle.Bold, Color.FromArgb(165, 180, 252));
        noticeTitle.Location = new Point(12, 10);
        var noticeText = CreateLabel("自动隐藏相关控制台窗口", 8, FontStyle.Regular, Color.FromArgb(148, 163, 184));
        noticeText.AutoSize = false;
        noticeText.Bounds = new Rectangle(12, 34, 182, 32);
        hiddenNotice.Controls.Add(noticeTitle);
        hiddenNotice.Controls.Add(noticeText);
        sidebar.Controls.Add(hiddenNotice, 0, 4);
        return sidebar;
    }

    private Control BuildMainArea()
    {
        var main = new TableLayoutPanel
        {
            BackColor = PageColor,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            RowCount = 2
        };
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new Panel
        {
            BackColor = Color.White,
            Dock = DockStyle.Fill,
            Padding = new Padding(28, 0, 28, 0)
        };
        header.Paint += (_, args) =>
        {
            using var pen = new Pen(BorderColor);
            args.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
        };
        var quote = CreateLabel("眉目山河空恋远，不如怜取眼前人", 11, FontStyle.Bold, Color.FromArgb(30, 41, 59));
        quote.AutoSize = true;
        quote.Location = new Point(28, 27);
        header.Controls.Add(quote);

        var avatar = CreateRoundedPanel(Color.FromArgb(238, 242, 255), 18);
        avatar.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        avatar.Bounds = new Rectangle(header.Width - 190, 18, 36, 36);
        avatar.Controls.Add(CreateCenteredLabel("A", 10, FontStyle.Bold, AccentColor));
        var administrator = CreateLabel("Administrator", 9, FontStyle.Bold, Color.FromArgb(30, 41, 59));
        administrator.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        administrator.Location = new Point(header.Width - 144, 18);
        var role = CreateLabel("无头控制", 8, FontStyle.Regular, MutedColor);
        role.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        role.Location = new Point(header.Width - 144, 39);
        header.Controls.Add(avatar);
        header.Controls.Add(administrator);
        header.Controls.Add(role);
        header.Resize += (_, _) =>
        {
            avatar.Left = header.ClientSize.Width - 190;
            administrator.Left = header.ClientSize.Width - 144;
            role.Left = header.ClientSize.Width - 144;
        };
        main.Controls.Add(header, 0, 0);
        main.Controls.Add(BuildContentArea(), 0, 1);
        return main;
    }

    private Control BuildContentArea()
    {
        var content = new TableLayoutPanel
        {
            BackColor = PageColor,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(28, 22, 28, 28),
            RowCount = 3
        };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 146));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var intro = new Panel { Dock = DockStyle.Fill };
        var title = CreateLabel("运行概览", 20, FontStyle.Bold, Color.FromArgb(17, 24, 39));
        title.Location = new Point(0, 0);
        var subtitle = CreateLabel("在一个窗口中监控并控制所有无头实例。", 9, FontStyle.Regular, MutedColor);
        subtitle.Location = new Point(0, 47);
        _lastUpdatedLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _lastUpdatedLabel.AutoSize = true;
        _lastUpdatedLabel.Location = new Point(intro.Width - 110, 47);
        intro.Controls.Add(title);
        intro.Controls.Add(subtitle);
        intro.Controls.Add(_lastUpdatedLabel);
        intro.Resize += (_, _) => _lastUpdatedLabel.Left = intro.ClientSize.Width - _lastUpdatedLabel.Width;
        content.Controls.Add(intro, 0, 0);

        var metrics = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill, RowCount = 1 };
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var countCard = CreateMetricCard("无头主机数量", _profileCountValue, "已配置的无头主机", Color.FromArgb(2, 132, 199));
        countCard.Margin = new Padding(0, 0, 8, 16);
        var activeCard = CreateMetricCard("活跃会话", _activeCountValue, "当前正在运行", Color.FromArgb(217, 119, 6));
        activeCard.Margin = new Padding(8, 0, 0, 16);
        metrics.Controls.Add(countCard, 0, 0);
        metrics.Controls.Add(activeCard, 1, 0);
        content.Controls.Add(metrics, 0, 1);

        content.Controls.Add(BuildProfileTableCard(), 0, 2);
        return content;
    }

    private Control BuildProfileTableCard()
    {
        var card = CreateRoundedPanel(Color.White, 12, BorderColor);
        card.Dock = DockStyle.Fill;
        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(1),
            RowCount = 2
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(layout);

        var heading = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 14, 20, 10) };
        var title = CreateLabel("已管理无头主机", 12, FontStyle.Bold, Color.FromArgb(17, 24, 39));
        title.Location = new Point(20, 15);
        var subtitle = CreateLabel("启动、停止、重启、编辑或查看实例实时输出。", 8, FontStyle.Regular, MutedColor);
        subtitle.Location = new Point(20, 42);
        var stopAllButton = CreateButton("全部停止", HopeButtonType.Warning);
        stopAllButton.Dock = DockStyle.None;
        stopAllButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        stopAllButton.Bounds = new Rectangle(heading.Width - 132, 18, 112, 38);
        stopAllButton.Click += async (_, _) => await StopAllAsync();
        heading.Controls.Add(title);
        heading.Controls.Add(subtitle);
        heading.Controls.Add(stopAllButton);
        heading.Resize += (_, _) => stopAllButton.Left = heading.ClientSize.Width - stopAllButton.Width - 20;
        layout.Controls.Add(heading, 0, 0);

        ConfigureProfileGrid();
        layout.Controls.Add(_profileGrid, 0, 1);
        return card;
    }

    private void ConfigureProfileGrid()
    {
        _profileGrid.AllowUserToAddRows = false;
        _profileGrid.AllowUserToDeleteRows = false;
        _profileGrid.AllowUserToResizeRows = false;
        _profileGrid.AutoGenerateColumns = false;
        _profileGrid.BackgroundColor = Color.White;
        _profileGrid.BorderStyle = BorderStyle.None;
        _profileGrid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _profileGrid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        _profileGrid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            Alignment = DataGridViewContentAlignment.MiddleLeft,
            BackColor = Color.FromArgb(250, 250, 251),
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = MutedColor,
            Padding = new Padding(8, 0, 8, 0),
            SelectionBackColor = Color.FromArgb(250, 250, 251),
            SelectionForeColor = MutedColor
        };
        _profileGrid.ColumnHeadersHeight = 42;
        _profileGrid.Dock = DockStyle.Fill;
        _profileGrid.EnableHeadersVisualStyles = false;
        _profileGrid.GridColor = Color.FromArgb(238, 240, 243);
        _profileGrid.ReadOnly = true;
        _profileGrid.RowHeadersVisible = false;
        _profileGrid.RowTemplate.Height = 48;
        _profileGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _profileGrid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.White,
            ForeColor = Color.FromArgb(30, 41, 59),
            Padding = new Padding(8, 0, 8, 0),
            SelectionBackColor = Color.FromArgb(238, 242, 255),
            SelectionForeColor = Color.FromArgb(30, 41, 59)
        };

        _profileGrid.Columns.Add(CreateTextColumn("Name", "无头主机", "Name", 150, DataGridViewAutoSizeColumnMode.Fill));
        _profileGrid.Columns.Add(CreateTextColumn("Backend", "目标服务器地址", "BackendUrl", 190, DataGridViewAutoSizeColumnMode.Fill));
        _profileGrid.Columns.Add(CreateTextColumn("Status", "状态", "StatusDisplay", 82));
        _profileGrid.Columns.Add(CreateTextColumn("ProcessId", "进程 ID", "ProcessId", 78));
        _profileGrid.Columns.Add(CreateTextColumn("Uptime", "运行时间", "Uptime", 92));
        _profileGrid.Columns.Add(CreateButtonColumn("View", "查看", 58));
        _profileGrid.Columns.Add(CreateButtonColumn("Start", "启动", 58));
        _profileGrid.Columns.Add(CreateButtonColumn("Stop", "停止", 58));
        _profileGrid.Columns.Add(CreateButtonColumn("Restart", "重启", 58));
        _profileGrid.Columns.Add(CreateButtonColumn("Edit", "编辑", 58));
        _profileGrid.Columns.Add(CreateButtonColumn("Remove", "移除", 58));
        _profileGrid.CellContentClick += ProfileGrid_CellContentClick;
        _profileGrid.CellFormatting += ProfileGrid_CellFormatting;
    }

    private void BuildEditorOverlay(Control host)
    {
        ConfigureOverlay(_editorOverlay);
        _editorCard.BackColor = Color.White;
        _editorCard.Padding = new Padding(28);
        _editorCard.Size = new Size(640, 620);
        _editorCard.Paint += PaintCardBorder;
        _editorOverlay.Controls.Add(_editorCard);

        var layout = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Fill, RowCount = 8 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        _editorCard.Controls.Add(layout);

        var titlePanel = new Panel { Dock = DockStyle.Fill };
        _editorTitle.Location = new Point(0, 0);
        var caption = CreateLabel("配置一个 SPT 安装目录及其 Fika 无头配置。", 9, FontStyle.Regular, MutedColor);
        caption.Location = new Point(0, 34);
        titlePanel.Controls.Add(_editorTitle);
        titlePanel.Controls.Add(caption);
        layout.Controls.Add(titlePanel, 0, 0);
        layout.Controls.Add(CreateLabeledField("实例名称", _nameInput), 0, 1);

        var directoryPanel = CreateLabeledField("SPT 安装目录", _directoryInput);
        _directoryInput.Width = 500;
        _directoryInput.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        var browseButton = CreateButton("浏览", HopeButtonType.Info);
        browseButton.Dock = DockStyle.None;
        browseButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        browseButton.Bounds = new Rectangle(directoryPanel.Width - 76, 24, 76, 36);
        browseButton.Click += (_, _) => BrowseInstallDirectory();
        directoryPanel.Controls.Add(browseButton);
        directoryPanel.Resize += (_, _) =>
        {
            browseButton.Left = directoryPanel.ClientSize.Width - browseButton.Width;
            _directoryInput.Width = browseButton.Left - 8;
        };
        layout.Controls.Add(directoryPanel, 0, 2);

        layout.Controls.Add(CreateTwoColumnFields("配置文件 ID / 令牌", _profileInput, "后端地址", _backendInput), 0, 3);
        layout.Controls.Add(CreateLabeledField("窗口标题（可选）", _titleInput), 0, 4);

        var options = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        _autoRestartInput.Checked = true;
        _autoRestartInput.Margin = new Padding(0, 8, 26, 0);
        _extraLoggingInput.Margin = new Padding(0, 8, 0, 0);
        options.Controls.Add(_autoRestartInput);
        options.Controls.Add(_extraLoggingInput);
        layout.Controls.Add(options, 0, 5);
        _editorError.Dock = DockStyle.Fill;
        _editorError.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(_editorError, 0, 6);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        var saveButton = CreateButton("保存实例", HopeButtonType.Primary);
        saveButton.Dock = DockStyle.None;
        saveButton.Size = new Size(112, 40);
        saveButton.Click += async (_, _) => await SaveEditorAsync();
        var cancelButton = CreateButton("取消", HopeButtonType.Default);
        cancelButton.Dock = DockStyle.None;
        cancelButton.Size = new Size(90, 40);
        cancelButton.Click += (_, _) => HideEditor();
        buttons.Controls.Add(saveButton);
        buttons.Controls.Add(cancelButton);
        layout.Controls.Add(buttons, 0, 7);

        host.Controls.Add(_editorOverlay);
        _editorOverlay.BringToFront();
        _editorOverlay.Visible = false;
    }

    private void BuildLogOverlay(Control host)
    {
        ConfigureOverlay(_logOverlay);
        _logCard.BackColor = Color.White;
        _logCard.Padding = new Padding(24);
        _logCard.Size = new Size(980, 680);
        _logCard.Paint += PaintCardBorder;
        _logOverlay.Controls.Add(_logCard);

        var layout = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Fill, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _logCard.Controls.Add(layout);

        var heading = new Panel { Dock = DockStyle.Fill };
        _logTitle.Location = new Point(0, 0);
        _logSubtitle.Location = new Point(0, 32);
        var closeButton = CreateButton("关闭", HopeButtonType.Default);
        closeButton.Dock = DockStyle.None;
        closeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        closeButton.Bounds = new Rectangle(heading.Width - 90, 2, 90, 38);
        closeButton.Click += (_, _) => HideLogViewer();
        heading.Controls.Add(_logTitle);
        heading.Controls.Add(_logSubtitle);
        heading.Controls.Add(closeButton);
        heading.Resize += (_, _) => closeButton.Left = heading.ClientSize.Width - closeButton.Width;
        layout.Controls.Add(heading, 0, 0);

        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        filters.Controls.Add(CreateFilterLabel("输出来源"));
        _logCategoryFilter.Items.AddRange(["全部来源", "标准输出", "BepInEx 输出"]);
        _logCategoryFilter.SelectedIndex = 0;
        _logCategoryFilter.Size = new Size(150, 34);
        _logCategoryFilter.SelectedIndexChanged += (_, _) => RefreshLogView();
        filters.Controls.Add(_logCategoryFilter);
        filters.Controls.Add(CreateFilterLabel("日志级别"));
        _logLevelFilter.Items.AddRange(["全部级别", "消息", "信息", "警告", "错误", "调试", "致命"]);
        _logLevelFilter.SelectedIndex = 0;
        _logLevelFilter.Size = new Size(130, 34);
        _logLevelFilter.SelectedIndexChanged += (_, _) => RefreshLogView();
        filters.Controls.Add(_logLevelFilter);
        layout.Controls.Add(filters, 0, 1);

        _logOutput.BackColor = Color.FromArgb(15, 23, 42);
        _logOutput.BorderStyle = BorderStyle.None;
        _logOutput.DetectUrls = false;
        _logOutput.Dock = DockStyle.Fill;
        _logOutput.Font = new Font("Cascadia Mono", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        _logOutput.ForeColor = Color.FromArgb(226, 232, 240);
        _logOutput.ReadOnly = true;
        _logOutput.WordWrap = false;
        layout.Controls.Add(_logOutput, 0, 2);

        host.Controls.Add(_logOverlay);
        _logOverlay.BringToFront();
        _logOverlay.Visible = false;
    }

    private async void MainForm_Load(object? sender, EventArgs e)
    {
        try
        {
            var profiles = await _configurationStore.LoadAsync();
            if (profiles.Count == 0)
            {
                var imported = await TryImportLegacyProfileAsync();
                if (imported is not null)
                {
                    profiles = [imported];
                    await _configurationStore.SaveAsync(profiles);
                }
            }

            foreach (var profile in profiles)
            {
                _profiles.Add(profile);
            }
        }
        catch (Exception exception)
        {
            ShowError($"无法加载已保存的实例：{exception.Message}");
        }

        PositionOverlayCards();
        RefreshDashboard();
        _refreshTimer.Start();
    }

    private void ManagerService_StateChanged(object? sender, EventArgs e) =>
        RunOnUiThread(() =>
        {
            RefreshDashboard();
            if (_viewedLogProfileId is Guid profileId)
            {
                var profile = _profiles.FirstOrDefault(item => item.Id == profileId);
                if (profile is not null)
                {
                    UpdateLogSubtitle(profile);
                }
            }
        });

    private void ManagerService_LogReceived(object? sender, ProcessLogEntry entry) =>
        RunOnUiThread(() =>
        {
            if (!_logsByProfile.TryGetValue(entry.ProfileId, out var entries))
            {
                entries = new Queue<ProcessLogEntry>();
                _logsByProfile[entry.ProfileId] = entries;
            }

            entries.Enqueue(entry);
            while (entries.Count > 3000)
            {
                entries.Dequeue();
            }

            if (_viewedLogProfileId == entry.ProfileId && _logOverlay.Visible && MatchesLogFilter(entry))
            {
                AppendLogText(FormatLogEntry(entry));
            }
        });

    private void RefreshDashboard()
    {
        foreach (var profile in _profiles)
        {
            profile.RefreshUptime();
        }

        _profileCountValue.Text = _profiles.Count.ToString();
        _activeCountValue.Text = _profiles.Count(profile => profile.Status == "Running").ToString();
        _lastUpdatedLabel.Text = $"更新于 {DateTime.Now:HH:mm:ss}";
        _profileSource.ResetBindings(false);
        RefreshSidebarProfiles();
    }

    private void RefreshSidebarProfiles()
    {
        _sidebarProfiles.SuspendLayout();
        _sidebarProfiles.Controls.Clear();
        foreach (var profile in _profiles)
        {
            var color = profile.Status switch
            {
                "Running" => Color.FromArgb(52, 211, 153),
                "Error" => Color.FromArgb(248, 113, 113),
                "Starting" or "Connecting" or "Restarting" => Color.FromArgb(251, 191, 36),
                _ => Color.FromArgb(100, 116, 139)
            };
            var item = new Panel { BackColor = SidebarColor, Height = 30, Margin = new Padding(0, 2, 0, 2), Width = 176 };
            var dot = CreateRoundedPanel(color, 4);
            dot.Bounds = new Rectangle(2, 11, 8, 8);
            var name = CreateLabel(profile.Name, 9, FontStyle.Regular, Color.FromArgb(203, 213, 225));
            name.AutoEllipsis = true;
            name.Bounds = new Rectangle(20, 4, 150, 23);
            item.Controls.Add(dot);
            item.Controls.Add(name);
            _sidebarProfiles.Controls.Add(item);
        }
        _sidebarProfiles.ResumeLayout();
    }

    private async void ProfileGrid_CellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _profileGrid.Rows[e.RowIndex].DataBoundItem is not ManagerProfile profile)
        {
            return;
        }

        switch (_profileGrid.Columns[e.ColumnIndex].Name)
        {
            case "View":
                ShowLogViewer(profile);
                break;
            case "Start":
                await _managerService.StartAsync(profile);
                break;
            case "Stop":
                await _managerService.StopAsync(profile);
                break;
            case "Restart":
                await _managerService.RestartAsync(profile);
                break;
            case "Edit":
                ShowEditEditor(profile);
                break;
            case "Remove":
                await RemoveProfileAsync(profile);
                break;
        }
    }

    private void ProfileGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (_profileGrid.Columns[e.ColumnIndex].Name != "Status" ||
            _profileGrid.Rows[e.RowIndex].DataBoundItem is not ManagerProfile profile)
        {
            return;
        }

        e.CellStyle.ForeColor = profile.Status switch
        {
            "Running" => Color.FromArgb(5, 150, 105),
            "Error" => Color.FromArgb(220, 38, 38),
            "Starting" or "Connecting" or "Restarting" => Color.FromArgb(217, 119, 6),
            _ => MutedColor
        };
        e.CellStyle.Font = new Font(_profileGrid.Font, FontStyle.Bold);
    }

    private async Task StartAllAsync()
    {
        foreach (var profile in _profiles.Where(profile => profile.Status is "Stopped" or "Error").ToArray())
        {
            await _managerService.StartAsync(profile);
        }
    }

    private async Task StopAllAsync()
    {
        foreach (var profile in _profiles.ToArray())
        {
            await _managerService.StopAsync(profile);
        }
    }

    private void ShowAddEditor()
    {
        _editingProfile = null;
        _editorTitle.Text = "添加实例";
        _nameInput.Text = $"无头实例 {_profiles.Count + 1}";
        _directoryInput.Text = string.Empty;
        _profileInput.Text = string.Empty;
        _backendInput.Text = "https://127.0.0.1:6969/";
        _titleInput.Text = string.Empty;
        _autoRestartInput.Checked = true;
        _extraLoggingInput.Checked = false;
        _editorError.Text = string.Empty;
        ShowEditor();
    }

    private void ShowEditEditor(ManagerProfile profile)
    {
        if (profile.Status is not ("Stopped" or "Error"))
        {
            ShowWarning("请先停止该实例，再编辑其配置。");
            return;
        }

        _editingProfile = profile;
        _editorTitle.Text = "编辑实例";
        _nameInput.Text = profile.Name;
        _directoryInput.Text = profile.InstallDirectory;
        _profileInput.Text = profile.ProfileId;
        _backendInput.Text = profile.BackendUrl;
        _titleInput.Text = profile.Title;
        _autoRestartInput.Checked = profile.AutoRestart;
        _extraLoggingInput.Checked = profile.ExtraLogging;
        _editorError.Text = string.Empty;
        ShowEditor();
    }

    private void ShowEditor()
    {
        _logOverlay.Visible = false;
        _editorOverlay.Visible = true;
        _editorOverlay.BringToFront();
        PositionOverlayCards();
        _nameInput.Focus();
    }

    private void HideEditor() => _editorOverlay.Visible = false;

    private async Task SaveEditorAsync()
    {
        var validationMessage = ValidateEditor();
        if (validationMessage is not null)
        {
            _editorError.Text = validationMessage;
            return;
        }

        var profile = new ManagerProfile
        {
            Id = _editingProfile?.Id ?? Guid.NewGuid(),
            Name = _nameInput.Text.Trim(),
            InstallDirectory = Path.GetFullPath(_directoryInput.Text.Trim()),
            ProfileId = _profileInput.Text.Trim(),
            BackendUrl = _backendInput.Text.Trim(),
            Title = _titleInput.Text.Trim(),
            AutoRestart = _autoRestartInput.Checked,
            ExtraLogging = _extraLoggingInput.Checked
        };

        if (_editingProfile is null)
        {
            _profiles.Add(profile);
        }
        else
        {
            _profiles[_profiles.IndexOf(_editingProfile)] = profile;
        }

        await SaveProfilesAsync();
        HideEditor();
        RefreshDashboard();
    }

    private async Task RemoveProfileAsync(ManagerProfile profile)
    {
        if (profile.Status is not ("Stopped" or "Error"))
        {
            ShowWarning("请先停止该实例，再将其移除。");
            return;
        }

        if (MessageBox.Show(this, $"确定移除“{profile.Name}”吗？", "移除实例", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        _profiles.Remove(profile);
        _logsByProfile.Remove(profile.Id);
        await SaveProfilesAsync();
        RefreshDashboard();
    }

    private void BrowseInstallDirectory()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择 SPT 安装目录",
            InitialDirectory = Directory.Exists(_directoryInput.Text) ? _directoryInput.Text : Environment.CurrentDirectory,
            ShowNewFolderButton = false,
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _directoryInput.Text = dialog.SelectedPath;
        }
    }

    private string? ValidateEditor()
    {
        if (string.IsNullOrWhiteSpace(_nameInput.Text) || string.IsNullOrWhiteSpace(_directoryInput.Text) ||
            string.IsNullOrWhiteSpace(_profileInput.Text) || string.IsNullOrWhiteSpace(_backendInput.Text))
        {
            return "实例名称、安装目录、配置文件 ID 和后端地址均为必填项。";
        }

        if (!Uri.TryCreate(_backendInput.Text.Trim(), UriKind.Absolute, out _))
        {
            return "请输入有效的绝对后端地址。";
        }

        if (!Directory.Exists(_directoryInput.Text.Trim()))
        {
            return "所选安装目录不存在。";
        }

        if (!File.Exists(Path.Combine(_directoryInput.Text.Trim(), "EscapeFromTarkov.exe")))
        {
            return "所选目录中未找到 EscapeFromTarkov.exe。";
        }

        if (!File.Exists(Path.Combine(_directoryInput.Text.Trim(), "BepInEx", "plugins", "Fika", "Fika.Headless.dll")))
        {
            return "BepInEx\\plugins\\Fika 目录中未找到 Fika.Headless.dll。";
        }

        return null;
    }

    private async Task SaveProfilesAsync()
    {
        try
        {
            await _configurationStore.SaveAsync(_profiles);
        }
        catch (Exception exception)
        {
            ShowError($"无法保存实例配置：{exception.Message}");
        }
    }

    private static async Task<ManagerProfile?> TryImportLegacyProfileAsync()
    {
        var configPath = Path.Combine(Environment.CurrentDirectory, "HeadlessConfig.json");
        if (!File.Exists(configPath) || !File.Exists(Path.Combine(Environment.CurrentDirectory, "EscapeFromTarkov.exe")))
        {
            return null;
        }

        await using var stream = File.OpenRead(configPath);
        var settings = await JsonSerializer.DeserializeAsync<LegacySettings>(stream);
        if (settings?.ProfileId is null || settings.BackendUrl is null)
        {
            return null;
        }

        return new ManagerProfile
        {
            Name = string.IsNullOrWhiteSpace(settings.Title) ? "已导入的无头实例" : settings.Title,
            InstallDirectory = Environment.CurrentDirectory,
            ProfileId = settings.ProfileId,
            BackendUrl = settings.BackendUrl.ToString(),
            Title = settings.Title ?? string.Empty,
            ExtraLogging = settings.ExtraLogging
        };
    }

    private void ShowLogViewer(ManagerProfile profile)
    {
        _viewedLogProfileId = profile.Id;
        _logTitle.Text = $"{profile.Name} · 实时输出";
        UpdateLogSubtitle(profile);
        _editorOverlay.Visible = false;
        _logOverlay.Visible = true;
        _logOverlay.BringToFront();
        PositionOverlayCards();
        RefreshLogView();
    }

    private void HideLogViewer()
    {
        _logOverlay.Visible = false;
        _viewedLogProfileId = null;
    }

    private void UpdateLogSubtitle(ManagerProfile profile)
    {
        _logSubtitle.Text = profile.ProcessId is null
            ? $"状态：{profile.StatusDisplay} · 当前无活动进程"
            : $"状态：{profile.StatusDisplay} · 进程 ID {profile.ProcessId}";
    }

    private void RefreshLogView()
    {
        if (!_logOverlay.Visible)
        {
            return;
        }

        if (_viewedLogProfileId is not Guid profileId || !_logsByProfile.TryGetValue(profileId, out var entries))
        {
            _logOutput.Text = "暂无实时输出。启动该实例后将在这里显示日志。\r\n";
            return;
        }

        var filteredEntries = entries.Where(MatchesLogFilter).ToArray();
        _logOutput.Text = filteredEntries.Length == 0
            ? "当前筛选条件下暂无实时输出。\r\n"
            : string.Concat(filteredEntries.Select(FormatLogEntry));
        _logOutput.SelectionStart = _logOutput.TextLength;
        _logOutput.ScrollToCaret();
    }

    private bool MatchesLogFilter(ProcessLogEntry entry)
    {
        var categoryMatches = _logCategoryFilter.SelectedIndex switch
        {
            1 => entry.Category == "标准输出",
            2 => entry.Category == "BepInEx",
            _ => true
        };
        var selectedLevel = _logLevelFilter.SelectedIndex switch
        {
            1 => "消息",
            2 => "信息",
            3 => "警告",
            4 => "错误",
            5 => "调试",
            6 => "致命",
            _ => null
        };
        return categoryMatches && (selectedLevel is null || entry.Level == selectedLevel);
    }

    private void AppendLogText(string text)
    {
        const int maximumCharacters = 2_000_000;
        if (_logOutput.Text.StartsWith("暂无实时输出。", StringComparison.Ordinal) ||
            _logOutput.Text.StartsWith("当前筛选条件下暂无实时输出。", StringComparison.Ordinal))
        {
            _logOutput.Clear();
        }

        _logOutput.AppendText(text);
        if (_logOutput.TextLength > maximumCharacters)
        {
            _logOutput.Select(0, _logOutput.TextLength - maximumCharacters);
            _logOutput.SelectedText = string.Empty;
        }

        _logOutput.SelectionStart = _logOutput.TextLength;
        _logOutput.ScrollToCaret();
    }

    private static string FormatLogEntry(ProcessLogEntry entry)
    {
        var labels = string.Equals(entry.Category, entry.Source, StringComparison.Ordinal)
            ? $"{entry.Category}/{entry.Level}"
            : $"{entry.Category}/{entry.Level}/{entry.Source}";
        var prefix = $"{entry.Timestamp.LocalDateTime:HH:mm:ss.fff} [{labels}] ";
        var normalized = entry.Message.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        return string.Join(
            Environment.NewLine,
            lines.Select((line, index) => index == 0 ? prefix + line : new string(' ', prefix.Length) + line)) +
            Environment.NewLine;
    }

    private void RunOnUiThread(Action action)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(action);
        }
        else
        {
            action();
        }
    }

    private void BeginWindowDrag(MouseEventArgs eventArgs)
    {
        if (eventArgs.Button != MouseButtons.Left)
        {
            return;
        }

        _ = ReleaseCapture();
        _ = SendMessage(Handle, NonClientLeftButtonDown, CaptionHitTest, 0);
    }

    private async void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        _refreshTimer.Stop();
        Enabled = false;
        await _managerService.DisposeAsync();
        _allowClose = true;
        Close();
    }

    private void PositionOverlayCards()
    {
        PositionOverlayCard(_editorOverlay, _editorCard, 640, 620);
        PositionOverlayCard(_logOverlay, _logCard, 1040, 700);
    }

    private static void PositionOverlayCard(Panel overlay, Panel card, int preferredWidth, int preferredHeight)
    {
        var width = Math.Min(preferredWidth, Math.Max(500, overlay.ClientSize.Width - 80));
        var height = Math.Min(preferredHeight, Math.Max(420, overlay.ClientSize.Height - 60));
        card.Bounds = new Rectangle(
            Math.Max(0, (overlay.ClientSize.Width - width) / 2),
            Math.Max(0, (overlay.ClientSize.Height - height) / 2),
            width,
            height);
    }

    private static void ConfigureOverlay(Panel overlay)
    {
        overlay.BackColor = Color.FromArgb(15, 23, 42);
        overlay.Dock = DockStyle.Fill;
        overlay.Visible = false;
    }

    private static Panel CreateMetricCard(string title, Label value, string caption, Color accent)
    {
        var card = CreateRoundedPanel(Color.White, 12, BorderColor);
        card.Dock = DockStyle.Fill;
        var titleLabel = CreateLabel(title, 9, FontStyle.Regular, MutedColor);
        titleLabel.Location = new Point(20, 18);
        value.Location = new Point(20, 45);
        var captionLabel = CreateLabel(caption, 8, FontStyle.Regular, accent);
        captionLabel.Location = new Point(20, 96);
        var badge = CreateRoundedPanel(Color.FromArgb(238, 242, 255), 18);
        badge.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        badge.Bounds = new Rectangle(card.Width - 58, 18, 38, 38);
        badge.Controls.Add(CreateCenteredLabel("●", 10, FontStyle.Bold, accent));
        card.Controls.Add(titleLabel);
        card.Controls.Add(value);
        card.Controls.Add(captionLabel);
        card.Controls.Add(badge);
        card.Resize += (_, _) => badge.Left = card.ClientSize.Width - badge.Width - 20;
        return card;
    }

    private static Panel CreateLabeledField(string label, Control input)
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        var labelControl = CreateLabel(label, 9, FontStyle.Bold, Color.FromArgb(30, 41, 59));
        labelControl.Location = new Point(0, 0);
        input.Location = new Point(0, 24);
        input.Size = new Size(panel.Width, 36);
        input.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        panel.Controls.Add(labelControl);
        panel.Controls.Add(input);
        panel.Resize += (_, _) => input.Width = panel.ClientSize.Width;
        return panel;
    }

    private static Panel CreateTwoColumnFields(string leftLabel, Control leftInput, string rightLabel, Control rightInput)
    {
        var panel = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill, RowCount = 1 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var left = CreateLabeledField(leftLabel, leftInput);
        left.Margin = new Padding(0, 0, 8, 0);
        var right = CreateLabeledField(rightLabel, rightInput);
        right.Margin = new Padding(8, 0, 0, 0);
        panel.Controls.Add(left, 0, 0);
        panel.Controls.Add(right, 1, 0);
        return panel;
    }

    private static HopeButton CreateButton(string text, HopeButtonType type)
    {
        return new HopeButton
        {
            BorderColor = BorderColor,
            ButtonType = type,
            Cursor = Cursors.Hand,
            DefaultColor = Color.FromArgb(241, 245, 249),
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
            HoverTextColor = Color.White,
            InfoColor = Color.FromArgb(14, 165, 233),
            PrimaryColor = AccentColor,
            SuccessColor = Color.FromArgb(16, 185, 129),
            Text = text,
            TextColor = type == HopeButtonType.Default ? Color.FromArgb(51, 65, 85) : Color.White,
            WarningColor = Color.FromArgb(245, 158, 11),
            DangerColor = Color.FromArgb(239, 68, 68)
        };
    }

    private static HopeTextBox CreateTextBox(string hint)
    {
        return new HopeTextBox
        {
            BackColor = Color.White,
            BaseColor = Color.White,
            BorderColorA = BorderColor,
            BorderColorB = AccentColor,
            Font = new Font("Microsoft YaHei UI", 9F),
            ForeColor = Color.FromArgb(30, 41, 59),
            Hint = hint,
            MaxLength = 1024
        };
    }

    private static HopeComboBox CreateComboBox()
    {
        return new HopeComboBox
        {
            BackColor = Color.White,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Microsoft YaHei UI", 9F),
            ForeColor = Color.FromArgb(30, 41, 59),
            Margin = new Padding(8, 6, 22, 0)
        };
    }

    private static HopeCheckBox CreateCheckBox(string text)
    {
        return new HopeCheckBox
        {
            AutoSize = true,
            CheckedColor = AccentColor,
            EnabledCheckedColor = AccentColor,
            EnabledStringColor = Color.FromArgb(30, 41, 59),
            EnabledUncheckedColor = Color.FromArgb(148, 163, 184),
            Font = new Font("Microsoft YaHei UI", 9F),
            Text = text
        };
    }

    private static Label CreateFilterLabel(string text)
    {
        var label = CreateLabel(text, 9, FontStyle.Bold, MutedColor);
        label.Margin = new Padding(0, 10, 0, 0);
        return label;
    }

    private static DataGridViewTextBoxColumn CreateTextColumn(
        string name,
        string header,
        string property,
        int width,
        DataGridViewAutoSizeColumnMode autoSizeMode = DataGridViewAutoSizeColumnMode.None)
    {
        return new DataGridViewTextBoxColumn
        {
            AutoSizeMode = autoSizeMode,
            DataPropertyName = property,
            HeaderText = header,
            MinimumWidth = width,
            Name = name,
            ReadOnly = true,
            Width = width
        };
    }

    private static DataGridViewButtonColumn CreateButtonColumn(string name, string text, int width)
    {
        return new DataGridViewButtonColumn
        {
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                BackColor = Color.White,
                ForeColor = AccentDarkColor,
                Padding = new Padding(3, 7, 3, 7),
                SelectionBackColor = Color.FromArgb(238, 242, 255),
                SelectionForeColor = AccentDarkColor
            },
            FlatStyle = FlatStyle.Flat,
            HeaderText = string.Empty,
            Name = name,
            ReadOnly = true,
            Text = text,
            UseColumnTextForButtonValue = true,
            Width = width
        };
    }

    private static Label CreateLabel(string text, float size, FontStyle style, Color color)
    {
        return new Label
        {
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point),
            ForeColor = color,
            Text = text
        };
    }

    private static Label CreateCenteredLabel(string text, float size, FontStyle style, Color color)
    {
        var label = CreateLabel(text, size, style, color);
        label.AutoSize = false;
        label.Dock = DockStyle.Fill;
        label.TextAlign = ContentAlignment.MiddleCenter;
        return label;
    }

    private static RoundedPanel CreateRoundedPanel(Color backColor, int radius, Color? borderColor = null) =>
        new(backColor, radius, borderColor ?? backColor);

    private static void PaintCardBorder(object? sender, PaintEventArgs e)
    {
        if (sender is not Panel panel)
        {
            return;
        }

        using var pen = new Pen(BorderColor);
        e.Graphics.DrawRectangle(pen, 0, 0, panel.Width - 1, panel.Height - 1);
    }

    private static Icon? LoadApplicationIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "FIKA_TOOLS_LOGO.ico");
        if (File.Exists(iconPath))
        {
            return new Icon(iconPath);
        }

        try
        {
            return Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty);
        }
        catch (Exception exception) when (exception is ArgumentException or FileNotFoundException)
        {
            Debug.WriteLine(exception);
            return null;
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, int message, nint wordParameter, nint longParameter);

    private void ShowWarning(string message) =>
        MessageBox.Show(this, message, "Fika 无头管理器", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private void ShowError(string message) =>
        MessageBox.Show(this, message, "Fika 无头管理器", MessageBoxButtons.OK, MessageBoxIcon.Error);

    private sealed record LegacySettings
    {
        public string? ProfileId { get; init; }
        public Uri? BackendUrl { get; init; }
        public bool ExtraLogging { get; init; }
        public string? Title { get; init; }
    }

    private sealed class BufferedPanel : Panel
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BufferedPanel"/> class.
        /// </summary>
        public BufferedPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }
    }

    private sealed class RoundedPanel : Panel
    {
        private readonly int _cornerRadius;
        private readonly Color _borderColor;

        /// <summary>
        /// Initializes a new instance of the <see cref="RoundedPanel"/> class.
        /// </summary>
        /// <param name="backColor">The panel background color.</param>
        /// <param name="cornerRadius">The corner radius.</param>
        /// <param name="borderColor">The panel border color.</param>
        public RoundedPanel(Color backColor, int cornerRadius, Color borderColor)
        {
            BackColor = backColor;
            _cornerRadius = cornerRadius;
            _borderColor = borderColor;
        }

        /// <inheritdoc/>
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = CreateRoundedRectangle(ClientRectangle, _cornerRadius);
            using var pen = new Pen(_borderColor);
            e.Graphics.DrawPath(pen, path);
        }

        /// <inheritdoc/>
        protected override void OnResize(EventArgs eventArgs)
        {
            base.OnResize(eventArgs);
            using var path = CreateRoundedRectangle(ClientRectangle, _cornerRadius);
            Region = new Region(path);
        }

        private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            if (bounds.Width <= 1 || bounds.Height <= 1)
            {
                return path;
            }

            bounds.Width--;
            bounds.Height--;
            var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            if (diameter <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }

            var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
