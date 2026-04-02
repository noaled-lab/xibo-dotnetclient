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
using System.Runtime.InteropServices;
using System.Text;

namespace XiboClient.Rendering
{
    /// <summary>
    /// libmpv-2.dll의 P/Invoke 래퍼.
    /// mpv_create / mpv_initialize / mpv_command 등 핵심 API를 노출하고
    /// 커맨드 배열 메모리 관리를 위한 AllocCommand / FreeCommand / Command 헬퍼를 제공한다.
    /// </summary>
    internal static class LibMpv
    {
        private const string DllName = "libmpv-2.dll";

        // mpv_format constants
        public const int MPV_FORMAT_NONE   = 0;
        public const int MPV_FORMAT_STRING = 1;
        public const int MPV_FORMAT_FLAG   = 3;
        public const int MPV_FORMAT_INT64  = 4;
        public const int MPV_FORMAT_DOUBLE = 5;

        // mpv_event_id constants
        public const int MPV_EVENT_NONE               = 0;
        public const int MPV_EVENT_SHUTDOWN            = 1;
        public const int MPV_EVENT_LOG_MESSAGE         = 2;
        public const int MPV_EVENT_START_FILE          = 6;
        public const int MPV_EVENT_END_FILE            = 7;
        public const int MPV_EVENT_FILE_LOADED         = 8;
        public const int MPV_EVENT_VIDEO_RECONFIG      = 17;
        public const int MPV_EVENT_PROPERTY_CHANGE     = 22;

        // mpv_end_file_reason
        public const int MPV_END_FILE_REASON_EOF      = 0;
        public const int MPV_END_FILE_REASON_STOP     = 2;
        public const int MPV_END_FILE_REASON_ERROR    = 4;

        [StructLayout(LayoutKind.Sequential)]
        public struct mpv_event
        {
            public int event_id;
            public int error;
            public ulong reply_userdata;
            public IntPtr data;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct mpv_event_end_file
        {
            public int reason;
            public int error;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct mpv_event_property {
            [MarshalAs(UnmanagedType.LPStr)]
            public string name;
            public int format;
            public IntPtr data;
        }

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr mpv_create();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_initialize(IntPtr ctx);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void mpv_destroy(IntPtr ctx);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void mpv_terminate_destroy(IntPtr ctx);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int mpv_set_option_string(IntPtr ctx, string name, string data);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_set_option(IntPtr ctx, [MarshalAs(UnmanagedType.LPStr)] string name, int format, ref long data);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_command(IntPtr ctx, IntPtr args);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int mpv_set_property_string(IntPtr ctx, string name, string data);

        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_set_property(IntPtr ctx, [MarshalAs(UnmanagedType.LPStr)] string name, int format, ref long data);

        [DllImport("libmpv-2.dll", EntryPoint = "mpv_set_property", CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_set_property_double(IntPtr ctx, [MarshalAs(UnmanagedType.LPStr)] string name, int format, ref double data);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_get_property(IntPtr ctx, [MarshalAs(UnmanagedType.LPStr)] string name, int format, ref double data);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_get_property_long(IntPtr ctx, [MarshalAs(UnmanagedType.LPStr)] string name, int format, ref long data);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr mpv_get_property_string(IntPtr ctx, [MarshalAs(UnmanagedType.LPStr)] string name);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void mpv_free(IntPtr data);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr mpv_wait_event(IntPtr ctx, double timeout);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void mpv_set_wakeup_callback(IntPtr ctx, MpvWakeupCallback cb, IntPtr d);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_request_event(IntPtr ctx, int event_id, int enable);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_observe_property(IntPtr ctx, ulong reply_userdata, [MarshalAs(UnmanagedType.LPStr)] string name, int format);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void MpvWakeupCallback(IntPtr d);

        /// <summary>
        /// mpv_command 호출을 위해 NULL 종료 UTF-8 문자열 배열을 비관리 메모리에 할당.
        /// 사용 후 반드시 FreeCommand()로 해제해야 한다.
        /// </summary>
        public static IntPtr AllocCommand(params string[] args)
        {
            // array of IntPtr, last one is IntPtr.Zero
            IntPtr[] ptrs = new IntPtr[args.Length + 1];
            for (int i = 0; i < args.Length; i++)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(args[i] + "\0");
                ptrs[i] = Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, ptrs[i], bytes.Length);
            }
            ptrs[args.Length] = IntPtr.Zero;

            IntPtr arr = Marshal.AllocHGlobal(IntPtr.Size * ptrs.Length);
            Marshal.Copy(ptrs, 0, arr, ptrs.Length);
            return arr;
        }

        /// <summary>
        /// AllocCommand()로 할당된 비관리 메모리를 해제한다.
        /// </summary>
        public static void FreeCommand(IntPtr arr, int argCount)
        {
            if (arr == IntPtr.Zero) return;
            IntPtr[] ptrs = new IntPtr[argCount + 1];
            Marshal.Copy(arr, ptrs, 0, argCount + 1);
            for (int i = 0; i < argCount; i++)
            {
                if (ptrs[i] != IntPtr.Zero)
                    Marshal.FreeHGlobal(ptrs[i]);
            }
            Marshal.FreeHGlobal(arr);
        }

        /// <summary>
        /// mpv 커맨드를 안전하게 실행. AllocCommand/FreeCommand를 내부에서 처리한다.
        /// </summary>
        public static int Command(IntPtr ctx, params string[] args)
        {
            IntPtr cmd = AllocCommand(args);
            try
            {
                return mpv_command(ctx, cmd);
            }
            finally
            {
                FreeCommand(cmd, args.Length);
            }
        }
    }
}
