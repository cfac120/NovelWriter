namespace NovelWriter.Core.ValueObjects;

/// <summary>WORLD_{NNN}</summary>
public record WorldSettingId(int Number)
{
    public override string ToString() => $"WORLD_{Number:D3}";
}
