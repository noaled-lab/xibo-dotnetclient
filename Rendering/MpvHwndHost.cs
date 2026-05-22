/**
 * Copyright (C) 2024 Xibo Signage Ltd
 *
 * Xibo - Digital Signage - https://xibosignage.com
 *
 * This file is part of Xibo.
 *
 * Xibo is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * any later version.
 *
 * Xibo is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with Xibo.  If not, see <http://www.gnu.org/licenses/>.
 */
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace XiboClient.Rendering
{
    /// <summary>
    /// WPF HwndHost????속??mpv ??레??어??Win32 ??식 창으????베??하??컨트??
    /// WM_ERASEBKGND??검??으??처리????초기??????색 ??래???? 방????다.
    /// </summary>
    internal class MpvHwndHost : HwndHost, INativeZIndexHost
    {
        // Runtime-configurable guard to hide/show the host window around VO init.
        private readonly bool _useWindowTimingGuard;

        private const int WS_CHILD    = 0x40000000;
        private const int WS_VISIBLE  = 0x10000000;
        private const int WS_CLIPCHILDREN = 0x02000000;
        private const int WS_CLIPSIBLINGS = 0x04000000;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int LWA_COLORKEY = 0x00000001;

        [DllImport("user32.dll")]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreateWindowEx(
            int dwExStyle, string lpClassName, string lpWindowName,
            int dwStyle, int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern int FillRect(IntPtr hDC, [In] ref RECT lprc, IntPtr hbr);

        [DllImport("gdi32.dll")]
        private static extern IntPtr GetStockObject(int fnObject);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_SHOW = 5;
        private const int SW_SHOWNA = 8;
        private const int SW_HIDE = 0;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_NOOWNERZORDER = 0x0200;

        // ??색/??색 ??래??방??
        private const int BLACK_BRUSH = 4;
        private const int WM_ERASEBKGND = 0x0014;
        private const int WM_PAINT = 0x000F;

        [DllImport("user32.dll")]
        private static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT lpPaint);
        [DllImport("user32.dll")]
        private static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT lpPaint);

        [StructLayout(LayoutKind.Sequential)]
        public struct PAINTSTRUCT
        {
            public IntPtr hdc;
            public bool fErase;
            public RECT rcPaint;
            public bool fRestore;
            public bool fIncUpdate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] rgbReserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        public event System.Action FileLoaded;
        public event System.Action VideoReconfig;
        public event System.Action<string> MediaFailed;
        public event System.Action<int> EndFile;

        // VIDEO_RECONFIG ??까지 WM_PAINT??검??으??처리????색 ??래??방??
        private volatile bool _videoReady = false;

        public IntPtr Hwnd => _hwndHost;
        public int NativeZIndex => _nativeZIndex;
        public long HostSequenceCounter => _hostSequence;
        public bool IsDisposed => _disposed;

        private IntPtr _mpvHandle = IntPtr.Zero;
        private IntPtr _hwndHost = IntPtr.Zero;
        private int _nativeZIndex;
        private long _hostSequence;
        private Thread _eventThread;
        private volatile bool _disposed = false;
        private readonly Dispatcher _dispatcher;

        private LibMpv.MpvWakeupCallback _wakeupCallback;
        private readonly AutoResetEvent _wakeupEvent = new AutoResetEvent(false);

        // BuildWindowCore ??전??Load/SetVolume ??이 ??출??경우????한 ??????
        private string _pendingFilePath;
        private bool? _pendingStretch;
        private int? _pendingVolume;
        private bool? _pendingMute;
        private bool? _pendingPause;

        [StructLayout(LayoutKind.Sequential)]
        struct mpv_event_log_message {
            public IntPtr prefix;
            public IntPtr level;
            public IntPtr text;
            public int log_level;
        }

        public MpvHwndHost()
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            _useWindowTimingGuard = ApplicationSettings.Default.MpvUseTimingGuard;
            Trace.WriteLine("MpvHwndHost: Constructor", "MpvHwndHost");
        }

        public bool EnableTransparency { get; set; } = false;

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            Trace.WriteLine($"MpvHwndHost: BuildWindowCore. Parent: {hwndParent.Handle}, Size: {Width}x{Height}", "MpvHwndHost");

            int w = (int)Math.Max(1, Width);
            int h = (int)Math.Max(1, Height);

            const int SS_BLACKRECT = 0x0004;

            int style = WS_CHILD | WS_CLIPCHILDREN | WS_CLIPSIBLINGS | SS_BLACKRECT;
            if (!_useWindowTimingGuard)
                style |= WS_VISIBLE;

            _hwndHost = CreateWindowEx(
                0, "STATIC", "",
                style,
                0, 0, w, h,
                hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

            if (_hwndHost == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                Trace.WriteLine($"MpvHwndHost: CreateWindowEx failed with error {err}", "MpvHwndHost");
                throw new InvalidOperationException("Failed to create host window for mpv. Error: " + err);
            }

            InitMpv(_hwndHost);
            RegisterHost();
            SyncHostWindowSize("BuildWindowCore");
            ApplyNativeZOrder();

            return new HandleRef(this, _hwndHost);
        }

        protected override void OnWindowPositionChanged(Rect rcBoundingBox)
        {
            base.OnWindowPositionChanged(rcBoundingBox);
            SyncHostWindowSize("OnWindowPositionChanged");
            ApplyNativeZOrder();
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            SyncHostWindowSize("OnRenderSizeChanged");
            ApplyNativeZOrder();
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            Trace.WriteLine("MpvHwndHost: DestroyWindowCore", "MpvHwndHost");
            UnregisterHost();
            Shutdown();
            if (hwnd.Handle != IntPtr.Zero)
                DestroyWindow(hwnd.Handle);
        }

        protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_ERASEBKGND)
            {
                IntPtr hdc = wParam;
                if (hdc != IntPtr.Zero)
                {
                    RECT rect;
                    if (GetClientRect(hwnd, out rect))
                    {
                        IntPtr hBrush = GetStockObject(BLACK_BRUSH);
                        FillRect(hdc, ref rect, hBrush);
                    }
                }
                handled = true;
                return new IntPtr(1);
            }

            // VIDEO_RECONFIG ??까지 WM_PAINT??검??으??처리 (mpv GPU ??더??초기??????색 ??래??방??)
            if (_useWindowTimingGuard && msg == WM_PAINT && !_videoReady)
            {
                PAINTSTRUCT ps;
                IntPtr hdc = BeginPaint(hwnd, out ps);
                if (hdc != IntPtr.Zero)
                {
                    RECT rect;
                    if (GetClientRect(hwnd, out rect))
                    {
                        IntPtr hBrush = GetStockObject(BLACK_BRUSH);
                        FillRect(hdc, ref rect, hBrush);
                    }
                    EndPaint(hwnd, ref ps);
                }
                handled = true;
                return IntPtr.Zero;
            }

            return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
        }

        protected override void Dispose(bool disposing)
        {
            Shutdown();
            base.Dispose(disposing);
        }

        /// <summary>
        /// mpv ??스??스??초기??하????벤??루프 ??레???? ??작??다.
        /// BuildWindowCore??서 ??출??????????점??hwnd가 ??효??다.
        /// </summary>
        private void InitMpv(IntPtr hwnd)
        {
            Trace.WriteLine("MpvHwndHost: InitMpv starting", "MpvHwndHost");

            int SetOptionStringChecked(string name, string value, bool required = false)
            {
                int rc = LibMpv.mpv_set_option_string(_mpvHandle, name, value);
                if (rc < 0)
                {
                    string msg = $"MpvHwndHost: mpv_set_option_string failed ({name}={value}) rc={rc}";
                    Trace.WriteLine(msg, "MpvHwndHost");
                    if (required)
                        throw new InvalidOperationException(msg);
                }
                return rc;
            }

            _mpvHandle = LibMpv.mpv_create();
            if (_mpvHandle == IntPtr.Zero)
                throw new InvalidOperationException("mpv_create() returned NULL.");

            // 콘솔 출력 비활??화 / 로그??Trace로만 ??집
            SetOptionStringChecked("terminal", "no");
            SetOptionStringChecked("msg-level", "all=v");

            // ??스??????들??mpv????달????당 ????에 ??더??
            long wid = hwnd.ToInt64();
            int widRc = LibMpv.mpv_set_option(_mpvHandle, "wid", LibMpv.MPV_FORMAT_INT64, ref wid);
            if (widRc < 0)
                throw new InvalidOperationException("MpvHwndHost: mpv_set_option(wid) failed rc=" + widRc);

            // ??오??크/??이?????? OSD·??력 비활??화
            // keep-open=yes: EOF ??에??마??????레??을 ????????색 ??방??.
            // SeekToStart() + Play()??루프??구현??????다.
            SetOptionStringChecked("keep-open", "yes", required: true);
            SetOptionStringChecked("osc", "no");
            SetOptionStringChecked("osd-level", "0");
            SetOptionStringChecked("input-default-bindings", "no");
            SetOptionStringChecked("input-vo-keyboard", "no");

            // vo / hwdec: Player Options??MPV ????????정??????용
            string vo = ApplicationSettings.Default.MpvVo;
            if (string.IsNullOrWhiteSpace(vo)) vo = "direct3d";
            SetOptionStringChecked("vo", vo, required: true);

            string hwdec = ApplicationSettings.Default.MpvHwdec;
            if (string.IsNullOrWhiteSpace(hwdec)) hwdec = "auto-safe";
            SetOptionStringChecked("hwdec", hwdec);

            // 배경 ??션?? mode/background-color??분리??어 ??어 ????지??해????다.
            SetOptionStringChecked("background", "color");
            SetOptionStringChecked("background-color", "#000000");

            // 추?? ??션: "key=value" ??줄씩
            string extra = ApplicationSettings.Default.MpvExtraOptions;
            if (!string.IsNullOrWhiteSpace(extra))
            {
                foreach (string line in extra.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0)
                    {
                        string key = line.Substring(0, eq).Trim();
                        string val = line.Substring(eq + 1).Trim();
                        if (!string.IsNullOrEmpty(key))
                            SetOptionStringChecked(key, val);
                    }
                }
            }

            int initRc = LibMpv.mpv_initialize(_mpvHandle);
            if (initRc < 0)
            {
                Trace.WriteLine($"MpvHwndHost: mpv_initialize failed: {initRc}", "MpvHwndHost");
                throw new InvalidOperationException("mpv_initialize() failed: " + initRc);
            }

            Trace.WriteLine("MpvHwndHost: mpv_initialize success", "MpvHwndHost");

            // Request events
            LibMpv.mpv_request_event(_mpvHandle, LibMpv.MPV_EVENT_LOG_MESSAGE, 1);
            LibMpv.mpv_request_event(_mpvHandle, LibMpv.MPV_EVENT_START_FILE, 1);
            LibMpv.mpv_request_event(_mpvHandle, LibMpv.MPV_EVENT_FILE_LOADED, 1);
            LibMpv.mpv_request_event(_mpvHandle, LibMpv.MPV_EVENT_END_FILE, 1);
            LibMpv.mpv_request_event(_mpvHandle, LibMpv.MPV_EVENT_VIDEO_RECONFIG, 1);

            // keep-open=yes ??태??서 EOF??감????기 ??해 eof-reached ??로??티 감시
            LibMpv.mpv_observe_property(_mpvHandle, 1, "eof-reached", LibMpv.MPV_FORMAT_FLAG);

            _wakeupCallback = OnWakeup;
            LibMpv.mpv_set_wakeup_callback(_mpvHandle, _wakeupCallback, IntPtr.Zero);

            _eventThread = new Thread(EventLoop) { IsBackground = true, Name = "MpvEventLoop" };
            _eventThread.Start();

            if (_pendingStretch.HasValue) SetStretch(_pendingStretch.Value);
            if (_pendingVolume.HasValue) SetVolume(_pendingVolume.Value);
            if (_pendingMute.HasValue) SetMute(_pendingMute.Value);
            if (_pendingPause.HasValue) SetPause(_pendingPause.Value);

            // Load pending file if any
            if (!string.IsNullOrEmpty(_pendingFilePath))
            {
                string path = _pendingFilePath;
                _pendingFilePath = null;
                Trace.WriteLine($"MpvHwndHost: Loading pending file {path}", "MpvHwndHost");
                Load(path);
            }
        }

        private void OnWakeup(IntPtr d)
        {
            try { _wakeupEvent.Set(); } catch { }
        }

        private void EventLoop()
        {
            while (!_disposed)
            {
                _wakeupEvent.WaitOne(500);

                if (_disposed || _mpvHandle == IntPtr.Zero) break;

                while (true)
                {
                    IntPtr evPtr = LibMpv.mpv_wait_event(_mpvHandle, 0);
                    if (evPtr == IntPtr.Zero) break;

                    var ev = Marshal.PtrToStructure<LibMpv.mpv_event>(evPtr);
                    if (ev.event_id == LibMpv.MPV_EVENT_NONE) break;

                    switch (ev.event_id)
                    {
                        case LibMpv.MPV_EVENT_LOG_MESSAGE:
                            if (ev.data != IntPtr.Zero)
                            {
                                var log = Marshal.PtrToStructure<mpv_event_log_message>(ev.data);
                                string prefix = Marshal.PtrToStringAnsi(log.prefix);
                                string text = Marshal.PtrToStringAnsi(log.text);
                                Trace.WriteLine($"[mpv:{prefix}] {text.Trim()}", "MpvHwndHost");
                            }
                            break;

                        case LibMpv.MPV_EVENT_START_FILE:
                            Trace.WriteLine("MpvHwndHost: Event START_FILE", "MpvHwndHost");
                            break;

                        case LibMpv.MPV_EVENT_FILE_LOADED:
                            Trace.WriteLine("MpvHwndHost: Event FILE_LOADED", "MpvHwndHost");
                            _dispatcher.BeginInvoke(new System.Action(() => FileLoaded?.Invoke()));
                            break;

                        case LibMpv.MPV_EVENT_VIDEO_RECONFIG:
                            Trace.WriteLine("MpvHwndHost: Event VIDEO_RECONFIG", "MpvHwndHost");
                            _dispatcher.BeginInvoke(new System.Action(() =>
                            {
                                SyncHostWindowSize("VIDEO_RECONFIG");

                                if (!_videoReady)
                                {
                                    _videoReady = true;
                                    // ??VIDEO_RECONFIG: VO 구성 ??료. 창을 ??시????색 ??래??방??
                                    if (_useWindowTimingGuard && _hwndHost != IntPtr.Zero)
                                        ShowWindow(_hwndHost, SW_SHOWNA);
                                }

                                ApplyNativeZOrder();
                                VideoReconfig?.Invoke();
                            }));
                            break;

                        case LibMpv.MPV_EVENT_PROPERTY_CHANGE:
                            if (ev.data != IntPtr.Zero)
                            {
                                var prop = Marshal.PtrToStructure<LibMpv.mpv_event_property>(ev.data);
                                if (prop.name == "eof-reached" && prop.format == LibMpv.MPV_FORMAT_FLAG && prop.data != IntPtr.Zero)
                                {
                                    int flag = Marshal.ReadInt32(prop.data);
                                    Trace.WriteLine($"MpvHwndHost: eof-reached={flag}", "MpvHwndHost");
                                    if (flag == 1)
                                    {
                                        _dispatcher.BeginInvoke(new System.Action(() =>
                                            EndFile?.Invoke(LibMpv.MPV_END_FILE_REASON_EOF)));
                                    }
                                }
                            }
                            break;

                        case LibMpv.MPV_EVENT_END_FILE:
                            if (ev.data != IntPtr.Zero)
                            {
                                var endData = Marshal.PtrToStructure<LibMpv.mpv_event_end_file>(ev.data);
                                int reason = endData.reason;
                                int error  = endData.error;
                                Trace.WriteLine($"MpvHwndHost: Event END_FILE. reason={reason}, error={error}", "MpvHwndHost");

                                _dispatcher.BeginInvoke(new System.Action(() =>
                                {
                                    if (reason == LibMpv.MPV_END_FILE_REASON_ERROR)
                                        MediaFailed?.Invoke("mpv end_file error: " + error);
                                    else
                                        EndFile?.Invoke(reason);
                                }));
                            }
                            break;

                        case LibMpv.MPV_EVENT_SHUTDOWN:
                            return;

                        default:
                            Trace.WriteLine($"MpvHwndHost: Event id={ev.event_id} (unhandled)", "MpvHwndHost");
                            break;
                    }
                }
            }
        }

        public void Load(string filePath)
        {
            if (_mpvHandle == IntPtr.Zero)
            {
                Trace.WriteLine($"MpvHwndHost: Load called but handle null. Storing pending path: {filePath}", "MpvHwndHost");
                _pendingFilePath = filePath;
                return;
            }

            _videoReady = false;
            // ??음 ??일 로드 ??창을 ??시 ??겨 ??색 ??래??방?? (VIDEO_RECONFIG??서 ??시 ??시??
            if (_useWindowTimingGuard && _hwndHost != IntPtr.Zero)
                ShowWindow(_hwndHost, SW_HIDE);
            SyncHostWindowSize("Load");
            ApplyNativeZOrder();
            Trace.WriteLine($"MpvHwndHost: Load {filePath}", "MpvHwndHost");
            LibMpv.Command(_mpvHandle, "loadfile", filePath);
        }

        public void SetNativeZIndex(int nativeZIndex)
        {
            _nativeZIndex = nativeZIndex;
            ApplyNativeZOrder();
        }

        public void ApplyNativeZOrder()
        {
            NativeZOrderManager.ApplyAll(_dispatcher);
        }

        private void RegisterHost()
        {
            _hostSequence = NativeZOrderManager.GetNextSequence();
            NativeZOrderManager.Register(this);
        }

        private void UnregisterHost()
        {
            NativeZOrderManager.Unregister(this);
        }

        private void SyncHostWindowSize(string reason)
        {
            if (_hwndHost == IntPtr.Zero || _disposed)
                return;

            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.BeginInvoke(new System.Action(() => SyncHostWindowSize(reason)));
                return;
            }

            int w = (int)Math.Max(1, Math.Round(ActualWidth > 0 ? ActualWidth : Width));
            int h = (int)Math.Max(1, Math.Round(ActualHeight > 0 ? ActualHeight : Height));

            bool ok = SetWindowPos(
                _hwndHost,
                IntPtr.Zero,
                0,
                0,
                w,
                h,
                SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER);

            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                Trace.WriteLine($"MpvHwndHost: SyncHostWindowSize({reason}) failed. size={w}x{h}, error={err}", "MpvHwndHost");
                return;
            }

            InvalidateRect(_hwndHost, IntPtr.Zero, false);
            Trace.WriteLine($"MpvHwndHost: SyncHostWindowSize({reason}) size={w}x{h}", "MpvHwndHost");
        }

        public void SeekAbsolute(double seconds)
        {
            if (_mpvHandle == IntPtr.Zero)
            {
                Trace.WriteLine($"MpvHwndHost: SeekAbsolute({seconds}) ??handle is null, skipping", "MpvHwndHost");
                return;
            }
            int rc = LibMpv.Command(_mpvHandle, "seek", seconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture), "absolute");
            Trace.WriteLine($"MpvHwndHost: SeekAbsolute({seconds}) rc={rc}", "MpvHwndHost");
        }

        public void SeekToStart()
        {
            Trace.WriteLine("MpvHwndHost: SeekToStart()", "MpvHwndHost");
            SeekAbsolute(0);
        }

        public void Play()
        {
            if (_mpvHandle == IntPtr.Zero) return;
            int rc = LibMpv.Command(_mpvHandle, "set", "pause", "no");
            Trace.WriteLine($"MpvHwndHost: Play() rc={rc}", "MpvHwndHost");
        }

        public void SetPause(bool paused)
        {
            if (_mpvHandle == IntPtr.Zero)
            {
                _pendingPause = paused;
                return;
            }

            int rc = LibMpv.Command(_mpvHandle, "set", "pause", paused ? "yes" : "no");
            Trace.WriteLine($"MpvHwndHost: SetPause({paused}) rc={rc}", "MpvHwndHost");
        }

        public void SetVolume(int volume)
        {
            if (_mpvHandle == IntPtr.Zero)
            {
                _pendingVolume = volume;
                return;
            }
            double v = volume;
            LibMpv.mpv_set_property_double(_mpvHandle, "volume", LibMpv.MPV_FORMAT_DOUBLE, ref v);
        }

        public void SetMute(bool muted)
        {
            if (_mpvHandle == IntPtr.Zero)
            {
                _pendingMute = muted;
                return;
            }
            LibMpv.mpv_set_property_string(_mpvHandle, "mute", muted ? "yes" : "no");
        }

        public void SetStretch(bool stretch)
        {
            if (_mpvHandle == IntPtr.Zero)
            {
                _pendingStretch = stretch;
                return;
            }
            if (stretch)
            {
                LibMpv.mpv_set_property_string(_mpvHandle, "video-unscaled", "no");
                LibMpv.mpv_set_property_string(_mpvHandle, "keepaspect", "no");
            }
            else
            {
                LibMpv.mpv_set_property_string(_mpvHandle, "video-unscaled", "no");
                LibMpv.mpv_set_property_string(_mpvHandle, "keepaspect", "yes");
            }
        }

        public double GetDuration()
        {
            if (_mpvHandle == IntPtr.Zero) return 0;
            double d = 0;
            LibMpv.mpv_get_property(_mpvHandle, "duration", LibMpv.MPV_FORMAT_DOUBLE, ref d);
            return d;
        }

        private void Shutdown()
        {
            if (_disposed) return;
            _disposed = true;
            UnregisterHost();

            _wakeupEvent.Set();

            IntPtr handle = Interlocked.Exchange(ref _mpvHandle, IntPtr.Zero);
            if (handle != IntPtr.Zero)
            {
                try { LibMpv.mpv_terminate_destroy(handle); }
                catch (Exception ex)
                {
                    Trace.WriteLine(new LogMessage("MpvHwndHost", "Shutdown: " + ex.Message), LogType.Error.ToString());
                }
            }

            _eventThread?.Join(2000);
        }
    }
}

