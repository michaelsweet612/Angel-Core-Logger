using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Ellipse = System.Windows.Shapes.Ellipse;

[assembly: AssemblyTitle("Angel Core Logger")]
[assembly: AssemblyDescription("Angel Core Logger - Friendly PowerShell Deep Background & System Activity Monitor")]
[assembly: AssemblyCompany("Angel Core Logger")]
[assembly: AssemblyProduct("Angel Core Logger")]
[assembly: AssemblyCopyright("Copyright (c) 2026")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

namespace AngelSystemLoggerPro
{
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
                }
            }
        }

        public string Title { get { return ApplyIpMask(RawTitle, _maskIp); } }
        public string Subtitle { get { return ApplyIpMask(RawSubtitle, _maskIp); } }
        public string Details { get { return ApplyIpMask(RawDetails, _maskIp); } }

        public Brush BadgeBackground { get; set; }
        public Brush BadgeForeground { get; set; }
        public Brush DotBrush { get; set; }

        public static string ApplyIpMask(string text, bool mask)
        {
            if (!mask || string.IsNullOrEmpty(text)) return text ?? "";
            string masked = Regex.Replace(text, @"\b(?:\d{1,3}\.){3}\d{1,3}\b", m => Regex.Replace(m.Value, @"\d", "#"));
            masked = Regex.Replace(masked, @"\b(?:[0-9a-fA-F]{1,4}:){2,}[0-9a-fA-F:]+\b", m => Regex.Replace(m.Value, @"[0-9a-fA-F]", "#"));
            return masked;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }

    internal static class Program
    {
        private const string StartupRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupValueName = "AngelSystemLoggerPro";

        [STAThread]
        private static int Main(string[] args)
        {
            string exePath = Assembly.GetExecutingAssembly().Location;

            string previewPath = null;
            if (args.Length > 0)
            {
                string arg0 = args[0].Trim().ToLowerInvariant();
                if (arg0 == "--install-startup" || arg0 == "/install-startup" || arg0 == "/startup")
                {
                    SetStartupRegistry(exePath, true);
                    return 0;
                }
                if (arg0 == "--remove-startup" || arg0 == "/remove-startup" || arg0 == "/uninstall")
                {
                    SetStartupRegistry(exePath, false);
                    return 0;
                }
                if (arg0 == "--render-preview" && args.Length > 1)
                {
                    previewPath = args[1];
                }
            }

            if (!IsRegisteredInStartup())
            {
                SetStartupRegistry(exePath, true);
            }

            try
            {
                Application app = new Application();
                MainWindow mainWindow = new MainWindow(exePath, previewPath);
                app.Run(mainWindow);
            }
            catch (Exception ex)
            {
                string errLog = Path.Combine(Path.GetDirectoryName(exePath) ?? "", "startup-error.log");
                File.WriteAllText(errLog, ex.ToString());
                MessageBox.Show("Angel Core Logger encountered an error:\n" + ex.Message, "Angel Core Logger", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
            return 0;
        }

        public static bool IsRegisteredInStartup()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupRegistryPath, false))
            {
                if (key == null) return false;
                return key.GetValue(StartupValueName) != null;
            }
        }

        public static void SetStartupRegistry(string exePath, bool enable)
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

    public class MainWindow : Window
    {
        private readonly string _exePath;
        private readonly string _exeDir;
        private readonly string _logDir;
        private Process _psEngineProcess;

        private readonly ObservableCollection<ActivityEventItem> _allEvents = new ObservableCollection<ActivityEventItem>();
        private ICollectionView _filteredView;

        private bool _maskIp = true;
        private bool _deepBackground = true;
        private bool _isPaused = false;
        private string _selectedCategory = "ALL";
        private string _searchQuery = "";

        private int _totalCount = 0;
        private int _hiddenCount = 0;
        private int _networkCount = 0;
        private int _dnsCount = 0;
        private int _appCount = 0;
        private int _taskSvcCount = 0;

        // UI Controls
        private TextBlock _engineStatusText;
        private Ellipse _engineStatusDot;
        private TextBlock _totalStatText;
        private TextBlock _hiddenStatText;
        private TextBlock _netStatText;
        private TextBlock _dnsStatText;
        private TextBlock _appStatText;
        private TextBlock _taskStatText;
        private TextBlock _diskFreeText;
        private TextBlock _selectedDetailsText;
        private Border _emptyStatePanel;
        private ListBox _eventsListBox;
        private Button _categoryDropBtn;
        private TextBlock _categoryDropLabel;
        private Popup _categoryPopup;
        private TextBox _searchBox;
        private ToggleButton _ipMaskToggle;
        private ToggleButton _deepBgToggle;
        private ToggleButton _startupToggle;
        private Button _liveModeBtn;
        private Button _pauseModeBtn;

        public MainWindow(string exePath, string previewOutPath = null)
        {
            _exePath = exePath;
            _exeDir = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory;
            _logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LocalActivityLogs");
            Directory.CreateDirectory(_logDir);

            this.Title = "Angel Core Logger";
            this.Width = 980;
            this.Height = 680;
            this.MinWidth = 820;
            this.MinHeight = 540;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.WindowStyle = WindowStyle.None;
            this.AllowsTransparency = true;
            this.Background = Brushes.Transparent;
            this.FontFamily = new FontFamily("Segoe UI");

            InitializeWindowLayout();
            SetupCollectionView();
            UpdateDiskSpaceInfo();
            StartPowerShellEngine();

            // Automatically run an initial Deep Background X-Ray scan on launch so hidden activity is immediately visible
            DispatcherTimer initialXrayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            initialXrayTimer.Tick += (s, e) =>
            {
                initialXrayTimer.Stop();
                RunDeepBackgroundXRayScan();
            };
            initialXrayTimer.Start();

            if (!string.IsNullOrEmpty(previewOutPath))
            {
                DispatcherTimer snapTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(4500) };
                snapTimer.Tick += (s, e) =>
                {
                    snapTimer.Stop();
                    try
                    {
                        FrameworkElement content = this.Content as FrameworkElement;
                        if (content != null)
                        {
                            RenderTargetBitmap rtb = new RenderTargetBitmap(
                                (int)this.ActualWidth, (int)this.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                            rtb.Render(content);
                            PngBitmapEncoder enc = new PngBitmapEncoder();
                            enc.Frames.Add(BitmapFrame.Create(rtb));
                            using (FileStream fs = new FileStream(previewOutPath, FileMode.Create, FileAccess.Write))
                            {
                                enc.Save(fs);
                            }
                        }
                    }
                    catch { }
                    this.Close();
                };
                snapTimer.Start();
            }

            DispatcherTimer diskTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            diskTimer.Tick += (s, e) => UpdateDiskSpaceInfo();
            diskTimer.Start();

            this.Closed += (s, e) => StopPowerShellEngine();
        }

        private void InitializeWindowLayout()
        {
            string xaml = @"
<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        CornerRadius='10' Background='#18181B' BorderBrush='#A82479' BorderThickness='1.5' ClipToBounds='True'>

    <Border.Resources>
        <!-- Sleek Toggle Switch Style matching reference UI -->
        <Style x:Key='PillSwitchStyle' TargetType='ToggleButton'>
            <Setter Property='Width' Value='42'/>
            <Setter Property='Height' Value='22'/>
            <Setter Property='Cursor' Value='Hand'/>
            <Setter Property='Template'>
                <Setter.Value>
                    <ControlTemplate TargetType='ToggleButton'>
                        <Border x:Name='Track' CornerRadius='11' Background='#323238' BorderBrush='#42424A' BorderThickness='1' Padding='2'>
                            <Ellipse x:Name='Thumb' Width='16' Height='16' Fill='#F4F4F6' HorizontalAlignment='Left'/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property='IsChecked' Value='True'>
                                <Setter TargetName='Track' Property='Background' Value='#34D399'/>
                                <Setter TargetName='Track' Property='BorderBrush' Value='#10B981'/>
                                <Setter TargetName='Thumb' Property='HorizontalAlignment' Value='Right'/>
                                <Setter TargetName='Thumb' Property='Fill' Value='#141417'/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <!-- Gold Primary Button Style matching '+ New download' button -->
        <Style x:Key='GoldActionBtn' TargetType='Button'>
            <Setter Property='Background' Value='#D5A449'/>
            <Setter Property='Foreground' Value='#161618'/>
            <Setter Property='FontWeight' Value='SemiBold'/>
            <Setter Property='FontSize' Value='13'/>
            <Setter Property='Cursor' Value='Hand'/>
            <Setter Property='Padding' Value='14,7'/>
            <Setter Property='Template'>
                <Setter.Value>
                    <ControlTemplate TargetType='Button'>
                        <Border x:Name='Bd' Background='{TemplateBinding Background}' CornerRadius='7' Padding='{TemplateBinding Padding}'>
                            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property='IsMouseOver' Value='True'>
                                <Setter TargetName='Bd' Property='Background' Value='#E2B45B'/>
                            </Trigger>
                            <Trigger Property='IsPressed' Value='True'>
                                <Setter TargetName='Bd' Property='Background' Value='#C09038'/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <!-- Dark Pill Button Style -->
        <Style x:Key='DarkPillBtn' TargetType='Button'>
            <Setter Property='Background' Value='#26262C'/>
            <Setter Property='Foreground' Value='#E4E4E7'/>
            <Setter Property='BorderBrush' Value='#35353E'/>
            <Setter Property='FontSize' Value='12.5'/>
            <Setter Property='Cursor' Value='Hand'/>
            <Setter Property='Padding' Value='12,6'/>
            <Setter Property='Template'>
                <Setter.Value>
                    <ControlTemplate TargetType='Button'>
                        <Border x:Name='Bd' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='7' Padding='{TemplateBinding Padding}'>
                            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property='IsMouseOver' Value='True'>
                                <Setter TargetName='Bd' Property='Background' Value='#2F2F37'/>
                                <Setter TargetName='Bd' Property='BorderBrush' Value='#484854'/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <!-- Category Menu Item Button -->
        <Style x:Key='CatMenuBtn' TargetType='Button'>
            <Setter Property='Background' Value='Transparent'/>
            <Setter Property='Foreground' Value='#E4E4E7'/>
            <Setter Property='FontSize' Value='13'/>
            <Setter Property='HorizontalContentAlignment' Value='Left'/>
            <Setter Property='Padding' Value='12,8'/>
            <Setter Property='Cursor' Value='Hand'/>
            <Setter Property='Template'>
                <Setter.Value>
                    <ControlTemplate TargetType='Button'>
                        <Border x:Name='Bd' Background='{TemplateBinding Background}' CornerRadius='5' Padding='{TemplateBinding Padding}'>
                            <ContentPresenter HorizontalAlignment='Left' VerticalAlignment='Center'/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property='IsMouseOver' Value='True'>
                                <Setter TargetName='Bd' Property='Background' Value='#2E2E37'/>
                                <Setter Property='Foreground' Value='#D5A449'/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <!-- Window Caption Button -->
        <Style x:Key='CaptionBtn' TargetType='Button'>
            <Setter Property='Width' Value='42'/>
            <Setter Property='Height' Value='34'/>
            <Setter Property='Background' Value='Transparent'/>
            <Setter Property='Foreground' Value='#A1A1AA'/>
            <Setter Property='FontSize' Value='13'/>
            <Setter Property='BorderThickness' Value='0'/>
            <Setter Property='Cursor' Value='Hand'/>
            <Setter Property='Template'>
                <Setter.Value>
                    <ControlTemplate TargetType='Button'>
                        <Border x:Name='Bd' Background='{TemplateBinding Background}'>
                            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property='IsMouseOver' Value='True'>
                                <Setter TargetName='Bd' Property='Background' Value='#2D2D35'/>
                                <Setter Property='Foreground' Value='#FFFFFF'/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <!-- Dark Minimalist ScrollBar -->
        <Style TargetType='ScrollBar'>
            <Setter Property='Width' Value='8'/>
            <Setter Property='Background' Value='Transparent'/>
            <Setter Property='Template'>
                <Setter.Value>
                    <ControlTemplate TargetType='ScrollBar'>
                        <Grid Background='#18181B' Width='8'>
                            <Track x:Name='PART_Track' IsDirectionReversed='True'>
                                <Track.Thumb>
                                    <Thumb>
                                        <Thumb.Template>
                                            <ControlTemplate TargetType='Thumb'>
                                                <Border CornerRadius='4' Background='#32323C' Margin='1,2'/>
                                            </ControlTemplate>
                                        </Thumb.Template>
                                    </Thumb>
                                </Track.Thumb>
                            </Track>
                        </Grid>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </Border.Resources>

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height='38'/>
            <RowDefinition Height='56'/>
            <RowDefinition Height='Auto'/>
            <RowDefinition Height='*'/>
            <RowDefinition Height='Auto'/>
            <RowDefinition Height='46'/>
        </Grid.RowDefinitions>

        <!-- 1. CUSTOM TITLE BAR -->
        <Border x:Name='TitleBarDragArea' Grid.Row='0' Background='#1F1F23' BorderBrush='#28282E' BorderThickness='0,0,0,1'>
            <Grid>
                <StackPanel Orientation='Horizontal' HorizontalAlignment='Left' VerticalAlignment='Center' Margin='14,0,0,0'>
                    <Ellipse Width='8' Height='8' Fill='#34D399' Margin='0,0,8,0'/>
                    <TextBlock Text='PS' FontSize='10' FontWeight='Bold' Foreground='#34D399' Opacity='0.85'/>
                </StackPanel>

                <TextBlock Text='Angel Core Logger' Foreground='#D4D4D8' FontSize='13' FontWeight='Medium'
                           HorizontalAlignment='Center' VerticalAlignment='Center'/>

                <StackPanel Orientation='Horizontal' HorizontalAlignment='Right' VerticalAlignment='Center'>
                    <Button x:Name='MinBtn' Style='{StaticResource CaptionBtn}' Content='—' ToolTip='Minimize'/>
                    <Button x:Name='MaxBtn' Style='{StaticResource CaptionBtn}' Content='▢' ToolTip='Maximize / Restore'/>
                    <Button x:Name='CloseBtn' Style='{StaticResource CaptionBtn}' Content='✕' ToolTip='Close'/>
                </StackPanel>
            </Grid>
        </Border>

        <!-- 2. TOP ACTION & FILTER HEADER (Plexo-style bar) -->
        <Border Grid.Row='1' Background='#1F1F23' BorderBrush='#2A2A31' BorderThickness='0,0,0,1' Padding='18,0'>
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width='Auto'/>
                    <ColumnDefinition Width='245'/>
                    <ColumnDefinition Width='*'/>
                    <ColumnDefinition Width='Auto'/>
                </Grid.ColumnDefinitions>

                <!-- Dark Category Filter Dropdown Button ('All activity ⌄') -->
                <Grid Grid.Column='0' VerticalAlignment='Center'>
                    <Button x:Name='CategoryDropBtn' Style='{StaticResource DarkPillBtn}' Padding='14,7'>
                        <StackPanel Orientation='Horizontal'>
                            <TextBlock x:Name='CategoryDropLabel' Text='All activity' Foreground='#F4F4F5' FontSize='13.5' FontWeight='SemiBold' VerticalAlignment='Center'/>
                            <TextBlock Text='  ⌄' Foreground='#A1A1AA' FontSize='13' FontWeight='Bold' VerticalAlignment='Center' Margin='4,-2,0,0'/>
                        </StackPanel>
                    </Button>
                    <Popup x:Name='CategoryPopup' PlacementTarget='{Binding ElementName=CategoryDropBtn}' Placement='Bottom' StaysOpen='False' AllowsTransparency='True'>
                        <Border Background='#222228' BorderBrush='#383844' BorderThickness='1' CornerRadius='8' Padding='6' Margin='0,4,0,0' Width='210'>
                            <StackPanel>
                                <Button x:Name='CatAllBtn' Style='{StaticResource CatMenuBtn}' Content='All activity' Tag='ALL'/>
                                <Button x:Name='CatHiddenBtn' Style='{StaticResource CatMenuBtn}' Content='Hidden background' Tag='HIDDEN'/>
                                <Button x:Name='CatNetBtn' Style='{StaticResource CatMenuBtn}' Content='Network &amp; connections' Tag='NETWORK'/>
                                <Button x:Name='CatDnsBtn' Style='{StaticResource CatMenuBtn}' Content='DNS &amp; websites' Tag='DNS'/>
                                <Button x:Name='CatAppBtn' Style='{StaticResource CatMenuBtn}' Content='Apps &amp; windows' Tag='APP_WIN'/>
                                <Button x:Name='CatTaskBtn' Style='{StaticResource CatMenuBtn}' Content='Tasks &amp; services' Tag='TASK_SVC'/>
                                <Button x:Name='CatSecBtn' Style='{StaticResource CatMenuBtn}' Content='Security &amp; startup' Tag='SECURITY'/>
                            </StackPanel>
                        </Border>
                    </Popup>
                </Grid>

                <!-- Quick Filter Search Box -->
                <Border Grid.Column='1' Margin='12,0,0,0' Height='34' CornerRadius='7' Background='#161619' BorderBrush='#2E2E36' BorderThickness='1' VerticalAlignment='Center'>
                    <Grid>
                        <TextBox x:Name='SearchBox' Background='Transparent' Foreground='#F4F4F5' CaretBrush='#D5A449'
                                 BorderThickness='0' VerticalContentAlignment='Center' Padding='10,0,26,0' FontSize='12.5'
                                 ToolTip='Search by process name, domain, PID, command line, or port'/>
                        <TextBlock Text='⌕' Foreground='#71717A' FontSize='14' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,9,0' IsHitTestVisible='False'/>
                    </Grid>
                </Border>

                <!-- Right Status Pill & Gold Action Buttons -->
                <StackPanel Grid.Column='3' Orientation='Horizontal' VerticalAlignment='Center'>
                    <!-- Live PowerShell Engine Pill -->
                    <Border CornerRadius='7' Background='#25252B' BorderBrush='#34343D' BorderThickness='1' Padding='12,6' Margin='0,0,10,0' VerticalAlignment='Center'>
                        <StackPanel Orientation='Horizontal' VerticalAlignment='Center'>
                            <Ellipse x:Name='EngineStatusDot' Width='8' Height='8' Fill='#34D399' Margin='0,0,8,0' VerticalAlignment='Center'/>
                            <TextBlock x:Name='EngineStatusText' Text='PowerShell Live' Foreground='#E4E4E7' FontSize='12.5' FontWeight='Medium' VerticalAlignment='Center'/>
                        </StackPanel>
                    </Border>

                    <Button x:Name='OpenLogsBtn' Style='{StaticResource DarkPillBtn}' Content='Open Log Folder' Margin='0,0,10,0' ToolTip='Open Documents\LocalActivityLogs in Explorer'/>
                    <Button x:Name='DeepScanBtn' Style='{StaticResource GoldActionBtn}' Content='+ Deep Background Scan' ToolTip='Run an immediate PowerShell X-Ray scan of hidden background processes, sockets, and tasks'/>
                </StackPanel>
            </Grid>
        </Border>

        <!-- 3. VISUAL TELEMETRY SCHEMATIC & LIVE METRICS STRIP -->
        <Border Grid.Row='2' Background='#1B1B1F' BorderBrush='#26262C' BorderThickness='0,0,0,1' Padding='18,10'>
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width='220'/>
                    <ColumnDefinition Width='*'/>
                </Grid.ColumnDefinitions>

                <!-- Mini Flow Diagram (inspired by WI-FI / USB / ETHERNET -> arrow in screenshot) -->
                <Canvas Grid.Column='0' Width='210' Height='52' VerticalAlignment='Center' HorizontalAlignment='Left'>
                    <TextBlock Text='BG SPAWNS' FontSize='8.5' FontFamily='Consolas' Foreground='#71717A' Canvas.Left='2' Canvas.Top='3'/>
                    <TextBlock Text='NET / DNS' FontSize='8.5' FontFamily='Consolas' Foreground='#71717A' Canvas.Left='2' Canvas.Top='20'/>
                    <TextBlock Text='SCHED TASKS' FontSize='8.5' FontFamily='Consolas' Foreground='#71717A' Canvas.Left='2' Canvas.Top='37'/>

                    <Ellipse Width='6' Height='6' Fill='#D5A449' Canvas.Left='68' Canvas.Top='5'/>
                    <Ellipse Width='6' Height='6' Fill='#34D399' Canvas.Left='68' Canvas.Top='22'/>
                    <Ellipse Width='6' Height='6' Fill='#60A5FA' Canvas.Left='68' Canvas.Top='39'/>

                    <Path Stroke='#52525B' StrokeThickness='1.5' StrokeDashArray='3,2' Data='M 74,8 C 96,8 102,25 122,25'/>
                    <Path Stroke='#52525B' StrokeThickness='1.5' StrokeDashArray='3,2' Data='M 74,25 L 122,25'/>
                    <Path Stroke='#52525B' StrokeThickness='1.5' StrokeDashArray='3,2' Data='M 74,42 C 96,42 102,25 122,25'/>

                    <Ellipse Width='14' Height='14' Stroke='#71717A' StrokeThickness='1.2' StrokeDashArray='2,2' Canvas.Left='116' Canvas.Top='18'/>
                    <Path Stroke='#34D399' StrokeThickness='2.5' Data='M 124,25 L 166,25'/>
                    <Polygon Points='164,20 173,25 164,30' Fill='#34D399'/>
                    <TextBlock Text='PS ENGINE' FontSize='8.5' FontWeight='Bold' FontFamily='Consolas' Foreground='#A1A1AA' Canvas.Left='132' Canvas.Top='6'/>
                </Canvas>

                <!-- Live Category Counters -->
                <UniformGrid Grid.Column='1' Columns='6' VerticalAlignment='Center'>
                    <Border Background='#222227' BorderBrush='#2E2E36' BorderThickness='1' CornerRadius='7' Padding='10,6' Margin='4,0'>
                        <StackPanel>
                            <TextBlock Text='TOTAL EVENTS' FontSize='9.5' FontWeight='Bold' Foreground='#71717A'/>
                            <TextBlock x:Name='TotalStatText' Text='0' FontSize='17' FontWeight='Bold' Foreground='#F4F4F5' Margin='0,1,0,0'/>
                        </StackPanel>
                    </Border>
                    <Border Background='#222227' BorderBrush='#3A3020' BorderThickness='1' CornerRadius='7' Padding='10,6' Margin='4,0'>
                        <StackPanel>
                            <TextBlock Text='HIDDEN BG' FontSize='9.5' FontWeight='Bold' Foreground='#D5A449'/>
                            <TextBlock x:Name='HiddenStatText' Text='0' FontSize='17' FontWeight='Bold' Foreground='#FBBF24' Margin='0,1,0,0'/>
                        </StackPanel>
                    </Border>
                    <Border Background='#222227' BorderBrush='#1E3A34' BorderThickness='1' CornerRadius='7' Padding='10,6' Margin='4,0'>
                        <StackPanel>
                            <TextBlock Text='NETWORK' FontSize='9.5' FontWeight='Bold' Foreground='#34D399'/>
                            <TextBlock x:Name='NetStatText' Text='0' FontSize='17' FontWeight='Bold' Foreground='#34D399' Margin='0,1,0,0'/>
                        </StackPanel>
                    </Border>
                    <Border Background='#222227' BorderBrush='#382640' BorderThickness='1' CornerRadius='7' Padding='10,6' Margin='4,0'>
                        <StackPanel>
                            <TextBlock Text='DNS / WEB' FontSize='9.5' FontWeight='Bold' Foreground='#C084FC'/>
                            <TextBlock x:Name='DnsStatText' Text='0' FontSize='17' FontWeight='Bold' Foreground='#C084FC' Margin='0,1,0,0'/>
                        </StackPanel>
                    </Border>
                    <Border Background='#222227' BorderBrush='#23354D' BorderThickness='1' CornerRadius='7' Padding='10,6' Margin='4,0'>
                        <StackPanel>
                            <TextBlock Text='APPS &amp; TABS' FontSize='9.5' FontWeight='Bold' Foreground='#60A5FA'/>
                            <TextBlock x:Name='AppStatText' Text='0' FontSize='17' FontWeight='Bold' Foreground='#60A5FA' Margin='0,1,0,0'/>
                        </StackPanel>
                    </Border>
                    <Border Background='#222227' BorderBrush='#3D262A' BorderThickness='1' CornerRadius='7' Padding='10,6' Margin='4,0'>
                        <StackPanel>
                            <TextBlock Text='TASKS &amp; SVC' FontSize='9.5' FontWeight='Bold' Foreground='#F87171'/>
                            <TextBlock x:Name='TaskStatText' Text='0' FontSize='17' FontWeight='Bold' Foreground='#F87171' Margin='0,1,0,0'/>
                        </StackPanel>
                    </Border>
                </UniformGrid>
            </Grid>
        </Border>

        <!-- 4. MAIN ACTIVITY STREAM LIST OR EMPTY STATE -->
        <Grid Grid.Row='3' Background='#18181B'>
            <Border x:Name='EmptyStatePanel' Visibility='Collapsed' HorizontalAlignment='Center' VerticalAlignment='Center'>
                <StackPanel HorizontalAlignment='Center'>
                    <TextBlock Text='Listening for background &amp; system activity...' FontSize='17' FontWeight='SemiBold' Foreground='#F4F4F5' HorizontalAlignment='Center'/>
                    <TextBlock Text='PowerShell is monitoring hidden process spawns, network connections, DNS queries, and scheduled tasks in real time.'
                               FontSize='13' Foreground='#71717A' HorizontalAlignment='Center' Margin='0,8,0,18'/>
                </StackPanel>
            </Border>

            <ListBox x:Name='EventsListBox' Background='Transparent' BorderThickness='0'
                     ScrollViewer.HorizontalScrollBarVisibility='Disabled'
                     VirtualizingStackPanel.IsVirtualizing='True'
                     VirtualizingStackPanel.VirtualizationMode='Recycling'
                     Padding='14,10'>
                <ListBox.ItemContainerStyle>
                    <Style TargetType='ListBoxItem'>
                        <Setter Property='HorizontalContentAlignment' Value='Stretch'/>
                        <Setter Property='Padding' Value='0'/>
                        <Setter Property='Margin' Value='0,0,0,7'/>
                        <Setter Property='Background' Value='Transparent'/>
                        <Setter Property='Template'>
                            <Setter.Value>
                                <ControlTemplate TargetType='ListBoxItem'>
                                    <ContentPresenter/>
                                </ControlTemplate>
                            </Setter.Value>
                        </Setter>
                    </Style>
                </ListBox.ItemContainerStyle>
                <ListBox.ItemTemplate>
                    <DataTemplate>
                        <Border CornerRadius='8' Background='#202025' BorderBrush='#2B2B33' BorderThickness='1' Padding='13,10' Cursor='Hand'>
                            <Grid>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width='126'/>
                                    <ColumnDefinition Width='*'/>
                                    <ColumnDefinition Width='76'/>
                                </Grid.ColumnDefinitions>

                                <!-- Category Badge Pill -->
                                <Border Grid.Column='0' Background='{Binding BadgeBackground}' CornerRadius='6' Padding='8,4' HorizontalAlignment='Left' VerticalAlignment='Top' Margin='0,1,0,0'>
                                    <StackPanel Orientation='Horizontal' VerticalAlignment='Center'>
                                        <Ellipse Width='6' Height='6' Fill='{Binding DotBrush}' Margin='0,0,6,0' VerticalAlignment='Center'/>
                                        <TextBlock Text='{Binding Badge}' Foreground='{Binding BadgeForeground}' FontSize='10.5' FontWeight='Bold' FontFamily='Consolas'/>
                                    </StackPanel>
                                </Border>

                                <!-- Event Friendly Title, Plain-English Explanation & Technical Details -->
                                <StackPanel Grid.Column='1' Margin='6,0,12,0'>
                                    <TextBlock Text='{Binding Title}' Foreground='#F4F4F5' FontSize='13.5' FontWeight='SemiBold' TextWrapping='Wrap'/>
                                    <TextBlock Text='{Binding Subtitle}' Foreground='#A1A1AA' FontSize='12' Margin='0,3,0,0' TextWrapping='Wrap'/>
                                    <TextBlock Text='{Binding Details}' Foreground='#71717A' FontSize='11' FontFamily='Consolas' Margin='0,4,0,0' TextWrapping='Wrap'/>
                                </StackPanel>

                                <!-- Timestamp -->
                                <TextBlock Grid.Column='2' Text='{Binding Time}' Foreground='#71717A' FontSize='11.5' FontFamily='Consolas'
                                           HorizontalAlignment='Right' VerticalAlignment='Top' Margin='0,2,0,0'/>
                            </Grid>
                        </Border>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>
        </Grid>

        <!-- 5. INSPECTOR / SELECTED EVENT BAR -->
        <Border Grid.Row='4' Background='#1C1C20' BorderBrush='#28282F' BorderThickness='0,1,0,0' Padding='18,7'>
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width='*'/>
                    <ColumnDefinition Width='Auto'/>
                </Grid.ColumnDefinitions>
                <TextBlock x:Name='SelectedDetailsText' Grid.Column='0'
                           Text='Tip: Click any activity card above to inspect its full command line &amp; parent process chain, or press Copy.'
                           Foreground='#A1A1AA' FontSize='11.5' FontFamily='Consolas' VerticalAlignment='Center' TextTrimming='CharacterEllipsis'/>
                <Button x:Name='CopyEventBtn' Grid.Column='1' Style='{StaticResource DarkPillBtn}' Content='Copy Details' Padding='10,3' Margin='10,0,0,0'/>
            </Grid>
        </Border>

        <!-- 6. BOTTOM STATUS & TOGGLE BAR (Plexo-style footer) -->
        <Border Grid.Row='5' Background='#1F1F23' BorderBrush='#2A2A31' BorderThickness='0,1,0,0' Padding='18,0'>
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width='Auto'/>
                    <ColumnDefinition Width='*'/>
                    <ColumnDefinition Width='Auto'/>
                </Grid.ColumnDefinitions>

                <!-- Disk Free Space & Local Log Status -->
                <StackPanel Grid.Column='0' Orientation='Horizontal' VerticalAlignment='Center'>
                    <TextBlock x:Name='DiskFreeText' Text='0.0 GB free' Foreground='#A1A1AA' FontSize='12.5' FontFamily='Consolas' VerticalAlignment='Center'/>
                    <TextBlock Text='  •  100% Local Privacy Log' Foreground='#71717A' FontSize='12' VerticalAlignment='Center' Margin='6,0,0,0'/>
                </StackPanel>

                <!-- Right-side Toggles & Mode Selector -->
                <StackPanel Grid.Column='2' Orientation='Horizontal' VerticalAlignment='Center'>
                    <Button x:Name='ClearFeedBtn' Style='{StaticResource DarkPillBtn}' Content='↺ Clear' Padding='10,4' Margin='0,0,14,0' ToolTip='Clear current screen feed'/>

                    <TextBlock Text='Mask IPs (###)' Foreground='#A1A1AA' FontSize='12.5' VerticalAlignment='Center' Margin='0,0,8,0'/>
                    <ToggleButton x:Name='IpMaskToggle' Style='{StaticResource PillSwitchStyle}' IsChecked='True' VerticalAlignment='Center' Margin='0,0,16,0'
                                  ToolTip='When ON, masks IPv4 and IPv6 addresses with # across the UI'/>

                    <TextBlock Text='Deep Hidden Mode' Foreground='#A1A1AA' FontSize='12.5' VerticalAlignment='Center' Margin='0,0,8,0'/>
                    <ToggleButton x:Name='DeepBgToggle' Style='{StaticResource PillSwitchStyle}' IsChecked='True' VerticalAlignment='Center' Margin='0,0,16,0'
                                  ToolTip='Show silent background process spawns, scheduled tasks, and telemetry'/>

                    <TextBlock Text='Startup' Foreground='#A1A1AA' FontSize='12.5' VerticalAlignment='Center' Margin='0,0,8,0'/>
                    <ToggleButton x:Name='StartupToggle' Style='{StaticResource PillSwitchStyle}' IsChecked='True' VerticalAlignment='Center' Margin='0,0,16,0'
                                  ToolTip='Automatically launch Angel Core Logger when Windows starts'/>

                    <!-- Segmented Live / Pause Pill -->
                    <Border Background='#151518' BorderBrush='#2E2E36' BorderThickness='1' CornerRadius='7' Padding='2' VerticalAlignment='Center'>
                        <StackPanel Orientation='Horizontal'>
                            <Button x:Name='LiveModeBtn' Content='Live' FontSize='11.5' FontWeight='SemiBold' Padding='10,3' BorderThickness='0' Cursor='Hand'
                                    Background='#2E2E36' Foreground='#F4F4F5'/>
                            <Button x:Name='PauseModeBtn' Content='Pause' FontSize='11.5' FontWeight='SemiBold' Padding='10,3' BorderThickness='0' Cursor='Hand'
                                    Background='Transparent' Foreground='#71717A'/>
                        </StackPanel>
                    </Border>
                </StackPanel>
            </Grid>
        </Border>
    </Grid>
</Border>";

            Border rootBorder = (Border)XamlReader.Parse(xaml);
            this.Content = rootBorder;

            string icoPath = Path.Combine(_exeDir, "AngelSystemLoggerPro.ico");
            if (File.Exists(icoPath))
            {
                try { this.Icon = new BitmapImage(new Uri(icoPath, UriKind.Absolute)); } catch { }
            }

            Border titleBar = (Border)rootBorder.FindName("TitleBarDragArea");
            Button minBtn = (Button)rootBorder.FindName("MinBtn");
            Button maxBtn = (Button)rootBorder.FindName("MaxBtn");
            Button closeBtn = (Button)rootBorder.FindName("CloseBtn");

            _categoryDropBtn = (Button)rootBorder.FindName("CategoryDropBtn");
            _categoryDropLabel = (TextBlock)rootBorder.FindName("CategoryDropLabel");
            _categoryPopup = (Popup)rootBorder.FindName("CategoryPopup");

            string[] catBtnNames = { "CatAllBtn", "CatHiddenBtn", "CatNetBtn", "CatDnsBtn", "CatAppBtn", "CatTaskBtn", "CatSecBtn" };
            foreach (string btnName in catBtnNames)
            {
                Button b = (Button)rootBorder.FindName(btnName);
                if (b != null)
                {
                    b.Click += (s, e) =>
                    {
                        Button clicked = (Button)s;
                        _selectedCategory = clicked.Tag.ToString();
                        _categoryDropLabel.Text = clicked.Content.ToString();
                        _categoryPopup.IsOpen = false;
                        if (_filteredView != null) _filteredView.Refresh();
                    };
                }
            }

            _categoryDropBtn.Click += (s, e) => { _categoryPopup.IsOpen = !_categoryPopup.IsOpen; };

            _searchBox = (TextBox)rootBorder.FindName("SearchBox");
            _engineStatusDot = (Ellipse)rootBorder.FindName("EngineStatusDot");
            _engineStatusText = (TextBlock)rootBorder.FindName("EngineStatusText");
            Button openLogsBtn = (Button)rootBorder.FindName("OpenLogsBtn");
            Button deepScanBtn = (Button)rootBorder.FindName("DeepScanBtn");

            _totalStatText = (TextBlock)rootBorder.FindName("TotalStatText");
            _hiddenStatText = (TextBlock)rootBorder.FindName("HiddenStatText");
            _netStatText = (TextBlock)rootBorder.FindName("NetStatText");
            _dnsStatText = (TextBlock)rootBorder.FindName("DnsStatText");
            _appStatText = (TextBlock)rootBorder.FindName("AppStatText");
            _taskStatText = (TextBlock)rootBorder.FindName("TaskStatText");

            _emptyStatePanel = (Border)rootBorder.FindName("EmptyStatePanel");
            _eventsListBox = (ListBox)rootBorder.FindName("EventsListBox");
            _selectedDetailsText = (TextBlock)rootBorder.FindName("SelectedDetailsText");
            Button copyEventBtn = (Button)rootBorder.FindName("CopyEventBtn");

            _diskFreeText = (TextBlock)rootBorder.FindName("DiskFreeText");
            Button clearFeedBtn = (Button)rootBorder.FindName("ClearFeedBtn");
            _ipMaskToggle = (ToggleButton)rootBorder.FindName("IpMaskToggle");
            _deepBgToggle = (ToggleButton)rootBorder.FindName("DeepBgToggle");
            _startupToggle = (ToggleButton)rootBorder.FindName("StartupToggle");
            _liveModeBtn = (Button)rootBorder.FindName("LiveModeBtn");
            _pauseModeBtn = (Button)rootBorder.FindName("PauseModeBtn");

            _startupToggle.IsChecked = Program.IsRegisteredInStartup();

            titleBar.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2) ToggleMaximize();
                else this.DragMove();
            };
            minBtn.Click += (s, e) => this.WindowState = WindowState.Minimized;
            maxBtn.Click += (s, e) => ToggleMaximize();
            closeBtn.Click += (s, e) => this.Close();

            _searchBox.TextChanged += (s, e) =>
            {
                _searchQuery = (_searchBox.Text ?? "").Trim();
                if (_filteredView != null) _filteredView.Refresh();
            };

            openLogsBtn.Click += (s, e) =>
            {
                try { Process.Start("explorer.exe", "\"" + _logDir + "\""); } catch { }
            };

            deepScanBtn.Click += (s, e) => RunDeepBackgroundXRayScan();

            _eventsListBox.SelectionChanged += (s, e) =>
            {
                ActivityEventItem sel = _eventsListBox.SelectedItem as ActivityEventItem;
                if (sel != null)
                {
                    _selectedDetailsText.Text = string.Format("[{0}] [{1}] {2} — {3}", sel.FullTime, sel.Badge, sel.Title, sel.Details);
                }
            };

            copyEventBtn.Click += (s, e) =>
            {
                ActivityEventItem sel = _eventsListBox.SelectedItem as ActivityEventItem;
                if (sel != null)
                {
                    Clipboard.SetText(string.Format("[{0}] [{1}] {2}\r\nExplanation: {3}\r\nDetails: {4}", sel.FullTime, sel.Badge, sel.Title, sel.Subtitle, sel.Details));
                    _selectedDetailsText.Text = "Copied selected event details to clipboard!";
                }
            };

            clearFeedBtn.Click += (s, e) =>
            {
                _allEvents.Clear();
                _totalCount = _hiddenCount = _networkCount = _dnsCount = _appCount = _taskSvcCount = 0;
                UpdateCountersUI();
            };

            _ipMaskToggle.Click += (s, e) =>
            {
                _maskIp = _ipMaskToggle.IsChecked == true;
                foreach (var ev in _allEvents) ev.MaskIp = _maskIp;
            };

            _deepBgToggle.Click += (s, e) =>
            {
                _deepBackground = _deepBgToggle.IsChecked == true;
                if (_filteredView != null) _filteredView.Refresh();
            };

            _startupToggle.Click += (s, e) =>
            {
                bool enable = _startupToggle.IsChecked == true;
                Program.SetStartupRegistry(_exePath, enable);
                AddSyntheticEvent("SECURITY", enable ? "STARTUP ON" : "STARTUP OFF",
                    enable ? "Registered Angel Core Logger in Windows Startup Apps" : "Removed Angel Core Logger from Windows Startup Apps",
                    "Updated HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\\AngelSystemLoggerPro",
                    "Executable: " + _exePath,
                    enable ? "Success" : "Warning");
            };

            _liveModeBtn.Click += (s, e) => SetPauseState(false);
            _pauseModeBtn.Click += (s, e) => SetPauseState(true);
        }

        private void ToggleMaximize()
        {
            this.WindowState = (this.WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
        }

        private void SetPauseState(bool paused)
        {
            _isPaused = paused;
            Brush activeBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E2E36"));
            Brush activeFg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F4F4F5"));
            Brush inactiveFg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#71717A"));

            _liveModeBtn.Background = paused ? Brushes.Transparent : activeBg;
            _liveModeBtn.Foreground = paused ? inactiveFg : activeFg;
            _pauseModeBtn.Background = paused ? activeBg : Brushes.Transparent;
            _pauseModeBtn.Foreground = paused ? activeFg : inactiveFg;

            _engineStatusText.Text = paused ? "Stream Paused" : "PowerShell Live";
            _engineStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(paused ? "#FBBF24" : "#34D399"));
        }

        private void SetupCollectionView()
        {
            _filteredView = CollectionViewSource.GetDefaultView(_allEvents);
            _filteredView.Filter = FilterEvent;
            _eventsListBox.ItemsSource = _filteredView;
        }

        private bool FilterEvent(object obj)
        {
            ActivityEventItem item = obj as ActivityEventItem;
            if (item == null) return false;

            if (!_deepBackground && (item.Category == "HIDDEN" || item.Badge == "BG TELEMETRY"))
            {
                return false;
            }

            if (_selectedCategory != "ALL")
            {
                if (_selectedCategory == "HIDDEN" && item.Category != "HIDDEN" && item.Badge != "BG NETWORK" && item.Badge != "BG TELEMETRY")
                    return false;
                if (_selectedCategory == "NETWORK" && item.Category != "NETWORK")
                    return false;
                if (_selectedCategory == "DNS" && item.Category != "DNS")
                    return false;
                if (_selectedCategory == "APP_WIN" && item.Category != "APP" && item.Category != "WINDOW")
                    return false;
                if (_selectedCategory == "TASK_SVC" && item.Category != "TASK" && item.Category != "SERVICE")
                    return false;
                if (_selectedCategory == "SECURITY" && item.Category != "SECURITY")
                    return false;
            }

            if (!string.IsNullOrEmpty(_searchQuery))
            {
                string q = _searchQuery.ToLowerInvariant();
                bool match = (item.Title != null && item.Title.ToLowerInvariant().Contains(q)) ||
                             (item.Subtitle != null && item.Subtitle.ToLowerInvariant().Contains(q)) ||
                             (item.Details != null && item.Details.ToLowerInvariant().Contains(q)) ||
                             (item.Badge != null && item.Badge.ToLowerInvariant().Contains(q));
                if (!match) return false;
            }

            return true;
        }

        private void UpdateDiskSpaceInfo()
        {
            try
            {
                string root = Path.GetPathRoot(_exePath) ?? "C:\\";
                DriveInfo di = new DriveInfo(root);
                double freeGb = di.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                _diskFreeText.Text = string.Format("{0:0.0} GB free", freeGb);
            }
            catch
            {
                _diskFreeText.Text = "Local Disk Active";
            }
        }

        private string EnsurePowerShellScriptPath()
        {
            string localPath = Path.Combine(_exeDir, "Start-LocalLogger.ps1");
            if (File.Exists(localPath)) return localPath;

            string appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AngelSystemLoggerPro");
            Directory.CreateDirectory(appDataDir);
            string extractedPath = Path.Combine(appDataDir, "Start-LocalLogger.ps1");

            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream s = asm.GetManifestResourceStream("Start-LocalLogger.ps1"))
            {
                if (s != null)
                {
                    using (FileStream fs = new FileStream(extractedPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        s.CopyTo(fs);
                    }
                }
            }
            return extractedPath;
        }

        private void StartPowerShellEngine()
        {
            try
            {
                string scriptPath = EnsurePowerShellScriptPath();
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = string.Format("-NoLogo -NoProfile -ExecutionPolicy Bypass -File \"{0}\" -GuiStream", scriptPath),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    WorkingDirectory = _exeDir
                };
                psi.EnvironmentVariables["ANGEL_LOGGER_EXE_PATH"] = _exePath;

                _psEngineProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
                _psEngineProcess.OutputDataReceived += OnPowerShellOutputReceived;
                _psEngineProcess.Start();
                _psEngineProcess.BeginOutputReadLine();
            }
            catch (Exception ex)
            {
                AddSyntheticEvent("SECURITY", "ENGINE ERROR",
                    "Failed to start background PowerShell collector",
                    ex.Message, _exePath, "Warning");
            }
        }

        private void StopPowerShellEngine()
        {
            try
            {
                if (_psEngineProcess != null && !_psEngineProcess.HasExited)
                {
                    _psEngineProcess.Kill();
                    _psEngineProcess.Dispose();
                }
            }
            catch { }
        }

        private void OnPowerShellOutputReceived(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            ProcessRawEventLine(e.Data);
        }

        private void ProcessRawEventLine(string line)
        {
            if (string.IsNullOrEmpty(line) || !line.StartsWith("ANGEL_EVENT|")) return;

            string json = line.Substring("ANGEL_EVENT|".Length);
            string time = ExtractJsonField(json, "time");
            string fullTime = ExtractJsonField(json, "fullTime");
            string category = ExtractJsonField(json, "category");
            string badge = ExtractJsonField(json, "badge");
            string title = ExtractJsonField(json, "title");
            string subtitle = ExtractJsonField(json, "subtitle");
            string details = ExtractJsonField(json, "details");
            string severity = ExtractJsonField(json, "severity");

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_isPaused) return;
                AddEventToFeed(time, fullTime, category, badge, title, subtitle, details, severity);
            }));
        }

        private void RunDeepBackgroundXRayScan()
        {
            _engineStatusText.Text = "Running Deep X-Ray...";
            _engineStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D5A449"));

            string xrayScript = @"
$ErrorActionPreference = 'SilentlyContinue'

# 1. Registered CurrentUser Startup entries
$runItems = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
foreach ($p in $runItems.PSObject.Properties) {
    if ($p.Name -notlike 'PS*') {
        $obj = @{
            time = (Get-Date -Format 'HH:mm:ss')
            fullTime = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
            category = 'SECURITY'
            badge = 'X-RAY STARTUP'
            title = ""Registered Windows Startup App: '$($p.Name)'""
            subtitle = ""Automatically launches in the background when you sign in""
            details = ""Command: $($p.Value)""
            severity = 'Info'
        } | ConvertTo-Json -Compress
        Write-Output ""ANGEL_EVENT|$obj""
    }
}

# 2. Currently running background scheduled tasks
$tasks = Get-ScheduledTask | Where-Object { $_.State -eq 'Running' } | Select-Object -First 4
foreach ($t in $tasks) {
    $obj = @{
        time = (Get-Date -Format 'HH:mm:ss')
        fullTime = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
        category = 'TASK'
        badge = 'X-RAY TASK'
        title = ""Background Scheduled Task running: '$($t.TaskName)'""
        subtitle = ""Triggered silently by Windows Task Scheduler ($($t.TaskPath))""
        details = ""Author: $($t.Author) | Description: $($t.Description)""
        severity = 'Accent'
    } | ConvertTo-Json -Compress
    Write-Output ""ANGEL_EVENT|$obj""
}

# 3. Active Foreground Window Apps
$fgApps = Get-Process | Where-Object { $_.MainWindowHandle -ne 0 -and -not [string]::IsNullOrWhiteSpace($_.MainWindowTitle) } | Select-Object -First 3
foreach ($app in $fgApps) {
    $obj = @{
        time = (Get-Date -Format 'HH:mm:ss')
        fullTime = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
        category = 'APP'
        badge = 'ACTIVE APP'
        title = ""$($app.ProcessName).exe window open: '$($app.MainWindowTitle)'""
        subtitle = ""Visible desktop application running in user session""
        details = ""PID: $($app.Id) | Memory: $([math]::Round($app.WorkingSet64 / 1MB, 1)) MB""
        severity = 'Info'
    } | ConvertTo-Json -Compress
    Write-Output ""ANGEL_EVENT|$obj""
}

# 4. Recent Cached DNS Lookups & Telemetry Domains
$dnsEntries = Get-DnsClientCache | Where-Object { $_.Entry -and $_.Entry -notin @('localhost','wpad') } | Select-Object -Unique Entry, Data, TimeToLive -First 4
foreach ($d in $dnsEntries) {
    $isTel = ($d.Entry -match 'telemetry|vortex|settings-win|data\.microsoft|events\.data|aria|metrics|analytics')
    $obj = @{
        time = (Get-Date -Format 'HH:mm:ss')
        fullTime = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
        category = 'DNS'
        badge = $(if ($isTel) { 'BG TELEMETRY' } else { 'DNS CACHE' })
        title = ""Cached domain lookup: $($d.Entry)""
        subtitle = $(if ($isTel) { 'Background diagnostic/telemetry endpoint resolved by Windows or app' } else { 'Active domain resolved in system DNS cache' })
        details = ""Resolved Data: $($d.Data) | TTL: $($d.TimeToLive)s""
        severity = $(if ($isTel) { 'Warning' } else { 'Accent' })
    } | ConvertTo-Json -Compress
    Write-Output ""ANGEL_EVENT|$obj""
}

# 5. Hidden background processes with active external internet connections
$conns = Get-NetTCPConnection -State Established | Where-Object { $_.RemoteAddress -notmatch '^(127\.|0\.|::1|fe80:)' }
$grouped = $conns | Group-Object OwningProcess
foreach ($g in ($grouped | Select-Object -First 6)) {
    $pidVal = [int]$g.Name
    $proc = Get-Process -Id $pidVal
    $cim = Get-CimInstance Win32_Process -Filter ""ProcessId = $pidVal""
    $isHidden = ($null -eq $proc -or [string]::IsNullOrWhiteSpace($proc.MainWindowTitle))
    $ips = ($g.Group | Select-Object -First 3 | ForEach-Object { ""$($_.RemoteAddress):$($_.RemotePort)"" }) -join ', '
    $cmd = if ($cim.CommandLine) { $cim.CommandLine } else { $proc.Path }
    if ($cmd.Length -gt 180) { $cmd = $cmd.Substring(0, 180) + '...' }
    $parentPid = if ($cim) { $cim.ParentProcessId } else { 0 }
    $obj = @{
        time = (Get-Date -Format 'HH:mm:ss')
        fullTime = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
        category = $(if ($isHidden) { 'HIDDEN' } else { 'NETWORK' })
        badge = $(if ($isHidden) { 'HIDDEN NET BG' } else { 'LIVE SOCKET' })
        title = ""$(if ($isHidden) { 'Hidden background process' } else { 'App' }) $($proc.ProcessName).exe holds $($g.Count) external connection(s)""
        subtitle = ""Communicating in background with external server(s): $ips""
        details = ""PID: $pidVal (Parent PID: $parentPid) | Endpoints: $ips | Cmd: $cmd""
        severity = $(if ($isHidden) { 'Hidden' } else { 'Success' })
    } | ConvertTo-Json -Compress
    Write-Output ""ANGEL_EVENT|$obj""
}
";
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(xrayScript));
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = "-NoLogo -NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encoded,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        StandardOutputEncoding = Encoding.UTF8
                    };
                    using (Process p = Process.Start(psi))
                    {
                        while (!p.StandardOutput.EndOfStream)
                        {
                            string line = p.StandardOutput.ReadLine();
                            if (line != null && line.StartsWith("ANGEL_EVENT|"))
                            {
                                ProcessRawEventLine(line);
                            }
                        }
                        p.WaitForExit(8000);
                    }
                }
                catch { }
                finally
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _engineStatusText.Text = _isPaused ? "Stream Paused" : "PowerShell Live";
                        _engineStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_isPaused ? "#FBBF24" : "#34D399"));
                    }));
                }
            });
        }

        private void AddSyntheticEvent(string category, string badge, string title, string subtitle, string details, string severity)
        {
            string nowShort = DateTime.Now.ToString("HH:mm:ss");
            string nowFull = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            AddEventToFeed(nowShort, nowFull, category, badge, title, subtitle, details, severity);
        }

        private void AddEventToFeed(string time, string fullTime, string category, string badge, string title, string subtitle, string details, string severity)
        {
            Brush badgeBg, badgeFg, dotBrush;
            GetBadgeColors(category, severity, out badgeBg, out badgeFg, out dotBrush);

            ActivityEventItem item = new ActivityEventItem
            {
                Time = time,
                FullTime = fullTime,
                Category = category,
                Badge = badge,
                RawTitle = title,
                RawSubtitle = subtitle,
                RawDetails = details,
                Severity = severity,
                MaskIp = _maskIp,
                BadgeBackground = badgeBg,
                BadgeForeground = badgeFg,
                DotBrush = dotBrush
            };

            _allEvents.Insert(0, item);
            if (_allEvents.Count > 600)
            {
                _allEvents.RemoveAt(_allEvents.Count - 1);
            }

            _totalCount++;
            switch (category)
            {
                case "HIDDEN":
                    _hiddenCount++;
                    if (badge != null && badge.Contains("NET")) _networkCount++;
                    break;
                case "NETWORK":
                    _networkCount++;
                    if (badge != null && badge.Contains("BG")) _hiddenCount++;
                    break;
                case "DNS": _dnsCount++; break;
                case "APP":
                case "WINDOW": _appCount++; break;
                case "TASK":
                case "SERVICE":
                case "SECURITY": _taskSvcCount++; break;
            }

            UpdateCountersUI();
        }

        private void UpdateCountersUI()
        {
            _totalStatText.Text = _totalCount.ToString();
            _hiddenStatText.Text = _hiddenCount.ToString();
            _netStatText.Text = _networkCount.ToString();
            _dnsStatText.Text = _dnsCount.ToString();
            _appStatText.Text = _appCount.ToString();
            _taskStatText.Text = _taskSvcCount.ToString();
            _emptyStatePanel.Visibility = (_allEvents.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
        }

        private static void GetBadgeColors(string category, string severity, out Brush bg, out Brush fg, out Brush dot)
        {
            string bgHex = "#27272E", fgHex = "#E4E4E7", dotHex = "#A1A1AA";

            if (category == "HIDDEN" || severity == "Hidden")
            {
                bgHex = "#332818"; fgHex = "#FBBF24"; dotHex = "#F59E0B";
            }
            else if (category == "NETWORK")
            {
                bgHex = "#162E28"; fgHex = "#34D399"; dotHex = "#10B981";
            }
            else if (category == "DNS")
            {
                bgHex = "#2E1F38"; fgHex = "#C084FC"; dotHex = "#A855F7";
            }
            else if (category == "APP" || category == "WINDOW")
            {
                bgHex = "#1B2A3E"; fgHex = "#60A5FA"; dotHex = "#3B82F6";
            }
            else if (category == "TASK" || category == "SERVICE")
            {
                bgHex = "#29233B"; fgHex = "#A5B4FC"; dotHex = "#818CF8";
            }
            else if (category == "SECURITY" || severity == "Warning")
            {
                bgHex = "#381E22"; fgHex = "#F87171"; dotHex = "#EF4444";
            }
            else if (severity == "Success")
            {
                bgHex = "#162E28"; fgHex = "#34D399"; dotHex = "#10B981";
            }

            bg = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgHex));
            fg = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fgHex));
            dot = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dotHex));
            bg.Freeze(); fg.Freeze(); dot.Freeze();
        }

        private static string ExtractJsonField(string json, string field)
        {
            string pattern = "\"" + Regex.Escape(field) + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"";
            Match m = Regex.Match(json, pattern);
            if (!m.Success) return "";
            return Regex.Unescape(m.Groups[1].Value);
        }
    }
}
