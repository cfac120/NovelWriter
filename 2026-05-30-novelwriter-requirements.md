# NovelWriter — 需求规格说明书

> 基于 [技术架构方案](2026-05-29-novelwriter-tech-architecture.md) v1.0 提取。

---

## 目录

- [1. 项目概述](#1-项目概述)
  - [1.1 产品定位](#11-产品定位)
  - [1.2 整体架构](#12-整体架构)
  - [1.3 关键设计决策](#13-关键设计决策)
  - [1.4 全局约束](#14-全局约束)
- [2. 功能模块需求](#2-功能模块需求)
  - [2.1 项目管理](#21-项目管理)
  - [2.2 流水线编排](#22-流水线编排)
  - [2.3 题材选择 (Stage01)](#23-题材选择-stage01)
  - [2.4 梗概生成 (Stage02)](#24-梗概生成-stage02)
  - [2.5 大纲生成 (Stage03)](#25-大纲生成-stage03)
  - [2.6 写前准备 (Stage04)](#26-写前准备-stage04)
  - [2.7 章节生成 (Stage05)](#27-章节生成-stage05)
  - [2.8 记忆管理 (Stage06)](#28-记忆管理-stage06)
  - [2.9 人工确认闸门](#29-人工确认闸门)
  - [2.10 子Agent评审 (Stage07)](#210-子agent评审-stage07)
  - [2.11 AI检测预检](#211-ai检测预检)
  - [2.12 SelfCheck偏差检测](#212-selfcheck偏差检测)
  - [2.13 风格库管理](#213-风格库管理)
  - [2.14 插曲库管理](#214-插曲库管理)
  - [2.15 LLM适配层](#215-llm适配层)
  - [2.16 配置与密钥管理](#216-配置与密钥管理)
  - [2.17 备份恢复](#217-备份恢复)
  - [2.18 UI交互](#218-ui交互)
  - [2.19 网文平台数据采集 (Phase 2)](#219-网文平台数据采集-phase-2)
  - [2.20 多平台发布 (Phase 3)](#220-多平台发布-phase-3)
- [附录 A: 优先级说明](#附录-a-优先级说明)
- [附录 B: 需求统计](#附录-b-需求统计)

---

## 1. 项目概述

### 1.1 产品定位

NovelWriter 是一款 **Windows 桌面端 AI 网文写作助手**。AI 负责记忆追踪、草稿扩写、一致性检查；人类掌控核心创意决策。输出产物为高质量草稿，最终修改权和署名权属于作者。

**核心原则**:
- 消除写作摩擦力，不替代创作者
- 数据完全本地化，不上传作品至任何云端
- 遵循《人工智能生成合成内容标识办法》，内置 AI 检测预检

### 1.2 整体架构

```
NovelWriter.App (WPF 表现层)
  → NovelWriter.Engine (业务引擎层: Pipeline / Memory / Review / LLM / Style / SelfCheck)
    → NovelWriter.Core (领域核心层: Entities / ValueObjects / Interfaces / DomainServices)
      ← NovelWriter.Storage (持久化层: EF Core + SQLite)
```

- **分层**: App → Engine → Core ← Storage（依赖倒置，Core 不依赖任何外层）
- **框架**: .NET 9 + WPF (MVVM) + CommunityToolkit.Mvvm
- **数据**: SQLite (EF Core Code-First)，作品完全本地存储
- **AI**: DeepSeek/Qwen/Kimi 三模型适配，OpenAI 兼容 API 格式
- **项目结构**: 5 个项目 — App / Core / Engine / Storage / Tests
- **交付阶段**: Phase 1 MVP (核心创作闭环) → Phase 2 (数据驱动增强) → Phase 3 (发布分发)

### 1.3 关键设计决策

以下设计决策约束所有模块的实现，不得违反：

| ID | 决策 | 约束 |
|----|------|------|
| D-001 | **三层记忆架构 (L1→L2→L3)** | L1=5章滑动窗口即时记忆，L2=卷级伏笔/弧线/支线，L3=全书人物/世界观。记忆写入必须区分自动通过和需人工确认，L3 修改走版本化追加不可原地覆盖 |
| D-002 | **Memory Manager Agent 独立调用，拆分为 2 次** | 记忆提取使用独立 LLM 调用（可与写作 LLM 不同模型）。Call1=摘要生成（任务A），Call2=结构检测（任务B-F + SelfCheck）。不得在写作 LLM 调用中附带记忆管理任务 |
| D-003 | **人工确认闸门** | 阶段 1-3 产出物（题材/梗概/大纲）需用户确认；阶段 6 记忆变更按条目分流：L1摘要/milestone进度自动通过，新伏笔(auto_detected)/低置信度回收/L3变更需逐项确认。确认流程通过 PipelineResult.ConfirmationItems 驱动，不使用异常做控制流 |
| D-004 | **随机风格 + 插曲注入** | 每章写前随机选取风格档案和插曲，分别注入 System Prompt 和 User Message。卷内不重复。可独立开关。不挤占记忆 token 预算（风格 ≤400 token，插曲嵌入 User Message） |
| D-005 | **多模型降级** | 主模型 deepseek-v4-pro → 备选1 qwen-max → 备选2 moonshot-v1-128k。连续3次失败熔断30s自动切换。不同用途可使用不同模型（写作用主模型，评审用便宜模型） |
| D-006 | **YAML 记忆序列化** | L1/L2/L3 实体在注入 ContextWindow 前序列化为 YAML（LLM token 效率优于 JSON）。LLM 输出统一使用 JSON（可 schema 校验），仅记忆持久化用 YAML |
| D-007 | **追加式版本化** | L3 实体（CharacterProfile、WorldSetting）修改时 INSERT 新版本行，旧版本保留。检索取最新版本号。不可原地 UPDATE |
| D-008 | **LLM 流式输出** | 章节写作（Stage05）必须使用流式输出（`StreamChatAsync`），UI 逐步渲染生成文本。记忆提取/评审等结构化输出场景使用非流式 |
| D-009 | **L3 检索策略** | L3 检索采用 ID 直查（大纲 CharacterIds/SettingIds）+ 关键词补充（tags/aliases）双路径。精简注入 ≤40K token，缓解 Lost in the Middle。System Prompt 总预算 ≤160K |
| D-010 | **技术验证 PoC** | 全面开发前必须完成 30 章规模记忆一致性 PoC，通过标准：角色特质一致性≥90%、伏笔状态准确率≥85%、L3 检索召回率≥80% |

### 1.4 全局约束

以下约束适用于所有功能模块：

**数据约束**:
- [ ] 所有用户作品数据存储在本地 SQLite，不得上传至任何云端
- [ ] API Key 通过 Windows DPAPI 加密存储，不得出现在配置文件、日志或错误信息中
- [ ] LLM 调用日志仅记录模型名、耗时、token 用量、成功/失败，不记录请求/响应正文
- [ ] Personas 表首次启动自动预置 6 条默认记录

**性能约束**:
- [ ] LLM 调用期间 UI 不得阻塞（async/await + IProgress<T>）；章节写作使用流式输出（IAsyncEnumerable<string>）
- [ ] 流水线暂停/恢复需完整持久化上下文，支持应用重启后恢复
- [ ] SQLite 备份使用 backup API 热备份，不阻塞正常读写

**Phase 边界**:
- [ ] Phase 1 (MVP): 阶段 1-7，核心创作闭环，本文档除标注 P2/P3 外的全部需求
- [ ] Phase 2: 阶段 8 (数据采集)，默认关闭，通过配置开启
- [ ] Phase 3: 阶段 9 (多平台发布)，默认关闭，通过配置开启

**文件与代码**:
- [ ] 项目文件组织遵循架构文档 §3.1 目录树
- [ ] Core 层零外部依赖（不引用 EF Core、HttpClient、WPF）
- [ ] Engine 层不引用 WPF（不依赖 UI 框架）

---

## 2. 功能模块需求

### 2.1 项目管理

负责项目（一本书）的创建、打开、元数据管理和状态持久化。

| ID | 需求名称 | 描述 | 优先级 | 依赖 | 验收标准 |
|----|---------|------|--------|------|---------|
| PRJ-001 | 创建新项目 | 用户输入书名（必填，≤100字）、选择题材/类型、设定目标字数（≥1万字）。系统创建 Project 实体，初始化 SQLite 数据库，自动运行 EF Core Migration。Project 含字段：Title、Genre、Status(Draft)、TargetWordCount、CreatedAt | P0 | — | 项目创建后 Projects 表新增记录；数据库文件生成在 AppData/NovelWriter/ 下；题材和类型从预定义枚举中选择 |
| PRJ-002 | 打开已有项目 | 列出本地所有项目（从 Projects 表读取），显示书名+类型+创建时间+当前进度（已完成章数/总章数）。选择后恢复完整状态：流水线阶段、章节进度、记忆数据、Bag 上下文。若当前阶段为 Stage04-07 且存在 status=Draft 的章节，从该章恢复 | P0 | PRJ-001 | 项目列表按最近打开时间排序；恢复后 ShellWindow 展示正确的流水线阶段和章节状态 |
| PRJ-003 | 项目状态持久化 | 流水线暂停或应用退出时，将 Projects 表的 CurrentStage、CurrentChapterNumber、CurrentVolumeNumber、PendingConfirmationJson、StateBagJson、SuspendedAt 字段写入。恢复时从 Projects 表读取 + ChapterSummaries 表重建 L1 窗口 + 重新执行 L3 检索（不信任缓存） | P0 | PRJ-002, PIP-003 | 应用崩溃/正常退出后重启，项目可从中断处恢复；恢复时 L3 检索重新执行而非使用过期缓存 |
| PRJ-004 | 项目元数据编辑 | 支持修改书名、类型、目标字数。不允许修改创建时间。显示进度信息：已完成章节数/总章节数、当前卷号/总卷数 | P1 | PRJ-001 | 修改即时持久化；进度信息实时更新 |

---

### 2.2 流水线编排

负责 9 阶段流水线的状态机、章节循环体推进、暂停/恢复控制。

| ID | 需求名称 | 描述 | 优先级 | 依赖 | 验收标准 |
|----|---------|------|--------|------|---------|
| PIP-001 | 阶段状态机 | 实现 9 阶段顺序推进：Stage01→02→03→[04→05→06→07 循环]→08→09。每阶段通过 IPipelineStage.ExecuteAsync(context) 执行，返回 PipelineResult。NextStage=null + ConfirmationItems为空 = 完成；NextStage=null + ConfirmationItems非空 = 暂停确认；NextStage!=null = 推进到指定阶段 | P0 | PRJ-001 | 阶段不会跳过或乱序；PipelineResult 的状态语义正确实现 |
| PIP-002 | 章节循环体 | Stage04→05→06→07 构成每章循环体。从 context.State.Outlines 读取大纲列表，从 context.State.CurrentChapterNumber 确定当前章。每完成一章 CurrentChapterNumber++。判断是否为卷末章（大纲中该章节的 VolumeNumber 与下一章不同或已是最后一章），卷末自动触发 L2→L3 压缩 + SelfCheck 全量检查 | P0 | PIP-001, OUT-001 | 章节号严格递增不跳跃；卷末检测准确（包括最后一卷最后一章）；压缩和全量检查仅在卷末触发 |
| PIP-003 | 暂停与恢复 | 阶段返回非空 ConfirmationItems 时：① SaveStateAsync 持久化至 Projects 表（6个字段）；② 返回 result 给 UI 展示确认对话框；③ 暂停等待。用户完成决策后调用 ResumeWithDecisionsAsync(decisions)：① RestoreStateAsync 从 Projects 表恢复；② 将 decisions 注入 context.State.PendingDecisions；③ 重新执行当前阶段的 ExecuteAsync | P0 | PIP-001, CFM-001 | 暂停→退出应用→重启→恢复，决策正确传递到阶段内部；恢复时验证阶段匹配，不匹配则报错 |
| PIP-004 | 评审-润色循环 | Stage07 综合评分 < 7.0 时自动回到 Stage05（重写），context.State 中保留上一版评审反馈。最多重写 MaxRevisionRounds 轮（默认3，可配置）。达到上限后 UI 提示"已达润色上限，需人工介入"，允许用户选择：接受当前版本 / 手动修改 / 放弃本章重写 | P0 | PIP-001, REV-004 | 润色轮次计数正确；达到上限后不自动继续；用户可选择跳过但需二次确认 |
| PIP-005 | State 类型安全 | PipelineContext 使用强类型 `PipelineState` 对象（替代 Dictionary<string, object> Bag），每个阶段输入输出通过类型化属性访问。所有属性在 `PipelineState` 类中定义，编译期类型检查。状态序列化/反序列化自然有类型约束 | P0 | PIP-001 | 编译期捕获类型错误；阶段间数据传递不可能出现 key 拼写错误或类型不匹配 |
| PIP-006 | 阶段开关 | Stage08 (数据采集) 和 Stage09 (发布) 默认排除在流水线外。用户通过配置分别启用。Phase 1 用户看到并使用的只有 Stage01-07 | P2 | PIP-001 | 默认关闭时 GetNextStage 在 Stage07 后返回 null（项目完成）；开启后在 Stage07 后返回 Stage08 |

---

### 2.3 题材选择 (Stage01)

负责根据用户偏好推荐题材，并初始化世界观框架。

| ID | 需求名称 | 描述 | 优先级 | 依赖 | 验收标准 |
|----|---------|------|--------|------|---------|
| TOP-001 | 偏好输入 | 用户输入：偏好的网文类型（可多选，如玄幻/都市/科幻等）、风格倾向（爽文/慢热/烧脑等）、目标读者画像（男频/女频/不限+年龄段）。所有字段可选，至少填写一项即可触发推荐 | P0 | UI-001 | 类型和风格从预定义枚举列表中选择（支持多选）；读者画像为单选 |
| TOP-002 | 题材推荐 | 调用写作 LLM，System Prompt 包含角色设定+输出格式要求，User Message 包含用户偏好。返回 3-5 个题材选项，每个选项为结构化 JSON：core_conflict（核心冲突一句话）、target_audience（目标读者画像）、unique_selling_point（差异化卖点，与同类题材的区分点）、suggested_genre（建议的具体分类标签）。生成失败时展示错误并提供重试 | P0 | LLM-001 | 返回选项数在 3-5 范围内；每个选项四个字段非空；重试按钮在失败时可用 |
| TOP-003 | 世界观框架初始化 | 用户确认题材（从选项中选择一个或手动输入）后，LLM 生成 3-5 条核心世界观规则 + 势力框架。每条含：name（规则名）、category（地域/势力/功法体系/历史事件/种族/物品/规则）、description（一句话规则描述）。写入 WorldSettings 表，每条 version=1, locked=false, global 按规则类型标记 | P0 | TOP-002 | WorldSettings 表新增 3-5 条记录；category 字段值在枚举范围内 |
| TOP-004 | 用户确认 | 题材选项列表展示，用户可选择任一选项进入下一步。用户也可选择"都不满意，重新生成"或"手动输入题材"。确认后题材内容写入 context.State.TopicSelection，世界观规则写入 DB | P0 | TOP-002, TOP-003 | 三种路径均可操作；确认后不可回退到 Stage01（除非重置项目） |

---

### 2.4 梗概生成 (Stage02)

负责基于已确认的题材生成故事梗概，并初始化人物档案。

| ID | 需求名称 | 描述 | 优先级 | 依赖 | 验收标准 |
|----|---------|------|--------|------|---------|
| SYN-001 | 梗概生成 | System Prompt 包含 L3 WorldSetting 当前版本（YAML 序列化）+ 梗概写作要求（五段式结构）。User Message 包含题材选择结果。输出 300-500 字五段式梗概：开端(设定引入+冲突萌芽)、发展(冲突升级+人物关系展开)、转折(关键事件改变走向)、高潮(核心冲突爆发)、结局(冲突解决+人物归宿) | P0 | TOP-004, LLM-001 | 梗概字数 300-500；五段结构完整可辨识；写入 Synopses 表（Type=梗概） |
| SYN-002 | 人物档案初始化 | 基于梗概 + WorldSetting，LLM 生成人物档案。至少包含：1 名主角 + 2 名以上核心配角。每名角色含：name（≤10字）、traits.primary（2-4个核心特质）、traits.forbidden（1-3个禁止特质）、background（≤3行非叙事性描述）、abilities（可选，含 name/level/acquired_chapter/notes）、relationships（与其他角色的关系，含 relation/dynamic）。写入 CharacterProfiles 表，version=1, locked=false | P0 | SYN-001 | CharacterProfiles 表 ≥3 条记录；每人 traits.forbidden 非空（防止角色漂移）；主角至少有一条 relationship |
| SYN-003 | 用户编辑确认 | 展示梗概文本（可编辑）+ 人物列表（每个角色展开显示所有字段，可编辑）。用户可：直接确认 / 修改后确认 / 重新生成。确认后写入 context.State.Synopsis，进入 Stage03。重新生成仅重新调用 LLM，不丢失用户手动修改的人物设定（用户可选择保留哪些） | P0 | SYN-001, SYN-002 | 编辑后字段正确保存；重新生成不影响用户手动保留的角色 |

---

### 2.5 大纲生成 (Stage03)

负责生成分卷分章大纲，初始化 L2 实体，锁定 L3。

| ID | 需求名称 | 描述 | 优先级 | 依赖 | 验收标准 |
|----|---------|------|--------|------|---------|
| OUT-001 | 分卷分章大纲生成 | System Prompt 包含全部 L3 实体（YAML 序列化）+ 梗概。输出完整大纲树。每章节点含：VolumeNumber、ChapterNumber、Title（≤20字）、CoreConflict（本章核心冲突一句话）、KeyEvents（2-5个关键事件）、CharacterIds（本章涉及人物 ID 列表）、SettingIds（本章涉及设定 ID 列表）。大纲总数 = 目标字数 ÷ 章均字数（默认3000字/章，可配置） | P0 | SYN-003, LLM-001 | 大纲覆盖全书；每章节点所有字段非空；章节总数与目标字数匹配（±10%） |
| OUT-002 | L2 实体初始化 | 从大纲中自动提取：① ArcTracker（每条弧线=角色成长线，含 goal + milestones 列表，每个 milestone 对应一个章节号）；② SubplotTracker（从大纲中识别支线，含 description + last_referenced_chapter + dangling_since=0）；③ 初始伏笔：此阶段不自动检测，作者在大纲审阅时手动标记（快捷键插入 `[FS:描述]`），planted_by=manual | P0 | OUT-001 | ArcTrackers 和 SubplotTrackers 表有记录；手动标记的伏笔正确写入 Foreshadowings 表 |
| OUT-003 | 大纲一致性检查 | Agent（可用便宜模型）检查：① 每位角色的 first_appearance_chapter 与 ArcTracker 的 start_chapter 是否对齐；② 每条伏笔的 expected_reveal_chapter_range 是否在有效章号范围内；③ 每章 CharacterIds 中的人物是否在该章之前已出场；④ 是否存在连续 10 章以上未提及的支线。输出不一致项列表，每项含：类型+涉及实体+问题描述+建议修复 | P0 | OUT-001, OUT-002, LLM-001 | 检查完成输出报告；报告项可点击定位到对应大纲节点 |
| OUT-004 | L3 锁定 | 用户确认大纲后，所有 CharacterProfile.locked 和 WorldSetting.locked 设为 true。此后任何对这些实体的修改必须走版本化追加流程（INSERT 新行，version+1），原版本保留。锁定操作在一个事务中完成 | P0 | OUT-003 | 锁定后所有 L3 实体的 locked 字段为 true；尝试 UPDATE 旧行被 Repository 层阻止 |
| OUT-005 | 用户确认 | 展示完整大纲树（树形可折叠，按卷分组）+ 一致性检查报告 + L2 实体概览。用户可编辑每章大纲节点、手动标记伏笔、调整弧线里程碑。确认后写入 context.State.Outlines，初始化 ArcTrackers/SubplotTrackers/Foreshadowings 表，锁定 L3，进入 Stage04 | P0 | OUT-004 | 大纲树可逐章编辑；用户可新增/删除章节节点；确认后不可回退（除非重置项目） |

---

### 2.6 写前准备 (Stage04)

负责每章写作前编译 ContextWindow（记忆+大纲+系统指令），注入随机风格和插曲。

| ID | 需求名称 | 描述 | 优先级 | 依赖 | 验收标准 |
|----|---------|------|--------|------|---------|
| PRE-001 | ContextWindow 编译 | ContextWindowCompiler.CompileAsync 按三步编译：① L1 编译：读取最近5章 ChapterSummary 组装摘要窗口(≤25K token) + 当前场景状态；② L2 全量注入：读取当前卷全部 Foreshadowings + ArcTrackers + SubplotTrackers(≤50K token)；③ L3 检索采用 ID 直查（大纲 CharacterIds/SettingIds）+ 关键词补充（tags/aliases），合并去重 + 剧透过滤 + 截断到 ≤40K token。然后 SystemPromptBuilder 组装系统指令(≤15K token)：人物行为边界(traits.forbidden 排在 traits.primary 前) + 世界观规则 + 全书基调约束 + 写作技术指令。总预算 ≤160K，超限时裁剪 L3 检索结果重试，最多3次 | P0 | MEM-002, MEM-003, MEM-004, LLM-001 | 编译产物 CompiledContext 含 SystemPrompt、UserMessage、TokenBudget；TokenBudget 各层级用量可追溯；超限重试后仍超限则报错提示用户 |
| PRE-002 | 随机风格注入 | StyleInjector.SelectRandomStyleAsync：随机查询 StyleProfiles 表一条记录，排除当前卷 StyleUsageLog 中已使用的风格。将 StyleProfile.ProfileJson 转为 ~300-400 token 纯文本风格指令，追加到 CompiledContext.SystemPrompt。若 StyleLibrary.Enabled=false 或无可用风格（卷内已用完），跳过。记录 StyleUsageLog(ProjectId, ChapterNumber, StyleId) | P1 | STY-004 | 卷内风格不重复；注入后总 token 仍 ≤160K；跳过时不影响后续流程 |
| PRE-003 | 随机插曲注入 | InterludeInjector.PrepareInterludeAsync：随机查询 InterludeEntries 表一条（可按大纲题材过滤），排除当前卷已使用的。调用便宜模型改编为 ≤100 字故事闲笔。插入位置：避开章节头尾10%区域，优先场景切换/时间跳跃处，每1000-1500字最多1处。结果追加到 CompiledContext.UserMessage。若 InterludeLibrary.Enabled=false 或无可用插曲，跳过。记录 InterludeUsageLog | P1 | INT-004, LLM-001 | 卷内插曲不重复；改编文本 ≤100字；改编文本通过 AI 检测（AID-001）；跳过时不影响后续流程 |
| PRE-004 | L3 检索策略 | 从当前章大纲提取两种输入：① ID 直查：大纲节点已含 CharacterIds 和 SettingIds，直接查询 L3 实体最新版本（确保不遗漏）；② 关键词补充：KeywordExtractor 提取人物别名、地名、概念词，匹配 L3 的 tags/aliases 字段（发现未显式关联的条目）。合并去重后调用剧透过滤。global=true 的 WorldSetting 不受过滤限制 | P0 | MEM-004, MEM-005 | ID 直查确保大纲规划的设定不遗漏；关键词补充发现语义相关条目；剧透过滤规则正确 |
| PRE-005 | 最终组装与校验 | 四步编排（编译器→风格→插曲→关键约束重复）完成后：① 调用 TokenCounter 校验总 token ≤160K；② 校验 system prompt 中 traits.forbidden 区块在 traits.primary 之前；③ User Message 中包含关键约束重复区块；④ 将 CompiledContext、StyleProfile、InterludePrompt 写入 context.State；⑤ 返回 `new PipelineResult { Success = true, NextStage = PipelineStage.Stage05 }` | P0 | PRE-001, PRE-002, PRE-003 | 三项校验通过后才进入 Stage05；校验失败给出具体超限/排序问题描述 |

---

### 2.7 章节生成 (Stage05)

负责调用写作 LLM 生成章节正文，支持伏笔标记语法和润色重写。

| ID | 需求名称 | 描述 | 优先级 | 依赖 | 验收标准 |
|----|---------|------|--------|------|---------|
| CHG-001 | AI 章节写作 | 从 context.State 读取 CompiledContext。调用写作 LLM（deepseek-v4-pro）使用流式输出（`StreamChatAsync`）：System Prompt = CompiledContext.SystemPrompt，User Message = CompiledContext.UserMessage + 当前章大纲。输出纯 Markdown 正文，UI 逐步渲染。写作前先将 Chapter 实体写入 DB（status=Draft, Content=空），流式返回的内容累积后更新 Content。若 LLM 调用失败，草稿记录保留不丢失 | P0 | PRE-005, LLM-001 | 输出为合法 Markdown；Chapter 实体 status=Draft；Content 非空；失败时 Draft 记录保留且用户可手动重试；流式输出 UI 不卡顿 |
| CHG-002 | 伏笔标记语法 | 编辑器支持 `[FS:描述文字]` 内联标记。在编辑器中高亮显示（ForeshadowingHighlightBehavior）。LLM 在 System Prompt 中被指示理解此语法并在适当位置插入伏笔标记。MemoryManagerAgent 在 Stage06 识别标记语法为 planted_by=manual | P0 | UI-002, MEM-001 | 标记在编辑器中以特殊颜色/下划线高亮；LLM 生成的标记被正确解析 |
| CHG-003 | 润色重写 | 评审触发润色时（PIP-004），context.State.AggregatedReview 包含上轮评审的 weaknesses + suggestions + flagged 问题。重新调用 Stage05 时，将这些反馈追加到 User Message 末尾（"上一版的评审反馈：..." + 具体弱点 + 修改建议）。润色版本的 Chapter.DraftNumber 递增（v1→v2→v3） | P0 | PIP-004, REV-004 | 润色版 User Message 包含评审反馈；DraftNumber 正确递增 |

---

### 2.8 记忆管理 (Stage06)

负责每章写后的记忆提取、L1/L2/L3 更新、卷级压缩。这是整个系统最核心的模块。

| ID | 需求名称 | 描述 | 优先级 | 依赖 | 验收标准 |
|----|---------|------|--------|------|---------|
| MEM-001 | 写后记忆提取 | MemoryManagerAgent 拆分为 2 次独立 LLM 调用：Call1=`GenerateSummaryAsync` 生成摘要（任务A：400-600字 + key_events + scene_state），Call2=`ExtractStructuralChangesAsync` 结构检测（任务B-F + SelfCheck，依赖 Call1 的 key_events）。每次调用输出单一 JSON 对象。调用模型为 qwen-plus（降低成本）。每次调用的 JSON 输出通过 `LlmJsonOutputParser<T>` 校验，并对引用的 ID 做存在性校验 | P0 | CHG-001, LLM-001 | 两次调用分别输出可解析的 JSON；字段完整率各自≥90%；ID 存在性校验拦截编造 ID |
| MEM-002 | L1 摘要管理 | 每章定稿后 L1Compiler 从 MemoryExtractionResult.taskA_l1_summary 生成 ChapterSummary（summary 文本 + key_events 列表 + word_count + current_scene_state），写入 ChapterSummaries 表。超出最近5章窗口的旧摘要标记为 Archived=true，但保留在 DB 中供回溯。ContextWindow 编译时仅读取 Archived=false 的最近5条 | P0 | MEM-001 | 窗口始终取最近5章；Archived 标记正确；重启后窗口可恢复 |
| MEM-003 | L2 伏笔管理 | 从 MemoryExtractionResult 处理伏笔：① taskB: 高置信度 resolution→自动更新 Foreshadowing.resolution_chapter + status=resolved；低置信度→生成 ConfirmationItem(type=LowConfidenceResolution)。② taskC: 新伏笔候选→全部生成 ConfirmationItem(type=NewForeshadowing, payload=ForeshadowingCandidate)。③ planted_by 字段：manual(作者标记语法) vs auto_detected(Agent检测) | P0 | MEM-001, CFM-001 | 高置信度回收自动处理；新伏笔全部需确认（防止 LLM 误判闲笔为伏笔） |
| MEM-004 | L2 弧线与支线管理 | ① ArcTracker: taskD 的 ArcMilestoneUpdate 自动更新 milestone 状态。新增 milestone→生成 ConfirmationItem(type=ArcMilestoneAddition)。② SubplotTracker: taskE 的 mentioned_in_chapter 更新 last_referenced_chapter。dangling_since 纯规则计算（非LLM输出）：当前章号 - last_referenced_chapter。dangling_since >10 时通知作者 | P0 | MEM-001 | milestone 自动更新；dangling_since 计算准确；超过阈值通知触发 |
| MEM-005 | L3 版本化追加 | taskF 的 L3ChangeProposal：① confidence=High→自动通过，写入 AutoConfirmedItems；② confidence=Low→生成 ConfirmationItem(type=L3CharacterChange 或 L3WorldSettingChange)。写入时 INSERT 新行（version=旧版本号+1），旧行保留。PRIMARY KEY(Id, Version) 联合主键。检索时取 MAX(Version) | P0 | MEM-001 | 每次修改生成新版本行；旧版本可查；检索返回最新版本 |
| MEM-006 | 确认分流 | 所有 MemoryExtractionResult 中的变更按规则分流：AutoConfirmItems = L1摘要 + milestone自动更新 + subplot自动更新 + High置信度L3变更。NeedsConfirmationItems = auto_detected新伏笔 + 低置信度回收 + Low置信度L3变更 | P0 | MEM-001, MEM-003, MEM-004, MEM-005 | 分流规则正确；AutoConfirmItems 和 NeedsConfirmationItems 分别进入 PipelineResult 对应列表 |
| MEM-007 | L2→L3 卷级压缩 | 触发条件：卷末章定稿 + Review 通过。流程：① resolved/abandoned 伏笔→移入 ForeshadowingArchives 表，从 Foreshadowings 表删除；② 完成弧线→压缩摘要追加到 CharacterProfile.arc_summary（版本化追加）；③ 卷冲突→新增 VolumeSummaries 记录；④ 跨卷 active 伏笔保留。压缩后验证 L2 ≤15K token，不满足则提示作者手动处理 | P0 | MEM-003, MEM-004, MEM-005, PIP-002 | 压缩后 L2 表仅保留跨卷 active 伏笔和新卷实体；归档记录完整；token 验证通过 |
| MEM-008 | 伏笔健康报告 | 每卷压缩前输出：① high 优先级伏笔中未回收数；② 超过 expected_reveal_chapter_range 上限仍未回收的 high 优先级伏笔列表；③ priority=high 且 status=abandoned 的伏笔（需作者二次确认才能放弃）。报告展示在 UI 中，作者确认后继续压缩流程 | P1 | MEM-007 | 三项数据准确；超范围伏笔强提示；high+abandoned 需二次确认 |

---

### 2.9 人工确认闸门

负责记忆变更的逐项确认 UI 和确认结果的事务写入。

| ID | 需求名称 | 描述 | 优先级 | 依赖 | 验收标准 |
|----|---------|------|--------|------|---------|
| CFM-001 | 确认闸门对话框 | 流水线暂停时弹出确认对话框。按类型分组展示 ConfirmationItems：新伏笔(黄色)、伏笔回收(绿色)、L3人物变更(蓝色)、L3设定变更(紫色)、里程碑新增(灰色)。每条显示：类型图标+颜色、Summary(一句话描述)、详情(点击展开 Payload 完整信息)。每条两个按钮：Approve(绿色✓) / Reject(红色✗)，可选输入备注 | P0 | UI-001, PIP-003 | 5种类型有对应图标和颜色；详情展开后显示 Payload 完整字段；Approve/Reject 即时响应 |
| CFM-002 | 批量确认 | 对话框顶部提供"全部 Approve"、"全部 Reject"、"按类型 Approve"操作。按类型 Approve 仅对选中类型的所有条目生效。批量操作前有确认提示（"将 Approve 3 条新伏笔，确认？"） | P1 | CFM-001 | 批量操作有二次确认；操作结果准确反映到每条 |
| CFM-003 | 确认结果事务写入 | MemoryRepository.WriteMemoryChangesAsync(extraction, decisions) 在一个 DbContext 事务中完成：① AutoConfirmItems 全部写入；② decisions 中 Approved=true 的 ConfirmationItem 写入；③ Rejected 的条目丢弃不写入。最后 SaveChanges + Commit。若事务中任一步失败，全部回滚 | P0 | CFM-001, MEM-003, MEM-004, MEM-005 | 事务原子性；Rejected 条目不在任何记忆表中出现；失败回滚后 UI 提示错误 |
| CFM-004 | 记忆变更预览 | 每条 ConfirmationItem 的详情面板中展示"变更预览"：如果是新伏笔→预览写入后的 Foreshadowing YAML；如果是 L3 变更→展示旧版本 vs 新版本的 diff。实施时选择简单的文本 diff 展示方式 | P2 | CFM-001 | 预览内容与实际写入格式一致 |

---

### 2.10 子Agent评审 (Stage07)

负责多 Persona 并行评审、聚合评分、评审结果展示和润色触发。

| ID | 需求名称 | 描述 | 优先级 | 依赖 | 验收标准 |
|----|---------|------|--------|------|---------|
| REV-001 | Persona 预置管理 | 首次启动时 Personas 表 INSERT 6 条默认记录。各 Persona 定义：① 爽文党(爽感60%节奏40%) ② 逻辑党(逻辑70%设定30%) ③ 情感党(情感50%人物50%) ④ 老书虫(创新40%文笔60%) ⑤ 追更党(钩子80%信息量20%) ⑥ 考据党(考据70%细节30%)。每条含 Name、AgeGroup、Preferences、CritiqueStyle、SystemPrompt（完整评审提示词）、ScoreWeights（JSON，维度名→权重）。用户可编辑和新增 | P0 | — | 6 条默认记录在首次启动后存在；ScoreWeights 各维度权重和为100%；用户新增的 Persona 可正常参与评审 |
| REV-002 | 并行评审调用 | ReviewOrchestrator.ReviewChapterAsync 接收 personaCount 参数（默认4，可配置2-5）。从 Personas 表随机选取 N 个 Persona。对每个 Persona 启动独立 LLM 调用（qwen-plus，降低成本），所有调用通过 Task.WhenAll 并行。单个 Persona 失败（超时/HTTP错误）不阻塞其他，失败的 Persona 在结果中标记为 Failed | P0 | LLM-001, REV-001 | N 个调用同时发起；单个失败不影响其他；结果列表含成功+失败项 |
| REV-003 | 结构化评审输出 | 每个 Persona 的 System Prompt 包含评审角色设定 + JSON 输出格式要求。User Message 包含章节全文 + 大纲 + 写作时的 System Prompt（让评审者了解写作约束）。输出 JSON：scores（维度名→1-10分，维度名来自 Persona.ScoreWeights 定义的键）、overall（加权综合分，1-10）、strengths（优点列表，每条≤50字）、weaknesses（缺点列表，每条≤50字）、suggestions（修改建议，每条≤100字）、flagged（问题标记列表）。flagged 每项含 type(continuity_error/logic_flaw/character_inconsistency/setting_violation)、detail（引用章号和原文） | P0 | REV-002 | scores 维度名与 Persona.ScoreWeights 一致；flagged 项 detail 包含章号引用；至少2个 Persona 成功 |
| REV-004 | 评审聚合 | ReviewAggregator.Aggregate：OverallScore = 所有成功 Persona 的 overall 分数平均值。若 <2 个 Persona 成功→抛出 LlmUnavailableException→PipelineOrchestrator 暂停流水线。聚合结果 AggregatedReview 包含：OverallScore、各 Persona 分数明细、合并的 strengths/weaknesses/suggestions（去重+按提及次数排序）、合并的 flagged（去重，同类型问题合并） | P0 | REV-003 | OverallScore 为平均值；<2成功时正确抛异常；合并去重逻辑合理（相同或高度相似的项不重复出现） |
| REV-005 | 评审结果 UI | 右栏 ReviewPanelView 展示：① OverallScore 大字显示（绿色≥7/红色<7）+ 各 Persona 分数条形图；② 各 Persona 可展开卡片，显示 strengths/weaknesses/suggestions；③ Flagged 问题列表，每条可点击跳转到中栏编辑器对应正文位置；④ 底部操作按钮："接受并定稿"(≥7时)、"润色重写"(<7时或手动)、"跳过评审"(需二次确认) | P0 | REV-004, UI-001, UI-006 | 分数颜色正确；Persona 卡片展开/折叠流畅；Flagged 跳转定位准确（行号匹配） |
| REV-006 | 润色循环 | OverallScore <7 自动标记 NeedsRevision=true。PIP-004 触发润色循环。用户也可在 OverallScore ≥7 时手动选择润色。每次润色后重新跑评审（新的并行调用），直到评分 ≥7 或达到上限。润色记录 Review 实体写入 Reviews 表（N:1→Chapter） | P0 | REV-004, PIP-004 | 润色后重新评审；评分记录完整保留各轮评审结果 |

---

### 2.11 AI检测预检

负责章节定稿前检测 AI 生成特征，标注风险等级，供作者参考。

| ID | 需求名称 | 描述 | 优先级 | 依赖 | 验收标准 |
|----|---------|------|--------|------|---------|
| AID-001 | 统计特征检测 | StatisticalDetector 纯规则检测（无 LLM 调用），对章节正文计算 4 个维度：① 句式均匀性（句长标准差<阈值→扣分）；② 词汇多样性（TTR Type-Token Ratio<阈值→扣分）；③ 情绪节奏（滑动窗口情感强度方差<阈值→扣分）；④ 段落结构（段落首句模式聚类>阈值→扣分）。各维度分数 0-1，综合得 RiskLevel(Low/Medium/High/Critical) | P0 | — | 检测实时完成（<1秒）；4维度分数和综合风险等级输出到 DetectionReport |
| AID-002 | LLM 辅助检测 | StatisticalDetector 输出 High/Critical 时，自动调用 LlmAssistedDetector（qwen-plus）：将标记段落发送给 LLM 做人工作判断（"这段文字读起来像AI写的吗？"）。LLM 返回补充判断和建议。若 RiskLevel=Low/Medium，跳过此步骤节省成本 | P1 | AID-001, LLM-001 | 仅 High+ 触发 LLM 调用；补充判断追加到 DetectionReport.Suggestions |
| AID-003 | 检测结果展示 | 评审通过后、定稿前展示 DetectionReport：风险等级（颜色标识：Low=绿/Medium=黄/High=橙/Critical=红）、4 维度分数量表、FlaggedPatterns（被标记的具体模式描述）、Suggestions（降低 AI 味的修改建议）。高风险显示"建议深度人工润色"提示，不自动拦截定稿 | P0 | AID-001, UI-001 | 颜色标识准确；建议具体可操作；不阻塞定稿 |
| AID-004 | 外部检测器接口 | 预留 IExternalDetector 接口：Task<ExternalDetectionResult> DetectAsync(string content)。配置文件可指定外部检测器类型（如"zhuque"），Phase 1 无实现，接口留空 | P2 | AID-001 | 接口定义完整；配置切换后 DI 注入对应实现 |

---

### 2.12 SelfCheck偏差检测

负责检测长期写作中的人物/设定一致性偏差。

| ID | 需求名称 | 描述 | 优先级 | 依赖 | 验收标准 |
|----|---------|------|--------|------|---------|
| SCK-001 | 增量检查（每5章） | 触发条件：章节号 % 5 == 0。复用 MemoryManagerAgent 的记忆提取 LLM 调用，在 User Message 中追加 SelfCheck 任务（检查最近5章是否违反 L3 traits.forbidden 列表中的禁止特质）。提取结果中 selfcheck_forbidden_violations 非空→追加到 context.State.IncrementalViolations 列表 | P0 | MEM-001 | 每5章准确触发；违规累积到列表不丢失；不额外增加 LLM 调用 |
| SCK-002 | 全量检查（每卷末） | 触发条件：卷末压缩前。独立 LLM 调用（qwen-plus）：System Prompt = 偏差检查专家角色 + 输出要求；User Message = 全卷所有章节摘要 + 全部 L3 实体（CharacterProfile 和 WorldSetting 的完整 YAML）。逐条检查：① traits.forbidden 违规 ② 人物关系一致性 ③ 世界观规则违反 ④ 伏笔回收状态与实际正文一致性。输出 Deviation 列表，含 Severity + 章号引用 | P0 | SCK-001, LLM-001, PIP-002 | 全部 L3 实体被检查（非采样）；Deviation 含具体章号；Severity 分类准确 |
| SCK-003 | 偏差报告 | 合并增量累积违规 + 全量新发现 → DeviationReport。每项含：EntityId、Severity(Critical/High/Medium/Low)、Description（含章号引用）、DetectedInChapter、ForbiddenTrait(适用时)、Suggestion。Critical→强制暂停压缩，要求作者处理；High→弹窗通知；Medium/Low→记录报告供查阅 | P0 | SCK-002 | Critical 偏差阻止压缩继续；报告可导出/打印 |
| SCK-004 | 偏差处理 | 对每条偏差提供三种处理方式：① "修正正文"→标记章节需润色，触发 Stage05 重写涉及段落；② "更新设定"→将偏差内容写入 L3（版本化追加），接受为新的设定标准；③ "标记例外"→偏差保留在报告中但标记为 KnownException，后续检查不再报告同类偏差 | P1 | SCK-003, PIP-001 | 三种处理均可操作；修正正文后偏差从报告中移除；更新设定后对应 L3 实体版本+1 |

---

### 2.13 风格库管理

负责写作风格档案的构建、入库、审核和随机选择。

| ID | 需求名称 | 描述 | 优先级 | 依赖 | 验收标准 |
|----|---------|------|--------|------|---------|
| STY-001 | 风格档案提取 | StyleExtractionAgent.ExtractAsync(storyText, sourceTitle, sourceAuthor)：输入 ≤5000 字短篇全文，调用 deepseek-v4-pro 提取结构化风格档案。System Prompt = 风格分析专家角色 + JSON Schema（句式特征/词汇偏好/修辞习惯/叙事距离/段落节奏五个维度）。输出 JSON ≤500字，写入 StyleProfile.ProfileJson。数据源：公版作品（聊斋/世说新语/古登堡）+ 人工搜集短篇 | P1 | LLM-001 | 提取结果包含5个维度；单篇成本约 ¥0.02；Phase 1 目标 30-100 篇 |
| STY-002 | 风格档案入库 | StyleProfile 写入 StyleProfiles 表。字段：Id(STYLE_{NNN})、SourceTitle、SourceAuthor、SourceType(public_domain/creative_commons/manual)、SourceWordCount、ProfileJson、Tags(JSON array)、UsageCount(初始0)、CreatedAt | P1 | STY-001 | 入库后可检索；Tags 用于分类筛选 |
| STY-003 | 人工审核抽检 | 入库前人工抽检 20%（随机选取）。审核标准：5个维度是否都有有效内容、提取的风格描述是否与实际文风匹配。不合格→标记为 NeedsReExtraction，退回 StyleExtractionAgent 重新提取。支持手动编辑 ProfileJson 后直接通过 | P1 | STY-002 | 抽检比例 20%；不合格可退回或手动编辑；审核记录可追溯 |
| STY-004 | 随机风格选择 | StyleInjector.SelectRandomStyleAsync 调用 IStyleLibraryRepository.GetRandomStyleExcludingAsync(usedIds)。usedIds 来自 StyleUsageLog 表中当前卷的使用记录。选择后调用 RecordUsageAsync 写入日志。若可用风格数为 0→返回 null，Stage04 跳过风格注入 | P1 | STY-002 | 卷内不重复；日志记录完整；无可用风格时静默跳过 |
| STY-005 | 风格注入开关 | 配置 StyleLibrary.Enabled(默认 false，需用户手动开启)。关闭时 Stage04 不调用 StyleInjector。开启时若风格库为空→提示用户先构建风格库 | P1 | STY-004 | 开关即时生效；空库时给用户明确提示而非静默失败 |

---

### 2.14 插曲库管理

负责插曲条目的入库、数据源接入、LLM 改编和随机选择。

| ID | 需求名称 | 描述 | 优先级 | 依赖 | 验收标准 |
|----|---------|------|--------|------|---------|
| INT-001 | 插曲条目入库 | InterludeEntry 写入 InterludeEntries 表。字段：Id(EP_{NNN})、SourceType(historical/news/anecdote/trivia)、Source(来源描述)、CoreFact(≤50字核心事实)、NarrativeHook(叙事钩子一句话)、AdaptableThemes(JSON array，可适配的主题)、SuggestedGenres(JSON array，建议题材)、UsageCount(初始0)、CreatedAt | P1 | — | 入库后可检索；CoreFact ≤50字 |
| INT-002 | 数据源接入 | Phase 1 主数据源：天聚数行"简说历史"API。注册获取 API Key→配置到 appsettings.json。每次调用返回历史事件(30-50字)。调用频率限制遵循 API 免费额度(100条/天)。响应缓存到本地 InterludeEntries 表（检查 CoreFact 去重）。Phase 2：批量提取公版笔记小说典故 + LLM 改写入库 | P1 | CFG-001 | API 调用成功率 ≥95%；去重有效；超免费额度时提示用户 |
| INT-003 | LLM 插曲改编 | InterludeInjector 调用便宜模型(qwen-plus)：System Prompt = 改编指令("将以下历史事件改编为适合插入网文章节的闲笔...")；User Message = CoreFact + 当前章节大纲摘要(提供上下文)。输出 ≤100 字纯文本闲笔，需：① 与章节语境融合 ② 不破坏叙事节奏 ③ 不引入新人物或设定。改编后文本通过 AID-001 检测 | P1 | INT-002, LLM-001 | 改编文本 ≤100 字；通过 AI 检测（RiskLevel 不为 High/Critical）；改编失败时静默跳过不影响流水线 |
| INT-004 | 随机插曲选择 | InterludeInjector 调用 IInterludeRepository.GetRandomInterludeAsync(genre, excludeIds)。genre 可选(从大纲题材匹配)，excludeIds 来自 InterludeUsageLog 当前卷使用记录。选择后 RecordUsageAsync 写入日志(ProjectId, ChapterNumber, InterludeId, AdaptedText, InsertPosition) | P1 | INT-001 | 卷内不重复；题材过滤有效；无可用插曲时静默跳过 |
| INT-005 | 插曲注入开关 | 配置 InterludeLibrary.Enabled(默认 false)。关闭时 Stage04 不调用 InterludeInjector。开启时若插曲库为空→提示用户先构建插曲库或开启 API 自动获取 | P1 | INT-004 | 开关即时生效；空库提示明确 |

---

### 2.15 LLM适配层

负责多模型适配、重试/降级策略、调用日志。

| ID | 需求名称 | 描述 | 优先级 | 依赖 | 验收标准 |
|----|---------|------|--------|------|---------|
| LLM-001 | 三模型适配器 + 流式输出 | LlmAdapterBase 抽象基类实现共用逻辑（重试/超时/日志/流式）。三个子类：DeepSeekAdapter(端点 api.deepseek.com, 模型 deepseek-v4-pro, 1M上下文)、QwenAdapter(端点 dashscope.aliyuncs.com, 模型 qwen-plus/qwen-max)、KimiAdapter(端点 api.moonshot.cn, 模型 moonshot-v1-8k/32k/128k)。接口包含非流式 `ChatAsync`（用于记忆提取/评审等 JSON 场景）和流式 `StreamChatAsync`（用于章节写作，返回 IAsyncEnumerable<string>）。各子类仅覆写 BuildRequest 和 ParseResponse | P0 | — | 三个适配器可独立调用并返回正确响应；流式输出可逐步返回文本片段；适配器切换不影响调用方代码 |
| LLM-002 | 重试策略 | 所有 ChatAsync 调用通过 Polly ResiliencePipeline 包裹：① Retry: 3次，指数退避 1s/3s/9s（仅对 transient 错误：5xx/超时/网络错误；4xx 不重试）；② Timeout: 5分钟。超时或重试耗尽→抛 HttpRequestException 给 LlmDegradationPolicy | P0 | LLM-001 | 5xx 错误正确重试；4xx 错误不重试直接失败；超时时间准确 |
| LLM-003 | 模型降级 | LlmDegradationPolicy 维护降级链：deepseek-v4-pro(优先级1)→qwen-max(2)→moonshot-v1-128k(3)。每个模型有独立 CircuitState(Closed/Open/ HalfOpen)。连续3次失败→熔断(Open)30秒→自动切换下一优先级。30秒后变为 HalfOpen→下一次调用成功则恢复 Closed。全部 Open→抛出 LlmUnavailableException | P0 | LLM-002 | 熔断/恢复逻辑正确；降级切换对 PipelineOrchestrator 透明（除最终 LlmUnavailableException） |
| LLM-004 | 模型分用途配置 | 不同调用点通过配置指定模型：WritingLlm(默认 deepseek-v4-pro)、ExtractionLlm(默认 deepseek-v4-pro)、ReviewLlm(默认 qwen-plus)、SelfCheckLlm(默认 qwen-plus)、StyleExtractionLlm(默认 deepseek-v4-pro)、InterludeAdaptationLlm(默认 qwen-plus)。LlmAdapterFactory.Create(modelName) 从配置读取模型名→创建对应适配器 | P0 | LLM-001, CFG-001 | 各调用点使用配置指定的模型；配置修改后下次调用生效 |
| LLM-005 | API Key 验证 | 应用启动时检查配置的 API Key 数量。至少一个服务有 Key→正常启动。全部服务无 Key→弹出设置对话框引导用户配置。Key 通过 ApiKeyStore（DPAPI 加密）读取，验证方式：调用一次廉价 API（如模型列表）确认 Key 有效 | P0 | CFG-002 | 无 Key 时引导配置；Key 无效时提示具体哪个服务 |
| LLM-006 | 调用日志 | 每次 ChatAsync 调用记录：Timestamp、ModelName、CallerMethod(调用点标识)、Duration、InputTokens(从 usage.prompt_tokens 获取)、OutputTokens(usage.completion_tokens)、Success/Fail。不记录 systemPrompt、userMessage、responseContent。通过 Serilog 写入滚动日志文件 | P1 | LLM-001 | 日志不含正文内容；可按模型/调用点/时间段过滤；用于成本统计 |

---

### 2.16 配置与密钥管理

| ID | 需求名称 | 描述 | 优先级 | 依赖 | 验收标准 |
|----|---------|------|--------|------|---------|
| CFG-001 | 用户配置管理 | appsettings.json 定义所有配置项及默认值。配置节：Llm(DefaultModel/ReviewModel/ExtractionModel+DegradationChain)、Memory(L1MaxTokens/L2MaxTokens/L3MaxTokens/ContextWindowMaxTokens/SummaryWindowSize/DanglingThreshold)、Review(DefaultPersonaCount=4/PassThreshold=7.0/MaxRevisionRounds=3)、SelfCheck(IncrementalInterval=5/FullCheckAtVolumeEnd=true)、StyleLibrary(Enabled=false/MaxProfileTokens=400)、InterludeLibrary(Enabled=false/MaxInterludeChars=100/FrequencyPer1000Chars=1)、Storage(DatabasePath/BackupDirectory/MaxBackupCount=20) | P0 | — | 所有配置项有默认值；缺失配置项时使用默认值而非崩溃 |
| CFG-002 | API Key 加密存储 | ApiKeyStore 使用 System.Security.Cryptography.ProtectedData (Windows DPAPI)。Scope: CurrentUser。SaveKey(service, apiKey)→加密后写入 %AppData%/NovelWriter/keys.dat。GetKey(service)→解密返回。DeleteKey(service)→删除。Key 不出现在 appsettings.json、日志、错误消息中 | P0 | — | Key 写入后文件内容不可读；同账号同机器可解密；不同账号或机器解密失败返回 null |
| CFG-003 | 设置对话框 | SettingsDialog 提供：① API Key 配置区（三个服务分别输入框，密码遮罩，测试连接按钮）；② 模型选择区（各用途的下拉选择框）；③ 评审参数区（Persona数量滑块/通过阈值/润色上限）；④ 存储路径区（数据库路径/备份路径选择）；⑤ 风格/插曲开关（ToggleButton）。修改即时保存或提示需重启 | P0 | CFG-001, CFG-002, UI-001 | 设置修改即时生效（除存储路径需重启）；测试连接反馈成功/失败及原因 |

---

### 2.17 备份恢复

| ID | 需求名称 | 描述 | 优先级 | 依赖 | 验收标准 |
|----|---------|------|--------|------|---------|
| BKP-001 | WAL 热备份 | BackupService.BackupAsync：① 执行 WAL checkpoint（PRAGMA wal_checkpoint(TRUNCATE)）确保 WAL 数据写入主数据库文件；② 使用 Microsoft.Data.Sqlite 的 connection.BackupDatabase() 方法（封装 sqlite3_backup_init API）复制到备份目录。备份文件命名：NovelWriter_backup_{ProjectName}_{yyyyMMdd_HHmmss}.db | P0 | — | 备份不阻塞正常读写；备份文件可被 SQLite 直接打开；checkpoint 后再备份 |
| BKP-002 | 自动备份触发 | ① 每章定稿后（Review 通过+AI检测完成）自动备份；② 卷级压缩（MEM-007）前强制备份（标记为 pre_compression）；③ 用户可在任意时刻点击"立即备份"按钮。自动备份在后台执行不阻塞 UI | P0 | BKP-001, PIP-002 | 定稿后备份触发；压缩前备份标记可识别；手动备份按钮可操作 |
| BKP-003 | 备份保留策略 | 备份目录保留最近 MaxBackupCount 个备份文件（默认20）。超出时自动删除最旧文件。pre_compression 标记的备份不计入数量限制（永久保留，直到用户手动删除）。用户可手动删除任意备份文件 | P1 | BKP-001 | 备份文件数 ≤ 20 + pre_compression 备份数；删除前有确认提示 |
| BKP-004 | 备份恢复 | 用户选择备份文件→"恢复到当前项目"（覆盖现有数据，需输入项目名确认）或"恢复为新项目"（创建新 Project 记录）。恢复前弹窗警告："此操作将覆盖当前项目数据，不可撤销。建议先手动备份。"确认后关闭当前项目连接→复制备份文件→重新打开 | P1 | BKP-001 | 恢复后项目状态与备份时一致；覆盖操作有明确的二次确认 |

---

### 2.18 UI交互

| ID | 需求名称 | 描述 | 优先级 | 依赖 | 验收标准 |
|----|---------|------|--------|------|---------|
| UI-001 | 三栏布局 ShellWindow | ShellWindow 为三栏布局：左栏 NavigationTreeView(默认200px，可拖拽调整80-300px)、中栏 EditorView+EditorPreviewView(弹性宽度，两者Tab切换)、右栏 ContextPanelView/ReviewPanelView(默认280px，可拖拽调整200-400px)。左栏支持折叠(汉堡菜单按钮)。窗口最小尺寸 1024×640 | P0 | — | 三栏可拖拽调整；左栏折叠/展开有过渡动画；最小尺寸限制生效 |
| UI-002 | Markdown 编辑器 | 中栏 EditorView：基于 **AvalonEdit** 组件的 Markdown 编辑区。支持：语法高亮（标题/粗体/斜体/列表/引用，通过自定义 HighlightingDefinition）、`[FS:描述]` 伏笔标记高亮（自定义 VisualLineTransformer 实现特殊背景色+下划线+tooltip）、行号显示（内置）、Ctrl+S 手动保存。EditorPreviewView 通过 Markdig 渲染为 FlowDocument 预览。编辑/预览切换通过 Tab 或 Ctrl+P | P0 | — | 语法高亮正确；伏笔标记高亮醒目；预览渲染正确；基于 AvalonEdit 而非完全自绘 |
| UI-003 | 导航树 | 左栏 NavigationTreeView 以树形展示：项目根节点→梗概→大纲(卷→章)→人物档案→世界观→伏笔列表。点击节点跳转到对应内容。大纲节点显示章标题+状态图标(Draft/Completed/InReview)。右键菜单：重命名/删除章节点(仅 Draft 状态可删除) | P0 | PRJ-002, OUT-001 | 树结构正确反映项目数据；点击跳转准确；右键菜单按状态控制可用性 |
| UI-004 | 上下文面板 | 右栏 ContextPanelView 在写前准备完成后展示：① L1 摘要窗（最近5章，每章可展开查看摘要文本+key_events）；② L2 列表（活跃伏笔/弧线/支线的概览卡片）；③ L3 检索结果（本次注入的人物+世界观条目）。数据源为 context.State.CompiledContext | P0 | PRE-005 | 面板内容与写前编译结果一致；可展开/折叠各项 |
| UI-005 | Token 预算可视化 | TokenBudgetBar 为水平进度条，根据 CompiledContext.TokenBudget 显示：绿(<70%, Safe)、黄(70-90%, Warning)、红(>90%, Critical)。Tooltip 显示各层级用量明细：L1/L2/L3/系统指令/风格/插曲 各自的 token 数。超出 160K 时进度条满格+红色闪烁 | P0 | PRE-005 | 颜色阈值准确；Tooltip 明细正确；超限时闪烁提醒 |
| UI-006 | 评审面板 | 右栏 ReviewPanelView：顶部 OverallScore 大字+颜色；各 Persona 卡片堆叠（点击展开/折叠，展开显示 scores+strengths+weaknesses+suggestions）；Flagged 列表在底部（红色边框卡片，点击跳转编辑器对应行）。操作按钮在面板底部 | P0 | REV-005 | Persona 卡片流畅展开；Flagged 跳转定位准确 |
| UI-007 | 流水线状态栏 | 底部状态栏 PipelineStatusView：PipelineProgress 阶段指示器（水平步骤条，当前阶段高亮+脉冲动画，已完成阶段打勾）。右侧信息：字数统计(当前章/全书目标)、L3 条目数、当前阶段名称、LLM 调用状态（等待中/进行中/完成/失败，带动画指示器） | P0 | PIP-001 | 阶段切换同步更新；字数实时更新；LLM 状态指示准确 |
| UI-008 | 响应式调用进度 | LLM 调用期间：① 阶段指示器显示旋转动画+文字"正在生成..."；② 中栏编辑器不可编辑（只读模式+半透明遮罩）；③ IProgress<string> 报告文本显示在状态栏（如"正在调用 DeepSeek..."→"正在等待响应..."）。调用完成→恢复正常状态 | P0 | LLM-001 | 调用期间 UI 不卡顿（主线程不阻塞）；进度文本更新；完成后恢复可编辑 |
| UI-009 | 记忆树可视化 | MemoryTreeView 自定义控件：三层树形结构。L1=摘要窗(显示5章摘要缩略)，L2=伏笔/弧线/支线(按类型分层，hover显示详情 tooltip)，L3=人物档案/世界观(可展开查看各版本历史)。适合放在独立弹窗或右栏 Tab 中 | P1 | MEM-001 | 树结构反映数据库实际状态；各层节点可展开 |

---

### 2.19 网文平台数据采集 (Phase 2)

| ID | 需求名称 | 描述 | 优先级 | 依赖 | 验收标准 |
|----|---------|------|--------|------|---------|
| COL-001 | 统一采集器 | IPlatformCollector 接口：FetchHotListAsync(HotListType: Daily/Weekly/Monthly) → HotListEntry 列表；FetchPlatformStatsAsync → PlatformStats。三平台实现：番茄(内部 API get_book_list，User-Agent 轮换)、起点(Playwright headless=false + --disable-blink-features + 高斯延迟+贝塞尔鼠标轨迹)、晋江(Playwright + 住宅代理池)。每个采集器独立可测 | P2 | — | 三个采集器可独立运行并返回数据；HotListEntry 字段完整 |
| COL-002 | 采集合规 | 仅采集公开榜单页面的元数据（书名/作者/分类/排名/评分/字数/连载状态），不采集正文、评论、章节内容。不绕过任何安全措施（字体加密/CAPTCHA/登录墙）。请求间隔硬编码 ≥3秒，单平台并发 ≤2。遵守各平台 robots.txt。住宅代理池仅用于晋江（数据中心 IP 封禁率极高） | P2 | COL-001 | 日志可证明请求间隔 ≥3s；采集日志不含正文内容；robots.txt 遵守可验证 |
| COL-003 | 定时调度 | CollectionScheduler：日榜每日 2:00、周榜每周一 3:00、月榜每月1日 4:00。错过触发时间（如应用未运行）→启动后立即补采一次。采集失败→重试3次（间隔 10s/30s/90s），仍失败→标记 Failed+记录日志，不阻塞下次调度 | P2 | COL-001 | 定时触发准确（±5分钟）；补采机制有效；失败不阻塞后续 |
| COL-004 | 趋势分析 | TrendAnalyzer：对比连续两个周期的数据，计算各品类的环比增速。新兴题材判定：环比增速 >50% 且上周期基数 <10（避免小基数误判）。输出 TrendReport：增速排名 Top20 品类、新兴题材列表（含代表作品+增速）、热度衰减品类（环比下降 >30%） | P2 | COL-001, COL-003 | 环比计算准确；新兴题材判定合理；报告可导出 |
| COL-005 | 选材建议 | RecommendationEngine：输入用户偏好(类型/风格/目标读者)→输出 RecommendationReport。维度：① 热度匹配（有多少热门作品在相似品类）② 竞争度（同品类头部作品数量）③ 增长潜力（品类环比增速）④ 差异化建议（同品类下可切入的细分角度）。每个维度分数 1-10 + 综合建议 | P2 | COL-004 | 四个维度都有评分；建议有具体作品实例支撑 |

---

### 2.20 多平台发布 (Phase 3)

| ID | 需求名称 | 描述 | 优先级 | 依赖 | 验收标准 |
|----|---------|------|--------|------|---------|
| PUB-001 | 多平台格式化 | 根据目标平台要求调整格式：起点(段间空行+章节标题格式)、番茄(段首缩进+特定分隔符)、晋江(纯文本+标题规范)等。生成平台兼容的纯文本/Markdown 版本。格式规则可配置（JSON 配置文件，支持用户自定义规则） | P3 | — | 至少支持 3 个主流平台；格式转换正确；自定义规则可加载 |
| PUB-002 | AI 合规检查 | 发布前检查：① 内容是否符合该平台最新 AI 生成内容政策；② 是否包含平台禁止的敏感词/违规内容；③ 是否需要添加 AI 生成标识（遵循《标识办法》）。检查策略通过配置文件定义，支持在线更新策略 | P3 | PUB-001 | 不合规项标注具体政策和修改建议；策略可更新 |
| PUB-003 | 一键发布 | 通过平台创作者后台 API 或模拟浏览器操作提交章节。发布流程：选择平台→格式化→合规检查→预览→确认发布。支持定时发布（设定日期时间，到时自动提交）。发布状态（待发布/已提交/审核中/已发布/被拒）在 UI 中追踪 | P3 | PUB-001, PUB-002 | 发布状态实时更新；定时发布准时触发；失败自动重试 2 次 |
| PUB-004 | 数据追踪面板 | 发布后定时抓取各平台数据：阅读量/收藏/推荐票/评论数/评分。数据按日聚合存储。UI 面板展示：各平台数据对比图（折线图/柱状图）、单书总数据趋势、章节级数据明细（哪章数据好/差） | P3 | PUB-003 | 数据按日聚合准确；图表可交互；数据源可切换 |

---

## 附录 A: 优先级说明

| 优先级 | 含义 | 预计交付 |
|--------|------|---------|
| P0 | Phase 1 MVP 核心，必须实现。缺少任一项则创作闭环不完整 | Phase 1 |
| P1 | Phase 1 重要但非阻塞。可在 MVP 后期完成，或功能可用但体验简化 | Phase 1 后期 |
| P2 | Phase 2 数据驱动增强。默认关闭，需用户主动开启 | Phase 2 |
| P3 | Phase 3 发布与分发。默认关闭，需用户主动开启 | Phase 3 |

## 附录 B: 需求统计

| 模块 | P0 | P1 | P2 | P3 | 合计 |
|------|----|----|----|----|------|
| 项目管理 | 3 | 1 | — | — | 4 |
| 流水线编排 | 5 | — | 1 | — | 6 |
| 题材选择 | 4 | — | — | — | 4 |
| 梗概生成 | 3 | — | — | — | 3 |
| 大纲生成 | 5 | — | — | — | 5 |
| 写前准备 | 3 | 2 | — | — | 5 |
| 章节生成 | 3 | — | — | — | 3 |
| 记忆管理 | 7 | 1 | — | — | 8 |
| 人工确认闸门 | 2 | 1 | 1 | — | 4 |
| 子Agent评审 | 6 | — | — | — | 6 |
| AI检测预检 | 2 | 1 | 1 | — | 4 |
| SelfCheck | 3 | 1 | — | — | 4 |
| 风格库管理 | — | 5 | — | — | 5 |
| 插曲库管理 | — | 5 | — | — | 5 |
| LLM适配层 | 5 | 1 | — | — | 6 |
| 配置与密钥管理 | 3 | — | — | — | 3 |
| 备份恢复 | 2 | 2 | — | — | 4 |
| UI交互 | 8 | 1 | — | — | 9 |
| 数据采集 (P2) | — | — | 5 | — | 5 |
| 多平台发布 (P3) | — | — | — | 4 | 4 |
| **合计** | **64** | **21** | **8** | **4** | **97** |

