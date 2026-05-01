# VS2026 大模型接口接入分析

记录时间：2026-05-01

本文用于分析 VSCopilotSwitch 后续如何接入 VS2026 的大模型能力。当前结论基于本仓库已经验证的 VS Code/Copilot Ollama Provider 路径，以及 Visual Studio 系列产品通常更封闭的扩展与网络边界；在拿到 VS2026 公开预览版前，不应把任何私有文件格式、内部服务地址或鉴权协议写死到实现中。

## 结论

优先路线不是“强行拦截 GitHub Copilot 私有接口”，而是寻找 VS2026 是否提供可配置的模型 Provider、Ollama/OpenAI-compatible 接入点或扩展 SDK 入口。只有用户明确配置到本地代理地址时，VSCopilotSwitch 才应作为本地兼容服务承接请求。

不建议做以下事情：

- 不劫持 GitHub Copilot / Microsoft 服务域名。
- 不安装根证书做 TLS 中间人解密。
- 不读取、复用或导出 VS / Copilot 的私有 Token、Cookie、设备码或会话凭据。
- 不 patch VS2026 二进制文件或注入进程修改内部调用。

这些路径安全风险高、容易破坏用户凭据，也很可能随版本变化失效。

## 已验证的 VS Code 路线对 VS2026 的启发

VS Code 当前可行路径是把 VSCopilotSwitch 注册成 Ollama Provider：

1. 写入用户配置目录下的 `chatLanguageModels.json` Provider 条目。
2. Provider 指向 `http://127.0.0.1:5124`。
3. VS Code 发现阶段调用 `/api/version`、`/api/tags`、`/api/show`。
4. Copilot Chat 实际聊天阶段调用 OpenAI-compatible 的 `/v1/chat/completions`。
5. VSCopilotSwitch 再把请求路由到当前启用的上游 Provider Adapter。

VS2026 如果提供类似“自定义模型 Provider”“Ollama”“OpenAI-compatible endpoint”“Bring your own key / endpoint”能力，就可以复用同一套本地代理协议面。

## VS2026 需要优先验证的入口

拿到 VS2026 后，建议按以下顺序只读验证：

1. 设置界面是否有 AI / Copilot / Model Provider / Ollama / OpenAI-compatible 相关配置。
2. 用户配置目录中是否出现可编辑的模型 Provider 文件。
3. ActivityLog、扩展日志、开发者日志中是否能看到对本地 Ollama 的探测请求。
4. VS SDK 是否暴露 Chat、Language Model、Copilot Agent、Tool Calling 或 MCP 相关扩展点。
5. 是否支持通过环境变量、注册表或 `.vsconfig` 指定模型 endpoint。

验证过程只记录请求路径、方法、响应形状和非敏感 Header；不得记录 Authorization、Cookie、设备码或完整 API Key。

## 推荐接入方案

### 方案 A：官方模型 Provider 配置

如果 VS2026 支持自定义 Ollama 或 OpenAI-compatible Provider，这是最优路径。

VSCopilotSwitch 需要做的工作：

- 增加 VS2026 配置发现服务，定位 VS2026 用户配置目录。
- dry-run 展示将写入的 Provider 名称、URL 和目标文件。
- 写入前备份，重复写入幂等，撤销时只移除本项目管理条目。
- 复用现有 `/api/version`、`/api/tags`、`/api/show`、`/api/chat`、`/v1/chat/completions`。
- 增加 VS2026 专用验收清单和最小探针。

### 方案 A1：通过 Azure BYOM 自定义 HTTPS URL 接入

二次探测显示 VS2026 的 Azure BYOM Provider 是自定义 URL Provider，Endpoint 枚举值为 `7`，模型配置中可保存 `CustomURL`。它使用 `Authorization: Bearer <api-key>`，并会校验 `<CustomURL>/models/<modelId>`。UI 层要求 URL 必须是 `https://`。

这条路线最适合 VSCopilotSwitch 试验接入：

- VSCopilotSwitch 增加本地 HTTPS OpenAI-compatible 监听，或引导用户配置 HTTPS 反向代理。
- 在 VS2026 Manage Models 中选择 Azure。
- API Key 填写用户自定义占位值，由 VSCopilotSwitch 仅用于通过 VS2026 校验，不作为上游密钥。
- Model ID 填写 VSCopilotSwitch 暴露的模型 ID，例如 `gpt-5.5@vscs`。
- Resource Endpoint / Custom URL 指向 VSCopilotSwitch 的 HTTPS `/v1` 基地址。

该路线仍必须由用户在 VS2026 UI 中显式配置，不能静默写入私有凭据。

### 不推荐：伪装 Foundry Local

虽然 BYOM 文件中 `Endpoint = 8` 对应 Foundry Local，但 VS2026 的 Foundry Local Provider 不读取 JSON 中的 URL。它会启动系统 `foundry service start`，从 CLI 输出中解析本地 `http://...` endpoint，然后请求相对路径 `/v1/models`。

因此，直接把 VSCopilotSwitch 写成 Foundry Local 条目大概率不会生效，还可能破坏用户真实 Foundry Local 配置。除非后续明确实现一个兼容的 `foundry` CLI 代理并由用户显式选择，否则不要走该路线。

### 方案 B：VS 扩展提供独立 AI 面板


如果 VS2026 不允许替换内置 Copilot Provider，但 VS SDK 允许创建工具窗口、命令或编辑器上下文扩展，可以发布一个 VSCopilotSwitch Visual Studio 扩展。

该方案不能“替换内置 Copilot”，但可以提供：

- 代码选区解释、重构、生成测试。
- 当前文件/解决方案上下文注入。
- 调用本地 VSCopilotSwitch 代理。
- 后续接 MCP / 工具调用。

### 方案 C：用户显式配置的本地代理


如果 VS2026 只允许填写 OpenAI-compatible endpoint，可以让用户手动或由向导写入本地地址：

- Base URL：`http://127.0.0.1:5124/v1`
- Chat endpoint：`/chat/completions`
- Models endpoint：`/models`（当前仓库需要补齐时再实现）

该方案仍属于显式配置，不属于网络劫持。

## 不推荐的“拦截”方案

网络层拦截内置 Copilot 请求不可作为产品路线：

- HTTPS 证书校验会阻止透明代理。
- Copilot 请求通常绑定 GitHub / Microsoft 身份和服务端策略。
- 私有 Token 不应被本项目读取或复用。
- 请求协议可能含内部字段，版本漂移成本高。
- 用户无法理解具体修改内容，违反本项目“低误操作、可回滚”的配置原则。

如果需要分析协议，只能在用户自有本地端点或明确允许的调试环境中记录 VSCopilotSwitch 端收到的请求，不对第三方 TLS 流量解密。

## 对当前代码的增量需求

短期不需要重写核心代理，优先增加 VS2026 探测与配置层：

- `VSCopilotSwitch.VsCodeConfig` 后续可抽象为 IDE 配置模块，拆出 VS Code 与 VS2026 profile。
- 增加 `VisualStudioConfigService`，只做只读发现、dry-run、备份、幂等写入和撤销。
- 在 UI 顶部 `VS2026` 入口中接入“检测 VS2026 支持情况”。
- 补充 `/v1/models`，以覆盖更标准的 OpenAI-compatible 模型发现路径。
- 增加请求日志中的客户端识别，区分 VS Code、VS2026、Ollama CLI 和其他客户端。

## 最小验收路径

如果 VS2026 支持官方自定义 endpoint，最小闭环应为：

1. VSCopilotSwitch 监听 `127.0.0.1:5124`。
2. VS2026 配置向导 dry-run 展示目标文件和 Provider 差异。
3. 用户确认后写入本地代理 Provider，并创建备份。
4. VS2026 模型选择器能看到 `@vscs` 或等价后缀模型。
5. 普通聊天进入 `/v1/chat/completions` 或 `/api/chat`。
6. Agent / 工具调用字段能被转发并回传。
7. 撤销只删除 VSCopilotSwitch 管理的配置，不影响用户其他 AI 设置。

## 当前状态

当前只能把 VS2026 视为待验证目标。仓库已经具备本地兼容代理、Provider Adapter、脱敏日志、VS Code 配置写入和回滚能力；下一步应先做 VS2026 公开版本的只读侦测工具和人工验收清单，再决定是否实现写入。