using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SolidWorksTeamRenameTool
{
    internal sealed class GtkButton : Button
    {
        private bool _hover;
        private bool _pressed;

        public bool Active { get; set; }
        public bool Subtle { get; set; }

        public GtkButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            UseVisualStyleBackColor = false;
            BackColor = Color.Transparent;
            ForeColor = GtkTheme.Text;
            FlatAppearance.BorderSize = 0;
            Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular);
            Padding = Padding.Empty;
            TextAlign = ContentAlignment.MiddleCenter;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            _pressed = true;
            Invalidate();
            base.OnMouseDown(mevent);
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(mevent);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using (SolidBrush clear = new SolidBrush(Parent == null ? GtkTheme.App : Parent.BackColor))
            {
                e.Graphics.FillRectangle(clear, ClientRectangle);
            }

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return;
            }

            Color top = Subtle ? Color.FromArgb(248, 249, 250) : Color.FromArgb(252, 252, 252);
            Color bottom = Subtle ? Color.FromArgb(226, 231, 235) : Color.FromArgb(229, 233, 237);
            Color border = GtkTheme.BorderDark;
            Color text = Enabled ? GtkTheme.Text : GtkTheme.DisabledText;

            bool drawShine = Enabled && !Active;

            if (!Enabled)
            {
                top = Color.FromArgb(241, 243, 245);
                bottom = Color.FromArgb(224, 228, 232);
                border = GtkTheme.Border;
                text = GtkTheme.DisabledText;
                drawShine = false;
            }
            else if (Active)
            {
                top = Color.FromArgb(77, 163, 218);
                bottom = GtkTheme.Accent;
                border = Color.FromArgb(32, 110, 164);
                text = Color.White;
            }
            else if (_pressed)
            {
                top = Color.FromArgb(217, 222, 226);
                bottom = Color.FromArgb(242, 244, 246);
            }
            else if (_hover)
            {
                top = Color.White;
                bottom = Color.FromArgb(235, 239, 242);
                border = GtkTheme.Accent;
            }

            using (GraphicsPath path = GtkTheme.Rounded(rect, 3))
            using (LinearGradientBrush brush = new LinearGradientBrush(rect, top, bottom, LinearGradientMode.Vertical))
            using (Pen pen = new Pen(border))
            {
                e.Graphics.FillPath(brush, path);
                if (drawShine)
                {
                    Rectangle shineRect = new Rectangle(rect.X + 2, rect.Y + 2, Math.Max(1, rect.Width - 4), 2);
                    using (LinearGradientBrush shine = new LinearGradientBrush(
                        shineRect,
                        Color.FromArgb(170, Color.White),
                        Color.FromArgb(20, Color.White),
                        LinearGradientMode.Vertical))
                    {
                        e.Graphics.FillRectangle(shine, shineRect);
                    }
                }
                e.Graphics.DrawPath(pen, path);
            }

            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                rect,
                text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    internal sealed class GtkFrame : Panel
    {
        public GtkFrame()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = GtkTheme.Surface;
            Padding = new Padding(10);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (SolidBrush fill = new SolidBrush(BackColor))
            {
                e.Graphics.FillRectangle(fill, ClientRectangle);
            }
            using (GraphicsPath path = GtkTheme.Rounded(rect, 3))
            using (Pen light = new Pen(Color.White))
            using (Pen border = new Pen(GtkTheme.Border))
            {
                e.Graphics.DrawPath(light, path);
                rect.Inflate(-1, -1);
                using (GraphicsPath inner = GtkTheme.Rounded(rect, 2))
                {
                    e.Graphics.DrawPath(border, inner);
                }
            }
        }
    }

    internal sealed class GtkGroupBox : GroupBox
    {
        public GtkGroupBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = GtkTheme.Surface;
            ForeColor = GtkTheme.Text;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);
            Rectangle rect = new Rectangle(0, 8, Width - 1, Height - 9);
            using (GraphicsPath path = GtkTheme.Rounded(rect, 3))
            using (Pen border = new Pen(GtkTheme.Border))
            {
                e.Graphics.DrawPath(border, path);
            }

            Size textSize = TextRenderer.MeasureText(Text, Font);
            Rectangle textRect = new Rectangle(10, 0, textSize.Width + 8, 18);
            using (SolidBrush brush = new SolidBrush(BackColor))
            {
                e.Graphics.FillRectangle(brush, textRect);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, new Point(14, 1), GtkTheme.Text);
        }
    }

    internal sealed class GtkToolbar : Panel
    {
        public GtkToolbar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(238, 241, 243);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (SolidBrush fill = new SolidBrush(BackColor))
            {
                e.Graphics.FillRectangle(fill, ClientRectangle);
            }
            using (Pen light = new Pen(Color.White))
            using (Pen dark = new Pen(GtkTheme.Border))
            {
                e.Graphics.DrawLine(light, 0, 0, Width, 0);
                e.Graphics.DrawLine(dark, 0, Height - 1, Width, Height - 1);
            }
        }
    }

    internal sealed class GtkTabButton : Button
    {
        private bool _hover;
        private bool _pressed;

        public GtkTabButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            UseVisualStyleBackColor = false;
            BackColor = Color.Transparent;
            ForeColor = GtkTheme.Text;
            FlatAppearance.BorderSize = 0;
            Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            Padding = Padding.Empty;
            TextAlign = ContentAlignment.MiddleCenter;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            _pressed = true;
            Invalidate();
            base.OnMouseDown(mevent);
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(mevent);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using (SolidBrush clear = new SolidBrush(Parent == null ? GtkTheme.App : Parent.BackColor))
            {
                e.Graphics.FillRectangle(clear, ClientRectangle);
            }

            Rectangle rect = new Rectangle(0, 2, Width - 1, Height - 2);
            Color top = Enabled ? Color.FromArgb(252, 253, 254) : Color.FromArgb(239, 241, 243);
            Color bottom = Enabled ? Color.FromArgb(224, 229, 233) : Color.FromArgb(224, 228, 232);
            Color border = Enabled ? GtkTheme.BorderDark : GtkTheme.Border;
            Color text = Enabled ? GtkTheme.Text : GtkTheme.DisabledText;

            if (Enabled && _pressed)
            {
                top = Color.FromArgb(218, 223, 228);
                bottom = Color.FromArgb(243, 245, 247);
            }
            else if (Enabled && _hover)
            {
                top = Color.White;
                bottom = Color.FromArgb(232, 237, 241);
                border = GtkTheme.Accent;
            }

            using (GraphicsPath path = GtkTheme.TopRoundedTab(rect, 4))
            using (LinearGradientBrush brush = new LinearGradientBrush(rect, top, bottom, LinearGradientMode.Vertical))
            using (Pen pen = new Pen(border))
            {
                e.Graphics.FillPath(brush, path);
                Rectangle shineRect = new Rectangle(rect.X + 2, rect.Y + 2, Math.Max(1, rect.Width - 4), 2);
                using (LinearGradientBrush shine = new LinearGradientBrush(
                    shineRect,
                    Color.FromArgb(160, Color.White),
                    Color.FromArgb(30, Color.White),
                    LinearGradientMode.Vertical))
                {
                    e.Graphics.FillRectangle(shine, shineRect);
                }
                e.Graphics.DrawPath(pen, path);
            }

            using (Pen bottomPen = new Pen(Parent == null ? GtkTheme.Border : Parent.BackColor))
            {
                e.Graphics.DrawLine(bottomPen, 1, Height - 1, Width - 2, Height - 1);
            }

            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                rect,
                text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    internal static class GtkTheme
    {
        public static readonly Color App = Color.FromArgb(232, 235, 238);
        public static readonly Color Surface = Color.FromArgb(248, 249, 250);
        public static readonly Color Work = Color.White;
        public static readonly Color Border = Color.FromArgb(180, 188, 196);
        public static readonly Color BorderDark = Color.FromArgb(142, 151, 160);
        public static readonly Color Text = Color.FromArgb(32, 36, 40);
        public static readonly Color Muted = Color.FromArgb(85, 94, 103);
        public static readonly Color DisabledText = Color.FromArgb(142, 148, 154);
        public static readonly Color Accent = Color.FromArgb(45, 142, 202);

        public static GraphicsPath Rounded(Rectangle rect, int radius)
        {
            int d = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static GraphicsPath TopRoundedTab(Rectangle rect, int radius)
        {
            int d = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            path.AddLine(rect.Right, rect.Top + radius, rect.Right, rect.Bottom);
            path.AddLine(rect.Right, rect.Bottom, rect.Left, rect.Bottom);
            path.AddLine(rect.Left, rect.Bottom, rect.Left, rect.Top + radius);
            path.CloseFigure();
            return path;
        }
    }
}
