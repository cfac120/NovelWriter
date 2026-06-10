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
            OpenTab(fi.DisplayName, fi.FullPath);
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

    // 用户选择状态：1=确认（持久化并进入下一阶段），2=重写（重新生成当前阶段）
    private TaskCompletionSource<int>? _confirmTcs;
    private bool _isRunning;       // 流水线是否在执行（控制停止/开始按钮可见性）
    private int _pipelineSteps;    // 流水线当前已执行步数（防止无限循环）
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
            var projGuid = string.IsNullOrEmpty(_projectId)
                ? Guid.NewGuid()
                : Guid.Parse(_projectId + "00000000");

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
                    onGeneratingAsync: async () =>
                    {
                        var r = await synopsisGen.GenerateAsync(
                            title: meta?.Title ?? "未命名",
                            genre: genre,
                            tags: "",
                            storyIdea: idea,
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
                        Chat("系统", "✓ 梗概已保存。下一步：生成大纲。");
                    },
                    ct);
            }

            if (ct.IsCancellationRequested) return;

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
                    onGeneratingAsync: async () =>
                    {
                        var r = await outlineGen.GenerateAsync(
                            new ProjectId(projGuid),
                            genre: genre,
                            synopsis: synopsisText,
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
                        UpdatePhase(ProjectPhase.OutlineDone);
                        RefreshTree();
                        Chat("系统", $"✓ 大纲已保存 ({_pendingOutline?.Outlines.Count ?? 0}章)。下一步：开始写作。");
                    },
                    ct);
            }

            if (ct.IsCancellationRequested) return;

            // ============= Stage 3+：后续阶段（占位） =============
            UpdatePhase(ProjectPhase.ChapterActive);
            for (int i = 2; i < _stages.Count; i++)
            {
                if (ct.IsCancellationRequested) break;
                GuardStep();
                SetStage(i, true);
                ShowActions(false);
                ShowThinking($"{_stages[i].Name} 阶段执行中...");
                await Task.Delay(500, ct);
                HideThinking();
                SetStage(i, false);
                ShowActions(true);
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
        Func<Task<(bool Success, string? Error, string ResultText)>> onGeneratingAsync,
        Func<string, Task> onConfirmed,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            GuardStep();
            SetStage(stageIndex, true);
            ShowActions(false);
            ShowThinking(generatingText);
            ShowPreview(previewTitle, "AI 正在构思中...");

            var (ok, error, resultText) = await onGeneratingAsync();
            HideThinking();

            if (!ok)
            {
                SetStage(stageIndex, false);
                ShowPreview($"{_stages[stageIndex].Name}（生成失败）", error ?? "未知错误");
                Chat("错误", $"{_stages[stageIndex].Name}生成失败: {error}");
                return;
            }

            ShowPreview(previewTitle, resultText);
            SetStage(stageIndex, false);
            ShowActions(true);
            Chat("AI", $"{_stages[stageIndex].Name}已生成。请在预览区查看。");

            var decision = await WaitUserDecisionAsync(ct);
            if (decision == 2)
            {
                // 重写：循环顶端重新生成
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

    private void StageConfirm_Click(object sender, RoutedEventArgs e)
    {
        _confirmTcs?.TrySetResult(1);
        ShowActions(false);
    }

    private void StageRewrite_Click(object sender, RoutedEventArgs e)
    {
        _confirmTcs?.TrySetResult(2);
        ShowActions(false);
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
        if (!NovelWriterApp.LlmConfig.HasKey)
        {
            Chat("错误", "请先在左下角 ▼ 配置 LLM API Key");
            return;
        }

        try
        {
            ShowThinking("AI 思考中...");
            // 直接使用 DI 单例，自动应用最新配置
            var adapter = NovelWriterApp.Services.GetRequiredService<ILlmAdapter>();
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
    }

    private void HideThinking()
    {
        ThinkingBar.Visibility = Visibility.Collapsed;
    }

    // === 待确认预览面板 ===
    private void ShowPreview(string title, string body)
    {
        PreviewTitle.Text = title;
        PreviewBody.Text = body;
        PreviewPanel.Visibility = Visibility.Visible;
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
}
