// BiliTray - 可以缩到托盘的 B 站听歌小窗口（便携版）
// 最小化 / 点叉 / Alt+F4 / Ctrl+W = 藏到托盘继续播放；单击托盘 = 显示/隐藏；右键托盘 = 菜单
// 浏览器数据保存在 exe 同目录的 Data\ChromeProfile
// 编译：见 build.ps1（用 Windows 自带的 .NET Framework csc.exe，C# 5 语法）
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Management;
using System.Net;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("BiliTray")]
[assembly: System.Reflection.AssemblyProduct("BiliTray")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

static class Native {
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr h, int c);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr v);

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int l, t, r, b; }
    [StructLayout(LayoutKind.Sequential)] public struct MSLL { public POINT pt; public uint data, flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] public struct KBLL { public uint vk, scan, flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] public struct MSG { public IntPtr h; public uint m; public IntPtr w, l; public uint t; public POINT p; }
    public delegate IntPtr LLProc(int code, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int id, LLProc fn, IntPtr mod, uint tid);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("kernel32.dll")] public static extern IntPtr GetModuleHandle(string n);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint f);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] public static extern int GetMessage(out MSG m, IntPtr h, uint a, uint b);
    [DllImport("user32.dll")] public static extern bool PostThreadMessage(uint tid, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
    [DllImport("psapi.dll")] public static extern bool EmptyWorkingSet(IntPtr h);
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);

    // 把进程暂时用不到的内存交还给系统（需要时会自动换回来）
    public static void TrimProcess(uint pid) {
        IntPtr h = OpenProcess(0x0400 | 0x0100, false, pid);   // QUERY_INFORMATION | SET_QUOTA
        if (h == IntPtr.Zero) return;
        EmptyWorkingSet(h);
        CloseHandle(h);
    }

    public static void Key(byte vk) {
        keybd_event(vk, 0, 0, UIntPtr.Zero); keybd_event(vk, 0, 2, UIntPtr.Zero);
    }
    public static string Title(IntPtr h) {
        var sb = new StringBuilder(512); GetWindowText(h, sb, 512); return sb.ToString();
    }
    public static IntPtr FindChromeWindow(HashSet<uint> pids) {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr h, IntPtr l) {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (!pids.Contains(pid) || !IsWindowVisible(h) || GetWindowTextLength(h) == 0) return true;
            var sb = new StringBuilder(64); GetClassName(h, sb, 64);
            if (sb.ToString() == "Chrome_WidgetWin_1") { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}

// 拦截"关闭"：点窗口右上角的叉、Alt+F4、Ctrl+W 都改成隐藏到托盘
static class CloseHook {
    public static IntPtr Target = IntPtr.Zero;
    public static volatile bool HiddenByClose = false;
    static Native.LLProc mProc, kProc;
    static uint threadId;
    static bool downSwallowed;

    public static void Start() {
        var th = new Thread(delegate() {
            try { Native.SetThreadDpiAwarenessContext(new IntPtr(-4)); } catch { }
            threadId = Native.GetCurrentThreadId();
            mProc = MouseCb; kProc = KeyCb;
            IntPtr mod = Native.GetModuleHandle(null);
            IntPtr mh = Native.SetWindowsHookEx(14, mProc, mod, 0);   // WH_MOUSE_LL
            IntPtr kh = Native.SetWindowsHookEx(13, kProc, mod, 0);   // WH_KEYBOARD_LL
            Native.MSG msg;
            while (Native.GetMessage(out msg, IntPtr.Zero, 0, 0) > 0) { }
            Native.UnhookWindowsHookEx(mh); Native.UnhookWindowsHookEx(kh);
        });
        th.IsBackground = true;
        th.Start();
    }
    public static void Stop() { if (threadId != 0) Native.PostThreadMessage(threadId, 0x12, IntPtr.Zero, IntPtr.Zero); }

    static void Hide() { Native.ShowWindowAsync(Target, 0); HiddenByClose = true; }

    static bool InCloseButton(Native.POINT p) {
        IntPtr t = Target;
        if (t == IntPtr.Zero) return false;
        IntPtr w = Native.WindowFromPoint(p);
        if (w == IntPtr.Zero || Native.GetAncestor(w, 2) != t) return false;
        Native.RECT r; if (!Native.GetWindowRect(t, out r)) return false;
        double s = Native.GetDpiForWindow(t) / 96.0; if (s <= 0) s = 1;
        int inset = Native.IsZoomed(t) ? (int)(8 * s) : 0;          // 最大化时窗口边框伸出屏幕外
        int right = r.r - inset, top = r.t + inset;
        return p.x >= right - (int)(48 * s) && p.x < right && p.y >= top && p.y < top + (int)(34 * s);
    }

    static IntPtr MouseCb(int code, IntPtr w, IntPtr l) {
        if (code >= 0) {
            int m = w.ToInt32();
            if (m == 0x201 || m == 0x202) {
                var d = (Native.MSLL)Marshal.PtrToStructure(l, typeof(Native.MSLL));
                if (m == 0x201 && InCloseButton(d.pt)) { downSwallowed = true; return new IntPtr(1); }
                if (m == 0x202 && downSwallowed) {
                    downSwallowed = false;
                    if (InCloseButton(d.pt)) Hide();
                    return new IntPtr(1);
                }
            }
        }
        return Native.CallNextHookEx(IntPtr.Zero, code, w, l);
    }

    static IntPtr KeyCb(int code, IntPtr w, IntPtr l) {
        if (code >= 0 && Target != IntPtr.Zero) {
            int m = w.ToInt32();
            if ((m == 0x100 || m == 0x104) && Native.GetForegroundWindow() == Target) {
                var k = (Native.KBLL)Marshal.PtrToStructure(l, typeof(Native.KBLL));
                bool alt = (k.flags & 0x20) != 0;
                bool ctrl = Native.GetAsyncKeyState(0x11) < 0;
                if ((k.vk == 0x73 && alt) || (k.vk == 0x57 && ctrl)) { Hide(); return new IntPtr(1); }
            }
        }
        return Native.CallNextHookEx(IntPtr.Zero, code, w, l);
    }
}

// 和 Chrome 调试接口通信
static class Cdp {
    public static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

    public static string Get(string url) {
        try {
            var r = (HttpWebRequest)WebRequest.Create(url);
            r.Proxy = null; r.Timeout = 800; r.ReadWriteTimeout = 800;
            using (var resp = r.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) return sr.ReadToEnd();
        } catch { return null; }
    }

    public static void Call(string wsUrl, string method, Dictionary<string, object> prms) {
        try {
            var msg = new Dictionary<string, object>();
            msg["id"] = 1; msg["method"] = method; msg["params"] = prms;
            var b = Encoding.UTF8.GetBytes(Json.Serialize(msg));
            using (var ws = new ClientWebSocket())
            using (var cts = new CancellationTokenSource(2000)) {
                ws.Options.Proxy = null;
                ws.ConnectAsync(new Uri(wsUrl), cts.Token).GetAwaiter().GetResult();
                ws.SendAsync(new ArraySegment<byte>(b), WebSocketMessageType.Text, true, cts.Token).GetAwaiter().GetResult();
                var buf = new byte[65536];
                ws.ReceiveAsync(new ArraySegment<byte>(buf), cts.Token).GetAwaiter().GetResult();
                ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", cts.Token).GetAwaiter().GetResult();
            }
        } catch { }
    }
}

class TrayApp : ApplicationContext {
    const int CdpPort = 29333;
    const string CdpBase = "http://127.0.0.1:29333";

    // 注入到页面：让"新标签页打开"的链接直接在当前窗口打开
    const string SameWindowJs = @"(function(){
  if (window.__biliTray) return; window.__biliTray = 1;
  document.addEventListener('click', function(e){
    var a = e.target && e.target.closest ? e.target.closest('a[target]') : null;
    if (a && a.target !== '_self' && a.href && !/^javascript:/i.test(a.href)) a.target = '_self';
  }, true);
  var _open = window.open;
  window.open = function(u){
    if (u) { location.href = new URL(u, location.href).href; return window; }
    return _open.apply(window, arguments);
  };
})();";

    // 省电模式脚本（src/power.js，编译时嵌入 exe）：480P、关弹幕、关页面动画
    static readonly string PowerJs = LoadResource("power.js");
    static string LoadResource(string name) {
        using (var s = typeof(TrayApp).Assembly.GetManifestResourceStream(name))
        using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
    }

    readonly string profileDir, settingsFile;
    bool powerSave = true;
    bool powerDirty = true;
    readonly NotifyIcon tray;
    readonly System.Windows.Forms.Timer timer;
    IntPtr hwnd = IntPtr.Zero;
    bool tipShown;
    int ticks, hiddenTicks;
    string mainId, mainWs, lastUrl;
    readonly Dictionary<string, int> blankSeen = new Dictionary<string, int>();

    public TrayApp(string url) {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        profileDir = Path.Combine(baseDir, @"Data\ChromeProfile");
        settingsFile = Path.Combine(baseDir, @"Data\settings.ini");
        try { if (File.Exists(settingsFile) && File.ReadAllText(settingsFile).Contains("power=0")) powerSave = false; } catch { }

        string chrome = FindChrome();
        if (chrome == null) {
            MessageBox.Show("没有找到 Chrome，请先安装 Google Chrome。", "BiliTray");
            Environment.Exit(0);
        }
        Directory.CreateDirectory(profileDir);

        if (GetOurPids().Count == 0) {
            string args = string.Join(" ", new[] {
                "--app=\"" + url + "\"",
                "--user-data-dir=\"" + profileDir + "\"",
                "--remote-debugging-port=" + CdpPort,
                "--remote-debugging-address=127.0.0.1",
                "--window-size=1200,800",
                "--no-first-run",
                "--no-default-browser-check",
                "--autoplay-policy=no-user-gesture-required",
                // 窗口隐藏后也别降速，保证连播/自动下一首正常
                "--disable-background-timer-throttling",
                "--disable-renderer-backgrounding",
                "--disable-backgrounding-occluded-windows",
                // 关掉听歌用不上的后台功能，省内存
                "--disable-extensions",
                "--disable-component-extensions-with-background-pages",
                "--disable-default-apps",
                "--disable-sync",
                "--disable-background-networking",
                "--disable-features=Translate,OptimizationHints,OptimizationGuideModelDownloading,MediaRouter,DialMediaRouteProvider,AutofillServerCommunication,InterestFeedContentSuggestions"
            });
            Process.Start(new ProcessStartInfo(chrome, args) { UseShellExecute = false });
        }

        tray = new NotifyIcon();
        tray.Icon = MakeIcon();
        tray.Text = "BiliTray - 正在启动…";
        tray.Visible = true;

        var menu = new ContextMenuStrip();
        var miShow = menu.Items.Add("显示 / 隐藏窗口");
        miShow.Font = new Font(miShow.Font, FontStyle.Bold);
        miShow.Click += delegate { TogglePlayer(); };
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("播放 / 暂停").Click += delegate { Native.Key(0xB3); };
        menu.Items.Add("下一首").Click += delegate { Native.Key(0xB0); };
        menu.Items.Add("上一首").Click += delegate { Native.Key(0xB1); };
        menu.Items.Add(new ToolStripSeparator());
        var miPower = new ToolStripMenuItem("省电模式（480P / 关弹幕 / 关动画）");
        miPower.CheckOnClick = true;
        miPower.Checked = powerSave;
        miPower.CheckedChanged += delegate {
            powerSave = miPower.Checked; powerDirty = true;
            try { File.WriteAllText(settingsFile, "power=" + (powerSave ? "1" : "0")); } catch { }
        };
        menu.Items.Add(miPower);
        menu.Items.Add("打开数据文件夹").Click += delegate { Process.Start("explorer.exe", "\"" + profileDir + "\""); };
        menu.Items.Add("退出").Click += delegate { QuitAll(); };
        tray.ContextMenuStrip = menu;
        tray.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) TogglePlayer(); };

        CloseHook.Start();

        timer = new System.Windows.Forms.Timer();
        timer.Interval = 300;
        timer.Tick += delegate { OnTick(); };
        timer.Start();
    }

    static string FindChrome() {
        foreach (var root in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine }) {
            using (var k = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe")) {
                if (k != null) {
                    var p = k.GetValue("") as string;
                    if (!string.IsNullOrEmpty(p) && File.Exists(p)) return p;
                }
            }
        }
        foreach (var p in new[] {
            Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\Google\Chrome\Application\chrome.exe"),
            Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe"),
            Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Google\Chrome\Application\chrome.exe") }) {
            if (File.Exists(p)) return p;
        }
        return null;
    }

    HashSet<uint> GetOurPids() {
        var set = new HashSet<uint>();
        try {
            using (var q = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name='chrome.exe'"))
            using (var res = q.Get()) {
                foreach (ManagementObject o in res) {
                    var cl = o["CommandLine"] as string;
                    if (cl != null && cl.IndexOf(profileDir, StringComparison.OrdinalIgnoreCase) >= 0)
                        set.Add((uint)o["ProcessId"]);
                    o.Dispose();
                }
            }
        } catch { }
        return set;
    }

    static Icon MakeIcon() {
        using (var bmp = new Bitmap(32, 32))
        using (var g = Graphics.FromImage(bmp)) {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            using (var br = new SolidBrush(Color.FromArgb(251, 114, 153))) g.FillEllipse(br, 0, 0, 31, 31);
            using (var f = new Font("Segoe UI Symbol", 17, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString("♫", f, Brushes.White, new RectangleF(0, 1, 32, 32), sf);
            IntPtr h = bmp.GetHicon();
            var ico = (Icon)Icon.FromHandle(h).Clone();
            Native.DestroyIcon(h);
            return ico;
        }
    }

    void ShowHiddenTip() {
        if (tipShown) return;
        tipShown = true;
        tray.ShowBalloonTip(2500, "BiliTray", "已缩到托盘，音乐继续播放。单击图标打开窗口，右键可以退出。", ToolTipIcon.Info);
    }
    void ShowPlayer() {
        if (hwnd == IntPtr.Zero) return;
        Native.ShowWindow(hwnd, 9);
        Native.SetForegroundWindow(hwnd);
    }
    void HidePlayer() {
        if (hwnd == IntPtr.Zero) return;
        Native.ShowWindow(hwnd, 0);
        ShowHiddenTip();
    }
    void TogglePlayer() {
        if (hwnd == IntPtr.Zero) return;
        if (Native.IsWindowVisible(hwnd) && !Native.IsIconic(hwnd)) HidePlayer(); else ShowPlayer();
    }
    void QuitAll() {
        timer.Stop();
        CloseHook.Stop();
        // 发 WM_CLOSE 让 Chrome 正常退出，登录状态等数据会先保存好
        if (hwnd != IntPtr.Zero && Native.IsWindow(hwnd)) Native.PostMessage(hwnd, 0x10, IntPtr.Zero, IntPtr.Zero);
        tray.Visible = false;
        tray.Dispose();
        ExitThread();
    }

    void OnTick() {
        ticks++;
        if (hwnd == IntPtr.Zero) {
            if (ticks % 3 == 0) {
                var pids = GetOurPids();
                if (pids.Count > 0) { hwnd = Native.FindChromeWindow(pids); CloseHook.Target = hwnd; }
            }
            if (ticks > 200 && hwnd == IntPtr.Zero) QuitAll();   // 约 60 秒还没窗口就放弃
            return;
        }
        if (!Native.IsWindow(hwnd)) { QuitAll(); return; }       // 窗口真的没了 = 一起退出
        if (CloseHook.HiddenByClose) { CloseHook.HiddenByClose = false; ShowHiddenTip(); }
        if (Native.IsIconic(hwnd) && Native.IsWindowVisible(hwnd)) HidePlayer();
        WatchTabs();

        // 窗口藏起来后：3 秒后清一次内存，之后每 2 分钟清一次
        if (Native.IsWindowVisible(hwnd)) hiddenTicks = 0;
        else if (++hiddenTicks == 10 || hiddenTicks % 400 == 0) TrimMemory();
        if (ticks % 5 == 0) {
            string t = Native.Title(hwnd);
            if (t.EndsWith("_哔哩哔哩_bilibili")) t = t.Substring(0, t.Length - "_哔哩哔哩_bilibili".Length);
            if (t.Length == 0) t = "BiliTray";
            if (t.Length > 60) t = t.Substring(0, 60) + "…";
            tray.Text = t;
        }
    }

    void TrimMemory() {
        if (mainWs != null) {
            var prm = new Dictionary<string, object>(); prm["level"] = "critical";
            Cdp.Call(mainWs, "Memory.simulatePressureNotification", prm);   // 让 Chrome 自己先丢掉缓存
        }
        foreach (uint pid in GetOurPids()) Native.TrimProcess(pid);
    }

    // 有新标签页冒出来：把网址拿回播放窗口打开，再关掉它
    void WatchTabs() {
        string raw = Cdp.Get(CdpBase + "/json/list");
        if (raw == null) return;
        var pages = new List<Dictionary<string, object>>();
        try {
            foreach (var t in Cdp.Json.Deserialize<List<Dictionary<string, object>>>(raw))
                if (Str(t, "type") == "page") pages.Add(t);
        } catch { return; }
        if (pages.Count == 0) return;

        Dictionary<string, object> main = null;
        foreach (var p in pages) if (Str(p, "id") == mainId) main = p;
        if (main == null) {
            if (pages.Count != 1) return;   // 一开始只有播放窗口这一个页面
            main = pages[0];
            mainId = Str(main, "id"); mainWs = Str(main, "webSocketDebuggerUrl"); lastUrl = null;
        }

        foreach (var t in pages) {
            string id = Str(t, "id");
            if (id == mainId) continue;
            string u = Str(t, "url");
            if (u.Length > 0 && !u.StartsWith("about:blank") && !u.StartsWith("chrome://newtab") && !u.StartsWith("chrome://new-tab-page")) {
                var prm = new Dictionary<string, object>(); prm["url"] = u;
                Cdp.Call(mainWs, "Page.navigate", prm);
                Cdp.Get(CdpBase + "/json/close/" + id);
            } else {
                int n; blankSeen.TryGetValue(id, out n); blankSeen[id] = ++n;
                if (n > 10) Cdp.Get(CdpBase + "/json/close/" + id);
            }
        }

        string mainUrl = Str(main, "url");
        if (mainUrl != lastUrl || ticks % 15 == 0 || powerDirty) {
            lastUrl = mainUrl;
            powerDirty = false;
            var prm = new Dictionary<string, object>(); prm["expression"] = SameWindowJs;
            Cdp.Call(mainWs, "Runtime.evaluate", prm);
            var prm2 = new Dictionary<string, object>(); prm2["expression"] = PowerJs.Replace("__ON__", powerSave ? "true" : "false");
            Cdp.Call(mainWs, "Runtime.evaluate", prm2);
        }
    }

    static string Str(Dictionary<string, object> d, string k) {
        object v; return d.TryGetValue(k, out v) && v != null ? v.ToString() : "";
    }
}

static class Program {
    [STAThread]
    static void Main(string[] args) {
        bool created;
        using (var mutex = new Mutex(true, "Local\\BiliTrayMusicPlayer", out created)) {
            if (!created) return;
            // 更新时正在运行的旧 exe 会被改名成 BiliTray.old.exe，这里顺手删掉
            try { File.Delete(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BiliTray.old.exe")); } catch { }
            try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayApp(args.Length > 0 ? args[0] : "https://www.bilibili.com"));
        }
    }
}
