# VS2026 用户配置与 AI Provider 配置位置探测

记录时间：2026-05-01

本文记录在当前 Windows 机器上对 VS2026 / Visual Studio 18.0 的只读配置探测结果。所有路径均使用环境变量表达，避免把固定用户名写入实现。

## 已发现实例

当前机器存在 Visual Studio 18.0 / VS2026 候选实例：

- Roaming 用户配置根：`%APPDATA%\Microsoft\VisualStudio\18.0_22a431f4`
- Local 用户状态根：`%LOCALAPPDATA%\Microsoft\VisualStudio\18.0_22a431f4`
- 全局 Copilot 状态根：`%LOCALAPPDATA%\Microsoft\VisualStudio\Copilot`
- 安装根：`%ProgramFiles%\Microsoft Visual Studio\18\Community`

同时存在非实例化的 `18.0` 目录和 VS2022 `17.0` 目录。实现中不能假设实例 ID 固定，应枚举 `18.0_*`，并允许用户在 UI 中选择目标实例。

## 用户设置文件

VS2026 的主要 JSON 设置文件位于：

- `%LOCALAPPDATA%\Microsoft\VisualStudio\18.0_22a431f4\settings.json`

已观察到的 Copilot 相关键包括：

- `copilot.general.chat.preferredChatMode`
- `copilot.general.chat.enableCopilotCodingAgent`
- `copilot.general.chat.enableDotNetProjectSpecificInstructions`
- `copilot.general.chat.defaultAgent`
- `copilot.general.chat.enablePlanning`
- `copilot.general.chat.autoSelectRefinementMode`
- `copilot.general.chat.preferredModelFamily`
- `copilot.featureFlags.chatUI.enabledTools`
- `copilot.featureFlags.chatUI.enabledMcpServers`
- `copilot.general.tools.toolSettings`

当前未在该文件中发现可直接配置 Ollama、OpenAI-compatible endpoint、base URL 或 AI Provider URL 的字段。`preferredModelFamily` 只记录模型族偏好，例如 `gpt-5.4`，不是本地 Provider 接入点。

## Bring Your Own Model 配置位置

当前机器发现 VS2026 Copilot 的 Bring Your Own Model 配置目录：

- `%LOCALAPPDATA%\Microsoft\VisualStudio\Copilot\BringYourOwnModel`

其中存在：

- `%LOCALAPPDATA%\Microsoft\VisualStudio\Copilot\BringYourOwnModel\ConfiguredBringYourOwnModel_v1.json`

首次探测时内容为一个空 JSON 数组：

```json
[]
```

用户通过 VS2026 UI 增加 BYOM 条目后，二次探测到当前内容为：

```json
[
  {
    "Name": "Foundry Local",
    "IsApiKeyAvailable": true,
    "Models": [],
    "Endpoint": 8
  },
  {
    "Name": "Azure",
    "IsApiKeyAvailable": true,
    "Models": [],
    "Endpoint": 7
  }
]
```

已确认字段含义的安全部分：

- `Name` 是 VS2026 UI 中显示的自定义模型配置名称。
- `IsApiKeyAvailable` 只表示密钥是否存在，不包含密钥原文。
- `Models` 当前为空数组，本轮尚未观察到模型清单结构。
- `Endpoint` 是枚举型数字，不是 URL 字符串；本轮观察到 `7` 对应 Azure，`8` 对应 Foundry Local。

这说明 VS2026 的 BYOM 文件不是完整 Provider 配置文件，而是“已配置 Provider 摘要”。API Key 和 endpoint 详细参数很可能存储在 VS 私有设置、凭据存储或服务账户体系中。VSCopilotSwitch 不能只靠写这个文件完成接入，必须继续观察真实自定义 endpoint 类型是否会在此处或其他文件中落出 URL 字段。

## MCP 配置位置

当前机器发现 VS2026 Copilot 的 MCP 状态目录：

- `%LOCALAPPDATA%\Microsoft\VisualStudio\Copilot\McpServers`

其中 `.cache` 文件是工具缓存，不适合作为用户配置写入目标。

安装侧内置 MCP 配置位于：

- `%ProgramFiles%\Microsoft Visual Studio\18\Community\Common7\IDE\Extensions\Microsoft\Copilot\Mcp\mcp.json`

已观察到内容：

```json
{
  "servers": {
    "Microsoft Learn": {
      "url": "https://learn.microsoft.com/api/mcp"
    }
  }
}
```

用户设置中 `copilot.featureFlags.chatUI.enabledMcpServers` 会引用安装侧 MCP 配置路径，例如 Microsoft Learn 和 NuGet。该机制更适合接入工具能力，不等同于替换大模型 Provider。

## Copilot 安装组件线索

VS2026 安装目录中存在 Copilot 相关组件：

- `%ProgramFiles%\Microsoft Visual Studio\18\Community\Common7\IDE\Extensions\Microsoft\Copilot\Conversations.Service`
- `%ProgramFiles%\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\Microsoft.VisualStudio.Copilot.Contracts`

其中包含 `OpenAI.dll`、`ModelContextProtocol.Core.dll`、`ModelContextProtocol.dll` 和 Visual Studio Copilot contracts 相关程序集。这说明 VS2026 内置聊天服务侧已经包含 OpenAI SDK 与 MCP 支持，但本轮只读扫描没有发现可通过安装目录直接修改的 AI Provider 配置模板。

## 当前结论

本机 VS2026 / Visual Studio 18.0 的配置落点可以分为三类：

1. 常规 Copilot 设置：`%LOCALAPPDATA%\Microsoft\VisualStudio\18.0_22a431f4\settings.json`
2. 自定义模型候选入口：`%LOCALAPPDATA%\Microsoft\VisualStudio\Copilot\BringYourOwnModel\ConfiguredBringYourOwnModel_v1.json`
3. MCP 工具配置与缓存：安装侧 `Mcp\mcp.json` 加本地 `McpServers` 缓存

当前最有价值的 AI Provider 线索仍是 `ConfiguredBringYourOwnModel_v1.json`。二次探测已经确认它记录 BYOM 摘要条目，但只包含名称、密钥可用状态、模型数组和 endpoint 枚举，不包含 URL 或 API Key。下一步应在 VS2026 UI 中新增 OpenAI-compatible / custom endpoint 类型条目，或为当前条目选择具体模型，再观察 `Models` 的元素结构和是否出现 URL 字段。

## BYOM Provider 行为分析

官方资料显示 Visual Studio Copilot Chat 的 BYOM 从模型选择器的 Manage Models 入口进入，支持 OpenAI、Anthropic、Google 等模型；同类 SSMS Copilot 文档还列出 Azure 与 Foundry Local。Azure 模型需要 API Key、模型部署名和 Resource Endpoint。Foundry Local 是本机模型运行时，公开资料显示其本地 OpenAI-compatible endpoint 常见形态为 `http://localhost:5272/openai/v1`，示例 API Key 为 `foundry-local`。

对 VS2026 Copilot 组件做只读类型分析后，观察到以下实现细节：

- `CopilotModelEndpoint` 枚举值为：`GitHub = 1`、`OpenAI = 2`、`Anthropic = 3`、`Google = 4`、`xAI = 5`、`GitHubResponses = 6`、`AzureFoundry = 7`、`FoundryLocal = 8`、`GitHubAnthropicMessages = 9`。
- `ModelProvider` 会持久化 `Name`、`IsApiKeyAvailable`、`Models`、`Endpoint`。
- `Model` 会持久化 `Id`、`DisplayName`、`IsToolCallingEnabled`、`IsVisionEnabled`、`MaxInputTokens`、`MaxOutputTokens`、`ProviderName`、`IsCustom`、`IsSelected` 和 `CustomURL`。
- API Key 不在 BYOM JSON 中，VS2026 使用安全凭据存储，键名形态为 `providerName@githubUserName` 的小写字符串。
- `AzureFoundryModelProvider` 是自定义 URL Provider，Provider 名称为 `Azure`，Endpoint 为 `AzureFoundry = 7`，使用 `Authorization: Bearer <api-key>`，模型验证走 `<CustomURL>/models/<modelId>`。UI 增加自定义模型时要求 `CustomURL` 必须是 `https://` 绝对地址。
- `FoundryLocalModelProvider` 名称为 `Foundry Local`，Endpoint 为 `FoundryLocal = 8`，不读取 BYOM JSON 中的 URL；它会执行 `foundry service start`，从命令输出解析 `on (http://...)`，再把该地址作为 BaseAddress，请求相对路径 `/v1/models`。

因此：

- 不能简单把 VSCopilotSwitch 写成 `Foundry Local` 条目来接入。VS2026 会启动系统 `foundry` CLI 并使用 CLI 返回的本地 endpoint，而不是读取 BYOM JSON 的 `CustomURL`。
- 可以优先验证 Azure 自定义 URL 路线：把 VSCopilotSwitch 暴露为 HTTPS OpenAI-compatible 服务，然后在 VS2026 的 Azure BYOM 中添加模型，`CustomURL` 指向 VSCopilotSwitch 的 HTTPS 地址。
- 由于 Azure 自定义 URL 校验强制 HTTPS，`http://127.0.0.1:5124` 不能直接通过 VS2026 UI 校验；发布版会额外准备受当前用户信任的本地 HTTPS 监听，开发环境可通过 `VSCOPILOTSWITCH_HTTPS_URL` 显式开启。

## 对实现的要求

- VS2026 配置发现应枚举 `%LOCALAPPDATA%\Microsoft\VisualStudio\18.0_*`，不要硬编码 `22a431f4`。
- 写入前必须确认 `ConfiguredBringYourOwnModel_v1.json` 的完整真实格式，尤其是 `Models` 元素和自定义 URL 的存储位置，不能只凭摘要条目生成字段。
- 写入目标应优先放在全局 Copilot BYOM 文件，而不是安装目录或 MCP 缓存目录。
- 如果未来写入该文件，应只追加或更新 `Name` 属于 VSCopilotSwitch 管理的条目，并保留用户的 Foundry Local、Azure 等已有条目。
- MCP 路径可用于后续工具接入，但不应被包装成模型 Provider 替代方案。
- 所有写入仍必须 dry-run、备份、幂等、可撤销，并只修改 VSCopilotSwitch 管理的条目。
- 不应伪造 `Foundry Local` Provider，因为它依赖系统 `foundry` CLI 生命周期。除非后续明确实现兼容的 `foundry` 命令代理，否则该路线不可作为安全产品方案。
- 若走 Azure BYOM，应先实现并验证 HTTPS OpenAI-compatible endpoint：至少覆盖 `GET /models`、`GET /models/{id}`、`POST /chat/completions`，并接受 `Authorization: Bearer <用户在 VS2026 输入的占位密钥>`。

## Azure BYOM 试验配置

VSCopilotSwitch 当前已补齐 Azure BYOM 试验所需的最小服务端能力：

- `GET /v1/models`
- `GET /v1/models/{modelId}`
- `POST /v1/chat/completions`
- `GET /internal/vs2026/byom`

发布版会在 `http://127.0.0.1:5124` 之外默认尝试启用 `https://127.0.0.1:5443`，用于让 VS2026 Azure BYOM 接受本地地址。启动时程序会自动生成 `localhost/127.0.0.1/::1` 本地服务器证书，把带私钥证书写入当前用户 `My` 证书库，把公钥证书写入当前用户 `Root` 信任根，并把该证书交给 Kestrel；AOT 单文件不依赖 `dotnet dev-certs`。

开发环境默认不自动启用 HTTPS，若需要验证 VS2026，可显式设置：

```powershell
$env:VSCOPILOTSWITCH_HTTPS_URL = 'https://127.0.0.1:5443'
dotnet run --project .\src\VSCopilotSwitch\VSCopilotSwitch.csproj
```

也可以用 `VSCOPILOTSWITCH_VS2026_AUTO_HTTPS=false` 关闭发布版自动 HTTPS。启动后访问 `GET /internal/vs2026/byom` 获取建议填写值。VS2026 中选择 Azure Provider 后，按以下规则填写：

- API Key：使用 `vscs-local` 这类占位值，VSCopilotSwitch 不把它当作上游密钥。
- Model ID：使用 `GET /internal/vs2026/byom` 返回的 `ModelId`，例如 `gpt-5.5@vscs`。
- Resource Endpoint / Custom URL：使用 `GET /internal/vs2026/byom` 返回的 `Endpoint`，形如 `https://127.0.0.1:5443/v1`。

该流程仍是用户在 VS2026 UI 中显式配置，不直接写入 VS2026 私有凭据。

## 本轮只读命令摘要

本轮执行了以下类别的只读检查：

- 枚举 `%APPDATA%\Microsoft\VisualStudio`、`%LOCALAPPDATA%\Microsoft\VisualStudio` 和 `%ProgramFiles%\Microsoft Visual Studio`。
- 检索 VS2026 实例目录中的 Copilot、model、OpenAI、Ollama、provider、endpoint 关键词。
- 读取空的 `ConfiguredBringYourOwnModel_v1.json`。
- 读取 `settings.json` 中 Copilot 相关设置键。
- 读取安装侧 Copilot MCP `mcp.json`。
- 用户新增 BYOM 条目后，重新读取 `ConfiguredBringYourOwnModel_v1.json`，确认 Foundry Local 和 Azure 条目形状。
- 检查最近 6 小时内 VS2026 / Copilot 相关文件变更，只发现 BYOM JSON 是 Copilot 目录下的直接变更；VS 实例目录下另有 `CurrentSettings.vssettings`、`ApplicationPrivateSettings.xml`、`privateregistry.bin` 等常规状态变更。
- 在 `ApplicationPrivateSettings.xml`、`CurrentSettings.vssettings` 和 `settings.json` 中检索 `BringYourOwnModel`、`Foundry Local`、`Azure`、`Endpoint` 等关键词，未发现 BYOM URL 形态配置；`CurrentSettings.vssettings` 仅暴露 Copilot 常规开关和空的 `preferredModelFamily` 默认项。
- 检索官方 Microsoft Learn 资料，确认 Visual Studio BYOM 入口在 Copilot Chat 模型选择器；Azure 需要部署名和资源 endpoint；Foundry Local 是本地运行时能力。
- 只读反编译 VS2026 Copilot BYOM 相关类型，确认 Azure 是自定义 HTTPS URL Provider，Foundry Local 通过 `foundry service start` 获取 endpoint。

未执行任何写入操作，也未读取或输出 VS / Copilot 的私有 Token、Cookie、Authorization Header 或设备凭据。
