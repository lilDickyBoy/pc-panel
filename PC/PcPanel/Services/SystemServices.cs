using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using PcPanel.Core;

namespace PcPanel.Services
{
    // ---------------------------------------------------------------- Processes
    public static class ProcessService
    {
        static readonly HashSet<string> Protected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "system", "idle", "registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost",
            "dwm", "fontdrvhost", "memory compression", "secure system", "lsaiso", "sihost", "ctfmon", "conhost",
            "explorer", "taskhostw", "spoolsv", "audiodg", "searchhost", "startmenuexperiencehost", "shellexperiencehost",
            "runtimebroker", "textinputhost", "msmpeng", "securityhealthservice", "nvcontainer", "nvdisplay.container", "pcpanel", "mobilink"
        };

        public static object List()
        {
            var groups = new Dictionary<string, (long mem, int count)>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    var name = p.ProcessName;
                    long ws = p.WorkingSet64;
                    groups.TryGetValue(name, out var g);
                    groups[name] = (g.mem + ws, g.count + 1);
                }
                catch { }
                finally { p.Dispose(); }
            }
            var list = groups
                .OrderByDescending(kv => kv.Value.mem)
                .Take(30)
                .Select(kv => (object)new { name = kv.Key, mem = kv.Value.mem, count = kv.Value.count, locked = Protected.Contains(kv.Key) })
                .ToList();
            return new { type = "procs", list };
        }

        public static bool Kill(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || Protected.Contains(name)) return false;
            bool any = false;
            foreach (var p in Process.GetProcessesByName(name))
            {
                try { p.Kill(); any = true; } catch { }
                finally { p.Dispose(); }
            }
            return any;
        }
    }

    // ---------------------------------------------------------------- Display
    public static class DisplayService
    {
        public static int NightLevel { get; private set; }

        static T WithMonitor<T>(Func<IntPtr, T> f, T fallback)
        {
            var hmon = Native.MonitorFromPoint(new Native.POINT { X = 0, Y = 0 }, 1);
            if (!Native.GetNumberOfPhysicalMonitorsFromHMONITOR(hmon, out uint n) || n == 0) return fallback;
            var arr = new Native.PHYSICAL_MONITOR[n];
            if (!Native.GetPhysicalMonitorsFromHMONITOR(hmon, n, arr)) return fallback;
            try { return f(arr[0].hPhysicalMonitor); }
            finally { Native.DestroyPhysicalMonitors(n, arr); }
        }

        /// <summary>0..100, or -1 when the monitor doesn't support DDC/CI.</summary>
        public static int GetBrightness()
        {
            try
            {
                return WithMonitor(h =>
                {
                    if (!Native.GetMonitorBrightness(h, out uint min, out uint cur, out uint max) || max <= min) return -1;
                    return (int)Math.Round((cur - min) * 100.0 / (max - min));
                }, -1);
            }
            catch { return -1; }
        }

        public static bool SetBrightness(int pct)
        {
            try
            {
                return WithMonitor(h =>
                {
                    if (!Native.GetMonitorBrightness(h, out uint min, out uint cur, out uint max) || max <= min) return false;
                    uint v = (uint)(min + (max - min) * Math.Clamp(pct, 0, 100) / 100.0);
                    return Native.SetMonitorBrightness(h, v);
                }, false);
            }
            catch { return false; }
        }

        /// <summary>Warm screen filter through the gamma ramp. 0 = off, 100 = warmest.</summary>
        public static bool SetNight(int level)
        {
            level = Math.Clamp(level, 0, 100);
            double t = level / 100.0;
            double rM = 1.0, gM = 1.0 - 0.22 * t, bM = 1.0 - 0.48 * t;
            var ramp = new Native.RAMP { Red = new ushort[256], Green = new ushort[256], Blue = new ushort[256] };
            for (int i = 0; i < 256; i++)
            {
                ramp.Red[i] = (ushort)Math.Min(65535, i * 257 * rM);
                ramp.Green[i] = (ushort)Math.Min(65535, i * 257 * gM);
                ramp.Blue[i] = (ushort)Math.Min(65535, i * 257 * bM);
            }
            var dc = Native.GetDC(IntPtr.Zero);
            try
            {
                bool ok = Native.SetDeviceGammaRamp(dc, ref ramp);
                if (ok) NightLevel = level;
                return ok;
            }
            finally { Native.ReleaseDC(IntPtr.Zero, dc); }
        }
    }

    // ---------------------------------------------------------------- Install / startup / firewall
    public static class SystemSetup
    {
        public static string ExePath => Environment.ProcessPath;

        /// <summary>
        /// Opens a URL, file, folder or URI as the normal (non-admin) user, through Explorer.
        /// PC Panel runs elevated for the sensor driver; this keeps browsers and apps un-elevated.
        /// </summary>
        public static void ShellOpen(string target)
        {
            if (string.IsNullOrWhiteSpace(target)) return;
            try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + target.Replace("\"", "") + "\"") { UseShellExecute = false }); }
            catch (Exception ex)
            {
                Log.Write("ShellOpen: " + ex.Message);
                try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
            }
        }

        public static void EnsureInstalled()
        {
            try
            {
                var exe = ExePath;
                if (App.Settings.InstalledPath != exe)
                {
                    CreateStartMenuShortcut(exe);
                    SetupFirewall(exe);
                    RemoveOldMobiLink();
                    App.Settings.InstalledPath = exe;
                    App.Settings.Save();
                }
                SetAutostart(App.Settings.Autostart);
            }
            catch (Exception ex) { Log.Write("Setup: " + ex.Message); }
        }

        /// <summary>Removes leftovers of MobiLink 1.x (the old name of this app).</summary>
        static void RemoveOldMobiLink()
        {
            try
            {
                Run("schtasks", "/Delete /TN \"MobiLink\" /F");
                Run("netsh", "advfirewall firewall delete rule name=\"MobiLink\"");
                var lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "MobiLink.lnk");
                if (File.Exists(lnk)) File.Delete(lnk);
            }
            catch { }
        }

        static void CreateStartMenuShortcut(string exe)
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
                Directory.CreateDirectory(dir);
                var lnkPath = Path.Combine(dir, "PC Panel.lnk");
                var t = Type.GetTypeFromProgID("WScript.Shell");
                dynamic sh = Activator.CreateInstance(t);
                dynamic lnk = sh.CreateShortcut(lnkPath);
                lnk.TargetPath = exe;
                lnk.WorkingDirectory = Path.GetDirectoryName(exe);
                lnk.IconLocation = exe + ",0";
                lnk.Description = "PC Panel - control your PC from your phone";
                lnk.Save();
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(lnk);
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(sh);
            }
            catch (Exception ex) { Log.Write("Shortcut: " + ex.Message); }
        }

        static void SetupFirewall(string exe)
        {
            Run("netsh", "advfirewall firewall delete rule name=\"PC Panel\"");
            Run("netsh", $"advfirewall firewall add rule name=\"PC Panel\" dir=in action=allow program=\"{exe}\" enable=yes profile=any remoteip=localsubnet");
        }

        /// <summary>
        /// Uses Task Scheduler (highest privileges) so PC Panel starts at logon without a UAC prompt
        /// and is never killed after 72 h (ExecutionTimeLimit = PT0S).
        /// </summary>
        public static void SetAutostart(bool on)
        {
            try
            {
                if (!on) { Run("schtasks", "/Delete /TN \"PC Panel\" /F"); return; }
                var user = WindowsIdentity.GetCurrent().Name;
                var exe = ExePath;
                var xml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo><Description>PC Panel</Description></RegistrationInfo>
  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{System.Security.SecurityElement.Escape(user)}</UserId><Delay>PT5S</Delay></LogonTrigger></Triggers>
  <Principals><Principal id=""Author""><UserId>{System.Security.SecurityElement.Escape(user)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>5</Priority>
  </Settings>
  <Actions Context=""Author""><Exec><Command>{System.Security.SecurityElement.Escape(exe)}</Command><Arguments>--tray</Arguments><WorkingDirectory>{System.Security.SecurityElement.Escape(Path.GetDirectoryName(exe))}</WorkingDirectory></Exec></Actions>
</Task>";
                var tmp = Path.Combine(Path.GetTempPath(), "pcpanel_task.xml");
                File.WriteAllText(tmp, xml, Encoding.Unicode);
                Run("schtasks", $"/Create /TN \"PC Panel\" /XML \"{tmp}\" /F");
                try { File.Delete(tmp); } catch { }
            }
            catch (Exception ex) { Log.Write("Autostart: " + ex.Message); }
        }

        static void Run(string exe, string args)
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo(exe, args) { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
                p?.WaitForExit(8000);
            }
            catch (Exception ex) { Log.Write(exe + ": " + ex.Message); }
        }
    }
}
