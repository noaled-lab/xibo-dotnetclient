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
    /// libmpv P/Invoke를 이용한 동영상 렌더러.
    /// Video 클래스의 옵션 파싱 및 감시 타이머 로직을 상속하고
    /// RenderMedia / Stopped 만 오버라이드한다.
    /// </summary>
    class VideoMpv : Media
    {
        private MpvHost _mpvHost;
        private DispatcherTimer _startWatchman;
        private DispatcherTimer _stopWatchman;

        // Video.cs에서 가져온 필드들 (Video.cs 수정을 피하기 위해 직접 관리)
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

        public VideoMpv(MediaOptions options) : base(options)
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
        // MpvHost 이벤트 핸들러
        // ----------------------------------------------------------------

        /// <summary>
        /// 파일 로드 완료 이벤트. MediaOpened 에 해당하며 seek 및 감시 타이머를 설정.
        /// 로드 완료 시점에 Visibility를 Visible로 전환해 흰색 플래시를 방지.
        /// </summary>
        private void MpvHost_FileLoaded()
        {
            Trace.WriteLine(new LogMessage("VideoMpv", "FileLoaded: " + this.Id + " seek to: " + _position), LogType.Audit.ToString());

            _openCalled = true;

            if (!_detectEnd)
                RestartTimer();

            if (_position > 0)
            {
                _mpvHost?.SeekAbsolute(_position);
                _position = 0;
            }

            var watchmanTtl = TimeSpan.FromSeconds(60);
            if (_duration == 0)
            {
                double naturalDuration = _mpvHost?.GetDuration() ?? 0;
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
                LogMessage.Error("VideoMpv", "FileLoaded", this.Id + " video running past watchman end check.");
                SignalElapsedEvent();
            };
            _stopWatchman.Start();
        }

        /// <summary>
        /// mpv VO 구성 완료 이벤트. FILE_LOADED 이후 발생하며 실제 첫 프레임 렌더링 직전이다.
        /// MpvHost 내부에서 이미 WM_PAINT를 검정으로 처리하므로 여기서는 로그만 남긴다.
        /// </summary>
        private void MpvHost_VideoReconfig()
        {
            Trace.WriteLine(new LogMessage("VideoMpv", "VideoReconfig: " + this.Id), LogType.Audit.ToString());
        }

        /// <summary>
        /// 재생 실패 이벤트. 캐시 블랙리스트에 추가하고 미디어를 만료시킨다.
        /// </summary>
        private void MpvHost_MediaFailed(string errorMessage)
        {
            Trace.WriteLine(new LogMessage("VideoMpv", "MediaFailed: " + this.Id + " – " + errorMessage), LogType.Error.ToString());

            _openCalled = true;

            CacheManager.Instance.AddUnsafeItem(UnsafeItemType.Media, UnsafeFaultCodes.VideoUnexpected,
                LayoutId, FileId, "Video Failed: " + errorMessage, 120);

            IsFailedToPlay = true;
            SignalElapsedEvent();
        }

        /// <summary>
        /// 파일 재생 종료 이벤트. 루프 설정이면 처음으로 seek, 아니면 Expired 처리.
        /// </summary>
        private void MpvHost_EndFile(int reason)
        {
            Trace.WriteLine(new LogMessage("VideoMpv", "EndFile: " + this.Id + " reason=" + reason + " looping=" + isLooping + " stopped=" + _stopped), LogType.Audit.ToString());

            if (_stopped) return;

            if (reason == LibMpv.MPV_END_FILE_REASON_EOF)
            {
                if (isLooping)
                {
                    Trace.WriteLine(new LogMessage("VideoMpv", "EndFile: " + this.Id + " – looping, SeekToStart + Play"), LogType.Audit.ToString());
                    _mpvHost?.SeekToStart();
                    _mpvHost?.Play();
                }
                else
                    Expired = true;
            }
        }

        // ----------------------------------------------------------------
        // RenderMedia / Stopped 오버라이드
        // ----------------------------------------------------------------

        /// <summary>
        /// libmpv 기반 렌더링 시작.
        /// MpvHost를 생성하고 초기에는 Hidden으로 설정 후 FileLoaded 이벤트에서 Visible로 전환.
        /// Video.RenderMedia()의 WPF MediaElement 초기화를 건너뛰기 위해 StartRenderBase() 호출.
        /// </summary>
        public override void RenderMedia(double position)
        {
            _position = position;

            Uri uri = new Uri(_filePath);
            if (uri.IsFile && !File.Exists(_filePath))
            {
                Trace.WriteLine(new LogMessage("VideoMpv", "RenderMedia: " + this.Id + ", File " + _filePath + " not found."));
                throw new FileNotFoundException();
            }

            _mpvHost = new MpvHost();

            if (!ShouldBeVisible)
            {
                _mpvHost.Width = 0;
                _mpvHost.Height = 0;
                _mpvHost.Visibility = Visibility.Hidden;
            }
            else
            {
                _mpvHost.Width = Width;
                _mpvHost.Height = Height;
                _mpvHost.Visibility = Visibility.Visible;
            }

            _mpvHost.FileLoaded   += MpvHost_FileLoaded;
            _mpvHost.VideoReconfig += MpvHost_VideoReconfig;
            _mpvHost.MediaFailed  += MpvHost_MediaFailed;
            _mpvHost.EndFile      += MpvHost_EndFile;

            if (_duration == 0)
            {
                Duration   = 1;
                _detectEnd = true;
            }

            // Render media as normal (starts the timer, shows the form, etc)
            base.RenderMedia(position);

            try
            {
                this.MediaScene.Children.Add(_mpvHost);

                _mpvHost.SetVolume(volume);
                _mpvHost.SetMute(Muted);
                _mpvHost.SetStretch(Stretch);

                _startWatchman = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(ApplicationSettings.Default.VideoStartTimeout)
                };
                _startWatchman.Tick += (s, e) =>
                {
                    _startWatchman?.Stop();
                    if (!_openCalled && !IsFailedToPlay && !_stopped)
                    {
                        LogMessage.Error("VideoMpv", "RenderMedia", this.Id + " Open not called after " +
                            ApplicationSettings.Default.VideoStartTimeout + " seconds, marking unsafe and expiring.");
                        CacheManager.Instance.AddUnsafeItem(UnsafeItemType.Media, UnsafeFaultCodes.VideoUnexpected,
                            LayoutId, FileId, "Video Failed: Open not called after " + ApplicationSettings.Default.VideoStartTimeout + " seconds", 120);
                        SignalElapsedEvent();
                    }
                };
                _startWatchman.Start();

                _mpvHost.Load(_filePath);

                Trace.WriteLine(new LogMessage("VideoMpv", "RenderMedia: " + this.Id + " loaded, detectEnd=" + _detectEnd), LogType.Audit.ToString());
            }
            catch (Exception ex)
            {
                Trace.WriteLine(new LogMessage("VideoMpv", "RenderMedia: " + ex.Message), LogType.Error.ToString());
                throw;
            }
        }

        // ----------------------------------------------------------------
        // Stopped override
        // ----------------------------------------------------------------

        /// <summary>
        /// libmpv 리소스 해제.
        /// Video.Stopped()는 mediaElement를 직접 참조하므로 호출하지 않고
        /// StopBase()로 Media.Stopped()만 호출한다.
        /// </summary>
        public override void Stopped()
        {
            Trace.WriteLine(new LogMessage("VideoMpv", "Stopped: " + this.Id), LogType.Audit.ToString());

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

            if (_mpvHost != null)
            {
                _mpvHost.FileLoaded   -= MpvHost_FileLoaded;
                _mpvHost.VideoReconfig -= MpvHost_VideoReconfig;
                _mpvHost.MediaFailed  -= MpvHost_MediaFailed;
                _mpvHost.EndFile      -= MpvHost_EndFile;
                _mpvHost.Dispose();
                _mpvHost = null;
            }

            // Call Media.Stopped directly
            base.Stopped();
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
