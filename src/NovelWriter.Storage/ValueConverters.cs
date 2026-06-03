using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NovelWriter.Core.ValueObjects;

namespace NovelWriter.Storage;

public static class ValueConverters
{
    public static readonly ValueConverter<ProjectId, Guid> ProjectIdConverter =
        new(v => v.Value, v => new ProjectId(v));

    public static readonly ValueConverter<ChapterId, Guid> ChapterIdConverter =
        new(v => v.Value, v => new ChapterId(v));

    public static readonly ValueConverter<CharacterId, int> CharacterIdConverter =
        new(v => v.Number, v => new CharacterId(v));

    public static readonly ValueConverter<WorldSettingId, int> WorldSettingIdConverter =
        new(v => v.Number, v => new WorldSettingId(v));

    public static readonly ValueConverter<ForeshadowingId, int> ForeshadowingIdConverter =
        new(v => v.Number, v => new ForeshadowingId(v));

    public static readonly ValueConverter<ArcId, int> ArcIdConverter =
        new(v => v.Number, v => new ArcId(v));

    public static readonly ValueComparer<ProjectId> ProjectIdComparer =
        new((a, b) => a != null && b != null && a.Value == b.Value, v => v.Value.GetHashCode());

    public static readonly ValueComparer<ChapterId> ChapterIdComparer =
        new((a, b) => a != null && b != null && a.Value == b.Value, v => v.Value.GetHashCode());

    public static readonly ValueComparer<CharacterId> CharacterIdComparer =
        new((a, b) => a != null && b != null && a.Number == b.Number, v => v.Number.GetHashCode());

    public static readonly ValueComparer<WorldSettingId> WorldSettingIdComparer =
        new((a, b) => a != null && b != null && a.Number == b.Number, v => v.Number.GetHashCode());

    public static readonly ValueComparer<ForeshadowingId> ForeshadowingIdComparer =
        new((a, b) => a != null && b != null && a.Number == b.Number, v => v.Number.GetHashCode());

    public static readonly ValueComparer<ArcId> ArcIdComparer =
        new((a, b) => a != null && b != null && a.Number == b.Number, v => v.Number.GetHashCode());
}
