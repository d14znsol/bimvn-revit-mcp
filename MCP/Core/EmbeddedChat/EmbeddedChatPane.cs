#if REVIT2023 || REVIT2025
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Controls.Primitives;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace DSCons.RevitMcp.Core.EmbeddedChat;

internal sealed class EmbeddedChatPaneHost : IDockablePaneProvider, IDisposable
{
    public static readonly DockablePaneId PaneId = new(new Guid("9C07DF57-4401-4B62-B608-710E235D5997"));
    public static EmbeddedChatPaneHost? Current { get; private set; }
    private readonly EmbeddedChatPaneControl _control = new();
    private UIControlledApplication? _controlledApplication;
    private bool _contextCaptured;
    private bool _autoConnected;

    public static EmbeddedChatPaneHost Register(UIControlledApplication application)
    {
        var host = new EmbeddedChatPaneHost();
        application.RegisterDockablePane(PaneId, "DSCons AI · Revit", host);
        host._controlledApplication = application;
        application.Idling += host.OnIdling;
        Current = host;
        return host;
    }

    public void SetupDockablePane(DockablePaneProviderData data)
    {
        data.FrameworkElement = _control;
        data.InitialState = new DockablePaneState { DockPosition = DockPosition.Right };
    }

    public void Show(UIApplication application)
    {
        _contextCaptured = true;
        _control.SetRevitContext(application);
        application.GetDockablePane(PaneId).Show();
        _autoConnected = true;
        _ = _control.EnsureConnectedAsync();
    }

    private void OnIdling(object? sender, IdlingEventArgs args)
    {
        if (sender is not UIApplication application) return;
        if (!_contextCaptured)
        {
            _contextCaptured = true;
            _control.SetRevitContext(application);
        }
        if (_autoConnected) return;
        try
        {
            if (!application.GetDockablePane(PaneId).IsShown()) return;
            _autoConnected = true;
            _ = _control.EnsureConnectedAsync();
        }
        catch (InvalidOperationException) { }
    }

    public void Dispose()
    {
        if (_controlledApplication != null) _controlledApplication.Idling -= OnIdling;
        _controlledApplication = null;
        _control.Dispose();
        if (ReferenceEquals(Current, this)) Current = null;
    }
}

internal sealed class ChatMessageViewModel : INotifyPropertyChanged
{
    private string _text;
    public string Role { get; }
    public string Label { get; }
    public Brush Bubble { get; }
    public Brush Border { get; }
    public Brush Foreground { get; }
    public Brush LabelForeground { get; }
    public HorizontalAlignment Alignment { get; }
    public CornerRadius Radius { get; }
    public double MaxBubbleWidth { get; }
    public string Text { get => _text; set { _text = value; OnPropertyChanged(nameof(Text)); OnPropertyChanged(nameof(RenderedText)); } }
    public string RenderedText => MarkdownLite.Render(Text);
    public event PropertyChangedEventHandler? PropertyChanged;

    public ChatMessageViewModel(string role, string text)
    {
        Role = role; _text = text;
        Label = role == "user" ? "Bạn" : role == "system" ? "Hệ thống" : "DSCons AI";
        Alignment = role == "user" ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        Bubble = role == "user" ? UiBrush(232, 242, 252) : role == "system" ? UiBrush(255, 248, 229) : Brushes.White;
        Border = role == "user" ? UiBrush(190, 216, 241) : role == "system" ? UiBrush(239, 215, 153) : UiBrush(220, 228, 236);
        Foreground = UiBrush(29, 43, 57);
        LabelForeground = role == "system" ? UiBrush(141, 97, 18) : role == "user" ? UiBrush(40, 93, 143) : UiBrush(29, 86, 139);
        Radius = role == "user" ? new CornerRadius(12, 12, 3, 12) : role == "system" ? new CornerRadius(8) : new CornerRadius(3, 12, 12, 12);
        MaxBubbleWidth = role == "user" ? 460 : 620;
    }
    private static SolidColorBrush UiBrush(byte red, byte green, byte blue) => new(Color.FromRgb(red, green, blue));
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal sealed class ChatActivityViewModel
{
    public string Tool { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public Brush Foreground { get; set; } = Brushes.DimGray;
    public string Glyph { get; set; } = "•";
}

internal static class MarkdownLite
{
    public static string Render(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return value.Replace("**", string.Empty).Replace("`", string.Empty).Replace("\r\n", "\n").TrimEnd();
    }
}

internal sealed class MarkdownTextBlock : TextBlock
{
    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
        nameof(Markdown), typeof(string), typeof(MarkdownTextBlock), new PropertyMetadata(string.Empty, OnMarkdownChanged));

    public string Markdown { get => (string)GetValue(MarkdownProperty); set => SetValue(MarkdownProperty, value); }

    public MarkdownTextBlock()
    {
        TextWrapping = TextWrapping.Wrap;
        LineHeight = 19;
    }

    private static void OnMarkdownChanged(DependencyObject target, DependencyPropertyChangedEventArgs args) => ((MarkdownTextBlock)target).Render(args.NewValue as string ?? string.Empty);

    private void Render(string markdown)
    {
        Inlines.Clear();
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var codeBlock = false;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) { codeBlock = !codeBlock; continue; }
            if (Inlines.Count > 0) Inlines.Add(new LineBreak());
            if (codeBlock)
            {
                Inlines.Add(new Run(line) { FontFamily = new FontFamily("Consolas"), FontSize = 11.5, Background = UiBrush(239, 243, 247), Foreground = UiBrush(39, 54, 68) });
                continue;
            }
            var trimmed = line.TrimStart();
            var heading = 0;
            while (heading < trimmed.Length && heading < 3 && trimmed[heading] == '#') heading++;
            if (heading > 0 && heading < trimmed.Length && trimmed[heading] == ' ')
            {
                AddInlineRuns(trimmed.Substring(heading + 1), FontWeights.SemiBold, heading == 1 ? 15 : heading == 2 ? 13.5 : 12.5);
            }
            else if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
            {
                Inlines.Add(new Run("•  ") { Foreground = UiBrush(28, 94, 157), FontWeight = FontWeights.Bold });
                AddInlineRuns(trimmed.Substring(2), FontWeights.Normal, FontSize);
            }
            else AddInlineRuns(line, FontWeights.Normal, FontSize);
        }
    }

    private void AddInlineRuns(string line, FontWeight weight, double size)
    {
        var cursor = 0;
        while (cursor < line.Length)
        {
            var bold = line.IndexOf("**", cursor, StringComparison.Ordinal);
            var code = line.IndexOf('`', cursor);
            var next = bold < 0 ? code : code < 0 ? bold : Math.Min(bold, code);
            if (next < 0) { AddRun(line.Substring(cursor), weight, size, false); break; }
            if (next > cursor) AddRun(line.Substring(cursor, next - cursor), weight, size, false);
            if (next == bold)
            {
                var end = line.IndexOf("**", bold + 2, StringComparison.Ordinal);
                if (end < 0) { AddRun(line.Substring(bold), weight, size, false); break; }
                AddRun(line.Substring(bold + 2, end - bold - 2), FontWeights.SemiBold, size, false); cursor = end + 2;
            }
            else
            {
                var end = line.IndexOf('`', code + 1);
                if (end < 0) { AddRun(line.Substring(code), weight, size, false); break; }
                AddRun(line.Substring(code + 1, end - code - 1), weight, size, true); cursor = end + 1;
            }
        }
        if (line.Length == 0) Inlines.Add(new Run(string.Empty));
    }

    private void AddRun(string value, FontWeight weight, double size, bool code)
    {
        var run = new Run(value) { FontWeight = weight, FontSize = size };
        if (code) { run.FontFamily = new FontFamily("Consolas"); run.FontSize = 11.5; run.Background = UiBrush(235, 241, 246); run.Foreground = UiBrush(32, 73, 111); }
        Inlines.Add(run);
    }

    private static SolidColorBrush UiBrush(byte red, byte green, byte blue) => new(Color.FromRgb(red, green, blue));
}

internal sealed class EmbeddedChatPaneControl : UserControl, IDisposable
{
    private readonly TextBlock _account = BadgeValue();
    private readonly TextBlock _codex = BadgeValue();
    private readonly TextBlock _mcp = BadgeValue();
    private readonly TextBlock _revit = BadgeValue();
    private readonly Ellipse _accountDot = StatusDot();
    private readonly Ellipse _codexDot = StatusDot();
    private readonly Ellipse _mcpDot = StatusDot();
    private readonly WpfComboBox _provider = ComboBoxControl(nameof(ChatProviderOption.DisplayName));
    private readonly WpfComboBox _model = ComboBoxControl(nameof(ChatModelOption.DisplayName));
    private readonly WpfComboBox _conversations = ComboBoxControl(nameof(ChatConversation.DisplayName));
    private readonly ObservableCollection<ChatMessageViewModel> _messages = new();
    private readonly ObservableCollection<ChatActivityViewModel> _activities = new();
    private readonly ListBox _messageList = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent, ItemsSource = null, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top };
    private readonly WpfTextBox _input = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 44, MaxHeight = 116, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(4, 5, 4, 5), BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    private readonly TextBlock _inputPlaceholder = new() { Text = "Nhắn cho DSCons AI…", Foreground = UiBrush(139, 151, 164), IsHitTestVisible = false, Margin = new Thickness(5, 6, 0, 0), VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _progress = new() { Text = "Sẵn sàng", TextWrapping = TextWrapping.Wrap, Foreground = UiBrush(67, 84, 102), VerticalAlignment = VerticalAlignment.Center };
    private readonly Ellipse _progressDot = new() { Width = 7, Height = 7, Fill = UiBrush(53, 170, 112), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _send = PrimaryButton("Gửi");
    private readonly Button _newChat = HeaderActionButton("Mới");
    private readonly Button _deleteChat = HeaderActionButton("Xóa");
    private readonly Button _login = HeaderActionButton("Đăng nhập");
    private readonly Button _memory = HeaderActionButton("Bộ nhớ");
    private readonly Button _usageRefresh = HeaderActionButton("↻");
    private readonly TextBlock _usageSession = UsageValue();
    private readonly TextBlock _usageWeek = UsageValue();
    private readonly TextBlock _usageCredits = UsageValue();
    private readonly TextBlock _usageConfidence = new() { Text = "Không khả dụng", FontSize = 9.5, Foreground = UiBrush(124, 138, 151), VerticalAlignment = VerticalAlignment.Center };
    private readonly Expander _toolDetails = new() { Header = "Hoạt động công cụ", IsExpanded = false, Visibility = Visibility.Collapsed, Foreground = UiBrush(63, 82, 101), Margin = new Thickness(0, 7, 0, 0) };
    private readonly Grid _root = new();
    private readonly UniformGrid _statusGrid = new() { Columns = 3 };
    private readonly UniformGrid _usageGrid = new() { Columns = 3 };
    private readonly Grid _selectorGrid = new();
    private readonly UniformGrid _toolbar = new() { Columns = 4 };
    private readonly Grid _headerTop = new();
    private readonly Grid _projectToolbarRow = new();
    private FrameworkElement _projectBadge = null!;
    private readonly Border _welcomePanel = new();
    private readonly TextBlock _headerSubtitle = new();
    private FrameworkElement _betaBadge = null!;
    private FrameworkElement _providerField = null!;
    private FrameworkElement _modelField = null!;
    private FrameworkElement _historyField = null!;
    private int _responsiveMode = -1;
    private EmbeddedApprovalServer? _approval;
    private IEmbeddedChatProvider? _client;
    private UIApplication? _uiApplication;
    private ApprovalPrompt? _pendingApproval;
    private readonly DispatcherTimer _refreshTimer;
    private readonly object _connectionGate = new();
    private Task<bool>? _connectionTask;
    private ChatProjectContext? _projectContext;
    private ChatMessageViewModel? _streamingMessage;
    private bool _loadingConversation;
    private bool _switchingProvider;
    private bool _disposed;

    public EmbeddedChatPaneControl()
    {
        Background = UiBrush(242, 245, 248);
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 12.5;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        _root.Margin = new Thickness(10);
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _root.Children.Add(CreateHeader());
        _messageList.ItemsSource = _messages;
        _messageList.ItemTemplate = CreateMessageTemplate();
        _messageList.Padding = new Thickness(10, 12, 10, 14);
        _messageList.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        _messageList.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        _messageList.SetValue(ScrollViewer.CanContentScrollProperty, true);
        _messageList.ItemContainerStyle = CreateMessageContainerStyle();
        _messageList.ContextMenu = new WpfContextMenu();
        var copy = new WpfMenuItem { Header = "Sao chép tin đã chọn" };
        copy.Click += (_, _) => CopySelectedMessage();
        _messageList.ContextMenu.Items.Add(copy);
        var conversation = CreateConversationArea(); Grid.SetRow(conversation, 1); _root.Children.Add(conversation);
        var composer = CreateComposer(); Grid.SetRow(composer, 2); _root.Children.Add(composer);
        Content = _root;

        _provider.ItemsSource = EmbeddedChatProviderFactory.Options;
        _provider.SelectedItem = EmbeddedChatProviderFactory.Options.First();

        _send.Click += async (_, _) => await SendOrStopAsync();
        _newChat.Click += async (_, _) => await NewChatAsync();
        _deleteChat.Click += async (_, _) => await DeleteChatAsync();
        _login.Click += async (_, _) => await LoginAsync();
        _memory.Click += (_, _) => ShowMemories();
        _usageRefresh.Click += async (_, _) => await RefreshUsageAsync();
        _provider.SelectionChanged += async (_, _) => await SwitchProviderAsync();
        _conversations.SelectionChanged += async (_, _) => await SelectConversationAsync();
        _input.PreviewKeyDown += async (_, args) =>
        {
            if (args.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                args.Handled = true;
                await SendOrStopAsync();
            }
        };
        _input.TextChanged += (_, _) => _inputPlaceholder.Visibility = string.IsNullOrEmpty(_input.Text) ? Visibility.Visible : Visibility.Collapsed;
        SizeChanged += (_, args) => ApplyResponsiveLayout(args.NewSize.Width);
        Loaded += (_, _) => { ApplyResponsiveLayout(ActualWidth); RefreshRevitContext(); UpdateWelcomeVisibility(); };
        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick += (_, _) => { RefreshRevitContext(); UpdateTurnState(); PollPendingApproval(); };
        _refreshTimer.Start();
    }

    public void SetRevitContext(UIApplication application)
    {
        _uiApplication = application;
        RefreshRevitContext();
    }

    public Task<bool> EnsureConnectedAsync()
    {
        if (_disposed) return Task.FromResult(false);
        lock (_connectionGate)
        {
            if (_connectionTask == null || _connectionTask.IsCompleted) _connectionTask = ConnectAndReportAsync();
            return _connectionTask;
        }
    }

    private async Task<bool> ConnectAndReportAsync()
    {
        SetCompactStatus(_codex, _codexDot, "Đang kết nối", "Đang kết nối");
        try { await GetOrCreateClient().ConnectAsync(); return true; }
        catch (Exception ex)
        {
            SetCompactStatus(_codex, _codexDot, "Chưa sẵn sàng", "Lỗi");
            SetCompactStatus(_mcp, _mcpDot, "Đã khóa thao tác", "Đã khóa");
            AddMessage("system", FriendlyError(ex)); _progress.Text = "Chưa thể kết nối.";
            return false;
        }
    }

    private IEmbeddedChatProvider GetOrCreateClient()
    {
        if (_client != null) return _client;
        var config = EmbeddedChatConfig.Load();
        if (_approval == null)
        {
            _approval = new EmbeddedApprovalServer();
            _approval.ApprovalRequested += OnApprovalRequested; _approval.Start();
        }
        _client = EmbeddedChatProviderFactory.Create(SelectedProviderId, config, _approval);
        BindClientEvents(_client);
        if (_projectContext != null) _client.SetProjectContext(_projectContext);
        return _client;
    }

    private string SelectedProviderId => (_provider.SelectedItem as ChatProviderOption)?.ProviderId ?? "codex";

    private void BindClientEvents(IEmbeddedChatProvider client)
    {
        client.AgentDelta += value => Dispatcher.BeginInvoke(new Action(() => AppendAgentDelta(value)));
        client.Progress += value => Dispatcher.BeginInvoke(new Action(() => _progress.Text = value));
        client.TurnFinished += (success, status) => Dispatcher.BeginInvoke(new Action(() => FinishTurn(success, status)));
        client.StatusChanged += value => Dispatcher.BeginInvoke(new Action(() => ApplyStatus(value)));
        client.LoginUrlAvailable += value => Dispatcher.BeginInvoke(new Action(() => OpenLoginUrl(value)));
        client.ModelsChanged += (models, selected) => Dispatcher.BeginInvoke(new Action(() => SetModels(models, selected)));
        client.ToolActivity += value => Dispatcher.BeginInvoke(new Action(() => AddToolActivity(value)));
        client.ConversationChanged += value => Dispatcher.BeginInvoke(new Action(() => LoadConversation(value)));
        client.UsageChanged += value => Dispatcher.BeginInvoke(new Action(() => ApplyUsage(value)));
        client.Error += value => Dispatcher.BeginInvoke(new Action(() => AddMessage("system", value)));
    }

    private async Task SwitchProviderAsync()
    {
        if (_switchingProvider || _disposed || _client == null || string.Equals(_client.ProviderId, SelectedProviderId, StringComparison.Ordinal)) return;
        if (_client.IsTurnRunning || _pendingApproval != null)
        {
            _switchingProvider = true;
            _provider.SelectedItem = EmbeddedChatProviderFactory.Options.First(option => option.ProviderId == _client.ProviderId);
            _switchingProvider = false;
            AddMessage("system", "Không thể đổi trợ lý AI khi lượt chat hoặc xác nhận đang chạy.");
            return;
        }
        _switchingProvider = true;
        try
        {
            DisposeProviderOnly(); _connectionTask = null; _messages.Clear(); _activities.Clear(); _model.ItemsSource = null; _conversations.ItemsSource = null;
            ApplyUsage(UsageSnapshot.Unavailable(SelectedProviderId, "Đang kiểm tra hạn mức provider."));
            _progress.Text = "Đang đổi sang " + ((_provider.SelectedItem as ChatProviderOption)?.DisplayName ?? "AI") + "…";
            await EnsureConnectedAsync();
        }
        finally { _switchingProvider = false; UpdateWelcomeVisibility(); }
    }

    private async Task SendOrStopAsync()
    {
        if (_client?.IsTurnRunning == true && _pendingApproval == null) { await StopAsync(); return; }
        var text = _input.Text.Trim();
        if (text.Length == 0) return;
        if (_pendingApproval != null)
        {
            if (!TryParseApprovalDecision(text, out var approved)) { _progress.Text = "Đang chờ xác nhận: chat “đồng ý” hoặc “hủy”."; return; }
            _input.Clear(); AddMessage("user", text); AddMessage("system", approved ? "Đã xác nhận yêu cầu đang chờ." : "Đã hủy yêu cầu đang chờ.");
            ResolveApproval(approved); return;
        }
        RefreshRevitContext();
        if (!await EnsureConnectedAsync() || _client == null) return;
        if (TryRemember(text, out var memoryNote)) AddMessage("system", memoryNote);
        _input.Clear(); AddMessage("user", text);
        _streamingMessage = null; _activities.Clear(); _toolDetails.Visibility = Visibility.Collapsed; SetBusy(true);
        try { await _client.StartTurnAsync(text, (_model.SelectedItem as ChatModelOption)?.Model); }
        catch (Exception ex) { AddMessage("system", FriendlyError(ex)); SetBusy(false); }
    }

    private bool TryRemember(string text, out string note)
    {
        note = string.Empty;
        var normalized = Normalize(text);
        var phrases = new[] { "hay nho", "ghi nho" };
        if (!phrases.Any(normalized.StartsWith)) return false;
        var value = text.Substring(text.IndexOf(' ') + 1).Trim();
        if (value.Length < 3 || _client == null) return false;
        var global = normalized.Contains("tat ca project") || normalized.Contains("moi project") || normalized.Contains("tat ca du an");
        if (!_client.Remember(value, global))
        {
            note = "Không lưu Bộ nhớ: chỉ lưu sở thích bền vững, không lưu đường dẫn, PID, ID, preview hay thông tin đăng nhập.";
            return true;
        }
        note = global ? "Đã lưu vào Bộ nhớ dùng cho mọi project." : "Đã lưu vào Bộ nhớ của project này.";
        return true;
    }

    private async Task StopAsync()
    {
        try { if (_client != null) await _client.InterruptAsync(); }
        catch (Exception ex) { AddMessage("system", FriendlyError(ex)); }
    }

    private async Task NewChatAsync()
    {
        try
        {
            if (!await EnsureConnectedAsync() || _client == null) return;
            await _client.NewConversationAsync();
            _progress.Text = "Đã mở cuộc trò chuyện mới cho project này.";
        }
        catch (Exception ex) { AddMessage("system", FriendlyError(ex)); }
    }

    private async Task DeleteChatAsync()
    {
        try
        {
            var selected = _conversations.SelectedItem as ChatConversation;
            if (selected == null || _client == null) return;
            await _client.DeleteConversationAsync(selected.Id);
            _progress.Text = "Đã xóa cuộc trò chuyện cục bộ.";
        }
        catch (Exception ex) { AddMessage("system", FriendlyError(ex)); }
    }

    private async Task SelectConversationAsync()
    {
        if (_loadingConversation || _client == null || _conversations.SelectedItem is not ChatConversation selected) return;
        try { await _client.SelectConversationAsync(selected.Id); }
        catch (Exception ex) { AddMessage("system", FriendlyError(ex)); }
    }

    private async Task LoginAsync()
    {
        try { _progress.Text = "Đang kiểm tra đăng nhập " + (GetOrCreateClient().DisplayName) + "…"; await GetOrCreateClient().StartLoginAsync(); }
        catch (Exception ex) { AddMessage("system", FriendlyError(ex)); }
    }

    private void OnApprovalRequested(ApprovalPrompt prompt) => Dispatcher.BeginInvoke(new Action(() => ShowPendingApproval(prompt)));
    private void PollPendingApproval() { if (_approval?.PendingPrompt is { } prompt) ShowPendingApproval(prompt); }

    private void ShowPendingApproval(ApprovalPrompt prompt)
    {
        if (_disposed || prompt.Decision.Task.IsCompleted) return;
        if (_pendingApproval != null)
        {
            if (!string.Equals(_pendingApproval.RequestId, prompt.RequestId, StringComparison.Ordinal)) prompt.Decision.TrySetResult(false);
            return;
        }
        _pendingApproval = prompt; RefreshRevitContext();
        AddMessage("system", "Preview PASS. DSCons đang chờ bạn chat “đồng ý”, “thực hiện”, “tiếp tục”, “làm đi”, “ok” hoặc “xác nhận”; chat “hủy” để dừng.");
        _progress.Text = "Đang chờ bạn xác nhận."; SetBusy(true); _input.Focus();
    }

    private void ResolveApproval(bool approved)
    {
        var prompt = _pendingApproval; _pendingApproval = null;
        if (prompt == null) return;
        prompt.Decision.TrySetResult(approved);
        _progress.Text = approved ? "Đang Apply và kiểm tra kết quả…" : "Đã hủy. Model không thay đổi bởi yêu cầu này.";
        SetBusy(_client?.IsTurnRunning == true);
    }

    private void RefreshRevitContext()
    {
        try
        {
            var uiDocument = _uiApplication?.ActiveUIDocument;
            if (uiDocument == null) { _revit.Text = "Chưa mở Project"; _revit.ToolTip = _revit.Text; return; }
            var document = uiDocument.Document;
            var path = string.IsNullOrWhiteSpace(document.PathName) ? null : document.PathName;
            var context = path == null && _projectContext != null && !_projectContext.IsPersistable && _projectContext.DisplayName.StartsWith(document.Title, StringComparison.Ordinal)
                ? _projectContext : ChatProjectContext.Create(path, document.Title);
            var changed = _projectContext == null || !string.Equals(_projectContext.Key, context.Key, StringComparison.Ordinal);
            _projectContext = context;
            _revit.Text = document.Title + "  /  " + (document.ActiveView?.Name ?? "Không có view");
            _revit.ToolTip = _revit.Text;
            if (changed && _client != null)
            {
                if (_client.IsTurnRunning)
                {
                    _pendingApproval?.Decision.TrySetResult(false); _pendingApproval = null;
                    _ = _client.InterruptAsync(); AddMessage("system", "Đã đổi Project, nên yêu cầu cũ được hủy an toàn.");
                }
                _client.SetProjectContext(context);
                RefreshConversationList();
            }
        }
        catch (Exception ex) { _revit.Text = "Không đọc được Project: " + ex.Message; }
    }

    private void UpdateTurnState()
    {
        if (_pendingApproval != null || _client == null) return;
        var activity = _client.GetTurnActivity();
        if (!activity.IsRunning) return;
        var idle = DateTime.UtcNow - activity.LastActivityUtc;
        if (idle >= TimeSpan.FromSeconds(60)) _progress.Text = "Phản hồi đang chậm, bạn có thể bấm Dừng.";
        else if (!string.IsNullOrWhiteSpace(activity.Stage)) _progress.Text = FriendlyStage(activity.Stage);
    }

    private void AppendAgentDelta(string value)
    {
        _streamingMessage ??= AddMessage("assistant", string.Empty);
        _streamingMessage.Text += value;
        ScrollToEnd();
    }

    private void FinishTurn(bool success, string status)
    {
        if (_streamingMessage != null && _client != null) _client.RecordMessage("assistant", _streamingMessage.Text);
        _streamingMessage = null; SetBusy(false);
        _progress.Text = success ? "Hoàn tất." : "Lượt chat kết thúc: " + status;
    }

    private void AddToolActivity(ChatToolActivity activity)
    {
        var friendly = FriendlyToolName(activity.Tool);
        var item = new ChatActivityViewModel
        {
            Tool = activity.Tool,
            Text = activity.IsError ? "Có lỗi · " + friendly : activity.IsComplete ? "Hoàn tất · " + friendly : "Đang gọi · " + friendly,
            Glyph = activity.IsError ? "!" : activity.IsComplete ? "✓" : "•",
            Foreground = activity.IsError ? UiBrush(186, 55, 55) : activity.IsComplete ? UiBrush(38, 137, 91) : UiBrush(38, 101, 163)
        };
        var pending = _activities.Select((value, index) => new { value, index }).LastOrDefault(entry => string.Equals(entry.value.Tool, activity.Tool, StringComparison.Ordinal) && entry.value.Glyph == "•");
        if (activity.IsComplete && pending != null) _activities[pending.index] = item; else _activities.Add(item);
        _toolDetails.Header = $"Hoạt động công cụ  ·  {_activities.Count}";
        _toolDetails.Visibility = Visibility.Visible;
        _progress.Text = FriendlyStage(item.Text);
    }

    private void LoadConversation(ChatConversation conversation)
    {
        _loadingConversation = true;
        _messages.Clear(); _activities.Clear(); _streamingMessage = null;
        foreach (var item in conversation.Messages) AddMessage(item.Role, item.Text, false);
        _toolDetails.Visibility = Visibility.Collapsed;
        RefreshConversationList(conversation.Id);
        _loadingConversation = false; UpdateWelcomeVisibility(); ScrollToEnd();
    }

    private void RefreshConversationList(string? selectedId = null)
    {
        if (_client == null) return;
        _loadingConversation = true;
        var items = _client.ListConversations();
        _conversations.ItemsSource = items;
        var desired = selectedId ?? _client.CurrentConversation?.Id;
        _conversations.SelectedItem = items.FirstOrDefault(item => item.Id == desired) ?? items.FirstOrDefault();
        _conversations.IsEnabled = items.Count > 0 && !_client.IsTurnRunning;
        _deleteChat.IsEnabled = _conversations.SelectedItem != null && !_client.IsTurnRunning;
        _loadingConversation = false;
    }

    private void ShowMemories()
    {
        if (_client == null) { AddMessage("system", "Kết nối trợ lý AI trước khi xem Bộ nhớ."); return; }
        var items = new ObservableCollection<ChatMemoryEntry>(_client.GetMemories());
        var list = new ListBox { ItemsSource = items, DisplayMemberPath = nameof(ChatMemoryEntry.Text), MinHeight = 160 };
        var editor = new WpfTextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 52, Margin = new Thickness(0, 8, 0, 4) };
        var global = new CheckBox { Content = "Dùng cho mọi project", Margin = new Thickness(0, 2, 0, 6) };
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is ChatMemoryEntry selected) { editor.Text = selected.Text; global.IsChecked = selected.Scope == "global"; }
        };
        var save = PrimaryButton("Lưu"); var forget = SecondaryButton("Quên mục chọn"); var close = SecondaryButton("Đóng");
        var buttons = new WrapPanel(); buttons.Children.Add(save); buttons.Children.Add(forget); buttons.Children.Add(close);
        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = "Bộ nhớ AI", FontSize = 16, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Chỉ lưu các ưu tiên bền vững. Không lưu ID, token, đường dẫn hay trạng thái Preview.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 4, 0, 6) });
        panel.Children.Add(list); panel.Children.Add(editor); panel.Children.Add(global); panel.Children.Add(buttons);
        var window = new Window { Title = "DSCons · Bộ nhớ", Content = panel, Width = 420, Height = 420, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Window.GetWindow(this) };
        save.Click += (_, _) =>
        {
            var value = editor.Text.Trim(); if (value.Length < 3) return;
            if (list.SelectedItem is ChatMemoryEntry old) { _client.Forget(old.Id); items.Remove(old); }
            if (!_client.Remember(value, global.IsChecked == true))
            {
                MessageBox.Show("Chỉ lưu sở thích bền vững; không lưu đường dẫn, PID, ID, preview hoặc thông tin đăng nhập.", "DSCons · Bộ nhớ", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            items.Clear(); foreach (var item in _client.GetMemories()) items.Add(item);
            editor.Clear(); global.IsChecked = false; list.SelectedItem = null;
            AddMessage("system", "Đã lưu Bộ nhớ do bạn chủ động chọn.");
        };
        forget.Click += (_, _) =>
        {
            if (list.SelectedItem is not ChatMemoryEntry selected) return;
            _client.Forget(selected.Id); items.Remove(selected); editor.Clear(); AddMessage("system", "Đã quên mục Bộ nhớ đã chọn.");
        };
        close.Click += (_, _) => window.Close();
        window.ShowDialog();
    }

    private ChatMessageViewModel AddMessage(string role, string text, bool persist = true)
    {
        var item = new ChatMessageViewModel(role, text); _messages.Add(item);
        if (persist && role != "assistant") _client?.RecordMessage(role, text);
        UpdateWelcomeVisibility(); ScrollToEnd(); return item;
    }

    private void SetModels(IReadOnlyList<ChatModelOption> models, string? selected)
    {
        var previous = (_model.SelectedItem as ChatModelOption)?.Model;
        _model.ItemsSource = models;
        _model.SelectedItem = models.FirstOrDefault(item => string.Equals(item.Model, previous ?? selected, StringComparison.Ordinal)) ?? models.FirstOrDefault();
        _model.IsEnabled = models.Count > 0 && _client?.IsTurnRunning != true;
    }

    private void ApplyStatus(ChatProviderStatus status)
    {
        SetCompactStatus(_account, _accountDot, status.Account, CompactStatus(status.Account, "Tài khoản"));
        SetCompactStatus(_codex, _codexDot, status.Provider, CompactStatus(status.Provider, "AI"));
        SetCompactStatus(_mcp, _mcpDot, status.Mcp, CompactStatus(status.Mcp, "MCP"));
    }

    private async Task RefreshUsageAsync()
    {
        if (_client == null) return;
        _usageRefresh.IsEnabled = false;
        try { await _client.RefreshUsageAsync(true, CancellationToken.None); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _progress.Text = "Không cập nhật được hạn mức: " + FriendlyError(ex); }
        finally { _usageRefresh.IsEnabled = true; }
    }

    private void ApplyUsage(UsageSnapshot snapshot)
    {
        var session = snapshot.Windows.FirstOrDefault(window => window.Kind == UsageWindowKind.Session5Hours)
            ?? snapshot.Windows.FirstOrDefault(window => window.Kind == UsageWindowKind.ProviderSpecific);
        var weekly = snapshot.Windows.FirstOrDefault(window => window.Kind == UsageWindowKind.Weekly);
        _usageSession.Text = FormatUsageWindow(session);
        _usageWeek.Text = FormatUsageWindow(weekly);
        _usageCredits.Text = FormatCredits(snapshot.Credits);
        _usageConfidence.Text = snapshot.Confidence switch
        {
            UsageConfidence.Exact => "Trực tiếp",
            UsageConfidence.Partial => "Một phần",
            _ => "Không khả dụng"
        };
        var tooltip = snapshot.Message;
        if (!string.IsNullOrWhiteSpace(snapshot.HelpText)) tooltip += " " + snapshot.HelpText;
        if (!string.IsNullOrWhiteSpace(snapshot.Source)) tooltip += " Nguồn: " + snapshot.Source + ".";
        _usageConfidence.ToolTip = tooltip.Trim();
        _usageSession.ToolTip = _usageConfidence.ToolTip;
        _usageWeek.ToolTip = _usageConfidence.ToolTip;
        _usageCredits.ToolTip = _usageConfidence.ToolTip;
    }

    private static string FormatUsageWindow(UsageWindow? window)
    {
        if (window == null) return "Không khả dụng";
        var value = window.UsedPercent.HasValue
            ? Math.Max(0, 100 - window.UsedPercent.Value).ToString("0.#", CultureInfo.InvariantCulture) + "% còn lại"
            : window.Remaining.HasValue ? window.Remaining.Value.ToString("0.##", CultureInfo.InvariantCulture) + " còn lại" : "Một phần";
        if (window.ResetAtUtc.HasValue) value += " · " + window.ResetAtUtc.Value.ToLocalTime().ToString("HH:mm dd/MM", CultureInfo.CurrentCulture);
        return value;
    }

    private static string FormatCredits(UsageCredits? credits)
    {
        if (credits == null) return "Không khả dụng";
        if (credits.Unlimited == true) return "Không giới hạn";
        if (credits.HasCredits == false) return "Không có";
        return string.IsNullOrWhiteSpace(credits.Balance) ? "Có" : credits.Balance!;
    }
    private void SetBusy(bool busy)
    {
        var waiting = _pendingApproval != null;
        _send.Content = busy && !waiting ? "Dừng" : "Gửi";
        _send.Background = busy && !waiting ? UiBrush(174, 55, 55) : UiBrush(24, 94, 160);
        _send.BorderBrush = _send.Background;
        _progressDot.Fill = waiting ? UiBrush(222, 155, 32) : busy ? UiBrush(39, 116, 189) : UiBrush(53, 170, 112);
        if (busy && !waiting)
        {
            _progressDot.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1.0, TimeSpan.FromMilliseconds(720)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        }
        else
        {
            _progressDot.BeginAnimation(OpacityProperty, null); _progressDot.Opacity = 1;
        }
        _send.IsEnabled = true; _newChat.IsEnabled = !busy && !waiting; _deleteChat.IsEnabled = !busy && !waiting && _conversations.SelectedItem != null;
        _provider.IsEnabled = !busy && !waiting; _model.IsEnabled = !busy && _model.Items.Count > 0; _conversations.IsEnabled = !busy && _conversations.Items.Count > 0;
    }

    private void CopySelectedMessage()
    {
        if (_messageList.SelectedItem is ChatMessageViewModel item) Clipboard.SetText(item.Text);
    }
    private void ScrollToEnd() { if (_messages.Count > 0) _messageList.ScrollIntoView(_messages[_messages.Count - 1]); }

    private static bool TryParseApprovalDecision(string text, out bool approved)
    {
        var value = Normalize(text);
        if (new[] { "dong y", "thuc hien", "xac nhan", "xac nhan thuc hien", "tiep tuc", "lam di", "ok", "okay" }.Contains(value)) { approved = true; return true; }
        if (new[] { "huy", "khong thuc hien", "tu choi", "dung lai", "thoi" }.Contains(value)) { approved = false; return true; }
        approved = false; return false;
    }

    private static string Normalize(string text)
    {
        var decomposed = text.Trim().ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed) if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark) builder.Append(char.IsLetterOrDigit(character) ? character : ' ');
        return string.Join(" ", builder.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string FriendlyStage(string text)
    {
        if (text.IndexOf("preview", StringComparison.OrdinalIgnoreCase) >= 0) return "Đang gọi Preview…";
        if (text.IndexOf("apply", StringComparison.OrdinalIgnoreCase) >= 0) return "Đang Apply…";
        if (text.IndexOf("document", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("context", StringComparison.OrdinalIgnoreCase) >= 0) return "Đang đọc Project…";
        return text;
    }

    private string FriendlyError(Exception ex)
    {
        var text = ex.GetBaseException().Message;
        var provider = _client?.DisplayName ?? ((_provider.SelectedItem as ChatProviderOption)?.DisplayName ?? "AI");
        if (text.IndexOf("rate", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("limit", StringComparison.OrdinalIgnoreCase) >= 0) return "Tài khoản " + provider + " đã chạm hạn mức. Hãy thử lại sau.";
        if (text.IndexOf("login", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("account", StringComparison.OrdinalIgnoreCase) >= 0) return provider + " chưa đăng nhập. Hãy dùng lệnh đăng nhập chính thức của CLI rồi thử lại.";
        return text;
    }

    private void OpenLoginUrl(string url)
    {
        var provider = _client?.DisplayName ?? "AI";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)) { AddMessage("system", provider + " trả về liên kết đăng nhập không hợp lệ."); return; }
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); _progress.Text = "Đã mở trang đăng nhập " + provider + "."; }
        catch (Exception ex) { AddMessage("system", "Không mở được trình duyệt: " + ex.Message); }
    }

    private UIElement CreateHeader()
    {
        var card = Card();
        var panel = new StackPanel();
        var brand = _headerTop;
        brand.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        brand.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        brand.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        brand.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var mark = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(10),
            Background = UiBrush(20, 85, 148),
            Child = new TextBlock { Text = "D", Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        brand.Children.Add(mark);
        var titles = new StackPanel { Margin = new Thickness(10, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = "DSCons AI", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = UiBrush(20, 48, 76) });
        _headerSubtitle.Text = "Trợ lý Revit · mọi thay đổi đều qua Preview và xác nhận";
        _headerSubtitle.FontSize = 11;
        _headerSubtitle.Foreground = UiBrush(104, 119, 134);
        _headerSubtitle.TextTrimming = TextTrimming.CharacterEllipsis;
        titles.Children.Add(_headerSubtitle);
        Grid.SetColumn(titles, 1); brand.Children.Add(titles);
        var beta = new Border { Background = UiBrush(232, 241, 250), CornerRadius = new CornerRadius(10), Padding = new Thickness(7, 3, 7, 3), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = "BETA · R23/25", FontSize = 9.5, FontWeight = FontWeights.SemiBold, Foreground = UiBrush(28, 91, 151) } };
        _betaBadge = beta;
        Grid.SetColumn(beta, 2); brand.Children.Add(beta);
        panel.Children.Add(brand);

        _statusGrid.Margin = new Thickness(0, 11, 0, 0);
        _statusGrid.Children.Add(StatusItem("TÀI KHOẢN", _account, _accountDot));
        _statusGrid.Children.Add(StatusItem("AI", _codex, _codexDot));
        _statusGrid.Children.Add(StatusItem("MCP", _mcp, _mcpDot));
        panel.Children.Add(_statusGrid);

        panel.Children.Add(CreateUsageCard());

        var project = new Border { Background = UiBrush(239, 245, 251), CornerRadius = new CornerRadius(7), Padding = new Thickness(9, 6, 9, 6) };
        _projectBadge = project;
        var projectGrid = new Grid();
        projectGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        projectGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        projectGrid.Children.Add(new TextBlock { Text = "RVT", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = UiBrush(36, 101, 163), Margin = new Thickness(0, 1, 9, 0) });
        _revit.TextTrimming = TextTrimming.CharacterEllipsis; _revit.TextWrapping = TextWrapping.NoWrap;
        Grid.SetColumn(_revit, 1); projectGrid.Children.Add(_revit); project.Child = projectGrid;
        _projectToolbarRow.Margin = new Thickness(0, 9, 0, 0);
        _projectToolbarRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _projectToolbarRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _projectToolbarRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _projectToolbarRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _projectToolbarRow.Children.Add(project);
        foreach (var button in new[] { _newChat, _deleteChat, _memory, _login }) _toolbar.Children.Add(button);
        _toolbar.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(_toolbar, 1); _projectToolbarRow.Children.Add(_toolbar);
        panel.Children.Add(_projectToolbarRow);

        _providerField = SelectorField("Trợ lý AI", _provider);
        _modelField = SelectorField("Model AI", _model);
        _historyField = SelectorField("Cuộc trò chuyện", _conversations);
        _selectorGrid.Margin = new Thickness(0, 10, 0, 0);
        _selectorGrid.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Children.Add(_selectorGrid);
        card.Child = panel;
        return card;
    }

    private UIElement CreateConversationArea()
    {
        var card = Card();
        card.Margin = new Thickness(0, 8, 0, 0);
        card.Padding = new Thickness(0);
        var content = new Grid();
        content.Children.Add(_messageList);
        var welcome = new Grid { Margin = new Thickness(12, 18, 12, 7) };
        welcome.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        welcome.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var hero = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 480 };
        hero.Children.Add(new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(14), Background = UiBrush(232, 241, 250), HorizontalAlignment = HorizontalAlignment.Center, Child = new TextBlock { Text = "✦", FontSize = 20, Foreground = UiBrush(24, 94, 160), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
        hero.Children.Add(new TextBlock { Text = "Bạn muốn làm gì trong Revit?", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = UiBrush(24, 48, 72), TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 10, 0, 4) });
        hero.Children.Add(new TextBlock { Text = "Mô tả tự nhiên như khi trao đổi với đồng nghiệp. DSCons AI sẽ đọc project và báo rõ từng bước.", Foreground = UiBrush(104, 119, 134), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
        welcome.Children.Add(hero);
        var suggestions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 14, 0, 0) };
        foreach (var value in new[] { "Kiểm tra kết nối", "Vẽ ống", "Tạo Schedule", "Tạo Sheet", "Tạo Family" })
        {
            var button = SuggestionButton(value); button.Click += (_, _) => { _input.Text = value; _input.CaretIndex = _input.Text.Length; _input.Focus(); };
            suggestions.Children.Add(button);
        }
        Grid.SetRow(suggestions, 1); welcome.Children.Add(suggestions);
        _welcomePanel.Background = Brushes.Transparent; _welcomePanel.Child = welcome; content.Children.Add(_welcomePanel);
        card.Child = content;
        return card;
    }

    private UIElement CreateUsageCard()
    {
        var header = new Grid { Margin = new Thickness(0, 0, 0, 5) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock { Text = "HẠN MỨC", FontSize = 8.5, FontWeight = FontWeights.SemiBold, Foreground = UiBrush(124, 138, 151), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(_usageConfidence, 1); header.Children.Add(_usageConfidence);
        _usageRefresh.ToolTip = "Cập nhật hạn mức"; _usageRefresh.Margin = new Thickness(5, 0, 0, 0); _usageRefresh.MinWidth = 25; _usageRefresh.Padding = new Thickness(5, 2, 5, 2);
        Grid.SetColumn(_usageRefresh, 2); header.Children.Add(_usageRefresh);
        _usageGrid.Children.Add(UsageItem("PHIÊN HIỆN TẠI", _usageSession));
        _usageGrid.Children.Add(UsageItem("TUẦN", _usageWeek));
        _usageGrid.Children.Add(UsageItem("CREDITS", _usageCredits));
        var stack = new StackPanel(); stack.Children.Add(header); stack.Children.Add(_usageGrid);
        return new Border { Child = stack, Background = UiBrush(247, 249, 251), BorderBrush = UiBrush(224, 231, 238), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 7, 0, 0) };
    }

    private UIElement CreateComposer()
    {
        var card = Card();
        card.Margin = new Thickness(0, 8, 0, 0);
        var panel = new StackPanel();
        var progress = new DockPanel();
        progress.Children.Add(_progressDot); progress.Children.Add(_progress); panel.Children.Add(progress);
        _toolDetails.Content = new ItemsControl { ItemsSource = _activities, ItemTemplate = CreateActivityTemplate() }; panel.Children.Add(_toolDetails);
        var inputShell = new Border { Background = UiBrush(247, 249, 251), BorderBrush = UiBrush(207, 217, 226), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 4, 6, 4), Margin = new Thickness(0, 9, 0, 0) };
        var inputGrid = new Grid();
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var inputLayer = new Grid(); inputLayer.Children.Add(_inputPlaceholder); inputLayer.Children.Add(_input); inputGrid.Children.Add(inputLayer);
        _send.Margin = new Thickness(8, 3, 0, 3); _send.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetColumn(_send, 1); inputGrid.Children.Add(_send); inputShell.Child = inputGrid; panel.Children.Add(inputShell);
        panel.Children.Add(new TextBlock { Text = "Enter để gửi  ·  Shift+Enter để xuống dòng", FontSize = 10.5, Foreground = UiBrush(116, 130, 144), Margin = new Thickness(2, 7, 0, 0) });
        card.Child = panel;
        return card;
    }

    private static DataTemplate CreateMessageTemplate()
    {
        var root = new FrameworkElementFactory(typeof(StackPanel));
        root.SetBinding(StackPanel.HorizontalAlignmentProperty, new Binding(nameof(ChatMessageViewModel.Alignment)));
        root.SetValue(StackPanel.MarginProperty, new Thickness(2, 0, 2, 10));
        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, new Binding(nameof(ChatMessageViewModel.Label)));
        label.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(ChatMessageViewModel.LabelForeground)));
        label.SetBinding(TextBlock.HorizontalAlignmentProperty, new Binding(nameof(ChatMessageViewModel.Alignment)));
        label.SetValue(TextBlock.FontSizeProperty, 10.5d); label.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); label.SetValue(TextBlock.MarginProperty, new Thickness(3, 0, 3, 3)); root.AppendChild(label);
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty, new Binding(nameof(ChatMessageViewModel.Bubble)));
        border.SetBinding(Border.BorderBrushProperty, new Binding(nameof(ChatMessageViewModel.Border)));
        border.SetBinding(Border.CornerRadiusProperty, new Binding(nameof(ChatMessageViewModel.Radius)));
        border.SetBinding(Border.MaxWidthProperty, new Binding(nameof(ChatMessageViewModel.MaxBubbleWidth)));
        border.SetBinding(Border.HorizontalAlignmentProperty, new Binding(nameof(ChatMessageViewModel.Alignment)));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1)); border.SetValue(Border.PaddingProperty, new Thickness(11, 8, 11, 8));
        var text = new FrameworkElementFactory(typeof(MarkdownTextBlock)); text.SetBinding(MarkdownTextBlock.MarkdownProperty, new Binding(nameof(ChatMessageViewModel.Text))); text.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(ChatMessageViewModel.Foreground))); border.AppendChild(text); root.AppendChild(border);
        return new DataTemplate { VisualTree = root };
    }

    private static DataTemplate CreateActivityTemplate()
    {
        var row = new FrameworkElementFactory(typeof(StackPanel)); row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal); row.SetValue(StackPanel.MarginProperty, new Thickness(2, 3, 0, 3));
        var glyph = new FrameworkElementFactory(typeof(TextBlock)); glyph.SetBinding(TextBlock.TextProperty, new Binding(nameof(ChatActivityViewModel.Glyph))); glyph.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(ChatActivityViewModel.Foreground))); glyph.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold); glyph.SetValue(TextBlock.WidthProperty, 18d); row.AppendChild(glyph);
        var text = new FrameworkElementFactory(typeof(TextBlock)); text.SetBinding(TextBlock.TextProperty, new Binding(nameof(ChatActivityViewModel.Text))); text.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(ChatActivityViewModel.Foreground))); text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); row.AppendChild(text);
        return new DataTemplate { VisualTree = row };
    }

    private void ApplyResponsiveLayout(double width)
    {
        if (double.IsNaN(width) || width <= 0) return;
        var mode = width >= 620 ? 3 : width >= 390 ? 2 : width >= 340 ? 1 : 0;
        if (mode == _responsiveMode) return;
        _responsiveMode = mode;
        _root.Margin = mode == 0 ? new Thickness(6) : new Thickness(10);
        _headerSubtitle.Visibility = mode == 0 ? Visibility.Collapsed : Visibility.Visible;
        _messageList.Padding = mode == 0 ? new Thickness(6, 10, 6, 12) : new Thickness(10, 12, 10, 14);
        _login.Content = mode == 0 ? "ĐN" : "Đăng nhập";
        _login.ToolTip = mode == 0 ? "Đăng nhập" : null;

        _betaBadge.Visibility = mode == 0 ? Visibility.Collapsed : Visibility.Visible;
        _usageGrid.Columns = width >= 390 ? 3 : 1;
        if (width >= 340)
        {
            _toolbar.Columns = 4; _toolbar.Margin = new Thickness(7, 0, 0, 0); _toolbar.Width = double.NaN;
            Grid.SetRow(_projectBadge, 0); Grid.SetColumn(_projectBadge, 0); Grid.SetColumnSpan(_projectBadge, 1);
            Grid.SetRow(_toolbar, 0); Grid.SetColumn(_toolbar, 1); Grid.SetColumnSpan(_toolbar, 1);
        }
        else
        {
            _toolbar.Columns = 4; _toolbar.Margin = new Thickness(0, 7, 0, 0); _toolbar.Width = Math.Min(220, Math.Max(180, width - 48));
            Grid.SetRow(_projectBadge, 0); Grid.SetColumn(_projectBadge, 0); Grid.SetColumnSpan(_projectBadge, 2);
            Grid.SetRow(_toolbar, 1); Grid.SetColumn(_toolbar, 0); Grid.SetColumnSpan(_toolbar, 2);
        }

        _selectorGrid.Children.Clear(); _selectorGrid.RowDefinitions.Clear(); _selectorGrid.ColumnDefinitions.Clear();
        if (width >= 620)
        {
            _selectorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _selectorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _selectorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _selectorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _providerField.Width = 142; _modelField.Width = 160; _historyField.Width = 190;
            _providerField.Margin = new Thickness(0, 0, 4, 0); _modelField.Margin = new Thickness(4, 0, 4, 0); _historyField.Margin = new Thickness(4, 0, 0, 0);
            Grid.SetRow(_providerField, 0); Grid.SetColumn(_providerField, 0); _selectorGrid.Children.Add(_providerField);
            Grid.SetRow(_modelField, 0); Grid.SetColumn(_modelField, 1); _selectorGrid.Children.Add(_modelField);
            Grid.SetRow(_historyField, 0); Grid.SetColumn(_historyField, 2); _selectorGrid.Children.Add(_historyField);
        }
        else if (width >= 390)
        {
            _selectorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _selectorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _selectorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _selectorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _providerField.Width = 145; _modelField.Width = 145; _historyField.Width = Math.Min(300, Math.Max(260, width - 48));
            _providerField.Margin = new Thickness(0, 0, 4, 0); _modelField.Margin = new Thickness(4, 0, 0, 0); _historyField.Margin = new Thickness(0, 8, 0, 0);
            Grid.SetRow(_providerField, 0); Grid.SetColumn(_providerField, 0); _selectorGrid.Children.Add(_providerField);
            Grid.SetRow(_modelField, 0); Grid.SetColumn(_modelField, 1); _selectorGrid.Children.Add(_modelField);
            Grid.SetRow(_historyField, 1); Grid.SetColumn(_historyField, 0); Grid.SetColumnSpan(_historyField, 2); _selectorGrid.Children.Add(_historyField);
        }
        else
        {
            _selectorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _selectorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _selectorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _selectorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _providerField.Width = Math.Min(220, Math.Max(190, width - 48)); _modelField.Width = _providerField.Width; _historyField.Width = _providerField.Width;
            _providerField.Margin = new Thickness(0); _modelField.Margin = new Thickness(0, 8, 0, 0); _historyField.Margin = new Thickness(0, 8, 0, 0);
            Grid.SetRow(_providerField, 0); Grid.SetColumn(_providerField, 0); _selectorGrid.Children.Add(_providerField);
            Grid.SetRow(_modelField, 1); Grid.SetColumn(_modelField, 0); _selectorGrid.Children.Add(_modelField);
            Grid.SetRow(_historyField, 2); Grid.SetColumn(_historyField, 0); _selectorGrid.Children.Add(_historyField);
        }
    }

    private void UpdateWelcomeVisibility() => _welcomePanel.Visibility = _messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private static Border Card() => new() { Background = Brushes.White, BorderBrush = UiBrush(218, 226, 234), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11), Padding = new Thickness(12) };
    private static Border StatusItem(string title, TextBlock value, Ellipse dot)
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(dot);
        var text = new StackPanel { Margin = new Thickness(7, 0, 0, 0) };
        text.Children.Add(new TextBlock { Text = title, FontSize = 8.5, FontWeight = FontWeights.SemiBold, Foreground = UiBrush(124, 138, 151) }); text.Children.Add(value);
        Grid.SetColumn(text, 1); grid.Children.Add(text);
        return new Border { Child = grid, Background = UiBrush(247, 249, 251), CornerRadius = new CornerRadius(7), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 5, 0) };
    }
    private static Border UsageItem(string title, TextBlock value)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = title, FontSize = 8, FontWeight = FontWeights.SemiBold, Foreground = UiBrush(124, 138, 151) });
        panel.Children.Add(value);
        return new Border { Child = panel, Padding = new Thickness(4, 2, 4, 2) };
    }
    private static FrameworkElement SelectorField(string label, Control control)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = label, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = UiBrush(72, 88, 104), Margin = new Thickness(1, 0, 0, 4) });
        var frame = new Border { Background = UiBrush(247, 249, 251), BorderBrush = UiBrush(196, 207, 218), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7) };
        control.HorizontalAlignment = HorizontalAlignment.Stretch; control.Background = Brushes.Transparent; control.BorderThickness = new Thickness(0);
        void RefreshClip() => control.Clip = new RectangleGeometry(new Rect(0, 0, Math.Max(0, control.ActualWidth), Math.Max(0, control.ActualHeight)), 7, 7);
        control.Loaded += (_, _) => RefreshClip(); control.SizeChanged += (_, _) => RefreshClip();
        frame.Child = control; panel.Children.Add(frame); return panel;
    }
    private static Style CreateMessageContainerStyle()
    {
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0))); style.Setters.Add(new Setter(Control.MarginProperty, new Thickness(0))); style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0))); style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent)); style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch)); style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true }; selected.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent)); selected.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent)); style.Triggers.Add(selected);
        return style;
    }
    private static WpfComboBox ComboBoxControl(string displayMemberPath) => new() { MinWidth = 0, Height = 31, Padding = new Thickness(7, 2, 7, 2), IsEnabled = false, DisplayMemberPath = displayMemberPath, BorderBrush = UiBrush(196, 207, 218), Background = Brushes.White, VerticalContentAlignment = VerticalAlignment.Center };
    private static TextBlock BadgeValue() => new() { Text = "Chưa kiểm tra", TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = UiBrush(47, 62, 77), FontSize = 10.5 };
    private static TextBlock UsageValue() => new() { Text = "Không khả dụng", TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = UiBrush(47, 62, 77), FontSize = 10 };
    private static Ellipse StatusDot() => new() { Width = 7, Height = 7, Fill = UiBrush(164, 175, 186), VerticalAlignment = VerticalAlignment.Center };
    private static Button PrimaryButton(string text) => ButtonControl(text, UiBrush(24, 94, 160), Brushes.White, UiBrush(24, 94, 160), 72);
    private static Button SecondaryButton(string text) => ButtonControl(text, Brushes.White, UiBrush(48, 65, 82), UiBrush(194, 205, 216), 0);
    private static Button HeaderActionButton(string text)
    {
        var button = ButtonControl(text, Brushes.Transparent, UiBrush(53, 76, 99), UiBrush(202, 213, 223), 0);
        button.MinHeight = 25; button.Padding = new Thickness(7, 3, 7, 3); button.Margin = new Thickness(2, 0, 0, 0); button.FontSize = 10.5;
        return button;
    }
    private static Button SuggestionButton(string text)
    {
        var button = ButtonControl(text, UiBrush(247, 250, 253), UiBrush(31, 82, 132), UiBrush(190, 211, 231), 0); button.Margin = new Thickness(2); button.Padding = new Thickness(7, 4, 7, 4); button.FontSize = 9.5; button.FontStyle = FontStyles.Italic; return button;
    }
    private static Button ButtonControl(string text, Brush background, Brush foreground, Brush border, double minWidth)
    {
        var button = new Button { Content = text, MinWidth = minWidth, MinHeight = 31, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(3, 0, 3, 0), Background = background, Foreground = foreground, BorderBrush = border, BorderThickness = new Thickness(1), Cursor = Cursors.Hand, FocusVisualStyle = null };
        var frame = new FrameworkElementFactory(typeof(Border)); frame.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) }); frame.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) }); frame.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) }); frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(7)); frame.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center); content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center); frame.AppendChild(content); button.Template = new ControlTemplate(typeof(Button)) { VisualTree = frame };
        return button;
    }
    private static SolidColorBrush UiBrush(byte red, byte green, byte blue) => new(Color.FromRgb(red, green, blue));

    private static string CompactStatus(string full, string kind)
    {
        if (string.IsNullOrWhiteSpace(full)) return "Chưa kiểm tra";
        var lower = full.ToLowerInvariant();
        if (lower.Contains("công cụ")) return "Đã xác minh";
        if (lower.Contains("đang kết nối") || lower.Contains("đang kiểm tra")) return "Đang kết nối";
        if (lower.Contains("sẵn sàng")) return "Sẵn sàng";
        if (lower.Contains("chưa đăng nhập")) return "Chưa đăng nhập";
        if (lower.Contains("đã kết nối")) return "Đã kết nối";
        if (lower.Contains("khóa")) return "Đã khóa";
        return full.Length > 18 ? full.Substring(0, 17) + "…" : full;
    }

    private static string FriendlyToolName(string tool)
    {
        switch (tool)
        {
            case "document_info": return "Đọc thông tin Project";
            case "get_active_view": return "Đọc view đang mở";
            case "get_selection": return "Đọc đối tượng đang chọn";
            case "get_capabilities": return "Kiểm tra kết nối MCP";
            case "bim_context_snapshot": return "Chụp ngữ cảnh Revit";
            case "bim_model_catalog": return "Đọc dữ liệu model";
            case "bim_changeset_preview": return "Chạy Preview an toàn";
            case "bim_changeset_apply": return "Thực hiện thay đổi";
            case "mep_create_route": return "Dựng tuyến MEP";
            case "documentation_plan": return "Lập kế hoạch bản vẽ";
            case "documentation_apply": return "Tạo bản vẽ và Sheet";
            case "family_authoring": return "Tạo Family";
            default:
                var words = tool.Replace('_', ' ').Trim();
                return string.IsNullOrWhiteSpace(words) ? "Công cụ DSCons" : CultureInfo.CurrentCulture.TextInfo.ToTitleCase(words);
        }
    }

    private static void SetCompactStatus(TextBlock value, Ellipse dot, string full, string compact)
    {
        value.Text = compact; value.ToolTip = full;
        var lower = full.ToLowerInvariant();
        dot.Fill = lower.Contains("đang") ? UiBrush(222, 155, 32) : lower.Contains("chưa") || lower.Contains("lỗi") || lower.Contains("khóa") || lower.Contains("dừng") ? UiBrush(193, 68, 68) : UiBrush(53, 170, 112);
    }

    private void DisposeProviderOnly() { _client?.Dispose(); _client = null; }
    private void DisposeClient() { DisposeProviderOnly(); _approval?.Dispose(); _approval = null; }
    public void Dispose() { if (_disposed) return; _disposed = true; _refreshTimer.Stop(); _pendingApproval?.Decision.TrySetResult(false); DisposeClient(); }
}
#endif
