namespace NovelWriter.Core.ValueObjects;

public record ReviewScore(double Value)
{
    public static ReviewScore Zero => new(0);
    public bool IsPassing => Value >= 6.0;
    public bool IsExcellent => Value >= 8.5;
}
