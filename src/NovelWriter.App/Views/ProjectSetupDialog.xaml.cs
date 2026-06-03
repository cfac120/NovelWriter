using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NovelWriter.Core.Entities;
using NovelWriter.Core.Enums;
using NovelWriter.Core.ValueObjects;
using NovelWriter.Engine.Pipeline;
using NovelWriter.Storage;

namespace NovelWriter.App.Views;

public partial class ProjectSetupDialog : Window
{
    private readonly ProjectId _projectId;
    private SynopsisResult? _synopsisResult;
    private List<Outline>? _outlines;
    private int _step = 1;
    private readonly CancellationTokenSource _cts = new();

    public bool SetupCompleted { get; private set; }

    public ProjectSetupDialog(ProjectId projectId)
    {
        _projectId = projectId;
        InitializeComponent();
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        switch (_step)
        {
            case 1: await GenerateSynopsis(); break;
            case 2: await GenerateOutline(); break;
            case 3: await FinalizeSetup(); break;
        }
    }

    private async Task GenerateSynopsis()
    {
        NextBtn.IsEnabled = false;
        var genre = ((ComboBoxItem)GenreCombo.SelectedItem).Content.ToString()!;
        var tags = TagsBox.Text;
        var words = WordGoalBox.Text;

        Panel1.Visibility = Visibility.Collapsed;
        Panel2.Visibility = Visibility.Visible;
        PrevBtn.Visibility = Visibility.Visible;
        NextBtn.Content = "确认梗概，生成大纲";
        SetStepHighlight(2);

        SynopsisStatus.Text = "正在调用 AI 生成梗概...";

        try
        {
            await using var scope = NovelWriterApp.Services.CreateAsyncScope();
            var gen = scope.ServiceProvider.GetRequiredService<SynopsisGenerator>();
            _synopsisResult = await gen.GenerateAsync(genre, tags, words, _cts.Token);

            if (_synopsisResult.Success)
            {
                SynopsisBox.Text = $"""
                    书名: {_synopsisResult.Title}
                    核心冲突: {_synopsisResult.CoreConflict}
                    主角: {_synopsisResult.MainCharacterName}

                    {_synopsisResult.Synopsis}
                    """;

                SynopsisStatus.Text = "梗概已生成，可编辑后确认：";
                SynopsisStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1));
            }
            else
            {
                SynopsisStatus.Text = $"生成失败: {_synopsisResult.Error}";
            }
        }
        catch (Exception ex)
        {
            SynopsisStatus.Text = $"错误: {ex.Message}";
        }
        finally { NextBtn.IsEnabled = true; }
    }

    private async Task GenerateOutline()
    {
        NextBtn.IsEnabled = false;
        Panel2.Visibility = Visibility.Collapsed;
        Panel3.Visibility = Visibility.Visible;
        NextBtn.Content = "确认大纲，开始写作";
        SetStepHighlight(3);

        OutlineStatus.Text = "正在调用 AI 生成大纲...";

        try
        {
            await using var scope = NovelWriterApp.Services.CreateAsyncScope();
            var gen = scope.ServiceProvider.GetRequiredService<OutlineGenerator>();

            // 用户可能编辑了梗概，使用编辑后的内容
            var synopsisText = SynopsisBox.Text;
            var result = await gen.GenerateAsync(
                _projectId,
                synopsisText,
                _synopsisResult?.CoreConflict ?? "",
                _synopsisResult?.MainCharacterName ?? "主角",
                TagsBox.Text,
                totalChapters: 10,
                _cts.Token);

            if (result.Success)
            {
                _outlines = result.Outlines;

                var db = scope.ServiceProvider.GetRequiredService<NovelWriterDbContext>();
                foreach (var o in _outlines)
                    db.Outlines.Add(o);
                await db.SaveChangesAsync(_cts.Token);

                OutlineList.ItemsSource = _outlines.Select(o => new OutlineItem
                {
                    ChapterText = $"第{o.ChapterNumber}章",
                    Title = o.SceneDescription ?? $"(第{o.ChapterNumber}章)",
                    Scene = o.SceneDescription ?? ""
                }).ToList();

                OutlineStatus.Text = $"大纲已生成: {_outlines.Count}章，{_outlines.Max(o => o.VolumeNumber)}卷";
                OutlineStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1));
            }
            else
            {
                OutlineStatus.Text = $"生成失败: {result.Error}";
            }
        }
        catch (Exception ex)
        {
            OutlineStatus.Text = $"错误: {ex.Message}";
        }
        finally { NextBtn.IsEnabled = true; }
    }

    private async Task FinalizeSetup()
    {
        await using var scope = NovelWriterApp.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelWriterDbContext>();

        var project = await db.Projects.FindAsync([_projectId], _cts.Token);
        if (project != null)
        {
            project.Genre = ((ComboBoxItem)GenreCombo.SelectedItem).Content.ToString();
            project.Status = ProjectStatus.Active;
            await db.SaveChangesAsync(_cts.Token);
        }

        SetupCompleted = true;
        DialogResult = true;
        Close();
    }

    private void Prev_Click(object sender, RoutedEventArgs e)
    {
        switch (_step)
        {
            case 2:
                Panel2.Visibility = Visibility.Collapsed;
                Panel1.Visibility = Visibility.Visible;
                SetStepHighlight(1);
                NextBtn.Content = "生成梗概";
                PrevBtn.Visibility = Visibility.Collapsed;
                break;
            case 3:
                Panel3.Visibility = Visibility.Collapsed;
                Panel2.Visibility = Visibility.Visible;
                SetStepHighlight(2);
                NextBtn.Content = "确认梗概，生成大纲";
                break;
        }
    }

    private void SetStepHighlight(int step)
    {
        _step = step;
        var blue = new SolidColorBrush(Color.FromRgb(0x89, 0xB4, 0xFA));
        var gray = new SolidColorBrush(Color.FromRgb(0x6C, 0x70, 0x86));
        Step1Text.Foreground = step >= 1 ? blue : gray;
        Step2Text.Foreground = step >= 2 ? blue : gray;
        Step3Text.Foreground = step >= 3 ? blue : gray;
        Step4Text.Foreground = step >= 4 ? blue : gray;
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts.Cancel(); _cts.Dispose();
        base.OnClosed(e);
    }
}

public class OutlineItem
{
    public string ChapterText { get; init; } = "";
    public string Title { get; init; } = "";
    public string Scene { get; init; } = "";
}
