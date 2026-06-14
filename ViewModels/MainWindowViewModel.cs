using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using DiskHealthAdvisor.Helpers;
using DiskHealthAdvisor.Models;
using DiskHealthAdvisor.Services;
using DiskHealthAdvisor.Services.Database;
using DiskHealthAdvisor.Services.DiskProviders;
using DiskHealthAdvisor.Services.HealthAnalysis;

namespace DiskHealthAdvisor.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private static readonly TimeSpan MonitorPulseInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan FullDiskScanInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ProcessWriteEventCooldown = TimeSpan.FromMinutes(10);
    private const ulong SignificantProcessWriteBytesPerSecond = 1_000_000;

    private readonly IDiskInfoProvider _diskProvider;
    private readonly DiskHealthAnalyzer _analyzer;
    private readonly HistoryService _historyService;
    private readonly DiskMonitorEventService _monitorEventService;
    private readonly SsdTbwDatabaseService _tbwDatabase;
    private readonly ProcessDiskActivityService _processActivityService;
    private readonly SettingsService _settingsService;
    private readonly ReportExportService _reportExportService;
    private readonly OnlineTbwLookupService _onlineTbwLookupService;
    private readonly InvestigationEngine _investigationEngine;
    private readonly InvestigationExportService _investigationExportService;
    private readonly SmartCtlBootstrapService _smartCtlBootstrapService;
    private readonly LocalUpdateService _localUpdateService;
    private readonly IDialogService _dialogService;
    private readonly AppLogger _logger;
    private readonly Dictionary<string, HealthReport> _reports = [];
    private readonly Dictionary<string, SsdTbwRecord?> _tbwRecords = [];
    private readonly Dictionary<string, DiskSnapshot> _lastMonitorSnapshots = [];
    private readonly DispatcherTimer _monitorTimer;
    private IReadOnlyList<ProcessDiskActivity> _lastProcessActivities = [];
    private List<DiskSnapshot> _history = [];
    private DateTimeOffset _lastFullMonitorScan = DateTimeOffset.MinValue;
    private AppSettings _settings = new();
    private DiskInfo? _selectedDisk;
    private HealthReport? _selectedReport;
    private SsdResourceSummary? _ssdResource;
    private DiskInvestigation? _selectedInvestigation;
    private bool _isBusy;
    private bool _isMonitoring;
    private bool _isMonitorTickRunning;
    private string _statusMessage = "Готово";
    private string _monitorStatus = "Мониторинг ещё не запускался.";
    private string _manualTbwText = "";
    private string _smartCtlPath = "";
    private string _smartCtlStatus = "smartctl.exe не найден";
    private string _selectedInvestigationAction = "Я сделал резервную копию";
    private string _investigationComment = "";
    private bool _expertMode;
    private string _diagnosticWizardText = "Выберите диск, чтобы увидеть безопасный следующий шаг.";
    private string _toastMessage = "";
    private bool _isToastVisible;
    private int _toastToken;
    private string _selectedTheme = "Океан";
    private string _onlineTbwQuery = "";
    private string _onlineTbwStatus = "Онлайн-поиск ничего не меняет сам. Сначала выберите найденное значение, потом сохраните.";
    private OnlineTbwCandidate? _selectedOnlineTbwCandidate;
    private string _investigationProcessHint = "Выберите расследование, чтобы увидеть текущую запись по процессам.";
    private string _investigationWriteHistoryHint = "Журнал заметной записи появится после наблюдения.";
    private string _localUpdateSourcePath = "";
    private LocalUpdateStatus _localUpdateStatus = new();

    public MainWindowViewModel(
        IDiskInfoProvider diskProvider,
        DiskHealthAnalyzer analyzer,
        HistoryService historyService,
        DiskMonitorEventService monitorEventService,
        SsdTbwDatabaseService tbwDatabase,
        ProcessDiskActivityService processActivityService,
        SettingsService settingsService,
        ReportExportService reportExportService,
        OnlineTbwLookupService onlineTbwLookupService,
        InvestigationEngine investigationEngine,
        InvestigationExportService investigationExportService,
        SmartCtlBootstrapService smartCtlBootstrapService,
        LocalUpdateService localUpdateService,
        IDialogService dialogService,
        AppLogger logger)
    {
        _diskProvider = diskProvider;
        _analyzer = analyzer;
        _historyService = historyService;
        _monitorEventService = monitorEventService;
        _tbwDatabase = tbwDatabase;
        _processActivityService = processActivityService;
        _settingsService = settingsService;
        _reportExportService = reportExportService;
        _onlineTbwLookupService = onlineTbwLookupService;
        _investigationEngine = investigationEngine;
        _investigationExportService = investigationExportService;
        _smartCtlBootstrapService = smartCtlBootstrapService;
        _localUpdateService = localUpdateService;
        _dialogService = dialogService;
        _logger = logger;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        ExportReportCommand = new AsyncRelayCommand(ExportReportAsync, () => SelectedDisk is not null);
        PickSmartCtlCommand = new AsyncRelayCommand(PickSmartCtlAsync);
        InstallSmartCtlCommand = new AsyncRelayCommand(InstallSmartCtlAsync);
        AddTbwCommand = new AsyncRelayCommand(AddTbwAsync, () => SelectedDisk is not null);
        SelectInvestigationCommand = new ParameterRelayCommand<DiskInvestigation>(i => SelectedInvestigation = i);
        MarkInvestigationActionCommand = new AsyncRelayCommand(MarkInvestigationActionAsync, () => SelectedInvestigation is not null);
        RecheckInvestigationCommand = new AsyncRelayCommand(RecheckInvestigationAsync, () => SelectedInvestigation is not null);
        ExportInvestigationCommand = new AsyncRelayCommand(ExportInvestigationAsync, () => SelectedInvestigation is not null);
        CopyShortReportCommand = new RelayCommand(CopyShortReport, () => SelectedDisk is not null);
        SearchOnlineTbwCommand = new AsyncRelayCommand(SearchOnlineTbwAsync, () => SelectedDisk is not null);
        SaveOnlineTbwCommand = new AsyncRelayCommand(SaveOnlineTbwAsync, () => SelectedDisk is not null && SelectedOnlineTbwCandidate is not null);
        ToggleMonitoringCommand = new RelayCommand(ToggleMonitoring);
        RunMaintenanceActionCommand = new ParameterRelayCommand<MaintenanceActionDisplay>(RunMaintenanceAction);
        PickLocalUpdateSourceCommand = new RelayCommand(PickLocalUpdateSource);
        RefreshLocalUpdateCommand = new RelayCommand(RefreshLocalUpdateStatus);
        ApplyLocalUpdateCommand = new AsyncRelayCommand(ApplyLocalUpdateAsync, () => CanApplyLocalUpdate);

        _monitorTimer = new DispatcherTimer
        {
            Interval = MonitorPulseInterval
        };
        _monitorTimer.Tick += OnMonitorTick;

        SeedKnowledgeBase();
    }

    public event EventHandler? RequestApplicationExit;

    public ObservableCollection<DiskInfo> Disks { get; } = [];
    public ObservableCollection<MetricDisplay> DiskDetails { get; } = [];
    public ObservableCollection<MetricDisplay> HistoryDetails { get; } = [];
    public ObservableCollection<MetricDisplay> InvestigationContext { get; } = [];
    public ObservableCollection<MetricDisplay> InvestigationDataReadiness { get; } = [];
    public ObservableCollection<ProcessDiskActivity> ProcessActivities { get; } = [];
    public ObservableCollection<ProcessDiskActivity> InvestigationTopWriters { get; } = [];
    public ObservableCollection<DiskMonitorEvent> InvestigationWriteEvents { get; } = [];
    public ObservableCollection<string> DataWarnings { get; } = [];
    public ObservableCollection<string> ImportantChanges { get; } = [];
    public ObservableCollection<RiskCategoryDisplay> RiskCategories { get; } = [];
    public ObservableCollection<HistoryChart> HistoryCharts { get; } = [];
    public ObservableCollection<KnowledgeEntry> KnowledgeBase { get; } = [];
    public ObservableCollection<OnlineTbwCandidate> OnlineTbwCandidates { get; } = [];
    public ObservableCollection<DiskMonitorEvent> MonitorEvents { get; } = [];
    public ObservableCollection<MetricDisplay> DiskDaySummary { get; } = [];
    public ObservableCollection<MetricDisplay> SelectedDiskDayDetails { get; } = [];
    public ObservableCollection<DiskMonitorEvent> SelectedDiskDayEvents { get; } = [];
    public ObservableCollection<DiskMonitorEvent> SelectedDiskDayProcessEvents { get; } = [];
    public ObservableCollection<MetricDisplay> MaintenanceSummary { get; } = [];
    public ObservableCollection<MaintenanceActionDisplay> MaintenanceActions { get; } = [];
    public ObservableCollection<string> DiskProfileOptions { get; } =
    [
        "Не задан",
        "Системный диск",
        "Игровой диск",
        "Архивный диск",
        "Рабочий диск",
        "Внешний/USB диск",
        "Диск для торрентов",
        "Диск для видео/записи"
    ];
    public ObservableCollection<string> ThemeOptions { get; } =
    [
        "Океан",
        "Графит",
        "Север",
        "Контраст",
        "Неон",
        "Сканер",
        "Пульс",
        "Терминал"
    ];
    public ObservableCollection<DiskInvestigation> Investigations { get; } = [];
    public ObservableCollection<string> InvestigationActions { get; } =
    [
        "Я сделал резервную копию",
        "Я заменил SATA-кабель",
        "Я подключил диск в другой порт",
        "Я улучшил охлаждение",
        "Я поставил радиатор",
        "Я освободил место",
        "Я закрыл программу, которая писала на диск",
        "Другое"
    ];

    public ICommand RefreshCommand { get; }
    public ICommand ExportReportCommand { get; }
    public ICommand PickSmartCtlCommand { get; }
    public ICommand InstallSmartCtlCommand { get; }
    public ICommand AddTbwCommand { get; }
    public ICommand SelectInvestigationCommand { get; }
    public ICommand MarkInvestigationActionCommand { get; }
    public ICommand RecheckInvestigationCommand { get; }
    public ICommand ExportInvestigationCommand { get; }
    public ICommand CopyShortReportCommand { get; }
    public ICommand SearchOnlineTbwCommand { get; }
    public ICommand SaveOnlineTbwCommand { get; }
    public ICommand ToggleMonitoringCommand { get; }
    public ICommand RunMaintenanceActionCommand { get; }
    public ICommand PickLocalUpdateSourceCommand { get; }
    public ICommand RefreshLocalUpdateCommand { get; }
    public ICommand ApplyLocalUpdateCommand { get; }

    public DiskInfo? SelectedDisk
    {
        get => _selectedDisk;
        set
        {
            if (SetProperty(ref _selectedDisk, value))
            {
                UpdateSelectedDisk();
            }
        }
    }

    public HealthReport? SelectedReport
    {
        get => _selectedReport;
        private set => SetProperty(ref _selectedReport, value);
    }

    public SsdResourceSummary? SsdResource
    {
        get => _ssdResource;
        private set => SetProperty(ref _ssdResource, value);
    }

    public DiskInvestigation? SelectedInvestigation
    {
        get => _selectedInvestigation;
        set
        {
            if (SetProperty(ref _selectedInvestigation, value))
            {
                RefreshInvestigationContext();
                BuildDiagnosticWizard();
                NotifyComputed();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public bool IsMonitoring
    {
        get => _isMonitoring;
        private set
        {
            if (SetProperty(ref _isMonitoring, value))
            {
                OnPropertyChanged(nameof(MonitoringButtonText));
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string MonitorStatus
    {
        get => _monitorStatus;
        private set => SetProperty(ref _monitorStatus, value);
    }

    public string InvestigationProcessHint
    {
        get => _investigationProcessHint;
        private set => SetProperty(ref _investigationProcessHint, value);
    }

    public string InvestigationWriteHistoryHint
    {
        get => _investigationWriteHistoryHint;
        private set => SetProperty(ref _investigationWriteHistoryHint, value);
    }

    public string MonitoringButtonText => IsMonitoring ? "Остановить наблюдение" : "Запустить наблюдение";

    public string ManualTbwText
    {
        get => _manualTbwText;
        set => SetProperty(ref _manualTbwText, value);
    }

    public string OnlineTbwQuery
    {
        get => _onlineTbwQuery;
        set => SetProperty(ref _onlineTbwQuery, value);
    }

    public string OnlineTbwStatus
    {
        get => _onlineTbwStatus;
        private set => SetProperty(ref _onlineTbwStatus, value);
    }

    public OnlineTbwCandidate? SelectedOnlineTbwCandidate
    {
        get => _selectedOnlineTbwCandidate;
        set
        {
            if (SetProperty(ref _selectedOnlineTbwCandidate, value) &&
                SaveOnlineTbwCommand is AsyncRelayCommand saveOnlineTbw)
            {
                saveOnlineTbw.RaiseCanExecuteChanged();
            }
        }
    }

    public string SmartCtlPath
    {
        get => _smartCtlPath;
        set => SetProperty(ref _smartCtlPath, value);
    }

    public string SmartCtlStatus
    {
        get => _smartCtlStatus;
        private set => SetProperty(ref _smartCtlStatus, value);
    }

    public string SelectedInvestigationAction
    {
        get => _selectedInvestigationAction;
        set => SetProperty(ref _selectedInvestigationAction, value);
    }

    public string InvestigationComment
    {
        get => _investigationComment;
        set => SetProperty(ref _investigationComment, value);
    }

    public bool ExpertMode
    {
        get => _expertMode;
        set
        {
            if (SetProperty(ref _expertMode, value))
            {
                _settings.ExpertMode = value;
                _ = _settingsService.SaveAsync(_settings);
                OnPropertyChanged(nameof(ExpertModeText));
            }
        }
    }

    public string SelectedDiskProfile
    {
        get
        {
            if (SelectedDisk is null)
            {
                return "Не задан";
            }

            return _settings.DiskProfiles.GetValueOrDefault(SelectedDisk.Identity, SelectedDisk.UserProfile);
        }
        set
        {
            if (SelectedDisk is null || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            SelectedDisk.UserProfile = value;
            _settings.DiskProfiles[SelectedDisk.Identity] = value;
            _ = _settingsService.SaveAsync(_settings);
            OnPropertyChanged();
            BuildDiagnosticWizard();
        }
    }

    public string DiagnosticWizardText
    {
        get => _diagnosticWizardText;
        private set => SetProperty(ref _diagnosticWizardText, value);
    }

    public string ToastMessage
    {
        get => _toastMessage;
        private set => SetProperty(ref _toastMessage, value);
    }

    public bool IsToastVisible
    {
        get => _isToastVisible;
        private set => SetProperty(ref _isToastVisible, value);
    }

    public string SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (SetProperty(ref _selectedTheme, value))
            {
                _settings.ThemeName = value;
                _ = _settingsService.SaveAsync(_settings);
                NotifyThemeChanged();
            }
        }
    }

    public string LocalUpdateSourcePath
    {
        get => _localUpdateSourcePath;
        set
        {
            if (SetProperty(ref _localUpdateSourcePath, value ?? ""))
            {
                _settings.LocalUpdateSourceDirectory = _localUpdateSourcePath;
                _ = _settingsService.SaveAsync(_settings);
                RefreshLocalUpdateStatus();
            }
        }
    }

    public string LocalUpdateStatusText => _localUpdateStatus.Summary;

    public string LocalUpdateProblemText => _localUpdateStatus.Problem;

    public string LocalUpdateCurrentBuildText => _localUpdateStatus.CurrentBuildText;

    public string LocalUpdateSourceBuildText => _localUpdateStatus.SourceBuildText;

    public string LocalUpdateCurrentPathText => string.IsNullOrWhiteSpace(_localUpdateStatus.CurrentExePath)
        ? AppContext.BaseDirectory
        : _localUpdateStatus.CurrentExePath;

    public bool HasLocalUpdateProblem => !string.IsNullOrWhiteSpace(_localUpdateStatus.Problem);

    public bool CanApplyLocalUpdate => _localUpdateStatus.CanApply;

    public string ThemeAccentBrush => ThemeKey switch
    {
        "graphite" => "#A7B0BC",
        "north" => "#56D6C9",
        "contrast" => "#FFB24A",
        "neon" => "#D66BFF",
        "scanner" => "#69FFB5",
        "pulse" => "#FF6B86",
        "terminal" => "#7DFF75",
        _ => "#5E9EFF"
    };

    public string ThemeAccentSoftBrush => ThemeKey switch
    {
        "graphite" => "#2A3038",
        "north" => "#183A3A",
        "contrast" => "#3A2A18",
        "neon" => "#321A45",
        "scanner" => "#12382A",
        "pulse" => "#3A1820",
        "terminal" => "#14351A",
        _ => "#1A2C44"
    };

    private string ThemeKey => NormalizeThemeKey(SelectedTheme);

    public string ThemeNoticeText => $"Тема: {SelectedTheme}. Меняется палитра, акцент, экран загрузки и фоновый рисунок интерфейса.";

    public string ExpertModeText => ExpertMode
        ? "Режим опытного пользователя включён: сырые SMART/NVMe-поля и технические подробности оставлены на виду."
        : "Обычный режим: главное объяснение показывается человеческим языком, технические поля собраны в отдельных вкладках.";

    public string LevelText => SelectedReport is null ? "Нет данных" : FormatHelper.LevelText(SelectedReport.Level);

    public string LevelBrush => SelectedReport?.Level switch
    {
        HealthLevel.Good => "#2FA36B",
        HealthLevel.Caution => "#C9A227",
        HealthLevel.Warning => "#D17A22",
        HealthLevel.Critical => "#D94B4B",
        _ => "#6F7B8A"
    };

    public string SelectedDiskDayTitle => SelectedDisk is null
        ? "Выберите диск"
        : FormatHelper.OptionalString(SelectedDisk.Model);

    public string SelectedDiskDaySubtitle => SelectedDisk is null
        ? "Выберите диск, чтобы увидеть его день отдельно."
        : $"{SelectedDisk.MediaTypeDisplay} · {FormatHelper.Bytes(SelectedDisk.SizeBytes)} · {VolumesText(SelectedDisk)}";

    public string SelectedDiskDayVerdict => BuildSelectedDiskDayVerdict();

    public string SelectedDiskDayEventsHint => SelectedDisk is null
        ? "Нет выбранного диска."
        : SelectedDiskDayEvents.Count == 0
            ? "По этому диску сегодня не было отдельных предупреждений. Это хорошо: журнал не видел резкого ухудшения SMART, температуры или счетчиков."
            : $"События именно этого диска за сегодня: {SelectedDiskDayEvents.Count}.";

    public string SelectedDiskDayProcessHint => SelectedDiskDayProcessEvents.Count == 0
        ? "Заметных всплесков записи процессов сегодня не поймано."
        : "Это общие записи процессов Windows за день. Windows не всегда сообщает точный физический диск, поэтому сверяйте время всплеска с ростом записи выбранного диска.";

    public async Task InitializeAsync()
    {
        _settings = await _settingsService.LoadAsync();
        SmartCtlPath = _settings.SmartCtlPath ?? "";
        ExpertMode = _settings.ExpertMode;
        SelectedTheme = string.IsNullOrWhiteSpace(_settings.ThemeName) ? "Океан" : _settings.ThemeName;
        LocalUpdateSourcePath = _settings.LocalUpdateSourceDirectory ?? "";
        RefreshLocalUpdateStatus();
        RefreshSmartCtlStatus();
        await LoadMonitorEventsAsync();
        await RefreshAsync();
        StartMonitoring();
    }

    private async Task RefreshAsync()
    {
        IsBusy = true;
        StatusMessage = "Читаю данные дисков...";

        try
        {
            _history = await _historyService.LoadAsync();
            var disks = await _diskProvider.GetDisksAsync();
            Disks.Clear();
            _reports.Clear();
            _tbwRecords.Clear();

            foreach (var disk in disks)
            {
                var tbw = await _tbwDatabase.FindForDiskAsync(disk);
                var report = _analyzer.Analyze(disk, _history, tbw);
                disk.HealthBadge = FormatHelper.LevelText(report.Level);
                disk.HealthBadgeBrush = BrushForLevel(report.Level);
                disk.UserProfile = _settings.DiskProfiles.GetValueOrDefault(disk.Identity, "Не задан");
                _reports[disk.Identity] = report;
                _tbwRecords[disk.Identity] = tbw;
                Disks.Add(disk);
            }

            var snapshots = Disks.Select(d => HistoryService.CreateSnapshot(d, _reports[d.Identity])).ToList();
            if (snapshots.Count > 0)
            {
                await _historyService.AddSnapshotsAsync(snapshots);
                _history = await _historyService.LoadAsync();
            }

            await RefreshInvestigationsAsync();
            await RefreshProcessActivityAsync();
            BuildImportantChanges();
            SelectedDisk ??= Disks.FirstOrDefault();
            UpdateSelectedDisk();
            RefreshDiskDaySummary();
            BuildMaintenanceActions();
            StatusMessage = Disks.Count == 0
                ? "Диски не найдены или Windows не отдала данные."
                : $"Обновлено: {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            await _logger.LogAsync("Ошибка при обновлении данных.", ex);
            StatusMessage = "Не удалось обновить данные. Подробности записаны в Logs/app.log.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshProcessActivityAsync()
    {
        ProcessActivities.Clear();
        var activities = await _processActivityService.GetActivityAsync();
        _lastProcessActivities = activities;
        foreach (var activity in activities)
        {
            ProcessActivities.Add(activity);
        }

        await LogProcessWriteEventsAsync(activities, DateTimeOffset.Now);
        RefreshInvestigationContext();
    }

    private async Task LoadMonitorEventsAsync()
    {
        MonitorEvents.Clear();
        foreach (var item in await _monitorEventService.LoadTodayAsync())
        {
            MonitorEvents.Add(item);
        }

        RefreshDiskDaySummary();
    }

    private void ToggleMonitoring()
    {
        if (IsMonitoring)
        {
            StopMonitoring();
        }
        else
        {
            StartMonitoring();
        }
    }

    private void StartMonitoring()
    {
        if (IsMonitoring)
        {
            return;
        }

        IsMonitoring = true;
        MonitorStatus = "Наблюдение включено: лёгкая проверка каждые 10 секунд, полный опрос дисков примерно раз в минуту.";
        _monitorTimer.Start();
        _ = MonitorTickAsync(forceFullScan: true);
    }

    private void StopMonitoring()
    {
        _monitorTimer.Stop();
        IsMonitoring = false;
        MonitorStatus = "Наблюдение остановлено.";
    }

    private async void OnMonitorTick(object? sender, EventArgs e)
    {
        await MonitorTickAsync(forceFullScan: false);
    }

    private async Task MonitorTickAsync(bool forceFullScan)
    {
        if (_isMonitorTickRunning)
        {
            return;
        }

        _isMonitorTickRunning = true;
        try
        {
            var now = DateTimeOffset.Now;
            var activities = await _processActivityService.GetActivityAsync();
            _lastProcessActivities = activities;
            ReplaceProcessActivities(activities);
            await LogProcessWriteEventsAsync(activities, now);

            var shouldFullScan = forceFullScan || now - _lastFullMonitorScan >= FullDiskScanInterval;
            if (!shouldFullScan)
            {
                MonitorStatus = $"Наблюдение активно. Последний лёгкий тик: {DateTime.Now:HH:mm:ss}. Полный опрос дисков будет позже.";
                return;
            }

            _lastFullMonitorScan = now;
            var disks = await _diskProvider.GetDisksAsync();
            var snapshots = new List<DiskSnapshot>();
            var events = new List<DiskMonitorEvent>();

            foreach (var disk in disks)
            {
                var tbw = await _tbwDatabase.FindForDiskAsync(disk);
                var report = _analyzer.Analyze(disk, _history, tbw);
                var snapshot = HistoryService.CreateSnapshot(disk, report);
                var previous = _lastMonitorSnapshots.GetValueOrDefault(disk.Identity) ??
                               _history.Where(s => s.DiskIdentity == disk.Identity).OrderByDescending(s => s.Timestamp).FirstOrDefault();

                snapshots.Add(snapshot);
                events.AddRange(BuildMonitorEvents(disk, snapshot, previous, report));
                _lastMonitorSnapshots[disk.Identity] = snapshot;
            }

            if (snapshots.Count > 0)
            {
                await _historyService.AddSnapshotsAsync(snapshots);
                _history.AddRange(snapshots);
            }

            var freshEvents = events
                .Where(e => !HasRecentMonitorEvent(e, TimeSpan.FromMinutes(5)))
                .OrderByDescending(e => e.Timestamp)
                .ToList();

            if (freshEvents.Count > 0)
            {
                await _monitorEventService.AddAsync(freshEvents);
                foreach (var item in freshEvents)
                {
                    MonitorEvents.Insert(0, item);
                }
                RefreshDiskDaySummary();
            }

            MonitorStatus = freshEvents.Count == 0
                ? $"Наблюдение активно. Полный опрос: {DateTime.Now:HH:mm:ss}. Новых проблем не найдено."
                : $"Наблюдение активно. Полный опрос: {DateTime.Now:HH:mm:ss}. Новых событий: {freshEvents.Count}.";
        }
        catch (Exception ex)
        {
            await _logger.LogAsync("Ошибка фонового наблюдения за дисками.", ex);
            MonitorStatus = "Фоновое наблюдение не смогло выполнить проверку. Подробности записаны в Logs/app.log.";
        }
        finally
        {
            _isMonitorTickRunning = false;
        }
    }

    private void ReplaceProcessActivities(IReadOnlyList<ProcessDiskActivity> activities)
    {
        ProcessActivities.Clear();
        foreach (var activity in activities)
        {
            ProcessActivities.Add(activity);
        }

        BuildMaintenanceActions();
        RefreshInvestigationContext();
    }

    private void BuildMaintenanceActions()
    {
        MaintenanceSummary.Clear();
        MaintenanceActions.Clear();

        var selected = SelectedDisk;
        var disksWithTemperature = Disks.Count(d => d.TemperatureCelsius is not null);
        var disksWithWriteCounter = Disks.Count(d => d.TotalBytesWritten is not null);
        var warnings = _reports.Values.Count(r => r.Level is HealthLevel.Caution or HealthLevel.Warning or HealthLevel.Critical);
        var topWriter = _lastProcessActivities
            .OrderByDescending(a => a.WrittenBytesPerSecond.GetValueOrDefault())
            .FirstOrDefault(a => a.WrittenBytesPerSecond.GetValueOrDefault() > 0);

        MaintenanceSummary.Add(new MetricDisplay("Итог", warnings == 0
            ? "Критичных действий прямо сейчас не требуется. Можно выполнить безопасное обслуживание Windows."
            : $"Есть {warnings} пункт(ов), где стоит сначала посмотреть диагностику и рекомендации."));
        MaintenanceSummary.Add(new MetricDisplay("Данные", $"Дисков: {Disks.Count}. Температура есть у {disksWithTemperature}, счетчик записи у {disksWithWriteCounter}."));
        MaintenanceSummary.Add(new MetricDisplay("Выбранный диск", selected is null
            ? "Диск не выбран. Часть советов показана общая."
            : $"{FormatHelper.OptionalString(selected.Model)} · {selected.MediaTypeDisplay} · {FreeSpaceText(selected)}"));
        MaintenanceSummary.Add(new MetricDisplay("Главный писатель", topWriter is null
            ? "Сейчас заметной записи процессов не видно."
            : $"{topWriter.ProcessName}: {topWriter.WrittenRateText}. Если повторяется в простое, проверьте кэш/загрузки этой программы."));

        AddMaintenanceAction(
            "Освободить место",
            "Место",
            selected is null ? "Выберите диск, чтобы увидеть точнее." : BuildFreeSpaceMaintenanceStatus(selected),
            "Открывает штатную страницу Windows «Память». Сначала посмотрите рекомендации Windows, потом выбирайте, что удалять.",
            "Безопасно: приложение ничего не удаляет само.",
            "Открыть память Windows",
            "storage",
            selected is not null && CalculateFreeSpacePercent(selected) is < 15 ? "#D17A22" : "#5E9EFF");

        AddMaintenanceAction(
            "Очистка временных файлов",
            "Место",
            "Подходит для Windows Update cache, временных файлов, корзины и старых миниатюр.",
            "Запускает штатную очистку диска Windows. Перед удалением Windows покажет список категорий.",
            "Умеренно безопасно: удаление выполняет Windows после вашего выбора.",
            "Открыть очистку диска",
            "cleanmgr",
            "#5E9EFF");

        AddMaintenanceAction(
            "Оптимизация SSD/TRIM",
            "SSD",
            selected is null
                ? "Для SSD полезно периодически выполнять TRIM через Windows Optimize Drives."
                : selected.MediaType is DiskMediaKind.SSD or DiskMediaKind.SataSSD or DiskMediaKind.NvmeSSD
                    ? "Для выбранного SSD используйте штатную оптимизацию Windows. Это TRIM, не классическая дефрагментация."
                    : "Для HDD Windows может предложить обычную дефрагментацию; для SSD нужен TRIM.",
            "Открывает окно «Оптимизация дисков». Windows сама выбирает корректный режим для SSD/HDD.",
            "Безопасно: используется штатный инструмент Windows.",
            "Открыть оптимизацию",
            "optimize",
            "#2FA36B");

        AddMaintenanceAction(
            "Кто пишет на диск",
            "Нагрузка",
            topWriter is null
                ? "Сейчас сильной записи не видно. Если диск снова начнет шуметь, откройте монитор ресурсов."
                : $"{topWriter.ProcessName} сейчас пишет {topWriter.WrittenRateText}. Проверьте вкладку «День диска» и путь процесса.",
            "Открывает Resource Monitor на Windows. Там можно посмотреть Disk Activity и файлы, которые читает/пишет процесс.",
            "Безопасно: только просмотр.",
            "Открыть монитор ресурсов",
            "resmon",
            topWriter?.WrittenBytesPerSecond >= 1_000_000 ? "#C9A227" : "#5E9EFF");

        AddMaintenanceAction(
            "Проверка файловой системы",
            "Проверка",
            selected is null
                ? "Команда chkdsk /scan проверяет файловую систему онлайн без принудительного ремонта."
                : $"Для разделов {VolumesText(selected)} можно начать с безопасной онлайн-проверки chkdsk /scan.",
            "Кнопка скопирует команду. Запустите ее в терминале администратора, если хотите проверить выбранный раздел.",
            "Осторожно: /scan обычно безопасен, а /f и /r без понимания запускать не стоит.",
            "Скопировать chkdsk /scan",
            "copy-chkdsk",
            "#C9A227");

        AddMaintenanceAction(
            "Проверка системных файлов Windows",
            "Windows",
            "Полезно, если странности похожи не на поломку диска, а на проблемы Windows, обновлений или системных файлов.",
            "Кнопка скопирует DISM + SFC команду. Ее нужно запускать в терминале администратора.",
            "Осторожно: это штатный ремонт Windows, он может идти долго.",
            "Скопировать DISM/SFC",
            "copy-sfc",
            "#5E9EFF");

        AddMaintenanceAction(
            "SMART и smartctl",
            "Данные",
            _smartCtlBootstrapService.Find(SmartCtlPath) is null
                ? "smartctl не найден. Для NVMe/SATA он часто дает температуру, счетчики записи и ошибки лучше, чем Windows."
                : "smartctl найден. Расширенные read-only данные доступны при обновлении диагностики.",
            "Если данных мало, установите smartmontools или укажите путь к smartctl.exe в настройках.",
            "Безопасно: smartctl используется только для чтения.",
            "Открыть настройки",
            "settings",
            _smartCtlBootstrapService.Find(SmartCtlPath) is null ? "#C9A227" : "#2FA36B");

        if (selected is not null && selected.CrcErrors.GetValueOrDefault() > 0)
        {
            AddMaintenanceAction(
                "SATA-кабель / порт",
                "Железо",
                $"У выбранного диска есть CRC errors: {selected.CrcErrors}. Часто это кабель, порт, контакт или питание, а не сам диск.",
                "Скопируйте чеклист: заменить SATA-кабель, другой порт, проверить питание, затем наблюдать, растет ли счетчик.",
                "Безопасно: это ручная проверка железа, без изменения данных.",
                "Скопировать чеклист",
                "copy-cable",
                "#D17A22");
        }

        if (selected?.TemperatureCelsius is not null && selected.TemperatureCelsius >= (selected.MediaType == DiskMediaKind.HDD ? 45 : 60))
        {
            AddMaintenanceAction(
                "Охлаждение диска",
                "Температура",
                $"Температура выбранного диска {selected.TemperatureCelsius}°C. Это уже зона, где стоит проверить airflow/радиатор/нагрузку.",
                "Сверьте температуру с процессами и нагрузкой. Для NVMe часто помогает радиатор, airflow от вентилятора и удаление пыли.",
                "Безопасно: только рекомендации.",
                "Скопировать чеклист",
                "copy-cooling",
                "#D17A22");
        }
    }

    private void AddMaintenanceAction(
        string title,
        string category,
        string status,
        string details,
        string safety,
        string buttonText,
        string actionKind,
        string accentBrush)
    {
        MaintenanceActions.Add(new MaintenanceActionDisplay
        {
            Title = title,
            Category = category,
            Status = status,
            Details = details,
            Safety = safety,
            ButtonText = buttonText,
            ActionKind = actionKind,
            AccentBrush = accentBrush
        });
    }

    private string BuildFreeSpaceMaintenanceStatus(DiskInfo disk)
    {
        var freePercent = CalculateFreeSpacePercent(disk);
        return freePercent is null
            ? "Нет данных о свободном месте по разделам."
            : freePercent < 10
                ? $"Свободно около {freePercent:0.#}%. Для SSD лучше держать хотя бы 10-15% свободно."
                : freePercent < 15
                    ? $"Свободно около {freePercent:0.#}%. Уже близко к нижней границе для комфортной работы SSD."
                    : $"Свободно около {freePercent:0.#}%. По месту выглядит нормально.";
    }

    private void RunMaintenanceAction(MaintenanceActionDisplay? action)
    {
        if (action is null)
        {
            return;
        }

        try
        {
            switch (action.ActionKind)
            {
                case "storage":
                    OpenShellTarget("ms-settings:storagesense");
                    break;
                case "cleanmgr":
                    OpenShellTarget("cleanmgr.exe");
                    break;
                case "optimize":
                    OpenShellTarget("dfrgui.exe");
                    break;
                case "resmon":
                    OpenShellTarget("resmon.exe");
                    break;
                case "settings":
                    StatusMessage = "Откройте шестеренку в верхней панели и проверьте путь к smartctl.exe.";
                    ShowToast("Откройте настройки через шестеренку");
                    break;
                case "copy-chkdsk":
                    CopyMaintenanceText(BuildChkdskCommand(), "Команда chkdsk скопирована");
                    break;
                case "copy-sfc":
                    CopyMaintenanceText("DISM /Online /Cleanup-Image /RestoreHealth\r\nsfc /scannow", "Команды DISM/SFC скопированы");
                    break;
                case "copy-cable":
                    CopyMaintenanceText("Чеклист SATA/CRC:\r\n1. Сделать резервную копию важных данных.\r\n2. Заменить SATA-кабель.\r\n3. Подключить диск в другой SATA-порт.\r\n4. Проверить питание диска.\r\n5. Обновить данные в Disk Health Advisor и смотреть, растет ли CRC errors.", "Чеклист кабеля скопирован");
                    break;
                case "copy-cooling":
                    CopyMaintenanceText("Чеклист охлаждения:\r\n1. Сверить температуру с нагрузкой и процессами.\r\n2. Проверить пыль и airflow корпуса.\r\n3. Для NVMe проверить радиатор/термопрокладку.\r\n4. Убрать постоянную запись в простое.\r\n5. Повторить проверку температуры.", "Чеклист охлаждения скопирован");
                    break;
            }
        }
        catch (Exception ex)
        {
            _ = _logger.LogAsync("Не удалось выполнить действие обслуживания.", ex);
            StatusMessage = "Не удалось выполнить действие обслуживания. Подробности записаны в Logs/app.log.";
            ShowToast("Действие не выполнено");
        }
    }

    private string BuildChkdskCommand()
    {
        var volume = SelectedDisk?.LogicalVolumes
            .Select(v => v.DisplayName)
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v) && v.Contains(':'));
        return string.IsNullOrWhiteSpace(volume)
            ? "chkdsk C: /scan"
            : $"chkdsk {volume.Trim()} /scan";
    }

    private void CopyMaintenanceText(string text, string toast)
    {
        System.Windows.Clipboard.SetText(text);
        StatusMessage = toast + ".";
        ShowToast(toast);
    }

    private static void OpenShellTarget(string target)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = target,
            UseShellExecute = true
        });
    }

    private void PickLocalUpdateSource()
    {
        var selected = _dialogService.PickLocalUpdateSourceDirectory(LocalUpdateSourcePath);
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        LocalUpdateSourcePath = selected;
        ShowToast("Папка локального обновления выбрана");
    }

    private void RefreshLocalUpdateStatus()
    {
        _localUpdateStatus = _localUpdateService.Inspect(LocalUpdateSourcePath);
        OnPropertyChanged(nameof(LocalUpdateStatusText));
        OnPropertyChanged(nameof(LocalUpdateProblemText));
        OnPropertyChanged(nameof(LocalUpdateCurrentBuildText));
        OnPropertyChanged(nameof(LocalUpdateSourceBuildText));
        OnPropertyChanged(nameof(LocalUpdateCurrentPathText));
        OnPropertyChanged(nameof(HasLocalUpdateProblem));
        OnPropertyChanged(nameof(CanApplyLocalUpdate));

        if (ApplyLocalUpdateCommand is AsyncRelayCommand applyLocalUpdate)
        {
            applyLocalUpdate.RaiseCanExecuteChanged();
        }
    }

    private async Task ApplyLocalUpdateAsync()
    {
        RefreshLocalUpdateStatus();
        if (!CanApplyLocalUpdate)
        {
            ShowToast("Локальное обновление не готово");
            return;
        }

        await _localUpdateService.StartApplyAsync(_localUpdateStatus);
        StatusMessage = "Локальное обновление запущено. Приложение закроется, файлы обновятся, затем оно откроется снова.";
        ShowToast("Запускаю локальное обновление");
        RequestApplicationExit?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshDiskDaySummary()
    {
        DiskDaySummary.Clear();

        var events = MonitorEvents
            .OrderByDescending(e => e.Timestamp)
            .ToList();
        var diskEvents = events
            .Where(e => !string.Equals(e.DiskIdentity, "process-activity", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var processEvents = events
            .Where(e => !string.IsNullOrWhiteSpace(e.ProcessName))
            .ToList();
        var criticalCount = events.Count(e => e.Severity == "Critical");
        var warningCount = events.Count(e => e.Severity is "Warning" or "Caution");
        var maxTemperature = Disks
            .Where(d => d.TemperatureCelsius is not null)
            .OrderByDescending(d => d.TemperatureCelsius)
            .FirstOrDefault();
        var topProcess = processEvents
            .GroupBy(e => e.ProcessName)
            .Select(g => new
            {
                ProcessName = g.Key,
                Count = g.Count(),
                MaxWrite = g.Max(e => e.WrittenBytesPerSecond.GetValueOrDefault()),
                MaxProjectedGb = g.Max(e => e.ProjectedDailyWriteGb ?? 0)
            })
            .OrderByDescending(x => x.MaxWrite)
            .FirstOrDefault();
        var recurringProcesses = processEvents
            .GroupBy(e => e.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() >= 3)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Max(e => e.WrittenBytesPerSecond.GetValueOrDefault()))
            .Take(3)
            .Select(g => $"{g.Key}: {g.Count()} раз(а)")
            .ToList();
        var highestWrite = processEvents
            .OrderByDescending(e => e.WrittenBytesPerSecond.GetValueOrDefault())
            .FirstOrDefault();
        var affectedDisks = diskEvents
            .Select(e => e.DiskModel)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var latestEvent = events.FirstOrDefault();
        var knownTemperatureCount = Disks.Count(d => d.TemperatureCelsius is not null);
        var knownWriteCounterCount = Disks.Count(d => d.TotalBytesWritten is not null);
        var smartProblemCount = Disks.Count(d => d.SmartPassed == false);
        var smartOkCount = Disks.Count(d => d.SmartPassed == true);
        var reportsByLevel = _reports.Values
            .GroupBy(r => r.Level)
            .ToDictionary(g => g.Key, g => g.Count());
        var riskyDiskCount =
            reportsByLevel.GetValueOrDefault(HealthLevel.Critical) +
            reportsByLevel.GetValueOrDefault(HealthLevel.Warning) +
            reportsByLevel.GetValueOrDefault(HealthLevel.Caution);

        DiskDaySummary.Add(new MetricDisplay("Итог дня", BuildDayVerdict(events.Count, criticalCount, warningCount, processEvents.Count, riskyDiskCount)));
        DiskDaySummary.Add(new MetricDisplay("Период наблюдения", BuildObservationWindow(events)));
        DiskDaySummary.Add(new MetricDisplay("Покрытие данных", Disks.Count == 0
            ? "Диски не найдены."
            : $"Дисков: {Disks.Count}. Температура есть у {knownTemperatureCount}, счетчик записи у {knownWriteCounterCount}, SMART OK у {smartOkCount}, SMART с проблемой у {smartProblemCount}."));
        DiskDaySummary.Add(new MetricDisplay("Состояние дисков", Disks.Count == 0
            ? "Нет свежего списка дисков."
            : riskyDiskCount == 0
                ? "По свежей диагностике явных проблем не видно."
                : $"Есть диски с вниманием/риском: {riskyDiskCount}. Откройте вкладку «Состояние» и выберите диск с не-нормальным статусом."));
        DiskDaySummary.Add(new MetricDisplay("Температура", maxTemperature is null
            ? "Нет данных о температуре ни по одному диску."
            : $"{FormatHelper.OptionalString(maxTemperature.Model)}: максимум сейчас {maxTemperature.TemperatureCelsius}°C. Источник: {TemperatureSourceText(maxTemperature)}."));
        DiskDaySummary.Add(new MetricDisplay("Запись сегодня", processEvents.Count == 0
            ? "Заметных писателей пока не поймано."
            : $"Поймано {processEvents.Count} всплеск(ов). Самый сильный: {highestWrite?.ProcessName} {FormatHelper.Bytes(highestWrite?.WrittenBytesPerSecond)}/с."));
        DiskDaySummary.Add(new MetricDisplay("Главный писатель", topProcess is null
            ? "Пока нет."
            : $"{topProcess.ProcessName}: максимум {FormatHelper.Bytes(topProcess.MaxWrite)}/с, всплесков {topProcess.Count}, если держать весь день ~{topProcess.MaxProjectedGb:0.#} ГБ."));
        DiskDaySummary.Add(new MetricDisplay("Повторяемость", recurringProcesses.Count == 0
            ? processEvents.Count == 0
                ? "Повторяющихся всплесков пока нет."
                : "Пока это похоже на отдельные всплески, а не постоянную запись одним процессом."
            : $"Повторялись чаще остальных: {string.Join("; ", recurringProcesses)}."));
        DiskDaySummary.Add(new MetricDisplay("Связь с дисками", BuildAffectedDisksText(affectedDisks, processEvents.Count, diskEvents.Count)));
        DiskDaySummary.Add(new MetricDisplay("Последнее событие", latestEvent is null
            ? "Нет событий."
            : $"{latestEvent.TimeText}: {latestEvent.Title}. {FormatEventOwner(latestEvent)}"));
        DiskDaySummary.Add(new MetricDisplay("Что проверить первым", BuildDayNextStep(criticalCount, warningCount, processEvents.Count, recurringProcesses.Count, knownTemperatureCount, knownWriteCounterCount)));
        RefreshSelectedDiskDay();
    }

    private void RefreshSelectedDiskDay()
    {
        SelectedDiskDayDetails.Clear();
        SelectedDiskDayEvents.Clear();
        SelectedDiskDayProcessEvents.Clear();

        if (SelectedDisk is null)
        {
            SelectedDiskDayDetails.Add(new MetricDisplay("Итог", "Выберите диск, чтобы увидеть его дневную сводку."));
            NotifySelectedDiskDayChanged();
            return;
        }

        var disk = SelectedDisk;
        var report = _reports.GetValueOrDefault(disk.Identity);
        var tbw = _tbwRecords.GetValueOrDefault(disk.Identity);
        var trend = CalculateWriteTrend(disk);
        var diskEvents = MonitorEvents
            .Where(e => string.Equals(e.DiskIdentity, disk.Identity, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.Timestamp)
            .ToList();
        var processEvents = MonitorEvents
            .Where(e => !string.IsNullOrWhiteSpace(e.ProcessName))
            .OrderByDescending(e => e.Timestamp)
            .Take(8)
            .ToList();

        foreach (var item in diskEvents.Take(8))
        {
            SelectedDiskDayEvents.Add(item);
        }

        foreach (var item in processEvents)
        {
            SelectedDiskDayProcessEvents.Add(item);
        }

        SelectedDiskDayDetails.Add(new MetricDisplay("Итог по диску", BuildSelectedDiskDayVerdict()));
        SelectedDiskDayDetails.Add(new MetricDisplay("Состояние сейчас", report is null
            ? "Нет свежего отчета по этому диску."
            : $"{FormatHelper.LevelText(report.Level)}. {report.Summary}"));
        SelectedDiskDayDetails.Add(new MetricDisplay("Температура", disk.TemperatureCelsius is null
            ? "Нет данных. Для NVMe/SATA часто помогает smartctl или запуск от администратора."
            : $"{disk.TemperatureCelsius}°C. Источник: {TemperatureSourceText(disk)}."));
        SelectedDiskDayDetails.Add(new MetricDisplay("Запись сегодня", trend.TodayWrittenBytes is null
            ? "Пока нет пары сегодняшних снимков, чтобы посчитать прирост записи."
            : $"{FormatHelper.Bytes(trend.TodayWrittenBytes)} за окно {FormatTrendWindow(trend.TodayWindow)}."));
        SelectedDiskDayDetails.Add(new MetricDisplay("Темп записи", trend.AverageBytesPerDay is null
            ? "Недостаточно истории для честного среднего темпа."
            : $"{FormatBytesPerDay(trend.AverageBytesPerDay.Value)}. Основано на {trend.SnapshotCount} снимк(ах): {trend.TrendSource}."));
        SelectedDiskDayDetails.Add(new MetricDisplay("Ресурс SSD/TBW", BuildTbwForecastText(disk, tbw, trend)));
        SelectedDiskDayDetails.Add(new MetricDisplay("События диска", diskEvents.Count == 0
            ? "Сегодня по этому диску отдельных тревог не было."
            : $"За сегодня событий: {diskEvents.Count}. Последнее: {diskEvents[0].TimeText} - {diskEvents[0].Title}."));
        SelectedDiskDayDetails.Add(new MetricDisplay("Общие записи процессов", processEvents.Count == 0
            ? "Сегодня заметных процессовых всплесков не поймано."
            : $"Поймано {processEvents.Count} последних всплеск(ов). Самый свежий: {processEvents[0].ProcessTitle}, {processEvents[0].WriteRateText}."));
        SelectedDiskDayDetails.Add(new MetricDisplay("Что делать", BuildSelectedDiskDayNextStep(disk, report, tbw, trend, diskEvents.Count, processEvents.Count)));

        NotifySelectedDiskDayChanged();
    }

    private string BuildSelectedDiskDayVerdict()
    {
        if (SelectedDisk is null)
        {
            return "Выберите диск.";
        }

        var disk = SelectedDisk;
        var report = _reports.GetValueOrDefault(disk.Identity);
        var diskEvents = MonitorEvents.Count(e => string.Equals(e.DiskIdentity, disk.Identity, StringComparison.OrdinalIgnoreCase));
        var trend = CalculateWriteTrend(disk);
        var tbw = _tbwRecords.GetValueOrDefault(disk.Identity);
        var tbwWarning = IsTbwForecastShort(disk, tbw, trend);

        if (report?.Level is HealthLevel.Critical)
        {
            return "По этому диску есть серьезный риск. Сначала сделайте резервную копию важных данных, потом разбирайте причины.";
        }

        if (report?.Level is HealthLevel.Warning)
        {
            return "Диск требует внимания: есть признаки повышенного риска. Лучше открыть диагностику и расследование по этому диску.";
        }

        if (diskEvents > 0)
        {
            return "Сегодня по этому диску были отдельные события. Ниже показано, что именно происходило и когда.";
        }

        if (tbwWarning)
        {
            return "Сейчас явной аварии не видно, но темп записи заметный относительно заявленного TBW.";
        }

        if (report?.Level is HealthLevel.Caution)
        {
            return "Диск выглядит рабочим, но есть пункты для наблюдения. Следите за повторением событий и ростом счетчиков.";
        }

        return "По выбранному диску сейчас все выглядит спокойно. Тревожных событий за день не найдено.";
    }

    private WriteTrend CalculateWriteTrend(DiskInfo disk)
    {
        var snapshots = _history
            .Where(s => s.DiskIdentity == disk.Identity && s.TotalBytesWritten is not null)
            .OrderBy(s => s.Timestamp)
            .ToList();

        var now = DateTimeOffset.Now;
        var today = now.ToLocalTime().Date;
        var currentWritten = disk.TotalBytesWritten;
        ulong? todayWritten = null;
        TimeSpan? todayWindow = null;

        var firstToday = snapshots.FirstOrDefault(s => s.Timestamp.ToLocalTime().Date == today);
        if (firstToday?.TotalBytesWritten is not null && currentWritten is not null)
        {
            todayWritten = currentWritten.Value >= firstToday.TotalBytesWritten.Value
                ? currentWritten.Value - firstToday.TotalBytesWritten.Value
                : 0;
            todayWindow = now - firstToday.Timestamp;
        }

        double? averageBytesPerDay = null;
        var source = "история пока короткая";

        if (todayWritten is not null && todayWindow is not null && todayWindow.Value.TotalMinutes >= 30)
        {
            averageBytesPerDay = todayWritten.Value / Math.Max(todayWindow.Value.TotalDays, 1d / 48d);
            source = "сегодняшний темп";
        }
        else if (snapshots.Count >= 2 && currentWritten is not null)
        {
            var first = snapshots.First();
            if (first.TotalBytesWritten is not null)
            {
                var delta = currentWritten.Value >= first.TotalBytesWritten.Value
                    ? currentWritten.Value - first.TotalBytesWritten.Value
                    : 0;
                var window = now - first.Timestamp;
                if (window.TotalHours >= 1)
                {
                    averageBytesPerDay = delta / Math.Max(window.TotalDays, 1d / 24d);
                    source = "средний темп по истории";
                }
            }
        }

        return new WriteTrend(todayWritten, todayWindow, averageBytesPerDay, source, snapshots.Count);
    }

    private string BuildTbwForecastText(DiskInfo disk, SsdTbwRecord? tbw, WriteTrend trend)
    {
        var isSsd = disk.MediaType is DiskMediaKind.SSD or DiskMediaKind.SataSSD or DiskMediaKind.NvmeSSD;
        if (!isSsd)
        {
            return "Для HDD TBW обычно не используют. Важнее SMART-ошибки, температура и поверхность.";
        }

        if (tbw is null || tbw.Tbw <= 0)
        {
            return "Нет TBW в базе. Добавьте TBW во вкладке «Ресурс SSD», тогда прогноз ресурса станет понятнее.";
        }

        if (disk.TotalBytesWritten is null)
        {
            return $"TBW известен: {tbw.Tbw:0.##} ТБ, но Windows/smartctl не отдали общий счетчик записи.";
        }

        var tbwBytes = tbw.Tbw * 1_000_000_000_000m;
        var written = disk.TotalBytesWritten.Value;
        var usedPercent = (decimal)written / tbwBytes * 100m;
        var baseText = $"Записано {FormatHelper.Terabytes(written)} из {tbw.Tbw:0.##} ТБ ({usedPercent:0.#}%).";

        if (written >= tbwBytes)
        {
            return baseText + " Заявленный TBW уже пройден. Это не значит мгновенную поломку, но резервная копия обязательна.";
        }

        if (trend.AverageBytesPerDay is null || trend.AverageBytesPerDay.Value <= 0)
        {
            return baseText + " Темпа записи пока не хватает для прогноза по дням.";
        }

        var remainingBytes = (double)(tbwBytes - written);
        var days = remainingBytes / trend.AverageBytesPerDay.Value;
        return baseText + $" Если писать примерно в таком темпе, до TBW ориентировочно {FormatDuration(days)}.";
    }

    private static bool IsTbwForecastShort(DiskInfo disk, SsdTbwRecord? tbw, WriteTrend trend)
    {
        if (tbw is null || tbw.Tbw <= 0 || disk.TotalBytesWritten is null || trend.AverageBytesPerDay is null || trend.AverageBytesPerDay.Value <= 0)
        {
            return false;
        }

        var remainingBytes = (double)(tbw.Tbw * 1_000_000_000_000m - disk.TotalBytesWritten.Value);
        return remainingBytes > 0 && remainingBytes / trend.AverageBytesPerDay.Value < 365;
    }

    private static string BuildSelectedDiskDayNextStep(
        DiskInfo disk,
        HealthReport? report,
        SsdTbwRecord? tbw,
        WriteTrend trend,
        int diskEventCount,
        int processEventCount)
    {
        if (report?.Level is HealthLevel.Critical or HealthLevel.Warning)
        {
            return "Сначала резервная копия, затем откройте «Расследование» и проверьте конкретные причины.";
        }

        if (diskEventCount > 0)
        {
            return "Сверьте время событий ниже с температурой, нагрузкой и тем, какие программы писали в этот момент.";
        }

        if (disk.MediaType is DiskMediaKind.SSD or DiskMediaKind.SataSSD or DiskMediaKind.NvmeSSD &&
            (tbw is null || tbw.Tbw <= 0))
        {
            return "Добавьте TBW для SSD, чтобы прогноз ресурса был не общим, а привязанным к модели.";
        }

        if (trend.AverageBytesPerDay is null)
        {
            return "Оставьте программу в трее на несколько часов: появится больше точек истории и темп записи станет честнее.";
        }

        if (processEventCount > 0)
        {
            return "Если диск шумит или растет запись, смотрите общие всплески процессов и сверяйте их с ростом записи этого диска.";
        }

        return "Ничего срочного. Можно просто оставить наблюдение включенным и иногда смотреть эту карточку.";
    }

    private void NotifySelectedDiskDayChanged()
    {
        OnPropertyChanged(nameof(SelectedDiskDayTitle));
        OnPropertyChanged(nameof(SelectedDiskDaySubtitle));
        OnPropertyChanged(nameof(SelectedDiskDayVerdict));
        OnPropertyChanged(nameof(SelectedDiskDayEventsHint));
        OnPropertyChanged(nameof(SelectedDiskDayProcessHint));
    }

    private static string FormatTrendWindow(TimeSpan? window)
    {
        if (window is null)
        {
            return "нет данных";
        }

        if (window.Value.TotalHours >= 1)
        {
            return $"{window.Value.TotalHours:0.#} ч";
        }

        return $"{Math.Max(1, window.Value.TotalMinutes):0} мин";
    }

    private static string FormatBytesPerDay(double bytesPerDay)
    {
        if (bytesPerDay <= 0 || double.IsNaN(bytesPerDay) || double.IsInfinity(bytesPerDay))
        {
            return "нет данных";
        }

        return $"{FormatHelper.Bytes((ulong)Math.Min(bytesPerDay, ulong.MaxValue))}/день";
    }

    private static string FormatDuration(double days)
    {
        if (double.IsNaN(days) || double.IsInfinity(days) || days < 0)
        {
            return "нет данных";
        }

        if (days >= 3650)
        {
            return "больше 10 лет";
        }

        if (days >= 730)
        {
            return $"{days / 365d:0.#} года";
        }

        if (days >= 365)
        {
            return "около 1 года";
        }

        if (days >= 60)
        {
            return $"{days / 30d:0.#} месяца";
        }

        return $"{Math.Max(1, days):0} дней";
    }

    private readonly record struct WriteTrend(
        ulong? TodayWrittenBytes,
        TimeSpan? TodayWindow,
        double? AverageBytesPerDay,
        string TrendSource,
        int SnapshotCount);

    private static string BuildDayVerdict(int eventCount, int criticalCount, int warningCount, int processEventCount, int riskyDiskCount)
    {
        if (criticalCount > 0)
        {
            return "Есть критичные события. Сначала сохраните важные данные и проверьте проблемный диск.";
        }

        if (warningCount > 0)
        {
            return "Есть события, за которыми стоит понаблюдать. Откройте детали ниже.";
        }

        if (riskyDiskCount > 0)
        {
            return "Свежая диагностика видит диски, которым нужно внимание, даже если новых событий сегодня мало.";
        }

        if (processEventCount > 0)
        {
            return "По дискам критики не видно, но были заметные всплески записи процессов.";
        }

        if (eventCount > 0)
        {
            return "День спокойный: есть только информационные записи.";
        }

        return "Пока спокойно. Чем дольше программа висит в трее, тем полезнее дневник.";
    }

    private static string BuildObservationWindow(IReadOnlyList<DiskMonitorEvent> events)
    {
        if (events.Count == 0)
        {
            return "Сегодня еще нет событий. Оставьте наблюдение включенным, чтобы появилась картина дня.";
        }

        var first = events.Min(e => e.Timestamp).ToLocalTime();
        var last = events.Max(e => e.Timestamp).ToLocalTime();
        return $"{first:HH:mm} - {last:HH:mm}. Событий: {events.Count}. Чем длиннее окно, тем точнее выводы.";
    }

    private static string BuildAffectedDisksText(IReadOnlyList<string> affectedDisks, int processEventCount, int diskEventCount)
    {
        if (affectedDisks.Count > 0)
        {
            return $"События по дискам: {string.Join(", ", affectedDisks.Take(3))}{(affectedDisks.Count > 3 ? "..." : "")}.";
        }

        if (processEventCount > 0 && diskEventCount == 0)
        {
            return "Сегодня видны процессы, но Windows не всегда привязывает их к физическому диску. Сверяйте время всплеска с ростом записи нужного SSD/HDD.";
        }

        return "Тревог по конкретным дискам сегодня нет.";
    }

    private static string FormatEventOwner(DiskMonitorEvent item)
    {
        if (!string.IsNullOrWhiteSpace(item.ProcessName))
        {
            return $"Процесс: {item.ProcessName}.";
        }

        if (!string.IsNullOrWhiteSpace(item.DiskModel))
        {
            return $"Диск: {item.DiskModel}.";
        }

        return "";
    }

    private static string BuildDayNextStep(
        int criticalCount,
        int warningCount,
        int processEventCount,
        int recurringProcessCount,
        int knownTemperatureCount,
        int knownWriteCounterCount)
    {
        if (criticalCount > 0)
        {
            return "Откройте критичное событие ниже, сделайте резервную копию и повторите проверку.";
        }

        if (warningCount > 0)
        {
            return "Сверьте время предупреждений с нагрузкой, температурой и процессами.";
        }

        if (knownTemperatureCount == 0 || knownWriteCounterCount == 0)
        {
            return "Для лучшей картины включите smartctl/запуск от администратора: тогда появятся температура, SMART и счетчики записи у большего числа дисков.";
        }

        if (recurringProcessCount > 0)
        {
            return "Повторяющиеся процессы проверьте первыми: путь процесса, загрузки, кэш, обновления, запись видео или игровые лаунчеры.";
        }

        if (processEventCount > 0)
        {
            return "Если запись повторяется, проверьте главный процесс и его папку кэша/загрузок.";
        }

        return "Оставьте наблюдение включенным. Сводка станет полезнее после нескольких часов работы.";
    }

    private async Task LogProcessWriteEventsAsync(IReadOnlyList<ProcessDiskActivity> activities, DateTimeOffset timestamp)
    {
        var events = activities
            .Where(a => a.WrittenBytesPerSecond.GetValueOrDefault() >= SignificantProcessWriteBytesPerSecond)
            .OrderByDescending(a => a.WrittenBytesPerSecond.GetValueOrDefault())
            .Take(5)
            .Where(a => !HasRecentProcessWriteEvent(a, timestamp))
            .Select(a => CreateProcessWriteEvent(a, timestamp))
            .ToList();

        if (events.Count == 0)
        {
            return;
        }

        await _monitorEventService.AddAsync(events);
        foreach (var item in events.OrderByDescending(e => e.Timestamp))
        {
            MonitorEvents.Insert(0, item);
        }

        RefreshDiskDaySummary();
        RefreshInvestigationContext();
    }

    private bool HasRecentProcessWriteEvent(ProcessDiskActivity activity, DateTimeOffset timestamp)
    {
        return MonitorEvents.Any(e =>
            string.Equals(e.Title, "Заметная запись процесса", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.ProcessName, activity.ProcessName, StringComparison.OrdinalIgnoreCase) &&
            e.ProcessId == activity.ProcessId &&
            timestamp - e.Timestamp < ProcessWriteEventCooldown);
    }

    private static DiskMonitorEvent CreateProcessWriteEvent(ProcessDiskActivity activity, DateTimeOffset timestamp)
    {
        var writeBytes = activity.WrittenBytesPerSecond.GetValueOrDefault();
        var projectedGb = writeBytes / 1_000_000_000m * 86400m;
        return new DiskMonitorEvent
        {
            Timestamp = timestamp,
            DiskIdentity = "process-activity",
            DiskModel = "Процессы Windows",
            Severity = projectedGb >= 100 ? "Caution" : "Info",
            Title = "Заметная запись процесса",
            Details = $"{activity.ProcessName} пишет {FormatHelper.Bytes(writeBytes)}/с. Если такая скорость будет держаться весь день, получится примерно {projectedGb:0.#} ГБ/день.",
            PossibleCause = activity.Comment,
            WrittenBytesPerSecond = activity.WrittenBytesPerSecond,
            ReadBytesPerSecond = activity.ReadBytesPerSecond,
            ProcessName = activity.ProcessName,
            ProcessId = activity.ProcessId,
            ProjectedDailyWriteGb = projectedGb
        };
    }

    private IReadOnlyList<DiskMonitorEvent> BuildMonitorEvents(
        DiskInfo disk,
        DiskSnapshot snapshot,
        DiskSnapshot? previous,
        HealthReport report)
    {
        var events = new List<DiskMonitorEvent>();
        var now = DateTimeOffset.Now;
        var cause = BuildPossibleCause();

        var temperatureLimit = disk.MediaType == DiskMediaKind.HDD ? 50 : 65;
        if (disk.TemperatureCelsius >= temperatureLimit)
        {
            events.Add(CreateMonitorEvent(disk, now, "Warning", "Высокая температура",
                $"Температура диска {disk.TemperatureCelsius}°C. Ориентир для внимания: {temperatureLimit}°C.",
                cause, snapshot));
        }

        if (previous?.TemperatureCelsius is not null &&
            disk.TemperatureCelsius is not null &&
            disk.TemperatureCelsius.Value - previous.TemperatureCelsius.Value >= 8)
        {
            events.Add(CreateMonitorEvent(disk, now, "Caution", "Резкий рост температуры",
                $"Температура выросла с {previous.TemperatureCelsius}°C до {disk.TemperatureCelsius}°C.",
                cause, snapshot));
        }

        if (disk.SmartPassed == false)
        {
            events.Add(CreateMonitorEvent(disk, now, "Critical", "SMART сообщил о проблеме",
                "Диск вернул отрицательный SMART-статус. Сначала сохраните важные данные.",
                cause, snapshot));
        }

        AddCounterGrowth(events, disk, now, previous?.MediaErrors, disk.MediaErrors, "Media errors", "Вырос счётчик ошибок носителя", snapshot, cause);
        AddCounterGrowth(events, disk, now, previous?.UncorrectableErrors, disk.UncorrectableErrors, "Uncorrectable errors", "Вырос счётчик неисправимых ошибок", snapshot, cause);
        AddCounterGrowth(events, disk, now, previous?.CurrentPendingSectors, disk.CurrentPendingSectors, "Pending sectors", "Появились нестабильные сектора", snapshot, cause);
        AddCounterGrowth(events, disk, now, previous?.ReallocatedSectors, disk.ReallocatedSectors, "Reallocated sectors", "Вырос счётчик переназначенных секторов", snapshot, cause);
        AddCounterGrowth(events, disk, now, previous?.CrcErrors, disk.CrcErrors, "CRC errors", "Вырос счётчик ошибок передачи данных", snapshot, cause);
        AddCounterGrowth(events, disk, now, previous?.UnsafeShutdowns, disk.UnsafeShutdowns, "Unsafe shutdowns", "Диск зафиксировал некорректное отключение", snapshot, cause);

        if (previous?.TotalBytesWritten is not null &&
            disk.TotalBytesWritten is not null &&
            disk.TotalBytesWritten > previous.TotalBytesWritten)
        {
            var delta = disk.TotalBytesWritten.Value - previous.TotalBytesWritten.Value;
            if (delta >= 1_000_000_000)
            {
                events.Add(CreateMonitorEvent(disk, now, "Info", "Заметная запись на диск",
                    $"С момента прошлой проверки записано примерно {FormatHelper.Bytes(delta)}.",
                    cause, snapshot));
            }
        }

        if (previous is not null &&
            HealthLevelRank(report.Level) >= HealthLevelRank(HealthLevel.Warning) &&
            HealthLevelRank(report.Level) > HealthLevelRank(previous.HealthLevel))
        {
            events.Add(CreateMonitorEvent(disk, now, "Warning", "Уровень риска ухудшился",
                $"Новый уровень: {FormatHelper.LevelText(report.Level)}. Индекс риска: {report.RiskScore}/100.",
                cause, snapshot));
        }

        return events;
    }

    private static void AddCounterGrowth(
        ICollection<DiskMonitorEvent> events,
        DiskInfo disk,
        DateTimeOffset timestamp,
        ulong? previous,
        ulong? current,
        string metric,
        string title,
        DiskSnapshot snapshot,
        string cause)
    {
        if (previous is null || current is null || current <= previous)
        {
            return;
        }

        events.Add(CreateMonitorEvent(disk, timestamp, metric.Contains("CRC", StringComparison.OrdinalIgnoreCase) ? "Caution" : "Warning",
            title, $"{metric}: {previous} → {current}.", cause, snapshot));
    }

    private static DiskMonitorEvent CreateMonitorEvent(
        DiskInfo disk,
        DateTimeOffset timestamp,
        string severity,
        string title,
        string details,
        string cause,
        DiskSnapshot snapshot)
    {
        return new DiskMonitorEvent
        {
            Timestamp = timestamp,
            DiskIdentity = disk.Identity,
            DiskModel = disk.Model ?? disk.Id,
            Severity = severity,
            Title = title,
            Details = details,
            PossibleCause = cause,
            TemperatureCelsius = disk.TemperatureCelsius,
            HealthLevel = snapshot.HealthLevel,
            WrittenBytesPerSecond = null,
            ReadBytesPerSecond = null
        };
    }

    private string BuildPossibleCause()
    {
        var writers = _lastProcessActivities
            .Where(p => (p.WrittenBytesPerSecond ?? 0) > 0)
            .OrderByDescending(p => p.WrittenBytesPerSecond ?? 0)
            .Take(3)
            .ToList();

        if (writers.Count == 0)
        {
            return "Активного процесса записи в момент проверки не видно. Возможны системные операции, драйверы, кэш или кратковременная нагрузка между тиками.";
        }

        return "В момент проверки больше всего писали: " +
               string.Join(", ", writers.Select(p => $"{p.ProcessName} ({FormatHelper.Bytes(p.WrittenBytesPerSecond)}/с)"));
    }

    private bool HasRecentMonitorEvent(DiskMonitorEvent candidate, TimeSpan window)
    {
        return MonitorEvents.Any(e =>
            e.DiskIdentity == candidate.DiskIdentity &&
            e.Title == candidate.Title &&
            candidate.Timestamp - e.Timestamp < window);
    }

    private static int HealthLevelRank(HealthLevel level) => level switch
    {
        HealthLevel.Good => 0,
        HealthLevel.Caution => 1,
        HealthLevel.Warning => 2,
        HealthLevel.Critical => 3,
        _ => -1
    };

    private async Task RefreshInvestigationsAsync()
    {
        var investigations = await _investigationEngine.RefreshAsync(Disks, _history);
        Investigations.Clear();
        foreach (var investigation in investigations)
        {
            Investigations.Add(investigation);
        }

        if (SelectedInvestigation is null)
        {
            SelectedInvestigation = Investigations.FirstOrDefault(i => SelectedDisk is null || i.DiskId == SelectedDisk.Identity)
                                    ?? Investigations.FirstOrDefault();
        }
        else
        {
            SelectedInvestigation = Investigations.FirstOrDefault(i => i.Id == SelectedInvestigation.Id) ?? Investigations.FirstOrDefault();
        }
    }

    private async Task ExportReportAsync()
    {
        if (SelectedDisk is null || SelectedReport is null)
        {
            return;
        }

        var path = _dialogService.PickMarkdownReportPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            await _reportExportService.ExportMarkdownAsync(path, Disks, SelectedDisk, SelectedReport);
            _dialogService.ShowMessage("Отчёт сохранён.", "Disk Health Advisor");
        }
        catch (Exception ex)
        {
            await _logger.LogAsync("Не удалось сохранить отчёт.", ex);
            _dialogService.ShowMessage("Не удалось сохранить отчёт. Подробности записаны в Logs/app.log.", "Disk Health Advisor");
        }
    }

    private async Task PickSmartCtlAsync()
    {
        var path = _dialogService.PickSmartCtlPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        SmartCtlPath = path;
        _settings.SmartCtlPath = path;
        await _settingsService.SaveAsync(_settings);
        RefreshSmartCtlStatus();
        StatusMessage = "Путь к smartctl.exe сохранён. Нажмите «Обновить», чтобы перечитать данные.";
    }

    private async Task InstallSmartCtlAsync()
    {
        IsBusy = true;
        StatusMessage = "Устанавливаю smartmontools через winget...";
        try
        {
            var result = await _smartCtlBootstrapService.InstallWithWingetAsync();
            RefreshSmartCtlStatus();
            StatusMessage = result.Message;

            if (result.Success)
            {
                await RefreshAsync();
            }
            else
            {
                _dialogService.ShowMessage(result.Message, "Disk Health Advisor");
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddTbwAsync()
    {
        if (SelectedDisk is null)
        {
            return;
        }

        if (!decimal.TryParse(ManualTbwText.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var tbw) || tbw <= 0)
        {
            _dialogService.ShowMessage("Введите TBW в терабайтах, например 600.", "Disk Health Advisor");
            return;
        }

        await _tbwDatabase.AddManualAsync(SelectedDisk, tbw);
        ManualTbwText = "";
        StatusMessage = "TBW сохранён в пользовательской JSON-базе.";
        await RefreshAsync();
    }

    private async Task SearchOnlineTbwAsync()
    {
        if (SelectedDisk is null)
        {
            return;
        }

        IsBusy = true;
        OnlineTbwCandidates.Clear();
        SelectedOnlineTbwCandidate = null;
        OnlineTbwStatus = "Ищу TBW в онлайн-базе. Диск не изменяется, это только чтение страницы.";
        StatusMessage = "Ищу данные TBW в интернете...";

        try
        {
            var candidates = await _onlineTbwLookupService.SearchAsync(SelectedDisk, OnlineTbwQuery);
            foreach (var candidate in candidates)
            {
                OnlineTbwCandidates.Add(candidate);
            }

            SelectedOnlineTbwCandidate = OnlineTbwCandidates.FirstOrDefault();
            OnlineTbwStatus = OnlineTbwCandidates.Count == 0
                ? "Ничего похожего не найдено. Попробуйте запрос короче: бренд + модель, например “Team GX2” или “Samsung 970 EVO”."
                : $"Найдено вариантов: {OnlineTbwCandidates.Count}. Проверьте модель, ёмкость и предупреждение перед сохранением.";
            ShowToast(OnlineTbwCandidates.Count == 0 ? "TBW не найден" : "Найдены варианты TBW");
        }
        catch (Exception ex)
        {
            await _logger.LogAsync("Ошибка при онлайн-поиске TBW.", ex);
            OnlineTbwStatus = "Онлайн-поиск TBW не завершился. Проверьте подключение к интернету или добавьте TBW вручную.";
            StatusMessage = "Не удалось выполнить онлайн-поиск TBW. Подробности записаны в Logs/app.log.";
            ShowToast("Ошибка поиска TBW");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveOnlineTbwAsync()
    {
        if (SelectedDisk is null || SelectedOnlineTbwCandidate is null)
        {
            return;
        }

        try
        {
            await _tbwDatabase.AddOnlineCandidateAsync(SelectedDisk, SelectedOnlineTbwCandidate);
            OnlineTbwStatus = $"Сохранено: {SelectedOnlineTbwCandidate.TbwText}. Источник: {SelectedOnlineTbwCandidate.Source}.";
            StatusMessage = "Найденный TBW сохранён в локальную JSON-базу.";
            ShowToast("TBW сохранён в базу");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            await _logger.LogAsync("Ошибка при сохранении найденного TBW.", ex);
            OnlineTbwStatus = "Не удалось сохранить выбранный TBW в локальную базу.";
            StatusMessage = "Ошибка сохранения TBW. Подробности записаны в Logs/app.log.";
            ShowToast("TBW не сохранён");
        }
    }

    private async Task MarkInvestigationActionAsync()
    {
        if (SelectedInvestigation is null)
        {
            return;
        }

        var investigations = await _investigationEngine.AddUserActionAsync(SelectedInvestigation.Id, SelectedInvestigationAction, InvestigationComment);
        InvestigationComment = "";
        ReloadInvestigations(investigations);
        StatusMessage = "Действие записано. Теперь нажмите «Повторить проверку», чтобы сравнить состояние.";
    }

    private async Task RecheckInvestigationAsync()
    {
        if (SelectedInvestigation is null)
        {
            return;
        }

        var investigationId = SelectedInvestigation.Id;
        await RefreshAsync();
        var investigation = Investigations.FirstOrDefault(i => i.Id == investigationId);
        if (investigation is null)
        {
            return;
        }

        var latestSnapshot = _history
            .Where(s => s.DiskIdentity == investigation.DiskId)
            .OrderByDescending(s => s.Timestamp)
            .FirstOrDefault();

        if (latestSnapshot is null)
        {
            StatusMessage = "Нет нового снимка для сравнения.";
            return;
        }

        var investigations = await _investigationEngine.RecheckAsync(investigationId, latestSnapshot);
        ReloadInvestigations(investigations);
        StatusMessage = "Повторная проверка завершена. Вывод расследования обновлён.";
    }

    private async Task ExportInvestigationAsync()
    {
        if (SelectedInvestigation is null)
        {
            return;
        }

        var path = _dialogService.PickInvestigationReportPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var disk = Disks.FirstOrDefault(d => d.Identity == SelectedInvestigation.DiskId);
        await _investigationExportService.ExportMarkdownAsync(path, SelectedInvestigation, disk);
        _dialogService.ShowMessage("Расследование сохранено.", "Disk Health Advisor");
    }

    private void ReloadInvestigations(IReadOnlyList<DiskInvestigation> investigations)
    {
        var selectedId = SelectedInvestigation?.Id;
        Investigations.Clear();
        foreach (var investigation in investigations.OrderByDescending(i => i.UpdatedAt))
        {
            Investigations.Add(investigation);
        }

        SelectedInvestigation = Investigations.FirstOrDefault(i => i.Id == selectedId) ?? Investigations.FirstOrDefault();
    }

    private void UpdateSelectedDisk()
    {
        DiskDetails.Clear();
        HistoryDetails.Clear();
        DataWarnings.Clear();
        RiskCategories.Clear();
        HistoryCharts.Clear();

        if (SelectedDisk is null)
        {
            SelectedReport = null;
            SsdResource = null;
            RefreshSelectedDiskDay();
            RefreshInvestigationContext();
            BuildMaintenanceActions();
            NotifyComputed();
            return;
        }

        SelectedReport = _reports.GetValueOrDefault(SelectedDisk.Identity);
        var tbw = _tbwRecords.GetValueOrDefault(SelectedDisk.Identity);
        SsdResource = _analyzer.BuildSsdResourceSummary(SelectedDisk, tbw, _history);
        FillDiskDetails(SelectedDisk);
        FillHistoryDetails(SelectedDisk);
        FillRiskCategories(SelectedDisk, SelectedReport, tbw);
        FillHistoryCharts(SelectedDisk);
        RefreshSelectedDiskDay();
        RefreshInvestigationContext();
        BuildDiagnosticWizard();
        BuildMaintenanceActions();

        foreach (var warning in SelectedDisk.DataSourceWarnings.Distinct())
        {
            DataWarnings.Add(warning);
        }

        if (_smartCtlBootstrapService.Find(SmartCtlPath) is null)
        {
            DataWarnings.Add("smartctl.exe не найден. Без него Windows часто показывает только базовые сведения: модель, объём, разделы и общий SMART-статус.");
        }

        if (SelectedDisk.MediaType is DiskMediaKind.SSD or DiskMediaKind.SataSSD &&
            (SelectedDisk.TotalBytesWritten is null || SelectedDisk.WearPercentage is null))
        {
            DataWarnings.Add("Для SATA SSD запись и износ не всегда стандартизированы. Если производитель отдаёт только vendor-specific SMART-атрибуты, они показаны во вкладке «Сырые данные», но не переводятся в проценты без точной расшифровки модели.");
        }

        if (SelectedDisk.MediaType is DiskMediaKind.SSD or DiskMediaKind.SataSSD &&
            SelectedDisk.CurrentPendingSectors is null &&
            SelectedDisk.ReallocatedSectors is null)
        {
            DataWarnings.Add("Некоторые SSD не отдают HDD-поля Reallocated/Pending sectors. Это не ошибка само по себе; для SSD важнее wear, media/program/erase fail и vendor-specific bad block атрибуты.");
        }

        NotifyComputed();
    }

    private void RefreshInvestigationContext()
    {
        InvestigationContext.Clear();
        InvestigationDataReadiness.Clear();
        InvestigationTopWriters.Clear();
        InvestigationWriteEvents.Clear();

        if (SelectedInvestigation is null)
        {
            InvestigationProcessHint = "Выберите расследование, чтобы увидеть текущую запись по процессам.";
            InvestigationWriteHistoryHint = "Выберите расследование, чтобы увидеть журнал записи процессов.";
            InvestigationDataReadiness.Add(new MetricDisplay("Итог", "Выберите расследование."));
            return;
        }

        var disk = Disks.FirstOrDefault(d => d.Identity == SelectedInvestigation.DiskId);
        if (disk is null)
        {
            InvestigationContext.Add(new MetricDisplay("Диск", string.IsNullOrWhiteSpace(SelectedInvestigation.DiskModel) ? SelectedInvestigation.DiskId : SelectedInvestigation.DiskModel));
            InvestigationContext.Add(new MetricDisplay("Сработало", string.IsNullOrWhiteSpace(SelectedInvestigation.TriggerMetricText) ? "Нет свежих данных" : SelectedInvestigation.TriggerMetricText));
            InvestigationDataReadiness.Add(new MetricDisplay("Итог", "Диска нет в свежем списке. Нельзя надежно проверить состояние."));
            InvestigationDataReadiness.Add(new MetricDisplay("Что сделать", "Нажмите «Обновить» или проверьте подключение диска."));
            InvestigationProcessHint = "Этого диска сейчас нет в свежем списке. Нажмите «Обновить» или проверьте подключение диска.";
            FillInvestigationWriteEvents();
            return;
        }

        InvestigationContext.Add(new MetricDisplay("Диск", string.IsNullOrWhiteSpace(SelectedInvestigation.DiskSummary) ? FormatHelper.OptionalString(disk.Model) : SelectedInvestigation.DiskSummary));
        InvestigationContext.Add(new MetricDisplay("Разделы", VolumesText(disk)));
        InvestigationContext.Add(new MetricDisplay("Температура", FormatHelper.Optional(disk.TemperatureCelsius, "°C")));
        InvestigationContext.Add(new MetricDisplay("Записано всего", FormatHelper.Terabytes(disk.TotalBytesWritten)));
        InvestigationContext.Add(new MetricDisplay("TBW", TbwUsageText(disk)));
        InvestigationContext.Add(new MetricDisplay("Сработало", string.IsNullOrWhiteSpace(SelectedInvestigation.TriggerMetricText) ? "Нет свежих данных" : SelectedInvestigation.TriggerMetricText));
        FillInvestigationDataReadiness(disk);

        var writers = _lastProcessActivities
            .Where(a => a.WrittenBytesPerSecond.GetValueOrDefault() > 0 || a.ReadBytesPerSecond.GetValueOrDefault() > 0)
            .OrderByDescending(a => a.WrittenBytesPerSecond.GetValueOrDefault())
            .ThenByDescending(a => a.ReadBytesPerSecond.GetValueOrDefault())
            .Take(6)
            .ToList();

        foreach (var writer in writers)
        {
            InvestigationTopWriters.Add(writer);
        }

        InvestigationProcessHint = writers.Count == 0
            ? "Сейчас Windows не показывает активную запись по процессам. Оставьте наблюдение включенным или нажмите «Обновить», когда нагрузка повторится."
            : "Это кандидаты по общей записи Windows прямо сейчас. Windows не всегда отдает точную привязку процесса к физическому диску, но верхние строки стоит проверить первыми.";

        FillInvestigationWriteEvents();
    }

    private void FillInvestigationDataReadiness(DiskInfo disk)
    {
        var score = 0;
        const int total = 5;
        var snapshotsCount = _history.Count(s => s.DiskIdentity == disk.Identity);
        var hasTemperatureOrSmart = disk.TemperatureCelsius is not null ||
                                    disk.SmartPassed is not null ||
                                    disk.RawAttributes.Count > 0 ||
                                    disk.MediaErrors is not null ||
                                    disk.ReallocatedSectors is not null ||
                                    disk.CurrentPendingSectors is not null;
        var hasWriteCounter = disk.TotalBytesWritten is not null;
        var hasHistory = snapshotsCount >= 2;
        var tbw = _tbwRecords.GetValueOrDefault(disk.Identity);
        var needsTbw = disk.MediaType is DiskMediaKind.SSD or DiskMediaKind.SataSSD or DiskMediaKind.NvmeSSD;
        var hasTbw = !needsTbw || tbw is not null && tbw.Tbw > 0;
        var processEvents = MonitorEvents.Count(e => !string.IsNullOrWhiteSpace(e.ProcessName));
        var hasProcessSignal = processEvents > 0 || _lastProcessActivities.Count > 0;

        if (hasTemperatureOrSmart) score++;
        if (hasWriteCounter) score++;
        if (hasHistory) score++;
        if (hasTbw) score++;
        if (hasProcessSignal) score++;

        var summary = score switch
        {
            >= 5 => "Данных достаточно для нормального наблюдения.",
            4 => "Данных почти достаточно, выводы уже полезные.",
            3 => "Данных частично хватает: выводы полезные, но не все причины можно доказать.",
            _ => "Данных мало: программа может заметить часть проблем, но расследование будет слабым."
        };

        InvestigationDataReadiness.Add(new MetricDisplay("Итог", $"{summary} ({score}/{total})"));
        InvestigationDataReadiness.Add(new MetricDisplay("SMART/температура",
            hasTemperatureOrSmart
                ? $"Есть: {FormatHelper.Optional(disk.TemperatureCelsius, "°C")}. Источник: {TemperatureSourceText(disk)}"
                : "Нет. Лучше запустить от администратора или подключить smartctl."));
        InvestigationDataReadiness.Add(new MetricDisplay("Счетчик записи",
            hasWriteCounter
                ? $"Есть: {FormatHelper.Terabytes(disk.TotalBytesWritten)} записано всего."
                : "Нет. Нельзя точно считать рост записи и ГБ/день."));
        InvestigationDataReadiness.Add(new MetricDisplay("История",
            hasHistory
                ? $"Есть {snapshotsCount} снимка(ов). Можно сравнивать до/после."
                : $"Пока {snapshotsCount} снимка(ов). Нужны хотя бы 2: нажмите «Обновить» позже или оставьте наблюдение в трее."));
        InvestigationDataReadiness.Add(new MetricDisplay("TBW",
            hasTbw
                ? needsTbw ? $"Есть: {tbw!.Tbw:0.##} ТБ." : "Для этого типа диска не нужен."
                : "Нет. Для SSD ресурс можно оценивать лучше, если добавить TBW."));
        InvestigationDataReadiness.Add(new MetricDisplay("Процессы",
            hasProcessSignal
                ? processEvents > 0
                    ? $"Есть журнал заметной записи: {processEvents} событие(й) сегодня."
                    : "Есть текущие счетчики процессов, журнал заполнится при заметных всплесках."
                : "Пока нет данных. Оставьте программу в трее, чтобы ловить кто писал."));
        InvestigationDataReadiness.Add(new MetricDisplay("Следующий шаг", BuildDataReadinessNextStep(hasTemperatureOrSmart, hasWriteCounter, hasHistory, hasTbw, hasProcessSignal)));
    }

    private static string TemperatureSourceText(DiskInfo disk)
    {
        return string.IsNullOrWhiteSpace(disk.TemperatureSource)
            ? "не указан, вероятно Windows/драйвер"
            : disk.TemperatureSource;
    }

    private static string BuildDataReadinessNextStep(bool hasTemperatureOrSmart, bool hasWriteCounter, bool hasHistory, bool hasTbw, bool hasProcessSignal)
    {
        if (!hasTemperatureOrSmart || !hasWriteCounter)
        {
            return "Сначала включите smartctl или запустите программу от администратора: так появятся более надежные SMART/NVMe-данные.";
        }

        if (!hasHistory)
        {
            return "Оставьте программу в трее и сделайте еще одно обновление позже, чтобы появились сравнения.";
        }

        if (!hasTbw)
        {
            return "Добавьте TBW для SSD: тогда ресурс и расход записи будут понятнее.";
        }

        if (!hasProcessSignal)
        {
            return "Оставьте наблюдение в трее: журнал покажет, какие процессы писали в момент всплеска.";
        }

        return "Данных достаточно. Смотрите статус расследования, журнал процессов и повторную проверку после действий.";
    }

    private void FillInvestigationWriteEvents()
    {
        var events = MonitorEvents
            .Where(e => string.Equals(e.Title, "Заметная запись процесса", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.Timestamp)
            .Take(12)
            .ToList();

        foreach (var item in events)
        {
            InvestigationWriteEvents.Add(item);
        }

        InvestigationWriteHistoryHint = events.Count == 0
            ? "Пока за сегодня не поймано процессов с заметной записью. Оставьте программу в трее: она будет записывать только заметные всплески, без спама каждые 10 секунд."
            : "Это журнал заметной записи за сегодня. Windows показывает процесс и скорость, но не всегда точно отдает физический диск, поэтому сверяйте время события с ростом записи нужного диска.";
    }

    private string TbwUsageText(DiskInfo disk)
    {
        var tbw = _tbwRecords.GetValueOrDefault(disk.Identity);
        if (tbw is null || tbw.Tbw <= 0 || disk.TotalBytesWritten is null)
        {
            return "Нет данных";
        }

        var writtenTb = disk.TotalBytesWritten.Value / 1_000_000_000_000m;
        var percent = writtenTb / tbw.Tbw * 100m;
        return $"{writtenTb:0.##} из {tbw.Tbw:0.##} ТБ ({percent:0.#}%)";
    }

    private void FillDiskDetails(DiskInfo disk)
    {
        DiskDetails.Add(new MetricDisplay("Модель", FormatHelper.OptionalString(disk.Model)));
        DiskDetails.Add(new MetricDisplay("Тип", MediaTypeText(disk.MediaType)));
        DiskDetails.Add(new MetricDisplay("Объём", FormatHelper.Bytes(disk.SizeBytes)));
        DiskDetails.Add(new MetricDisplay("Серийный номер", FormatHelper.MaskSerial(disk.Serial)));
        DiskDetails.Add(new MetricDisplay("Прошивка", FormatHelper.OptionalString(disk.Firmware)));
        DiskDetails.Add(new MetricDisplay("Интерфейс", FormatHelper.OptionalString(disk.BusType)));
        DiskDetails.Add(new MetricDisplay("Разделы", VolumesText(disk)));
        DiskDetails.Add(new MetricDisplay("Свободно", FreeSpaceText(disk)));
        DiskDetails.Add(new MetricDisplay("Температура", FormatHelper.Optional(disk.TemperatureCelsius, "°C")));
        DiskDetails.Add(new MetricDisplay("Время работы", FormatHelper.Optional(disk.PowerOnHours, " ч")));
        DiskDetails.Add(new MetricDisplay("Количество включений", FormatHelper.Optional(disk.PowerCycleCount)));
        DiskDetails.Add(new MetricDisplay("Записано", FormatHelper.Terabytes(disk.TotalBytesWritten)));
        DiskDetails.Add(new MetricDisplay("Прочитано", FormatHelper.Terabytes(disk.TotalBytesRead)));
        DiskDetails.Add(new MetricDisplay("Износ SSD", FormatHelper.Optional(disk.WearPercentage, "%")));
        DiskDetails.Add(new MetricDisplay("Unsafe shutdowns", FormatHelper.Optional(disk.UnsafeShutdowns)));
        DiskDetails.Add(new MetricDisplay("Reallocated sectors", FormatHelper.Optional(disk.ReallocatedSectors)));
        DiskDetails.Add(new MetricDisplay("Pending sectors", FormatHelper.Optional(disk.CurrentPendingSectors)));
        DiskDetails.Add(new MetricDisplay("Uncorrectable errors", FormatHelper.Optional(disk.UncorrectableErrors)));
        DiskDetails.Add(new MetricDisplay("CRC errors", FormatHelper.Optional(disk.CrcErrors)));
        DiskDetails.Add(new MetricDisplay("SMART", FormatHelper.BoolSmart(disk.SmartPassed)));
    }

    private void FillHistoryDetails(DiskInfo disk)
    {
        var snapshots = _history
            .Where(s => s.DiskIdentity == disk.Identity)
            .OrderByDescending(s => s.Timestamp)
            .Take(2)
            .ToList();

        if (snapshots.Count == 0)
        {
            HistoryDetails.Add(new MetricDisplay("История", "Пока нет снимков."));
            return;
        }

        var latest = snapshots[0];
        HistoryDetails.Add(new MetricDisplay("Последний снимок", latest.Timestamp.ToString("yyyy-MM-dd HH:mm:ss")));
        if (snapshots.Count < 2)
        {
            HistoryDetails.Add(new MetricDisplay("Изменения", "Нужен ещё один запуск или обновление для сравнения."));
            return;
        }

        var previous = snapshots[1];
        HistoryDetails.Add(new MetricDisplay("Температура", Delta(previous.TemperatureCelsius, latest.TemperatureCelsius, "°C")));
        HistoryDetails.Add(new MetricDisplay("Записано", DeltaBytes(previous.TotalBytesWritten, latest.TotalBytesWritten)));
        HistoryDetails.Add(new MetricDisplay("Reallocated sectors", Delta(previous.ReallocatedSectors, latest.ReallocatedSectors)));
        HistoryDetails.Add(new MetricDisplay("Pending sectors", Delta(previous.CurrentPendingSectors, latest.CurrentPendingSectors)));
        HistoryDetails.Add(new MetricDisplay("Uncorrectable errors", Delta(previous.UncorrectableErrors, latest.UncorrectableErrors)));
        HistoryDetails.Add(new MetricDisplay("CRC errors", Delta(previous.CrcErrors, latest.CrcErrors)));

        if (latest.CurrentPendingSectors > previous.CurrentPendingSectors)
        {
            HistoryDetails.Add(new MetricDisplay("Предупреждение", $"Количество проблемных секторов увеличилось с {previous.CurrentPendingSectors} до {latest.CurrentPendingSectors}. Это плохой признак."));
        }
    }

    private void BuildImportantChanges()
    {
        ImportantChanges.Clear();

        foreach (var disk in Disks)
        {
            var pair = LatestPair(disk);
            if (pair is null)
            {
                continue;
            }

            var (latest, previous) = pair.Value;
            var name = FormatHelper.OptionalString(disk.Model);

            if (latest.TemperatureCelsius is not null && previous.TemperatureCelsius is not null)
            {
                var diff = latest.TemperatureCelsius.Value - previous.TemperatureCelsius.Value;
                if (diff >= 10)
                {
                    ImportantChanges.Add($"{name}: температура выросла на {diff}°C. Стоит проверить нагрузку и охлаждение.");
                }
            }

            AddGrowthNotification(name, "CRC errors", previous.CrcErrors, latest.CrcErrors, "Возможна проблема с SATA-кабелем, портом или питанием.");
            AddGrowthNotification(name, "нестабильные участки", previous.CurrentPendingSectors, latest.CurrentPendingSectors, "Сначала сохраните важные файлы.");
            AddGrowthNotification(name, "переназначенные участки", previous.ReallocatedSectors, latest.ReallocatedSectors, "Это может указывать на деградацию диска.");
            AddGrowthNotification(name, "некорректные выключения", previous.UnsafeShutdowns, latest.UnsafeShutdowns, "Проверьте питание, зависания Windows и журнал событий.");

            if (latest.TotalBytesWritten is not null && previous.TotalBytesWritten is not null)
            {
                var writtenToday = latest.TotalBytesWritten.Value > previous.TotalBytesWritten.Value
                    ? latest.TotalBytesWritten.Value - previous.TotalBytesWritten.Value
                    : 0;
                if (writtenToday >= 200UL * 1_000_000_000UL)
                {
                    ImportantChanges.Add($"{name}: за последнюю проверку записано {FormatHelper.Terabytes(writtenToday)}. Посмотрите вкладку «Что пишет на диск».");
                }
            }
        }

        if (ImportantChanges.Count == 0)
        {
            ImportantChanges.Add("Важных ухудшений между последними снимками не найдено.");
        }
    }

    private void FillRiskCategories(DiskInfo disk, HealthReport? report, SsdTbwRecord? tbw)
    {
        var freePercent = CalculateFreeSpacePercent(disk);
        var tbwUsed = CalculateTbwUsedPercent(disk, tbw);

        AddRiskCategory("Температура", TemperatureStatus(disk), TemperatureDetail(disk), TemperatureBrush(disk));
        AddRiskCategory("Ошибки диска", ErrorStatus(disk), ErrorDetail(disk), ErrorBrush(disk));
        AddRiskCategory("Кабель / порт / питание", CableStatus(disk), CableDetail(disk), CableBrush(disk));
        AddRiskCategory("Ресурс SSD", SsdWearStatus(disk, tbwUsed), SsdWearDetail(disk, tbwUsed), SsdWearBrush(disk, tbwUsed));
        AddRiskCategory("Свободное место", FreeSpaceStatus(freePercent), FreeSpaceDetail(freePercent), FreeSpaceBrush(freePercent));
        AddRiskCategory("Качество данных", DataQualityStatus(disk), DataQualityDetail(disk), DataQualityBrush(disk));

        if (report is not null)
        {
            AddRiskCategory("Итоговый риск", $"{report.RiskScore}/100", report.Summary, BrushForLevel(report.Level));
        }
    }

    private void FillHistoryCharts(DiskInfo disk)
    {
        var snapshots = _history
            .Where(s => s.DiskIdentity == disk.Identity)
            .OrderBy(s => s.Timestamp)
            .TakeLast(12)
            .ToList();

        HistoryCharts.Add(BuildChart("Температура", snapshots, s => s.TemperatureCelsius is null ? null : s.TemperatureCelsius.Value, "°C"));
        HistoryCharts.Add(BuildChart("Запись", snapshots, s => s.TotalBytesWritten is null ? null : (double)(s.TotalBytesWritten.Value / 1_000_000_000_000d), " ТБ"));
        HistoryCharts.Add(BuildChart("Износ SSD", snapshots, s => s.WearPercentage is null ? null : s.WearPercentage.Value, "%"));
        HistoryCharts.Add(BuildChart("Ошибки", snapshots, s => (double?)((s.CurrentPendingSectors ?? 0) + (s.ReallocatedSectors ?? 0) + (s.UncorrectableErrors ?? 0) + (s.CrcErrors ?? 0)), ""));
    }

    private void BuildDiagnosticWizard()
    {
        if (SelectedDisk is null)
        {
            DiagnosticWizardText = "Выберите диск, чтобы увидеть безопасный следующий шаг.";
            return;
        }

        if (SelectedInvestigation is not null && SelectedInvestigation.DiskId == SelectedDisk.Identity)
        {
            DiagnosticWizardText = $"По выбранному диску есть расследование: «{SelectedInvestigation.SimpleTitle}». Сначала выполните рекомендацию, затем нажмите «Повторить проверку», чтобы сравнить снимки до/после.";
            return;
        }

        var profile = SelectedDiskProfile;
        var smartCtl = _smartCtlBootstrapService.Find(SmartCtlPath) is null
            ? "smartctl не найден: если данных мало, установите smartmontools или укажите путь в настройках."
            : "smartctl найден: расширенная read-only диагностика доступна.";

        var profileAdvice = profile switch
        {
            "Системный диск" => "Для системного диска особенно важны резервная копия профиля пользователя и контроль свободного места.",
            "Игровой диск" => "Для игрового диска нормальны всплески записи при обновлениях, но постоянная высокая запись требует проверки процессов.",
            "Архивный диск" => "Для архивного диска важнее отсутствие роста ошибок и наличие второй копии важных данных.",
            "Диск для торрентов" => "Для торрентов ожидаема высокая запись, поэтому полезно смотреть TBW и вкладку «Что пишет на диск».",
            "Диск для видео/записи" => "Для записи видео следите за температурой, свободным местом и дневным объёмом записи.",
            _ => "Если указать роль диска, подсказки будут точнее."
        };

        DiagnosticWizardText = $"Профиль: {profile}. Сейчас активного расследования по этому диску нет. Безопасный сценарий: нажмите «Обновить», посмотрите уведомления и историю. {profileAdvice} {smartCtl}";
    }

    private void CopyShortReport()
    {
        if (SelectedDisk is null)
        {
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Disk Health Advisor: краткий отчёт");
        builder.AppendLine($"Дата: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine($"Диск: {FormatHelper.OptionalString(SelectedDisk.Model)} ({SelectedDisk.MediaTypeDisplay}, {FormatHelper.Bytes(SelectedDisk.SizeBytes)})");
        builder.AppendLine($"Профиль: {SelectedDiskProfile}");
        builder.AppendLine($"Состояние: {LevelText}");
        builder.AppendLine($"Вывод: {SelectedReport?.Summary ?? "Нет данных"}");
        builder.AppendLine();
        builder.AppendLine("Главные причины:");
        foreach (var reason in SelectedReport?.Reasons ?? Enumerable.Empty<string>())
        {
            builder.AppendLine("- " + reason);
        }

        builder.AppendLine();
        builder.AppendLine("Что сделать:");
        foreach (var recommendation in SelectedReport?.Recommendations ?? Enumerable.Empty<string>())
        {
            builder.AppendLine("- " + recommendation);
        }

        builder.AppendLine();
        builder.AppendLine("Ключевые параметры:");
        foreach (var detail in DiskDetails.Take(12))
        {
            builder.AppendLine($"- {detail.Name}: {detail.Value}");
        }

        System.Windows.Clipboard.SetText(builder.ToString());
        StatusMessage = "Краткий отчёт скопирован в буфер обмена.";
        ShowToast("Скопировано в буфер обмена");
    }

    private async void ShowToast(string message)
    {
        var token = ++_toastToken;
        ToastMessage = message;
        IsToastVisible = true;

        await Task.Delay(2600);
        if (token == _toastToken)
        {
            IsToastVisible = false;
        }
    }

    private void SeedKnowledgeBase()
    {
        KnowledgeBase.Clear();
        KnowledgeBase.Add(new KnowledgeEntry
        {
            Title = "Почему не все поля доступны",
            Summary = "Windows часто отдаёт только базовые сведения, особенно для USB и некоторых SATA SSD.",
            Details = "Для температуры NVMe, счётчиков записи, unsafe shutdowns и media errors обычно помогает smartctl. USB-переходники могут скрывать SMART полностью."
        });
        KnowledgeBase.Add(new KnowledgeEntry
        {
            Title = "CRC errors",
            Summary = "Это ошибки передачи данных между диском и контроллером.",
            Details = "Часто причина не в самом диске, а в SATA-кабеле, порте, контакте или питании. Важно смотреть, растёт ли число ошибок."
        });
        KnowledgeBase.Add(new KnowledgeEntry
        {
            Title = "Pending sectors",
            Summary = "HDD нашёл участки, которые не смог нормально прочитать.",
            Details = "Это повод сначала сделать резервную копию. Если показатель растёт, вероятна физическая деградация."
        });
        KnowledgeBase.Add(new KnowledgeEntry
        {
            Title = "TBW",
            Summary = "TBW — гарантийный ориентир ресурса записи SSD, а не точная дата поломки.",
            Details = "При приближении к TBW риск повышается, но диск может работать дальше. Важно смотреть темп записи и иметь резервную копию."
        });
        KnowledgeBase.Add(new KnowledgeEntry
        {
            Title = "Unsafe shutdowns",
            Summary = "Диск фиксирует некорректные выключения.",
            Details = "Причины: отключение электричества, зависания, удержание кнопки питания, БП или драйвер. Рост показателя стоит расследовать."
        });
    }

    private void AddRiskCategory(string name, string status, string detail, string brush)
    {
        RiskCategories.Add(new RiskCategoryDisplay
        {
            Name = name,
            Status = status,
            Detail = detail,
            Brush = brush
        });
    }

    private static HistoryChart BuildChart(string title, IReadOnlyList<DiskSnapshot> snapshots, Func<DiskSnapshot, double?> selector, string suffix)
    {
        var values = snapshots
            .Select(s => new { Snapshot = s, Value = selector(s) })
            .Where(x => x.Value is not null)
            .ToList();

        var chart = new HistoryChart { Title = title };
        if (values.Count == 0)
        {
            return chart;
        }

        var min = values.Min(x => x.Value!.Value);
        var max = values.Max(x => x.Value!.Value);
        var range = Math.Max(1, max - min);

        foreach (var item in values)
        {
            var height = 12 + ((item.Value!.Value - min) / range * 58);
            chart.Points.Add(new HistoryChartPoint
            {
                Height = Math.Clamp(height, 10, 70),
                Label = $"{item.Snapshot.Timestamp:MM-dd HH:mm}: {item.Value:0.##}{suffix}"
            });
        }

        chart.Subtitle = values.Count == 1
            ? $"{values[0].Value:0.##}{suffix}"
            : $"{values.First().Value:0.##}{suffix} → {values.Last().Value:0.##}{suffix}";
        return chart;
    }

    private (DiskSnapshot Latest, DiskSnapshot Previous)? LatestPair(DiskInfo disk)
    {
        var snapshots = _history
            .Where(s => s.DiskIdentity == disk.Identity)
            .OrderByDescending(s => s.Timestamp)
            .Take(2)
            .ToList();

        return snapshots.Count < 2 ? null : (snapshots[0], snapshots[1]);
    }

    private void AddGrowthNotification(string diskName, string metric, ulong? previous, ulong? current, string advice)
    {
        if (previous is not null && current is not null && current > previous)
        {
            ImportantChanges.Add($"{diskName}: вырос показатель «{metric}» с {previous} до {current}. {advice}");
        }
    }

    private static decimal? CalculateTbwUsedPercent(DiskInfo disk, SsdTbwRecord? tbw)
    {
        if (tbw is null || tbw.Tbw <= 0 || disk.TotalBytesWritten is null)
        {
            return null;
        }

        return (decimal)disk.TotalBytesWritten.Value / (tbw.Tbw * 1_000_000_000_000m) * 100m;
    }

    private static decimal? CalculateFreeSpacePercent(DiskInfo disk)
    {
        var total = disk.LogicalVolumes.Aggregate<LogicalVolumeInfo, ulong>(0, (sum, volume) => sum + (volume.SizeBytes ?? 0));
        var free = disk.LogicalVolumes.Aggregate<LogicalVolumeInfo, ulong>(0, (sum, volume) => sum + (volume.FreeBytes ?? 0));
        return total == 0 ? null : (decimal)free / total * 100m;
    }

    private static string TemperatureStatus(DiskInfo disk)
    {
        if (disk.TemperatureCelsius is null) return "Нет данных";
        var limit = disk.MediaType == DiskMediaKind.HDD ? 50 : 70;
        return disk.TemperatureCelsius >= limit ? "Внимание" : "Норма";
    }

    private static string TemperatureDetail(DiskInfo disk) => disk.TemperatureCelsius is null
        ? "Температура не получена. Для NVMe/SATA часто помогает smartctl."
        : $"Сейчас {disk.TemperatureCelsius}°C.";

    private static string TemperatureBrush(DiskInfo disk) => disk.TemperatureCelsius is null
        ? "#6F7B8A"
        : disk.TemperatureCelsius >= (disk.MediaType == DiskMediaKind.HDD ? 50 : 70) ? "#D17A22" : "#2FA36B";

    private static string ErrorStatus(DiskInfo disk)
    {
        if ((disk.CurrentPendingSectors ?? 0) + (disk.UncorrectableErrors ?? 0) + (disk.MediaErrors ?? 0) > 0) return "Высокий риск";
        if ((disk.ReallocatedSectors ?? 0) > 0) return "Нужно наблюдение";
        return HasAnyErrorData(disk) ? "Норма" : "Нет данных";
    }

    private static string ErrorDetail(DiskInfo disk) =>
        $"Pending: {FormatHelper.Optional(disk.CurrentPendingSectors)}, Reallocated: {FormatHelper.Optional(disk.ReallocatedSectors)}, Uncorrectable: {FormatHelper.Optional(disk.UncorrectableErrors)}, Media: {FormatHelper.Optional(disk.MediaErrors)}.";

    private static string ErrorBrush(DiskInfo disk) => ErrorStatus(disk) switch
    {
        "Высокий риск" => "#D94B4B",
        "Нужно наблюдение" => "#C9A227",
        "Норма" => "#2FA36B",
        _ => "#6F7B8A"
    };

    private static string CableStatus(DiskInfo disk) => disk.CrcErrors is null ? "Нет данных" : disk.CrcErrors > 0 ? "Нужно наблюдение" : "Норма";

    private static string CableDetail(DiskInfo disk) => disk.CrcErrors is null
        ? "CRC-счётчик недоступен."
        : disk.CrcErrors > 0 ? $"CRC errors: {disk.CrcErrors}. Если число растёт, сначала проверяют SATA-кабель/порт." : "Ошибок передачи не видно.";

    private static string CableBrush(DiskInfo disk) => disk.CrcErrors is null ? "#6F7B8A" : disk.CrcErrors > 0 ? "#C9A227" : "#2FA36B";

    private static string SsdWearStatus(DiskInfo disk, decimal? tbwUsed)
    {
        var wear = Math.Max(disk.WearPercentage ?? 0, (int)Math.Floor(tbwUsed ?? 0));
        if (disk.MediaType is DiskMediaKind.HDD) return "Не относится к HDD";
        if (wear >= 100) return "Ресурс TBW превышен";
        if (wear >= 80) return "Внимание";
        return disk.WearPercentage is null && tbwUsed is null ? "Нет данных" : "Норма";
    }

    private static string SsdWearDetail(DiskInfo disk, decimal? tbwUsed) =>
        $"Wear: {FormatHelper.Optional(disk.WearPercentage, "%")}, TBW: {(tbwUsed is null ? "Нет данных" : $"{tbwUsed:0.#}%")}.";

    private static string SsdWearBrush(DiskInfo disk, decimal? tbwUsed)
    {
        var wear = Math.Max(disk.WearPercentage ?? 0, (int)Math.Floor(tbwUsed ?? 0));
        if (disk.MediaType == DiskMediaKind.HDD) return "#6F7B8A";
        return wear >= 100 ? "#D94B4B" : wear >= 80 ? "#D17A22" : disk.WearPercentage is null && tbwUsed is null ? "#6F7B8A" : "#2FA36B";
    }

    private static string FreeSpaceStatus(decimal? freePercent) => freePercent is null ? "Нет данных" : freePercent < 10 ? "Внимание" : "Норма";

    private static string FreeSpaceDetail(decimal? freePercent) => freePercent is null
        ? "Нет данных о разделах или свободном месте."
        : $"Свободно около {freePercent:0.#}% по видимым разделам.";

    private static string FreeSpaceBrush(decimal? freePercent) => freePercent is null ? "#6F7B8A" : freePercent < 10 ? "#D17A22" : "#2FA36B";

    private static string DataQualityStatus(DiskInfo disk)
    {
        var known = CountKnownHealthFields(disk);
        return known >= 8 ? "Данные полные" : known >= 4 ? "Часть данных есть" : "Данных мало";
    }

    private static string DataQualityDetail(DiskInfo disk)
    {
        var known = CountKnownHealthFields(disk);
        return $"Получено {known} из 13 ключевых показателей. Если данных мало, проверьте запуск от администратора, smartctl и USB-переходник.";
    }

    private static string DataQualityBrush(DiskInfo disk)
    {
        var known = CountKnownHealthFields(disk);
        return known >= 8 ? "#2FA36B" : known >= 4 ? "#C9A227" : "#6F7B8A";
    }

    private static int CountKnownHealthFields(DiskInfo disk)
    {
        object?[] values =
        [
            disk.TemperatureCelsius,
            disk.PowerOnHours,
            disk.PowerCycleCount,
            disk.TotalBytesWritten,
            disk.TotalBytesRead,
            disk.WearPercentage,
            disk.UnsafeShutdowns,
            disk.MediaErrors,
            disk.ReallocatedSectors,
            disk.CurrentPendingSectors,
            disk.UncorrectableErrors,
            disk.CrcErrors,
            disk.SmartPassed
        ];

        return values.Count(v => v is not null);
    }

    private static bool HasAnyErrorData(DiskInfo disk) =>
        disk.CurrentPendingSectors is not null ||
        disk.UncorrectableErrors is not null ||
        disk.ReallocatedSectors is not null ||
        disk.MediaErrors is not null;

    private void NotifyComputed()
    {
        OnPropertyChanged(nameof(LevelText));
        OnPropertyChanged(nameof(LevelBrush));
        OnPropertyChanged(nameof(SelectedDiskProfile));
        OnPropertyChanged(nameof(ExpertModeText));
        NotifyThemeChanged();
        if (ExportReportCommand is AsyncRelayCommand exportReport)
        {
            exportReport.RaiseCanExecuteChanged();
        }

        if (AddTbwCommand is AsyncRelayCommand addTbw)
        {
            addTbw.RaiseCanExecuteChanged();
        }

        if (SearchOnlineTbwCommand is AsyncRelayCommand searchOnlineTbw)
        {
            searchOnlineTbw.RaiseCanExecuteChanged();
        }

        if (SaveOnlineTbwCommand is AsyncRelayCommand saveOnlineTbw)
        {
            saveOnlineTbw.RaiseCanExecuteChanged();
        }

        if (CopyShortReportCommand is RelayCommand copyShortReport)
        {
            copyShortReport.RaiseCanExecuteChanged();
        }

        if (MarkInvestigationActionCommand is AsyncRelayCommand markInvestigation)
        {
            markInvestigation.RaiseCanExecuteChanged();
        }

        if (RecheckInvestigationCommand is AsyncRelayCommand recheckInvestigation)
        {
            recheckInvestigation.RaiseCanExecuteChanged();
        }

        if (ExportInvestigationCommand is AsyncRelayCommand exportInvestigation)
        {
            exportInvestigation.RaiseCanExecuteChanged();
        }

        if (ApplyLocalUpdateCommand is AsyncRelayCommand applyLocalUpdate)
        {
            applyLocalUpdate.RaiseCanExecuteChanged();
        }
    }

    private static string BrushForLevel(HealthLevel level) => level switch
    {
        HealthLevel.Good => "#2FA36B",
        HealthLevel.Caution => "#C9A227",
        HealthLevel.Warning => "#D17A22",
        HealthLevel.Critical => "#D94B4B",
        _ => "#6F7B8A"
    };

    private void NotifyThemeChanged()
    {
        OnPropertyChanged(nameof(ThemeAccentBrush));
        OnPropertyChanged(nameof(ThemeAccentSoftBrush));
        OnPropertyChanged(nameof(ThemeNoticeText));
    }

    private static string NormalizeThemeKey(string? themeName)
    {
        var value = themeName?.ToLowerInvariant() ?? "";
        if (value.Contains("граф") || value.Contains("graph"))
        {
            return "graphite";
        }

        if (value.Contains("север") || value.Contains("north") || value.Contains("aurora"))
        {
            return "north";
        }

        if (value.Contains("контраст") || value.Contains("contrast"))
        {
            return "contrast";
        }

        if (value.Contains("неон") || value.Contains("neon"))
        {
            return "neon";
        }

        if (value.Contains("скан") || value.Contains("scan"))
        {
            return "scanner";
        }

        if (value.Contains("пульс") || value.Contains("pulse"))
        {
            return "pulse";
        }

        if (value.Contains("термин") || value.Contains("terminal") || value.Contains("matrix"))
        {
            return "terminal";
        }

        return "ocean";
    }

    private static string MediaTypeText(DiskMediaKind kind)
    {
        return kind switch
        {
            DiskMediaKind.HDD => "HDD",
            DiskMediaKind.SSD => "SSD",
            DiskMediaKind.SataSSD => "SATA SSD",
            DiskMediaKind.NvmeSSD => "NVMe SSD",
            DiskMediaKind.USB => "USB",
            _ => "Unknown"
        };
    }

    private static string VolumesText(DiskInfo disk)
    {
        if (disk.LogicalVolumes.Count == 0)
        {
            return "Нет данных";
        }

        return string.Join(", ", disk.LogicalVolumes.Select(v => $"{v.DisplayName} ({FormatHelper.OptionalString(v.FileSystem)})"));
    }

    private static string FreeSpaceText(DiskInfo disk)
    {
        if (disk.LogicalVolumes.Count == 0)
        {
            return "Нет данных";
        }

        return string.Join(", ", disk.LogicalVolumes.Select(v => $"{v.DisplayName}: {FormatHelper.Bytes(v.FreeBytes)} свободно"));
    }

    private static string Delta(int? previous, int? current, string suffix = "")
    {
        if (previous is null || current is null)
        {
            return "Нет данных";
        }

        var diff = current.Value - previous.Value;
        return $"{previous}{suffix} → {current}{suffix} ({diff:+#;-#;0}{suffix})";
    }

    private static string Delta(ulong? previous, ulong? current)
    {
        if (previous is null || current is null)
        {
            return "Нет данных";
        }

        var diff = current.Value >= previous.Value ? current.Value - previous.Value : 0;
        return $"{previous} → {current} (+{diff})";
    }

    private static string DeltaBytes(ulong? previous, ulong? current)
    {
        if (previous is null || current is null)
        {
            return "Нет данных";
        }

        var diff = current.Value >= previous.Value ? current.Value - previous.Value : 0;
        return $"{FormatHelper.Terabytes(previous)} → {FormatHelper.Terabytes(current)} (+{FormatHelper.Terabytes(diff)})";
    }

    private void RefreshSmartCtlStatus()
    {
        var path = _smartCtlBootstrapService.Find(SmartCtlPath);
        SmartCtlStatus = path is null
            ? "smartctl.exe не найден. Для NVMe/M.2 это главный источник температуры, ресурса и ошибок."
            : "smartctl.exe найден: " + path;
    }
}
