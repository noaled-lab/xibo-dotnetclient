/**
 * Copyright (C) 2023 Xibo Signage Ltd
 *
 * Xibo - Digital Signage - http://www.xibo.org.uk
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
using System.Net;
using System.Text;
using System.Threading;

namespace XiboClient.XmdsAgents
{
    class FileAgent
    {
        /// <summary>
        /// OnComplete delegate
        /// </summary>
        /// <param name="fileId"></param>
        public delegate void OnCompleteDelegate(int fileId, string fileType);
        public event OnCompleteDelegate OnComplete;

        /// <summary>
        /// OnPartComplete delegate
        /// </summary>
        /// <param name="fileId"></param>
        public delegate void OnPartCompleteDelegate(int fileId);
        public event OnPartCompleteDelegate OnPartComplete;

        /// <summary>
        /// Client Hardware key
        /// </summary>
        public string HardwareKey
        {
            set
            {
                _hardwareKey = value;
            }
        }
        private string _hardwareKey;

        /// <summary>
        /// Required Files Object
        /// </summary>
        private RequiredFiles _requiredFiles;

        /// <summary>
        /// The Required File to download
        /// </summary>
        private RequiredFile _requiredFile;

        /// <summary>
        /// File Download Limit Semaphore
        /// </summary>
        public Semaphore FileDownloadLimit
        {
            set
            {
                _fileDownloadLimit = value;
            }
        }
        private Semaphore _fileDownloadLimit;

        /// <summary>
        /// File Agent Responsible for downloading a single file
        /// </summary>
        public FileAgent(RequiredFiles files, RequiredFile file)
        {
            _requiredFiles = files;
            _requiredFile = file;
        }

        /// <summary>
        /// Runs the agent
        /// </summary>
        public void Run()
        {
            Trace.WriteLine(new LogMessage("FileAgent - Run", "Thread Started"), LogType.Audit.ToString());

            // Set downloading to be true
            _requiredFile.Downloading = true;

            // Wait for the Semaphore lock to become available
            _fileDownloadLimit.WaitOne();

            try
            {
                Trace.WriteLine(new LogMessage("FileAgent - Run", "Thread alive and Lock Obtained"), LogType.Audit.ToString());

                if (_requiredFile.FileType == "resource")
                {
                    // Download using XMDS GetResource
                    using (xmds.xmds xmds = new xmds.xmds())
                    {
                        xmds.Credentials = null;
                        xmds.UseDefaultCredentials = true;
                        xmds.Url = ApplicationSettings.Default.XiboClient_xmds_xmds + "&method=getResource";

                        string result = xmds.GetResource(ApplicationSettings.Default.ServerKey, ApplicationSettings.Default.HardwareKey, _requiredFile.LayoutId, _requiredFile.RegionId, _requiredFile.MediaId);

                        // Write the result to disk
                        using (FileStream fileStream = File.Open(ApplicationSettings.Default.LibraryPath + @"\" + _requiredFile.SaveAs, FileMode.Create, FileAccess.Write, FileShare.Read))
                        {
                            using (StreamWriter sw = new StreamWriter(fileStream))
                            {
                                sw.Write(result);
                                sw.Close();
                            }
                        }

                        // File completed
                        _requiredFile.Downloading = false;
                        _requiredFile.Complete = true;
                    }
                }
                else if (_requiredFile.Http)
                {
                    // HTTP 직접 다운로드 (XMDS를 거치지 않고 외부 URL에서 직접 다운로드)
                    using (WebClient wc = new WebClient())
                    {
                        // ManualResetEvent를 이용한 스레드 관리
                        using (ManualResetEvent downloadComplete = new ManualResetEvent(false))
                        {
                            // 진행률 추적 변수들
                            long lastReportedBytes = 0;  // 마지막으로 진행률을 보고한 시점의 바이트 수
                            long progressThreshold = Math.Max(1024, (long)(_requiredFile.Size / 100));  // 진행률 보고 간격 (최소 1KB 또는 전체 크기의 1%)

                            // 다운로드 에러 추적
                            Exception downloadException = null;
                            bool downloadCancelled = false;

                            // 이벤트 핸들러 1: 다운로드 진행 중 (비동기 콜백)
                            wc.DownloadProgressChanged += (sender, e) =>
                            {
                                // 현재 다운로드된 바이트 수를 ChunkOffset에 기록 (MediaInventory 보고용) - ChunkOffset은 기존에 있던거임
                                _requiredFile.ChunkOffset = e.BytesReceived;

                                // 일정 간격(1KB 또는 1%)마다만 OnPartComplete 이벤트 발생 (부하 방지)
                                if (e.BytesReceived - lastReportedBytes > progressThreshold)
                                {
                                    OnPartComplete?.Invoke(_requiredFile.Id); // 이걸 호출하면 퍼센트를 다시 계산해둠..실제로 UI 업데이트는 5초마다 발생함
                                    lastReportedBytes = e.BytesReceived;
                                }
                            };

                            // 이벤트 핸들러 2: 다운로드 완료 또는 실패 (비동기 콜백)
                            wc.DownloadFileCompleted += (sender, e) =>
                            {
                                // 에러 발생 시 예외 저장
                                if (e.Error != null)
                                {
                                    downloadException = e.Error;
                                }
                                // 취소된 경우
                                else if (e.Cancelled)
                                {
                                    downloadCancelled = true;
                                }

                                // 완료 신호 전송 (메인 스레드 깨우기)
                                downloadComplete.Set();
                            };

                            // 비동기 다운로드 시작
                            wc.DownloadFileAsync(new Uri(_requiredFile.Path), ApplicationSettings.Default.LibraryPath + @"\" + _requiredFile.SaveAs);

                            // 다운로드 완료까지 대기 (블로킹) - 파일 다운로드는 별도 스레드라 상관없음 (세마포어라는 고급 기술 써서 동시 다운로드 및 제한도 있음)
                            downloadComplete.WaitOne();

                            // 다운로드 결과 확인 및 예외 처리 (예외 던지면 어딘가에서 잡고있음)
                            if (downloadCancelled)
                            {
                                throw new WebException("Download was cancelled");
                            }

                            if (downloadException != null)
                            {
                                throw downloadException;
                            }
                        }
                    }

                    // File completed
                    _requiredFile.Downloading = false;

                    // Check MD5
                    string md5 = CacheManager.Instance.GetMD5(_requiredFile.SaveAs);
                    if (_requiredFile.Md5 == md5)
                    {
                        // Mark it as complete
                        _requiredFiles.MarkComplete(_requiredFile.Id, _requiredFile.Md5);

                        // Add it to the cache manager
                        CacheManager.Instance.Add(_requiredFile.SaveAs, _requiredFile.Md5);

                        Trace.WriteLine(new LogMessage("FileAgent - Run", "File Downloaded Successfully. " + _requiredFile.SaveAs), LogType.Info.ToString());
                    }
                    else
                    {
                        // Just error - we will pick it up again the next time we download
                        Trace.WriteLine(new LogMessage("FileAgent - Run", "Downloaded file failed MD5 check. Calculated [" + md5 + "] & XMDS [ " + _requiredFile.Md5 + "] . " + _requiredFile.SaveAs), LogType.Info.ToString());
                    }
                }
                else
                {
                    // Download using XMDS GetFile/GetDependency
                    while (!_requiredFile.Complete)
                    {
                        byte[] getFileReturn;

                        // Call XMDS GetFile
                        using (xmds.xmds xmds = new xmds.xmds())
                        {
                            xmds.Credentials = null;
                            xmds.UseDefaultCredentials = false;

                            if (_requiredFile.FileType == "dependency")
                            {
                                xmds.Url = ApplicationSettings.Default.XiboClient_xmds_xmds + "&method=getDepencency";
                                getFileReturn = xmds.GetDependency(ApplicationSettings.Default.ServerKey, _hardwareKey, _requiredFile.DependencyFileType, _requiredFile.DependencyId, _requiredFile.ChunkOffset, _requiredFile.ChunkSize);
                            }
                            else
                            {
                                xmds.Url = ApplicationSettings.Default.XiboClient_xmds_xmds + "&method=getFile";
                                getFileReturn = xmds.GetFile(ApplicationSettings.Default.ServerKey, _hardwareKey, _requiredFile.Id, _requiredFile.FileType, _requiredFile.ChunkOffset, _requiredFile.ChunkSize);
                            }
                        }

                        // Set the flag to indicate we have a connection to XMDS
                        ApplicationSettings.Default.XmdsLastConnection = DateTime.Now;

                        if (_requiredFile.FileType == "layout")
                        {
                            // Decode this byte[] into a string and stick it in the file.
                            string layoutXml = Encoding.UTF8.GetString(getFileReturn);

                            // Full file is downloaded
                            using (FileStream fileStream = File.Open(ApplicationSettings.Default.LibraryPath + @"\" + _requiredFile.SaveAs, FileMode.Create, FileAccess.Write, FileShare.Read))
                            {
                                using (StreamWriter sw = new StreamWriter(fileStream))
                                {
                                    sw.Write(layoutXml);
                                    sw.Close();
                                }
                            }

                            _requiredFile.Complete = true;
                        }
                        else
                        {
                            // Dependency / Media file
                            // We're OK to use path for dependency as that will be the original file name
                            // Need to write to the file - in append mode
                            using (FileStream fs = new FileStream(ApplicationSettings.Default.LibraryPath + @"\" + _requiredFile.Path, FileMode.Append, FileAccess.Write))
                            {
                                fs.Write(getFileReturn, 0, getFileReturn.Length);
                                fs.Close();
                            }

                            // Increment the offset by the amount we just asked for
                            _requiredFile.ChunkOffset = _requiredFile.ChunkOffset + _requiredFile.ChunkSize;

                            // Has the offset reached the total size?
                            if (_requiredFile.Size > _requiredFile.ChunkOffset)
                            {
                                double remaining = _requiredFile.Size - _requiredFile.ChunkOffset;

                                // There is still more to come
                                if (remaining < _requiredFile.ChunkSize)
                                {
                                    // Get the remaining
                                    _requiredFile.ChunkSize = remaining;
                                }

                                // Part is complete
                                OnPartComplete(_requiredFile.Id);
                            }
                            else
                            {
                                // File complete
                                _requiredFile.Complete = true;
                            }
                        }

                        getFileReturn = null;
                    }

                    // File completed
                    _requiredFile.Downloading = false;

                    // Check MD5
                    string md5 = CacheManager.Instance.GetMD5(_requiredFile.SaveAs);
                    if (_requiredFile.Md5 == md5)
                    {
                        // Mark it as complete
                        _requiredFiles.MarkComplete(_requiredFile.Id, _requiredFile.Md5);

                        // Add it to the cache manager
                        CacheManager.Instance.Add(_requiredFile.SaveAs, _requiredFile.Md5);

                        Trace.WriteLine(new LogMessage("FileAgent - Run", "File Downloaded Successfully. " + _requiredFile.SaveAs), LogType.Info.ToString());
                    }
                    else
                    {
                        // Just error - we will pick it up again the next time we download
                        Trace.WriteLine(new LogMessage("FileAgent - Run", "Downloaded file failed MD5 check. Calculated [" + md5 + "] & XMDS [ " + _requiredFile.Md5 + "] . " + _requiredFile.SaveAs), LogType.Info.ToString());
                    }
                }

                // Inform the Player thread that a file has been modified.
                OnComplete(_requiredFile.Id, _requiredFile.FileType);
            }
            catch (WebException webEx)
            {
                // Remove from the cache manager
                CacheManager.Instance.Remove(_requiredFile.SaveAs);

                // Log this message, but dont abort the thread
                Trace.WriteLine(new LogMessage("FileAgent - Run", "Web Exception in Run: " + webEx.Message), LogType.Info.ToString());

                // Mark as not downloading
                _requiredFile.Downloading = false;
            }
            catch (Exception ex)
            {
                // Remove from the cache manager
                CacheManager.Instance.Remove(_requiredFile.SaveAs);

                // Log this message, but dont abort the thread
                Trace.WriteLine(new LogMessage("FileAgent - Run", "Exception in Run: " + ex.Message), LogType.Error.ToString());

                // Mark as not downloading
                _requiredFile.Downloading = false;
            }

            // Release the Semaphore
            Trace.WriteLine(new LogMessage("FileAgent - Run", "Releasing Lock"), LogType.Audit.ToString());

            _fileDownloadLimit.Release();
        }
    }
}
