using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ZStage
{
    internal static class Program
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string path);
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--self-test") return ControllerTests.Run(args);
            bool created;
            using (Mutex single = new Mutex(true, @"Local\HKDAQ_ZStage_PWM1_OUT3", out created))
            {
                if (!created) { MessageBox.Show("本机已有一个 Z 轴控制程序在运行。"); return 1; }
                if (!Environment.Is64BitProcess) { MessageBox.Show("请运行 64 位版本。"); return 1; }
                // Resolve vendor DLL and its local dependencies relative to this executable.
                if (!SetDllDirectory(AppDomain.CurrentDomain.BaseDirectory))
                { MessageBox.Show("无法设置板卡库目录。"); return 1; }
                if (!File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "USBDAQ_DLL_V12X64.dll")))
                { MessageBox.Show("缺少 USBDAQ_DLL_V12X64.dll，请完整复制整个文件夹。"); return 1; }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
                return 0;
            }
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly StageController controller = new StageController(new UsbBoard());
        private readonly Button connect = new Button(), disconnect = new Button();
        private readonly Button up = new Button(), down = new Button(), stop = new Button();
        private readonly NumericUpDown hz = new NumericUpDown(), seconds = new NumericUpDown();
        private readonly CheckBox invert = new CheckBox();
        private readonly Label status = new Label();
        private readonly TextBox log = new TextBox();
        private StageState current = StageState.Disconnected;
        private bool closing, allowClose, busy;

        public MainForm()
        {
            Text = "Z 轴位移台驱动 · HK USB DAQ";
            Font = new Font("Microsoft YaHei UI", 10F);
            BackColor = Color.FromArgb(243, 246, 250);
            ClientSize = new Size(760, 650);
            MinimumSize = new Size(760, 680);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9, Padding = new Padding(24) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            Controls.Add(root);
            root.Controls.Add(new Label { Text = "Z 轴位移台", Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 22, FontStyle.Bold) });
            var connection = new FlowLayoutPanel { Dock = DockStyle.Fill };
            SetupButton(connect, "连接板卡", Color.FromArgb(38, 91, 160));
            SetupButton(disconnect, "断开板卡", Color.FromArgb(95, 108, 124));
            connection.Controls.AddRange(new Control[] { connect, disconnect }); root.Controls.Add(connection);
            var settings = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2 };
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            settings.Controls.Add(new Label { Text = "脉冲频率 / Hz", AutoSize = true }, 0, 0);
            hz.Minimum = 1; hz.Maximum = 10000; hz.Value = 200; hz.Increment = 100; hz.Dock = DockStyle.Top;
            settings.Controls.Add(hz, 1, 0);
            settings.Controls.Add(new Label { Text = "单次时长 / 秒", AutoSize = true }, 2, 0);
            seconds.Minimum = 0.1M; seconds.Maximum = 60; seconds.DecimalPlaces = 1; seconds.Increment = 0.1M; seconds.Value = 2; seconds.Dock = DockStyle.Top;
            settings.Controls.Add(seconds, 3, 0);
            var note = new Label { Text = "首次建议 100–300 Hz、0.1–0.5 秒点动，确认上下方向及行程。", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(85, 97, 113) };
            settings.Controls.Add(note, 0, 1); settings.SetColumnSpan(note, 4); root.Controls.Add(settings);
            invert.Text = "反转上下方向（默认：上移 OUT3=1，下移 OUT3=0）"; invert.AutoSize = true; root.Controls.Add(invert);
            var motion = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
            for (int i = 0; i < 3; i++) motion.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
            SetupButton(up, "↑  上移", Color.FromArgb(38, 91, 160));
            SetupButton(down, "↓  下移", Color.FromArgb(38, 91, 160));
            SetupButton(stop, "■  停止", Color.FromArgb(193, 42, 49));
            up.Dock = down.Dock = stop.Dock = DockStyle.Fill;
            up.Font = down.Font = stop.Font = new Font(Font.FontFamily, 17, FontStyle.Bold);
            motion.Controls.Add(up); motion.Controls.Add(down); motion.Controls.Add(stop); root.Controls.Add(motion);
            status.Text = "未连接板卡"; status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft; status.Font = new Font(Font, FontStyle.Bold); root.Controls.Add(status);
            root.Controls.Add(new Label { Text = "点击上移/下移后限时运动；停止按钮、空格或 Esc 可停止。\n窗口失去焦点、最小化或关闭时也会请求停止。", Dock = DockStyle.Fill });
            log.Dock = DockStyle.Fill; log.Multiline = true; log.ReadOnly = true; log.ScrollBars = ScrollBars.Vertical; log.BackColor = Color.White; root.Controls.Add(log);
            root.Controls.Add(new Label { Text = "接线：5VO → PUL+/DIR+；PWM1 → PUL−；OUT3 → DIR−。\n无位置反馈及限位输入；软件停止有 USB 延迟，不能替代硬件急停。", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(140, 68, 35), Font = new Font(Font.FontFamily, 9) });
            connect.Click += delegate { busy = true; UpdateControls(); controller.Connect(); };
            disconnect.Click += delegate { busy = true; UpdateControls(); controller.Disconnect(); };
            up.Click += delegate { Start(true); };
            down.Click += delegate { Start(false); };
            stop.MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) controller.Stop(); };
            stop.Click += delegate { controller.Stop(); };
            Deactivate += delegate { if (current == StageState.Moving || current == StageState.Preparing || busy) controller.Stop(); };
            FormClosing += ClosingForm;
            FormClosed += delegate { controller.Finish(1000); };
            controller.Changed += delegate(StageState state, string message)
            {
                if (IsDisposed || !IsHandleCreated) return;
                try { BeginInvoke((Action)delegate { ShowState(state, message); }); }
                catch (InvalidOperationException) { }
            };
            UpdateControls();
        }
        private static void SetupButton(Button button, string text, Color color)
        {
            button.Text = text; button.BackColor = color; button.ForeColor = Color.White;
            button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 0;
            button.Size = new Size(136, 36); button.Margin = new Padding(0, 0, 12, 6);
            button.UseVisualStyleBackColor = false;
        }
        private void Start(bool upward)
        {
            if (busy || current != StageState.Idle || closing) return;
            busy = true; UpdateControls();
            controller.MoveAxis(upward, invert.Checked, (int)hz.Value, (double)seconds.Value);
        }
        private void ShowState(StageState state, string message)
        {
            if (IsDisposed) return;
            current = state; busy = false;
            status.Text = message;
            status.ForeColor = state == StageState.Fault ? Color.Firebrick : Color.FromArgb(30, 58, 90);
            if (log.TextLength > 24000) log.Clear();
            log.AppendText(DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine);
            if (closing && state == StageState.Fault) closing = false;
            UpdateControls();
            if (closing && state == StageState.Disconnected) { allowClose = true; Close(); }
        }
        private void UpdateControls()
        {
            bool idle = current == StageState.Idle && !busy && !closing;
            connect.Enabled = current == StageState.Disconnected && !busy && !closing;
            disconnect.Enabled = current != StageState.Disconnected && current != StageState.Connecting && !closing;
            up.Enabled = down.Enabled = idle;
            hz.Enabled = seconds.Enabled = invert.Enabled = !busy && !closing && (current == StageState.Idle || current == StageState.Disconnected);
            // Remain available during connection, movement, faults, and an outstanding close.
            stop.Enabled = true;
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape || keyData == Keys.Space) { controller.Stop(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        private void ClosingForm(object sender, FormClosingEventArgs e)
        {
            if (allowClose) return;
            e.Cancel = true;
            if (closing) return;
            closing = true; UpdateControls();
            status.Text = "正在停止并断开板卡，请等待…";
            controller.Disconnect();
        }
    }
}
