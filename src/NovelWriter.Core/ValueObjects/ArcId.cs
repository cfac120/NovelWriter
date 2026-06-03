namespace NovelWriter.Core.ValueObjects;

/// <summary>ARC_{NNN}</summary>
public record ArcId(int Number)
{
    public override string ToString() => $"ARC_{Number:D3}";
}
