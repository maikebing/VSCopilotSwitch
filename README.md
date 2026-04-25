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
| sub2api | 中转站模型统一接入 | 兼容 OpenAI 风格接口和站点自定义字段 |
| OpenAI Official | 模型列表、chat/completions、responses 兼容策略 | 以官方 API 为基线 |
| Claude Official | Messages API 到 Ollama chat 语义转换 | 处理 system、tool use、stream delta |
| DeepSeek | OpenAI 兼容接口接入 | 保留推理模型字段映射 |
| NVIDIA NIM | build.nvidia.com 模型接入 | 兼容 NVIDIA 推理服务模型命名 |
| Moark | 平台协议适配 | 根据实际 API 文档补齐 |

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

## 已实现 MVP API

- `GET /health`：宿主健康检查。
- `GET /api/tags`：Ollama 兼容模型列表，当前返回内置占位模型 `vscopilotswitch/default`。
- `POST /api/chat`：Ollama 兼容非流式聊天，当前走内置 `InMemoryModelProvider` 回显，用于验证协议链路。
- `GET /internal/vscode/user-directories`：发现本机可能的 VS Code User 配置目录。
- `POST /internal/vscode/apply-ollama`：对 `settings.json` 和 `chatLanguageModels.json` 做干运行预览或安全写入，写入前会备份已有文件。

当前管理界面已提供 VS Code Ollama 配置写入向导骨架：用户需要先选择 Windows VS Code User 目录并生成 dry-run 差异预览，确认目标文件和托管字段变更后才能执行写入；写入结果会展示备份路径、文件状态和字段级变化。
## 文档

- [路线图](ROADMAP.md)
- [智能体协作要求](AGENTS.md)
- [变更日志](CHANGELOG.md)

## 当前状态

阶段 0 和阶段 1 已按 Windows 优先范围收尾：项目已具备 OmniHost-ready 工程骨架、Visual Studio SPA 模式的 Vue 3 + TypeScript 管理首页、VS Code 配置管理模块、Ollama 兼容代理 MVP，以及 Win32 + WebView2 原生窗口承载能力。下一步会继续接入真实 Provider Adapter、Windows 系统托盘和配置写入向导。

## 当前 OmniHost 接入状态

Windows 端已进入源码集成阶段：宿主项目直接引用 `external/OmniHost/src/OmniHost`、`external/OmniHost/src/OmniHost.Windows` 和 `external/OmniHost/src/OmniHost.WebView2`。运行时会先在 `127.0.0.1` 随机可用端口启动 ASP.NET Core API / SPA 服务，再使用 OmniHost 的 `Win32Runtime` 与 `WebView2AdapterFactory` 打开原生窗口承载管理界面。

当前阶段仍保留本地 HTTP 服务边界，便于 VS Code 配置 API、Ollama 兼容代理和 Vue SPA 继续复用现有开发链路；后续托盘菜单、窗口聚焦、快速切换供应商和退出代理会在 OmniHost 宿主层继续补齐。
