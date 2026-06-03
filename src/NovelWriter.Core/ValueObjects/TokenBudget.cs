namespace NovelWriter.Core.ValueObjects;

public record TokenBudget(int Total, int SystemPrompt, int L1Context, int L2Context, int L3Context, int UserMessage, int Reserved)
{
    public int Used => SystemPrompt + L1Context + L2Context + L3Context + UserMessage + Reserved;
    public int Remaining => Total - Used;

    public double UsagePercent => Total > 0 ? (double)Used / Total * 100 : 0;
}
