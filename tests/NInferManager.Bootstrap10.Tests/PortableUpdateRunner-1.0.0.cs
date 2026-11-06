using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace NInferManager.Backend;

public static class PortableUpdateRunner
{
    public static bool TryRun(string[] args)
    {
        if (args.Length == 0 || args[0] is not ("--apply-portable-update" or "--recover-portable-update" or "--apply-installed-update")) return false;
        if ((args.Length - 1) % 2 != 0) throw new ArgumentException("Update arguments are incomplete.");
        var values = args.Skip(1).Chunk(2).ToDictionary(x => x[0], x => x[1], StringComparer.Ordinal);
        foreach (var key in values.Keys)
            if (key is not ("--zip" or "--installer" or "--target" or "--ui-pid" or "--backend-pid" or "--sha256"))
                throw new ArgumentException("Unsupported update argument.");
        var target = Path.GetFullPath(values["--target"]);
        if (args[0] == "--apply-installed-update")
        {
            if (File.Exists(Path.Combine(target,"portable.mode")) || !File.Exists(Path.Combine(target,PortableUpdateTransaction.ApplicationName)))
                throw new InvalidOperationException("Installed updates require the existing installed application folder.");
            var installer=Path.GetFullPath(values["--installer"]);
            var expected=values["--sha256"];
            if(!installer.EndsWith(".exe",StringComparison.OrdinalIgnoreCase) || expected.Length!=64 || !expected.All(Uri.IsHexDigit))
                throw new InvalidDataException("A verified installer executable is required.");
            WaitForExit(int.Parse(values["--ui-pid"]));
            WaitForExit(int.Parse(values["--backend-pid"]));
            using(var stream=File.OpenRead(installer))
                if(!Convert.ToHexString(SHA256.HashData(stream)).Equals(expected,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The installer changed after verification.");
            _ = Process.Start(new ProcessStartInfo(installer,"/SILENT /SUPPRESSMSGBOXES /NORESTART /NINFEREZUPDATE=1") { UseShellExecute=true })
                ?? throw new IOException("The verified installer could not start.");
            return true;
        }
        if (args[0] == "--recover-portable-update")
        {
            RefuseRunningApplication(target);
            new PortableUpdateTransaction(target).Rollback();
            return true;
        }
        WaitForExit(int.Parse(values["--ui-pid"]));
        WaitForExit(int.Parse(values["--backend-pid"]));
        var transaction = new PortableUpdateTransaction(target);
        transaction.Prepare(values["--zip"], values["--sha256"]);
        Process? launched = null;
        try
        {
            File.Copy(Environment.ProcessPath!, Path.Combine(transaction.RecoveryDirectory, "recover.exe"), true);
            transaction.Apply();
            launched = Process.Start(new ProcessStartInfo(Path.Combine(target, PortableUpdateTransaction.ApplicationName))
            { UseShellExecute = true, WorkingDirectory = target })
                ?? throw new IOException("The updated application could not start.");
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(90))
            {
                if (!Directory.Exists(transaction.RecoveryDirectory)) return true;
                if (File.Exists(transaction.AcknowledgementFile))
                {
                    transaction.Confirm();
                    return true;
                }
                if (launched.HasExited) throw new IOException("The updated application stopped before startup completed.");
                Thread.Sleep(250);
            }
            // The app may be healthy but its first-start extraction is slow. Keep the backup;
            // never roll back over a still-running application or delete the old copy blindly.
            transaction.RecordError("Startup confirmation was not received. Recovery files were retained.");
        }
        catch (Exception error)
        {
            transaction.RecordError(error.Message);
            if (launched is { HasExited: false })
            {
                launched.Kill(true);
                if (!launched.WaitForExit(10_000)) throw new IOException("The updated application is still running. Recovery files were retained.", error);
            }
            transaction.Rollback();
            Process.Start(new ProcessStartInfo(Path.Combine(target, PortableUpdateTransaction.ApplicationName))
            { UseShellExecute = true, WorkingDirectory = target });
            throw;
        }
        finally { launched?.Dispose(); }
        return true;
    }

    private static void RefuseRunningApplication(string target)
    {
        var application = Path.Combine(target, PortableUpdateTransaction.ApplicationName);
        var backend = Path.Combine(target, "Backend", "NInferManager.Backend.exe");
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId) continue;
                string? path;
                try { path = process.MainModule?.FileName; }
                catch { continue; }
                if (string.Equals(path, application, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(path, backend, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Close this portable application's window and backend before recovery.");
            }
        }
    }

    private static void WaitForExit(int pid)
    {
        if (pid <= 0 || pid == Environment.ProcessId) throw new ArgumentException("Invalid application process ID.");
        Process process;
        try { process = Process.GetProcessById(pid); }
        catch (ArgumentException) { return; }
        using (process)
            if (!process.WaitForExit(60_000))
                throw new TimeoutException("Close NInferEZ Manager before installing this update. No application files were changed.");
    }
}

/// <summary>A bounded, journalled replacement of application files, never user data or engines.</summary>
public sealed class PortableUpdateTransaction
{
    public const string ApplicationName = "NInferEZ Manager.exe";
    public const string RecoveryDirectoryName = ".update-recovery";
    private const long MaximumExpandedBytes = 2L * 1024 * 1024 * 1024;
    private static readonly HashSet<string> Preserved = new(StringComparer.OrdinalIgnoreCase)
        { "Data", "Models", "Engine", "portable.mode" };
    private static readonly HashSet<string> Components = new(StringComparer.OrdinalIgnoreCase)
        { ApplicationName, "Backend", "Assets", "Docs" };
    private readonly string _target;
    private readonly string _recovery;
    private readonly string _staging;
    private readonly string _backup;
    private readonly string _journalPath;
    private readonly Action<string>? _checkpoint;

    public PortableUpdateTransaction(string target, Action<string>? checkpoint = null)
    {
        _target = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar);
        if (!File.Exists(Path.Combine(_target, "portable.mode")) ||
            (!File.Exists(Path.Combine(_target, ApplicationName)) &&
             !File.Exists(Path.Combine(_target, RecoveryDirectoryName, "backup", ApplicationName))))
            throw new InvalidOperationException("Select the existing portable application folder.");
        RejectReparsePoint(_target);
        _recovery = Path.Combine(_target, RecoveryDirectoryName);
        _staging = Path.Combine(_recovery, "staging");
        _backup = Path.Combine(_recovery, "backup");
        _journalPath = Path.Combine(_recovery, "journal.json");
        _checkpoint = checkpoint;
    }

    public string AcknowledgementFile => Path.Combine(_recovery, "launch-ack");
    public string RecoveryDirectory => _recovery;

    public void Prepare(string archivePath, string expectedSha256)
    {
        if (expectedSha256.Length != 64 || !expectedSha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("A verified SHA-256 digest is required.");
        using (var stream = File.OpenRead(archivePath))
            if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The update file changed after verification.");
        if (Directory.Exists(_recovery))
        {
            RejectReparseTree(_recovery);
            if (!File.Exists(_journalPath))
            {
                if (Directory.Exists(_backup) && Directory.EnumerateFileSystemEntries(_backup).Any())
                    throw new InvalidDataException("Recovery metadata is missing but saved application files exist. Keep this folder and contact support.");
                if (Directory.EnumerateFileSystemEntries(_recovery).Any(path => Path.GetFileName(path) is not ("staging" or "backup" or "journal.json.tmp")))
                    throw new InvalidDataException("The unfinished recovery folder contains unexpected files; it was preserved.");
                DeleteOwnedDirectory(_recovery);
            }
            else
            {
                var previous = ReadJournal();
                if (previous.State is "RolledBack" or "Confirmed") DeleteOwnedDirectory(_recovery);
                else if (previous.State == "AwaitingConfirmation" && File.Exists(AcknowledgementFile)) Confirm();
                else throw new InvalidOperationException("A previous update has recovery files. Recover or confirm it before trying again.");
            }
        }
        Directory.CreateDirectory(_staging);
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            if (archive.Entries.Count is 0 or > 5000) throw new InvalidDataException("The update archive has an invalid entry count.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long expanded = 0;
            foreach (var entry in archive.Entries)
            {
                var relative = entry.FullName.Replace('\\', '/');
                var parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;
                if (relative.StartsWith('/') || relative.Contains(':') || parts.Any(x => x is "." or ".." || x.EndsWith(' ') || x.EndsWith('.')))
                    throw new InvalidDataException("The update archive contains an unsafe path.");
                if ((entry.ExternalAttributes >> 16 & 0xF000) == 0xA000)
                    throw new InvalidDataException("Update archives must not contain symbolic links.");
                if (Preserved.Contains(parts[0])) continue;
                if (!Components.Contains(parts[0])) throw new InvalidDataException("The archive contains an unexpected application component.");
                var destination = Under(_staging, relative);
                if (!names.Add(destination)) throw new InvalidDataException("The update archive contains duplicate paths.");
                expanded = checked(expanded + entry.Length);
                if (expanded > MaximumExpandedBytes) throw new InvalidDataException("The update archive exceeds its safety limit.");
                if (relative.EndsWith('/')) { Directory.CreateDirectory(destination); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, false);
            }
            if (!File.Exists(Path.Combine(_staging, ApplicationName)) ||
                !File.Exists(Path.Combine(_staging, "Backend", "NInferManager.Backend.exe")) ||
                !File.Exists(Path.Combine(_staging, "Assets", "NInferEZ.ico")))
                throw new InvalidDataException("The update is missing an application, backend or icon.");
            Directory.CreateDirectory(_backup);
            var units = Directory.EnumerateFileSystemEntries(_staging)
                .Select(path => new UpdateUnit(Path.GetFileName(path), Exists(Under(_target, Path.GetFileName(path)))))
                .ToArray();
            WriteJournal(new UpdateJournal(1, _target, "Prepared", units, null));
        }
        catch { DeleteOwnedDirectory(_recovery); throw; }
    }

    public void Apply()
    {
        var journal = ReadJournal();
        if (journal.State != "Prepared") throw new InvalidOperationException("The update has not been prepared.");
        foreach (var unit in journal.Units) RejectReparseTree(Under(_target, unit.Name));
        WriteJournal(journal with { State = "Applying" });
        try
        {
            foreach (var unit in journal.Units)
            {
                var destination = Under(_target, unit.Name);
                var replacement = Under(_staging, unit.Name);
                var saved = Under(_backup, unit.Name);
                RejectReparseTree(destination);
                if (unit.HadOriginal) Move(destination, saved);
                _checkpoint?.Invoke("backup:" + unit.Name);
                Move(replacement, destination);
                _checkpoint?.Invoke("installed:" + unit.Name);
            }
            WriteJournal(journal with { State = "AwaitingConfirmation" });
        }
        catch { Rollback(); throw; }
    }

    public void Rollback()
    {
        var journal = ReadJournal();
        if (journal.State is "RolledBack" or "Confirmed") return;
        foreach (var unit in journal.Units.Reverse())
        {
            var destination = Under(_target, unit.Name);
            var saved = Under(_backup, unit.Name);
            RejectReparseTree(destination);
            RejectReparseTree(saved);
            if (Exists(saved))
            {
                DeleteComponent(destination);
                Move(saved, destination);
            }
            else if (!unit.HadOriginal) DeleteComponent(destination);
        }
        WriteJournal(journal with { State = "RolledBack" });
    }

    public void Confirm()
    {
        var identity = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(_target.ToUpperInvariant())));
        using var mutex = new Mutex(false, @"Local\NInferEZ.UpdateConfirmation." + identity);
        bool acquired;
        try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new IOException("Another process is confirming this update. Try again shortly.");
        try
        {
            // Both the update helper and a late-starting backend can acknowledge startup.
            if (!Directory.Exists(_recovery)) return;
            var journal = ReadJournal();
            if (journal.State is not ("AwaitingConfirmation" or "Confirmed") || !File.Exists(AcknowledgementFile))
                throw new InvalidOperationException("The updated application has not confirmed successful startup.");
            WriteJournal(journal with { State = "Confirmed" });
            DeleteOwnedDirectory(_recovery);
        }
        finally { mutex.ReleaseMutex(); }
    }

    public void RecordError(string error)
    {
        if (!File.Exists(_journalPath)) return;
        var journal = ReadJournal();
        WriteJournal(journal with { Error = error });
    }

    private UpdateJournal ReadJournal()
    {
        RejectReparseTree(_recovery);
        if (new FileInfo(_journalPath).Length > 16384) throw new InvalidDataException("Update recovery metadata exceeds its limit.");
        var journal = JsonSerializer.Deserialize<UpdateJournal>(File.ReadAllText(_journalPath))
            ?? throw new InvalidDataException("Update recovery metadata is missing.");
        if (journal.SchemaVersion != 1 || !journal.Target.Equals(_target, StringComparison.OrdinalIgnoreCase) ||
            journal.State is not ("Prepared" or "Applying" or "AwaitingConfirmation" or "Confirmed" or "RolledBack") ||
            journal.Units is null || journal.Units.Length is 0 or > 4 || journal.Units.Any(x => !Components.Contains(x.Name)) ||
            journal.Units.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != journal.Units.Length)
            throw new InvalidDataException("Update recovery metadata does not match this application.");
        return journal;
    }

    private void WriteJournal(UpdateJournal journal)
    {
        var temporary = _journalPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(journal));
        File.Move(temporary, _journalPath, true);
    }

    private static string Under(string root, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Update path is outside its application folder.");
        return path;
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
    private static void Move(string from, string to)
    {
        if (Directory.Exists(from)) Directory.Move(from, to);
        else File.Move(from, to);
    }
    private static void RejectReparsePoint(string path)
    {
        if (Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("Update targets must not be symbolic links or junctions.");
    }
    private static void RejectReparseTree(string path)
    {
        RejectReparsePoint(path);
        if (Directory.Exists(path))
            foreach (var item in Directory.EnumerateFileSystemEntries(path)) RejectReparseTree(item);
    }
    private void DeleteComponent(string path)
    {
        if (!Components.Contains(Path.GetFileName(path)) || Path.GetDirectoryName(path) != _target)
            throw new InvalidDataException("Refusing to remove a non-application component.");
        DeleteOwnedDirectoryOrFile(path);
    }
    private void DeleteOwnedDirectory(string path)
    {
        if (path != _recovery) throw new InvalidDataException("Refusing to remove an unrelated folder.");
        DeleteOwnedDirectoryOrFile(path);
    }
    private static void DeleteOwnedDirectoryOrFile(string path)
    {
        RejectReparsePoint(path);
        if (Directory.Exists(path))
        {
            foreach (var item in Directory.EnumerateFileSystemEntries(path)) DeleteOwnedDirectoryOrFile(item);
            Directory.Delete(path);
        }
        else if (File.Exists(path)) File.Delete(path);
    }

    private sealed record UpdateUnit(string Name, bool HadOriginal);
    private sealed record UpdateJournal(int SchemaVersion, string Target, string State, UpdateUnit[] Units, string? Error);
}
