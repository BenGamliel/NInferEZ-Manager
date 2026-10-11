using System.Text.Json;
using NInferManager.Backend;
using NInferManager.Contracts;

internal static class ModelVariantChecks
{
    public static void Run(AppPaths paths, SettingsService settings, ModelCatalog catalog, Action<bool,string> assert)
    {
        // Earlier launch tests deliberately replace curated sizes with tiny fake weights.
        catalog=new ModelCatalog(paths,settings);
        var mtp=catalog.Find("orca-router-iq3-xxs.ninfer")!;
        var draft=catalog.Find("orca-router-iq3-xxs-dflash2.ninfer")!;
        assert(mtp.FamilyId==draft.FamilyId&&!string.IsNullOrWhiteSpace(draft.FamilyId),"MTP and DFlash2 share one explicit recommendation family");
        assert(mtp.SizeBytes==10796696832&&draft.SizeBytes==13023514368,"variant sizes describe their own artifact, not the family");
        assert(mtp.DownloadUrl.Contains("qwen3.8-27b-orcarouter-iq3-xxs-mtp-only.ninfer")&&draft.DownloadUrl.Contains("qwen3.8-27b-orcarouter-iq3-xxs-mtp-dflash2.ninfer"),"variant download actions use the exact published filenames");
        assert(mtp.Revision==draft.Revision&&draft.Revision=="d19429861f973f87c6331af120470d22942430ca"&&mtp.Sha256!=draft.Sha256,"downloads are pinned to verified revision and separate checksums");
        var profile=draft.RecommendedProfile();
        assert(profile.SpeculativeMode==SpeculativeMode.Dflash2&&profile.DraftTokens==4&&profile.KvPrecision==KvPrecision.Rk8v4&&profile.PrefillChunk==2048,"new DFlash2 download receives its K4 RK8V4 starting profile");
        assert(profile.NgramDraftTokens==15&&profile.StructuredOutput&&profile.GdnStateFp16,"DFlash2 profile preserves its selected draft and state options");
        var groups=ModelRecommendations.Group(catalog.Snapshot());
        assert(groups.Count==catalog.Entries.Where(x=>x.Featured).Count()-1&&groups.Single(x=>x[0].FamilyId==draft.FamilyId).Count==2,"featured downloads group OrcaRouter variants without hiding other families");
        var variants=groups.Single(x=>x[0].FamilyId==draft.FamilyId);
        var absent=variants.Select(x=>x with {Installed=false,Active=false}).ToArray();
        assert(ModelRecommendations.Select(absent).FileName==mtp.FileName,"new users default to the smaller MTP-only variant");
        var installed=absent.Select(x=>x with {Installed=x.FileName==draft.FileName}).ToArray();
        assert(ModelRecommendations.Select(installed).FileName==draft.FileName,"existing DFlash2 is selected instead of showing an unnecessary MTP download");
        assert(ModelRecommendations.Select(installed,mtp.FileName).FileName==mtp.FileName,"refresh preserves a user's explicit variant selection");
        assert(ModelRecommendations.Select(installed,"removed.ninfer").Installed,"a removed variant selection falls back to the installed variant");
        assert(variants.Single(x=>x.FileName==draft.FileName).ModelCardUrl.Contains("/blob/"+draft.Revision+"/"+draft.RemoteFileName),"Hugging Face action points to the selected variant file");

        var path=Path.Combine(paths.ModelsDirectory,draft.RemoteFileName);
        File.WriteAllBytes(path,[1,2,3,4,5,6,7,8]);
        settings.Current.ModelAliases[draft.FileName]="my-unchanged-draft-name";
        settings.Current.Profiles[draft.FileName]=new(){ManageAdvancedSettings=false,AutoContext=false,MaxContext=32768,DefaultMaxTokens=256,SpeculativeMode=SpeculativeMode.Disabled,KvPrecision=KvPrecision.Rk8v4};
        settings.Save();
        File.WriteAllText(paths.CatalogCacheFile,JsonSerializer.Serialize(new[]{new ModelCatalogEntry{
            FileName=draft.FileName,DisplayName="stale-local-name",Repository="Local file",SizeBytes=8,
            Sha256="",ArtifactVersion=3,DownloadAvailable=false,Featured=false,LocalPath=path
        }},SettingsService.JsonOptions));
        var restored=new ModelCatalog(paths,new SettingsService(paths));
        var known=restored.Find(draft.FileName)!;
        assert(known.Featured&&known.DownloadAvailable&&known.FamilyId==draft.FamilyId&&known.Sha256==draft.Sha256,"old local cache cannot hide the newly cataloged DFlash2 recommendation");
        assert(known.RemoteFileName==draft.RemoteFileName&&known.Revision==draft.Revision,"stale cache cannot replace the verified download mapping");
        assert(!restored.IsInstalled(known),"an artifact with the wrong length is not marked installed");
        known.SizeBytes=8;
        known.LocalPath=null;
        var linked=restored.LinkExternal(path);
        assert(linked.FileName==draft.FileName&&restored.Entries.Count(x=>x.FamilyId==draft.FamilyId)==2,"published filename links to its canonical variant without another library entry");
        var available=restored.Snapshot().Single(x=>x.FileName==draft.FileName);
        assert(available.Installed&&available.ModelId=="my-unchanged-draft-name"&&available.RecommendedProfile.SpeculativeMode==SpeculativeMode.Disabled,"existing alias and manual profile remain unchanged by catalog association");
        assert(restored.FindInstalledByApiName("my-unchanged-draft-name")?.FileName==draft.FileName,"API routing still resolves the existing variant by its user-selected name");
        assert(ModelRecommendations.Select(ModelRecommendations.Group(restored.Snapshot()).Single(x=>x[0].FamilyId==draft.FamilyId)).Installed,"associated local variant is presented as installed in its family card");
    }
}
