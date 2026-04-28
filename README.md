# VSCopilotSwitch

VSCopilotSwitch 是一个面向 VS Code / GitHub Copilot Chat 体验的本地模型供应商切换与协议转换工具。项目目标是把多个第三方模型供应商统一转换为 Ollama 兼容接口，并自动维护 VS Code 用户配置，让编辑器中的模型入口可以像使用 Ollama 一样调用不同供应商的模型。

## 项目目标

- 支持多种模型供应商协议接入：sub2api 中转站协议、OpenAI Official、Claude Official、DeepSeek、NVIDIA NIM / build.nvidia.com、Moark 等。
- 将上游模型统一暴露为 Ollama 兼容协议，降低 VS Code 与本地工具链的接入成本。
- 自动修改 VS Code 用户目录中的 Ollama 相关配置，包括 `chatLanguageModels.json` 和 `settings.json`。
- 提供熔断、重试、健康检查、限流、故障降级等稳定性能力。
- 基于 [OmniHost](https://github.com/maikebing/OmniHost) 优先实现 Windows 桌面端；macOS、Linux、WSL 暂不实现，后续跨平台阶段再补齐。
- 界面使用 Vue 3 + TypeScript，以 Visual Studio SPA 模式在项目内调试、构建和发布。
- 最终发布支持 AOT，并将 SPA 构建产物作为嵌入式资源打包进单体应用。
- 支持系统托盘图标，可从托盘打开主界面、快速选择当前提供商并退出程序。
- UI 参考 `cc switch` 的快速切换体验，采用浅色卡片式供应商列表和添加/编辑表单，强调当前供应商、模型、密钥、代理和 VS Code 配置的一站式管理。

## 核心能力

### 协议转换

VSCopilotSwitch 计划维护一个统一的 Provider Adapter 层，将不同供应商的认证、模型列表、聊天补全、流式输出、错误结构和限流语义转换为 Ollama 兼容接口。

首批计划支持：

| 供应商 | 目标能力 | 备注 |
| --- | --- | --- |
| sub2api | ✅️ 中转站模型统一接入 | 已接入首版 OpenAI Chat Completions 兼容 Adapter，站点自定义字段后续继续扩展 |
| OpenAI Official | ✅️ 模型列表、chat/completions 兼容策略 | 已接入官方 `/v1/models` 和 `/v1/chat/completions`；Responses API 后续按 VS Code 需求评估 |
| Claude Official | ✅️ Messages API 到 Ollama chat 语义转换 | 已接入官方 `/v1/models` 和 `/v1/messages`；tool use 后续继续扩展 |
| DeepSeek | ✅️ OpenAI 兼容接口接入 | 已接入官方 `/models` 和 `/chat/completions`；推理模型扩展字段后续继续补齐 |
| NVIDIA NIM | ✅️ build.nvidia.com 模型接入 | 已接入 OpenAI-compatible `/v1/models` 和 `/v1/chat/completions` |
| Moark | ✅️ 平台协议适配 | 已接入 OpenAI-compatible `/v1/models` 和 `/v1/chat/completions` |

### VS Code 配置管理

工具会检测并维护以下文件：

- `C:\Users\mysti\AppData\Roaming\Code\User\chatLanguageModels.json`
- `C:\Users\mysti\AppData\Roaming\Code\User\settings.json`

当前阶段优先支持 Windows 路径解析；其他平台路径只作为后续规划，不会在现阶段自动写入：

| 平台 | VS Code User 配置目录 |
| --- | --- |
| Windows | `%APPDATA%\Code\User` |
| macOS | `~/Library/Application Support/Code/User`，后续实现 |
| Linux | `~/.config/Code/User`，后续实现 |
| WSL | Windows 侧 VS Code Server / Remote 配置与 Linux 用户配置并存检测，后续实现 |

配置写入必须具备备份、差异预览、回滚和幂等更新能力，避免破坏用户现有设置。

### 稳定性

计划提供以下运行时保护：

- 熔断：供应商连续失败后短时间停止路由到该供应商。
- 重试：对短暂网络错误、429、5xx 做可配置重试。
- 超时：按供应商和模型配置请求超时。
- 降级：主供应商不可用时切换到备用供应商或备用模型。
- 健康检查：周期性检测 API Key、模型列表和聊天接口可用性。
- 观测：记录请求耗时、失败原因、熔断状态和当前路由。

## 技术方向

项目界面和宿主能力基于 OmniHost，前端界面使用 Vue 3 SPA。开发阶段通过项目内 npm 脚本启动 SPA 调试服务；发布阶段先构建 SPA 静态产物，再作为嵌入式资源打包进 AOT 单体应用，由宿主在运行时加载内置 SPA 资源。实现上建议分层：

- `host`：OmniHost 桌面宿主、系统托盘、窗口生命周期、SPA 资源加载；当前优先 Windows，跨平台能力后续补齐。
- `core`：协议转换、路由、熔断、配置模型、加密存储。
- `providers`：各供应商 Adapter。
- `vscode-config`：VS Code 配置发现、备份、写入、回滚。
- `ui`：Vue 3 SPA，负责供应商配置、模型切换、状态面板、日志和向导；当前主界面采用 `cc switch` 风格的浅色卡片布局，关键写入能力仍通过预览入口承接。


### 托盘能力

桌面端需要提供系统托盘图标，降低日常切换成本：

- 点击托盘图标可打开或聚焦主界面。
- 托盘菜单可显示当前提供商和当前模型。
- 托盘菜单可快速切换当前提供商。
- 托盘菜单可查看代理服务运行状态。
- 托盘菜单提供退出程序入口，并在退出前安全停止本地代理。


## 工程结构

```text
src/
  VSCopilotSwitch/               # 本地宿主与 HTTP API，最终可执行文件名为 VSCopilotSwitch
  VSCopilotSwitch.Core/          # Ollama 协议模型、代理服务和 Provider 抽象
  VSCopilotSwitch.VsCodeConfig/  # VS Code 配置目录发现、安全读写、备份和干运行预览
  VSCopilotSwitch.Ui/            # Vue 3 + TypeScript + Vite SPA，包含 Visual Studio JavaScript .esproj
```

## 开发命令

```powershell
# 构建后端 MVP
$env:DOTNET_CLI_HOME = "$PWD/.dotnet-home"
$env:MSBuildEnableWorkloadResolver = "false"
dotnet build src/VSCopilotSwitch/VSCopilotSwitch.csproj -m:1 /p:RestoreUseStaticGraphEvaluation=false

# 启动 HTTP Web 宿主和 Visual Studio SPA Proxy，访问 http://localhost:5124/
dotnet run --project src/VSCopilotSwitch --launch-profile http

# 仅调试 Ollama 兼容代理端口时，可显式监听 127.0.0.1:11434
dotnet run --project src/VSCopilotSwitch --urls http://127.0.0.1:11434

# SpaProxy 会自动以 HTTP 调用此脚本；也可单独启动 Vue 调试服务
npm --prefix src/VSCopilotSwitch.Ui run dev
```

## 供应商配置与运行时路由

当前桌面宿主以 UI 中保存并启用的供应商作为 Ollama 代理运行时来源。供应商的 API Key 使用当前 Windows 用户保护数据加密落盘，前端和内部列表 API 只返回脱敏预览；`/api/tags` 会向当前启用供应商实时获取模型列表，非流式 `/api/chat` 和流式 `/api/chat` 会在请求时读取当前启用供应商，并在内存中构建对应 Provider Adapter。未保存真实 API Key 或未配置真实供应商时，代理才回退到内置占位 Provider。

当前保存表单已经能配置供应商名称、官网/API 地址、模型名和 API Key；Provider 类型选择仍在阶段 5.5 后续任务中，因此非 Claude 供应商会先按 OpenAI-compatible 路径接入，Claude 供应商按 Anthropic Messages API 接入。

下面各 Adapter 段落记录已实现协议能力和底层配置字段，主要用于后续测试、调试和 Provider 类型选择接入参考；真实桌面运行路径以 UI 保存并启用的供应商为准。

## sub2api Adapter

```powershell
$env:Providers__Sub2Api__BaseUrl = "https://your-sub2api.example"
$env:Providers__Sub2Api__ApiKey = "<sub2api-api-key>"
$env:Providers__Sub2Api__Models__0__UpstreamModel = "gpt-4.1-mini"
$env:Providers__Sub2Api__Models__0__Name = "sub2api/gpt-4.1-mini"
$env:Providers__Sub2Api__Models__0__Aliases__0 = "gpt41-mini"
```

`Models` 可以不配置；未配置时 Adapter 会通过 sub2api 的 `/v1/models` 拉取模型列表。配置静态模型后，`/api/tags` 会直接暴露这些模型，并在 `/api/chat` 中把 Ollama 侧模型名路由到对应的上游 `UpstreamModel`。上游 HTTP 错误会映射为 Ollama 代理错误，公开错误消息会脱敏当前 API Key。

## OpenAI Official 配置

OpenAI Official Adapter 默认使用 `https://api.openai.com`，并自动访问 `/v1/models` 与 `/v1/chat/completions`。底层配置支持 `OrganizationId` 和 `ProjectId`：

```powershell
$env:Providers__OpenAI__ApiKey = "<openai-api-key>"
$env:Providers__OpenAI__OrganizationId = "<optional-org-id>"
$env:Providers__OpenAI__ProjectId = "<optional-project-id>"
$env:Providers__OpenAI__Models__0__UpstreamModel = "gpt-4.1-mini"
$env:Providers__OpenAI__Models__0__Name = "openai/gpt-4.1-mini"
$env:Providers__OpenAI__Models__0__Aliases__0 = "openai-mini"
```

`Models` 可以不配置；未配置时 Adapter 会通过 OpenAI 的 `/v1/models` 拉取当前 API Key 可见的模型列表。配置静态模型后可控制暴露给 Ollama 的模型名和别名，避免不同 Provider 返回相同上游模型名时产生歧义。

## DeepSeek 配置

DeepSeek Adapter 默认使用 `https://api.deepseek.com`，并按官方 OpenAI-compatible 路径访问 `/models` 与 `/chat/completions`：

```powershell
$env:Providers__DeepSeek__ApiKey = "<deepseek-api-key>"
$env:Providers__DeepSeek__Models__0__UpstreamModel = "deepseek-chat"
$env:Providers__DeepSeek__Models__0__Name = "deepseek/deepseek-chat"
$env:Providers__DeepSeek__Models__0__Aliases__0 = "deepseek"
$env:Providers__DeepSeek__Models__1__UpstreamModel = "deepseek-reasoner"
$env:Providers__DeepSeek__Models__1__Name = "deepseek/deepseek-reasoner"
$env:Providers__DeepSeek__Models__1__Aliases__0 = "deepseek-reasoner"
```

`Models` 可以不配置；未配置时 Adapter 会通过 DeepSeek 的 `/models` 拉取当前 API Key 可见的模型列表。配置静态模型后可稳定 Ollama 侧模型名和别名，避免模型列表随上游变化影响 VS Code 配置。

## NVIDIA NIM 配置

NVIDIA NIM Adapter 默认使用 `https://integrate.api.nvidia.com`，并访问 OpenAI-compatible 的 `/v1/models` 与 `/v1/chat/completions`：

```powershell
$env:Providers__NvidiaNim__ApiKey = "<nvidia-api-key>"
$env:Providers__NvidiaNim__Models__0__UpstreamModel = "meta/llama-3.1-8b-instruct"
$env:Providers__NvidiaNim__Models__0__Name = "nvidia-nim/llama-3.1-8b"
$env:Providers__NvidiaNim__Models__0__Aliases__0 = "nim-llama"
```

`BaseUrl` 可改成本地或私有 NIM 网关地址；如果地址本身已经包含 `/v1`，Adapter 不会重复拼接。

## MoArk 配置

MoArk Adapter 默认使用 `https://moark.ai/v1`，并访问 OpenAI-compatible 的 `/models` 与 `/chat/completions` 组合路径：

```powershell
$env:Providers__Moark__ApiKey = "<moark-api-key>"
$env:Providers__Moark__Models__0__UpstreamModel = "claude-sonnet-4-5"
$env:Providers__Moark__Models__0__Name = "moark/claude-sonnet-4-5"
$env:Providers__Moark__Models__0__Aliases__0 = "moark-sonnet"
```

`Models` 可以不配置；未配置时 Adapter 会通过 `/v1/models` 拉取当前 API Key 可见的模型列表。

## Claude Official 配置

Claude Official Adapter 默认使用 `https://api.anthropic.com`，访问 `/v1/models` 与 `/v1/messages`，并自动携带 `x-api-key` 和 `anthropic-version` 请求头：

```powershell
$env:Providers__Claude__ApiKey = "<anthropic-api-key>"
$env:Providers__Claude__AnthropicVersion = "2023-06-01"
$env:Providers__Claude__MaxTokens = "4096"
$env:Providers__Claude__Models__0__UpstreamModel = "claude-sonnet-4-5"
$env:Providers__Claude__Models__0__Name = "claude/sonnet-4-5"
$env:Providers__Claude__Models__0__Aliases__0 = "claude-sonnet"
```

Claude Adapter 会把 Ollama 侧 `system` 消息提升为 Anthropic Messages API 顶层 `system` 字段，普通 `user` / `assistant` 消息保持为消息数组。当前首版聚焦文本聊天，tool use 和多模态内容后续按 VS Code 实际需求扩展。

## 已实现 MVP API

- `GET /health`：宿主健康检查。
- `GET /api/tags`：Ollama 兼容模型列表；优先向 UI 当前启用供应商实时获取模型，未保存真实 API Key 或未配置真实供应商时返回内置占位模型 `vscopilotswitch/default`。
- `POST /api/chat`：Ollama 兼容非流式和流式聊天；优先转发到 UI 当前启用供应商对应的上游聊天接口，未配置真实供应商时走内置 `InMemoryModelProvider` 回显。
- `GET /internal/vscode/user-directories`：发现本机可能的 VS Code User 配置目录。
- `POST /internal/vscode/apply-ollama`：对 `settings.json` 和 `chatLanguageModels.json` 做干运行预览或安全写入，写入前会备份已有文件。

当前管理界面已提供 VS Code Ollama 配置写入向导骨架：用户需要先选择 Windows VS Code User 目录并生成 dry-run 差异预览，确认目标文件和托管字段变更后才能二次确认写入；写入结果会展示备份路径、文件状态和字段级变化。回滚入口会列出最近的 VSCopilotSwitch 备份，并在恢复指定备份前要求二次确认，同时为当前文件再创建安全备份。

当配置预览、写入、备份读取或恢复失败时，界面会按权限不足、JSON 无效、文件占用、端口冲突等类型展示可执行修复建议，帮助用户在不覆盖原配置的前提下处理问题。

代理地址、熔断失败阈值、重试次数和备用路由等高级选项默认折叠，日常切换供应商时不会干扰主流程。

高级选项中的本地代理地址支持端口占用检测，可提示 `127.0.0.1` 上的目标端口是否已被 Ollama 或其他代理占用。Windows 桌面端已提供最小托盘菜单，可打开或聚焦主界面、查看当前提供商和代理状态，并通过退出菜单触发宿主关闭流程以停止本地代理。

仓库包含一个无外部测试框架依赖的 VS Code 配置最小测试项目，覆盖配置写入幂等、备份列表和恢复前安全备份：

```powershell
dotnet run --project tests/VSCopilotSwitch.Core.Tests/VSCopilotSwitch.Core.Tests.csproj --no-restore
dotnet run --project tests/VSCopilotSwitch.VsCodeConfig.Tests/VSCopilotSwitch.VsCodeConfig.Tests.csproj --no-restore
```
## 文档

- [路线图](ROADMAP.md)
- [智能体协作要求](AGENTS.md)
- [变更日志](CHANGELOG.md)

## 当前状态

阶段 5 首批 Provider Adapter 首版已完成，阶段 5.5 正在把 UI、受保护供应商配置、Provider Adapter 和 Ollama 代理串成真实闭环：当前已支持 UI 启用供应商驱动 `/api/tags` 与 `/api/chat`，并在首页展示真实模型刷新状态和失败原因；下一步会补齐测试连接、VS Code 写入使用当前代理模型，以及托盘状态联动。

## 当前 OmniHost 接入状态

Windows 端已进入源码集成阶段：宿主项目直接引用 `external/OmniHost/src/OmniHost`、`external/OmniHost/src/OmniHost.Windows` 和 `external/OmniHost/src/OmniHost.WebView2`。运行时会先在 `127.0.0.1` 随机可用端口启动 ASP.NET Core API / SPA 服务，再使用 OmniHost 的 `Win32Runtime` 与 `WebView2AdapterFactory` 打开原生窗口承载管理界面。

当前阶段仍保留本地 HTTP 服务边界，便于 VS Code 配置 API、Ollama 兼容代理和 Vue SPA 继续复用现有开发链路；后续托盘菜单、窗口聚焦、快速切换供应商和退出代理会在 OmniHost 宿主层继续补齐。
