using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace XiboClient.Rendering
{
    /// <summary>
    /// libmpv(HwndHost) Í∏∞Î∞ò ?¥Î?ÏßÄ ?åÎçî??
    /// mpv Î™®Îìú?êÏÑú image ?Ä?ÖÎèÑ ?ôÏùº??Win32 Í≤ΩÎ°úÎ°??åÎçîÎßÅÌïú??
    /// </summary>
    class ImageHwnd : Media
    {
        private MpvHwndHost _MpvHwndHost;
        private readonly string _filePath;
        private readonly bool _stretch;
        private readonly int _volume;
        private readonly bool _muted;

        public ImageHwnd(MediaOptions options) : base(options)
        {
            _filePath = Uri.UnescapeDataString(options.uri).Replace('+', ' ');
            _stretch = options.Dictionary.Get("scaleType", "aspect").ToLowerInvariant() == "stretch";
            _volume = options.Dictionary.Get("volume", 100);
            _muted = options.Dictionary.Get("mute", "0") == "1";
        }

        public override void RenderMedia(double position)
        {
            Uri uri = new Uri(_filePath);
            if (uri.IsFile && !File.Exists(_filePath))
            {
                Trace.WriteLine(new LogMessage("ImageHwnd", "RenderMedia: " + this.Id + ", File " + _filePath + " not found."));
                throw new FileNotFoundException();
            }

            _MpvHwndHost = new MpvHwndHost
            {
                Width = Width,
                Height = Height,
                Visibility = Visibility.Visible
            };
            _MpvHwndHost.SetNativeZIndex(NativeZIndex);

            try
            {
                MediaScene.Children.Add(_MpvHwndHost);

                _MpvHwndHost.SetStretch(_stretch);
                _MpvHwndHost.SetVolume(_volume);
                _MpvHwndHost.SetMute(_muted);
                // Still image should stay visible for widget duration.
                _MpvHwndHost.SetPause(true);
                _MpvHwndHost.Load(_filePath);

                Trace.WriteLine(new LogMessage("ImageHwnd", "RenderMedia: " + this.Id + " loaded."), LogType.Audit.ToString());

                base.RenderMedia(position);
            }
            catch (Exception ex)
            {
                Trace.WriteLine(new LogMessage("ImageHwnd", "RenderMedia: " + ex.Message), LogType.Error.ToString());
                throw;
            }
        }

        public override void Stopped()
        {
            Trace.WriteLine(new LogMessage("ImageHwnd", "Stopped: " + this.Id), LogType.Audit.ToString());

            if (_MpvHwndHost != null)
            {
                _MpvHwndHost.Dispose();
                _MpvHwndHost = null;
            }

            base.Stopped();
        }

        public override void ApplyNativeZOrder()
        {
            _MpvHwndHost?.SetNativeZIndex(NativeZIndex);
        }
    }
}

