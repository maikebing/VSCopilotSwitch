# VSCopilotSwitch

VSCopilotSwitch 是一个面向 VS Code / GitHub Copilot Chat 体验的本地模型供应商切换与协议转换工具。项目目标是把多个第三方模型供应商统一转换为 Ollama 兼容接口，并自动维护 VS Code 用户配置，让编辑器中的模型入口可以像使用 Ollama 一样调用不同供应商的模型。

## 项目目标

- 支持多种模型供应商协议接入：sub2api 中转站协议、OpenAI Official、Claude Official、DeepSeek、NVIDIA NIM / build.nvidia.com、Moark 等。
- 将上游模型统一暴露为 Ollama 兼容协议，降低 VS Code 与本地工具链的接入成本。
- 自动修改 VS Code 用户目录中的 Ollama Provider 配置，当前只维护 `chatLanguageModels.json` 中的 `vscs` 条目。
- 提供熔断、重试、健康检查、限流、故障降级等稳定性能力。
- 基于 [OmniHost](https://github.com/maikebing/OmniHost) 优先实现 Windows 桌面端；macOS、Linux、WSL 暂不实现，后续跨平台阶段再补齐。
- 界面使用 Vue 3 + TypeScript，以 Visual Studio SPA 模式在项目内调试、构建和发布。
- 最终发布支持 AOT，并将 SPA 构建产物和 WebView2 Loader 作为嵌入式资源打包进单体应用。
- 系统托盘使用 Win32 原生实现，避免主项目依赖 WinForms 并影响 AOT 发布。
- UI 参考 `cc switch` 的快速切换体验，采用浅色卡片式供应商列表和添加/编辑表单，强调当前供应商、模型、密钥、代理和 VS Code 配置的一站式管理。

## 核心能力

### 协议转换

VSCopilotSwitch 计划维护一个统一的 Provider Adapter 层，将不同供应商的认证、模型列表、聊天补全、流式输出、错误结构和限流语义转换为 Ollama 兼容接口。

首批计划支持：

| 供应商 | 目标能力 | 备注 |
| --- | --- | --- |
| sub2api | ✅️ 中转站模型统一接入 | 已接入 OpenAI Chat Completions 兼容 Adapter，支持工具定义、工具结果消息和工具调用响应透传 |
| OpenAI Official | ✅️ 模型列表、chat/completions 兼容策略 | 已接入官方 `/v1/models` 和 `/v1/chat/completions`，支持工具调用；Responses API 后续按 VS Code 需求评估 |
| Claude Official | ✅️ Messages API 到 Ollama chat 语义转换 | 已接入官方 `/v1/models` 和 `/v1/messages`，支持 Anthropic tool use / tool result 映射 |
| DeepSeek | ✅️ OpenAI 兼容接口接入 | 已接入官方 `/models` 和 `/chat/completions`；默认按 text-only 暴露能力，不声明视觉；thinking/reasoning 走请求字段或推理模型名触发的专用链路 |
| NVIDIA NIM | ✅️ build.nvidia.com 模型接入 | 已接入 OpenAI-compatible `/v1/models` 和 `/v1/chat/completions`；模型能力默认保守声明，确认支持后再打开工具/视觉标签 |
| Moark | ✅️ 平台协议适配 | 已接入 OpenAI-compatible `/v1/models` 和 `/v1/chat/completions`；模型能力默认保守声明，确认支持后再打开工具/视觉标签 |

Copilot Chat 当前真实聊天入口使用 OpenAI-compatible `/v1/chat/completions`。本地代理会把上游工具调用稳定转换为非流式 `choices[].message.tool_calls` 或流式 `choices[].delta.tool_calls`，以便 Agent 模式继续进入工具结果回合。`/api/chat` 也保留 Ollama 官方兼容面，支持顶层 `tools`、`think` 和响应 `message.thinking`，方便 Copilot 之外的 Ollama 客户端复用。

模型发现阶段采用供应商/模型能力矩阵：未知模型默认只声明文本聊天；OpenAI / Claude 已知聊天模型按名称声明工具和视觉能力；DeepSeek V4 默认按 text-only 暴露，不声明 `vision`；thinking / reasoning 不默认影响 Copilot 模型选择，只在真实请求携带 `reasoning_effort` / `thinking` / Ollama `think` 或目标模型为 DeepSeek thinking/reasoner 系列时进入专用链路。

### VS Code 配置管理

工具会检测并维护以下文件：

- `C:\Users\mysti\AppData\Roaming\Code\User\chatLanguageModels.json`

当前写入格式遵循 VS Code 语言模型界面实际生成的 Provider 数组，只维护本项目的 Ollama 条目：

```json
{
  "name": "vscs",
  "vendor": "ollama",
  "url": "http://127.0.0.1:5124"
}
```

模型列表不再静态写入 VS Code 配置文件，而是由 VS Code 通过该 `url` 调用 `/api/tags` 和 `/api/show` 动态发现，例如 `gpt-5.5@vscs`。

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
- 托盘菜单可快速切换已保存密钥和模型名的真实提供商。
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

# 启动 HTTP Web 宿主和 Visual Studio SPA Proxy，访问 http://127.0.0.1:5124/
dotnet run --project src/VSCopilotSwitch --launch-profile http

# 仅调试 VS Code 专用 Ollama 兼容入口时，可显式监听 127.0.0.1:5124
dotnet run --project src/VSCopilotSwitch --urls http://127.0.0.1:5124

# SpaProxy 会自动以 HTTP 调用此脚本；也可单独启动 Vue 调试服务
npm --prefix src/VSCopilotSwitch.Ui run dev
```

## 供应商配置与运行时路由

当前桌面宿主以 UI 中保存并启用的供应商作为 Ollama 代理运行时来源。供应商的 API Key 使用当前 Windows 用户保护数据加密落盘，前端和内部列表 API 只返回脱敏预览；`/api/tags` 会向当前启用供应商实时获取模型列表，非流式 `/api/chat` 和流式 `/api/chat` 会在请求时读取当前启用供应商，并在内存中构建对应 Provider Adapter。未保存真实 API Key 或未配置真实供应商时，代理才回退到内置占位 Provider。

当前保存表单已经能配置供应商名称、协议类型、官网/API 地址、模型名和 API Key，并可在保存前或供应商列表中执行“测试连接”。协议类型用于选择底层 Adapter，目前可选 OpenAI-compatible、OpenAI Official、DeepSeek、Claude、NVIDIA NIM、MoArk 和 sub2api；sub2api 中转站应选择 `sub2api`。测试连接会按顺序验证 Base URL、API Key、模型列表和一次最小非流式聊天探测，返回给前端的错误会经过脱敏，不包含 API Key 原文。模型名称可以先留空，测试连接会从远程模型列表中优先选择 `gpt-5.5`，其次选择 `sonnet-4.6`，否则回填第一个返回模型。

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

DeepSeek thinking 专用链路只在请求携带 `reasoning_effort` / `thinking` / Ollama `think`，或目标模型名匹配 `deepseek-reasoner`、`deepseek-v4`、`deepseek-chat-v4`、`deepseek-v3.1` 等推理模型时启用。启用后会透传上游 `reasoning_content`，在同一进程内按工具调用 ID 缓存上一轮 reasoning，并在后续工具结果回合自动补回，避免 DeepSeek 工具调用场景缺失 `reasoning_content` 导致 400；普通 DeepSeek 请求会剥离历史 `reasoning_content`，避免把不该发送的字段带给上游。Ollama `/api/chat` 的 `think: "high"` 等字符串强度会桥接为上游 `reasoning_effort`。

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

Claude Adapter 会把 Ollama 侧 `system` 消息提升为 Anthropic Messages API 顶层 `system` 字段，普通 `user` / `assistant` 消息保持为消息数组，并将工具调用映射为 Anthropic `tool_use` / `tool_result`。

## 已实现 MVP API

- `GET /health`：宿主健康检查。
- `GET /api/tags`：Ollama 兼容模型列表；优先向 UI 当前启用供应商实时获取模型，并在每个模型上直接返回 400K 上下文、按能力矩阵声明的工具/视觉能力、`general.architecture`、`general.basename` 和 `{architecture}.context_length`，兼容 VS Code 只读取模型列表的场景。上游模型列表临时不可用时会用当前已保存模型降级生成清单，避免 Copilot 模型选择器收到 503；未保存真实 API Key 或未配置真实供应商时返回内置占位模型 `vscopilotswitch/default`。
- `GET /api/version`：Ollama 兼容版本探测接口，用于让 VS Code 确认本地代理满足 Ollama 0.6.4+ 要求。
- `POST /api/show`：Ollama 兼容模型详情接口，用于 VS Code Copilot Chat 探测模型能力和元信息；当前统一声明 400K 上下文，并按能力矩阵声明工具调用和视觉能力，不默认声明未验证的 thinking / reasoning 能力。
- `POST /api/chat`：Ollama 兼容非流式和流式聊天；优先转发到 UI 当前启用供应商对应的上游聊天接口，支持顶层 `tools`、Ollama 官方 `think`、历史消息 `message.thinking` 和响应 `message.thinking`；未配置真实供应商时走内置 `InMemoryModelProvider` 回显。
- `GET /v1/models`：OpenAI-compatible 标准模型发现接口，复用 `/api/tags` 的当前启用供应商模型列表，返回可直接用于 `/v1/chat/completions` 的 `@vscs` 模型 ID。
- `POST /v1/chat/completions`：Copilot Chat 当前真实聊天入口，支持 OpenAI-compatible 非流式和 SSE 流式响应、工具调用字段、usage、`reasoning_effort` / `thinking` 请求透传，以及 DeepSeek `reasoning_content` 响应映射。
- `GET /internal/about`：返回关于页面所需的应用标题、当前版本、GitHub 地址和企业微信二维码路径。
- `POST /internal/copilot/probe`：运行 Copilot 兼容最小探针，覆盖模型选择器、模型元信息、普通聊天、Agent 工具字段和流式结束。
- `GET /internal/vscode/user-directories`：发现本机可能的 VS Code User 配置目录。
- `POST /internal/vscode/apply-ollama`：对 `chatLanguageModels.json` 中的 `vscs` Ollama Provider 条目做干运行预览或安全写入，写入前会备份已有文件。
- `GET /internal/providers/export`：导出供应商配置；默认不包含 API Key 原文、脱敏预览或加密密文，只保留 `HasApiKey` 状态。

当前管理界面已提供 VS Code Ollama 配置写入向导：用户需要先选择 Windows VS Code User 目录并生成 dry-run 差异预览，确认 `vscs` Provider 条目的新增、更新或删除后才能二次确认写入；写入结果会展示备份路径、文件状态和字段级变化。顶部 VS Code Ollama 开关打开时只做状态检测，缺失配置时会跳转到写入向导并显示明确的预览/确认流程，不会静默写入。回滚入口会列出最近的 VSCopilotSwitch 备份，并在恢复指定备份前要求二次确认，同时为当前文件再创建安全备份。

端到端人工验收清单见 [docs/end-to-end-acceptance-checklist.md](docs/end-to-end-acceptance-checklist.md)，覆盖新增供应商、启用、测试连接、刷新模型、VS Code 写入、Copilot 调用、本地撤销和备份回滚。Copilot 兼容验收清单见 [docs/copilot-compatibility-acceptance.md](docs/copilot-compatibility-acceptance.md)，包含手工验证路径和当前自动化探针覆盖范围。

设置页还提供“关于”页面，展示应用标题、当前版本、GitHub 项目地址和企业微信二维码，便于用户确认版本并进入项目仓库或反馈渠道。

当配置预览、写入、备份读取或恢复失败时，界面会按权限不足、JSON 无效、文件占用、端口冲突等类型展示可执行修复建议，帮助用户在不覆盖原配置的前提下处理问题。

代理地址、熔断失败阈值、重试次数和备用路由等高级选项默认折叠，日常切换供应商时不会干扰主流程。

高级选项中的本地代理地址支持端口占用检测，可填写 `5124`、`127.0.0.1:5124` 或完整 URL，并提示 `127.0.0.1` 上的目标端口是否已被其他代理占用。VSCopilotSwitch 不再把 VS Code Provider URL 指向 Ollama 默认的 `11434`，避免与用户本机原生 Ollama 服务冲突。主窗口当前由 OmniHost Win32 + Native WebView2 承载；发布包运行时从单体程序内嵌的 SPA 静态资源加载界面，不依赖外部 `wwwroot` 目录。托盘菜单由 Win32 原生方式实现，可查看当前供应商和模型，并快速切换真实供应商；点击主窗口关闭按钮只会隐藏到托盘并保持本地代理运行，只有托盘“退出”会停止宿主进程，避免引入 WinForms 依赖。

自动更新策略默认启用发布版后台下载：宿主会定时读取 GitHub Release 信息，比较当前程序集版本和远端 `tag_name`，选择匹配 `VSCopilotSwitch` / `win-x64` / `aot` 的 `.exe`、`.zip` 或 `.msi` 资产并下载到 `%LOCALAPPDATA%\VSCopilotSwitch\Updates`。设置页“更新”选项卡也提供手动检查和下载入口。当前阶段只下载发布包，不会静默替换正在运行的单文件程序；开发环境通过 `appsettings.Development.json` 关闭后台自动下载。

发布 CI 位于 `.github/workflows/release.yml`，在 Windows runner 上执行完整链路：`npm install --prefix src/VSCopilotSwitch.Ui`、`npm run ui:build`、检查 `dist/index.html` 和静态资源数量、运行 .NET build/tests、发布 `win-x64` Native AOT 单文件、启动发布产物访问 `/health` 冒烟，并打包 `VSCopilotSwitch-<version>-win-x64-aot.zip` 与 `.sha256`。分支 push 和 PR 只构建、测试并上传 workflow artifact；只有推送 `v*` 标签时才会把这两个文件上传到 GitHub Release。Release zip 只包含 `VSCopilotSwitch.exe`，自动更新下载到的也是这个单文件包；本地可用 `npm run release:win-x64` 复用 npm install、SPA build 和 AOT 发布顺序。Release 发布配置显式启用 full trim 和 Native AOT size 优化，关闭发布包不需要的调试器、EventSource 与 HTTP activity propagation 支持，以控制单文件体积。

仓库包含一个无外部测试框架依赖的 VS Code 配置最小测试项目，覆盖配置写入幂等、备份列表和恢复前安全备份：

```powershell
dotnet run --project tests/VSCopilotSwitch.Core.Tests/VSCopilotSwitch.Core.Tests.csproj --no-restore
dotnet run --project tests/VSCopilotSwitch.Services.Tests/VSCopilotSwitch.Services.Tests.csproj --no-restore
dotnet run --project tests/VSCopilotSwitch.VsCodeConfig.Tests/VSCopilotSwitch.VsCodeConfig.Tests.csproj --no-restore
```
## 文档

- [路线图](ROADMAP.md)
- [智能体协作要求](AGENTS.md)
- [变更日志](CHANGELOG.md)

## 当前状态

阶段 5 首批 Provider Adapter 首版已完成，阶段 5.5 已把 UI、受保护供应商配置、Provider Adapter 和 Ollama 代理串成真实闭环：当前已支持 UI 启用供应商驱动 `/api/tags` 与 `/api/chat`，并在首页展示真实模型刷新状态、上游模型名和失败原因；供应商测试连接、协议类型选择、VS Code 写入当前代理模型、托盘快速切换和端到端验收清单也已接入。

右上角工具栏提供“分析统计”入口，可查看当前本地进程内存中的请求日志、监听端口状态、Token、费用、耗时和 User-Agent。分析服务会优先解析上游返回的真实 `usage`；若响应缺少 usage，才退回按正文长度估算，并在每条日志中标记来源。费用按本地 `UsagePricing` 单价表计算，不硬编码会过期的供应商官方价格；未配置单价时请求会标记为“未计价”。每条日志可展开查看脱敏后的请求头、请求体、响应头和响应体；Authorization、Cookie、API Key、Token 等敏感字段会被替换，正文采样也会限制长度。

可通过配置或环境变量维护本地单价表，费率单位为“每百万 Token”：

```json
{
  "UsagePricing": {
    "Currency": "USD",
    "Models": [
      {
        "ModelPattern": "gpt-5.5",
        "Label": "gpt-5.5 custom",
        "InputPerMillionTokens": 2.0,
        "OutputPerMillionTokens": 10.0
      },
      {
        "ModelPattern": "deepseek-*",
        "InputPerMillionTokens": 1.0,
        "OutputPerMillionTokens": 3.0
      }
    ]
  }
}
```

环境变量示例：`UsagePricing__Models__0__ModelPattern=gpt-5.5`、`UsagePricing__Models__0__InputPerMillionTokens=2`、`UsagePricing__Models__0__OutputPerMillionTokens=10`。

返回给 VS Code / Copilot 的模型名会追加 `@vscs` 后缀，`/api/tags` 会直接暴露 `gpt-5.5@vscs` 这类模型名，用于避免和 VS Code Copilot 内置模型名冲突。本地代理收到带后缀的模型请求后只用于路由识别，转发到上游 Provider 前会恢复为原始模型名，例如 `gpt-5.5`。

## 当前 OmniHost 接入状态

Windows 端已进入源码集成阶段：宿主项目直接引用 `external/OmniHost/src/OmniHost`、`external/OmniHost/src/OmniHost.Windows` 和 `external/OmniHost/src/OmniHost.NativeWebView2`。运行时会先启动 ASP.NET Core API / SPA 服务；开发模式优先使用 `launchSettings.json` / `ASPNETCORE_URLS` 中的固定地址，方便 Vite 代理对齐，未显式配置时再回退到 `127.0.0.1` 随机可用端口。随后使用 OmniHost 的 `Win32Runtime` 与 `NativeWebView2AdapterFactory` 打开原生窗口承载管理界面。

`OmniHost.NativeWebView2` 基于 `WebView2Aot` generated COM binding 实现，不再依赖 classic `Microsoft.Web.WebView2.Core` 托管 wrapper；发布时会把对应架构的 `WebView2Loader.dll` 作为嵌入式资源加载，便于 Native AOT 单文件分发。

当前阶段仍保留本地 HTTP 服务边界，便于 VS Code 配置 API、Ollama 兼容代理和 Vue SPA 继续复用现有开发链路；托盘菜单、窗口聚焦、关闭隐藏到托盘、快速切换供应商、退出代理和 GitHub Release 更新包缓存下载已在 OmniHost Windows 宿主层接入，后续会继续补齐跨平台宿主策略。
