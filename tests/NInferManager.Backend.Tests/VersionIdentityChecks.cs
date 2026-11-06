using System.Security.Cryptography;
using NInferManager.Backend;

internal static class VersionIdentityChecks
{
    public static async Task RunAsync(AppPaths paths,SettingsService settings,Action<bool,string> assert)
    {
        var catalog=new ModelCatalog(paths,settings);
        var known=catalog.Entries.Single(e=>e.ModelId=="omniedu-27b-q6-k");
        var path=Path.Combine(paths.ModelsDirectory,"renamed-identity-fixture.ninfer");
        byte[] bytes=[11,22,33,44];await File.WriteAllBytesAsync(path,bytes);
        known.SizeBytes=bytes.Length;known.Sha256=Convert.ToHexString(SHA256.HashData(bytes));
        var local=catalog.LinkExternal(path);var calls=0;
        async Task<string> Hash(string file,CancellationToken token)
        {
            calls++;await using var input=File.OpenRead(file);
            return Convert.ToHexString(await SHA256.HashDataAsync(input,token));
        }
        await catalog.ResolveVersionRequirementsCoreAsync(local,CancellationToken.None,Hash);
        assert(calls==1 && local.IdentityVerified && local.ExpectedSha256==known.Sha256 && local.MinimumManagerVersion=="1.1.0" && local.MinimumEngineVersion=="1.1.0","first renamed artifact verification caches matched SHA and inherits version requirements");
        await catalog.ResolveVersionRequirementsCoreAsync(local,CancellationToken.None,Hash);
        assert(calls==1,"unchanged verified renamed artifact reuses SHA identity without reading its weights again");
        File.SetLastWriteTimeUtc(path,File.GetLastWriteTimeUtc(path).AddSeconds(2));
        await catalog.ResolveVersionRequirementsCoreAsync(local,CancellationToken.None,Hash);
        assert(calls==2 && local.IdentityVerified,"changed write stamp triggers fresh identity verification");
        await File.WriteAllBytesAsync(path,[44,33,22,11]);File.SetLastWriteTimeUtc(path,File.GetLastWriteTimeUtc(path).AddSeconds(4));
        await catalog.ResolveVersionRequirementsCoreAsync(local,CancellationToken.None,Hash);
        assert(calls==3 && !local.IdentityVerified && local.MinimumManagerVersion=="1.1.0" && local.MinimumEngineVersion=="1.1.0","changed artifact invalidates cached identity while retaining stronger version requirements");
        await File.WriteAllBytesAsync(path,bytes);File.SetLastWriteTimeUtc(path,File.GetLastWriteTimeUtc(path).AddSeconds(6));
        try
        {
            await catalog.ResolveVersionRequirementsCoreAsync(local,CancellationToken.None,async(file,token)=>
            {
                var hash=await Hash(file,token);File.SetLastWriteTimeUtc(file,File.GetLastWriteTimeUtc(file).AddSeconds(2));return hash;
            });
            throw new Exception("Expected concurrent artifact change to reject verification.");
        }
        catch(IOException error) when(error.Message.Contains("during verification")) { }
        assert(!local.IdentityVerified && local.MinimumEngineVersion=="1.1.0","artifact mutation during hashing cannot publish a verified identity or weaken its version gate");
        File.Delete(path);
        try { await catalog.ResolveVersionRequirementsCoreAsync(local,CancellationToken.None,Hash);throw new Exception("Expected missing artifact."); }
        catch(FileNotFoundException) { }
        assert(local.MinimumManagerVersion=="1.1.0" && local.MinimumEngineVersion=="1.1.0","missing renamed artifact retains its model requirements");
    }
}
