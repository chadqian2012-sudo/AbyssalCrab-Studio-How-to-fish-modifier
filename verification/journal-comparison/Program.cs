using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using System.Threading;
using HowToFishTrainer.App;

internal static class Program
{
    private static readonly (int Catches, int Sessions)[] Sizes = [(200, 1), (4000, 1), (20000, 1), (20000, 100)];
    private static readonly string[] Terms = ["鲈", "湖", "不存在", "é", "e\u0301", "I", "İ", "岸边", "  ", "鱼北"];
    private static readonly DateTimeOffset Epoch = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static Guid Id(int number, short type) => new(number + 1, type, 0, new byte[8]);
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string FileName(int count, int sessions) => $"catches-{count}-sessions-{sessions}.json";

    // Generate once. Every backend reads these very same bytes, including old-format display fields when prepared with r30.
    private static void Prepare(string input)
    {
        Directory.CreateDirectory(input);
        foreach (var (count, sessions) in Sizes)
        {
            var data = new FishingData { LastSpecies = "鲈鱼", LastCount = 2, LastWeightKg = .25, LastNote = "手记", LastEquipment = "试验鱼竿" };
            for (int i = 0; i < sessions; i++)
                data.Sessions.Add(new(Id(i, 1), $"场次 {i}", Epoch, Epoch.AddDays(1), Epoch.AddDays(2), "完成"));
            string[] species = ["鲈鱼", "Café", "Cafe\u0301", "I", "İ", "黑鱼", "Eel"];
            for (int i = 0; i < count; i++)
            {
                var at = Epoch.AddSeconds((i * 73 % 3001) / 2).ToOffset(TimeSpan.FromHours(i % 3 * 4));
                data.Catches.Add(new(Id(i, 2), data.Sessions[i % sessions].Id, at, species[i % species.Length],
                    i % 6 + 1, i % 7 == 0 ? null : (i * 11 % 90) / 8d + .25, "北岸", "边钓", i % 3 == 0 ? "湖" : ""));
            }
            File.WriteAllBytes(Path.Combine(input, FileName(count, sessions)), JsonSerializer.SerializeToUtf8Bytes(data));
        }
    }

    // Warm JIT and file-replacement paths first. Each value is the median cost of one whole operation in milliseconds.
    private static double Time(Action run, int samples, int batch = 1)
    {
        run(); run();
        var times = new double[samples];
        for (int i = 0; i < samples; i++)
        {
            long start = Stopwatch.GetTimestamp();
            for (int j = 0; j < batch; j++) run();
            times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds / batch;
        }
        Array.Sort(times);
        return times[samples / 2];
    }

    private static object Check(string input, string output, int count, int sessions)
    {
        var name = FileName(count, sessions);
        var source = File.ReadAllBytes(Path.Combine(input, name));
        var data = FishingDataStore.Parse(Encoding.UTF8.GetString(source));
        string sandbox = Path.Combine(output, name + "-run");
        Directory.CreateDirectory(sandbox);
        string file = Path.Combine(sandbox, "fishing-journal.json");
        // Keep benchmark disk writes confined to a newly created results directory.
        File.Copy(Path.Combine(input, name), file);
        double memoryMs, replacementMs;
        using (var store = new FishingDataStore(null)) memoryMs = Time(() => store.Save(data), 9);
        using (var store = new FishingDataStore(file))
        {
            replacementMs = Time(() => store.Save(data), 7);
            var again = store.Load();
            Require(data.Catches.SequenceEqual(again.Catches) && data.Sessions.SequenceEqual(again.Sessions)
                && JsonSerializer.Serialize(data) == JsonSerializer.Serialize(again), "Saved journal did not round-trip");
        }
        var saved = File.ReadAllBytes(file);
        var openTimes = new List<double>();
        var firstSwitchTimes = new List<double>();
        for (int i = 0; i < 9; i++)
        {
            long start = Stopwatch.GetTimestamp();
            using var opened = new FishingViewModel(sandbox, null, () => false, false);
            double open = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            start = Stopwatch.GetTimestamp();
            opened.SelectedSession = sessions > 1 ? opened.Sessions[1] : null;
            double firstSwitch = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (i >= 2) { openTimes.Add(open); firstSwitchTimes.Add(firstSwitch); }
        }
        double loadMs = openTimes.Order().ElementAt(3), firstSwitchMs = firstSwitchTimes.Order().ElementAt(3);
        using var view = new FishingViewModel(sandbox, null, () => false, false);
        var first = view.Sessions[0]; var other = view.Sessions.Count > 1 ? view.Sessions[1] : null;
        view.SelectedSession = first;
        int pos = 0;
        double typeMs = Time(() => view.Query = Terms[pos++ % Terms.Length], 15, 4);
        view.Query = "鱼";
        double switchMs = Time(() => view.SelectedSession = view.SelectedSession == first ? other : first, 11, 2);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var queries = 0;
        Span<byte> idBuffer = stackalloc byte[16];
        foreach (var culture in new[] { "zh-CN", "tr-TR" })
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            foreach (var selected in new[] { first, other, null }.Distinct())
            {
                view.SelectedSession = selected;
                foreach (var term in Terms)
                {
                    view.Query = term;
                    var q = term.Trim();
                    var expected = data.Catches.Where(c => (selected == null || c.SessionId == selected.Id)
                        && (q.Length == 0 || (c.Species + c.Place + c.Equipment + c.Note).Contains(q, StringComparison.CurrentCultureIgnoreCase)))
                        .OrderByDescending(c => c.At).Select(c => c.Id);
                    Require(view.Catches.Select(c => c.Id).SequenceEqual(expected), $"Search mismatch: {name} / {culture} / {term}");
                    digest.AppendData(BitConverter.GetBytes(view.Catches.Count));
                    foreach (var c in view.Catches) { c.Id.TryWriteBytes(idBuffer); digest.AppendData(idBuffer); }
                    digest.AppendData(JsonSerializer.SerializeToUtf8Bytes(new { view.Summary, view.PersonalStats, view.Codex }));
                    queries++;
                }
            }
        }
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("zh-CN");
        return new { count, sessions, inputSha256 = Hash(source), savedSha256 = Hash(saved), bytes = saved.Length,
            parityQueries = queries, resultSha256 = Convert.ToHexString(digest.GetHashAndReset()), memoryMs, replacementMs, loadMs, firstSwitchMs, typeMs, switchMs };
    }

    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }

    private static void CheckEdits(string directory)
    {
        using var view = new FishingViewModel(directory, null, () => false, false);
        view.BeginSession();
        var session = view.SelectedSession!;
        view.Query = "鲈";
        view.SpeciesText = "鲈鱼";
        view.AddCatch();
        var added = view.Catches.Single();
        view.SelectedSession = null;
        Require(view.Catches.Single().Id == added.Id, "Added catch missing from all sessions");
        view.SelectedSession = session;
        view.SelectedCatch = view.Catches.Single();
        string path = Path.Combine(directory, "fishing-journal.json");
        using (var held = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            view.DeleteSelectedCatch();
            Require(view.Catches.Single().Id == added.Id && view.Status.Contains("未保存"), "Failed deletion changed the search results");
        }
        view.DeleteSelectedCatch();
        view.SelectedSession = null;
        Require(view.Catches.Count == 0 && FishingDataStore.Parse(File.ReadAllText(path)).Catches.Count == 0,
            "Deleted catch still present in the index or saved journal");
    }

    private static void ReadBack(string inputs, string results)
    {
        foreach (var (count, sessions) in Sizes)
        {
            string name = FileName(count, sessions);
            var expected = FishingDataStore.Parse(File.ReadAllText(Path.Combine(inputs, name)));
            using var store = new FishingDataStore(Path.Combine(results, name + "-run", "fishing-journal.json"));
            var actual = store.Load();
            Require(store.Warning.Length == 0 && JsonSerializer.Serialize(actual) == JsonSerializer.Serialize(expected),
                "Reader did not recover all fields: " + name);
        }
    }

    private static void Compare(string baseline, string managed, string rust)
    {
        static JsonElement Rows(string directory, string backend)
        {
            using var report = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "measurement.json")));
            Require(report.RootElement.GetProperty("backend").GetString() == backend, "Unexpected backend in report");
            return report.RootElement.GetProperty("rows").Clone();
        }
        var oldRows = Rows(baseline, "baseline");
        var newRows = Rows(managed, "managed");
        var rustRows = Rows(rust, "rust");
        Require(new[] { oldRows, newRows, rustRows }.All(rows => rows.GetArrayLength() == Sizes.Length), "Missing fixture");
        for (int i = 0; i < Sizes.Length; i++)
        {
            foreach (string key in new[] { "inputSha256", "resultSha256" })
                Require(newRows[i].GetProperty(key).GetString() == oldRows[i].GetProperty(key).GetString()
                    && rustRows[i].GetProperty(key).GetString() == oldRows[i].GetProperty(key).GetString(), key + " mismatch");
            Require(newRows[i].GetProperty("savedSha256").GetString() == rustRows[i].GetProperty("savedSha256").GetString(),
                "Backends produced different files");
        }
        Console.WriteLine("All inputs, search results, summaries and codex rows match r30. Both backends saved identical files.");
    }

    [STAThread]
    public static void Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("zh-CN");
        CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        if (args.Length == 2 && args[0] == "prepare") { Prepare(args[1]); return; }
        if (args.Length == 2 && args[0] == "edits") { CheckEdits(args[1]); return; }
        if (args.Length == 3 && args[0] == "readback") { ReadBack(args[1], args[2]); return; }
        if (args.Length == 4 && args[0] == "compare") { Compare(args[1], args[2], args[3]); return; }
        if (args.Length != 3 || args[0] is not ("baseline" or "managed" or "rust"))
            throw new ArgumentException("prepare INPUT, readback INPUT RESULTS, compare BASELINE MANAGED RUST, or {baseline|managed|rust} INPUT NEW_OUTPUT");
        bool rust = args[0] == "rust";
        var app = typeof(FishingDataStore).Assembly;
        var native = app.GetType("HowToFishTrainer.App.FishingNative");
        if (args[0] != "baseline")
        {
            var enabled = native?.GetProperty("Enabled", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Require(enabled?.GetValue(null) is bool actual && actual == rust, "Requested backend is unavailable");
        }
        Directory.CreateDirectory(args[2]);
        var rows = Sizes.Select(s => Check(args[1], args[2], s.Catches, s.Sessions)).ToArray();
        CheckEdits(Path.Combine(args[2], "edits"));
        File.WriteAllText(Path.Combine(args[2], "measurement.json"), JsonSerializer.Serialize(new { backend = args[0],
            appSha256 = Hash(File.ReadAllBytes(app.Location)), rows }, Json));
    }
}
