# NovelWriter — 技术架构方案

> **产品定位**: NovelWriter 是创作助手，不是内容工厂。AI 负责记忆追踪、草稿扩写、一致性检查；人类掌控核心创意决策。输出产物为高质量草稿，最终修改权和署名权属于作者。

## 目录

- [1. 技术架构总览](#1-技术架构总览)
- [2. 技术栈明细](#2-技术栈明细)
- [3. 项目结构与模块划分](#3-项目结构与模块划分)
- [4. 模块通信设计](#4-模块通信设计)
- [5. 关键技术设计](#5-关键技术设计)
- [6. 数据流](#6-数据流)
- [7. 数据库表清单](#7-数据库表清单)
- [8. 错误处理与韧性设计](#8-错误处理与韧性设计)
- [9. 配置管理](#9-配置管理)
- [10. 启动与初始化流程](#10-启动与初始化流程)
- [11. 实现注意事项](#11-实现注意事项)
- [12. LLM Prompt 结构规范](#12-llm-prompt-结构规范)

## 1. 技术架构总览

### 1.1 架构分层

```
┌─────────────────────────────────────────────────────────────────┐
│                    NovelWriter.App (WPF 表现层)                   │
│  Views / ViewModels / Controls / Converters / Behaviors          │
├─────────────────────────────────────────────────────────────────┤
│                    NovelWriter.Engine (业务引擎层)                 │
│  PipelineOrchestrator / ContextWindowCompiler /                  │
│  MemoryManagerAgent / ReviewOrchestrator / SelfCheckRunner /     │
│  StyleInjector / InterludeInjector                               │
├─────────────────────────────────────────────────────────────────┤
│                    NovelWriter.Core (领域核心层)                   │
│  Entities / ValueObjects / Interfaces / DomainServices           │
├─────────────────────────────────────────────────────────────────┤
│                    NovelWriter.Storage (持久化层)                  │
│  DbContext / Repositories / Migrations / BackupService           │
├─────────────────────────────────────────────────────────────────┤
│                    外部服务 (跨进程/跨网络)                         │
│  LLM APIs (DeepSeek/Qwen/Kimi) / Platform APIs (Phase 2-3)      │
└─────────────────────────────────────────────────────────────────┘
```

**依赖方向**: App → Engine → Core ← Storage。Core 层不依赖任何外层，Engine 依赖 Core 接口，Storage 实现 Core 定义的仓储接口，App 通过 DI 容器组装全部依赖。

### 1.2 架构决策记录 (ADR)

| ID | 决策 | 理由 | 权衡 |
|----|------|------|------|
| ADR-001 | WPF 单体而非 Electron/Web | 文件系统直访、无浏览器沙箱限制、SQLite 本地性能最优 | 仅 Windows，无法跨平台 |
| ADR-002 | MVVM 而非 MVC/MVP | WPF 原生绑定机制、CommunityToolkit.Mvvm 成熟度 | ViewModel 层可能膨胀，需配合 Service 层 |
| ADR-003 | Core 层纯接口，Engine 层实现 | 方便单元测试、多模型适配器替换 | 增加项目间引用复杂度 |
| ADR-004 | YAML 而非 JSON 做记忆序列化 | LLM 对 YAML 的 token 效率更高、人类可读性更好 | 解析精度依赖严格的模板格式 |
| ADR-005 | Memory Manager Agent 独立调用，拆分为 2 次 LLM 调用 | 记忆管理不应污染写作 LLM 的输出分布；摘要生成和结构检测是异构任务，拆分后 JSON schema 更简单、遵守率更高 | 每章增加 2 次 LLM 调用（Call1=摘要，Call2=结构检测）+ 2-5 次并行评审调用 (默认 4) |
| ADR-006 | 追加式版本化记忆（不可原地覆盖） | 防止错误修改不可逆、支持审计回溯 | L3 存储量持续增长，需定期归档 |
| ADR-007 | 随机写作风格 + 随机插曲注入（每章写前随机选择） | 打破章间结构同质化，降低 AI 检测风险（句式/词汇/情绪/段落四个维度天然多样化） | 每章增加 1 次插曲改编 LLM 调用；风格库需手动构建（Phase 1 目标 30-100 篇） |
| ADR-008 | L3 检索采用 ID 直查 + 关键词补充，精简注入 ≤40K token | 大纲已含 CharacterIds/SettingIds，直查确保不遗漏；精简注入缓解 Lost in the Middle 问题 | L3 注入量受限，可能漏掉语义相关但未显式关联的条目 |
| ADR-009 | LLM 接口支持流式输出 | 章节写作 30-120 秒，流式输出根本性改善 UX | 流式模式下重试/降级策略更复杂 |
| ADR-010 | 全面开发前先做 30 章规模记忆一致性 PoC | 三层记忆架构是技术核心，必须在大规模投入前验证可行性 | 增加 1-2 周前置时间 |

---

## 2. 技术栈明细

### 2.1 运行时与框架

| 技术 | 版本 | 用途 | 选型理由 |
|------|------|------|---------|
| .NET SDK | 9.0 | 运行时 | 与 Windows 桌面生态最佳集成，避免 .NET 8 EOL (2026.11) 风险 |
| WPF | .NET 9 内置 | UI 框架 | 原生 Windows 渲染，无 WebView 开销，复杂文本编辑器可实现 |
| C# | 13.0 | 开发语言 | .NET 9 默认，record/required/primary constructor/params 减少样板代码 |

### 2.2 核心依赖

| NuGet 包 | 最低版本 | 用途 |
|----------|---------|------|
| CommunityToolkit.Mvvm | 8.4.0 | 源生成器驱动的 MVVM：`[ObservableProperty]`、`[RelayCommand]`、`IMessenger` |
| Microsoft.EntityFrameworkCore.Sqlite | 9.0.0 | SQLite ORM，Code-First 迁移 |
| MaterialDesignInXamlToolkit | 5.0.0 | UI 控件库（三栏布局、对话框、主题） |
| Markdig | 0.37.0 | Markdown → FlowDocument 转换，编辑器预览 |
| AvalonEdit | 6.3.0 | WPF 文本编辑器组件，语法高亮、行号、自定义高亮规则（伏笔标记） |
| Microsoft.Extensions.DependencyInjection | 9.0.0 | DI 容器，全项目统一注入 |
| Microsoft.Extensions.Http | 9.0.0 | HttpClientFactory + 重试策略（配合 Polly） |
| Polly | 8.0.0 | 指数退避重试、断路器、降级策略 |
| System.Security.Cryptography.ProtectedData | 9.0.0 | Windows DPAPI，API Key 本地加密存储 |
| Serilog | 4.0.0 | 结构化日志（文件 + 控制台） |
| xUnit | 2.9.0 | 单元测试 + 集成测试 |
| Moq | 4.20.0 | 接口模拟 |
| FluentAssertions | 6.12.0 | 可读断言 |

### 2.3 LLM API 集成

| 服务 | API 端点格式 | 模型 |
|------|-------------|------|
| DeepSeek | `https://api.deepseek.com/v1/chat/completions` | deepseek-v4-pro (默认) / deepseek-v4-flash |
| 通义千问 | `https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions` | qwen-plus / qwen-max |
| Moonshot (Kimi) | `https://api.moonshot.cn/v1/chat/completions` | moonshot-v1-8k/32k/128k |

三家 API 均兼容 OpenAI Chat Completions 格式，适配层统一使用 `HttpClient` + JSON 序列化。

---

## 3. 项目结构与模块划分

### 3.1 完整目录树

```
NovelWriter/
├── NovelWriter.App/                    # WPF 主工程
│   ├── App.xaml                        # 应用入口、资源字典合并
│   ├── App.xaml.cs                     # DI 容器初始化、全局异常处理
│   ├── Views/
│   │   ├── ShellWindow.xaml            # 主窗口 (三栏布局 Host)
│   │   ├── NavigationTreeView.xaml     # 左栏: 项目导航树
│   │   ├── EditorView.xaml             # 中栏: Markdown 编辑器
│   │   ├── EditorPreviewView.xaml      # 中栏: 预览面板
│   │   ├── ContextPanelView.xaml       # 右栏: 上下文面板
│   │   ├── ReviewPanelView.xaml        # 右栏: 评审面板
│   │   ├── PipelineStatusView.xaml     # 底部: 流水线状态
│   │   ├── SettingsDialog.xaml         # 设置对话框 (API Key 等)
│   │   └── Dialogs/
│   │       ├── ConfirmationDialog.xaml # 人工确认闸门对话框
│   │       ├── ForeshadowingDialog.xaml# 伏笔确认对话框
│   │       └── SelfCheckReportDialog.xaml
│   ├── ViewModels/
│   │   ├── ShellViewModel.cs           # 主窗口 VM (三栏协调)
│   │   ├── NavigationTreeViewModel.cs  # 左栏 VM
│   │   ├── EditorViewModel.cs          # 中栏 VM (编辑+预览切换)
│   │   ├── ContextPanelViewModel.cs    # 右栏 VM (L1/L2/L3 注入可视化)
│   │   ├── ReviewPanelViewModel.cs     # 右栏 VM (评审结果展示)
│   │   ├── PipelineStatusViewModel.cs  # 底部 VM
│   │   └── SettingsViewModel.cs
│   ├── Controls/
│   │   ├── MarkdownEditor.xaml         # 自绘编辑区 (语法高亮、伏笔标记语法)
│   │   ├── TokenBudgetBar.xaml         # Token 使用量进度条
│   │   ├── MemoryTreeView.xaml         # 三层记忆可视化树
│   │   └── PipelineProgress.xaml       # 流水线阶段指示器
│   ├── Converters/
│   │   ├── StatusToColorConverter.cs
│   │   ├── TokenToPercentageConverter.cs
│   │   └── MarkdownToFlowDocumentConverter.cs
│   ├── Behaviors/
│   │   └── ForeshadowingHighlightBehavior.cs
│   ├── Services/
│   │   ├── IDialogService.cs
│   │   └── DialogService.cs
│   └── appsettings.json
│
├── NovelWriter.Core/                   # 领域核心层 (无外部依赖)
│   ├── Entities/
│   │   ├── Project.cs
│   │   ├── Outline.cs
│   │   ├── Chapter.cs
│   │   ├── Synopsis.cs
│   │   ├── Review.cs
│   │   ├── Persona.cs
│   │   ├── StyleProfile.cs              # 写作风格档案
│   │   ├── InterludeEntry.cs            # 插曲条目
│   │   ├── StyleUsageLog.cs             # 风格使用记录
│   │   └── InterludeUsageLog.cs         # 插曲使用记录
│   ├── ValueObjects/
│   │   ├── ChapterId.cs
│   │   ├── ProjectId.cs
│   │   ├── CharacterId.cs              # CHAR_{NNN}
│   │   ├── WorldSettingId.cs            # WORLD_{NNN}
│   │   ├── ForeshadowingId.cs          # FS_{NNN}
│   │   ├── ArcId.cs                    # ARC_{NNN}
│   │   ├── TokenBudget.cs
│   │   ├── VersionNumber.cs
│   │   └── ReviewScore.cs
│   ├── Memory/                         # 三层记忆实体
│   │   ├── CharacterProfile.cs         # L3: 人物档案
│   │   ├── WorldSetting.cs             # L3: 世界观条目
│   │   ├── Foreshadowing.cs            # L2: 伏笔追踪
│   │   ├── ArcTracker.cs               # L2: 故事弧线
│   │   ├── SubplotTracker.cs           # L2: 支线状态
│   │   ├── ChapterContext.cs           # L1: 写作上下文 (非持久化，每次编译生成)
│   │   ├── ChapterSummary.cs           # L1: 章节摘要 (持久化到 ChapterSummaries 表)
│   │   ├── L2State.cs                  # L2: 当前活跃 L2 快照 (ExtractAfterChapter 入参)
│   │   ├── L2VolumeState.cs            # L2: 卷级全量记忆聚合 (CompressVolume 入参)
│   │   ├── ForeshadowingArchive.cs     # L2→L3 归档
│   │   └── VolumeSummary.cs            # L2→L3 归档
│   ├── Enums/
│   │   ├── ProjectStatus.cs
│   │   ├── ChapterStatus.cs
│   │   ├── ForeshadowingStatus.cs      # Active / Resolved / Abandoned
│   │   ├── ForeshadowingPriority.cs    # High / Low
│   │   ├── PlantedBy.cs                # Manual / AutoDetected
│   │   ├── ArcMilestoneStatus.cs
│   │   ├── PipelineStage.cs
│   │   ├── Confidence.cs               # High / Low
│   │   ├── DeviationSeverity.cs        # Critical / High / Medium / Low
│   │   └── AiRiskLevel.cs               # Low / Medium / High / Critical (AI检测)
│   ├── Interfaces/
│   │   ├── IProjectRepository.cs
│   │   ├── IChapterRepository.cs
│   │   ├── IOutlineRepository.cs
│   │   ├── IMemoryRepository.cs
│   │   ├── INovelWriterDbContext.cs
│   │   ├── ILlmAdapter.cs
│   │   ├── IPipelineStage.cs
│   │   ├── IReviewerAgent.cs
│   │   ├── IMemoryChangeNotifier.cs
│   │   ├── IStyleLibraryRepository.cs
│   │   └── IInterludeRepository.cs
│   ├── DomainServices/
│   │   ├── TokenBudgetCalculator.cs
│   │   ├── KeywordExtractor.cs
│   │   ├── ReviewScoreCalculator.cs
│   │   └── ConfidenceEvaluator.cs
│   ├── Dtos/
│   │   ├── ChatMessage.cs                 # 多轮对话消息 record
│   │   ├── MemoryExtractionResult.cs    # Memory Manager 输出
│   │   ├── VolumeCompressionReport.cs   # L2→L3 压缩报告
│   │   ├── CompiledContext.cs           # ContextWindow 编译结果
│   │   ├── AggregatedReview.cs          # 多Persona评审聚合
│   │   ├── DetectionReport.cs           # AI检测报告
│   │   ├── StyleExtractionResult.cs     # 风格档案提取结果
│   │   ├── ReviewResult.cs              # 单个 Persona 评审输出
│   │   ├── TopicSelectionResult.cs      # Stage01 题材选择输出
│   │   └── InterludePromptResult.cs     # 插曲改编输出（供 PipelineState 引用）
│   ├── Exceptions/
│   │   └── LlmUnavailableException.cs
│   └── Events/
│       ├── MemoryWriteRequested.cs
│       ├── MemoryConfirmed.cs
│       ├── PipelineStageChanged.cs
│       └── ForeshadowingDetected.cs
│
├── NovelWriter.Engine/                 # 业务引擎层
│   ├── Pipeline/
│   │   ├── PipelineOrchestrator.cs
│   │   ├── Stage01_TopicSelection.cs
│   │   ├── Stage02_SynopsisWriting.cs
│   │   ├── Stage03_OutlineWriting.cs
│   │   ├── Stage04_PreWritePrepare.cs
│   │   ├── Stage05_ChapterGenerate.cs
│   │   ├── Stage06_MemoryExtract.cs
│   │   ├── Stage07_ReviewPolish.cs
│   │   ├── Stage08_DataEnhancement.cs    # Phase 2
│   │   └── Stage09_Publish.cs            # Phase 3
│   ├── Memory/
│   │   ├── MemoryManagerAgent.cs
│   │   ├── L1Compiler.cs
│   │   ├── L2Updater.cs
│   │   ├── L3Retriever.cs
│   │   ├── L2ToL3Compressor.cs
│   │   └── MemoryChangeValidator.cs
│   ├── ContextWindow/
│   │   ├── ContextWindowCompiler.cs
│   │   ├── SystemPromptBuilder.cs
│   │   ├── TokenCounter.cs
│   │   └── PromptTemplateEngine.cs
│   ├── Llm/
│   │   ├── LlmAdapterFactory.cs
│   │   ├── LlmAdapterBase.cs
│   │   ├── DeepSeekAdapter.cs
│   │   ├── QwenAdapter.cs
│   │   ├── KimiAdapter.cs
│   │   ├── LlmDegradationPolicy.cs
│   │   ├── RetryPolicyFactory.cs
│   │   └── LlmResponseParser.cs
│   ├── Review/
│   │   ├── ReviewOrchestrator.cs
│   │   ├── ReviewerAgent.cs
│   │   ├── PersonaLoader.cs
│   │   └── ReviewAggregator.cs
│   ├── AiDetection/                      # AI 检测预检（平台合规）
// 注: 目录名 AiDetection(领域) vs 类前缀 AiDetector(实体), 语义差异有意为之
│   │   ├── AiDetectorBase.cs             # 抽象基类: 统一检测接口
│   │   ├── StatisticalDetector.cs        # 统计特征检测 (句长/词汇多样性/情绪曲线)
│   │   ├── ExternalDetector.cs           # 外部检测器调用 (朱雀等API)
│   │   ├── LlmAssistedDetector.cs       # LLM辅助检测 (High+ 二次确认)
│   │   └── DetectionReport.cs            # 检测报告模型
│   ├── DataCollection/                  # Phase 2: 网文平台数据采集
│   │   ├── IPlatformCollector.cs         # 统一采集器接口
│   │   ├── BaseCollector.cs              # 公共服务: 代理轮换/延迟/重试
│   │   ├── QidianCollector.cs            # 起点中文网: Playwright + HTML 解析
│   │   ├── FanqieCollector.cs            # 番茄小说: 内部 API 调用
│   │   ├── JinjiangCollector.cs          # 晋江文学城: Playwright + HTML 解析
│   │   ├── ProxyPoolManager.cs           # 住宅代理池管理
│   │   ├── CollectionScheduler.cs        # 定时采集调度 (日/周/月)
│   │   ├── TrendAnalyzer.cs              # 趋势分析 (环比增速/新兴题材)
│   │   └── RecommendationEngine.cs       # 选材建议 (基于热榜数据)
│   ├── SelfCheck/
│   │   ├── SelfCheckRunner.cs
│   │   ├── IncrementalSelfCheck.cs
│   │   ├── CharacterConsistencyChecker.cs
│   │   ├── SettingViolationChecker.cs
│   │   └── DeviationReportBuilder.cs
│   └── Serialization/
│       ├── YamlMemorySerializer.cs
│       └── YamlMemoryParser.cs
│   ├── Style/                             # 风格与插曲注入
│   │   ├── StyleInjector.cs               # 随机风格选择 + Prompt 注入
│   │   ├── StyleProfilePromptBuilder.cs   # 风格档案 → Prompt 指令转换
│   │   ├── StyleExtractionAgent.cs        # LLM 风格档案提取 (单次调用)
│   │   └── InterludeInjector.cs           # 插曲随机选择 + LLM 改编 + Prompt 注入
│
├── NovelWriter.Storage/                # 持久化层
│   ├── NovelWriterDbContext.cs
│   ├── Configurations/
│   │   ├── ProjectConfiguration.cs
│   │   ├── ChapterConfiguration.cs
│   │   ├── OutlineConfiguration.cs
│   │   ├── ForeshadowingConfiguration.cs
│   │   ├── ForeshadowingArchiveConfiguration.cs
│   │   ├── CharacterProfileConfiguration.cs
│   │   ├── WorldSettingConfiguration.cs
│   │   ├── ArcTrackerConfiguration.cs
│   │   ├── SubplotTrackerConfiguration.cs
│   │   ├── ChapterSummaryConfiguration.cs
│   │   ├── VolumeSummaryConfiguration.cs
│   │   ├── SynopsisConfiguration.cs
│   │   ├── PersonaConfiguration.cs
│   │   ├── ReviewConfiguration.cs
│   │   ├── StyleProfileConfiguration.cs
│   │   └── InterludeEntryConfiguration.cs
│   ├── Repositories/
│   │   ├── ProjectRepository.cs
│   │   ├── ChapterRepository.cs
│   │   ├── OutlineRepository.cs
│   │   ├── MemoryRepository.cs
│   │   ├── ReviewRepository.cs
│   │   ├── StyleLibraryRepository.cs
│   │   └── InterludeRepository.cs
│   ├── Migrations/
│   └── BackupService.cs
│
├── NovelWriter.Tests/
│   ├── UnitTests/
│   │   ├── Core/
│   │   │   ├── TokenBudgetCalculatorTests.cs
│   │   │   ├── ConfidenceEvaluatorTests.cs
│   │   │   └── ReviewScoreCalculatorTests.cs
│   │   ├── Engine/
│   │   │   ├── L1CompilerTests.cs
│   │   │   ├── L2UpdaterTests.cs
│   │   │   ├── L3RetrieverTests.cs
│   │   │   ├── ContextWindowCompilerTests.cs
│   │   │   ├── L2ToL3CompressorTests.cs
│   │   │   ├── ReviewAggregatorTests.cs
│   │   │   ├── StyleInjectorTests.cs
│   │   │   └── InterludeInjectorTests.cs
│   │   └── Storage/
│   │       ├── MemoryRepositoryTests.cs
│   │       └── BackupServiceTests.cs
│   ├── IntegrationTests/
│   │   ├── LlmAdapterIntegrationTests.cs
│   │   ├── PipelineOrchestratorTests.cs
│   │   └── MemoryManagerAgentTests.cs
│   └── TestHelpers/
│       ├── MockLlmHandler.cs
│       ├── TestDataFactory.cs
│       └── InMemoryDbContext.cs
│
└── NovelWriter.sln
```

### 3.2 模块职责矩阵

| 模块 | 单一职责 | 不应做的事 |
|------|---------|-----------|
| **App** | 用户界面渲染、输入捕获、对话框交互 | 不应包含业务逻辑、不直接调用 LLM API |
| **Core** | 领域实体、值对象、仓储接口、领域事件定义 | 不应引用任何 I/O 库、不应包含 SQL/HTTP 代码 |
| **Engine** | 流水线编排、记忆管理、LLM 调用、评审调度、风格注入、插曲改编 | 不应包含 UI 代码、不应直接操作 DbContext |
| **Storage** | 实现 Core 定义的仓储接口，管理 SQLite 数据 | 不应包含业务逻辑、不应依赖 Engine |
| **Tests** | 验证各层行为正确性 | — |

---

## 4. 模块通信设计

### 4.1 依赖关系图

```
App ──→ Engine ──→ Core
  │        │
  │        ├──→ Core.Interfaces.ILlmAdapter (实现在 Engine/Llm/)
  │        └──→ Core.Interfaces.IPipelineStage (实现在 Engine/Pipeline/)
  │
  ├──→ Storage ──→ Core.Interfaces.I*Repository
  │
  └──→ Core (实体类型引用)
```

### 4.2 DI 注册

```csharp
// App.xaml.cs 中初始化
services.AddSingleton<ILlmAdapterFactory, LlmAdapterFactory>();
services.AddSingleton<LlmDegradationPolicy>();
services.AddScoped<INovelWriterDbContext, NovelWriterDbContext>();
services.AddDbContextFactory<NovelWriterDbContext>();   // BackupService 使用
services.AddScoped<IProjectRepository, ProjectRepository>();
services.AddScoped<IMemoryRepository, MemoryRepository>();
services.AddScoped<IChapterRepository, ChapterRepository>();
services.AddScoped<IOutlineRepository, OutlineRepository>();
services.AddScoped<MemoryManagerAgent>();
services.AddScoped<ContextWindowCompiler>();   // 仅负责记忆编译，不含风格/插曲
services.AddScoped<PipelineOrchestrator>();    // 在 Stage04 中编排 Compiler + Style + Interlude
services.AddScoped<ReviewOrchestrator>();
services.AddScoped<SelfCheckRunner>();
services.AddScoped<StyleInjector>();           // 由 PipelineOrchestrator 独立调用，非 Compiler 依赖
services.AddScoped<InterludeInjector>();       // 由 PipelineOrchestrator 独立调用，非 Compiler 依赖
services.AddScoped<IStyleLibraryRepository, StyleLibraryRepository>();
services.AddScoped<IInterludeRepository, InterludeRepository>();
services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);
```

### 4.3 通信方式

| 通信场景 | 方式 | 理由 |
|---------|------|------|
| App ↔ Engine | DI (IServiceProvider) + 异步方法 | 标准分层架构，ViewModel 通过注入的 Service 发起调用 |
| Engine 内部模块间 | 直接方法调用 + 领域事件 | 同一进程，无需中介；领域事件用于解耦 Memory 模块与 Pipeline |
| Engine → Core | 直接引用 | Engine 依赖 Core，单向 |
| Engine → Storage | 通过 Core 接口 (DI) | 依赖倒置，Core 定义接口，Storage 实现 |
| App 内部 View ↔ ViewModel | WPF Binding + `IMessenger` | 标准 MVVM；跨 ViewModel 消息用 Messenger |
| Engine → LLM API | `HttpClient` (via `IHttpClientFactory`) | 外部 HTTP 调用 |
| 人工确认流程 | 异步接口 via `IDialogService`（UI 线程 Modal 对话框，Engine 侧通过 TaskCompletionSource 异步等待用户响应） | 确认在 UI 线程，Engine 通过接口发起而不依赖 View |

### 4.4 关键接口定义

```csharp
// --- LLM 适配器接口 (Core/Interfaces/ILlmAdapter.cs) ---
public interface ILlmAdapter
{
    string ModelName { get; }
    int MaxContextTokens { get; }
    int RecommendedOutputTokens { get; }

    /// <summary>
    /// 非流式单轮对话。用于记忆提取、评审等需要完整 JSON 输出的场景。
    /// </summary>
    Task<string> ChatAsync(
        string systemPrompt,
        string userMessage,
        CancellationToken ct = default);

    /// <summary>
    /// 非流式多轮对话。
    /// </summary>
    Task<string> ChatAsync(
        IEnumerable<ChatMessage> messages,
        CancellationToken ct = default);

    /// <summary>
    /// 流式单轮对话。用于章节写作等长输出场景，UI 可逐步渲染文本。
    /// 返回的 IAsyncEnumerable 每次 yield 一个文本片段。
    /// </summary>
    IAsyncEnumerable<string> StreamChatAsync(
        string systemPrompt,
        string userMessage,
        CancellationToken ct = default);
}

// --- 多轮对话消息 (Core/Dtos/ChatMessage.cs) ---
public record ChatMessage(string Role, string Content);

// --- 记忆提取结果 (Core/Dtos/MemoryExtractionResult.cs) ---
public class MemoryExtractionResult
{
    // 任务 A: L1 摘要
    public ChapterSummary L1Summary { get; set; } = null!;

    // 任务 B: 伏笔回收检测
    public List<ForeshadowingResolution> ForeshadowingResolutions { get; set; } = new();

    // 任务 C: 新伏笔候选
    public List<ForeshadowingCandidate> NewForeshadowings { get; set; } = new();

    // 任务 D: ArcTracker milestone 进度
    public List<ArcMilestoneUpdate> ArcUpdates { get; set; } = new();

    // 任务 E: SubplotTracker 更新
    public List<SubplotUpdate> SubplotUpdates { get; set; } = new();

    // 任务 F: L3 人物/设定变更建议
    public List<L3ChangeProposal> L3ChangeProposals { get; set; } = new();

    // 增量 SelfCheck: traits.forbidden 违规 (章节号 % 5 == 0 时填充)
    public List<Deviation>? ForbiddenTraitViolations { get; set; }

    // 确认闸门分流
    public List<ConfirmationItem> AutoConfirmedItems { get; set; } = new();
    public List<ConfirmationItem> NeedsConfirmationItems { get; set; } = new();
}

#### 4.4.1 支撑类型定义

MemoryExtractionResult 引用的 DTO 类型，均在 `Core/Dtos/` 下定义。此处仅列出类型名和职责，字段定义留给实施阶段。

| 类型 | 职责 |
|------|------|
| `ForeshadowingResolution` | 伏笔回收检测输出：关联伏笔 ID、置信度、回收章号 |
| `ForeshadowingCandidate` | 新伏笔候选：描述、优先级、关联合人物/设定 |
| `ArcMilestoneUpdate` | ArcTracker 里程碑进度更新 |
| `SubplotUpdate` | SubplotTracker 更新：是否提及、dangling 计数（纯规则计算） |
| `L3ChangeProposal` | L3 人物/设定变更建议：目标实体、变更描述、置信度 |
| `Deviation` | SelfCheck 偏差检测输出：实体 ID、严重度、违规描述、检测章号 |
| `ConfirmationItem` | 确认闸门条目：类型（新伏笔/回收/L3变更等）+ 摘要 + 关联负载 |
| `ConfirmationDecision` | 用户确认决策：条目 ID + Approved/Rejected + 可选备注 |
| `IMemoryEntry` | L3 检索返回的抽象接口，统一 CharacterProfile / WorldSetting / VolumeSummary / ForeshadowingArchive 的检索返回 |
| `TopicSelectionResult` | Stage01 题材选择输出：核心冲突 + 目标读者 + 差异化卖点 + 初始 WorldSetting 建议 |
| `InterludePromptResult` | 插曲改编输出：插曲 ID + 改编文本 + 插入位置提示 |

// --- 流水线阶段接口 (Core/Interfaces/IPipelineStage.cs) ---
public interface IPipelineStage
{
    PipelineStage Stage { get; }

    /// <summary>
    /// 执行本阶段逻辑。
    /// 需要用户确认时，返回 PipelineResult.AwaitingConfirmation(...)。
    /// 阶段恢复时通过 context.State.PendingDecisions 读取 ConfirmationDecision 列表。
    /// </summary>
    Task<PipelineResult> ExecuteAsync(PipelineContext context, CancellationToken ct = default);
}

public record PipelineContext
{
    public required ProjectId ProjectId { get; init; }
    public PipelineStage CurrentStage { get; set; }
    public PipelineState State { get; init; } = new();
}

/// <summary>
/// 强类型流水线状态，替代 Dictionary&lt;string, object&gt; Bag。
/// 阶段间数据传递通过强类型属性，消除运行时类型错误。
/// </summary>
public class PipelineState
{
    // === Stage01 产出 ===
    public TopicSelectionResult? TopicSelection { get; set; }

    // === Stage02 产出 ===
    public string? Synopsis { get; set; }

    // === Stage03 产出 ===
    public IReadOnlyList<Outline>? Outlines { get; set; }

    // === 循环体状态 ===
    public int CurrentChapterNumber { get; set; }
    public int CurrentVolumeNumber { get; set; }

    // === 单章编译产物 (Stage04 → Stage05) ===
    public CompiledContext? CompiledContext { get; set; }
    public StyleProfile? StyleProfile { get; set; }
    public InterludePromptResult? InterludePrompt { get; set; }

    // === 单章写作产物 (Stage05 → Stage06/07) ===
    public Chapter? ChapterDraft { get; set; }
    public MemoryExtractionResult? ExtractionResult { get; set; }
    public AggregatedReview? AggregatedReview { get; set; }
    public DetectionReport? DetectionReport { get; set; }

    // === 跨章状态 ===
    public List<IMemoryEntry>? L3SearchCache { get; set; }
    public List<Deviation> IncrementalViolations { get; set; } = new();

    // === 确认恢复 ===
    public IReadOnlyList<ConfirmationDecision>? PendingDecisions { get; set; }
}

/// <summary>
/// 流水线阶段执行结果。
/// ConfirmationItems 非空 = 流水线暂停，等待用户决策后通过 ResumeWithDecisionsAsync 恢复。
/// 不使用异常做控制流。
/// </summary>
public record PipelineResult
{
    public bool Success { get; init; }
    public PipelineStage? NextStage { get; init; }  // null = 流水线完成或暂停（由 ConfirmationItems 是否为空区分）
    public List<ConfirmationItem> ConfirmationItems { get; init; } = new();   // 需要用户确认的条目
    public List<ConfirmationItem> AutoConfirmedItems { get; init; } = new();  // 自动通过，仅信息展示
    public List<DomainEvent> Events { get; init; } = new();
    public bool RequiresConfirmation => ConfirmationItems.Count > 0;
    public static PipelineResult Completed => new() { Success = true, NextStage = null };
}

#### 4.4.2 PipelineState 属性契约

下表记录 PipelineState 中所有属性的读写阶段。这是设计契约，目的是让阶段实现明确数据依赖关系，让需求提取阶段能验证阶段间数据流的完整性。

| 属性 | 类型 | 写入阶段 | 读取阶段 | 生命周期 | 说明 |
|------|------|---------|---------|---------|------|
| `TopicSelection` | `TopicSelectionResult` | Stage01 | Stage02, Stage03 | 单项目 | 题材选择结果 |
| `Synopsis` | `string` | Stage02 | Stage03, Stage05 | 单项目 | 梗概正文 |
| `Outlines` | `IReadOnlyList<Outline>` | Stage03 | Stage04, Stage05, Stage06 | 单项目 | 分章大纲列表 |
| `CurrentChapterNumber` | `int` | Stage03, Stage04 | Stage04-07 | 单章 | 当前章节号 |
| `CurrentVolumeNumber` | `int` | Stage03, Stage04 | Stage04, Stage06 | 单卷 | 当前卷号 |
| `CompiledContext` | `CompiledContext` | Stage04 | Stage05 | 单章 | ContextWindow 编译结果 |
| `StyleProfile` | `StyleProfile` | Stage04 | Stage05 | 单章 | 随机选择的风格档案 |
| `InterludePrompt` | `InterludePromptResult` | Stage04 | Stage05 | 单章 | 插曲改编结果 |
| `ChapterDraft` | `Chapter` | Stage05 | Stage06, Stage07 | 单章 | 本章草稿 |
| `ExtractionResult` | `MemoryExtractionResult` | Stage06 | 确认闸门 | 单章 | 记忆提取输出 |
| `AggregatedReview` | `AggregatedReview` | Stage07 | 决策 | 单章 | 评审聚合结果 |
| `DetectionReport` | `DetectionReport` | Stage07 | 展示 | 单章 | AI 检测报告 |
| `L3SearchCache` | `List<IMemoryEntry>` | Stage04 | 缓存复用 | 单章 | L3 检索缓存 |
| `IncrementalViolations` | `List<Deviation>` | Stage06 (每5章) | Stage06 (卷末) | 单卷 | 增量违规累积 |
| `PendingDecisions` | `IReadOnlyList<ConfirmationDecision>` | UI 输入 | Stage06 恢复 | 单次恢复 | 用户确认决策 |

// --- 记忆仓储接口 (Core/Interfaces/IMemoryRepository.cs) ---
public interface IMemoryRepository
{
    // L3
    Task<IReadOnlyList<CharacterProfile>> GetCharacterProfilesAsync(ProjectId projectId);
    Task<CharacterProfile?> GetLatestCharacterProfileAsync(CharacterId id);
    Task AddCharacterProfileVersionAsync(CharacterProfile profile);
    Task<IReadOnlyList<WorldSetting>> GetWorldSettingsAsync(ProjectId projectId);
    Task<WorldSetting?> GetLatestWorldSettingAsync(WorldSettingId id);
    Task AddWorldSettingAsync(WorldSetting setting);

    // L2
    Task<IReadOnlyList<Foreshadowing>> GetActiveForeshadowingsAsync(ProjectId projectId, int volume);
    Task AddForeshadowingAsync(Foreshadowing fs);
    Task UpdateForeshadowingAsync(Foreshadowing fs);
    Task<IReadOnlyList<ArcTracker>> GetArcTrackersAsync(ProjectId projectId);
    Task UpdateArcTrackerAsync(ArcTracker arc);
    Task<IReadOnlyList<SubplotTracker>> GetSubplotTrackersAsync(ProjectId projectId, int volume);

    // L3 检索 (ID 直查 + 关键词补充)
    Task<IReadOnlyList<IMemoryEntry>> SearchL3ByIdsAsync(
        ProjectId projectId,
        IEnumerable<CharacterId> characterIds,
        IEnumerable<WorldSettingId> settingIds,
        int upToChapter);    // 剧透过滤: 排除关联章节 > upToChapter 的条目（global=true 的 WorldSetting 除外）

    Task<IReadOnlyList<IMemoryEntry>> SearchL3ByKeywordsAsync(
        ProjectId projectId,
        IEnumerable<string> keywords,
        int maxTokens,
        int upToChapter);    // 关键词补充检索 tags/aliases，与 ID 直查结果合并去重

    // L2 卷级操作
    Task<L2VolumeState> GetAllL2ForVolumeAsync(ProjectId projectId, int volumeNumber);
    Task DeleteArchivedForeshadowingsAsync(IEnumerable<ForeshadowingId> ids);

    // 归档
    Task AddForeshadowingArchiveAsync(ForeshadowingArchive archive);
    Task AddVolumeSummaryAsync(VolumeSummary summary);

    // 事务写入（供 ConfirmationGate 调用）
    Task WriteMemoryChangesAsync(
        MemoryExtractionResult extraction,
        IReadOnlyList<ConfirmationDecision> decisions);
}

// --- DbContext 接口 (用于 DI 和单元测试替换) ---
public interface INovelWriterDbContext
{
    DbSet<Project> Projects { get; }
    DbSet<Chapter> Chapters { get; }
    DbSet<Outline> Outlines { get; }
    DbSet<Foreshadowing> Foreshadowings { get; }
    DbSet<ArcTracker> ArcTrackers { get; }
    DbSet<SubplotTracker> SubplotTrackers { get; }
    DbSet<CharacterProfile> CharacterProfiles { get; }
    DbSet<WorldSetting> WorldSettings { get; }
    DbSet<ChapterSummary> ChapterSummaries { get; }
    DbSet<ForeshadowingArchive> ForeshadowingArchives { get; }
    DbSet<VolumeSummary> VolumeSummaries { get; }
    DbSet<Synopsis> Synopses { get; }
    DbSet<Review> Reviews { get; }
    DbSet<Persona> Personas { get; }
    DbSet<StyleProfile> StyleProfiles { get; }
    DbSet<StyleUsageLog> StyleUsageLogs { get; }
    DbSet<InterludeEntry> InterludeEntries { get; }
    DbSet<InterludeUsageLog> InterludeUsageLogs { get; }
    DatabaseFacade Database { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
```

### 4.5 风格与插曲仓储接口

```csharp
// --- 风格库仓储接口 (Core/Interfaces/IStyleLibraryRepository.cs) ---
public interface IStyleLibraryRepository
{
    Task<StyleProfile?> GetByIdAsync(string id);
    Task<IReadOnlyList<StyleProfile>> GetAllAsync();
    Task<StyleProfile> GetRandomStyleAsync();
    Task<StyleProfile> GetRandomStyleExcludingAsync(IEnumerable<string> excludeIds);
    Task AddAsync(StyleProfile profile);
    Task AddRangeAsync(IEnumerable<StyleProfile> profiles);
    Task<IReadOnlyList<string>> GetUsedStyleIdsForVolumeAsync(
        ProjectId projectId, int volumeNumber);
    Task RecordUsageAsync(ProjectId projectId, int chapterNumber, string styleId);
    Task<int> GetCountAsync();
}

// --- 插曲仓储接口 (Core/Interfaces/IInterludeRepository.cs) ---
public interface IInterludeRepository
{
    Task<InterludeEntry?> GetByIdAsync(string id);
    Task<IReadOnlyList<InterludeEntry>> GetAllAsync();
    Task<InterludeEntry> GetRandomInterludeAsync(
        string? genre = null, IEnumerable<string>? excludeIds = null);
    Task AddAsync(InterludeEntry entry);
    Task AddRangeAsync(IEnumerable<InterludeEntry> entries);
    Task<IReadOnlyList<string>> GetUsedInterludeIdsForVolumeAsync(
        ProjectId projectId, int volumeNumber);
    Task RecordUsageAsync(ProjectId projectId, int chapterNumber,
        string interludeId, string adaptedText, double insertPosition);
    Task<int> GetCountAsync();
}
```

### 4.6 风格档案与插曲条目实体

```csharp
// --- 风格档案实体 (Core/Entities/StyleProfile.cs) ---
public class StyleProfile
{
    public string Id { get; init; }          // STYLE_{NNN}
    public string SourceTitle { get; init; }
    public string SourceAuthor { get; init; }
    public string SourceType { get; init; }   // public_domain / creative_commons / manual
    public int SourceWordCount { get; init; }
    public string ProfileJson { get; init; }  // 结构化风格档案 JSON
    public string Tags { get; init; }         // JSON array: 风格标签
    public int UsageCount { get; set; }
    public int? LastUsedChapter { get; set; }
    public DateTime CreatedAt { get; init; }
}

// --- 插曲条目实体 (Core/Entities/InterludeEntry.cs) ---
public class InterludeEntry
{
    public string Id { get; init; }          // EP_{NNN}
    public string SourceType { get; init; }   // historical / news / anecdote / trivia
    public string Source { get; init; }
    public string CoreFact { get; init; }     // 核心事实 (≤50字)
    public string NarrativeHook { get; init; } // 叙事钩子
    public string AdaptableThemes { get; init; } // JSON array
    public string SuggestedGenres { get; init; } // JSON array
    public int UsageCount { get; set; }
    public DateTime CreatedAt { get; init; }
}
```

---

## 5. 关键技术设计

### 5.1 ContextWindow 编译器

**职责**: 在每章写作前，将 L1/L2/L3 记忆 + 系统指令 + 章节大纲组装成 System Prompt（≤ 160K token）。

```
编译流程:

  1. 编译 L1 摘要                                         (~25K token)
     ├── 从 DB 读取最近 5 章正文
     ├── 调用 L1Compiler 生成每章 400-600 字摘要
     └── 组装 ChapterContext (recent_summaries + current_scene_state)

  2. 全量注入 L2                                           (~50K token)
     └── 从 DB 读取当前卷的全部活跃伏笔/弧线/支线

  3. 按需检索 L3                                           (≤40K token)
     ├── 主路径: 大纲 CharacterIds/SettingIds → 直查 L3 实体最新版本
     ├── 辅路径: KeywordExtractor 提取关键词 → 补充检索 tags/aliases
     ├── 合并去重，剧透过滤: 排除 related_chapters 全部 > N 的条目（global=true 除外）
     └── 按相关性排序，截断到 40K token

  4. 组装系统指令                                          (~15K token)
     ├── 人物行为边界 (traits.forbidden 优先于 traits.primary，负面约束先于正面描述)
     ├── 写作风格约束 + 当前卷核心冲突
     └── 本章大纲

  5. User Message 重复注入关键约束
     ├── traits.forbidden 在 User Message 中再次提及（缓解 Lost in the Middle）
     └── 本章涉及的关键设定简要重复

  6. Token 计数校验 → 超出预算则压缩检索结果重试

输出: System Prompt + User Message + TokenBudget 快照
```

```csharp
public class ContextWindowCompiler
{
    // --- 核心依赖 (仅记忆编译相关) ---
    private readonly IMemoryRepository _memoryRepo;
    private readonly IChapterRepository _chapterRepo;
    private readonly IOutlineRepository _outlineRepo;
    private readonly L1Compiler _l1Compiler;
    private readonly L3Retriever _l3Retriever;
    private readonly TokenCounter _tokenCounter;
    private readonly SystemPromptBuilder _promptBuilder;

    // 注: StyleInjector 和 InterludeInjector 不在此处调用。
    // 它们由 PipelineOrchestrator 在 Stage04 中按以下顺序独立编排:
    //   1. CompileAsync → CompiledContext (System Prompt 基础版 + User Message 基础版)
    //   2. StyleInjector.SelectRandomStyleAsync → 追加风格约束到 CompiledContext.SystemPrompt
    //   3. InterludeInjector.PrepareInterludeAsync → 追加插曲到 CompiledContext.UserMessage
    // 编译器职责单一 (仅记忆编译), 风格/插曲是可选的装饰步骤。

    /// <summary>
    /// 编译 L1+L2+L3+大纲 → System Prompt 基础版本 (不含风格约束和插曲)。
    /// 风格约束和插曲由 Stage04 编排器在 CompileAsync 返回后追加到 CompiledContext。
    /// </summary>
    public Task<CompiledContext> CompileAsync(
        ProjectId projectId, int chapterNumber, CancellationToken ct);

    // 预算超限时压缩 L3 检索结果并重试
    private Task<CompiledContext> TrimAndRetryAsync(/* ... */);
}
```

### 5.2 Token 计数器

采用“调用前估算 + 调用后校准”策略，针对每个模型独立校准 char/token 比率：

```csharp
public class TokenCounter
{
    // 每个模型独立维护校准系数，通过实际 usage 数据动态更新
    private readonly Dictionary<string, double> _calibratedRatios = new();

    /// <summary>
    /// 字符比例估算，使用模型特定校准系数。
    /// 初始默认: 中文 ≈ 1.5 char/token, 英文 ≈ 4 char/token。
    /// 每次 LLM 调用后通过 Calibrate 方法更新系数。
    /// </summary>
    public int Estimate(string text, string modelName)
    {
        var ratio = _calibratedRatios.GetValueOrDefault(modelName, 1.5);
        int chineseChars = CountChineseChars(text);
        int englishChars = text.Length - chineseChars;
        return (int)(chineseChars / ratio + englishChars / 4.0);
    }

    /// <summary>
    /// 每次 LLM 调用后，用 response 中的 usage.prompt_tokens 校准系数。
    /// 使用指数移动平均 (EMA) 平滑波动。
    /// </summary>
    public void Calibrate(string text, string modelName, int actualTokens)
    {
        var actualRatio = (double)text.Length / actualTokens;
        _calibratedRatios[modelName] = _calibratedRatios.TryGetValue(modelName, out var old)
            ? old * 0.7 + actualRatio * 0.3
            : actualRatio;
    }

    // 预算安全边际: 始终预留 15%，而非精确卡到上限
    public bool IsWithinBudget(int estimatedTokens, int maxBudget)
        => estimatedTokens <= maxBudget * 0.85;
}
```

### 5.3 Memory Manager Agent

整个系统最核心的组件，负责记忆的全生命周期管理。

```
写后记忆提取流程 (每章定稿后，2 次独立 LLM 调用):

  Call 1 — 摘要生成 (任务A):
    输入: 本章正文 + 本章大纲
    任务A: 生成本章 L1 摘要 (400-600字 + key_events + scene_state)
    输出: ChapterSummary (JSON)
    模型: deepseek-v4-pro 或 qwen-plus

  Call 2 — 结构检测 (任务B-F + SelfCheck):
    输入: 本章正文 + 大纲 + Call1 摘要的 key_events + 当前 L2 状态
    任务B: 对比本章内容与 L2 活跃伏笔 → 检测是否回收 (输出置信度)
    任务C: 从本章中检测潜在新伏笔 → planted_by: auto_detected
    任务D: 检查 ArcTracker milestones 是否达成
    任务E: 更新 SubplotTracker (提及检测 + dangling_since 计数)
    任务F: 检查人物关系是否有显著变化 → 生成 L3 更新建议
    输出: 结构化变更建议 (JSON)
    模型: deepseek-v4-pro 或 qwen-plus

  拆分理由:
    - 摘要生成和结构检测是异构任务，认知模式不同
    - 拆分后每次调用的 JSON schema 更简单，遵守率显著提升
    - 可独立验证每次调用的输出质量
    - 成本增加 ~¥0.02/章，全书 ~¥10，可忽略

  输出分流:
    ├── 自动通过: L1 摘要、milestone 进度、subplot 计数
    └── 需要确认: 新伏笔(auto_detected)、低置信度回收、人物变更、设定新增
```

```csharp
public class MemoryManagerAgent
{
    // 注入依赖
    private readonly ILlmAdapter _extractionLlm;
    private readonly L2ToL3Compressor _compressor;
    private readonly IMemoryRepository _memoryRepo;

    /// <summary>
    /// Call 1: 摘要生成。独立调用，确保 L1 基础可靠。
    /// </summary>
    public Task<ChapterSummary> GenerateSummaryAsync(
        Chapter chapter, Outline outline, CancellationToken ct);

    /// <summary>
    /// Call 2: 结构检测。依赖 Call 1 的 key_events 作为输入之一。
    /// 任务 B-F + SelfCheck 合并为单次 JSON 输出。
    /// </summary>
    public Task<MemoryExtractionResult> ExtractStructuralChangesAsync(
        Chapter chapter, Outline outline, ChapterSummary summary,
        L2State currentL2, CancellationToken ct);

    // L2→L3 卷级压缩
    public Task<VolumeCompressionReport> CompressVolumeAsync(
        ProjectId projectId, int volumeNumber, CancellationToken ct);

    // SubplotTracker dangling_since 纯规则计数（不需要 LLM）
    private List<SubplotUpdate> UpdateSubplotReferences(
        int chapterNumber, string content, IReadOnlyList<SubplotTracker> subplots);
}
```

**Stage06 确认机制说明**:

Stage06.ExecuteAsync 先调用 `MemoryManagerAgent.GenerateSummaryAsync` 获取摘要，再调用 `ExtractStructuralChangesAsync` 获取结构检测结果，合并为 `MemoryExtractionResult`。将其中的 `AutoConfirmedItems` 和 `NeedsConfirmationItems` 分别填充到 `PipelineResult.AutoConfirmedItems` 和 `PipelineResult.ConfirmationItems`。若 `ConfirmationItems` 非空 → 返回 `new PipelineResult { Success = true, ConfirmationItems = items, ... }`，流水线暂停。恢复时从 `context.State.PendingDecisions` 读取用户决策列表，调用 `IMemoryRepository.WriteMemoryChangesAsync(extraction, decisions)` 批量写入。

> **设计决策**: 确认流程不使用异常做控制流。`ConfirmationRequiredException` 已移除 —— `PipelineResult.ConfirmationItems` 的非空即表示需要暂停。仅保留 `LlmUnavailableException` 用于真正的异常情况（所有 LLM 模型不可用）。

### 5.4 多模型适配器设计

**设计模式**: 适配器模式 + 策略模式 + 工厂模式。

```
                    ILlmAdapter (接口)
                         │
                  LlmAdapterBase (抽象基类: 共用重试/日志/降级逻辑)
                 /         │          \
    DeepSeekAdapter   QwenAdapter   KimiAdapter

    LlmAdapterFactory.Create(modelName) → ILlmAdapter
    LlmDegradationPolicy: 主 deepseek-v4-pro → 备选1 qwen-max → 备选2 moonshot-v1-128k
```

```csharp
public abstract class LlmAdapterBase : ILlmAdapter
{
    // 构造函数注入 HttpClient(由 IHttpClientFactory 管理)、API Key、完整 chat 端点
    protected LlmAdapterBase(HttpClient httpClient, string apiKey, string chatEndpoint);

    public abstract string ModelName { get; }
    public abstract int MaxContextTokens { get; }
    public abstract int RecommendedOutputTokens { get; }

    // 非流式单轮对话: Polly Retry(3次,1s/3s/9s) + Timeout(5min) 包裹
    public Task<string> ChatAsync(string systemPrompt, string userMessage, CancellationToken ct);

    // 非流式多轮对话: 取最后一条 user + 合并前面为 context，转调单轮
    public Task<string> ChatAsync(IEnumerable<ChatMessage> messages, CancellationToken ct);

    // 流式单轮对话: 用于章节写作场景，UI 逐步渲染文本
    // 实现: HTTP 请求设置 stream=true，读取 SSE 事件流，yield return 每个 delta
    public async IAsyncEnumerable<string> StreamChatAsync(
        string systemPrompt, string userMessage,
        [EnumeratorCancellation] CancellationToken ct);

    // 子类实现: 构建特定模型请求体 + 解析响应
    protected abstract object BuildRequest(string systemPrompt, string userMessage, bool stream = false);
    protected abstract string ParseResponse(string jsonResponse);
}

// DeepSeek 适配器（示例，QwenAdapter / KimiAdapter 同理）
public class DeepSeekAdapter : LlmAdapterBase
{
    public override string ModelName => "deepseek-v4-pro";
    public override int MaxContextTokens => 1_000_000;
    public override int RecommendedOutputTokens => 8_192;
    // 端点: https://api.deepseek.com/v1/chat/completions
}
```

**降级策略**:

```csharp
public class LlmDegradationPolicy
{
    private readonly (string Model, int Priority)[] _chain = new[]
    {
        ("deepseek-v4-pro", 1),
        ("qwen-max", 2),
        ("moonshot-v1-128k", 3)
    };

    private readonly ConcurrentDictionary<string, CircuitState> _circuitStates = new();

    public string GetActiveModel() => _chain
        .OrderBy(m => m.Priority)
        .First(m => _circuitStates.GetValueOrDefault(m.Model) != CircuitState.Open)
        .Model;

    public void ReportFailure(string model)
    {
        // 连续 3 次失败 → 熔断 30s, 自动切换下一个优先级
    }

    public void ReportSuccess(string model)
    {
        _circuitStates[model] = CircuitState.Closed;
    }
}
```

### 5.5 流水线编排器

**设计模式**: 状态机 + 责任链。

```
状态转换图 (Phase 1 — MVP):

  [新建项目]
       │
       ▼
  Stage01 (选题材) ──[用户确认]──→ Stage02 (写梗概)
                                        │
                                   [用户确认]
                                        ▼
                                   Stage03 (写大纲)
                                        │
                                   [用户确认]
                                        ▼
                              ┌──────────────────┐
                              │  章节写作循环体    │
                              │                  │
                              │  Stage04 (写前)   │
                              │  ├ 编译记忆       │
                              │  ├ 随机风格注入    │
                              │  └ 随机插曲注入    │
                              │      ↓           │
                              │  Stage05 (生成)   │
                              │      ↓           │
                              │  Stage06 (记忆)   │←──── [润色后重写]
                              │      ↓           │
                              │  Stage07 (评审)   │
                              │  ├── [评分<7] ── 回到 Stage05
                              │  └── [评分>=7]
                              │      ↓           │
                              │  AI检测预检       │
                              │      ↓           │
                              │  [定稿]           │
                              └──────┬───────────┘
                                     │
                              [还有章节?]
                              ├─ 是 → Stage04
                              └─ 否 → [项目完成]
```

**Stage04 (写前准备) 执行顺序**:

Stage04 由 PipelineOrchestrator 编排四步调用：① `ContextWindowCompiler.CompileAsync` 编译记忆（L3 采用 ID 直查 + 关键词补充） → ② `StyleInjector.SelectRandomStyleAsync` 追加风格约束到 System Prompt → ③ `InterludeInjector.PrepareInterludeAsync` 追加改编闲笔到 User Message → ④ 关键约束重复注入（traits.forbidden + 本章关键设定追加到 User Message）。步骤②③可通过配置关闭。最终 Token 超限时裁剪 L3 检索结果重试。

> **设计决策**: `ContextWindowCompiler` 只负责记忆编译，风格/插曲由编排器作为独立装饰步骤调用。编译器职责单一、可独立测试；风格/插曲可单独开关。

```csharp
public class PipelineOrchestrator
{
    // 注入: 所有 IPipelineStage 实现 + IMessenger + IProjectRepository
    // 线程安全: SemaphoreSlim(1,1) 保护 _context 读写
    private PipelineContext _context;
    private readonly IProjectRepository _projectRepo;

    // 启动流水线: 初始化 context → SaveStateAsync → AdvanceAsync
    public Task<PipelineResult> StartAsync(ProjectId projectId, CancellationToken ct);

    /// <summary>
    /// 统一的确认恢复入口。接收批量 ConfirmationDecision 列表，
    /// 注入到 context.State.PendingDecisions，重新执行当前阶段并应用决策。
    /// </summary>
    public Task<PipelineResult> ResumeWithDecisionsAsync(
        IReadOnlyList<ConfirmationDecision> decisions, CancellationToken ct);

    // 核心状态推进: GetNextStage → ExecuteStage → 检查 ConfirmationItems/IsLastChapter
    private Task<PipelineResult> AdvanceAsync(CancellationToken ct);
    private Task<PipelineResult> ExecuteStageAsync(PipelineStage stage, CancellationToken ct);
    private PipelineStage? GetNextStage(PipelineStage current);
    private bool IsLastChapter();

    // 状态持久化与恢复
    private Task SaveStateAsync(CancellationToken ct);
    private Task RestoreStateAsync(ProjectId projectId, CancellationToken ct);
}
```

**暂停/恢复**: 阶段执行后若 `result.ConfirmationItems` 非空 → `SaveStateAsync` 将当前阶段、章节号、待确认条目等序列化到 Projects 表 → 返回给 UI 暂停。用户决策后调用 `ResumeWithDecisionsAsync` → `RestoreStateAsync` 从 Projects 表重建上下文 → 阶段内部从 `context.State.PendingDecisions` 读取决策并应用 → 继续推进。具体持久化字段和恢复步骤见实施阶段。

### 5.6 子Agent 评审系统

**并行执行模型**: 多个 Persona 的评审调用完全独立，使用 `Task.WhenAll` 并行发送。

```csharp
public class ReviewOrchestrator
{
    // 注入: ILlmAdapter (评审可用便宜模型 qwen-plus/moonshot-v1-8k)
    // 并行评审: Task.WhenAll + 部分失败不阻塞 (ContinueWith 容错)
    // 最小可用评审数: 至少 2 个 Persona 成功返回才计算综合评分
    // 若 < 2 个成功 -> 抛出 LlmUnavailableException, 暂停流水线
    public Task<AggregatedReview> ReviewChapterAsync(
        Chapter chapter, Outline outline, string systemPrompt,
        int personaCount = 4, CancellationToken ct = default);

    private Task<ReviewResult> ReviewWithPersonaAsync(
        Persona persona, Chapter chapter, Outline outline,
        string writingSystemPrompt, CancellationToken ct);
}

public class ReviewAggregator
{
    // 加权聚合: OverallScore = AVG(persona.Overall)
    // NeedsRevision = OverallScore < 7.0
    public AggregatedReview Aggregate(List<ReviewResult> results);
}
```

### 5.7 SelfCheck 偏差检测

两级运行模式：

**增量检查（每 5 章，轻量，复用记忆提取 LLM）**:

```csharp
public class IncrementalSelfCheck
{
    // 触发: 章节号 % 5 == 0，作为 MemoryManagerAgent 记忆提取 LLM 调用的附加任务
    // 在同一个 LLM 请求中追加 "检查本章是否违反 traits.forbidden" 的结构化输出要求
    // 复用记忆提取 LLM，不增加独立调用次数
    public List<Deviation> ParseForbiddenCheckResult(
        MemoryExtractionResult extractionResult)
    {
        return extractionResult.ForbiddenTraitViolations ?? new();
    }
}
```

**全量检查（每卷末，完整，独立 LLM 调用）**:

```csharp
public class SelfCheckRunner
{
    // 注入: ILlmAdapter (便宜模型) + IMemoryRepository + IChapterRepository
    // 全量检查: 全 L3 实体 (非采样) + 全卷章节 + 合并增量累积违规
    public Task<DeviationReport> RunFullCheckAsync(
        ProjectId projectId, int volumeStartChapter, int volumeEndChapter,
        List<Deviation> accumulatedIncrementalViolations, CancellationToken ct);
}
```

---

### 5.8 网文平台数据采集（Phase 2）

**设计约束**（基于法律合规研究）：
- 仅采集公开榜单元数据（书名/作者/分类/排名/评分/字数/连载状态），不采集正文内容
- 不绕过任何安全措施（字体加密/CAPTCHA/登录墙/SSL Pinning）
- 请求间隔硬编码 >= 3 秒，单平台并发 <= 2
- 遵守各平台 robots.txt

**统一采集器接口**：

```csharp
public interface IPlatformCollector
{
    string PlatformName { get; }
    Task<IReadOnlyList<HotListEntry>> FetchHotListAsync(
        HotListType listType, CancellationToken ct);
    Task<PlatformStats> FetchPlatformStatsAsync(CancellationToken ct);
}

public record HotListEntry
{
    public string BookTitle { get; init; }
    public string Author { get; init; }
    public string Genre { get; init; }
    public int Rank { get; init; }
    public int WordCount { get; init; }
    public long ClickCount { get; init; }          // 点击量
    public long CollectCount { get; init; }        // 收藏量
    public double? Rating { get; init; }
    public string SerialStatus { get; init; }      // 连载/完结
    public DateTime? LastUpdated { get; init; }
    public string SourcePlatform { get; init; }
    public DateTime CollectedAt { get; init; }
}

public record PlatformStats
{
    public Dictionary<string, int> GenreDistribution { get; init; } = new();
    public double AvgRating { get; init; }
    public int TotalWorks { get; init; }
    public List<string> RisingGenres { get; init; } = new();  // 环比增速 > 50% 的新兴题材
}
```

**三平台采集器实现**：

| 平台 | 采集方式 | 反爬对抗 |
|------|---------|---------|
| 番茄小说 | 内部 API（HttpClient 直调 `get_book_list`） | 无需 Playwright，仅需 User-Agent 轮换 |
| 起点中文网 | Playwright + HTML 解析（headless=false） | `--disable-blink-features=AutomationControlled`、高斯延迟、贝塞尔鼠标轨迹 |
| 晋江文学城 | Playwright + HTML 解析 | 住宅代理池（数据中心 IP 封禁率极高） |

**热度加权模型**：`CalculateHeatScore(clickCount, collectCount, rating, lastUpdated)` → 点击40% + 收藏25% + 评分20% + 更新频率15%

**定时调度**：`CollectionScheduler` — 日榜 2:00 / 周榜 周一 3:00 / 月榜 1日 4:00；内置请求间隔 >= 3s、单平台并发 <= 2、失败重试 3 次

**与流水线集成**：

`Stage08_DataEnhancement` 在用户进入 Phase 2 时触发：
1. 启动 `CollectionScheduler` 后台定时采集
2. 用户查看选题时调用 `TrendAnalyzer` 分析当前市场趋势
3. 用户选材时调用 `RecommendationEngine` 给出品类建议

---

### 5.9 AI 检测预检（平台合规）

**定位**: 在章节定稿前检测"机器骨相"特征，标注风险等级，防止输出内容在发布后触发平台 AI 审核封号。

**检测维度**：

| 维度 | 检测方法 | 原理 |
|------|---------|------|
| **句式均匀性** | 统计句长标准差 < 阈值 | AI 倾向均匀句长，人类有自然波动 |
| **词汇多样性** | TTR (Type-Token Ratio) | AI 倾向重复高频词 |
| **情绪节奏** | 滑动窗口情感强度方差 | AI 缺乏"积累→爆发→余韵"的情绪曲线 |
| **段落结构** | 段落首句模式聚类 | AI 倾向以相似句式开头段落 |

```csharp
public abstract class AiDetectorBase
{
    public abstract Task<DetectionReport> AnalyzeAsync(
        Chapter chapter, CancellationToken ct);
}

public record DetectionReport
{
    public AiRiskLevel RiskLevel { get; init; }  // Low / Medium / High / Critical
    public Dictionary<string, double> DimensionScores { get; init; } = new();
    public List<string> FlaggedPatterns { get; init; } = new();   // 被标记的具体模式
    public List<string> Suggestions { get; init; } = new();       // 降低 AI 味的修改建议
}

public enum AiRiskLevel { Low, Medium, High, Critical }

// 纯规则检测: 实时、零成本，但误报率可能较高
public class StatisticalDetector : AiDetectorBase
{
    // TTR / 句长变异系数 / 情绪曲线方差
}

// LLM 辅助检测: 更准确但需成本，用于 High 风险章节的二次确认
// 仅在 StatisticalDetector 标记 High/Critical 时才调用
public class LlmAssistedDetector : AiDetectorBase
{
    // 调用便宜模型 (qwen-plus) 对标记段落做人工感判断
    // 成本: ~0.01 元/章, 仅在 StatisticalDetector 标记 High+ 时触发
}

// 外部 API（高准确率但需成本）：
public class ExternalDetector : AiDetectorBase
{
    // 调用朱雀/第三方检测 API
    // 注：仅用于预检，不将作品存储在外部服务
}
```

**与流水线集成**：评审通过（评分 >= 7）后 → 跑 AI 检测预检 → 标注风险 → 作者确认/润色 → 正式定稿。

**设计约束**（遵循《人工智能生成合成内容标识办法》，2025.09.01 实施）：
- 检测结果仅供作者参考，不自动拦截
- 高风险章节标注"建议深度人工润色"
- NovelWriter 最终输出无隐藏标识——遵循显式标识要求，发布时由作者决定是否声明

---

### 5.10 随机风格注入与插曲系统

**设计目标**: 打破 AI 生成章与章之间的结构同质化，降低 AI 检测风险，引入"结构呼吸感"。

**核心思路**: 每章写作前，在三级记忆体系保证剧情总体走向和人物关系不变的前提下：
1. 从写作风格库随机选取一个风格档案，约束本章的句式/词汇/修辞/叙事距离
2. 从插曲库随机选取一个典故/轶事，由 LLM 改编为故事内的"闲笔"插入章节

```
随机风格 + 插曲注入流程 (Stage04 中新增):

  ① 随机风格选择
    ├── 从 StyleProfiles 表随机查询一条
    ├── 排除当前卷已使用的风格 (保证卷内风格多样性)
    ├── 风格档案 → System Prompt 追加 ~400 token 风格约束
    └── 记录 StyleUsageLog

  ② 随机插曲选择
    ├── 从 InterludeEntries 表随机查询一条 (可按题材过滤)
    ├── 排除当前卷已使用的插曲
    ├── LLM 改编: 将典故改写为故事内的 ~100 字闲笔
    └── 记录 InterludeUsageLog

  ③ 插曲插入位置选择
    ├── 不在章节开头 10% 和结尾 10%
    ├── 优先场景切换/时间跳跃处
    ├── 每 1000-1500 字最多 1 处 (C24 结构呼吸感约束)
    └── 位置随机化

  ④ 合并输出
    └── 改编后的插曲 + 插入位置 → 追加到 User Message
```

#### 5.10.1 StyleInjector

```csharp
public class StyleInjector
{
    // 随机选择风格档案，保证卷内不重复
    // 返回风格档案 + 约 300-400 token 的 System Prompt 注入块
    public Task<(StyleProfile Profile, string PromptBlock)> SelectRandomStyleAsync(
        ProjectId projectId, int volumeNumber, int chapterNumber);
}

public static class StyleProfilePromptBuilder
{
    // 结构化 StyleProfileJson → System Prompt 注入文本 (~300-400 token)
    public static string Build(StyleProfile profile);
}
```

#### 5.10.2 StyleExtractionAgent

负责从短篇小说原文中提取结构化风格档案。**仅在构建风格库时使用，不参与流水线运行**。

```csharp
public class StyleExtractionAgent
{
    // 仅在构建风格库时使用，不参与流水线运行
    // 输入: ≤5000 字短篇全文 → 输出: StyleProfileJson (严格按 schema, ≤500 字)
    // 成本: 约 $0.02/篇 (deepseek-v4-pro)
    public Task<StyleExtractionResult> ExtractAsync(
        string storyText, string sourceTitle, string sourceAuthor, CancellationToken ct);
}
```

**成本估算**：
- 输入: 5000 字短篇 ≈ 7500 token
- 输出: 500 字风格档案 ≈ 750 token
- 单篇提取成本约 ¥0.02 (deepseek-v4-pro)
- 构建 100 篇风格库总成本约 ¥2

#### 5.10.3 InterludeInjector

```csharp
public class InterludeInjector
{
    // 随机选择插曲 → LLM 改编为 ~100 字故事内闲笔 → 返回插入指令
    // 由 PipelineOrchestrator 将返回的闲笔文本追加到 CompiledContext.UserMessage
    // InterludePromptResult 定义在 Core/Dtos/InterludePromptResult.cs（供 PipelineState 引用，避免层级违规）
    public Task<InterludePromptResult> PrepareInterludeAsync(
        Outline outline, ProjectId projectId, int volumeNumber, CancellationToken ct);
}
```

#### 5.10.4 设计约束

| 约束 | 值 | 理由 |
|------|-----|------|
| 风格档案最大 token | 400 token / 章 | 不挤占记忆预算 |
| 插曲最大字数 | 100 字/处 | 不破坏叙事节奏 |
| 插曲最大频率 | 1 处/1000-1500 字 | C24 结构呼吸感策略 |
| 插入位置限制 | 避开章节头尾 10% | 保护开篇吸引力和结尾冲击力 |
| 卷内风格去重 | 同一风格不在同一卷内重复 | 保证风格多样性 |
| 卷内插曲去重 | 同一插曲不在同一卷内重复 | 保证新鲜度 |
| 插曲改编后 AI 检测 | 通过 AiDetector 预检 | 改编后的闲笔不能引入新 AI 味 |

#### 5.10.5 数据源与构建流程

**风格库数据源**（公版作品 + 可搜集的短篇，人工搜集为主）：
| 来源 | 规模 | 获取方式 |
|------|------|---------|
| 古典笔记小说（聊斋/世说新语/阅微草堂笔记） | 数百篇短篇 | 公版全文 |
| 公版近现代作家短篇（鲁迅/老舍等，**严格核验版权状态至 2038 年**） | ~30 篇 | 公版全文 |
| 古登堡计划中文分站 | 数百部公版书 | 直接下载 txt |
| 人工搜集的公开短篇（创作志/散文集/公众号公开推文等） | 不限 | 手动收集 ≤5000 字短篇 |
| MNBVC 语料集（备选） | 60TB+ 中文语料 | GitLab 下载 + 筛选 | ≤5000 字短篇 |

**风格库构建流程**：
```
手动/半自动收集短篇 txt → StyleExtractionAgent 逐篇提取
→ **人工抽检 20%**（其余自动入库，审核不合格则退回重提取）→ 入库 StyleProfiles 表
Phase 1 目标: 30-100 篇
```

**插曲库数据源**：

| 来源 | 类型 | 免费额度 |
|------|------|---------|
| 天聚数行"简说历史" API | 历史事件 (30-50字) | 100 条/天 |
| Day in History API | 历史上的今天 (50-100字) | 10 次/小时 |
| 公版笔记小说典故 | 世说新语/太平广记等 | 无限制 |

**插曲库构建策略**：
```
Phase 1: 注册天聚数行 API → 每章写作前实时调用 1 次 → 缓存到本地 SQLite
Phase 2: 批量提取公版笔记小说典故 → LLM 改写为叙事钩子 → 入库
Phase 3: RSS 新闻头条 → LLM 提取为 50 字"当代轶事"
```

---

## 6. 数据流

### 6.1 章节写作完整数据流

```
① 写前准备 (Stage04, 由 PipelineOrchestrator 编排三步调用)
  [Step 1: ContextWindowCompiler.CompileAsync]
    L1 编译 (5章→摘要) + L2 全量注入 + L3 ID直查+关键词补充 + 大纲 + 系统指令
    → CompiledContext (System Prompt 基础版 + User Message 基础版)
                    │
  [Step 2: StyleInjector.SelectRandomStyleAsync]
    随机选择风格 → 追加 ~400 token 风格约束到 CompiledContext.SystemPrompt
    (可通过配置 StyleLibrary.Enabled 关闭)
                    │
  [Step 3: InterludeInjector.PrepareInterludeAsync]
    插曲改编为故事闲笔 → 追加到 CompiledContext.UserMessage
    (可通过配置 InterludeLibrary.Enabled 关闭)
                    │
  [Step 4: User Message 关键约束重复注入]
    traits.forbidden + 本章关键设定 → 追加到 CompiledContext.UserMessage
                    ▼
  [CompiledContext 最终版: System Prompt + User Message, ≤ 160K token]
                                   │
② 章节生成 (Writing LLM, 流式输出)  ▼
                        [Writing LLM (deepseek-v4-pro)]
                        输入: System Prompt + User Message
                        输出: 章节正文 (Markdown), 流式返回逐步渲染
                                   ▼
                        [章节初稿 v1 → DB (status=Draft)]
                                   │
③ 记忆提取 (2 次独立 LLM 调用)     ▼
                        [Memory Manager Agent Call 1: 摘要生成]
                        [Memory Manager Agent Call 2: 结构检测 B-F]
                                   ▼
                        [变更建议 JSON] → 自动项 + 待确认项
                                   │
④ 人工确认闸门                    ▼
                        [确认对话框: 用户逐条 Approve/Reject]
                                   ▼
                        [写入 L2/L3 变更 → DB 事务]
                                   │
⑤ 评审润色循环                    ▼
                        [Review Orchestrator: 并行 2-5 Persona]
                                   ▼
                             综合评分 ≥ 7?
                           ├─ 否 → 润色 → 回到 ②
                           └─ 是
                                   ▼
⑥ AI 检测预检
                        [AiDetector: 统计特征 + 句式/词汇/情绪/段落]
                                   ▼
                             AiRiskLevel?
                           ├─ High/Critical → 标注建议 → 人工润色
                           └─ Low/Medium → 章节定稿 (status=Completed)
                                   │
⑦ 增量检查 (每 5 章)              ▼
                        章节号 % 5 == 0?
                        ├─ 是 → [IncrementalSelfCheck: traits.forbidden 关键词匹配]
                        │        └─ 违规累积到卷末报告，不阻塞写作
                        └─ 否 → 继续
                                   │
⑧ 卷级检查 (每卷末)               ▼
                        [SelfCheckRunner 全量: 全部 L3 实体 + LLM 调用]
                                   ▼
                        [L2ToL3 Compressor → 归档+压缩]
```

### 6.2 L1→L2→L3 记忆生命周期

```
L1 生命周期 (5章滑动窗口):
  [Ch1摘要] [Ch2摘要] [Ch3摘要] [Ch4摘要] [Ch5摘要]
       ↓ Ch6 写入后 Ch1 丢弃 (key_events 已提取进 L2)
  [Ch2摘要] [Ch3摘要] [Ch4摘要] [Ch5摘要] [Ch6摘要]

L2 生命周期 (卷级):
  卷1: [FS_001] [FS_002] [ARC_001] [SUB_001] ...
       ↓ 卷结束: L2→L3 压缩
  归档: FS_001(resolved) → L3 FORESHADOW_ARCHIVE
        ARC_001(完成)    → 追加到 L3 CharacterProfile.arc_summary
  卷2: [FS_003(跨卷active)] [FS_004] [ARC_002] ...

L3 生命周期 (追加式版本化):
  CHARACTER_PROFILE:CHAR_001:
    v1 (阶段2创建, locked:false)
    v2 (阶段3确认, locked:true)
    v3 (卷2结束 arc_summary 追加)
    v4 (卷3发现性格偏离, 作者手动更新)
  旧版本保留, 检索取最新版本号
```

---

## 7. 数据库表清单

**核心表**：Projects (项目+流水线状态), Synopses (梗概/设定摘要), Outlines (大纲), Chapters (章节Markdown)

**L1 记忆表**：ChapterSummaries (每章摘要, 持久化用于重启恢复)

**L2 记忆表**：Foreshadowings (伏笔追踪), ArcTrackers (故事弧线), SubplotTrackers (支线状态)

**L3 记忆表**(版本化追加, UNIQUE Id+Version)：CharacterProfiles (人物档案), WorldSettings (世界观条目)

**归档表**：ForeshadowingArchives (伏笔归档), VolumeSummaries (卷摘要)

**评审表**：Personas (读者画像), Reviews (评审记录)

**风格/插曲表**：StyleProfiles, StyleUsageLog, InterludeEntries, InterludeUsageLog

### 7.1 存储策略说明

**L3 实体存储**：CharacterProfile 和 WorldSetting 使用独立的 `CharacterProfiles` 和 `WorldSettings` 表。版本化追加通过 `PRIMARY KEY(Id, Version)` 联合主键实现，每次修改 INSERT 新行，检索取 `MAX(Version)`。`Synopses` 表仅用于存储阶段 1-3 的梗概/设定摘要原始产出物，与 L3 结构化实体分离。

**L2→L3 归档存储**：ForeshadowingArchive 和 VolumeSummary 使用独立的 `ForeshadowingArchives` 和 `VolumeSummaries` 表。归档数据按 ProjectId + VolumeNumber 检索，卷级压缩时批量生成，不频繁修改。

**风格/插曲使用日志**：StyleUsageLog 和 InterludeUsageLog 记录每章使用的风格和插曲，支持卷内去重查询。

## 8. 错误处理与韧性设计

### 8.1 LLM 调用韧性

```
调用链:
  Pipeline.ExecuteStage
    → ILlmAdapter.ChatAsync
      → ResiliencePipeline (Polly)
        ├── Retry: 3次, 指数退避 (1s / 3s / 9s)
        ├── Timeout: 5分钟
        └── Fallback: LlmDegradationPolicy

失败处理:
  1. 单次 HTTP 错误 → 自动重试 (transient)
  2. 3次重试耗尽 → 报告 LlmDegradationPolicy
  3. DegradationPolicy 熔断当前模型 → 切换下一个优先级
  4. 所有模型不可用 → 抛出 LlmUnavailableException
  5. PipelineOrchestrator 捕获 → 暂停流水线, 通知用户
  6. 章节草稿在 LLM 调用前已保存到 DB (原子写入保护)
```

### 8.2 数据一致性

以下方法属于 `MemoryRepository`（Storage 层），通过 `INovelWriterDbContext` 操作数据库，封装事务逻辑：

```csharp
// MemoryRepository 方法 (Storage 层), 封装在 DbContext 事务中:
// 1. L1 摘要入库 → 2. 自动确认项写入 → 3. 人工确认项写入 → 4. SaveChanges + Commit
public Task WriteMemoryChangesAsync(
    MemoryExtractionResult extraction,
    IReadOnlyList<ConfirmationDecision> decisions);
```

### 8.3 SQLite 备份策略

使用 SQLite 原生 `backup` API（`Microsoft.Data.Sqlite` 的 `connection.BackupDatabase()`），而非 `File.Copy`：

```csharp
public class BackupService
{
    // 使用 SQLite backup API (sqlite3_backup_init) 执行 WAL-safe 热备份
    // 备份前先 WAL checkpoint 确保持久化数据完整
    public Task BackupAsync(CancellationToken ct);
}
```

- 触发条件: 每章定稿后自动备份、卷级压缩前强制备份、用户手动触发
- 保留策略: 最近 20 个备份文件
- 备份方式: SQLite `backup` API，支持 WAL 模式下的在线热备份，不会产生损坏副本

### 8.4 LLM 调用成本估算

| 调用 | 模型 | 输入 token | 输出 token | 单次成本 | 每章次数 |
|------|------|-----------|-----------|---------|--------|
| 章节写作 (流式) | deepseek-v4-pro | ~160K | ~5K | ~¥0.16 | 1 |
| 记忆提取 Call1 (摘要) | qwen-plus | ~8K | ~0.8K | ~¥0.01 | 1 |
| 记忆提取 Call2 (结构检测) | qwen-plus | ~15K | ~1.5K | ~¥0.02 | 1 |
| 评审 (x4) | qwen-plus | ~15K | ~1K | ~¥0.02 | 2-5 |
| 插曲改编 | qwen-plus | ~1K | ~0.2K | ~¥0.005 | 1 |
| AI检测(LlmAssisted) | qwen-plus | ~3K | ~0.5K | ~¥0.005 | 0-1 |
| **每章合计** | | | | **~¥0.25-0.45** | |
| **500章全书** | | | | **~¥125-225** | |

### 8.5 日志策略

| 项目 | 策略 |
|------|------|
| 框架 | Serilog，输出控制台 + 滚动文件 |
| 生产环境级别 | `Information` + `Warning` + `Error` |
| 开发环境级别 | `Debug` 以上 |
| 文件轮转 | 按日切割，保留 30 天 |
| 敏感信息脱敏 | API Key 不出现在日志中；LLM 响应内容仅记录长度和 token 用量，不记录正文 |
| 关键事件 | LLM 每次调用记录模型、耗时、token 用量、成功/失败；记忆写入记录变更摘要 |

---

## 9. 配置管理

### 9.1 用户配置

核心配置项（完整 JSON 在实施阶段定义）：
- **Llm**: DefaultModel/ReviewModel/ExtractionModel + DegradationChain (3级优先级)
- **Memory**: L1/L2/L3 单次注入上限 + ContextWindow 总预算 + 窗口大小 + dangling 阈值
- **Review**: DefaultPersonaCount / PassThreshold / MaxRevisionRounds
- **SelfCheck**: IncrementalInterval (增量检查频率) / FullCheckAtVolumeEnd
- **StyleLibrary**: 风格注入开关 + MaxProfileTokens + 卷内去重
- **InterludeLibrary**: 插曲注入开关 + MaxInterludeChars + 频率限制 + 改编模型
- **Storage**: DatabasePath (AppData) / BackupDirectory / MaxBackupCount

### 9.2 敏感配置

API Keys 通过 Windows DPAPI 加密存储，不在 appsettings.json 中：

```csharp
public class ApiKeyStore
{
    // 使用 System.Security.Cryptography.ProtectedData
    // 以当前用户身份加密，仅本机本账号可解密
    public void SaveKey(string service, string apiKey);
    public string? GetKey(string service);
    public void DeleteKey(string service);
}
```

---

## 10. 启动与初始化流程

```
App 启动顺序:

1. App.xaml.cs → OnStartup
   ├── 初始化 Serilog
   ├── 检查 SQLite 数据库 → 不存在则创建
   ├── 运行 EF Core 迁移 (`context.Database.Migrate()`，非 `EnsureCreated()`)
   ├── 构建 DI 容器
   │   ├── HttpClientFactory
   │   ├── ILlmAdapterFactory + 降级策略
   │   ├── 所有 Repository (Scoped)
   │   ├── Engine 服务 (Scoped)
   │   ├── ViewModel (Transient)
   │   └── IDialogService
   ├── 验证至少有一个 LLM API Key 可用
   │   └── 无 → 弹出设置对话框
   └── 显示 ShellWindow 主窗口

2. ShellWindow 加载
   ├── ShellViewModel 订阅 IMessenger
   ├── NavigationTreeView 显示项目列表
   └── 等待用户操作

3. 加载已有项目
   ├── 读取 Project + Outlines + Chapters
   ├── 恢复 PipelineOrchestrator 状态:
   │   ├── 从 Projects 表读取 CurrentStage 和 CurrentChapter 字段
   │   ├── 若 CurrentStage ∈ {Stage04-07}: 检查是否有未完成的章节
   │   │   ├── 有 status=Draft 的章节 → 从该章的 Stage04 恢复
   │   │   └── 全部 Completed → 推进到下一章的 Stage04
   │   ├── 若 CurrentStage ∈ {Stage01-03}: 检查确认状态 → 等待用户操作
   │   └── 加载 PipelineContext.State (L3SearchCache 不信任缓存，重新执行检索)
   ├── 检查 Personas 表是否为空 → 为空则 INSERT 6 条默认 Persona 记录
   └── NavigationTreeView 填充树结构
```

---

## 11. 实现注意事项

### 11.1 编辑器伏笔标记语法

`[FS:描述]` 标记由 `ForeshadowingHighlightBehavior` 高亮。作者快捷键插入，Memory Manager Agent 在步骤③识别为 `planted_by: manual`。

编辑器基于 **AvalonEdit** 组件实现（而非完全自绘），利用其语法高亮框架、行号显示、自定义 `VisualLineTransformer` 实现伏笔标记高亮。预览模式仍用 Markdig 渲染 FlowDocument。

### 11.2 响应式 UI 与长 LLM 调用

写作 LLM 使用流式输出（`StreamChatAsync`），UI 逐步渲染生成文本。非流式调用（记忆提取/评审）可能持续 30-120 秒，ViewModel 方法使用 `async/await`，通过 `IProgress<T>` 报告进度。

### 11.3 Token 预算可视化

`TokenBudgetBar` 控件绿→黄→红显示 ContextWindow 实时用量，数据源为 `CompiledContext.TokenBudget`。

### 11.4 YAML 解析容错策略

LLM 输出的 YAML 可能偏离模板格式。`YamlMemoryParser` 需要以下容错机制：

```
解析流程:
  1. 按固定分隔符 ([CHARACTER_PROFILE:vN], [FORESHADOW:v1] 等) 切分文档
  2. 逐条目解析键值对，使用宽松匹配 (trim + case-insensitive key matching)
  3. 必填字段 (id, name, version) 缺失 → 丢弃该条目，记录 Warning 日志
  4. 选填字段缺失 → 填充类型默认值 (string="" / array=[] / int=0)
  5. 未知字段 → 保留但不使用，不报错 (向前兼容)
  6. 整段解析失败 → 保留原始文本在 _raw 字段，供人工查阅
```

### 11.5 模型选择灵活性

| 用途 | 模型 | 理由 |
|------|------|------|
| 写作 LLM | deepseek-v4-pro | DeepSeek V4 Pro，1M 上下文，最强推理 |
| 记忆提取 LLM | deepseek-v4-pro 或 qwen-plus | 结构化分析，可适当降级 |
| 评审 LLM | qwen-plus | 轻量评审，成本优先 |
| SelfCheck LLM | qwen-plus | 一致性比对 |
| 风格提取 LLM | deepseek-v4-pro | 单次 ~8000 token，成本极低(¥0.02/篇)，Phase 1 仅离线用 |
| 插曲改编 LLM | qwen-plus | ~200 token 轻量调用，改写一句闲笔 |

### 11.6 LLM JSON 输出校验与降级

LLM 输出的 JSON 可能存在问题（不完整、编造 ID、类型不匹配）。建立统一的 JSON 输出处理管线：

```csharp
public class LlmJsonOutputParser<T> where T : class
{
    // 1. 尝试标准 JSON 解析
    // 2. 失败 → 尝试修复常见问题（截断补全、转义修复）
    // 3. 仍失败 → 尝试从输出中提取 JSON 片段（正则匹配 {...} 块）
    // 4. 仍失败 → 返回解析错误详情，由调用方决定是否重试
    public ParseResult<T> Parse(string llmOutput);
}

public record ParseResult<T>(T? Value, bool Success, List<string> Errors);
```

同时对所有 LLM 输出做 **ID 存在性校验**：`MemoryExtractionResult` 中引用的所有 `FS_{NNN}`、`CHAR_{NNN}`、`ARC_{NNN}` 等 ID，必须与数据库中实际存在的 ID 匹配。不存在的 ID 标记为可疑，不直接写入。

### 11.7 System Prompt 结构与 Lost in the Middle 应对

基于研究，LLM 对长上下文中间部分的注意力显著低于首尾。System Prompt 采用以下结构优化：

```
System Prompt 区块顺序 (注意力从高到低):
  [写作角色设定]                    ← 开头，注意力最高
  [人物行为边界 - forbidden 优先]    ← 开头，关键约束
  [世界观规则]                      ← 前部
  [L2 卷级记忆]                     ← 中部
  [L1 近期摘要]                     ← 中部
  [全书基调]                        ← 中后部
  [风格约束]                        ← 后部
  [写作技术指令]                    ← 结尾，注意力较高

User Message 结构:
  [本章大纲]
  [当前场景接续]
  [⚠️ 本章关键约束提醒]  ← 重复 forbidden + 本章相关的1-2条关键设定
  [插曲改编]
  [评审反馈（润色时）]
```

关键约束在 System Prompt 和 User Message 中重复出现，实测表明重复出现的约束召回率显著更高。L3 注入预算精简到 40K，宁可少注入高相关条目确保被模型有效利用。

---

## 12. LLM Prompt 结构规范

### 12.1 通用规则

**角色划分**:

| 角色 | 内容 | 说明 |
|------|------|------|
| `system` | 持久约束：人物设定、世界观规则、记忆上下文、写作指令、风格要求 | 不因单次对话变化的基础约束 |
| `user` | 任务描述：本章大纲、关键事件、改编插曲、当前场景状态、评审反馈 | 每章变化的即时任务 |

**输出格式选择**:

| 场景 | 格式 | 理由 |
|------|------|------|
| 章节正文生成 | Markdown（无结构化包装） | LLM 直接输出正文，不经过 JSON 编码层 |
| 记忆提取 Call1 (摘要) | JSON（单一结构化对象） | 字段简单，schema 遵守率高 |
| 记忆提取 Call2 (结构检测) | JSON（单一结构化对象） | 字段精确可解析，可做 schema 校验；YAML 仅用于记忆持久化序列化 |
| 评审（子Agent） | JSON | 见 §5.6 评审输出的结构化 JSON schema |
| 风格档案提取 | JSON | 离线单次，严格按 StyleProfile schema |
| 插曲改编 | 纯文本（≤100 字） | 直接嵌入 User Message，无需结构包装 |

**System Prompt 总预算**: ≤ 160K token（含 L1/L2/L3 记忆注入 + 风格约束 + 系统指令）。超限时裁剪 L3 检索结果重试。

### 12.2 各调用点 Prompt 结构

**章节写作 (Stage05)**: System Prompt 包含 8 个区块：写作角色设定 → 人物行为边界(L3) → 世界观规则(L3) → 卷级记忆(L2) → 近期摘要(L1) → 全书基调约束 → 风格约束(StyleInjector) → 写作技术指令。User Message 包含：本章大纲 + 当前场景接续 + 关键约束重复(forbidden + 本章关键设定) + 插曲改编(InterludeInjector，条件性) + 评审反馈(仅润色循环)。使用流式输出。总预算 System ≤ 160K + User ≤ 10K token。

**记忆提取 Call1 (Stage06-摘要)**: `system` + `user` 调用，仅任务 A（摘要 + key_events + scene_state）。System Prompt 定义角色、输出 JSON Schema。User Message 包含本章正文 + 大纲。总预算 System ≤ 3K + User ≤ 15K token。

**记忆提取 Call2 (Stage06-结构检测)**: `system` + `user` 调用，任务 B-F + SelfCheck 合并为一个 JSON 输出。System Prompt 定义角色、输出 JSON Schema、各任务语义说明。User Message 包含本章正文 + 大纲 + Call1 摘要的 key_events + 当前 L2 状态 + L3 实体 ID 列表。总预算 System ≤ 5K + User ≤ 30K token。

### 12.3 记忆提取期望输出 Schema

此 Schema 定义了 MemoryManagerAgent 输出的 JSON 结构，各字段直接映射到 `MemoryExtractionResult` 的 DTO 类型（§4.4.1）。具体字段定义见实施阶段。

```json
{
  "taskA_l1_summary":       { "summary": "...", "key_events": [...], "word_count": N,
                              "current_scene_state": { "location": "...", "present_characters": [...], ... } },
  "taskB_foreshadowing_resolutions": [{ "foreshadowing_id": "FS_{NNN}", "confidence": "high|low", ... }],
  "taskC_new_foreshadowings":       [{ "description": "...", "priority": "high|low", ... }],
  "taskD_arc_updates":              [{ "arc_id": "ARC_{NNN}", "new_status": "completed|...", ... }],
  "taskE_subplot_updates":          [{ "subplot_id": "SUB_{NNN}", "mentioned_in_chapter": bool, ... }],
  "taskF_l3_change_proposals":      [{ "target": "CharacterProfile|WorldSetting", "confidence": "high|low", ... }],
  "selfcheck_forbidden_violations": [{ "entity_id": "...", "severity": "critical|...", ... }]
}
```

### 12.4 调用点总览

| 调用点 | System Prompt | User Message | 输出格式 | 输出 token | 流式 |
|--------|--------------|-------------|---------|-----------|------|
| Stage05 章节写作 | ≤ 160K token | ≤ 10K token | Markdown | ≤ 5K | 是 |
| Stage06 Call1 摘要 | ≤ 3K token | ≤ 15K token | JSON | ≤ 0.8K | 否 |
| Stage06 Call2 结构检测 | ≤ 5K token | ≤ 30K token | JSON | ≤ 1.5K | 否 |
| Stage07 评审 (x N) | Persona 系统提示 | 章节正文 + 大纲 | JSON | ≤ 1K | 否 |
| 插曲改编 | 改编指令 | 典故事实 + 语境 | 纯文本 | ≤ 200 | 否 |
| 风格提取 (离线) | 提取指令 + Schema | 短篇全文 | JSON | ≤ 750 | 否 |
| SelfCheck 全量 | 偏差检查指令 | 全卷章节 + L3 实体 | JSON | ≤ 3K | 否 |
