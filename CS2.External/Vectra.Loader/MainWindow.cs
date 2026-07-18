using System.Reflection;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Vectra.External;

namespace Vectra.Loader;

public sealed class MainWindow : Window
{
    private readonly LoaderCoordinator _coordinator;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly bool _autoClose;
    private Border _externalCard = null!;
    private Button _externalButton = null!;
    private Button _startButton = null!;
    private Button _retryButton = null!;
    private Button _closeAction = null!;
    private ProgressBar _progress = null!;
    private Border _statusHost = null!;
    private TextBlock _statusGlyph = null!;
    private TextBlock _statusTitle = null!;
    private TextBlock _statusDetail = null!;
    private bool _externalSelected;
    private int _closeScheduled;

    public MainWindow(LoaderCoordinator coordinator, bool autoClose = true)
    {
        _coordinator = coordinator;
        _autoClose = autoClose;
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.11.0";
        Title = $"Vectra Loader v{version}";
        Width = 700; Height = 430; MinWidth = 640; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true;
        Background = Brushes.Transparent; Foreground = LuminPalette.Text; FontFamily = new FontFamily("Segoe UI");
        Content = BuildShell(version, ReadBuild());
        _coordinator.StatusChanged += CoordinatorStatusChanged;
        Closing += (_, _) => _lifetime.Cancel();
        Closed += (_, _) => { _coordinator.StatusChanged -= CoordinatorStatusChanged; _lifetime.Dispose(); };
        ApplyStatus(LoaderStatus.Selection);
    }

    private UIElement BuildShell(string version, string build)
    {
        var shell = new Border
        {
            Background = LuminPalette.Background,
            BorderBrush = LuminPalette.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(12),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 28, ShadowDepth = 8, Opacity = .35, Direction = 270 }
        };
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(82) });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(90) });
        root.Children.Add(BuildTitleBar(version, build));

        var intro = new StackPanel { Margin = new Thickness(15, 8, 15, 8) };
        intro.Children.Add(new TextBlock { Text = "CHOOSE YOUR CLIENT", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = LuminPalette.Text });
        intro.Children.Add(new TextBlock { Text = "A clean handoff from Steam to your private training environment.", FontSize = 11, Foreground = LuminPalette.Muted, Margin = new Thickness(0, 4, 0, 0) });
        Grid.SetRow(intro, 1); root.Children.Add(intro);

        var cards = new Grid { Margin = new Thickness(15, 0, 15, 12) };
        cards.ColumnDefinitions.Add(new ColumnDefinition()); cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) }); cards.ColumnDefinitions.Add(new ColumnDefinition());
        _externalButton = ProductButton("EXTERNAL", "READY", "Lightweight overlay client", true, out _externalCard);
        _externalButton.Click += (_, _) => SelectExternal(); cards.Children.Add(_externalButton);
        var internalButton = ProductButton("INTERNAL", "COMING SOON", "Reserved for a future client", false, out _);
        AutomationProperties.SetName(internalButton, "Internal coming soon"); internalButton.IsEnabled = false; Grid.SetColumn(internalButton, 2); cards.Children.Add(internalButton);
        Grid.SetRow(cards, 2); root.Children.Add(cards);

        var footer = new Grid { Margin = new Thickness(15, 0, 15, 4) };
        footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(174) });
        footer.Children.Add(BuildStatus());
        var action = new Grid { Margin = new Thickness(12, 8, 0, 8) };
        _startButton = ActionButton("START", LuminPalette.Accent, LuminPalette.AccentText); _startButton.IsEnabled = false; _startButton.Click += StartClicked; AutomationProperties.SetName(_startButton, "Start External"); action.Children.Add(_startButton);
        _retryButton = ActionButton("RETRY", LuminPalette.Warning, LuminPalette.AccentText); _retryButton.Visibility = Visibility.Collapsed; _retryButton.Click += StartClicked; AutomationProperties.SetName(_retryButton, "Retry launch"); action.Children.Add(_retryButton);
        _closeAction = ActionButton("CLOSE", LuminPalette.Widget, LuminPalette.Text); _closeAction.Visibility = Visibility.Collapsed; _closeAction.Margin = new Thickness(0, 40, 0, 0); _closeAction.Click += (_, _) => Close(); AutomationProperties.SetName(_closeAction, "Close loader"); action.Children.Add(_closeAction);
        Grid.SetColumn(action, 1); footer.Children.Add(action); Grid.SetRow(footer, 3); root.Children.Add(footer);
        shell.Child = root;
        return shell;
    }

    private UIElement BuildTitleBar(string version, string build)
    {
        var bar = new Grid { Background = LuminPalette.Surface };
        bar.ColumnDefinitions.Add(new ColumnDefinition()); bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(13, 0, 0, 0) };
        title.Children.Add(new TextBlock { Text = "VECTRA", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = LuminPalette.Accent });
        title.Children.Add(new TextBlock { Text = $"  LOADER  /  v{version}  /  CS2 {build}", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = LuminPalette.Muted, VerticalAlignment = VerticalAlignment.Center });
        bar.Children.Add(title);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(WindowButton("−", "Minimize loader", (_, _) => WindowState = WindowState.Minimized));
        buttons.Children.Add(WindowButton("×", "Close loader window", (_, _) => Close())); Grid.SetColumn(buttons, 1); bar.Children.Add(buttons);
        bar.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        return bar;
    }

    private Button ProductButton(string title, string chip, string detail, bool available, out Border card)
    {
        var scale = new ScaleTransform(.985, .985);
        card = new Border
        {
            Background = LuminPalette.GlassCard(), BorderBrush = available ? LuminPalette.GlassBorder : LuminPalette.Border,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(20),
            RenderTransform = scale, RenderTransformOrigin = new Point(.5, .5),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 16, ShadowDepth = 3, Opacity = .2, Direction = 270 },
            Opacity = available ? 1 : .52
        };
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition());
        var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.Bold, Foreground = available ? LuminPalette.Text : LuminPalette.Muted });
        var badge = new Border { Background = available ? LuminPalette.GlassWidget : LuminPalette.Widget, BorderBrush = available ? LuminPalette.Accent : LuminPalette.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 3, 8, 3) };
        badge.Child = new TextBlock { Text = chip, FontSize = 9, FontWeight = FontWeights.Bold, Foreground = available ? LuminPalette.Accent : LuminPalette.Muted }; Grid.SetColumn(badge, 1); header.Children.Add(badge); root.Children.Add(header);
        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        copy.Children.Add(new TextBlock { Text = detail, FontSize = 11, Foreground = LuminPalette.Muted });
        copy.Children.Add(new TextBlock { Text = available ? "SELECT CLIENT  →" : "NOT AVAILABLE", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = available ? LuminPalette.Accent : LuminPalette.Muted, Margin = new Thickness(0, 10, 0, 0) });
        Grid.SetRow(copy, 1); root.Children.Add(copy); card.Child = root;
        var button = new Button { Content = card, Template = BareButtonTemplate(), Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        if (available) AutomationProperties.SetName(button, "Select External");
        return button;
    }

    private UIElement BuildStatus()
    {
        _statusHost = new Border { Background = LuminPalette.Surface, BorderBrush = LuminPalette.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 9, 12, 9), RenderTransform = new TranslateTransform() };
        var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) }); row.ColumnDefinitions.Add(new ColumnDefinition());
        _statusGlyph = new TextBlock { Text = "○", FontSize = 17, Foreground = LuminPalette.Muted, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(_statusGlyph);
        var copy = new StackPanel(); _statusTitle = new TextBlock { FontSize = 10, FontWeight = FontWeights.Bold, Foreground = LuminPalette.Text }; _statusDetail = new TextBlock { FontSize = 10, Foreground = LuminPalette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        copy.Children.Add(_statusTitle); copy.Children.Add(_statusDetail); Grid.SetColumn(copy, 1); row.Children.Add(copy);
        _progress = new ProgressBar { Height = 2, IsIndeterminate = true, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Bottom, Foreground = LuminPalette.Accent, Background = Brushes.Transparent };
        var root = new Grid(); root.Children.Add(row); root.Children.Add(_progress); _statusHost.Child = root;
        AutomationProperties.SetName(_statusHost, "Loader status");
        return _statusHost;
    }

    private void SelectExternal()
    {
        if (_coordinator.IsRunning || _externalSelected) return;
        _externalSelected = true; _startButton.IsEnabled = true;
        _externalCard.BorderBrush = LuminPalette.Accent; _externalCard.BorderThickness = new Thickness(1.5);
        if (_externalCard.Effect is DropShadowEffect shadow) { shadow.Color = Color.FromRgb(176, 180, 255); shadow.BlurRadius = 24; shadow.Opacity = .28; }
        if (_externalCard.RenderTransform is ScaleTransform scale)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.985, 1, TimeSpan.FromMilliseconds(150)) { EasingFunction = Ease() }, HandoffBehavior.SnapshotAndReplace);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.985, 1, TimeSpan.FromMilliseconds(150)) { EasingFunction = Ease() }, HandoffBehavior.SnapshotAndReplace);
        }
    }

    private async void StartClicked(object sender, RoutedEventArgs e)
    {
        if (!_externalSelected || _coordinator.IsRunning) return;
        _startButton.IsEnabled = false; _externalButton.IsEnabled = false; _retryButton.IsEnabled = false;
        await _coordinator.StartAsync(_lifetime.Token);
    }

    private void CoordinatorStatusChanged(LoaderStatus status)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => ApplyStatus(status)); return; }
        ApplyStatus(status);
    }

    private void ApplyStatus(LoaderStatus status)
    {
        _statusTitle.Text = status.Title; _statusDetail.Text = status.Detail;
        var working = status.State is LoaderState.LaunchingSteam or LoaderState.WaitingForCs2 or LoaderState.Stabilizing or LoaderState.StartingExternal;
        _progress.Visibility = working ? Visibility.Visible : Visibility.Collapsed;
        _statusGlyph.Text = status.State switch { LoaderState.Ready => "✓", LoaderState.Error => "!", _ when working => "•", _ => "○" };
        _statusGlyph.Foreground = status.State switch { LoaderState.Ready => LuminPalette.Accent, LoaderState.Error => LuminPalette.Danger, _ when working => LuminPalette.Warning, _ => LuminPalette.Muted };
        _statusHost.BorderBrush = status.State switch { LoaderState.Ready => LuminPalette.Accent, LoaderState.Error => LuminPalette.Danger, _ when working => LuminPalette.Warning, _ => LuminPalette.Border };

        _startButton.Visibility = status.State == LoaderState.Selection ? Visibility.Visible : Visibility.Collapsed;
        _retryButton.Visibility = status.State == LoaderState.Error ? Visibility.Visible : Visibility.Collapsed;
        _closeAction.Visibility = status.State == LoaderState.Error ? Visibility.Visible : Visibility.Collapsed;
        _retryButton.IsEnabled = status.State == LoaderState.Error;
        if (status.State == LoaderState.Selection) { _startButton.IsEnabled = _externalSelected; _externalButton.IsEnabled = true; }

        _statusHost.BeginAnimation(OpacityProperty, null); _statusHost.Opacity = 1;
        _statusHost.BeginAnimation(OpacityProperty, new DoubleAnimation(.25, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = Ease() }, HandoffBehavior.SnapshotAndReplace);
        if (_statusHost.RenderTransform is TranslateTransform translate)
            translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(5, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = Ease() }, HandoffBehavior.SnapshotAndReplace);

        if (status.State == LoaderState.Ready && _autoClose && Interlocked.CompareExchange(ref _closeScheduled, 1, 0) == 0) _ = CloseAfterSuccessAsync();
    }

    private async Task CloseAfterSuccessAsync()
    {
        try { await Task.Delay(TimeSpan.FromSeconds(2), _lifetime.Token); }
        catch (OperationCanceledException) { return; }
        if (!_lifetime.IsCancellationRequested) Close();
    }

    private static Button ActionButton(string text, Brush background, Brush foreground) => new()
    {
        Content = text, Height = 34, Background = background, Foreground = foreground, BorderBrush = LuminPalette.GlassBorder,
        BorderThickness = new Thickness(1), FontSize = 10, FontWeight = FontWeights.Bold, Padding = new Thickness(12, 0, 12, 0)
    };

    private static Button WindowButton(string text, string name, RoutedEventHandler click)
    {
        var button = new Button { Content = text, Width = 36, Height = 30, Background = Brushes.Transparent, Foreground = LuminPalette.Muted, BorderBrush = Brushes.Transparent, FontSize = 16 };
        AutomationProperties.SetName(button, name); button.Click += click; return button;
    }

    private static ControlTemplate BareButtonTemplate()
    {
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Stretch);
        presenter.SetBinding(ContentPresenter.ContentProperty, new System.Windows.Data.Binding(nameof(ContentControl.Content)) { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        return new ControlTemplate(typeof(Button)) { VisualTree = presenter };
    }

    private static CubicEase Ease() => new() { EasingMode = EasingMode.EaseOut };

    private static string ReadBuild()
    {
        try
        {
            using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Offsets", "info.json"));
            using var document = JsonDocument.Parse(stream);
            return document.RootElement.TryGetProperty("build_number", out var build) ? build.GetInt32().ToString() : "UNKNOWN";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return "UNKNOWN"; }
    }
}
