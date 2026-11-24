using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using PcPanel.Core;

namespace PcPanel.Services
{
    /// <summary>
    /// Two-way clipboard text sync. Listens for WM_CLIPBOARDUPDATE on a hidden message window.
    /// Everything here runs on the UI thread.
    /// </summary>
    public static class ClipboardService
    {
        static HwndSource _src;
        static string _lastSent = "";
        static string _lastReceived = "";
        static DateTime _suppressUntil = DateTime.MinValue;

        public static void Init()
        {
            try
            {
                var p = new HwndSourceParameters("PCPanelClipboard") { Width = 0, Height = 0, WindowStyle = 0, ParentWindow = new IntPtr(-3) /* HWND_MESSAGE */ };
                _src = new HwndSource(p);
                _src.AddHook(Hook);
                Native.AddClipboardFormatListener(_src.Handle);
            }
            catch (Exception ex) { Log.Write("Clipboard init: " + ex.Message); }
        }

        public static void Shutdown()
        {
            try { if (_src != null) { Native.RemoveClipboardFormatListener(_src.Handle); _src.Dispose(); } } catch { }
        }

        static IntPtr Hook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == Native.WM_CLIPBOARDUPDATE) OnChanged();
            return IntPtr.Zero;
        }

        static void OnChanged()
        {
            if (!App.Settings.ClipboardSync || App.Server == null || !App.Server.AnyConnected) return;
            if (DateTime.UtcNow < _suppressUntil) return;
            string text = null;
            for (int i = 0; i < 3 && text == null; i++)
            {
                try { if (Clipboard.ContainsText()) text = Clipboard.GetText(); else return; }
                catch { System.Threading.Thread.Sleep(30); }
            }
            if (string.IsNullOrEmpty(text) || text.Length > 200_000) return;
            if (text == _lastSent || text == _lastReceived) return;
            _lastSent = text;
            App.Server.Broadcast(new { type = "clip", text });
        }

        /// <summary>Send the current PC clipboard to the phone right now (button in the PC window).</summary>
        public static bool SendNow()
        {
            try
            {
                if (!Clipboard.ContainsText()) return false;
                var t = Clipboard.GetText();
                if (string.IsNullOrEmpty(t)) return false;
                _lastSent = t;
                App.Server?.Broadcast(new { type = "clip", text = t });
                return true;
            }
            catch { return false; }
        }

        /// <summary>Text arrived from the phone.</summary>
        public static void Receive(string text, bool explicitSend)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (!App.Settings.ClipboardSync && !explicitSend) return;
            App.Ui(() =>
            {
                _lastReceived = text;
                _suppressUntil = DateTime.UtcNow.AddMilliseconds(600);
                for (int i = 0; i < 5; i++)
                {
                    try { Clipboard.SetDataObject(text, true); break; }
                    catch { System.Threading.Thread.Sleep(40); }
                }
                App.Notch?.ShowInfo(Glyph.Clipboard, Loc.T("pc.clip.received"), 2.2);
            });
        }
    }
}
