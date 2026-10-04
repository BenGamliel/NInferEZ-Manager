using System.IO.Compression;
using System.Security.Cryptography;
using NInferManager.Backend;

var root = Path.Combine(Path.GetTempPath(), "NInferEZ-Update-Tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var passed = 0;
try
{
    Test("successful replacement preserves model/data/engine and removes backup after acknowledgement", () =>
    {
        var target = Target("success");
        var zip = Archive("success.zip");
        var transaction = new PortableUpdateTransaction(target);
        transaction.Prepare(zip, Hash(zip));
        transaction.Apply();
        Assert(File.ReadAllText(Path.Combine(target, PortableUpdateTransaction.ApplicationName)) == "application replacement");
        Assert(File.ReadAllText(Path.Combine(target, "Data", "settings.json")) == "user settings");
        Assert(File.ReadAllText(Path.Combine(target, "Models", "model.ninfer")) == "user model");
        Assert(File.ReadAllText(Path.Combine(target, "Engine", "ninfer-serve.exe")) == "existing engine");
        Assert(Directory.Exists(transaction.RecoveryDirectory));
        File.WriteAllText(transaction.AcknowledgementFile, "ready");
        transaction.Confirm();
        Assert(!Directory.Exists(transaction.RecoveryDirectory));
    });
    Test("copy failure restores every original component", () =>
    {
        var target = Target("failure");
        var zip = Archive("failure.zip");
        var transaction = new PortableUpdateTransaction(target, checkpoint =>
        {
            if (checkpoint == "installed:Backend") throw new IOException("simulated disk failure");
        });
        transaction.Prepare(zip, Hash(zip));
        Throws<IOException>(transaction.Apply);
        Assert(File.ReadAllText(Path.Combine(target, PortableUpdateTransaction.ApplicationName)) == "original application");
        Assert(File.ReadAllText(Path.Combine(target, "Backend", "NInferManager.Backend.exe")) == "original backend");
        Assert(File.ReadAllText(Path.Combine(target, "Data", "settings.json")) == "user settings");
        transaction.Rollback();
    });
    Test("duplicate startup confirmation is idempotent", () =>
    {
        var target = Target("confirm-twice"); var zip = Archive("confirm-twice.zip");
        var transaction = new PortableUpdateTransaction(target);
        transaction.Prepare(zip, Hash(zip)); transaction.Apply();
        File.WriteAllText(transaction.AcknowledgementFile, "ready");
        transaction.Confirm(); transaction.Confirm();
        Assert(!Directory.Exists(transaction.RecoveryDirectory));
    });
    Test("simultaneous helper and backend confirmation serialize safely", () =>
    {
        var target = Target("confirm-concurrent"); var zip = Archive("confirm-concurrent.zip");
        var transaction = new PortableUpdateTransaction(target);
        transaction.Prepare(zip, Hash(zip)); transaction.Apply();
        File.WriteAllText(transaction.AcknowledgementFile, "ready");
        Parallel.Invoke(transaction.Confirm, new PortableUpdateTransaction(target).Confirm);
        Assert(!Directory.Exists(transaction.RecoveryDirectory));
    });
    Test("pending confirmation cannot be silently discarded", () =>
    {
        var target = Target("pending"); var zip = Archive("pending.zip");
        var transaction = new PortableUpdateTransaction(target);
        transaction.Prepare(zip, Hash(zip)); transaction.Apply();
        Throws<InvalidOperationException>(transaction.Confirm);
        Throws<InvalidOperationException>(() => transaction.Prepare(zip, Hash(zip)));
        transaction.Rollback();
        Assert(File.ReadAllText(Path.Combine(target, PortableUpdateTransaction.ApplicationName)) == "original application");
    });
    Test("hash mismatch changes no installed files", () =>
    {
        var target = Target("hash"); var zip = Archive("hash.zip");
        var transaction = new PortableUpdateTransaction(target);
        Throws<InvalidDataException>(() => transaction.Prepare(zip, new string('0', 64)));
        Assert(!Directory.Exists(transaction.RecoveryDirectory));
        Assert(File.ReadAllText(Path.Combine(target, PortableUpdateTransaction.ApplicationName)) == "original application");
    });
    Test("installed updater refuses portable targets before launching anything", () =>
    {
        var target=Target("installed-refusal");
        Throws<InvalidOperationException>(()=>PortableUpdateRunner.TryRun(["--apply-installed-update","--target",target,"--installer","missing.exe","--sha256",new string('0',64),"--ui-pid","999999","--backend-pid","999998"]));
        Assert(File.ReadAllText(Path.Combine(target,PortableUpdateTransaction.ApplicationName))=="original application");
    });
    Test("a completed rollback permits retry without manual deletion", () =>
    {
        var target = Target("retry"); var zip = Archive("retry.zip");
        var transaction = new PortableUpdateTransaction(target);
        transaction.Prepare(zip, Hash(zip)); transaction.Apply(); transaction.Rollback();
        transaction.Prepare(zip, Hash(zip)); transaction.Apply();
        Assert(File.ReadAllText(Path.Combine(target, PortableUpdateTransaction.ApplicationName)) == "application replacement");
        transaction.Rollback();
    });
    Test("path traversal rejected before commit", () =>
    {
        var target = Target("traversal"); var zip = Archive("traversal.zip", "../escape.txt");
        var transaction = new PortableUpdateTransaction(target);
        Throws<InvalidDataException>(() => transaction.Prepare(zip, Hash(zip)));
        Assert(!File.Exists(Path.Combine(root, "escape.txt")));
        Assert(!Directory.Exists(transaction.RecoveryDirectory));
    });
    Test("pre-journal staging interruption permits safe retry", () =>
    {
        var target = Target("pre-journal"); var zip = Archive("pre-journal.zip");
        var transaction = new PortableUpdateTransaction(target);
        Directory.CreateDirectory(Path.Combine(transaction.RecoveryDirectory, "staging"));
        File.WriteAllText(Path.Combine(transaction.RecoveryDirectory, "staging", "partial-file.txt"), "partial staging");
        transaction.Prepare(zip, Hash(zip)); transaction.Apply(); transaction.Rollback();
        Assert(File.ReadAllText(Path.Combine(target, PortableUpdateTransaction.ApplicationName)) == "original application");
    });
    Test("duplicate archive destination rejected", () =>
    {
        var target = Target("duplicate"); var zip = Archive("duplicate.zip", "Backend/NInferManager.Backend.exe");
        var transaction = new PortableUpdateTransaction(target);
        Throws<InvalidDataException>(() => transaction.Prepare(zip, Hash(zip)));
    });
    Test("unexpected application component rejected", () =>
    {
        var target = Target("unexpected"); var zip = Archive("unexpected.zip", "other.exe");
        var transaction = new PortableUpdateTransaction(target);
        Throws<InvalidDataException>(() => transaction.Prepare(zip, Hash(zip)));
    });
    Test("recovery can restore interrupted root executable move", () =>
    {
        var target = Target("interrupted"); var zip = Archive("interrupted.zip");
        var transaction = new PortableUpdateTransaction(target);
        transaction.Prepare(zip, Hash(zip));
        File.Move(Path.Combine(target, PortableUpdateTransaction.ApplicationName),
            Path.Combine(transaction.RecoveryDirectory, "backup", PortableUpdateTransaction.ApplicationName));
        new PortableUpdateTransaction(target).Rollback();
        Assert(File.ReadAllText(Path.Combine(target, PortableUpdateTransaction.ApplicationName)) == "original application");
    });
    Console.WriteLine($"{passed} updater host tests passed. No application, engine or GPU was launched.");
}
finally
{
    var full = Path.GetFullPath(root);
    if (full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) &&
        Path.GetFileName(full).StartsWith("NInferEZ-Update-Tests-", StringComparison.Ordinal))
        Directory.Delete(full, true);
}

void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}
string Target(string name)
{
    var target = Path.Combine(root, name);
    Directory.CreateDirectory(target);
    File.WriteAllText(Path.Combine(target, "portable.mode"), "");
    File.WriteAllText(Path.Combine(target, PortableUpdateTransaction.ApplicationName), "original application");
    foreach (var (folder, file, text) in new[]
    {
        ("Backend", "NInferManager.Backend.exe", "original backend"),
        ("Assets", "NInferEZ.ico", "original icon"),
        ("Docs", "LICENSE.txt", "original license"),
        ("Data", "settings.json", "user settings"),
        ("Models", "model.ninfer", "user model"),
        ("Engine", "ninfer-serve.exe", "existing engine")
    })
    {
        Directory.CreateDirectory(Path.Combine(target, folder));
        File.WriteAllText(Path.Combine(target, folder, file), text);
    }
    return target;
}
string Archive(string name, string? extra = null)
{
    var path = Path.Combine(root, name);
    using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
    foreach (var (file, text) in new[]
    {
        (PortableUpdateTransaction.ApplicationName, "application replacement"),
        ("Backend/NInferManager.Backend.exe", "backend replacement"),
        ("Assets/NInferEZ.ico", "icon replacement"),
        ("Docs/LICENSE.txt", "license replacement"),
        ("Data/settings.json", "must not overwrite user data"),
        ("Models/model.ninfer", "must not overwrite user model"),
        ("Engine/ninfer-serve.exe", "must not overwrite engine")
    })
    {
        using var writer = new StreamWriter(zip.CreateEntry(file).Open()); writer.Write(text);
    }
    if (extra is not null) { using var writer = new StreamWriter(zip.CreateEntry(extra).Open()); writer.Write("invalid extra entry"); }
    return path;
}
string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
