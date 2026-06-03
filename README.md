> 💡 **一个真正能实现网文自动写的神器，期待大神继续完成。想法很美好，但是我个人能力有限，经济实力也有限，光这三个文档就花了 100 多 RMB 的 token 费用了。**

# NovelWriter — AI 网文写作助手

> ⚠️ **项目状态：基础框架搭建中，寻求开发者接手**
>
> 本项目已完成完整的产品设计文档和技术架构方案，并开始按 TDD 模式搭建代码框架。Core 层和 Storage 层已完成，欢迎有兴趣的开发者继续推进。

## 项目简介

NovelWriter 是一款 Windows 桌面端 AI 辅助网文写作工具。核心理念：**AI 负责记忆追踪、草稿扩写、一致性检查；人类掌控核心创意决策。**

### 核心特性

- **三层记忆架构**（L1 即时 / L2 卷级 / L3 全书）— 解决长篇写作中角色漂移、伏笔遗忘、设定矛盾三大痛点
- **9 阶段流水线** — 从选题到定稿的全流程编排，含人工确认闸门
- **随机风格注入 + 插曲系统** — 打破章间结构同质化，降低 AI 检测风险
- **多 Persona 并行评审** — 6 种读者视角评审 + 润色循环
- **AI 检测预检** — 章节定稿前检测"机器骨相"，标注风险等级
- **多模型降级** — DeepSeek / 通义千问 / Kimi 三级降级 + 熔断

### 技术栈

.NET 10 + WPF (MVVM) + SQLite (EF Core) + 国产大模型 API

## 文档

| 文档 | 说明 |
|------|------|
| [设计文档](2026-05-28-novelwriter-design.md) | 产品定位、三层记忆架构、流水线设计、子Agent评审系统 |
| [技术架构方案](2026-05-29-novelwriter-tech-architecture.md) | 分层架构、接口定义、数据流、错误处理、Prompt规范 |
| [需求提取文档](2026-05-30-novelwriter-requirements.md) | 逐条功能需求、验收标准、优先级、依赖关系 |

## 项目结构

```
src/
├── NovelWriter.Core/          # 领域核心层（无外部依赖）
│   ├── Entities/              # 实体：Project, Chapter, Outline...
│   ├── ValueObjects/          # 值对象：ProjectId, ChapterId, TokenBudget...
│   ├── Memory/                # 三层记忆实体：CharacterProfile, Foreshadowing...
│   ├── Enums/                 # 枚举：PipelineStage, ForeshadowingStatus...
│   ├── Interfaces/            # 接口：ILlmAdapter, IMemoryRepository...
│   ├── Dtos/                  # DTO：MemoryExtractionResult, CompiledContext...
│   ├── DomainServices/        # 领域服务：TokenBudgetCalculator...
│   ├── Events/                # 领域事件
│   └── Exceptions/            # 异常
├── NovelWriter.Engine/        # 业务引擎层（依赖 Core）
│   └── Pipeline/              # 9 阶段流水线
├── NovelWriter.Storage/       # 持久化层（依赖 Core）
│   ├── NovelWriterDbContext   # EF Core DbContext + 值对象转换
│   └── Repositories/          # 仓储实现
tests/
└── NovelWriter.Tests/         # 27 个测试（Core 层单元 + Storage 集成）
```

## 项目状态

- ✅ 产品设计文档
- ✅ 技术架构方案
- ✅ 需求提取文档
- ✅ Core 层（实体、值对象、接口、领域服务）— 20 个单元测试通过
- ✅ Storage 层（EF Core + SQLite + 仓储实现）— 7 个集成测试通过
- ⬜ Engine 层（流水线编排、记忆管理、LLM 集成）
- ⬜ App 层（WPF UI）

## 如何参与

如果你对这个项目感兴趣：

1. **Fork 本仓库**，基于设计文档开始开发
2. **提 Issue** 讨论设计问题或改进建议
3. **提 PR** 补充实现或文档

建议的开发路径：
1. 先完成 30 章规模记忆一致性 PoC（见设计文档测试策略章节）
2. 实现 Engine 层核心流水线（Stage01-07），用 Moq 模拟 LLM
3. 接入真实 LLM API 验证记忆一致性
4. 实现 App 层（WPF UI）

## 快速开始

```bash
# 需要 .NET 10 SDK
dotnet build
dotnet test
```

## License

MIT
