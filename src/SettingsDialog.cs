using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace AngelSystemLoggerPro
{
    public class SettingsDialog : Window
    {
        private readonly AppSettings _settings;
        private readonly string _exePath;
        private readonly Action _onSettingsChanged;
        private readonly Action _onExportReport;
        private readonly Action _onRestartAdmin;

        private CheckBox _autoSaveCheck;
        private CheckBox _startMinCheck;
        private CheckBox _closeToTrayCheck;
        private ListBox _excludedListBox;
        private TextBox _addProcBox;

        public SettingsDialog(Window owner, AppSettings settings, string exePath,
            Action onSettingsChanged, Action onExportReport, Action onRestartAdmin)
        {
            this.Owner = owner;
            _settings = settings;
            _exePath = exePath;
            _onSettingsChanged = onSettingsChanged;
            _onExportReport = onExportReport;
            _onRestartAdmin = onRestartAdmin;

            this.Title = "Angel Core Logger Settings";
            this.Width = 520;
            this.Height = 560;
            this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            this.WindowStyle = WindowStyle.None;
            this.AllowsTransparency = true;
            this.Background = Brushes.Transparent;
            this.FontFamily = new FontFamily("Segoe UI");

            BuildLayout();
            PopulateFields();
        }

        private void BuildLayout()
        {
            string xaml = @"
<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        CornerRadius='10' Background='#1A1A1E' BorderBrush='#383844' BorderThickness='1.5' ClipToBounds='True'>
    <Border.Resources>
        <Style x:Key='DlgDarkBtn' TargetType='Button'>
            <Setter Property='Background' Value='#26262D'/>
            <Setter Property='Foreground' Value='#E4E4E7'/>
            <Setter Property='BorderBrush' Value='#383844'/>
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
                                <Setter TargetName='Bd' Property='Background' Value='#303039'/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
        <Style x:Key='DlgGoldBtn' TargetType='Button'>
            <Setter Property='Background' Value='#D5A449'/>
            <Setter Property='Foreground' Value='#161618'/>
            <Setter Property='FontWeight' Value='SemiBold'/>
            <Setter Property='FontSize' Value='12.5'/>
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
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </Border.Resources>

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height='42'/>
            <RowDefinition Height='*'/>
            <RowDefinition Height='54'/>
        </Grid.RowDefinitions>

        <Border x:Name='DlgHeader' Grid.Row='0' Background='#222228' BorderBrush='#2E2E36' BorderThickness='0,0,0,1' Padding='16,0'>
            <Grid>
                <TextBlock Text='Settings &amp; Muted Processes' Foreground='#F4F4F5' FontSize='14' FontWeight='SemiBold' VerticalAlignment='Center'/>
                <Button x:Name='DlgCloseX' Content='✕' Width='32' Height='28' HorizontalAlignment='Right' VerticalAlignment='Center'
                        Background='Transparent' Foreground='#A1A1AA' BorderThickness='0' Cursor='Hand' FontSize='13'/>
            </Grid>
        </Border>

        <ScrollViewer Grid.Row='1' VerticalScrollBarVisibility='Auto' Padding='18,14'>
            <StackPanel>
                <TextBlock Text='BEHAVIOR &amp; TRAY' Foreground='#D5A449' FontSize='10.5' FontWeight='Bold' Margin='0,0,0,8'/>
                <Border Background='#202026' BorderBrush='#2E2E36' BorderThickness='1' CornerRadius='8' Padding='14,12' Margin='0,0,0,14'>
                    <StackPanel>
                        <CheckBox x:Name='AutoSaveCheck' Content='Save activity logs locally to Documents\LocalActivityLogs'
                                  Foreground='#E4E4E7' FontSize='12.5' Margin='0,0,0,10'/>
                        <CheckBox x:Name='StartMinCheck' Content='Start minimized to System Tray when Windows boots'
                                  Foreground='#E4E4E7' FontSize='12.5' Margin='0,0,0,10'/>
                        <CheckBox x:Name='CloseToTrayCheck' Content='Keep running in System Tray when clicking Close (✕)'
                                  Foreground='#E4E4E7' FontSize='12.5'/>
                    </StackPanel>
                </Border>

                <TextBlock Text='PRIVILEGES &amp; REPORTS' Foreground='#D5A449' FontSize='10.5' FontWeight='Bold' Margin='0,0,0,8'/>
                <Border Background='#202026' BorderBrush='#2E2E36' BorderThickness='1' CornerRadius='8' Padding='14,12' Margin='0,0,0,14'>
                    <StackPanel>
                        <TextBlock x:Name='PrivilegeDesc' Text='Running in Standard Mode. Elevate as Administrator to reveal protected system command lines.'
                                   Foreground='#A1A1AA' FontSize='12' TextWrapping='Wrap' Margin='0,0,0,10'/>
                        <StackPanel Orientation='Horizontal'>
                            <Button x:Name='ElevateBtn' Style='{StaticResource DlgDarkBtn}' Content='Restart as Administrator' Margin='0,0,10,0'/>
                            <Button x:Name='ExportBtn' Style='{StaticResource DlgDarkBtn}' Content='Export Full Report (.txt)'/>
                        </StackPanel>
                    </StackPanel>
                </Border>

                <TextBlock Text='MUTED / EXCLUDED PROCESSES' Foreground='#D5A449' FontSize='10.5' FontWeight='Bold' Margin='0,0,0,8'/>
                <Border Background='#202026' BorderBrush='#2E2E36' BorderThickness='1' CornerRadius='8' Padding='14,12'>
                    <StackPanel>
                        <TextBlock Text='Processes listed here are ignored by the live stream so trusted noisy helpers stay quiet:'
                                   Foreground='#A1A1AA' FontSize='12' TextWrapping='Wrap' Margin='0,0,0,8'/>
                        <Grid Margin='0,0,0,8'>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width='*'/>
                                <ColumnDefinition Width='Auto'/>
                            </Grid.ColumnDefinitions>
                            <Border Background='#151518' BorderBrush='#303038' BorderThickness='1' CornerRadius='6' Height='32'>
                                <TextBox x:Name='AddProcBox' Background='Transparent' Foreground='#F4F4F5' CaretBrush='#D5A449'
                                         BorderThickness='0' Padding='10,0' VerticalContentAlignment='Center' FontSize='12.5'
                                         ToolTip='Enter process name (e.g. conhost)'/>
                            </Border>
                            <Button x:Name='AddProcBtn' Grid.Column='1' Style='{StaticResource DlgDarkBtn}' Content='+ Add' Margin='8,0,0,0' Padding='12,5'/>
                        </Grid>
                        <ListBox x:Name='ExcludedListBox' Height='130' Background='#151518' Foreground='#E4E4E7' BorderBrush='#2E2E36' BorderThickness='1'
                                 FontFamily='Consolas' FontSize='12'/>
                        <Button x:Name='RemoveProcBtn' Style='{StaticResource DlgDarkBtn}' Content='Unmute Selected Process' HorizontalAlignment='Left' Margin='0,8,0,0'/>
                    </StackPanel>
                </Border>
            </StackPanel>
        </ScrollViewer>

        <Border Grid.Row='2' Background='#222228' BorderBrush='#2E2E36' BorderThickness='0,1,0,0' Padding='18,0'>
            <StackPanel Orientation='Horizontal' HorizontalAlignment='Right' VerticalAlignment='Center'>
                <Button x:Name='CancelBtn' Style='{StaticResource DlgDarkBtn}' Content='Cancel' Margin='0,0,10,0'/>
                <Button x:Name='SaveBtn' Style='{StaticResource DlgGoldBtn}' Content='Save Settings'/>
            </StackPanel>
        </Border>
    </Grid>
</Border>";

            Border root = (Border)XamlReader.Parse(xaml);
            this.Content = root;

            Border hdr = (Border)root.FindName("DlgHeader");
            hdr.MouseLeftButtonDown += (s, e) => { try { this.DragMove(); } catch { } };

            Button closeX = (Button)root.FindName("DlgCloseX");
            closeX.Click += (s, e) => this.Close();

            _autoSaveCheck = (CheckBox)root.FindName("AutoSaveCheck");
            _startMinCheck = (CheckBox)root.FindName("StartMinCheck");
            _closeToTrayCheck = (CheckBox)root.FindName("CloseToTrayCheck");

            TextBlock privDesc = (TextBlock)root.FindName("PrivilegeDesc");
            Button elevateBtn = (Button)root.FindName("ElevateBtn");
            Button exportBtn = (Button)root.FindName("ExportBtn");

            if (ProcessActionHelper.IsAdministrator())
            {
                privDesc.Text = "Already running with Administrator privileges. Full protected command lines are visible.";
                elevateBtn.IsEnabled = false;
                elevateBtn.Opacity = 0.5;
            }

            elevateBtn.Click += (s, e) =>
            {
                if (_onRestartAdmin != null) _onRestartAdmin();
            };

            exportBtn.Click += (s, e) =>
            {
                if (_onExportReport != null) _onExportReport();
            };

            _excludedListBox = (ListBox)root.FindName("ExcludedListBox");
            _addProcBox = (TextBox)root.FindName("AddProcBox");
            Button addBtn = (Button)root.FindName("AddProcBtn");
            Button remBtn = (Button)root.FindName("RemoveProcBtn");

            addBtn.Click += (s, e) =>
            {
                string val = (_addProcBox.Text ?? "").Trim();
                if (val.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    val = val.Substring(0, val.Length - 4);
                if (!string.IsNullOrEmpty(val) && !_excludedListBox.Items.Contains(val))
                {
                    _excludedListBox.Items.Add(val);
                    _addProcBox.Text = "";
                }
            };

            remBtn.Click += (s, e) =>
            {
                if (_excludedListBox.SelectedItem != null)
                {
                    string sel = _excludedListBox.SelectedItem.ToString();
                    if (sel.Equals("AngelSystemLoggerPro", StringComparison.OrdinalIgnoreCase) ||
                        sel.Equals("Start-LocalLogger", StringComparison.OrdinalIgnoreCase))
                    {
                        MessageBox.Show(this, "Angel Core Logger keeps itself excluded to avoid feedback loops.", "Required Exclusion", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    _excludedListBox.Items.Remove(sel);
                }
            };

            Button cancelBtn = (Button)root.FindName("CancelBtn");
            Button saveBtn = (Button)root.FindName("SaveBtn");
            cancelBtn.Click += (s, e) => this.Close();
            saveBtn.Click += (s, e) => SaveAndClose();
        }

        private void PopulateFields()
        {
            _autoSaveCheck.IsChecked = _settings.AutoSaveLogs;
            _startMinCheck.IsChecked = _settings.StartMinimizedToTray;
            _closeToTrayCheck.IsChecked = _settings.MinimizeToTrayOnClose;

            _excludedListBox.Items.Clear();
            foreach (string proc in _settings.ExcludedProcesses)
            {
                _excludedListBox.Items.Add(proc);
            }
        }

        private void SaveAndClose()
        {
            _settings.AutoSaveLogs = _autoSaveCheck.IsChecked == true;
            _settings.StartMinimizedToTray = _startMinCheck.IsChecked == true;
            _settings.MinimizeToTrayOnClose = _closeToTrayCheck.IsChecked == true;

            List<string> updated = new List<string>();
            foreach (object item in _excludedListBox.Items)
            {
                updated.Add(item.ToString());
            }
            _settings.ExcludedProcesses = updated;
            _settings.Save();

            if (StartupManager.IsRegisteredInStartup())
            {
                StartupManager.SetStartupRegistry(_exePath, true, _settings.StartMinimizedToTray);
            }

            if (_onSettingsChanged != null) _onSettingsChanged();
            this.Close();
        }
    }
}
