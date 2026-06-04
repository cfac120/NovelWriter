using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using NovelWriter.App.ViewModels;
using NovelWriter.Core;
using NovelWriter.Core.Entities;
using NovelWriter.Core.ValueObjects;
using NovelWriter.Storage;

namespace NovelWriter.App.Views;

public partial class ShellWindow : Window
{
    // === 状态 ===
    private string? _projectDir;
    private string? _projectId;
    private string? _currentApiKey;
    private string? _currentModel = "deepseek-v4-pro";
    private string _currentUri = "https://api.deepseek.com/v1/chat/completions";
    private readonly ObservableCollection<StageItem> _stages = [];
    private readonly ObservableCollection<ChatMsg> _chatMsgs = [];
    private int _activeStage;
    private CancellationTokenSource? _aiCts;

    private static readonly string ProjectsRoot =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NovelWriter");

    public ShellWindow()
    {
        InitializeComponent();
        ChatList.ItemsSource = _chatMsgs;
        StageList.ItemsSource = _stages;
        InitStages();
        LoadLlmConfig();
        Chat("系统", "欢迎使用 NovelWriter。新建或打开一个项目开始写作。");
    }

    // === 初始化 ===
    private void InitStages()
    {
        var names = new[] { "1. 故事梗概", "2. 分章大纲", "3. AI 写作中", "4. 记忆提取", "5. 评审润色" };
        foreach (var n in names) _stages.Add(new StageItem { Name = n, Status = "○", Color = "#6C7086" });
        StageList.Items.Refresh();
    }

    private void LoadLlmConfig()
    {
        ApiKeyBox.Text = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY") ?? "";
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

        ProjectTitle.Text = proj.Title;
        Chat("系统", $"项目 {proj.Title} 已创建，开始 AI 辅助写作...");
        RefreshTree();
        WelcomePanel.Visibility = Visibility.Collapsed;
        await SaveProjectToDb(proj);
        _ = StartAiPipelineAsync();
    }

    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        var dir = Microsoft.VisualBasic.Interaction.InputBox(
            "输入项目目录路径:", "打开项目", ProjectsRoot, -1, -1);
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return;
        OpenExistingProject(dir);
    }

    private void OpenExistingProject(string dir)
    {
        _projectDir = dir;
        var meta = LoadOrRecoverMeta();
        if (meta == null) return;

        ProjectTitle.Text = meta.Title;
        _projectId = meta.Id;
        Chat("系统", $"已打开: {meta.Title} (阶段: {meta.Phase})");
        RefreshTree();
        WelcomePanel.Visibility = Visibility.Collapsed;
        LoadLlmConfig();

        // 根据阶段恢复或继续流水线
        if (meta.Phase != ProjectPhase.ChapterActive)
            _ = ResumePipelineAsync(meta);
    }

    /// <summary>
    /// 加载 project.json。如果损坏/缺失，从目录文件恢复。
    /// </summary>
    private ProjectMeta? LoadOrRecoverMeta()
    {
        var path = Path.Combine(_projectDir!, "project.json");
        if (File.Exists(path))
        {
            try
            {
                var meta = JsonSerializer.Deserialize<ProjectMeta>(File.ReadAllText(path));
                if (meta != null && !string.IsNullOrWhiteSpace(meta.Title))
                    return meta;
            }
            catch { Chat("警告", "project.json 已损坏，尝试从目录恢复..."); }
        }

        // === 恢复逻辑: 扫描目录推断项目状态 ===
        var title = new DirectoryInfo(_projectDir!).Name;
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

        Chat("恢复", $"已从目录恢复: {title}, 阶段={phase}");

        var recovered = new ProjectMeta { Title = title, Phase = phase, Idea = idea };
        SaveProjectMeta(recovered);
        return recovered;
    }

    /// <summary>
    /// 根据阶段恢复流水线。topic/synopsis阶段从头开始；
    /// outline/chapterActive阶段从当前进度继续。
    /// </summary>
    private async Task ResumePipelineAsync(ProjectMeta meta)
    {
        if (meta.Phase <= ProjectPhase.SynopsisDone)
        {
            // 从梗概阶段开始（如果已有梗概文件则跳过）
            Chat("系统", "继续从梗概阶段...");
            _ = StartAiPipelineAsync();
        }
        else if (meta.Phase == ProjectPhase.OutlineDone)
        {
            // 大纲已完成，开始写作
            SetStage(1, false); // 大纲完成
            Chat("系统", "大纲已完成，准备开始写作。");
            ShowActions(true);
        }
        else if (meta.Phase == ProjectPhase.ChapterActive)
        {
            // 直接进入写作
            SetStage(1, false);
            SetStage(2, true);
            Chat("系统", $"继续写作 (第{meta.CurrentChapter + 1}章)...");
        }
    }

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
    private async Task StartAiPipelineAsync()
    {
        if (_projectDir == null) return;
        _aiCts?.Cancel();
        _aiCts = new CancellationTokenSource();
        var ct = _aiCts.Token;

        try
        {
            // Stage 1: 梗概
            SetStage(0, true);
            ShowActions(false);
            Chat("AI", "正在生成故事梗概...");

            var idea = File.Exists(Path.Combine(_projectDir, "idea.md"))
                ? await File.ReadAllTextAsync(Path.Combine(_projectDir, "idea.md"), ct) : "";

            var gen = NovelWriterApp.Services.GetRequiredService<NovelWriter.Engine.Pipeline.SynopsisGenerator>();
            var synopsis = await gen.GenerateAsync(ProjectTitle.Text, "", "30万", ct);

            if (synopsis.Success)
            {
                var synopsisText = $"# {synopsis.Title}\n\n**核心冲突:** {synopsis.CoreConflict}\n**主角:** {synopsis.MainCharacterName}\n\n{synopsis.Synopsis}";
                await File.WriteAllTextAsync(Path.Combine(_projectDir, "synopsis.md"), synopsisText, ct);
                OpenTab("梗概", Path.Combine(_projectDir, "synopsis.md"));
            }
            SetStage(0, false);
            ShowActions(true);
            Chat("AI", $"梗概生成完成。请确认或重写。\n书名: {synopsis.Title}\n{synopsis.Synopsis.Truncate(150)}");

            // Stage 2: 大纲 (梗概确认后立即写盘)
            await WaitConfirmOrRetryAsync(0, ct);
            UpdatePhase(ProjectPhase.SynopsisDone);
            SetStage(1, true);
            ShowActions(false);
            Chat("AI", "正在生成分章大纲...");

            var outlineGen = NovelWriterApp.Services.GetRequiredService<NovelWriter.Engine.Pipeline.OutlineGenerator>();
            var synopsisText2 = File.Exists(Path.Combine(_projectDir, "synopsis.md"))
                ? await File.ReadAllTextAsync(Path.Combine(_projectDir, "synopsis.md"), ct) : "";
            var progId = _projectId;
            var projGuid = string.IsNullOrEmpty(progId) ? Guid.NewGuid() : Guid.Parse(progId + "00000000");
            var outline = await outlineGen.GenerateAsync(new ProjectId(projGuid), synopsisText2, "", synopsis.MainCharacterName, "", 10, ct);

            if (outline.Success)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("# 分章大纲\n");
                foreach (var o in outline.Outlines)
                {
                    sb.AppendLine($"## 第{o.ChapterNumber}章 {o.SceneDescription}");
                    sb.AppendLine(o.KeyEvents ?? "");
                    sb.AppendLine();

                    // 写章节文件
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

            // Stage 3+: 逐章写作 (大纲确认后写盘)
            await WaitConfirmOrRetryAsync(1, ct);
            UpdatePhase(ProjectPhase.OutlineDone);
            for (int i = 2; i < _stages.Count; i++)
            {
                SetStage(i, true);
                ShowActions(false);
                Chat("AI", $"Stage {i + 1} 执行中...");
                await Task.Delay(500, ct); // placeholder
                SetStage(i, false);
                ShowActions(true);
            }
        }
        catch (OperationCanceledException) { Chat("系统", "流水线已取消"); }
        catch (Exception ex) { Chat("错误", ex.Message); }
    }

    private TaskCompletionSource<int>? _confirmTcs;
    private async Task WaitConfirmOrRetryAsync(int stage, CancellationToken ct)
    {
        _activeStage = stage;
        while (!ct.IsCancellationRequested)
        {
            _confirmTcs = new TaskCompletionSource<int>();
            var result = await _confirmTcs.Task;
            if (result == 1) return; // confirmed
            if (result == 2) // retry
            {
                // 重新生成此阶段的产出
                Chat("AI", "正在重新生成...");
                await Task.Delay(1000, ct); // TODO: actual regeneration
                Chat("AI", "重新生成完成。");
            }
        }
    }

    private void StageConfirm_Click(object sender, RoutedEventArgs e) { _confirmTcs?.TrySetResult(1); ShowActions(false); Chat("系统", "✓ 已确认"); }
    private void StageRewrite_Click(object sender, RoutedEventArgs e) { _confirmTcs?.TrySetResult(2); Chat("系统", "↻ 触发重写"); }
    private void StageRetry_Click(object sender, RoutedEventArgs e) { _confirmTcs?.TrySetResult(2); }

    private void SetStage(int index, bool active)
    {
        for (int i = 0; i < _stages.Count; i++)
        {
            if (active && i == index) { _stages[i].Status = "●"; _stages[i].Color = "#FAB387"; }
            else if (!active && i <= index) { _stages[i].Status = "✓"; _stages[i].Color = "#A6E3A1"; }
            else { _stages[i].Status = "○"; _stages[i].Color = "#6C7086"; }
        }
        StageList.Items.Refresh();
    }

    private void ShowActions(bool show)
    {
        StageActions.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
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

    private void ChatSend_Click(object sender, RoutedEventArgs e)
    {
        var text = ChatInput.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;
        Chat("用户", text);
        ChatInput.Text = "";
        _ = AutoReplyAsync(text);
    }

    private async Task AutoReplyAsync(string userMsg)
    {
        var key = ApiKeyBox.Text.Trim();
        if (string.IsNullOrEmpty(key)) { Chat("错误", "请先配置 LLM API Key"); return; }
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            var adapter = new NovelWriter.Engine.Llm.GenericOpenAiAdapter(http, key, ModelBox.Text.Trim(), UriBox.Text.Trim());
            var resp = await adapter.ChatAsync("你是NovelWriter助手", userMsg, CancellationToken.None);
            Chat("AI", resp);
        }
        catch (Exception ex) { Chat("错误", ex.Message); }
    }

    // === LLM 配置 ===
    private void SaveLlmConfig_Click(object sender, RoutedEventArgs e)
    {
        _currentApiKey = ApiKeyBox.Text.Trim();
        _currentModel = ModelBox.Text.Trim();
        _currentUri = UriBox.Text.Trim();
        Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", _currentApiKey, EnvironmentVariableTarget.Process);
        Chat("系统", $"已连接: {_currentModel}");
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
}

// === 数据类 ===
public class StageItem
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = "○";
    public string Color { get; set; } = "#6C7086";
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
