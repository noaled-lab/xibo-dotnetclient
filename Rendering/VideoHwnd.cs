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
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace XiboClient.Rendering
{
    /// <summary>
    /// libmpv P/InvokeÎ•??¥Ïö©???ôÏòÅ???åÎçî??
    /// Video ?¥Îûò?§Ïùò ?µÏÖò ?åÏã± Î∞?Í∞êÏãú ?Ä?¥Î®∏ Î°úÏßÅ???ÅÏÜç?òÍ≥†
    /// RenderMedia / Stopped Îß??§Î≤Ñ?ºÏù¥?úÌïú??
    /// </summary>
    class VideoHwnd : Media
    {
        private MpvHwndHost _MpvHwndHost;
        private DispatcherTimer _startWatchman;
        private DispatcherTimer _stopWatchman;

        // Video.cs?êÏÑú Í∞Ä?∏Ïò® ?ÑÎìú??(Video.cs ?òÏ†ï???ºÌïòÍ∏??ÑÌï¥ ÏßÅÏ†ë Í¥ÄÎ¶?
        private string _filePath;
        private int _duration;
        private int volume;
        private bool _detectEnd = false;
        private bool isLooping = false;
        private bool _openCalled = false;
        private bool _stopped = false;
        private double _position;

        protected bool ShouldBeVisible { get; set; }
        protected bool Muted { get; set; }
        protected bool Stretch { get; set; }

        public VideoHwnd(MediaOptions options) : base(options)
        {
            this.ShouldBeVisible = true;
            _filePath = Uri.UnescapeDataString(options.uri).Replace('+', ' ');
            _duration = options.duration;
            this.volume = options.Dictionary.Get("volume", 100);

            string muteOption = options.Dictionary.Get("mute");
            if (!string.IsNullOrEmpty(muteOption))
            {
                this.Muted = muteOption == "1";
            }

            this.isLooping = (options.Dictionary.Get("loop", "0") == "1" && _duration != 0);
            Stretch = options.Dictionary.Get("scaleType", "aspect").ToLowerInvariant() == "stretch";
        }

        // ----------------------------------------------------------------
        // MpvHwndHost ?¥Î≤§???∏Îì§??
        // ----------------------------------------------------------------

        /// <summary>
        /// ?åÏùº Î°úÎìú ?ÑÎ£å ?¥Î≤§?? MediaOpened ???¥Îãπ?òÎ©∞ seek Î∞?Í∞êÏãú ?Ä?¥Î®∏Î•??§Ï†ï.
        /// Î°úÎìú ?ÑÎ£å ?úÏ†ê??VisibilityÎ•?VisibleÎ°??ÑÌôò???∞ÏÉâ ?åÎûò?úÎ? Î∞©Ï?.
        /// </summary>
        private void MpvHwndHost_FileLoaded()
        {
            Trace.WriteLine(new LogMessage("VideoHwnd", "FileLoaded: " + this.Id + " seek to: " + _position), LogType.Audit.ToString());

            _openCalled = true;

            if (!_detectEnd)
                RestartTimer();

            if (_position > 0)
            {
                _MpvHwndHost?.SeekAbsolute(_position);
                _position = 0;
            }

            var watchmanTtl = TimeSpan.FromSeconds(60);
            if (_duration == 0)
            {
                double naturalDuration = _MpvHwndHost?.GetDuration() ?? 0;
                if (naturalDuration > 0)
                    watchmanTtl = watchmanTtl.Add(TimeSpan.FromSeconds(naturalDuration));
            }
            else
            {
                watchmanTtl = watchmanTtl.Add(TimeSpan.FromSeconds(_duration));
            }

            _stopWatchman = new DispatcherTimer { Interval = watchmanTtl };
            _stopWatchman.Tick += (s, e) =>
            {
                _stopWatchman.Stop();
                LogMessage.Error("VideoHwnd", "FileLoaded", this.Id + " video running past watchman end check.");
                SignalElapsedEvent();
            };
            _stopWatchman.Start();
        }

        /// <summary>
        /// mpv VO Íµ¨ÏÑ± ?ÑÎ£å ?¥Î≤§?? FILE_LOADED ?¥ÌõÑ Î∞úÏÉù?òÎ©∞ ?§Ï†ú Ï≤??ÑÎ†à???åÎçîÎß?ÏßÅÏ†Ñ?¥Îã§.
        /// MpvHwndHost ?¥Î??êÏÑú ?¥Î? WM_PAINTÎ•?Í≤Ä?ïÏúºÎ°?Ï≤òÎ¶¨?òÎ?Î°??¨Í∏∞?úÎäî Î°úÍ∑∏Îß??®Í∏¥??
        /// </summary>
        private void MpvHwndHost_VideoReconfig()
        {
            Trace.WriteLine(new LogMessage("VideoHwnd", "VideoReconfig: " + this.Id), LogType.Audit.ToString());
        }

        /// <summary>
        /// ?¨ÏÉù ?§Ìå® ?¥Î≤§?? Ï∫êÏãú Î∏îÎûôÎ¶¨Ïä§?∏Ïóê Ï∂îÍ??òÍ≥† ÎØ∏Îîî?¥Î? ÎßåÎ£å?úÌÇ®??
        /// </summary>
        private void MpvHwndHost_MediaFailed(string errorMessage)
        {
            Trace.WriteLine(new LogMessage("VideoHwnd", "MediaFailed: " + this.Id + " ??" + errorMessage), LogType.Error.ToString());

            _openCalled = true;

            CacheManager.Instance.AddUnsafeItem(UnsafeItemType.Media, UnsafeFaultCodes.VideoUnexpected,
                LayoutId, FileId, "Video Failed: " + errorMessage, 120);

            IsFailedToPlay = true;
            SignalElapsedEvent();
        }

        /// <summary>
        /// ?åÏùº ?¨ÏÉù Ï¢ÖÎ£å ?¥Î≤§?? Î£®ÌîÑ ?§Ï†ï?¥Î©¥ Ï≤òÏùå?ºÎ°ú seek, ?ÑÎãàÎ©?Expired Ï≤òÎ¶¨.
        /// </summary>
        private void MpvHwndHost_EndFile(int reason)
        {
            Trace.WriteLine(new LogMessage("VideoHwnd", "EndFile: " + this.Id + " reason=" + reason + " looping=" + isLooping + " stopped=" + _stopped), LogType.Audit.ToString());

            if (_stopped) return;

            if (reason == LibMpv.MPV_END_FILE_REASON_EOF)
            {
                if (isLooping)
                {
                    Trace.WriteLine(new LogMessage("VideoHwnd", "EndFile: " + this.Id + " ??looping, SeekToStart + Play"), LogType.Audit.ToString());
                    _MpvHwndHost?.SeekToStart();
                    _MpvHwndHost?.Play();
                }
                else
                    Expired = true;
            }
        }

        // ----------------------------------------------------------------
        // RenderMedia / Stopped ?§Î≤Ñ?ºÏù¥??
        // ----------------------------------------------------------------

        /// <summary>
        /// libmpv Í∏∞Î∞ò ?åÎçîÎß??úÏûë.
        /// MpvHwndHostÎ•??ùÏÑ±?òÍ≥† Ï¥àÍ∏∞?êÎäî Hidden?ºÎ°ú ?§Ï†ï ??FileLoaded ?¥Î≤§?∏Ïóê??VisibleÎ°??ÑÌôò.
        /// Video.RenderMedia()??WPF MediaElement Ï¥àÍ∏∞?îÎ? Í±¥ÎÑà?∞Í∏∞ ?ÑÌï¥ StartRenderBase() ?∏Ï∂ú.
        /// </summary>
        public override void RenderMedia(double position)
        {
            _position = position;

            Uri uri = new Uri(_filePath);
            if (uri.IsFile && !File.Exists(_filePath))
            {
                Trace.WriteLine(new LogMessage("VideoHwnd", "RenderMedia: " + this.Id + ", File " + _filePath + " not found."));
                throw new FileNotFoundException();
            }

            _MpvHwndHost = new MpvHwndHost();_MpvHwndHost.SetNativeZIndex(NativeZIndex);

            if (!ShouldBeVisible)
            {
                _MpvHwndHost.Width = 0;
                _MpvHwndHost.Height = 0;
                _MpvHwndHost.Visibility = Visibility.Hidden;
            }
            else
            {
                _MpvHwndHost.Width = Width;
                _MpvHwndHost.Height = Height;
                _MpvHwndHost.Visibility = Visibility.Visible;
            }

            _MpvHwndHost.FileLoaded   += MpvHwndHost_FileLoaded;
            _MpvHwndHost.VideoReconfig += MpvHwndHost_VideoReconfig;
            _MpvHwndHost.MediaFailed  += MpvHwndHost_MediaFailed;
            _MpvHwndHost.EndFile      += MpvHwndHost_EndFile;

            if (_duration == 0)
            {
                Duration   = 1;
                _detectEnd = true;
            }

            // Render media as normal (starts the timer, shows the form, etc)
            base.RenderMedia(position);

            try
            {
                this.MediaScene.Children.Add(_MpvHwndHost);

                _MpvHwndHost.SetVolume(volume);
                _MpvHwndHost.SetMute(Muted);
                _MpvHwndHost.SetStretch(Stretch);

                _startWatchman = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(ApplicationSettings.Default.VideoStartTimeout)
                };
                _startWatchman.Tick += (s, e) =>
                {
                    _startWatchman?.Stop();
                    if (!_openCalled && !IsFailedToPlay && !_stopped)
                    {
                        LogMessage.Error("VideoHwnd", "RenderMedia", this.Id + " Open not called after " +
                            ApplicationSettings.Default.VideoStartTimeout + " seconds, marking unsafe and expiring.");
                        CacheManager.Instance.AddUnsafeItem(UnsafeItemType.Media, UnsafeFaultCodes.VideoUnexpected,
                            LayoutId, FileId, "Video Failed: Open not called after " + ApplicationSettings.Default.VideoStartTimeout + " seconds", 120);
                        SignalElapsedEvent();
                    }
                };
                _startWatchman.Start();

                _MpvHwndHost.Load(_filePath);

                Trace.WriteLine(new LogMessage("VideoHwnd", "RenderMedia: " + this.Id + " loaded, detectEnd=" + _detectEnd), LogType.Audit.ToString());
            }
            catch (Exception ex)
            {
                Trace.WriteLine(new LogMessage("VideoHwnd", "RenderMedia: " + ex.Message), LogType.Error.ToString());
                throw;
            }
        }

        // ----------------------------------------------------------------
        // Stopped override
        // ----------------------------------------------------------------

        /// <summary>
        /// libmpv Î¶¨ÏÜå???¥Ï†ú.
        /// Video.Stopped()??mediaElementÎ•?ÏßÅÏ†ë Ï∞∏Ï°∞?òÎ?Î°??∏Ï∂ú?òÏ? ?äÍ≥†
        /// StopBase()Î°?Media.Stopped()Îß??∏Ï∂ú?úÎã§.
        /// </summary>
        public override void Stopped()
        {
            Trace.WriteLine(new LogMessage("VideoHwnd", "Stopped: " + this.Id), LogType.Audit.ToString());

            _stopped = true;

            if (_startWatchman != null)
            {
                _startWatchman.Stop();
                _startWatchman = null;
            }

            if (_stopWatchman != null)
            {
                _stopWatchman.Stop();
                _stopWatchman = null;
            }

            if (_MpvHwndHost != null)
            {
                _MpvHwndHost.FileLoaded   -= MpvHwndHost_FileLoaded;
                _MpvHwndHost.VideoReconfig -= MpvHwndHost_VideoReconfig;
                _MpvHwndHost.MediaFailed  -= MpvHwndHost_MediaFailed;
                _MpvHwndHost.EndFile      -= MpvHwndHost_EndFile;
                _MpvHwndHost.Dispose();
                _MpvHwndHost = null;
            }

            // Call Media.Stopped directly
            base.Stopped();
        }

        public override void ApplyNativeZOrder()
        {
            _MpvHwndHost?.SetNativeZIndex(NativeZIndex);
        }

        /// <summary>
        /// Override the timer tick to prevent premature expiration when detecting end of file.
        /// </summary>
        protected override void timer_Tick(object sender, EventArgs e)
        {
            if (!_detectEnd || Expired)
            {
                base.timer_Tick(sender, e);
            }
        }
    }
}
