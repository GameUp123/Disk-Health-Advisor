using System.Windows;
using System.ComponentModel;
using System.Windows.Media;
using DiskHealthAdvisor.Services;
using DiskHealthAdvisor.Services.Database;
using DiskHealthAdvisor.Services.DiskProviders;
using DiskHealthAdvisor.Services.HealthAnalysis;
using DiskHealthAdvisor.ViewModels;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace DiskHealthAdvisor;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private Forms.NotifyIcon? _notifyIcon;
    private bool _isExitRequested;

    public MainWindow()
    {
        InitializeComponent();

        var paths = new ApplicationPaths();
        var logger = new AppLogger(paths);
        var powerShellRunner = new PowerShellJsonRunner(logger);
        var settingsService = new SettingsService(paths, logger);
        var windowsProvider = new WindowsStorageProvider(powerShellRunner, logger);
        var smartCtlProvider = new SmartCtlProvider(logger, async () => (await settingsService.LoadAsync()).SmartCtlPath);
        var diskProvider = new CompositeDiskInfoProvider(windowsProvider, smartCtlProvider, logger);
        var tbwDatabase = new SsdTbwDatabaseService(paths, logger);
        var diagnosticRuleRepository = new DiagnosticRuleRepository(paths, logger);
        var investigationRepository = new InvestigationRepository(paths, logger);
        var investigationEngine = new InvestigationEngine(
            diagnosticRuleRepository,
            investigationRepository,
            tbwDatabase,
            new InvestigationComparisonService(),
            new SimpleTextFormatter());

        _viewModel = new MainWindowViewModel(
            diskProvider,
            new DiskHealthAnalyzer(),
            new HistoryService(paths, logger),
            new DiskMonitorEventService(paths, logger),
            tbwDatabase,
            new ProcessDiskActivityService(powerShellRunner, logger),
            settingsService,
            new ReportExportService(),
            new OnlineTbwLookupService(logger),
            investigationEngine,
            new InvestigationExportService(),
            new SmartCtlBootstrapService(logger),
            new WpfDialogService(),
            logger);

        DataContext = _viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        SetupTrayIcon();
        Loaded += OnLoaded;
        StateChanged += OnStateChanged;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await _viewModel.InitializeAsync();
        ApplyTheme(_viewModel.SelectedTheme);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedTheme))
        {
            ApplyTheme(_viewModel.SelectedTheme);
        }
    }

    private void SetupTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "DiskHealthAdvisor.ico");
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = File.Exists(iconPath) ? new Drawing.Icon(iconPath) : Drawing.SystemIcons.Application,
            Text = "Disk Health Advisor",
            Visible = true,
            ContextMenuStrip = BuildTrayMenu()
        };

        _notifyIcon.DoubleClick += (_, _) => ShowFromTray();
    }

    private Forms.ContextMenuStrip BuildTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Открыть", null, (_, _) => ShowFromTray());
        menu.Items.Add("Скрыть в трей", null, (_, _) => HideToTray());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) =>
        {
            _isExitRequested = true;
            Close();
        });
        return menu;
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            HideToTray();
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_isExitRequested)
        {
            return;
        }

        e.Cancel = true;
        HideToTray();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_notifyIcon is null)
        {
            return;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _notifyIcon = null;
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = true;
        }
    }

    private void ShowFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    public void ShowFromSingleInstanceSignal()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(ShowFromSingleInstanceSignal);
            return;
        }

        ShowFromTray();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        SettingsOverlay.Visibility = Visibility.Visible;
    }

    private void CloseSettings_Click(object sender, RoutedEventArgs e)
    {
        SettingsOverlay.Visibility = Visibility.Collapsed;
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void ToggleMaximizeWindow_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ApplyTheme(string themeName)
    {
        var theme = NormalizeThemeName(themeName);
        var colors = theme switch
        {
            "graphite" => new ThemeColors("#11151A", "#171D24", "#202832", "#354254", "#A7B0BC", "#9BA8B6", "#0B0F14"),
            "north" => new ThemeColors("#0E1718", "#142124", "#1B3032", "#2E5558", "#56D6C9", "#9FC8C5", "#0A1112"),
            "contrast" => new ThemeColors("#14110D", "#1D1913", "#2B2419", "#5B4524", "#FFB24A", "#D0B999", "#0E0B08"),
            "neon" => new ThemeColors("#120F18", "#191523", "#251B31", "#49335F", "#D66BFF", "#BCA9CA", "#09070D"),
            "scanner" => new ThemeColors("#0B1512", "#121E1A", "#182A24", "#2D5949", "#69FFB5", "#A2CDBB", "#07100D"),
            "pulse" => new ThemeColors("#151015", "#1F171D", "#2B1D27", "#573346", "#FF6B86", "#CFABB3", "#0D080C"),
            "terminal" => new ThemeColors("#08110A", "#101A12", "#172419", "#2A4D30", "#7DFF75", "#A8CAA6", "#050A06"),
            _ => new ThemeColors("#101418", "#171D24", "#1E2630", "#2E3845", "#5E9EFF", "#9BA8B6", "#0D1116")
        };

        Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colors.Window));
        TitleBar.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colors.TitleBar));
        SetBrush("PanelBrush", colors.Panel);
        SetBrush("PanelAltBrush", colors.PanelAlt);
        SetBrush("BorderBrushSoft", colors.Border);
        SetBrush("AccentBrush", colors.Accent);
        SetBrush("MutedTextBrush", colors.Muted);
        ThemeArtworkLayer.Background = CreateThemeArtwork(theme, colors);
    }

    private void SetBrush(string key, string color)
    {
        if (Resources[key] is SolidColorBrush brush)
        {
            brush.Color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color);
        }
    }

    private static string NormalizeThemeName(string? themeName)
    {
        var value = themeName?.ToLowerInvariant() ?? "";
        if (value.Contains("граф") || value.Contains("graph") || value.Contains("сЂР°С„".ToLowerInvariant()))
        {
            return "graphite";
        }

        if (value.Contains("север") || value.Contains("north") || value.Contains("РµРІРµСЂ".ToLowerInvariant()))
        {
            return "north";
        }

        if (value.Contains("контраст") || value.Contains("contrast") || value.Contains("РѕРЅС‚СЂ".ToLowerInvariant()))
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

    private static System.Windows.Media.Brush CreateThemeArtwork(string theme, ThemeColors colors)
    {
        var accent = ToColor(colors.Accent);
        var border = ToColor(colors.Border);
        var panel = ToColor(colors.PanelAlt);
        var window = ToColor(colors.Window);
        var group = new DrawingGroup();
        var bounds = new Rect(0, 0, 1280, 760);

        group.Children.Add(new GeometryDrawing(
            new LinearGradientBrush
            {
                GradientStops = new GradientStopCollection
                {
                    new(WithAlpha(window, 0x90), 0),
                    new(WithAlpha(panel, 0x45), 0.55),
                    new(WithAlpha(accent, 0x18), 1)
                },
                StartPoint = new System.Windows.Point(0, 0),
                EndPoint = new System.Windows.Point(1, 1)
            },
            null,
            new RectangleGeometry(bounds)));

        switch (theme)
        {
            case "graphite":
                AddGraphiteArtwork(group, accent, border);
                break;
            case "north":
                AddNorthArtwork(group, accent, border);
                break;
            case "contrast":
                AddContrastArtwork(group, accent, border);
                break;
            case "neon":
                AddNeonArtwork(group, accent, border);
                break;
            case "scanner":
                AddScannerArtwork(group, accent, border);
                break;
            case "pulse":
                AddPulseArtwork(group, accent, border);
                break;
            case "terminal":
                AddTerminalArtwork(group, accent, border);
                break;
            default:
                AddOceanArtwork(group, accent, border);
                break;
        }

        return new DrawingBrush(group)
        {
            Stretch = Stretch.Fill,
            Viewbox = bounds,
            ViewboxUnits = BrushMappingMode.Absolute
        };
    }

    private static void AddOceanArtwork(DrawingGroup group, System.Windows.Media.Color accent, System.Windows.Media.Color border)
    {
        AddPath(group, "M 20,120 C 170,40 250,170 390,95 S 620,20 780,115 S 1010,205 1240,78", accent, 0x44, 3);
        AddPath(group, "M 80,610 C 220,540 360,680 520,585 S 800,480 1040,590 S 1190,660 1280,610", accent, 0x2E, 2);
        AddEllipse(group, new Rect(945, 72, 240, 240), accent, 0x18, border, 0x24, 1);
        AddEllipse(group, new Rect(1005, 132, 120, 120), accent, 0x12, accent, 0x28, 1.5);
        AddPath(group, "M 910,192 L1035,192 M1095,192 L1220,192 M1035,192 C1060,155 1075,155 1095,192", accent, 0x38, 2);
    }

    private static void AddGraphiteArtwork(DrawingGroup group, System.Windows.Media.Color accent, System.Windows.Media.Color border)
    {
        for (var x = 0; x <= 1280; x += 96)
        {
            AddPath(group, $"M {x},0 L {x + 180},760", border, 0x24, 1);
        }

        AddPath(group, "M 760,70 L1160,70 L1220,135 L1010,278 L805,210 Z", accent, 0x2F, 2);
        AddPath(group, "M 70,525 L260,410 L470,455 L640,350", accent, 0x34, 2.2);
        AddPath(group, "M 835,210 L1010,278 L1010,420 L870,505", border, 0x45, 1.6);
    }

    private static void AddNorthArtwork(DrawingGroup group, System.Windows.Media.Color accent, System.Windows.Media.Color border)
    {
        AddPath(group, "M 40,710 C 180,340 320,620 470,190 S 745,80 860,430 S 1090,620 1250,170", accent, 0x48, 3.5);
        AddPath(group, "M 0,615 C 160,420 280,450 430,260 S 670,115 785,275 S 1010,480 1280,255", border, 0x38, 2);
        AddPath(group, "M 160,115 C 245,180 300,180 380,105 M865,115 C930,180 1000,175 1085,95", accent, 0x34, 2);
        AddEllipse(group, new Rect(960, 42, 190, 190), accent, 0x16, accent, 0x20, 1);
    }

    private static void AddContrastArtwork(DrawingGroup group, System.Windows.Media.Color accent, System.Windows.Media.Color border)
    {
        for (var x = -160; x < 1280; x += 110)
        {
            AddPath(group, $"M {x},760 L {x + 260},0", accent, 0x24, 11);
        }

        AddPath(group, "M 1000,90 L1195,430 L805,430 Z", accent, 0x26, 2.5);
        AddPath(group, "M 1000,170 L1000,315 M1000,350 L1000,382", accent, 0x52, 8);
        AddPath(group, "M 70,650 L230,480 L410,650 Z", border, 0x40, 2);
    }

    private static void AddNeonArtwork(DrawingGroup group, System.Windows.Media.Color accent, System.Windows.Media.Color border)
    {
        AddPath(group, "M 72,160 H265 V248 H420 V130 H610", accent, 0x50, 2.4);
        AddPath(group, "M 710,600 H930 V492 H1150 V360 H1265", accent, 0x42, 2.4);
        AddPath(group, "M 190,585 H340 V488 H515 V420 H680", border, 0x4A, 1.8);
        AddEllipse(group, new Rect(247, 230, 34, 34), accent, 0x35, accent, 0x72, 2);
        AddEllipse(group, new Rect(594, 114, 32, 32), accent, 0x28, accent, 0x66, 2);
        AddEllipse(group, new Rect(1128, 340, 42, 42), accent, 0x28, accent, 0x66, 2);
        AddPath(group, "M 815,110 C 930,70 1010,92 1120,42", accent, 0x36, 2);
    }

    private static void AddScannerArtwork(DrawingGroup group, System.Windows.Media.Color accent, System.Windows.Media.Color border)
    {
        AddPath(group, "M 0,185 H1280 M 0,372 H1280 M 0,560 H1280", accent, 0x25, 4);
        AddPath(group, "M 220,90 H430 V300 H220 Z M 850,420 H1110 V645 H850 Z", border, 0x4A, 2);
        AddPath(group, "M 310,60 V330 M 115,195 H520 M 980,385 V680 M 780,535 H1180", accent, 0x38, 1.8);
        AddEllipse(group, new Rect(230, 115, 160, 160), accent, 0x08, accent, 0x4A, 2);
        AddEllipse(group, new Rect(912, 470, 140, 140), accent, 0x08, accent, 0x4A, 2);
    }

    private static void AddPulseArtwork(DrawingGroup group, System.Windows.Media.Color accent, System.Windows.Media.Color border)
    {
        AddPath(group, "M 20,380 H225 L265,295 L315,520 L382,260 L430,380 H600 L650,335 L705,470 L760,380 H1260", accent, 0x60, 3);
        AddPath(group, "M 90,150 C 210,80 315,260 450,175 S 720,55 900,190 S 1095,320 1240,180", border, 0x34, 2);
        AddEllipse(group, new Rect(885, 250, 210, 210), accent, 0x12, accent, 0x30, 1.5);
        AddEllipse(group, new Rect(935, 300, 110, 110), accent, 0x10, accent, 0x44, 1.5);
    }

    private static void AddTerminalArtwork(DrawingGroup group, System.Windows.Media.Color accent, System.Windows.Media.Color border)
    {
        for (var x = 60; x < 1280; x += 115)
        {
            AddPath(group, $"M {x},50 V690", border, 0x1D, 1);
            AddRectangle(group, new Rect(x - 5, 95 + x % 260, 10, 42), accent, 0x35);
            AddRectangle(group, new Rect(x - 5, 285 + x % 180, 10, 24), accent, 0x28);
        }

        AddPath(group, "M 100,600 H310 V505 H510 V440 H710 V350 H900", accent, 0x4C, 2);
        AddPath(group, "M 945,130 L1115,250 L945,370", accent, 0x3C, 2.4);
        AddPath(group, "M 1125,130 L1195,370", border, 0x42, 2.4);
    }

    private static void AddPath(DrawingGroup group, string pathData, System.Windows.Media.Color color, byte alpha, double thickness)
    {
        group.Children.Add(new GeometryDrawing(
            null,
            new System.Windows.Media.Pen(new SolidColorBrush(WithAlpha(color, alpha)), thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            },
            Geometry.Parse(pathData)));
    }

    private static void AddEllipse(
        DrawingGroup group,
        Rect rect,
        System.Windows.Media.Color fill,
        byte fillAlpha,
        System.Windows.Media.Color stroke,
        byte strokeAlpha,
        double thickness)
    {
        group.Children.Add(new GeometryDrawing(
            new SolidColorBrush(WithAlpha(fill, fillAlpha)),
            new System.Windows.Media.Pen(new SolidColorBrush(WithAlpha(stroke, strokeAlpha)), thickness),
            new EllipseGeometry(rect)));
    }

    private static void AddRectangle(DrawingGroup group, Rect rect, System.Windows.Media.Color color, byte alpha)
    {
        group.Children.Add(new GeometryDrawing(
            new SolidColorBrush(WithAlpha(color, alpha)),
            null,
            new RectangleGeometry(rect, 3, 3)));
    }

    private static System.Windows.Media.Color ToColor(string color)
    {
        return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color);
    }

    private static System.Windows.Media.Color WithAlpha(System.Windows.Media.Color color, byte alpha)
    {
        return System.Windows.Media.Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    private readonly record struct ThemeColors(
        string Window,
        string Panel,
        string PanelAlt,
        string Border,
        string Accent,
        string Muted,
        string TitleBar);
}
