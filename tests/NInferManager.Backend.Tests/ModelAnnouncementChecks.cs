using System.Net;
using System.Text.Json;
using NInferManager.Backend;
using NInferManager.Contracts;

internal static class ModelAnnouncementChecks
{
    public static async Task RunAsync(AppPaths paths,SettingsService settings,ManagerLog log,Action<bool,string> check)
    {
        var clock=new Clock(new(2026,10,10,12,0,0,TimeSpan.Zero));
        var now=clock.GetUtcNow();
        check(ModelAnnouncementPolicy.IsRecent(now,now.AddDays(-29),null,null),"published catalog model remains NEW within its month");
        check(!ModelAnnouncementPolicy.IsRecent(now,now.AddMonths(-1),null,null),"NEW expires at the calendar month boundary");
        check(!ModelAnnouncementPolicy.IsRecent(now,now.AddDays(1),null,null),"future publication cannot trigger an early model popup");
        check(ModelAnnouncementPolicy.RecentUntil(null,new(2024,1,31,0,0,0,TimeSpan.Zero),null)==new DateTimeOffset(2024,2,29,0,0,0,TimeSpan.Zero),"month expiry handles leap years and short months");
        check(ModelAnnouncementPolicy.RecentUntil(now,now,now.AddYears(1))==now.AddMonths(1),"feed NEW dates cannot extend the marker beyond one month");
        check(!ModelAnnouncementPolicy.IsRecent(now,DateTimeOffset.MaxValue,null,null),"extreme future feed date cannot crash the notification policy");
        var entry=new ModelCatalogEntry{DisplayName="New fixture model",FileName="announcement-fixture.ninfer",ModelId="announcement-fixture",
            Repository="2beng2/fixture-model",Revision=new('1',40),Sha256=new('a',64),SizeBytes=8,ArtifactVersion=3,Featured=true,
            RecommendedContext=32768,NativeContext=32768,MaximumContext=65536,RecommendedSpeculativeMode=SpeculativeMode.Disabled};
        using var handler=new FeedHandler(entry);
        using var http=new HttpClient(handler);
        var catalog=new ModelCatalog(paths,settings,http,clock);
        settings.Update(x=>{x.AutoCheckCatalog=false;x.LastCatalogCheckUtc=now;});
        var monitor=new CatalogMonitor(catalog,settings,log,clock);
        await monitor.CheckAsync(true,CancellationToken.None);
        check(handler.Calls==0&&monitor.StartupCheckComplete,"disabled automatic checks do no network work and complete startup");
        settings.Update(x=>x.AutoCheckCatalog=true);
        check(!catalog.ShouldRefresh(),"recent successful check prevents unnecessary periodic network traffic");
        await monitor.CheckAsync(true,CancellationToken.None);
        check(handler.Calls==1&&monitor.StartupCheckComplete,"every enabled application startup checks the feed even within the periodic interval");
        var state=catalog.Notifications(true);
        var notice=state.Pending.Single(x=>x.FileName==entry.FileName);
        check(state.RecentFiles.Contains(entry.FileName)&&state.Revision>1,"new remote entry appears immediately in notification and NEW state");
        check(catalog.Find(entry.FileName)!.FirstSeenUtc==now,"feed without a publication date records first discovery once");
        var before=JsonSerializer.Serialize(settings.Current.Profiles,SettingsService.JsonOptions);
        catalog.DismissAnnouncements([notice.Identity]);
        check(!catalog.Notifications(true).Pending.Any(x=>x.FileName==entry.FileName),"Dismiss prevents a duplicate popup for this exact artifact");
        check(catalog.Notifications(true).RecentFiles.Contains(entry.FileName)&&catalog.Snapshot().Single(x=>x.FileName==entry.FileName).IsNew,"Dismiss does not remove the month-long NEW badge");
        var restarted=new ModelCatalog(paths,new SettingsService(paths),http,clock);
        check(!restarted.Notifications(true).Pending.Any(x=>x.FileName==entry.FileName)&&restarted.Notifications(true).RecentFiles.Contains(entry.FileName),"dismissal and first-discovery age survive an application restart");
        entry.Revision=new('2',40);
        await catalog.RefreshOnlineAsync();
        check(!catalog.Notifications(true).Pending.Any(x=>x.FileName==entry.FileName),"repository revision or description changes cannot reannounce unchanged weights");
        var checks=handler.Calls;
        await monitor.CheckAsync(false,CancellationToken.None);
        check(handler.Calls==checks,"periodic monitor skips an interval that is not due");
        clock.Advance(TimeSpan.FromHours(24));
        await monitor.CheckAsync(false,CancellationToken.None);
        check(handler.Calls==checks+1,"due periodic check refreshes the feed without UI or inference activity");
        entry.Sha256=new('b',64);
        await catalog.RefreshOnlineAsync();
        var updated=catalog.Notifications(true).Pending.Single(x=>x.FileName==entry.FileName);
        check(updated.Identity!=notice.Identity,"different published weights can produce a new artifact announcement");
        catalog.DismissAnnouncements([updated.Identity]);
        check(!catalog.Notifications(true).Pending.Any(x=>x.FileName==entry.FileName),"Try it acknowledgement shares the persistent dismissal behavior");
        var seen=settings.Current.SeenModelArtifacts.Count;
        try{catalog.DismissAnnouncements(["not-a-catalog-identity"]);throw new Exception("Unknown identity accepted.");}
        catch(ArgumentException){check(settings.Current.SeenModelArtifacts.Count==seen,"unknown acknowledgement cannot mutate user settings");}
        before=JsonSerializer.Serialize(settings.Current.Profiles,SettingsService.JsonOptions);
        handler.Fail=true;
        await monitor.CheckAsync(true,CancellationToken.None);
        check(monitor.StartupCheckComplete&&catalog.Find(entry.FileName) is not null,"offline startup retains cached models and does not block application readiness");
        checks=handler.Calls;
        clock.Advance(TimeSpan.FromHours(24));
        await monitor.CheckAsync(false,CancellationToken.None);
        checks=handler.Calls;
        await monitor.CheckAsync(false,CancellationToken.None);
        check(handler.Calls==checks,"failed automatic check has retry backoff instead of repeated HTTP attempts");
        check(before==JsonSerializer.Serialize(settings.Current.Profiles,SettingsService.JsonOptions),"background checks and notifications do not change inference profiles");
        clock.Advance(TimeSpan.FromDays(31));
        check(!catalog.Notifications(true).RecentFiles.Contains(entry.FileName),"NEW expires even while the application remains open");
        handler.Fail=false;
        await catalog.RefreshOnlineAsync();
        check(!catalog.Notifications(true).RecentFiles.Contains(entry.FileName),"refresh does not reset an old model's first-discovery age");
    }

    private sealed class Clock(DateTimeOffset now):TimeProvider
    {
        private DateTimeOffset _now=now;
        public override DateTimeOffset GetUtcNow()=>_now;
        public void Advance(TimeSpan elapsed)=>_now+=elapsed;
    }
    private sealed class FeedHandler(ModelCatalogEntry entry):HttpMessageHandler
    {
        public int Calls{get;private set;}
        public bool Fail{get;set;}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Calls++;
            if(Fail)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            var json=JsonSerializer.Serialize(new{schemaVersion=1,product="NInferEZ Model Catalog",models=new[]{entry}},SettingsService.JsonOptions);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json)});
        }
    }
}
