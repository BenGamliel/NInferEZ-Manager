using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
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
    private readonly DispatcherTimer _catalogNotificationTimer=new(){Interval=TimeSpan.FromSeconds(30)};
    private bool _checkingModelAnnouncements;
    private bool _dialogOpen;
    private long _catalogRevision;
    private readonly HashSet<string> _sessionAnnouncements=new(StringComparer.Ordinal);
    private readonly Dictionary<InfoBar,CancellationTokenSource> _infoBarDismissals=[];
    private readonly HashSet<InfoBar> _hoveredInfoBars=[];
    private readonly HashSet<InfoBar> _expiredInfoBars=[];
    private TrayIconController? _tray;
    private WindowIcon? _nativeWindowIcon;
    private readonly IntPtr _windowHandle;
    private ManagerSettings? _settings;
    private ModelInfo? _activeModel;
    private ModelInfo? _profileModel;
    private bool _realExit;
    private bool _hiddenToTray;
    private bool _portConflictShown;
    private bool _refreshing;
    private bool _closing;
    private bool _exitPromptOpen;
    private bool _checkingUpdates;
    private bool _apiEnabled=true;
    private string? _lastStatusEvent;
    private string? _lastDownloadEvent;
    private string? _lastEngineEvent;
    private string? _lastOperationEvent;
    private string? _requestsFingerprint;
    private readonly SemaphoreSlim _settingsWriteGate = new(1, 1);
    private string _latestLogText=string.Empty;
    private int _visibleLogTicks;
    private EngineState _engineState=EngineState.Unloaded;
    private GpuStatus? _fitGpu;
    private bool _hasActiveModel;
    private bool _sidebarExpanded=true;
    private string _activePage="dashboard";
    private readonly Dictionary<SettingDescriptor,Control> _advancedAppControls=[];
    private readonly Dictionary<SettingDescriptor,Control> _advancedProfileControls=[];
    public ObservableCollection<ModelItemView> Models { get; }=[];
    public ObservableCollection<ModelItemView> ProfileModels { get; }=[];
    public ObservableCollection<RecommendedModelView> RecommendedModels { get; }=[];
    public ObservableCollection<RequestItemView> Requests { get; }=[];
    private readonly List<ModelItemView> _catalogModels=[];
    private readonly List<EnginePackageInfo> _enginePackages=[];
    private string _lastEnginePackageStage="Idle";
    private bool _loadingSettings;
    private bool _loadingProfile;
    private ModelProfile? _editingProfile;
    private string _lastModelDownloadStage="Idle";

    public MainWindow()
    {
        InitializeComponent();
        InitializeConverter();
        InstallSettingsHelp();
        RegisterTransientInfoBars();
        Title="NInferEZ Manager";
        AboutVersionText.Text=$"{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)??"unknown"}";
        _=LoadPublisherBrandingAsync();
        _windowHandle=WinRT.Interop.WindowNative.GetWindowHandle(this);
        var executableDirectory=Path.GetDirectoryName(Environment.ProcessPath)??AppContext.BaseDirectory;
        var iconPath=Path.Combine(executableDirectory,"Assets","NInferEZ.ico");
        if(File.Exists(iconPath)){AppWindow.SetIcon(iconPath);_nativeWindowIcon=new WindowIcon(_windowHandle,iconPath);}
        AppWindow.Resize(new SizeInt32(1420,900));
        AppWindow.Closing+=OnWindowClosing;
        Closed+=(_,_)=>{_closing=true;_timer.Stop();_settingsSaveTimer.Stop();_catalogNotificationTimer.Stop();_converterTimer.Stop();foreach(var dismissal in _infoBarDismissals.Values){dismissal.Cancel();dismissal.Dispose();}_infoBarDismissals.Clear();_tray?.Dispose();_nativeWindowIcon?.Dispose();_backend.Dispose();};
        _timer.Tick+=async (_,_)=>await RefreshVisibleAsync();
        _catalogNotificationTimer.Tick+=async (_,_)=>await CheckModelAnnouncementsAsync();
        _settingsSaveTimer.Tick+=async (_,_)=>{_settingsSaveTimer.Stop();await SaveApplicationPreferencesAsync();};
        Root.SizeChanged+=(_,e)=>ApplyResponsiveLayout(e.NewSize.Width);
        Root.ActualThemeChanged+=(_,_)=>UpdateNavigationSelection();
        Activated+=MainWindow_Activated;
        SetSidebarExpanded(true);
        NavigateToPage("dashboard");
    }
    private async Task RefreshVisibleAsync()
    {
        if (_refreshing || _closing || _hiddenToTray) return;
        _refreshing = true;
        try
        {
            await RefreshStatusAsync();
            if (_activePage is "requests" or "dashboard") await RefreshRequestsAsync();
            if (_activePage == "logs" && ++_visibleLogTicks % 2 == 0) await RefreshLogTextAsync();
            if (_activePage == "engine") await RefreshEnginePackageProgressAsync();
            if (_activePage == "models") await RefreshModelDownloadAsync();
        }
        finally { _refreshing = false; }
    }
    private void RegisterTransientInfoBars()
    {
        foreach(var infoBar in new[]{GlobalInfo,ModelsInfo,EngineLibraryInfo,SettingsInfo,ConverterInfo})
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
            var logoFile=await StorageFile.GetFileFromPathAsync(Path.Combine(executableDirectory,"Assets","NInferEZ.svg"));
            using(var logoStream=await logoFile.OpenReadAsync())
            {
                var logo=new SvgImageSource();await logo.SetSourceAsync(logoStream);MainLogoExpanded.Source=logo;MainLogoCompact.Source=logo;EngineLogo.Source=logo;
            }
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
        TopBreadcrumbApp.Visibility=availableWidth<820?Visibility.Collapsed:Visibility.Visible;TopBreadcrumbSeparator.Visibility=TopBreadcrumbApp.Visibility;CopyEndpointHeaderLabel.Visibility=availableWidth<700?Visibility.Collapsed:Visibility.Visible;

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
        Grid.SetColumn(OverviewGpuCard,narrow?0:1);Grid.SetRow(OverviewGpuCard,narrow?1:0);Grid.SetColumnSpan(OverviewGpuCard,narrow?2:1);Grid.SetColumnSpan(OverviewModelCard,narrow?2:1);
        Grid.SetColumn(EngineSupportCard,narrow?0:1);Grid.SetRow(EngineSupportCard,narrow?1:0);Grid.SetColumnSpan(EngineSupportCard,narrow?2:1);Grid.SetColumnSpan(EngineInstallationCard,narrow?2:1);
        Grid.SetColumn(EngineUpdateCheckButton,narrow?0:1);Grid.SetRow(EngineUpdateCheckButton,narrow?1:0);Grid.SetColumn(EnginePagePrimaryButton,2);Grid.SetRow(EnginePagePrimaryButton,narrow?1:0);
    }
    private async void MainWindow_Activated(object sender,WindowActivatedEventArgs args)
    {
        Activated-=MainWindow_Activated;
        try
        {
            InitializeTray();
            if(!InspectPortableUpdateJournal(false))return;
            await _backend.EnsureStartedAsync();await ReloadAllAsync();ConfirmPortableUpdateLaunch();if(!string.IsNullOrWhiteSpace(_backend.SettingsRecoveryMessage))ShowGlobal("Settings recovered",_backend.SettingsRecoveryMessage,InfoBarSeverity.Warning);_timer.Start();_catalogNotificationTimer.Start();
            if(_settings?.StartMinimized==true||Environment.GetCommandLineArgs().Contains("--minimized",StringComparer.OrdinalIgnoreCase))MinimizeToTray();
            else _=CheckModelAnnouncementsAsync();
        }
        catch(Exception ex){ShowGlobal("Backend unavailable",ex.Message,InfoBarSeverity.Error);}
    }
    private void ModelCardGrid_SizeChanged(object sender,SizeChangedEventArgs args)
    {
        if(sender is not Grid grid)return;
        var actions=grid.Children.OfType<StackPanel>().FirstOrDefault(x=>x.Tag as string=="card-actions");if(actions is null)return;
        var stacked=args.NewSize.Width<760;Grid.SetRow(actions,stacked?1:0);Grid.SetColumn(actions,stacked?1:2);Grid.SetColumnSpan(actions,stacked?2:1);actions.HorizontalAlignment=stacked?HorizontalAlignment.Left:HorizontalAlignment.Right;
    }
    private void RecommendationCardGrid_SizeChanged(object sender,SizeChangedEventArgs args)
    {
        if(sender is not Grid grid)return;
        var actions=grid.Children.OfType<StackPanel>().FirstOrDefault(x=>x.Tag as string=="card-actions");if(actions is null)return;
        var stacked=args.NewSize.Width<800;Grid.SetRow(actions,stacked?1:0);Grid.SetColumn(actions,stacked?0:1);Grid.SetColumnSpan(actions,stacked?2:1);actions.HorizontalAlignment=stacked?HorizontalAlignment.Left:HorizontalAlignment.Right;
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
    private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        if(_dialogOpen||_closing)return ContentDialogResult.None;
        _dialogOpen=true;
        try{return await dialog.ShowAsync();}
        finally{_dialogOpen=false;}
    }
    private async Task CheckModelAnnouncementsAsync()
    {
        if(_checkingModelAnnouncements||_closing||_hiddenToTray||_dialogOpen||_exitPromptOpen)return;
        _checkingModelAnnouncements=true;
        try
        {
            var update=await _backend.ModelNotificationsAsync();
            if(update is null||!update.StartupCheckComplete||_closing||_hiddenToTray)return;
            if(_catalogRevision!=update.Revision&&_activePage!="settings"&&!_dialogOpen)
            {
                await RefreshModelsAsync();
                _catalogRevision=update.Revision;
            }
            var recent=update.RecentFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach(var model in _catalogModels)model.UpdateNewness(recent.Contains(model.FileName));
            var pending=update.Pending.Where(x=>!_sessionAnnouncements.Contains(x.Identity)).ToArray();
            if(pending.Length==0||_dialogOpen||_closing||_hiddenToTray||_activePage=="settings")return;
            var names=pending.Select(x=>x.DisplayName).Distinct(StringComparer.Ordinal).ToArray();
            var content=new StackPanel{Spacing=12,MaxWidth=440};
            content.Children.Add(new TextBlock{Text="Fresh model recommendations are available in your library.",TextWrapping=TextWrapping.Wrap});
            foreach(var name in names.Take(5))content.Children.Add(new TextBlock{Text=name.Length>160?name[..157]+"…":name,TextWrapping=TextWrapping.Wrap,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold});
            if(names.Length>5)content.Children.Add(new TextBlock{Text=$"And {names.Length-5} more in Models.",TextWrapping=TextWrapping.Wrap});
            content.Children.Add(new TextBlock{Text="Try it opens Models. Nothing is downloaded or loaded automatically.",TextWrapping=TextWrapping.Wrap,Style=(Style)Application.Current.Resources["CaptionStyle"]});
            var dialog=new ContentDialog{Title=names.Length==1?"A new model is available":"New models are available",Content=content,PrimaryButtonText="Try it",CloseButtonText="Dismiss",DefaultButton=ContentDialogButton.Close,XamlRoot=Root.XamlRoot};
            var choice=await ShowDialogAsync(dialog);
            if(_closing)return;
            foreach(var model in pending)_sessionAnnouncements.Add(model.Identity);
            if(choice==ContentDialogResult.Primary)
            {
                await RefreshModelsAsync();
                _catalogRevision=update.Revision;
                NavigateToPage("models");
            }
            try{await _backend.DismissModelNotificationsAsync(pending.Select(x=>x.Identity));}
            catch{ShowGlobal("Announcement preference could not be saved","The message is dismissed for this session, but may return after a restart. Your models and profiles are unchanged.",InfoBarSeverity.Warning);}
        }
        catch(Exception error)when(_closing||error is System.Net.Http.HttpRequestException or OperationCanceledException or InvalidOperationException)
        {Debug.WriteLine($"Model announcement check deferred: {error.Message}");}
        finally{_checkingModelAnnouncements=false;}
    }
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
            if(status.Gpu?.TotalMiB!=_fitGpu?.TotalMiB||status.Gpu?.Name!=_fitGpu?.Name||status.Gpu?.DeviceIndex!=_fitGpu?.DeviceIndex)
            {
                _fitGpu=status.Gpu;
                foreach(var item in _catalogModels)item.UpdateMemoryFit(_fitGpu);
            }
            var currentDefault=_catalogModels.FirstOrDefault(x=>x.ApiName.Equals(status.ActiveModelId,StringComparison.OrdinalIgnoreCase));
            if(currentDefault is not null&&currentDefault.Source.FileName!=_activeModel?.FileName){_activeModel=currentDefault.Source;UpdateProfileSummary();}
            _apiEnabled=status.ApiEnabled;ApiToggleButton.Content=_apiEnabled?"Turn off":"Turn on";ToolTipService.SetToolTip(ApiToggleButton,_apiEnabled?"Stop accepting API connections":"Start the public API listener");
            TopEndpoint.Text=$":{status.PublicPort}";EndpointBox.Text=status.ApiBaseUrl;EngineBadge.Text=status.Engine.ToString().ToUpperInvariant();EngineHeadline.Text=status.Engine switch{EngineState.Ready=>"Ready for your next request.",EngineState.Loading=>"Loading your model…",EngineState.Error=>"Engine needs attention.",EngineState.Unloading=>"Releasing model memory…",_=>_hasActiveModel?"Your model, on demand.":"Let's add your first model."};ActiveModelText.Text=status.ActiveModelName??"Choose an installed model";
            TopApiStatusText.Text=!status.ApiEnabled?"API off":status.PortConflict?"API unavailable":"API listening";TopApiIndicator.Fill=(Brush)Application.Current.Resources[!status.ApiEnabled||status.PortConflict?"WarningBrush":"SuccessBrush"];
            ApiAvailabilityText.Text=!status.ApiEnabled?"API stopped · turn it on to accept requests":status.PortConflict?"API unavailable · resolve the port conflict in Settings":"API online · models load on demand";
            EngineSubheadline.Text=!status.ApiEnabled?"The public API is paused. Turn it on in the header when you want to accept requests.":status.Engine==EngineState.Loading?"Cold loading can take time. You can cancel from the Engine page.":"The API is listening; model memory is used only when needed.";
            ModelMemoryText.Text=status.Engine==EngineState.Ready?"Model memory is allocated. The idle policy controls when it is released.":status.Engine==EngineState.Loading?"The engine is preparing model memory and acceleration.":"No model memory is reserved while the engine is unloaded.";
            RunningSessionText.Text=status.LoadedModelId??(status.Engine==EngineState.Loading?"Preparing model":"No model loaded");
            RunningSessionDetailText.Text=status.LoadedContext is int loadedContext?$"{loadedContext:N0} configured tokens · {status.LoadedEngineArchitecture??"engine"} {status.LoadedEngineVersion??""}. This is the allocation limit, not n_past.":"Saved context is a limit, not the number of tokens currently in memory.";
            ActiveModelApiName.Text=status.ActiveModelId??"—";EngineStateText.Text=status.Engine.ToString();EngineStateDetailText.Text=status.Engine.ToString();EngineModelText.Text=status.ActiveModelName??"None selected";EngineApiText.Text=status.ApiBaseUrl;
            await RefreshEngineOperationAsync();
            UpdateEngineActions();
            _tray?.SetStatus(status.Engine,status.ActiveModelName);
            if(status.Gpu is not null){var p=status.Gpu.TotalMiB==0?0:status.Gpu.UsedMiB*100d/status.Gpu.TotalMiB;VramRing.Value=p;SidebarVramBar.Value=p;VramPercent.Text=$"{p:0}%";VramValue.Text=$"{status.Gpu.UsedMiB:N0} / {status.Gpu.TotalMiB:N0} MiB";GpuName.Text=status.Gpu.Name;GpuUtilizationText.Text=$"{status.Gpu.UtilizationPercent}%";SidebarGpuName.Text=status.Gpu.Name;SidebarVramText.Text=$"{status.Gpu.UsedMiB/1024d:0.0} / {status.Gpu.TotalMiB/1024d:0.0} GB VRAM";EngineGpuText.Text=status.Gpu.Name;ToolTipService.SetToolTip(PaneFooterCompact,$"{status.Gpu.Name}\n{status.Gpu.UsedMiB/1024d:0.0} / {status.Gpu.TotalMiB/1024d:0.0} GB VRAM\nLocal workspace");}
            else ToolTipService.SetToolTip(PaneFooterCompact,"GPU information unavailable\nLocal workspace");
            var statusEvent=$"{status.PortChangedAutomatically}:{status.PortConflict}:{status.PublicPort}";
            if(status.PortChangedAutomatically&&_lastStatusEvent!=statusEvent)ShowGlobal("Port changed",$"Port {_settings?.PublicPort} was unavailable. The API is using {status.PublicPort} for this session.",InfoBarSeverity.Warning);
            _lastStatusEvent=statusEvent;
            if(status.PortConflict&&!_portConflictShown){_portConflictShown=true;ShowGlobal("Locked port unavailable",$"Port {status.PublicPort} is already in use. Choose another port in Settings and select Save and restart API.",InfoBarSeverity.Error);}
        }
        catch(Exception ex) { TopApiStatusText.Text="Connection lost";TopApiIndicator.Fill=(Brush)Application.Current.Resources["WarningBrush"];ToolTipService.SetToolTip(TopApiStatusText,ex.Message); }
    }
    private async Task RefreshModelsAsync()
    {
        var previous=_profileModel?.FileName;
        var selectedVariants=RecommendedModels.ToDictionary(x=>x.FamilyId,x=>x.SelectedVariant.FileName,StringComparer.OrdinalIgnoreCase);
        var entries=await _backend.ModelsAsync();
        _catalogModels.Clear();_catalogModels.AddRange(entries.Select(x=>new ModelItemView(x)));
        foreach(var item in _catalogModels)item.UpdateMemoryFit(_fitGpu);
        ProfileModels.Clear();foreach(var x in _catalogModels.Where(x=>x.Installed))ProfileModels.Add(x);
        RecommendedModels.Clear();
        foreach(var group in ModelRecommendations.Group(entries))
        {
            selectedVariants.TryGetValue(group[0].RecommendationKey,out var selectedFile);
            var variants=group.Select(x=>_catalogModels.Single(item=>item.FileName==x.FileName)).ToArray();
            RecommendedModels.Add(new(variants,selectedFile));
        }
        _activeModel=entries.FirstOrDefault(x=>x.Active&&x.Installed);_profileModel=entries.FirstOrDefault(x=>x.Installed&&x.FileName.Equals(previous,StringComparison.OrdinalIgnoreCase))??_activeModel??entries.FirstOrDefault(x=>x.Installed);ProfileModelPicker.ItemsSource=ProfileModels;ProfileModelPicker.SelectedItem=ProfileModels.FirstOrDefault(x=>x.FileName.Equals(_profileModel?.FileName,StringComparison.OrdinalIgnoreCase));ApplyModelFilter();LoadProfileControls();UpdateProfileSummary();BuildAdvancedSettings();ActiveModelMeta.Text=_activeModel is null?"No artifact selected":$"{ModelPresentation.WeightSummary(_activeModel.Weights,_activeModel.FileName)} · {_activeModel.SizeText}{(_activeModel.Vision?" · Vision":"")}";
    }
    private void ApplyModelFilter(){Models.Clear();foreach(var item in _catalogModels.Where(x=>x.Installed||x.Source.Missing))Models.Add(item);ModelsEmptyState.Visibility=Models.Count==0?Visibility.Visible:Visibility.Collapsed;ModelsList.Visibility=Models.Count==0?Visibility.Collapsed:Visibility.Visible;}
    private void ProfileModel_SelectionChanged(object sender,SelectionChangedEventArgs e){if(ProfileModelPicker.SelectedItem is not ModelItemView item)return;_profileModel=item.Source;LoadProfileControls();BuildAdvancedSettings();}
    private void UpdateProfileSummary()
    {
        UnloadPolicyText.Text=_settings?.AutoUnloadEnabled!=true?"Keep loaded":_settings.IdleMinutes==0?"After every request":$"After {_settings.IdleMinutes:0.#} minutes idle";
        EngineModelSupportText.Text=_activeModel is null?"No default model selected":_activeModel.NativeContext<=0?"Artifact capabilities unknown · choose an explicit conservative context":$"{ModelPresentation.WeightSummary(_activeModel.Weights,_activeModel.FileName)} · {(_activeModel.Vision?"Vision reported":"Text input")} · model limit {_activeModel.MaximumContext:N0} tokens";
        if(_activeModel is null){ActiveProfileText.Text="Recommended";ContextRingText.Text="—";ContextValue.Text="Choose a model";KvValue.Text="KV profile unavailable";return;}
        var p=_activeModel.RecommendedProfile;
        if(_settings?.Profiles.TryGetValue(_activeModel.FileName,out var saved)==true)p=saved;
        ContextRingText.Text=p.MaxContext>=1000?$"{p.MaxContext/1000d:0.#}K":p.MaxContext.ToString();
        ContextValue.Text=$"{p.MaxContext:N0} tokens{(p.AutoContext?" · Auto estimate ceiling":" · Explicit limit")}";
        KvValue.Text=$"{p.KvPrecision.ToString().ToUpperInvariant()} KV cache · next load";
        ActiveProfileText.Text=$"{p.KvPrecision.ToString().ToUpperInvariant()} · {p.SpeculativeMode}";
    }
    private async Task RefreshRequestsAsync()
    {
        try{var snapshot=await _backend.RequestsAsync();if(snapshot is null)return;var fingerprint=System.Text.Json.JsonSerializer.Serialize(snapshot.Requests);if(fingerprint!=_requestsFingerprint){_requestsFingerprint=fingerprint;Requests.Clear();foreach(var x in snapshot.Requests)Requests.Add(new(x));}CompletedValue.Text=snapshot.FailedCount>0?$"{snapshot.CompletedCount:N0} · {snapshot.FailedCount} failed":snapshot.CompletedCount.ToString("N0");AverageValue.Text=snapshot.AverageDecode is null?"—":$"{snapshot.AverageDecode:N1} tok/s";TtftValue.Text=snapshot.AverageTtft is null?"—":$"{snapshot.AverageTtft:N0} ms";TotalTokensValue.Text=snapshot.TotalTokens.ToString("N0");SpeedRingText.Text=snapshot.AverageDecode is null?"—":$"{snapshot.AverageDecode:N0}";SpeedRing.Value=snapshot.AverageDecode is null?0:Math.Clamp(snapshot.AverageDecode.Value/200d*100d,0,100);SpeedValue.Text=snapshot.AverageDecode is null?"No completed requests":$"{snapshot.AverageDecode:N1} tok/s";}catch{}
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
        ConverterPage.Visibility=tag=="converter"?Visibility.Visible:Visibility.Collapsed;
        RequestsPage.Visibility=tag=="requests"?Visibility.Visible:Visibility.Collapsed;
        LogsPage.Visibility=tag=="logs"?Visibility.Visible:Visibility.Collapsed;
        EnginePage.Visibility=tag=="engine"?Visibility.Visible:Visibility.Collapsed;
        SettingsPage.Visibility=tag=="settings"?Visibility.Visible:Visibility.Collapsed;
        TopPageTitle.Text=tag switch{"dashboard"=>"Overview","models"=>"Models","converter"=>"Converter","requests"=>"Requests","logs"=>"Logs","engine"=>"Engine","settings"=>"Settings",_=>"Overview"};
        UpdateNavigationSelection();
        if(tag=="requests")_ = RefreshRequestsAsync();
        if(tag=="converter")_ = OpenConverterAsync();
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
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button,button.Tag?.ToString() switch{"dashboard"=>"Overview","models"=>"Models","converter"=>"Converter","requests"=>"Requests","logs"=>"Logs","engine"=>"Engine","settings"=>"Settings",_=>"Navigate"});
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(button,selected?"Current page":"Open page");
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
    private Button[] SidebarButtons()=>[DashboardNavButton,ModelsNavButton,ConverterNavButton,RequestsNavButton,LogsNavButton,EngineNavButton,SettingsNavButton];
    private TextBlock[] SidebarLabels()=>[DashboardNavLabel,ModelsNavLabel,ConverterNavLabel,RequestsNavLabel,LogsNavLabel,EngineNavLabel,SettingsNavLabel];
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
            var operationEvent=$"{operation.OperationId}:{operation.State}";
            if(operation.State==EngineOperationState.Failed&&_lastOperationEvent!=operationEvent)ShowGlobal("Model could not load",operation.Error??operation.Message,InfoBarSeverity.Error);
            _lastOperationEvent=operationEvent;
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
            var selected=snapshot.Packages.FirstOrDefault(x=>x.Active);
            EnginePackageStateText.Text=snapshot.ActiveArchitecture is null?"ENGINE NOT SELECTED":"SELECTED PACKAGE";
            EngineHardwareSupportText.Text=selected is null?"No engine selected. Download a hardware-matched package below.":$"{FriendlyArchitecture(selected.CudaArchitecture)} · {selected.Qualification} · {(selected.NativeNvfp4?"Native NVFP4 hardware route":"Compatible quantized routes; no native NVFP4 promise")}";
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
            if(!progress.Running&&progress.Stage=="Ready"&&_settings is not null&&!_settings.FirstRunCompleted){await SavePreferencesAsync(new{firstRunCompleted=true});ShowGlobal("Engine installed","The package is ready. It will load model memory only when requested.",InfoBarSeverity.Success);}
            var progressEvent=$"{progress.Package}:{progress.Stage}:{progress.Error}";
            if(!progress.Running&&!string.IsNullOrWhiteSpace(progress.Error)&&_lastEngineEvent!=progressEvent)ShowInfoBar(EngineLibraryInfo,"Engine installation failed",progress.Error,InfoBarSeverity.Error);
            _lastEngineEvent=progressEvent;
            _lastEnginePackageStage=progress.Stage;
        }
        catch { }
    }
    private async void CancelEngineInstall_Click(object sender,RoutedEventArgs e){try{await _backend.CancelEngineInstallAsync();}catch(Exception ex){ShowGlobal("Could not cancel download",ex.Message,InfoBarSeverity.Error);}}
    private static string FriendlyArchitecture(string value)=>value.ToLowerInvariant() switch{"sm120a"=>"RTX 5000 Series (sm120a)","sm89"=>"RTX 4000 Series (sm89)","sm86"=>"RTX 3000 Series (sm86)",_=>value};
    private async void RefreshModels_Click(object sender,RoutedEventArgs e){try{await _backend.RefreshCatalogAsync();await RefreshModelsAsync();_ = CheckModelAnnouncementsAsync();ShowInfoBar(ModelsInfo,"Library refreshed","The Models folder and linked files were scanned without loading GPU memory.",InfoBarSeverity.Success);}catch(Exception ex){ShowModelsError(ex);}}
    private async void AddModel_Click(object sender,RoutedEventArgs e)
    {
        var picker=new Windows.Storage.Pickers.FileOpenPicker();picker.FileTypeFilter.Add(".ninfer");WinRT.Interop.InitializeWithWindow.Initialize(picker,_windowHandle);
        var file=await picker.PickSingleFileAsync();if(file is null)return;
        try{await _backend.LinkModelAsync(file.Path);await RefreshModelsAsync();await RefreshStatusAsync();ShowInfoBar(ModelsInfo,"Model linked","The file stays in its current location and is now ready to select.",InfoBarSeverity.Success);}catch(Exception ex){ShowModelsError(ex);}
    }
    private async void OpenModelsFolder_Click(object sender,RoutedEventArgs e){try{await _backend.OpenAsync("models");}catch(Exception ex){ShowModelsError(ex);}}
    private async void MarkModelSeen_Click(object sender,RoutedEventArgs e){if(sender is not FrameworkElement{Tag:string file})return;try{await _backend.MarkModelSeenAsync(file);await RefreshModelsAsync();}catch(Exception ex){ShowModelsError(ex);}}
    private async void ApiToggle_Click(object sender,RoutedEventArgs e){ApiToggleButton.IsEnabled=false;try{await _backend.SetApiEnabledAsync(!_apiEnabled);await RefreshStatusAsync();}catch(Exception ex){ShowGlobal("API state could not change",ex.Message,InfoBarSeverity.Error);}finally{ApiToggleButton.IsEnabled=true;}}
    private async void RelinkModel_Click(object sender,RoutedEventArgs e)
    {
        if(sender is not FrameworkElement{Tag:string file})return;
        try{var picker=new Windows.Storage.Pickers.FileOpenPicker();picker.FileTypeFilter.Add(".ninfer");WinRT.Interop.InitializeWithWindow.Initialize(picker,_windowHandle);var chosen=await picker.PickSingleFileAsync();if(chosen is null)return;await _backend.RelinkModelAsync(file,chosen.Path);_settings=await _backend.SettingsAsync();await RefreshModelsAsync();ShowInfoBar(ModelsInfo,"File location updated","The existing API name and profile were preserved.",InfoBarSeverity.Success);}catch(Exception ex){ShowModelsError(ex);}
    }
    private async void ImportPreset_Click(object sender,RoutedEventArgs e)
    {
        if(sender is not FrameworkElement{Tag:string file})return;
        try
        {
            var picker=new Windows.Storage.Pickers.FileOpenPicker();picker.FileTypeFilter.Add(".json");WinRT.Interop.InitializeWithWindow.Initialize(picker,_windowHandle);var chosen=await picker.PickSingleFileAsync();if(chosen is null)return;
            var dialog=new ContentDialog{Title="Import this model's settings?",Content="The preset is validated and saved for the next model load. Imported Exact settings are preserved, but the model's full SHA-256 is not calculated during import. Exact settings and verified model identity are different things.",PrimaryButtonText="Import settings",CloseButtonText="Cancel",DefaultButton=ContentDialogButton.Close,XamlRoot=Root.XamlRoot};if(await ShowDialogAsync(dialog)!=ContentDialogResult.Primary)return;
            var result=await _backend.ImportPresetAsync(file,chosen.Path);_settings=await _backend.SettingsAsync();await RefreshModelsAsync();ShowInfoBar(ModelsInfo,"Preset imported",result?.Message??"Settings saved for the next load. File identity has not been verified.",InfoBarSeverity.Warning);
        }
        catch(Exception ex){ShowModelsError(ex);}
    }
    private async void ExportPreset_Click(object sender,RoutedEventArgs e)
    {
        if(sender is not FrameworkElement{Tag:string file})return;
        try
        {
            var text=await _backend.ExportPresetAsync(file);var picker=new Windows.Storage.Pickers.FileSavePicker();picker.FileTypeChoices.Add("NInferEZ model preset",new List<string>{".json"});picker.SuggestedFileName=Path.GetFileNameWithoutExtension(file)+".ninferez";WinRT.Interop.InitializeWithWindow.Initialize(picker,_windowHandle);var chosen=await picker.PickSaveFileAsync();if(chosen is null)return;
            await FileIO.WriteTextAsync(chosen,text);ShowInfoBar(ModelsInfo,"Preset exported","Use with the matching artifact. Review any local file paths before sharing; model weights were not copied.",InfoBarSeverity.Success);
        }
        catch(Exception ex){ShowModelsError(ex);}
    }
    private async void EditModelApiName_Click(object sender,RoutedEventArgs e)
    {
        if(sender is not FrameworkElement{Tag:string file})return;
        var model=_catalogModels.FirstOrDefault(x=>x.FileName.Equals(file,StringComparison.OrdinalIgnoreCase));
        if(model is null)return;
        var input=new TextBox{Header="API model name",Text=model.ApiName,PlaceholderText=model.Source.ModelId,MaxLength=128,MinWidth=420};
        var content=new StackPanel{Spacing=8};content.Children.Add(input);content.Children.Add(new TextBlock{Text="Clients use this exact value in the model field. Clear it to restore the original name.",TextWrapping=TextWrapping.Wrap,Style=(Style)Application.Current.Resources["CaptionStyle"]});
        var dialog=new ContentDialog{Title=$"Edit {model.DisplayName}",Content=content,PrimaryButtonText="Save",CloseButtonText="Cancel",DefaultButton=ContentDialogButton.Primary,XamlRoot=Root.XamlRoot};
        if(await ShowDialogAsync(dialog)!=ContentDialogResult.Primary)return;
        try
        {
            var result=await _backend.SetModelAliasAsync(file,input.Text);
            _settings=await _backend.SettingsAsync();
            await RefreshModelsAsync();await RefreshStatusAsync();
            ShowInfoBar(ModelsInfo,"API name updated",result?.Message??"The model name was updated.",InfoBarSeverity.Success);
        }
        catch(Exception ex){ShowModelsError(ex);}
    }
    private async void DownloadModel_Click(object sender,RoutedEventArgs e)
    {
        if(sender is not FrameworkElement{Tag:string file})return;
        try{await _backend.DownloadAsync(file);await RefreshModelDownloadAsync();}
        catch(Exception ex){ShowModelsError(ex);}
    }
    private void OpenModelCard_Click(object sender,RoutedEventArgs e)
    {
        try{if(sender is FrameworkElement{Tag:string url}&&Uri.TryCreate(url,UriKind.Absolute,out var uri)&&uri.Scheme==Uri.UriSchemeHttps)Process.Start(new ProcessStartInfo(uri.ToString()){UseShellExecute=true});}
        catch(Exception ex){ShowGlobal("Could not open model page",ex.Message,InfoBarSeverity.Error);}
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
            var progressEvent=$"{progress.ModelFile}:{progress.Stage}:{progress.Error}";
            if(!progress.Running&&!string.IsNullOrWhiteSpace(progress.Error)&&_lastDownloadEvent!=progressEvent)ShowGlobal("Model download failed",progress.Error,InfoBarSeverity.Error);
            _lastDownloadEvent=progressEvent;
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
        if(await ShowDialogAsync(dialog)!=ContentDialogResult.Primary)return;
        try{await _backend.DeleteAsync(file);await RefreshModelsAsync();}catch(Exception ex){ShowModelsError(ex);}
    }
    private void CopyEndpoint_Click(object sender,RoutedEventArgs e){try{var package=new DataPackage();package.SetText(EndpointBox.Text);Clipboard.SetContent(package);ShowGlobal("Copied","API base URL copied to the clipboard.",InfoBarSeverity.Success);}catch(Exception ex){ShowGlobal("Could not copy endpoint",ex.Message,InfoBarSeverity.Error);}}
    private async void ThemePicker_SelectionChanged(object sender,SelectionChangedEventArgs e){if(_settings is null||_loadingSettings)return;try{var name=(ThemePicker.SelectedItem as ComboBoxItem)?.Tag?.ToString()??"System";var theme=Enum.Parse<ThemePreference>(name);Root.RequestedTheme=theme switch{ThemePreference.Light=>ElementTheme.Light,ThemePreference.Dark=>ElementTheme.Dark,_=>ElementTheme.Default};await SavePreferencesAsync(new{theme});UpdateNavigationSelection();}catch(Exception ex){ShowGlobal("Theme could not be saved",ex.Message,InfoBarSeverity.Error);}}
    private void ApplyTheme(){if(_settings is null)return;_loadingSettings=true;try{Root.RequestedTheme=_settings.Theme switch{ThemePreference.Light=>ElementTheme.Light,ThemePreference.Dark=>ElementTheme.Dark,_=>ElementTheme.Default};ThemePicker.SelectedIndex=(int)_settings.Theme;UpdateNavigationSelection();}finally{_loadingSettings=false;}}
    private async Task SavePreferencesAsync(object changes)
    {
        await _settingsWriteGate.WaitAsync();
        try
        {
            var latest=await _backend.SettingsAsync()??throw new InvalidOperationException("Settings are temporarily unavailable.");
            _settings=await _backend.PatchSettingsAsync(latest.Revision,changes)??latest;
        }
        finally{_settingsWriteGate.Release();}
    }
    private void LoadSettingsControls(){if(_settings is null)return;_loadingSettings=true;try{PublicPortBox.Value=_settings.PublicPort;IdleMinutesBox.Value=_settings.IdleMinutes>0?_settings.IdleMinutes:3;MemoryPolicyPicker.SelectedIndex=!_settings.AutoUnloadEnabled?2:_settings.IdleMinutes==0?1:0;IdleTimePanel.Visibility=MemoryPolicyPicker.SelectedIndex==0?Visibility.Visible:Visibility.Collapsed;LockPortBox.IsChecked=_settings.LockPublicPort;LocalNetworkBox.IsChecked=_settings.AllowLocalNetwork;ApiKeyBox.Password=_settings.ApiKey;StartWithWindowsBox.IsChecked=SystemIntegration.IsStartupEnabled();StartMinimizedBox.IsChecked=_settings.StartMinimized;CloseToTrayBox.IsChecked=_settings.CloseToTray;AutoUpdatesBox.IsChecked=_settings.AutoCheckUpdates;AutoCatalogBox.IsChecked=_settings.AutoCheckCatalog;AutoEnginesBox.IsChecked=_settings.AutoCheckEngines;CorsBox.IsChecked=_settings.CorsEnabled;}finally{_loadingSettings=false;}}
    private void MemoryPolicy_Changed(object sender,SelectionChangedEventArgs e){if(IdleTimePanel is null)return;IdleTimePanel.Visibility=MemoryPolicyPicker.SelectedIndex==0?Visibility.Visible:Visibility.Collapsed;ApplicationPreference_Changed(sender,new RoutedEventArgs());}
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
            if(!double.IsFinite(IdleMinutesBox.Value)||IdleMinutesBox.Value<.1)throw new InvalidOperationException("Enter an idle time between 0.1 and 1440 minutes.");
            var startWithWindows=StartWithWindowsBox.IsChecked==true;
            await SavePreferencesAsync(new{idleMinutes=MemoryPolicyPicker.SelectedIndex==1?0:IdleMinutesBox.Value,autoUnloadEnabled=MemoryPolicyPicker.SelectedIndex!=2,startWithWindows,startMinimized=StartMinimizedBox.IsChecked==true,closeToTray=CloseToTrayBox.IsChecked==true,autoCheckUpdates=AutoUpdatesBox.IsChecked==true,autoCheckCatalog=AutoCatalogBox.IsChecked==true,autoCheckEngines=AutoEnginesBox.IsChecked==true});
            SystemIntegration.SetStartupEnabled(startWithWindows);UpdateProfileSummary();
        }
        catch(Exception ex){ShowGlobal("Preference could not be saved",ex.Message,InfoBarSeverity.Error);}
    }
    private void LoadProfileControls()
    {
        if(_profileModel is null){ProfileEditor.Visibility=Visibility.Collapsed;NoInstalledProfile.Visibility=Visibility.Visible;return;}
        _loadingProfile=true;
        try
        {
            ProfileEditor.Visibility=Visibility.Visible;NoInstalledProfile.Visibility=Visibility.Collapsed;
            var p=_profileModel.RecommendedProfile;if(_settings?.Profiles.TryGetValue(_profileModel.FileName,out var saved)==true)p=saved;
            _editingProfile=ProfileSettingsPolicy.Clone(p);
            if(p.ManageAdvancedSettings)_editingProfile=ProfileSettingsPolicy.Managed(p,_profileModel.RecommendedProfile);
            p=_editingProfile;ManageAdvancedBox.IsChecked=p.ManageAdvancedSettings;
            AutoContextBox.IsChecked=p.AutoContext;ContextBox.Maximum=Math.Max(1024,_profileModel.MaximumContext);ContextBox.Value=p.MaxContext;PrefillBox.Value=p.PrefillChunk;KvBox.SelectedIndex=(int)p.KvPrecision;
            RefreshSpeculativeChoices();
            DraftBox.Value=p.DraftTokens;ConcurrencyBox.Value=1;VisionCapabilityPanel.Visibility=_profileModel.Vision?Visibility.Visible:Visibility.Collapsed;VisionBox.IsChecked=_profileModel.Vision&&p.VisionEnabled;CudaBox.IsChecked=p.CudaGraphEnabled;PrefixBox.IsChecked=p.PrefixReuseEnabled;
        }
        finally{_loadingProfile=false;}
        UpdateSpeculativeControls();
    }
    private void SpeculativeMode_Changed(object sender,SelectionChangedEventArgs e)=>UpdateProfileDependencies();
    private void BasicProfile_Changed(object sender,RoutedEventArgs e)=>UpdateProfileDependencies();
    private void BasicProfileSelection_Changed(object sender,SelectionChangedEventArgs e)=>UpdateProfileDependencies();
    private void BasicProfileNumber_Changed(NumberBox sender,NumberBoxValueChangedEventArgs e)=>UpdateProfileDependencies();
    private void Management_Changed(object sender,RoutedEventArgs e)
    {
        if(_loadingProfile||_editingProfile is null||_profileModel is null)return;
        try
        {
            var selected=ReadProfileControls(!_editingProfile.ManageAdvancedSettings);
            selected.ManageAdvancedSettings=_editingProfile.ManageAdvancedSettings;
            _editingProfile=ProfileSettingsPolicy.SwitchManagement(selected,ManageAdvancedBox.IsChecked==true,_profileModel.RecommendedProfile);
            _loadingProfile=true;
            try{RefreshSpeculativeChoices();}finally{_loadingProfile=false;}
            RefreshAdvancedValues();UpdateSpeculativeControls();
        }
        catch(Exception ex)
        {
            _loadingProfile=true;ManageAdvancedBox.IsChecked=_editingProfile.ManageAdvancedSettings;_loadingProfile=false;
            ShowInfoBar(SettingsInfo,"Check the current profile",ex.Message,InfoBarSeverity.Warning);
        }
    }
    private void RefreshSpeculativeChoices()
    {
        if(_profileModel is null||_editingProfile is null)return;
        var p=_editingProfile;
        var choices=SpeculativeChoices.ForModel(_profileModel,p.SpeculativeMode,p.ManageAdvancedSettings);
        var selected=choices.FirstOrDefault(x=>x.Mode==p.SpeculativeMode)??choices[0];
        if(selected.Mode!=p.SpeculativeMode)
        {
            p.SpeculativeMode=selected.Mode;
            _editingProfile=ProfileSettingsPolicy.Managed(p,_profileModel.RecommendedProfile);
            ShowInfoBar(SettingsInfo,"Acceleration unavailable","The saved acceleration mode is not included in this model. Disabled is selected in the editor; Save profile to apply. Manual settings remain backed up.",InfoBarSeverity.Warning);
        }
        SpecBox.ItemsSource=choices;SpecBox.SelectedItem=selected;
    }
    private ModelProfile ReadProfileControls(bool readAdvanced)
    {
        var p=ProfileSettingsPolicy.Clone(_editingProfile??throw new InvalidOperationException("Choose a model profile first."));
        p.AutoContext=AutoContextBox.IsChecked==true;p.MaxContext=ReadWholeNumber(ContextBox,"Context limit");
        p.KvPrecision=(KvPrecision)KvBox.SelectedIndex;p.SpeculativeMode=(SpecBox.SelectedItem as SpeculativeChoice)?.Mode??throw new InvalidOperationException("Choose an acceleration mode.");
        p.VisionEnabled=_profileModel?.Vision==true&&VisionBox.IsChecked==true;p.MaxConcurrency=1;
        if(readAdvanced)
        {
            p.PrefillChunk=ReadWholeNumber(PrefillBox,"Prompt processing batch");
            if(DraftBox.IsEnabled)p.DraftTokens=ReadWholeNumber(DraftBox,"Draft tokens");
            p.CudaGraphEnabled=CudaBox.IsChecked==true;p.PrefixReuseEnabled=PrefixBox.IsChecked==true;
            ApplyAdvancedValues(p,_advancedProfileControls);
        }
        p.ManageAdvancedSettings=ManageAdvancedBox.IsChecked==true;
        return p;
    }
    private void UpdateProfileDependencies()
    {
        if(_loadingProfile||_editingProfile is null||_profileModel is null)return;
        try
        {
            var p=ReadProfileControls(!_editingProfile.ManageAdvancedSettings);
            if(p.SpeculativeMode==SpeculativeMode.Mtp)p.DraftTokens=Math.Clamp(p.DraftTokens,1,5);
            _editingProfile=p.ManageAdvancedSettings?ProfileSettingsPolicy.Managed(p,_profileModel.RecommendedProfile):p;
            RefreshAdvancedValues();UpdateSpeculativeControls();
        }
        catch(InvalidOperationException){ /* Incomplete numeric edits are checked when saved. */ }
    }
    private void RefreshAdvancedValues()
    {
        if(_editingProfile is null)return;
        var p=ProfileSettingsPolicy.Effective(_editingProfile);
        _loadingProfile=true;
        try
        {
            PrefillBox.Value=p.PrefillChunk;DraftBox.Maximum=p.SpeculativeMode==SpeculativeMode.Mtp?5:15;DraftBox.Value=Math.Clamp(p.DraftTokens,1,(int)DraftBox.Maximum);
            CudaBox.IsChecked=p.CudaGraphEnabled;PrefixBox.IsChecked=p.PrefixReuseEnabled;
            foreach(var (descriptor,input) in _advancedProfileControls)SetPropertyControl(input,typeof(ModelProfile).GetProperty(descriptor.Property)!.GetValue(p));
        }
        finally{_loadingProfile=false;}
    }
    private void UpdateSpeculativeControls()
    {
        if(_profileModel is null||SpecBox.SelectedItem is not SpeculativeChoice choice||DraftControlsPanel is null)return;
        var p=_editingProfile??_profileModel.RecommendedProfile;var managed=ManageAdvancedBox.IsChecked==true;
        DraftControlsPanel.Visibility=Visibility.Visible;DraftBox.IsEnabled=!managed&&choice.Mode!=SpeculativeMode.Disabled;
        PrefillBox.IsEnabled=CudaBox.IsEnabled=PrefixBox.IsEnabled=!managed;
        ContextBox.IsEnabled=AutoContextBox.IsChecked!=true;
        ManagementHint.Text=managed?"Managed: advanced values follow your basic choices. Your manual setup is kept separately; turn this off to restore it. Save profile to apply.":"Manual: your last advanced values are restored. Grey values are inactive and are not sent as incompatible engine flags. Save profile to apply.";
        SpecCapabilityText.Text=!_profileModel.Inspected?"Acceleration metadata is unknown. The existing setting is preserved; inspect with a compatible engine before choosing another mode.":!choice.ConfirmedSupported?"The imported setting is preserved, but this artifact does not report the required component. Choose a supported mode before loading.":"Only acceleration modes reported by this artifact are offered.";
        foreach(var (descriptor,input) in _advancedProfileControls)
            input.IsEnabled=!managed&&ProfileSettingsPolicy.IsApplicable(descriptor.Property,p,_profileModel.Vision);
    }
    private async void SaveSettings_Click(object sender,RoutedEventArgs e)
    {
        if(_settings is null||_profileModel is null)return;
        try
        {
            var model=_profileModel;
            var p=ReadProfileControls(ManageAdvancedBox.IsChecked!=true);
            if(p.ManageAdvancedSettings)p=ProfileSettingsPolicy.Managed(p,model.RecommendedProfile);
            else p.ManualAdvancedSettings=ProfileSettingsPolicy.CaptureAdvanced(p);
            await SavePreferencesAsync(new{profiles=new Dictionary<string,ModelProfile>{{model.FileName,p}}});
            await RefreshModelsAsync();ShowInfoBar(SettingsInfo,"Profile saved","Applies on the next load. The running model and its numerical settings have not changed.",InfoBarSeverity.Success);
        }
        catch(Exception ex){ShowInfoBar(SettingsInfo,"Could not save",ex.Message,InfoBarSeverity.Error);}
    }
    private async void RestoreProfile_Click(object sender,RoutedEventArgs e){if(_settings is null||_profileModel is null)return;try{await SavePreferencesAsync(new{profiles=new Dictionary<string,ModelProfile>{{_profileModel.FileName,_profileModel.RecommendedProfile}}});LoadProfileControls();BuildAdvancedSettings();ShowGlobal("Profile restored","Recommended settings will apply on the next model load.",InfoBarSeverity.Success);}catch(Exception ex){ShowGlobal("Profile could not be restored",ex.Message,InfoBarSeverity.Error);}}
    private static int ReadWholeNumber(NumberBox input,string label){if(!double.IsFinite(input.Value)||input.Value!=Math.Round(input.Value)||input.Value<input.Minimum||input.Value>input.Maximum)throw new InvalidOperationException($"{label}: enter a whole number between {input.Minimum:N0} and {input.Maximum:N0}.");return checked((int)input.Value);}
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
            await RestartPortAsync(true);
        }
        catch(Exception ex){ShowGlobal("API settings could not be applied",ex.Message,InfoBarSeverity.Error);}
    }
    private async Task RestartPortAsync(bool save)
    {
        try{var port=ReadWholeNumber(PublicPortBox,"Public API port");if(LocalNetworkBox.IsChecked==true&&string.IsNullOrWhiteSpace(ApiKeyBox.Password))throw new InvalidOperationException("Set an API key before allowing network access. Binding all interfaces also includes VPN interfaces.");var current=await _backend.SettingsAsync();var result=await _backend.RestartPortAsync(port,LockPortBox.IsChecked==true,LocalNetworkBox.IsChecked==true,save,ApiKeyBox.Password,CorsBox.IsChecked==true,current?.Revision);_settings=await _backend.SettingsAsync();ShowGlobal("API settings applied",result?.Message??"The selected endpoint is active.",InfoBarSeverity.Success);await RefreshStatusAsync();}
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
        var result=await ShowDialogAsync(dialog);
        if(result!=ContentDialogResult.Primary)return;
        if(needsEngine)
        {
            if(enginePicker.SelectedIndex<0||enginePicker.SelectedIndex>=availableEngines.Count){ShowGlobal("Choose an engine","Select a compatible engine package to continue.",InfoBarSeverity.Warning);return;}
            var selected=availableEngines[enginePicker.SelectedIndex];
            if(selected.Installed){await _backend.ActivateEngineAsync(selected.EngineVersion,selected.CudaArchitecture);await SavePreferencesAsync(new{firstRunCompleted=true});}
            else{await _backend.InstallEngineAsync(selected.EngineVersion,selected.CudaArchitecture);NavigateToPage("engine");}
        }
        else{await SavePreferencesAsync(new{firstRunCompleted=true});}
        PublicPortBox.Value=port.Value;await RestartPortAsync(true);
        if(modelPicker.SelectedItem is ModelItemView model)
        {
            await _backend.ActivateAsync(model.FileName);
            await RefreshModelsAsync();await RefreshStatusAsync();
        }
    }

    private async Task CheckForUpdatesAsync(bool interactive)
    {
        if(_checkingUpdates){if(interactive)ShowGlobal("Update already in progress","Wait for the current check or download to finish.",InfoBarSeverity.Informational);return;}
        _checkingUpdates=true;
        try
        {
            var update=await _backend.CheckUpdateAsync();if(update is null)return;
            if(!update.UpdateAvailable){if(interactive)ShowGlobal("Up to date",update.Message,InfoBarSeverity.Success);return;}
            if(string.IsNullOrWhiteSpace(update.AssetUrl)){ShowGlobal("Update available",update.Message,InfoBarSeverity.Warning);if(interactive&&update.ReleaseUrl is not null)Process.Start(new ProcessStartInfo(update.ReleaseUrl){UseShellExecute=true});return;}
            ShowGlobal("Update available",update.Message,InfoBarSeverity.Informational);
            if(!interactive)return;
            var dialog=new ContentDialog{Title=$"Install NInferEZ Manager {update.LatestVersion}?",Content="The package will be downloaded and verified with SHA-256 before installation.",PrimaryButtonText="Download and install",SecondaryButtonText="View release",CloseButtonText="Later",DefaultButton=ContentDialogButton.Primary,XamlRoot=Root.XamlRoot};
            var choice=await ShowDialogAsync(dialog);if(choice==ContentDialogResult.Secondary&&update.ReleaseUrl is not null){Process.Start(new ProcessStartInfo(update.ReleaseUrl){UseShellExecute=true});return;}if(choice!=ContentDialogResult.Primary)return;
            await _backend.DownloadUpdateAsync(update);
            ApplicationUpdatePanel.Visibility=Visibility.Visible;
            var deadline=System.Diagnostics.Stopwatch.StartNew();
            while(!_closing){await Task.Delay(500);var progress=await _backend.UpdateProgressAsync();if(progress is null){if(deadline.Elapsed>TimeSpan.FromSeconds(30))throw new InvalidOperationException("The updater did not report progress. No application files were replaced.");continue;}ApplicationUpdateText.Text=$"{progress.Stage} · {progress.Completed/1024d/1024d:0} MiB";ApplicationUpdateProgress.IsIndeterminate=progress.Total<=0;if(progress.Total>0)ApplicationUpdateProgress.Value=Math.Clamp(progress.Completed*100d/progress.Total,0,100);if(!progress.Running){if(progress.Error is not null)throw new InvalidOperationException(progress.Error);break;}}
            if(_closing)return;
            await _backend.ApplyUpdateAsync();_backend.ReleaseOwnershipForUpdate();_realExit=true;await Task.Delay(250);Close();
        }
        catch(Exception ex){if(interactive)ShowGlobal("Update failed",ex.Message,InfoBarSeverity.Error);}
        finally{_checkingUpdates=false;ApplicationUpdatePanel.Visibility=Visibility.Collapsed;}
    }
    private async void CancelApplicationUpdate_Click(object sender,RoutedEventArgs e){try{await _backend.CancelUpdateAsync();}catch(Exception ex){ShowGlobal("Could not cancel update",ex.Message,InfoBarSeverity.Error);}}

    private void BuildAdvancedSettings()
    {
        if(_settings is null)return;
        AdvancedAppPanel.Children.Clear();AdvancedProfilePanel.Children.Clear();_advancedAppControls.Clear();_advancedProfileControls.Clear();
        AddPropertyControls(AdvancedAppPanel,_settings,_advancedAppControls,SettingDescriptors.Application);
        var apply=new Button{Content="Save advanced preferences",HorizontalAlignment=HorizontalAlignment.Left};apply.Click+=SaveAdvancedPreferences_Click;AdvancedAppPanel.Children.Add(apply);
        if(_profileModel is null){AdvancedProfilePanel.Children.Add(new TextBlock{Text="Choose a model profile to edit its complete settings.",TextWrapping=TextWrapping.Wrap});return;}
        var profile=_editingProfile??(_settings.Profiles.TryGetValue(_profileModel.FileName,out var saved)?saved:_profileModel.RecommendedProfile);
        var descriptors=SettingDescriptors.Profile.Where(x=>!x.VisionOnly||_profileModel.Vision);
        AddPropertyControls(AdvancedProfilePanel,profile,_advancedProfileControls,descriptors);
        foreach(var input in _advancedProfileControls.Values)
        {
            if(input is CheckBox check){check.Checked+=BasicProfile_Changed;check.Unchecked+=BasicProfile_Changed;}
            else if(input is ComboBox combo)combo.SelectionChanged+=BasicProfileSelection_Changed;
            else if(input is NumberBox number&&input==_advancedProfileControls.FirstOrDefault(x=>x.Key.Property==nameof(ModelProfile.HostCacheMiB)).Value)number.ValueChanged+=BasicProfileNumber_Changed;
        }
        RefreshAdvancedValues();
        UpdateSpeculativeControls();
    }
    private static void AddPropertyControls(StackPanel panel,object source,Dictionary<SettingDescriptor,Control> map,IEnumerable<SettingDescriptor> descriptors)
    {
        string? group=null;
        StackPanel groupPanel=panel;
        foreach(var descriptor in descriptors.GroupBy(x=>x.Group).SelectMany(x=>x))
        {
            var property=source.GetType().GetProperty(descriptor.Property)??throw new InvalidOperationException($"Unknown setting descriptor: {descriptor.Property}");
            if(group!=descriptor.Group)
            {
                group=descriptor.Group;groupPanel=new StackPanel{Spacing=14};
                panel.Children.Add(new Expander{Header=group,Content=groupPanel,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,IsExpanded=group=="Context and memory"||source is ManagerSettings});
            }
            var value=property.GetValue(source);Control input;
            var actual=Nullable.GetUnderlyingType(property.PropertyType)??property.PropertyType;
            if(actual==typeof(bool))input=new CheckBox{IsChecked=(bool?)value??false};
            else if(actual.IsEnum){var combo=new ComboBox{ItemsSource=Enum.GetValues(actual),HorizontalAlignment=HorizontalAlignment.Stretch};combo.SelectedItem=value;input=combo;}
            else if(actual==typeof(int)||actual==typeof(double)){input=new NumberBox{Minimum=descriptor.Minimum,Maximum=descriptor.Maximum,Value=value is null?double.NaN:Convert.ToDouble(value),PlaceholderText=Nullable.GetUnderlyingType(property.PropertyType) is not null?"Engine default":string.Empty,SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Compact,HorizontalAlignment=HorizontalAlignment.Stretch};}
            else input=new TextBox{Text=value?.ToString()??"",HorizontalAlignment=HorizontalAlignment.Stretch};
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(input,descriptor.Label);
            ToolTipService.SetToolTip(input,descriptor.Description);
            var card=new StackPanel{Spacing=6};card.Children.Add(HelpLabel(descriptor.Label,descriptor.Description));card.Children.Add(input);groupPanel.Children.Add(card);map[descriptor]=input;
        }
    }
    private static void ApplyAdvancedValues(object target,Dictionary<SettingDescriptor,Control> controls)
    {
        foreach(var (descriptor,control) in controls)
        {
            if(!control.IsEnabled)continue;
            var property=target.GetType().GetProperty(descriptor.Property)!;
            var nullable=Nullable.GetUnderlyingType(property.PropertyType);var actual=nullable??property.PropertyType;object? value;
            if(control is CheckBox check)value=check.IsChecked==true;
            else if(control is ComboBox combo)value=combo.SelectedItem;
            else if(control is NumberBox number)
            {
                if(double.IsNaN(number.Value)&&nullable is not null)value=null;
                else if(!double.IsFinite(number.Value)||number.Value<descriptor.Minimum||number.Value>descriptor.Maximum)throw new InvalidOperationException($"{descriptor.Label}: enter a value between {descriptor.Minimum} and {descriptor.Maximum}.");
                // Cast each branch to object explicitly. Without this, C# promotes the
                // int branch of the conditional expression back to double before boxing,
                // and PropertyInfo.SetValue cannot assign that Double to Int32/Int32?.
                else if(actual==typeof(int)){if(number.Value!=Math.Round(number.Value))throw new InvalidOperationException($"{descriptor.Label} requires a whole number.");value=(object)checked((int)number.Value);}
                else value=(object)number.Value;
            }
            else value=((TextBox)control).Text;
            property.SetValue(target,value);
        }
    }
    private static void SetPropertyControl(Control control,object? value)
    {
        if(control is CheckBox check)check.IsChecked=(bool?)value??false;
        else if(control is ComboBox combo)combo.SelectedItem=value;
        else if(control is NumberBox number)number.Value=value is null?double.NaN:Convert.ToDouble(value);
        else if(control is TextBox text)text.Text=value?.ToString()??string.Empty;
    }
    private static Grid HelpLabel(string label,string description)
    {
        var header=new Grid{ColumnSpacing=8};header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        header.Children.Add(new TextBlock{Text=label,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold});
        var help=HelpIcon(label,description);
        Grid.SetColumn(help,1);header.Children.Add(help);return header;
    }
    private static Border HelpIcon(string label,string description)
    {
        var help=new Border{Width=18,Height=18,CornerRadius=new CornerRadius(9),BorderThickness=new Thickness(1),
            Background=new SolidColorBrush(Colors.Transparent),BorderBrush=new SolidColorBrush(Colors.SlateGray),VerticalAlignment=VerticalAlignment.Center,
            Child=new TextBlock{Text="?",FontSize=11,Foreground=new SolidColorBrush(Colors.SlateGray),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(help,$"Help: {label}. {description}");
        ToolTipService.SetToolTip(help,new ToolTip{Content=new TextBlock{Text=description,TextWrapping=TextWrapping.Wrap,MaxWidth=300}});
        help.Tapped+=(_,e)=>e.Handled=true;
        return help;
    }
    private static void AddControlHelp(Control control,string description)
    {
        ToolTipService.SetToolTip(control,description);
        if(control.Parent is StackPanel panel&&panel.Children.IndexOf(control)==1&&panel.Children.FirstOrDefault() is TextBlock label)
        { panel.Children.Remove(label);panel.Children.Insert(0,HelpLabel(label.Text,description)); }
        else if(control is CheckBox check&&check.Content is string text)
        {
            var content=new StackPanel{Orientation=Orientation.Horizontal,Spacing=7};content.Children.Add(new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap});
            content.Children.Add(HelpIcon(text,description));check.Content=content;
        }
        else if(control is ComboBox combo&&combo.Header is string comboLabel)combo.Header=HelpLabel(comboLabel,description);
        else if(control is PasswordBox password&&password.Header is string passwordLabel)password.Header=HelpLabel(passwordLabel,description);
    }
    private void InstallSettingsHelp()
    {
        AddControlHelp(ManageAdvancedBox,"Recommended for everyday use. The app adjusts dependent advanced values to your context, cache quality and acceleration choice. Switching this off restores your last manual advanced setup; Save profile applies it.");
        AddControlHelp(AutoContextBox,"Managed: the engine sizes its context cache using free VRAM after loading the model. Manual: the catalog estimates a context size. Neither changes the model weights or your selected cache quality; some models still need more memory than your GPU has.");
        AddControlHelp(ContextBox,"How much text a request can remember, including the answer. More tokens need more memory. This is an upper limit, not the amount currently loaded. Automatic fitting can allocate less.");
        AddControlHelp(KvBox,"Precision of the remembered context, not model weights. Higher precision uses more VRAM. Keep the recommended setting for the model unless you have checked quality and engine compatibility.");
        AddControlHelp(SpecBox,"Predicts several tokens ahead to speed generation. MTP and DFlash need matching components in the model. Disabled turns drafting off; its dependent controls become inactive.");
        AddControlHelp(VisionBox,"Allows image input only if the model and engine include vision support. Image processing also needs memory.");
        AddControlHelp(PrefillBox,"Number of prompt tokens processed in one batch. Larger batches can process input faster but need more working memory.");
        AddControlHelp(DraftBox,"How many future tokens the accelerator proposes at once. Larger is not always faster. Managed mode uses the recommended value.");
        AddControlHelp(ConcurrencyBox,"This app currently runs one generation request at a time. Extra requests wait in the queue.");
        AddControlHelp(CudaBox,"Reuses GPU execution plans to reduce overhead. Keep the recommended value unless diagnosing an engine issue.");
        AddControlHelp(PrefixBox,"Reuses matching text from earlier requests so repeated prompts can start faster. Cached state consumes memory.");
        AddControlHelp(PublicPortBox,"The port your API clients connect to. The default is 8173. Apply API changes restarts the listener, not the model.");
        AddControlHelp(LockPortBox,"If another app uses this port, report a conflict instead of choosing a different port.");
        AddControlHelp(ApiKeyBox,"An optional password for API clients. Required when network access is enabled. Never share it in screenshots or diagnostics.");
        AddControlHelp(CorsBox,"Allows browser-based clients to call the API. Desktop API clients normally do not need this.");
        AddControlHelp(LocalNetworkBox,"Allows other computers or VPN peers to reach the API if firewall rules permit. Requires an API key. It does not configure your firewall or VPN.");
        AddControlHelp(MemoryPolicyPicker,"Choose when the loaded model leaves GPU memory. Unloading saves VRAM but the next request must load the model again.");
        AddControlHelp(IdleMinutesBox,"How long to keep the model after the last finished request. The default is three minutes; active requests are not interrupted.");
        AddControlHelp(StartWithWindowsBox,"Opens Manager when you sign in to Windows. It does not load a model until a request needs it.");
        AddControlHelp(StartMinimizedBox,"Starts quietly in the notification area instead of opening the main window.");
        AddControlHelp(CloseToTrayBox,"Closing the window keeps the API available in the notification area. Use Exit to stop the application completely.");
        AddControlHelp(AutoUpdatesBox,"Checks for application releases. You still choose whether to download and install an update.");
        AddControlHelp(AutoCatalogBox,"Refreshes model recommendations. It does not automatically download model weights.");
        AddControlHelp(AutoEnginesBox,"Refreshes available GPU engine packages. It does not replace a running engine automatically.");
    }
    private async void SaveAdvancedPreferences_Click(object sender,RoutedEventArgs e)
    {
        if(_settings is null)return;
        try{var copy=System.Text.Json.JsonSerializer.Deserialize<ManagerSettings>(System.Text.Json.JsonSerializer.Serialize(_settings))!;ApplyAdvancedValues(copy,_advancedAppControls);var changes=_advancedAppControls.Keys.ToDictionary(x=>char.ToLowerInvariant(x.Property[0])+x.Property[1..],x=>typeof(ManagerSettings).GetProperty(x.Property)!.GetValue(copy));await SavePreferencesAsync(changes);ShowInfoBar(SettingsInfo,"Preferences saved","Catalog check intervals apply to future checks.",InfoBarSeverity.Success);}
        catch(Exception ex){ShowInfoBar(SettingsInfo,"Could not save",ex.Message,InfoBarSeverity.Error);}
    }

    private async Task SetIdleFromTrayAsync(double? minutes)
    {
        if(_settings is null)return;try{await SavePreferencesAsync(new{autoUnloadEnabled=minutes is not null,idleMinutes=minutes??_settings.IdleMinutes});LoadSettingsControls();UpdateProfileSummary();}catch(Exception ex){ShowGlobal("Idle policy could not be saved",ex.Message,InfoBarSeverity.Error);}
    }
    private void OnWindowClosing(AppWindow sender,AppWindowClosingEventArgs args)
    {
        if(_realExit)return;args.Cancel=true;if(_tray is null||_settings?.CloseToTray==false)ExitCompletely();else MinimizeToTray();
    }
    private void MinimizeToTray()
    {
        if(_hiddenToTray)return;if(_tray is null){ShowGlobal("Notification area unavailable","The window remains open so you can restore or exit the application safely.",InfoBarSeverity.Warning);return;}_hiddenToTray=true;_timer.Stop();_catalogNotificationTimer.Stop();_converterTimer.Stop();SystemIntegration.HideWindow(_windowHandle);
    }
    private void RestoreFromTray()
    {
        if(!_hiddenToTray)return;_hiddenToTray=false;SystemIntegration.ShowWindow(_windowHandle);_timer.Start();_catalogNotificationTimer.Start();_ = ReloadVisibleAsync();_ = CheckModelAnnouncementsAsync();if(_conversionStatus?.Running==true||_activePage=="converter")_ = PollConverterAsync();
    }
    public void ActivateFromSecondaryInstance()
    {
        if(_hiddenToTray)RestoreFromTray();
        else SystemIntegration.ShowWindow(_windowHandle);
        Activate();
    }
    private async Task ReloadVisibleAsync(){await RefreshVisibleAsync();}
    private async void ExitCompletely()
    {
        if(_exitPromptOpen||_closing)return;
        _exitPromptOpen=true;
        try
        {
            ManagerStatus? status=null;
            try{status=await _backend.StatusAsync().WaitAsync(TimeSpan.FromSeconds(2));}catch{}
            if(status is not null && !await ConfirmConverterExitAsync())return;
            if(status?.ActiveRequests>0||status is null&&_engineState==EngineState.Ready)
            {
                if(_hiddenToTray)RestoreFromTray();
                var dialog=new ContentDialog{Title=status is null?"Exit without checking active requests?":"Exit while a request is running?",Content="Exiting closes this application's backend and cancels any active generation. To keep requests running, leave the application in the notification area.",PrimaryButtonText="Exit and cancel request",CloseButtonText="Keep running",DefaultButton=ContentDialogButton.Close,XamlRoot=Root.XamlRoot};
                if(await ShowDialogAsync(dialog)!=ContentDialogResult.Primary)return;
            }
            _realExit=true;Close();
        }
        catch(Exception ex){ShowGlobal("Could not exit safely",ex.Message,InfoBarSeverity.Error);}
        finally{_exitPromptOpen=false;}
    }
    private void ConfirmPortableUpdateLaunch()=>InspectPortableUpdateJournal(true);
    private bool InspectPortableUpdateJournal(bool confirmLaunch)
    {
        var directory=Path.GetDirectoryName(Environment.ProcessPath)!;
        var recovery=Path.Combine(directory,".update-recovery");
        try
        {
            if(!File.Exists(Path.Combine(directory,"portable.mode"))||!File.Exists(Path.Combine(recovery,"journal.json")))return true;
            var journalInfo=new FileInfo(Path.Combine(recovery,"journal.json"));if(journalInfo.Length>16384)throw new IOException("Update recovery metadata exceeds its limit; it was preserved.");
            using var journal=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(recovery,"journal.json")));
            string? state=null,target=null;var schema=0;
            foreach(var property in journal.RootElement.EnumerateObject())
            {
                if(property.Name.Equals("state",StringComparison.OrdinalIgnoreCase))state=property.Value.GetString();
                if(property.Name.Equals("target",StringComparison.OrdinalIgnoreCase))target=property.Value.GetString();
                if(property.Name.Equals("schemaVersion",StringComparison.OrdinalIgnoreCase)&&property.Value.TryGetInt32(out var version))schema=version;
            }
            if(schema!=1||target is null||!Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar).Equals(directory.TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase))throw new IOException("Update recovery metadata does not match this application; it was preserved.");
            if(state=="AwaitingConfirmation"&&confirmLaunch)File.WriteAllText(Path.Combine(recovery,"launch-ack"),DateTimeOffset.UtcNow.ToString("O"));
            else if(state is "Applying" or "Prepared" or "RolledBack")
            {
                ShowGlobal("Update recovery available","An earlier update did not complete. Your model files and settings are preserved. Close this application, then run .update-recovery/recover.exe --recover-portable-update --target with this application's folder. Do not delete the recovery folder before recovery.",InfoBarSeverity.Warning);
                if(state=="Applying"){TopApiStatusText.Text="Recovery required";EngineHeadline.Text="An update needs recovery.";ApiAvailabilityText.Text="Application startup paused · previous files are preserved";EngineSubheadline.Text="Restore the previous application files before starting the backend.";ApiToggleButton.IsEnabled=false;EnginePrimaryButton.IsEnabled=false;EnginePagePrimaryButton.IsEnabled=false;return false;}
            }
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException){ShowGlobal("Update confirmation pending",ex.Message,InfoBarSeverity.Warning);}
        catch(System.Text.Json.JsonException){ShowGlobal("Update journal unreadable","The previous update could not be confirmed. Do not delete .update-recovery until recovery is complete.",InfoBarSeverity.Warning);}
        return true;
    }
    private void ShowInfoBar(InfoBar infoBar,string title,string message,InfoBarSeverity severity){if(infoBar.IsOpen&&infoBar.Title==title&&infoBar.Message==message&&infoBar.Severity==severity)return;infoBar.Title=title;infoBar.Message=message;infoBar.Severity=severity;infoBar.IsOpen=true;ScheduleInfoBarDismissal(infoBar);}
    private void ShowModelsError(Exception ex)=>ShowInfoBar(ModelsInfo,"Model action failed",ex.Message,InfoBarSeverity.Error);
    private void ShowGlobal(string title,string message,InfoBarSeverity severity)=>ShowInfoBar(GlobalInfo,title,message,severity);
}

public sealed class ModelItemView : System.ComponentModel.INotifyPropertyChanged
{
    public string VariantName => Source.VariantName;
    public string VariantLabel => VariantName+(NewVisibility==Visibility.Visible?" · NEW":"");
    public void UpdateNewness(bool recent)
    {
        var visibility=recent?Visibility.Visible:Visibility.Collapsed;
        if(NewVisibility==visibility)return;
        NewVisibility=visibility;
        PropertyChanged?.Invoke(this,new(nameof(NewVisibility)));
        PropertyChanged?.Invoke(this,new(nameof(VariantLabel)));
    }
    private ModelMemoryFit _memoryFit=new(MemoryFitLevel.Unknown,"","GPU capacity has not been read yet.");
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public string MemoryFitText=>_memoryFit.Label;
    public string MemoryFitHelp=>_memoryFit.Explanation;
    public Visibility FitGreen=>_memoryFit.Level==MemoryFitLevel.Comfortable?Visibility.Visible:Visibility.Collapsed;
    public Visibility FitAmber=>_memoryFit.Level==MemoryFitLevel.Close?Visibility.Visible:Visibility.Collapsed;
    public Visibility FitRed=>_memoryFit.Level==MemoryFitLevel.ExceedsCapacity?Visibility.Visible:Visibility.Collapsed;
    public Visibility MemoryFitVisibility=>_memoryFit.Level==MemoryFitLevel.Unknown?Visibility.Collapsed:Visibility.Visible;
    public void UpdateMemoryFit(GpuStatus? gpu)
    {
        var fit=ModelMemoryFit.Assess(Source,gpu);
        if(fit==_memoryFit)return;
        _memoryFit=fit;
        foreach(var name in new[]{nameof(MemoryFitText),nameof(MemoryFitHelp),nameof(FitGreen),nameof(FitAmber),nameof(FitRed),nameof(MemoryFitVisibility)})
            PropertyChanged?.Invoke(this,new(name));
    }
    public ModelItemView(ModelInfo x){Source=x;DisplayName=x.DisplayName;Repository=x.Repository;FileName=x.FileName;ApiName=x.ModelId;Weights=ModelPresentation.WeightSummary(x.Weights,x.FileName);SizeText=x.SizeText;ModelCardUrl=x.ModelCardUrl;Installed=x.Installed;Vision=x.Vision;VisionVisibility=x.Vision?Visibility.Visible:Visibility.Collapsed;DownloadAvailable=x.DownloadAvailable;DownloadVisibility=!x.Installed&&x.DownloadAvailable?Visibility.Visible:Visibility.Collapsed;InstalledVisibility=x.Installed?Visibility.Visible:Visibility.Collapsed;NewVisibility=x.IsNew?Visibility.Visible:Visibility.Collapsed;ActivateVisibility=x.Installed&&!x.Active?Visibility.Visible:Visibility.Collapsed;VerifyVisibility=x.Installed?Visibility.Visible:Visibility.Collapsed;DeleteVisibility=x.Installed||x.Missing?Visibility.Visible:Visibility.Collapsed;StateLabel=x.Missing?"File missing · locate the original artifact":x.Active?"Default · available through API":"Available through API";OriginText=x.Missing?"The linked file moved or is unavailable.":x.LinkedExternal?"External file · not copied":$"{x.Origin}{(!x.Inspected?" · metadata not verified":" · inspected")}";MissingVisibility=x.Missing?Visibility.Visible:Visibility.Collapsed;PresetText=x.PresetStatus??(x.Inspected?"Metadata inspected; full file identity is a separate check.":"Artifact metadata has not been verified.");}
    public ModelInfo Source{get;} public string DisplayName{get;} public string Repository{get;} public string FileName{get;} public string ApiName{get;} public string Weights{get;} public string SizeText{get;} public string ModelCardUrl{get;} public bool Installed{get;} public bool Vision{get;} public Visibility VisionVisibility{get;} public bool DownloadAvailable{get;} public Visibility DownloadVisibility{get;} public Visibility InstalledVisibility{get;} public Visibility NewVisibility{get;private set;} public Visibility ActivateVisibility{get;} public Visibility VerifyVisibility{get;} public Visibility DeleteVisibility{get;} public string StateLabel{get;}
    public string OriginText{get;}public string PresetText{get;}public Visibility MissingVisibility{get;}
}
public sealed class RequestItemView
{
    public RequestItemView(RequestMetric x){Time=x.StartedAt.ToString("HH:mm:ss");Status=x.Status;Type=x.Type;Input=x.PromptTokens==0?"—":x.PromptTokens.ToString("N0");Output=x.GeneratedTokens==0?"—":x.GeneratedTokens.ToString("N0");Ttft=x.TtftMs is null?"—":$"{x.TtftMs:N0} ms";Prefill=x.PrefillTokensPerSecond is null?"—":$"{x.PrefillTokensPerSecond:N1}";Decode=x.DecodeTokensPerSecond is null?"—":$"{x.DecodeTokensPerSecond:N1}";Speculative=x.Speculative;}
    public string Time{get;}public string Status{get;}public string Type{get;}public string Input{get;}public string Output{get;}public string Ttft{get;}public string Prefill{get;}public string Decode{get;}public string Speculative{get;}
}
