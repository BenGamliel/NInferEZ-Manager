namespace NInferManager.Contracts;

public static class ModelAnnouncementPolicy
{
    public static DateTimeOffset? RecentUntil(DateTimeOffset? published, DateTimeOffset? firstSeen, DateTimeOffset? suppliedUntil)
    {
        var start=published??firstSeen;
        if(start is null)return suppliedUntil;
        DateTimeOffset monthEnd;
        try{monthEnd=start.Value.AddMonths(1);}catch(ArgumentOutOfRangeException){monthEnd=DateTimeOffset.MaxValue;}
        return suppliedUntil is { } until&&until<monthEnd?until:monthEnd;
    }

    public static bool IsRecent(DateTimeOffset now, DateTimeOffset? published, DateTimeOffset? firstSeen, DateTimeOffset? suppliedUntil)
    {
        var start=published??firstSeen;
        return (start is null||start<=now)&&RecentUntil(published,firstSeen,suppliedUntil)>now;
    }
}

public sealed record ModelAnnouncement(string Identity,string FileName,string DisplayName,string VariantName);
public sealed record CatalogNotificationSnapshot(long Revision,bool StartupCheckComplete,IReadOnlyList<string> RecentFiles,IReadOnlyList<ModelAnnouncement> Pending);
public sealed record DismissModelAnnouncementsRequest(IReadOnlyList<string> Identities);
