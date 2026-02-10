using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using XiboClient.Logic;

namespace XiboClient
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        [STAThread]
        protected override void OnStartup(StartupEventArgs e)
        {
            NativeMethods.SetErrorMode(NativeMethods.SetErrorMode(0) |
                           ErrorModes.SEM_NOGPFAULTERRORBOX |
                           ErrorModes.SEM_FAILCRITICALERRORS |
                           ErrorModes.SEM_NOOPENFILEERRORBOX);


            // Ensure our process has the highest priority
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.RealTime;

#if !DEBUG
            // Catch unhandled exceptions
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
#endif

            // Load settings
            ApplicationSettings.Default.Load();

            // Add the Xibo Tracelistener
            Trace.Listeners.Add(new XiboTraceListener());

            bool shouldQuit = false;
            try
            {
                // Check for any passed arguments
                if (e.Args.Length > 0)
                {
                    if (e.Args[0].ToString() == "o")
                    {
                        shouldQuit = true;
                        RunSettings();
                    }
                    else
                    {
                        switch (e.Args[0].ToLower().Trim().Substring(0, 2))
                        {
                            // Preview the screen saver
                            case "/p":
                                // Do nothing
                                break;

                            // Show the screen saver
                            case "/s":
                                RunClient(true);
                                break;

                            // Configure the screesaver's settings
                            case "/c":
                                // Show the settings form
                                RunSettings();
                                break;

                            // Show the screen saver
                            default:
                                RunClient(true);
                                break;
                        }
                    }
                }
                else
                {
                    RunClient(false);
                }
            }
            catch (Exception ex)
            {
                HandleUnhandledException(ex, "Startup", shouldQuit);
            }

            // Always flush at the end
            Trace.WriteLine(new LogMessage("Main", "Application Finished"), LogType.Info.ToString());
            Trace.Flush();

            Environment.Exit(0); // end application here as all windows are shown as dialog (sync).
        }

        /// <summary>
        /// Run the Settings Window
        /// </summary>
        private static void RunSettings()
        {
            // If we are showing the options form, enable visual styles
            OptionsForm windowMain = new OptionsForm();
            windowMain.ShowDialog();
        }

        /// <summary>
        /// Run the Player
        /// </summary>
        /// <param name="screenSaver"></param>
        private static void RunClient(bool screenSaver)
        {
            Trace.WriteLine(new LogMessage("Main", "Client Started"), LogType.Info.ToString());

#if !DEBUG
            KeyInterceptor.SetHook();
            MouseInterceptor.SetHook();
#endif

            using (var windowMain = new MainWindow(screenSaver))
            {
                windowMain.ShowDialog();
            }
#if !DEBUG
            KeyInterceptor.UnsetHook();
            MouseInterceptor.UnsetHook();
#endif
        }

        #region Exception Handlers
        static void Application_ThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
        {
            HandleUnhandledException(e.Exception, "ThreadException", true);
        }

        static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            HandleUnhandledException(e.ExceptionObject, "UnhandledException", true);
        }

        static void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            HandleUnhandledException(e.Exception, "UnobservedTaskException", false);
        }

        /// <summary>
        /// Event for unhandled exceptions
        /// </summary>
        /// <param name="o"></param>
        static void HandleUnhandledException(Object o, string source, bool quit)
        {
            Exception e = o as Exception;

            // What happens if we cannot start?
            Trace.WriteLine(new LogMessage("Main", "Unhandled Exception: " + source + ": " + e.Message), LogType.Error.ToString());
            Trace.WriteLine(new LogMessage("Main", "Stack Trace: " + e.StackTrace), LogType.Error.ToString());

            // Should we quit or continue
            if (quit)
            {
                try
                {
                    string productName = ApplicationSettings.GetProductNameFromAssembly();

                    // Also write to the event log
                    try
                    {
                        if (!EventLog.SourceExists(productName))
                        {
                            EventLog.CreateEventSource(productName, "Xibo");
                        }

                        EventLog.WriteEntry(productName, e.ToString(), EventLogEntryType.Error);
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine(new LogMessage("Main", "Couldn't write to event log: " + ex.Message), LogType.Info.ToString());
                    }

                    // Trace.Flush can hang when the rendering thread is dead,
                    // so run it with a timeout
                    var flushTask = Task.Run(() => Trace.Flush());
                    if (!flushTask.Wait(3000))
                    {
                        // Flush timed out - write directly to file as fallback
                        try
                        {
                            if (!string.IsNullOrEmpty(ApplicationSettings.Default.LogToDiskLocation))
                            {
                                File.AppendAllText(ApplicationSettings.Default.LogToDiskLocation,
                                    DateTime.Now + " Unhandled Exception: " + source + ": " + e.Message + Environment.NewLine +
                                    DateTime.Now + " Stack Trace: " + e.StackTrace + Environment.NewLine);
                            }
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    // Direct file write as last resort
                    try
                    {
                        if (!string.IsNullOrEmpty(ApplicationSettings.Default.LogToDiskLocation))
                        {
                            File.AppendAllText(ApplicationSettings.Default.LogToDiskLocation,
                                DateTime.Now + " Unhandled Exception: " + source + ": " + e.Message + Environment.NewLine +
                                DateTime.Now + " Unable to write Trace Listeners: " + ex.Message + Environment.NewLine);
                        }
                    }
                    catch { }
                }

                // Exit the application and allow it to be restarted by the Watchdog.
                Process.GetCurrentProcess().Kill();
            }
        }

        internal static class NativeMethods
        {
            [DllImport("kernel32.dll")]
            internal static extern ErrorModes SetErrorMode(ErrorModes mode);
        }

        [Flags]
        internal enum ErrorModes : uint
        {
            SYSTEM_DEFAULT = 0x0,
            SEM_FAILCRITICALERRORS = 0x0001,
            SEM_NOALIGNMENTFAULTEXCEPT = 0x0004,
            SEM_NOGPFAULTERRORBOX = 0x0002,
            SEM_NOOPENFILEERRORBOX = 0x8000
        }

        #endregion
    }
}
