using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using NInferManager.Contracts;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;

namespace NInferManager.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly BackendClient _backend=new();
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromSeconds(2)};
    private readonly DispatcherTimer _settingsSaveTimer=new(){Interval=TimeSpan.FromMilliseconds(700)};
    private readonly Dictionary<InfoBar,CancellationTokenSource> _infoBarDismissals=[];
    private readonly HashSet<InfoBar> _hoveredInfoBars=[];
    private readonly HashSet<InfoBar> _expiredInfoBars=[];
    private TrayIconController? _tray;
    private readonly IntPtr _windowHandle;
    private ManagerSettings? _settings;
    private ModelInfo? _activeModel;
    private ModelInfo? _profileModel;
    private bool _realExit;
    private bool _hiddenToTray;
    private bool _portConflictShown;
    private UpdateInfo? _availableUpdate;
    private string _latestLogText=string.Empty;
    private int _visibleLogTicks;
    private EngineState _engineState=EngineState.Unloaded;
    private bool _hasActiveModel;
    private bool _sidebarExpanded=true;
    private string _activePage="dashboard";
    private readonly Dictionary<PropertyInfo,Control> _advancedAppControls=[];
    private readonly Dictionary<PropertyInfo,Control> _advancedProfileControls=[];
    public ObservableCollection<ModelItemView> Models { get; }=[];
    public ObservableCollection<ModelItemView> ProfileModels { get; }=[];
    public ObservableCollection<ModelItemView> RecommendedModels { get; }=[];
    public ObservableCollection<RequestItemView> Requests { get; }=[];
    private readonly List<ModelItemView> _catalogModels=[];
    private readonly List<EnginePackageInfo> _enginePackages=[];
    private string _lastEnginePackageStage="Idle";
    private bool _loadingSettings;
    private string _lastModelDownloadStage="Idle";

    public MainWindow()
    {
        InitializeComponent();
        RegisterTransientInfoBars();
        Title="NInferEZ Manager";
        AboutVersionText.Text=$"{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)??"unknown"}";
        _=LoadPublisherBrandingAsync();
        _windowHandle=WinRT.Interop.WindowNative.GetWindowHandle(this);
        var iconPath=Path.Combine(AppContext.BaseDirectory,"Assets","NInferEZ.ico");
        if(File.Exists(iconPath))AppWindow.SetIcon(iconPath);
        AppWindow.Resize(new SizeInt32(1420,900));
        AppWindow.Closing+=OnWindowClosing;
        Closed+=(_,_)=>{_timer.Stop();_settingsSaveTimer.Stop();foreach(var dismissal in _infoBarDismissals.Values){dismissal.Cancel();dismissal.Dispose();}_infoBarDismissals.Clear();_tray?.Dispose();_backend.Dispose();};
        _timer.Tick+=async (_,_)=>
        {
            await RefreshStatusAsync();
            if(RequestsPage.Visibility==Visibility.Visible)await RefreshRequestsAsync();
            if(LogsPage.Visibility==Visibility.Visible&&++_visibleLogTicks%2==0)await RefreshLogTextAsync();
            if(EnginePage.Visibility==Visibility.Visible)await RefreshEnginePackageProgressAsync();
            if(ModelsPage.Visibility==Visibility.Visible)await RefreshModelDownloadAsync();
        };
        _settingsSaveTimer.Tick+=async (_,_)=>{_settingsSaveTimer.Stop();await SaveApplicationPreferencesAsync();};
        Root.SizeChanged+=(_,e)=>ApplyResponsiveLayout(e.NewSize.Width);
        Activated+=MainWindow_Activated;
        SetSidebarExpanded(true);
        NavigateToPage("dashboard");
    }
    private void RegisterTransientInfoBars()
    {
        foreach(var infoBar in new[]{GlobalInfo,ModelsInfo,EngineLibraryInfo,SettingsInfo})
        {
            infoBar.RegisterPropertyChangedCallback(InfoBar.IsOpenProperty,(_,_)=>
            {
                if(infoBar.IsOpen)ScheduleInfoBarDismissal(infoBar);
                else CancelInfoBarDismissal(infoBar);
            });
            infoBar.PointerEntered+=(_,_)=>_hoveredInfoBars.Add(infoBar);
            infoBar.PointerExited+=(_,_)=>
            {
                _hoveredInfoBars.Remove(infoBar);
                if(_expiredInfoBars.Contains(infoBar)&&infoBar.IsOpen)infoBar.IsOpen=false;
            };
        }
    }
    private void ScheduleInfoBarDismissal(InfoBar infoBar)
    {
        CancelInfoBarDismissal(infoBar);
        var cancellation=new CancellationTokenSource();
        _infoBarDismissals[infoBar]=cancellation;
        _=DismissInfoBarAfterDelayAsync(infoBar,cancellation);
    }
    private async Task DismissInfoBarAfterDelayAsync(InfoBar infoBar,CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10),cancellation.Token);
            if(cancellation.IsCancellationRequested)return;
            _expiredInfoBars.Add(infoBar);
            if(!_hoveredInfoBars.Contains(infoBar)&&infoBar.IsOpen)infoBar.IsOpen=false;
        }
        catch(OperationCanceledException){}
    }
    private void CancelInfoBarDismissal(InfoBar infoBar)
    {
        if(_infoBarDismissals.Remove(infoBar,out var cancellation))
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }
        _expiredInfoBars.Remove(infoBar);
        if(!infoBar.IsOpen)_hoveredInfoBars.Remove(infoBar);
    }
    private async Task LoadPublisherBrandingAsync()
    {
        try
        {
            var executableDirectory=Path.GetDirectoryName(Environment.ProcessPath)??AppContext.BaseDirectory;
            var file=await StorageFile.GetFileFromPathAsync(Path.Combine(executableDirectory,"Assets","2beng2.png"));
            using var stream=await file.OpenReadAsync();
            var image=new BitmapImage{DecodePixelWidth=160};
            await image.SetSourceAsync(stream);
            SidebarPublisherLogo.Source=image;
            EnginePublisherLogo.Source=image;
        }
        catch
        {
            ToolTipService.SetToolTip(SidebarPublisherSignature,"Created by 2beng2 · logo could not be loaded");
            ToolTipService.SetToolTip(EnginePublisherSignature,"Created by 2beng2 · logo could not be loaded");
        }
    }
    private void ApplyResponsiveLayout(double width)
    {
        var availableWidth=Math.Max(0,width-(_sidebarExpanded?236:72));
        var narrow=availableWidth<930;
        var veryNarrow=availableWidth<650;
        var settingsStacked=availableWidth<900;

        Grid.SetColumn(EngineActions,narrow?0:1);Grid.SetRow(EngineActions,narrow?1:0);Grid.SetColumnSpan(EngineActions,narrow?2:1);EngineActions.Orientation=narrow?Orientation.Vertical:Orientation.Horizontal;EngineActions.HorizontalAlignment=narrow?HorizontalAlignment.Stretch:HorizontalAlignment.Right;

        Grid.SetColumn(VramCard,0);Grid.SetRow(VramCard,0);Grid.SetColumnSpan(VramCard,narrow?3:1);
        Grid.SetColumn(ContextCard,narrow?0:1);Grid.SetRow(ContextCard,narrow?1:0);Grid.SetColumnSpan(ContextCard,narrow?3:1);
        Grid.SetColumn(SpeedCard,narrow?0:2);Grid.SetRow(SpeedCard,narrow?2:0);Grid.SetColumnSpan(SpeedCard,narrow?3:1);

        Grid.SetColumn(AddModelButton,narrow?0:1);Grid.SetRow(AddModelButton,narrow?1:0);Grid.SetColumnSpan(AddModelButton,narrow?1:1);AddModelButton.HorizontalAlignment=narrow?HorizontalAlignment.Stretch:HorizontalAlignment.Right;
        Grid.SetColumn(RefreshModelsButton,narrow?1:2);Grid.SetRow(RefreshModelsButton,narrow?1:0);Grid.SetColumnSpan(RefreshModelsButton,narrow?2:1);RefreshModelsButton.HorizontalAlignment=HorizontalAlignment.Stretch;

        var cards=new[]{CompletedCard,AverageCard,TtftCard,TokensCard};
        for(var i=0;i<cards.Length;i++)
        {
            Grid.SetRow(cards[i],veryNarrow?i:narrow?i/2:0);
            Grid.SetColumn(cards[i],veryNarrow?0:narrow?(i%2)*2:i);
            Grid.SetColumnSpan(cards[i],veryNarrow?4:narrow?2:1);
        }

        Grid.SetColumn(SettingsProfileColumn,0);Grid.SetRow(SettingsProfileColumn,0);Grid.SetColumnSpan(SettingsProfileColumn,settingsStacked?2:1);
        Grid.SetColumn(SettingsApplicationColumn,settingsStacked?0:1);Grid.SetRow(SettingsApplicationColumn,settingsStacked?1:0);Grid.SetColumnSpan(SettingsApplicationColumn,settingsStacked?2:1);
        SettingsWorkspace.ColumnSpacing=settingsStacked?0:16;
    }
    private async void MainWindow_Activated(object sender,WindowActivatedEventArgs args)
    {
        Activated-=MainWindow_Activated;
        try
        {
            InitializeTray();
            await _backend.EnsureStartedAsync();await ReloadAllAsync();_timer.Start();
            if(_settings?.StartMinimized==true||Environment.GetCommandLineArgs().Contains("--minimized",StringComparer.OrdinalIgnoreCase))MinimizeToTray();
        }
        catch(Exception ex){ShowGlobal("Backend unavailable",ex.Message,InfoBarSeverity.Error);}
    }
    private void InitializeTray()
    {
        if(_tray is not null)return;
        try
        {
            _tray=new TrayIconController(_windowHandle);
            _tray.RestoreRequested+=()=>DispatcherQueue.TryEnqueue(RestoreFromTray);
            _tray.PrimaryActionRequested+=()=>DispatcherQueue.TryEnqueue(async()=>await ToggleEngineAsync());
            _tray.RestartRequested+=()=>DispatcherQueue.TryEnqueue(async()=>await RunCommandAsync("restart"));
            _tray.IdleRequested+=minutes=>DispatcherQueue.TryEnqueue(async()=>await SetIdleFromTrayAsync(minutes));
            _tray.CheckUpdatesRequested+=()=>DispatcherQueue.TryEnqueue(async()=>await CheckForUpdatesAsync(true));
            _tray.ExitRequested+=()=>DispatcherQueue.TryEnqueue(ExitCompletely);
        }
        catch(Exception ex)
        {
            _tray=null;
            ShowGlobal("Tray icon unavailable",ex.Message,InfoBarSeverity.Warning);
        }
    }
    private async Task ReloadAllAsync(){_settings=await _backend.SettingsAsync();ApplyTheme();LoadSettingsControls();BuildAdvancedSettings();await RefreshModelsAsync();await RefreshStatusAsync();await RefreshEngineLibraryAsync();await RefreshRequestsAsync();await ShowFirstRunAsync();if(_settings?.AutoCheckUpdates==true)_=CheckUpdateSilentlyAsync();}
    private async Task CheckUpdateSilentlyAsync(){try{if(_settings?.LastUpdateCheckUtc is not null&&DateTimeOffset.UtcNow-_settings.LastUpdateCheckUtc<TimeSpan.FromHours(Math.Max(1,_settings.UpdateCheckHours)))return;await CheckForUpdatesAsync(false);}catch{}}
    private async Task ShowFirstRunAsync()
    {
        if(_settings is null||_settings.FirstRunCompleted)return;
        await ShowSetupWizardAsync(true);
    }
    private async Task RefreshStatusAsync()
    {
        try
        {
            var status=await _backend.StatusAsync(); if(status is null)return;
            _engineState=status.Engine;_hasActiveModel=!string.IsNullOrWhiteSpace(status.ActiveModelId);
            TopEndpoint.Text=$":{status.PublicPort}";EndpointBox.Text=status.ApiBaseUrl;EngineBadge.Text=status.Engine.ToString().ToUpperInvariant();EngineHeadline.Text=status.Engine switch{EngineState.Ready=>"Model ready.",EngineState.Loading=>"Loading your model…",EngineState.Error=>"Engine needs attention.",_=>"Everything is ready."};ActiveModelText.Text=status.ActiveModelName??"Choose an installed model";
            ActiveModelApiName.Text=status.ActiveModelId??"—";EngineStateText.Text=status.Engine.ToString();EngineStateDetailText.Text=status.Engine.ToString();EngineModelText.Text=status.ActiveModelName??"None selected";EngineApiText.Text=status.ApiBaseUrl;
            await RefreshEngineOperationAsync();
            UpdateEngineActions();
            _tray?.SetStatus(status.Engine,status.ActiveModelName);
            if(status.Gpu is not null){var p=status.Gpu.TotalMiB==0?0:status.Gpu.UsedMiB*100d/status.Gpu.TotalMiB;VramRing.Value=p;SidebarVramBar.Value=p;VramPercent.Text=$"{p:0}%";VramValue.Text=$"{status.Gpu.UsedMiB:N0} / {status.Gpu.TotalMiB:N0} MiB";GpuName.Text=status.Gpu.Name;GpuUtilizationText.Text=$"{status.Gpu.UtilizationPercent}%";SidebarGpuName.Text=status.Gpu.Name;SidebarVramText.Text=$"{status.Gpu.UsedMiB/1024d:0.0} / {status.Gpu.TotalMiB/1024d:0.0} GB VRAM";EngineGpuText.Text=status.Gpu.Name;ToolTipService.SetToolTip(PaneFooterCompact,$"{status.Gpu.Name}\n{status.Gpu.UsedMiB/1024d:0.0} / {status.Gpu.TotalMiB/1024d:0.0} GB VRAM\nLocal workspace");}
            else ToolTipService.SetToolTip(PaneFooterCompact,"GPU information unavailable\nLocal workspace");
            if(status.PortChangedAutomatically)ShowGlobal("Port changed",$"Port {_settings?.PublicPort} was unavailable. The API is using {status.PublicPort} for this session.",InfoBarSeverity.Warning);
            if(status.PortConflict&&!_portConflictShown){_portConflictShown=true;ShowGlobal("Locked port unavailable",$"Port {status.PublicPort} is already in use. Choose another port in Settings and select Save and restart API.",InfoBarSeverity.Error);}
        }
        catch { }
    }
    private async Task RefreshModelsAsync()
    {
        var previous=_profileModel?.FileName;var entries=await _backend.ModelsAsync();_catalogModels.Clear();_catalogModels.AddRange(entries.Select(x=>new ModelItemView(x)));ProfileModels.Clear();foreach(var x in _catalogModels.Where(x=>x.Installed))ProfileModels.Add(x);RecommendedModels.Clear();foreach(var x in _catalogModels.Where(x=>x.Source.Featured))RecommendedModels.Add(x);_activeModel=entries.FirstOrDefault(x=>x.Active&&x.Installed);_profileModel=entries.FirstOrDefault(x=>x.Installed&&x.FileName.Equals(previous,StringComparison.OrdinalIgnoreCase))??_activeModel??entries.FirstOrDefault(x=>x.Installed);ProfileModelPicker.ItemsSource=ProfileModels;ProfileModelPicker.SelectedItem=ProfileModels.FirstOrDefault(x=>x.FileName.Equals(_profileModel?.FileName,StringComparison.OrdinalIgnoreCase));ApplyModelFilter();LoadProfileControls();UpdateProfileSummary();BuildAdvancedSettings();ActiveModelMeta.Text=_activeModel is null?"No artifact selected":$"{_activeModel.Weights} · {_activeModel.SizeText}{(_activeModel.Vision?" · Vision":"")}";
    }
    private void ApplyModelFilter(){Models.Clear();foreach(var item in _catalogModels.Where(x=>x.Installed))Models.Add(item);ModelsEmptyState.Visibility=Models.Count==0?Visibility.Visible:Visibility.Collapsed;ModelsList.Visibility=Models.Count==0?Visibility.Collapsed:Visibility.Visible;}
    private void ProfileModel_SelectionChanged(object sender,SelectionChangedEventArgs e){if(ProfileModelPicker.SelectedItem is not ModelItemView item)return;_profileModel=item.Source;LoadProfileControls();BuildAdvancedSettings();}
    private void UpdateProfileSummary(){if(_activeModel is null){ActiveProfileText.Text="Recommended";UnloadPolicyText.Text=_settings?.AutoUnloadEnabled==true?$"After {_settings.IdleMinutes:0.#} minutes idle":"Keep loaded";return;}var p=_activeModel.RecommendedProfile;if(_settings?.Profiles.TryGetValue(_activeModel.FileName,out var saved)==true)p=saved;ContextRingText.Text=p.MaxContext>=1000?$"{p.MaxContext/1000d:0.#}K":p.MaxContext.ToString();ContextValue.Text=$"{p.MaxContext:N0} tokens";KvValue.Text=$"{p.KvPrecision.ToString().ToUpperInvariant()} KV cache";ActiveProfileText.Text=$"{p.KvPrecision.ToString().ToUpperInvariant()} · {p.SpeculativeMode}";UnloadPolicyText.Text=_settings?.AutoUnloadEnabled==true?$"After {_settings.IdleMinutes:0.#} minutes idle":"Keep loaded";}
    private async Task RefreshRequestsAsync()
    {
        try{var snapshot=await _backend.RequestsAsync();if(snapshot is null)return;Requests.Clear();foreach(var x in snapshot.Requests)Requests.Add(new(x));CompletedValue.Text=snapshot.FailedCount>0?$"{snapshot.CompletedCount:N0} · {snapshot.FailedCount} failed":snapshot.CompletedCount.ToString("N0");AverageValue.Text=snapshot.AverageDecode is null?"—":$"{snapshot.AverageDecode:N1} tok/s";TtftValue.Text=snapshot.AverageTtft is null?"—":$"{snapshot.AverageTtft:N0} ms";TotalTokensValue.Text=snapshot.TotalTokens.ToString("N0");SpeedRingText.Text=snapshot.AverageDecode is null?"—":$"{snapshot.AverageDecode:N0}";SpeedRing.Value=snapshot.AverageDecode is null?0:Math.Clamp(snapshot.AverageDecode.Value/200d*100d,0,100);SpeedValue.Text=snapshot.AverageDecode is null?"No completed requests":$"{snapshot.AverageDecode:N1} tok/s";}catch{}
    }
    private void SidebarNav_Click(object sender,RoutedEventArgs e)
    {
        if(sender is FrameworkElement{Tag:string tag})NavigateToPage(tag);
    }
    private void NavigateToPage(string tag)
    {
        _activePage=tag;
        DashboardPage.Visibility=tag=="dashboard"?Visibility.Visible:Visibility.Collapsed;
        ModelsPage.Visibility=tag=="models"?Visibility.Visible:Visibility.Collapsed;
        RequestsPage.Visibility=tag=="requests"?Visibility.Visible:Visibility.Collapsed;
        LogsPage.Visibility=tag=="logs"?Visibility.Visible:Visibility.Collapsed;
        EnginePage.Visibility=tag=="engine"?Visibility.Visible:Visibility.Collapsed;
        SettingsPage.Visibility=tag=="settings"?Visibility.Visible:Visibility.Collapsed;
        TopPageTitle.Text=tag switch{"dashboard"=>"Overview","models"=>"Models","requests"=>"Requests","logs"=>"Logs","engine"=>"Engine","settings"=>"Settings",_=>"Overview"};
        UpdateNavigationSelection();
        if(tag=="requests")_ = RefreshRequestsAsync();
        if(tag=="logs"){_visibleLogTicks=0;_ = RefreshLogTextAsync();}
    }
    private void UpdateNavigationSelection()
    {
        var selectedBackground=(Brush)Application.Current.Resources["AccentSoftBrush"];
        var selectedForeground=(Brush)Application.Current.Resources["AccentBrush"];
        var normalForeground=(Brush)Application.Current.Resources["PrimaryTextBrush"];
        foreach(var button in SidebarButtons())
        {
            var selected=string.Equals(button.Tag?.ToString(),_activePage,StringComparison.Ordinal);
            button.Background=selected?selectedBackground:new SolidColorBrush(Colors.Transparent);
            button.Foreground=selected?selectedForeground:normalForeground;
        }
    }
    private void PaneToggle_Click(object sender,RoutedEventArgs e)=>SetSidebarExpanded(!_sidebarExpanded);
    private void SetSidebarExpanded(bool expanded)
    {
        _sidebarExpanded=expanded;
        SidebarColumn.Width=new GridLength(expanded?236:72);
        SidebarHeader.Height=expanded?64:112;
        SidebarBrandExpanded.Visibility=expanded?Visibility.Visible:Visibility.Collapsed;
        SidebarBrandCompact.Visibility=expanded?Visibility.Collapsed:Visibility.Visible;
        PaneFooterExpanded.Visibility=expanded?Visibility.Visible:Visibility.Collapsed;
        PaneFooterCompact.Visibility=expanded?Visibility.Collapsed:Visibility.Visible;
        PaneToggleButton.HorizontalAlignment=expanded?HorizontalAlignment.Right:HorizontalAlignment.Center;
        PaneToggleButton.VerticalAlignment=expanded?VerticalAlignment.Center:VerticalAlignment.Bottom;
        PaneToggleButton.Margin=expanded?new Thickness(0):new Thickness(0,0,0,8);
        PaneToggleDirectionGlyph.Glyph=expanded?"\uE76B":"\uE76C";
        foreach(var label in SidebarLabels())label.Visibility=expanded?Visibility.Visible:Visibility.Collapsed;
        foreach(var button in SidebarButtons())
        {
            button.Width=expanded?double.NaN:52;
            button.HorizontalAlignment=expanded?HorizontalAlignment.Stretch:HorizontalAlignment.Center;
            button.HorizontalContentAlignment=expanded?HorizontalAlignment.Left:HorizontalAlignment.Center;
            button.Padding=expanded?new Thickness(14,0,14,0):new Thickness(0);
        }
        ToolTipService.SetToolTip(PaneToggleButton,expanded?"Collapse navigation":"Expand navigation");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(PaneToggleButton,expanded?"Collapse navigation":"Expand navigation");
        if(Root.ActualWidth>0)ApplyResponsiveLayout(Root.ActualWidth);
    }
    private Button[] SidebarButtons()=>[DashboardNavButton,ModelsNavButton,RequestsNavButton,LogsNavButton,EngineNavButton,SettingsNavButton];
    private TextBlock[] SidebarLabels()=>[DashboardNavLabel,ModelsNavLabel,RequestsNavLabel,LogsNavLabel,EngineNavLabel,SettingsNavLabel];
    private void GoToModels_Click(object sender,RoutedEventArgs e)
        =>NavigateToModels();
    private void NavigateToModels()=>NavigateToPage("models");
    private void UpdateEngineActions()
    {
        var label=_engineState switch
        {
            EngineState.Ready=>"Unload model",
            EngineState.Loading=>"Cancel loading",
            EngineState.Unloading=>"Unloading…",
            EngineState.Error=>"Retry load",
            _=>_hasActiveModel?"Load model":"Choose model"
        };
        var enabled=_engineState is not EngineState.Unloading;
        EnginePrimaryButton.Content=label;EnginePrimaryButton.IsEnabled=enabled;
        EnginePagePrimaryButton.Content=label;EnginePagePrimaryButton.IsEnabled=enabled;
        EngineMoreButton.Visibility=_engineState is EngineState.Ready or EngineState.Error?Visibility.Visible:Visibility.Collapsed;
    }
    private async void EnginePrimary_Click(object sender,RoutedEventArgs e)=>await ToggleEngineAsync();
    private async Task ToggleEngineAsync()
    {
        if(!_hasActiveModel){NavigateToModels();return;}
        if(_engineState==EngineState.Loading){await CancelEngineOperationAsync();return;}
        await RunCommandAsync(_engineState==EngineState.Ready?"unload":"load");
    }
    private async void Restart_Click(object sender,RoutedEventArgs e)=>await RunCommandAsync("restart");
    private async Task RunCommandAsync(string command){try{await _backend.CommandAsync(command);await RefreshStatusAsync();}catch(Exception ex){ShowGlobal("Command failed",ex.Message,InfoBarSeverity.Error);}}
    private async Task RefreshEngineOperationAsync()
    {
        try
        {
            var operation=await _backend.EngineOperationAsync();
            var visible=operation?.State==EngineOperationState.Running;
            EngineOperationPanel.Visibility=visible?Visibility.Visible:Visibility.Collapsed;
            if(operation is null)return;
            EngineOperationPhaseText.Text=operation.Phase;
            EngineOperationMessageText.Text=operation.Message;
            EngineOperationElapsedText.Text=$"{TimeSpan.FromSeconds(operation.ElapsedSeconds):m\\:ss} elapsed";
            CancelEngineOperationButton.IsEnabled=operation.CanCancel;
        }
        catch { EngineOperationPanel.Visibility=Visibility.Collapsed; }
    }
    private async void CancelEngineOperation_Click(object sender,RoutedEventArgs e)=>await CancelEngineOperationAsync();
    private async Task CancelEngineOperationAsync()
    {
        try{await _backend.CancelEngineOperationAsync();await RefreshStatusAsync();}
        catch(Exception ex){ShowGlobal("Could not cancel loading",ex.Message,InfoBarSeverity.Error);}
    }
    private async Task RefreshEngineLibraryAsync()
    {
        try
        {
            var snapshot=await _backend.EnginesAsync();if(snapshot is null)return;
            _enginePackages.Clear();_enginePackages.AddRange(snapshot.Packages);
            EnginePackagePicker.Items.Clear();
            foreach(var package in _enginePackages)EnginePackagePicker.Items.Add($"{(package.Recommended?"Recommended · ":"")}{FriendlyArchitecture(package.CudaArchitecture)} · {package.EngineVersion}{(package.Installed?" · Installed":"")}");
            var index=_enginePackages.FindIndex(x=>x.Active);if(index<0)index=_enginePackages.FindIndex(x=>x.Recommended);if(index<0&&_enginePackages.Count>0)index=0;
            EnginePackagePicker.SelectedIndex=index;
            EngineRecommendationText.Text=snapshot.RecommendedArchitecture is null
                ? $"{snapshot.DetectedGpu??"No NVIDIA GPU detected"}. Choose a package manually only if you know the GPU architecture."
                : $"{snapshot.DetectedGpu} · Recommended: {FriendlyArchitecture(snapshot.RecommendedArchitecture)}. Unlisted cards in the same architecture are Community Preview.";
            EngineCompatibilityText.Text=snapshot.ActiveArchitecture is null?"No active package":$"{FriendlyArchitecture(snapshot.ActiveArchitecture)} · NInfer All compatible · Windows x64";
        }
        catch(Exception ex){ShowInfoBar(EngineLibraryInfo,"Engine catalog unavailable",ex.Message,InfoBarSeverity.Warning);}
    }
    private void EnginePackagePicker_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(EnginePackagePicker.SelectedIndex<0||EnginePackagePicker.SelectedIndex>=_enginePackages.Count){EnginePackageActionButton.IsEnabled=false;return;}
        var package=_enginePackages[EnginePackagePicker.SelectedIndex];
        EnginePackageDetailText.Text=$"{package.GpuFamily} · {package.Qualification} · {(package.NativeNvfp4?"Native NVFP4":"GSQ-RCO and compatible formats")} · {package.SizeBytes/1024d/1024d:0} MiB";
        EnginePackageActionButton.Content=package.Active?"Active engine":package.Installed?"Use this engine":"Download and install";
        EnginePackageActionButton.IsEnabled=!package.Active&&_engineState is not (EngineState.Loading or EngineState.Ready or EngineState.Unloading);
    }
    private async void RefreshEngines_Click(object sender,RoutedEventArgs e)
    {
        try{await _backend.RefreshEnginesAsync();await RefreshEngineLibraryAsync();ShowInfoBar(EngineLibraryInfo,"Engine catalog refreshed","Verified packages were loaded from the official NInferEZ Engine channel.",InfoBarSeverity.Success);}
        catch(Exception ex){ShowInfoBar(EngineLibraryInfo,"Could not refresh",ex.Message,InfoBarSeverity.Error);}
    }
    private async void EnginePackageAction_Click(object sender,RoutedEventArgs e)
    {
        if(EnginePackagePicker.SelectedIndex<0||EnginePackagePicker.SelectedIndex>=_enginePackages.Count)return;
        var package=_enginePackages[EnginePackagePicker.SelectedIndex];
        try
        {
            if(package.Installed)await _backend.ActivateEngineAsync(package.EngineVersion,package.CudaArchitecture);
            else await _backend.InstallEngineAsync(package.EngineVersion,package.CudaArchitecture);
            await RefreshEngineLibraryAsync();await RefreshEnginePackageProgressAsync();
        }
        catch(Exception ex){ShowInfoBar(EngineLibraryInfo,"Engine change failed",ex.Message,InfoBarSeverity.Error);}
    }
    private async Task RefreshEnginePackageProgressAsync()
    {
        try
        {
            var progress=await _backend.EnginePackageProgressAsync();if(progress is null)return;
            EngineInstallProgressPanel.Visibility=progress.Running?Visibility.Visible:Visibility.Collapsed;
            CancelEngineInstallButton.Visibility=progress.Running?Visibility.Visible:Visibility.Collapsed;
            EngineInstallProgressBar.IsIndeterminate=progress.Total<=0;
            if(progress.Total>0)EngineInstallProgressBar.Value=Math.Clamp(progress.Completed*100d/progress.Total,0,100);
            EngineInstallProgressText.Text=progress.Total>0?$"{progress.Stage} · {progress.Completed/1024d/1024d:0} / {progress.Total/1024d/1024d:0} MiB":progress.Stage;
            if(!progress.Running&&progress.Stage=="Ready"&&_lastEnginePackageStage!="Ready")await RefreshEngineLibraryAsync();
            if(!progress.Running&&progress.Stage=="Ready"&&_settings is not null&&!_settings.FirstRunCompleted){_settings.FirstRunCompleted=true;await _backend.SaveSettingsAsync(_settings);ShowGlobal("Engine ready","NInferEZ Engine is installed and ready to load a model.",InfoBarSeverity.Success);}
            if(!progress.Running&&!string.IsNullOrWhiteSpace(progress.Error))ShowInfoBar(EngineLibraryInfo,"Engine installation failed",progress.Error,InfoBarSeverity.Error);
            _lastEnginePackageStage=progress.Stage;
        }
        catch { }
    }
    private async void CancelEngineInstall_Click(object sender,RoutedEventArgs e){try{await _backend.CancelEngineInstallAsync();}catch(Exception ex){ShowGlobal("Could not cancel download",ex.Message,InfoBarSeverity.Error);}}
    private static string FriendlyArchitecture(string value)=>value.ToLowerInvariant() switch{"sm120a"=>"RTX 5000 Series (sm120a)","sm89"=>"RTX 4000 Series (sm89)","sm86"=>"RTX 3000 Series (sm86)",_=>value};
    private async void RefreshModels_Click(object sender,RoutedEventArgs e){try{await _backend.RefreshCatalogAsync();await RefreshModelsAsync();ShowInfoBar(ModelsInfo,"Library refreshed","The Models folder and linked files were scanned without loading GPU memory.",InfoBarSeverity.Success);}catch(Exception ex){ShowModelsError(ex);}}
    private async void AddModel_Click(object sender,RoutedEventArgs e)
    {
        var picker=new Windows.Storage.Pickers.FileOpenPicker();picker.FileTypeFilter.Add(".ninfer");WinRT.Interop.InitializeWithWindow.Initialize(picker,_windowHandle);
        var file=await picker.PickSingleFileAsync();if(file is null)return;
        try{await _backend.LinkModelAsync(file.Path);await RefreshModelsAsync();await RefreshStatusAsync();ShowInfoBar(ModelsInfo,"Model linked","The file stays in its current location and is now ready to select.",InfoBarSeverity.Success);}catch(Exception ex){ShowModelsError(ex);}
    }
    private async void DownloadModel_Click(object sender,RoutedEventArgs e)
    {
        if(sender is not FrameworkElement{Tag:string file})return;
        try{await _backend.DownloadAsync(file);await RefreshModelDownloadAsync();}
        catch(Exception ex){ShowModelsError(ex);}
    }
    private void OpenModelCard_Click(object sender,RoutedEventArgs e)
    {
        if(sender is FrameworkElement{Tag:string url}&&Uri.TryCreate(url,UriKind.Absolute,out var uri)&&uri.Scheme==Uri.UriSchemeHttps)Process.Start(new ProcessStartInfo(uri.ToString()){UseShellExecute=true});
    }
    private async Task RefreshModelDownloadAsync()
    {
        try
        {
            var progress=await _backend.DownloadStatusAsync();if(progress is null){ModelDownloadPanel.Visibility=Visibility.Collapsed;return;}
            ModelDownloadPanel.Visibility=progress.Running?Visibility.Visible:Visibility.Collapsed;
            ModelDownloadProgressBar.IsIndeterminate=progress.Total<=0;
            if(progress.Total>0)ModelDownloadProgressBar.Value=Math.Clamp(progress.Completed*100d/progress.Total,0,100);
            ModelDownloadProgressText.Text=progress.Total>0?$"{progress.Stage} · {progress.Completed/1024d/1024d/1024d:0.00} / {progress.Total/1024d/1024d/1024d:0.00} GiB":progress.Stage;
            if(!progress.Running&&progress.Stage=="Installed and verified"&&_lastModelDownloadStage!="Installed and verified")await RefreshModelsAsync();
            if(!progress.Running&&!string.IsNullOrWhiteSpace(progress.Error))ShowGlobal("Model download failed",progress.Error,InfoBarSeverity.Error);
            _lastModelDownloadStage=progress.Stage;
        }
        catch { }
    }
    private async void CancelModelDownload_Click(object sender,RoutedEventArgs e){try{await _backend.CancelDownloadAsync();}catch(Exception ex){ShowModelsError(ex);}}
    private async void Verify_Click(object sender,RoutedEventArgs e){if(sender is FrameworkElement{Tag:string file})try{var result=await _backend.VerifyAsync(file);ShowInfoBar(ModelsInfo,result?.Success==true?"Verification passed":"Verification failed",result?.Message??"Verification completed.",result?.Success==true?InfoBarSeverity.Success:InfoBarSeverity.Error);}catch(Exception ex){ShowModelsError(ex);}}
    private async void Activate_Click(object sender,RoutedEventArgs e){if(sender is Button{Tag:string file})try{await _backend.ActivateAsync(file);await RefreshModelsAsync();await RefreshStatusAsync();}catch(Exception ex){ShowModelsError(ex);}}
    private async void Delete_Click(object sender,RoutedEventArgs e)
    {
        if(sender is not FrameworkElement{Tag:string file})return;
        var dialog=new ContentDialog{Title="Remove model from manager?",Content="A linked external file will only be unlinked. A model stored inside the application's Models folder will be moved to the Recycle Bin.",PrimaryButtonText="Remove",CloseButtonText="Cancel",DefaultButton=ContentDialogButton.Close,XamlRoot=Root.XamlRoot};
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
        try{await _backend.DeleteAsync(file);await RefreshModelsAsync();}catch(Exception ex){ShowModelsError(ex);}
    }
    private void CopyEndpoint_Click(object sender,RoutedEventArgs e){var package=new DataPackage();package.SetText(EndpointBox.Text);Clipboard.SetContent(package);ShowGlobal("Copied","API base URL copied to the clipboard.",InfoBarSeverity.Success);}
    private async void ThemePicker_SelectionChanged(object sender,SelectionChangedEventArgs e){if(_settings is null)return;try{var name=(ThemePicker.SelectedItem as ComboBoxItem)?.Tag?.ToString()??"System";_settings.Theme=Enum.Parse<ThemePreference>(name);ApplyTheme();await _backend.SaveSettingsAsync(_settings);}catch(Exception ex){ShowGlobal("Theme could not be saved",ex.Message,InfoBarSeverity.Error);}}
    private void ApplyTheme(){if(_settings is null)return;Root.RequestedTheme=_settings.Theme switch{ThemePreference.Light=>ElementTheme.Light,ThemePreference.Dark=>ElementTheme.Dark,_=>ElementTheme.Default};ThemePicker.SelectedIndex=(int)_settings.Theme;}
    private void LoadSettingsControls(){if(_settings is null)return;_loadingSettings=true;try{PublicPortBox.Value=_settings.PublicPort;IdleMinutesBox.Value=_settings.IdleMinutes;LockPortBox.IsChecked=_settings.LockPublicPort;AutoUnloadBox.IsChecked=_settings.AutoUnloadEnabled;ApiKeyBox.Password=_settings.ApiKey;StartWithWindowsBox.IsChecked=SystemIntegration.IsStartupEnabled();StartMinimizedBox.IsChecked=_settings.StartMinimized;CloseToTrayBox.IsChecked=_settings.CloseToTray;AutoUpdatesBox.IsChecked=_settings.AutoCheckUpdates;AutoCatalogBox.IsChecked=_settings.AutoCheckCatalog;AutoEnginesBox.IsChecked=_settings.AutoCheckEngines;CorsBox.IsChecked=_settings.CorsEnabled;}finally{_loadingSettings=false;}}
    private void ApplicationPreference_Changed(object sender,RoutedEventArgs e)
    {
        if(_loadingSettings||_settings is null)return;
        _settingsSaveTimer.Stop();_settingsSaveTimer.Start();
    }
    private void ApplicationNumberPreference_Changed(NumberBox sender,NumberBoxValueChangedEventArgs e)
    {
        if(_loadingSettings||_settings is null)return;
        _settingsSaveTimer.Stop();_settingsSaveTimer.Start();
    }
    private async Task SaveApplicationPreferencesAsync()
    {
        if(_loadingSettings||_settings is null)return;
        try
        {
            _settings.IdleMinutes=IdleMinutesBox.Value;_settings.AutoUnloadEnabled=AutoUnloadBox.IsChecked==true;
            _settings.StartWithWindows=StartWithWindowsBox.IsChecked==true;_settings.StartMinimized=StartMinimizedBox.IsChecked==true;
            _settings.CloseToTray=CloseToTrayBox.IsChecked==true;_settings.AutoCheckUpdates=AutoUpdatesBox.IsChecked==true;_settings.AutoCheckCatalog=AutoCatalogBox.IsChecked==true;_settings.AutoCheckEngines=AutoEnginesBox.IsChecked==true;
            SystemIntegration.SetStartupEnabled(_settings.StartWithWindows);await _backend.SaveSettingsAsync(_settings);UpdateProfileSummary();
        }
        catch(Exception ex){ShowGlobal("Preference could not be saved",ex.Message,InfoBarSeverity.Error);}
    }
    private void LoadProfileControls(){if(_profileModel is null){ProfileEditor.Visibility=Visibility.Collapsed;NoInstalledProfile.Visibility=Visibility.Visible;return;}ProfileEditor.Visibility=Visibility.Visible;NoInstalledProfile.Visibility=Visibility.Collapsed;var p=_profileModel.RecommendedProfile;if(_settings?.Profiles.TryGetValue(_profileModel.FileName,out var saved)==true)p=saved;ModelApiNameBox.Text=_profileModel.ModelId;AutoContextBox.IsChecked=p.AutoContext;ContextBox.Value=p.MaxContext;PrefillBox.Value=p.PrefillChunk;KvBox.SelectedIndex=(int)p.KvPrecision;SpecBox.SelectedIndex=(int)p.SpeculativeMode;DraftBox.Value=p.DraftTokens;ConcurrencyBox.Value=1;VisionCapabilityPanel.Visibility=_profileModel.Vision?Visibility.Visible:Visibility.Collapsed;VisionBox.IsChecked=_profileModel.Vision&&p.VisionEnabled;CudaBox.IsChecked=p.CudaGraphEnabled;PrefixBox.IsChecked=p.PrefixReuseEnabled;}
    private async void SaveSettings_Click(object sender,RoutedEventArgs e)
    {
        if(_settings is null)return;try{_settings.PublicPort=(int)PublicPortBox.Value;_settings.IdleMinutes=IdleMinutesBox.Value;_settings.LockPublicPort=LockPortBox.IsChecked==true;_settings.AutoUnloadEnabled=AutoUnloadBox.IsChecked==true;_settings.ApiKey=ApiKeyBox.Password;_settings.StartWithWindows=StartWithWindowsBox.IsChecked==true;_settings.StartMinimized=StartMinimizedBox.IsChecked==true;_settings.CloseToTray=CloseToTrayBox.IsChecked==true;_settings.AutoCheckUpdates=AutoUpdatesBox.IsChecked==true;_settings.AutoCheckCatalog=AutoCatalogBox.IsChecked==true;_settings.AutoCheckEngines=AutoEnginesBox.IsChecked==true;_settings.CorsEnabled=CorsBox.IsChecked==true;ApplyAdvancedValues(_settings,_advancedAppControls);SystemIntegration.SetStartupEnabled(_settings.StartWithWindows);if(_profileModel is not null){var p=_settings.Profiles.TryGetValue(_profileModel.FileName,out var current)?current:_profileModel.RecommendedProfile;p.AutoContext=AutoContextBox.IsChecked==true;p.MaxContext=(int)ContextBox.Value;p.PrefillChunk=(int)PrefillBox.Value;p.KvPrecision=(KvPrecision)KvBox.SelectedIndex;p.SpeculativeMode=(SpeculativeMode)SpecBox.SelectedIndex;p.DraftTokens=(int)DraftBox.Value;p.MaxConcurrency=1;p.VisionEnabled=_profileModel.Vision&&VisionBox.IsChecked==true;p.CudaGraphEnabled=CudaBox.IsChecked==true;p.PrefixReuseEnabled=PrefixBox.IsChecked==true;ApplyAdvancedValues(p,_advancedProfileControls);_settings.Profiles[_profileModel.FileName]=p;var alias=ModelApiNameBox.Text.Trim();if(string.IsNullOrEmpty(alias))_settings.ModelAliases.Remove(_profileModel.FileName);else _settings.ModelAliases[_profileModel.FileName]=alias;}await _backend.SaveSettingsAsync(_settings);await RefreshModelsAsync();ShowInfoBar(SettingsInfo,"Settings saved","The API name is active immediately. Engine profile changes apply on the next model load.",InfoBarSeverity.Success);}catch(Exception ex){ShowInfoBar(SettingsInfo,"Could not save",ex.Message,InfoBarSeverity.Error);}
    }
    private async void RestoreProfile_Click(object sender,RoutedEventArgs e){if(_settings is null||_profileModel is null)return;try{_settings.Profiles[_profileModel.FileName]=_profileModel.RecommendedProfile;await _backend.SaveSettingsAsync(_settings);LoadProfileControls();BuildAdvancedSettings();ShowGlobal("Profile restored","Recommended settings will apply on the next model load.",InfoBarSeverity.Success);}catch(Exception ex){ShowGlobal("Profile could not be restored",ex.Message,InfoBarSeverity.Error);}}
    private async void RefreshLogs_Click(object sender,RoutedEventArgs e){try{await RefreshLogTextAsync();await RefreshRequestsAsync();}catch(Exception ex){ShowGlobal("Logs unavailable",ex.Message,InfoBarSeverity.Error);}}
    private async Task RefreshLogTextAsync()
    {
        try{_latestLogText=await _backend.LogsAsync();ApplyLogFilter();LogStatusText.Text=$"{_latestLogText.Count(x=>x=='\n')+1:N0} retained lines";}catch(Exception ex){LogStatusText.Text=ex.Message;}
    }
    private void LogSearchBox_TextChanged(object sender,TextChangedEventArgs e)=>ApplyLogFilter();
    private void ApplyLogFilter()
    {
        var query=LogSearchBox.Text.Trim();
        if(string.IsNullOrEmpty(query)){FullLog.Text=_latestLogText;return;}
        FullLog.Text=string.Join(Environment.NewLine,_latestLogText.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries).Where(x=>x.Contains(query,StringComparison.OrdinalIgnoreCase)));
    }
    private async void CheckUpdate_Click(object sender,RoutedEventArgs e)=>await CheckForUpdatesAsync(true);
    private async void SaveRestartPort_Click(object sender,RoutedEventArgs e)
    {
        if(_settings is null)return;
        try
        {
            _settings.PublicPort=(int)PublicPortBox.Value;_settings.LockPublicPort=LockPortBox.IsChecked==true;_settings.ApiKey=ApiKeyBox.Password;_settings.CorsEnabled=CorsBox.IsChecked==true;_settings.AutoUnloadEnabled=AutoUnloadBox.IsChecked==true;_settings.IdleMinutes=IdleMinutesBox.Value;
            await _backend.SaveSettingsAsync(_settings);
            await RestartPortAsync(true);
        }
        catch(Exception ex){ShowGlobal("API settings could not be applied",ex.Message,InfoBarSeverity.Error);}
    }
    private async Task RestartPortAsync(bool save)
    {
        try{var result=await _backend.RestartPortAsync((int)PublicPortBox.Value,LockPortBox.IsChecked==true,save);if(save&&_settings is not null){_settings.PublicPort=(int)PublicPortBox.Value;_settings.LockPublicPort=LockPortBox.IsChecked==true;}ShowGlobal("API restarted",result?.Message??"The selected port is active.",InfoBarSeverity.Success);await RefreshStatusAsync();}
        catch(Exception ex){ShowGlobal("Port unavailable",ex.Message,InfoBarSeverity.Error);}
    }
    private async void OpenLog_Click(object sender,RoutedEventArgs e){try{await _backend.OpenAsync("logs");}catch(Exception ex){ShowGlobal("Could not open log",ex.Message,InfoBarSeverity.Error);}}
    private async void Diagnostics_Click(object sender,RoutedEventArgs e){try{var result=await _backend.DiagnosticsAsync();if(!string.IsNullOrWhiteSpace(result?.Message))Process.Start(new ProcessStartInfo("explorer.exe",$"/select,\"{result.Message}\""){UseShellExecute=true});ShowGlobal("Diagnostics created",result?.Message??"The diagnostics package was created.",InfoBarSeverity.Success);}catch(Exception ex){ShowGlobal("Diagnostics failed",ex.Message,InfoBarSeverity.Error);}}
    private async void CopyCommand_Click(object sender,RoutedEventArgs e){try{var package=new DataPackage();package.SetText(await _backend.CommandPreviewAsync());Clipboard.SetContent(package);ShowGlobal("Copied","The generated NInfer command was copied.",InfoBarSeverity.Success);}catch(Exception ex){ShowGlobal("Command unavailable",ex.Message,InfoBarSeverity.Error);}}
    private async void SetupWizard_Click(object sender,RoutedEventArgs e){try{await ShowSetupWizardAsync(false);}catch(Exception ex){ShowGlobal("Setup failed",ex.Message,InfoBarSeverity.Error);}}

    private async Task ShowSetupWizardAsync(bool firstRun)
    {
        if(_settings is null)return;
        if(Models.Count==0)await RefreshModelsAsync();
        var engineSnapshot=await _backend.EnginesAsync();
        var availableEngines=engineSnapshot?.Packages.ToList()??[];
        var needsEngine=string.IsNullOrWhiteSpace(engineSnapshot?.ActiveArchitecture);
        var enginePicker=new ComboBox{HorizontalAlignment=HorizontalAlignment.Stretch};
        foreach(var engine in availableEngines)enginePicker.Items.Add($"{(engine.Recommended?"Recommended · ":"")}{FriendlyArchitecture(engine.CudaArchitecture)} · {engine.EngineVersion}{(engine.Installed?" · Installed":"")}");
        var recommendedIndex=availableEngines.FindIndex(x=>x.Recommended);enginePicker.SelectedIndex=recommendedIndex>=0?recommendedIndex:(availableEngines.Count>0?0:-1);
        var modelPicker=new ComboBox{ItemsSource=Models,DisplayMemberPath=nameof(ModelItemView.DisplayName),HorizontalAlignment=HorizontalAlignment.Stretch,SelectedIndex=Models.Count>0?0:-1};
        var port=new NumberBox{Minimum=1024,Maximum=65535,Value=_settings.PublicPort,SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Compact,HorizontalAlignment=HorizontalAlignment.Stretch};
        var content=new StackPanel{Spacing=12};content.Children.Add(new TextBlock{Text=needsEngine?"Choose the engine package recommended for this computer. The verified package is downloaded from the official NInferEZ Engine release.":"The installed engine is ready. Choose the API port and an optional local model.",TextWrapping=TextWrapping.Wrap});
        if(needsEngine){content.Children.Add(new TextBlock{Text="Engine package"});content.Children.Add(enginePicker);content.Children.Add(new TextBlock{Text="You can choose another compatible package manually. Unqualified cards in the same architecture are Community Preview.",TextWrapping=TextWrapping.Wrap,Opacity=.72});}
        content.Children.Add(new TextBlock{Text="API port"});content.Children.Add(port);
        if(Models.Count>0){content.Children.Add(new TextBlock{Text="Model"});content.Children.Add(modelPicker);}
        var dialog=new ContentDialog{Title="NInferEZ Manager setup",Content=content,PrimaryButtonText=needsEngine?"Download engine and continue":"Apply setup",CloseButtonText=firstRun&&needsEngine?string.Empty:"Cancel",DefaultButton=ContentDialogButton.Primary,XamlRoot=Root.XamlRoot};
        var result=await dialog.ShowAsync();
        if(result!=ContentDialogResult.Primary)return;
        if(needsEngine)
        {
            if(enginePicker.SelectedIndex<0||enginePicker.SelectedIndex>=availableEngines.Count){ShowGlobal("Choose an engine","Select a compatible engine package to continue.",InfoBarSeverity.Warning);return;}
            var selected=availableEngines[enginePicker.SelectedIndex];
            if(selected.Installed){await _backend.ActivateEngineAsync(selected.EngineVersion,selected.CudaArchitecture);_settings.FirstRunCompleted=true;await _backend.SaveSettingsAsync(_settings);}
            else{await _backend.InstallEngineAsync(selected.EngineVersion,selected.CudaArchitecture);NavigateToPage("engine");}
        }
        else{_settings.FirstRunCompleted=true;await _backend.SaveSettingsAsync(_settings);}
        PublicPortBox.Value=port.Value;await RestartPortAsync(true);
        if(modelPicker.SelectedItem is ModelItemView model)
        {
            await _backend.ActivateAsync(model.FileName);
            await RefreshModelsAsync();await RefreshStatusAsync();
        }
    }

    private async Task CheckForUpdatesAsync(bool interactive)
    {
        try
        {
            var update=await _backend.CheckUpdateAsync();if(update is null)return;_availableUpdate=update;
            if(!update.UpdateAvailable){if(interactive)ShowGlobal("Up to date",update.Message,InfoBarSeverity.Success);return;}
            if(string.IsNullOrWhiteSpace(update.AssetUrl)){ShowGlobal("Update available",update.Message,InfoBarSeverity.Warning);if(interactive&&update.ReleaseUrl is not null)Process.Start(new ProcessStartInfo(update.ReleaseUrl){UseShellExecute=true});return;}
            ShowGlobal("Update available",update.Message,InfoBarSeverity.Informational);
            if(!interactive)return;
            var dialog=new ContentDialog{Title=$"Install NInferEZ Manager {update.LatestVersion}?",Content="The package will be downloaded and verified with SHA-256 before installation.",PrimaryButtonText="Download and install",SecondaryButtonText="View release",CloseButtonText="Later",DefaultButton=ContentDialogButton.Primary,XamlRoot=Root.XamlRoot};
            var choice=await dialog.ShowAsync();if(choice==ContentDialogResult.Secondary&&update.ReleaseUrl is not null){Process.Start(new ProcessStartInfo(update.ReleaseUrl){UseShellExecute=true});return;}if(choice!=ContentDialogResult.Primary)return;
            await _backend.DownloadUpdateAsync(update);
            while(true){await Task.Delay(500);var progress=await _backend.UpdateProgressAsync();if(progress is null)continue;ShowInfoBar(GlobalInfo,"Updating NInferEZ Manager",$"{progress.Stage} · {(progress.Total>0?progress.Completed*100d/progress.Total:0):0}%",progress.Error is null?InfoBarSeverity.Informational:InfoBarSeverity.Error);if(!progress.Running){if(progress.Error is not null)return;break;}}
            await _backend.ApplyUpdateAsync();_realExit=true;await Task.Delay(250);Close();
        }
        catch(Exception ex){if(interactive)ShowGlobal("Update failed",ex.Message,InfoBarSeverity.Error);}
    }

    private void BuildAdvancedSettings()
    {
        if(_settings is null)return;
        AdvancedAppPanel.Children.Clear();AdvancedProfilePanel.Children.Clear();_advancedAppControls.Clear();_advancedProfileControls.Clear();
        var appSkip=new HashSet<string>([nameof(ManagerSettings.Theme),nameof(ManagerSettings.PublicPort),nameof(ManagerSettings.LockPublicPort),nameof(ManagerSettings.ApiKey),nameof(ManagerSettings.AutoUnloadEnabled),nameof(ManagerSettings.IdleMinutes),nameof(ManagerSettings.FirstRunCompleted),nameof(ManagerSettings.ActiveModelFile),nameof(ManagerSettings.Profiles),nameof(ManagerSettings.ExternalModelPaths),nameof(ManagerSettings.ModelAliases),nameof(ManagerSettings.LastCatalogCheckUtc),nameof(ManagerSettings.LastEngineCheckUtc),nameof(ManagerSettings.LastUpdateCheckUtc),nameof(ManagerSettings.StartWithWindows),nameof(ManagerSettings.StartMinimized),nameof(ManagerSettings.CloseToTray),nameof(ManagerSettings.AutoCheckUpdates),nameof(ManagerSettings.AutoCheckCatalog),nameof(ManagerSettings.AutoCheckEngines),nameof(ManagerSettings.CorsEnabled)]);
        AddPropertyControls(AdvancedAppPanel,_settings,_advancedAppControls,appSkip);
        if(_profileModel is null){AdvancedProfilePanel.Children.Add(new TextBlock{Text="Choose a model profile to edit its complete settings.",TextWrapping=TextWrapping.Wrap});return;}
        var profile=_settings.Profiles.TryGetValue(_profileModel.FileName,out var saved)?saved:_profileModel.RecommendedProfile;
        var profileSkip=new HashSet<string>([nameof(ModelProfile.AutoContext),nameof(ModelProfile.MaxContext),nameof(ModelProfile.PrefillChunk),nameof(ModelProfile.KvPrecision),nameof(ModelProfile.SpeculativeMode),nameof(ModelProfile.DraftTokens),nameof(ModelProfile.MaxConcurrency),nameof(ModelProfile.VisionEnabled),nameof(ModelProfile.CudaGraphEnabled),nameof(ModelProfile.PrefixReuseEnabled)]);
        AddPropertyControls(AdvancedProfilePanel,profile,_advancedProfileControls,profileSkip);
    }
    private static void AddPropertyControls(StackPanel panel,object source,Dictionary<PropertyInfo,Control> map,HashSet<string> skip)
    {
        foreach(var property in source.GetType().GetProperties().Where(p=>p.CanRead&&p.CanWrite&&!skip.Contains(p.Name)&&EditableSettingTypes.Supports(p.PropertyType)))
        {
            var value=property.GetValue(source);Control input;
            var actual=Nullable.GetUnderlyingType(property.PropertyType)??property.PropertyType;
            if(actual==typeof(bool))input=new CheckBox{IsChecked=(bool?)value??false};
            else if(actual.IsEnum){var combo=new ComboBox{ItemsSource=Enum.GetValues(actual),HorizontalAlignment=HorizontalAlignment.Stretch};combo.SelectedItem=value;input=combo;}
            else if(actual==typeof(int)||actual==typeof(double)){input=new NumberBox{Minimum=-1_000_000,Maximum=10_000_000,Value=value is null?double.NaN:Convert.ToDouble(value),SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Compact,HorizontalAlignment=HorizontalAlignment.Stretch};}
            else input=new TextBox{Text=value?.ToString()??"",HorizontalAlignment=HorizontalAlignment.Stretch};
            var card=new StackPanel{Spacing=4};card.Children.Add(new TextBlock{Text=Regex.Replace(property.Name,"([a-z])([A-Z])","$1 $2")});card.Children.Add(input);panel.Children.Add(card);map[property]=input;
        }
    }
    private static void ApplyAdvancedValues(object target,Dictionary<PropertyInfo,Control> controls)
    {
        foreach(var (property,control) in controls)
        {
            if(!EditableSettingTypes.Supports(property.PropertyType))continue;
            var nullable=Nullable.GetUnderlyingType(property.PropertyType);var actual=nullable??property.PropertyType;object? value;
            if(control is CheckBox check)value=check.IsChecked==true;
            else if(control is ComboBox combo)value=combo.SelectedItem;
            else if(control is NumberBox number)
            {
                if(double.IsNaN(number.Value))value=null;
                // Cast each branch to object explicitly. Without this, C# promotes the
                // int branch of the conditional expression back to double before boxing,
                // and PropertyInfo.SetValue cannot assign that Double to Int32/Int32?.
                else if(actual==typeof(int))value=(object)checked((int)Math.Round(number.Value,MidpointRounding.AwayFromZero));
                else value=(object)number.Value;
            }
            else value=((TextBox)control).Text;
            property.SetValue(target,value);
        }
    }

    private async Task SetIdleFromTrayAsync(double? minutes)
    {
        if(_settings is null)return;_settings.AutoUnloadEnabled=minutes is not null;if(minutes is not null)_settings.IdleMinutes=minutes.Value;await _backend.SaveSettingsAsync(_settings);LoadSettingsControls();
    }
    private void OnWindowClosing(AppWindow sender,AppWindowClosingEventArgs args)
    {
        if(_realExit||_settings?.CloseToTray==false)return;args.Cancel=true;MinimizeToTray();
    }
    private void MinimizeToTray()
    {
        if(_hiddenToTray)return;_hiddenToTray=true;_timer.Stop();SystemIntegration.HideWindow(_windowHandle);_ = TrimIdleAsync();
    }
    private async Task TrimIdleAsync()
    {
        // WinUI completes a small amount of deferred layout work after the window
        // is hidden. Trimming after that work keeps tray memory low and avoids a
        // polling timer or any permanent background worker.
        await Task.Delay(1500);
        if(!_hiddenToTray)return;
        try{await _backend.TrimAsync();}catch{}
        SystemIntegration.TrimWorkingSet();
        await Task.Delay(2500);
        if(_hiddenToTray)SystemIntegration.TrimWorkingSet();
    }
    private void RestoreFromTray()
    {
        if(!_hiddenToTray)return;_hiddenToTray=false;SystemIntegration.ShowWindow(_windowHandle);_timer.Start();_ = ReloadVisibleAsync();
    }
    public void ActivateFromSecondaryInstance()
    {
        if(_hiddenToTray)RestoreFromTray();
        else SystemIntegration.ShowWindow(_windowHandle);
        Activate();
    }
    private async Task ReloadVisibleAsync(){await RefreshStatusAsync();await RefreshModelsAsync();await RefreshRequestsAsync();}
    private void ExitCompletely(){_realExit=true;Close();}
    private void ShowInfoBar(InfoBar infoBar,string title,string message,InfoBarSeverity severity){infoBar.Title=title;infoBar.Message=message;infoBar.Severity=severity;infoBar.IsOpen=true;ScheduleInfoBarDismissal(infoBar);}
    private void ShowModelsError(Exception ex)=>ShowInfoBar(ModelsInfo,"Model action failed",ex.Message,InfoBarSeverity.Error);
    private void ShowGlobal(string title,string message,InfoBarSeverity severity)=>ShowInfoBar(GlobalInfo,title,message,severity);
}

public sealed class ModelItemView
{
    public ModelItemView(ModelInfo x){Source=x;DisplayName=x.DisplayName;Repository=x.Repository;FileName=x.FileName;Weights=x.Weights;SizeText=x.SizeText;ModelCardUrl=x.ModelCardUrl;Installed=x.Installed;Vision=x.Vision;VisionVisibility=x.Vision?Visibility.Visible:Visibility.Collapsed;DownloadAvailable=x.DownloadAvailable;DownloadVisibility=!x.Installed&&x.DownloadAvailable?Visibility.Visible:Visibility.Collapsed;InstalledVisibility=x.Installed?Visibility.Visible:Visibility.Collapsed;NewVisibility=x.IsNew?Visibility.Visible:Visibility.Collapsed;ImportVisibility=x.Installed?Visibility.Collapsed:Visibility.Visible;ActivateVisibility=x.Installed&&!x.Active?Visibility.Visible:Visibility.Collapsed;VerifyVisibility=x.Installed?Visibility.Visible:Visibility.Collapsed;DeleteVisibility=x.Installed?Visibility.Visible:Visibility.Collapsed;StateLabel=x.Active?"ACTIVE":x.Installed?"INSTALLED":"AVAILABLE";}
    public ModelInfo Source{get;} public string DisplayName{get;} public string Repository{get;} public string FileName{get;} public string Weights{get;} public string SizeText{get;} public string ModelCardUrl{get;} public bool Installed{get;} public bool Vision{get;} public Visibility VisionVisibility{get;} public bool DownloadAvailable{get;} public Visibility DownloadVisibility{get;} public Visibility InstalledVisibility{get;} public Visibility NewVisibility{get;} public Visibility ImportVisibility{get;} public Visibility ActivateVisibility{get;} public Visibility VerifyVisibility{get;} public Visibility DeleteVisibility{get;} public string StateLabel{get;}
}
public sealed class RequestItemView
{
    public RequestItemView(RequestMetric x){Time=x.StartedAt.ToString("HH:mm:ss");Status=x.Status;Type=x.Type;Input=x.PromptTokens==0?"—":x.PromptTokens.ToString("N0");Output=x.GeneratedTokens==0?"—":x.GeneratedTokens.ToString("N0");Ttft=x.TtftMs is null?"—":$"{x.TtftMs:N0} ms";Prefill=x.PrefillTokensPerSecond is null?"—":$"{x.PrefillTokensPerSecond:N1}";Decode=x.DecodeTokensPerSecond is null?"—":$"{x.DecodeTokensPerSecond:N1}";Speculative=x.Speculative;}
    public string Time{get;}public string Status{get;}public string Type{get;}public string Input{get;}public string Output{get;}public string Ttft{get;}public string Prefill{get;}public string Decode{get;}public string Speculative{get;}
}
