using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NInferManager.Contracts;

namespace NInferManager.WinUI;

public sealed partial class MainWindow
{
    private bool _checkingUpdates;
    private bool _installingUpdate;
    private bool _cancelUpdateRequested;
    private ApplicationUpdateNotice? _knownApplicationUpdate;
    private string? _dismissedUpdateVersion;

    private void RestoreApplicationUpdateNotice()
    {
        var known=_settings?.AvailableApplicationUpdate;
        _knownApplicationUpdate=known is not null && Version.TryParse(known.Version,out var latest) &&
            Version.TryParse(AboutVersionText.Text,out var installed) && latest>installed ? known : null;
        _dismissedUpdateVersion=_settings?.DismissedUpdateVersion;
        SettingsInstalledVersionText.Text=$"Installed version: {AboutVersionText.Text}";
        RenderApplicationUpdate();
    }

    private void RenderApplicationUpdate()
    {
        var known=_knownApplicationUpdate;
        var busy=_checkingUpdates||_installingUpdate;
        SettingsCheckUpdatesButton.IsEnabled=_settings is not null&&!busy;
        SettingsCheckUpdatesButton.Content=_checkingUpdates?"Checking for updates…":"Check for updates";
        SettingsAvailableUpdatePanel.Visibility=known is null?Visibility.Collapsed:Visibility.Visible;
        var installable=known?.CanInstall==true;
        SettingsInstallUpdateButton.Visibility=NoticeInstallUpdateButton.Visibility=installable?Visibility.Visible:Visibility.Collapsed;
        SettingsInstallUpdateButton.IsEnabled=NoticeInstallUpdateButton.IsEnabled=installable&&!busy;
        if(known is not null)
        {
            var message=installable?$"Version {known.Version} is available.":$"Version {known.Version} is available. A verified package is not ready yet.";
            ApplicationUpdateNoticeBar.Message=SettingsAvailableUpdateText.Text=message;
        }
        // This bar is independent of the timers and status messages for transient notifications.
        ApplicationUpdateNoticeBar.IsOpen=known is not null && known.Version!=_dismissedUpdateVersion;
    }

    private async void ApplicationUpdateNotice_CloseButtonClick(InfoBar sender,object args)
    {
        if(_knownApplicationUpdate is not { } known)return;
        _dismissedUpdateVersion=known.Version;
        try{await _backend.DismissUpdateAsync(known.Version);}
        catch(Exception ex){if(!_closing)ShowGlobal("Update dismissal could not be saved",ex.Message,InfoBarSeverity.Warning);}
    }

    private async Task<bool> EnsureProductUpdateReadyAsync(bool interactive)
    {
        var completion=await _backend.ProductUpdateAsync();
        if(completion is not {Ready:false})return true;
        if(!interactive){ShowGlobal("Update incomplete",completion.Message,InfoBarSeverity.Warning);return false;}
        await _backend.CompleteProductUpdateAsync();
        await CompleteProductUpdateAsync();
        if(!_productUpdateReady)return false;
        ConfirmPortableUpdateLaunch();
        await RefreshEngineLibraryAsync();
        return true;
    }

    private async Task<UpdateInfo> GetLatestApplicationUpdateAsync()
    {
        var update=await _backend.CheckUpdateAsync()??throw new InvalidOperationException("The update service did not respond.");
        if(!Version.TryParse(update.LatestVersion,out _))throw new InvalidOperationException(update.Message);
        _knownApplicationUpdate=update.UpdateAvailable?new(update.LatestVersion,update.AssetUrl is not null):null;
        if(!_closing)RenderApplicationUpdate();
        return update;
    }

    private async Task CheckForUpdatesAsync(bool interactive)
    {
        if(_closing||_checkingUpdates||_installingUpdate)return;
        _checkingUpdates=true;
        RenderApplicationUpdate();
        try
        {
            if(!await EnsureProductUpdateReadyAsync(interactive))return;
            var update=await GetLatestApplicationUpdateAsync();
            if(!_closing&&interactive&&!update.UpdateAvailable)ShowGlobal("Up to date",update.Message,InfoBarSeverity.Success);
        }
        catch(Exception ex){if(!_closing&&interactive)ShowGlobal("Update check failed",ex.Message,InfoBarSeverity.Error);}
        finally{_checkingUpdates=false;if(!_closing)RenderApplicationUpdate();}
    }

    private async void InstallApplicationUpdate_Click(object sender,RoutedEventArgs e)
    {
        if(_closing||_checkingUpdates||_installingUpdate||_knownApplicationUpdate?.CanInstall!=true)return;
        _installingUpdate=true;
        _cancelUpdateRequested=false;
        RenderApplicationUpdate();
        ApplicationUpdateText.Text="Checking for the latest release…";
        ApplicationUpdateProgress.IsIndeterminate=true;
        CancelApplicationUpdateButton.IsEnabled=false;
        ApplicationUpdatePanel.Visibility=Visibility.Visible;
        try
        {
            if(!await EnsureProductUpdateReadyAsync(true))return;
            // Never install cached package details; the latest release is checked again on click.
            var update=await GetLatestApplicationUpdateAsync();
            if(_closing)return;
            if(!update.UpdateAvailable){ShowGlobal("Up to date",update.Message,InfoBarSeverity.Success);return;}
            if(update.AssetUrl is null){ShowGlobal("Update package unavailable",update.Message,InfoBarSeverity.Warning);return;}
            await _backend.DownloadUpdateAsync(update);
            CancelApplicationUpdateButton.IsEnabled=true;
            var deadline=Stopwatch.StartNew();
            while(!_closing)
            {
                await Task.Delay(500);
                var progress=await _backend.UpdateProgressAsync();
                if(progress is null)
                {
                    if(deadline.Elapsed>TimeSpan.FromSeconds(30))throw new InvalidOperationException("The updater did not report progress.");
                    continue;
                }
                ApplicationUpdateText.Text=_cancelUpdateRequested?"Cancelling…":$"{progress.Stage} · {progress.Completed/1024d/1024d:0} MiB";
                ApplicationUpdateProgress.IsIndeterminate=progress.Total<=0;
                if(progress.Total>0)ApplicationUpdateProgress.Value=Math.Clamp(progress.Completed*100d/progress.Total,0,100);
                if(progress.Running)continue;
                if(_cancelUpdateRequested){ShowGlobal("Update cancelled","The application was not replaced.",InfoBarSeverity.Informational);return;}
                if(progress.Error is not null)throw new InvalidOperationException(progress.Error);
                if(progress.Stage!="Ready to install")throw new InvalidOperationException("No verified update is ready to install.");
                break;
            }
            if(_closing)return;
            CancelApplicationUpdateButton.IsEnabled=false;
            ApplicationUpdateText.Text=$"Installing NInferEZ {update.LatestVersion}…";
            await _backend.ApplyUpdateAsync();
            _backend.ReleaseOwnershipForUpdate();
            _realExit=true;
            await Task.Delay(250);
            Close();
        }
        catch(Exception ex){if(!_closing)ShowGlobal("Update failed",ex.Message,InfoBarSeverity.Error);}
        finally
        {
            _installingUpdate=false;
            if(!_closing){ApplicationUpdatePanel.Visibility=Visibility.Collapsed;RenderApplicationUpdate();}
        }
    }

    private async void CancelApplicationUpdate_Click(object sender,RoutedEventArgs e)
    {
        _cancelUpdateRequested=true;
        CancelApplicationUpdateButton.IsEnabled=false;
        try{await _backend.CancelUpdateAsync();}
        catch(Exception ex){if(!_closing)ShowGlobal("Could not cancel update",ex.Message,InfoBarSeverity.Error);}
    }
}
