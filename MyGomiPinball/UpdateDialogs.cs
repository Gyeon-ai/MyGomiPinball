using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MyGomiPinball
{
    // Keep the frame and controls on one opaque surface; DWM owns the anti-aliased outer corners.
    internal class UpdateDialog : Form
    {
        protected static readonly Color SurfaceColor = Color.FromArgb(255, 252, 250);
        protected static readonly Color TextColor = Color.FromArgb(43, 28, 38);
        protected static readonly Color MutedColor = Color.FromArgb(92, 60, 72);
        protected static readonly Color AccentColor = Color.FromArgb(229, 65, 101);
        protected static readonly Color BorderColor = Color.FromArgb(241, 183, 195);
        private bool _nativeFrame;

        protected UpdateDialog(string caption, bool canClose)
        {
            Text = caption;
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            ControlBox = false;
            BackColor = SurfaceColor;
            Font = UiFont.Make(9.0f, FontStyle.Regular);
            DoubleBuffered = true;

            if (canClose)
            {
                var close = new UpdateActionButton();
                close.Name = "closeButton";
                close.Text = "×";
                close.AccessibleName = "닫기";
                close.Font = UiFont.Make(11f, FontStyle.Regular);
                close.FillColor = SurfaceColor;
                close.LineColor = SurfaceColor;
                close.ForeColor = MutedColor;
                close.SetBounds(374, 21, 28, 28);
                close.DialogResult = DialogResult.Cancel;
                close.TabIndex = 2;
                Controls.Add(close);
            }
        }

        protected void DragWindow(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, 0x00A1, new IntPtr(2), IntPtr.Zero);
            }
        }

        protected Label AddText(string name, string text, float size, FontStyle style, Color color,
            int x, int y, int width, int height)
        {
            var label = new UpdateTextLabel();
            label.Name = name;
            label.Text = text;
            label.Font = UiFont.Make(size, style);
            label.ForeColor = color;
            label.BackColor = SurfaceColor;
            label.SetBounds(x, y, width, height);
            Controls.Add(label);
            return label;
        }

        protected UpdateActionButton AddAction(string name, string text, bool primary, int x, int y)
        {
            var button = new UpdateActionButton();
            button.Name = name;
            button.Text = text;
            button.Font = UiFont.Make(10.0f, FontStyle.Bold);
            button.FillColor = primary ? AccentColor : Color.White;
            button.LineColor = primary ? AccentColor : BorderColor;
            button.ForeColor = primary ? Color.White : TextColor;
            button.SetBounds(x, y, 92, 38);
            Controls.Add(button);
            return button;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams value = base.CreateParams;
                value.Style |= 0x00C00000 | 0x00040000; // DWM frame, without a native caption strip.
                return value;
            }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0083 && message.WParam != IntPtr.Zero)
            {
                message.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref message);
            if (message.Msg == 0x0084 && message.Result.ToInt32() >= 10 && message.Result.ToInt32() <= 17)
            {
                message.Result = new IntPtr(1); // Fixed-size dialog: do not expose resize edges.
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int preference = 2;
                _nativeFrame = DwmSetWindowAttribute(Handle, 33, ref preference, sizeof(int)) == 0;
                int color = ColorTranslator.ToWin32(BorderColor);
                DwmSetWindowAttribute(Handle, 34, ref color, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // Older Windows keeps a clean rectangular frame instead of a jagged binary region.
            if (!_nativeFrame)
            {
                using (var pen = new Pen(BorderColor))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
                }
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    }

    internal sealed class UpdatePromptDialog : UpdateDialog
    {
        public UpdatePromptDialog(Version version, string releaseNotes) : base("업데이트 안내", true)
        {
            var title = AddText("title", "새 버전을 사용할 수 있습니다", 13.0f, FontStyle.Bold, TextColor, 18, 20, 348, 30);
            title.MouseDown += DragWindow;
            AddText("version", "버전 " + version, 9.5f, FontStyle.Bold, AccentColor, 18, 56, 384, 22);
            var notesBox = new RoundedPanel();
            notesBox.Name = "notesBox";
            notesBox.AccessibleName = "업데이트 내용";
            notesBox.Radius = 8;
            notesBox.FillColor = Color.White;
            notesBox.BorderColor = BorderColor;
            notesBox.SetBounds(18, 86, 384, 0);
            Controls.Add(notesBox);

            var notesTitle = AddText("notesTitle", "업데이트 내용", 9.5f, FontStyle.Bold, TextColor, 14, 12, 356, 22);
            notesTitle.BackColor = notesBox.FillColor;
            notesBox.Controls.Add(notesTitle);
            var notes = AddText("notes", releaseNotes, 10.0f, FontStyle.Regular, MutedColor, 14, 40, 356, 42);
            notes.BackColor = notesBox.FillColor;
            notesBox.Controls.Add(notes);
            Size measured = TextRenderer.MeasureText(notes.Text, notes.Font, new Size(notes.Width, Int32.MaxValue),
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
            notes.Height = Math.Max(42, measured.Height + 4);
            notesBox.Height = notes.Bottom + 14;
            int buttonY = notesBox.Bottom + 16;
            ClientSize = new Size(420, buttonY + 38 + 16);

            var later = AddAction("laterButton", "나중에", false, 208, buttonY);
            later.DialogResult = DialogResult.Cancel;
            later.TabIndex = 0;
            var update = AddAction("updateButton", "업데이트", true, 310, buttonY);
            update.DialogResult = DialogResult.OK;
            update.TabIndex = 1;
            AcceptButton = update;
            CancelButton = later;
            ActiveControl = later;
        }
    }

    internal sealed class UpdateTextLabel : Label
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
        }
    }

    // A real Button preserves Enter/Escape, tab navigation and accessibility while matching RoundButton.
    internal sealed class UpdateActionButton : Button
    {
        public Color FillColor { get; set; }
        public Color LineColor { get; set; }
        private bool _hover;
        private bool _pressed;

        public UpdateActionButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? BackColor : Parent.BackColor);
            if (Width < 3 || Height < 3) return;
            const int renderScale = 4;
            // Supersample only the outline; draw text at native resolution for crisp glyphs.
            using (var bitmap = new Bitmap(Width * renderScale, Height * renderScale))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Parent == null ? BackColor : Parent.BackColor);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.ScaleTransform(renderScale, renderScale);
                int radius = Math.Max(1, (int)Math.Round(12 * e.Graphics.DpiX / 96));
                var rect = new Rectangle(1, 1, Width - 3, Height - 3);
                using (GraphicsPath path = Shape.Rounded(rect, radius))
                using (var fill = new SolidBrush(FillColor))
                using (var border = new Pen(LineColor))
                {
                    graphics.FillPath(fill, path);
                    if (_hover || _pressed)
                    {
                        using (var overlay = new SolidBrush(Color.FromArgb(_pressed ? 22 : 10, Color.Black)))
                            graphics.FillPath(overlay, path);
                    }
                    graphics.DrawPath(border, path);
                }
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                e.Graphics.DrawImage(bitmap, ClientRectangle, 0, 0, bitmap.Width, bitmap.Height, GraphicsUnit.Pixel);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(5, 5, Width - 10, Height - 10), ForeColor, FillColor);
        }
    }
}
