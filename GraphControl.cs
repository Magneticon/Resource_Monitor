using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LegacyGpuMonitor
{
    public sealed class GraphControl : Control
    {
        private readonly List<float> samples = new List<float>();
        private float maximum = 100;
        private string unit = "";
        private string valueText = "";

        private static readonly Color PlotBlack = Color.Black;
        private static readonly Color GridGreen = Color.FromArgb(0, 72, 0);
        private static readonly Color TraceGreen = Color.FromArgb(0, 255, 0);

        public GraphControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = PlotBlack;
            ForeColor = TraceGreen;
            Font = new Font("Tahoma", 8.25f);
            MinimumSize = new Size(90, 60);
        }

        public float Maximum
        {
            get { return maximum; }
            set { maximum = value <= 0 ? 100 : value; Invalidate(); }
        }

        public string Unit
        {
            get { return unit; }
            set { unit = value ?? ""; Invalidate(); }
        }

        public string ValueText
        {
            get { return valueText; }
            set { valueText = value ?? "N/A"; Invalidate(); }
        }

        public void AddSample(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) value = 0;
            if (value < 0) value = 0;
            if (value > maximum) value = maximum;

            samples.Add(value);
            int maxSamples = Math.Max(60, Width / 2);
            while (samples.Count > maxSamples) samples.RemoveAt(0);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            using (SolidBrush b = new SolidBrush(PlotBlack)) g.FillRectangle(b, ClientRectangle);

            Rectangle plot = new Rectangle(2, 2, Math.Max(1, Width - 4), Math.Max(1, Height - 4));

            using (Pen grid = new Pen(GridGreen, 1.0f))
            {
                // Keep grid cells approximately square.  There are always eight
                // horizontal cells; vertical divisions follow the graph aspect.
                const int horizontalDivisions = 8;
                for (int i = 0; i <= horizontalDivisions; i++)
                {
                    int y = plot.Top + plot.Height * i / horizontalDivisions;
                    g.DrawLine(grid, plot.Left, y, plot.Right, y);
                }

                int verticalDivisions = Math.Max(1,
                    (int)Math.Round(plot.Width * horizontalDivisions / (double)Math.Max(1, plot.Height)));
                verticalDivisions = Math.Min(160, verticalDivisions);
                for (int i = 0; i <= verticalDivisions; i++)
                {
                    int x = plot.Left + plot.Width * i / verticalDivisions;
                    g.DrawLine(grid, x, plot.Top, x, plot.Bottom);
                }
            }

            if (samples.Count > 1)
            {
                // Fixed distance between consecutive samples in pixels.
                // 2.0f gives a dense XP Task Manager-style graph.
                float sampleSpacing = 2.0f * (g.DpiX / 96.0f);

                // Number of samples that can physically fit in the graph.
                int maxVisibleSamples = Math.Max(2, (int)(plot.Width / sampleSpacing) + 1);

                // Only draw the newest samples that fit.
                int firstSample = Math.Max(0, samples.Count - maxVisibleSamples);

                int visibleCount = samples.Count - firstSample;

                PointF[] pts = new PointF[visibleCount];

                for (int i = 0; i < visibleCount; i++)
                {
                    int sampleIndex = firstSample + i;

                    // Newest sample stays at the right edge.
                    // Earlier samples extend toward the left.
                    float x = plot.Right - (visibleCount - 1 - i) * sampleSpacing;

                    float value = samples[sampleIndex];

                    // Protect against invalid values.
                    if (value < 0.0f)
                        value = 0.0f;

                    if (value > maximum)
                        value = maximum;

                    float y =
                        plot.Bottom -
                        (value / maximum) * plot.Height;

                    pts[i] = new PointF(x, y);
                }

                using (Pen line = new Pen(TraceGreen, 1.0f))
                {
                    g.DrawLines(line, pts);
                }
            }

            using (Pen border = new Pen(Color.FromArgb(150, 150, 150))) g.DrawRectangle(border, plot);
        }
    }
}
