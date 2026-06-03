namespace NovelWriter.Core.ValueObjects;

public record VersionNumber(int Major = 1, int Minor = 0)
{
    public VersionNumber NextMinor() => new(Major, Minor + 1);
    public VersionNumber NextMajor() => new(Major + 1, 0);
    public override string ToString() => $"v{Major}.{Minor}";
}
