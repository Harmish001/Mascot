using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// ponytail: native GL, not WebView. This GLB is one static mesh, so walk/jump/float
// are body motion. A clip player belongs here only after the file grows a skeleton.

namespace Mascot
{
    static class Program
    {
        static Mutex _mutex;

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--self-check")
            {
                AttachConsole(ATTACH_PARENT_PROCESS);
                int motion = Motion.SelfCheck();
                if (motion != 0)
                    return motion;
                int skin = Skin.SelfCheck();
                if (skin != 0)
                    return skin;
                int clocks = TimerPlan.SelfCheck();
                if (clocks != 0)
                    return clocks;
                int pixels = Pixels.SelfCheck();
                if (pixels != 0)
                    return pixels;
                try
                {
                    return GlbMesh.SelfCheck(AvatarConfig.Load().ModelPath);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    return 1;
                }
            }

            try
            {
                SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
            }
            catch
            {
                SetProcessDPIAware();
            }

            bool createdNew;
            _mutex = new Mutex(true, "Local\\AvatarMascot", out createdNew);
            if (!createdNew)
                return 0;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Application.Run(new PetForm());
            }
            catch (Exception ex)
            {
                string dir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                File.WriteAllText(Path.Combine(dir, "mascot-error.txt"), ex.ToString());
                MessageBox.Show(ex.Message, "Mascot");
                return 1;
            }
            return 0;
        }

        const int ATTACH_PARENT_PROCESS = -1;
        static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);

        [DllImport("kernel32.dll")]
        static extern bool AttachConsole(int dwProcessId);

        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    }

    // Movement is pure so --self-check can run without a window.
    static class Motion
    {
        public static int Width = 300;
        public static int Height = 440;
        public const float MaxStep = 8f;

        public static bool Fits(float x, float y, int w, int h, Rectangle[] areas)
        {
            if (areas == null || areas.Length == 0)
                return false;
            // Whole sprite stays inside the virtual desktop. Center must sit on a real monitor
            // so it does not park in the gap under a shorter display. Bob is ±4px.
            const int margin = 4;
            int left = areas[0].Left;
            int top = areas[0].Top;
            int right = areas[0].Right;
            int bottom = areas[0].Bottom;
            for (int i = 1; i < areas.Length; i++)
            {
                if (areas[i].Left < left) left = areas[i].Left;
                if (areas[i].Top < top) top = areas[i].Top;
                if (areas[i].Right > right) right = areas[i].Right;
                if (areas[i].Bottom > bottom) bottom = areas[i].Bottom;
            }
            if (x < left + margin || y < top + margin || x + w > right - margin || y + h > bottom - margin)
                return false;

            float cx = x + w * 0.5f;
            float cy = y + h * 0.5f;
            for (int i = 0; i < areas.Length; i++)
            {
                Rectangle a = areas[i];
                if (cx >= a.Left && cx < a.Right && cy >= a.Top && cy < a.Bottom)
                    return true;
            }
            return false;
        }

        public static void Step(ref float x, ref float y, ref float vx, ref float vy, float dt, int w, int h, Rectangle[] areas)
        {
            float dx = vx * dt;
            float dy = vy * dt;
            float mag = (float)Math.Sqrt(dx * dx + dy * dy);
            if (mag > MaxStep)
            {
                float s = MaxStep / mag;
                dx *= s;
                dy *= s;
            }

            float nx = x + dx;
            float ny = y + dy;
            if (!Fits(nx, ny, w, h, areas))
            {
                if (!Fits(nx, y, w, h, areas))
                    vx = -vx;
                if (!Fits(x, ny, w, h, areas))
                    vy = -vy;
                dx = vx * dt;
                dy = vy * dt;
                mag = (float)Math.Sqrt(dx * dx + dy * dy);
                if (mag > MaxStep)
                {
                    float s = MaxStep / mag;
                    dx *= s;
                    dy *= s;
                }
                nx = x + dx;
                ny = y + dy;
                if (!Fits(nx, ny, w, h, areas))
                {
                    nx = x;
                    ny = y;
                }
            }
            x = nx;
            y = ny;
        }

        public static bool Slide(ref float x, ref float y, float dx, float dy, int w, int h, Rectangle[] areas)
        {
            float mag = (float)Math.Sqrt(dx * dx + dy * dy);
            if (mag > MaxStep)
            {
                float s = MaxStep / mag;
                dx *= s;
                dy *= s;
            }
            if (Fits(x + dx, y + dy, w, h, areas))
            {
                x += dx;
                y += dy;
                return true;
            }
            if (Fits(x + dx, y, w, h, areas))
            {
                x += dx;
                return true;
            }
            if (Fits(x, y + dy, w, h, areas))
            {
                y += dy;
                return true;
            }
            return false;
        }

        public static int SelfCheck()
        {
            Rectangle[] one = new Rectangle[] { new Rectangle(0, 0, 200, 200) };
            float x = 40, y = 40, vx = 60, vy = 0;
            Step(ref x, ref y, ref vx, ref vy, 0.05f, 20, 20, one);
            if (!Fits(x, y, 20, 20, one))
                return Fail("inside step left the screen");

            x = 170;
            y = 40;
            vx = 80;
            vy = 0;
            for (int i = 0; i < 30; i++)
                Step(ref x, ref y, ref vx, ref vy, 0.05f, 20, 20, one);
            if (!Fits(x, y, 20, 20, one) || vx >= 0)
                return Fail("right edge did not bounce");

            Rectangle[] two = new Rectangle[] {
                new Rectangle(0, 0, 200, 200),
                new Rectangle(200, 0, 200, 200)
            };
            x = 170;
            y = 40;
            vx = 80;
            vy = 0;
            bool crossed = false;
            for (int i = 0; i < 40; i++)
            {
                Step(ref x, ref y, ref vx, ref vy, 0.05f, 20, 20, two);
                if (x >= 190)
                    crossed = true;
                if (!Fits(x, y, 20, 20, two))
                    return Fail("gap between adjacent monitors");
            }
            if (!crossed || vx < 0)
                return Fail("bounce on a monitor seam");

            Rectangle[] neg = new Rectangle[] { new Rectangle(-500, -200, 400, 300) };
            x = -480;
            y = -100;
            vx = -40;
            vy = 10;
            for (int i = 0; i < 40; i++)
                Step(ref x, ref y, ref vx, ref vy, 0.05f, 20, 20, neg);
            if (!Fits(x, y, 20, 20, neg))
                return Fail("negative monitor origin");

            if (Fits(10, -8, 20, 20, one))
                return Fail("allowed sprite above the screen");

            Rectangle[] gap = new Rectangle[] {
                new Rectangle(0, 0, 200, 200),
                new Rectangle(200, 0, 200, 120)
            };
            if (Fits(250, 150, 20, 20, gap))
                return Fail("parked in the gap under a shorter monitor");

            Console.WriteLine("ok");
            return 0;
        }

        static int Fail(string why)
        {
            Console.Error.WriteLine(why);
            return 1;
        }
    }

    struct Heart
    {
        public float X;
        public float Y;
        public float Life;
    }

    struct FaceRig
    {
        public bool Has;
        public float Lx, Ly, Lw, Lh;
        public float Rx, Ry, Rw, Rh;
        public Color Lid;
        public Color Line;

        public static FaceRig For(string path)
        {
            FaceRig r = new FaceRig();
            if (string.IsNullOrEmpty(path))
                return r;
            string name = Path.GetFileName(path).ToLowerInvariant();
            if (name.StartsWith("doraemon"))
            {
                r.Has = true;
                r.Lx = 0.40f; r.Ly = 0.235f; r.Lw = 0.12f; r.Lh = 0.075f;
                r.Rx = 0.62f; r.Ry = 0.235f; r.Rw = 0.12f; r.Rh = 0.075f;
                r.Lid = Color.FromArgb(255, 244, 243, 242);
                r.Line = Color.FromArgb(255, 35, 35, 40);
                return r;
            }
            if (name.StartsWith("panda"))
            {
                r.Has = true;
                r.Lx = 0.399f; r.Ly = 0.356f; r.Lw = 0.10f; r.Lh = 0.062f;
                r.Rx = 0.700f; r.Ry = 0.332f; r.Rw = 0.10f; r.Rh = 0.062f;
                r.Lid = Color.FromArgb(255, 12, 12, 14);
                r.Line = Color.FromArgb(255, 30, 24, 24);
                return r;
            }
            return r;
        }
    }

    sealed class MenuCommand
    {
        public string label;
        public string command;
        public string action;
        public float? yawOffset;
        public float? turn;
        public float? scale;

        public static MenuCommand[] Load(string file, string fallback)
        {
            string dir = Path.GetDirectoryName(Application.ExecutablePath);
            string path = Path.Combine(dir, file);
            if (!File.Exists(path))
                File.WriteAllText(path, fallback);
            try
            {
                JavaScriptSerializer ser = new JavaScriptSerializer();
                return ser.Deserialize<MenuCommand[]>(File.ReadAllText(path));
            }
            catch
            {
                return new MenuCommand[0];
            }
        }

        public static string CommandsText()
        {
            return "[\r\n"
                + "  { \"label\": \"Timers\", \"action\": \"timers\" },\r\n"
                + "  { \"label\": \"Notepad\", \"command\": \"notepad.exe\" }\r\n"
                + "]\r\n";
        }

        public static string AvatarsText()
        {
            return "[\r\n"
                + "  { \"label\": \"Dance\", \"action\": \"dance\" },\r\n"
                + "  { \"label\": \"Doraemon\", \"action\": \"avatar\", \"command\": \"doraemon.png\" },\r\n"
                + "  { \"label\": \"Panda\", \"action\": \"avatar\", \"command\": \"panda.png\" },\r\n"
                + "  { \"label\": \"Fish\", \"action\": \"avatar\", \"command\": \"3d avatars/cartoon_clownfish_animated (1).glb\", \"yawOffset\": 90, \"turn\": 180, \"scale\": 1 }\r\n"
                + "]\r\n";
        }
    }

    sealed class PetEvent
    {
        public string on;
        public string action;
        public string command;
        public string[] lines;

        public static PetEvent Find(string name)
        {
            PetEvent[] all = Load();
            if (all == null)
                return null;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && string.Equals(all[i].on, name, StringComparison.OrdinalIgnoreCase))
                    return all[i];
            }
            return null;
        }

        static PetEvent[] Load()
        {
            string path = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "events.json");
            if (!File.Exists(path))
                File.WriteAllText(path, DefaultText());
            try
            {
                return new JavaScriptSerializer().Deserialize<PetEvent[]>(File.ReadAllText(path));
            }
            catch
            {
                return new PetEvent[0];
            }
        }

        static string DefaultText()
        {
            return "[\r\n"
                + "  { \"on\": \"click\", \"action\": \"laugh\" },\r\n"
                + "  { \"on\": \"doubleClick\", \"action\": \"hide\" },\r\n"
                + "  { \"on\": \"hover\", \"action\": \"say\", \"lines\": [\"Hi!\", \"Hey there\", \"Hello!\", \"What's up?\", \"Boop!\"] },\r\n"
                + "  { \"on\": \"pet\", \"action\": \"happy\" },\r\n"
                + "  { \"on\": \"rightClick\", \"action\": \"menu\" },\r\n"
                + "  { \"on\": \"longPress\" },\r\n"
                + "  { \"on\": \"mouseDown\" },\r\n"
                + "  { \"on\": \"mouseUp\" },\r\n"
                + "  { \"on\": \"mouseLeave\" },\r\n"
                + "  { \"on\": \"middleClick\" },\r\n"
                + "  { \"on\": \"wheelUp\" },\r\n"
                + "  { \"on\": \"wheelDown\" }\r\n"
                + "]\r\n";
        }
    }

    static class Pixels
    {
        public static bool Bounds(byte[] alpha, int w, int h, out int left, out int top, out int right, out int bottom)
        {
            left = w;
            top = h;
            right = 0;
            bottom = 0;
            if (alpha == null || w < 1 || h < 1 || alpha.Length < w * h)
                return false;
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    if (alpha[row + x] < 16)
                        continue;
                    if (x < left) left = x;
                    if (y < top) top = y;
                    if (x + 1 > right) right = x + 1;
                    if (y + 1 > bottom) bottom = y + 1;
                }
            }
            return right > left && bottom > top;
        }

        public static int SelfCheck()
        {
            byte[] a = new byte[12];
            a[1 * 4 + 2] = 200;
            int l, t, r, b;
            if (!Bounds(a, 4, 3, out l, out t, out r, out b) || l != 2 || t != 1 || r != 3 || b != 2)
            {
                Console.Error.WriteLine("pixel bounds");
                return 40;
            }
            a[1 * 4 + 2] = 10;
            if (Bounds(a, 4, 3, out l, out t, out r, out b))
            {
                Console.Error.WriteLine("faint pixel");
                return 41;
            }
            return 0;
        }
    }

    sealed class Skin
    {
        public Color HoverBg = Color.FromArgb(255, 32, 36, 46);
        public Color HoverFg = Color.White;
        public Color HoverBorder = Color.FromArgb(255, 90, 164, 232);
        public float HoverBorderW = 1.4f;
        public float HoverFont = 9.5f;
        public int HoverRadius = 8;
        public int HoverPadL = 11, HoverPadT = 8, HoverPadR = 11, HoverPadB = 6;
        public Color MenuBg = Color.FromArgb(255, 28, 32, 42);
        public Color MenuFg = Color.FromArgb(255, 236, 240, 246);
        public Color MenuBorder = Color.FromArgb(255, 255, 255, 255);
        public float MenuBorderW = 1f;
        public Color MenuHover = Color.FromArgb(255, 48, 118, 196);
        public float MenuFont = 10f;
        public int MenuRadius = 8;
        public int PadL = 12, PadT = 4, PadR = 14, PadB = 4;
        public Color ClockBg = Color.FromArgb(7, 16, 22);
        public Color ClockFg = Color.FromArgb(230, 252, 252);
        public Color ClockBorder = Color.FromArgb(92, 232, 240);
        public float ClockFont = 18f;
        public int ClockRadius = 2;
        public float ClockBorderW = 1.6f;
        public int ClockPadL = 6, ClockPadT = 2, ClockPadR = 6, ClockPadB = 2;
        public Color RemindBg = Color.FromArgb(42, 26, 12);
        public Color RemindFg = Color.FromArgb(255, 196, 110);
        public Color RemindBorder = Color.FromArgb(255, 176, 72);
        public float RemindFont = 11f;
        public int RemindRadius = 2;
        public float RemindBorderW = 1.6f;
        public int RemindPadL = 8, RemindPadT = 3, RemindPadR = 8, RemindPadB = 3;
        public Color TimerBg = Color.FromArgb(7, 16, 22);
        public Color TimerFg = Color.FromArgb(214, 246, 248);
        public Color TimerBorder = Color.FromArgb(92, 232, 240);
        public Color TimerMuted = Color.FromArgb(118, 168, 176);
        public Color TimerButton = Color.FromArgb(16, 86, 98);
        public Color TimerAlert = Color.FromArgb(255, 176, 72);
        public float TimerFont = 11f;
        public float TimerBorderW = 2f;
        public int TimerRadius = 2;
        public int TimerPadL = 16, TimerPadT = 14, TimerPadR = 16, TimerPadB = 14;

        public static Skin Load()
        {
            Skin s = new Skin();
            string path = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "style.css");
            if (!File.Exists(path))
                return s;
            try
            {
                Apply(s, File.ReadAllText(path));
            }
            catch
            {
            }
            return s;
        }

        public static void Apply(Skin s, string css)
        {
            css = Strip(css);
            string[] blocks = css.Split('}');
            for (int i = 0; i < blocks.Length; i++)
            {
                int brace = blocks[i].IndexOf('{');
                if (brace < 0)
                    continue;
                string sel = blocks[i].Substring(0, brace).Trim().TrimStart('.').ToLowerInvariant();
                string[] decls = blocks[i].Substring(brace + 1).Split(';');
                for (int d = 0; d < decls.Length; d++)
                {
                    int colon = decls[d].IndexOf(':');
                    if (colon < 0)
                        continue;
                    string key = decls[d].Substring(0, colon).Trim().ToLowerInvariant();
                    string val = decls[d].Substring(colon + 1).Trim();
                    if (sel == "hover")
                        SetHover(s, key, val);
                    else if (sel == "menu")
                        SetMenu(s, key, val);
                    else if (sel == "clock")
                        SetClock(s, key, val);
                    else if (sel == "reminder")
                        SetRemind(s, key, val);
                    else if (sel == "timer")
                        SetTimer(s, key, val);
                }
            }
        }

        static void SetHover(Skin s, string key, string val)
        {
            if (key == "background") s.HoverBg = ColorOf(val, s.HoverBg);
            else if (key == "color") s.HoverFg = ColorOf(val, s.HoverFg);
            else if (key == "border" || key == "border-color") ReadBorder(val, ref s.HoverBorder, ref s.HoverBorderW);
            else if (key == "font-size") s.HoverFont = SizeOf(val, s.HoverFont);
            else if (key == "radius" || key == "border-radius") s.HoverRadius = (int)SizeOf(val, s.HoverRadius);
            else if (key == "padding")
            {
                int l, t, r, b;
                PadOf(val, s.HoverPadL, s.HoverPadT, s.HoverPadR, s.HoverPadB, out l, out t, out r, out b);
                s.HoverPadL = l; s.HoverPadT = t; s.HoverPadR = r; s.HoverPadB = b;
            }
        }

        static void SetMenu(Skin s, string key, string val)
        {
            if (key == "background") s.MenuBg = ColorOf(val, s.MenuBg);
            else if (key == "color") s.MenuFg = ColorOf(val, s.MenuFg);
            else if (key == "border" || key == "border-color") ReadBorder(val, ref s.MenuBorder, ref s.MenuBorderW);
            else if (key == "hover" || key == "background-hover") s.MenuHover = ColorOf(val, s.MenuHover);
            else if (key == "font-size") s.MenuFont = SizeOf(val, s.MenuFont);
            else if (key == "radius" || key == "border-radius") s.MenuRadius = (int)SizeOf(val, s.MenuRadius);
            else if (key == "padding")
            {
                int l, t, r, b;
                PadOf(val, s.PadL, s.PadT, s.PadR, s.PadB, out l, out t, out r, out b);
                s.PadL = l; s.PadT = t; s.PadR = r; s.PadB = b;
            }
        }

        static void SetClock(Skin s, string key, string val)
        {
            if (key == "background") s.ClockBg = ColorOf(val, s.ClockBg);
            else if (key == "color") s.ClockFg = ColorOf(val, s.ClockFg);
            else if (key == "border" || key == "border-color") ReadBorder(val, ref s.ClockBorder, ref s.ClockBorderW);
            else if (key == "font-size") s.ClockFont = SizeOf(val, s.ClockFont);
            else if (key == "radius" || key == "border-radius") s.ClockRadius = (int)SizeOf(val, s.ClockRadius);
            else if (key == "padding")
            {
                int l, t, r, b;
                PadOf(val, s.ClockPadL, s.ClockPadT, s.ClockPadR, s.ClockPadB, out l, out t, out r, out b);
                s.ClockPadL = l; s.ClockPadT = t; s.ClockPadR = r; s.ClockPadB = b;
            }
        }

        static void SetRemind(Skin s, string key, string val)
        {
            if (key == "background") s.RemindBg = ColorOf(val, s.RemindBg);
            else if (key == "color") s.RemindFg = ColorOf(val, s.RemindFg);
            else if (key == "border" || key == "border-color") ReadBorder(val, ref s.RemindBorder, ref s.RemindBorderW);
            else if (key == "font-size") s.RemindFont = SizeOf(val, s.RemindFont);
            else if (key == "radius" || key == "border-radius") s.RemindRadius = (int)SizeOf(val, s.RemindRadius);
            else if (key == "padding")
            {
                int l, t, r, b;
                PadOf(val, s.RemindPadL, s.RemindPadT, s.RemindPadR, s.RemindPadB, out l, out t, out r, out b);
                s.RemindPadL = l; s.RemindPadT = t; s.RemindPadR = r; s.RemindPadB = b;
            }
        }

        static void SetTimer(Skin s, string key, string val)
        {
            if (key == "background") s.TimerBg = ColorOf(val, s.TimerBg);
            else if (key == "color") s.TimerFg = ColorOf(val, s.TimerFg);
            else if (key == "border" || key == "border-color") ReadBorder(val, ref s.TimerBorder, ref s.TimerBorderW);
            else if (key == "muted") s.TimerMuted = ColorOf(val, s.TimerMuted);
            else if (key == "button") s.TimerButton = ColorOf(val, s.TimerButton);
            else if (key == "alert") s.TimerAlert = ColorOf(val, s.TimerAlert);
            else if (key == "font-size") s.TimerFont = SizeOf(val, s.TimerFont);
            else if (key == "radius" || key == "border-radius") s.TimerRadius = (int)SizeOf(val, s.TimerRadius);
            else if (key == "padding")
            {
                int l, t, r, b;
                PadOf(val, s.TimerPadL, s.TimerPadT, s.TimerPadR, s.TimerPadB, out l, out t, out r, out b);
                s.TimerPadL = l; s.TimerPadT = t; s.TimerPadR = r; s.TimerPadB = b;
            }
        }

        public static int SelfCheck()
        {
            Skin s = new Skin();
            Apply(s, "/* note */ .menu { background: #112233; hover: #abcdef; font-size: 14px; padding: 1 2 3 4; border-radius: 12px; } .hover { color: #ffffff; border: #010203; }");
            if (s.MenuBg.R != 0x11 || s.MenuBg.G != 0x22 || s.MenuBg.B != 0x33)
                return 20;
            if (s.MenuHover.R != 0xab || s.MenuFont != 14f)
                return 21;
            if (s.PadL != 1 || s.PadT != 2 || s.PadR != 3 || s.PadB != 4)
                return 22;
            if (s.HoverFg.B != 255 || s.HoverBorder.R != 1)
                return 23;
            if (s.MenuRadius != 12)
                return 24;
            Apply(s, ".clock { color: #abcdef; font-size: 22px; } .reminder { border: #010203; } .timer { button: #112233; alert: #ff00aa; }");
            if (s.ClockFg.R != 0xab || s.ClockFont != 22f)
                return 25;
            if (s.RemindBorder.R != 1)
                return 26;
            if (s.TimerButton.R != 0x11 || s.TimerAlert.B != 0xaa)
                return 27;
            Apply(s, ".hover { border: 2px solid red; padding: 3 4 5 6; } .menu { border-radius: 7px; } .timer { border-radius: 9px; border: 3px solid #00ff00; }");
            if (s.HoverBorder.R != 255 || s.HoverBorder.G != 0 || Math.Abs(s.HoverBorderW - 2f) > 0.01f)
                return 28;
            if (s.HoverPadL != 3 || s.HoverPadT != 4 || s.HoverPadR != 5 || s.HoverPadB != 6)
                return 29;
            if (s.MenuRadius != 7)
                return 30;
            if (s.TimerRadius != 9 || s.TimerBorder.G != 255 || Math.Abs(s.TimerBorderW - 3f) > 0.01f)
                return 31;
            Apply(s, ".menu { border: 1px solid rgb(201, 201, 201); } .timer { padding: 8 9 10 11; }");
            if (s.MenuBorder.R != 201 || s.MenuBorder.G != 201 || s.MenuBorder.B != 201)
                return 32;
            if (s.TimerPadL != 8 || s.TimerPadT != 9 || s.TimerPadR != 10 || s.TimerPadB != 11)
                return 33;
            return 0;
        }

        static void ReadBorder(string val, ref Color color, ref float width)
        {
            color = ColorOf(val, color);
            string[] parts = val.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string bit = parts[i].ToLowerInvariant();
                if (bit.EndsWith("px"))
                    bit = bit.Substring(0, bit.Length - 2);
                float w;
                if (bit.Length > 0 && (char.IsDigit(bit[0]) || bit[0] == '.') &&
                    float.TryParse(bit, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out w) &&
                    w >= 0f && w <= 32f)
                    width = w;
            }
        }

        static string Strip(string css)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            int i = 0;
            while (i < css.Length)
            {
                if (i + 1 < css.Length && css[i] == '/' && css[i + 1] == '*')
                {
                    int end = css.IndexOf("*/", i + 2);
                    if (end < 0)
                        break;
                    i = end + 2;
                    continue;
                }
                sb.Append(css[i]);
                i++;
            }
            return sb.ToString();
        }

        static Color ColorOf(string val, Color fallback)
        {
            Color hit;
            if (TryRgb(val, out hit))
                return hit;
            if (TryColor(val, out hit))
                return hit;
            string[] parts = val.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = parts.Length - 1; i >= 0; i--)
            {
                if (TryColor(parts[i], out hit))
                    return hit;
            }
            return fallback;
        }

        static bool TryRgb(string val, out Color color)
        {
            color = Color.Empty;
            int p = val.IndexOf("rgb", StringComparison.OrdinalIgnoreCase);
            if (p < 0)
                return false;
            int open = val.IndexOf('(', p);
            int close = open < 0 ? -1 : val.IndexOf(')', open + 1);
            if (open < 0 || close < 0)
                return false;
            string[] n = val.Substring(open + 1, close - open - 1).Split(',');
            if (n.Length < 3)
                return false;
            int r, g, b;
            if (!int.TryParse(n[0].Trim(), out r) || !int.TryParse(n[1].Trim(), out g) || !int.TryParse(n[2].Trim(), out b))
                return false;
            if (r < 0 || r > 255 || g < 0 || g > 255 || b < 0 || b > 255)
                return false;
            color = Color.FromArgb(255, r, g, b);
            return true;
        }

        static bool TryColor(string val, out Color color)
        {
            color = Color.Empty;
            string v = val.Trim();
            int hash = v.IndexOf('#');
            if (hash >= 0)
            {
                v = v.Substring(hash + 1);
                int n = 0;
                while (n < v.Length && ((v[n] >= '0' && v[n] <= '9') || (v[n] >= 'a' && v[n] <= 'f') || (v[n] >= 'A' && v[n] <= 'F')))
                    n++;
                v = v.Substring(0, n);
                if (v.Length == 3)
                    v = "" + v[0] + v[0] + v[1] + v[1] + v[2] + v[2];
                int rgb;
                if (v.Length == 6 && int.TryParse(v, System.Globalization.NumberStyles.HexNumber, null, out rgb))
                {
                    color = Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
                    return true;
                }
                return false;
            }
            Color named = Color.FromName(v);
            if (!named.IsKnownColor)
                return false;
            color = Color.FromArgb(255, named);
            return true;
        }

        static float SizeOf(string val, float fallback)
        {
            string v = val.Trim().ToLowerInvariant();
            if (v.EndsWith("px"))
                v = v.Substring(0, v.Length - 2);
            float n;
            if (float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out n) && n > 0f)
                return n;
            return fallback;
        }

        static void PadOf(string val, int dl, int dt, int dr, int db, out int l, out int t, out int r, out int b)
        {
            l = dl; t = dt; r = dr; b = db;
            string[] p = val.Split(new char[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            int[] n = new int[p.Length];
            for (int i = 0; i < p.Length; i++)
            {
                string bit = p[i].ToLowerInvariant();
                if (bit.EndsWith("px"))
                    bit = bit.Substring(0, bit.Length - 2);
                if (!int.TryParse(bit, out n[i]))
                    return;
            }
            if (n.Length == 1)
                l = t = r = b = n[0];
            else if (n.Length == 2)
            {
                l = r = n[0];
                t = b = n[1];
            }
            else if (n.Length >= 4)
            {
                l = n[0]; t = n[1]; r = n[2]; b = n[3];
            }
        }

        public static GraphicsPath Round(Rectangle r, int rad)
        {
            GraphicsPath p = new GraphicsPath();
            int d = rad * 2;
            if (d < 2 || d > r.Width || d > r.Height)
            {
                p.AddRectangle(r);
                return p;
            }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    sealed class MenuLook : ProfessionalColorTable
    {
        public Color Bg = Color.FromArgb(255, 28, 32, 42);
        public Color Hi = Color.FromArgb(255, 48, 118, 196);
        public Color Edge = Color.FromArgb(255, 86, 156, 224);

        public override Color ToolStripDropDownBackground { get { return Bg; } }
        public override Color MenuBorder { get { return Edge; } }
        public override Color MenuItemBorder { get { return Color.Transparent; } }
        public override Color MenuItemSelected { get { return Hi; } }
        public override Color MenuItemSelectedGradientBegin { get { return Lift(Hi); } }
        public override Color MenuItemSelectedGradientEnd { get { return Hi; } }
        public override Color ImageMarginGradientBegin { get { return Bg; } }
        public override Color ImageMarginGradientMiddle { get { return Bg; } }
        public override Color ImageMarginGradientEnd { get { return Bg; } }
        public override Color SeparatorDark { get { return Color.FromArgb(255, 70, 80, 98); } }
        public override Color SeparatorLight { get { return Color.FromArgb(255, 70, 80, 98); } }

        static Color Lift(Color c)
        {
            return Color.FromArgb(255, Math.Min(255, c.R + 18), Math.Min(255, c.G + 18), Math.Min(255, c.B + 18));
        }
    }

    sealed class MenuPaint : ToolStripProfessionalRenderer
    {
        public Color Text = Color.FromArgb(236, 240, 246);
        public int Radius = 8;
        public float EdgeW = 1f;

        public MenuPaint(MenuLook look) : base(look)
        {
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            Rectangle r = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Skin.Round(r, Radius))
            using (SolidBrush fill = new SolidBrush(ColorTable.ToolStripDropDownBackground))
                e.Graphics.FillPath(fill, path);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            Rectangle r = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Skin.Round(r, Radius))
            using (Pen edge = new Pen(ColorTable.MenuBorder, EdgeW))
                e.Graphics.DrawPath(edge, path);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected)
                return;
            Rectangle box = e.Item.Bounds;
            box.Inflate(-4, -1);
            if (box.Width < 4 || box.Height < 4)
                return;
            int rad = Math.Min(Radius, box.Height / 2);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Skin.Round(box, rad))
            using (SolidBrush fill = new SolidBrush(ColorTable.MenuItemSelected))
                e.Graphics.FillPath(fill, path);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = Text;
            base.OnRenderItemText(e);
        }
    }

    sealed class RoundMenu : ContextMenuStrip
    {
        public int Radius = 8;

        public RoundMenu()
        {
            DropShadowEnabled = false;
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            Clip();
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            Clip();
        }

        void Clip()
        {
            if (Width < 4 || Height < 4)
                return;
            Rectangle r = new Rectangle(0, 0, Width, Height);
            using (GraphicsPath path = Skin.Round(r, Radius))
            {
                Region old = Region;
                Region = new Region(path);
                if (old != null)
                    old.Dispose();
            }
        }
    }

    sealed class SayBubble : Form
    {
        string _text = "";
        Font _font = new Font("Segoe UI", 9.5f);
        Color _bg = Color.FromArgb(255, 32, 36, 46);
        Color _fg = Color.White;
        Color _edge = Color.FromArgb(255, 90, 164, 232);
        float _edgeW = 1.4f;
        int _radius = 8;
        int _padL = 11, _padT = 8, _padR = 11, _padB = 6;

        public void Apply(Skin skin)
        {
            _bg = skin.HoverBg;
            _fg = skin.HoverFg;
            _edge = skin.HoverBorder;
            _edgeW = skin.HoverBorderW;
            _radius = skin.HoverRadius < 2 ? 2 : skin.HoverRadius;
            _padL = skin.HoverPadL;
            _padT = skin.HoverPadT;
            _padR = skin.HoverPadR;
            _padB = skin.HoverPadB;
            if (Math.Abs(_font.Size - skin.HoverFont) > 0.1f)
            {
                _font.Dispose();
                _font = new Font("Segoe UI", skin.HoverFont);
            }
        }

        public SayBubble()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            AutoScaleMode = AutoScaleMode.None;
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080 | 0x08000000;
                return cp;
            }
        }

        public void Say(string text, int anchorX, int anchorY)
        {
            _text = text;
            TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            Size sz = TextRenderer.MeasureText(text, _font, new Size(800, 200), flags);
            ClientSize = new Size(sz.Width + _padL + _padR + 4, sz.Height + _padT + _padB + 12);
            Place(anchorX, anchorY);
            Invalidate();
            if (!Visible)
                Show();
        }

        public void Place(int anchorX, int anchorY)
        {
            Location = new Point(anchorX - Width / 2, anchorY - Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            int w = Math.Max(1, Width);
            int h = Math.Max(1, Height);
            int tail = 8;
            using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    Rectangle body = new Rectangle(1, 1, w - 3, h - tail - 2);
                    int rad = _radius;
                    if (rad * 2 > body.Width) rad = Math.Max(1, body.Width / 2);
                    if (rad * 2 > body.Height) rad = Math.Max(1, body.Height / 2);
                    using (GraphicsPath path = Skin.Round(body, rad))
                    using (SolidBrush fill = new SolidBrush(_bg))
                    using (Pen edge = new Pen(_edge, _edgeW))
                    {
                        g.FillPath(fill, path);
                        g.FillPolygon(fill, new Point[] {
                            new Point(w / 2 - 6, body.Bottom - 1),
                            new Point(w / 2 + 6, body.Bottom - 1),
                            new Point(w / 2, h - 2)
                        });
                        g.DrawPath(edge, path);
                    }
                }
                KeyMagenta(bmp);
                e.Graphics.DrawImage(bmp, 0, 0);
            }
            Rectangle text = new Rectangle(_padL, _padT, w - _padL - _padR, h - tail - _padT - _padB);
            TextRenderer.DrawText(e.Graphics, _text, _font, text, _fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }

        static void KeyMagenta(Bitmap bmp)
        {
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            int stride = data.Stride;
            int bytes = Math.Abs(stride) * bmp.Height;
            byte[] buf = new byte[bytes];
            Marshal.Copy(data.Scan0, buf, 0, bytes);
            for (int y = 0; y < bmp.Height; y++)
            {
                int row = y * stride;
                if (row < 0)
                    row = (bmp.Height - 1 - y) * Math.Abs(stride);
                for (int x = 0; x < bmp.Width; x++)
                {
                    int i = row + x * 4;
                    if (buf[i + 3] < 128)
                    {
                        buf[i] = 255;
                        buf[i + 1] = 0;
                        buf[i + 2] = 255;
                        buf[i + 3] = 255;
                    }
                    else
                        buf[i + 3] = 255;
                }
            }
            Marshal.Copy(buf, 0, data.Scan0, bytes);
            bmp.UnlockBits(data);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _font.Dispose();
            base.Dispose(disposing);
        }
    }

    sealed class PetForm : Form
    {
        const int HitAlpha = 96;
        const int WM_NCHITTEST = 0x84;
        const int WM_MOUSEWHEEL = 0x020A;
        const int WM_DISPLAYCHANGE = 0x007E;
        const int HTTRANSPARENT = -1;
        const int HTCLIENT = 1;
        const int ULW_ALPHA = 2;
        const byte AC_SRC_OVER = 0;
        const byte AC_SRC_ALPHA = 1;
        const uint SWP_NOSIZE = 0x0001;
        const uint SWP_NOACTIVATE = 0x0010;
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        enum Mode { Walk, Jump, Float, Hold }

        struct Frame
        {
            public int W;
            public int H;
            public byte[] Alpha;
            public IntPtr Dib;
            public IntPtr Bits;
        }

        readonly Random _rng = new Random();
        readonly Stopwatch _clock = Stopwatch.StartNew();
        readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();
        readonly SoundPlayer _player;
        readonly MemoryStream _wav;
        NotifyIcon _tray;
        ToolStripMenuItem _startItem;
        ToolStripMenuItem _stopItem;
        Icon _trayIcon;

        Rectangle[] _areas = new Rectangle[0];
        float _x, _y, _vx, _vy;
        int _winX, _winY;
        bool _running = true;
        bool _laughing;
        double _laughUntil;
        double _nextTurn;
        double _modeUntil;
        double _nextSpecial;
        double _settleUntil;
        double _phase;
        float _yaw;
        float _heading;
        float _headingTarget;
        float _spin;
        byte[][] _flatBgra;
        byte[][] _flatAlpha;
        int _flatFace = -1;
        Bitmap _base;
        Bitmap _compose;
        FaceRig _rig;
        SayBubble _say;
        TimerCard _timers;
        double _sayUntil;
        float _petDir;
        float _petDirSign;
        int _petFlips;
        double _petMark;
        double _blinkAt;
        double _nextBlinkAt;
        double _happyUntil;
        double _danceUntil;
        Heart[] _hearts;
        ContextMenuStrip _cmdMenu;
        System.Windows.Forms.Timer _clickWait;
        System.Windows.Forms.Timer _holdWait;
        bool _armClick;
        bool _holdFired;
        Mode _mode = Mode.Walk;
        bool _presented;
        bool _dragging;
        bool _dragMoved;
        int _grabX, _grabY;
        int _dragStartX, _dragStartY;
        Frame _frame;
        GlAvatar _avatar;
        AvatarConfig _config;

        public PetForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Text = "Mascot";
            _config = AvatarConfig.Load();
            Bitmap flatImage = null;
            if (_config.Is2d)
            {
                int fw;
                int fh;
                flatImage = LoadFlatImage(_config.ModelPath, _config.Scale, out fw, out fh);
                Motion.Width = fw;
                Motion.Height = fh;
            }
            else
            {
                int mw;
                int mh;
                MeshWindow(_config.Scale, out mw, out mh);
                Motion.Width = mw;
                Motion.Height = mh;
            }
            ClientSize = new Size(Motion.Width, Motion.Height);
            Cursor = Cursors.Hand;

            _frame = CreateFrame(Motion.Width, Motion.Height);
            if (_config.Is2d)
            {
                _flatBgra = new byte[2][];
                _flatAlpha = new byte[2][];
                _base = flatImage != null ? flatImage : DrawOrange(Motion.Width, Motion.Height);
                _rig = FaceRig.For(_config.ModelPath);
                _compose = new Bitmap(Motion.Width, Motion.Height, PixelFormat.Format32bppArgb);
                _frame.Alpha = new byte[Motion.Width * Motion.Height];
                _hearts = new Heart[8];
                _nextBlinkAt = 2;
            }
            else
            {
                _avatar = new GlAvatar(GlbMesh.Load(_config.ModelPath), 1f, Motion.Width, Motion.Height);
                _frame.Alpha = _avatar.Alpha;
            }

            _wav = MakeLaughWav();
            _player = new SoundPlayer(_wav);
            _player.Load();

            _timer.Interval = 33;
            _timer.Tick += OnTick;

            BuildTray();
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_LAYERED = 0x00080000;
                const int WS_EX_TOOLWINDOW = 0x00000080;
                const int WS_EX_NOACTIVATE = 0x08000000;
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RefreshScreens();
            Respawn();
            _nextTurn = Now + 1.5;
            _nextSpecial = Now + 5;
            _heading = (float)(_rng.NextDouble() * Math.PI * 2);
            _headingTarget = _heading;
            RenderPose();
            _timer.Start();
            EnsureTimers();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _timer.Stop();
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
            }
            if (_trayIcon != null)
                DestroyIcon(_trayIcon.Handle);
            if (_avatar != null)
                _avatar.Dispose();
            if (_base != null)
                _base.Dispose();
            if (_compose != null)
                _compose.Dispose();
            if (_say != null)
                _say.Dispose();
            if (_timers != null)
                _timers.Dispose();
            if (_clickWait != null)
                _clickWait.Dispose();
            if (_holdWait != null)
                _holdWait.Dispose();
            if (_frame.Dib != IntPtr.Zero)
                DeleteObject(_frame.Dib);
            base.OnFormClosing(e);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            if (_dragging)
                return;
            RunEvent("hover");
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            HideSay();
            if (_holdWait != null)
                _holdWait.Stop();
            RunEvent("mouseLeave");
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right)
            {
                HideSay();
                RunEvent("rightClick");
                return;
            }
            if (e.Button == MouseButtons.Middle)
            {
                RunEvent("middleClick");
                return;
            }
            if (e.Button != MouseButtons.Left)
                return;
            RunEvent("mouseDown");
            ArmHold();
            POINT p;
            if (!GetCursorPos(out p))
                return;
            _dragging = true;
            _dragMoved = false;
            _grabX = p.X - _winX;
            _grabY = p.Y - _winY;
            _dragStartX = p.X;
            _dragStartY = p.Y;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_config != null && _config.Is2d)
                TrackPet();
            if (_dragging && !(_petFlips >= 2 && !_dragMoved))
                DragAtCursor();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_dragging && e.Button == MouseButtons.Left)
                EndDrag();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_MOUSEWHEEL)
            {
                int delta = (short)((m.WParam.ToInt32() >> 16) & 0xffff);
                RunEvent(delta > 0 ? "wheelUp" : "wheelDown");
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == WM_DISPLAYCHANGE)
                RefreshScreens();
            if (m.Msg == WM_NCHITTEST)
            {
                POINT p;
                if (!GetCursorPos(out p))
                {
                    m.Result = (IntPtr)HTTRANSPARENT;
                    return;
                }
                int lx = p.X - _winX;
                int ly = p.Y - _winY;
                if (_frame.Alpha == null || (uint)lx >= (uint)_frame.W || (uint)ly >= (uint)_frame.H || _frame.Alpha[ly * _frame.W + lx] < HitAlpha)
                {
                    m.Result = (IntPtr)HTTRANSPARENT;
                    return;
                }
                m.Result = (IntPtr)HTCLIENT;
                return;
            }
            base.WndProc(ref m);
        }

        void BuildTray()
        {
            _trayIcon = MakeTrayIcon();
            _startItem = new ToolStripMenuItem("Start", null, delegate { StartPet(); });
            _stopItem = new ToolStripMenuItem("Stop", null, delegate { StopPet(); });
            ToolStripMenuItem exitItem = new ToolStripMenuItem("Exit", null, delegate { Close(); });
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add(_startItem);
            menu.Items.Add(_stopItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);
            menu.Opening += delegate { SyncMenu(); };

            _tray = new NotifyIcon();
            _tray.Icon = _trayIcon;
            _tray.Visible = true;
            _tray.ContextMenuStrip = menu;
            _tray.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    if (_running)
                        StopPet();
                    else
                        StartPet();
                }
            };
            SyncMenu();
        }

        void SyncMenu()
        {
            _startItem.Enabled = !_running;
            _stopItem.Enabled = _running;
            _tray.Text = _running ? "Mascot (running)" : "Mascot (stopped)";
        }

        void StartPet()
        {
            if (_running)
                return;
            _running = true;
            if (!Motion.Fits(_x, _y, Motion.Width, Motion.Height, _areas))
                Respawn();
            Show();
            Place(_x, _y);
            _timer.Start();
            SyncMenu();
        }

        void StopPet()
        {
            _running = false;
            _timer.Stop();
            Hide();
            SyncMenu();
        }

        void RefreshScreens()
        {
            Screen[] screens = Screen.AllScreens;
            var list = new System.Collections.Generic.List<Rectangle>(screens.Length);
            for (int i = 0; i < screens.Length; i++)
                list.Add(screens[i].WorkingArea);
            if (list.Count == 0)
                list.Add(new Rectangle(0, 0, 800, 600));
            _areas = list.ToArray();
            if (_presented && !Motion.Fits(_x, _y, Motion.Width, Motion.Height, _areas))
                Respawn();
        }

        void Respawn()
        {
            Rectangle home = _areas[0];
            for (int i = 0; i < Screen.AllScreens.Length; i++)
            {
                if (Screen.AllScreens[i].Primary)
                {
                    home = Screen.AllScreens[i].WorkingArea;
                    break;
                }
            }
            _x = home.Left + (home.Width - Motion.Width) * 0.5f;
            _y = home.Top + (home.Height - Motion.Height) * 0.5f;
        }

        void PickVelocity()
        {
            double ang = _rng.NextDouble() * Math.PI * 2;
            const float speed = 70f;
            _vx = (float)(Math.Cos(ang) * speed);
            _vy = (float)(Math.Sin(ang) * speed);
        }

        double Now
        {
            get { return _clock.Elapsed.TotalSeconds; }
        }

        void OnTick(object sender, EventArgs e)
        {
            if (!_running)
                return;
            double t = Now;
            float dt = 0.033f;

            if (_dragging)
            {
                if ((GetAsyncKeyState(1) & 0x8000) == 0)
                    EndDrag();
                else
                    DragAtCursor();
            }

            if (!_dragging && t >= _settleUntil)
            {
                if (t >= _nextTurn)
                {
                    _headingTarget = (float)(_rng.NextDouble() * Math.PI * 2);
                    _nextTurn = t + 2.4 + _rng.NextDouble() * 3.2;
                }
                BiasFromEdges();
                _heading = ApproachRad(_heading, _headingTarget, 1.35f * dt);
                AdvanceMode(t);
                float speed = 40f + 16f * (float)Math.Sin(t * 0.37);
                if (_mode == Mode.Float)
                    speed *= 0.4f;
                float dx = (float)Math.Cos(_heading) * speed * dt;
                float dy = (float)Math.Sin(_heading) * speed * dt;
                if (!Motion.Slide(ref _x, ref _y, dx, dy, Motion.Width, Motion.Height, _areas))
                    _headingTarget = _heading + (float)Math.PI;
                _phase += speed * dt * 0.11f;
                _vx = (float)Math.Cos(_heading) * speed;
                _vy = (float)Math.Sin(_heading) * speed;
            }
            if (!_dragging && _config.Turn >= 359.5f)
            {
                _spin += 42f * dt;
                if (_spin >= 360f)
                    _spin -= 360f;
            }

            if (_config.Is2d)
            {
                if (_rig.Has && t >= _nextBlinkAt)
                {
                    _blinkAt = t;
                    _nextBlinkAt = t + 3.0 + _rng.NextDouble() * 2.0;
                }
                StepHearts(dt);
            }

            RenderPose();
            if (_timers != null)
                _timers.Follow();
            if (_say != null && _say.Visible)
            {
                if (t > _sayUntil)
                    HideSay();
                else
                    ParkSay();
            }
        }

        void BiasFromEdges()
        {
            if (_areas == null || _areas.Length == 0)
                return;
            float cx = _x + Motion.Width * 0.5f;
            float cy = _y + Motion.Height * 0.5f;
            Rectangle home = _areas[0];
            float best = float.MaxValue;
            for (int i = 0; i < _areas.Length; i++)
            {
                Rectangle a = _areas[i];
                if (cx >= a.Left && cx < a.Right && cy >= a.Top && cy < a.Bottom)
                {
                    home = a;
                    best = -1f;
                    break;
                }
                float px = cx < a.Left ? a.Left : (cx > a.Right ? a.Right : cx);
                float py = cy < a.Top ? a.Top : (cy > a.Bottom ? a.Bottom : cy);
                float d = (px - cx) * (px - cx) + (py - cy) * (py - cy);
                if (d < best)
                {
                    best = d;
                    home = a;
                }
            }
            const float margin = 130f;
            float rx = 0f;
            float ry = 0f;
            if (cx < home.Left + margin) rx += home.Left + margin - cx;
            if (cx > home.Right - margin) rx -= cx - (home.Right - margin);
            if (cy < home.Top + margin) ry += home.Top + margin - cy;
            if (cy > home.Bottom - margin) ry -= cy - (home.Bottom - margin);
            if (rx != 0f || ry != 0f)
                _headingTarget = (float)Math.Atan2(ry, rx);
        }

        static float ApproachRad(float cur, float target, float maxStep)
        {
            float d = target - cur;
            const float pi = (float)Math.PI;
            while (d > pi) d -= pi * 2f;
            while (d < -pi) d += pi * 2f;
            if (d > maxStep) d = maxStep;
            if (d < -maxStep) d = -maxStep;
            return cur + d;
        }

        void AdvanceMode(double t)
        {
            if (_laughing)
            {
                if (t >= _laughUntil)
                    _laughing = false;
                _mode = Mode.Hold;
                return;
            }
            if (_mode == Mode.Jump || _mode == Mode.Float)
            {
                if (t < _modeUntil)
                    return;
                _mode = Mode.Walk;
            }
            if (t >= _nextSpecial)
            {
                if (_rng.Next(2) == 0)
                {
                    _mode = Mode.Jump;
                    _modeUntil = t + 0.62;
                }
                else
                {
                    _mode = Mode.Float;
                    _modeUntil = t + 2.4;
                }
                _nextSpecial = _modeUntil + 6 + _rng.NextDouble() * 6;
            }
        }

        float YawForHeading()
        {
            if (_config.Turn >= 359.5f)
                return _spin + _config.YawOffset;
            float facing = _heading * (180f / (float)Math.PI) + 180f;
            float half = _config.Turn * 0.5f;
            float d = facing - _config.YawOffset;
            while (d > 180f) d -= 360f;
            while (d < -180f) d += 360f;
            if (d > half) d = half;
            if (d < -half) d = -half;
            return _config.YawOffset + d;
        }

        void RenderPose()
        {
            double t = Now;
            float pitch = 8f;
            float roll = 0f;
            float lift = 0f;
            if (_dragging)
            {
                pitch = -14f;
                lift = 0.25f;
            }
            else if (_laughing)
            {
                pitch = -6f;
                roll = (float)Math.Sin(t * 28.0) * 8f;
                lift = 0.35f;
            }
            else if (_mode == Mode.Jump)
            {
                float u = 1f - (float)((_modeUntil - t) / 0.62);
                if (u < 0f) u = 0f;
                if (u > 1f) u = 1f;
                lift = (float)Math.Sin(u * Math.PI);
                pitch = 18f - lift * 28f;
            }
            else if (_mode == Mode.Float)
            {
                float u = 1f - (float)((_modeUntil - t) / 2.4);
                if (u < 0f) u = 0f;
                if (u > 1f) u = 1f;
                float env = (float)Math.Sin(Math.Min(u, 1f) * Math.PI);
                lift = 0.72f * env;
                pitch = -4f;
                roll = (float)Math.Sin(t * 1.6) * 4f;
            }
            else if (_mode == Mode.Hold || t < _settleUntil)
            {
                pitch = 0f;
                lift = 0.04f * (float)Math.Sin(t * 2.2);
            }
            else
            {
                float bob = Math.Abs((float)Math.Sin(_phase));
                lift = bob * 0.16f;
                roll = (float)Math.Sin(_phase) * 7f;
                pitch = 10f;
            }
            _winX = (int)Math.Round(_x);
            _winY = (int)Math.Round(_y);
            if (_config.Is2d)
            {
                if (_laughing)
                    _winY -= (int)(Math.Sin((Now - (_laughUntil - 0.85)) * 14.0) * 8);
                ComposeFlat();
                return;
            }
            _yaw = YawForHeading();
            _avatar.Render(_frame.Bits, _yaw, pitch, roll, lift);
            Present(_frame);
        }

        void BlitFlat(int face)
        {
            if (face != _flatFace)
            {
                _flatFace = face;
                Marshal.Copy(_flatBgra[face], 0, _frame.Bits, _flatBgra[face].Length);
                _frame.Alpha = _flatAlpha[face];
                _presented = false;
            }
            if (!_presented)
                Present(_frame);
            else
                SetWindowPos(Handle, HWND_TOPMOST, _winX, _winY, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
        }

        static readonly string[] Lines = {
            "Hi!", "Hey there", "Hello!", "How's it going?", "Nice to see you",
            "What's up?", "Stay a while", "Boop!", "Missed you", "Hehe",
            "Need anything?", "*waves*"
        };

        void ShowSay(string text)
        {
            if (_say == null)
                _say = new SayBubble();
            _say.Apply(Skin.Load());
            _sayUntil = Now + 2.4;
            _say.Say(text, 0, 0);
            if (_timers != null)
                _timers.Follow();
            ParkSay();
        }

        void ParkSay()
        {
            Rectangle body = ContentBox();
            int y = body.Top - 8;
            if (_timers != null && _timers.Visible)
                y = _timers.Top - 8;
            _say.Place(body.Left + body.Width / 2, y);
        }

        void ArmHold()
        {
            _holdFired = false;
            PetEvent hold = PetEvent.Find("longPress");
            if (hold == null || (string.IsNullOrEmpty(hold.action) && string.IsNullOrEmpty(hold.command)))
                return;
            if (_holdWait == null)
            {
                _holdWait = new System.Windows.Forms.Timer();
                _holdWait.Interval = 550;
                _holdWait.Tick += delegate
                {
                    _holdWait.Stop();
                    if (!_dragging || _dragMoved)
                        return;
                    _holdFired = true;
                };
            }
            _holdWait.Stop();
            _holdWait.Start();
        }

        void NoteClick()
        {
            PetEvent dbl = PetEvent.Find("doubleClick");
            bool wantDouble = dbl != null && (!string.IsNullOrEmpty(dbl.action) || !string.IsNullOrEmpty(dbl.command));
            if (!wantDouble)
            {
                _armClick = false;
                RunEvent("click");
                return;
            }
            if (_armClick)
            {
                _armClick = false;
                if (_clickWait != null)
                    _clickWait.Stop();
                RunEvent("doubleClick");
                return;
            }
            _armClick = true;
            if (_clickWait == null)
            {
                _clickWait = new System.Windows.Forms.Timer();
                _clickWait.Interval = SystemInformation.DoubleClickTime;
                _clickWait.Tick += delegate
                {
                    _clickWait.Stop();
                    if (!_armClick)
                        return;
                    _armClick = false;
                    RunEvent("click");
                };
            }
            _clickWait.Stop();
            _clickWait.Start();
        }

        void RunEvent(string name)
        {
            PetEvent ev = PetEvent.Find(name);
            if (ev == null)
                return;
            string action = ev.action == null ? "" : ev.action;
            if (action == "menu")
                ShowCommandMenu();
            else if (action == "avatars")
                ShowAvatarMenu();
            else if (action == "say")
            {
                string[] lines = ev.lines;
                if (lines == null || lines.Length == 0)
                    lines = Lines;
                ShowSay(lines[_rng.Next(lines.Length)]);
            }
            else if (action == "laugh")
                Laugh();
            else if (action == "happy")
                Happy();
            else if (action == "hide")
                StopPet();
            else if (action == "timers")
                OpenTimers();
            else if (action == "dance" || action == "avatar")
                RunUserCommand(action, ev.command, null, null, null);
            else if (!string.IsNullOrEmpty(action))
                RunUserCommand("", action);
            if (!string.IsNullOrEmpty(ev.command) && action != "avatar")
                RunUserCommand("", ev.command);
        }

        void HideSay()
        {
            if (_say != null && _say.Visible)
                _say.Hide();
        }

        void TrackPet()
        {
            POINT p;
            if (!GetCursorPos(out p))
                return;
            float d = p.X - _petDir;
            if (Math.Abs(d) < 5f)
                return;
            if (d * _petDirSign < 0f && Math.Abs(d) > 8f)
            {
                _petFlips++;
                _petMark = Now;
            }
            _petDirSign = d;
            _petDir = p.X;
            if (Now - _petMark > 0.75)
                _petFlips = 0;
            if (_petFlips >= 3)
            {
                _petFlips = 0;
                RunEvent("pet");
            }
        }

        void Happy()
        {
            double t = Now;
            if (t > _happyUntil)
            {
                try
                {
                    _wav.Position = 0;
                    _player.Play();
                }
                catch
                {
                }
            }
            _happyUntil = t + 2.6;
            if (_danceUntil < t + 1.4)
                _danceUntil = t + 1.4;
            int n = 0;
            for (int i = 0; i < _hearts.Length && n < 5; i++)
            {
                if (_hearts[i].Life > 0f)
                    continue;
                _hearts[i].Life = 1.1f;
                _hearts[i].X = Motion.Width * (0.3f + n * 0.1f);
                _hearts[i].Y = Motion.Height * 0.42f;
                n++;
            }
        }

        void StepHearts(float dt)
        {
            if (_hearts == null)
                return;
            for (int i = 0; i < _hearts.Length; i++)
            {
                if (_hearts[i].Life <= 0f)
                    continue;
                _hearts[i].Life -= dt;
                _hearts[i].Y -= 28f * dt;
            }
        }

        float BlinkK()
        {
            double u = (Now - _blinkAt) / 0.16;
            if (u < 0 || u > 1)
                return 0f;
            return (float)Math.Sin(u * Math.PI);
        }

        void ComposeFlat()
        {
            if (_base == null || _compose == null)
                return;
            int w = Motion.Width;
            int h = Motion.Height;
            double t = Now;
            float danceY = 0f;
            float danceA = 0f;
            if (t < _danceUntil)
            {
                danceY = (float)Math.Sin(t * 9.0) * 6f;
                danceA = (float)Math.Sin(t * 6.0) * 7f;
            }
            using (Graphics g = Graphics.FromImage(_compose))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                g.TranslateTransform(w * 0.5f, h * 0.5f + danceY);
                g.RotateTransform(danceA);
                g.TranslateTransform(-w * 0.5f, -h * 0.5f);
                g.DrawImage(_base, 0, 0, w, h);
                float k = BlinkK();
                if (k > 0.04f && _rig.Has)
                    DrawLids(g, w, h, k);
                if (t < _happyUntil && _rig.Has)
                    DrawBlush(g, w, h, (float)((_happyUntil - t) / 2.6));
                g.ResetTransform();
                DrawHearts(g);
            }
            PackCompose();
            Present(_frame);
        }

        void DrawLids(Graphics g, int w, int h, float k)
        {
            using (SolidBrush lid = new SolidBrush(_rig.Lid))
            using (Pen line = new Pen(_rig.Line, 2.2f))
            {
                DrawOneLid(g, lid, line, w, h, _rig.Lx, _rig.Ly, _rig.Lw, _rig.Lh, k);
                DrawOneLid(g, lid, line, w, h, _rig.Rx, _rig.Ry, _rig.Rw, _rig.Rh, k);
            }
        }

        static void DrawOneLid(Graphics g, SolidBrush lid, Pen line, int w, int h, float cx, float cy, float ew, float eh, float k)
        {
            float rw = ew * w;
            float rh = eh * h;
            float x = cx * w - rw * 0.5f;
            float y = cy * h - rh * 0.5f;
            g.FillEllipse(lid, x, y, rw, rh * k);
            if (k > 0.35f)
                g.DrawArc(line, x, y + rh * (k * 0.15f), rw, rh * 0.55f, 200, 140);
        }

        void DrawBlush(Graphics g, int w, int h, float fade)
        {
            int a = (int)(110 * fade);
            if (a < 1)
                return;
            using (SolidBrush b = new SolidBrush(Color.FromArgb(a, 255, 120, 140)))
            {
                float s = w * 0.06f;
                g.FillEllipse(b, _rig.Lx * w - s, (_rig.Ly + 0.08f) * h, s, s * 0.65f);
                g.FillEllipse(b, _rig.Rx * w, (_rig.Ry + 0.08f) * h, s, s * 0.65f);
            }
        }

        void DrawHearts(Graphics g)
        {
            if (_hearts == null)
                return;
            for (int i = 0; i < _hearts.Length; i++)
            {
                if (_hearts[i].Life <= 0f)
                    continue;
                int a = (int)(255 * Math.Min(1f, _hearts[i].Life));
                using (SolidBrush b = new SolidBrush(Color.FromArgb(a, 255, 80, 110)))
                {
                    float x = _hearts[i].X;
                    float y = _hearts[i].Y;
                    g.FillEllipse(b, x, y, 7, 7);
                    g.FillEllipse(b, x + 5, y, 7, 7);
                    g.FillPolygon(b, new PointF[] {
                        new PointF(x, y + 4),
                        new PointF(x + 12, y + 4),
                        new PointF(x + 6, y + 12)
                    });
                }
            }
        }

        void PackCompose()
        {
            int w = _compose.Width;
            int h = _compose.Height;
            BitmapData data = _compose.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            int stride = data.Stride;
            byte[] raw = new byte[Math.Abs(stride) * h];
            Marshal.Copy(data.Scan0, raw, 0, raw.Length);
            _compose.UnlockBits(data);
            if (_frame.Alpha == null || _frame.Alpha.Length != w * h)
                _frame.Alpha = new byte[w * h];
            byte[] alpha = _frame.Alpha;
            byte[] bgra = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            {
                int src = y * stride;
                for (int x = 0; x < w; x++)
                {
                    int i = src + x * 4;
                    byte b = raw[i];
                    byte gg = raw[i + 1];
                    byte r = raw[i + 2];
                    byte a = raw[i + 3];
                    int p = y * w + x;
                    alpha[p] = a;
                    int o = p * 4;
                    bgra[o] = (byte)(b * a / 255);
                    bgra[o + 1] = (byte)(gg * a / 255);
                    bgra[o + 2] = (byte)(r * a / 255);
                    bgra[o + 3] = a;
                }
            }
            Marshal.Copy(bgra, 0, _frame.Bits, bgra.Length);
            _presented = true;
        }

        Rectangle ContentBox()
        {
            int left, top, right, bottom;
            if (!Pixels.Bounds(_frame.Alpha, _frame.W, _frame.H, out left, out top, out right, out bottom))
                return new Rectangle(_winX, _winY, Motion.Width, Motion.Height);
            return new Rectangle(_winX + left, _winY + top, right - left, bottom - top);
        }

        void ShowCommandMenu()
        {
            ShowMenu("commands.json", MenuCommand.CommandsText(), "Add commands.json");
        }

        void EnsureTimers()
        {
            if (_timers != null)
                return;
            _timers = new TimerCard(delegate
            {
                return ContentBox();
            });
            _timers.LoadSaved();
        }

        void OpenTimers()
        {
            EnsureTimers();
            _timers.ShowSetup();
        }

        void ShowAvatarMenu()
        {
            ShowMenu("avatars.json", MenuCommand.AvatarsText(), "Add avatars.json");
        }

        void ShowMenu(string file, string fallback, string emptyLabel)
        {
            if (_cmdMenu != null)
            {
                _cmdMenu.Dispose();
                _cmdMenu = null;
            }
            MenuCommand[] cmds = MenuCommand.Load(file, fallback);
            Skin skin = Skin.Load();
            MenuLook look = new MenuLook();
            look.Bg = skin.MenuBg;
            look.Hi = skin.MenuHover;
            look.Edge = skin.MenuBorder;
            MenuPaint paint = new MenuPaint(look);
            paint.Text = skin.MenuFg;
            paint.Radius = skin.MenuRadius;
            paint.EdgeW = skin.MenuBorderW;
            RoundMenu menu = new RoundMenu();
            menu.Radius = skin.MenuRadius;
            menu.Renderer = paint;
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = false;
            menu.Font = new Font("Segoe UI", skin.MenuFont);
            menu.ForeColor = skin.MenuFg;
            menu.BackColor = skin.MenuBg;
            int edge = Math.Max(6, skin.MenuRadius);
            menu.Padding = new Padding(edge, edge, edge, edge);
            if (cmds == null || cmds.Length == 0)
            {
                menu.Items.Add(new ToolStripMenuItem(emptyLabel));
            }
            else
            {
                for (int i = 0; i < cmds.Length; i++)
                {
                    MenuCommand cmd = cmds[i];
                    if (cmd == null || string.IsNullOrEmpty(cmd.label))
                        continue;
                    ToolStripMenuItem item = new ToolStripMenuItem(cmd.label);
                    item.ForeColor = skin.MenuFg;
                    item.Padding = new Padding(skin.PadL, skin.PadT, skin.PadR, skin.PadB);
                    string action = cmd.action;
                    string command = cmd.command;
                    float? yaw = cmd.yawOffset;
                    float? turn = cmd.turn;
                    float? scale = cmd.scale;
                    item.Click += delegate { RunUserCommand(action, command, yaw, turn, scale); };
                    menu.Items.Add(item);
                }
            }
            _cmdMenu = menu;
            menu.Show(Cursor.Position);
        }

        void RunUserCommand(string action, string command)
        {
            RunUserCommand(action, command, null, null, null);
        }

        void RunUserCommand(string action, string command, float? yaw, float? turn, float? scale)
        {
            if (action == "timers")
            {
                OpenTimers();
                return;
            }
            if (action == "dance")
            {
                _danceUntil = Now + 4.2;
                return;
            }
            if (action == "avatar")
            {
                SwitchAvatar(command, yaw, turn, scale);
                return;
            }
            if (string.IsNullOrEmpty(command))
                return;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "cmd.exe";
                psi.Arguments = "/c start \"\" " + command;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = Path.GetDirectoryName(Application.ExecutablePath);
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Mascot");
            }
        }

        void SwitchAvatar(string file, float? yaw, float? turn, float? scale)
        {
            if (string.IsNullOrEmpty(file))
                return;
            string path = file;
            if (!Path.IsPathRooted(path))
                path = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), path);
            bool mesh = path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase);
            if (mesh)
            {
                if (!SwitchMesh(path, scale))
                    return;
            }
            else if (!SwitchFlat(path, scale))
                return;
            if (yaw.HasValue)
                _config.YawOffset = yaw.Value;
            if (turn.HasValue)
            {
                float t = turn.Value;
                _config.Turn = t < 1f || t > 360f ? 360f : t;
            }
            _config.ModelPath = path;
        }

        bool SwitchMesh(string path, float? scale)
        {
            float modelScale = ClampScale(scale.HasValue ? scale.Value : _config.Scale);
            int w;
            int h;
            MeshWindow(modelScale, out w, out h);
            GlbMesh mesh;
            GlAvatar next;
            try
            {
                mesh = GlbMesh.Load(path);
                next = new GlAvatar(mesh, 1f, w, h);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Mascot");
                return false;
            }
            if (_avatar != null)
                _avatar.Dispose();
            _avatar = next;
            _config.Is2d = false;
            _config.Scale = modelScale;
            if (_base != null)
            {
                _base.Dispose();
                _base = null;
            }
            if (_compose != null)
            {
                _compose.Dispose();
                _compose = null;
            }
            _flatFace = -1;
            UseFrame(w, h);
            _frame.Alpha = _avatar.Alpha;
            _presented = false;
            if (_running)
                RenderPose();
            return true;
        }

        bool SwitchFlat(string path, float? scale)
        {
            float modelScale = ClampScale(scale.HasValue ? scale.Value : _config.Scale);
            int w;
            int h;
            Bitmap next = LoadFlatImage(path, modelScale, out w, out h);
            if (next == null)
                return false;
            if (_avatar != null)
            {
                _avatar.Dispose();
                _avatar = null;
            }
            if (_base != null)
                _base.Dispose();
            _base = next;
            _config.Is2d = true;
            _config.Scale = modelScale;
            _rig = FaceRig.For(path);
            UseFrame(w, h);
            if (_flatBgra == null)
            {
                _flatBgra = new byte[2][];
                _flatAlpha = new byte[2][];
            }
            if (_hearts == null)
                _hearts = new Heart[8];
            if (_compose != null)
                _compose.Dispose();
            _compose = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            _frame.Alpha = new byte[w * h];
            _flatFace = -1;
            _presented = false;
            ComposeFlat();
            return true;
        }

        static float ClampScale(float scale)
        {
            if (scale <= 0f || scale > 3f)
                return 1f;
            return scale;
        }

        static void MeshWindow(float scale, out int w, out int h)
        {
            w = Math.Max(16, (int)Math.Round(300 * scale));
            h = Math.Max(16, (int)Math.Round(440 * scale));
        }

        void UseFrame(int w, int h)
        {
            if (_frame.Dib != IntPtr.Zero && _frame.W == w && _frame.H == h)
                return;
            if (_frame.Dib != IntPtr.Zero)
                DeleteObject(_frame.Dib);
            Motion.Width = w;
            Motion.Height = h;
            _frame = CreateFrame(w, h);
            ClientSize = new Size(w, h);
        }

        static Bitmap DrawOrange(int w, int h)
        {
            Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (SolidBrush body = new SolidBrush(Color.FromArgb(255, 255, 140, 64)))
                    g.FillEllipse(body, w * 0.18f, h * 0.12f, w * 0.64f, h * 0.7f);
                using (SolidBrush belly = new SolidBrush(Color.FromArgb(255, 255, 214, 176)))
                    g.FillEllipse(belly, w * 0.3f, h * 0.4f, w * 0.4f, h * 0.28f);
                using (SolidBrush eye = new SolidBrush(Color.FromArgb(255, 55, 36, 30)))
                {
                    g.FillEllipse(eye, w * 0.34f, h * 0.3f, w * 0.08f, h * 0.08f);
                    g.FillEllipse(eye, w * 0.56f, h * 0.3f, w * 0.08f, h * 0.08f);
                }
            }
            return bmp;
        }

        void BakeFlat(int slot, bool laugh)
        {
            int w = Motion.Width;
            int h = Motion.Height;
            using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (SolidBrush body = new SolidBrush(Color.FromArgb(255, 255, 140, 64)))
                    g.FillEllipse(body, 28, 22, 104, 120);
                using (SolidBrush belly = new SolidBrush(Color.FromArgb(255, 255, 214, 176)))
                    g.FillEllipse(belly, 48, 70, 64, 52);
                if (laugh)
                {
                    using (Pen eye = new Pen(Color.FromArgb(255, 70, 42, 32), 3f))
                    {
                        g.DrawArc(eye, 52, 52, 20, 16, 200, 140);
                        g.DrawArc(eye, 86, 52, 20, 16, 200, 140);
                    }
                    using (SolidBrush mouth = new SolidBrush(Color.FromArgb(255, 150, 40, 48)))
                        g.FillEllipse(mouth, 68, 86, 24, 18);
                }
                else
                {
                    using (SolidBrush eye = new SolidBrush(Color.FromArgb(255, 55, 36, 30)))
                    {
                        g.FillEllipse(eye, 56, 56, 12, 14);
                        g.FillEllipse(eye, 90, 56, 12, 14);
                    }
                    using (Pen mouth = new Pen(Color.FromArgb(255, 70, 42, 32), 3f))
                        g.DrawArc(mouth, 66, 84, 28, 16, 15, 150);
                }
                PackFlat(bmp, slot);
            }
        }

        static Bitmap LoadFlatImage(string path, float scale, out int w, out int h)
        {
            w = Math.Max(48, (int)(160 * scale));
            h = Math.Max(48, (int)(180 * scale));
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".png" && ext != ".jpg" && ext != ".jpeg" && ext != ".bmp" && ext != ".gif")
                return null;
            using (Bitmap src = new Bitmap(path))
            using (Bitmap argb = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(argb))
                    g.DrawImage(src, 0, 0, src.Width, src.Height);
                int left, top, right, bottom;
                if (!OpaqueBounds(argb, out left, out top, out right, out bottom))
                    return null;
                int cw = right - left;
                int ch = bottom - top;
                int maxH = Math.Max(16, (int)Math.Round(440 * scale));
                int maxW = Math.Max(12, (int)Math.Round(340 * scale));
                int dh = maxH;
                int dw = (int)Math.Round(dh * (cw / (float)ch));
                if (dw > maxW)
                {
                    dw = maxW;
                    dh = (int)Math.Round(dw * (ch / (float)cw));
                }
                if (dw < 1) dw = 1;
                if (dh < 1) dh = 1;
                Bitmap dst = new Bitmap(dw, dh, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(dst))
                {
                    g.Clear(Color.Transparent);
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.DrawImage(argb, new Rectangle(0, 0, dw, dh), new Rectangle(left, top, cw, ch), GraphicsUnit.Pixel);
                }
                w = dw;
                h = dh;
                return dst;
            }
        }

        static bool OpaqueBounds(Bitmap bmp, out int left, out int top, out int right, out int bottom)
        {
            left = bmp.Width;
            top = bmp.Height;
            right = 0;
            bottom = 0;
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            int stride = data.Stride;
            byte[] raw = new byte[Math.Abs(stride) * bmp.Height];
            Marshal.Copy(data.Scan0, raw, 0, raw.Length);
            bmp.UnlockBits(data);
            for (int y = 0; y < bmp.Height; y++)
            {
                int row = y * stride;
                for (int x = 0; x < bmp.Width; x++)
                {
                    if (raw[row + x * 4 + 3] < 16)
                        continue;
                    if (x < left) left = x;
                    if (y < top) top = y;
                    if (x + 1 > right) right = x + 1;
                    if (y + 1 > bottom) bottom = y + 1;
                }
            }
            return right > left && bottom > top;
        }

        void PackFlat(Bitmap bmp, int slot)
        {
            int w = bmp.Width;
            int h = bmp.Height;
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            int stride = data.Stride;
            byte[] raw = new byte[Math.Abs(stride) * h];
            Marshal.Copy(data.Scan0, raw, 0, raw.Length);
            bmp.UnlockBits(data);
            byte[] bgra = new byte[w * h * 4];
            byte[] alpha = new byte[w * h];
            for (int y = 0; y < h; y++)
            {
                int src = y * stride;
                for (int x = 0; x < w; x++)
                {
                    int i = src + x * 4;
                    byte b = raw[i];
                    byte gg = raw[i + 1];
                    byte r = raw[i + 2];
                    byte a = raw[i + 3];
                    int p = y * w + x;
                    alpha[p] = a;
                    int o = p * 4;
                    bgra[o] = (byte)(b * a / 255);
                    bgra[o + 1] = (byte)(gg * a / 255);
                    bgra[o + 2] = (byte)(r * a / 255);
                    bgra[o + 3] = a;
                }
            }
            _flatBgra[slot] = bgra;
            _flatAlpha[slot] = alpha;
        }

        void DragAtCursor()
        {
            POINT p;
            if (!GetCursorPos(out p))
                return;
            if (Math.Abs(p.X - _dragStartX) + Math.Abs(p.Y - _dragStartY) > 8)
            {
                _dragMoved = true;
                if (_holdWait != null)
                    _holdWait.Stop();
            }
            _x = p.X - _grabX;
            _y = p.Y - _grabY;
            Place(_x, _y);
        }

        void EndDrag()
        {
            if (!_dragging)
                return;
            _dragging = false;
            if (_holdWait != null)
                _holdWait.Stop();
            RunEvent("mouseUp");
            if (_holdFired)
            {
                _holdFired = false;
                RunEvent("longPress");
                return;
            }
            if (!_dragMoved)
            {
                NoteClick();
                return;
            }
            SnapToNearest();
            _settleUntil = Now + 1.1;
            _mode = Mode.Hold;
        }

        void SnapToNearest()
        {
            if (Motion.Fits(_x, _y, Motion.Width, Motion.Height, _areas))
                return;
            float cx = _x + Motion.Width * 0.5f;
            float cy = _y + Motion.Height * 0.5f;
            Rectangle best = _areas[0];
            float bestD = float.MaxValue;
            for (int i = 0; i < _areas.Length; i++)
            {
                Rectangle a = _areas[i];
                float px = cx < a.Left ? a.Left : (cx > a.Right ? a.Right : cx);
                float py = cy < a.Top ? a.Top : (cy > a.Bottom ? a.Bottom : cy);
                float d = (px - cx) * (px - cx) + (py - cy) * (py - cy);
                if (d < bestD)
                {
                    bestD = d;
                    best = a;
                }
            }
            if (_x < best.Left) _x = best.Left;
            if (_y < best.Top) _y = best.Top;
            if (_x > best.Right - Motion.Width) _x = best.Right - Motion.Width;
            if (_y > best.Bottom - Motion.Height) _y = best.Bottom - Motion.Height;
        }

        static float ApproachAngle(float cur, float target, float maxDeg)
        {
            float d = target - cur;
            while (d > 180f) d -= 360f;
            while (d < -180f) d += 360f;
            if (d > maxDeg) d = maxDeg;
            if (d < -maxDeg) d = -maxDeg;
            return cur + d;
        }

        void Laugh()
        {
            _laughing = true;
            _laughUntil = Now + 0.85;
            try
            {
                _wav.Position = 0;
                _player.Play();
            }
            catch
            {
                // visual laugh still runs if audio device is busy
            }
        }

        void Place(float x, float y)
        {
            int ix = (int)Math.Round(x);
            int iy = (int)Math.Round(y);
            if (ix == _winX && iy == _winY)
                return;
            _winX = ix;
            _winY = iy;
            if (!_presented)
                Present(_frame);
            else
                SetWindowPos(Handle, HWND_TOPMOST, ix, iy, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
        }

        void Present(Frame frame)
        {
            if (frame.Dib == IntPtr.Zero)
                return;
            IntPtr screen = GetDC(IntPtr.Zero);
            IntPtr mem = CreateCompatibleDC(screen);
            IntPtr old = SelectObject(mem, frame.Dib);
            POINT dst = new POINT();
            dst.X = _winX;
            dst.Y = _winY;
            SIZE size = new SIZE();
            size.cx = frame.W;
            size.cy = frame.H;
            POINT src = new POINT();
            BLENDFUNCTION blend = new BLENDFUNCTION();
            blend.BlendOp = AC_SRC_OVER;
            blend.BlendFlags = 0;
            blend.SourceConstantAlpha = 255;
            blend.AlphaFormat = AC_SRC_ALPHA;
            bool ok = UpdateLayeredWindow(Handle, screen, ref dst, ref size, mem, ref src, 0, ref blend, ULW_ALPHA);
            SelectObject(mem, old);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException("UpdateLayeredWindow failed: " + err);
            }
            _presented = true;
        }

        static Frame CreateFrame(int w, int h)
        {
            BITMAPINFO info = new BITMAPINFO();
            info.bmiHeader.biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER));
            info.bmiHeader.biWidth = w;
            info.bmiHeader.biHeight = -h;
            info.bmiHeader.biPlanes = 1;
            info.bmiHeader.biBitCount = 32;
            info.bmiHeader.biCompression = 0;
            IntPtr bits;
            IntPtr screen = GetDC(IntPtr.Zero);
            IntPtr dib = CreateDIBSection(screen, ref info, 0, out bits, IntPtr.Zero, 0);
            ReleaseDC(IntPtr.Zero, screen);
            if (dib == IntPtr.Zero)
                throw new InvalidOperationException("CreateDIBSection failed");
            Frame frame = new Frame();
            frame.W = w;
            frame.H = h;
            frame.Dib = dib;
            frame.Bits = bits;
            return frame;
        }

        static Icon MakeTrayIcon()
        {
            using (Bitmap bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (SolidBrush body = new SolidBrush(Color.FromArgb(255, 255, 140, 64)))
                        g.FillEllipse(body, 2, 2, 28, 28);
                    using (SolidBrush eye = new SolidBrush(Color.FromArgb(255, 55, 36, 30)))
                    {
                        g.FillEllipse(eye, 9, 12, 4, 5);
                        g.FillEllipse(eye, 18, 12, 4, 5);
                    }
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        static MemoryStream MakeLaughWav()
        {
            int rate = 22050;
            int n = (int)(rate * 0.62);
            short[] samples = new short[n];
            double[] start = { 0.02, 0.18, 0.36 };
            double[] freq = { 430, 380, 470 };
            for (int b = 0; b < 3; b++)
            {
                int i0 = (int)(start[b] * rate);
                int len = (int)(0.13 * rate);
                for (int i = 0; i < len; i++)
                {
                    int idx = i0 + i;
                    if (idx >= n)
                        break;
                    double t = (double)i / rate;
                    double env = Math.Sin(Math.PI * i / len);
                    double tone = Math.Sin(2 * Math.PI * freq[b] * t)
                        + 0.3 * Math.Sin(2 * Math.PI * freq[b] * 2 * t);
                    int noise = ((idx * 1103515245 + 12345) & 255) - 128;
                    double s = (tone * 0.8 + noise / 128.0 * 0.15) * env * 0.4;
                    if (s > 1) s = 1;
                    if (s < -1) s = -1;
                    samples[idx] = (short)(s * 32767);
                }
            }

            MemoryStream ms = new MemoryStream();
            BinaryWriter bw = new BinaryWriter(ms);
            int dataBytes = samples.Length * 2;
            bw.Write(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
            bw.Write(36 + dataBytes);
            bw.Write(new byte[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
            bw.Write(new byte[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
            bw.Write(16);
            bw.Write((short)1);
            bw.Write((short)1);
            bw.Write(rate);
            bw.Write(rate * 2);
            bw.Write((short)2);
            bw.Write((short)16);
            bw.Write(new byte[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
            bw.Write(dataBytes);
            for (int i = 0; i < samples.Length; i++)
                bw.Write(samples[i]);
            bw.Flush();
            ms.Position = 0;
            return ms;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SIZE
        {
            public int cx;
            public int cy;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll", SetLastError = true)]
        static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr hIcon);
    }
}
