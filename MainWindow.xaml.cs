using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace LiangWenWidget;

public partial class MainWindow : Window
{
    // module border gradient endpoints
    private static readonly Color GradStart = Color.FromRgb(0xF4, 0x8F, 0xB1); // 浅粉
    private static readonly Color GradEnd   = Color.FromRgb(0x81, 0xD4, 0xFA); // 淡蓝

    // status colors
    private static readonly Brush Pink   = Frozen(Color.FromRgb(0xF4,0x8F,0xB1)); // 峰
    private static readonly Brush Blue   = Frozen(Color.FromRgb(0x81,0xD4,0xFA)); // 谷
    private static readonly Brush Yellow = Frozen(Color.FromRgb(0xFF,0xEB,0x3B)); // 满
    private static readonly Brush GrayW  = Frozen(Color.FromRgb(0xEC,0xEF,0xF1)); // 停
    private static readonly Brush GrayD  = Frozen(Color.FromRgb(0x61,0x61,0x61)); // 崩
    private static readonly Brush Green  = Frozen(Color.FromRgb(0xA5,0xD6,0xA7)); // 活
    private static readonly Brush White  = Frozen(Color.FromRgb(0xFF,0xFF,0xFF));

    private static readonly TimeZoneInfo Tz =
        TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");

    private readonly AppConfig _cfg;
    private readonly DeepSeekClient _client = new();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private DispatcherTimer _poll = new();

    private ApiHealth _health = ApiHealth.Ok;
    private double? _balance;
    private string? _currency;
    private string _detail = "等待首次查询…";
    private DateTime _reviveUntil = DateTime.MinValue;
    private bool _wasDown;

    private ImageSource[] _frames = Array.Empty<ImageSource>();

    public MainWindow()
    {
        InitializeComponent();
        _cfg = AppConfig.Load();
        Loaded += OnLoaded;
        _clock.Tick += (_, _) => UpdateClockAndStatus();
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Icon = CreateIcon();
        ApplyGradientBorders();
        ApplyBackground();
        LoadFrames();

        if (string.IsNullOrWhiteSpace(_cfg.ApiKey))
            ShowFirstRunDialog();

        ConfigurePollTimer();
        _clock.Start();
        _ = PollNow();
    }

    private void ConfigurePollTimer()
    {
        _poll.Stop();
        _poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(5, _cfg.PollIntervalSec)) };
        _poll.Tick += (_, _) => _ = PollNow();
        _poll.Start();
    }

    private void ApplyGradientBorders()
    {
        var borders = new[] { M1, M2, M3, M4, M5 };
        for (int i = 0; i < borders.Length; i++)
        {
            double t = borders.Length == 1 ? 0 : (double)i / (borders.Length - 1);
            var c = Lerp(GradStart, GradEnd, t);
            borders[i].BorderBrush = Frozen(c);
        }
    }

    private static Color Lerp(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));

    private void ApplyBackground()
    {
        if (!string.IsNullOrWhiteSpace(_cfg.BackgroundImagePath) && File.Exists(_cfg.BackgroundImagePath))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(_cfg.BackgroundImagePath);
                bmp.EndInit();
                bmp.Freeze();
                Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };

                double norm = AverageBrightness(bmp); // 0..1
                DimOverlay.Opacity = 0.5 * norm;      // bright -> 50% dim, dark -> 0%
                DimOverlay.Visibility = Visibility.Visible;
                return;
            }
            catch { /* fall back to black */ }
        }
        Background = Brushes.Black;
        DimOverlay.Visibility = Visibility.Collapsed;
    }

    // normalized mean luminance (0..1) of a bitmap, downscaled for speed
    private static double AverageBrightness(BitmapSource src)
    {
        try
        {
            var scaled = new TransformedBitmap(src, new ScaleTransform(
                64.0 / src.PixelWidth, 64.0 / src.PixelHeight));
            var conv = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
            int w = conv.PixelWidth, h = conv.PixelHeight;
            var px = new byte[w * h * 4];
            conv.CopyPixels(px, w * 4, 0);
            double sum = 0;
            for (int i = 0; i < px.Length; i += 4)
            {
                byte b = px[i], g = px[i + 1], r = px[i + 2];
                sum += 0.299 * r + 0.587 * g + 0.114 * b;
            }
            return (sum / (w * h)) / 255.0;
        }
        catch { return 0; }
    }

    private void LoadFrames()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "assets", "frames");
        if (!Directory.Exists(dir)) return;
        var list = new List<ImageSource>();
        foreach (var f in Directory.GetFiles(dir, "frame-*.jpg").OrderBy(x => x, StringComparer.Ordinal))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(f);
                bmp.EndInit();
                bmp.Freeze();
                list.Add(bmp);
            }
            catch { /* skip bad frame */ }
        }
        _frames = list.ToArray();
        if (_frames.Length > 0) AvatarImage.Source = _frames[0];
    }

    private async Task PollNow()
    {
        if (string.IsNullOrWhiteSpace(_cfg.ApiKey))
        {
            _detail = "未设置 API Key";
            UpdateClockAndStatus();
            return;
        }

        var res = await _client.ProbeAsync(_cfg, CancellationToken.None);
        _health = res.Health;
        _detail = res.Detail;
        if (res.Balance is not null) _balance = res.Balance;
        if (res.Currency is not null) _currency = res.Currency;

        if (_health == ApiHealth.Down)
        {
            _wasDown = true;
        }
        else if (_wasDown)
        {
            _wasDown = false;
            _reviveUntil = BeijingNow().AddSeconds(_cfg.ReviveDisplaySec);
        }

        UpdateClockAndStatus();
    }

    private static DateTime BeijingNow() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Tz);

    private void UpdateClockAndStatus()
    {
        var info = PeakCalendar.Compute();
        var now = BeijingNow();
        var remain = info.NextBoundary - now;
        if (remain < TimeSpan.Zero) remain = TimeSpan.Zero;
        CountdownValue.Text = FormatSpan(remain);

        // resolve status char + color (priority)
        string ch; Brush color;
        if (_health == ApiHealth.Down) { ch = "崩"; color = GrayD; }
        else if (now < _reviveUntil) { ch = "活"; color = Green; }
        else if (_health == ApiHealth.Crowded) { ch = "满"; color = Yellow; }
        else if (BalanceTokens() is long tk && tk < _cfg.LowBalanceTokenThreshold) { ch = "停"; color = GrayW; }
        else if (!info.IsPeak) { ch = "谷"; color = Blue; }
        else { ch = "峰"; color = Pink; }

        StatusChar.Text = ch;
        StatusChar.Foreground = color;

        if (_cfg.AvatarEnabled && _frames.Length > 0)
            AvatarImage.Source = _frames[Math.Min(info.FrameIndex, _frames.Length - 1)];

        DescriptionText.Text = BuildDescription(info.IsPeak);
        UpdateBalanceDisplay();
    }

    private string BuildDescription(bool isPeak)
    {
        var cur = _currency ?? "CNY";
        var bal = _balance is null ? "未知" : $"{_balance:F2}";
        var phase = isPeak ? "峰价时段" : "谷价时段";
        return $"{_detail} · 余额 {bal} {cur} · {phase}";
    }

    private long? BalanceTokens()
    {
        if (_balance is null) return null;
        var p = CurrentPricing();
        if (p.CacheMissPerM <= 0) return null;
        return (long)(_balance.Value * 1_000_000 / p.CacheMissPerM);
    }

    private ModelPricing CurrentPricing()
    {
        _cfg.Pricing.TryGetValue(_cfg.Model, out var p);
        return p ?? new ModelPricing { CacheMissPerM = 4, CacheHitPerM = 1, OutputPerM = 16 };
    }

    private void UpdateBalanceDisplay()
    {
        ModelLabel.Text = _cfg.Model;
        if (_balance is null)
        {
            BalanceText.Text = "余量/token 未知";
            return;
        }
        var p = CurrentPricing();
        double miss = _balance.Value * 1_000_000 / p.CacheMissPerM;
        double hit  = _balance.Value * 1_000_000 / p.CacheHitPerM;
        double outp = _balance.Value * 1_000_000 / p.OutputPerM;
        BalanceText.Text = $"未命中 {Fmt(miss)}\n命中 {Fmt(hit)}\n输出 {Fmt(outp)}";
    }

    private static string Fmt(double tokens)
    {
        if (tokens >= 1_000_000_000) return $"{tokens / 1_000_000_000:F2}B";
        if (tokens >= 1_000_000) return $"{tokens / 1_000_000:F2}M";
        if (tokens >= 1_000) return $"{tokens / 1_000:F1}K";
        return $"{tokens:F0}";
    }

    private static string FormatSpan(TimeSpan s)
        => s.TotalHours >= 1
            ? $"{(int)s.TotalHours:D2}:{s.Minutes:D2}:{s.Seconds:D2}"
            : $"{s.Minutes:D2}:{s.Seconds:D2}";

    // ---- interactions ----

    private ImageSource CreateIcon()
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black, new Pen(Frozen(GradStart), 2), new Rect(0, 0, 32, 32));
            var ft = new FormattedText("梁", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Microsoft YaHei UI"), 22, Frozen(GradStart), 1.0);
            dc.DrawText(ft, new Point((32 - ft.Width) / 2, (32 - ft.Height) / 2));
        }
        var rtb = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnFileDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        var img = files.FirstOrDefault(f =>
        {
            var ext = Path.GetExtension(f).ToLowerInvariant();
            return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif";
        });
        if (img is null) return;
        _cfg.BackgroundImagePath = img;
        _cfg.Save();
        ApplyBackground();
    }

    private void OnToggleModel(object sender, RoutedEventArgs e)
    {
        _cfg.Model = _cfg.Model == "flash" ? "pro" : "flash";
        _cfg.Save();
        UpdateBalanceDisplay();
    }

    private void OnClearBackground(object sender, RoutedEventArgs e)
    {
        _cfg.BackgroundImagePath = null;
        _cfg.Save();
        ApplyBackground();
    }

    private void OnNavigate(object sender, RoutedEventArgs e)
    {
        var uri = ((Hyperlink)sender).NavigateUri?.ToString();
        if (string.IsNullOrWhiteSpace(uri)) return;
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch { }
    }

    private void OnReconfigure(object sender, RoutedEventArgs e) => ShowFirstRunDialog();

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void ShowFirstRunDialog()
    {
        var dlg = new SettingsDialog(_cfg) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            _cfg.ApiKey = dlg.ApiKeyResult;
            _cfg.BaseUrl = dlg.BaseUrlResult;
            _cfg.Save();
            ConfigurePollTimer();
            _ = PollNow();
        }
    }
}
