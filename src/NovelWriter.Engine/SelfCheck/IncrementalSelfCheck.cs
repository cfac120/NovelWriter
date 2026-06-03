using NovelWriter.Core.Dtos;
using NovelWriter.Core.Interfaces;
using NovelWriter.Core.ValueObjects;

namespace NovelWriter.Engine.SelfCheck;

/// <summary>
/// 增量 SelfCheck。触发条件: 章节号 % 5 == 0。
/// 在 MemoryManagerAgent 的 LLM 调用中检测 traits.forbidden 违规。
/// </summary>
public class IncrementalSelfCheck
{
    /// <summary>
    /// 从记忆提取结果中提取 forbidden 违规。
    /// </summary>
    public List<Deviation> CheckForForbiddenViolations(MemoryExtractionResult extraction)
    {
        return extraction.ForbiddenTraitViolations ?? [];
    }

    /// <summary>
    /// 判断是否应该触发增量检查。
    /// </summary>
    public static bool ShouldCheck(int chapterNumber) => chapterNumber % 5 == 0;
}
