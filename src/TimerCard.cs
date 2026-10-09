using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Mascot
{
    public sealed class TimerMark
    {
        public string kind;
        public string message;
        public string until;
        public bool done;
    }

    static class TimerPlan
    {
        public static bool ParseClock(string text, out int hour, out int minute)
        {
            hour = 0;
            minute = 0;
            if (string.IsNullOrEmpty(text))
                return false;
            string s = text.Trim().ToLowerInvariant().Replace('.', ':');
            bool pm = s.Contains("pm");
            bool am = s.Contains("am");
            s = s.Replace("pm", "").Replace("am", "").Trim();
            int colon = s.IndexOf(':');
            if (colon <= 0)
                return false;
            int h, m;
            if (!int.TryParse(s.Substring(0, colon).Trim(), out h))
                return false;
            if (!int.TryParse(s.Substring(colon + 1).Trim(), out m))
                return false;
            if (m < 0 || m > 59 || h < 0)
                return false;
            if (am || pm)
            {
                if (h < 1 || h > 12)
                    return false;
                if (h == 12)
                    h = 0;
                if (pm)
                    h += 12;
            }
            else if (h > 23)
                return false;
            hour = h;
            minute = m;
            return true;
        }

        public static bool ParseMinutes(string text, out int minutes)
        {
            minutes = 0;
            if (string.IsNullOrEmpty(text))
                return false;
            string s = text.Trim().ToLowerInvariant();
            if (s.EndsWith("mins"))
                s = s.Substring(0, s.Length - 4).Trim();
            else if (s.EndsWith("min"))
                s = s.Substring(0, s.Length - 3).Trim();
            else if (s.EndsWith("m"))
                s = s.Substring(0, s.Length - 1).Trim();
            int colon = s.IndexOf(':');
            if (colon > 0)
            {
                int hh, mm;
                if (!int.TryParse(s.Substring(0, colon), out hh) || !int.TryParse(s.Substring(colon + 1), out mm))
                    return false;
                if (hh < 0 || mm < 0 || mm > 59)
                    return false;
                minutes = hh * 60 + mm;
            }
            else if (!int.TryParse(s, out minutes))
                return false;
            return minutes >= 1 && minutes <= 1440;
        }

        public static DateTime NextClock(DateTime now, int hour, int minute)
        {
            DateTime at = new DateTime(now.Year, now.Month, now.Day, hour, minute, 0);
            if (at <= now)
                at = at.AddDays(1);
            return at;
        }

        public static bool Due(DateTime now, DateTime until)
        {
            return now >= until;
        }

        public static string FormatLeft(TimeSpan left)
        {
            if (left.Ticks < 0)
                left = TimeSpan.Zero;
            int total = (int)left.TotalSeconds;
            int h = total / 3600;
            int m = (total % 3600) / 60;
            int s = total % 60;
            if (h > 0)
                return h.ToString() + ":" + m.ToString("00") + ":" + s.ToString("00");
            return m.ToString() + ":" + s.ToString("00");
        }

        public static string[] ClockParts(TimeSpan left)
        {
            if (left.Ticks < 0)
                left = TimeSpan.Zero;
            int total = (int)left.TotalSeconds;
            int h = total / 3600;
            int m = (total % 3600) / 60;
            int s = total % 60;
            if (h > 0)
                return new string[] { h.ToString("00"), m.ToString("00"), s.ToString("00") };
            return new string[] { m.ToString("00"), s.ToString("00") };
        }

        public static bool ParseUntil(string text, out DateTime at)
        {
            return DateTime.TryParseExact(text, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out at);
        }

        public static string Stamp(DateTime at)
        {
            return at.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        public static int SelfCheck()
        {
            int h, m;
            if (!ParseClock("12.30", out h, out m) || h != 12 || m != 30)
                return Fail("12.30");
            if (!ParseClock("1:05pm", out h, out m) || h != 13 || m != 5)
                return Fail("pm");
            if (!ParseClock("12:30am", out h, out m) || h != 0 || m != 30)
                return Fail("am");
            if (ParseClock("25:00", out h, out m))
                return Fail("bad hour");
            int mins;
            if (!ParseMinutes("30min", out mins) || mins != 30)
                return Fail("30min");
            if (!ParseMinutes("1:30", out mins) || mins != 90)
                return Fail("1:30");
            if (ParseMinutes("0", out mins))
                return Fail("zero");
            if (NextClock(new DateTime(2026, 10, 9, 10, 0, 0), 12, 30) != new DateTime(2026, 10, 9, 12, 30, 0))
                return Fail("same day");
            if (NextClock(new DateTime(2026, 10, 9, 13, 0, 0), 12, 30) != new DateTime(2026, 10, 10, 12, 30, 0))
                return Fail("next day");
            if (FormatLeft(TimeSpan.FromSeconds(90)) != "1:30")
                return Fail("fmt 90");
            if (FormatLeft(TimeSpan.FromMinutes(30)) != "30:00")
                return Fail("fmt 30");
            if (FormatLeft(TimeSpan.FromSeconds(-5)) != "0:00")
                return Fail("fmt neg");
            string[] parts = ClockParts(TimeSpan.FromSeconds(90));
            if (parts.Length != 2 || parts[0] != "01" || parts[1] != "30")
                return Fail("tiles");
            parts = ClockParts(TimeSpan.FromSeconds(3600 + 125));
            if (parts.Length != 3 || parts[0] != "01" || parts[1] != "02" || parts[2] != "05")
                return Fail("tiles h");
            if (!Due(new DateTime(2026, 10, 9, 12, 30, 0), new DateTime(2026, 10, 9, 12, 30, 0)))
                return Fail("due");
            if (Due(new DateTime(2026, 10, 9, 12, 29, 59), new DateTime(2026, 10, 9, 12, 30, 0)))
                return Fail("early");
            return 0;
        }

        static int Fail(string why)
        {
            Console.Error.WriteLine(why);
            return 30;
        }
    }

    sealed class TimerCard : Form
    {
        const int SWP_NOSIZE = 0x0001;
        const int SWP_NOACTIVATE = 0x0010;
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        struct Face
        {
            public TimerMark Mark;
            public Rectangle Box;
        }

        readonly Func<Rectangle> _pet;
        readonly List<TimerMark> _items = new List<TimerMark>();
        readonly List<Face> _face = new List<Face>();
        Skin _skin = new Skin();
        readonly Timer _ui = new Timer();
        Font _tiny;
        Font _mid;
        Font _digit;
        Font _head;
        readonly Button _countBtn;
        readonly Button _remindBtn;
        readonly Label _minLabel;
        readonly Label _noteLabel;
        readonly Label _timeLabel;
        readonly Label _msgLabel;
        readonly Label _err;
        readonly TextBox _minBox;
        readonly TextBox _noteBox;
        readonly TextBox _timeBox;
        readonly TextBox _msgBox;
        readonly Button _go;
        Rectangle _minChrome;
        Rectangle _noteChrome;
        Rectangle _timeChrome;
        Rectangle _msgChrome;
        bool _setupOpen;
        bool _quiet = true;
        bool _modeCount = true;
        int _blink = -1;
        const int FlipMs = 360;
        readonly List<Flap> _flaps = new List<Flap>();
        readonly List<string> _seen = new List<string>();

        public TimerCard(Func<Rectangle> pet)
        {
            _pet = pet;
            _tiny = new Font("Consolas", 8.5f);
            _mid = new Font("Consolas", 11f, FontStyle.Bold);
            _digit = new Font("Consolas", 20f, FontStyle.Bold);
            _head = new Font("Consolas", 13f, FontStyle.Bold);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = _skin.TimerBg;
            ForeColor = _skin.TimerFg;
            DoubleBuffered = true;
            ResizeRedraw = true;
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.None;
            Text = "Timer";
            ClientSize = new Size(328, 280);

            _countBtn = Chip("COUNT");
            _remindBtn = Chip("REMIND");
            _countBtn.Click += delegate { SetMode(true); };
            _remindBtn.Click += delegate { SetMode(false); };
            _minLabel = Lab("MINUTES");
            _noteLabel = Lab("NOTE  (optional)");
            _timeLabel = Lab("TIME   12:30  or  1:05pm");
            _msgLabel = Lab("MESSAGE");
            _minBox = Box();
            _noteBox = Box();
            _timeBox = Box();
            _msgBox = Box();
            _go = Chip("START");
            _go.Height = 34;
            _go.Font = _mid;
            _go.TabStop = true;
            _go.Click += delegate { Submit(); };
            AcceptButton = _go;
            _err = Lab("");
            _err.Visible = false;
            UseSkin();

            Controls.Add(_countBtn);
            Controls.Add(_remindBtn);
            Controls.Add(_minLabel);
            Controls.Add(_minBox);
            Controls.Add(_noteLabel);
            Controls.Add(_noteBox);
            Controls.Add(_timeLabel);
            Controls.Add(_timeBox);
            Controls.Add(_msgLabel);
            Controls.Add(_msgBox);
            Controls.Add(_go);
            Controls.Add(_err);

            _ui.Interval = 200;
            _ui.Tick += delegate { Pump(); };
            _ui.Start();
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape)
                {
                    CloseCard();
                    e.Handled = true;
                }
            };
        }

        public void LoadSaved()
        {
            _items.Clear();
            _items.AddRange(Read());
            bool flipped = CatchDue(true);
            if (flipped)
                Save();
            if (NeedsCard())
            {
                _setupOpen = false;
                ShowQuiet();
            }
            else if (flipped)
                ShowQuiet();
            Rebuild();
        }

        public void ShowSetup()
        {
            _setupOpen = true;
            _quiet = false;
            Rebuild();
            if (!IsHandleCreated)
                CreateControl();
            Follow();
            if (!Visible)
                Show();
            Follow();
            Activate();
            if (_modeCount)
                _minBox.Focus();
            else
                _timeBox.Focus();
        }

        public void Follow()
        {
            if (!IsHandleCreated)
                return;
            Rectangle pet = _pet();
            Rectangle area = Screen.FromRectangle(pet).WorkingArea;
            bool hold = Visible && Bounds.Contains(Cursor.Position);
            int x = Left;
            int y = Top;
            if (!hold)
            {
                x = pet.Left + pet.Width / 2 - Width / 2;
                y = pet.Top - Height - 4;
                if (y < area.Top)
                    y = pet.Bottom + 10;
                if (x < area.Left + 8)
                    x = area.Left + 8;
                if (x + Width > area.Right - 8)
                    x = area.Right - Width - 8;
                if (y + Height > area.Bottom - 8)
                    y = area.Bottom - Height - 8;
                if (y < area.Top)
                    y = area.Top;
            }
            if (!Visible)
            {
                Location = new Point(x, y);
                return;
            }
            SetWindowPos(Handle, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
        }

        protected override bool ShowWithoutActivation
        {
            get { return _quiet; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080;
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (!_setupOpen)
            {
                PaintFace(g);
                return;
            }
            using (Pen pen = new Pen(_skin.TimerBorder, _skin.TimerBorderW))
            {
                if (_skin.TimerRadius >= 2)
                {
                    Rectangle box = new Rectangle(1, 1, Width - 3, Height - 3);
                    using (GraphicsPath path = Skin.Round(box, _skin.TimerRadius))
                        g.DrawPath(pen, path);
                }
                else
                    Brackets(g, pen, 1, 1, Width, Height);
            }
            using (SolidBrush ink = new SolidBrush(_skin.TimerFg))
            using (SolidBrush mute = new SolidBrush(_skin.TimerMuted))
            {
                g.DrawString("TIMER", _head, ink, _skin.TimerPadL, _skin.TimerPadT);
                string clock = DateTime.Now.ToString("HH:mm:ss");
                Size cs = TextRenderer.MeasureText(clock, _tiny, new Size(200, 40), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                int xClose = Width - _skin.TimerPadR - 14;
                g.DrawString(clock, _tiny, mute, xClose - cs.Width - 10, _skin.TimerPadT + 2);
            }
            using (Pen xpen = new Pen(_skin.TimerMuted, 1.6f))
            {
                int x0 = Width - _skin.TimerPadR - 12;
                int y0 = _skin.TimerPadT + 2;
                g.DrawLine(xpen, x0, y0, x0 + 10, y0 + 10);
                g.DrawLine(xpen, x0 + 10, y0, x0, y0 + 10);
            }
            PaintField(g, _minBox, _minChrome);
            PaintField(g, _noteBox, _noteChrome);
            PaintField(g, _timeBox, _timeChrome);
            PaintField(g, _msgBox, _msgChrome);
        }

        void PaintField(Graphics g, TextBox box, Rectangle chrome)
        {
            if (box == null || !box.Visible || chrome.Width < 4 || chrome.Height < 4)
                return;
            Rectangle r = new Rectangle(chrome.X, chrome.Y, chrome.Width - 1, chrome.Height - 1);
            int rad = _skin.TimerRadius;
            if (rad * 2 > r.Width) rad = Math.Max(0, r.Width / 2);
            if (rad * 2 > r.Height) rad = Math.Max(0, r.Height / 2);
            using (GraphicsPath path = Skin.Round(r, rad))
            using (SolidBrush fill = new SolidBrush(_skin.TimerBg))
            using (Pen pen = new Pen(_skin.TimerBorder, 1.5f))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
        }

        void PaintFace(Graphics g)
        {
            _seen.Clear();
            for (int i = 0; i < _face.Count; i++)
            {
                Face face = _face[i];
                bool clock = face.Mark.kind == "count" && !face.Mark.done;
                if (clock)
                {
                    PaintTiles(g, face);
                    continue;
                }
                Color bg = _skin.RemindBg;
                Color fg = _skin.RemindFg;
                Color edge = _skin.RemindBorder;
                int rad = _skin.RemindRadius;
                Rectangle r = face.Box;
                float edgeW = _skin.RemindBorderW;
                Rectangle draw = r;
                draw.Inflate(-1, -1);
                using (SolidBrush fill = new SolidBrush(bg))
                using (Pen pen = new Pen(edge, edgeW))
                {
                    if (rad >= 2 && draw.Width > 2 && draw.Height > 2)
                    {
                        using (GraphicsPath path = Skin.Round(draw, rad))
                        {
                            g.FillPath(fill, path);
                            g.DrawPath(pen, path);
                        }
                    }
                    else
                    {
                        g.FillRectangle(fill, draw);
                        Brackets(g, pen, draw.Left, draw.Top, draw.Right, draw.Bottom);
                    }
                }
                Rectangle text = r;
                Font font = _mid;
                TextRenderer.DrawText(g, FaceText(face.Mark), font, text, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
            PruneFlaps();
        }

        void PaintTiles(Graphics g, Face face)
        {
            string[] parts = PartsOf(face.Mark);
            string id = face.Mark.until == null ? "" : face.Mark.until;
            for (int i = 0; i < parts.Length; i++)
            {
                Rectangle tile = TileAt(face.Box, i, parts.Length);
                if (tile.Width < 4 || tile.Height < 4)
                    continue;
                string key = id + ":" + i;
                _seen.Add(key);
                Flap flap = TouchFlap(key, parts[i]);
                float p = FlapProgress(flap);
                PaintFlip(g, tile, flap.From, flap.To, p);
            }
        }

        Flap TouchFlap(string key, string text)
        {
            for (int i = 0; i < _flaps.Count; i++)
            {
                if (_flaps[i].Key != key)
                    continue;
                if (_flaps[i].To != text)
                {
                    _flaps[i].From = _flaps[i].To;
                    _flaps[i].To = text;
                    _flaps[i].Start = Environment.TickCount;
                }
                return _flaps[i];
            }
            Flap flap = new Flap();
            flap.Key = key;
            flap.From = text;
            flap.To = text;
            flap.Start = Environment.TickCount - FlipMs;
            _flaps.Add(flap);
            return flap;
        }

        static float FlapProgress(Flap flap)
        {
            int age = Environment.TickCount - flap.Start;
            if (age < 0 || age >= FlipMs)
                return 1f;
            return age / (float)FlipMs;
        }

        void PruneFlaps()
        {
            for (int i = _flaps.Count - 1; i >= 0; i--)
            {
                bool keep = false;
                for (int s = 0; s < _seen.Count; s++)
                {
                    if (_flaps[i].Key == _seen[s])
                    {
                        keep = true;
                        break;
                    }
                }
                if (!keep)
                    _flaps.RemoveAt(i);
            }
        }

        void PaintFlip(Graphics g, Rectangle tile, string oldText, string newText, float p)
        {
            int mid = tile.Height / 2;
            Rectangle top = new Rectangle(tile.X, tile.Y, tile.Width, mid);
            Rectangle bot = new Rectangle(tile.X, tile.Y + mid, tile.Width, tile.Height - mid);
            using (Bitmap neu = Glyph(newText, tile.Size))
            using (Bitmap old = (p >= 1f) ? null : Glyph(oldText, tile.Size))
            {
                if (p >= 1f)
                    g.DrawImage(neu, tile);
                else
                {
                    DrawSlice(g, neu, tile, top);
                    DrawSlice(g, p < 0.5f ? old : neu, tile, bot);
                    Rectangle flap;
                    Bitmap src;
                    bool upper;
                    float edge;
                    if (p < 0.5f)
                    {
                        float u = p / 0.5f;
                        int h = Math.Max(1, (int)(top.Height * (1f - u)));
                        flap = new Rectangle(top.X, top.Bottom - h, top.Width, h);
                        src = old;
                        upper = true;
                        edge = u;
                    }
                    else
                    {
                        float u = (p - 0.5f) / 0.5f;
                        int h = Math.Max(1, (int)(bot.Height * u));
                        flap = new Rectangle(bot.X, bot.Y, bot.Width, h);
                        src = neu;
                        upper = false;
                        edge = 1f - u;
                    }
                    BlitHalf(g, src, upper, flap);
                    int shade = (int)(110 * edge);
                    using (SolidBrush dim = new SolidBrush(Color.FromArgb(shade, 0, 0, 0)))
                        g.FillRectangle(dim, flap);
                }
            }
            using (Pen pen = new Pen(_skin.ClockBorder, _skin.ClockBorderW))
            using (GraphicsPath path = Skin.Round(tile, _skin.ClockRadius))
                g.DrawPath(pen, path);
        }

        static void DrawSlice(Graphics g, Bitmap src, Rectangle tile, Rectangle clip)
        {
            GraphicsState state = g.Save();
            g.SetClip(clip);
            g.DrawImage(src, tile);
            g.Restore(state);
        }

        Bitmap Glyph(string text, Size size)
        {
            Bitmap bmp = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height));
            using (Graphics g = Graphics.FromImage(bmp))
            {
                Rectangle area = new Rectangle(0, 0, bmp.Width, bmp.Height);
                Color bg = _skin.ClockBg;
                using (LinearGradientBrush fill = new LinearGradientBrush(area, Lift(bg, 36), bg, LinearGradientMode.Vertical))
                    g.FillRectangle(fill, area);
                TextRenderer.DrawText(g, text, _digit, area, _skin.ClockFg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }
            return bmp;
        }

        static void BlitHalf(Graphics g, Bitmap src, bool upper, Rectangle dest)
        {
            if (dest.Width < 1 || dest.Height < 1)
                return;
            int mid = src.Height / 2;
            Rectangle slice = upper
                ? new Rectangle(0, 0, src.Width, Math.Max(1, mid))
                : new Rectangle(0, mid, src.Width, Math.Max(1, src.Height - mid));
            g.DrawImage(src, dest, slice, GraphicsUnit.Pixel);
        }

        static Color Lift(Color c, int n)
        {
            return Color.FromArgb(c.A < 1 ? 255 : c.A, Math.Min(255, c.R + n), Math.Min(255, c.G + n), Math.Min(255, c.B + n));
        }

        static string[] PartsOf(TimerMark mark)
        {
            DateTime at;
            if (!TimerPlan.ParseUntil(mark.until, out at))
                return new string[] { "00", "00" };
            return TimerPlan.ClockParts(at - DateTime.Now);
        }

        const int TileGap = 6;

        static Rectangle TileAt(Rectangle box, int i, int n)
        {
            int side = n <= 1 ? box.Width : (box.Width - TileGap * (n - 1)) / n;
            return new Rectangle(box.X + i * (side + TileGap), box.Y, side, box.Height);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;
            if (_setupOpen)
            {
                if (e.Y < _skin.TimerPadT + 22 && e.X > Width - _skin.TimerPadR - 24)
                    CloseCard();
                return;
            }
            for (int i = _face.Count - 1; i >= 0; i--)
            {
                if (!_face[i].Box.Contains(e.Location))
                    continue;
                TimerMark mark = _face[i].Mark;
                bool clock = mark.kind == "count" && !mark.done;
                if (clock)
                {
                    if (e.Clicks >= 2)
                        Remove(mark);
                    return;
                }
                Remove(mark);
                return;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _ui.Stop();
                _ui.Dispose();
                _tiny.Dispose();
                _mid.Dispose();
                _digit.Dispose();
                _head.Dispose();
            }
            base.Dispose(disposing);
        }

        void ShowQuiet()
        {
            _quiet = true;
            if (!Visible)
                Show();
            Follow();
        }

        void CloseCard()
        {
            _setupOpen = false;
            if (!NeedsCard())
            {
                Hide();
                Rebuild();
                return;
            }
            Rebuild();
        }

        void SetMode(bool count)
        {
            _modeCount = count;
            Fail("");
            LayoutCard();
        }

        void Submit()
        {
            if (_modeCount)
                AddCount();
            else
                AddRemind();
        }

        void AddCount()
        {
            int mins;
            if (!TimerPlan.ParseMinutes(_minBox.Text, out mins))
            {
                Fail("Use 30, 30min, or 1:30");
                return;
            }
            if (_items.Count >= 12)
            {
                Fail("12 timers max. Clear one.");
                return;
            }
            TimerMark mark = new TimerMark();
            mark.kind = "count";
            mark.message = Trim(_noteBox.Text);
            mark.until = TimerPlan.Stamp(DateTime.Now.AddMinutes(mins));
            mark.done = false;
            _items.Add(mark);
            _minBox.Text = "";
            _noteBox.Text = "";
            _err.Text = "";
            _setupOpen = false;
            Save();
            Rebuild();
        }

        void AddRemind()
        {
            int hour, minute;
            if (!TimerPlan.ParseClock(_timeBox.Text, out hour, out minute))
            {
                Fail("Time like 12:30 or 1:05pm.");
                return;
            }
            string msg = Trim(_msgBox.Text);
            if (msg.Length == 0)
            {
                Fail("Write the reminder.");
                return;
            }
            if (_items.Count >= 12)
            {
                Fail("12 timers max. Clear one.");
                return;
            }
            TimerMark mark = new TimerMark();
            mark.kind = "remind";
            mark.message = msg;
            mark.until = TimerPlan.Stamp(TimerPlan.NextClock(DateTime.Now, hour, minute));
            mark.done = false;
            _items.Add(mark);
            _timeBox.Text = "";
            _msgBox.Text = "";
            _err.Text = "";
            _setupOpen = false;
            Save();
            if (!NeedsCard())
            {
                Hide();
                return;
            }
            Rebuild();
        }

        void Remove(TimerMark mark)
        {
            _items.Remove(mark);
            Save();
            if (!_setupOpen && !NeedsCard())
                Hide();
            Rebuild();
        }

        void Pump()
        {
            if (CatchDue(true))
            {
                Save();
                ShowQuiet();
                Rebuild();
                return;
            }
            int b = Environment.TickCount / 450;
            if (b != _blink)
            {
                _blink = b;
                Invalidate(new Rectangle(0, 0, Width, 44));
            }
            if (!Visible)
                return;
            if (!_setupOpen)
            {
                if (TilesStale())
                    LayoutFace();
                _ui.Interval = HasTicking() ? 40 : 200;
                Invalidate();
            }
            else
                _ui.Interval = 200;
            Follow();
        }

        bool CatchDue(bool sound)
        {
            bool any = false;
            DateTime now = DateTime.Now;
            for (int i = 0; i < _items.Count; i++)
            {
                TimerMark mark = _items[i];
                if (mark == null || mark.done)
                    continue;
                DateTime at;
                if (!TimerPlan.ParseUntil(mark.until, out at))
                    continue;
                if (!TimerPlan.Due(now, at))
                    continue;
                mark.done = true;
                any = true;
            }
            if (any && sound)
            {
                try { SystemSounds.Exclamation.Play(); }
                catch { }
            }
            return any;
        }

        bool NeedsCard()
        {
            for (int i = 0; i < _items.Count; i++)
            {
                TimerMark mark = _items[i];
                if (mark == null)
                    continue;
                if (mark.done || mark.kind == "count")
                    return true;
            }
            return false;
        }

        void Rebuild()
        {
            UseSkin();
            LayoutCard();
            Follow();
        }

        void LayoutCard()
        {
            bool count = _setupOpen && _modeCount;
            bool remind = _setupOpen && !_modeCount;
            _countBtn.Visible = _setupOpen;
            _remindBtn.Visible = _setupOpen;
            _minLabel.Visible = count;
            _minBox.Visible = count;
            _noteLabel.Visible = count;
            _noteBox.Visible = count;
            _timeLabel.Visible = remind;
            _timeBox.Visible = remind;
            _msgLabel.Visible = remind;
            _msgBox.Visible = remind;
            _go.Visible = _setupOpen;
            _err.Visible = _setupOpen && _err.Text.Length > 0;
            if (!_setupOpen)
            {
                LayoutFace();
                return;
            }
            int padL = _skin.TimerPadL;
            int padT = _skin.TimerPadT;
            int padR = _skin.TimerPadR;
            int padB = _skin.TimerPadB;
            int inner = 296;
            int width = inner + padL + padR;
            int gap = 8;
            int labelH = TextRenderer.MeasureText("Ag", _tiny).Height + 2;
            int btnH = labelH + 12;
            int boxH = btnH;
            int headerH = TextRenderer.MeasureText("TIMER", _head).Height;
            int y = padT + headerH + 12;
            int half = (inner - gap) / 2;
            _countBtn.SetBounds(padL, y, half, btnH);
            _remindBtn.SetBounds(padL + half + gap, y, inner - half - gap, btnH);
            StyleChip(_countBtn, _modeCount);
            StyleChip(_remindBtn, !_modeCount);
            y += btnH + 12;
            if (count)
            {
                _minLabel.SetBounds(padL, y, inner, labelH);
                PlaceField(_minBox, ref _minChrome, padL, y + labelH, inner, boxH);
                _noteLabel.SetBounds(padL, y + labelH + boxH + 8, inner, labelH);
                PlaceField(_noteBox, ref _noteChrome, padL, y + labelH + boxH + 8 + labelH, inner, boxH);
                _go.Text = "START";
                _go.SetBounds(padL, y + (labelH + boxH + 8) * 2, inner, btnH + 6);
                y += (labelH + boxH + 8) * 2 + btnH + 6;
            }
            else
            {
                _timeLabel.SetBounds(padL, y, inner, labelH);
                PlaceField(_timeBox, ref _timeChrome, padL, y + labelH, inner, boxH);
                _msgLabel.SetBounds(padL, y + labelH + boxH + 8, inner, labelH);
                PlaceField(_msgBox, ref _msgChrome, padL, y + labelH + boxH + 8 + labelH, inner, boxH);
                _go.Text = "SET";
                _go.SetBounds(padL, y + (labelH + boxH + 8) * 2, inner, btnH + 6);
                y += (labelH + boxH + 8) * 2 + btnH + 6;
            }
            StyleChip(_go, true);
            if (_err.Visible)
            {
                y += 6;
                _err.SetBounds(padL, y, inner, labelH * 2);
                y += labelH * 2;
            }
            ClientSize = new Size(width, y + padB);
            BackColor = _skin.TimerBg;
            ClipRound(_skin.TimerRadius);
            Invalidate();
        }

        void PlaceField(TextBox box, ref Rectangle chrome, int x, int y, int w, int h)
        {
            chrome = new Rectangle(x, y, w, h);
            int inset = Math.Max(8, _skin.TimerRadius);
            int top = 6;
            box.SetBounds(x + inset, y + top, Math.Max(8, w - inset * 2), Math.Max(8, h - top * 2));
            if (box.Region != null)
            {
                Region prev = box.Region;
                box.Region = null;
                prev.Dispose();
            }
        }

        void LayoutFace()
        {
            _face.Clear();
            int y = 0;
            int maxW = 8;
            TextFormatFlags measure = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            for (int i = 0; i < _items.Count; i++)
            {
                TimerMark mark = _items[i];
                if (mark == null)
                    continue;
                bool clock = mark.kind == "count" && !mark.done;
                if (!clock && !mark.done)
                    continue;
                string text = FaceText(mark);
                Font font = clock ? _digit : _mid;
                int padL = clock ? _skin.ClockPadL : _skin.RemindPadL;
                int padT = clock ? _skin.ClockPadT : _skin.RemindPadT;
                int padR = clock ? _skin.ClockPadR : _skin.RemindPadR;
                int padB = clock ? _skin.ClockPadB : _skin.RemindPadB;
                int w;
                int h;
                if (clock)
                {
                    int n = PartsOf(mark).Length;
                    Size sz = TextRenderer.MeasureText("00", font, new Size(200, 80), measure);
                    int side = Math.Max(sz.Width + padL + padR, sz.Height + padT + padB);
                    w = side * n + TileGap * (n - 1);
                    h = side;
                }
                else
                {
                    Size sz = TextRenderer.MeasureText(text, font, new Size(400, 80), measure);
                    w = sz.Width + padL + padR;
                    h = sz.Height + padT + padB;
                    if (w > 240)
                        w = 240;
                }
                Face face = new Face();
                face.Mark = mark;
                face.Box = new Rectangle(0, y, w, h);
                _face.Add(face);
                if (w > maxW)
                    maxW = w;
                y += h + 4;
            }
            if (_face.Count == 0)
            {
                ClientSize = new Size(8, 8);
                ClipRound(0);
                return;
            }
            bool ticking = false;
            for (int i = 0; i < _face.Count; i++)
            {
                if (_face[i].Mark.kind == "count" && !_face[i].Mark.done)
                    ticking = true;
            }
            ClientSize = new Size(maxW, y);
            BackColor = ticking ? _skin.ClockBg : _skin.RemindBg;
            ClipFaces();
            Invalidate();
        }

        void ClipFaces()
        {
            using (GraphicsPath all = new GraphicsPath())
            {
                for (int i = 0; i < _face.Count; i++)
                {
                    bool clock = _face[i].Mark.kind == "count" && !_face[i].Mark.done;
                    if (clock)
                    {
                        int n = PartsOf(_face[i].Mark).Length;
                        for (int t = 0; t < n; t++)
                        {
                            using (GraphicsPath one = Skin.Round(TileAt(_face[i].Box, t, n), _skin.ClockRadius))
                                all.AddPath(one, false);
                        }
                        continue;
                    }
                    int rad = _skin.RemindRadius;
                    using (GraphicsPath one = Skin.Round(_face[i].Box, rad))
                        all.AddPath(one, false);
                }
                SetShape(all);
            }
        }

        void ClipRound(int rad)
        {
            using (GraphicsPath path = Skin.Round(new Rectangle(0, 0, Width, Height), rad))
                SetShape(path);
        }

        void SetShape(GraphicsPath path)
        {
            Region next = new Region(path);
            Region prev = Region;
            Region = next;
            if (prev != null)
                prev.Dispose();
        }

        static string FaceText(TimerMark mark)
        {
            if (mark.kind == "count" && !mark.done)
            {
                DateTime at;
                if (!TimerPlan.ParseUntil(mark.until, out at))
                    return "--";
                return TimerPlan.FormatLeft(at - DateTime.Now);
            }
            if (mark.message != null && mark.message.Length > 0)
                return mark.message;
            return mark.kind == "count" ? "Timer down" : "Reminder";
        }

        void UseSkin()
        {
            _skin = Skin.Load();
            FitFont(ref _digit, _skin.ClockFont, FontStyle.Bold, "Segoe UI");
            FitFont(ref _mid, _skin.RemindFont, FontStyle.Bold, "Consolas");
            FitFont(ref _tiny, _skin.TimerFont, FontStyle.Regular, "Consolas");
            FitFont(ref _head, Math.Max(11f, _skin.TimerFont + 2f), FontStyle.Bold, "Consolas");
            BackColor = _skin.TimerBg;
            ForeColor = _skin.TimerFg;
            Color label = _skin.TimerMuted;
            _minLabel.ForeColor = label;
            _noteLabel.ForeColor = label;
            _timeLabel.ForeColor = label;
            _msgLabel.ForeColor = label;
            _minLabel.BackColor = _skin.TimerBg;
            _noteLabel.BackColor = _skin.TimerBg;
            _timeLabel.BackColor = _skin.TimerBg;
            _msgLabel.BackColor = _skin.TimerBg;
            _err.ForeColor = _skin.TimerAlert;
            _err.BackColor = _skin.TimerBg;
            _minLabel.Font = _tiny;
            _noteLabel.Font = _tiny;
            _timeLabel.Font = _tiny;
            _msgLabel.Font = _tiny;
            _err.Font = _tiny;
            _countBtn.Font = _tiny;
            _remindBtn.Font = _tiny;
            _go.Font = _tiny;
            BoxColor(_minBox);
            BoxColor(_noteBox);
            BoxColor(_timeBox);
            BoxColor(_msgBox);
        }

        void BoxColor(TextBox box)
        {
            box.BackColor = _skin.TimerBg;
            box.ForeColor = _skin.TimerFg;
            box.Font = _tiny;
        }

        static void FitFont(ref Font font, float size, FontStyle style, string family)
        {
            if (font != null && Math.Abs(font.Size - size) < 0.1f && font.Style == style && font.FontFamily.Name == family)
                return;
            if (font != null)
                font.Dispose();
            font = new Font(family, size, style);
        }

        bool HasTicking()
        {
            for (int i = 0; i < _face.Count; i++)
            {
                TimerMark mark = _face[i].Mark;
                if (mark != null && mark.kind == "count" && !mark.done)
                    return true;
            }
            return false;
        }

        sealed class Flap
        {
            public string Key;
            public string From;
            public string To;
            public int Start;
        }

        bool TilesStale()
        {
            for (int i = 0; i < _face.Count; i++)
            {
                TimerMark mark = _face[i].Mark;
                if (mark.kind != "count" || mark.done)
                    continue;
                int n = PartsOf(mark).Length;
                int side = _face[i].Box.Height;
                int expect = side * n + TileGap * (n - 1);
                if (_face[i].Box.Width != expect)
                    return true;
            }
            return false;
        }

        void Fail(string msg)
        {
            _err.Text = msg;
            LayoutCard();
        }

        static string Trim(string text)
        {
            if (text == null)
                return "";
            text = text.Trim();
            if (text.Length > 120)
                text = text.Substring(0, 120);
            return text;
        }

        void Save()
        {
            try
            {
                JavaScriptSerializer ser = new JavaScriptSerializer();
                File.WriteAllText(FilePath(), ser.Serialize(_items));
            }
            catch
            {
            }
        }

        static List<TimerMark> Read()
        {
            List<TimerMark> list = new List<TimerMark>();
            try
            {
                string path = FilePath();
                if (!File.Exists(path))
                    return list;
                TimerMark[] arr = new JavaScriptSerializer().Deserialize<TimerMark[]>(File.ReadAllText(path));
                if (arr == null)
                    return list;
                for (int i = 0; i < arr.Length; i++)
                {
                    TimerMark mark = arr[i];
                    DateTime at;
                    if (mark == null || (mark.kind != "count" && mark.kind != "remind"))
                        continue;
                    if (!TimerPlan.ParseUntil(mark.until, out at))
                        continue;
                    if (mark.message == null)
                        mark.message = "";
                    list.Add(mark);
                }
            }
            catch
            {
            }
            return list;
        }

        static string FilePath()
        {
            return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "timers.json");
        }

        Label Lab(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.ForeColor = _skin.TimerMuted;
            l.Font = _tiny;
            l.BackColor = _skin.TimerBg;
            l.AutoSize = false;
            return l;
        }

        TextBox Box()
        {
            TextBox t = new TextBox();
            t.BorderStyle = BorderStyle.None;
            t.AutoSize = false;
            t.BackColor = _skin.TimerBg;
            t.ForeColor = _skin.TimerFg;
            t.Font = _mid;
            t.MaxLength = 80;
            t.HandleCreated += delegate(object s, EventArgs e)
            {
                TextBox box = (TextBox)s;
                SendMessage(box.Handle, 0xD3, (IntPtr)3, (IntPtr)(4 | (4 << 16)));
            };
            return t;
        }

        RoundButton Chip(string text)
        {
            RoundButton b = new RoundButton();
            b.Text = text;
            b.Font = _tiny;
            b.Cursor = Cursors.Hand;
            b.TabStop = false;
            StyleChip(b, false);
            return b;
        }

        void StyleChip(Button b, bool on)
        {
            b.BackColor = on ? _skin.TimerButton : _skin.TimerBg;
            b.FlatAppearance.BorderColor = on ? _skin.TimerBorder : _skin.TimerMuted;
            b.FlatAppearance.MouseOverBackColor = _skin.TimerButton;
            b.ForeColor = on ? _skin.TimerFg : _skin.TimerMuted;
            RoundButton round = b as RoundButton;
            if (round != null)
            {
                round.Radius = _skin.TimerRadius;
                round.Invalidate();
            }
        }

        static void Brackets(Graphics g, Pen pen, int left, int top, int right, int bottom)
        {
            int n = 8;
            right -= 1;
            bottom -= 1;
            g.DrawLine(pen, left, top, left + n, top);
            g.DrawLine(pen, left, top, left, top + n);
            g.DrawLine(pen, right - n, top, right, top);
            g.DrawLine(pen, right, top, right, top + n);
            g.DrawLine(pen, left, bottom, left + n, bottom);
            g.DrawLine(pen, left, bottom - n, left, bottom);
            g.DrawLine(pen, right - n, bottom, right, bottom);
            g.DrawLine(pen, right, bottom - n, right, bottom);
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }

    sealed class RoundButton : Button
    {
        public int Radius = 12;

        public RoundButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color outside = Parent == null ? BackColor : Parent.BackColor;
            g.Clear(outside);
            int rad = Radius;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (rad * 2 > r.Width) rad = Math.Max(0, r.Width / 2);
            if (rad * 2 > r.Height) rad = Math.Max(0, r.Height / 2);
            using (GraphicsPath path = Skin.Round(r, rad))
            using (SolidBrush fill = new SolidBrush(BackColor))
            using (Pen pen = new Pen(FlatAppearance.BorderColor, 1.5f))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }
}
