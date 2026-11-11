using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

[assembly: AssemblyTitle("Angel System Logger Pro")]
[assembly: AssemblyDescription("Angel System Logger Pro - Local System Activity Logger & Startup Monitor")]
[assembly: AssemblyCompany("Angel System Logger Pro")]
[assembly: AssemblyProduct("Angel System Logger Pro")]
[assembly: AssemblyCopyright("Copyright (c) 2026")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace AngelSystemLoggerPro
{
    internal static class Program
    {
        private const string StartupRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupValueName = "AngelSystemLoggerPro";

        private static Process _childProcess;

        [DllImport("Kernel32")]
        private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate handler, bool add);

        private delegate bool ConsoleCtrlDelegate(int sig);

        private static bool OnConsoleCtrl(int sig)
        {
            try
            {
                if (_childProcess != null && !_childProcess.HasExited)
                {
                    _childProcess.Kill();
                }
            }
            catch
            {
                // Ignore cleanup errors on exit
            }
            return false;
        }

        [STAThread]
        private static int Main(string[] args)
        {
            Console.Title = "Angel System Logger Pro - Activity Logger & Monitor";
            string exePath = Assembly.GetExecutingAssembly().Location;
            string exeDir = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory;

            // Handle quick CLI switches for startup registration
            if (args.Length > 0)
            {
                string arg0 = args[0].Trim().ToLowerInvariant();
                if (arg0 == "--install-startup" || arg0 == "/install-startup" || arg0 == "/startup")
                {
                    RegisterInStartup(exePath, true);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("[STARTUP] Registered Angel System Logger Pro in Windows Startup Apps.");
                    Console.ResetColor();
                    return 0;
                }
                if (arg0 == "--remove-startup" || arg0 == "/remove-startup" || arg0 == "/uninstall")
                {
                    RegisterInStartup(exePath, false);
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("[STARTUP] Removed Angel System Logger Pro from Windows Startup Apps.");
                    Console.ResetColor();
                    return 0;
                }
            }

            // Locate Start-LocalLogger.ps1 alongside EXE, or extract embedded script
            string localScriptPath = Path.Combine(exeDir, "Start-LocalLogger.ps1");
            string scriptToRun = localScriptPath;

            if (!File.Exists(scriptToRun))
            {
                string appDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AngelSystemLoggerPro");
                Directory.CreateDirectory(appDataDir);
                scriptToRun = Path.Combine(appDataDir, "Start-LocalLogger.ps1");
                ExtractEmbeddedScript(scriptToRun);
            }

            ConsoleCtrlDelegate ctrlHandler = new ConsoleCtrlDelegate(OnConsoleCtrl);
            SetConsoleCtrlHandler(ctrlHandler, true);

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = string.Format("-NoLogo -NoProfile -ExecutionPolicy Bypass -File \"{0}\"", scriptToRun),
                UseShellExecute = false,
                CreateNoWindow = false,
                WorkingDirectory = exeDir
            };
            psi.EnvironmentVariables["ANGEL_LOGGER_EXE_PATH"] = exePath;

            try
            {
                _childProcess = Process.Start(psi);
                if (_childProcess != null)
                {
                    _childProcess.WaitForExit();
                    return _childProcess.ExitCode;
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Failed to launch activity logger engine: " + ex.Message);
                Console.ResetColor();
                return 1;
            }
            finally
            {
                GC.KeepAlive(ctrlHandler);
            }

            return 0;
        }

        private static void ExtractEmbeddedScript(string destinationPath)
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream stream = asm.GetManifestResourceStream("Start-LocalLogger.ps1"))
            {
                if (stream == null)
                {
                    throw new FileNotFoundException("Embedded resource 'Start-LocalLogger.ps1' was not found.");
                }
                using (FileStream fs = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    stream.CopyTo(fs);
                }
            }
        }

        private static void RegisterInStartup(string exePath, bool enable)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupRegistryPath, true))
            {
                if (key == null) return;
                if (enable)
                {
                    key.SetValue(StartupValueName, "\"" + exePath + "\"", RegistryValueKind.String);
                }
                else
                {
                    if (key.GetValue(StartupValueName) != null)
                    {
                        key.DeleteValue(StartupValueName, false);
                    }
                }
            }
        }
    }
}
