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

## 对实现的要求

- VS2026 配置发现应枚举 `%LOCALAPPDATA%\Microsoft\VisualStudio\18.0_*`，不要硬编码 `22a431f4`。
- 写入前必须确认 `ConfiguredBringYourOwnModel_v1.json` 的完整真实格式，尤其是 `Models` 元素和自定义 URL 的存储位置，不能只凭摘要条目生成字段。
- 写入目标应优先放在全局 Copilot BYOM 文件，而不是安装目录或 MCP 缓存目录。
- 如果未来写入该文件，应只追加或更新 `Name` 属于 VSCopilotSwitch 管理的条目，并保留用户的 Foundry Local、Azure 等已有条目。
- MCP 路径可用于后续工具接入，但不应被包装成模型 Provider 替代方案。
- 所有写入仍必须 dry-run、备份、幂等、可撤销，并只修改 VSCopilotSwitch 管理的条目。

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

未执行任何写入操作，也未读取或输出 VS / Copilot 的私有 Token、Cookie、Authorization Header 或设备凭据。