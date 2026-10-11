using System.Text.Json;
using NInferManager.Backend;

internal static class PublisherCatalogChecks
{
    public static void Run(string root, Action<bool,string> check)
    {
        var names=new[]{"NINFEREZ_MANAGER_DATA_ROOT","NINFEREZ_MANAGER_MODELS_ROOT","NINFEREZ_MANAGER_ENGINE_ROOT"};
        var previous=names.Select(Environment.GetEnvironmentVariable).ToArray();
        try
        {
            foreach(var name in names)Environment.SetEnvironmentVariable(name,Path.Combine(root,"publisher-cache",name));
            var paths=new AppPaths();
            var settings=new SettingsService(paths);
            var present=Path.Combine(paths.ModelsDirectory,"legacy-present.ninfer");
            File.WriteAllBytes(present,[1,2,3,4]);
            var entries=new[]{
                new ModelCatalogEntry{FileName="legacy-absent.ninfer",Repository="other/publisher",SizeBytes=4,ArtifactVersion=3,Featured=true},
                new ModelCatalogEntry{FileName="legacy-present.ninfer",Repository="other/publisher",SizeBytes=4,ArtifactVersion=3,Featured=true}
            };
            File.WriteAllText(paths.CatalogCacheFile,JsonSerializer.Serialize(entries,SettingsService.JsonOptions));
            var catalog=new ModelCatalog(paths,settings);
            check(catalog.Find("legacy-absent.ninfer") is null,"old third-party recommendations without local files are removed from the cache");
            var local=catalog.Find("legacy-present.ninfer")!;
            check(catalog.IsInstalled(local)&&!local.Featured&&!local.DownloadAvailable&&local.Repository=="Local file",
                "existing third-party local models remain usable without promotion or download links");
            check(File.ReadAllBytes(present).SequenceEqual(new byte[]{1,2,3,4}),"catalog migration never changes or deletes model files");
        }
        finally
        {
            for(var i=0;i<names.Length;i++)Environment.SetEnvironmentVariable(names[i],previous[i]);
        }
    }
}
