namespace NovelWriter.Core.ValueObjects;

/// <summary>FS_{NNN}</summary>
public record ForeshadowingId(int Number)
{
    public override string ToString() => $"FS_{Number:D3}";
}
