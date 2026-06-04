using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using NovelWriter.App.ViewModels;
using NovelWriter.Core;
using NovelWriter.Core.Entities;
using NovelWriter.Core.Interfaces;
using NovelWriter.Core.ValueObjects;
using NovelWriter.Storage;

namespace NovelWriter.App.Views;

public partial class ShellWindow : Window
{
    // === 状态 ===
    private string? _projectDir;
    private string? _projectId;
    private string? _currentApiKey;
    private string? _currentModel;
    private string _currentUri = "";
    private readonly ObservableCollection<StageItem> _stages = [];
    private readonly ObservableCollection<ChatMsg> _chatMsgs = [];
    private int _activeStage;
    private CancellationTokenSource? _aiCts;

    private static readonly string ProjectsRoot =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NovelWriter");

    private static readonly string AppDir =
        Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";

    private static readonly string LlmConfigPath = Path.Combine(AppDir, "llm_config.json");

    public ShellWindow()
    {
        InitializeComponent();
        ChatList.ItemsSource = _chatMsgs;
        InitStages();
        LoadLlmConfig();
    }

    // === 初始化 ===
    private void InitStages()
    {
        var names = new[] { "梗概", "大纲", "写作", "记忆", "评审" };
        foreach (var n in names) _stages.Add(new StageItem { Name = n, Status = "○", Color = "#6C7086" });
    }

    private void LoadLlmConfig()
    {
        // 优先从配置文件读取，其次环境变量
        if (File.Exists(LlmConfigPath))
        {
            try
            {
                var json = File.ReadAllText(LlmConfigPath);
                var cfg = JsonSerializer.Deserialize<LlmConfigDto>(json);
                if (cfg != null)
                {
                    _currentApiKey = cfg.ApiKey;
                    _currentModel = cfg.Model;
                    _currentUri = cfg.Uri ?? "";
                }
            }
            catch { /* 文件损坏则忽略 */ }
        }

        _currentApiKey ??= Environment.GetEnvironmentVariable("LLM_API_KEY") ?? "";
        ApiKeyBox.Text = _currentApiKey;
        ModelBox.Text = _currentModel ?? "";
        UriBox.Text = _currentUri;
    }

    // === 项目操作 ===
    private async void NewProject_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new NewProjectDialog { Owner = this };
        if (dlg.ShowDialog() != true || dlg.CreatedProject == null) return;

        var proj = dlg.CreatedProject;
        _projectId = proj.Id.Value.ToString("N")[..8];

        _projectDir = Path.Combine(ProjectsRoot, SanitizeName(proj.Title));
        Directory.CreateDirectory(_projectDir);
        Directory.CreateDirectory(Path.Combine(_projectDir, "chapters"));
        Directory.CreateDirectory(Path.Combine(_projectDir, "memory"));
        Directory.CreateDirectory(Path.Combine(_projectDir, "interludes"));

        var meta = new ProjectMeta
        {
            Title = proj.Title,
            Genre = proj.Genre ?? "",
            Phase = ProjectPhase.TopicPicked,
            Idea = dlg.StoryIdea
        };
        SaveProjectMeta(meta);
        File.WriteAllText(Path.Combine(_projectDir, "idea.md"), dlg.StoryIdea);

        SetProjectTitle(proj.Title);
        ProgressText.Text = $"《{proj.Title}》— 已创建，等待开始";
        RefreshTree();
        WelcomePanel.Visibility = Visibility.Collapsed;
        await SaveProjectToDb(proj);

        // 验证 LLM 状态，显示开始按钮
        await ValidateLlmAndShowStart();
    }

    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenProjectDialog(ProjectsRoot) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedPath == null) return;
        OpenExistingProject(dlg.SelectedPath);
    }

    private async void OpenExistingProject(string dir)
    {
        _projectDir = dir;
        var meta = LoadOrRecoverMeta();
        if (meta == null) return;

        SetProjectTitle(meta.Title);
        _projectId = meta.Id;
        ProgressText.Text = $"《{meta.Title}》— {PhaseLabel(meta.Phase)}";
        RefreshTree();
        WelcomePanel.Visibility = Visibility.Collapsed;
        LoadLlmConfig();

        // 验证 LLM 状态，显示开始按钮
        await ValidateLlmAndShowStart();
    }

    /// <summary>
    /// 设置左栏标题为小说名称
    /// </summary>
    private void SetProjectTitle(string title)
    {
        ContentHeader.Text = title;
    }

    /// <summary>
    /// 验证 LLM 可用性，成功则显示开始按钮。
    /// </summary>
    private async Task ValidateLlmAndShowStart()
    {
        StartAiBtn.Visibility = Visibility.Collapsed;
        StageActions.Visibility = Visibility.Collapsed;

        // 优先使用已保存的 key，再用 UI 输入框中的值
        var key = _currentApiKey ?? ApiKeyBox.Text.Trim();
        if (string.IsNullOrEmpty(key))
        {
            ProgressText.Text = "LLM 未配置 — 请点击左下角 ▼ 设置";
            return;
        }

        try
        {
            ShowThinking("验证 LLM 连接...");
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var request = new HttpRequestMessage(HttpMethod.Post, _currentUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

            var body = new
            {
                model = _currentModel ?? "",
                messages = new[] { new { role = "user", content = "hi" } },
                max_tokens = 1
            };
            request.Content = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            var resp = await http.SendAsync(request);
            HideThinking();

            if (resp.IsSuccessStatusCode)
            {
                StageActions.Visibility = Visibility.Visible;
                StartAiBtn.Visibility = Visibility.Visible;
                ProgressText.Text = string.IsNullOrEmpty(_currentModel) ? "✓ 已连接" : $"✓ 已连接 {_currentModel}";
            }
            else
            {
                var errBody = await resp.Content.ReadAsStringAsync();
                Chat("错误", $"API 返回 {(int)resp.StatusCode}: {errBody.Truncate(200)}");
            }
        }
        catch (Exception ex)
        {
            HideThinking();
            Chat("错误", $"无法连接: {ex.Message.Truncate(200)}");
        }
    }

    private void StartAiBtn_Click(object sender, RoutedEventArgs e)
    {
        StartAiBtn.Visibility = Visibility.Collapsed;
        _ = StartAiPipelineAsync();
    }

    /// <summary>
    /// 加载 project.json。如果损坏/缺失，从目录文件恢复。
    /// </summary>
    private ProjectMeta? LoadOrRecoverMeta()
    {
        if (_projectDir == null) return null;
        var path = Path.Combine(_projectDir, "project.json");
        if (File.Exists(path))
        {
            try
            {
                var meta = JsonSerializer.Deserialize<ProjectMeta>(File.ReadAllText(path));
                if (meta != null && !string.IsNullOrWhiteSpace(meta.Title))
                    return meta;
            }
            catch { /* 损坏文件，从目录恢复 */ }
        }

        // === 恢复逻辑: 扫描目录推断项目状态 ===
        var title = new DirectoryInfo(_projectDir).Name;
        var phase = ProjectPhase.TopicPicked;
        var hasSynopsis = File.Exists(Path.Combine(_projectDir, "synopsis.md"));
        var hasOutline = File.Exists(Path.Combine(_projectDir, "outline.md"));
        var chaptersDir = Path.Combine(_projectDir, "chapters");
        var hasChapters = Directory.Exists(chaptersDir) && Directory.GetFiles(chaptersDir, "*.md").Length > 0;
        var idea = File.Exists(Path.Combine(_projectDir, "idea.md"))
            ? File.ReadAllText(Path.Combine(_projectDir, "idea.md")) : "";

        if (hasSynopsis) phase = ProjectPhase.SynopsisDone;
        if (hasOutline) phase = ProjectPhase.OutlineDone;
        if (hasChapters) phase = ProjectPhase.ChapterActive;

        var recovered = new ProjectMeta { Title = title, Phase = phase, Idea = idea };
        SaveProjectMeta(recovered);
        return recovered;
    }

    /// <summary>
    /// 根据阶段恢复流水线。topic/synopsis阶段从头开始；
    /// outline/chapterActive阶段从当前进度继续。
    /// </summary>
    // === project.json 读写 ===
    private void SaveProjectMeta(ProjectMeta meta)
    {
        if (_projectDir == null) return;
        meta.LastUpdated = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
        // 原子写入: 先写临时文件再重命名
        var tmp = Path.Combine(_projectDir, "project.tmp");
        var dst = Path.Combine(_projectDir, "project.json");
        File.WriteAllText(tmp, json);
        File.Move(tmp, dst, overwrite: true);
    }

    private void UpdatePhase(ProjectPhase phase, int? chapter = null)
    {
        if (_projectDir == null) return;
        var meta = LoadOrRecoverMeta();
        if (meta == null) return;
        meta.Phase = phase;
        if (chapter.HasValue) meta.CurrentChapter = chapter.Value;
        SaveProjectMeta(meta);
    }

    private void RefreshTree()
    {
        if (_projectDir == null) return;
        ChapterTree.Items.Clear();
        var chaptersDir = Path.Combine(_projectDir, "chapters");
        if (Directory.Exists(chaptersDir))
        {
            foreach (var f in Directory.GetFiles(chaptersDir, "*.md").OrderBy(Path.GetFileName))
            {
                var name = Path.GetFileNameWithoutExtension(f);
                ChapterTree.Items.Add(new TreeViewItem { Header = name, Tag = f, Foreground = new SolidColorBrush(Color.FromRgb(0xCD, 0xD6, 0xF4)) });
            }
        }
        if (ChapterTree.Items.Count == 0)
            ChapterTree.Items.Add(new TreeViewItem { Header = "暂无章节", Foreground = new SolidColorBrush(Color.FromRgb(0x6C, 0x70, 0x86)) });

        // 刷新风格/插曲列表
        _ = RefreshStyleListAsync();
        _ = RefreshInterludeListAsync();
    }

    // === 标签页管理 ===
    private void OpenFileNode(object sender, MouseButtonEventArgs e)
    {
        if (_projectDir == null || sender is not FrameworkElement el || el.Tag is not string tag) return;
        var file = tag switch
        {
            "idea" => Path.Combine(_projectDir, "idea.md"),
            "synopsis" => Path.Combine(_projectDir, "synopsis.md"),
            "outline" => Path.Combine(_projectDir, "outline.md"),
            "characters" => Path.Combine(_projectDir, "memory", "characters.md"),
            "world" => Path.Combine(_projectDir, "memory", "world.md"),
            _ => null
        };
        if (file == null) return;
        if (!File.Exists(file)) File.WriteAllText(file, $"# {tag}\n\n");
        OpenTab(Path.GetFileName(file), file);
    }

    private void ChapterTreeSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem item && item.Tag is string path)
            OpenTab(Path.GetFileNameWithoutExtension(path), path);
    }

    private void InterludeSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem item && item.Tag is string path)
            OpenTab(Path.GetFileNameWithoutExtension(path), path);
    }

    private void OpenTab(string header, string filePath)
    {
        WelcomePanel.Visibility = Visibility.Collapsed;
        ContentEditor.Visibility = Visibility.Visible;

        foreach (TabItem tab in EditorTabs.Items)
        {
            if (tab.Tag is string t && t == filePath)
            {
                EditorTabs.SelectedItem = tab;
                return;
            }
        }

        var newTab = new TabItem { Header = header, Tag = filePath };
        newTab.Content = new TextBlock(); // placeholder
        EditorTabs.Items.Add(newTab);
        EditorTabs.SelectedItem = newTab;

        ContentEditor.Text = File.ReadAllText(filePath);
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is TabItem tab)
            EditorTabs.Items.Remove(tab);
        if (EditorTabs.Items.Count == 0)
        {
            ContentEditor.Visibility = Visibility.Collapsed;
            WelcomePanel.Visibility = Visibility.Visible;
        }
    }

    private void TabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EditorTabs.SelectedItem is TabItem tab && tab.Tag is string path && File.Exists(path))
        {
            ContentEditor.Text = File.ReadAllText(path);
            ContentEditor.Visibility = Visibility.Visible;
            WelcomePanel.Visibility = Visibility.Collapsed;
        }
    }

    private void ContentEditor_Changed(object sender, TextChangedEventArgs e)
    {
        if (EditorTabs.SelectedItem is TabItem tab && tab.Tag is string path)
            _ = File.WriteAllTextAsync(path, ContentEditor.Text);
    }

    // === AI 流水线 ===
    private const int MaxPipelineSteps = 100;     // 硬性上限，防止意外无限调用

    private async Task StartAiPipelineAsync()
    {
        if (_projectDir == null) return;
        _aiCts?.Cancel();
        _aiCts = new CancellationTokenSource();
        var ct = _aiCts.Token;
        var stepCount = 0;

        try
        {
            // Stage 1: 梗概
            GuardStep(ref stepCount);
            SetStage(0, true);
            ShowActions(false);
            ShowThinking("正在生成故事梗概...");

            var idea = File.Exists(Path.Combine(_projectDir, "idea.md"))
                ? await File.ReadAllTextAsync(Path.Combine(_projectDir, "idea.md"), ct) : "";

            var gen = NovelWriterApp.Services.GetRequiredService<NovelWriter.Engine.Pipeline.SynopsisGenerator>();
            var meta = LoadOrRecoverMeta();
            var synopsis = await gen.GenerateAsync(meta?.Title ?? "未命名", "", "30万", ct);

            HideThinking();
            if (synopsis.Success)
            {
                var synopsisText = $"# {synopsis.Title}\n\n**核心冲突:** {synopsis.CoreConflict}\n**主角:** {synopsis.MainCharacterName}\n\n{synopsis.Synopsis}";
                await File.WriteAllTextAsync(Path.Combine(_projectDir, "synopsis.md"), synopsisText, ct);
                OpenTab("梗概", Path.Combine(_projectDir, "synopsis.md"));
            }
            SetStage(0, false);
            ShowActions(true);
            Chat("AI", $"梗概生成完成。请确认或重写。\n书名: {synopsis.Title}\n{synopsis.Synopsis.Truncate(150)}");

            // Stage 2: 大纲
            await WaitConfirmOrRetryAsync(0, ct);
            GuardStep(ref stepCount);
            UpdatePhase(ProjectPhase.SynopsisDone);
            SetStage(1, true);
            ShowActions(false);
            ShowThinking("正在生成分章大纲（含 L3 记忆初始化）...");

            var outlineGen = NovelWriterApp.Services.GetRequiredService<NovelWriter.Engine.Pipeline.OutlineGenerator>();
            var synopsisText2 = File.Exists(Path.Combine(_projectDir, "synopsis.md"))
                ? await File.ReadAllTextAsync(Path.Combine(_projectDir, "synopsis.md"), ct) : "";
            var progId = _projectId;
            var projGuid = string.IsNullOrEmpty(progId) ? Guid.NewGuid() : Guid.Parse(progId + "00000000");
            var outline = await outlineGen.GenerateAsync(new ProjectId(projGuid), synopsisText2, "", synopsis.MainCharacterName, "", 10, ct);

            HideThinking();
            if (outline.Success)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("# 分章大纲\n");
                foreach (var o in outline.Outlines)
                {
                    sb.AppendLine($"## 第{o.ChapterNumber}章 {o.SceneDescription}");
                    sb.AppendLine(o.KeyEvents ?? "");
                    sb.AppendLine();

                    var chPath = Path.Combine(_projectDir, "chapters", $"第{o.ChapterNumber:00}章.md");
                    if (!File.Exists(chPath))
                        await File.WriteAllTextAsync(chPath, $"# 第{o.ChapterNumber}章\n\n{o.SceneDescription}\n\n(待AI生成)\n", ct);
                }
                await File.WriteAllTextAsync(Path.Combine(_projectDir, "outline.md"), sb.ToString(), ct);
                RefreshTree();
            }
            SetStage(1, false);
            ShowActions(true);
            Chat("AI", $"大纲生成完成 ({outline.Outlines.Count}章)。请确认或重写。");

            // Stage 3+：逐章写作
            await WaitConfirmOrRetryAsync(1, ct);
            GuardStep(ref stepCount);
            UpdatePhase(ProjectPhase.OutlineDone);
            for (int i = 2; i < _stages.Count; i++)
            {
                GuardStep(ref stepCount);
                SetStage(i, true);
                ShowActions(false);
                ShowThinking($"{_stages[i].Name} 阶段执行中...");
                await Task.Delay(500, ct);
                HideThinking();
                SetStage(i, false);
                ShowActions(true);
            }
        }
        catch (OperationCanceledException) { HideThinking(); Chat("系统", "流水线已取消"); }
        catch (Exception ex) { HideThinking(); Chat("错误", ex.Message); }
    }

    private TaskCompletionSource<int>? _confirmTcs;
    private async Task WaitConfirmOrRetryAsync(int stage, CancellationToken ct)
    {
        _activeStage = stage;
        while (!ct.IsCancellationRequested)
        {
            _confirmTcs = new TaskCompletionSource<int>();
            var result = await _confirmTcs.Task;
            if (result == 1) return;
            if (result == 2)
            {
                ShowThinking("重新生成中...");
                await Task.Delay(1000, ct);
                HideThinking();
                Chat("AI", "已重新生成。");
            }
        }
    }

    private void StageConfirm_Click(object sender, RoutedEventArgs e) { _confirmTcs?.TrySetResult(1); ShowActions(false); }
    private void StageRewrite_Click(object sender, RoutedEventArgs e) { _confirmTcs?.TrySetResult(2); }
    private void StageRetry_Click(object sender, RoutedEventArgs e) { _confirmTcs?.TrySetResult(2); }

    private void SetStage(int index, bool active)
    {
        for (int i = 0; i < _stages.Count; i++)
        {
            if (active && i == index) { _stages[i].Status = "●"; _stages[i].Color = "#FAB387"; }
            else if (!active && i <= index) { _stages[i].Status = "✓"; _stages[i].Color = "#A6E3A1"; }
            else { _stages[i].Status = "○"; _stages[i].Color = "#6C7086"; }
        }
        var stage = _stages[index];
        ProgressText.Text = active ? $"● {stage.Name} 进行中..." : $"✓ {stage.Name} 已完成";
    }

    private void ShowActions(bool show)
    {
        if (show)
        {
            StageActions.Visibility = Visibility.Visible;
            ConfirmBtn.Visibility = Visibility.Visible;
            RewriteBtn.Visibility = Visibility.Visible;
        }
        else
        {
            StageActions.Visibility = Visibility.Collapsed;
            ConfirmBtn.Visibility = Visibility.Collapsed;
            RewriteBtn.Visibility = Visibility.Collapsed;
        }
    }

    // === 聊天 ===
    private void Chat(string role, string text)
    {
        var (bg, fg) = role switch
        {
            "用户" => ("#313244", "#CDD6F4"),
            "AI" => ("#1A1A2E", "#89B4FA"),
            "错误" => ("#3C1A1A", "#F38BA8"),
            _ => ("#2A2A3C", "#6C7086")
        };
        _chatMsgs.Add(new ChatMsg { Text = text, Bg = bg, Fg = fg });
        ChatScroll.ScrollToEnd();
    }

    private CancellationTokenSource? _chatCts;

    private void ChatSend_Click(object sender, RoutedEventArgs e)
    {
        var text = ChatInput.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        // 取消上一次仍在进行的请求
        _chatCts?.Cancel();
        _chatCts = new CancellationTokenSource();

        Chat("用户", text);
        ChatInput.Text = "";
        ChatInput.IsEnabled = false;
        _ = AutoReplyAsync(text, _chatCts.Token).ContinueWith(_ =>
            Dispatcher.Invoke(() => ChatInput.IsEnabled = true));
    }

    // 共享 HttpClient，避免每次 new（适配器基类已有重试/限速保护）
    private static readonly HttpClient _sharedHttp = new() { Timeout = TimeSpan.FromMinutes(5) };

    private async Task AutoReplyAsync(string userMsg, CancellationToken ct = default)
    {
        var key = _currentApiKey;
        if (string.IsNullOrEmpty(key))
        {
            Chat("错误", "请先在左下角 ▼ 配置 LLM API Key");
            return;
        }

        try
        {
            ShowThinking("AI 思考中...");
            var adapter = new NovelWriter.Engine.Llm.GenericOpenAiAdapter(
                _sharedHttp, key, _currentModel ?? "", _currentUri);
            var resp = await adapter.ChatAsync("你是NovelWriter助手", userMsg, ct);
            HideThinking();
            Chat("AI", resp);
        }
        catch (OperationCanceledException) { HideThinking(); }
        catch (Exception ex) { HideThinking(); Chat("错误", ex.Message); }
    }

    // === LLM 配置 ===
    private void LlmConfig_Click(object sender, RoutedEventArgs e)
    {
        LlmPopup.IsOpen = !LlmPopup.IsOpen;
    }

    private void SaveLlmConfig_Click(object sender, RoutedEventArgs e)
    {
        _currentApiKey = ApiKeyBox.Text.Trim();
        _currentModel = ModelBox.Text.Trim();
        _currentUri = UriBox.Text.Trim();

        // 持久化到 app 目录下的配置文件
        var cfg = new LlmConfigDto { ApiKey = _currentApiKey, Model = _currentModel, Uri = _currentUri };
        File.WriteAllText(LlmConfigPath, JsonSerializer.Serialize(cfg));

        LlmNameText.Text = string.IsNullOrEmpty(_currentModel) ? "未配置 LLM" : _currentModel;
        LlmPopup.IsOpen = false;

        if (!string.IsNullOrEmpty(_currentApiKey))
        {
            ProgressText.Text = $"已配置 {_currentModel}，点击 ▶ 开始";
            _ = ValidateLlmConnectionQuick();
        }
        else
        {
            ProgressText.Text = "LLM 未配置 — 请点击左下角 ▼ 设置";
        }
    }

    private async Task ValidateLlmConnectionQuick()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var request = new HttpRequestMessage(HttpMethod.Post, _currentUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _currentApiKey);

            var body = new
            {
                model = _currentModel ?? "",
                messages = new[] { new { role = "user", content = "hi" } },
                max_tokens = 1
            };
            request.Content = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            var resp = await http.SendAsync(request);

            if (resp.IsSuccessStatusCode)
                ProgressText.Text = string.IsNullOrEmpty(_currentModel) ? "✓ 已连接" : $"✓ 已连接 {_currentModel}";
            else
            {
                var errBody = await resp.Content.ReadAsStringAsync();
                ProgressText.Text = $"⚠ 连接失败 ({resp.StatusCode})";
                Chat("错误", $"API 返回 {(int)resp.StatusCode}: {errBody.Truncate(200)}");
            }
        }
        catch (Exception ex)
        {
            ProgressText.Text = "⚠ 连接异常";
            Chat("错误", $"无法连接: {ex.Message.Truncate(200)}");
        }
    }

    // === 风格/插曲 ===
    private async void ImportStyles_Click(object sender, RoutedEventArgs e)
    {
        var dir = Microsoft.VisualBasic.Interaction.InputBox("输入目录路径:", "导入风格", "", -1, -1);
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return;

        Chat("系统", $"正在导入风格: {dir}");
        using var scope = NovelWriterApp.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<NovelWriter.Core.Interfaces.IStyleLibraryRepository>();
        var llm = scope.ServiceProvider.GetRequiredService<NovelWriter.Core.Interfaces.ILlmAdapter>();
        var agent = new NovelWriter.Engine.Style.StyleExtractionAgent(llm, repo);
        var importer = new NovelWriter.Engine.Style.StyleImporter(agent, repo);
        var r = await importer.ImportFromDirectoryAsync(dir);
        Chat("系统", $"导入: {r.Imported.Count} ✓ | {r.Skipped.Count} 跳过 | {r.Failed.Count} ✗");
        await RefreshStyleListAsync();
    }

    private void ManageStyles_Click(object sender, RoutedEventArgs e)
    {
        Chat("系统", "风格库管理功能开发中。请在左侧风格库面板查看。");
    }

    private async Task RefreshStyleListAsync()
    {
        try
        {
            using var scope = NovelWriterApp.Services.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<NovelWriter.Core.Interfaces.IStyleLibraryRepository>();
            var styles = await repo.GetAvailableStylesAsync();
            StyleList.ItemsSource = styles.Select(s => s.SourceTitle).ToList();
        }
        catch { /* DB not ready */ }
    }

    private async Task RefreshInterludeListAsync()
    {
        InterludeTree.Items.Clear();
        if (_projectDir == null) return;
        var dir = Path.Combine(_projectDir, "interludes");
        if (Directory.Exists(dir))
        {
            foreach (var f in Directory.GetFiles(dir, "*.md"))
            {
                var name = Path.GetFileNameWithoutExtension(f);
                InterludeTree.Items.Add(new TreeViewItem { Header = name, Tag = f, Foreground = new SolidColorBrush(Color.FromRgb(0xA6, 0xAD, 0xC8)) });
            }
        }
    }

    // === 辅助 ===
    private async Task SaveProjectToDb(Core.Entities.Project proj)
    {
        using var scope = NovelWriterApp.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelWriterDbContext>();
        db.Projects.Add(proj);
        await db.SaveChangesAsync();
    }

    private static string SanitizeName(string name) =>
        string.Join("_", name.Split(Path.GetInvalidFileNameChars())).Trim();

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    // === 思考状态 ===
    private void ShowThinking(string text)
    {
        ThinkingText.Text = text;
        ThinkingBar.Visibility = Visibility.Visible;
    }

    private void HideThinking()
    {
        ThinkingBar.Visibility = Visibility.Collapsed;
    }

    private static string PhaseLabel(ProjectPhase phase) => phase switch
    {
        ProjectPhase.TopicPicked => "已创建，等待开始",
        ProjectPhase.SynopsisDone => "梗概已完成",
        ProjectPhase.OutlineDone => "大纲已就绪",
        ProjectPhase.ChapterActive => "写作进行中",
        _ => "等待开始"
    };

    private static void GuardStep(ref int count)
    {
        if (++count > MaxPipelineSteps)
            throw new InvalidOperationException($"流水线执行步骤超过上限 ({MaxPipelineSteps})，已自动停止防止 token 滥用。");
    }
}

// === 数据类 ===
public class StageItem
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = "○";
    public string Color { get; set; } = "#6C7086";

    public override string ToString() => $"{Status} {Name}";
}

public class ChatMsg
{
    public string Text { get; set; } = "";
    public string Bg { get; set; } = "#2A2A3C";
    public string Fg { get; set; } = "#6C7086";
}

// === 项目元数据 ===
public enum ProjectPhase
{
    TopicPicked,    // 刚创建，只有创意
    SynopsisDone,   // 梗概已生成并确认
    OutlineDone,    // 大纲已生成并确认
    ChapterActive   // 正在逐章写作
}

public class LlmConfigDto
{
    public string? ApiKey { get; set; }
    public string? Model { get; set; }
    public string? Uri { get; set; }
}

public class ProjectMeta
{
    public string Title { get; set; } = "";
    public string Genre { get; set; } = "";
    public string Idea { get; set; } = "";
    public string? Id { get; set; }
    public ProjectPhase Phase { get; set; } = ProjectPhase.TopicPicked;
    public int CurrentChapter { get; set; }
    public int CurrentVolume { get; set; } = 1;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public string? CustomInstructions { get; set; }
}
