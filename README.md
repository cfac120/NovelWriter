# NovelWriter — AI 网文写作助手

Windows 桌面端创作效率工具，AI 辅助消除写作非创意摩擦（记忆追踪、草稿扩写、一致性检查），核心创意决策由作者掌控。

## 架构

```
NovelWriter.App (WPF) → Engine → Core ← Storage (SQLite)
```

| 层 | 职责 |
|---|---|
| Core | 领域实体、值对象、接口、DTO、领域服务 |
| Engine | LLM适配、流水线、记忆管理、评审、SelfCheck |
| Storage | EF Core + SQLite 仓储 |
| App | WPF 三栏写作 IDE |
| Tests | 单元/集成/PoC |

## 快速开始

```powershell
# 1. 编译
dotnet build

# 2. 启动应用
dotnet run --project src/NovelWriter.App

# 3. 在应用中配置 LLM（点击左下角 ▼）
#    - 填入 API 端点（完整 URL，兼容 OpenAI 格式）
#    - 填入模型名
#    - 填入 API Key
#    - 配置自动保存到 app 目录的 llm_config.json

# 4. 创建或打开项目，点击 ▶ 开始
#    梗概 → 确认 → 大纲 → 确认 → 逐章写作

# 5. 测试（可选）
dotnet run --project tests/NovelWriter.Tests
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

## 界面布局

- **左栏**：项目资源树（故事创意 / 梗概 / 大纲 / 章节 / 人物档案 / 世界观 / 插曲库 / 风格库）
- **中栏**：标签式 Markdown 编辑器
- **右栏**：LLM 工作区（进度状态 / 聊天对话 / 思考指示器 / 输入框）
- **底部**：状态栏

## MVP 进度

- [x] Core 层 —— 领域模型、接口、DTO
- [x] Storage 层 —— EF Core + SQLite
- [x] Engine 层 —— LLM 适配（OpenAI 兼容、多模型、限速、重试）、流水线、记忆管理
- [x] PoC 30 章验证 —— 人物一致性 100%
- [x] App 层基础界面 —— 三栏布局、项目管理、LLM 配置持久化、梗概/大纲生成
- [ ] 逐章写作循环（Stage04-07）
- [ ] 风格库与插曲系统
- [ ] 子Agent 评审
- [ ] 发布与分发

## LLM 支持

模型无关设计：任何兼容 OpenAI Chat Completions 格式的 API 均可使用。

填入完整端点和模型名即可，不做任何 URL 拼接或模型名替换。
