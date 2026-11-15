using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using Ellipse = System.Windows.Shapes.Ellipse;
using WinForms = System.Windows.Forms;

namespace AngelSystemLoggerPro
{
    public class MainWindow : Window
    {
        private readonly string _exePath;
        private readonly string _exeDir;
        private readonly AppSettings _settings;
        private Process _psEngineProcess;
        private WinForms.NotifyIcon _trayIcon;
        private WinForms.ToolStripMenuItem _trayPauseMenu;
        private bool _forceExit = false;

        // Collections & Views
        private readonly ObservableCollection<ActivityEventItem> _allEvents = new ObservableCollection<ActivityEventItem>();
        private readonly ObservableCollection<HiddenProcessItem> _hiddenProcesses = new ObservableCollection<HiddenProcessItem>();
        private readonly ObservableCollection<NetworkConnectionItem> _connections = new ObservableCollection<NetworkConnectionItem>();
        private readonly ObservableCollection<ListeningPortItem> _listeningPorts = new ObservableCollection<ListeningPortItem>();
        private readonly ObservableCollection<PrivacyAccessItem> _privacyItems = new ObservableCollection<PrivacyAccessItem>();
        private readonly ObservableCollection<StartupTaskItem> _startupItems = new ObservableCollection<StartupTaskItem>();
        private readonly ObservableCollection<StartupTaskItem> _runningTasks = new ObservableCollection<StartupTaskItem>();
        private readonly ObservableCollection<StartupTaskItem> _fileDrops = new ObservableCollection<StartupTaskItem>();

        private ICollectionView _eventsView;
        private ICollectionView _hiddenView;
        private ICollectionView _connectionsView;
        private ICollectionView _listeningView;
        private ICollectionView _privacyView;
        private ICollectionView _startupView;
        private ICollectionView _tasksView;
        private ICollectionView _dropsView;

        // State
        private bool _isPaused = false;
        private string _activeMainTab = "ACTIVITY"; // ACTIVITY, HIDDEN, NETWORK, STARTUP
        private string _activeNetSubTab = "CONNS";  // CONNS, LISTEN, PRIVACY
        private string _activeStartSubTab = "STARTUP"; // STARTUP, TASKS, DROPS
        private string _selectedCategory = "ALL";
        private string _searchQuery = "";
        private bool _onlyHiddenWithNet = false;
        private bool _hideMicrosoftHidden = false;
        private int _totalHiddenProcessCount = 0;

        private int _cntAll = 0;
        private int _cntHidden = 0;
        private int _cntNetwork = 0;
        private int _cntDns = 0;
        private int _cntAppWin = 0;
        private int _cntPrivacySys = 0;

        // Bound UI Controls
        private TextBlock _engineStatusText;
        private Ellipse _engineStatusDot;
        private TextBlock _vitalsText;
        private Border _privBadge;
        private TextBlock _privBadgeText;

        private Button _navActivityBtn, _navHiddenBtn, _navNetworkBtn, _navStartupBtn;
        private Grid _viewActivity, _viewHidden, _viewNetwork, _viewStartup;

        private Border _statAllCard, _statHiddenCard, _statNetCard, _statDnsCard, _statAppCard, _statPrivCard;
        private TextBlock _statAllText, _statHiddenText, _statNetText, _statDnsText, _statAppText, _statPrivText;

        private TextBox _searchBox;
        private TextBlock _searchWatermark;
        private Button _searchClearBtn;

        private Border _emptyStatePanel;
        private ListBox _eventsListBox;

        // Inspector controls (Activity Tab)
        private StackPanel _inspectorEmptyHint;
        private ScrollViewer _inspectorDetailScroll;
        private Border _inspBadgePill;
        private TextBlock _inspBadgeText;
        private TextBlock _inspTimeText;
        private TextBlock _inspTrustText;
        private TextBlock _inspTitleText;
        private TextBlock _inspSubtitleText;
        private TextBlock _inspParentText;
        private TextBlock _inspPathText;
        private TextBlock _inspEndpointText;
        private TextBox _inspCmdBox;
        private Button _inspOpenLocBtn, _inspMuteBtn, _inspKillBtn;
        private ActivityEventItem _selectedEvent;

        // Hidden Background tab controls
        private TextBlock _hiddenSummaryText;
        private CheckBox _hiddenOnlyNetCheck;
        private CheckBox _hiddenNoMsCheck;
        private ListBox _hiddenListBox;
        private TextBlock _hiddenInspectorText;
        private HiddenProcessItem _selectedHidden;

        // Network & Privacy tab controls
        private Button _subConnsBtn, _subListenBtn, _subPrivacyBtn;
        private ListBox _connsListBox, _listenListBox, _privacyListBox;
        private TextBlock _micBannerText, _camBannerText, _locBannerText;

        // Startup & Tasks tab controls
        private Button _subStartupBtn, _subTasksBtn, _subDropsBtn;
        private ListBox _startupListBox, _tasksListBox, _dropsListBox;

        // Footer controls
        private TextBlock _diskFreeText;
        private Button _settingsBtn;
        private ToggleButton _ipMaskToggle;
        private ToggleButton _deepBgToggle;
        private ToggleButton _startupToggle;
        private Button _liveModeBtn;
        private Button _pauseModeBtn;

        public MainWindow(string exePath, AppSettings settings, bool startMinimized, string previewOutPath)
        {
            _exePath = exePath;
            _exeDir = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory;
            _settings = settings;

            this.Title = "Angel Core Logger";
            this.Width = 1220;
            this.Height = 760;
            this.MinWidth = 960;
            this.MinHeight = 600;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.WindowStyle = WindowStyle.None;
            this.AllowsTransparency = true;
            this.Background = Brushes.Transparent;
            this.FontFamily = new FontFamily("Segoe UI");

            WindowChrome chrome = new WindowChrome
            {
                CaptionHeight = 38,
                CornerRadius = new CornerRadius(10),
                GlassFrameThickness = new Thickness(0),
                ResizeBorderThickness = new Thickness(6),
                UseAeroCaptionButtons = false
            };
            WindowChrome.SetWindowChrome(this, chrome);

            InitializeWindowLayout();
            SetupCollectionViews();
            SetupTrayIcon();
            UpdateDiskSpaceInfo();
            UpdateVitalsDisplay();
            StartPowerShellEngine();

            DispatcherTimer vitalsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            vitalsTimer.Tick += (s, e) => UpdateVitalsDisplay();
            vitalsTimer.Start();

            DispatcherTimer diskTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            diskTimer.Tick += (s, e) => UpdateDiskSpaceInfo();
            diskTimer.Start();

            this.Closing += MainWindow_Closing;
            this.Closed += (s, e) => CleanupAndExit();

            if (startMinimized)
            {
                this.ShowInTaskbar = false;
                this.WindowState = WindowState.Minimized;
                this.Hide();
            }

            if (!string.IsNullOrEmpty(previewOutPath))
            {
                DispatcherTimer snapTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(9500) };
                snapTimer.Tick += (s, e) =>
                {
                    snapTimer.Stop();
                    try
                    {
                        string previewTab = (Environment.GetEnvironmentVariable("ANGEL_PREVIEW_TAB") ?? "").ToUpperInvariant();
                        if (previewTab == "HIDDEN")
                        {
                            SwitchMainTab("HIDDEN");
                            if (_hiddenListBox.Items.Count > 0 && _hiddenListBox.SelectedIndex < 0)
                                _hiddenListBox.SelectedIndex = 0;
                        }
                        else if (previewTab.StartsWith("NETWORK"))
                        {
                            SwitchMainTab("NETWORK");
                            if (previewTab.EndsWith("LISTEN")) SwitchNetSubTab("LISTEN");
                            else if (previewTab.EndsWith("PRIVACY")) SwitchNetSubTab("PRIVACY");
                            else SwitchNetSubTab("CONNS");
                        }
                        else if (previewTab.StartsWith("STARTUP"))
                        {
                            SwitchMainTab("STARTUP");
                            if (previewTab.EndsWith("TASKS")) SwitchStartSubTab("TASKS");
                            else if (previewTab.EndsWith("DROPS")) SwitchStartSubTab("DROPS");
                            else SwitchStartSubTab("STARTUP");
                        }
                        else
                        {
                            SwitchMainTab("ACTIVITY");
                            if (_eventsListBox.Items.Count > 0)
                            {
                                _eventsListBox.SelectedIndex = 0;
                                PopulateEventInspector(_eventsListBox.SelectedItem as ActivityEventItem);
                            }
                        }

                        FrameworkElement content = this.Content as FrameworkElement;
                        if (content != null && this.ActualWidth > 0 && this.ActualHeight > 0)
                        {
                            content.InvalidateMeasure();
                            content.UpdateLayout();
                            Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.Render);

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
                    _forceExit = true;
                    this.Close();
                };
                snapTimer.Start();
            }
        }

        public void BringToForeground()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                this.Show();
                this.ShowInTaskbar = true;
                if (this.WindowState == WindowState.Minimized)
                    this.WindowState = WindowState.Normal;
                this.Activate();
                this.Topmost = true;
                this.Topmost = false;
                this.Focus();
            }));
        }

        private void SetupTrayIcon()
        {
            try
            {
                System.Drawing.Icon trayIco = null;
                string icoPath = Path.Combine(_exeDir, "AngelSystemLoggerPro.ico");
                if (File.Exists(icoPath))
                    trayIco = new System.Drawing.Icon(icoPath);
                else
                    trayIco = System.Drawing.Icon.ExtractAssociatedIcon(_exePath);

                WinForms.ContextMenuStrip menu = new WinForms.ContextMenuStrip();
                menu.Items.Add("Open Angel Core Logger", null, (s, e) => BringToForeground());
                menu.Items.Add("Run Deep X-Ray Scan", null, (s, e) => TriggerDeepScan());
                _trayPauseMenu = new WinForms.ToolStripMenuItem("Pause Monitoring", null, (s, e) =>
                {
                    Dispatcher.BeginInvoke(new Action(() => SetPauseState(!_isPaused)));
                });
                menu.Items.Add(_trayPauseMenu);
                menu.Items.Add("Clear In-Memory Feed", null, (s, e) =>
                {
                    Dispatcher.BeginInvoke(new Action(() => ClearLiveFeed()));
                });
                menu.Items.Add(new WinForms.ToolStripSeparator());
                menu.Items.Add("Exit Completely", null, (s, e) =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _forceExit = true;
                        this.Close();
                    }));
                });

                _trayIcon = new WinForms.NotifyIcon
                {
                    Icon = trayIco,
                    Text = "Angel Core Logger (100% Local Monitoring)",
                    Visible = true,
                    ContextMenuStrip = menu
                };
                _trayIcon.DoubleClick += (s, e) => BringToForeground();
            }
            catch { }
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (!_forceExit && _settings.MinimizeToTrayOnClose && _trayIcon != null)
            {
                e.Cancel = true;
                this.Hide();
                this.ShowInTaskbar = false;
            }
        }

        private void CleanupAndExit()
        {
            StopPowerShellEngine();
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
        }

        private void InitializeWindowLayout()
        {
            string xaml = @"
<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        CornerRadius='10' Background='#18181B' BorderBrush='#3F3F4B' BorderThickness='1.2' ClipToBounds='True'>

    <Border.Resources>
        <Style x:Key='PillSwitchStyle' TargetType='ToggleButton'>
            <Setter Property='Width' Value='40'/>
            <Setter Property='Height' Value='21'/>
            <Setter Property='Cursor' Value='Hand'/>
            <Setter Property='Template'>
                <Setter.Value>
                    <ControlTemplate TargetType='ToggleButton'>
                        <Border x:Name='Track' CornerRadius='10.5' Background='#32323A' BorderBrush='#454550' BorderThickness='1' Padding='2'>
                            <Ellipse x:Name='Thumb' Width='15' Height='15' Fill='#F4F4F6' HorizontalAlignment='Left'/>
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

        <Style x:Key='GoldActionBtn' TargetType='Button'>
            <Setter Property='Background' Value='#D5A449'/>
            <Setter Property='Foreground' Value='#161618'/>
            <Setter Property='FontWeight' Value='SemiBold'/>
            <Setter Property='FontSize' Value='12.5'/>
            <Setter Property='Cursor' Value='Hand'/>
            <Setter Property='Padding' Value='13,6'/>
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

        <Style x:Key='DarkPillBtn' TargetType='Button'>
            <Setter Property='Background' Value='#26262D'/>
            <Setter Property='Foreground' Value='#E4E4E7'/>
            <Setter Property='BorderBrush' Value='#35353E'/>
            <Setter Property='FontSize' Value='12'/>
            <Setter Property='Cursor' Value='Hand'/>
            <Setter Property='Padding' Value='11,6'/>
            <Setter Property='Template'>
                <Setter.Value>
                    <ControlTemplate TargetType='Button'>
                        <Border x:Name='Bd' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='7' Padding='{TemplateBinding Padding}'>
                            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property='IsMouseOver' Value='True'>
                                <Setter TargetName='Bd' Property='Background' Value='#303039'/>
                                <Setter TargetName='Bd' Property='BorderBrush' Value='#4A4A57'/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <Style x:Key='NavTabBtn' TargetType='Button'>
            <Setter Property='Background' Value='Transparent'/>
            <Setter Property='Foreground' Value='#A1A1AA'/>
            <Setter Property='FontSize' Value='12'/>
            <Setter Property='FontWeight' Value='SemiBold'/>
            <Setter Property='Cursor' Value='Hand'/>
            <Setter Property='Padding' Value='10,5.5'/>
            <Setter Property='Margin' Value='0,0,3,0'/>
            <Setter Property='Template'>
                <Setter.Value>
                    <ControlTemplate TargetType='Button'>
                        <Border x:Name='Bd' Background='{TemplateBinding Background}' CornerRadius='7' Padding='{TemplateBinding Padding}'>
                            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property='IsMouseOver' Value='True'>
                                <Setter TargetName='Bd' Property='Background' Value='#2A2A32'/>
                                <Setter Property='Foreground' Value='#F4F4F5'/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <Style x:Key='CaptionBtn' TargetType='Button'>
            <Setter Property='Width' Value='38'/>
            <Setter Property='Height' Value='36'/>
            <Setter Property='Background' Value='Transparent'/>
            <Setter Property='Foreground' Value='#A1A1AA'/>
            <Setter Property='FontSize' Value='12.5'/>
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
                                <Setter TargetName='Bd' Property='Background' Value='#2D2D36'/>
                                <Setter Property='Foreground' Value='#FFFFFF'/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

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
                                                <Border CornerRadius='4' Background='#34343F' Margin='1,2'/>
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
            <RowDefinition Height='52'/>
            <RowDefinition Height='Auto'/>
            <RowDefinition Height='*'/>
            <RowDefinition Height='46'/>
        </Grid.RowDefinitions>

        <!-- 1. CUSTOM TITLE BAR WITH LIVE VITALS -->
        <Border Grid.Row='0' Background='#1F1F24' BorderBrush='#2A2A32' BorderThickness='0,0,0,1'>
            <Grid>
                <StackPanel Orientation='Horizontal' HorizontalAlignment='Left' VerticalAlignment='Center' Margin='14,0,0,0'>
                    <Ellipse Width='8' Height='8' Fill='#34D399' Margin='0,0,8,0'/>
                    <TextBlock Text='Angel Core Logger' Foreground='#F4F4F5' FontSize='13' FontWeight='SemiBold' VerticalAlignment='Center'/>
                    <Border Background='#272730' BorderBrush='#383844' BorderThickness='1' CornerRadius='4' Padding='5,1' Margin='8,0,0,0' VerticalAlignment='Center'>
                        <TextBlock Text='v3.0' FontSize='10' FontWeight='Bold' Foreground='#D5A449'/>
                    </Border>
                    <Border x:Name='PrivBadge' Background='#22222A' BorderBrush='#33333E' BorderThickness='1' CornerRadius='4' Padding='6,1' Margin='6,0,0,0' VerticalAlignment='Center'>
                        <TextBlock x:Name='PrivBadgeText' Text='STANDARD MODE' FontSize='9.5' FontWeight='Bold' Foreground='#A1A1AA'/>
                    </Border>
                </StackPanel>

                <!-- Center-Right Live Vitals -->
                <StackPanel Orientation='Horizontal' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,162,0'>
                    <Border Background='#18181C' BorderBrush='#2E2E36' BorderThickness='1' CornerRadius='5' Padding='10,3'>
                        <TextBlock x:Name='VitalsText' Text='CPU 0%  •  RAM 0%  •  Mic: Idle  •  Cam: Idle' Foreground='#A1A1AA' FontSize='11' FontFamily='Consolas'/>
                    </Border>
                </StackPanel>

                <StackPanel Orientation='Horizontal' HorizontalAlignment='Right' VerticalAlignment='Center' WindowChrome.IsHitTestVisibleInChrome='True'>
                    <Button x:Name='TrayBtn' Style='{StaticResource CaptionBtn}' Content='▾' ToolTip='Hide to System Tray (keeps monitoring)'/>
                    <Button x:Name='MinBtn' Style='{StaticResource CaptionBtn}' Content='—' ToolTip='Minimize'/>
                    <Button x:Name='MaxBtn' Style='{StaticResource CaptionBtn}' Content='▢' ToolTip='Maximize / Restore'/>
                    <Button x:Name='CloseBtn' Style='{StaticResource CaptionBtn}' Content='✕' ToolTip='Close / Send to Tray'/>
                </StackPanel>
            </Grid>
        </Border>

        <!-- 2. TOP NAVIGATION TABS, SEARCH & ACTION BUTTONS -->
        <Border Grid.Row='1' Background='#1F1F24' BorderBrush='#2A2A32' BorderThickness='0,0,0,1' Padding='14,0'>
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width='Auto'/>
                    <ColumnDefinition Width='*'/>
                    <ColumnDefinition Width='Auto'/>
                </Grid.ColumnDefinitions>

                <!-- 4 Main Navigation Tabs -->
                <StackPanel Grid.Column='0' Orientation='Horizontal' VerticalAlignment='Center'>
                    <Button x:Name='NavActivityBtn' Style='{StaticResource NavTabBtn}' Background='#2E2E37' Foreground='#F4F4F5' Content='Live Feed (0)'/>
                    <Button x:Name='NavHiddenBtn' Style='{StaticResource NavTabBtn}' Content='Hidden BG (0)'/>
                    <Button x:Name='NavNetworkBtn' Style='{StaticResource NavTabBtn}' Content='Net &amp; Privacy (0)'/>
                    <Button x:Name='NavStartupBtn' Style='{StaticResource NavTabBtn}' Content='Startup &amp; Tasks (0)'/>
                </StackPanel>

                <!-- Search Input with Placeholder -->
                <Border Grid.Column='1' Margin='10,0' Height='32' MinWidth='220' MaxWidth='380' HorizontalAlignment='Stretch' CornerRadius='7' Background='#151519' BorderBrush='#2E2E37' BorderThickness='1' VerticalAlignment='Center'>
                    <Grid>
                        <TextBlock x:Name='SearchWatermark' Text='Search process, domain, PID, path...' Foreground='#52525B' FontSize='12'
                                   VerticalAlignment='Center' Margin='10,0,26,0' IsHitTestVisible='False'/>
                        <TextBox x:Name='SearchBox' Background='Transparent' Foreground='#F4F4F5' CaretBrush='#D5A449'
                                 BorderThickness='0' VerticalContentAlignment='Center' Padding='10,0,26,0' FontSize='12'/>
                        <Button x:Name='SearchClearBtn' Content='✕' Visibility='Collapsed' HorizontalAlignment='Right' VerticalAlignment='Center'
                                Margin='0,0,6,0' Background='Transparent' Foreground='#71717A' BorderThickness='0' FontSize='11' Cursor='Hand'/>
                    </Grid>
                </Border>

                <!-- Status Pill & Action Buttons -->
                <StackPanel Grid.Column='2' Orientation='Horizontal' VerticalAlignment='Center'>
                    <Border CornerRadius='7' Background='#25252C' BorderBrush='#34343F' BorderThickness='1' Padding='9,5' Margin='0,0,7,0' VerticalAlignment='Center'
                            ToolTip='PowerShell deep telemetry collector status'>
                        <StackPanel Orientation='Horizontal' VerticalAlignment='Center'>
                            <Ellipse x:Name='EngineStatusDot' Width='8' Height='8' Fill='#34D399' Margin='0,0,6,0' VerticalAlignment='Center'/>
                            <TextBlock x:Name='EngineStatusText' Text='Starting...' Foreground='#E4E4E7' FontSize='12' FontWeight='Medium' VerticalAlignment='Center'/>
                        </StackPanel>
                    </Border>

                    <Button x:Name='DeepScanBtn' Style='{StaticResource GoldActionBtn}' Content='+ Deep Scan' ToolTip='Force immediate PowerShell Deep X-Ray scan of hidden processes, sockets, privacy &amp; tasks'/>
                </StackPanel>
            </Grid>
        </Border>

        <!-- 3. TELEMETRY SCHEMATIC & 6 CLICKABLE FILTER CARDS -->
        <Border Grid.Row='2' Background='#1B1B20' BorderBrush='#27272E' BorderThickness='0,0,0,1' Padding='14,9'>
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width='198'/>
                    <ColumnDefinition Width='*'/>
                </Grid.ColumnDefinitions>

                <Canvas Grid.Column='0' Width='190' Height='50' VerticalAlignment='Center' HorizontalAlignment='Left'>
                    <TextBlock Text='BG SPAWNS' FontSize='8.5' FontFamily='Consolas' Foreground='#71717A' Canvas.Left='0' Canvas.Top='3'/>
                    <TextBlock Text='NET / DNS' FontSize='8.5' FontFamily='Consolas' Foreground='#71717A' Canvas.Left='0' Canvas.Top='19'/>
                    <TextBlock Text='MIC &amp; TASKS' FontSize='8.5' FontFamily='Consolas' Foreground='#71717A' Canvas.Left='0' Canvas.Top='35'/>

                    <Ellipse Width='6' Height='6' Fill='#D5A449' Canvas.Left='62' Canvas.Top='5'/>
                    <Ellipse Width='6' Height='6' Fill='#34D399' Canvas.Left='62' Canvas.Top='21'/>
                    <Ellipse Width='6' Height='6' Fill='#F87171' Canvas.Left='62' Canvas.Top='37'/>

                    <Path Stroke='#52525B' StrokeThickness='1.4' StrokeDashArray='3,2' Data='M 68,8 C 88,8 94,24 112,24'/>
                    <Path Stroke='#52525B' StrokeThickness='1.4' StrokeDashArray='3,2' Data='M 68,24 L 112,24'/>
                    <Path Stroke='#52525B' StrokeThickness='1.4' StrokeDashArray='3,2' Data='M 68,40 C 88,40 94,24 112,24'/>

                    <Ellipse Width='14' Height='14' Stroke='#71717A' StrokeThickness='1.2' StrokeDashArray='2,2' Canvas.Left='106' Canvas.Top='17'/>
                    <Path Stroke='#34D399' StrokeThickness='2.4' Data='M 114,24 L 154,24'/>
                    <Polygon Points='152,19 161,24 152,29' Fill='#34D399'/>
                    <TextBlock Text='PS ENGINE' FontSize='8.5' FontWeight='Bold' FontFamily='Consolas' Foreground='#A1A1AA' Canvas.Left='120' Canvas.Top='6'/>
                </Canvas>

                <UniformGrid Grid.Column='1' Columns='6' VerticalAlignment='Center'>
                    <Border x:Name='StatAllCard' Background='#23232A' BorderBrush='#D5A449' BorderThickness='1.5' CornerRadius='7' Padding='10,6' Margin='3,0' Cursor='Hand' ToolTip='Show all captured activity'>
                        <StackPanel>
                            <TextBlock Text='ALL EVENTS' FontSize='9.5' FontWeight='Bold' Foreground='#A1A1AA'/>
                            <TextBlock x:Name='StatAllText' Text='0' FontSize='17' FontWeight='Bold' Foreground='#F4F4F5'/>
                        </StackPanel>
                    </Border>
                    <Border x:Name='StatHiddenCard' Background='#222227' BorderBrush='#382E1E' BorderThickness='1' CornerRadius='7' Padding='10,6' Margin='3,0' Cursor='Hand' ToolTip='Filter: Hidden windowless spawns &amp; silent scripts'>
                        <StackPanel>
                            <TextBlock Text='HIDDEN BG' FontSize='9.5' FontWeight='Bold' Foreground='#D5A449'/>
                            <TextBlock x:Name='StatHiddenText' Text='0' FontSize='17' FontWeight='Bold' Foreground='#FBBF24'/>
                        </StackPanel>
                    </Border>
                    <Border x:Name='StatNetCard' Background='#222227' BorderBrush='#1E3A34' BorderThickness='1' CornerRadius='7' Padding='10,6' Margin='3,0' Cursor='Hand' ToolTip='Filter: External network sockets &amp; open listening ports'>
                        <StackPanel>
                            <TextBlock Text='NETWORK' FontSize='9.5' FontWeight='Bold' Foreground='#34D399'/>
                            <TextBlock x:Name='StatNetText' Text='0' FontSize='17' FontWeight='Bold' Foreground='#34D399'/>
                        </StackPanel>
                    </Border>
                    <Border x:Name='StatDnsCard' Background='#222227' BorderBrush='#382640' BorderThickness='1' CornerRadius='7' Padding='10,6' Margin='3,0' Cursor='Hand' ToolTip='Filter: DNS lookups &amp; telemetry domains'>
                        <StackPanel>
                            <TextBlock Text='DNS &amp; WEB' FontSize='9.5' FontWeight='Bold' Foreground='#C084FC'/>
                            <TextBlock x:Name='StatDnsText' Text='0' FontSize='17' FontWeight='Bold' Foreground='#C084FC'/>
                        </StackPanel>
                    </Border>
                    <Border x:Name='StatAppCard' Background='#222227' BorderBrush='#23354D' BorderThickness='1' CornerRadius='7' Padding='10,6' Margin='3,0' Cursor='Hand' ToolTip='Filter: Visible apps &amp; active window/tab changes'>
                        <StackPanel>
                            <TextBlock Text='APPS &amp; TABS' FontSize='9.5' FontWeight='Bold' Foreground='#60A5FA'/>
                            <TextBlock x:Name='StatAppText' Text='0' FontSize='17' FontWeight='Bold' Foreground='#60A5FA'/>
                        </StackPanel>
                    </Border>
                    <Border x:Name='StatPrivCard' Background='#222227' BorderBrush='#3D262A' BorderThickness='1' CornerRadius='7' Padding='10,6' Margin='3,0' Cursor='Hand' ToolTip='Filter: Mic/Webcam/Location, Startup, Tasks, Services &amp; File Drops'>
                        <StackPanel>
                            <TextBlock Text='PRIVACY &amp; SYS' FontSize='9.5' FontWeight='Bold' Foreground='#F87171'/>
                            <TextBlock x:Name='StatPrivText' Text='0' FontSize='17' FontWeight='Bold' Foreground='#F87171'/>
                        </StackPanel>
                    </Border>
                </UniformGrid>
            </Grid>
        </Border>

        <!-- 4. MAIN WORKSPACE (4 VIEWS) -->
        <Grid Grid.Row='3' Background='#18181B'>
            <!-- VIEW 1: LIVE ACTIVITY STREAM + RIGHT DETAIL INSPECTOR -->
            <Grid x:Name='ViewActivity' Visibility='Visible'>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width='*' MinWidth='420'/>
                    <ColumnDefinition Width='4'/>
                    <ColumnDefinition Width='335' MinWidth='270' MaxWidth='480'/>
                </Grid.ColumnDefinitions>

                <Grid Grid.Column='0'>
                    <Border x:Name='EmptyStatePanel' Visibility='Visible' HorizontalAlignment='Center' VerticalAlignment='Center' Padding='24'>
                        <StackPanel HorizontalAlignment='Center'>
                            <TextBlock Text='Monitoring computer activity locally...' FontSize='16' FontWeight='SemiBold' Foreground='#F4F4F5' HorizontalAlignment='Center'/>
                            <TextBlock Text='New app launches, hidden background processes, network sockets, Mic/Webcam access &amp; tasks appear here in real time.'
                                       FontSize='12.5' Foreground='#71717A' HorizontalAlignment='Center' TextWrapping='Wrap' TextAlignment='Center' MaxWidth='460' Margin='0,8,0,0'/>
                        </StackPanel>
                    </Border>

                    <ListBox x:Name='EventsListBox' Background='Transparent' BorderThickness='0'
                             ScrollViewer.HorizontalScrollBarVisibility='Disabled'
                             VirtualizingStackPanel.IsVirtualizing='True'
                             VirtualizingStackPanel.VirtualizationMode='Recycling'
                             Padding='12,10'>
                        <ListBox.ItemContainerStyle>
                            <Style TargetType='ListBoxItem'>
                                <Setter Property='HorizontalContentAlignment' Value='Stretch'/>
                                <Setter Property='Padding' Value='0'/>
                                <Setter Property='Margin' Value='0,0,0,6'/>
                                <Setter Property='Background' Value='Transparent'/>
                                <Setter Property='Template'>
                                    <Setter.Value>
                                        <ControlTemplate TargetType='ListBoxItem'>
                                            <Border x:Name='CardBd' CornerRadius='8' Background='#202025' BorderBrush='#2A2A33' BorderThickness='1' Padding='12,9' Cursor='Hand'>
                                                <ContentPresenter/>
                                            </Border>
                                            <ControlTemplate.Triggers>
                                                <Trigger Property='IsMouseOver' Value='True'>
                                                    <Setter TargetName='CardBd' Property='Background' Value='#25252C'/>
                                                    <Setter TargetName='CardBd' Property='BorderBrush' Value='#3D3D4A'/>
                                                </Trigger>
                                                <Trigger Property='IsSelected' Value='True'>
                                                    <Setter TargetName='CardBd' Property='Background' Value='#262630'/>
                                                    <Setter TargetName='CardBd' Property='BorderBrush' Value='#D5A449'/>
                                                </Trigger>
                                            </ControlTemplate.Triggers>
                                        </ControlTemplate>
                                    </Setter.Value>
                                </Setter>
                            </Style>
                        </ListBox.ItemContainerStyle>
                        <ListBox.ItemTemplate>
                            <DataTemplate>
                                <Grid>
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width='128'/>
                                        <ColumnDefinition Width='*'/>
                                        <ColumnDefinition Width='68'/>
                                    </Grid.ColumnDefinitions>

                                    <StackPanel Grid.Column='0' VerticalAlignment='Top'>
                                        <Border Background='{Binding BadgeBackground}' CornerRadius='5' Padding='7,3.5' HorizontalAlignment='Left'>
                                            <StackPanel Orientation='Horizontal'>
                                                <Ellipse Width='6' Height='6' Fill='{Binding DotBrush}' Margin='0,0,5,0' VerticalAlignment='Center'/>
                                                <TextBlock Text='{Binding Badge}' Foreground='{Binding BadgeForeground}' FontSize='10' FontWeight='Bold' FontFamily='Consolas'/>
                                            </StackPanel>
                                        </Border>
                                        <TextBlock Text='{Binding TrustText}' Foreground='{Binding TrustBrush}' FontSize='10' Margin='1,4,0,0' TextTrimming='CharacterEllipsis'/>
                                    </StackPanel>

                                    <StackPanel Grid.Column='1' Margin='8,0,10,0'>
                                        <TextBlock Text='{Binding Title}' Foreground='#F4F4F5' FontSize='13' FontWeight='SemiBold' TextWrapping='Wrap'/>
                                        <TextBlock Text='{Binding Subtitle}' Foreground='#A1A1AA' FontSize='11.5' Margin='0,2.5,0,0' TextWrapping='Wrap'/>
                                        <TextBlock Text='{Binding Details}' Foreground='#71717A' FontSize='10.5' FontFamily='Consolas' Margin='0,3,0,0' TextWrapping='Wrap'/>
                                    </StackPanel>

                                    <TextBlock Grid.Column='2' Text='{Binding Time}' Foreground='#71717A' FontSize='11' FontFamily='Consolas' HorizontalAlignment='Right' VerticalAlignment='Top'/>
                                </Grid>
                            </DataTemplate>
                        </ListBox.ItemTemplate>
                    </ListBox>
                </Grid>

                <GridSplitter Grid.Column='1' HorizontalAlignment='Stretch' Background='#24242B' Cursor='SizeWE'/>

                <!-- RIGHT EVENT INSPECTOR PANEL -->
                <Border Grid.Column='2' Background='#1B1B20' BorderBrush='#27272E' BorderThickness='1,0,0,0' Padding='14'>
                    <Grid>
                        <Grid.RowDefinitions>
                            <RowDefinition Height='Auto'/>
                            <RowDefinition Height='*'/>
                        </Grid.RowDefinitions>

                        <Border Grid.Row='0' BorderBrush='#282832' BorderThickness='0,0,0,1' Padding='0,0,0,8' Margin='0,0,0,10'>
                            <Grid>
                                <TextBlock Text='EVENT INSPECTOR' FontSize='10' FontWeight='Bold' FontFamily='Consolas' Foreground='#A1A1AA' VerticalAlignment='Center'/>
                                <TextBlock Text='X-RAY DETAILS' FontSize='9.5' FontFamily='Consolas' Foreground='#D5A449' HorizontalAlignment='Right' VerticalAlignment='Center'/>
                            </Grid>
                        </Border>

                        <StackPanel x:Name='InspectorEmptyHint' Grid.Row='1' VerticalAlignment='Center' HorizontalAlignment='Center'>
                            <TextBlock Text='Select an Activity Card' Foreground='#E4E4E7' FontSize='14' FontWeight='SemiBold' HorizontalAlignment='Center'/>
                            <TextBlock Text='Click any event on the left to inspect its digital signature, parent process tree, decoded command line, and quick actions.'
                                       Foreground='#71717A' FontSize='12' TextWrapping='Wrap' TextAlignment='Center' Margin='8,8,8,0'/>
                        </StackPanel>

                        <ScrollViewer x:Name='InspectorDetailScroll' Grid.Row='1' Visibility='Collapsed'
                                      VerticalScrollBarVisibility='Auto' HorizontalScrollBarVisibility='Disabled'>
                            <StackPanel>
                                <Grid Margin='0,0,0,10'>
                                    <Border x:Name='InspBadgePill' Background='#2A2A32' CornerRadius='5' Padding='8,4' HorizontalAlignment='Left'>
                                        <TextBlock x:Name='InspBadgeText' Text='EVENT' FontSize='10.5' FontWeight='Bold' FontFamily='Consolas' Foreground='#F4F4F5'/>
                                    </Border>
                                    <TextBlock x:Name='InspTimeText' Text='00:00:00' FontSize='11' FontFamily='Consolas' Foreground='#71717A' HorizontalAlignment='Right' VerticalAlignment='Center'/>
                                </Grid>

                                <TextBlock x:Name='InspTrustText' Text='✓ Publisher' FontSize='11.5' FontWeight='SemiBold' Foreground='#34D399' Margin='0,0,0,8'/>
                                <TextBlock x:Name='InspTitleText' Text='' FontSize='14' FontWeight='SemiBold' Foreground='#F4F4F5' TextWrapping='Wrap' Margin='0,0,0,6'/>
                                <TextBlock x:Name='InspSubtitleText' Text='' FontSize='12' Foreground='#A1A1AA' TextWrapping='Wrap' Margin='0,0,0,12'/>

                                <TextBlock Text='PARENT &amp; PROCESS CHAIN' FontSize='9.5' FontWeight='Bold' Foreground='#D5A449' Margin='0,0,0,3'/>
                                <Border Background='#141418' BorderBrush='#282830' BorderThickness='1' CornerRadius='6' Padding='9,7' Margin='0,0,0,10'>
                                    <TextBlock x:Name='InspParentText' Text='—' FontSize='11' FontFamily='Consolas' Foreground='#D4D4D8' TextWrapping='Wrap'/>
                                </Border>

                                <TextBlock Text='EXECUTABLE / FILE PATH' FontSize='9.5' FontWeight='Bold' Foreground='#D5A449' Margin='0,0,0,3'/>
                                <Border Background='#141418' BorderBrush='#282830' BorderThickness='1' CornerRadius='6' Padding='9,7' Margin='0,0,0,10'>
                                    <TextBlock x:Name='InspPathText' Text='—' FontSize='11' FontFamily='Consolas' Foreground='#D4D4D8' TextWrapping='Wrap'/>
                                </Border>

                                <TextBlock Text='NETWORK ENDPOINT / HOST' FontSize='9.5' FontWeight='Bold' Foreground='#D5A449' Margin='0,0,0,3'/>
                                <Border Background='#141418' BorderBrush='#282830' BorderThickness='1' CornerRadius='6' Padding='9,7' Margin='0,0,0,10'>
                                    <TextBlock x:Name='InspEndpointText' Text='—' FontSize='11' FontFamily='Consolas' Foreground='#34D399' TextWrapping='Wrap'/>
                                </Border>

                                <TextBlock Text='COMMAND LINE / DECODED SCRIPT' FontSize='9.5' FontWeight='Bold' Foreground='#D5A449' Margin='0,0,0,3'/>
                                <Border Background='#141418' BorderBrush='#282830' BorderThickness='1' CornerRadius='6' Padding='8,6' Margin='0,0,0,12'>
                                    <TextBox x:Name='InspCmdBox' Text='—' IsReadOnly='True' TextWrapping='Wrap' Background='Transparent'
                                             Foreground='#E4E4E7' BorderThickness='0' FontSize='11' FontFamily='Consolas' MaxHeight='120' VerticalScrollBarVisibility='Auto'/>
                                </Border>

                                <TextBlock Text='QUICK ACTIONS' FontSize='9.5' FontWeight='Bold' Foreground='#D5A449' Margin='0,0,0,6'/>
                                <UniformGrid Columns='2'>
                                    <Button x:Name='InspOpenLocBtn' Style='{StaticResource DarkPillBtn}' Content='Open File Folder' Margin='0,0,4,6'/>
                                    <Button x:Name='InspCopyBtn' Style='{StaticResource DarkPillBtn}' Content='Copy Report' Margin='4,0,0,6'/>
                                    <Button x:Name='InspMuteBtn' Style='{StaticResource DarkPillBtn}' Content='Mute Process' Margin='0,0,4,0'/>
                                    <Button x:Name='InspKillBtn' Style='{StaticResource DarkPillBtn}' Content='End Process' Foreground='#F87171' Margin='4,0,0,0'/>
                                </UniformGrid>
                            </StackPanel>
                        </ScrollViewer>
                    </Grid>
                </Border>
            </Grid>

            <!-- VIEW 2: HIDDEN BACKGROUND PROCESSES X-RAY -->
            <Grid x:Name='ViewHidden' Visibility='Collapsed'>
                <Grid.RowDefinitions>
                    <RowDefinition Height='44'/>
                    <RowDefinition Height='*'/>
                    <RowDefinition Height='46'/>
                </Grid.RowDefinitions>

                <Border Grid.Row='0' Background='#1B1B20' BorderBrush='#27272E' BorderThickness='0,0,0,1' Padding='14,0'>
                    <Grid>
                        <TextBlock x:Name='HiddenSummaryText' Text='Scanning windowless background processes...' Foreground='#D4D4D8' FontSize='12.5' FontWeight='Medium' VerticalAlignment='Center'/>
                        <StackPanel Orientation='Horizontal' HorizontalAlignment='Right' VerticalAlignment='Center'>
                            <CheckBox x:Name='HiddenOnlyNetCheck' Content='Only with external network' Foreground='#A1A1AA' FontSize='12' Margin='0,0,16,0' VerticalAlignment='Center'/>
                            <CheckBox x:Name='HiddenNoMsCheck' Content='Hide Microsoft processes' Foreground='#A1A1AA' FontSize='12' VerticalAlignment='Center'/>
                        </StackPanel>
                    </Grid>
                </Border>

                <ListBox x:Name='HiddenListBox' Grid.Row='1' Background='Transparent' BorderThickness='0'
                         ScrollViewer.HorizontalScrollBarVisibility='Disabled' Padding='14,10'>
                    <ListBox.ItemContainerStyle>
                        <Style TargetType='ListBoxItem'>
                            <Setter Property='HorizontalContentAlignment' Value='Stretch'/>
                            <Setter Property='Padding' Value='0'/>
                            <Setter Property='Margin' Value='0,0,0,6'/>
                            <Setter Property='Background' Value='Transparent'/>
                            <Setter Property='Template'>
                                <Setter.Value>
                                    <ControlTemplate TargetType='ListBoxItem'>
                                        <Border x:Name='Bd' CornerRadius='8' Background='#202025' BorderBrush='#2A2A33' BorderThickness='1' Padding='12,9' Cursor='Hand'>
                                            <ContentPresenter/>
                                        </Border>
                                        <ControlTemplate.Triggers>
                                            <Trigger Property='IsSelected' Value='True'>
                                                <Setter TargetName='Bd' Property='BorderBrush' Value='#D5A449'/>
                                                <Setter TargetName='Bd' Property='Background' Value='#262630'/>
                                            </Trigger>
                                        </ControlTemplate.Triggers>
                                    </ControlTemplate>
                                </Setter.Value>
                            </Setter>
                        </Style>
                    </ListBox.ItemContainerStyle>
                    <ListBox.ItemTemplate>
                        <DataTemplate>
                            <Grid>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width='190'/>
                                    <ColumnDefinition Width='*'/>
                                    <ColumnDefinition Width='Auto'/>
                                </Grid.ColumnDefinitions>
                                <StackPanel Grid.Column='0' VerticalAlignment='Center'>
                                    <TextBlock Text='{Binding ProcessName}' Foreground='#F4F4F5' FontSize='13' FontWeight='SemiBold'/>
                                    <Border Background='{Binding TrustBg}' CornerRadius='4' Padding='6,2' HorizontalAlignment='Left' Margin='0,4,0,0'>
                                        <TextBlock Text='{Binding TrustText}' Foreground='{Binding TrustFg}' FontSize='10' FontWeight='Bold'/>
                                    </Border>
                                </StackPanel>
                                <StackPanel Grid.Column='1' Margin='10,0'>
                                    <TextBlock Text='{Binding ServiceOrRole}' Foreground='#E4E4E7' FontSize='12' TextWrapping='Wrap'/>
                                    <TextBlock Text='{Binding SummaryLine}' Foreground='#A1A1AA' FontSize='11' FontFamily='Consolas' Margin='0,3,0,0' TextWrapping='Wrap'/>
                                    <TextBlock Text='{Binding CommandLine}' Foreground='#71717A' FontSize='10.5' FontFamily='Consolas' Margin='0,3,0,0' TextTrimming='CharacterEllipsis'/>
                                </StackPanel>
                            </Grid>
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>

                <Border Grid.Row='2' Background='#1B1B20' BorderBrush='#27272E' BorderThickness='0,1,0,0' Padding='14,0'>
                    <Grid>
                        <TextBlock x:Name='HiddenInspectorText' Text='Select any hidden process above to inspect path, open its folder, mute, or terminate.'
                                   Foreground='#A1A1AA' FontSize='11.5' FontFamily='Consolas' VerticalAlignment='Center' TextTrimming='CharacterEllipsis' Margin='0,0,340,0'/>
                        <StackPanel Orientation='Horizontal' HorizontalAlignment='Right' VerticalAlignment='Center'>
                            <Button x:Name='HiddenOpenLocBtn' Style='{StaticResource DarkPillBtn}' Content='Open Folder' Margin='0,0,8,0'/>
                            <Button x:Name='HiddenMuteBtn' Style='{StaticResource DarkPillBtn}' Content='Mute' Margin='0,0,8,0'/>
                            <Button x:Name='HiddenKillBtn' Style='{StaticResource DarkPillBtn}' Content='End Process' Foreground='#F87171'/>
                        </StackPanel>
                    </Grid>
                </Border>
            </Grid>

            <!-- VIEW 3: NETWORK & HARDWARE PRIVACY -->
            <Grid x:Name='ViewNetwork' Visibility='Collapsed'>
                <Grid.RowDefinitions>
                    <RowDefinition Height='46'/>
                    <RowDefinition Height='*'/>
                </Grid.RowDefinitions>

                <Border Grid.Row='0' Background='#1B1B20' BorderBrush='#27272E' BorderThickness='0,0,0,1' Padding='14,0'>
                    <Grid>
                        <StackPanel Orientation='Horizontal' VerticalAlignment='Center'>
                            <Button x:Name='SubConnsBtn' Style='{StaticResource NavTabBtn}' Background='#2E2E37' Foreground='#F4F4F5' Content='Outbound (0)'/>
                            <Button x:Name='SubListenBtn' Style='{StaticResource NavTabBtn}' Content='Listening Ports (0)'/>
                            <Button x:Name='SubPrivacyBtn' Style='{StaticResource NavTabBtn}' Content='Mic / Cam / Loc (0)'/>
                        </StackPanel>
                        <StackPanel Orientation='Horizontal' HorizontalAlignment='Right' VerticalAlignment='Center'>
                            <Border Background='#22222A' BorderBrush='#30303B' BorderThickness='1' CornerRadius='5' Padding='8,3' Margin='0,0,6,0'>
                                <TextBlock x:Name='MicBannerText' Text='Mic: Idle' FontSize='11' FontFamily='Consolas' Foreground='#34D399'/>
                            </Border>
                            <Border Background='#22222A' BorderBrush='#30303B' BorderThickness='1' CornerRadius='5' Padding='8,3' Margin='0,0,6,0'>
                                <TextBlock x:Name='CamBannerText' Text='Cam: Idle' FontSize='11' FontFamily='Consolas' Foreground='#34D399'/>
                            </Border>
                            <Border Background='#22222A' BorderBrush='#30303B' BorderThickness='1' CornerRadius='5' Padding='8,3'>
                                <TextBlock x:Name='LocBannerText' Text='Location: Idle' FontSize='11' FontFamily='Consolas' Foreground='#A1A1AA'/>
                            </Border>
                        </StackPanel>
                    </Grid>
                </Border>

                <ListBox x:Name='ConnsListBox' Grid.Row='1' Visibility='Visible' Background='Transparent' BorderThickness='0'
                         ScrollViewer.HorizontalScrollBarVisibility='Disabled' Padding='14,10'>
                    <ListBox.ItemContainerStyle>
                        <Style TargetType='ListBoxItem'>
                            <Setter Property='HorizontalContentAlignment' Value='Stretch'/>
                            <Setter Property='Padding' Value='0'/>
                            <Setter Property='Margin' Value='0,0,0,6'/>
                            <Setter Property='Background' Value='Transparent'/>
                        </Style>
                    </ListBox.ItemContainerStyle>
                    <ListBox.ItemTemplate>
                        <DataTemplate>
                            <Border CornerRadius='8' Background='#202025' BorderBrush='#2A2A33' BorderThickness='1' Padding='12,9'>
                                <Grid>
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width='108'/>
                                        <ColumnDefinition Width='*'/>
                                    </Grid.ColumnDefinitions>
                                    <Border Grid.Column='0' Background='{Binding VisibilityBg}' CornerRadius='5' Padding='7,4' HorizontalAlignment='Left' VerticalAlignment='Top'>
                                        <TextBlock Text='{Binding VisibilityTag}' Foreground='{Binding VisibilityFg}' FontSize='10' FontWeight='Bold' FontFamily='Consolas'/>
                                    </Border>
                                    <StackPanel Grid.Column='1' Margin='8,0,0,0'>
                                        <TextBlock Text='{Binding DisplayProcess}' Foreground='#F4F4F5' FontSize='13' FontWeight='SemiBold' TextWrapping='Wrap'/>
                                        <TextBlock Text='{Binding EndpointLine}' Foreground='#34D399' FontSize='12' FontFamily='Consolas' Margin='0,2,0,0' TextWrapping='Wrap'/>
                                        <TextBlock Text='{Binding TrustMeta}' Foreground='#A1A1AA' FontSize='11' Margin='0,2,0,0'/>
                                    </StackPanel>
                                </Grid>
                            </Border>
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>

                <ListBox x:Name='ListenListBox' Grid.Row='1' Visibility='Collapsed' Background='Transparent' BorderThickness='0'
                         ScrollViewer.HorizontalScrollBarVisibility='Disabled' Padding='14,10'>
                    <ListBox.ItemContainerStyle>
                        <Style TargetType='ListBoxItem'>
                            <Setter Property='HorizontalContentAlignment' Value='Stretch'/>
                            <Setter Property='Padding' Value='0'/>
                            <Setter Property='Margin' Value='0,0,0,6'/>
                            <Setter Property='Background' Value='Transparent'/>
                        </Style>
                    </ListBox.ItemContainerStyle>
                    <ListBox.ItemTemplate>
                        <DataTemplate>
                            <Border CornerRadius='8' Background='#202025' BorderBrush='#2A2A33' BorderThickness='1' Padding='12,9'>
                                <Grid>
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width='92'/>
                                        <ColumnDefinition Width='*'/>
                                    </Grid.ColumnDefinitions>
                                    <Border Grid.Column='0' Background='#2E2218' CornerRadius='5' Padding='7,4' HorizontalAlignment='Left' VerticalAlignment='Top'>
                                        <TextBlock Text='{Binding LocalPort}' Foreground='#FBBF24' FontSize='11' FontWeight='Bold' FontFamily='Consolas'/>
                                    </Border>
                                    <StackPanel Grid.Column='1' Margin='8,0,0,0'>
                                        <TextBlock Text='{Binding DisplayProcess}' Foreground='#F4F4F5' FontSize='13' FontWeight='SemiBold' TextWrapping='Wrap'/>
                                        <TextBlock Text='{Binding BindLine}' Foreground='#D4D4D8' FontSize='12' FontFamily='Consolas' Margin='0,2,0,0'/>
                                        <TextBlock Text='{Binding TrustMeta}' Foreground='#71717A' FontSize='11' Margin='0,2,0,0'/>
                                    </StackPanel>
                                </Grid>
                            </Border>
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>

                <ListBox x:Name='PrivacyListBox' Grid.Row='1' Visibility='Collapsed' Background='Transparent' BorderThickness='0'
                         ScrollViewer.HorizontalScrollBarVisibility='Disabled' Padding='14,10'>
                    <ListBox.ItemContainerStyle>
                        <Style TargetType='ListBoxItem'>
                            <Setter Property='HorizontalContentAlignment' Value='Stretch'/>
                            <Setter Property='Padding' Value='0'/>
                            <Setter Property='Margin' Value='0,0,0,6'/>
                            <Setter Property='Background' Value='Transparent'/>
                        </Style>
                    </ListBox.ItemContainerStyle>
                    <ListBox.ItemTemplate>
                        <DataTemplate>
                            <Border CornerRadius='8' Background='#202025' BorderBrush='#2A2A33' BorderThickness='1' Padding='12,9'>
                                <Grid>
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width='118'/>
                                        <ColumnDefinition Width='*'/>
                                    </Grid.ColumnDefinitions>
                                    <Border Grid.Column='0' Background='{Binding BadgeBg}' CornerRadius='5' Padding='8,4' HorizontalAlignment='Left' VerticalAlignment='Top'>
                                        <TextBlock Text='{Binding StatusBadge}' Foreground='{Binding BadgeFg}' FontSize='10' FontWeight='Bold' FontFamily='Consolas'/>
                                    </Border>
                                    <StackPanel Grid.Column='1' Margin='8,0,0,0'>
                                        <TextBlock Text='{Binding AppName}' Foreground='#F4F4F5' FontSize='13' FontWeight='SemiBold'/>
                                        <TextBlock Text='{Binding DetailsLine}' Foreground='#A1A1AA' FontSize='11.5' Margin='0,2,0,0' TextWrapping='Wrap'/>
                                    </StackPanel>
                                </Grid>
                            </Border>
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>
            </Grid>

            <!-- VIEW 4: STARTUP, SCHEDULED TASKS & FILE DROPS -->
            <Grid x:Name='ViewStartup' Visibility='Collapsed'>
                <Grid.RowDefinitions>
                    <RowDefinition Height='46'/>
                    <RowDefinition Height='*'/>
                </Grid.RowDefinitions>

                <Border Grid.Row='0' Background='#1B1B20' BorderBrush='#27272E' BorderThickness='0,0,0,1' Padding='14,0'>
                    <StackPanel Orientation='Horizontal' VerticalAlignment='Center'>
                        <Button x:Name='SubStartupBtn' Style='{StaticResource NavTabBtn}' Background='#2E2E37' Foreground='#F4F4F5' Content='Startup Apps (0)'/>
                        <Button x:Name='SubTasksBtn' Style='{StaticResource NavTabBtn}' Content='Running Tasks (0)'/>
                        <Button x:Name='SubDropsBtn' Style='{StaticResource NavTabBtn}' Content='Temp / Drops (0)'/>
                    </StackPanel>
                </Border>

                <ListBox x:Name='StartupListBox' Grid.Row='1' Visibility='Visible' Background='Transparent' BorderThickness='0'
                         ScrollViewer.HorizontalScrollBarVisibility='Disabled' Padding='14,10'>
                    <ListBox.ItemContainerStyle>
                        <Style TargetType='ListBoxItem'>
                            <Setter Property='HorizontalContentAlignment' Value='Stretch'/>
                            <Setter Property='Padding' Value='0'/>
                            <Setter Property='Margin' Value='0,0,0,6'/>
                            <Setter Property='Background' Value='Transparent'/>
                        </Style>
                    </ListBox.ItemContainerStyle>
                    <ListBox.ItemTemplate>
                        <DataTemplate>
                            <Border CornerRadius='8' Background='#202025' BorderBrush='#2A2A33' BorderThickness='1' Padding='12,9'>
                                <Grid>
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width='135'/>
                                        <ColumnDefinition Width='*'/>
                                    </Grid.ColumnDefinitions>
                                    <Border Grid.Column='0' Background='{Binding KindBg}' CornerRadius='5' Padding='8,4' HorizontalAlignment='Left' VerticalAlignment='Top'>
                                        <TextBlock Text='{Binding Kind}' Foreground='{Binding KindFg}' FontSize='10' FontWeight='Bold' FontFamily='Consolas'/>
                                    </Border>
                                    <StackPanel Grid.Column='1' Margin='8,0,0,0'>
                                        <TextBlock Text='{Binding Name}' Foreground='#F4F4F5' FontSize='13' FontWeight='SemiBold'/>
                                        <TextBlock Text='{Binding MetaLine}' Foreground='#A1A1AA' FontSize='11.5' Margin='0,2,0,0'/>
                                        <TextBlock Text='{Binding Command}' Foreground='#71717A' FontSize='11' FontFamily='Consolas' Margin='0,3,0,0' TextWrapping='Wrap'/>
                                    </StackPanel>
                                </Grid>
                            </Border>
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>

                <ListBox x:Name='TasksListBox' Grid.Row='1' Visibility='Collapsed' Background='Transparent' BorderThickness='0'
                         ScrollViewer.HorizontalScrollBarVisibility='Disabled' Padding='14,10'>
                    <ListBox.ItemContainerStyle>
                        <Style TargetType='ListBoxItem'>
                            <Setter Property='HorizontalContentAlignment' Value='Stretch'/>
                            <Setter Property='Padding' Value='0'/>
                            <Setter Property='Margin' Value='0,0,0,6'/>
                            <Setter Property='Background' Value='Transparent'/>
                        </Style>
                    </ListBox.ItemContainerStyle>
                    <ListBox.ItemTemplate>
                        <DataTemplate>
                            <Border CornerRadius='8' Background='#202025' BorderBrush='#2A2A33' BorderThickness='1' Padding='12,9'>
                                <Grid>
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width='115'/>
                                        <ColumnDefinition Width='*'/>
                                    </Grid.ColumnDefinitions>
                                    <Border Grid.Column='0' Background='#29233B' CornerRadius='5' Padding='8,4' HorizontalAlignment='Left' VerticalAlignment='Top'>
                                        <TextBlock Text='{Binding State}' Foreground='#A5B4FC' FontSize='10' FontWeight='Bold' FontFamily='Consolas'/>
                                    </Border>
                                    <StackPanel Grid.Column='1' Margin='8,0,0,0'>
                                        <TextBlock Text='{Binding Name}' Foreground='#F4F4F5' FontSize='13' FontWeight='SemiBold'/>
                                        <TextBlock Text='{Binding Description}' Foreground='#A1A1AA' FontSize='11.5' Margin='0,2,0,0' TextWrapping='Wrap'/>
                                        <TextBlock Text='{Binding Command}' Foreground='#71717A' FontSize='10.5' FontFamily='Consolas' Margin='0,3,0,0'/>
                                    </StackPanel>
                                </Grid>
                            </Border>
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>

                <ListBox x:Name='DropsListBox' Grid.Row='1' Visibility='Collapsed' Background='Transparent' BorderThickness='0'
                         ScrollViewer.HorizontalScrollBarVisibility='Disabled' Padding='14,10'>
                    <ListBox.ItemContainerStyle>
                        <Style TargetType='ListBoxItem'>
                            <Setter Property='HorizontalContentAlignment' Value='Stretch'/>
                            <Setter Property='Padding' Value='0'/>
                            <Setter Property='Margin' Value='0,0,0,6'/>
                            <Setter Property='Background' Value='Transparent'/>
                        </Style>
                    </ListBox.ItemContainerStyle>
                    <ListBox.ItemTemplate>
                        <DataTemplate>
                            <Border CornerRadius='8' Background='#202025' BorderBrush='#2A2A33' BorderThickness='1' Padding='12,9'>
                                <Grid>
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width='115'/>
                                        <ColumnDefinition Width='*'/>
                                    </Grid.ColumnDefinitions>
                                    <Border Grid.Column='0' Background='#381E22' CornerRadius='5' Padding='8,4' HorizontalAlignment='Left' VerticalAlignment='Top'>
                                        <TextBlock Text='{Binding State}' Foreground='#F87171' FontSize='10' FontWeight='Bold' FontFamily='Consolas'/>
                                    </Border>
                                    <StackPanel Grid.Column='1' Margin='8,0,0,0'>
                                        <TextBlock Text='{Binding Name}' Foreground='#F4F4F5' FontSize='13' FontWeight='SemiBold'/>
                                        <TextBlock Text='{Binding Description}' Foreground='#A1A1AA' FontSize='11.5' Margin='0,2,0,0'/>
                                        <TextBlock Text='{Binding Command}' Foreground='#71717A' FontSize='10.5' FontFamily='Consolas' Margin='0,3,0,0'/>
                                    </StackPanel>
                                </Grid>
                            </Border>
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>
            </Grid>
        </Grid>

        <!-- 5. BOTTOM CONTROL & PRIVACY FOOTER -->
        <Border Grid.Row='4' Background='#1F1F24' BorderBrush='#2A2A32' BorderThickness='0,1,0,0' Padding='16,0'>
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width='Auto'/>
                    <ColumnDefinition Width='*'/>
                    <ColumnDefinition Width='Auto'/>
                </Grid.ColumnDefinitions>

                <StackPanel Grid.Column='0' Orientation='Horizontal' VerticalAlignment='Center'>
                    <TextBlock x:Name='DiskFreeText' Text='0.0 GB free' Foreground='#A1A1AA' FontSize='12' FontFamily='Consolas' VerticalAlignment='Center'/>
                    <TextBlock Text='  •  In-Memory Only (No Disk Logs)' Foreground='#34D399' FontSize='11.5' VerticalAlignment='Center' Margin='4,0,12,0'/>
                    <Button x:Name='SettingsBtn' Style='{StaticResource DarkPillBtn}' Content='⚙ Settings' Padding='10,4'/>
                </StackPanel>

                <StackPanel Grid.Column='2' Orientation='Horizontal' VerticalAlignment='Center'>
                    <Button x:Name='ClearFeedBtn' Style='{StaticResource DarkPillBtn}' Content='↺ Clear' Padding='10,4' Margin='0,0,12,0' ToolTip='Clear visual activity feed'/>

                    <TextBlock Text='Mask IPs (###)' Foreground='#A1A1AA' FontSize='12' VerticalAlignment='Center' Margin='0,0,7,0'/>
                    <ToggleButton x:Name='IpMaskToggle' Style='{StaticResource PillSwitchStyle}' IsChecked='True' VerticalAlignment='Center' Margin='0,0,14,0'
                                  ToolTip='Toggle real-time IP masking across all views and exported snapshots'/>

                    <TextBlock Text='Deep Hidden' Foreground='#A1A1AA' FontSize='12' VerticalAlignment='Center' Margin='0,0,7,0'/>
                    <ToggleButton x:Name='DeepBgToggle' Style='{StaticResource PillSwitchStyle}' IsChecked='True' VerticalAlignment='Center' Margin='0,0,14,0'
                                  ToolTip='Show silent background process spawns, scheduled tasks &amp; telemetry'/>

                    <TextBlock Text='Startup' Foreground='#A1A1AA' FontSize='12' VerticalAlignment='Center' Margin='0,0,7,0'/>
                    <ToggleButton x:Name='StartupToggle' Style='{StaticResource PillSwitchStyle}' IsChecked='True' VerticalAlignment='Center' Margin='0,0,14,0'
                                  ToolTip='Launch Angel Core Logger automatically when Windows starts'/>

                    <Border Background='#141418' BorderBrush='#2E2E37' BorderThickness='1' CornerRadius='7' Padding='2' VerticalAlignment='Center'>
                        <StackPanel Orientation='Horizontal'>
                            <Button x:Name='LiveModeBtn' Content='Live' FontSize='11.5' FontWeight='SemiBold' Padding='10,3' BorderThickness='0' Cursor='Hand'
                                    Background='#2E2E37' Foreground='#F4F4F5'/>
                            <Button x:Name='PauseModeBtn' Content='Pause' FontSize='11.5' FontWeight='SemiBold' Padding='10,3' BorderThickness='0' Cursor='Hand'
                                    Background='Transparent' Foreground='#71717A'/>
                        </StackPanel>
                    </Border>
                </StackPanel>
            </Grid>
        </Border>
    </Grid>
</Border>";

            Border root = (Border)XamlReader.Parse(xaml);
            this.Content = root;

            string icoPath = Path.Combine(_exeDir, "AngelSystemLoggerPro.ico");
            if (File.Exists(icoPath))
            {
                try { this.Icon = new BitmapImage(new Uri(icoPath, UriKind.Absolute)); } catch { }
            }

            // Bind Titlebar controls
            _privBadge = (Border)root.FindName("PrivBadge");
            _privBadgeText = (TextBlock)root.FindName("PrivBadgeText");
            _vitalsText = (TextBlock)root.FindName("VitalsText");
            if (ProcessActionHelper.IsAdministrator())
            {
                _privBadge.Background = ProcessActionHelper.MakeFrozenBrush("#162E28");
                _privBadge.BorderBrush = ProcessActionHelper.MakeFrozenBrush("#10B981");
                _privBadgeText.Text = "ADMIN ELEVATED";
                _privBadgeText.Foreground = ProcessActionHelper.MakeFrozenBrush("#34D399");
            }

            ((Button)root.FindName("TrayBtn")).Click += (s, e) => { this.Hide(); this.ShowInTaskbar = false; };
            ((Button)root.FindName("MinBtn")).Click += (s, e) => this.WindowState = WindowState.Minimized;
            ((Button)root.FindName("MaxBtn")).Click += (s, e) => ToggleMaximize();
            ((Button)root.FindName("CloseBtn")).Click += (s, e) => this.Close();

            // Navigation tabs & views
            _navActivityBtn = (Button)root.FindName("NavActivityBtn");
            _navHiddenBtn = (Button)root.FindName("NavHiddenBtn");
            _navNetworkBtn = (Button)root.FindName("NavNetworkBtn");
            _navStartupBtn = (Button)root.FindName("NavStartupBtn");

            _viewActivity = (Grid)root.FindName("ViewActivity");
            _viewHidden = (Grid)root.FindName("ViewHidden");
            _viewNetwork = (Grid)root.FindName("ViewNetwork");
            _viewStartup = (Grid)root.FindName("ViewStartup");

            _navActivityBtn.Click += (s, e) => SwitchMainTab("ACTIVITY");
            _navHiddenBtn.Click += (s, e) => SwitchMainTab("HIDDEN");
            _navNetworkBtn.Click += (s, e) => SwitchMainTab("NETWORK");
            _navStartupBtn.Click += (s, e) => SwitchMainTab("STARTUP");

            // Search & Top actions
            _searchBox = (TextBox)root.FindName("SearchBox");
            _searchWatermark = (TextBlock)root.FindName("SearchWatermark");
            _searchClearBtn = (Button)root.FindName("SearchClearBtn");
            _engineStatusDot = (Ellipse)root.FindName("EngineStatusDot");
            _engineStatusText = (TextBlock)root.FindName("EngineStatusText");

            _searchBox.TextChanged += (s, e) =>
            {
                _searchQuery = (_searchBox.Text ?? "").Trim();
                bool empty = string.IsNullOrEmpty(_searchQuery);
                _searchWatermark.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
                _searchClearBtn.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
                RefreshAllViews();
            };
            _searchClearBtn.Click += (s, e) => { _searchBox.Text = ""; };

            ((Button)root.FindName("DeepScanBtn")).Click += (s, e) => TriggerDeepScan();

            // Clickable Stat Filter Cards
            _statAllCard = (Border)root.FindName("StatAllCard");
            _statHiddenCard = (Border)root.FindName("StatHiddenCard");
            _statNetCard = (Border)root.FindName("StatNetCard");
            _statDnsCard = (Border)root.FindName("StatDnsCard");
            _statAppCard = (Border)root.FindName("StatAppCard");
            _statPrivCard = (Border)root.FindName("StatPrivCard");

            _statAllText = (TextBlock)root.FindName("StatAllText");
            _statHiddenText = (TextBlock)root.FindName("StatHiddenText");
            _statNetText = (TextBlock)root.FindName("StatNetText");
            _statDnsText = (TextBlock)root.FindName("StatDnsText");
            _statAppText = (TextBlock)root.FindName("StatAppText");
            _statPrivText = (TextBlock)root.FindName("StatPrivText");

            _statAllCard.MouseLeftButtonUp += (s, e) => SelectCategoryFilter("ALL");
            _statHiddenCard.MouseLeftButtonUp += (s, e) => SelectCategoryFilter("HIDDEN");
            _statNetCard.MouseLeftButtonUp += (s, e) => SelectCategoryFilter("NETWORK");
            _statDnsCard.MouseLeftButtonUp += (s, e) => SelectCategoryFilter("DNS");
            _statAppCard.MouseLeftButtonUp += (s, e) => SelectCategoryFilter("APP_WIN");
            _statPrivCard.MouseLeftButtonUp += (s, e) => SelectCategoryFilter("PRIV_SYS");

            // Activity Feed & Inspector
            _emptyStatePanel = (Border)root.FindName("EmptyStatePanel");
            _eventsListBox = (ListBox)root.FindName("EventsListBox");
            _inspectorEmptyHint = (StackPanel)root.FindName("InspectorEmptyHint");
            _inspectorDetailScroll = (ScrollViewer)root.FindName("InspectorDetailScroll");
            _inspBadgePill = (Border)root.FindName("InspBadgePill");
            _inspBadgeText = (TextBlock)root.FindName("InspBadgeText");
            _inspTimeText = (TextBlock)root.FindName("InspTimeText");
            _inspTrustText = (TextBlock)root.FindName("InspTrustText");
            _inspTitleText = (TextBlock)root.FindName("InspTitleText");
            _inspSubtitleText = (TextBlock)root.FindName("InspSubtitleText");
            _inspParentText = (TextBlock)root.FindName("InspParentText");
            _inspPathText = (TextBlock)root.FindName("InspPathText");
            _inspEndpointText = (TextBlock)root.FindName("InspEndpointText");
            _inspCmdBox = (TextBox)root.FindName("InspCmdBox");
            _inspOpenLocBtn = (Button)root.FindName("InspOpenLocBtn");
            _inspMuteBtn = (Button)root.FindName("InspMuteBtn");
            _inspKillBtn = (Button)root.FindName("InspKillBtn");

            _eventsListBox.SelectionChanged += (s, e) => PopulateEventInspector(_eventsListBox.SelectedItem as ActivityEventItem);
            _inspOpenLocBtn.Click += (s, e) =>
            {
                if (_selectedEvent != null) ProcessActionHelper.OpenFileLocation(_selectedEvent.ProcessPath);
            };
            ((Button)root.FindName("InspCopyBtn")).Click += (s, e) => CopySelectedEventReport();
            _inspMuteBtn.Click += (s, e) =>
            {
                if (_selectedEvent != null && !string.IsNullOrEmpty(_selectedEvent.ProcessName))
                    MuteProcess(_selectedEvent.ProcessName);
            };
            _inspKillBtn.Click += (s, e) =>
            {
                if (_selectedEvent != null && _selectedEvent.Pid > 0)
                    ProcessActionHelper.TryKillProcess(_selectedEvent.Pid, _selectedEvent.ProcessName, this);
            };

            // Hidden Background view
            _hiddenSummaryText = (TextBlock)root.FindName("HiddenSummaryText");
            _hiddenOnlyNetCheck = (CheckBox)root.FindName("HiddenOnlyNetCheck");
            _hiddenNoMsCheck = (CheckBox)root.FindName("HiddenNoMsCheck");
            _hiddenListBox = (ListBox)root.FindName("HiddenListBox");
            _hiddenInspectorText = (TextBlock)root.FindName("HiddenInspectorText");

            _hiddenOnlyNetCheck.Click += (s, e) => { _onlyHiddenWithNet = _hiddenOnlyNetCheck.IsChecked == true; if (_hiddenView != null) _hiddenView.Refresh(); };
            _hiddenNoMsCheck.Click += (s, e) => { _hideMicrosoftHidden = _hiddenNoMsCheck.IsChecked == true; if (_hiddenView != null) _hiddenView.Refresh(); };
            _hiddenListBox.SelectionChanged += (s, e) =>
            {
                _selectedHidden = _hiddenListBox.SelectedItem as HiddenProcessItem;
                if (_selectedHidden != null)
                {
                    _hiddenInspectorText.Text = string.Format("PID {0} | Path: {1}",
                        _selectedHidden.Pid,
                        string.IsNullOrEmpty(_selectedHidden.ProcessPath) ? "Protected System Process" : _selectedHidden.ProcessPath);
                }
            };
            ((Button)root.FindName("HiddenOpenLocBtn")).Click += (s, e) =>
            {
                if (_selectedHidden != null) ProcessActionHelper.OpenFileLocation(_selectedHidden.ProcessPath);
            };
            ((Button)root.FindName("HiddenMuteBtn")).Click += (s, e) =>
            {
                if (_selectedHidden != null) MuteProcess(_selectedHidden.ProcessName);
            };
            ((Button)root.FindName("HiddenKillBtn")).Click += (s, e) =>
            {
                if (_selectedHidden != null)
                {
                    if (ProcessActionHelper.TryKillProcess(_selectedHidden.Pid, _selectedHidden.ProcessName, this))
                        TriggerDeepScan();
                }
            };

            // Network & Privacy sub-tabs
            _subConnsBtn = (Button)root.FindName("SubConnsBtn");
            _subListenBtn = (Button)root.FindName("SubListenBtn");
            _subPrivacyBtn = (Button)root.FindName("SubPrivacyBtn");
            _connsListBox = (ListBox)root.FindName("ConnsListBox");
            _listenListBox = (ListBox)root.FindName("ListenListBox");
            _privacyListBox = (ListBox)root.FindName("PrivacyListBox");
            _micBannerText = (TextBlock)root.FindName("MicBannerText");
            _camBannerText = (TextBlock)root.FindName("CamBannerText");
            _locBannerText = (TextBlock)root.FindName("LocBannerText");

            _subConnsBtn.Click += (s, e) => SwitchNetSubTab("CONNS");
            _subListenBtn.Click += (s, e) => SwitchNetSubTab("LISTEN");
            _subPrivacyBtn.Click += (s, e) => SwitchNetSubTab("PRIVACY");

            // Startup & Tasks sub-tabs
            _subStartupBtn = (Button)root.FindName("SubStartupBtn");
            _subTasksBtn = (Button)root.FindName("SubTasksBtn");
            _subDropsBtn = (Button)root.FindName("SubDropsBtn");
            _startupListBox = (ListBox)root.FindName("StartupListBox");
            _tasksListBox = (ListBox)root.FindName("TasksListBox");
            _dropsListBox = (ListBox)root.FindName("DropsListBox");

            _subStartupBtn.Click += (s, e) => SwitchStartSubTab("STARTUP");
            _subTasksBtn.Click += (s, e) => SwitchStartSubTab("TASKS");
            _subDropsBtn.Click += (s, e) => SwitchStartSubTab("DROPS");

            // Footer controls
            _diskFreeText = (TextBlock)root.FindName("DiskFreeText");
            _settingsBtn = (Button)root.FindName("SettingsBtn");
            _ipMaskToggle = (ToggleButton)root.FindName("IpMaskToggle");
            _deepBgToggle = (ToggleButton)root.FindName("DeepBgToggle");
            _startupToggle = (ToggleButton)root.FindName("StartupToggle");
            _liveModeBtn = (Button)root.FindName("LiveModeBtn");
            _pauseModeBtn = (Button)root.FindName("PauseModeBtn");

            _ipMaskToggle.IsChecked = _settings.MaskIp;
            _deepBgToggle.IsChecked = _settings.DeepHiddenMode;
            _startupToggle.IsChecked = StartupManager.IsRegisteredInStartup();
            UpdateSettingsButtonLabel();

            _settingsBtn.Click += (s, e) => OpenSettingsDialog();
            ((Button)root.FindName("ClearFeedBtn")).Click += (s, e) => ClearLiveFeed();

            _ipMaskToggle.Click += (s, e) =>
            {
                _settings.MaskIp = (_ipMaskToggle.IsChecked == true);
                _settings.Save();
                foreach (ActivityEventItem item in _allEvents) item.MaskIp = _settings.MaskIp;
                foreach (HiddenProcessItem hp in _hiddenProcesses) hp.MaskIp = _settings.MaskIp;
                foreach (NetworkConnectionItem nc in _connections) nc.MaskIp = _settings.MaskIp;
                foreach (ListeningPortItem lp in _listeningPorts) lp.MaskIp = _settings.MaskIp;
                if (_selectedEvent != null) PopulateEventInspector(_selectedEvent);
            };

            _deepBgToggle.Click += (s, e) =>
            {
                _settings.DeepHiddenMode = (_deepBgToggle.IsChecked == true);
                _settings.Save();
                if (_eventsView != null) _eventsView.Refresh();
            };

            _startupToggle.Click += (s, e) =>
            {
                bool enable = (_startupToggle.IsChecked == true);
                StartupManager.SetStartupRegistry(_exePath, enable, _settings.StartMinimizedToTray);
                AddSyntheticEvent("SECURITY", enable ? "STARTUP ON" : "STARTUP OFF",
                    enable ? "Angel Core Logger enabled in Windows Startup Apps" : "Angel Core Logger removed from Windows Startup Apps",
                    "Updated HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run",
                    "Path: " + _exePath,
                    enable ? "Success" : "Warning");
            };

            _liveModeBtn.Click += (s, e) => SetPauseState(false);
            _pauseModeBtn.Click += (s, e) => SetPauseState(true);
        }

        private void ToggleMaximize()
        {
            this.WindowState = (this.WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
        }

        private void SwitchMainTab(string tab)
        {
            _activeMainTab = tab;
            _viewActivity.Visibility = (tab == "ACTIVITY") ? Visibility.Visible : Visibility.Collapsed;
            _viewHidden.Visibility = (tab == "HIDDEN") ? Visibility.Visible : Visibility.Collapsed;
            _viewNetwork.Visibility = (tab == "NETWORK") ? Visibility.Visible : Visibility.Collapsed;
            _viewStartup.Visibility = (tab == "STARTUP") ? Visibility.Visible : Visibility.Collapsed;

            StyleTabButton(_navActivityBtn, tab == "ACTIVITY");
            StyleTabButton(_navHiddenBtn, tab == "HIDDEN");
            StyleTabButton(_navNetworkBtn, tab == "NETWORK");
            StyleTabButton(_navStartupBtn, tab == "STARTUP");
        }

        private void SwitchNetSubTab(string sub)
        {
            _activeNetSubTab = sub;
            _connsListBox.Visibility = (sub == "CONNS") ? Visibility.Visible : Visibility.Collapsed;
            _listenListBox.Visibility = (sub == "LISTEN") ? Visibility.Visible : Visibility.Collapsed;
            _privacyListBox.Visibility = (sub == "PRIVACY") ? Visibility.Visible : Visibility.Collapsed;

            StyleTabButton(_subConnsBtn, sub == "CONNS");
            StyleTabButton(_subListenBtn, sub == "LISTEN");
            StyleTabButton(_subPrivacyBtn, sub == "PRIVACY");
        }

        private void SwitchStartSubTab(string sub)
        {
            _activeStartSubTab = sub;
            _startupListBox.Visibility = (sub == "STARTUP") ? Visibility.Visible : Visibility.Collapsed;
            _tasksListBox.Visibility = (sub == "TASKS") ? Visibility.Visible : Visibility.Collapsed;
            _dropsListBox.Visibility = (sub == "DROPS") ? Visibility.Visible : Visibility.Collapsed;

            StyleTabButton(_subStartupBtn, sub == "STARTUP");
            StyleTabButton(_subTasksBtn, sub == "TASKS");
            StyleTabButton(_subDropsBtn, sub == "DROPS");
        }

        private static void StyleTabButton(Button btn, bool active)
        {
            btn.Background = active ? ProcessActionHelper.MakeFrozenBrush("#2E2E37") : Brushes.Transparent;
            btn.Foreground = active ? ProcessActionHelper.MakeFrozenBrush("#F4F4F5") : ProcessActionHelper.MakeFrozenBrush("#A1A1AA");
        }

        private void SelectCategoryFilter(string cat)
        {
            _selectedCategory = cat;
            SwitchMainTab("ACTIVITY");

            SetCardHighlight(_statAllCard, cat == "ALL", "#D5A449", "#2E2E36");
            SetCardHighlight(_statHiddenCard, cat == "HIDDEN", "#FBBF24", "#382E1E");
            SetCardHighlight(_statNetCard, cat == "NETWORK", "#34D399", "#1E3A34");
            SetCardHighlight(_statDnsCard, cat == "DNS", "#C084FC", "#382640");
            SetCardHighlight(_statAppCard, cat == "APP_WIN", "#60A5FA", "#23354D");
            SetCardHighlight(_statPrivCard, cat == "PRIV_SYS", "#F87171", "#3D262A");

            if (_eventsView != null) _eventsView.Refresh();
        }

        private static void SetCardHighlight(Border card, bool active, string activeHex, string normalHex)
        {
            card.BorderBrush = ProcessActionHelper.MakeFrozenBrush(active ? activeHex : normalHex);
            card.BorderThickness = new Thickness(active ? 1.8 : 1.0);
            card.Background = ProcessActionHelper.MakeFrozenBrush(active ? "#282832" : "#222227");
        }

        private void SetupCollectionViews()
        {
            _eventsView = CollectionViewSource.GetDefaultView(_allEvents);
            _eventsView.Filter = FilterActivityEvent;
            _eventsListBox.ItemsSource = _eventsView;

            _hiddenView = CollectionViewSource.GetDefaultView(_hiddenProcesses);
            _hiddenView.Filter = FilterHiddenProcess;
            _hiddenListBox.ItemsSource = _hiddenView;

            _connectionsView = CollectionViewSource.GetDefaultView(_connections);
            _connectionsView.Filter = FilterConnection;
            _connsListBox.ItemsSource = _connectionsView;

            _listeningView = CollectionViewSource.GetDefaultView(_listeningPorts);
            _listeningView.Filter = FilterListeningPort;
            _listenListBox.ItemsSource = _listeningView;

            _privacyView = CollectionViewSource.GetDefaultView(_privacyItems);
            _privacyView.Filter = FilterPrivacyItem;
            _privacyListBox.ItemsSource = _privacyView;

            _startupView = CollectionViewSource.GetDefaultView(_startupItems);
            _startupView.Filter = FilterStartupTaskItem;
            _startupListBox.ItemsSource = _startupView;

            _tasksView = CollectionViewSource.GetDefaultView(_runningTasks);
            _tasksView.Filter = FilterStartupTaskItem;
            _tasksListBox.ItemsSource = _tasksView;

            _dropsView = CollectionViewSource.GetDefaultView(_fileDrops);
            _dropsView.Filter = FilterStartupTaskItem;
            _dropsListBox.ItemsSource = _dropsView;
        }

        private void RefreshAllViews()
        {
            if (_eventsView != null) _eventsView.Refresh();
            if (_hiddenView != null) _hiddenView.Refresh();
            if (_connectionsView != null) _connectionsView.Refresh();
            if (_listeningView != null) _listeningView.Refresh();
            if (_privacyView != null) _privacyView.Refresh();
            if (_startupView != null) _startupView.Refresh();
            if (_tasksView != null) _tasksView.Refresh();
            if (_dropsView != null) _dropsView.Refresh();
        }

        private bool MatchesSearch(params string[] fields)
        {
            if (string.IsNullOrEmpty(_searchQuery)) return true;
            string q = _searchQuery.ToLowerInvariant();
            foreach (string f in fields)
            {
                if (!string.IsNullOrEmpty(f) && f.ToLowerInvariant().Contains(q)) return true;
            }
            return false;
        }

        private bool FilterActivityEvent(object obj)
        {
            ActivityEventItem item = obj as ActivityEventItem;
            if (item == null) return false;

            if (!_settings.DeepHiddenMode && (item.Category == "HIDDEN" || item.Badge == "BG TELEMETRY"))
                return false;

            if (_selectedCategory != "ALL")
            {
                if (_selectedCategory == "HIDDEN" && item.Category != "HIDDEN") return false;
                if (_selectedCategory == "NETWORK" && item.Category != "NETWORK") return false;
                if (_selectedCategory == "DNS" && item.Category != "DNS") return false;
                if (_selectedCategory == "APP_WIN" && item.Category != "APP" && item.Category != "WINDOW") return false;
                if (_selectedCategory == "PRIV_SYS" && item.Category != "PRIVACY" && item.Category != "TASK" &&
                    item.Category != "SERVICE" && item.Category != "SECURITY" && item.Category != "SYSTEM")
                    return false;
            }

            return MatchesSearch(item.Title, item.Subtitle, item.Details, item.Badge, item.ProcessName, item.Publisher, item.CommandLine, item.ResolvedHost);
        }

        private bool FilterHiddenProcess(object obj)
        {
            HiddenProcessItem item = obj as HiddenProcessItem;
            if (item == null) return false;
            if (_onlyHiddenWithNet && item.ConnCount <= 0) return false;
            if (_hideMicrosoftHidden && item.IsMicrosoft) return false;
            return MatchesSearch(item.ProcessName, item.ServiceLabel, item.Role, item.Publisher, item.ParentName, item.CommandLine, item.Endpoints, item.Pid.ToString());
        }

        private bool FilterConnection(object obj)
        {
            NetworkConnectionItem item = obj as NetworkConnectionItem;
            if (item == null) return false;
            return MatchesSearch(item.DisplayProcess, item.EndpointLine, item.Publisher, item.ResolvedHost, item.PortMeaning, item.Pid.ToString());
        }

        private bool FilterListeningPort(object obj)
        {
            ListeningPortItem item = obj as ListeningPortItem;
            if (item == null) return false;
            return MatchesSearch(item.DisplayProcess, item.BindLine, item.Publisher, item.LocalPort.ToString(), item.Pid.ToString());
        }

        private bool FilterPrivacyItem(object obj)
        {
            PrivacyAccessItem item = obj as PrivacyAccessItem;
            if (item == null) return false;
            return MatchesSearch(item.Capability, item.AppName, item.AppPath, item.StatusBadge);
        }

        private bool FilterStartupTaskItem(object obj)
        {
            StartupTaskItem item = obj as StartupTaskItem;
            if (item == null) return false;
            return MatchesSearch(item.Kind, item.Name, item.Command, item.Publisher, item.Description);
        }

        private void PopulateEventInspector(ActivityEventItem item)
        {
            _selectedEvent = item;
            if (item == null)
            {
                _inspectorEmptyHint.Visibility = Visibility.Visible;
                _inspectorDetailScroll.Visibility = Visibility.Collapsed;
                return;
            }

            _inspectorEmptyHint.Visibility = Visibility.Collapsed;
            _inspectorDetailScroll.Visibility = Visibility.Visible;

            _inspBadgePill.Background = item.BadgeBackground;
            _inspBadgeText.Text = item.Badge;
            _inspBadgeText.Foreground = item.BadgeForeground;
            _inspTimeText.Text = item.FullTime;

            _inspTrustText.Text = string.IsNullOrEmpty(item.TrustText) ? "System Telemetry Event" : item.TrustText;
            _inspTrustText.Foreground = item.TrustBrush ?? ProcessActionHelper.MakeFrozenBrush("#A1A1AA");

            _inspTitleText.Text = item.Title;
            _inspSubtitleText.Text = item.Subtitle;
            _inspParentText.Text = string.IsNullOrEmpty(item.ParentChain) ? (item.Pid > 0 ? string.Format("PID {0}", item.Pid) : "N/A") : item.ParentChain;
            _inspPathText.Text = string.IsNullOrEmpty(item.ProcessPath) ? "Protected / Not Applicable" : item.ProcessPath;
            _inspEndpointText.Text = string.IsNullOrEmpty(item.EndpointDisplay) ? "No external network endpoint" : item.EndpointDisplay;
            _inspCmdBox.Text = string.IsNullOrEmpty(item.CommandLine) ? item.Details : item.CommandLine;

            bool hasPath = !string.IsNullOrEmpty(item.ProcessPath);
            bool canMute = !string.IsNullOrEmpty(item.ProcessName) &&
                           !item.ProcessName.StartsWith("AngelSystemLoggerPro", StringComparison.OrdinalIgnoreCase);
            bool canKill = item.Pid > 4 && canMute && item.Badge != "BG EXIT" && item.Badge != "APP CLOSED";

            if (_inspOpenLocBtn != null)
            {
                _inspOpenLocBtn.IsEnabled = hasPath;
                _inspOpenLocBtn.Opacity = hasPath ? 1.0 : 0.42;
            }
            if (_inspMuteBtn != null)
            {
                _inspMuteBtn.IsEnabled = canMute;
                _inspMuteBtn.Opacity = canMute ? 1.0 : 0.42;
            }
            if (_inspKillBtn != null)
            {
                _inspKillBtn.IsEnabled = canKill;
                _inspKillBtn.Opacity = canKill ? 1.0 : 0.42;
            }
        }

        private void CopySelectedEventReport()
        {
            if (_selectedEvent == null) return;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Angel Core Logger - Event Report");
            sb.AppendLine("Time: " + _selectedEvent.FullTime);
            sb.AppendLine("Badge: " + _selectedEvent.Badge);
            sb.AppendLine("Title: " + _selectedEvent.Title);
            sb.AppendLine("Explanation: " + _selectedEvent.Subtitle);
            if (!string.IsNullOrEmpty(_selectedEvent.Publisher))
                sb.AppendLine("Publisher: " + _selectedEvent.TrustText);
            if (!string.IsNullOrEmpty(_selectedEvent.ParentChain))
                sb.AppendLine("Process Tree: " + _selectedEvent.ParentChain);
            if (!string.IsNullOrEmpty(_selectedEvent.ProcessPath))
                sb.AppendLine("Executable: " + _selectedEvent.ProcessPath);
            if (!string.IsNullOrEmpty(_selectedEvent.EndpointDisplay))
                sb.AppendLine("Endpoint: " + _selectedEvent.EndpointDisplay);
            if (!string.IsNullOrEmpty(_selectedEvent.CommandLine))
                sb.AppendLine("Command Line: " + _selectedEvent.CommandLine);
            Clipboard.SetText(sb.ToString());
        }

        private void MuteProcess(string procName)
        {
            if (string.IsNullOrWhiteSpace(procName)) return;
            string clean = procName.Trim();
            if (clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                clean = clean.Substring(0, clean.Length - 4);
            if (clean.Equals("AngelSystemLoggerPro", StringComparison.OrdinalIgnoreCase)) return;

            bool exists = false;
            foreach (string existing in _settings.ExcludedProcesses)
            {
                if (existing.Equals(clean, StringComparison.OrdinalIgnoreCase)) { exists = true; break; }
            }
            if (!exists)
            {
                _settings.ExcludedProcesses.Add(clean);
                _settings.Save();
                UpdateSettingsButtonLabel();
                AddSyntheticEvent("SYSTEM", "PROCESS MUTED",
                    string.Format("Muted {0}.exe from future live stream alerts", clean),
                    "You can unmute it anytime under Settings -> Muted Processes",
                    "Saved to settings.json", "Info");
            }
        }

        private void TriggerDeepScan()
        {
            _engineStatusText.Text = "Scanning...";
            _engineStatusDot.Fill = ProcessActionHelper.MakeFrozenBrush("#D5A449");
            try
            {
                File.WriteAllText(AppSettings.GetScanTriggerPath(), DateTime.UtcNow.Ticks.ToString());
            }
            catch { }
        }

        private void ClearLiveFeed()
        {
            _allEvents.Clear();
            _cntAll = _cntHidden = _cntNetwork = _cntDns = _cntAppWin = _cntPrivacySys = 0;
            UpdateCountersUI();
            PopulateEventInspector(null);
        }

        private void SetPauseState(bool paused)
        {
            _isPaused = paused;
            _liveModeBtn.Background = paused ? Brushes.Transparent : ProcessActionHelper.MakeFrozenBrush("#2E2E37");
            _liveModeBtn.Foreground = paused ? ProcessActionHelper.MakeFrozenBrush("#71717A") : ProcessActionHelper.MakeFrozenBrush("#F4F4F5");
            _pauseModeBtn.Background = paused ? ProcessActionHelper.MakeFrozenBrush("#2E2E37") : Brushes.Transparent;
            _pauseModeBtn.Foreground = paused ? ProcessActionHelper.MakeFrozenBrush("#F4F4F5") : ProcessActionHelper.MakeFrozenBrush("#71717A");

            _engineStatusText.Text = paused ? "Paused" : "Live";
            _engineStatusDot.Fill = ProcessActionHelper.MakeFrozenBrush(paused ? "#FBBF24" : "#34D399");
            if (_trayPauseMenu != null) _trayPauseMenu.Text = paused ? "Resume Monitoring" : "Pause Monitoring";
        }

        private void UpdateSettingsButtonLabel()
        {
            int mutedCount = Math.Max(0, _settings.ExcludedProcesses.Count - 2);
            _settingsBtn.Content = string.Format("⚙ Settings (Muted: {0})", mutedCount);
        }

        private void OpenSettingsDialog()
        {
            SettingsDialog dlg = new SettingsDialog(this, _settings, _exePath,
                () =>
                {
                    UpdateSettingsButtonLabel();
                    if (_eventsView != null) _eventsView.Refresh();
                },
                () => ExportFullReport(),
                () =>
                {
                    if (ProcessActionHelper.RestartAsAdministrator(_exePath))
                    {
                        _forceExit = true;
                        this.Close();
                    }
                });
            dlg.ShowDialog();
        }

        private void ExportFullReport()
        {
            try
            {
                Microsoft.Win32.SaveFileDialog sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Save System Snapshot As",
                    Filter = "Text Report (*.txt)|*.txt|All Files (*.*)|*.*",
                    DefaultExt = ".txt",
                    FileName = string.Format("AngelCoreSnapshot_{0:yyyy-MM-dd_HHmmss}.txt", DateTime.Now)
                };
                if (sfd.ShowDialog(this) != true) return;
                string outFile = sfd.FileName;
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("========================================================================");
                sb.AppendLine("  ANGEL CORE LOGGER v3.0 - FULL LOCAL SYSTEM SNAPSHOT & EVENT REPORT");
                sb.AppendLine("  Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("========================================================================");
                sb.AppendLine();
                sb.AppendLine(string.Format("Summary: {0} Hidden BG Processes | {1} External Connections | {2} Listening Ports | {3} Startup Items | {4} Active Tasks",
                    _totalHiddenProcessCount, _connections.Count, _listeningPorts.Count, _startupItems.Count, _runningTasks.Count));
                sb.AppendLine();
                sb.AppendLine("--- RECENT ACTIVITY STREAM ---");
                foreach (ActivityEventItem ev in _allEvents)
                {
                    sb.AppendLine(string.Format("[{0}] [{1}] {2} ({3})", ev.FullTime, ev.Badge, ev.Title, ev.TrustText));
                    sb.AppendLine("    Why: " + ev.Subtitle);
                    if (!string.IsNullOrEmpty(ev.CommandLine)) sb.AppendLine("    Cmd: " + IpPrivacyHelper.ApplyIpMask(ev.CommandLine, _settings.MaskIp));
                }
                sb.AppendLine();
                sb.AppendLine("--- EXTERNAL NETWORK CONNECTIONS ---");
                foreach (NetworkConnectionItem nc in _connections)
                {
                    sb.AppendLine(string.Format("[{0}] {1} | {2}", nc.VisibilityTag, nc.DisplayProcess, nc.EndpointLine));
                }
                sb.AppendLine();
                sb.AppendLine("--- MICROPHONE / WEBCAM / LOCATION ACCESS ---");
                foreach (PrivacyAccessItem pi in _privacyItems)
                {
                    sb.AppendLine(string.Format("[{0}] {1} -> {2}", pi.StatusBadge, pi.AppName, pi.DetailsLine));
                }
                File.WriteAllText(outFile, sb.ToString(), Encoding.UTF8);
                ProcessActionHelper.OpenFileLocation(outFile);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not export report:\n" + ex.Message, "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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

        private void UpdateVitalsDisplay()
        {
            int cpu = SystemVitalsHelper.GetCpuLoadPercent();
            int ram = SystemVitalsHelper.GetMemoryLoadPercent();

            string micState = "Idle";
            string camState = "Idle";
            string locState = "Idle";
            foreach (PrivacyAccessItem pi in _privacyItems)
            {
                if (pi.IsActive)
                {
                    if (pi.Capability == "Microphone") micState = "ACTIVE (" + pi.AppName + ")";
                    else if (pi.Capability == "Webcam") camState = "ACTIVE (" + pi.AppName + ")";
                    else if (pi.Capability == "Location") locState = "ACTIVE (" + pi.AppName + ")";
                }
            }

            _vitalsText.Text = string.Format("CPU {0}%  •  RAM {1}%  •  Mic: {2}  •  Cam: {3}", cpu, ram, micState, camState);
            bool anySensorActive = (micState != "Idle" || camState != "Idle");
            _vitalsText.Foreground = ProcessActionHelper.MakeFrozenBrush(anySensorActive ? "#F87171" : "#A1A1AA");

            _micBannerText.Text = "Mic: " + micState;
            _micBannerText.Foreground = ProcessActionHelper.MakeFrozenBrush(micState == "Idle" ? "#34D399" : "#F87171");
            _camBannerText.Text = "Cam: " + camState;
            _camBannerText.Foreground = ProcessActionHelper.MakeFrozenBrush(camState == "Idle" ? "#34D399" : "#F87171");
            _locBannerText.Text = "Location: " + locState;
            _locBannerText.Foreground = ProcessActionHelper.MakeFrozenBrush(locState == "Idle" ? "#A1A1AA" : "#FBBF24");
        }

        private string EnsurePowerShellScriptPath()
        {
            string localPath = Path.Combine(_exeDir, "Start-LocalLogger.ps1");
            if (File.Exists(localPath)) return localPath;

            string extractedPath = Path.Combine(AppSettings.GetAppDataDir(), "Start-LocalLogger.ps1");
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
            string line = e.Data;

            if (line.StartsWith("ANGEL_EVENT|"))
            {
                Dictionary<string, object> dict = JsonHelper.DeserializeDict(line.Substring("ANGEL_EVENT|".Length));
                if (dict != null)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_engineStatusText.Text == "Starting..." || _engineStatusText.Text == "Starting Engine...")
                            _engineStatusText.Text = "Live";
                        if (_isPaused) return;
                        IngestEventDict(dict);
                    }));
                }
            }
            else if (line.StartsWith("ANGEL_SNAPSHOT|"))
            {
                Dictionary<string, object> snap = JsonHelper.DeserializeDict(line.Substring("ANGEL_SNAPSHOT|".Length));
                if (snap != null)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _engineStatusText.Text = _isPaused ? "Paused" : "Live";
                        _engineStatusDot.Fill = ProcessActionHelper.MakeFrozenBrush(_isPaused ? "#FBBF24" : "#34D399");
                        IngestSnapshotDict(snap);
                    }));
                }
            }
        }

        private void IngestEventDict(Dictionary<string, object> d)
        {
            string category = JsonHelper.GetStr(d, "category", "SYSTEM");
            string badge = JsonHelper.GetStr(d, "badge", "EVENT");
            string severity = JsonHelper.GetStr(d, "severity", "Info");
            string remoteIp = JsonHelper.GetStr(d, "remoteIp");
            string resolvedHost = JsonHelper.GetStr(d, "resolvedHost");

            if (string.IsNullOrEmpty(resolvedHost) && !string.IsNullOrEmpty(remoteIp))
            {
                resolvedHost = IpPrivacyHelper.GetCachedOrOrg(remoteIp);
            }

            Brush badgeBg, badgeFg, dotBrush;
            GetBadgeColors(category, severity, out badgeBg, out badgeFg, out dotBrush);
            bool isSigned = JsonHelper.GetBool(d, "isSigned", true);

            ActivityEventItem item = new ActivityEventItem
            {
                Time = JsonHelper.GetStr(d, "time", DateTime.Now.ToString("HH:mm:ss")),
                FullTime = JsonHelper.GetStr(d, "fullTime", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
                Category = category,
                Badge = badge,
                RawTitle = JsonHelper.GetStr(d, "title"),
                RawSubtitle = JsonHelper.GetStr(d, "subtitle"),
                RawDetails = JsonHelper.GetStr(d, "details"),
                Severity = severity,
                Pid = JsonHelper.GetInt(d, "pid"),
                ProcessName = JsonHelper.GetStr(d, "processName"),
                ProcessPath = JsonHelper.GetStr(d, "processPath"),
                ParentChain = JsonHelper.GetStr(d, "parentChain"),
                CommandLine = JsonHelper.GetStr(d, "commandLine"),
                Publisher = JsonHelper.GetStr(d, "publisher"),
                IsSigned = isSigned,
                RemoteIp = remoteIp,
                RemotePort = JsonHelper.GetInt(d, "remotePort"),
                ResolvedHost = resolvedHost,
                MaskIp = _settings.MaskIp,
                BadgeBackground = badgeBg,
                BadgeForeground = badgeFg,
                DotBrush = dotBrush,
                TrustBrush = ProcessActionHelper.MakeFrozenBrush(isSigned ? "#34D399" : "#F87171")
            };

            if (!string.IsNullOrEmpty(remoteIp))
            {
                IpPrivacyHelper.ResolveHostAsync(remoteIp, host =>
                {
                    Dispatcher.BeginInvoke(new Action(() => { item.ResolvedHost = host; }));
                });
            }

            InsertActivityItem(item);
        }

        private void AddSyntheticEvent(string category, string badge, string title, string subtitle, string details, string severity)
        {
            Brush badgeBg, badgeFg, dotBrush;
            GetBadgeColors(category, severity, out badgeBg, out badgeFg, out dotBrush);
            ActivityEventItem item = new ActivityEventItem
            {
                Time = DateTime.Now.ToString("HH:mm:ss"),
                FullTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Category = category,
                Badge = badge,
                RawTitle = title,
                RawSubtitle = subtitle,
                RawDetails = details,
                Severity = severity,
                Publisher = "Angel Core Logger",
                IsSigned = true,
                MaskIp = _settings.MaskIp,
                BadgeBackground = badgeBg,
                BadgeForeground = badgeFg,
                DotBrush = dotBrush,
                TrustBrush = ProcessActionHelper.MakeFrozenBrush("#34D399")
            };
            InsertActivityItem(item);
        }

        private void InsertActivityItem(ActivityEventItem item)
        {
            _allEvents.Insert(0, item);
            if (_allEvents.Count > 800)
            {
                _allEvents.RemoveAt(_allEvents.Count - 1);
            }

            _cntAll++;
            switch (item.Category)
            {
                case "HIDDEN": _cntHidden++; break;
                case "NETWORK": _cntNetwork++; break;
                case "DNS": _cntDns++; break;
                case "APP":
                case "WINDOW": _cntAppWin++; break;
                default: _cntPrivacySys++; break;
            }
            UpdateCountersUI();
            if (_selectedEvent == null && _eventsListBox.Items.Count > 0)
            {
                _eventsListBox.SelectedIndex = 0;
            }
        }

        private void UpdateCountersUI()
        {
            _statAllText.Text = _cntAll.ToString();
            _statHiddenText.Text = _cntHidden.ToString();
            _statNetText.Text = _cntNetwork.ToString();
            _statDnsText.Text = _cntDns.ToString();
            _statAppText.Text = _cntAppWin.ToString();
            _statPrivText.Text = _cntPrivacySys.ToString();
            _emptyStatePanel.Visibility = (_allEvents.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
            _navActivityBtn.Content = string.Format("Live Feed ({0})", _cntAll);
        }

        private void IngestSnapshotDict(Dictionary<string, object> snap)
        {
            _totalHiddenProcessCount = JsonHelper.GetInt(snap, "hiddenCount");

            // 1. Hidden Processes
            _hiddenProcesses.Clear();
            foreach (object obj in JsonHelper.GetArray(snap, "hiddenProcesses"))
            {
                Dictionary<string, object> row = obj as Dictionary<string, object>;
                if (row == null) continue;
                bool signed = JsonHelper.GetBool(row, "isSigned", true);
                bool isMs = JsonHelper.GetBool(row, "isMicrosoft", false);
                _hiddenProcesses.Add(new HiddenProcessItem
                {
                    Pid = JsonHelper.GetInt(row, "pid"),
                    ProcessName = JsonHelper.GetStr(row, "processName"),
                    ParentName = JsonHelper.GetStr(row, "parentName"),
                    ServiceLabel = JsonHelper.GetStr(row, "serviceLabel"),
                    Role = JsonHelper.GetStr(row, "role"),
                    Publisher = JsonHelper.GetStr(row, "publisher"),
                    IsSigned = signed,
                    IsMicrosoft = isMs,
                    MemoryMb = JsonHelper.GetDouble(row, "memoryMb"),
                    ConnCount = JsonHelper.GetInt(row, "connCount"),
                    RawEndpoints = JsonHelper.GetStr(row, "endpoints"),
                    ProcessPath = JsonHelper.GetStr(row, "processPath"),
                    CommandLine = JsonHelper.GetStr(row, "commandLine"),
                    MaskIp = _settings.MaskIp,
                    TrustBg = ProcessActionHelper.MakeFrozenBrush(signed ? (isMs ? "#162E28" : "#1B2A3E") : "#381E22"),
                    TrustFg = ProcessActionHelper.MakeFrozenBrush(signed ? (isMs ? "#34D399" : "#60A5FA") : "#F87171")
                });
            }
            _hiddenSummaryText.Text = string.Format("{0} total windowless background processes running (showing top {1} prioritized by network/trust/RAM)",
                _totalHiddenProcessCount, _hiddenProcesses.Count);
            _navHiddenBtn.Content = string.Format("Hidden BG ({0})", _totalHiddenProcessCount);

            // 2. Network Connections
            _connections.Clear();
            foreach (object obj in JsonHelper.GetArray(snap, "connections"))
            {
                Dictionary<string, object> row = obj as Dictionary<string, object>;
                if (row == null) continue;
                bool isHidden = JsonHelper.GetBool(row, "isHidden");
                string remoteIp = JsonHelper.GetStr(row, "remoteIp");
                string resolved = JsonHelper.GetStr(row, "resolvedHost");
                if (string.IsNullOrEmpty(resolved) && !string.IsNullOrEmpty(remoteIp))
                    resolved = IpPrivacyHelper.GetCachedOrOrg(remoteIp);

                NetworkConnectionItem nc = new NetworkConnectionItem
                {
                    Pid = JsonHelper.GetInt(row, "pid"),
                    ProcessName = JsonHelper.GetStr(row, "processName"),
                    ServiceLabel = JsonHelper.GetStr(row, "serviceLabel"),
                    RawLocalEndpoint = JsonHelper.GetStr(row, "localEndpoint"),
                    RawRemoteEndpoint = JsonHelper.GetStr(row, "remoteEndpoint"),
                    RemoteIp = remoteIp,
                    RemotePort = JsonHelper.GetInt(row, "remotePort"),
                    State = JsonHelper.GetStr(row, "state"),
                    PortMeaning = JsonHelper.GetStr(row, "portMeaning"),
                    IsHidden = isHidden,
                    Publisher = JsonHelper.GetStr(row, "publisher"),
                    IsSigned = JsonHelper.GetBool(row, "isSigned", true),
                    ProcessPath = JsonHelper.GetStr(row, "processPath"),
                    ResolvedHost = resolved,
                    MaskIp = _settings.MaskIp,
                    VisibilityBg = ProcessActionHelper.MakeFrozenBrush(isHidden ? "#332818" : "#1B2A3E"),
                    VisibilityFg = ProcessActionHelper.MakeFrozenBrush(isHidden ? "#FBBF24" : "#60A5FA")
                };
                _connections.Add(nc);

                if (!string.IsNullOrEmpty(remoteIp))
                {
                    NetworkConnectionItem captured = nc;
                    IpPrivacyHelper.ResolveHostAsync(remoteIp, host =>
                    {
                        Dispatcher.BeginInvoke(new Action(() => { captured.ResolvedHost = host; }));
                    });
                }
            }
            _subConnsBtn.Content = string.Format("Outbound ({0})", _connections.Count);

            // 3. Listening Ports
            _listeningPorts.Clear();
            foreach (object obj in JsonHelper.GetArray(snap, "listeningPorts"))
            {
                Dictionary<string, object> row = obj as Dictionary<string, object>;
                if (row == null) continue;
                _listeningPorts.Add(new ListeningPortItem
                {
                    Pid = JsonHelper.GetInt(row, "pid"),
                    ProcessName = JsonHelper.GetStr(row, "processName"),
                    ServiceLabel = JsonHelper.GetStr(row, "serviceLabel"),
                    RawLocalAddress = JsonHelper.GetStr(row, "localAddress"),
                    LocalPort = JsonHelper.GetInt(row, "localPort"),
                    PortMeaning = JsonHelper.GetStr(row, "portMeaning"),
                    IsHidden = JsonHelper.GetBool(row, "isHidden"),
                    Publisher = JsonHelper.GetStr(row, "publisher"),
                    IsSigned = JsonHelper.GetBool(row, "isSigned", true),
                    ProcessPath = JsonHelper.GetStr(row, "processPath"),
                    MaskIp = _settings.MaskIp
                });
            }
            _subListenBtn.Content = string.Format("Listening Ports ({0})", _listeningPorts.Count);

            // 4. Privacy Access Items (Mic, Webcam, Location)
            _privacyItems.Clear();
            foreach (object obj in JsonHelper.GetArray(snap, "privacyItems"))
            {
                Dictionary<string, object> row = obj as Dictionary<string, object>;
                if (row == null) continue;
                bool active = JsonHelper.GetBool(row, "isActive");
                string cap = JsonHelper.GetStr(row, "capability");
                string bgHex = active ? "#3D1C22" : (cap == "Microphone" ? "#1B2A3E" : (cap == "Webcam" ? "#2E1F38" : "#222834"));
                string fgHex = active ? "#F87171" : (cap == "Microphone" ? "#60A5FA" : (cap == "Webcam" ? "#C084FC" : "#A1A1AA"));

                _privacyItems.Add(new PrivacyAccessItem
                {
                    Capability = cap,
                    AppName = JsonHelper.GetStr(row, "appName"),
                    AppPath = JsonHelper.GetStr(row, "appPath"),
                    IsActive = active,
                    StartTime = JsonHelper.GetStr(row, "startTime"),
                    StopTime = JsonHelper.GetStr(row, "stopTime"),
                    Duration = JsonHelper.GetStr(row, "duration"),
                    BadgeBg = ProcessActionHelper.MakeFrozenBrush(bgHex),
                    BadgeFg = ProcessActionHelper.MakeFrozenBrush(fgHex)
                });
            }
            _subPrivacyBtn.Content = string.Format("Mic / Cam / Loc ({0})", _privacyItems.Count);
            _navNetworkBtn.Content = string.Format("Net & Privacy ({0})", _connections.Count);
            UpdateVitalsDisplay();

            // 5. Startup Items, Running Tasks, File Drops
            _startupItems.Clear();
            foreach (object obj in JsonHelper.GetArray(snap, "startupItems"))
            {
                Dictionary<string, object> row = obj as Dictionary<string, object>;
                if (row == null) continue;
                bool signed = JsonHelper.GetBool(row, "isSigned", true);
                _startupItems.Add(new StartupTaskItem
                {
                    Kind = JsonHelper.GetStr(row, "kind"),
                    Name = JsonHelper.GetStr(row, "name"),
                    Command = JsonHelper.GetStr(row, "command"),
                    Publisher = JsonHelper.GetStr(row, "publisher"),
                    IsSigned = signed,
                    State = JsonHelper.GetStr(row, "state"),
                    Description = JsonHelper.GetStr(row, "description"),
                    KindBg = ProcessActionHelper.MakeFrozenBrush(signed ? "#162E28" : "#381E22"),
                    KindFg = ProcessActionHelper.MakeFrozenBrush(signed ? "#34D399" : "#F87171")
                });
            }
            _subStartupBtn.Content = string.Format("Startup Apps ({0})", _startupItems.Count);

            _runningTasks.Clear();
            foreach (object obj in JsonHelper.GetArray(snap, "runningTasks"))
            {
                Dictionary<string, object> row = obj as Dictionary<string, object>;
                if (row == null) continue;
                _runningTasks.Add(new StartupTaskItem
                {
                    Kind = JsonHelper.GetStr(row, "kind"),
                    Name = JsonHelper.GetStr(row, "name"),
                    Command = JsonHelper.GetStr(row, "command"),
                    Publisher = JsonHelper.GetStr(row, "publisher"),
                    IsSigned = true,
                    State = JsonHelper.GetStr(row, "state"),
                    Description = JsonHelper.GetStr(row, "description")
                });
            }
            _subTasksBtn.Content = string.Format("Running Tasks ({0})", _runningTasks.Count);

            _fileDrops.Clear();
            foreach (object obj in JsonHelper.GetArray(snap, "fileDrops"))
            {
                Dictionary<string, object> row = obj as Dictionary<string, object>;
                if (row == null) continue;
                _fileDrops.Add(new StartupTaskItem
                {
                    Kind = "File Drop",
                    Name = JsonHelper.GetStr(row, "name"),
                    Command = JsonHelper.GetStr(row, "path"),
                    Publisher = "Detected at " + JsonHelper.GetStr(row, "time"),
                    IsSigned = false,
                    State = string.Format("{0:0.0} KB", JsonHelper.GetDouble(row, "sizeKb")),
                    Description = "New executable or script file created in Temp / Downloads during session"
                });
            }
            _subDropsBtn.Content = string.Format("Temp / Drops ({0})", _fileDrops.Count);
            _navStartupBtn.Content = string.Format("Startup & Tasks ({0})", _startupItems.Count + _runningTasks.Count);
        }

        private static void GetBadgeColors(string category, string severity, out Brush bg, out Brush fg, out Brush dot)
        {
            string bgHex = "#27272E", fgHex = "#E4E4E7", dotHex = "#A1A1AA";

            if (category == "PRIVACY" || severity == "Warning")
            {
                bgHex = "#3B1E24"; fgHex = "#F87171"; dotHex = "#EF4444";
            }
            else if (category == "HIDDEN" || severity == "Hidden")
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
            else if (category == "SECURITY" || severity == "Success")
            {
                bgHex = "#162E28"; fgHex = "#34D399"; dotHex = "#10B981";
            }

            bg = ProcessActionHelper.MakeFrozenBrush(bgHex);
            fg = ProcessActionHelper.MakeFrozenBrush(fgHex);
            dot = ProcessActionHelper.MakeFrozenBrush(dotHex);
        }
    }
}
