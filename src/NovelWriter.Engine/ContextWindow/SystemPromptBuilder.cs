using NovelWriter.Core.Dtos;
using NovelWriter.Core.ValueObjects;

namespace NovelWriter.Engine.ContextWindow;

/// <summary>
/// 系统指令构建器。按注意力从高到低排列 System Prompt 区块。
/// 顺序：角色设定 → traits.forbidden → traits.primary → 世界观 → L2 → L1 → 基调 → 风格 → 写作指令
/// </summary>
public static class SystemPromptBuilder
{
    /// <summary>
    /// 组装写作 System Prompt。按 Lost in the Middle 研究优化区块顺序。
    /// </summary>
    public static string BuildWritingSystemPrompt(
        string l3CharacterSection,
        string l3WorldSettingSection,
        string l2Section,
        string l1Section,
        string toneDirective,
        string styleDirective,
        string writingInstructions)
    {
        var sections = new List<string>();

        sections.Add("你是一位经验丰富的网文作家，擅长创作引人入胜的网络小说。你将根据提供的大纲和记忆上下文，生成高质量的章节正文。");

        if (!string.IsNullOrWhiteSpace(l3CharacterSection))
            sections.Add($"## 人物行为边界\n\n{l3CharacterSection}");

        if (!string.IsNullOrWhiteSpace(l3WorldSettingSection))
            sections.Add($"## 世界观规则\n\n{l3WorldSettingSection}");

        if (!string.IsNullOrWhiteSpace(l2Section))
            sections.Add($"## 当前卷记忆\n\n{l2Section}");

        if (!string.IsNullOrWhiteSpace(l1Section))
            sections.Add($"## 近期章节摘要\n\n{l1Section}");

        if (!string.IsNullOrWhiteSpace(toneDirective))
            sections.Add($"## 全书基调\n\n{toneDirective}");

        if (!string.IsNullOrWhiteSpace(styleDirective))
            sections.Add($"## 写作风格\n\n{styleDirective}");

        if (!string.IsNullOrWhiteSpace(writingInstructions))
            sections.Add($"## 写作指令\n\n{writingInstructions}");

        return string.Join("\n\n---\n\n", sections);
    }

    /// <summary>
    /// 组装记忆提取 Call1（摘要）的 System Prompt。
    /// </summary>
    public static string BuildSummaryExtractionPrompt()
    {
        return """
你是一位专业的文学分析师，擅长从小说正文中提取结构化摘要。你的任务是分析给定的章节正文和大纲，生成一份精炼的章节摘要。

## 输出格式

请严格按以下 JSON 格式输出:

{
  "summary": "400-600字的章节摘要，涵盖关键情节和人物互动",
  "key_events": ["事件1", "事件2", "事件3"],
  "word_count": 0,
  "current_scene_state": {
    "location": "当前场景位置",
    "present_characters": ["CHAR_001", "CHAR_002"],
    "time": "故事内时间描述",
    "scene_mood": "场景氛围标签",
    "pending_conflicts": ["未完成冲突1"]
  }
}

## 要求

- summary: 400-600字，涵盖本章所有重要情节
- key_events: 3-5个标志性事件，简洁描述
- current_scene_state: 章节结束时的场景状态快照
- 严禁编造正文中未出现的信息
- 严格按照 JSON 格式输出，不要添加额外文本
""";
    }

    /// <summary>
    /// 组装记忆提取 Call2（结构检测）的 System Prompt。
    /// </summary>
    public static string BuildStructuralDetectionPrompt()
    {
        return """
你是一位专业的文学分析师，擅长检测小说中的伏笔、人物弧线和支线变化。你的任务是分析章节正文，对比当前记忆状态，输出结构化变更建议。

## 输出格式

请严格按以下 JSON 格式输出:

{
  "taskB_foreshadowing_resolutions": [
    {"foreshadowing_id": "FS_NNN", "confidence": "high", "evidence": "正文中支持回收的证据"}
  ],
  "taskC_new_foreshadowings": [
    {"description": "伏笔描述", "priority": "high", "related_characters": ["CHAR_NNN"], "evidence": "正文依据"}
  ],
  "taskD_arc_updates": [
    {"arc_id": "ARC_NNN", "milestone_reached": "里程碑描述", "new_status": "completed"}
  ],
  "taskE_subplot_updates": [
    {"subplot_name": "支线名", "mentioned": true, "context": "提及上下文"}
  ],
  "taskF_l3_change_proposals": [
    {"target_type": "CharacterProfile", "target_id": "ID", "field": "字段名", "proposed_change": "变更描述", "confidence": "high", "reason": "变更理由"}
  ],
  "selfcheck_forbidden_violations": [
    {"entity_id": "CHAR_NNN", "severity": "critical", "violation": "违规描述", "chapter_quote": "相关原文"}
  ]
}

## 要求

- taskC（新伏笔检测）需极度克制: 只有明确为后续情节埋下悬念/线索的细节才算伏笔，正常的剧情展开、人物对话、动作描写不属于伏笔。如果本章没有真正的新伏笔，taskC_new_foreshadowings 必须输出空数组 []。一个 30 章的卷，10-15 条伏笔是正常范围，每章都有新伏笔意味着你把普通剧情误判为伏笔。
- 严禁编造 ID，所有引用的 FS_/CHAR_/ARC_ 等必须来自提供的记忆列表
- confidence: high=明确证据，low=疑似需人工确认
- 仅输出有实际发现的项目，无发现则输出空数组
- 严格按照 JSON 格式输出
""";
    }

    /// <summary>
    /// 组装 User Message 中的关键约束重复注入区块。
    /// </summary>
    public static string BuildUserMessageConstraintRepeat(
        string forbiddenTraits,
        string keySettingReminders)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(forbiddenTraits))
            parts.Add($"本章关键约束(再次提醒):\n以下行为严禁出现: {forbiddenTraits}");

        if (!string.IsNullOrWhiteSpace(keySettingReminders))
            parts.Add($"本章相关关键设定:\n{keySettingReminders}");

        return parts.Count > 0 ? string.Join("\n\n", parts) : "";
    }
}
