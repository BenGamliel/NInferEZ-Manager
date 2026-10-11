namespace NInferManager.Backend;

public sealed class CatalogMonitor(ModelCatalog catalog,SettingsService settings,ManagerLog log,TimeProvider? time=null) : BackgroundService
{
    private readonly TimeProvider _time=time??TimeProvider.System;
    private DateTimeOffset? _failedAt;
    private int _startupComplete;
    public bool StartupCheckComplete=>Volatile.Read(ref _startupComplete)!=0;

    public async Task CheckAsync(bool startup,CancellationToken token)
    {
        try
        {
            if(!settings.Current.AutoCheckCatalog||(!startup&&!catalog.ShouldRefresh()))return;
            if(!startup&&_failedAt is { } failed&&_time.GetUtcNow()-failed<TimeSpan.FromHours(1))return;
            await catalog.RefreshOnlineAsync(token);
            _failedAt=null;
        }
        catch(OperationCanceledException)when(token.IsCancellationRequested){}
        catch(Exception error)
        {
            _failedAt=_time.GetUtcNow();
            log.Write("Automatic model catalog check failed; cached models remain available",error);
        }
        finally{if(startup)Volatile.Write(ref _startupComplete,1);}
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await CheckAsync(true,stoppingToken);
        using var timer=new PeriodicTimer(TimeSpan.FromMinutes(5),_time);
        try{while(await timer.WaitForNextTickAsync(stoppingToken))await CheckAsync(false,stoppingToken);}
        catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){}
    }
}
