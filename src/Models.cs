using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Web.Script.Serialization;
using System.Windows.Media;

namespace AngelSystemLoggerPro
{
    public class AppSettings
    {
        public bool MaskIp { get; set; }
        public bool DeepHiddenMode { get; set; }
        public bool StartMinimizedToTray { get; set; }
        public bool MinimizeToTrayOnClose { get; set; }
        public List<string> ExcludedProcesses { get; set; }

        public AppSettings()
        {
            MaskIp = true;
            DeepHiddenMode = true;
            StartMinimizedToTray = false;
            MinimizeToTrayOnClose = true;
            ExcludedProcesses = new List<string> { "AngelSystemLoggerPro", "Start-LocalLogger" };
        }

        public static string GetAppDataDir()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AngelSystemLoggerPro");
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static string GetSettingsPath()
        {
            return Path.Combine(GetAppDataDir(), "settings.json");
        }

        public static string GetScanTriggerPath()
        {
            return Path.Combine(GetAppDataDir(), "scan.trigger");
        }

        public static AppSettings Load()
        {
            string path = GetSettingsPath();
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    JavaScriptSerializer ser = new JavaScriptSerializer();
                    AppSettings loaded = ser.Deserialize<AppSettings>(json);
                    if (loaded != null)
                    {
                        if (loaded.ExcludedProcesses == null)
                            loaded.ExcludedProcesses = new List<string>();
                        return loaded;
                    }
                }
            }
            catch { }
            AppSettings defaults = new AppSettings();
            defaults.Save();
            return defaults;
        }

        public void Save()
        {
            try
            {
                JavaScriptSerializer ser = new JavaScriptSerializer();
                string json = ser.Serialize(this);
                File.WriteAllText(GetSettingsPath(), json);
            }
            catch { }
        }
    }

    public class ActivityEventItem : INotifyPropertyChanged
    {
        public string Time { get; set; }
        public string FullTime { get; set; }
        public string Category { get; set; }
        public string Badge { get; set; }
        public string RawTitle { get; set; }
        public string RawSubtitle { get; set; }
        public string RawDetails { get; set; }
        public string Severity { get; set; }

        public int Pid { get; set; }
        public string ProcessName { get; set; }
        public string ProcessPath { get; set; }
        public string ParentChain { get; set; }
        public string CommandLine { get; set; }
        public string Publisher { get; set; }
        public bool IsSigned { get; set; }
        public string RemoteIp { get; set; }
        public int RemotePort { get; set; }

        private string _resolvedHost;
        public string ResolvedHost
        {
            get { return _resolvedHost; }
            set
            {
                if (_resolvedHost != value)
                {
                    _resolvedHost = value;
                    OnPropertyChanged("ResolvedHost");
                    OnPropertyChanged("Subtitle");
                    OnPropertyChanged("EndpointDisplay");
                }
            }
        }

        private bool _maskIp = true;
        public bool MaskIp
        {
            get { return _maskIp; }
            set
            {
                if (_maskIp != value)
                {
                    _maskIp = value;
                    OnPropertyChanged("Title");
                    OnPropertyChanged("Subtitle");
                    OnPropertyChanged("Details");
                    OnPropertyChanged("EndpointDisplay");
                }
            }
        }

        public string Title
        {
            get { return IpPrivacyHelper.ApplyIpMask(RawTitle, _maskIp); }
        }

        public string Subtitle
        {
            get
            {
                string sub = IpPrivacyHelper.ApplyIpMask(RawSubtitle, _maskIp);
                if (!string.IsNullOrEmpty(_resolvedHost) &&
                    sub.IndexOf(_resolvedHost, StringComparison.OrdinalIgnoreCase) < 0 &&
                    (RawTitle ?? "").IndexOf(_resolvedHost, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return string.Format("{0} • Endpoint: {1}", sub, _resolvedHost);
                }
                return sub;
            }
        }

        public string Details
        {
            get { return IpPrivacyHelper.ApplyIpMask(RawDetails, _maskIp); }
        }

        public string EndpointDisplay
        {
            get
            {
                if (string.IsNullOrEmpty(RemoteIp)) return "";
                string ipText = IpPrivacyHelper.ApplyIpMask(RemoteIp, _maskIp);
                if (RemotePort > 0) ipText = string.Format("{0}:{1}", ipText, RemotePort);
                if (!string.IsNullOrEmpty(_resolvedHost))
                    return string.Format("{0} ({1})", ipText, _resolvedHost);
                return ipText;
            }
        }

        public string TrustText
        {
            get
            {
                if (string.IsNullOrEmpty(Publisher)) return "";
                return IsSigned ? ("✓ " + Publisher) : ("⚠ " + Publisher);
            }
        }

        public Brush BadgeBackground { get; set; }
        public Brush BadgeForeground { get; set; }
        public Brush DotBrush { get; set; }
        public Brush TrustBrush { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }

    public class HiddenProcessItem : INotifyPropertyChanged
    {
        public int Pid { get; set; }
        public string ProcessName { get; set; }
        public string ParentName { get; set; }
        public string ServiceLabel { get; set; }
        public string Role { get; set; }
        public string Publisher { get; set; }
        public bool IsSigned { get; set; }
        public bool IsMicrosoft { get; set; }
        public double MemoryMb { get; set; }
        public int ConnCount { get; set; }
        public string RawEndpoints { get; set; }
        public string ProcessPath { get; set; }
        public string CommandLine { get; set; }

        private bool _maskIp = true;
        public bool MaskIp
        {
            get { return _maskIp; }
            set
            {
                if (_maskIp != value)
                {
                    _maskIp = value;
                    OnPropertyChanged("Endpoints");
                    OnPropertyChanged("SummaryLine");
                }
            }
        }

        public string Endpoints
        {
            get { return IpPrivacyHelper.ApplyIpMask(RawEndpoints, _maskIp); }
        }

        public string ServiceOrRole
        {
            get
            {
                if (!string.IsNullOrEmpty(ServiceLabel))
                    return "Hosting Windows Service(s): " + ServiceLabel;
                return Role;
            }
        }

        public string SummaryLine
        {
            get
            {
                string netPart = ConnCount > 0
                    ? string.Format("  |  {0} external connection(s): {1}", ConnCount, Endpoints)
                    : "  |  No external connections";
                return string.Format("PID {0}  •  Parent: {1}  •  {2:0.0} MB RAM{3}", Pid, ParentName, MemoryMb, netPart);
            }
        }

        public string TrustText
        {
            get { return IsSigned ? ("✓ " + Publisher) : ("⚠ " + Publisher); }
        }

        public Brush TrustBg { get; set; }
        public Brush TrustFg { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }

    public class NetworkConnectionItem : INotifyPropertyChanged
    {
        public int Pid { get; set; }
        public string ProcessName { get; set; }
        public string ServiceLabel { get; set; }
        public string RawLocalEndpoint { get; set; }
        public string RawRemoteEndpoint { get; set; }
        public string RemoteIp { get; set; }
        public int RemotePort { get; set; }
        public string State { get; set; }
        public string PortMeaning { get; set; }
        public bool IsHidden { get; set; }
        public string Publisher { get; set; }
        public bool IsSigned { get; set; }
        public string ProcessPath { get; set; }

        private string _resolvedHost;
        public string ResolvedHost
        {
            get { return _resolvedHost; }
            set
            {
                if (_resolvedHost != value)
                {
                    _resolvedHost = value;
                    OnPropertyChanged("ResolvedHost");
                    OnPropertyChanged("EndpointLine");
                }
            }
        }

        private bool _maskIp = true;
        public bool MaskIp
        {
            get { return _maskIp; }
            set
            {
                if (_maskIp != value)
                {
                    _maskIp = value;
                    OnPropertyChanged("EndpointLine");
                }
            }
        }

        public string VisibilityTag { get { return IsHidden ? "HIDDEN BG" : "VISIBLE APP"; } }
        public Brush VisibilityBg { get; set; }
        public Brush VisibilityFg { get; set; }

        public string DisplayProcess
        {
            get
            {
                if (!string.IsNullOrEmpty(ServiceLabel))
                    return string.Format("{0}  [{1}]", ProcessName, ServiceLabel);
                return ProcessName;
            }
        }

        public string EndpointLine
        {
            get
            {
                string rem = IpPrivacyHelper.ApplyIpMask(RawRemoteEndpoint, _maskIp);
                string hostPart = !string.IsNullOrEmpty(_resolvedHost) ? string.Format("  ({0})", _resolvedHost) : "";
                return string.Format("Remote: {0}{1}  •  {2}", rem, hostPart, PortMeaning);
            }
        }

        public string TrustMeta
        {
            get { return string.Format("PID {0}  •  {1}  •  State: {2}", Pid, IsSigned ? ("✓ " + Publisher) : ("⚠ " + Publisher), State); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }

    public class ListeningPortItem : INotifyPropertyChanged
    {
        public int Pid { get; set; }
        public string ProcessName { get; set; }
        public string ServiceLabel { get; set; }
        public string RawLocalAddress { get; set; }
        public int LocalPort { get; set; }
        public string PortMeaning { get; set; }
        public bool IsHidden { get; set; }
        public string Publisher { get; set; }
        public bool IsSigned { get; set; }
        public string ProcessPath { get; set; }

        private bool _maskIp = true;
        public bool MaskIp
        {
            get { return _maskIp; }
            set
            {
                if (_maskIp != value)
                {
                    _maskIp = value;
                    OnPropertyChanged("BindLine");
                }
            }
        }

        public string DisplayProcess
        {
            get
            {
                if (!string.IsNullOrEmpty(ServiceLabel))
                    return string.Format("{0}  [{1}]", ProcessName, ServiceLabel);
                return ProcessName;
            }
        }

        public string BindLine
        {
            get
            {
                string addr = RawLocalAddress;
                if (_maskIp && addr != "0.0.0.0" && addr != "127.0.0.1" && addr != "::" && addr != "::1")
                    addr = IpPrivacyHelper.ApplyIpMask(addr, true);
                return string.Format("Listening on {0}:{1}  •  {2}", addr, LocalPort, PortMeaning);
            }
        }

        public string TrustMeta
        {
            get { return string.Format("PID {0}  •  {1}  •  {2}", Pid, IsHidden ? "Background Process" : "Visible App", IsSigned ? ("✓ " + Publisher) : ("⚠ " + Publisher)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }

    public class PrivacyAccessItem
    {
        public string Capability { get; set; }
        public string AppName { get; set; }
        public string AppPath { get; set; }
        public bool IsActive { get; set; }
        public string StartTime { get; set; }
        public string StopTime { get; set; }
        public string Duration { get; set; }

        public string StatusBadge { get { return IsActive ? "● ACTIVE NOW" : Capability.ToUpperInvariant(); } }
        public string DetailsLine { get { return string.Format("Last Used: {0}  •  Duration: {1}  •  {2}", StartTime, Duration, AppPath); } }
        public Brush BadgeBg { get; set; }
        public Brush BadgeFg { get; set; }
    }

    public class StartupTaskItem
    {
        public string Kind { get; set; }
        public string Name { get; set; }
        public string Command { get; set; }
        public string Publisher { get; set; }
        public bool IsSigned { get; set; }
        public string State { get; set; }
        public string Description { get; set; }

        public string MetaLine { get { return string.Format("{0}  •  {1}  •  {2}", Kind, State, IsSigned ? ("✓ " + Publisher) : ("⚠ " + Publisher)); } }
        public Brush KindBg { get; set; }
        public Brush KindFg { get; set; }
    }
}
