# NovelWriter — AI 网文写作助手设计文档

## 概述

NovelWriter 是一款 Windows 桌面端创作效率工具，通过 AI 辅助消除写作中的非创意摩擦（记忆追踪、草稿扩写、一致性检查），同时确保核心创意决策始终由作者掌控。采用 WPF 单体架构 + 国产大模型 API + SQLite 本地存储。

**产品定位**：NovelWriter 是**创作助手**，不是内容工厂。AI 负责跟踪 100 章中每一个伏笔的角色数据，人类负责决定主角的生死。输出产物是高质量草稿——经过三层记忆验证、六维度评审和质量预检的可修改基础，最终的发布权、修改权和署名权完全属于作者。

**设计原则**：
- 消除写作摩擦力，不替代创作者
- 数据完全本地化，不上传作品至任何云端
- 遵循《人工智能生成合成内容标识办法》，内置 AI 检测预检

## 目录

- [概述](#概述)
- [核心决策汇总](#核心决策汇总)
- [分阶段交付](#分阶段交付)
- [项目结构](#项目结构)
- [技术栈](#技术栈)
- [数据模型](#数据模型)
- [三层记忆架构](#三层记忆架构)
- [流水线架构](#流水线架构)
- [子Agent 评审系统](#子agent-评审系统)
- [UI 布局](#ui-布局)
- [错误处理](#错误处理)
- [测试策略](#测试策略)

## 核心决策汇总

| 决策项 | 选择 |
|--------|------|
| 产品定位 | **创作助手**：AI 做记忆/草稿/检查，人类做创意/决策/定稿 |
| 桌面框架 | .NET 9 + WPF (MVVM) |
| AI 引擎 | 国产大模型 (deepseek-v4-pro 默认 / 通义千问 / Kimi) |
| 模型策略 | 适配器模式支持多模型，可降级切换，流式输出支持 |
| 数据存储 | SQLite (EF Core)，完全本地，不上传作品 |
| 人类干预 | 关键节点确认模式 (题材/梗概/大纲) + 记忆变更确认闸门 |
| 交互界面 | 三栏布局 (导航树 / 编辑器 / 上下文面板) |
| 合规 | 内置 AI 检测预检，遵循《标识办法》 |
| L3 检索策略 | ID 直查（大纲 CharacterIds/SettingIds）+ 关键词补充，精简注入 ≤40K token |
| 记忆提取 | 拆分为 2 次 LLM 调用（Call1=摘要生成，Call2=结构检测），提升可靠性 |
| 技术验证 | 全面开发前先做 30 章规模记忆一致性 PoC |

## 分阶段交付

### Phase 1 — MVP (核心创作闭环)
- 题材选择推荐
- 故事梗概生成
- 分章大纲生成
- 逐章 AI 写作 (含三层记忆架构)
- 随机风格注入与插曲系统 (打破章间结构同质化，降低 AI 检测风险)
- 子Agent 读者评审 (6种 Persona)
- 评审后润色修改
- **AI 检测预检** (章节定稿前跑检测，标注风险等级，确保输出可通过平台审核)

### Phase 2 — 数据驱动增强
- 平台热榜数据采集
- 趋势分析与选材建议
- 读者评论抓取与分析
- 反馈驱动的动态情节调整

> 详细设计见 [技术架构方案 §5.8](2026-05-29-novelwriter-tech-architecture.md)（网文平台数据采集）。

### Phase 3 — 发布与分发
- 多平台格式化排版
- AI 合规检查 (符合各平台最新 AI 政策)
- 一键发布/定时发布
- 全平台数据追踪面板

> 详细设计见技术架构方案 Stage08-09。

### Phase 与流水线阶段对应

| Phase | 涵盖的流水线阶段 | 职责 |
|-------|-----------------|------|
| Phase 1 (MVP) | 阶段 1-7 | 从选题到逐章写作的完整创作闭环 |
| Phase 2 | 阶段 8 | 数据采集、趋势分析、反馈驱动调整 |
| Phase 3 | 阶段 9 | 格式化排版、多平台发布、数据追踪 |

## 项目结构

```
NovelWriter/
├── NovelWriter.App/          # WPF 主工程 (Views, ViewModels)
├── NovelWriter.Core/         # 核心领域模型和接口
├── NovelWriter.Engine/       # 流水线编排、AI调用、子Agent
├── NovelWriter.Storage/      # EF Core DbContext、SQLite 存储
└── NovelWriter.Tests/        # 单元测试和集成测试
```

## 技术栈

| 用途 | 库 |
|------|-----|
| MVVM | CommunityToolkit.Mvvm |
| 数据库 | Microsoft.EntityFrameworkCore.Sqlite |
| UI 控件 | MaterialDesignInXamlToolkit |
| 文本处理 | Markdig (Markdown 渲染) |
| 测试 | xUnit + Moq |

## 数据模型

### 核心实体

- **Project** — 一本书。Title, Genre, Status, TargetWordCount, CreatedAt
- **Outline** — 大纲节点。VolumeNumber, ChapterNumber, Title, Summary, KeyConflict, CharacterIds, SettingIds, Status (1:1 → Chapter)
- **Chapter** — 章节。Content (Markdown), WordCount, Version, Status, DraftNumber (支持多版本)
- **Synopsis** — 梗概/设定摘要。Type (梗概/人物/世界观), Content, Version (N:1 → Project)。注：Synopsis 是阶段 1-3 产出物的序列化存储容器，用于存储梗概/设定摘要原始产出物；L3 的 CharacterProfile 和 WorldSetting 使用独立数据库表（`CharacterProfiles` / `WorldSettings`）存储，写前编译时从表中查询最新版本序列化为结构化 YAML 再注入 ContextWindow。
- **Review** — 评审记录。ReviewerPersona, Scores, Strengths, Weaknesses, Suggestions (N:1 → Chapter)
- **Persona** — 读者画像模板。Name, AgeGroup, Preferences, CritiqueStyle, SystemPrompt, ScoreWeights
- **StyleProfile** — 写作风格档案。SourceTitle, SourceAuthor, ProfileJson, Tags (用于随机风格注入，打破章间结构同质化)
- **InterludeEntry** — 插曲条目。SourceType, CoreFact, NarrativeHook, AdaptableThemes (用于随机插曲注入，引入"结构呼吸感")

### 三层记忆实体

> 记忆实体在数据库中通过 Synopsis（L3）和专用表（L2）持久化，此处仅定义注入 ContextWindow 时的序列化格式。格式设计原则：**LLM 解析优先，人类可读为辅** — 使用固定分隔符、一致的键值模式、明确的层级标识。

#### L3 实体（全书记忆，持久化）

**CharacterProfile（人物档案）** — 结构化快照，不依赖叙事性描述：

```yaml
[CHARACTER_PROFILE:v{N}]           # v{N}=版本号，锁定后不可原地修改，每次变更生成新行
id: CHAR_{NNN}
name: {角色名}
version: {N}
locked_since_chapter: {N}          # 锁定后在此章之后未再修改
first_appearance_chapter: {N}
traits:
  primary: [{核心特质1}, {核心特质2}, ...]      # 贯穿全文的稳定性格
  secondary: [{次要特质1}, ...]                # 情境性表现，非核心
  forbidden: [{禁止特质}, ...]                 # 明确禁止 LLM 赋予的特质，防止角色漂移
background: |
  {一句话出身+关键经历，2-3行，非叙事性}
abilities:
  - name: {能力/技能名}
    level: {熟练度等级}
    acquired_chapter: {N}
    notes: {掌握程度或限制条件，可空}
relationships:
  - target: CHAR_{NNN}
    name: {关联角色名}
    relation: {关系类型，如 师徒/道侣/仇敌/盟友/潜在对立}
    dynamic: {关系动态描述，含冲突或张力方向}
    last_significant_interaction_chapter: {N}
arc_summary: |
  {角色成长弧线的一句话总结。从状态A→状态B。当前处于哪个阶段。}
```

**WorldSetting（世界观条目）**：

```yaml
[WORLD_SETTING:v{N}]
id: WORLD_{NNN}
name: {设定名称}
category: {分类: 地域/势力/功法体系/历史事件/种族/物品/规则}
version: {N}
locked: {true|false}               # 阶段3后锁定为true
global: {true|false}               # global=true 的设定不受剧透过滤限制，适用于核心世界观规则
rules:
  - {规则1: 一句话，可量化的约束条件}
  - {规则2}
history: |
  {该设定的背景，2-4行}
related_characters: [CHAR_{NNN}, ...]
related_chapters: [{N}, ...]
tags: [{标签1}, {标签2}]           # 用于检索匹配的关键词
```

#### L2 实体（卷/弧记忆，持久化）

**Foreshadowing（伏笔追踪）**：

```yaml
[FORESHADOW:v1]
id: FS_{NNN}
description: {伏笔内容的一句话描述，包含谁+什么事+暗示了什么}
planted_chapter: {N}
planted_by: {manual|auto_detected}           # manual=作者标记 | auto_detected=Agent检测(需人工确认)
expected_reveal_chapter_range: [{N}, {M}]    # 预期回收窗口 [起始章, 结束章]
status: {active|resolved|abandoned}
resolution_chapter: {N|null}
resolution_note: {回收方式的一句话描述|null}
related_characters: [CHAR_{NNN}, ...]
related_settings: [WORLD_{NNN}, ...]
tags: [{标签}, ...]
priority: {high|low}                         # high=主线相关，必须回收 | low=支线彩蛋，可放弃
```

**ArcTracker（故事弧线）**：

```yaml
[ARC:v1]
id: ARC_{NNN}
name: {弧线名称，如"主角的身世揭秘"}
character_id: CHAR_{NNN}
goal: {弧线目标的一句话描述}
milestones:
  - chapter: {N}
    event: {里程碑事件描述}
    status: {completed|in_progress|pending}
start_chapter: {N}
expected_end_chapter: {N}
progress_summary: {当前进展的一句话总结}
```

**SubplotTracker（支线状态）**：

```yaml
[SUBPLOT:v1]
id: SUB_{NNN}
name: {支线名称}
description: {支线的一句话描述}
status: {active|resolved|abandoned}
last_referenced_chapter: {N}
dangling_since: {N}              # 连续未提及的章数，超过阈值则提示作者是否放弃此线
```

#### L1 实体（即时记忆，ChapterSummary 持久化，ChapterContext 不持久化）

**ChapterContext（写作上下文）** — 每次写新章时由 Memory Manager Agent 编译，不持久化到数据库：

```yaml
[L1_CONTEXT]
compiled_for_chapter: {N}
compiled_at: {ISO8601时间戳}

recent_summaries:                 # 最近5章摘要，每章≤500字
  - chapter: {N}
    summary: |
      {本章关键情节的一段话摘要，400-600字}
    key_events: [{事件1}, {事件2}, ...]   # 本章标志性事件，用于 L2 更新匹配
    word_count: {N}

  - chapter: {N-1}
    summary: |
      ...
    key_events: [...]
    word_count: {N}

  # ... 最多5条，按时间倒序

current_scene_state:
  location: {当前场景位置}
  present_characters: [CHAR_{NNN}, ...]   # 当前场景中出场的角色
  time: {故事内时间描述}
  scene_mood: {场景氛围标签}
  pending_conflicts:                      # 由上一章遗留、本章需要处理的未完成冲突
    - {冲突描述1}
    - {冲突描述2}
```

### 记忆实体存储说明

#### L2→L3 压缩归档实体

以下两个实体仅在 L2→L3 卷级压缩时生成，使用独立数据库表（`ForeshadowingArchives` / `VolumeSummaries`）持久化：

**ForeshadowingArchive（伏笔归档）** — 从 L2 移入的已回收/已放弃伏笔：

```yaml
[FORESHADOW_ARCHIVE:v1]
id: FS_ARCHIVE_VOL{N}            # 按卷分组，N=卷号
volume_number: {N}
archived_items:
  - fs_id: FS_{NNN}
    description: {伏笔描述}
    planted_chapter: {N}
    resolution_chapter: {N}
    resolution_note: {回收方式}
    priority: {high|low}
    status: {resolved|abandoned}
    archived_at: {ISO8601}
```

**VolumeSummary（已完结卷摘要）**：

```yaml
[VOLUME_SUMMARY:v1]
id: VOL_SUMMARY_{N}
volume_number: {N}
chapter_range: [{N}, {M}]
core_conflict: {本卷核心冲突的一句话总结}
key_outcomes:
  - {本卷关键结果1}
  - {本卷关键结果2}
character_arcs_progress:
  - char_id: CHAR_{NNN}
    arc_summary: {该卷中此角色的弧线进展}
foreshadowing_stats:
  total_planted: {N}
  resolved: {N}
  abandoned: {N}
  carried_over: {N}              # 跨卷保留到下一卷的 active 伏笔数
```

- L1 实体：ChapterSummary（章节摘要）持久化到 SQLite 的 `ChapterSummaries` 表，用于应用重启后恢复摘要窗口；ChapterContext（写作上下文编译产物）仅存在于内存中，不持久化
- L2 实体：持久化到 SQLite 专用表（Foreshadowing / ArcTracker / SubplotTracker），每写入一条生成一个版本记录
- L3 实体：CharacterProfile 和 WorldSetting 持久化到 SQLite 独立表（`CharacterProfiles` / `WorldSettings`），每次修改生成**新版本行**（不可原地覆盖），检索时取最新版本；支持按 tags、related_characters 建立索引
- L3 归档实体：ForeshadowingArchive 和 VolumeSummary 持久化到 SQLite 独立表（`ForeshadowingArchives` / `VolumeSummaries`），在每卷压缩时生成
- 序列化：所有实体在注入 ContextWindow 前按上述 YAML 模板序列化，LLM 可精确区分字段边界

### 评审实体

- **Review** — 评审记录。ReviewerPersona, Scores (JSON), Strengths, Weaknesses, Suggestions, Flagged (N:1 → Chapter)
- **Persona** — 读者画像模板。Name, AgeGroup, Preferences, CritiqueStyle, SystemPrompt, ScoreWeights

## 三层记忆架构

### 设计前提

- **LLM 上下文窗口**: DeepSeek V4 标称 1M token，实际可用按 700K 计
- **核心取舍**: 放宽记忆 token 预算换取记忆提取质量 — 宁可多花 token 保证准确，不要因压缩过度引入偏差
- **格式原则**: 所有记忆文件采用固定模板 + 统一分隔符，LLM 可精确解析字段边界；人类阅读是附带收益，不作为格式设计的约束条件

### Token 预算分配

```
写一章时的 ContextWindow 构成 (目标 ≤ 160K token，留 570K+ 给写作):

┌───────────────────────────────────────────┐
│ L3 检索注入   ≤ 40K token (按需检索)       │
│ L2 全量注入   ≤ 50K token (全卷记忆)       │
│ L1 全量注入   ≤ 25K token (5章摘要+状态)   │
│ 写作系统指令  ≤ 15K token                  │
│ 正文写作空间  ≥ 570K token                 │
└───────────────────────────────────────────┘
```

| 层级 | 单次注入上限 | 持久化 | 增长速度 | 压缩触发 |
|------|-------------|--------|---------|---------|
| L1 | 25K token | ChapterSummary 持久化 | 每章 ~5K token | 满 5 章窗口自动滚动 |
| L2 | 50K token | 是 | 每章 ~1-2K token | 每卷结束压缩进 L3 |
| L3 | 40K token (检索注入) | 是 | 每卷 ~5K token | 不定，按检索相关性裁剪 |
| 正文空间 | 570K+ token | 是 (章节存 DB) | — | — |

### L1 — 即时记忆

**内容**: 最近 5 章的结构化摘要 + 当前场景状态快照

**写入者**: Memory Manager Agent（每次写新章前自动编译）
**写入时机**: 第 N 章定稿后，编译 L1(N+1) 供下一章使用
**写入方式**: 读取第 N 章正文 → 提取摘要和状态 → 写入 L1 缓存；旧摘要超出 5 章窗口后直接丢弃

**上限**: ≤ 25K token（≈ 12,000 中文字）

**关键约束**:
- 每章摘要固定 400-600 字，由 Agent 从正文提取，不依赖写作 LLM 自行总结
- 摘要字段必须包含 `key_events` 列表 — 这是 L2 ArcTracker 更新和新伏笔检测的输入
- `current_scene_state.pending_conflicts` 是写作 LLM 必须在本章处理或至少提及的条目

### L2 — 卷/弧记忆

**内容**: 活跃伏笔 / 人物弧线 / 支线状态 / 本卷核心冲突

**写入者**:
| 实体.字段 | 写入者 | 确认机制 |
|----------|--------|---------|
| Foreshadowing.planted_by | 作者手动标记，或 Agent 检测+人工确认 | Agent 检测到的伏笔标记 `auto_detected`，作者不确认则不入库 |
| Foreshadowing.resolution_chapter + resolution_note | Agent 检测 | 高置信度自动标记；低置信度（疑似回收）需人工确认 |
| ArcTracker.milestones | Agent 自动更新进度 | 里程碑完成不需要确认；新增里程碑需要 |
| SubplotTracker.dangling_since + status | Agent 自动更新 | `dangling_since` 超过 10 章阈值时通知作者，由作者决定保留或放弃 |

**写入时机**: 每章定稿后，Memory Manager Agent 读取 L1 摘要 + 本章正文 → 对比 L2 现有状态 → 生成变更建议 → 人工确认关键项 → 写入

**上限**: ≤ 50K token（≈ 25,000 中文字）— 足够容纳 20 条伏笔 + 10 条弧线 + 5 条支线 + 卷级上下文

**每卷结束压缩规则**:
1. `status=resolved` 的伏笔 → 移入 L3 伏笔归档，从 L2 删除
2. `status=abandoned` 的伏笔 → 如果标记为 `priority=low`，直接归档；如果 `priority=high`，**必须**作者确认才能放弃
3. `status=active` 的跨卷伏笔 → 保留在 L2 中
4. 人物弧线 → 压缩为卷级进度摘要，追加到对应 L3 人物档案的 `arc_summary`
5. 卷核心冲突 → 写入 L3 已完结卷摘要
6. 清空后 L2 为新卷预留至少 35K token 空间

### L3 — 全书记忆

**内容**: 人物档案 / 世界观设定库 / 已完结卷摘要 / 伏笔归档

**写入阶段**: 阶段 1-3 由人工 + Agent 共同构建；写作阶段（阶段 4-7）追加不修改
**修改策略**: 追加式，不可原地覆盖
- 人物档案以 `[CHARACTER_PROFILE:vN]` 标识，每次修改生成新版本行，旧版本保留
- `locked_since_chapter` 字段标记锁定时间点；锁定后任何修改都需要作者显式确认
- `traits.forbidden` 字段显式声明"角色不应该有哪些特质"，防止 LLM 在长文中漂移
  - **设计依据**: 负面约束比正面描述更有效地防止角色漂移（Octopus 论文验证：减少 62% 上下文矛盾）。System Prompt 组装时，`traits.forbidden` 应排列在 `traits.primary` 之前，优先注入约束边界。

**上限**: 数据库层无上限；注入 ContextWindow 时 ≤ 40K token

**检索策略**（全文注入不可行，必须按需检索）:
1. **ID 直查（主路径）**: 大纲节点已包含 `CharacterIds` 和 `SettingIds`，直接用这些 ID 查询 L3 对应的 CharacterProfile 和 WorldSetting 最新版本，确保大纲规划涉及的设定不会遗漏
2. **关键词补充检索（辅路径）**: 从大纲文本中提取关键词（人物别名、地名、概念词），匹配 L3 中的 `tags`、`aliases` 字段，发现大纲未显式关联但语义相关的条目
3. 合并两路结果，去重，按相关性排序，截断到 40K token
4. 检索结果缓存到 L1 编译产物中，同一章重复生成时复用
5. 新增一条 L3 条目后，触发一次增量检索，更新当前缓存
6. **剧透过滤**: 检索结果排除 `related_chapters` 中存在 > 当前章节号 N 的条目（即仅返回所有 related_chapters 均 ≤ N 的设定）。未来设定被屏蔽而非降权，防止 LLM "预知"后续剧情。
   - 例外：阶段 3 大纲中明确标注跨卷的全局设定（如核心世界观规则），标记 `global: true`，不受剧透过滤限制

> **设计决策**: 精简 L3 注入预算从 80K 到 40K，基于 "Lost in the Middle" 研究——LLM 对长上下文中间部分的注意力显著低于首尾。宁可少注入高相关条目确保被模型有效利用，也不要大量注入低相关条目引入噪声。关键约束（如 `traits.forbidden`）在 System Prompt 和 User Message 中重复出现以提高召回率。

### 记忆写入管道（新增 — 原设计缺失）

这是整个记忆系统最关键的部分：**谁在什么时候往记忆里写什么、需要什么确认、出错了怎么发现。**

```
每章写作流程中的记忆管线:

1. 写前准备 (Memory Manager Agent)
   ├── 编译 L1: 汇总最近5章摘要 + 当前场景状态
   ├── 全量注入 L2: 当前卷全部伏笔/弧线/支线
   ├── 按需检索 L3: ID 直查 + 关键词补充 → 注入相关条目 (≤40K token)
   └── 组装 System Prompt，token 计数器检查预算

2. 章节生成 (Writing LLM，流式输出)
   ├── 输入: System Prompt (L1+L2+L3 检索结果+风格约束) + 大纲中本章规划
   ├── 输出: 章节正文 (Markdown)，流式返回逐步渲染
   └── 写作 LLM 不负责记忆管理 — 它只管写，不管记

3. 写后记忆提取 (Memory Manager Agent，2 次独立 LLM 调用)
   ├── Call 1: 摘要生成 (任务A)
   │   ├── 输入: 本章正文 + 本章大纲
   │   ├── 任务A: 生成本章 L1 摘要（400-600字 + key_events + scene_state）
   │   └── 输出: ChapterSummary (JSON) — 这是 L1 的基础，必须高可靠
   ├── Call 2: 结构检测 (任务B-F + SelfCheck)
   │   ├── 输入: 本章正文 + 大纲 + Call1 摘要的 key_events + 当前 L2 状态
   │   ├── 任务B: 对比本章内容与 L2 活跃伏笔 → 检测是否回收
   │   ├── 任务C: 从本章中检测潜在新伏笔 → 标记为 auto_detected
   │   ├── 任务D: 检查 ArcTracker milestones 是否达成
   │   ├── 任务E: 更新 SubplotTracker（是否提及、dangling_since 计数）
   │   ├── 任务F: 检查人物关系是否有显著变化 → 生成 L3 更新建议
   │   └── 输出: 结构化变更建议（JSON），字段对应 L2/L3 实体
   └── 拆分理由: 摘要生成和结构检测是异构任务，拆分后每次调用
       JSON schema 更简单、遵守率更高、可独立验证输出质量

4. 人工确认闸门
   ├── 自动通过: L1 摘要写入、ArcTracker milestone 进度更新、SubplotTracker 计数
   ├── 需要确认: 新伏笔（auto_detected）、伏笔回收（低置信度）、人物关系变更、
   │            traits 修改、世界观新增条目
   └── 确认后: 写入 L2/L3 数据库，生成版本记录

5. 评审-润色循环
   ├── 子Agent 评审后触发的修改，同样经过步骤 3-4 重新提取记忆
   └── 同一章多次润色时，以最后一次确认后的记忆为准
```

### 伏笔处理的防偏差设计

LLM 自动检测伏笔的核心问题是：**它不知道什么是"真正的伏笔"和"闲笔"。** 解决策略：

1. **双通道伏笔标记**:
   - 作者通道：写作时手动标记（编辑器内快捷键/标记语法 `[FS:描述]`）→ `planted_by: manual`，直接入库
   - Agent 通道：每章写完后 Agent 扫描检测 → `planted_by: auto_detected`，存入待确认队列

2. **伏笔回收的双阈值判断**:
   - Agent 对比本章内容与 L2 伏笔列表，输出置信度（高/低）
   - 高置信度 → 自动标记为 resolved，记录 `resolution_chapter`
   - 低置信度 → 推送给作者确认，"以下伏笔疑似在本章被回收，请确认"

3. **优先级分级**:
   - `priority: high` — 主线相关伏笔，必须回收，不可无声放弃
   - `priority: low` — 支线彩蛋，可放弃，不回收不影响主线

4. **定期审计**:
   - 每卷结束时，Memory Manager 输出一份"伏笔健康报告"：high 优先级伏笔中还有多少未回收、哪些即将超过预期回收窗口
   - 超过 `expected_reveal_chapter_range` 上限仍未回收的 high 优先级伏笔 → 强提示作者处理

### L1→L2→L3 压缩管道

```
L1 滚动淘汰
  5章窗口 → 第6章写入时,最旧的第1章摘要被丢弃
  (摘要中已通过的 key_events 已在步骤3中写入 L2/L3,丢弃是安全的)

L2 → L3 卷级压缩 (每卷结束时触发)
  触发条件: 当前卷最后一章定稿且 Review 通过
  执行者: Memory Manager Agent
  流程:
    1. 遍历 L2 所有实体,按状态分流:
       resolved/abandoned 伏笔 → L3 foreshadowing_archive
       完成的 Arc → 压缩摘要追加到 L3 CharacterProfile.arc_summary
       卷冲突 → L3 completed_volumes 新增一条
    2. 保留在 L2 的: 跨卷 active 伏笔、未完成弧线
    3. 验证压缩后 L2 ≤ 15K token (L2 总预算 50K token，为新卷留 35K 空间，见 Token 预算分配表)
    4. 输出压缩报告给作者
```

### SelfCheck：记忆偏差检测

SelfCheck 分两级运行，兼顾检测及时性和成本：

**增量检查（轻量，每 5 章一次）**:
```
触发条件: 章节号 % 5 == 0
执行者: Memory Manager Agent (复用记忆提取 LLM)
流程:
  1. 仅检查 traits.forbidden 违规（最高频失败模式）
  2. 遍历最近 5 章正文，关键词匹配 forbidden 列表
  3. 无违规 → 静默通过
  4. 有违规 → 记录 LOW/MEDIUM 偏差，不阻塞写作，累积到卷末报告
设计依据: ComoRAG (AAAI 2026) 证明实时检测优于批量检测
```

**全量检查（完整，每卷一次）**:
```
触发条件: 每卷最后一章定稿后、L2→L3 压缩前
执行者: SelfCheckRunner (独立 LLM 调用)
流程:
  1. 全量遍历 L3 中所有人物和世界观设定（非随机采样）
  2. 遍历全卷章节，逐条检查:
     - traits.forbidden 是否在正文中出现
     - 人物关系描述与 L3 是否一致
     - 世界观规则是否在正文中被违反
     - 伏笔回收状态与实际正文是否一致
  3. 输出偏差报告，标注严重程度 + 合并增量检查累积的违规记录
  4. 高严重度 → 暂停写作，作者决定是修正正文还是更新设定
```

SelfCheck 增量检查不阻塞写作流程；全量检查的高严重度偏差阻塞压缩流程。

## 流水线架构

### 阶段 1-3: 人工确认阶段（L3/L2 初始化）

这三个阶段是全书记忆的**地基**。L3 的准确度决定了后续 100+ 章写作的一致性。每阶段产物均使用结构化模板输出，不依赖 LLM 自由文本。

```
阶段1: 选题材 (Writing LLM + 用户确认)
  输入: 用户偏好 (类型/风格/目标读者)
  输出: 题材+切入点 (结构化: 核心冲突一句话 + 目标读者画像 + 差异化卖点)
  Agent 辅助: 检索同类作品世界观规模参考
  → 创建 L3: WorldSetting 初稿 (3-5条核心规则 + 势力框架)
    所有条目标记 version:1, locked:false
  🔴 审核点: 用户确认题材+世界观框架

阶段2: 写梗概 (Writing LLM + 用户确认)
  输入: 题材 + L3 WorldSetting
  输出: 300-500字梗概 (结构化: 开端/发展/转折/高潮/结局 五段式)
  读L3: WorldSetting 当前版本
  → 创建 L3: CharacterProfile (主角+核心配角, 每人含 traits.primary + traits.forbidden)
    所有条目标记 version:1, locked:false
  🔴 审核点: 用户确认梗概+人物设定

阶段3: 写大纲 (Writing LLM + Agent 辅助 + 用户确认)
  输入: 梗概 + L3 全部设定
  输出: 分卷分章大纲 (每章含 标题+核心冲突+关键事件+涉及人物列表+涉及设定列表)
  读L3: WorldSetting + CharacterProfile
  → 创建 L2: ArcTracker (每条弧线含 milestones, 每里程碑对应一个章节)
       Foreshadowing (作者在此阶段手动标记初始伏笔, planted_by:manual)
       SubplotTracker (从大纲中提取支线，`dangling_since` 初始值为 0)
  Agent 辅助: 检查大纲中的一致性 — 人物首次出场与弧线起点是否对齐，伏笔是否有预期回收章
  🔴 审核点: 用户确认大纲
  → 确认后: 所有 L3 条目锁定（CharacterProfile 以 locked_since_chapter 记录锁定时间，WorldSetting 以 locked:true 标记），此后的修改走版本化追加流程
```

### 阶段 4-7: 自动写作循环（含记忆管线、风格注入、插曲注入）

阶段 4-7 构成每章的循环体，按职责划分为四个子阶段。同一套循环逻辑应用于每一章，直到全书完成。

> 风格注入与插曲注入的详细设计见 [技术架构方案 §5.10](2026-05-29-novelwriter-tech-architecture.md)。

```
阶段4 (写前准备): ContextWindow 编译、随机风格选择、随机插曲改编与 Prompt 组装
阶段5 (章节生成): LLM 流式生成正文
阶段6 (记忆提取): Memory Manager Agent 2 次调用更新 L1/L2/L3
阶段7 (评审润色): 子Agent 评审 → 触发修改或通过

for each 章节:
  # === 写前准备 (Memory Manager Agent + Style/Interlude) ===
  a) 编译 L1: 汇总最近5章摘要 + 当前场景状态快照
  b) 注入 L2: 当前卷全部伏笔/弧线/支线 (≤50K token)
  c) 检索 L3: ID 直查 + 关键词补充 → 注入相关条目 (≤40K token)
  d) 随机选择写作风格 → System Prompt 追加 ~400 token 风格约束
  e) 随机选择插曲 → LLM 改编为 ~100 字故事闲笔 → 追加到 User Message
  f) 组装 System Prompt: L1+L2+L3检索结果 + 风格约束 + 章节大纲 + 写作指令 (≤160K token 总计)
  g) User Message 中重复注入关键约束 (traits.forbidden + 本章相关关键设定)

  # === 章节生成 (Writing LLM，流式输出，含风格+插曲约束) ===
  h) LLM 流式生成章节初稿 (UI 逐步渲染文本)

  # === 写后记忆提取 (Memory Manager Agent，2 次独立 LLM 调用) ===
  i) Call 1 — 摘要生成: 生成本章 L1 摘要 (400-600字 + key_events + scene_state)
  j) Call 2 — 结构检测:
     - 检测伏笔回收状态 + 新伏笔候选
     - 更新 ArcTracker milestones + SubplotTracker
     - 检查人物关系/设定变更 → 生成 L3 更新建议
     - 输出结构化变更建议 (JSON) → 递交人工确认闸门

  # === 评审与润色 ===
  k) 并行启动 N 个子Agent (不同 Persona) 评审
  l) 综合评分 < 7/10 → 触发润色 → 回到 h)
  m) 润色通过后，Memory Manager 以最终版本为准更新记忆

  # === 卷级检查 ===
  n) 🔴 每卷结束:
     - SelfCheck 偏差检测
     - L2→L3 压缩
     - 伏笔健康报告
     - 可选人工审核点: 作者审阅 SelfCheck 偏差报告和伏笔健康报告，决定是否需要手动干预（修正正文或更新设定）
```

### 阶段 8: 数据驱动增强 (Phase 2)

### 阶段 9: 发布与分发 (Phase 3)

## 子Agent 评审系统

### 6种 Persona

| 角色 | 关注点 | 评分权重 |
|------|--------|----------|
| 爽文党 | 节奏、爽点密度、打脸频率 | 爽感60% 节奏40% |
| 逻辑党 | 设定一致、伏笔回收、行为合理 | 逻辑70% 设定30% |
| 情感党 | 人物弧光、情感共鸣、关系发展 | 情感50% 人物50% |
| 老书虫 | 套路识别、创新度、文笔质量 | 创新40% 文笔60% |
| 追更党 | 章节结尾钩子、悬念设置 | 钩子80% 信息量20% |
| 考据党 | 历史/科学/文化真实性、细节考究 | 考据70% 细节30% |

### 评审流程
1. 章节初稿完成
2. 选择 Persona 组合 (2-5个)
3. 并行调用 LLM 评审 (可用更便宜模型)
4. 结构化 JSON 输出: 分项评分、优缺点、修改建议、连续性错误标记
5. 综合评分 < 阈值触发自动润色，回到步骤 1
6. 评分通过则标记完成

### 结构化评审输出

每个 Persona 使用其评分权重表中定义的评分维度（见上表"评分权重"列），输出结构化 JSON：

```json
{
  "persona": "{角色名}",
  "scores": {
    "{维度1}": {1-10},
    "{维度2}": {1-10}
  },
  "overall": {1-10},
  "strengths": ["{优点1}", "{优点2}"],
  "weaknesses": ["{缺点1}", "{缺点2}"],
  "suggestions": ["{修改建议1}"],
  "flagged": [
    {
      "type": "continuity_error|logic_flaw|character_inconsistency|setting_violation",
      "detail": "{具体问题描述，引用章号和原文}"
    }
  ]
}
```

- `scores` 中的维度名由各 Persona 的评分权重定义决定（如逻辑党使用 `logic` 和 `consistency`，情感党使用 `emotion` 和 `character`），非固定字段
- `overall` 为加权综合分，权重同样来自 Persona 定义
- `flagged` 可为空数组；连续性错误必须明确引用冲突的章号和原文片段

## UI 布局

三栏布局 — 写作 IDE 风格:

- **左栏 (200px)**: 项目导航树 (梗概/大纲/章节/人物/世界观/伏笔)
- **中栏 (弹性)**: 写作编辑器 + 预览模式切换，AI 提示内嵌
- **右栏 (280px)**: ContextWindow 上下文面板 + 读者评审面板 + 流水线状态

底部状态栏: 字数统计、目标进度、版本号、L3 记忆条目数

## 错误处理

### LLM 调用失败
- 自动重试 3 次，指数退避 (1s / 3s / 9s)
- 3 次失败后暂停流水线，通知用户，支持手动重试
- 章节写入前先保存本地草稿

### 多模型降级
- 主模型不可用时自动降级到备用模型
- 评审子Agent 部分失败不阻塞流水线（至少 2 个 Persona 成功返回才计算综合评分，< 2 个成功则暂停流水线）

### 数据一致性
- 持久化操作包裹在事务中
- 定期自动备份 SQLite 数据库

## 测试策略

| 层级 | 范围 | 工具 | 覆盖率目标 |
|------|------|------|------------|
| 技术验证 PoC | 30 章规模记忆一致性验证 | 控制台项目 + 实际 LLM 调用 | 核心路线验证 |
| 单元测试 | Core 领域逻辑、ContextWindow 编译器、评分计算 | xUnit + Moq | 80%+ |
| 集成测试 | Storage 层、LLM 适配器 (mock server) | xUnit | 80%+ |
| UI 测试 | ViewModel 绑定、命令执行 | xUnit + WPF TestHelpers | 尽力 |
| E2E | 完整创作流程 | 手动 | — |

### 技术验证 PoC（开发前置条件）

在投入全面开发之前，必须完成以下 PoC 验证，确认核心技术路线可行：

**目标**: 验证三层记忆架构在实际 LLM 调用下的记忆一致性表现

**方法**:
1. 用独立控制台项目实现 L1/L2/L3 三层记忆的最小化版本
2. 用 LLM 生成一部 20-30 章的短篇小说（含预设伏笔、角色关系变化）
3. 每 5 章检测一次：角色 traits 是否被违反、伏笔状态是否与实际一致、人物关系是否自相矛盾

**通过标准**:
- 20 章规模下，角色核心特质一致性 ≥ 90%
- 伏笔状态准确率 ≥ 85%
- L3 检索召回率 ≥ 80%（注入的相关条目 / 实际应注入的条目）

**未通过时的调整方向**:
- 一致性不达标 → 优化 Prompt 结构、调整记忆注入策略、考虑引入 embedding 语义检索
- 召回率不达标 → 增加 L3 注入预算、改进检索策略、引入向量检索
