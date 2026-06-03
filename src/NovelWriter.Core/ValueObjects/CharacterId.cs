namespace NovelWriter.Core.ValueObjects;

/// <summary>CHAR_{NNN}</summary>
public record CharacterId(int Number)
{
    public override string ToString() => $"CHAR_{Number:D3}";
}
