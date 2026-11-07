using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using HowToFishTrainer.App;

static class JournalTransactionReviewVerification
{
    static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<string> Passed = new(), Failed = new();
    static void Check(bool ok, string label) => (ok ? Passed : Failed).Add(label);
    static string World(string name) => JsonSerializer.Serialize(new { Name = name, Players = Array.Empty<object>(), WorldItems = Array.Empty<object>() });
    static void Await(Task task)
    {
        var watch = Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            if (watch.ElapsedMilliseconds > 15000) throw new Exception("Dispatcher fixture timeout");
            var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame); Thread.Sleep(1);
        }
        task.GetAwaiter().GetResult();
    }
    static FishingData Data(FishingViewModel vm) => (FishingData)typeof(FishingViewModel).GetField("_data", Hidden)!.GetValue(vm)!;
    static FishingData Copy(FishingData data)
    {
        var method = typeof(FishingData).GetMethod("Copy", Hidden);
        return method == null ? FishingDataStore.Parse(JsonSerializer.Serialize(data)) : (FishingData)method.Invoke(data, null)!;
    }
    static object Measure(Func<FishingData> action)
    {
        for (int i = 0; i < 3; i++) GC.KeepAlive(action()); GC.Collect(); long bytes = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.StartNew();
        for (int i = 0; i < 10; i++) GC.KeepAlive(action()); watch.Stop();
        return new { milliseconds = watch.Elapsed.TotalMilliseconds, bytes = GC.GetAllocatedBytesForCurrentThread() - bytes, iterations = 10 };
    }
    [STAThread] static int Main(string[] args)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        string folder = Path.Combine(args[0], "verification/journal-transaction-review-20261029", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        ThreadPool.GetMinThreads(out int minWorkers, out int minIo); ThreadPool.GetMaxThreads(out int maxWorkers, out int maxIo);
        object? timing = null;
        try
        {
            Check(ThreadPool.SetMinThreads(1, minIo) && ThreadPool.SetMaxThreads(1, maxIo), "Fixture can deterministically hold the only thread-pool worker");
            void PendingSignature(string name, Action<FishingViewModel, string> change, Action<FishingViewModel, string> verify)
            {
                string root = Path.Combine(folder, name), saves = Path.Combine(root, "Saves"); Directory.CreateDirectory(saves); File.WriteAllText(Path.Combine(saves, "World.txt"), World("A"));
                using var vm = new FishingViewModel(Path.Combine(root, "data"), saves, () => false, false); vm.AutoBackup = true;
                typeof(FishingViewModel).GetField("_lastCodexRead", Hidden)!.SetValue(vm, DateTimeOffset.UtcNow.AddDays(1));
                using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
                Task? pending = null;
                ThreadPool.QueueUserWorkItem(_ => { entered.Set(); release.Wait(); });
                if (!entered.Wait(5000)) throw new Exception("Worker blocker did not start");
                try
                {
                    pending = vm.TickAsync(DateTimeOffset.UtcNow);
                    Check(!pending.IsCompleted && !vm.Busy, "Auto signature is pending while settings remain editable: " + name);
                    change(vm, saves);
                }
                finally { release.Set(); }
                Await(pending!); verify(vm, saves);
            }
            PendingSignature("disable", (vm, _) => vm.AutoBackup = false,
                (vm, _) => Check(vm.Backups.Count == 0 && !vm.AutoBackup, "Disabling auto backup cancels a pending automatic snapshot"));
            PendingSignature("root-switch", (vm, saves) => { string next = Path.Combine(Path.GetDirectoryName(saves)!, "OtherSaves"); Directory.CreateDirectory(next); File.WriteAllText(Path.Combine(next, "World.txt"), World("B")); vm.SaveRoot = next; },
                (vm, _) => {
                    Check(vm.Backups.Count == 0, "A pending check from the prior root cannot back up the newly selected root");
                    Await(vm.TickAsync(DateTimeOffset.UtcNow.AddSeconds(1)));
                    Check(vm.Backups.Count == 1 && File.ReadAllText(Path.Combine(vm.Backups[0].Directory, "data", "World.txt")) == World("B"), "The next fresh check backs up the current root normally");
                });
            PendingSignature("disable-reenable", (vm, _) => { vm.AutoBackup = false; vm.AutoBackup = true; },
                (vm, _) => {
                    Check(vm.Backups.Count == 0, "Disable/re-enable invalidates the earlier check even with the same final flag");
                    Await(vm.TickAsync(DateTimeOffset.UtcNow.AddSeconds(1)));
                    Check(vm.Backups.Count == 1, "Re-enabled automatic backup runs on a fresh check");
                });
            PendingSignature("dispose", (vm, _) => vm.Dispose(),
                (vm, _) => Check(vm.Backups.Count == 0 && !vm.Writable, "Disposing during a pending check creates no snapshot"));
            PendingSignature("eligible", (_, _) => { },
                (vm, _) => Check(vm.Backups.Count == 1, "An unchanged eligible check still performs its initial backup"));
            PendingSignature("old-error", (vm, saves) => { vm.AutoBackup = false; File.Delete(Path.Combine(saves, "World.txt")); },
                (vm, _) => Check(vm.Backups.Count == 0 && !vm.BackupStatus.Contains("未执行"), "A canceled check's late error does not overwrite the current status"));
            PendingSignature("current-error", (_, saves) => File.Delete(Path.Combine(saves, "World.txt")),
                (vm, _) => Check(vm.Backups.Count == 0 && vm.BackupStatus.Contains("未执行"), "A current eligible check's real error remains visible"));

            void PendingExit(string name, Action<FishingViewModel, DateTimeOffset, Guid, Action<bool>> finish)
            {
                string directory = Path.Combine(folder, "exit-" + name), path = Path.Combine(directory, "fishing-journal.json");
                bool running = true;
                using var vm = new FishingViewModel(directory, null, () => running, false);
                typeof(FishingViewModel).GetField("_lastCodexRead", Hidden)!.SetValue(vm, DateTimeOffset.UtcNow.AddDays(1));
                vm.Account = "fixture";
                vm.BeginSession();
                var session = Data(vm).Sessions.Single();
                Await(vm.TickAsync(session.Started));
                var exitAt = session.Started.AddSeconds(10);
                byte[] primary = File.ReadAllBytes(path), backup = File.ReadAllBytes(path + ".bak");
                using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    running = false;
                    Await(vm.TickAsync(exitAt));
                    Check(Data(vm).Sessions.Single().Ended == null && vm.Status.Contains("未保存"), "Exit write failure remains visible without committing the session: " + name);
                    Check(File.ReadAllBytes(path).SequenceEqual(primary) && File.ReadAllBytes(path + ".bak").SequenceEqual(backup), "Exit write failure preserves primary and backup bytes: " + name);
                    Await(vm.TickAsync(exitAt.AddMinutes(1)));
                    Check(Data(vm).Sessions.Single().LastActive == session.LastActive, "Failed exit does not advance the old session heartbeat: " + name);
                    Check(vm.ActiveSessionText.Contains("结束待保存") && vm.BuildSummary().Contains("结束待保存"), "An unsaved exit is shown as pending rather than a running timer: " + name);
                    running = true; Await(vm.TickAsync(exitAt.AddMinutes(2)));
                    running = false; Await(vm.TickAsync(exitAt.AddMinutes(3)));
                    vm.SpeciesText = "new catch"; vm.AddCatch();
                    Check(Data(vm).Catches.Count == 0 && Data(vm).Sessions.Count == 1, "A catch cannot enter a session with an unsaved exit: " + name);
                }
                finish(vm, exitAt, session.Id, value => running = value);
                var ended = Data(vm).Sessions.Single(s => s.Id == session.Id);
                Check(ended.Ended == exitAt && ended.LastActive == exitAt && ended.EndReason == "游戏退出", "Exit retry retains the original observed time and reason: " + name);
                var saved = FishingDataStore.Parse(File.ReadAllText(path)).Sessions.Single(s => s.Id == session.Id);
                Check(saved == ended, "Retried exit commits identical memory and disk records: " + name);
            }
            PendingExit("retry", (vm, exitAt, _, _) => Await(vm.TickAsync(exitAt.AddMinutes(2))));
            PendingExit("restarted-game", (vm, exitAt, _, setRunning) => { setRunning(true); Await(vm.TickAsync(exitAt.AddMinutes(2))); });
            PendingExit("manual-finish", (vm, _, _, _) => vm.FinishSession("手动结束"));
            PendingExit("dispose", (vm, _, _, _) => vm.Dispose());
            PendingExit("next-catch", (vm, _, prior, _) => {
                vm.AddCatch();
                Check(Data(vm).Sessions.Count == 2 && Data(vm).Catches.Count == 1 && Data(vm).Catches[0].SessionId != prior,
                    "Adding a catch first commits the pending exit and records into a new session");
            });
            PendingExit("next-session", (vm, _, prior, _) => {
                vm.BeginSession();
                Check(Data(vm).Sessions.Count == 2 && Data(vm).Sessions.Single(s => s.Ended == null).Id != prior,
                    "Beginning a session first commits the old session's pending exit");
            });
            void PendingRecovery(string name, bool addCatch)
            {
                string recoveryDirectory = Path.Combine(folder, "recovery-" + name), recoveryPath = Path.Combine(recoveryDirectory, "fishing-journal.json");
                var started = DateTimeOffset.UtcNow.AddHours(-1);
                var interrupted = new FishingSession(Guid.NewGuid(), "interrupted session", started, started.AddMinutes(1));
                using (var store = new FishingDataStore(recoveryPath)) store.Save(new FishingData { Sessions = new() { interrupted } });
                byte[] primary = File.ReadAllBytes(recoveryPath);
                using var locked = new FileStream(recoveryPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var vm = new FishingViewModel(recoveryDirectory, null, () => false, false);
                typeof(FishingViewModel).GetField("_lastCodexRead", Hidden)!.SetValue(vm, DateTimeOffset.UtcNow.AddDays(1));
                Check(Data(vm).Sessions.Single() == interrupted && vm.Status.Contains("未保存") && File.ReadAllBytes(recoveryPath).SequenceEqual(primary),
                    "A failed crash recovery leaves the original disk and memory record intact: " + name);
                Await(vm.TickAsync(DateTimeOffset.UtcNow));
                Check(Data(vm).Sessions.Single().LastActive == interrupted.LastActive && vm.ActiveSessionText.Contains("结束待保存"),
                    "A crash recovery awaiting save does not resume the old session timer: " + name);
                locked.Dispose();
                if (addCatch) { vm.SpeciesText = "new catch"; vm.AddCatch(); }
                else Await(vm.TickAsync(DateTimeOffset.UtcNow));
                var ended = Data(vm).Sessions.Single(s => s.Id == interrupted.Id);
                Check(ended.Ended == interrupted.LastActive && ended.LastActive == interrupted.LastActive && ended.EndReason == "中断恢复",
                    "Crash recovery retry retains the last persisted heartbeat and recovery reason: " + name);
                Check(FishingDataStore.Parse(File.ReadAllText(recoveryPath)).Sessions.Single(s => s.Id == interrupted.Id) == ended,
                    "Crash recovery retry commits the same completion to memory and disk: " + name);
                if (addCatch) Check(Data(vm).Sessions.Count == 2 && Data(vm).Catches.Count == 1 && Data(vm).Catches[0].SessionId != interrupted.Id,
                    "A catch after failed crash recovery belongs to a new session");
            }
            PendingRecovery("tick", false);
            PendingRecovery("next-catch", true);
            using (var vm = new FishingViewModel(Path.Combine(folder, "offline-session"), null, () => false, false))
            {
                typeof(FishingViewModel).GetField("_lastCodexRead", Hidden)!.SetValue(vm, DateTimeOffset.UtcNow.AddDays(1));
                vm.BeginSession(); var first = Data(vm).Sessions.Single();
                Await(vm.TickAsync(first.Started.AddMinutes(1)));
                Check(Data(vm).Sessions.Single().Ended == null && Data(vm).Sessions.Single().LastActive == first.Started.AddMinutes(1),
                    "A normal offline manual session remains active and receives heartbeats");
                vm.FinishSession("手动结束");
                Check(Data(vm).Sessions.Single().Ended != null && Data(vm).Sessions.Single().EndReason == "手动结束",
                    "A normal offline session still ends by the user's manual action");
            }

            var now = DateTimeOffset.UtcNow; Guid session = Guid.NewGuid();
            var data = new FishingData {
                Version = 1, SaveRoot = "D:\\Fixture Saves", Account = "123", AutoBackup = true,
                LastSpecies = "中文 / fish", LastCount = 7, LastWeightKg = 1.25, LastPlace = "港口", LastEquipment = "鱼竿", LastNote = "备注\n测试", LastSessionName = "夜钓",
                Sessions = new() { new(session, "session", now.AddDays(-2), now, now, "done") },
                Catches = Enumerable.Range(0, 4000).Select(i => new FishingCatch(Guid.NewGuid(), session, now.AddMinutes(-i), "fish " + i % 50, 2, i % 3 == 0 ? null : (double?)1.25, "place", "rod", "note")).ToList()
            };
            string json = JsonSerializer.Serialize(data); var copy = Copy(data);
            Check(JsonSerializer.Serialize(copy) == JsonSerializer.Serialize(FishingDataStore.Parse(json)), "Snapshot preserves every persisted field, null weight, decimals and Unicode");
            Check(!ReferenceEquals(data.Catches, copy.Catches) && !ReferenceEquals(data.Sessions, copy.Sessions), "Snapshot owns independent mutable lists");
            copy.Catches[0] = copy.Catches[0] with { Count = 8 }; copy.Sessions[0] = copy.Sessions[0] with { Name = "edited" };
            Check(data.Catches[0].Count == 2 && data.Sessions[0].Name == "session", "Replacing immutable records in a snapshot cannot edit its source");
            copy.Catches.Add(copy.Catches[0] with { Id = Guid.NewGuid() }); data.Sessions.Clear(); copy.LastNote = "copy-only";
            Check(data.Catches.Count == 4000 && copy.Sessions.Count == 1 && data.LastNote != copy.LastNote, "List membership and scalar fields remain independent in both directions");
            data.Sessions.Add(new(session, "session", now.AddDays(-2), now, now, "done"));
            timing = new { records = 4000, before = Measure(() => FishingDataStore.Parse(JsonSerializer.Serialize(data))), after = Measure(() => Copy(data)) };

            string directory = Path.Combine(folder, "transaction"), path = Path.Combine(directory, "fishing-journal.json");
            using (var store = new FishingDataStore(path)) store.Save(data);
            using (var vm = new FishingViewModel(directory, null, () => false, false))
            {
                var change = typeof(FishingViewModel).GetMethod("Change", Hidden)!;
                bool Apply(Action<FishingData> edit) => (bool)change.Invoke(vm, new object[] { edit })!;
                Check(Apply(d => d.LastNote = "saved") && Data(vm).LastNote == "saved" && FishingDataStore.Parse(File.ReadAllText(path)).LastNote == "saved", "A valid transaction commits matching memory and disk data");
                var original = Data(vm); byte[] primary = File.ReadAllBytes(path), backup = File.ReadAllBytes(path + ".bak");
                using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    Check(!Apply(d => { d.LastNote = "not-committed"; d.Catches.Clear(); }), "Atomic replacement failure rejects the edit");
                Check(ReferenceEquals(Data(vm), original) && File.ReadAllBytes(path).SequenceEqual(primary) && File.ReadAllBytes(path + ".bak").SequenceEqual(backup), "Failed save leaves memory, primary and prior backup unchanged");
                Check(Directory.GetFiles(directory, "*.tmp").Length == 0, "Failed transaction removes temporary files");
                Check(!Apply(d => d.Catches.Add(d.Catches[0] with { Id = Guid.NewGuid(), Count = 0 })) && ReferenceEquals(Data(vm), original), "Structural validation still rejects invalid records without publishing a snapshot");
            }
        }
        catch (Exception error) { Failed.Add("Unhandled fixture error: " + error); }
        finally { ThreadPool.SetMaxThreads(maxWorkers, maxIo); ThreadPool.SetMinThreads(minWorkers, minIo); }
        var report = new { passed = Failed.Count == 0, checks = Passed.Count + Failed.Count, passedChecks = Passed, failedChecks = Failed, timing,
            originalGameSavesModified = false, gameInjected = false, isolatedStorage = folder, assemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(FishingViewModel).Assembly.Location))).ToLowerInvariant() };
        File.WriteAllText(Path.Combine(folder, "results.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var error in Failed) Console.WriteLine("FAIL: " + error);
        Console.WriteLine($"Journal transaction review: {Passed.Count}/{Passed.Count + Failed.Count} passed; {folder}");
        return Failed.Count == 0 ? 0 : 1;
    }
}
