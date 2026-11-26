using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using PcPanel.Core;

namespace PcPanel.Services.Screen
{
    /// <summary>
    /// Turns finger touches on the phone into real Windows touch input (multi-touch, scrolling, pinch zoom)
    /// on the virtual monitor. Falls back to the mouse if touch injection is not available.
    /// </summary>
    public sealed class TouchInjector : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        struct POINTER_INFO
        {
            public int pointerType;
            public uint pointerId;
            public uint frameId;
            public uint pointerFlags;
            public IntPtr sourceDevice;
            public IntPtr hwndTarget;
            public POINT ptPixelLocation, ptHimetricLocation, ptPixelLocationRaw, ptHimetricLocationRaw;
            public uint dwTime, historyCount;
            public int inputData;
            public uint dwKeyStates;
            public ulong performanceCount;
            public int buttonChangeType;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct POINTER_TOUCH_INFO
        {
            public POINTER_INFO pointerInfo;
            public uint touchFlags, touchMask;
            public RECT rcContact, rcContactRaw;
            public uint orientation, pressure;
        }

        const int PT_TOUCH = 2;
        const uint F_INRANGE = 0x2, F_INCONTACT = 0x4, F_DOWN = 0x10000, F_UPDATE = 0x20000, F_UP = 0x40000;
        const uint MASK_ALL = 0x7;

        [DllImport("user32.dll", SetLastError = true)] static extern bool InitializeTouchInjection(uint maxCount, uint mode);
        [DllImport("user32.dll", SetLastError = true)] static extern bool InjectTouchInput(uint count, [In] POINTER_TOUCH_INFO[] contacts);
        [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, int data, IntPtr extra);

        public enum Phase : byte { Down = 0, Move = 1, Up = 2 }
        public struct Contact { public int Id; public double X, Y; public Phase Phase; }

        readonly object _lock = new object();
        readonly Dictionary<uint, POINT> _active = new Dictionary<uint, POINT>();
        readonly Timer _keepAlive;
        readonly bool _touchOk;
        long _lastInject;
        bool _mouseDown;

        public Func<System.Drawing.Rectangle> Area;   // screen rectangle the phone shows

        public TouchInjector()
        {
            try { _touchOk = InitializeTouchInjection(10, 1); }
            catch { _touchOk = false; }
            if (!_touchOk) Log.Write("Touch injection unavailable, using the mouse");
            _keepAlive = new Timer(_ => KeepAlive(), null, 100, 100);
        }

        POINT Map(double nx, double ny)
        {
            var r = Area?.Invoke() ?? System.Drawing.Rectangle.Empty;
            if (r.Width <= 0) r = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            return new POINT
            {
                X = r.Left + (int)Math.Round(Math.Clamp(nx, 0, 1) * (r.Width - 1)),
                Y = r.Top + (int)Math.Round(Math.Clamp(ny, 0, 1) * (r.Height - 1))
            };
        }

        static POINTER_TOUCH_INFO Make(uint id, POINT p, uint flags)
        {
            var t = new POINTER_TOUCH_INFO();
            t.pointerInfo.pointerType = PT_TOUCH;
            t.pointerInfo.pointerId = id;
            t.pointerInfo.ptPixelLocation = p;
            t.pointerInfo.pointerFlags = flags;
            t.touchMask = MASK_ALL;
            t.rcContact = new RECT { Left = p.X - 2, Top = p.Y - 2, Right = p.X + 2, Bottom = p.Y + 2 };
            t.orientation = 90;
            t.pressure = 32000;
            return t;
        }

        /// <summary>One touch frame from the phone (all fingers currently on the glass).</summary>
        public void Handle(IReadOnlyList<Contact> frame)
        {
            if (frame.Count == 0) return;
            lock (_lock)
            {
                if (!_touchOk) { Mouse(frame[0]); return; }
                var list = new List<POINTER_TOUCH_INFO>(frame.Count + 2);
                var seen = new HashSet<uint>();
                foreach (var c in frame)
                {
                    uint id = (uint)(c.Id % 10);
                    if (!seen.Add(id)) continue;
                    var p = Map(c.X, c.Y);
                    bool known = _active.ContainsKey(id);
                    switch (c.Phase)
                    {
                        case Phase.Down:
                            if (known) list.Add(Make(id, p, F_UPDATE | F_INRANGE | F_INCONTACT));
                            else list.Add(Make(id, p, F_DOWN | F_INRANGE | F_INCONTACT));
                            _active[id] = p;
                            break;
                        case Phase.Move:
                            if (!known) { list.Add(Make(id, p, F_DOWN | F_INRANGE | F_INCONTACT)); _active[id] = p; break; }
                            list.Add(Make(id, p, F_UPDATE | F_INRANGE | F_INCONTACT));
                            _active[id] = p;
                            break;
                        case Phase.Up:
                            if (!known) break;
                            list.Add(Make(id, p, F_UP));
                            _active.Remove(id);
                            break;
                    }
                }
                // Windows wants every finger that's still down in each call
                foreach (var kv in _active)
                    if (!seen.Contains(kv.Key)) list.Add(Make(kv.Key, kv.Value, F_UPDATE | F_INRANGE | F_INCONTACT));
                Inject(list);
            }
        }

        void Inject(List<POINTER_TOUCH_INFO> list)
        {
            if (list.Count == 0) return;
            if (!InjectTouchInput((uint)list.Count, list.ToArray()))
            {
                int err = Marshal.GetLastWin32Error();
                // a stale contact – start over cleanly
                if (err == 87 && _active.Count > 0) _active.Clear();
            }
            Interlocked.Exchange(ref _lastInject, Environment.TickCount64);
        }

        void KeepAlive()
        {
            lock (_lock)
            {
                if (_active.Count == 0 || Environment.TickCount64 - Interlocked.Read(ref _lastInject) < 80) return;
                var list = new List<POINTER_TOUCH_INFO>();
                foreach (var kv in _active) list.Add(Make(kv.Key, kv.Value, F_UPDATE | F_INRANGE | F_INCONTACT));
                Inject(list);
            }
        }

        /// <summary>Lift every finger (connection lost, touch turned off).</summary>
        public void ReleaseAll()
        {
            lock (_lock)
            {
                if (_mouseDown) { mouse_event(0x0004, 0, 0, 0, IntPtr.Zero); _mouseDown = false; }
                if (!_touchOk || _active.Count == 0) return;
                var list = new List<POINTER_TOUCH_INFO>();
                foreach (var kv in _active) list.Add(Make(kv.Key, kv.Value, F_UP));
                _active.Clear();
                Inject(list);
            }
        }

        void Mouse(Contact c)
        {
            var p = Map(c.X, c.Y);
            SetCursorPos(p.X, p.Y);
            if (c.Phase == Phase.Down && !_mouseDown) { mouse_event(0x0002, 0, 0, 0, IntPtr.Zero); _mouseDown = true; }
            else if (c.Phase == Phase.Up && _mouseDown) { mouse_event(0x0004, 0, 0, 0, IntPtr.Zero); _mouseDown = false; }
        }

        public void Dispose()
        {
            ReleaseAll();
            _keepAlive.Dispose();
        }
    }
}
