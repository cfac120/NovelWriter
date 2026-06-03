namespace NovelWriter.Core.ValueObjects;

public record ProjectId(Guid Value)
{
    public static ProjectId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("N")[..8];
}
