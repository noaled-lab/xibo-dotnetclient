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
using System.Windows.Controls;
using System.Windows.Threading;

namespace XiboClient.Rendering
{
    class Video : Media
    {
        private string _filePath;
        private int _duration;
        private int volume;
        private bool _detectEnd = false;
        private bool isLooping = false;
        private readonly bool isFullScreenRequest = false;
        private bool _openCalled = false;
        private bool _stopped = false;

        #region Static Video Stall Detection

        private static readonly object _trackingLock = new object();
        private static bool _isVideoPlaying = false;
        private static TimeSpan _lastKnownPosition = TimeSpan.Zero;
        private static DateTime _lastPositionChangeTime = DateTime.Now;
        private static string _trackedVideoId = null;

        /// <summary>
        /// Is a visible video currently playing?
        /// </summary>
        public static bool IsVideoCurrentlyPlaying
        {
            get { lock (_trackingLock) { return _isVideoPlaying; } }
        }

        /// <summary>
        /// Is the currently playing video stalled?
        /// A video is considered stalled if it's playing but position hasn't changed for 10+ seconds.
        /// </summary>
        public static bool IsVideoStalled
        {
            get
            {
                lock (_trackingLock)
                {
                    if (!_isVideoPlaying)
                        return false;
                    return (DateTime.Now - _lastPositionChangeTime).TotalSeconds > 10;
                }
            }
        }

        /// <summary>
        /// Get stalled video info string for status reporting.
        /// Returns null if no stall detected.
        /// </summary>
        public static string GetVideoStallInfo()
        {
            lock (_trackingLock)
            {
                if (!_isVideoPlaying)
                    return null;

                double stallSeconds = (DateTime.Now - _lastPositionChangeTime).TotalSeconds;
                if (stallSeconds > 10)
                {
                    return string.Format("Video {0} stalled at position {1} for {2:F0}s",
                        _trackedVideoId, _lastKnownPosition, stallSeconds);
                }
                return null;
            }
        }

        #endregion

        /// <summary>
        /// Should this be visible? Audio sets this to false.
        /// </summary>
        protected bool ShouldBeVisible { get; set; }

        /// <summary>
        /// Muted?
        /// </summary>
        protected bool Muted { get; set; }

        /// <summary>
        /// Stretched?
        /// </summary>
        protected bool Stretch { get; set; }

        /// <summary>
        /// Should we seek to a position or not
        /// </summary>
        private double _position;

        /// <summary>
        /// The Media element for Playback
        /// </summary>
        private MediaElement mediaElement;

        /// <summary>
        /// Start Watchman
        /// </summary>
        private DispatcherTimer _StartWatchman;

        /// <summary>
        /// Stop Watchman
        /// </summary>
        private DispatcherTimer _StopWatchman;

        /// <summary>
        /// Position tracking timer for video stall detection
        /// </summary>
        private DispatcherTimer _positionTracker;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="options"></param>
        public Video(MediaOptions options) : base(options)
        {
            // Videos should be visible
            this.ShouldBeVisible = true;

            _filePath = Uri.UnescapeDataString(options.uri).Replace('+', ' ');
            _duration = options.duration;

            // Handle Volume
            this.volume = options.Dictionary.Get("volume", 100);

            // Mute - if not provided as an option, we keep the default.
            string muteOption = options.Dictionary.Get("mute");
            if (!string.IsNullOrEmpty(muteOption))
            {
                this.Muted = muteOption == "1";
            }

            // Should we loop?
            this.isLooping = (options.Dictionary.Get("loop", "0") == "1" && _duration != 0);

            // Full Screen?
            this.isFullScreenRequest = options.Dictionary.Get("showFullScreen", "0") == "1";

            // Scale type
            Stretch = options.Dictionary.Get("scaleType", "aspect").ToLowerInvariant() == "stretch";
        }

        #region "Media Events"

        /// <summary>
        /// Fired when the video is loaded and ready to seek
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MediaElement_MediaOpened(object sender, RoutedEventArgs e)
        {
            Trace.WriteLine(new LogMessage("Video", "MediaElement_MediaOpened: " + this.Id + " Opened, seek to: " + this._position), LogType.Audit.ToString());

            // Open has been called.
            this._openCalled = true;

            // Start position tracking for stall detection (visible videos only)
            if (ShouldBeVisible)
            {
                StartPositionTracking();
            }

            // If we have been given a duration, restart the timer
            // we are trying to cater for any time lost opening the media
            if (!_detectEnd)
            {
                RestartTimer();
            }

            // Try to seek
            if (this._position > 0)
            {
                this.mediaElement.Position = TimeSpan.FromSeconds(this._position);

                // Set the position to 0, so that if we loop around again we start from the beginning
                this._position = 0;
            }

            var watchmanTtl = TimeSpan.FromSeconds(60);
            if (_duration == 0)
            {
                // Add the duration of the video
                if (this.mediaElement.NaturalDuration == System.Windows.Duration.Automatic)
                {
                    // This is strange, so we will just log and keep the watchman duration at 60 seconds
                    LogMessage.Audit("Video", "MediaElement_MediaOpened", "Duration not detected on open");
                }
                else
                {
                    watchmanTtl = watchmanTtl.Add(this.mediaElement.NaturalDuration.TimeSpan);
                }
            }
            else
            {
                // Add the duration of the widget
                watchmanTtl = watchmanTtl.Add(TimeSpan.FromSeconds(this._duration));
            }

            // Set a watchman to make sure we actually end (normally this would be cancelled when we end naturally)
            _StopWatchman = new DispatcherTimer { Interval = watchmanTtl };
            _StopWatchman.Tick += (timerSender, args) =>
            {
                // You only tick once
                _StopWatchman.Stop();

                LogMessage.Error("Video", "MediaElement_MediaOpened", this.Id + " video running past watchman end check.");

                // Expire
                SignalElapsedEvent();
            };

            _StopWatchman.Start();

        }

        /// <summary>
        /// Media Failed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MediaElement_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            // Log and expire
            Trace.WriteLine(new LogMessage("Video", "MediaElement_MediaFailed: " + this.Id + " Media Failed. E = " + e.ErrorException.Message), LogType.Error.ToString());

            if (e.ErrorException.Message.Contains("HRESULT"))
            {
                // UCEERR_RENDERTHREADFAILURE - rendering thread is dead, force kill
                Trace.Flush();
                Process.GetCurrentProcess().Kill();
            }
            else
            {
                // Failed is the opposite of open, but we mark this as open called so that our watchman doesn't also try to expire
                this._openCalled = true;

                // Stop position tracking
                StopPositionTracking();

                // Add this to a temporary blacklist so that we don't repeat it too quickly
                CacheManager.Instance.AddUnsafeItem(UnsafeItemType.Media, UnsafeFaultCodes.VideoUnexpected, LayoutId, FileId, "Video Failed: " + e.ErrorException.Message, 120);

                // Set as failed to play
                IsFailedToPlay = true;

                // Expire
                SignalElapsedEvent();
            }
        }

        /// <summary>
        /// Media Ended
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MediaElement_MediaEnded(object sender, RoutedEventArgs e)
        {
            Trace.WriteLine(new LogMessage("Video", "MediaElement_MediaEnded: " + this.Id + " Ended, looping: " + isLooping), LogType.Audit.ToString());

            // Should we loop?
            if (isLooping)
            {
                this.mediaElement.Position = TimeSpan.Zero;
                this.mediaElement.Play();

                // Reset position change time so stall detection doesn't false-positive during loop restart
                lock (_trackingLock)
                {
                    _lastPositionChangeTime = DateTime.Now;
                }
            }
            else
            {
                StopPositionTracking();
                Expired = true;
            }
        }

        /// <summary>
        /// MediaElement has been added to the visual tree
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MediaElement_Loaded(object sender, RoutedEventArgs e)
        {
            Trace.WriteLine(new LogMessage("Video", "MediaElement_Loaded: " + this.Id + " Control loaded, calling Play."), LogType.Audit.ToString());

            // We make a watchman to check that the video actually gets loaded.
            _StartWatchman = new DispatcherTimer { Interval = TimeSpan.FromSeconds(ApplicationSettings.Default.VideoStartTimeout) };
            _StartWatchman.Tick += (timerSender, args) =>
            {
                // You only tick once
                _StartWatchman.Stop();

                // Check to see if open has been called.
                if (!_openCalled && !IsFailedToPlay && !_stopped)
                {
                    LogMessage.Error("Video", "MediaElement_Loaded", this.Id + " Open not called after " + ApplicationSettings.Default.VideoStartTimeout + " seconds, marking unsafe and Expiring.");
                    
                    // Add this to a temporary blacklist so that we don't repeat it too quickly
                    CacheManager.Instance.AddUnsafeItem(UnsafeItemType.Media, UnsafeFaultCodes.VideoUnexpected, LayoutId, FileId, "Video Failed: Open not called after " + ApplicationSettings.Default.VideoStartTimeout + " seconds", 120);

                    // Expire
                    SignalElapsedEvent();
                }
            };

            _StartWatchman.Start();

            // Actually play the video
            try
            {
                this.mediaElement.Play();
            }
            catch (Exception ex)
            {
                // Problem calling play, we should expire.
                Trace.WriteLine(new LogMessage("Video", "MediaElement_Loaded: " + this.Id + " Media Failed. E = " + ex.Message), LogType.Error.ToString());

                // Cancel the watchman
                _StartWatchman.Stop();
            }
        }

        #endregion

        /// <summary>
        /// Render
        /// </summary>
        /// <param name="position"></param>
        public override void RenderMedia(double position)
        {
            // Save the position
            this._position = position;

            // Check to see if the video exists or not (if it doesnt say we are already expired)
            // we only do this if we aren't a stream
            Uri uri = new Uri(_filePath);

            if (uri.IsFile && !File.Exists(_filePath))
            {
                Trace.WriteLine(new LogMessage("Video", "RenderMedia: " + this.Id + ", File " + _filePath + " not found."));
                throw new FileNotFoundException();
            }

            // Create a Media Element
            this.mediaElement = new MediaElement();
            this.mediaElement.Volume = this.volume;
            this.mediaElement.IsMuted = this.Muted;
            this.mediaElement.LoadedBehavior = MediaState.Manual;
            this.mediaElement.UnloadedBehavior = MediaState.Close;

            // This is false if we're an audio module, otherwise video.
            if (!this.ShouldBeVisible)
            {
                this.mediaElement.Width = 0;
                this.mediaElement.Height = 0;
                this.mediaElement.Visibility = Visibility.Hidden;
            }
            else
            {
                // Assert the Width/Height of the Parent
                this.mediaElement.Width = Width;
                this.mediaElement.Height = Height;
                this.mediaElement.Visibility = Visibility.Visible;
            }

            // Handle stretching
            if (Stretch)
            {
                this.mediaElement.Stretch = System.Windows.Media.Stretch.Fill;
            }

            // Apply opacity if specified (0-100, default 100)
            string opacityStr = Options.Dictionary.Get("opacity", "100");
            if (double.TryParse(opacityStr, out double opacityValue))
            {
                this.mediaElement.Opacity = Math.Max(0, Math.Min(1, opacityValue / 100.0));
            }

            // Apply brightness if specified (50-200, default 100)
            // Note: Video brightness control is limited in WPF MediaElement
            // Full brightness control would require more complex shader effects
            string brightnessStr = Options.Dictionary.Get("brightness", "100");
            if (double.TryParse(brightnessStr, out double brightnessValue))
            {
                // Convert brightness (50-200) to multiplier (0.5-2.0)
                double brightnessMultiplier = brightnessValue / 100.0;
                
                // For video, brightness adjustment is more complex
                // We can only adjust opacity as a workaround for darker videos
                // Full brightness control requires pixel shader effects which are complex
                if (brightnessMultiplier < 1.0)
                {
                    // Darker: reduce opacity slightly
                    this.mediaElement.Opacity *= (0.5 + brightnessMultiplier * 0.5);
                }
                // Note: Making videos brighter requires pixel shader effects
                // This is a basic implementation
            }

            // Events
            // MediaOpened is called after we've called Play()
            this.mediaElement.MediaOpened += MediaElement_MediaOpened;

            // Loaded is from the Framework and is called when the MediaElement is added to the visual tree (we call play in here)
            this.mediaElement.Loaded += MediaElement_Loaded;

            // Media ended is called when the media file has finished playing
            this.mediaElement.MediaEnded += MediaElement_MediaEnded;

            // Media Failed is called if the media file cannot be opened
            this.mediaElement.MediaFailed += MediaElement_MediaFailed;

            // Do we need to determine the end time ourselves?
            if (_duration == 0)
            {
                // Set the duration to 1 second
                // this essentially means RenderMedia will set up a timer which ticks every second
                // when we're actually expired and we detect the end, we set expired
                Duration = 1;
                _detectEnd = true;
            }

            // Render media as normal (starts the timer, shows the form, etc)
            base.RenderMedia(position);

            try
            {
                // Start Player
                this.mediaElement.Source = uri;

                this.MediaScene.Children.Add(this.mediaElement);

                Trace.WriteLine(new LogMessage("Video", "RenderMedia: " + this.Id + ", added MediaElement and set source, detect end is " + _detectEnd), LogType.Audit.ToString());
            }
            catch (Exception ex)
            {
                Trace.WriteLine(new LogMessage("Video", "RenderMedia: " + ex.Message), LogType.Error.ToString());

                // Unable to start video - expire this media immediately
                throw;
            }
        }

        /// <summary>
        /// Stop
        /// </summary>
        public override void Stopped()
        {
            Trace.WriteLine(new LogMessage("Video", "Stopped: " + this.Id), LogType.Audit.ToString());

            // We've stopped
            _stopped = true;

            // Stop position tracking for stall detection
            StopPositionTracking();

            // Clear the watchman
            if (_StartWatchman != null)
            {
                _StartWatchman.Stop();
                _StartWatchman = null;
            }

            if (_StopWatchman != null)
            {
                _StopWatchman.Stop();
                _StopWatchman = null;
            }

            // Remove the event handlers
            this.mediaElement.MediaOpened -= MediaElement_MediaOpened;
            this.mediaElement.Loaded -= MediaElement_Loaded;
            this.mediaElement.MediaEnded -= MediaElement_MediaEnded;
            this.mediaElement.MediaFailed -= MediaElement_MediaFailed;

            // Try and clear some memory
            this.mediaElement.Close();
            this.mediaElement.Clock = null;
            this.mediaElement.Source = null;
            this.mediaElement = null;

            base.Stopped();
        }

        #region Video Stall Detection Helpers

        /// <summary>
        /// Start tracking video position for stall detection
        /// </summary>
        private void StartPositionTracking()
        {
            lock (_trackingLock)
            {
                _isVideoPlaying = true;
                _lastKnownPosition = TimeSpan.Zero;
                _lastPositionChangeTime = DateTime.Now;
                _trackedVideoId = this.Id;
            }

            _positionTracker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _positionTracker.Tick += (s, e) =>
            {
                if (mediaElement == null || _stopped)
                {
                    _positionTracker?.Stop();
                    return;
                }

                try
                {
                    TimeSpan currentPosition = mediaElement.Position;
                    lock (_trackingLock)
                    {
                        if (currentPosition != _lastKnownPosition)
                        {
                            _lastKnownPosition = currentPosition;
                            _lastPositionChangeTime = DateTime.Now;
                        }
                    }
                }
                catch
                {
                    // MediaElement may have been disposed
                }
            };
            _positionTracker.Start();
        }

        /// <summary>
        /// Stop tracking video position
        /// </summary>
        private void StopPositionTracking()
        {
            if (_positionTracker != null)
            {
                _positionTracker.Stop();
                _positionTracker = null;
            }

            lock (_trackingLock)
            {
                if (_trackedVideoId == this.Id)
                {
                    _isVideoPlaying = false;
                    _trackedVideoId = null;
                }
            }
        }

        #endregion

        /// <summary>
        /// Override the timer tick
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected override void timer_Tick(object sender, EventArgs e)
        {
            if (!_detectEnd || Expired)
            {
                // We're not end detect, so we pass the timer through
                base.timer_Tick(sender, e);
            }
        }

        /// <summary>
        /// Is a region size change required
        /// </summary>
        /// <returns></returns>
        public override bool RegionSizeChangeRequired()
        {
            return this.isFullScreenRequest;
        }
    }
}
