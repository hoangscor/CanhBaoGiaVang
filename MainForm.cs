// đỡ nhầm 
using WinTimer = System.Windows.Forms.Timer;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Windows.Forms.DataVisualization.Charting;
using GoldPriceAlertWinForms.Models;
using GoldPriceAlertWinForms.Providers;
using GoldPriceAlertWinForms.Services;
using GoldPriceAlertWinForms.Utils;



namespace GoldPriceAlertWinForms
{
    public sealed class MainForm : Form
    {
        /// <summary>
        /// 
        /// </summary>
        // ===== Core services =====
        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        private readonly SettingsStore _store = new SettingsStore();
        private readonly RemoteConfigService _remote;
        private readonly EmailService _email = new EmailService();
        private readonly MetalPriceApiProvider _provider;

        private AppSettings _settings = new AppSettings();

        // ===== Runtime =====
        // _pollLock: khóa chống Tick chạy chồng (Timer Tick tới liên tục)
        // _cts: hủy request đang chạy khi Stop/Close
        // _running: cờ trạng thái Start/Stop
        // _lastPrice: giá lần trước -> tính delta
        // _lastAlertUtc: thời điểm gửi alert gần nhất -> cooldown

        /// <summary>
        /// // timer: nhịp poll theo phút
        // Tick -> gọi PollOnceAsync()
        // Interval set trong ApplyTimerInterval()
        /// </summary>
        private readonly List<PriceHistoryRow> _history = new();
        private readonly BindingSource _bsHistory = new();
        private readonly SemaphoreSlim _pollLock = new(1, 1);
        private CancellationTokenSource _cts = new();
        private bool _running = false;
        private double? _lastPrice = null;
        private DateTimeOffset? _lastAlertUtc = null;

        // ===== UI =====
        private TabControl tabMain = null!;
        private Chart chart = null!;
        private DataGridView grid = null!;
        private TextBox txtLog = null!;

        private Label lblPrice = null!;
        private Label lblUnit = null!;
        private Label lblDelta = null!;
        private Label lblRun = null!;
        private Label lblLast = null!;

        private Button btnStart = null!;
        private Button btnStop = null!;
        private Button btnRefresh = null!;
        private Button btnLoadWebCfg = null!;
        private Button btnReloadFileCfg = null!;
        private Button btnExportCsv = null!;
        private Button btnOpenConfig = null!;

        // Settings controls
        private TextBox txtApiKey = null!;
        private ComboBox cboRegion = null!;
        private TextBox txtBase = null!;
        private TextBox txtDisplay = null!;
        private ComboBox cboUnit = null!;
        private NumericUpDown numPoll = null!;
        private NumericUpDown numMaxPoints = null!;
        private CheckBox chkAutoStart = null!;
        private CheckBox chkDark = null!;
        private CheckBox chkTray = null!;
        private CheckBox chkSound = null!;

        private CheckBox chkMin = null!;
        private NumericUpDown numMin = null!;
        private CheckBox chkMax = null!;
        private NumericUpDown numMax = null!;
        private CheckBox chkDropAbs = null!;
        private NumericUpDown numDropAbs = null!;
        private CheckBox chkDropPct = null!;
        private NumericUpDown numDropPct = null!;
        private NumericUpDown numCooldown = null!;

        private CheckBox chkEmail = null!;
        private TextBox txtFrom = null!;
        private TextBox txtAppPass = null!;
        private TextBox txtTo = null!;
        private TextBox txtSmtp = null!;
        private NumericUpDown numPort = null!;
        private CheckBox chkSsl = null!;
        private TextBox txtSubject = null!;
        private TextBox txtBody = null!;
        private Button btnTestApi = null!;
        private Button btnTestEmail = null!;
        private Button btnSaveCfg = null!;

        private CheckBox chkRemote = null!;
        private CheckBox chkRemoteOnStart = null!;
        private CheckBox chkRemoteEachPoll = null!;
        private TextBox txtRemoteUrl = null!;

        // Status + tray
        private StatusStrip status = null!;
        private ToolStripStatusLabel stbStatus = null!;
        private ToolStripStatusLabel stbNext = null!;
        private NotifyIcon trayIcon = null!;
        private ContextMenuStrip trayMenu = null!;
        private WinTimer timer = null!;


        public MainForm()
        {
            _remote = new RemoteConfigService(_http);
            _provider = new MetalPriceApiProvider(_http);

            BuildUi();
            WireEvents();

            _bsHistory.DataSource = _history;
            grid.DataSource = _bsHistory;

            PrepareChart();

            // Load settings.json (auto create template if missing)
            _settings = _store.EnsureAndLoad();
            LoadSettingsToUi();

            Log($"Config path: {_store.ConfigPath}");
            Log($"Template path: {_store.TemplatePath}");

            if (_settings.AutoStart)
                _ = StartAsync();
        }

        // ================= UI BUILD =================
        private void BuildUi()
        {
            Text = "Gold Price Alert – MetalpriceAPI (Config-based)";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1300, 800);
            Font = new Font("Segoe UI", 10f);

            status = new StatusStrip();
            stbStatus = new ToolStripStatusLabel("Ready");
            stbNext = new ToolStripStatusLabel("Next: -");
            status.Items.Add(stbStatus);
            status.Items.Add(new ToolStripStatusLabel(" | "));
            status.Items.Add(stbNext);

            tabMain = new TabControl { Dock = DockStyle.Fill };
            var tabDash = new TabPage("Dashboard");
            var tabSettings = new TabPage("Settings");
            var tabLogs = new TabPage("Logs");
            var tabAbout = new TabPage("About");

            tabMain.TabPages.AddRange(new[] { tabDash, tabSettings, tabLogs, tabAbout });
            Controls.Add(tabMain);
            Controls.Add(status);

            // ---------- Dashboard ----------
            var dash = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), RowCount = 2, ColumnCount = 1 };
            dash.RowStyles.Add(new RowStyle(SizeType.Absolute, 300));
            dash.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            tabDash.Controls.Add(dash);

            //var summary = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 };
            //for (int i = 0; i < 4; i++) summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            var summary = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 };
            summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28)); // Giá hiện tại
            summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28)); // Biến động
            summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24)); // Trạng thái
            summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20)); // Nút
            dash.Controls.Add(summary, 0, 0);

            Panel Card(string title, out Label main, out Label sub, float mainSize)
            {
                var p = new Panel { Dock = DockStyle.Fill, BackColor = Color.WhiteSmoke, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(6), Padding = new Padding(12) };
                var t = new Label { Text = title, AutoSize = true, Font = new Font(Font, FontStyle.Bold), ForeColor = Color.DimGray, Location = new Point(6, 6) };
                main = new Label { Text = "-", AutoSize = true, Font = new Font("Segoe UI", mainSize, FontStyle.Bold), Location = new Point(10, 45) };
                sub = new Label { Text = "", AutoSize = true, ForeColor = Color.Gray, Location = new Point(10, 140) };
                p.Controls.Add(t); p.Controls.Add(main); p.Controls.Add(sub);
                return p;
            }

            summary.Controls.Add(Card("Giá hiện tại", out lblPrice, out lblUnit, 32), 0, 0);
            summary.Controls.Add(Card("Biến động", out lblDelta, out var sub2, 18), 1, 0);
            sub2.Text = "so với lần trước";

            var pnlStatus = new Panel { Dock = DockStyle.Fill, BackColor = Color.WhiteSmoke, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(6), Padding = new Padding(12) };
            pnlStatus.Controls.Add(new Label { Text = "Trạng thái", AutoSize = true, Font = new Font(Font, FontStyle.Bold), ForeColor = Color.DimGray, Location = new Point(6, 6) });
            lblRun = new Label { Text = "Stopped", AutoSize = true, Font = new Font("Segoe UI", 16, FontStyle.Bold), Location = new Point(6, 36) };
            lblLast = new Label { Text = "Last: -", AutoSize = true, ForeColor = Color.Gray, Location = new Point(8, 104) };
            pnlStatus.Controls.Add(lblRun);
            pnlStatus.Controls.Add(lblLast);
            summary.Controls.Add(pnlStatus, 2, 0);

            var pnlBtns = new Panel { Dock = DockStyle.Fill, Margin = new Padding(6) };
            var btnGrid = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 7, ColumnCount = 1 };
            for (int i = 0; i < 7; i++) btnGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 7f));

            Button B(string text) => new Button { Text = text, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, Margin = new Padding(3) };

            btnStart = B("▶ Start");
            btnStop = B("■ Stop");
            btnRefresh = B("⟳ Refresh Now");
            btnLoadWebCfg = B("⇩ Load Web Config");
            btnReloadFileCfg = B("⟳ Reload settings.json");
            btnOpenConfig = B("📂 Open Config Folder");
            btnExportCsv = B("⤓ Export CSV");

            btnGrid.Controls.Add(btnStart, 0, 0);
            btnGrid.Controls.Add(btnStop, 0, 1);
            btnGrid.Controls.Add(btnRefresh, 0, 2);
            btnGrid.Controls.Add(btnLoadWebCfg, 0, 3);
            btnGrid.Controls.Add(btnReloadFileCfg, 0, 4);
            btnGrid.Controls.Add(btnOpenConfig, 0, 5);
            btnGrid.Controls.Add(btnExportCsv, 0, 6);
            pnlBtns.Controls.Add(btnGrid);

            summary.Controls.Add(pnlBtns, 3, 0);

            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 320 };
            dash.Controls.Add(split, 0, 1);

            chart = new Chart { Dock = DockStyle.Fill };
            split.Panel1.Controls.Add(chart);

            grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            split.Panel2.Controls.Add(grid);

            // ---------- Settings ----------
            var settingsSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 560, Padding = new Padding(10) };
            tabSettings.Controls.Add(settingsSplit);

            var left = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var right = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            settingsSplit.Panel1.Controls.Add(left);
            settingsSplit.Panel2.Controls.Add(right);

            GroupBox Group(string title, int height)
                => new GroupBox { Text = title, Dock = DockStyle.Top, Height = height, Padding = new Padding(10) };

            TableLayoutPanel Grid(int rows)
            {
                var g = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = rows };
                g.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
                g.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                g.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
                for (int i = 0; i < rows; i++) g.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
                return g;
            }

            void AddRow(TableLayoutPanel g, int r, string label, Control c1, Control? c2 = null)
            {
                g.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, r);
                g.Controls.Add(c1, 1, r);
                if (c2 != null) g.Controls.Add(c2, 2, r);
            }

            TextBox TB(bool multiline = false, bool pass = false)
                => new TextBox { Dock = DockStyle.Fill, Multiline = multiline, UseSystemPasswordChar = pass };

            NumericUpDown Num(int min, int max, int val)
                => new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Clamp(val, min, max), Width = 150, Dock = DockStyle.Left };

            NumericUpDown NumD(decimal min, decimal max, decimal val, int dec)
                => new NumericUpDown { Minimum = min, Maximum = max, Value = val, DecimalPlaces = dec, Width = 170, Dock = DockStyle.Left };

            // Metal group
            var gMetal = Group("MetalpriceAPI (JSON)", 280);
            var gMetalGrid = Grid(8);
            gMetal.Controls.Add(gMetalGrid);
            left.Controls.Add(gMetal);

            txtApiKey = TB(pass: true);
            var chkShowKey = new CheckBox { Text = "Show", AutoSize = true };
            cboRegion = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            cboRegion.Items.AddRange(new object[] { "US", "EU" });
            txtBase = TB();
            txtDisplay = TB();
            cboUnit = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            cboUnit.Items.AddRange(new object[] { "Ounce (oz)", "Gram (g)" });
            numPoll = Num(1, 1440, 60);
            numMaxPoints = Num(10, 2000, 200);
            btnTestApi = new Button { Text = "Test API", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat };

            AddRow(gMetalGrid, 0, "API Key:", txtApiKey, chkShowKey);
            AddRow(gMetalGrid, 1, "Region:", cboRegion);
            AddRow(gMetalGrid, 2, "Base currency:", txtBase);
            AddRow(gMetalGrid, 3, "Display currency:", txtDisplay);
            AddRow(gMetalGrid, 4, "Display unit:", cboUnit);
            AddRow(gMetalGrid, 5, "Poll (minutes):", numPoll);
            AddRow(gMetalGrid, 6, "History points:", numMaxPoints);
            AddRow(gMetalGrid, 7, "", btnTestApi);

            // Remote group
            var gRemote = Group("Remote Config (Web - Postman Echo)", 230);
            var gRemoteGrid = Grid(5);
            gRemote.Controls.Add(gRemoteGrid);
            left.Controls.Add(gRemote);

            chkRemote = new CheckBox { Text = "Enable remote config", AutoSize = true };
            chkRemoteOnStart = new CheckBox { Text = "Auto load on start", AutoSize = true };
            chkRemoteEachPoll = new CheckBox { Text = "Auto load each poll", AutoSize = true };
            txtRemoteUrl = TB(multiline: true);
            txtRemoteUrl.Height = 75;

            AddRow(gRemoteGrid, 0, "", chkRemote);
            AddRow(gRemoteGrid, 1, "", chkRemoteOnStart);
            AddRow(gRemoteGrid, 2, "", chkRemoteEachPoll);
            AddRow(gRemoteGrid, 3, "Remote URL:", txtRemoteUrl);

            // Alert group
            var gAlert = Group("Alert rules", 320);
            var gAlertGrid = Grid(6);
            gAlert.Controls.Add(gAlertGrid);
            left.Controls.Add(gAlert);

            chkMin = new CheckBox { Text = "Alert if < Min", AutoSize = true };
            numMin = NumD(0, 100000000, 0, 2);

            chkMax = new CheckBox { Text = "Alert if > Max", AutoSize = true };
            numMax = NumD(0, 100000000, 0, 2);

            chkDropAbs = new CheckBox { Text = "Alert if giảm >= Abs", AutoSize = true };
            numDropAbs = NumD(0, 100000000, 20, 2);

            chkDropPct = new CheckBox { Text = "Alert if giảm >= %", AutoSize = true };
            numDropPct = NumD(0, 100, 0, 2);

            numCooldown = Num(1, 1440, 60);

            AddRow(gAlertGrid, 0, "", chkMin, numMin);
            AddRow(gAlertGrid, 1, "", chkMax, numMax);
            AddRow(gAlertGrid, 2, "", chkDropAbs, numDropAbs);
            AddRow(gAlertGrid, 3, "", chkDropPct, numDropPct);
            AddRow(gAlertGrid, 4, "Cooldown (min):", numCooldown);

            // Email group
            var gEmail = Group("Gmail SMTP", 520);
            var gEmailGrid = Grid(12);
            gEmail.Controls.Add(gEmailGrid);
            right.Controls.Add(gEmail);

            chkEmail = new CheckBox { Text = "Enable Email", AutoSize = true };
            txtFrom = TB();
            txtAppPass = TB(pass: true);
            var chkShowPass = new CheckBox { Text = "Show", AutoSize = true };
            txtTo = TB();
            txtSmtp = TB();
            numPort = Num(1, 65535, 587);
            chkSsl = new CheckBox { Text = "Enable SSL", AutoSize = true, Checked = true };
            txtSubject = TB();
            txtBody = TB(multiline: true);
            txtBody.Height = 150;
            btnTestEmail = new Button { Text = "Send Test Email", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat };

            AddRow(gEmailGrid, 0, "", chkEmail);
            AddRow(gEmailGrid, 1, "From:", txtFrom);
            AddRow(gEmailGrid, 2, "App Password:", txtAppPass, chkShowPass);
            AddRow(gEmailGrid, 3, "To:", txtTo);
            AddRow(gEmailGrid, 4, "SMTP Host:", txtSmtp);
            AddRow(gEmailGrid, 5, "Port:", numPort);
            AddRow(gEmailGrid, 6, "", chkSsl);
            AddRow(gEmailGrid, 7, "Subject tpl:", txtSubject);
            AddRow(gEmailGrid, 8, "Body tpl:", txtBody);
            AddRow(gEmailGrid, 9, "", btnTestEmail);

            // Local group
            var gLocal = Group("Local", 220);
            var gLocalGrid = Grid(6);
            gLocal.Controls.Add(gLocalGrid);
            right.Controls.Add(gLocal);

            chkAutoStart = new CheckBox { Text = "AutoStart", AutoSize = true };
            chkTray = new CheckBox { Text = "Minimize to tray", AutoSize = true };
            chkDark = new CheckBox { Text = "Dark mode", AutoSize = true };
            chkSound = new CheckBox { Text = "Sound on alert", AutoSize = true };
            btnSaveCfg = new Button { Text = "Save to settings.json", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat };

            AddRow(gLocalGrid, 0, "", chkAutoStart);
            AddRow(gLocalGrid, 1, "", chkTray);
            AddRow(gLocalGrid, 2, "", chkDark);
            AddRow(gLocalGrid, 3, "", chkSound);
            AddRow(gLocalGrid, 4, "", btnSaveCfg);

            // Logs
            txtLog = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 10f) };
            tabLogs.Controls.Add(txtLog);

            // About
            tabAbout.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                Text = "Gold Price Alert – MetalpriceAPI\nConfig-based: chỉ sửa settings.json là chạy\nRemote config: Postman Echo\nEmail: Gmail SMTP"
            });

            // Timer + Tray
            // dừng ở StartAsync() / Stop()
            timer = new WinTimer();


            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Show", null, (_, __) => RestoreFromTray());
            trayMenu.Items.Add("Start", null, async (_, __) => await StartAsync());
            trayMenu.Items.Add("Stop", null, (_, __) => Stop());
            trayMenu.Items.Add("Exit", null, (_, __) => Close());

            trayIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Visible = true,
                Text = "Gold Price Alert",
                ContextMenuStrip = trayMenu
            };

            // defaults
            txtSmtp.Text = "smtp.gmail.com";
            cboRegion.SelectedIndex = 0;
            cboUnit.SelectedIndex = 0;

            // show/hide password
            chkShowKey.CheckedChanged += (_, __) => txtApiKey.UseSystemPasswordChar = !chkShowKey.Checked;
            chkShowPass.CheckedChanged += (_, __) => txtAppPass.UseSystemPasswordChar = !chkShowPass.Checked;
        }

        private void WireEvents()
        {
            btnStart.Click += async (_, __) => await StartAsync();
            btnStop.Click += (_, __) => Stop();
            btnRefresh.Click += async (_, __) => await PollOnceAsync(force: true);
            btnLoadWebCfg.Click += async (_, __) => await LoadRemoteConfigAsync(showMessage: true);
            btnReloadFileCfg.Click += (_, __) => ReloadFromFile();
            btnOpenConfig.Click += (_, __) => OpenConfigFolder();
            btnExportCsv.Click += (_, __) => ExportCsv();

            btnTestApi.Click += async (_, __) => await TestApiAsync();
            btnTestEmail.Click += async (_, __) => await TestEmailAsync();
            btnSaveCfg.Click += (_, __) => SaveToFile();

            chkDark.CheckedChanged += (_, __) => ApplyTheme(chkDark.Checked);
            // force=false: chạy khi _running=true
            // _pollLock PollOnceAsync: chặn chạy chồng khi tới sớm
            timer.Tick += async (_, __) => await PollOnceAsync(force: false);

            trayIcon.DoubleClick += (_, __) => RestoreFromTray();

            Resize += (_, __) =>
            {
                if (_settings.MinimizeToTray && WindowState == FormWindowState.Minimized)
                {
                    Hide();
                    trayIcon.BalloonTipTitle = "Gold Price Alert";
                    trayIcon.BalloonTipText = _running ? "Running in tray" : "Stopped in tray";
                    trayIcon.ShowBalloonTip(1500);
                }
            };

            FormClosing += (_, __) =>
            {
                timer.Stop();
                try { _cts.Cancel(); } catch { }
                trayIcon.Visible = false;
            };
        }

        // =================== CONFIG IO ===================
        private void LoadSettingsToUi()
        {
            txtApiKey.Text = _settings.Metal.ApiKey ?? "";
            cboRegion.SelectedIndex = (_settings.Metal.Region?.ToUpperInvariant() == "EU") ? 1 : 0;

            txtBase.Text = _settings.BaseCurrency ?? "USD";
            txtDisplay.Text = _settings.DisplayCurrency ?? "USD";
            cboUnit.SelectedIndex = _settings.DisplayUnit == PriceUnit.Gram ? 1 : 0;

            numPoll.Value = Clamp(numPoll, _settings.PollMinutes);
            numMaxPoints.Value = Clamp(numMaxPoints, _settings.HistoryMaxPoints);

            chkAutoStart.Checked = _settings.AutoStart;
            chkTray.Checked = _settings.MinimizeToTray;
            chkDark.Checked = _settings.DarkMode;
            chkSound.Checked = _settings.SoundOnAlert;

            chkMin.Checked = _settings.Alert.EnableMin;
            numMin.Value = (decimal)_settings.Alert.MinPrice;
            chkMax.Checked = _settings.Alert.EnableMax;
            numMax.Value = (decimal)_settings.Alert.MaxPrice;
            chkDropAbs.Checked = _settings.Alert.EnableDropAbs;
            numDropAbs.Value = (decimal)_settings.Alert.DropAbs;
            chkDropPct.Checked = _settings.Alert.EnableDropPct;
            numDropPct.Value = (decimal)_settings.Alert.DropPct;
            numCooldown.Value = Clamp(numCooldown, _settings.Alert.CooldownMinutes);

            chkEmail.Checked = _settings.Email.Enabled;
            txtFrom.Text = _settings.Email.FromAddress ?? "";
            txtAppPass.Text = _settings.Email.AppPassword ?? "";
            txtTo.Text = _settings.Email.ToAddresses ?? "";
            txtSmtp.Text = _settings.Email.SmtpHost ?? "smtp.gmail.com";
            numPort.Value = Clamp(numPort, _settings.Email.SmtpPort);
            chkSsl.Checked = _settings.Email.EnableSsl;
            txtSubject.Text = _settings.Email.SubjectTemplate ?? "";
            txtBody.Text = _settings.Email.BodyTemplate ?? "";

            chkRemote.Checked = _settings.RemoteConfig.Enabled;
            chkRemoteOnStart.Checked = _settings.RemoteConfig.AutoLoadOnStart;
            chkRemoteEachPoll.Checked = _settings.RemoteConfig.AutoLoadEachPoll;
            txtRemoteUrl.Text = _settings.RemoteConfig.Url ?? "";

            ApplyTheme(_settings.DarkMode);
            // Start trước: cho Tick chạy
            // ApplyTimerInterval: set Interval theo PollMinutes + update "Next"
            ApplyTimerInterval();
        }

        private void ReadUiToSettings()
        {
            _settings.Metal.ApiKey = txtApiKey.Text ?? "";
            _settings.Metal.Region = (cboRegion.SelectedIndex == 1) ? "EU" : "US";

            _settings.BaseCurrency = (txtBase.Text ?? "USD").Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(_settings.BaseCurrency)) _settings.BaseCurrency = "USD";

            _settings.DisplayCurrency = (txtDisplay.Text ?? _settings.BaseCurrency).Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(_settings.DisplayCurrency)) _settings.DisplayCurrency = _settings.BaseCurrency;

            _settings.DisplayUnit = (cboUnit.SelectedIndex == 1) ? PriceUnit.Gram : PriceUnit.Ounce;

            _settings.PollMinutes = (int)numPoll.Value;
            _settings.HistoryMaxPoints = (int)numMaxPoints.Value;

            _settings.AutoStart = chkAutoStart.Checked;
            _settings.MinimizeToTray = chkTray.Checked;
            _settings.DarkMode = chkDark.Checked;
            _settings.SoundOnAlert = chkSound.Checked;

            _settings.Alert.EnableMin = chkMin.Checked;
            _settings.Alert.MinPrice = (double)numMin.Value;
            _settings.Alert.EnableMax = chkMax.Checked;
            _settings.Alert.MaxPrice = (double)numMax.Value;
            _settings.Alert.EnableDropAbs = chkDropAbs.Checked;
            _settings.Alert.DropAbs = (double)numDropAbs.Value;
            _settings.Alert.EnableDropPct = chkDropPct.Checked;
            _settings.Alert.DropPct = (double)numDropPct.Value;
            _settings.Alert.CooldownMinutes = (int)numCooldown.Value;

            _settings.Email.Enabled = chkEmail.Checked;
            _settings.Email.FromAddress = txtFrom.Text ?? "";
            _settings.Email.AppPassword = txtAppPass.Text ?? "";
            _settings.Email.ToAddresses = txtTo.Text ?? "";
            _settings.Email.SmtpHost = txtSmtp.Text ?? "smtp.gmail.com";
            _settings.Email.SmtpPort = (int)numPort.Value;
            _settings.Email.EnableSsl = chkSsl.Checked;
            _settings.Email.SubjectTemplate = txtSubject.Text ?? "";
            _settings.Email.BodyTemplate = txtBody.Text ?? "";

            _settings.RemoteConfig.Enabled = chkRemote.Checked;
            _settings.RemoteConfig.AutoLoadOnStart = chkRemoteOnStart.Checked;
            _settings.RemoteConfig.AutoLoadEachPoll = chkRemoteEachPoll.Checked;
            _settings.RemoteConfig.Url = txtRemoteUrl.Text ?? "";
        }

        private void SaveToFile()
        {
            ReadUiToSettings();
            _store.Save(_settings);
            ApplyTimerInterval();
            Log("Saved settings.json");
            stbStatus.Text = "Saved";
        }

        private void ReloadFromFile()
        {
            try
            {
                _settings = _store.Load();
                LoadSettingsToUi();
                Log("Reloaded settings.json");
            }
            catch (Exception ex)
            {
                Log("Reload error: " + ex.Message);
            }
        }

        private void OpenConfigFolder()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = AppContext.BaseDirectory,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Log("Open folder error: " + ex.Message);
            }
        }

        // =================== THEME ===================
        private void ApplyTheme(bool dark)
        {
            Color bg = dark ? Color.FromArgb(30, 30, 30) : Color.White;
            Color fg = dark ? Color.Gainsboro : Color.Black;

            BackColor = bg;
            ForeColor = fg;

            void Apply(Control c)
            {
                if (c is TextBox tb)
                {
                    tb.BackColor = dark ? Color.FromArgb(45, 45, 45) : Color.White;
                    tb.ForeColor = fg;
                }
                else if (c is DataGridView gv)
                {
                    gv.BackgroundColor = dark ? Color.FromArgb(45, 45, 45) : Color.White;
                    gv.ForeColor = fg;
                    gv.GridColor = dark ? Color.FromArgb(70, 70, 70) : Color.Gainsboro;
                }
                else if (c is Panel p && p.BorderStyle == BorderStyle.FixedSingle)
                {
                    p.BackColor = dark ? Color.FromArgb(45, 45, 45) : Color.WhiteSmoke;
                }
                else
                {
                    c.BackColor = bg;
                    c.ForeColor = fg;
                }

                foreach (Control child in c.Controls) Apply(child);
            }

            Apply(this);
        }

        // =================== CHART ===================
        private void PrepareChart()
        {
            chart.Series.Clear();
            chart.ChartAreas.Clear();

            var area = new ChartArea("Main");
            area.AxisX.LabelStyle.Format = "HH:mm";
            area.AxisX.MajorGrid.LineColor = Color.Gainsboro;
            area.AxisY.MajorGrid.LineColor = Color.Gainsboro;
            chart.ChartAreas.Add(area);

            var s = new Series("Gold")
            {
                ChartType = SeriesChartType.Line,
                XValueType = ChartValueType.DateTime,
                BorderWidth = 2
            };
            chart.Series.Add(s);
        }

        // =================== RUN ===================
        private async Task StartAsync()
        {
            SaveToFile();

            if (_running) return;

            _cts.Cancel();
            _cts.Dispose();
            _cts = new CancellationTokenSource();

            _running = true;
            lblRun.Text = "Running";
            btnStart.Enabled = false;
            btnStop.Enabled = true;

            _history.Clear();
            _bsHistory.ResetBindings(false);
            chart.Series["Gold"].Points.Clear();
            _lastPrice = null;
            _lastAlertUtc = null;

            timer.Start();
            ApplyTimerInterval();

            Log("▶ Start monitoring");

            if (_settings.RemoteConfig.Enabled && _settings.RemoteConfig.AutoLoadOnStart)
                await LoadRemoteConfigAsync(showMessage: false);

            await PollOnceAsync(force: true);
        }

        private void Stop()
        {
            if (!_running) return;
            // Tắt nhịp  -> ngưng Tick
            timer.Stop();
            try { _cts.Cancel(); } catch { }

            _running = false;
            lblRun.Text = "Stopped";
            btnStart.Enabled = true;
            btnStop.Enabled = false;

            Log("■ Stopped");
            stbStatus.Text = "Stopped";
            stbNext.Text = "Next: -";
        }

        private void ApplyTimerInterval()
        {
            int minutes = Math.Max(1, _settings.PollMinutes);
            timer.Interval = minutes * 60 * 1000;
            UpdateNextUi();
        }

        private void UpdateNextUi()
        {
            if (!_running) { stbNext.Text = "Next: -"; return; }
            stbNext.Text = "Next: " + DateTime.Now.AddMinutes(_settings.PollMinutes).ToString("HH:mm:ss");
        }

        private async Task PollOnceAsync(bool force)
        {
            if (!_running && !force) return;
            // không lấy được lock -> đang poll -> return
            if (!await _pollLock.WaitAsync(0)) return;

            try
            {
                stbStatus.Text = "Fetching...";

                ReadUiToSettings();

                if (_settings.RemoteConfig.Enabled && _settings.RemoteConfig.AutoLoadEachPoll)
                    await LoadRemoteConfigAsync(showMessage: false);

                var q = await _provider.GetLatestGoldAsync(_settings, _cts.Token);
                HandleQuote(q);

                await EvaluateAndAlertAsync(_cts.Token);

                stbStatus.Text = "OK";
            }
            catch (Exception ex)
            {
                Log("Poll error: " + ex.Message);
                stbStatus.Text = "Error";
            }
            finally
            {
                _pollLock.Release();
                UpdateNextUi();
            }
        }

        private void HandleQuote(PriceQuote quote)
        {
            double display = UnitConverter.ToDisplayUnit(quote.Price, _settings.DisplayUnit);
            string unitLabel = UnitConverter.UnitLabel(_settings.DisplayUnit);

            double? delta = null;
            double? deltaPct = null;

            if (_lastPrice.HasValue)
            {
                delta = display - _lastPrice.Value;
                if (_lastPrice.Value != 0)
                    deltaPct = (delta.Value / _lastPrice.Value) * 100.0;
            }

            var row = new PriceHistoryRow
            {
                TimeLocal = quote.Timestamp.ToLocalTime().DateTime,
                Currency = quote.Currency,
                Unit = unitLabel,
                Price = display,
                Delta = delta,
                DeltaPercent = deltaPct,
                Source = quote.Source
            };

            _history.Insert(0, row);
            while (_history.Count > _settings.HistoryMaxPoints)
                _history.RemoveAt(_history.Count - 1);

            _bsHistory.ResetBindings(false);

            lblPrice.Text = display.ToString("N2", CultureInfo.CurrentCulture);
            lblUnit.Text = $"{quote.Currency}/{unitLabel}";

            if (delta.HasValue)
            {
                string sign = delta.Value >= 0 ? "+" : "";
                lblDelta.Text = $"{sign}{delta.Value:N2} ({sign}{(deltaPct ?? 0):N2}%)";
            }
            else lblDelta.Text = "-";

            lblLast.Text = "Last: " + row.TimeLocal.ToString("yyyy-MM-dd HH:mm:ss");

            chart.Series["Gold"].Points.AddXY(row.TimeLocal, row.Price);
            while (chart.Series["Gold"].Points.Count > _settings.HistoryMaxPoints)
                chart.Series["Gold"].Points.RemoveAt(0);

            chart.ChartAreas[0].RecalculateAxesScale();
            _lastPrice = display;

            Log($"Price: {display:N2} {quote.Currency}/{unitLabel} | {quote.Source}");
        }

        private async Task EvaluateAndAlertAsync(CancellationToken ct)
        {
            if (_history.Count == 0) return;
            var latest = _history[0];

            var reasons = new List<string>();

            if (_settings.Alert.EnableMin && _settings.Alert.MinPrice > 0 && latest.Price < _settings.Alert.MinPrice)
                reasons.Add($"< MIN ({_settings.Alert.MinPrice:N2})");

            if (_settings.Alert.EnableMax && _settings.Alert.MaxPrice > 0 && latest.Price > _settings.Alert.MaxPrice)
                reasons.Add($"> MAX ({_settings.Alert.MaxPrice:N2})");

            if (latest.Delta.HasValue)
            {
                if (_settings.Alert.EnableDropAbs && _settings.Alert.DropAbs > 0 && latest.Delta.Value <= -_settings.Alert.DropAbs)
                    reasons.Add($"GIẢM {Math.Abs(latest.Delta.Value):N2} >= {_settings.Alert.DropAbs:N2}");

                if (_settings.Alert.EnableDropPct && _settings.Alert.DropPct > 0 && latest.DeltaPercent.HasValue && latest.DeltaPercent.Value <= -_settings.Alert.DropPct)
                    reasons.Add($"GIẢM {Math.Abs(latest.DeltaPercent.Value):N2}% >= {_settings.Alert.DropPct:N2}%");
            }

            if (reasons.Count == 0) return;

            var cooldown = TimeSpan.FromMinutes(Math.Max(1, _settings.Alert.CooldownMinutes));
            if (_lastAlertUtc.HasValue && DateTimeOffset.UtcNow - _lastAlertUtc.Value < cooldown)
            {
                Log($"Alert but cooldown {cooldown.TotalMinutes:N0} min -> skip.");
                return;
            }

            if (!_settings.Email.Enabled)
            {
                Log("Alert but Email disabled.");
                return;
            }

            string reason = string.Join(", ", reasons);

            var tokens = new Dictionary<string, string>
            {
                ["reason"] = reason,
                ["time"] = latest.TimeLocal.ToString("yyyy-MM-dd HH:mm:ss"),
                ["price"] = latest.Price.ToString("N2"),
                ["currency"] = latest.Currency,
                ["unit"] = latest.Unit,
                ["deltaAbs"] = latest.Delta?.ToString("N2") ?? "",
                ["deltaPct"] = latest.DeltaPercent?.ToString("N2") ?? "",
                ["min"] = _settings.Alert.MinPrice.ToString("N2"),
                ["max"] = _settings.Alert.MaxPrice.ToString("N2"),
                ["dropAbs"] = _settings.Alert.DropAbs.ToString("N2"),
                ["dropPct"] = _settings.Alert.DropPct.ToString("N2"),
                ["source"] = latest.Source
            };

            string subject = Utils.TemplateHelper.Render(_settings.Email.SubjectTemplate, tokens);
            string body = Utils.TemplateHelper.Render(_settings.Email.BodyTemplate, tokens);

            Log("Sending alert email...");
            await _email.SendGmailAsync(_settings.Email, subject, body, ct);
            Log("✅ Email sent.");

            _lastAlertUtc = DateTimeOffset.UtcNow;
            if (_settings.SoundOnAlert) System.Media.SystemSounds.Exclamation.Play();

            trayIcon.BalloonTipTitle = "Gold Alert";
            trayIcon.BalloonTipText = reason;
            trayIcon.ShowBalloonTip(2500);
        }

        // =================== REMOTE CONFIG ===================
        private async Task LoadRemoteConfigAsync(bool showMessage)
        {
            try
            {
                ReadUiToSettings();

                if (!_settings.RemoteConfig.Enabled) return;
                string url = _settings.RemoteConfig.Url ?? "";
                if (string.IsNullOrWhiteSpace(url)) return;

                stbStatus.Text = "Loading web config...";
                var cfg = await _remote.LoadAsync(url, _cts.Token);
                if (cfg == null) return;

                if (!string.IsNullOrWhiteSpace(cfg.MetalApiKey)) txtApiKey.Text = cfg.MetalApiKey.Trim();
                if (!string.IsNullOrWhiteSpace(cfg.Region)) cboRegion.SelectedIndex = cfg.Region.Trim().ToUpperInvariant() == "EU" ? 1 : 0;
                if (!string.IsNullOrWhiteSpace(cfg.BaseCurrency)) txtBase.Text = cfg.BaseCurrency.Trim().ToUpperInvariant();
                if (!string.IsNullOrWhiteSpace(cfg.DisplayCurrency)) txtDisplay.Text = cfg.DisplayCurrency.Trim().ToUpperInvariant();

                if (cfg.PollMinutes.HasValue) numPoll.Value = Clamp(numPoll, cfg.PollMinutes.Value);

                if (cfg.Min.HasValue) numMin.Value = (decimal)cfg.Min.Value;
                if (cfg.Max.HasValue) numMax.Value = (decimal)cfg.Max.Value;
                if (cfg.DropAbs.HasValue) numDropAbs.Value = (decimal)cfg.DropAbs.Value;
                if (cfg.DropPct.HasValue) numDropPct.Value = (decimal)cfg.DropPct.Value;
                if (cfg.CooldownMinutes.HasValue) numCooldown.Value = Clamp(numCooldown, cfg.CooldownMinutes.Value);

                if (cfg.EnableMin.HasValue) chkMin.Checked = cfg.EnableMin.Value;
                if (cfg.EnableMax.HasValue) chkMax.Checked = cfg.EnableMax.Value;
                if (cfg.EnableDropAbs.HasValue) chkDropAbs.Checked = cfg.EnableDropAbs.Value;
                if (cfg.EnableDropPct.HasValue) chkDropPct.Checked = cfg.EnableDropPct.Value;

                if (!string.IsNullOrWhiteSpace(cfg.ToAddresses)) txtTo.Text = cfg.ToAddresses.Trim();

                SaveToFile();

                Log("✅ Remote config applied.");
                if (showMessage) MessageBox.Show("Remote config applied.");
            }
            catch (Exception ex)
            {
                Log("Remote config error: " + ex.Message);
                if (showMessage) MessageBox.Show("Remote config error: " + ex.Message);
            }
            finally
            {
                stbStatus.Text = "OK";
            }
        }

        // =================== TEST + EXPORT ===================
        private async Task TestApiAsync()
        {
            try
            {
                SaveToFile();
                var q = await _provider.GetLatestGoldAsync(_settings, CancellationToken.None);
                double display = UnitConverter.ToDisplayUnit(q.Price, _settings.DisplayUnit);

                MessageBox.Show($"OK\nGold: {display:N2} {q.Currency}/{UnitConverter.UnitLabel(_settings.DisplayUnit)}\nTime: {q.Timestamp.LocalDateTime}\nSource: {q.Source}");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Test API failed: " + ex.Message);
            }
        }

        private async Task TestEmailAsync()
        {
            try
            {
                SaveToFile();
                await _email.SendGmailAsync(_settings.Email, "TEST Gold Alert", "Test email OK.", CancellationToken.None);
                MessageBox.Show("Test email sent. Check Inbox/Spam.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Send email failed: " + ex.Message);
            }
        }

        private void ExportCsv()
        {
            using var sfd = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = $"gold_{DateTime.Now:yyyyMMdd_HHmmss}.csv" };
            if (sfd.ShowDialog() != DialogResult.OK) return;

            var sb = new StringBuilder();
            sb.AppendLine("Time,Currency,Unit,Price,Delta,DeltaPercent,Source");
            foreach (var r in _history)
            {
                sb.Append(r.TimeLocal.ToString("yyyy-MM-dd HH:mm:ss")).Append(',');
                sb.Append(r.Currency).Append(',');
                sb.Append(r.Unit).Append(',');
                sb.Append(r.Price.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append((r.Delta?.ToString(CultureInfo.InvariantCulture) ?? "")).Append(',');
                sb.Append((r.DeltaPercent?.ToString(CultureInfo.InvariantCulture) ?? "")).Append(',');
                sb.Append(r.Source);
                sb.AppendLine();
            }
            File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
            Log("Exported: " + sfd.FileName);
        }

        // =================== HELPERS ===================
        private void RestoreFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private static decimal Clamp(NumericUpDown n, int value)
        {
            decimal v = value;
            if (v < n.Minimum) v = n.Minimum;
            if (v > n.Maximum) v = n.Maximum;
            return v;
        }

        private void InitializeComponent()
        {
            SuspendLayout();
            // 
            // MainForm
            // 
            ClientSize = new Size(282, 253);
            Name = "MainForm";
            Load += MainForm_Load;
            ResumeLayout(false);

        }

        private void Log(string msg)
        {
            txtLog.AppendText($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}{Environment.NewLine}");
            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.ScrollToCaret();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {

        }
    }
}
