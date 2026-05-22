using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;

namespace XiboClient.Rendering
{
    public interface INativeZIndexHost
    {
        IntPtr Hwnd { get; }
        int NativeZIndex { get; }
        long HostSequenceCounter { get; }
        bool IsDisposed { get; }
    }

    public static class NativeZOrderManager
    {
        private static readonly object Lock = new object();
        private static readonly List<INativeZIndexHost> Registry = new List<INativeZIndexHost>();
        private static long _sequenceCounter = 0;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        public static long GetNextSequence()
        {
            return Interlocked.Increment(ref _sequenceCounter);
        }

        public static void Register(INativeZIndexHost host)
        {
            lock (Lock)
            {
                if (!Registry.Contains(host))
                    Registry.Add(host);
            }
        }

        public static void Unregister(INativeZIndexHost host)
        {
            lock (Lock)
            {
                Registry.Remove(host);
            }
        }

        public static void ApplyAll(Dispatcher dispatcher = null)
        {
            if (dispatcher != null)
            {
                dispatcher.BeginInvoke(new System.Action(() => ApplyAll(null)), DispatcherPriority.Render);
                return;
            }

            List<INativeZIndexHost> orderedHosts;
            lock (Lock)
            {
                Registry.RemoveAll(host => host == null || host.IsDisposed);
                Registry.Sort((left, right) =>
                {
                    int z = right.NativeZIndex.CompareTo(left.NativeZIndex);
                    return z != 0 ? z : right.HostSequenceCounter.CompareTo(left.HostSequenceCounter);
                });
                orderedHosts = new List<INativeZIndexHost>(Registry);
            }

            IntPtr insertAfter = IntPtr.Zero; // HWND_TOP
            foreach (var host in orderedHosts)
            {
                IntPtr hwnd = IntPtr.Zero;
                try { hwnd = host.Hwnd; } catch { }
                if (hwnd == IntPtr.Zero) continue;

                try
                {
                    SetWindowPos(hwnd, insertAfter, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0200);
                    insertAfter = hwnd;
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Error applying Native Z-Order: {ex}");
                }
            }
        }
    }
}
