using System;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Threading;
using System.Windows;

[assembly: AssemblyTitle("Angel Core Logger")]
[assembly: AssemblyDescription("Angel Core Logger - PowerShell Deep Background, Network & Hardware Privacy Monitor")]
[assembly: AssemblyCompany("Angel Core Logger")]
[assembly: AssemblyProduct("Angel Core Logger")]
[assembly: AssemblyCopyright("Copyright (c) 2026")]
[assembly: AssemblyVersion("3.0.0.0")]
[assembly: AssemblyFileVersion("3.0.0.0")]

namespace AngelSystemLoggerPro
{
    internal static class Program
    {
        private const string SingleInstanceMutexName = @"Local\AngelCoreLoggerPro_SingleInstance_v3";
        private const string WakeupPipeName = "AngelCoreLoggerPro_WakeupPipe_v3";

        [STAThread]
        private static int Main(string[] args)
        {
            string exePath = Assembly.GetExecutingAssembly().Location;
            AppSettings settings = AppSettings.Load();

            bool startMinimized = false;
            string previewPath = null;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i].Trim().ToLowerInvariant();
                if (arg == "--install-startup" || arg == "/install-startup" || arg == "/startup")
                {
                    StartupManager.SetStartupRegistry(exePath, true, settings.StartMinimizedToTray);
                    return 0;
                }
                if (arg == "--remove-startup" || arg == "/remove-startup" || arg == "/uninstall")
                {
                    StartupManager.SetStartupRegistry(exePath, false, false);
                    return 0;
                }
                if (arg == "--minimized" || arg == "/minimized" || arg == "--tray")
                {
                    startMinimized = true;
                }
                if (arg == "--render-preview" && i + 1 < args.Length)
                {
                    previewPath = args[++i];
                }
            }

            if (!StartupManager.IsRegisteredInStartup())
            {
                StartupManager.SetStartupRegistry(exePath, true, settings.StartMinimizedToTray);
            }

            bool createdNew = true;
            Mutex instanceMutex = null;

            if (string.IsNullOrEmpty(previewPath))
            {
                instanceMutex = new Mutex(true, SingleInstanceMutexName, out createdNew);
                if (!createdNew)
                {
                    try
                    {
                        using (NamedPipeClientStream client = new NamedPipeClientStream(".", WakeupPipeName, PipeDirection.Out))
                        {
                            client.Connect(1200);
                            using (StreamWriter sw = new StreamWriter(client))
                            {
                                sw.WriteLine("SHOW");
                                sw.Flush();
                            }
                        }
                    }
                    catch { }
                    return 0;
                }
            }

            try
            {
                Application app = new Application();
                MainWindow mainWindow = new MainWindow(exePath, settings, startMinimized, previewPath);

                if (string.IsNullOrEmpty(previewPath))
                {
                    StartWakeupListener(mainWindow);
                }

                app.Run(mainWindow);
            }
            catch (Exception ex)
            {
                string errLog = Path.Combine(AppSettings.GetAppDataDir(), "startup-error.log");
                File.WriteAllText(errLog, ex.ToString());
                MessageBox.Show("Angel Core Logger encountered an error:\n" + ex.Message, "Angel Core Logger", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
            finally
            {
                if (instanceMutex != null)
                {
                    try { instanceMutex.ReleaseMutex(); instanceMutex.Dispose(); } catch { }
                }
            }

            return 0;
        }

        private static void StartWakeupListener(MainWindow mainWindow)
        {
            Thread t = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        using (NamedPipeServerStream server = new NamedPipeServerStream(WakeupPipeName, PipeDirection.In, 1))
                        {
                            server.WaitForConnection();
                            using (StreamReader sr = new StreamReader(server))
                            {
                                string cmd = sr.ReadLine();
                                if (cmd == "SHOW")
                                {
                                    mainWindow.BringToForeground();
                                }
                            }
                        }
                    }
                    catch
                    {
                        Thread.Sleep(500);
                    }
                }
            });
            t.IsBackground = true;
            t.Start();
        }
    }
}
