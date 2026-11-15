using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace AngelSystemLoggerPro
{
    public static class JsonHelper
    {
        public static Dictionary<string, object> DeserializeDict(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                JavaScriptSerializer ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                return ser.Deserialize<Dictionary<string, object>>(json);
            }
            catch
            {
                return null;
            }
        }

        public static string GetStr(Dictionary<string, object> d, string key, string fallback = "")
        {
            if (d == null || !d.ContainsKey(key) || d[key] == null) return fallback;
            return Convert.ToString(d[key]);
        }

        public static int GetInt(Dictionary<string, object> d, string key, int fallback = 0)
        {
            if (d == null || !d.ContainsKey(key) || d[key] == null) return fallback;
            int val;
            return int.TryParse(Convert.ToString(d[key]), out val) ? val : fallback;
        }

        public static double GetDouble(Dictionary<string, object> d, string key, double fallback = 0.0)
        {
            if (d == null || !d.ContainsKey(key) || d[key] == null) return fallback;
            double val;
            return double.TryParse(Convert.ToString(d[key]), out val) ? val : fallback;
        }

        public static bool GetBool(Dictionary<string, object> d, string key, bool fallback = false)
        {
            if (d == null || !d.ContainsKey(key) || d[key] == null) return fallback;
            bool val;
            return bool.TryParse(Convert.ToString(d[key]), out val) ? val : fallback;
        }

        public static object[] GetArray(Dictionary<string, object> d, string key)
        {
            if (d == null || !d.ContainsKey(key) || d[key] == null) return new object[0];
            object[] arr = d[key] as object[];
            if (arr != null) return arr;
            System.Collections.ArrayList list = d[key] as System.Collections.ArrayList;
            if (list != null) return list.ToArray();
            return new object[0];
        }
    }

    public static class IpPrivacyHelper
    {
        private static readonly ConcurrentDictionary<string, string> _dnsCache = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, byte> _inFlight = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        public static string ApplyIpMask(string text, bool mask)
        {
            if (!mask || string.IsNullOrEmpty(text)) return text ?? "";
            string masked = Regex.Replace(text, @"\b(?:\d{1,3}\.){3}\d{1,3}\b", m => Regex.Replace(m.Value, @"\d", "#"));
            masked = Regex.Replace(masked, @"(?:[0-9a-fA-F]{0,4}:){2,}[0-9a-fA-F:]+", m => Regex.Replace(m.Value, @"[0-9a-fA-F]", "#"));
            return masked;
        }

        public static string GetKnownNetworkOrg(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return "";
            string clean = ip.Trim('[', ']');
            if (clean.StartsWith("140.82.")) return "GitHub";
            if (clean.StartsWith("172.16.")) return "Cloudflare WARP / Tunnel";
            if (clean.StartsWith("1.1.1.") || clean.StartsWith("1.0.0.") || clean.StartsWith("104.16.") || clean.StartsWith("104.17.") ||
                clean.StartsWith("104.18.") || clean.StartsWith("104.19.") || clean.StartsWith("172.64.") || clean.StartsWith("172.67.") ||
                clean.StartsWith("2606:4700:")) return "Cloudflare Network";
            if (clean.StartsWith("142.250.") || clean.StartsWith("172.217.") || clean.StartsWith("74.125.") || clean.StartsWith("216.58.") ||
                clean.StartsWith("2001:4860:") || clean.StartsWith("2607:f8b0:")) return "Google LLC";
            if (clean.StartsWith("20.") || clean.StartsWith("40.") || clean.StartsWith("52.") || clean.StartsWith("13.107.") ||
                clean.StartsWith("204.79.197.") || clean.StartsWith("2603:") || clean.StartsWith("2620:1ec:")) return "Microsoft / Azure";
            if (clean.StartsWith("2600:9000:") || clean.StartsWith("54.") || clean.StartsWith("18.") || clean.StartsWith("13.32.") || clean.StartsWith("13.33.")) return "Amazon AWS / CloudFront";
            if (clean.StartsWith("2600:140") || clean.StartsWith("23.") || clean.StartsWith("104.64.") || clean.StartsWith("104.96.")) return "Akamai CDN";
            if (clean.StartsWith("151.101.") || clean.StartsWith("199.232.")) return "Fastly CDN";
            if (clean.StartsWith("162.159.") || clean.StartsWith("162.158.")) return "Cloudflare / Discord CDN";
            if (clean.StartsWith("155.133.") || clean.StartsWith("162.254.")) return "Valve / Steam";
            return "";
        }

        public static string GetCachedOrOrg(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return "";
            string cached;
            if (_dnsCache.TryGetValue(ip, out cached) && !string.IsNullOrEmpty(cached))
                return cached;
            return GetKnownNetworkOrg(ip);
        }

        public static void ResolveHostAsync(string ip, Action<string> onResolved)
        {
            if (string.IsNullOrWhiteSpace(ip)) return;
            string clean = ip.Trim('[', ']');
            if (clean.StartsWith("127.") || clean == "::1" || clean == "0.0.0.0") return;

            string existing;
            if (_dnsCache.TryGetValue(clean, out existing))
            {
                if (!string.IsNullOrEmpty(existing) && onResolved != null)
                    onResolved(existing);
                return;
            }

            if (!_inFlight.TryAdd(clean, 1)) return;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                string result = "";
                try
                {
                    IPAddress addr;
                    if (IPAddress.TryParse(clean, out addr))
                    {
                        IPHostEntry entry = Dns.GetHostEntry(addr);
                        if (entry != null && !string.IsNullOrWhiteSpace(entry.HostName) && entry.HostName != clean)
                        {
                            result = entry.HostName;
                        }
                    }
                }
                catch { }

                if (string.IsNullOrEmpty(result))
                {
                    result = GetKnownNetworkOrg(clean);
                }

                _dnsCache[clean] = result;
                byte removed;
                _inFlight.TryRemove(clean, out removed);

                if (!string.IsNullOrEmpty(result) && onResolved != null)
                {
                    onResolved(result);
                }
            });
        }
    }

    public static class SystemVitalsHelper
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

        private static ulong _prevIdle = 0;
        private static ulong _prevTotal = 0;
        private static bool _hasPrevCpu = false;

        private static ulong ToUInt64(FILETIME ft)
        {
            return ((ulong)ft.dwHighDateTime << 32) | ft.dwLowDateTime;
        }

        public static int GetMemoryLoadPercent()
        {
            try
            {
                MEMORYSTATUSEX mem = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(mem)) return (int)mem.dwMemoryLoad;
            }
            catch { }
            return 0;
        }

        public static int GetCpuLoadPercent()
        {
            try
            {
                FILETIME idleFt, kernelFt, userFt;
                if (GetSystemTimes(out idleFt, out kernelFt, out userFt))
                {
                    ulong idle = ToUInt64(idleFt);
                    ulong total = ToUInt64(kernelFt) + ToUInt64(userFt);
                    if (!_hasPrevCpu)
                    {
                        _prevIdle = idle;
                        _prevTotal = total;
                        _hasPrevCpu = true;
                        return 0;
                    }
                    ulong dIdle = idle - _prevIdle;
                    ulong dTotal = total - _prevTotal;
                    _prevIdle = idle;
                    _prevTotal = total;
                    if (dTotal > 0)
                    {
                        int pct = (int)Math.Round(((dTotal - dIdle) * 100.0) / dTotal);
                        if (pct < 0) pct = 0;
                        if (pct > 100) pct = 100;
                        return pct;
                    }
                }
            }
            catch { }
            return 0;
        }
    }

    public static class ProcessActionHelper
    {
        private static readonly HashSet<string> CriticalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "system", "idle", "smss", "csrss", "wininit", "services", "lsass", "winlogon",
            "dwm", "svchost", "explorer", "fontdrvhost", "sihost", "angelsystemloggerpro"
        };

        public static bool IsCriticalProcess(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return true;
            string clean = processName.Trim();
            if (clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                clean = clean.Substring(0, clean.Length - 4);
            return CriticalNames.Contains(clean);
        }

        public static bool IsAdministrator()
        {
            try
            {
                WindowsIdentity id = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(id);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        public static bool RestartAsAdministrator(string exePath)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    Verb = "runas"
                };
                Process.Start(psi);
                return true;
            }
            catch { return false; }
        }

        public static void OpenFileLocation(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;
            try
            {
                if (File.Exists(filePath))
                {
                    Process.Start("explorer.exe", string.Format("/select,\"{0}\"", filePath));
                }
                else
                {
                    string dir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        Process.Start("explorer.exe", string.Format("\"{0}\"", dir));
                }
            }
            catch { }
        }

        public static bool TryKillProcess(int pid, string processName, Window owner)
        {
            if (pid <= 4)
            {
                MessageBox.Show(owner, "Cannot terminate a core Windows kernel PID.", "Protected Process", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (IsCriticalProcess(processName))
            {
                MessageBox.Show(owner,
                    string.Format("{0} is a critical Windows system process and cannot be terminated from Angel Core Logger.", processName),
                    "Protected System Process", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            MessageBoxResult res = MessageBox.Show(owner,
                string.Format("Terminate {0} (PID {1})?\n\nUnsaved state in that process will be lost.", processName, pid),
                "Confirm Terminate Process", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return false;

            try
            {
                Process p = Process.GetProcessById(pid);
                p.Kill();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "Could not terminate process:\n" + ex.Message, "Permission or Exit Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        public static Brush MakeFrozenBrush(string hex)
        {
            SolidColorBrush b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }
    }

    public static class StartupManager
    {
        private const string StartupRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupValueName = "AngelSystemLoggerPro";

        public static bool IsRegisteredInStartup()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupRegistryPath, false))
                {
                    if (key == null) return false;
                    return key.GetValue(StartupValueName) != null;
                }
            }
            catch { return false; }
        }

        public static void SetStartupRegistry(string exePath, bool enable, bool startMinimized)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupRegistryPath, true))
                {
                    if (key == null) return;
                    if (enable)
                    {
                        string val = startMinimized
                            ? string.Format("\"{0}\" --minimized", exePath)
                            : string.Format("\"{0}\"", exePath);
                        key.SetValue(StartupValueName, val, RegistryValueKind.String);
                    }
                    else
                    {
                        if (key.GetValue(StartupValueName) != null)
                            key.DeleteValue(StartupValueName, false);
                    }
                }
            }
            catch { }
        }
    }
}
