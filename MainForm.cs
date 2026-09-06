using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Reflection;
using System.Runtime.InteropServices;

namespace LegacyGpuMonitor
{
    public sealed class MainForm : Form
    {
        //detect Win version

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OSVERSIONINFOEX
        {
            public int dwOSVersionInfoSize;
            public int dwMajorVersion;
            public int dwMinorVersion;
            public int dwBuildNumber;
            public int dwPlatformId;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szCSDVersion;

            public ushort wServicePackMajor;
            public ushort wServicePackMinor;
            public ushort wSuiteMask;
            public byte wProductType;
            public byte wReserved;
        }

        [DllImport("ntdll.dll", CharSet = CharSet.Unicode)]
        private static extern int RtlGetVersion(ref OSVERSIONINFOEX versionInfo);

        private static Version GetRealWindowsVersion()
        {
            OSVERSIONINFOEX versionInfo = new OSVERSIONINFOEX();

            versionInfo.dwOSVersionInfoSize =
                Marshal.SizeOf(typeof(OSVERSIONINFOEX));

            int status = RtlGetVersion(ref versionInfo);

            if (status == 0)
            {
                return new Version(
                    versionInfo.dwMajorVersion,
                    versionInfo.dwMinorVersion,
                    versionInfo.dwBuildNumber);
            }

            // Fallback if RtlGetVersion unexpectedly fails.
            return Environment.OSVersion.Version;
        }

        private static bool IsWindows10OrLater()
        {
            Version v = GetRealWindowsVersion();

            return v.Major >= 10;
        }

        private readonly SystemMonitor system;
        private readonly NvidiaApi nvapi;
        private readonly NvmlApi nvml;
        private readonly Timer timer;

        private readonly GraphControl cpu = Graph();
        private readonly GraphControl gpu = Graph();
        private readonly GraphControl ram = Graph();
        private readonly GraphControl threeD = Graph();
        private readonly GraphControl vram = Graph();
        private readonly GraphControl cuda = Graph();
        private readonly GraphControl gpuTemp = TempGraph();
        private readonly GraphControl cpuTemp = TempGraph();

        private readonly MeterControl cpuMeter = Meter();
        private readonly MeterControl gpuMeter = Meter();
        private readonly MeterControl ramMeter = Meter();
        private readonly MeterControl threeDMeter = Meter();
        private readonly MeterControl vramMeter = Meter();
        private readonly MeterControl cudaMeter = Meter();
        private readonly MeterControl cpuTempMeter = TempMeter();
        private readonly MeterControl gpuTempMeter = TempMeter();

        private readonly List<GraphControl> coreGraphs = new List<GraphControl>();
        private readonly TableLayoutPanel corePanel = new TableLayoutPanel();
        private readonly Dictionary<GraphControl, NoFlickerLabel> detailLabels = new Dictionary<GraphControl, NoFlickerLabel>();
        private ToolStripStatusLabel statusLeftItem;
        private ToolStripStatusLabel statusRightItem;
        private readonly Timer uptimeTimer = new Timer();

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_COMPOSITED = 0x02000000;

                CreateParams cp = base.CreateParams;

                if (Environment.OSVersion.Version.Major == 5)
                {
                    cp.ExStyle |= WS_EX_COMPOSITED;
                }

                return cp;
            }
        }

        public MainForm()
        {

            DoubleBuffered = true;

            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);

            UpdateStyles();
            Text = "Resource Monitor";
            this.Icon = LoadApplicationIcon();
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1000, 900);
            MinimumSize = new Size(600, 840);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            BackColor = SystemColors.Control;
            ForeColor = SystemColors.ControlText;
            Font = new Font("Tahoma", 8.25f);

            system = new SystemMonitor();
            nvapi = new NvidiaApi();
            nvml = new NvmlApi();
            BuildUi();

            timer = new Timer(); timer.Interval = 1000; timer.Tick += Tick;
            uptimeTimer.Interval = 1000; uptimeTimer.Tick += delegate { UpdateUptime(); };
            timer.Start(); uptimeTimer.Start();

            FormClosed += delegate
            {
                timer.Stop(); uptimeTimer.Stop(); system.Dispose(); nvapi.Dispose(); nvml.Dispose();
            };
        }

        private Icon LoadApplicationIcon()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();

            using (System.IO.Stream stream =
                assembly.GetManifestResourceStream("LegacyGpuMonitor.1.ico"))
            {
                if (stream != null)
                    return new Icon(stream);
            }

            return null;
        }

        private static GraphControl Graph() { GraphControl g = new GraphControl(); g.Dock = DockStyle.Fill; return g; }
        private static GraphControl TempGraph() { GraphControl g = Graph(); g.Maximum = 120; g.Unit = "°C"; return g; }
        private static MeterControl Meter() { MeterControl m = new MeterControl(); m.Dock = DockStyle.Fill; return m; }
        private static MeterControl TempMeter() { MeterControl m = Meter(); m.Maximum = 120; m.Unit = "°C"; return m; }

        private void BuildUi()
        {
            Control performance = BuildPerformancePage(); performance.Dock = DockStyle.Fill; Controls.Add(performance);
            StatusStrip strip = new StatusStrip(); strip.SizingGrip = true;
            statusLeftItem = new ToolStripStatusLabel(); statusLeftItem.Spring = true; statusLeftItem.TextAlign = ContentAlignment.MiddleLeft; statusLeftItem.Text = "";
            statusRightItem = new ToolStripStatusLabel(); statusRightItem.Spring = false; statusRightItem.Text = "System Uptime: 0d 00:00:00"; statusRightItem.BorderSides = ToolStripStatusLabelBorderSides.Left; statusRightItem.BorderStyle = Border3DStyle.Etched;
            strip.Items.Add(statusLeftItem); 
            strip.Items.Add(statusRightItem); Controls.Add(strip);
        }

        private Control BuildPerformancePage()
        {
            Panel page = new Panel(); page.BackColor = SystemColors.Control; page.Padding = new Padding(4);
            TableLayoutPanel root = new TableLayoutPanel(); root.Dock = DockStyle.Fill; root.Padding = new Padding(4); root.ColumnCount = 2; root.RowCount = 5;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 18)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 18)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 18)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 18)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 28));

            root.Controls.Add(BuildMetricGroup("CPU Usage", cpuMeter, cpu, 100), 0, 0);
            root.Controls.Add(BuildMetricGroup("Memory Usage (Physical RAM)", ramMeter, ram, 100), 1, 0);
            root.Controls.Add(BuildMetricGroup("GPU Usage", gpuMeter, gpu, 100), 0, 1);
            root.Controls.Add(BuildMetricGroup("VRAM Usage", vramMeter, vram, 100), 1, 1);
            root.Controls.Add(BuildMetricGroup("3D Utilization", threeDMeter, threeD, 100), 0, 2);
            root.Controls.Add(BuildMetricGroup("CUDA / Compute Utilization", cudaMeter, cuda, 100), 1, 2);
            if (!IsWindows10OrLater())
                root.Controls.Add(BuildMetricGroup("CPU Temperature", cpuTempMeter, cpuTemp, 120), 0, 3);
            else
                root.Controls.Add(BuildMetricGroup("ACPI Temperature", cpuTempMeter, cpuTemp, 120), 0, 3);
            root.Controls.Add(BuildMetricGroup("GPU Temperature", gpuTempMeter, gpuTemp, 120), 1, 3);

            corePanel.Dock = DockStyle.Fill; corePanel.AutoScroll = false; corePanel.AutoSize = false; corePanel.GrowStyle = TableLayoutPanelGrowStyle.FixedSize; corePanel.BackColor = SystemColors.Control; corePanel.Margin = new Padding(0); corePanel.Padding = new Padding(0); BuildCoreGraphs();
            Control coreGroup = Group("CPU Usage - Individual Logical Processors", corePanel); root.Controls.Add(coreGroup, 0, 4); root.SetColumnSpan(coreGroup, 2);
            page.Controls.Add(root); return page;
        }

        private Control BuildMetricGroup(string title, MeterControl meter, GraphControl graph, float maximum)
        {
            GroupBox box = new GroupBox(); box.Text = title; box.Dock = DockStyle.Fill; box.Padding = new Padding(7, 14, 7, 5);
            meter.Maximum = maximum; graph.Maximum = maximum;
            TableLayoutPanel layout = new TableLayoutPanel(); layout.Dock = DockStyle.Fill; layout.ColumnCount = 2; layout.RowCount = 2;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            layout.Controls.Add(meter, 0, 0); layout.SetRowSpan(meter, 2); layout.Controls.Add(graph, 1, 0);
            NoFlickerLabel detail = new NoFlickerLabel(); detail.Dock = DockStyle.Fill; detail.TextAlign = ContentAlignment.MiddleLeft; detail.AutoEllipsis = true; detail.Font = Font; detail.ForeColor = SystemColors.ControlText; detail.Padding = new Padding(2, 0, 0, 0); layout.Controls.Add(detail, 1, 1);
            detailLabels[graph] = detail; box.Controls.Add(layout); return box;
        }

        private Control Group(string title, Control content)
        {
            GroupBox box = new GroupBox(); box.Text = title; box.Dock = DockStyle.Fill; box.Padding = new Padding(6, 14, 6, 5); box.Controls.Add(content); return box;
        }

        private void BuildCoreGraphs()
        {
            int n = Math.Max(1, system.CoreCount); corePanel.ColumnCount = n; corePanel.RowCount = 1;
            for (int i = 0; i < n; i++) corePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / n));
            for (int i = 0; i < n; i++)
            {
                GraphControl g = Graph(); g.Maximum = 100; g.Margin = new Padding(1); coreGraphs.Add(g);
                Panel p = new Panel(); p.Dock = DockStyle.Fill; p.Margin = new Padding(1); p.Padding = new Padding(0);
                NoFlickerLabel l = new NoFlickerLabel(); l.Dock = DockStyle.Top; l.Height = 20; l.Text = "CPU " + i.ToString(); l.TextAlign = ContentAlignment.MiddleCenter; l.Font = new Font(Font, FontStyle.Bold);
                NoFlickerLabel value = new NoFlickerLabel(); value.Dock = DockStyle.Bottom; value.Height = 18; value.TextAlign = ContentAlignment.MiddleCenter;
                p.Controls.Add(g); p.Controls.Add(value); p.Controls.Add(l); corePanel.Controls.Add(p, i, 0); g.Tag = value;
            }
            corePanel.Resize += delegate { ResizeCoreGraphs(); };
            ResizeCoreGraphs();
        }

        private void ResizeCoreGraphs()
        {
            if (corePanel.ColumnCount <= 0) return;
            int available = Math.Max(100, corePanel.ClientSize.Width - 2);
            int width = Math.Max(70, available / corePanel.ColumnCount);
            foreach (Control c in corePanel.Controls) c.MinimumSize = new Size(60, 80);
            // TableLayoutPanel percentage columns do the actual responsive sizing.
            // No horizontal scrollbar is used.
        }

        private void UpdateCoreValue(GraphControl graph, float value)
        {
       //     Label label = graph.Tag as Label;
            NoFlickerLabel label = graph.Tag as NoFlickerLabel;
            if (label != null)
            {
                string text = value.ToString("0") + "%";
                if (!String.Equals(label.Text, text, StringComparison.Ordinal)) label.Text = text;
            }
        }

        float maxCPUValue = 0;
        float maxGPUValue = 0;
        float minCPUValue = 500;
        float minGPUValue = 500;
        private void Tick(object sender, EventArgs e)
        {
            system.RefreshHardwareInfo();
            float cpuValue = system.CpuPercent, ramValue = system.RamPercent;
            SetMetric(cpu, cpuMeter, cpuValue, cpuValue.ToString("0") + "%", FormatCpuSpeed());
            SetMetric(ram, ramMeter, ramValue, ramValue.ToString("0") + "%", FormatMemory());
            for (int i = 0; i < coreGraphs.Count; i++) { float v = system.GetCorePercent(i); coreGraphs[i].ValueText = ""; coreGraphs[i].AddSample(v); UpdateCoreValue(coreGraphs[i], v); }

            float ctemp = system.CpuTemperatureC;
            if (ctemp > maxCPUValue)
                maxCPUValue = ctemp;
            if (ctemp < minCPUValue)
                minCPUValue = ctemp;
            SetMetric(cpuTemp, cpuTempMeter, ctemp, IsValid(ctemp) ? ctemp.ToString("0") + " °C" : "N/A", IsValid(ctemp) ? "Current: " + ctemp.ToString("0") + " °C    Min: " + minCPUValue.ToString("0") + " °C    Max: " + maxCPUValue.ToString("0") + " °C" : system.CpuTemperatureSource);

            string gpuUsage = "N/A";
            NvidiaApi.Sample a = nvapi.Read(); NvmlApi.Sample b = nvml.Read();
            if (nvapi.IsAvailable)
            {
                gpuUsage = (a.GpuUtilization).ToString("0") + "%";
                SetMetric(gpu, gpuMeter, a.GpuUtilization, gpuUsage, FormatGpuClocks(a));
                SetMetric(threeD, threeDMeter, a.ThreeDUtilization, a.ThreeDUtilization.ToString("0") + "%", "Graphics engine: " + a.ThreeDUtilization.ToString("0") + "%");
            }
            else { 
                SetMetric(gpu, gpuMeter, float.NaN, "N/A", "Core: N/A    Memory: N/A"); SetMetric(threeD, threeDMeter, float.NaN, "N/A", "Graphics engine: N/A"); 
            }

            // Prefer NVML for VRAM when available, otherwise use the NVAPI
            // framebuffer allocation counter. Both are real memory counters.
            string vramUsage = "N/A";
            if (IsValid(b.VramUtilization) && b.VramTotalMb > 0){
                vramUsage = b.VramUtilization.ToString("0") + "%";
                SetMetric(vram, vramMeter, b.VramUtilization, vramUsage, FormatVram(b.VramUsedMb, b.VramTotalMb));
            }
            else if (IsValid(a.VramUtilization) && a.VramTotalMb > 0){
                vramUsage = a.VramUtilization.ToString("0") + "%";
                SetMetric(vram, vramMeter, a.VramUtilization, vramUsage, FormatVram(a.VramUsedMb, a.VramTotalMb));
            }
            else
                SetMetric(vram, vramMeter, float.NaN, "N/A", "Used: N/A    Free: N/A    Total: N/A");

            // Never substitute NVAPI graphics/D3D activity for CUDA. On this
            // legacy target the only trustworthy CUDA-specific signal available
            // is NVML's compute-process list. If NVML is absent, report 0% and
            // explicitly say that no CUDA context was reported by the available
            // legacy interface; do not scan for nvcuda.dll because many NVIDIA
            // components load it without executing CUDA work.
            float cudaValue = 0;
            string cudaDetail;
            if (b.CudaCounterReliable)
            {
                cudaValue = b.CudaProcessActive ? b.CudaUtilization : 0;
                cudaDetail = b.CudaProcessActive
                    ? "CUDA compute activity: " + b.CudaUtilization.ToString("0") + "%"
                    : "CUDA compute activity: 0% (no CUDA compute process)";
            }
            else
            {
                cudaDetail = "CUDA compute activity: 0% (NVML compute counter unavailable)";
            }
            SetMetric(cuda, cudaMeter, cudaValue, cudaValue.ToString("0") + "%", cudaDetail);

            float gt = b.TemperatureC > 0 ? b.TemperatureC : a.TemperatureC;
            if (gt > maxGPUValue)
                maxGPUValue = gt;
            if (gt < minGPUValue)
                minGPUValue = gt;
            SetMetric(gpuTemp, gpuTempMeter, gt, gt > 0 ? gt.ToString("0") + " °C" : "N/A", gt > 0 ? "Current: " + gt.ToString("0") + " °C    Min: " + minGPUValue.ToString("0") + " °C    Max: " + maxGPUValue.ToString("0") + " °C" : "Current: N/A");
            UpdateStatus(cpuValue, ramValue, gpuUsage, vramUsage);
        }

        private void SetMetric(GraphControl graph, MeterControl meter, float value, string text, string detail)
        {
            bool valid = IsValid(value); float safe = valid ? value : 0;
            graph.ValueText = ""; graph.AddSample(safe); meter.Value = safe; meter.ValueText = text;
            NoFlickerLabel label;
            if (detailLabels.TryGetValue(graph, out label) && !String.Equals(label.Text, detail, StringComparison.Ordinal))
                label.Text = detail;
        }

        private string FormatCpuSpeed() { return "Current: " + (system.CurrentCpuClockMHz > 0 ? FormatFrequency(system.CurrentCpuClockMHz) : "N/A") + "    Max: " + FormatFrequency(system.MaxCpuClockMHz); }
        private string FormatGpuClocks(NvidiaApi.Sample s) { return "Core: " + (s.GpuCoreClockMHz > 0 ? FormatFrequency(s.GpuCoreClockMHz) : "N/A") + "    Memory: " + (s.GpuMemoryClockMHz > 0 ? FormatFrequency(s.GpuMemoryClockMHz) : "N/A"); }
        private string FormatFrequency(double mhz) { if (mhz <= 0) return "N/A"; return mhz >= 1000.0 ? (mhz / 1000.0).ToString("0.00") + " GHz" : mhz.ToString("0") + " MHz"; }
        private string FormatMemory() { return system.TotalRamBytes == 0 ? "Used: N/A    Available: N/A    Total: N/A" : "Used: " + FormatBytes(system.UsedRamBytes) + "    Available: " + FormatBytes(system.AvailableRamBytes) + "    Total: " + FormatBytes(system.TotalRamBytes); }
        private string FormatVram(double usedMb, double totalMb) { if (totalMb <= 0) return "Used: N/A    Free: N/A    Total: N/A"; return "Used: " + FormatMb(usedMb) + "    Free: " + FormatMb(Math.Max(0, totalMb - usedMb)) + "    Total: " + FormatMb(totalMb); }
        private string FormatMb(double mb) { return mb >= 1024.0 ? (mb / 1024.0).ToString("0.00") + " GB" : mb.ToString("0") + " MB"; }
        private string FormatBytes(ulong bytes) { double gb = bytes / 1073741824.0; return gb >= 1.0 ? gb.ToString("0.00") + " GB" : (bytes / 1048576.0).ToString("0") + " MB"; }

        private void UpdateStatus(float cpuValue, float ramValue, string gpuValue, string vramValue)
        {
            if (statusLeftItem != null) statusLeftItem.Text = "CPU: " + cpuValue.ToString("0") + "%    GPU: " + gpuValue + "    RAM: " + ramValue.ToString("0") + "%" + "    VRAM: " + vramValue;
            UpdateUptime();
        }
        private void UpdateUptime()
        {
            if (statusRightItem == null) return; TimeSpan t = system.SystemUptime;
            statusRightItem.Text = "System Uptime: " + ((int)t.TotalDays).ToString("0") + "d " + t.Hours.ToString("00") + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00");
        }

        private sealed class NoFlickerLabel : Label
        {
            private const int WM_ERASEBKGND = 0x0014;

            public NoFlickerLabel()
            {
                SetStyle(
                    ControlStyles.UserPaint |
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw,
                    true);

                UpdateStyles();

                BackColor = SystemColors.Control;
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                // Do NOT let Label perform a separate background paint.
                // Background and text are painted together in OnPaint().
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                // Paint background first, into the same buffered surface.
                using (SolidBrush brush = new SolidBrush(BackColor))
                {
                    e.Graphics.FillRectangle(brush, ClientRectangle);
                }

                TextFormatFlags flags =
                    TextFormatFlags.NoPadding |
                    TextFormatFlags.EndEllipsis;

                // Horizontal alignment.
                switch (TextAlign)
                {
                    case ContentAlignment.TopCenter:
                    case ContentAlignment.MiddleCenter:
                    case ContentAlignment.BottomCenter:
                        flags |= TextFormatFlags.HorizontalCenter;
                        break;

                    case ContentAlignment.TopRight:
                    case ContentAlignment.MiddleRight:
                    case ContentAlignment.BottomRight:
                        flags |= TextFormatFlags.Right;
                        break;

                    default:
                        flags |= TextFormatFlags.Left;
                        break;
                }

                // Vertical alignment.
                switch (TextAlign)
                {
                    case ContentAlignment.MiddleLeft:
                    case ContentAlignment.MiddleCenter:
                    case ContentAlignment.MiddleRight:
                        flags |= TextFormatFlags.VerticalCenter;
                        break;

                    case ContentAlignment.BottomLeft:
                    case ContentAlignment.BottomCenter:
                    case ContentAlignment.BottomRight:
                        flags |= TextFormatFlags.Bottom;
                        break;

                    default:
                        flags |= TextFormatFlags.Top;
                        break;
                }

                TextRenderer.DrawText(
                    e.Graphics,
                    Text ?? String.Empty,
                    Font,
                    ClientRectangle,
                    ForeColor,
                    flags);
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_ERASEBKGND)
                {
                    // IMPORTANT:
                    // Non-zero means the erase was handled.
                    // Returning zero may allow Windows to erase again.
                    m.Result = (IntPtr)1;
                    return;
                }

                base.WndProc(ref m);
            }
        }
        private static bool IsValid(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }

        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            this.SuspendLayout();
            // 
            // MainForm
            // 
            this.ClientSize = new System.Drawing.Size(292, 257);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "MainForm";
            this.ResumeLayout(false);

        }

    }
}
