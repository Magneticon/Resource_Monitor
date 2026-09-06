using System;
using System.Drawing;
using System.Windows.Forms;

namespace LegacyGpuMonitor
{
    public sealed class MeterControl : Control
    {
        private float meterValue;
        private float maximum = 100.0f;
        private string valueText = "N/A";
        private string unit = "%";

        private static readonly Color MeterBlack = Color.Black;
        private static readonly Color MeterGreen = Color.FromArgb(0, 105, 0);
        private static readonly Color MeterBrightGreen = Color.FromArgb(0, 255, 0);

        public MeterControl()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);

            DoubleBuffered = true;

            BackColor = MeterBlack;
            ForeColor = MeterBrightGreen;

            Font = new Font("Tahoma", 9.0f, FontStyle.Bold);

            MinimumSize = new Size(70, 80);
            Size = new Size(82, 100);
        }

        public float Maximum
        {
            get { return maximum; }
            set
            {
                maximum = value > 0.0f ? value : 100.0f;

                // Keep the current meter value valid when Maximum changes.
                meterValue = Math.Max(0.0f, Math.Min(maximum, meterValue));

                Invalidate();
            }
        }

        public float Value
        {
            get { return meterValue; }
            set
            {
                meterValue = Math.Max(
                    0.0f,
                    Math.Min(maximum, value));

                Invalidate();
            }
        }

        public string ValueText
        {
            get { return valueText; }
            set
            {
                valueText = value ?? "N/A";
                Invalidate();
            }
        }

        public string Unit
        {
            get { return unit; }
            set
            {
                unit = value ?? "";
                Invalidate();
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // The entire background is painted in OnPaint().
            // This prevents a separate background erase and reduces
            // flicker while resizing.
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;

            // Paint everything in the same double-buffered operation.
            g.Clear(MeterBlack);

            int barLeft = 23;
            int barRight = Width - 23;

            int barTop = 9;
            int textReserve = 30;
            int barBottom = Math.Max(barTop + 9, Height - textReserve);

            int segments = 18;
            int gap = 2;

            int totalGap = (segments - 1) * gap;

            int availableHeight = barBottom - barTop - totalGap;

            int segmentHeight = Math.Max(
                1,
                availableHeight / segments);

            int barWidth = Math.Max(
                2,
                barRight - barLeft);

            float ratio = maximum > 0.0f
                ? meterValue / maximum
                : 0.0f;

            ratio = Math.Max(
                0.0f,
                Math.Min(1.0f, ratio));

            int lit = (int)Math.Round(segments * ratio);

            using (Brush dim = new SolidBrush(MeterGreen))
            using (Brush bright = new SolidBrush(MeterBrightGreen))
            {
                for (int i = 0; i < segments; i++)
                {
                    int y = barBottom -
                            segmentHeight -
                            i * (segmentHeight + gap);

                    g.FillRectangle(
                        i < lit ? bright : dim,
                        barLeft,
                        y,
                        barWidth,
                        segmentHeight);
                }
            }

            string text = valueText ?? String.Empty;

            TextFormatFlags flags =
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.NoPadding |
                TextFormatFlags.EndEllipsis;

            Rectangle textRectangle = new Rectangle(
                2,
                barBottom + 2,
                Math.Max(1, Width - 4),
                Math.Max(1, Height - barBottom - 2));

            TextRenderer.DrawText(
                g,
                text,
                Font,
                textRectangle,
                MeterBrightGreen,
                flags);
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_ERASEBKGND = 0x0014;

            if (m.Msg == WM_ERASEBKGND)
            {
                // Tell Windows that the background erase has already
                // been handled. OnPaint() paints the complete surface.
                m.Result = (IntPtr)1;
                return;
            }

            base.WndProc(ref m);
        }
    }
}