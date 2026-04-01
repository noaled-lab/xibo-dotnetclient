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
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace XiboClient.Rendering
{
    /// <summary>
    /// WPF HwndHost를 상속해 mpv 플레이어를 Win32 자식 창으로 임베드하는 컨트롤.
    /// WM_ERASEBKGND를 검정으로 처리해 창 초기화 시 흰색 플래시를 방지한다.
    /// </summary>
    internal class MpvHost : HwndHost
    {
        private const int WS_CHILD    = 0x40000000;
        private const int WS_VISIBLE  = 0x10000000;
        private const int WS_CLIPCHILDREN = 0x02000000;
        private const int WS_CLIPSIBLINGS = 0x04000000;

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
        private const int SW_HIDE = 0;

        // 흰색/회색 플래시 방지
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

        // VIDEO_RECONFIG 전까지 WM_PAINT를 검정으로 처리해 회색 플래시 방지
        private volatile bool _videoReady = false;

        private IntPtr _mpvHandle = IntPtr.Zero;
        private IntPtr _hwndHost = IntPtr.Zero;
        private Thread _eventThread;
        private volatile bool _disposed = false;
        private readonly Dispatcher _dispatcher;

        private LibMpv.MpvWakeupCallback _wakeupCallback;
        private readonly AutoResetEvent _wakeupEvent = new AutoResetEvent(false);

        // BuildWindowCore 이전에 Load/SetVolume 등이 호출된 경우를 위한 대기 값
        private string _pendingFilePath;
        private bool? _pendingStretch;
        private int? _pendingVolume;
        private bool? _pendingMute;

        [StructLayout(LayoutKind.Sequential)]
        struct mpv_event_log_message {
            public IntPtr prefix;
            public IntPtr level;
            public IntPtr text;
            public int log_level;
        }

        public MpvHost()
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            Trace.WriteLine("MpvHost: Constructor", "MpvHost");
        }

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            Trace.WriteLine($"MpvHost: BuildWindowCore. Parent: {hwndParent.Handle}, Size: {Width}x{Height}", "MpvHost");

            int w = (int)Math.Max(1, Width);
            int h = (int)Math.Max(1, Height);

            const int SS_BLACKRECT = 0x0004;

            // WS_VISIBLE 없이 생성 — mpv GPU 렌더러가 VO 초기화 완료(VIDEO_RECONFIG) 후 ShowWindow로 표시
            _hwndHost = CreateWindowEx(
                0, "STATIC", "",
                WS_CHILD | WS_CLIPCHILDREN | WS_CLIPSIBLINGS | SS_BLACKRECT,
                0, 0, w, h,
                hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

            if (_hwndHost == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                Trace.WriteLine($"MpvHost: CreateWindowEx failed with error {err}", "MpvHost");
                throw new InvalidOperationException("Failed to create host window for mpv. Error: " + err);
            }

            InitMpv(_hwndHost);

            return new HandleRef(this, _hwndHost);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            Trace.WriteLine("MpvHost: DestroyWindowCore", "MpvHost");
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

            // VIDEO_RECONFIG 전까지 WM_PAINT를 검정으로 처리 (mpv GPU 렌더러 초기화 중 회색 플래시 방지)
            if (msg == WM_PAINT && !_videoReady)
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
        /// mpv 인스턴스를 초기화하고 이벤트 루프 스레드를 시작한다.
        /// BuildWindowCore에서 호출되므로 이 시점에 hwnd가 유효하다.
        /// </summary>
        private void InitMpv(IntPtr hwnd)
        {
            Trace.WriteLine("MpvHost: InitMpv starting", "MpvHost");

            _mpvHandle = LibMpv.mpv_create();
            if (_mpvHandle == IntPtr.Zero)
                throw new InvalidOperationException("mpv_create() returned NULL.");

            // 콘솔 출력 비활성화 / 로그는 Trace로만 수집
            LibMpv.mpv_set_option_string(_mpvHandle, "terminal", "no");
            LibMpv.mpv_set_option_string(_mpvHandle, "msg-level", "all=v");

            // 호스트 창 핸들을 mpv에 전달해 해당 창 안에 렌더링
            long wid = hwnd.ToInt64();
            LibMpv.mpv_set_option(_mpvHandle, "wid", LibMpv.MPV_FORMAT_INT64, ref wid);

            // 키오스크/사이니지용: OSD·입력 비활성화
            // keep-open=yes: EOF 후에도 마지막 프레임을 유지해 회색 창 방지.
            // SeekToStart() + Play()로 루프를 구현할 수 있다.
            LibMpv.mpv_set_option_string(_mpvHandle, "keep-open", "yes");
            LibMpv.mpv_set_option_string(_mpvHandle, "osc", "no");
            LibMpv.mpv_set_option_string(_mpvHandle, "osd-level", "0");
            LibMpv.mpv_set_option_string(_mpvHandle, "input-default-bindings", "no");
            LibMpv.mpv_set_option_string(_mpvHandle, "input-vo-keyboard", "no");

            // GPU 우선, 실패 시 direct3d로 폴백 / 하드웨어 디코딩 자동 선택
            LibMpv.mpv_set_option_string(_mpvHandle, "vo", "gpu,direct3d");
            LibMpv.mpv_set_option_string(_mpvHandle, "hwdec", "auto-safe");
            
            // mpv 렌더러 배경색을 강제로 검정색 지정 (흰색 화면 깜빡임 방지용)
            LibMpv.mpv_set_option_string(_mpvHandle, "background", "#000000");

            int rc = LibMpv.mpv_initialize(_mpvHandle);
            if (rc < 0)
            {
                Trace.WriteLine($"MpvHost: mpv_initialize failed: {rc}", "MpvHost");
                throw new InvalidOperationException("mpv_initialize() failed: " + rc);
            }

            Trace.WriteLine("MpvHost: mpv_initialize success", "MpvHost");

            // Request events
            LibMpv.mpv_request_event(_mpvHandle, LibMpv.MPV_EVENT_LOG_MESSAGE, 1);
            LibMpv.mpv_request_event(_mpvHandle, LibMpv.MPV_EVENT_START_FILE, 1);
            LibMpv.mpv_request_event(_mpvHandle, LibMpv.MPV_EVENT_FILE_LOADED, 1);
            LibMpv.mpv_request_event(_mpvHandle, LibMpv.MPV_EVENT_END_FILE, 1);
            LibMpv.mpv_request_event(_mpvHandle, LibMpv.MPV_EVENT_VIDEO_RECONFIG, 1);

            // keep-open=yes 상태에서 EOF를 감지하기 위해 eof-reached 프로퍼티 감시
            LibMpv.mpv_observe_property(_mpvHandle, 1, "eof-reached", LibMpv.MPV_FORMAT_FLAG);

            _wakeupCallback = OnWakeup;
            LibMpv.mpv_set_wakeup_callback(_mpvHandle, _wakeupCallback, IntPtr.Zero);

            _eventThread = new Thread(EventLoop) { IsBackground = true, Name = "MpvEventLoop" };
            _eventThread.Start();

            if (_pendingStretch.HasValue) SetStretch(_pendingStretch.Value);
            if (_pendingVolume.HasValue) SetVolume(_pendingVolume.Value);
            if (_pendingMute.HasValue) SetMute(_pendingMute.Value);

            // Load pending file if any
            if (!string.IsNullOrEmpty(_pendingFilePath))
            {
                string path = _pendingFilePath;
                _pendingFilePath = null;
                Trace.WriteLine($"MpvHost: Loading pending file {path}", "MpvHost");
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
                                Trace.WriteLine($"[mpv:{prefix}] {text.Trim()}", "MpvHost");
                            }
                            break;

                        case LibMpv.MPV_EVENT_START_FILE:
                            Trace.WriteLine("MpvHost: Event START_FILE", "MpvHost");
                            break;

                        case LibMpv.MPV_EVENT_FILE_LOADED:
                            Trace.WriteLine("MpvHost: Event FILE_LOADED", "MpvHost");
                            _dispatcher.BeginInvoke(new System.Action(() => FileLoaded?.Invoke()));
                            break;

                        case LibMpv.MPV_EVENT_VIDEO_RECONFIG:
                            Trace.WriteLine("MpvHost: Event VIDEO_RECONFIG", "MpvHost");
                            if (!_videoReady)
                            {
                                _videoReady = true;
                                // 첫 VIDEO_RECONFIG: VO 구성 완료. 창을 표시해 회색 플래시 방지
                                if (_hwndHost != IntPtr.Zero)
                                    ShowWindow(_hwndHost, SW_SHOW);
                            }
                            _dispatcher.BeginInvoke(new System.Action(() => VideoReconfig?.Invoke()));
                            break;

                        case LibMpv.MPV_EVENT_PROPERTY_CHANGE:
                            if (ev.data != IntPtr.Zero)
                            {
                                var prop = Marshal.PtrToStructure<LibMpv.mpv_event_property>(ev.data);
                                if (prop.name == "eof-reached" && prop.format == LibMpv.MPV_FORMAT_FLAG && prop.data != IntPtr.Zero)
                                {
                                    int flag = Marshal.ReadInt32(prop.data);
                                    Trace.WriteLine($"MpvHost: eof-reached={flag}", "MpvHost");
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
                                Trace.WriteLine($"MpvHost: Event END_FILE. reason={reason}, error={error}", "MpvHost");

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
                            Trace.WriteLine($"MpvHost: Event id={ev.event_id} (unhandled)", "MpvHost");
                            break;
                    }
                }
            }
        }

        public void Load(string filePath)
        {
            if (_mpvHandle == IntPtr.Zero)
            {
                Trace.WriteLine($"MpvHost: Load called but handle null. Storing pending path: {filePath}", "MpvHost");
                _pendingFilePath = filePath;
                return;
            }

            _videoReady = false;
            // 다음 파일 로드 시 창을 다시 숨겨 회색 플래시 방지 (VIDEO_RECONFIG에서 다시 표시됨)
            if (_hwndHost != IntPtr.Zero)
                ShowWindow(_hwndHost, SW_HIDE);
            Trace.WriteLine($"MpvHost: Load {filePath}", "MpvHost");
            LibMpv.Command(_mpvHandle, "loadfile", filePath);
        }

        public void SeekAbsolute(double seconds)
        {
            if (_mpvHandle == IntPtr.Zero)
            {
                Trace.WriteLine($"MpvHost: SeekAbsolute({seconds}) – handle is null, skipping", "MpvHost");
                return;
            }
            int rc = LibMpv.Command(_mpvHandle, "seek", seconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture), "absolute");
            Trace.WriteLine($"MpvHost: SeekAbsolute({seconds}) rc={rc}", "MpvHost");
        }

        public void SeekToStart()
        {
            Trace.WriteLine("MpvHost: SeekToStart()", "MpvHost");
            SeekAbsolute(0);
        }

        public void Play()
        {
            if (_mpvHandle == IntPtr.Zero) return;
            int rc = LibMpv.Command(_mpvHandle, "set", "pause", "no");
            Trace.WriteLine($"MpvHost: Play() rc={rc}", "MpvHost");
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

            _wakeupEvent.Set();

            IntPtr handle = Interlocked.Exchange(ref _mpvHandle, IntPtr.Zero);
            if (handle != IntPtr.Zero)
            {
                try { LibMpv.mpv_terminate_destroy(handle); }
                catch (Exception ex)
                {
                    Trace.WriteLine(new LogMessage("MpvHost", "Shutdown: " + ex.Message), LogType.Error.ToString());
                }
            }

            _eventThread?.Join(2000);
        }
    }
}
