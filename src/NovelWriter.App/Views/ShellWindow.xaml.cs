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
using NovelWriter.App.Session;
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
        _currentModel ??= Environment.GetEnvironmentVariable("LLM_MODEL") ?? "";
        _currentUri ??= Environment.GetEnvironmentVariable("LLM_BASE_URL") ?? "";

        // 同步到 UI
        ApiKeyBox.Text = _currentApiKey;
        ModelBox.Text = _currentModel ?? "";
        UriBox.Text = _currentUri;

        // 同步到全局运行时配置 + 底部状态栏
        NovelWriterApp.LlmConfig.Update(
            apiKey: _currentApiKey,
            model: _currentModel,
            endpoint: _currentUri);

        LlmNameText.Text = string.IsNullOrEmpty(_currentModel) ? "未配置 LLM" : _currentModel;
    }

    // === 项目操作 ===
    private async void NewProject_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new NewProjectDialog { Owner = this };
        if (dlg.ShowDialog() != true || dlg.CreatedProject == null) return;

        var proj = dlg.CreatedProject;
        // 存完整 32 位 Guid（N 格式无连字符），不要截断：
        // StartAiPipelineAsync 后续要 Guid.Parse(_projectId)，截断成 8 位后再补 8 个 0 是 16 位，无效。
        _projectId = proj.Id.Value.ToString("N");

        _projectDir = Path.Combine(ProjectsRoot, SanitizeName(proj.Title));
        Directory.CreateDirectory(_projectDir);
        Directory.CreateDirectory(Path.Combine(_projectDir, "chapters"));
        Directory.CreateDirectory(Path.Combine(_projectDir, "memory"));
        Directory.CreateDirectory(Path.Combine(_projectDir, "interludes"));
        // 新建项目：初始化空会话记忆（短期/长期都是空）
        _session = new SessionMemoryManager(_projectDir);
        UpdateSessionButtonHud();

        var meta = new ProjectMeta
        {
            Title = proj.Title,
            Genre = proj.Genre ?? "",
            Phase = ProjectPhase.TopicPicked,
            Idea = dlg.StoryIdea,
            // 把 Guid 写进 project.json —— OpenExistingProject 时 _projectId = meta.Id 能直接 Guid.Parse
            Id = _projectId
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
        // 实例化会话记忆管理器（自动从 session/ 目录加载短期/长期）
        _session = new SessionMemoryManager(_projectDir);
        UpdateSessionButtonHud();

        var meta = LoadOrRecoverMeta();
        if (meta == null) return;

        SetProjectTitle(meta.Title);
        // 老 project.json 可能没有 Id 字段（修复前的版本写的），此时分配一个新 Guid 并持久化
        if (string.IsNullOrEmpty(meta.Id) || !Guid.TryParse(meta.Id, out _))
        {
            meta.Id = Guid.NewGuid().ToString("N");
            SaveProjectMeta(meta);
        }
        _projectId = meta.Id;
        ProgressText.Text = $"《{meta.Title}》— {PhaseLabel(meta.Phase)}";
        RefreshTree();
        WelcomePanel.Visibility = Visibility.Collapsed;
        LoadLlmConfig();

        // 显示项目状态记忆（最近 3 条状态变迁）—— 让用户了解
        // 是否发生过 phase 回退（例如"上次的失败导致从 OutlineDone 回到 SynopsisDone"）
        ShowStateHistoryIfAny();

        // 自动验证 LLM：已配置 → 验证后显示开始按钮；未配置 → 提示设置
        // 不需要用户手动点击"保存并连接"
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

        // 优先使用全局 LlmConfig（已由 LoadLlmConfig 同步），再回退到 UI 输入框
        var key = !string.IsNullOrEmpty(NovelWriterApp.LlmConfig.ApiKey)
            ? NovelWriterApp.LlmConfig.ApiKey
            : ApiKeyBox.Text.Trim();
        var model = !string.IsNullOrEmpty(NovelWriterApp.LlmConfig.Model)
            ? NovelWriterApp.LlmConfig.Model
            : ModelBox.Text.Trim();
        var uri = !string.IsNullOrEmpty(NovelWriterApp.LlmConfig.Endpoint)
            ? NovelWriterApp.LlmConfig.Endpoint
            : UriBox.Text.Trim();

        if (string.IsNullOrEmpty(key))
        {
            ProgressText.Text = "LLM 未配置 — 请点击左下角 ▼ 设置";
            return;
        }

        try
        {
            ShowThinking("验证 LLM 连接...");
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

            var body = new
            {
                model = model ?? "",
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
                ProgressText.Text = string.IsNullOrEmpty(model) ? "✓ 已连接" : $"✓ 已连接 {model}";
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
    /// 无论 project.json 是否存在，都会用目录里文件的存在性**校正** phase
    /// （避免 project.json 写过时的 TopicPicked 但 synopsis.md 已生成）。
    /// </summary>
    private ProjectMeta? LoadOrRecoverMeta()
    {
        if (_projectDir == null) return null;
        var path = Path.Combine(_projectDir, "project.json");
        ProjectMeta? meta = null;
        if (File.Exists(path))
        {
            try
            {
                meta = JsonSerializer.Deserialize<ProjectMeta>(File.ReadAllText(path));
            }
            catch { /* 损坏文件，从目录恢复 */ }
        }

        if (meta == null || string.IsNullOrWhiteSpace(meta.Title))
        {
            meta = new ProjectMeta { Title = new DirectoryInfo(_projectDir).Name };
        }

        // 始终根据实际文件存在性校正 phase —— 文件系统是真相的唯一来源
        // 但**文件存在 ≠ 有效**：上一步生成失败可能留下空文件/占位符。
        // 用内容校验决定 phase，避免被半成品文件误导进入死路。
        var synopsisPath = Path.Combine(_projectDir, "synopsis.md");
        var outlinePath  = Path.Combine(_projectDir, "outline.md");
        var chaptersDir  = Path.Combine(_projectDir, "chapters");

        var synopsisText = File.Exists(synopsisPath) ? File.ReadAllText(synopsisPath) : "";
        var outlineText  = File.Exists(outlinePath)  ? File.ReadAllText(outlinePath)  : "";
        var chapterFiles = Directory.Exists(chaptersDir) ? Directory.GetFiles(chaptersDir, "*.md") : Array.Empty<string>();

        // 校验文件是否"实质完成"
        // - synopsis 需包含 **核心冲突:** 字段 + 至少 100 字正文
        // - outline 需包含至少 1 个 `## 第N章` 段落
        // - 任一章节文件存在且 >500 字且不含"待AI生成"占位符才算真章节
        var synopsisValid = !string.IsNullOrWhiteSpace(synopsisText)
            && synopsisText.Contains("**核心冲突", StringComparison.OrdinalIgnoreCase)
            && synopsisText.Length >= 100;
        var outlineValid = !string.IsNullOrWhiteSpace(outlineText)
            && ParseOutlineChapters(outlineText).Count > 0;
        var hasRealChapters = chapterFiles.Any(f =>
        {
            try
            {
                var t = File.ReadAllText(f);
                return t.Length > 500 && !t.Contains("(待AI生成)");
            }
            catch { return false; }
        });

        var correctedPhase = ProjectPhase.TopicPicked;
        if (synopsisValid) correctedPhase = ProjectPhase.SynopsisDone;
        if (synopsisValid && outlineValid) correctedPhase = ProjectPhase.OutlineDone;
        if (synopsisValid && outlineValid && hasRealChapters) correctedPhase = ProjectPhase.ChapterActive;

        // 双向校正：文件系统是最权威的真相源。
        // - project.json 写高 phase（ChapterActive）但 chapters 目录空 → 回退到 TopicPicked
        // - project.json 写低 phase 但文件已存在 → 推进到文件对应的 phase
        // - 文件存在但内容无效（半成品/失败残留）→ 回退到上一步
        if (correctedPhase != meta.Phase)
        {
            var oldPhase = meta.Phase;
            meta.Phase = correctedPhase;
            // 记录到 project.json 的 PhaseHistory，便于用户/排错时看到
            // "为什么从 OutlineDone 回到 SynopsisDone"
            if (oldPhase > correctedPhase)
            {
                var reason = BuildInvalidFileReason(synopsisValid, outlineValid, hasRealChapters);
                var entry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {PhaseLabel(oldPhase)} → {PhaseLabel(correctedPhase)} | {reason}";
                meta.PhaseHistory.Add(entry);
                if (meta.PhaseHistory.Count > 20)
                    meta.PhaseHistory = meta.PhaseHistory.Skip(meta.PhaseHistory.Count - 20).ToList();
                meta.LastPhaseChangeReason = reason;
            }
            SaveProjectMeta(meta);
        }

        if (string.IsNullOrEmpty(meta.Idea))
        {
            var ideaPath = Path.Combine(_projectDir, "idea.md");
            if (File.Exists(ideaPath)) meta.Idea = File.ReadAllText(ideaPath);
        }

        return meta;
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

    private void UpdatePhase(ProjectPhase phase, int? chapter = null, string? reason = null)
    {
        if (_projectDir == null) return;
        var meta = LoadOrRecoverMeta();
        if (meta == null) return;
        var oldPhase = meta.Phase;
        meta.Phase = phase;
        if (chapter.HasValue) meta.CurrentChapter = chapter.Value;
        if (oldPhase != phase)
        {
            var entry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {PhaseLabel(oldPhase)} → {PhaseLabel(phase)} | {reason ?? "UpdatePhase"}";
            meta.PhaseHistory.Add(entry);
            // 保留最近 20 条，避免 project.json 无限膨胀
            if (meta.PhaseHistory.Count > 20)
                meta.PhaseHistory = meta.PhaseHistory.Skip(meta.PhaseHistory.Count - 20).ToList();
            meta.LastPhaseChangeReason = reason;
        }
        SaveProjectMeta(meta);
    }

    /// <summary>
    /// 启动时向用户提示最近的状态历史（最近 3 条）。让用户了解
    /// "为什么我刚才点开始，pipeline 突然从 OutlineDone 跳回了 SynopsisDone"。
    /// 数据来源：<see cref="ProjectMeta.PhaseHistory"/>（写在 project.json 里）。
    /// </summary>
    private void ShowStateHistoryIfAny()
    {
        if (_projectDir == null) return;
        var path = Path.Combine(_projectDir, "project.json");
        if (!File.Exists(path)) return;
        try
        {
            var meta = JsonSerializer.Deserialize<ProjectMeta>(File.ReadAllText(path));
            if (meta?.PhaseHistory == null || meta.PhaseHistory.Count == 0) return;
            var recent = meta.PhaseHistory.Skip(Math.Max(0, meta.PhaseHistory.Count - 3)).ToArray();
            Chat("系统", "📋 项目状态履历（最近 3 条）:");
            foreach (var l in recent)
                Chat("系统", "  " + l);
        }
        catch { /* 静默 */ }
    }

    private static string BuildInvalidFileReason(bool synopsisValid, bool outlineValid, bool hasRealChapters)
    {
        if (!synopsisValid) return "synopsis.md 内容无效（缺少核心冲突字段或太短）";
        if (!outlineValid)  return "outline.md 内容无效（未找到 `## 第N章` 段落，可能上次生成失败）";
        if (!hasRealChapters) return "chapters 目录无有效章节文件";
        return "内容校验通过";
    }

    private void RefreshTree()
    {
        if (_projectDir == null) return;
        ChapterTree.Items.Clear();

        // 填充各类目的子文件列表
        IdeaFileList.ItemsSource     = BuildFileList("idea.md");
        SynopsisFileList.ItemsSource = BuildFileList("synopsis.md");
        OutlineFileList.ItemsSource  = BuildFileList("outline.md");
        L3FileList.ItemsSource       = BuildFileList(Path.Combine("memory", "l3_memory.md"));
        L2FileList.ItemsSource       = BuildFileList(Path.Combine("memory", "l2_memory.md"));
        L1FileList.ItemsSource       = BuildFileList(Path.Combine("memory", "l1_memory.md"));

        var chaptersDir = Path.Combine(_projectDir, "chapters");
        if (Directory.Exists(chaptersDir))
        {
            foreach (var f in Directory.GetFiles(chaptersDir, "*.md").OrderBy(Path.GetFileName))
            {
                var name = Path.GetFileNameWithoutExtension(f);
                ChapterTree.Items.Add(new TreeViewItem { Header = name, Tag = f, Foreground = new SolidColorBrush(Color.FromRgb(0xCD, 0xD6, 0xF4)) });
            }
        }

        // 刷新风格/插曲列表
        _ = RefreshStyleListAsync();
        _ = RefreshInterludeListAsync();
    }

    /// <summary>
    /// 构造指定文件路径的子项列表。文件不存在时不列出（避免空 Expander 看起来别扭）。
    /// </summary>
    private System.Collections.IList BuildFileList(string relativeOrSimpleName)
    {
        if (_projectDir == null) return Array.Empty<FileItem>();
        var fullPath = Path.IsPathRooted(relativeOrSimpleName)
            ? relativeOrSimpleName
            : Path.Combine(_projectDir, relativeOrSimpleName);
        if (!File.Exists(fullPath)) return Array.Empty<FileItem>();
        return new List<FileItem> { new FileItem { FullPath = fullPath, DisplayName = Path.GetFileName(fullPath) } };
    }

    private void FileItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ItemsControl ic) return;
        // 找到点击的 TextBlock（事件冒泡）
        if (e.OriginalSource is TextBlock tb && tb.DataContext is FileItem fi)
        {
            try
            {
                OpenTab(fi.DisplayName, fi.FullPath);
            }
            catch (IOException)
            {
                // 重试 5 次后仍失败 —— 通常是外部程序（如 VS Code、Notepad）独占持有文件
                Chat("错误", $"无法打开文件 {fi.DisplayName}：文件被其他程序占用，请先关闭后重试");
            }
        }
    }

    // === 标签页管理 ===

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

        var newTab = new TabItem { Tag = filePath };
        newTab.Content = new TextBlock(); // placeholder
        newTab.Header = BuildTabHeader(header, newTab);
        EditorTabs.Items.Add(newTab);
        EditorTabs.SelectedItem = newTab;

        ContentEditor.Text = ReadFileSafe(filePath);
    }

    /// <summary>
    /// 构造 Tab Header —— 文件名 + 关闭 × 按钮。
    /// 必须在按钮创建后才能把 tab 引用设给按钮 Tag，所以本方法在 tab 实例化后调用。
    /// </summary>
    private object BuildTabHeader(string header, TabItem owner)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = header,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0xCD, 0xD6, 0xF4)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        });
        var closeBtn = new Button
        {
            Content = "×",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6C, 0x70, 0x86)),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Tag = owner
        };
        closeBtn.Click += CloseTab_Click;
        panel.Children.Add(closeBtn);
        return panel;
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
            ContentEditor.Text = ReadFileSafe(path);
            ContentEditor.Visibility = Visibility.Visible;
            WelcomePanel.Visibility = Visibility.Collapsed;
        }
    }

    private CancellationTokenSource? _editorWriteCts;

    private void ContentEditor_Changed(object sender, TextChangedEventArgs e)
    {
        if (EditorTabs.SelectedItem is TabItem tab && tab.Tag is string path)
        {
            // 取消上一次未完成的写入，防止并发写堆积
            // 旧写法 _ = File.WriteAllTextAsync(...) 在快速输入/切标签时会堆积大量并发 IO，
            // 既浪费资源又会与外部读取产生锁竞争。
            _editorWriteCts?.Cancel();
            _editorWriteCts = new CancellationTokenSource();
            var token = _editorWriteCts.Token;
            _ = WriteEditorTextDebouncedAsync(path, ContentEditor.Text, token);
        }
    }

    /// <summary>
    /// 编辑器防抖写入：150ms 内无新输入才落盘，使用 FileShare.Read 避免阻塞其他读取。
    /// </summary>
    private static async Task WriteEditorTextDebouncedAsync(string path, string text, CancellationToken ct)
    {
        try
        {
            await Task.Delay(150, ct);
            var bytes = Encoding.UTF8.GetBytes(text);
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            await fs.WriteAsync(bytes, ct);
        }
        catch (OperationCanceledException) { /* 被新写入覆盖，忽略 */ }
        catch (IOException) { /* 写入失败，下次编辑时再试 */ }
    }

    // === AI 流水线 ===
    private const int MaxPipelineSteps = 100;     // 硬性上限，防止意外无限调用

    // 用户选择状态：1=确认（持久化并进入下一阶段），2=重写（重新生成当前阶段）
    private TaskCompletionSource<int>? _confirmTcs;
    private bool _isRunning;       // 流水线是否在执行（控制停止/开始按钮可见性）
    private int _pipelineSteps;    // 流水线当前已执行步数（防止无限循环）
    private bool _pipelineAborted; // 阶段失败或被早期 return 时设为 true，外层跳过 Stage 3+
    private bool _continueToChapters; // 大纲确认后用户是否选"立即开始写作"
    private bool _awaitingRewriteDirective; // 点击 ↻ 后等待用户在聊天里输入改写指令
    private string? _rewriteDirective;       // 已收集的改写指令，下一轮生成会用上
    private SessionMemoryManager? _session;  // 当前项目的会话记忆（短期 + 长期）
    private NovelWriter.Engine.Pipeline.SynopsisResult? _pendingSynopsis;
    private NovelWriter.Engine.Pipeline.OutlineResult? _pendingOutline;

    private async Task StartAiPipelineAsync()
    {
        if (_projectDir == null) return;
        if (_isRunning) return;   // 防止重入
        _isRunning = true;

        _aiCts?.Cancel();
        _aiCts = new CancellationTokenSource();
        var ct = _aiCts.Token;
        _pipelineSteps = 0;
        _pipelineAborted = false;
        _continueToChapters = false;
        _awaitingRewriteDirective = false;
        _rewriteDirective = null;

        // 切到运行态：隐藏开始按钮，显示停止按钮
        StartAiBtn.Visibility = Visibility.Collapsed;
        StopBtn.Visibility = Visibility.Visible;
        ShowActions(false);

        try
        {
            // === 准备：读取题材/故事创意/当前阶段 ===
            var meta = LoadOrRecoverMeta();
            var idea = File.Exists(Path.Combine(_projectDir, "idea.md"))
                ? await File.ReadAllTextAsync(Path.Combine(_projectDir, "idea.md"), ct) : "";
            var genre = meta?.Genre ?? "";
            // _projectId 是 32 位 Guid 字符串（N 格式），直接 Parse；
            // 历史数据：旧版本可能存了 8 位截断，遇到解析失败就用空 Guid 兜底。
            Guid projGuid;
            if (string.IsNullOrEmpty(_projectId) || !Guid.TryParse(_projectId, out projGuid))
            {
                projGuid = Guid.Empty;
            }

            var synopsisGen = NovelWriterApp.Services.GetRequiredService<NovelWriter.Engine.Pipeline.SynopsisGenerator>();
            var outlineGen = NovelWriterApp.Services.GetRequiredService<NovelWriter.Engine.Pipeline.OutlineGenerator>();

            // 根据项目当前阶段决定从哪个 Stage 启动 —— 避免对已完成的阶段重新生成
            var startStage = meta?.Phase switch
            {
                ProjectPhase.TopicPicked   => 0,
                ProjectPhase.SynopsisDone  => 1,
                ProjectPhase.OutlineDone   => 2,
                ProjectPhase.ChapterActive => 2,
                _ => 0
            };

            // 如果已到 OutlineDone/ChapterActive 阶段 —— 直接进入章节写作
            // 之前是空实现，导致用户点了"开始"没有任何反馈。
            // 现在解析 outline.md，对每个未写作的章节调用 LLM 生成正文。
            if (startStage >= 2)
            {
                await RunChapterWritingStageAsync(meta, genre, ct);
                return;
            }

            // 把之前已完成阶段的 Stage 状态标记为 ✓（不重跑）
            for (int i = 0; i < startStage && i < _stages.Count; i++)
            {
                _stages[i].Status = "✓";
                _stages[i].Color = "#A6E3A1";
            }

            // ============= Stage 1: 梗概（仅 TopicPicked 时执行） =============
            string synopsisText = "";
            if (startStage <= 0 && !ct.IsCancellationRequested)
            {
                await RunStageWithRewriteAsync(
                    stageIndex: 0,
                    generatingText: "正在生成故事梗概...",
                    previewTitle: "梗概（待确认 — 点 ✓ 确认 或 ↻ 重写）",
                    onGeneratingAsync: async (previousDraft) =>
                    {
                        // 取出当前改写指令（如果有），传给生成器；用完清空避免污染下一阶段
                        var directive = _rewriteDirective;
                        _rewriteDirective = null;

                        // 编辑模式：用户点 ↻ + 带改写指令 + 有上一版草稿 → 局部修改
                        if (!string.IsNullOrWhiteSpace(directive) && !string.IsNullOrWhiteSpace(previousDraft))
                        {
                            return await EditSynopsisByDirectiveAsync(
                                directive: directive!,
                                previousDraft: previousDraft!,
                                title: meta?.Title ?? "未命名",
                                genre: genre,
                                ct: ct);
                        }

                        // 普通模式（首次生成 或 纯重写）：走 LLM 全量生成
                        var storyIdea = string.IsNullOrEmpty(directive)
                            ? idea
                            : $"{idea}\n\n【用户改写指令】: {directive}（这是重新生成，使用新方向）";

                        var r = await synopsisGen.GenerateAsync(
                            title: meta?.Title ?? "未命名",
                            genre: genre,
                            tags: "",
                            storyIdea: storyIdea,
                            targetWordCount: "30万",
                            ct: ct);
                        if (!r.Success) return (false, r.Error ?? "未知错误", "");
                        _pendingSynopsis = r;
                        var text =
                            $"# {r.Title}\n\n" +
                            $"**核心冲突:** {r.CoreConflict}\n" +
                            $"**主角:** {r.MainCharacterName}\n\n" +
                            $"{r.Synopsis}";
                        return (true, null, text);
                    },
                    onConfirmed: async (finalText) =>
                    {
                        synopsisText = finalText;
                        await File.WriteAllTextAsync(Path.Combine(_projectDir, "synopsis.md"), finalText, ct);
                        OpenTab("梗概", Path.Combine(_projectDir, "synopsis.md"));
                        UpdatePhase(ProjectPhase.SynopsisDone);
                        RefreshTree();   // 刷新左栏，让"梗概"条目下出现 synopsis.md
                        Chat("系统", "✓ 梗概已保存。下一步：生成大纲。");
                    },
                    ct);
            }

            if (ct.IsCancellationRequested) return;
            if (_pipelineAborted) return;

            // ============= Stage 2: 大纲（仅未完成大纲时执行） =============
            if (startStage <= 1 && !ct.IsCancellationRequested)
            {
                // 如果从 Stage 1 推进过来，synopsisText 已被填充；
                // 如果从已有 synopsis 直接跳到 Stage 2，从文件加载
                if (string.IsNullOrEmpty(synopsisText))
                {
                    var sp = Path.Combine(_projectDir, "synopsis.md");
                    if (File.Exists(sp))
                        synopsisText = await File.ReadAllTextAsync(sp, ct);

                    // 也尝试从已存在的 synopsis 解析 _pendingSynopsis（用于大纲 prompt）
                    if (_pendingSynopsis == null)
                    {
                        _pendingSynopsis = ParseSynopsisFromFile(synopsisText);
                    }
                }

                await RunStageWithRewriteAsync(
                    stageIndex: 1,
                    generatingText: "正在生成分章大纲...",
                    previewTitle: "大纲（待确认 — 点 ✓ 确认 或 ↻ 重写）",
                    onGeneratingAsync: async (previousDraft) =>
                    {
                        // 取出当前改写指令
                        var directive = _rewriteDirective;
                        _rewriteDirective = null;

                        // 编辑模式：用户点 ↻ + 指令 + 上一版草稿 → 局部修改大纲
                        if (!string.IsNullOrWhiteSpace(directive) && !string.IsNullOrWhiteSpace(previousDraft))
                        {
                            return await EditOutlineByDirectiveAsync(
                                directive: directive!,
                                previousDraft: previousDraft!,
                                title: meta?.Title ?? "未命名",
                                genre: genre,
                                synopsis: synopsisText,
                                ct: ct);
                        }

                        // 普通模式：把 directive 当作"重新生成的方向"传给 LLM
                        var synopsisWithDirective = string.IsNullOrEmpty(directive)
                            ? synopsisText
                            : $"{synopsisText}\n\n【用户改写指令】: {directive}（这是重新生成，使用新方向）";

                        var r = await outlineGen.GenerateAsync(
                            new ProjectId(projGuid),
                            genre: genre,
                            synopsis: synopsisWithDirective,
                            coreConflict: _pendingSynopsis?.CoreConflict ?? "",
                            mainCharacter: _pendingSynopsis?.MainCharacterName ?? "",
                            tags: "",
                            totalChapters: 10,
                            ct);
                        if (!r.Success || r.Outlines.Count == 0)
                            return (false, r.Error ?? "大纲为空", "");
                        _pendingOutline = r;
                        return (true, null, BuildOutlinePreview(r));
                    },
                    onConfirmed: async (finalText) =>
                    {
                        await File.WriteAllTextAsync(Path.Combine(_projectDir, "outline.md"), finalText, ct);
                        // 章节存根创建：优先用 _pendingOutline（首次生成）；如果是编辑后的草稿，
                        // 解析 finalText 里的 ## 第N章 段落来创建存根。
                        if (_pendingOutline != null)
                        {
                            foreach (var o in _pendingOutline.Outlines)
                            {
                                var chPath = Path.Combine(_projectDir, "chapters", $"第{o.ChapterNumber:00}章.md");
                                if (!File.Exists(chPath))
                                    await File.WriteAllTextAsync(chPath,
                                        $"# 第{o.ChapterNumber}章 {o.SceneDescription}\n\n{o.KeyEvents ?? ""}\n\n(待AI生成)\n", ct);
                            }
                        }
                        else
                        {
                            // 编辑模式：从 finalText 解析章节标题，存根
                            var edited = ParseOutlineChapters(finalText);
                            foreach (var o in edited)
                            {
                                var chPath = Path.Combine(_projectDir, "chapters", $"第{o.Number:00}章.md");
                                if (!File.Exists(chPath))
                                    await File.WriteAllTextAsync(chPath,
                                        $"# 第{o.Number}章 {o.Title}\n\n{o.Scene}\n\n(待AI生成)\n", ct);
                            }
                        }
                        UpdatePhase(ProjectPhase.OutlineDone);
                        RefreshTree();
                        var chCount = _pendingOutline?.Outlines.Count ?? ParseOutlineChapters(finalText).Count;
                        Chat("系统", $"✓ 大纲已保存 ({chCount}章)");
                    },
                    ct);
            }

            if (ct.IsCancellationRequested) return;
            if (_pipelineAborted) return;

            // 大纲已完成。在预览区给出明确的"下一步"二选一入口：
            // ✓ = 立即进入章节写作（继续当前 pipeline）
            // ↻ = 推迟，等用户再次点 ▶ 开始
            // 这样避免让用户去工具栏里找按钮。
            if (!ct.IsCancellationRequested && !_pipelineAborted)
            {
                ShowActions(false);
                ShowPreview("下一步 — 大纲已就绪", "✓ = 立即开始按大纲写作章节\n↻ = 暂不写作，我手动再点 ▶ 开始");
                Chat("系统", "大纲已就绪。点 ✓ 立即开始写作，或 ↻ 暂存大纲。");

                var decision = await WaitUserDecisionAsync(ct);

                if (decision == 1)
                {
                    // 继续写作 —— 在同一个 pipeline 内接着走章节阶段
                    _continueToChapters = true;
                    HidePreview();
                    await RunChapterWritingStageAsync(meta, genre, ct);
                }
                else
                {
                    // 暂不写作，保持 OutlineDone
                    _continueToChapters = false;
                    HidePreview();
                    Chat("系统", "已保存大纲状态。准备好后再次点击 ▶ 开始写作。");
                }
            }
        }
        catch (OperationCanceledException)
        {
            HideThinking();
            Chat("系统", "流水线已停止");
        }
        catch (Exception ex)
        {
            HideThinking();
            Chat("错误", ex.Message);
        }
        finally
        {
            _isRunning = false;
            StopBtn.Visibility = Visibility.Collapsed;
            HidePreview();
            ShowActions(false);  // 清理：失败/取消时不要留下"确认/重写"按钮
            UpdateStartButtonVisibility();
        }
    }

    /// <summary>
    /// 通用 Stage 执行器：循环"生成 → 等待用户 ✓ 确认/↻ 重写"。
    /// 确认后调 onConfirmed 持久化；重写则继续循环。
    /// </summary>
    private async Task RunStageWithRewriteAsync(
        int stageIndex,
        string generatingText,
        string previewTitle,
        Func<string?, Task<(bool Success, string? Error, string ResultText)>> onGeneratingAsync,
        Func<string, Task> onConfirmed,
        CancellationToken ct)
    {
        // previousDraft 跨轮保持：第一次为 null（首次撰写），后续轮带上上一轮结果作为基线
        // 让 onGeneratingAsync 在 LLM 看到"上一版草稿"时进入"局部改写"模式
        string? previousDraft = null;

        while (!ct.IsCancellationRequested)
        {
            GuardStep();
            SetStage(stageIndex, true);
            ShowActions(false);
            ShowThinking(generatingText);
            ShowPreview(previewTitle, "AI 正在构思中...");

            var (ok, error, resultText) = await onGeneratingAsync(previousDraft);
            HideThinking();

            if (!ok)
            {
                SetStage(stageIndex, false);
                ShowPreview($"{_stages[stageIndex].Name}（生成失败）", error ?? "未知错误");
                Chat("错误", $"{_stages[stageIndex].Name}生成失败: {error}");
                _pipelineAborted = true;   // 让外层跳过 Stage 3+ 占位循环
                return;
            }

            ShowPreview(previewTitle, resultText);
            SetStage(stageIndex, false);
            ShowActions(true);
            Chat("AI", $"{_stages[stageIndex].Name}已生成。请在预览区查看。");

            var decision = await WaitUserDecisionAsync(ct);
            if (decision == 2)
            {
                // 重写：把当前结果作为 previousDraft 传给下一轮
                previousDraft = resultText;
                continue;
            }

            // 确认：调 onConfirmed 持久化
            await onConfirmed(resultText);
            HidePreview();
            ShowActions(false);
            return;
        }
    }

    /// <summary>
    /// 单章写作循环：流式生成 → 实时预览 → 等待用户 ✓/↻。
    /// 与 <see cref="RunStageWithRewriteAsync"/> 的语义一致，但支持流式推送。
    /// 返回 true 表示用户确认并已写入磁盘，false 表示被中止。
    /// </summary>
    /// <summary>
    /// 章节写作阶段：解析 outline.md → 跳过已有正文的章节 → 逐章流式生成 + 确认。
    /// 被两处调用：
    /// 1. <c>startStage >= 2</c> 的旧项目（OutlineDone / ChapterActive）
    /// 2. 大纲刚刚确认完，用户在大纲预览区点 ✓ 选"继续写作"
    /// </summary>
    private async Task RunChapterWritingStageAsync(ProjectMeta? meta, string genre, CancellationToken ct)
    {
        var chapters = new System.Collections.Generic.List<ChapterOutlineItem>();
        var outlinePath = Path.Combine(_projectDir!, "outline.md");
        if (File.Exists(outlinePath))
        {
            var outlineText = await File.ReadAllTextAsync(outlinePath, ct);
            chapters = ParseOutlineChapters(outlineText);
        }

        if (chapters.Count == 0)
        {
            var outlineFilePath = Path.Combine(_projectDir!, "outline.md");
            if (File.Exists(outlineFilePath))
            {
                var stubContent = await File.ReadAllTextAsync(outlineFilePath, ct);
                Chat("系统", $"outline.md 内容无效（{stubContent.Length} 字符，未找到 `## 第N章` 段落）。判定为上次生成失败残留，将自动回退到 SynopsisDone 重新生成大纲。");
                UpdatePhase(ProjectPhase.SynopsisDone, reason: "outline.md 无效，自动回退");
            }
            else
            {
                Chat("系统", "未找到 outline.md。请先生成大纲。");
            }
            return;
        }

        var spPath = Path.Combine(_projectDir!, "synopsis.md");
        var synopsisForPrompt = File.Exists(spPath)
            ? await File.ReadAllTextAsync(spPath, ct)
            : (meta?.Idea ?? "");

        Chat("系统", $"开始按大纲写作 — 共 {chapters.Count} 章");
        SetStage(2, true);

        int written = 0;
        foreach (var ch in chapters)
        {
            if (ct.IsCancellationRequested) break;

            var chPath = Path.Combine(_projectDir!, "chapters", $"第{ch.Number:00}章.md");
            var existing = File.Exists(chPath) ? await File.ReadAllTextAsync(chPath, ct) : "";
            if (!string.IsNullOrWhiteSpace(existing) && existing.Length > 500
                && !existing.Contains("(待AI生成)"))
            {
                Chat("系统", $"第{ch.Number}章已有正文，跳过");
                continue;
            }

            var confirmed = await RunChapterWithRewriteAsync(
                chapter: ch,
                genre: genre,
                bookTitle: meta?.Title ?? "",
                synopsis: synopsisForPrompt,
                chapterPath: chPath,
                ct);

            if (ct.IsCancellationRequested) break;

            if (!confirmed)
            {
                Chat("系统", "章节写作已暂停。已确认的章节已保存。");
                break;
            }

            UpdatePhase(ProjectPhase.ChapterActive, ch.Number);
            RefreshTree();
            written++;
        }

        if (written > 0)
            Chat("系统", $"本次共写入 {written} 章。可继续点 ▶ 续写剩余章节，或在左侧资源栏打开章节查看。");
        else if (!_pipelineAborted && !ct.IsCancellationRequested)
            Chat("系统", "所有章节均已存在或未确认，未生成新内容。");
    }

    private async Task<bool> RunChapterWithRewriteAsync(
        ChapterOutlineItem chapter,
        string genre,
        string bookTitle,
        string synopsis,
        string chapterPath,
        CancellationToken ct)
    {
        var previewTitle = $"第{chapter.Number}章《{chapter.Title}》（待确认 — 点 ✓ 写入 或 ↻ 重写）";
        // previousDraft 在循环里跨轮保持：第一次生成时为 null（全新撰写），
        // 用户点 ↻ 后，下一轮带上"上一轮结果"作为基线 —— LLM 看到它就知道是"编辑"不是"重写"
        string? previousDraft = null;

        while (!ct.IsCancellationRequested)
        {
            GuardStep();
            ShowActions(false);
            ShowThinking($"正在写第{chapter.Number}章《{chapter.Title}》...");
            ShowPreview(previewTitle, "");  // 流式填充

            string content;
            try
            {
                // 取出当前改写指令（如果有），传给生成器；用完清空避免污染下一章
                var directive = _rewriteDirective;
                _rewriteDirective = null;

                content = await GenerateChapterStreamingAsync(
                    genre, bookTitle, synopsis, chapter,
                    directive: directive,
                    previousDraft: previousDraft,
                    onChunk: AppendPreview,
                    ct: ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                HideThinking();
                ShowPreview(previewTitle, $"[生成失败] {ex.Message.Truncate(500)}");
                Chat("错误", $"第{chapter.Number}章生成失败: {ex.Message.Truncate(200)}");
                _pipelineAborted = true;
                return false;
            }
            HideThinking();

            if (string.IsNullOrWhiteSpace(content))
            {
                ShowPreview(previewTitle, "[生成失败] 模型返回为空");
                Chat("错误", $"第{chapter.Number}章生成失败：模型返回为空");
                _pipelineAborted = true;
                return false;
            }

            // 记录本次结果，下一轮如果用户点 ↻ 就把它作为 previousDraft
            previousDraft = content;

            ShowActions(true);
            Chat("AI", $"第{chapter.Number}章《{chapter.Title}》已生成 ({content.Length} 字符)。点 ✓ 确认保存，或 ↻ 重新生成。");

            var decision = await WaitUserDecisionAsync(ct);

            if (decision == 2)
            {
                // 重写：清空预览，循环顶端重新生成
                ShowActions(false);
                continue;
            }

            // 确认：写入磁盘
            try
            {
                await File.WriteAllTextAsync(chapterPath, content, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Chat("错误", $"第{chapter.Number}章写入失败: {ex.Message.Truncate(200)}");
                _pipelineAborted = true;
                return false;
            }
            Chat("系统", $"✓ 第{chapter.Number}章《{chapter.Title}》已写入 ({content.Length} 字符)");
            HidePreview();
            ShowActions(false);
            return true;
        }
        return false;   // 取消视为中止
    }

    /// <summary>
    /// 等待用户在 ✓ 确认 / ↻ 重写 之间二选一。
    /// </summary>
    private async Task<int> WaitUserDecisionAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            _confirmTcs = new TaskCompletionSource<int>();
            var decision = await _confirmTcs.Task;
            if (decision == 1 || decision == 2) return decision;
        }
        return 1; // 取消视为"确认"
    }

    private static NovelWriter.Engine.Pipeline.SynopsisResult ParseSynopsisFromFile(string text)
    {
        // 简单解析：提取 # 标题、**核心冲突:** 和 **主角:** 字段
        var title = "";
        var coreConflict = "";
        var mainChar = "";

        foreach (var line in text.Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith("# ") && string.IsNullOrEmpty(title))
                title = t[2..].Trim();
            else if (t.StartsWith("**核心冲突:**") || t.StartsWith("**核心冲突："))
                coreConflict = t.Substring(t.IndexOf(':') + 1).Replace("：", ":").Trim();
            else if (t.StartsWith("**主角:**") || t.StartsWith("**主角："))
                mainChar = t.Substring(t.IndexOf(':') + 1).Replace("：", ":").Trim();
        }

        return new NovelWriter.Engine.Pipeline.SynopsisResult
        {
            Title = title,
            CoreConflict = coreConflict,
            MainCharacterName = mainChar,
            Synopsis = text
        };
    }

    private static string BuildOutlinePreview(NovelWriter.Engine.Pipeline.OutlineResult outline)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# 分章大纲");
        sb.AppendLine();
        foreach (var o in outline.Outlines)
        {
            sb.AppendLine($"## 第{o.ChapterNumber}章 {o.SceneDescription}");
            sb.AppendLine(o.KeyEvents ?? "");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>
    /// 章节大纲条目。从 outline.md 的 `## 第N章 标题` 段落解析得到。
    /// </summary>
    private class ChapterOutlineItem
    {
        public int Number { get; set; }
        public string Title { get; set; } = "";
        public string Scene { get; set; } = "";
        public string KeyEvents { get; set; } = "";
    }

    /// <summary>
    /// 从 outline.md 解析章节列表。匹配模式：`## 第N章 标题` 作为起点，
    /// 下一段 `## 第N+1章` 之前的所有非 `##` 开头的行都视作 key_events/scene 描述。
    /// </summary>
    private static System.Collections.Generic.List<ChapterOutlineItem> ParseOutlineChapters(string outlineText)
    {
        var list = new System.Collections.Generic.List<ChapterOutlineItem>();
        if (string.IsNullOrWhiteSpace(outlineText)) return list;

        // 匹配 "## 第N章 标题" 或 "## 第N卷 第N章 标题"
        var rx = new System.Text.RegularExpressions.Regex(
            @"^##\s*第\s*(\d+)\s*章\s*(.*?)\s*$",
            System.Text.RegularExpressions.RegexOptions.Multiline);

        var matches = rx.Matches(outlineText);
        if (matches.Count == 0) return list;

        for (int i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            var number = int.Parse(m.Groups[1].Value);
            var title = m.Groups[2].Value.Trim();
            if (string.IsNullOrEmpty(title)) title = $"第{number}章";

            // 提取本章节内容范围
            int start = m.Index + m.Length;
            int end = (i + 1 < matches.Count) ? matches[i + 1].Index : outlineText.Length;
            var section = outlineText.Substring(start, end - start).Trim();

            // 章节正文里可能包含 `**核心冲突:**` 这样的字段说明行，
            // 但我们只需要 key_events 描述行（没有 `**字段:**` 前缀的非空行）
            var lines = section.Split('\n')
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrEmpty(l)
                    && !l.StartsWith("#")
                    && !l.StartsWith("**")
                    && !l.StartsWith("---"))
                .ToList();

            var scene = title;
            var keyEvents = string.Join("\n", lines);

            list.Add(new ChapterOutlineItem
            {
                Number = number,
                Title = title,
                Scene = scene,
                KeyEvents = keyEvents
            });
        }

        return list;
    }

    /// <summary>
    /// 调用 LLM 生成单章正文。基于已确认的梗概和大纲条目提示模型。
    /// 输出格式：Markdown，第一个 H1 是章节标题，其后是 2000-3000 字的章节正文。
    /// </summary>
    private async Task<string> GenerateChapterAsync(
        string genre, string title, string synopsis, ChapterOutlineItem chapter,
        string? directive, CancellationToken ct)
    {
        var adapter = NovelWriterApp.Services.GetRequiredService<ILlmAdapter>();

        var (systemPrompt, userMessage) = BuildChapterPrompts(genre, title, synopsis, chapter, directive);
        var content = await adapter.ChatAsync(systemPrompt, userMessage, ct);
        return content?.Trim() ?? "";
    }

    /// <summary>
    /// 流式生成单章正文。每收到一段 LLM 输出就把回调里的 chunk 推给 UI（用于实时预览）。
    /// 返回拼接后的完整正文。directive 是用户的改写指令（可能为 null）。
    /// previousDraft 是上一次生成的结果（如果有），用于"局部改写"——LLM 把它作为基线只改指定部分。
    /// </summary>
    private async Task<string> GenerateChapterStreamingAsync(
        string genre, string title, string synopsis, ChapterOutlineItem chapter,
        string? directive, string? previousDraft,
        Action<string> onChunk, CancellationToken ct)
    {
        var adapter = NovelWriterApp.Services.GetRequiredService<ILlmAdapter>();
        var (systemPrompt, userMessage) = BuildChapterPrompts(genre, title, synopsis, chapter, directive, previousDraft);

        var sb = new System.Text.StringBuilder();
        await foreach (var chunk in adapter.StreamChatAsync(systemPrompt, userMessage, ct))
        {
            if (string.IsNullOrEmpty(chunk)) continue;
            sb.Append(chunk);
            onChunk?.Invoke(chunk);
        }
        return sb.ToString().Trim();
    }

    private static (string system, string user) BuildChapterPrompts(
        string genre, string title, string synopsis, ChapterOutlineItem chapter,
        string? directive = null, string? previousDraft = null)
    {
        // 模式判断：
        // - 有 directive 且有 previousDraft → 局部改写（编辑任务）
        // - 否则 → 全新撰写
        bool isEdit = !string.IsNullOrWhiteSpace(directive) && !string.IsNullOrWhiteSpace(previousDraft);

        string systemPrompt;
        string userMessage;
        if (isEdit)
        {
            // === 编辑模式：以 previousDraft 为基线，只按指令修改 ===
            systemPrompt = $$"""
                你是网文编辑。**任务**: 根据用户改写指令，对【现有章节草稿】进行**局部修改**。

                **核心原则**:
                1. **保留原稿的 90% 以上内容**——只修改用户指令明确要求改动的部分
                2. 人物名字、场景顺序、叙事结构、未提及的剧情一律保持原样
                3. 改写后输出**完整章节文本**（含原稿中保留的部分），不要只输出 diff
                4. 保持原有的字数规模（不要缩到几百字、也不要硬扩）
                5. 章节第一行仍是 `# 第{{chapter.Number}}章 {{chapter.Title}}`
                6. 题材风格保持不变：{{genre}}

                **题材**: {{genre}}
                """;

            userMessage = $"""
                【书名】: {title}

                【用户改写指令 — 必须严格执行】: {directive}

                【现有章节草稿（请以此为基线，仅修改指令相关部分）】:
                ```
                {TruncateForPrompt(previousDraft!, 4000)}
                ```

                请输出改写后的**完整章节文本**：
                """;
        }
        else
        {
            // === 全新撰写模式 ===
            var directiveBlock = string.IsNullOrWhiteSpace(directive)
                ? ""
                : $"\n\n**【用户改写指令】**: {directive}（这是重新生成，使用新方向）";

            systemPrompt = $$"""
                你是网文作家。**任务**: 撰写《{{title}}》的第{{chapter.Number}}章正文。

                **题材**: {{genre}}（**所有情节、人物、风格必须严格契合这个题材**）

                **写作要求**:
                1. 严格遵循提供的梗概和大纲条目——不要重设情节方向、不要修改人物设定
                2. 字数 **2000-3000 字** 的连续叙事正文
                3. 章节第一行是 `# 第{{chapter.Number}}章 {{chapter.Title}}`（Markdown H1）
                4. 段落分明、对话自然、动作有画面感
                5. **不要输出 JSON，不要输出大纲预览，只输出章节正文**
                6. 不要在末尾添加作者按语、说明、章节预告等额外内容{{directiveBlock}}
                """;

            userMessage = $"""
                【书名】: {title}
                【题材】: {genre}
                【梗概】: {TruncateForPrompt(synopsis, 1500)}
                【本章大纲】: {chapter.Title} — {chapter.KeyEvents}
                """;
        }
        return (systemPrompt, userMessage);
    }

    private static string TruncateForPrompt(string s, int maxChars) =>
        string.IsNullOrEmpty(s) || s.Length <= maxChars
            ? s ?? ""
            : s[..maxChars] + "…";

    /// <summary>
    /// 局部修改梗概：以 previousDraft 为基线，只按 directive 修改指定部分。
    /// 走 LLM 直接调用，不走 SynopsisGenerator（它不支持编辑模式）。
    /// 返回 (Success, Error, ResultText) —— resultText 是改写后的完整 Markdown 草稿。
    /// </summary>
    private async Task<(bool Success, string? Error, string ResultText)> EditSynopsisByDirectiveAsync(
        string directive, string previousDraft, string title, string genre, CancellationToken ct)
    {
        try
        {
            var adapter = NovelWriterApp.Services.GetRequiredService<ILlmAdapter>();
            var sys = $$"""
                你是网文编辑。**任务**: 根据用户改写指令，对【现有梗概草稿】进行**局部修改**。

                **核心原则**:
                1. **保留原稿的 90% 以上内容**——只修改用户指令明确要求改动的部分
                2. 主角名、核心冲突类型、题材基调、未提及的剧情一律保持原样
                3. 改写后输出**完整梗概**（含原稿中保留的部分），格式与原稿一致：
                   ```
                   # {title}

                   **核心冲突:** ...

                   **主角:** ...

                   <梗概正文>
                   ```
                4. 保持原有字数规模
                5. 题材风格保持不变：{genre}
                """;

            var user = $"""
                【用户改写指令 — 必须严格执行】: {directive}

                【现有梗概草稿（请以此为基线，仅修改指令相关部分）】:
                ```
                {TruncateForPrompt(previousDraft, 3000)}
                ```

                请输出改写后的完整梗概（Markdown）：
                """;

            var resp = await adapter.ChatAsync(sys, user, ct);
            if (string.IsNullOrWhiteSpace(resp))
                return (false, "LLM 返回为空", "");
            return (true, null, resp.Trim());
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return (false, $"编辑梗概失败: {ex.Message}", "");
        }
    }

    /// <summary>
    /// 局部修改大纲：保留原章节结构、标题、key_events，只按 directive 修改指定内容。
    /// 走 LLM 直接调用；返回改写后的完整 Markdown 大纲文本。
    /// </summary>
    private async Task<(bool Success, string? Error, string ResultText)> EditOutlineByDirectiveAsync(
        string directive, string previousDraft, string title, string genre, string synopsis, CancellationToken ct)
    {
        try
        {
            var adapter = NovelWriterApp.Services.GetRequiredService<ILlmAdapter>();
            var sys = $$"""
                你是网文编辑。**任务**: 根据用户改写指令，对【现有大纲】进行**局部修改**。

                **核心原则**:
                1. **保留原稿 90% 以上内容**——只修改用户指令明确要求改动的章节或要素
                2. 章节数、章节顺序、章节标题、未提及的 key_events 一律保持原样
                3. 改写后输出**完整大纲**（含原稿中保留的所有章节），格式与原稿一致：
                   ```
                   # 分章大纲

                   ## 第N章 <标题>
                   <场景简述、key_events 段>
                   ```
                4. 题材风格保持不变：{genre}
                5. 不要新增章节、不要删除章节、不要重排顺序
                """;

            var user = $"""
                【书名】: {title}
                【题材】: {genre}
                【梗概（参考）】: {TruncateForPrompt(synopsis, 800)}

                【用户改写指令 — 必须严格执行】: {directive}

                【现有大纲（请以此为基线，仅修改指令相关部分）】:
                ```
                {TruncateForPrompt(previousDraft, 4000)}
                ```

                请输出改写后的完整大纲（Markdown）：
                """;

            var resp = await adapter.ChatAsync(sys, user, ct);
            if (string.IsNullOrWhiteSpace(resp))
                return (false, "LLM 返回为空", "");
            return (true, null, resp.Trim());
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return (false, $"编辑大纲失败: {ex.Message}", "");
        }
    }




    private void StageConfirm_Click(object sender, RoutedEventArgs e)
    {
        _confirmTcs?.TrySetResult(1);
        ShowActions(false);
    }

    /// <summary>
    /// 点击 ↻ 重写 —— 不立即重新生成，而是先在聊天区询问用户是否有修改要求：
    /// - 用户在聊天框里输入具体修改指令 → 下一轮生成会带上指令
    /// - 用户直接发空消息（按回车即可） → 视为无修改重写
    /// 这样既保留"随机重写"的便利，也支持"按指令改写"。
    /// </summary>
    private void StageRewrite_Click(object sender, RoutedEventArgs e)
    {
        _awaitingRewriteDirective = true;
        _rewriteDirective = null;   // 清空上一轮可能残留的指令
        ShowActions(false);
        Chat("系统", "🔁 进入重写模式。在下方输入修改要求（例：'要直接打败朱元璋'）后回车；\n" +
                    "若直接回车（不输入任何内容）则按原方向重新生成。");
        ChatInput.Focus();
    }

    private void StageRetry_Click(object sender, RoutedEventArgs e) { _confirmTcs?.TrySetResult(2); }

    private void StopAi_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning)
        {
            Chat("系统", "正在停止...");
            _aiCts?.Cancel();
        }
    }

    private void UpdateStartButtonVisibility()
    {
        if (_isRunning) return;
        if (!NovelWriterApp.LlmConfig.HasKey) return;
        if (string.IsNullOrEmpty(_projectDir)) return;
        StartAiBtn.Visibility = Visibility.Visible;
    }

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
            // Stop 按钮跟随 ThinkingBar 走，不在这里管
        }
        else
        {
            StageActions.Visibility = Visibility.Collapsed;
            ConfirmBtn.Visibility = Visibility.Collapsed;
            RewriteBtn.Visibility = Visibility.Collapsed;
            // StopBtn 由 ShowThinking/HideThinking 一起管，不在这里管
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

        // 记录到会话记忆（短期）—— 用户/AI/系统/错误消息都是事件
        if (_session != null)
        {
            var evType = role switch
            {
                "用户" => "UserMessage",
                "AI" => "AIMessage",
                "错误" => "ErrorMessage",
                _ => "SystemMessage"
            };
            _session.AppendEvent(new SessionEvent
            {
                Type = evType,
                Text = text
            });
        }
    }

    private CancellationTokenSource? _chatCts;

    // === 会话记忆 UI 入口 ===

    /// <summary>
    /// 刷新会话状态条 —— 展示当前短期/长期记忆的事件数和 token 估算。
    /// </summary>
    private void UpdateSessionButtonHud()
    {
        if (_session == null) return;
        var tokens = _session.Short.EstimatedTokens;
        var recent = _session.Short.Recent.Count;
        var longCount = _session.Long.Entries.Count;
        var overLimit = tokens > SessionMemoryManager.TokenSoftLimit;
        // 状态栏：简洁数字提示，不变红（避免与上下文按钮的视觉告警重复）
        StatusBarText.Text = overLimit
            ? $"⚠ 上下文 {tokens}/{SessionMemoryManager.TokenSoftLimit} tokens — 点 📊 上下文 可压缩"
            : $"上下文: {tokens}/{SessionMemoryManager.TokenSoftLimit} tokens";
        // 如果上下文弹窗当前是打开的，刷新里面的内容
        if (ContextPopup.IsOpen) RefreshContextPopup();
    }

    private async void CompressSession_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null) return;
        // 1. 取出要压缩的事件（recent 里前 N - RecentKeepCount 条）
        var toCompress = _session.TakeEventsToCompress();
        if (toCompress.Count == 0)
        {
            Chat("系统", $"短期记忆只有 {_session.Short.Recent.Count} 条，无需压缩。");
            return;
        }

        Chat("系统", $"🗜 正在用 LLM 压缩 {toCompress.Count} 条早期事件为摘要...");
        try
        {
            var adapter = NovelWriterApp.Services.GetRequiredService<ILlmAdapter>();
            var eventsText = string.Join("\n", toCompress.Select(e => $"[{e.Timestamp:HH:mm:ss}] {e.Type}: {e.Text}"));
            var sys = "你是会话摘要助手。把下方事件列表压缩成 200-400 字的要点摘要，保留关键决策、改写指令、生成结果。输出纯文本，不要分点列表、不要 JSON。";
            var summary = await adapter.ChatAsync(sys, eventsText, default);

            // 2. 写回短期（合并到 summary）
            _session.FinalizeCompression(summary, toCompress, "Compress");
            UpdateSessionButtonHud();
            Chat("系统", $"✓ 压缩完成。当前短期 {tokensText(_session)}。");
        }
        catch (Exception ex)
        {
            Chat("错误", $"压缩失败: {ex.Message.Truncate(200)}");
        }
    }

    private void ArchiveToLongTerm_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null) return;
        if (_session.Short.Recent.Count == 0 && string.IsNullOrEmpty(_session.Short.Summary))
        {
            Chat("系统", "短期记忆为空，无需归档。");
            return;
        }

        // 用 LLM 一次性生成完整归档摘要（短期 + 早期 summary 合并）
        _ = ArchiveToLongTermAsync();
    }

    private async Task ArchiveToLongTermAsync()
    {
        if (_session == null) return;
        try
        {
            var adapter = NovelWriterApp.Services.GetRequiredService<ILlmAdapter>();
            var fullText = "";
            if (!string.IsNullOrEmpty(_session.Short.Summary))
                fullText += "【早期摘要】\n" + _session.Short.Summary + "\n\n";
            fullText += "【近期事件】\n" + string.Join("\n",
                _session.Short.Recent.Select(e => $"[{e.Timestamp:HH:mm:ss}] {e.Type}: {e.Text}"));

            var sys = "你是会话归档助手。给下方整段会话生成一份 300-500 字的结构化归档摘要：要点列表 + 关键决策 + 未解决问题。然后追加一行 `关键词: w1, w2, w3` 列出 3-5 个主题词。";
            var resp = await adapter.ChatAsync(sys, fullText, default);

            _session.ArchiveToLongTerm(resp, "Clear");
            _session.ClearShortTermWithoutArchive();
            UpdateSessionButtonHud();
            Chat("系统", "✓ 短期记忆已归档到长期，并清空。");
        }
        catch (Exception ex)
        {
            Chat("错误", $"归档失败: {ex.Message.Truncate(200)}");
        }
    }

    private void ShowSessionMemory_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null) return;
        var sb = new StringBuilder();
        sb.AppendLine($"📚 短期: {_session.Short.Recent.Count} 条 / 约 {_session.Short.EstimatedTokens} tokens");
        sb.AppendLine($"   早期摘要覆盖: {_session.Short.SummarySourceCount} 条");
        sb.AppendLine($"📦 长期: {_session.Long.Entries.Count} 条压缩归档");
        if (_session.Long.Entries.Count > 0)
        {
            foreach (var entry in _session.Long.Entries.TakeLast(3))
                sb.AppendLine($"   - [{entry.Timestamp:yyyy-MM-dd HH:mm}] {entry.Trigger} ({entry.SourceEventCount} 事件): {entry.Summary.Truncate(80)}");
        }
        Chat("系统", sb.ToString());
    }

    /// <summary>
    /// 点击"📊 上下文"按钮 —— 弹出小窗显示使用率 + 压缩/归档入口。
    /// </summary>
    private void ContextBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null) return;
        RefreshContextPopup();
        ContextPopup.IsOpen = !ContextPopup.IsOpen;
    }

    private void RefreshContextPopup()
    {
        if (_session == null)
        {
            ContextUsageText.Text = "未打开项目";
            ContextUsageBar.Width = 0;
            ContextStatsText.Text = "";
            return;
        }

        var tokens = _session.Short.EstimatedTokens;
        var softLimit = SessionMemoryManager.TokenSoftLimit;
        var percent = Math.Min(100, tokens * 100 / Math.Max(1, softLimit));
        ContextUsageText.Text = $"短期记忆: {tokens} / {softLimit} tokens ({percent}%)";
        ContextUsageText.Foreground = percent >= 100
            ? new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8))   // 红
            : percent >= 70
                ? new SolidColorBrush(Color.FromRgb(0xFA, 0xB3, 0x87)) // 橙
                : new SolidColorBrush(Color.FromRgb(0xCD, 0xD6, 0xF4)); // 默认

        // 进度条宽度（按比例 0-300px，因 Border Width=320-padding=14*2=292）
        ContextUsageBar.Width = Math.Min(290, 290 * percent / 100);
        ContextUsageBar.Background = percent >= 100
            ? new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8))
            : percent >= 70
                ? new SolidColorBrush(Color.FromRgb(0xFA, 0xB3, 0x87))
                : new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1));

        ContextStatsText.Text =
            $"短期: {_session.Short.Recent.Count} 条事件" +
            (_session.Short.SummarySourceCount > 0 ? $" (含 {_session.Short.SummarySourceCount} 条早期摘要)" : "") +
            $"\n长期: {_session.Long.Entries.Count} 条压缩归档";
    }

    private static string tokensText(SessionMemoryManager m) =>
        $"{m.Short.Recent.Count} 条 / 约 {m.Short.EstimatedTokens} tokens";

    private void ChatSend_Click(object sender, RoutedEventArgs e)
    {
        var text = ChatInput.Text.Trim();

        // === 改写指令拦截：两种触发条件 ===
        // 1. 显式点击 ↻ 后进入等待（_awaitingRewriteDirective = true）
        // 2. 隐式：当前预览面板可见 —— 任何时候用户在预览"待确认"期间发的消息都视为改写指令
        //    这样用户不必先点 ↻ 再打字，自然一些。
        // 即使 text 为空也允许（视为"无修改重写"），用于替代"无修改直接重写"的入口。
        bool isPendingPreview = PreviewPanel.Visibility == Visibility.Visible;
        if (_awaitingRewriteDirective || isPendingPreview)
        {
            _awaitingRewriteDirective = false;
            _rewriteDirective = string.IsNullOrEmpty(text) ? null : text;

            // 把用户的输入回显到聊天
            Chat("用户", string.IsNullOrEmpty(text) ? "（无修改指令，直接重写）" : text);
            ChatInput.Text = "";

            if (_rewriteDirective != null)
                Chat("系统", $"📝 已收到改写指令：{_rewriteDirective}\n将按此要求重新生成...");

            // 触发重写循环继续
            _confirmTcs?.TrySetResult(2);
            ShowActions(false);
            return;
        }

        // === 普通聊天：需要非空内容 ===
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
        if (!NovelWriterApp.LlmConfig.HasKey)
        {
            Chat("错误", "请先在左下角 ▼ 配置 LLM API Key");
            return;
        }

        try
        {
            ShowThinking("AI 思考中...");

            // 组装"项目感知"的 system prompt —— AI 知道当前小说标题/阶段/已有内容，
            // 即使用户没有显式点 ↻ 触发改写拦截，AI 也能基于上下文判断意图。
            var sys = BuildProjectAwareSystemPrompt();

            var adapter = NovelWriterApp.Services.GetRequiredService<ILlmAdapter>();
            var resp = await adapter.ChatAsync(sys, userMsg, ct);
            HideThinking();
            Chat("AI", resp);
        }
        catch (OperationCanceledException) { HideThinking(); }
        catch (Exception ex) { HideThinking(); Chat("错误", ex.Message); }
    }

    /// <summary>
    /// 拼装"项目感知"的 system prompt。给聊天 AI 提供：
    /// 1. 角色定位（你是 NovelWriter 助手）
    /// 2. 当前项目元信息（标题/题材/阶段）
    /// 3. 已生成的梗概/大纲前几行（让 AI 理解用户问的是关于这本书的问题）
    /// 4. 当前预览面板内容（如果可见）—— 让 AI 能基于真实草稿回答修改要求
    /// 5. 意图路由说明：让 AI 自行判断"这是改写指令还是普通聊天"
    /// </summary>
    private string BuildProjectAwareSystemPrompt()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("你是 NovelWriter 助手 —— 网文创作 AI。回答时优先基于下方提供的【当前小说项目上下文】。");

        if (_projectDir == null)
        {
            sb.AppendLine("\n【当前项目】: （未打开项目）");
            sb.AppendLine("\n（用户尚未打开任何项目，请按一般性问题回答）");
            return sb.ToString();
        }

        // 1. 项目元信息
        var meta = LoadOrRecoverMeta();
        sb.AppendLine($"\n【当前项目】");
        sb.AppendLine($"标题: {meta?.Title ?? "未命名"}");
        sb.AppendLine($"题材: {meta?.Genre ?? "未指定"}");
        sb.AppendLine($"阶段: {PhaseLabel(meta?.Phase ?? ProjectPhase.TopicPicked)}");

        // 2. 已生成的梗概
        var synopsisPath = Path.Combine(_projectDir, "synopsis.md");
        if (File.Exists(synopsisPath))
        {
            var synopsis = ReadFileSafe(synopsisPath);
            if (!string.IsNullOrWhiteSpace(synopsis))
                sb.AppendLine($"\n【当前梗概】\n{TruncateForPrompt(synopsis, 1200)}");
        }

        // 3. 已生成的大纲
        var outlinePath = Path.Combine(_projectDir, "outline.md");
        if (File.Exists(outlinePath))
        {
            var outline = ReadFileSafe(outlinePath);
            if (!string.IsNullOrWhiteSpace(outline))
                sb.AppendLine($"\n【当前大纲】\n{TruncateForPrompt(outline, 1500)}");
        }

        // 4. 预览面板可见时，附上预览内容 —— 这是 AI 判断"用户消息是否改写指令"的关键
        if (PreviewPanel.Visibility == Visibility.Visible)
        {
            var previewText = PreviewBody.Text ?? "";
            if (!string.IsNullOrWhiteSpace(previewText))
            {
                sb.AppendLine($"\n【当前待确认预览 — {PreviewTitle.Text}】\n{TruncateForPrompt(previewText, 2000)}");
                sb.AppendLine("\n【意图路由】");
                sb.AppendLine("如果用户消息看起来是对【当前待确认预览】的修改/重写要求（'改写...''改为...''重做...''不要...''增加...'等），");
                sb.AppendLine("请基于用户的指令和当前预览内容，**直接输出改写后的完整文本**，不要附加解释、确认、问答。");
                sb.AppendLine("否则按一般性问题回答。");
            }
        }

        // 5. 会话记忆（短期 + 长期）—— 让 AI 知道"之前我们聊过什么 / 你之前给过什么改写 / 什么梗概"
        if (_session != null && _session.TotalEvents > 0)
            sb.AppendLine(_session.ToPromptSection());

        return sb.ToString();
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

        // 同步到全局运行时配置 —— 让所有已构造的 ILlmAdapter 立即生效
        NovelWriterApp.LlmConfig.Update(
            apiKey: _currentApiKey,
            model: _currentModel,
            endpoint: _currentUri);

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
        // 停止按钮紧跟"AI 工作中"状态出现 —— 在 AI 实际生成/思考时才有意义
        // 等待用户决策时 ThinkingBar 是隐藏的，Stop 也不该出现
        StopBtn.Visibility = _isRunning ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HideThinking()
    {
        ThinkingBar.Visibility = Visibility.Collapsed;
        StopBtn.Visibility = Visibility.Collapsed;
    }

    // === 待确认预览面板 ===
    private void ShowPreview(string title, string body)
    {
        PreviewTitle.Text = title;
        PreviewBody.Text = body;
        PreviewPanel.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// 流式预览：把内容追加到 PreviewBody 末尾并自动滚到底部。
    /// 必须在 UI 线程调用（内部用 Dispatcher）。
    /// </summary>
    private void AppendPreview(string chunk)
    {
        if (string.IsNullOrEmpty(chunk)) return;
        void Do()
        {
            PreviewBody.Text += chunk;
            PreviewScroll.ScrollToEnd();
        }
        if (Dispatcher.CheckAccess()) Do();
        else Dispatcher.Invoke(Do);
    }

    private void HidePreview()
    {
        PreviewPanel.Visibility = Visibility.Collapsed;
        PreviewBody.Text = "";
    }

    private static string PhaseLabel(ProjectPhase phase) => phase switch
    {
        ProjectPhase.TopicPicked => "已创建，等待开始",
        ProjectPhase.SynopsisDone => "梗概已完成",
        ProjectPhase.OutlineDone => "大纲已就绪",
        ProjectPhase.ChapterActive => "写作进行中",
        _ => "等待开始"
    };

    private void GuardStep()
    {
        if (++_pipelineSteps > MaxPipelineSteps)
            throw new InvalidOperationException($"流水线执行步骤超过上限 ({MaxPipelineSteps})，已自动停止防止 token 滥用。");
    }

    /// <summary>
    /// 安全读取文件：处理文件被其他进程/线程瞬态锁定的瞬态错误（重试 5 次）。
    /// 常见场景：编辑器自动保存写入未完成、另一个程序临时持有句柄。
    /// </summary>
    private static string ReadFileSafe(string path)
    {
        const int maxRetries = 5;
        for (int i = 0; i < maxRetries; i++)
        {
            try { return File.ReadAllText(path); }
            catch (IOException) when (i < maxRetries - 1) { Thread.Sleep(50); }
        }
        return File.ReadAllText(path); // 最后一次让异常正常抛出
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

public class FileItem
{
    public string FullPath { get; set; } = "";
    public string DisplayName { get; set; } = "";
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

    /// <summary>
    /// 最近一次 phase 变更原因（仅作"履历提示"，文件系统校验才是真相源）。
    /// 例如："outline.md 内容无效（未找到 ## 第N章 段落，可能上次生成失败）"。
    /// </summary>
    public string? LastPhaseChangeReason { get; set; }

    /// <summary>
    /// 最近 N 条 phase 变更记录。结构: ISO时间|旧phase|新phase|原因。
    /// 只用于人看（聊天区显示），不影响主流程。
    /// </summary>
    public List<string> PhaseHistory { get; set; } = new();
}
