namespace NovelWriter.Core.ValueObjects;

public record ChapterId(Guid Value)
{
    public static ChapterId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("N")[..8];
}
