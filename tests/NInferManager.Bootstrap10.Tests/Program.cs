using System.Security.Cryptography;
using NInferManager.Backend;

if(args.Length!=1)throw new ArgumentException("Pass the complete new portable archive.");
var archive=Path.GetFullPath(args[0]);
var target=Path.Combine(Path.GetTempPath(),"NInferEZ110-Bootstrap-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(target);
try
{
    File.WriteAllText(Path.Combine(target,"portable.mode"),"");
    File.WriteAllText(Path.Combine(target,PortableUpdateTransaction.ApplicationName),"old application fixture");
    foreach(var component in new[]{"Backend","Data","Models","Engine"})Directory.CreateDirectory(Path.Combine(target,component));
    File.WriteAllText(Path.Combine(target,"Backend","NInferManager.Backend.exe"),"old backend fixture");
    File.WriteAllText(Path.Combine(target,"Data","settings.json"),"existing settings fixture");
    File.WriteAllText(Path.Combine(target,"Models","existing.ninfer"),"existing model fixture");
    File.WriteAllText(Path.Combine(target,"Engine","ninfer-serve.exe"),"existing engine fixture");
    using var input=File.OpenRead(archive);
    var hash=Convert.ToHexString(SHA256.HashData(input));
    var transaction=new PortableUpdateTransaction(target);
    transaction.Prepare(archive,hash);
    transaction.Apply();
    Check(File.Exists(Path.Combine(target,"Backend","Converter-runtime.zip")),"unchanged Converter archive is inside the old helper's Backend replacement unit");
    Check(File.ReadAllText(Path.Combine(target,"Data","settings.json"))=="existing settings fixture","old helper preserves user settings");
    Check(File.ReadAllText(Path.Combine(target,"Models","existing.ninfer"))=="existing model fixture","old helper preserves existing models");
    Check(File.ReadAllText(Path.Combine(target,"Engine","ninfer-serve.exe"))=="existing engine fixture","old helper preserves existing engine");
    transaction.Rollback();
    Check(File.ReadAllText(Path.Combine(target,PortableUpdateTransaction.ApplicationName))=="old application fixture","old helper rolls back the new application without launching it");
    Check(File.ReadAllText(Path.Combine(target,"Backend","NInferManager.Backend.exe"))=="old backend fixture" && !File.Exists(Path.Combine(target,"Backend","Converter-runtime.zip")),"old helper rolls back the entire new Backend and Converter unit");
    Console.WriteLine("6 bootstrap compatibility checks passed using the unmodified 1.0.0 updater source. No application, engine or converter executable was launched.");
}
finally
{
    var parent=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
    if(!Path.GetFullPath(target).StartsWith(parent,StringComparison.OrdinalIgnoreCase))throw new IOException("Unsafe fixture cleanup path.");
    Directory.Delete(target,true);
}
static void Check(bool ok,string message){if(!ok)throw new Exception("FAILED: "+message);Console.WriteLine("PASS: "+message);}
