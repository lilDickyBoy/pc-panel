using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Xml.Linq;
using PcPanel.Core;

namespace PcPanel.Services.Screen
{
    /// <summary>
    /// The extra monitor Windows extends the desktop to, provided by the open-source
    /// Virtual Display Driver (github.com/VirtualDrivers/Virtual-Display-Driver).
    /// It is only switched on while the phone is used as a second screen, so no window
    /// can get lost on an invisible monitor.
    /// </summary>
    public static class VirtualDisplay
    {
        const string SettingsPath = @"C:\VirtualDisplayDriver\vdd_settings.xml";
        const string Filter = "$_.FriendlyName -like '*Virtual Display*' -or $_.InstanceId -like '*MttVDD*' -or $_.FriendlyName -like '*IddSampleDriver*'";

        public static bool? Installed { get; private set; }

        public static async Task<bool> CheckInstalled()
        {
            var r = await ScreenTools.PowerShell("$d = Get-PnpDevice -Class Display -ErrorAction SilentlyContinue | Where-Object { " + Filter + " } | Select-Object -First 1; if ($d) { 'YES|' + $d.Status } else { 'NO' }", 20000);
            Installed = (r.Out ?? "").Contains("YES|");
            return Installed.Value;
        }

        /// <summary>Installs the driver with winget (comes with Windows 11). Returns false if it failed.</summary>
        public static async Task<bool> Install()
        {
            var r = await ScreenTools.Run("winget.exe",
                "install --id=VirtualDrivers.Virtual-Display-Driver -e --silent --accept-package-agreements --accept-source-agreements --disable-interactivity",
                10 * 60 * 1000);
            Log.Write("winget VDD: " + r.Code + " " + (r.Out + r.Err).Trim().Replace("\r", "").Split('\n').LastOrDefault());
            await Task.Delay(1500);
            return await CheckInstalled();
        }

        /// <summary>Makes sure the driver offers exactly one monitor with the phone's resolution at 60 Hz.</summary>
        public static bool Configure(int w, int h)
        {
            try
            {
                XDocument doc = null;
                if (File.Exists(SettingsPath))
                {
                    try { doc = XDocument.Load(SettingsPath); } catch { doc = null; }
                }
                bool changed = false;
                if (doc?.Root == null)
                {
                    doc = new XDocument(new XDeclaration("1.0", "utf-8", null), new XElement("vdd_settings"));
                    changed = true;
                }
                var root = doc.Root;

                XElement Child(XElement parent, string name)
                {
                    var e = parent.Element(name);
                    if (e == null) { e = new XElement(name); parent.Add(e); changed = true; }
                    return e;
                }

                var count = Child(Child(root, "monitors"), "count");
                if (count.Value.Trim() != "1") { count.Value = "1"; changed = true; }

                var global = Child(root, "global");
                if (!global.Elements("g_refresh_rate").Any(e => e.Value.Trim() == "60"))
                {
                    global.Add(new XElement("g_refresh_rate", "60"));
                    changed = true;
                }

                var res = Child(root, "resolutions");
                foreach (var (rw, rh) in new[] { (w, h), (h, w) })
                {
                    bool has = res.Elements("resolution").Any(e =>
                        e.Element("width")?.Value.Trim() == rw.ToString() && e.Element("height")?.Value.Trim() == rh.ToString());
                    if (!has)
                    {
                        res.Add(new XElement("resolution",
                            new XElement("width", rw), new XElement("height", rh), new XElement("refresh_rate", 60)));
                        changed = true;
                    }
                }

                if (changed)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                    if (File.Exists(SettingsPath) && !File.Exists(SettingsPath + ".pcpanel-backup"))
                        try { File.Copy(SettingsPath, SettingsPath + ".pcpanel-backup"); } catch { }
                    doc.Save(SettingsPath);
                }
                return changed;
            }
            catch (Exception ex) { Log.Write("VDD configure: " + ex.Message); return false; }
        }

        static string Marker => Path.Combine(AppSettings.Dir, "virtual-monitor-on");

        static readonly System.Threading.SemaphoreSlim _pnp = new System.Threading.SemaphoreSlim(1, 1);

        public static async Task SetEnabled(bool on)
        {
            await _pnp.WaitAsync();
            try { await SetEnabledCore(on); } finally { _pnp.Release(); }
        }

        static async Task SetEnabledCore(bool on)
        {
            // remember that we switched it on, so it can be switched off after a crash
            try { if (on) { Directory.CreateDirectory(AppSettings.Dir); File.WriteAllText(Marker, DateTime.Now.ToString("o")); } } catch { }
            await ScreenTools.PowerShell(
                "$d = Get-PnpDevice -Class Display -ErrorAction SilentlyContinue | Where-Object { " + Filter + " }; " +
                "foreach ($x in $d) { " + (on ? "Enable-PnpDevice" : "Disable-PnpDevice") + " -InstanceId $x.InstanceId -Confirm:$false -ErrorAction SilentlyContinue }", 30000);
            try { if (!on && File.Exists(Marker)) File.Delete(Marker); } catch { }
        }

        /// <summary>At start-up: if PC Panel quit unexpectedly while the phone was a monitor, take the monitor away again.</summary>
        public static void CleanUpAfterCrash()
        {
            if (!File.Exists(Marker)) return;
            Task.Run(() => SetEnabled(false));
        }

        public static async Task Restart()
        {
            await _pnp.WaitAsync();
            try
            {
                await SetEnabledCore(false);
                await Task.Delay(800);
                await SetEnabledCore(true);
            }
            finally { _pnp.Release(); }
        }

        // ================================================================== finding the monitor
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }
        const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 1;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        /// <summary>GDI name of the virtual monitor ("\\.\DISPLAY3"), or null when it isn't there.</summary>
        public static string FindGdiName()
        {
            var d = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            for (uint i = 0; EnumDisplayDevices(null, i, ref d, 0); i++)
            {
                var s = (d.DeviceString ?? "") + "|" + (d.DeviceID ?? "");
                if (s.Contains("Virtual Display", StringComparison.OrdinalIgnoreCase) || s.Contains("MttVDD", StringComparison.OrdinalIgnoreCase)
                    || s.Contains("IddSampleDriver", StringComparison.OrdinalIgnoreCase))
                    return d.DeviceName;
                d.cb = Marshal.SizeOf<DISPLAY_DEVICE>();
            }
            return null;
        }

        /// <summary>Waits until the driver's monitor exists (and, briefly, until Windows has put it on the desktop).</summary>
        public static async Task<string> WaitForMonitor(int timeoutMs = 10000)
        {
            var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            DateTime? foundAt = null;
            string name = null;
            while (DateTime.UtcNow < until)
            {
                name = FindGdiName();
                if (name != null)
                {
                    if (Bounds(name).Width > 0) return name;
                    foundAt ??= DateTime.UtcNow;
                    // exists but not attached to the desktop yet – SetMode will attach it
                    if ((DateTime.UtcNow - foundAt.Value).TotalMilliseconds > 2500) return name;
                }
                await Task.Delay(200);
            }
            return name;
        }

        // ================================================================== mode & position
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields;
            public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }
        const int ENUM_CURRENT_SETTINGS = -1;
        const int DM_POSITION = 0x20, DM_DISPLAYORIENTATION = 0x80, DM_PELSWIDTH = 0x80000, DM_PELSHEIGHT = 0x100000, DM_DISPLAYFREQUENCY = 0x400000;
        const uint CDS_UPDATEREGISTRY = 0x1, CDS_NORESET = 0x10000000;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int ChangeDisplaySettingsEx(string deviceName, ref DEVMODE devMode, IntPtr hwnd, uint flags, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int ChangeDisplaySettingsEx(string deviceName, IntPtr devMode, IntPtr hwnd, uint flags, IntPtr lParam);

        public static System.Drawing.Rectangle Bounds(string gdi)
        {
            var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            if (gdi == null || !EnumDisplaySettings(gdi, ENUM_CURRENT_SETTINGS, ref dm)) return System.Drawing.Rectangle.Empty;
            return new System.Drawing.Rectangle(dm.dmPositionX, dm.dmPositionY, dm.dmPelsWidth, dm.dmPelsHeight);
        }

        /// <summary>Sets w×h @60 Hz and puts the monitor to the right of the main screen (where a phone usually lies).</summary>
        public static bool SetMode(string gdi, int w, int h)
        {
            try
            {
                var primary = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
                EnumDisplaySettings(gdi, ENUM_CURRENT_SETTINGS, ref dm);
                dm.dmPelsWidth = w;
                dm.dmPelsHeight = h;
                dm.dmDisplayFrequency = 60;
                dm.dmDisplayOrientation = 0;
                dm.dmPositionX = primary.Right;
                dm.dmPositionY = primary.Top + Math.Max(0, (primary.Height - h) / 2);
                dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY | DM_POSITION | DM_DISPLAYORIENTATION;
                int r = ChangeDisplaySettingsEx(gdi, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY | CDS_NORESET, IntPtr.Zero);
                ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
                if (r != 0) Log.Write("SetMode " + w + "x" + h + " -> " + r);
                return r == 0;
            }
            catch (Exception ex) { Log.Write("SetMode: " + ex.Message); return false; }
        }

        // ================================================================== DPI scaling (per monitor)
        [StructLayout(LayoutKind.Sequential)] struct LUID { public uint Low; public int High; }

        [StructLayout(LayoutKind.Sequential)]
        struct PATH_INFO
        {
            public LUID srcAdapter; public uint srcId, srcModeIdx, srcFlags;
            public LUID tgtAdapter; public uint tgtId, tgtModeIdx, tech, rotation, scaling, refNum, refDen, scanline, available, tgtFlags;
            public uint flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct HEADER { public int type; public uint size; public LUID adapterId; public uint id; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SOURCE_NAME { public HEADER header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string gdi; }

        [StructLayout(LayoutKind.Sequential)] struct DPI_GET { public HEADER header; public int min, cur, max; }
        [StructLayout(LayoutKind.Sequential)] struct DPI_SET { public HEADER header; public int rel; }

        [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);
        [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint numPaths, [Out] PATH_INFO[] paths, ref uint numModes, [Out] byte[] modes, IntPtr topology);
        [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref SOURCE_NAME p);
        [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref DPI_GET p);
        [DllImport("user32.dll")] static extern int DisplayConfigSetDeviceInfo(ref DPI_SET p);

        static readonly int[] DpiSteps = { 100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500 };

        /// <summary>Windows "Scale" for that monitor (100, 125, 150 … %). Undocumented but stable API, used by many tools.</summary>
        public static void SetScale(string gdi, int percent)
        {
            try
            {
                if (GetDisplayConfigBufferSizes(2, out var np, out var nm) != 0) return;
                var paths = new PATH_INFO[np];
                var modes = new byte[nm * 64];
                if (QueryDisplayConfig(2, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return;
                for (int i = 0; i < np; i++)
                {
                    var name = new SOURCE_NAME { header = new HEADER { type = 1, size = (uint)Marshal.SizeOf<SOURCE_NAME>(), adapterId = paths[i].srcAdapter, id = paths[i].srcId } };
                    if (DisplayConfigGetDeviceInfo(ref name) != 0 || !string.Equals(name.gdi, gdi, StringComparison.OrdinalIgnoreCase)) continue;

                    var get = new DPI_GET { header = new HEADER { type = -3, size = (uint)Marshal.SizeOf<DPI_GET>(), adapterId = paths[i].srcAdapter, id = paths[i].srcId } };
                    if (DisplayConfigGetDeviceInfo(ref get) != 0) return;
                    int recommended = Math.Abs(get.min);
                    int target = Array.IndexOf(DpiSteps, DpiSteps.OrderBy(v => Math.Abs(v - percent)).First());
                    int rel = Math.Clamp(target - recommended, get.min, get.max);
                    if (rel == get.cur) return;
                    var set = new DPI_SET { header = new HEADER { type = -4, size = (uint)Marshal.SizeOf<DPI_SET>(), adapterId = paths[i].srcAdapter, id = paths[i].srcId }, rel = rel };
                    int r = DisplayConfigSetDeviceInfo(ref set);
                    if (r != 0) Log.Write("SetScale -> " + r);
                    return;
                }
            }
            catch (Exception ex) { Log.Write("SetScale: " + ex.Message); }
        }

        // ================================================================== DXGI output index (for FFmpeg ddagrab)
        [DllImport("dxgi.dll")] static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DXGI_OUTPUT_DESC
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            public int Left, Top, Right, Bottom;
            public int Attached, Rotation;
            public IntPtr Monitor;
        }

        static unsafe void** Vt(IntPtr p) => *(void***)(void*)p;

        static unsafe void Release(IntPtr p)
        {
            if (p == IntPtr.Zero) return;
            var vt = Vt(p);
            ((delegate* unmanaged[Stdcall]<IntPtr, uint>)vt[2])(p);
        }

        /// <summary>Which GPU (adapter) and which output of it shows <paramref name="gdi"/>.</summary>
        public static unsafe (int adapter, int output) FindDxgiOutput(string gdi)
        {
            var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387"); // IDXGIFactory1
            if (CreateDXGIFactory1(ref iid, out var factory) != 0) return (-1, -1);
            try
            {
                var fvt = Vt(factory);
                var enumAdapters1 = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)fvt[12];
                for (uint a = 0; a < 16; a++)
                {
                    IntPtr adapter;
                    if (enumAdapters1(factory, a, &adapter) != 0) break;
                    try
                    {
                        var avt = Vt(adapter);
                        var enumOutputs = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)avt[7];
                        for (uint o = 0; o < 16; o++)
                        {
                            IntPtr output;
                            if (enumOutputs(adapter, o, &output) != 0) break;
                            try
                            {
                                var ovt = Vt(output);
                                var getDesc = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)ovt[7];
                                var mem = Marshal.AllocHGlobal(Marshal.SizeOf<DXGI_OUTPUT_DESC>());
                                try
                                {
                                    if (getDesc(output, mem) == 0)
                                    {
                                        var desc = Marshal.PtrToStructure<DXGI_OUTPUT_DESC>(mem);
                                        if (string.Equals(desc.DeviceName, gdi, StringComparison.OrdinalIgnoreCase)) return ((int)a, (int)o);
                                    }
                                }
                                finally { Marshal.FreeHGlobal(mem); }
                            }
                            finally { Release(output); }
                        }
                    }
                    finally { Release(adapter); }
                }
            }
            catch (Exception ex) { Log.Write("DXGI: " + ex.Message); }
            finally { Release(factory); }
            return (-1, -1);
        }
    }
}
