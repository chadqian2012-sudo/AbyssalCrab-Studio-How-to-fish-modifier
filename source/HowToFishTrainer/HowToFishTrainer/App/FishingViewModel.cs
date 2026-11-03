using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;

namespace HowToFishTrainer.App;

public sealed class FishingViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly FishingDataStore _store;
    private readonly SaveBackupService _backup;
    private readonly bool _fileStorage;
    private readonly Func<bool> _gameRunning;
    private bool _gameWasRunning;
    private FishingSession? _pendingSessionEnd;
    private readonly DispatcherTimer? _timer;
    private FishingData _data;
    private bool _disposed, _busy, _tickBusy;
    private string _status = "鱼获由你补记；图鉴读取 local.txt 的发现状态。", _query = "", _codexStatus = "尚未读取存档图鉴";
    private string _species = "", _weight = "", _place = "", _equipment = "", _note = "", _sessionName = "休闲钓鱼";
    private double? _lastValidWeight;
    private string _catchTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    private FishingSession? _selectedSession;
    private FishingCatch? _selectedCatch;
    private SaveBackupEntry? _selectedBackup;
    private DateTimeOffset _lastAutoAttempt = DateTimeOffset.MinValue, _lastCodexRead = DateTimeOffset.MinValue;
    private string? _backupSignature;
    private int _backupContext;
    private readonly HashSet<int> _discovered = new();
    private readonly List<FishingSpecies> _catalog = FishingCatalog.All.ToList();
    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<FishingSession> Sessions { get; } = new();
    public ObservableCollection<FishingCatch> Catches { get; } = new();
    public ObservableCollection<FishingCodexRow> Codex { get; } = new();
    public ObservableCollection<SaveBackupEntry> Backups { get; } = new();
    public ObservableCollection<string> Accounts { get; } = new();
    public IEnumerable<string> SpeciesNames => _catalog.Select(s => s.Name).Concat(_data.Catches.Select(c => c.Species)).Distinct(StringComparer.CurrentCultureIgnoreCase).Order();
    public NumericInput CountInput { get; } = new("数量", 1, 1, 10000, true);
    public bool Busy { get => _busy; private set { Set(ref _busy, value); Changed(nameof(CanEdit)); } }
    public bool CanEdit => !_disposed && !Busy && _store.Writable;
    public bool Writable => !_disposed && _store.Writable;
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string CodexStatus { get => _codexStatus; private set => Set(ref _codexStatus, value); }
    public string SpeciesText { get => _species; set => Set(ref _species, value ?? ""); }
    public string WeightText
    {
        get => _weight;
        set
        {
            if (!Set(ref _weight, value ?? "")) return;
            if (_weight.Trim().Length == 0) _lastValidWeight = null;
            else if (double.TryParse(_weight, NumberStyles.Float, CultureInfo.InvariantCulture, out var w) && double.IsFinite(w) && w > 0 && w <= 1000000) _lastValidWeight = w;
            Changed(nameof(WeightError));
        }
    }
    public string WeightError => _weight.Trim().Length == 0 || (double.TryParse(_weight, NumberStyles.Float, CultureInfo.InvariantCulture, out var w) && double.IsFinite(w) && w > 0 && w <= 1000000) ? "" : "重量须大于 0 且不超过 1000000 kg，支持小数";
    public string Place { get => _place; set => Set(ref _place, value ?? ""); }
    public string Equipment { get => _equipment; set => Set(ref _equipment, value ?? ""); }
    public string Note { get => _note; set => Set(ref _note, value ?? ""); }
    public string CatchTime { get => _catchTime; set => Set(ref _catchTime, value ?? ""); }
    public string SessionName { get => _sessionName; set => Set(ref _sessionName, value ?? ""); }
    public string Query { get => _query; set { if (Set(ref _query, value ?? "")) RefreshRows(); } }
    public FishingSession? SelectedSession { get => _selectedSession; set { if (Set(ref _selectedSession, value)) RefreshRows(); } }
    public FishingCatch? SelectedCatch { get => _selectedCatch; set => Set(ref _selectedCatch, value); }
    public SaveBackupEntry? SelectedBackup { get => _selectedBackup; set => Set(ref _selectedBackup, value); }
    public string SaveRoot
    {
        get => _data.SaveRoot;
        set
        {
            if (Busy) return;
            if (value == _data.SaveRoot) { RefreshAccounts(); return; }
            if (Change(d => d.SaveRoot = value ?? "")) { _backupContext++; Changed(); _backupSignature = null; _lastAutoAttempt = DateTimeOffset.MinValue; RefreshAccounts(); }
        }
    }
    public string Account
    {
        get => _data.Account;
        set
        {
            if (Busy || value == _data.Account) return;
            if (Change(d => d.Account = value ?? "")) { Changed(); _discovered.Clear(); RefreshRows(); _lastCodexRead = DateTimeOffset.MinValue; }
        }
    }
    public bool AutoBackup
    {
        get => _data.AutoBackup;
        set
        {
            if (Busy || value == _data.AutoBackup) return;
            if (Change(d => d.AutoBackup = value)) { _backupContext++; Changed(); _lastAutoAttempt = DateTimeOffset.MinValue; _backupSignature = null; }
        }
    }
    public string ActiveSessionText
    {
        get
        {
            var active = _data.Sessions.SingleOrDefault(s => s.Ended == null);
            return active == null ? "尚未开始本局" : _pendingSessionEnd is FishingSession pending
                ? $"{active.Name} · 结束待保存 · 共 {(pending.Ended!.Value - active.Started).TotalMinutes:0.0} 分钟"
                : $"{active.Name} · 计时 {(DateTimeOffset.UtcNow - active.Started).TotalMinutes:0.0} 分钟";
        }
    }
    public string Summary { get; private set; } = "暂无场次，请先开始一局或记录鱼获。";
    public string PersonalStats { get; private set; } = "";
    public string BackupStatus { get; private set; } = "自动备份每 10 分钟检测磁盘存档变化；恢复只覆盖备份里的同名文件，其余文件保留。";
    public ICommand BeginSessionCommand => new RelayCommand(BeginSession);
    public ICommand FinishSessionCommand => new RelayCommand(() => FinishSession("手动结束"));
    public ICommand AddCatchCommand => new RelayCommand(AddCatch);
    public ICommand RefreshCodexCommand => new RelayCommand(() => _ = ReadCodexAsync());
    public ICommand CreateBackupCommand => new RelayCommand(() => _ = CreateBackupAsync());
    public ICommand RefreshBackupsCommand => new RelayCommand(() => _ = RefreshBackupsAsync());

    public FishingViewModel(string? dataDirectory = null, string? saveRoot = null, Func<bool>? gameRunning = null, bool startTimer = true)
    {
        _fileStorage = dataDirectory != null;
        _gameRunning = gameRunning ?? SaveBackupService.IsGameRunning;
        _store = new(dataDirectory == null ? null : Path.Combine(dataDirectory, "fishing-journal.json"));
        _data = _store.Load();
        _species = _data.LastSpecies; CountInput.Value = _data.LastCount; _weight = _data.LastWeightKg?.ToString("G", CultureInfo.InvariantCulture) ?? "";
        _lastValidWeight = _data.LastWeightKg;
        _place = _data.LastPlace; _equipment = _data.LastEquipment; _note = _data.LastNote; _sessionName = _data.LastSessionName;
        _backup = new(Path.Combine(dataDirectory ?? Path.GetTempPath(), "save-backups"), gameRunning);
        // A crash ends the prior session at its last persisted heartbeat, never at the next launch.
        if (_store.Writable && _data.Sessions.SingleOrDefault(s => s.Ended == null) is FishingSession interrupted)
        {
            _pendingSessionEnd = interrupted with { Ended = interrupted.LastActive, EndReason = "中断恢复" };
            SavePendingSessionEnd();
        }
        if (_data.SaveRoot.Length == 0 && saveRoot != null) Change(d => d.SaveRoot = saveRoot);
        if (_store.Warning.Length != 0) Status = _store.Warning;
        RefreshAll(); RefreshAccounts();
        if (startTimer && dataDirectory != null)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(10) };
            _timer.Tick += TimerTick; _timer.Start();
        }
    }
    private bool Change(Action<FishingData> action)
    {
        if (!Writable) { Status = _store.Warning.Length == 0 ? "当前日志只读" : _store.Warning; return false; }
        try
        {
            var next = _data.Copy(); action(next); _store.Save(next); _data = next; return true;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        { Status = "未保存此次更改 · " + e.Message; return false; }
    }
    public void SetCatalog(IEnumerable<FishingSpecies> species)
    {
        var entries = species.Where(s => s.Id >= 0 && !string.IsNullOrWhiteSpace(s.Name)).DistinctBy(s => s.Id).ToArray();
        if (entries.Length == 0) return;
        _catalog.Clear(); _catalog.AddRange(entries); Changed(nameof(SpeciesNames)); RefreshRows();
    }
    public void BeginSession()
    {
        if (!CanEdit || !SavePendingSessionEnd()) return;
        if (_data.Sessions.Any(s => s.Ended == null)) { Status = "本局正在记录，请先结束再新建"; return; }
        string name = SessionName.Trim();
        if (name.Length is 0 or > 80) { Status = "场次名称需要 1–80 个字符"; return; }
        if (_data.Sessions.Count >= 1000) { Status = "已达 1000 局上限，请先导出并整理日志"; return; }
        var now = DateTimeOffset.UtcNow; var session = new FishingSession(Guid.NewGuid(), name, now, now);
        if (Change(d => { d.Sessions.Add(session); d.LastSessionName = name; })) { RefreshAll(session.Id); Status = "已开始本局 · " + name; }
    }
    public void FinishSession(string reason)
    {
        var completed = _pendingSessionEnd;
        if (completed == null)
        {
            var active = _data.Sessions.SingleOrDefault(s => s.Ended == null); if (active == null) return;
            var now = DateTimeOffset.UtcNow < active.LastActive ? active.LastActive : DateTimeOffset.UtcNow;
            completed = active with { LastActive = now, Ended = now, EndReason = reason };
        }
        if (Change(d => d.Sessions[d.Sessions.FindIndex(s => s.Id == completed.Id)] = completed))
        { _pendingSessionEnd = null; RefreshAll(completed.Id); Status = "本局已结束，可在「每局回顾」导出"; }
    }
    private bool SavePendingSessionEnd()
    {
        if (_pendingSessionEnd == null) return true;
        FinishSession("游戏退出");
        return _pendingSessionEnd == null;
    }
    public void AddCatch()
    {
        if (!CanEdit || !SavePendingSessionEnd()) return;
        string name = SpeciesText.Trim(), weight = WeightText.Trim(); double? kg = null;
        if (name.Length is 0 or > 80 || Place.Length > 120 || Equipment.Length > 120 || Note.Length > 500) { Status = "请填写鱼种；鱼种最多 80 字，地点/装备 120 字，备注 500 字"; return; }
        if (CountInput.HasError) { Status = CountInput.Error; return; }
        if (weight.Length > 0)
        {
            if (!double.TryParse(weight, NumberStyles.Float, CultureInfo.InvariantCulture, out var w) || !double.IsFinite(w) || w <= 0 || w > 1000000)
            { Status = "单条重量须大于 0 且不超过 1000000 kg，允许小数；未知可留空"; return; }
            kg = w;
        }
        if (!DateTime.TryParseExact(CatchTime, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            || dt.Year < 2000 || new DateTimeOffset(dt) > DateTimeOffset.Now.AddMinutes(5))
        { Status = "鱼获时间请输入 yyyy-MM-dd HH:mm，不能晚于当前时间 5 分钟"; return; }
        if (_data.Catches.Count >= 20000) { Status = "已达 20000 条记录上限，请先导出整理"; return; }
        var active = _data.Sessions.SingleOrDefault(s => s.Ended == null);
        if (active == null) { BeginSession(); active = _data.Sessions.SingleOrDefault(s => s.Ended == null); if (active == null) return; }
        var entry = new FishingCatch(Guid.NewGuid(), active.Id, new DateTimeOffset(dt), name, (int)CountInput.Value, kg, Place.Trim(), Equipment.Trim(), Note.Trim());
        if (Change(d => { d.Catches.Add(entry); d.LastSpecies = name; d.LastCount = entry.Count; d.LastWeightKg = kg; d.LastPlace = entry.Place; d.LastEquipment = entry.Equipment; }))
        { RefreshAll(active.Id); CatchTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture); Status = $"已补记 {name} ×{entry.Count}；同批数量的重量按每条填写"; }
    }
    public void DeleteSelectedCatch()
    {
        if (!CanEdit || SelectedCatch == null) return;
        Guid id = SelectedCatch.Id;
        if (Change(d => d.Catches.RemoveAll(c => c.Id == id))) { RefreshAll(); Status = "已删除选中的日志记录"; }
    }
    private void RefreshAll(Guid? selected = null)
    {
        Guid? id = selected ?? SelectedSession?.Id;
        Sessions.Clear(); foreach (var s in _data.Sessions.OrderByDescending(s => s.Started)) Sessions.Add(s);
        _selectedSession = Sessions.FirstOrDefault(s => s.Id == id) ?? Sessions.FirstOrDefault(s => s.Ended == null) ?? Sessions.FirstOrDefault();
        Changed(nameof(SelectedSession)); Changed(nameof(SpeciesNames)); Changed(nameof(ActiveSessionText)); RefreshRows();
    }
    private void RefreshRows()
    {
        var id = SelectedSession?.Id; var query = Query.Trim();
        // Build all-time species totals once; both the codex and global summary use them.
        var totals = new Dictionary<string, (int Count, double? BestKg)>(StringComparer.CurrentCultureIgnoreCase);
        int totalLogged = 0;
        foreach (var entry in _data.Catches)
        {
            totals.TryGetValue(entry.Species, out var prior);
            double? best = prior.BestKg;
            if (entry.WeightKg is double weight && (!best.HasValue || weight > best.Value)) best = weight;
            totals[entry.Species] = (prior.Count + entry.Count, best);
            totalLogged += entry.Count;
        }
        Catches.Clear();
        foreach (var c in _data.Catches.Where(c => (id == null || c.SessionId == id) && (query.Length == 0 || (c.Species + c.Place + c.Equipment + c.Note).Contains(query, StringComparison.CurrentCultureIgnoreCase))).OrderByDescending(c => c.At)) Catches.Add(c);
        SelectedCatch = null;
        Codex.Clear();
        foreach (var s in _catalog.Where(s => query.Length == 0 || s.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
        {
            totals.TryGetValue(s.Name, out var total);
            Codex.Add(new(s.Id, s.Name, _discovered.Contains(s.Id), total.Count, total.BestKg));
        }
        Summary = BuildSummary(); PersonalStats = $"日志共 {totalLogged} 条鱼 · {totals.Count} 种 · {_data.Sessions.Count} 局\n图鉴目录 {_catalog.Count} 种；发现状态来源于所选账号的磁盘存档，不能当作钓获数量。";
        Changed(nameof(Summary)); Changed(nameof(PersonalStats));
    }
    public string BuildSummary()
    {
        var session = SelectedSession; if (session == null) return "暂无场次，请先开始一局或记录鱼获。";
        var fish = _data.Catches.Where(c => c.SessionId == session.Id).ToArray();
        var known = fish.Where(c => c.WeightKg != null).ToArray();
        var biggest = known.OrderByDescending(c => c.WeightKg).FirstOrDefault();
        int records = 0; var best = new Dictionary<string, double>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var c in _data.Catches.OrderBy(c => c.At)) if (c.WeightKg is double w)
        { if ((!best.TryGetValue(c.Species, out var previous) || w > previous) && c.SessionId == session.Id) records++; best[c.Species] = Math.Max(w, best.GetValueOrDefault(c.Species)); }
        var pending = _pendingSessionEnd?.Id == session.Id ? _pendingSessionEnd : null;
        var end = pending?.Ended ?? session.Ended ?? DateTimeOffset.UtcNow;
        return $"{session.Name}\n{session.Started.LocalDateTime:yyyy-MM-dd HH:mm} — {(pending != null ? "结束待保存" : session.Ended == null ? "进行中" : end.LocalDateTime.ToString("MM-dd HH:mm"))}\n\n" +
            $"本局用时  {Math.Max(0, (end - session.Started).TotalMinutes):0.0} 分钟\n记录鱼获  {fish.Sum(c => c.Count)} 条 / {fish.Select(c => c.Species).Distinct(StringComparer.CurrentCultureIgnoreCase).Count()} 种\n" +
            $"已知总重量  {known.Sum(c => c.WeightKg!.Value * c.Count):0.###} kg\n重量未记录  {fish.Where(c => c.WeightKg == null).Sum(c => c.Count)} 条\n" +
            $"本局最大  {(biggest == null ? "暂无重量数据" : $"{biggest.Species} · {biggest.WeightKg:0.###} kg")}\n新增重量纪录  {records} 次\n\n" +
            "统计来源：手动补记日志。图鉴发现状态不计入鱼获；同批数量按单条重量计算。";
    }
    public string ExportCsv(bool all)
    {
        string Cell(string? text)
        {
            text ??= "";
            if (text.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@' || text.StartsWith('\t') || text.StartsWith('\r')) text = "'" + text;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
        var rows = new List<string> { "时间,场次,鱼种,数量,单条重量kg,已知批次重量kg,地点,装备,备注,来源" };
        foreach (var c in _data.Catches.Where(c => all || c.SessionId == SelectedSession?.Id).OrderBy(c => c.At))
            rows.Add(string.Join(",", new[] { c.At.ToString("O"), _data.Sessions.First(s => s.Id == c.SessionId).Name, c.Species, c.Count.ToString(), c.WeightKg?.ToString("G", CultureInfo.InvariantCulture), (c.WeightKg * c.Count)?.ToString("G", CultureInfo.InvariantCulture), c.Place, c.Equipment, c.Note, "手动补记" }.Select(Cell)));
        return string.Join("\r\n", rows);
    }
    public void RefreshAccounts()
    {
        _discovered.Clear(); Accounts.Clear();
        try
        {
            if (!Directory.Exists(SaveRoot)) { CodexStatus = "未找到存档目录，可在存档备份页选择"; RefreshRows(); return; }
            SaveBackupService.NoLinks(SaveRoot);
            if (File.Exists(Path.Combine(SaveRoot, "local.txt"))) Accounts.Add("旧版目录");
            foreach (var dir in Directory.GetDirectories(SaveRoot).Where(d => Path.GetFileName(d).All(char.IsAsciiDigit))) { SaveBackupService.NoLinks(dir); Accounts.Add(Path.GetFileName(dir)); }
            if (!Accounts.Contains(Account) && Accounts.Count == 1) Account = Accounts[0];
            CodexStatus = Accounts.Count > 1 && !Accounts.Contains(Account) ? "请选择要读取图鉴的账号" : "点击刷新，从磁盘读取图鉴";
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { CodexStatus = "无法读取账号目录 · " + e.Message; }
        RefreshRows();
    }
    public async Task ReadCodexAsync()
    {
        if (Busy || _disposed) return;
        if (!Accounts.Contains(Account)) RefreshAccounts();
        if (!Accounts.Contains(Account)) { CodexStatus = "请先选择存档账号"; return; }
        string account = Account, root = SaveRoot;
        Busy = true;
        try
        {
            var ids = await Task.Run(() =>
            {
                string file = Path.Combine(root, account == "旧版目录" ? "" : account, "local.txt"); SaveBackupService.NoLinks(file);
                if (new FileInfo(file).Length > 16 * 1024 * 1024) throw new IOException("图鉴存档超过 16 MB");
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var memory = new MemoryStream();
                var buffer = new byte[81920]; int count;
                while ((count = stream.Read(buffer, 0, buffer.Length)) != 0)
                {
                    if (memory.Length + count > 16 * 1024 * 1024) throw new IOException("图鉴存档在读取过程中超过 16 MB");
                    memory.Write(buffer, 0, count);
                }
                using var doc = JsonDocument.Parse(memory.GetBuffer().AsMemory(0, (int)memory.Length));
                if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("CreaturesToList", out var entries) || entries.ValueKind != JsonValueKind.Array) throw new InvalidDataException("存档缺少图鉴数组");
                return entries.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("ID", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt32(out _)).Select(e => e.GetProperty("ID").GetInt32()).ToHashSet();
            });
            if (_disposed || root != SaveRoot || account != Account) return;
            _discovered.Clear(); _discovered.UnionWith(ids); RefreshRows();
            CodexStatus = $"已读取磁盘图鉴 · 当前目录匹配 {_catalog.Count(s => ids.Contains(s.Id))}/{_catalog.Count} 种 · {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException) { _discovered.Clear(); RefreshRows(); CodexStatus = "本次图鉴读取失败 · " + e.Message; }
        finally { Busy = false; }
    }
    public async Task CreateBackupAsync(string reason = "手动备份")
    {
        if (!CanEdit || !_fileStorage || string.IsNullOrWhiteSpace(SaveRoot)) return;
        string root = SaveRoot; _backupContext++; Busy = true;
        try
        {
            var entry = await Task.Run(() => _backup.Create(root, reason));
            _backupSignature = entry.SourceSignature;
            // Only the current comparison baseline needs this potentially large metadata string.
            entry = entry with { SourceSignature = null };
            Backups.Insert(0, entry); SelectedBackup = entry; BackupStatus = "备份完成 · " + entry.Display;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException) { BackupStatus = "备份失败，游戏存档未改动 · " + e.Message; }
        finally { Busy = false; Changed(nameof(BackupStatus)); }
    }
    public async Task RefreshBackupsAsync()
    {
        if (Busy || _disposed || !_fileStorage) return; Busy = true;
        try { var list = await Task.Run(() => _backup.List()); Backups.Clear(); foreach (var b in list) Backups.Add(b); SelectedBackup = Backups.FirstOrDefault(); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException) { BackupStatus = "备份列表读取失败 · " + e.Message; Changed(nameof(BackupStatus)); }
        finally { Busy = false; }
    }
    public async Task RestoreSelectedAsync()
    {
        if (!CanEdit || SelectedBackup == null || !_fileStorage || string.IsNullOrWhiteSpace(SaveRoot)) return;
        string root = SaveRoot; var selected = SelectedBackup; _backupContext++; Busy = true;
        try
        {
            var recovery = await Task.Run(() => _backup.Restore(selected, root));
            Backups.Insert(0, recovery); _backupSignature = null;
            BackupStatus = "恢复完成；当前存档已另存为「恢复前自动备份」。Steam 云同步若提示冲突，请核对本地存档时间。";
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException) { BackupStatus = "恢复未完成 · " + e.Message; }
        finally { Busy = false; Changed(nameof(BackupStatus)); }
    }
    private async void TimerTick(object? sender, EventArgs e) { try { await TickAsync(DateTimeOffset.UtcNow); } catch (Exception error) { Status = "后台记录暂停 · " + error.Message; } }
    public async Task TickAsync(DateTimeOffset now)
    {
        if (_disposed || _tickBusy || Busy) return; _tickBusy = true;
        try
        {
            bool running = _gameRunning();
            var active = _data.Sessions.SingleOrDefault(s => s.Ended == null);
            if (_gameWasRunning && !running && Writable && _pendingSessionEnd == null && active != null)
            {
                var ended = now < active.LastActive ? active.LastActive : now;
                _pendingSessionEnd = active with { LastActive = ended, Ended = ended, EndReason = "游戏退出" };
                Changed(nameof(ActiveSessionText)); Summary = BuildSummary(); Changed(nameof(Summary));
            }
            _gameWasRunning = running;
            bool sessionRecording = SavePendingSessionEnd();
            active = _data.Sessions.SingleOrDefault(s => s.Ended == null);
            if (sessionRecording && active != null && now - active.LastActive >= TimeSpan.FromSeconds(30))
            {
                Change(d => d.Sessions[d.Sessions.FindIndex(s => s.Id == active.Id)] = active with { LastActive = now });
                Changed(nameof(ActiveSessionText)); Summary = BuildSummary(); Changed(nameof(Summary));
            }
            if (now - _lastCodexRead >= TimeSpan.FromSeconds(30)) { _lastCodexRead = now; await ReadCodexAsync(); }
            if (!_disposed && AutoBackup && Writable && now - _lastAutoAttempt >= TimeSpan.FromMinutes(10))
            {
                _lastAutoAttempt = now;
                string root = SaveRoot; int context = _backupContext;
                bool CurrentCheck() => !_disposed && AutoBackup && Writable && context == _backupContext && root == SaveRoot;
                string sig; try { sig = await Task.Run(() => _backup.Signature(root)); }
                catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { if (CurrentCheck()) { BackupStatus = "自动备份未执行 · " + e.Message; Changed(nameof(BackupStatus)); } return; }
                if (!CurrentCheck()) return;
                if (sig != _backupSignature) await CreateBackupAsync("自动备份");
            }
        }
        finally { _tickBusy = false; }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _backupContext++;
        _timer?.Stop(); if (_timer != null) _timer.Tick -= TimerTick;
        if (Writable) Change(d =>
        {
            if (SpeciesText.Trim().Length <= 80) d.LastSpecies = SpeciesText.Trim();
            d.LastCount = (int)CountInput.LastValidValue;
            d.LastWeightKg = _lastValidWeight;
            if (Place.Length <= 120) d.LastPlace = Place;
            if (Equipment.Length <= 120) d.LastEquipment = Equipment;
            if (Note.Length <= 500) d.LastNote = Note;
            if (SessionName.Trim().Length is > 0 and <= 80) d.LastSessionName = SessionName.Trim();
        });
        FinishSession("退出时结束"); _disposed = true; _store.Dispose(); Changed(nameof(CanEdit));
    }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; Changed(name); return true; }
}
