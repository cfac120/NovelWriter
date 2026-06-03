# NovelWriter — AI 网文写作助手

Windows 桌面端创作效率工具，AI 辅助消除写作非创意摩擦（记忆追踪、草稿扩写、一致性检查），核心创意决策由作者掌控。

## 架构

```
NovelWriter.App (WPF) → Engine → Core ← Storage (SQLite)
```

| 层 | 职责 | 文件 |
|---|---|---|
| Core | 领域实体、值对象、接口、DTO、领域服务 | 45 |
| Engine | LLM适配、流水线、记忆管理、评审、SelfCheck | 23 |
| Storage | EF Core + SQLite 仓储 | 6 |
| App | WPF MVVM 界面 | 10 |
| Tests | 单元/集成/PoC | 3 项目 |

## 快速开始

```powershell
# 1. 设置 API Key
$env:DEEPSEEK_API_KEY = "你的key"

# 2. 编译
dotnet build

# 3. 运行测试
dotnet run --project tests/NovelWriter.Tests

# 4. 启动应用
dotnet run --project src/NovelWriter.App

# 5. PoC 验证（可选）
dotnet run --project tests/NovelWriter.PoC -- 1 5
```

## 技术栈

| 技术 | 版本 |
|---|---|
| .NET SDK | 10.0 |
| WPF | net10.0-windows |
| CommunityToolkit.Mvvm | 8.4.2 |
| MaterialDesignThemes | 5.3.2 |
| EF Core Sqlite | 10.0.8 |
| AvalonEdit | 6.3.1.120 |
| Markdig | 1.2.0 |
| Polly | 8.6.6 |
| xUnit v3 | 3.2.2 |

## PoC 验证结果

30 章修仙小说记忆一致性验证：
- 人物一致性: 100%（6次SelfCheck 0偏差）
- 伏笔准确率: ≥85%
- L3检索召回率: ≥80%
- 通过 ✓

## MVP 计划

- [x] Core 层
- [x] Storage 层
- [x] Engine 层（LLM适配/流水线/记忆/评审/SelfCheck）
- [x] PoC 30章验证
- [ ] App 层基础界面
- [ ] 风格库与插曲系统验证
- [ ] 完整创作流程集成
