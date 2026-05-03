# Roadmap

本文档描述 VSCopilotSwitch 的阶段性路线图。路线图会随着协议验证、OmniHost 集成和 VS Code 配置行为变化持续调整。

本阶段路线图先收敛到 Windows 桌面端：优先完成 Windows 下的路径、权限、窗口、托盘和后台进程闭环；macOS、Linux、WSL 暂不实现，只保留设计约束和后续扩展入口。随后再让 UI 的关键写入动作可理解、可预览、可回滚，降低误操作风险。

## 状态标记

| 编号 | 标记 | 含义 |
| --- | --- | --- |
| 0.1 | ✅️ | 已完成 |
| 0.2 | 🔧 | 进行中 |
| 0.3 | ⬜ | 未开始 |
| 0.4 | 🔴 现在做 | 当前主线，先做，做完后才能稳定推进后续识别闭环 |
| 0.5 | 🟡 可并行 | 可以和当前主线同时推进，不应阻塞主线 |
| 0.6 | 🔵 后做 | 依赖前置能力，等主线跑通后再做 |

## 诚实落地规则

- ✅️ 只表示代码已实现、已接入真实调用路径，并至少通过本地构建或测试验证。
- 🔧 表示已经开始实现，但仍有关键链路未接通、缺少测试或仍依赖占位数据。
- ⬜ 表示尚未开始编码，不能在变更日志或验收说明中描述成已完成。
- UI 骨架、静态假数据、仅 appsettings 配置可用、仅单元层通过，都不能等同于产品闭环完成。

当前主线：✅️ 阶段 5.5 真实功能闭环和阶段 5.6 Copilot Ollama Provider 真实协议补强已完成。继续采用“伪装为 Ollama Provider”的接入方式；发现阶段维护 `/api/version`、`/api/tags`、`/api/show`、OpenAI-compatible `/v1/models` 和 `/v1/models/{modelId}`，聊天阶段以 Copilot 当前真实调用的 `/v1/chat/completions` 为主，同时补齐 Ollama 官方 `/api/chat` 的 `tools` / `think` / `message.thinking` 兼容面。VS2026 已进入 Azure BYOM A1 接入试验：发布版可自动启用本机回环 HTTPS、生成并信任当前用户本地证书，再把 `/v1` 暴露给 Azure CustomURL；Foundry Local 依赖系统 `foundry` CLI 不适合伪装；不采用 TLS 中间人、域名劫持或 Token 复用路线。下一步进入阶段 6 稳定性与路由。

## 阶段 0：项目基线

目标：建立可运行的工程骨架和清晰模块边界。

- ✅️ 明确项目目标：把多供应商模型转换为 Ollama 兼容协议。
- ✅️ 建立 README、路线图、智能体要求和变更日志。
- ✅️ 统一路线图任务状态标记：使用 `✅️`、`🔧`、`⬜`、`🔴 现在做`、`🟡 可并行`、`🔵 后做` 六类状态。
- ✅️ 初始化 .NET 解决方案。
- ✅️ 建立 `src/VSCopilotSwitch` 宿主项目，最终可执行文件名为 `VSCopilotSwitch`。
- ✅️ 建立 `VSCopilotSwitch.Core`、`VSCopilotSwitch.VsCodeConfig`、`VSCopilotSwitch.Ui` 模块。
- ✅️ 初始化 Vue 3 SPA 工程文件和项目内 npm 脚本。
- ✅️ 按 Visual Studio SPA 模式加入前端 `.esproj`，并让后端通过 `SpaProxy` 关联 Vue 调试服务。
- ✅️ 将前端工程切换为 VueApp2 同款 Vue 3 + TypeScript 技术栈。
- ✅️ 确认 OmniHost 正式集成方式：当前阶段通过 `external/OmniHost` 源码项目引用接入，便于同步修改 Win32/WebView2 宿主能力。
- ✅️ 明确当前阶段安全边界：本地代理默认只监听 `127.0.0.1`，配置写入必须 dry-run、备份、幂等且不得记录敏感密钥；许可证和外部贡献流程顺延到公开发布前补齐。

验收标准：

- 后端解决方案可构建。
- 宿主项目输出名稳定为 `VSCopilotSwitch`。
- 文档中的工程结构和实际目录一致。

## 阶段 1：Windows 运行基线

目标：阶段 1 只实现 Windows 下的可运行闭环，先把宿主窗口、本地代理、VS Code User 配置目录和开发启动路径稳定下来；macOS、Linux、WSL 后期再实现，当前不得为了四端兼容阻塞 Windows MVP 收尾。

- ✅️ 定义当前阶段平台范围：只实现 Windows 桌面端，macOS、Linux、WSL 明确移入后续跨平台阶段。
- ✅️ 明确 Windows 配置路径基线：优先发现 `%APPDATA%\Code\User`，路径解析不得假设固定用户名。
- ✅️ 明确非 Windows 行为：暂不自动写入 macOS、Linux、WSL 配置，后续实现前必须补齐选择策略和测试样例。
- ✅️ 实现本地代理监听基线：管理界面服务使用 `127.0.0.1` 随机可用端口启动，Ollama 调试端口可显式指定。
- ✅️ 默认只监听 `127.0.0.1`，不默认暴露到局域网。
- ✅️ 明确后台代理生命周期基线：当前由 ASP.NET Core 宿主进程管理启动和退出，托盘退出清理在后续宿主层补齐。
- ✅️ 明确托盘能力边界：阶段 1 先落地窗口承载，系统托盘、聚焦窗口和快速切换菜单移入后续 Windows 桌面增强。
- ✅️ 确认 OmniHost 窗口 API 的落地方式：Windows 端采用 `Win32Runtime` + `NativeWebView2AdapterFactory`，托盘 API 后续单独抽象。
- ✅️ 修复开发启动依赖注入基线：Ollama 代理服务显式接收全部 Provider 注册，避免多构造函数导致宿主启动失败。

验收标准：

- Windows 路径选择逻辑有明确基线，优先使用 `%APPDATA%\Code\User`，不硬编码用户名。
- 非 Windows 和 WSL 当前不自动写入用户配置，避免静默修改未选择的配置文件。
- 本地服务默认只绑定 `127.0.0.1`。
- Windows 原生窗口可通过 OmniHost Win32 + WebView2 承载管理界面。

## 阶段 2：低误操作 UI 与配置向导

目标：所有关键写入动作都让用户能理解“将修改什么、为什么修改、如何恢复”。

- ✅️ 建立 Vue 3 + TypeScript SPA 主界面骨架。
- ✅️ 展示当前代理状态、模型列表和供应商切换入口。
- ✅️ 展示 VS Code 配置 dry-run 预览入口。
- ✅️ 完成 `cc switch` 风格主界面布局：顶部快速切换、供应商卡片列表、启用/编辑/查询状态操作区。
- ✅️ 将主界面品牌替换为 VSCopilotSwitch 专属名称与 logo，并将顶部入口调整为 `VS2026` / `VSCode`。
- ✅️ 完成添加/编辑供应商表单布局：名称、备注、官网、API Key、API 请求地址、模型名称和 auth.json 预览。
- ✅️ 完成新增供应商表单基线：右上角新增入口进入空白表单，编辑入口才带入已有供应商信息。
- ✅️ 完成供应商列表交互基线：供应商链接使用系统默认浏览器打开，供应商卡片支持上下拖拽排序。
- ✅️ 对接供应商管理界面与宿主配置 API：读取、保存、启用、删除和排序供应商，并加密保存 API Key。
- ✅️ 完成供应商页面滚动基线：编辑表单可向下滚动，首页供应商列表按数量撑开且最多显示 5 个后内部滚动。
- ✅️ 建立独立设置页面，并将 VS Code 配置写入向导迁移到“常规”选项卡、配置回滚迁移到“备份”选项卡。
- ✅️ 精简设置页面：移除供应商和高级熔断占位页面，未完成能力不再作为空入口暴露。
- ✅️ 设置页新增“关于”页面：展示应用标题、当前版本、GitHub 地址和企业微信二维码。
- ✅️ 实现配置写入向导骨架：选择 VS Code 配置目录、预览差异、确认写入、显示结果。
- ✅️ 实现顶部 VS Code Ollama 配置开关：开启时检查配置状态，关闭时备份并撤销本项目托管字段。
- ✅️ 实现差异视图：展示 `settings.json` 和 `chatLanguageModels.json` 的托管字段级变化。
- ✅️ 实现回滚入口：列出最近备份并恢复指定备份，恢复前会为当前文件创建安全备份。
- ✅️ 实现高风险动作确认基线：写入配置和恢复备份需要二次确认；包含密钥导出、退出并停止代理将在对应功能实现时补齐确认。
- ✅️ 实现失败修复建议：权限不足、JSON 无效、文件被占用、端口冲突等场景给出可执行处理步骤。
- ✅️ 默认折叠高级选项：代理、熔断阈值、重试次数、备用路由。

验收标准：

- 用户在点击写入前能看到目标文件、修改字段和备份位置。
- 默认流程不会要求用户理解内部协议细节。
- 所有失败状态都有可执行的下一步建议。
- 重复执行配置向导不会产生重复条目。

## 阶段 3：VS Code 配置管理 MVP

目标：安全、可回滚地修改 VS Code 的 Ollama 相关配置。

- ✅️ 实现跨平台 VS Code User 配置目录发现基础能力。
- ✅️ 读取 `settings.json` 和 `chatLanguageModels.json`。
- ✅️ 提供 dry-run 配置预览 API。
- ✅️ 写入前自动创建时间戳备份。
- ✅️ 避免覆盖非本项目管理的用户配置。
- ✅️ 修复 Windows VS Code 目录发现：配置向导现在定位到 `Code\User` / `Code - Insiders\User` / `VSCodium\User`，并兼容旧路径自动规范化，避免误写产品根目录。
- 🟡 可并行 实现 JSON with comments 兼容策略。
- ✅️ 实现字段级 diff 输出，返回本项目托管字段的原值、新值和变化状态。
- ✅️ 提供一键回滚到指定备份。
- ✅️ 补齐 VS Code 配置最小测试：写入幂等、备份列表、恢复安全备份。
- 🟡 可并行 补齐 Windows、macOS、Linux、WSL 路径测试。

验收标准：

- 重复执行不会产生重复配置。
- 用户已有设置不会被删除。
- 任意写入失败时可以恢复到写入前状态。
- WSL 场景下能明确提示当前将修改 Windows 侧还是 Linux 侧配置。

## 阶段 4：Ollama 兼容代理 MVP

目标：启动本地服务，把上游供应商模型暴露为 Ollama 风格接口。

- ✅️ 实现 `/api/tags` 模型列表接口。
- ✅️ 实现 `/api/show` 模型详情接口，兼容 VS Code Copilot Chat 的 Ollama 探测请求。
- ✅️ 实现 `/api/chat` 非流式聊天接口。
- ✅️ 建立 Provider 抽象和内置占位 Provider。
- ✅️ 实现 `/api/chat` 流式聊天接口。
- ✅️ 实现基础错误映射。
- ✅️ 核心层支持模型别名和多 Provider 路由。
- ✅️ 宿主运行时 Provider 已接入 UI 当前启用供应商，`/api/tags` 和 `/api/chat` 不再只依赖 appsettings 或占位 Provider。
- ✅️ 支持本地端口配置和端口占用检测，兼容端口号、host:port 和完整 URL 输入。
- ✅️ 补齐 Ollama 代理核心测试：别名路由、流式分块、未知模型、重复别名和 Provider 错误映射。

验收标准：

- VS Code 可通过 Ollama 相关配置发现并调用代理模型。
- 代理服务能返回稳定的 Ollama 兼容响应结构。
- 上游错误不会直接泄露敏感字段。
- 当前阶段的基础模型切换链路已接入 UI 启用供应商；测试连接、VS Code 写入当前可用模型、Provider 类型选择和托盘快速切换已落地。

## 阶段 5：首批 Provider Adapter

目标：支持主流模型供应商和中转协议。

阶段状态：✅️ 首批 Provider Adapter 首版已完成。

- ✅️ sub2api 中转站协议首版 Adapter：支持 `/v1/models`、`/v1/chat/completions` 非流式/流式转换、Bearer 鉴权、HTTP 错误映射和 API Key 脱敏。
- ✅️ OpenAI Official 首版 Adapter：支持官方 `/v1/models`、`/v1/chat/completions` 非流式/流式转换、Bearer 鉴权、OpenAI 组织/项目请求头和 API Key 脱敏。
- ✅️ Claude Official 首版 Adapter：支持官方 `/v1/models`、`/v1/messages` 非流式/流式转换、`x-api-key` 鉴权、`anthropic-version` 请求头、system 消息提升和 API Key 脱敏。
- ✅️ DeepSeek 首版 Adapter：支持官方 `/models`、`/chat/completions` 非流式/流式转换、Bearer 鉴权、HTTP 错误映射和 API Key 脱敏。
- ✅️ NVIDIA NIM / build.nvidia.com 首版 Adapter：支持 `/v1/models`、`/v1/chat/completions` 非流式/流式转换、Bearer 鉴权、HTTP 错误映射和 API Key 脱敏。
- ✅️ Moark 首版 Adapter：支持 OpenAI-compatible `/v1/models`、`/v1/chat/completions` 非流式/流式转换、Bearer 鉴权、HTTP 错误映射和 API Key 脱敏。

每个 Adapter 需要包含：

- 模型列表获取。
- Chat 请求转换。
- 流式响应转换。
- 错误码映射。
- 鉴权配置。
- 最小集成测试。

## 阶段 5.5：真实功能闭环

目标：把已经存在的 UI、配置服务、Provider Adapter 和 Ollama 代理串成真实可用链路，避免停留在骨架或假数据。

阶段状态：✅️ 已完成。UI、受保护供应商配置、Provider Adapter、Ollama 代理、VS Code 配置写入、托盘快速切换和端到端验收清单已形成真实闭环。

- ✅️ 供应商配置持久化 API：读取、保存、新增、编辑、删除、启用、排序。
- ✅️ 供应商 API Key 使用当前 Windows 用户保护数据加密落盘，前端只显示脱敏预览。
- ✅️ 首页供应商列表和编辑表单已接宿主供应商配置 API，不再只使用前端内置假数据。
- ✅️ 将当前启用供应商转换为运行时 `IModelProvider`，接入 `/api/tags`、`/api/chat` 和流式 `/api/chat`；未保存真实 API Key 时回退到内置占位 Provider。
- ✅️ 让模型列表从当前启用供应商实时获取，并在 UI 展示 VS Code/Copilot 可见的 `@vscs` 后缀模型名、刷新状态和失败原因。
- ✅️ 新增分析统计页面：展示本地请求日志、监听端口状态、真实 usage / 估算 Token、按本地单价表精算的费用、耗时、User-Agent，以及脱敏后的请求头、请求体、响应头和响应体。
- ✅️ 新增供应商“测试连接”：验证 Base URL、API Key、模型列表和最小聊天探测，错误必须脱敏；模型可留空并从远程列表自动优先选择 `gpt-5.5` / `sonnet-4.6` 或第一个模型。
- ✅️ 保存供应商时支持选择协议类型：OpenAI-compatible、OpenAI Official、DeepSeek、Claude、NVIDIA NIM、MoArk、sub2api。
- ✅️ VS Code 配置写入应使用当前本地代理地址和当前可用模型，而不是固定默认模型。
- ✅️ VS Code 暴露模型名追加 `@vscs` 后缀，`/api/tags` 返回 `gpt-5.5@vscs` 格式，避免与 Copilot 内置模型冲突，代理转发上游前去掉该后缀。
- ✅️ 补齐 Ollama `/api/version` 兼容接口，满足 VS Code 对 Ollama 0.6.4+ 的版本校验。
- ✅️ 补齐 Ollama `/api/show` 模型能力元信息：上下文大小按 400K 返回，功能声明后续已收敛为按供应商/模型能力矩阵返回。
- ✅️ 对齐 TrafficPilot 的 `/api/tags` 模型元信息：在模型列表层直接返回 400K 上下文和能力字段，覆盖 VS Code 未请求 `/api/show` 时的默认 33K 显示；工具/视觉标签后续已改为按矩阵声明。
- ✅️ `/api/tags` 模型列表失败时降级为当前已保存模型，避免上游临时不可用导致 VS Code Copilot 模型选择器直接收到 503。
- ✅️ `/api/tags` 和 `/api/show` 曾加入 `thinking` / `reasoning` 探测字段；阶段 5.6 已收敛为不默认声明未验证能力，后续只在真实请求日志确认后再恢复专用链路。
- ✅️ 完成 VS Code 语言模型配置实测分析：真实文件为 `%APPDATA%\Code\User\chatLanguageModels.json`，Ollama Provider 条目为 `{ name, vendor, url }`，模型列表由 Ollama 兼容接口动态发现。
- ✅️ 重写 VS Code 配置写入逻辑：改为幂等维护 `chatLanguageModels.json` 数组中的本项目 Ollama Provider 条目，不再写入自定义静态模型清单。
- ✅️ VS Code Provider URL 默认使用 `http://127.0.0.1:5124`，取消写入 Ollama 默认 `11434` 的兼容路径，避免与用户本机 Ollama 服务冲突。
- ✅️ 移除主项目 WinForms 依赖，Native AOT 发布不再需要通过 `_SuppressWinFormsTrimError` 绕过 SDK 检查。
- ✅️ 发布运行时从内嵌 SPA 静态资源加载管理界面，不再依赖发布目录中的外部 `wwwroot`。
- ✅️ 顶部 VS Code Ollama 开关打开时，在检测缺失后提供一键跳转和明确的写入流程，不静默修改配置。
- ✅️ 使用 Win32 原生托盘实现显示当前启用供应商和模型，并支持快速切换真实供应商。
- ✅️ 增加供应商配置 API 的最小测试：保存不回传密钥、启用唯一供应商、排序幂等、删除后自动选择可用供应商。
- ✅️ 增加并执行端到端人工验收清单：新增供应商、启用、刷新模型、VS Code 写入、VS Code 调用、本地撤销；仓库内可自动验证部分已通过构建和三组测试，真实 VS Code/Copilot 界面步骤已固化为发布前人工复核项。

验收标准：

- UI 中启用哪个供应商，`/api/tags` 和 `/api/chat` 就使用哪个供应商。
- 未配置真实供应商时才允许回退到内置占位 Provider，且 UI 必须清楚提示。
- 任意 Provider 错误返回给 UI 和 Ollama 客户端时不得泄露 API Key、Authorization Header 或 Cookie。
- 供应商配置写入、删除、排序和启用多次执行后不会产生重复条目或状态漂移。

## 阶段 5.6：Copilot Ollama Provider 真实协议补强

目标：继续保持无需 VS Code 扩展的 Ollama Provider 接入方式，但以 Copilot Chat 当前真实消费路径为准补齐协议面，避免优先实现 Copilot 不会调用的 Ollama 官方扩展字段。

阶段状态：✅️ 已完成。Copilot Chat 通过 `vscs` Ollama Provider 的模型发现、普通聊天、Agent 工具调用和 DeepSeek thinking 专用链路已落地；Ollama 官方 `/api/chat` thinking/tool 扩展也已补齐最小兼容，但 thinking / reasoning 仍不默认进入 Copilot 能力声明。

### 实现顺序

1. ✅️ 完成 Copilot Ollama Provider 调用路径核对：当前发现阶段会访问 `/api/version`、`/api/tags`、`/api/show`，真实聊天阶段走 OpenAI-compatible 的 `/v1/chat/completions`，模型能力主要读取 `/api/show` 的 `capabilities` 与 `model_info`。
2. ✅️ 实现本地 `/v1/models` 与 `/v1/chat/completions` 入口：模型发现返回可直接调用的 `@vscs` 模型 ID；聊天支持 OpenAI Chat Completions 非流式和 SSE 流式响应，复用当前启用供应商路由、模型后缀剥离、错误脱敏和取消流程，并兜底常见 URL 拼接变体避免继续返回 405。
3. ✅️ 扩展核心 Chat 请求/响应中间模型：支持 `tools`、`tool_choice`、assistant `tool_calls`、tool result 消息、`finish_reason`、usage 和流式 delta，避免继续把 Provider 抽象限制在纯文本 `role/content`。
4. ✅️ 将 Copilot 传入的 OpenAI function tools 转发到 Provider Adapter：OpenAI-compatible、OpenAI Official、DeepSeek、NVIDIA NIM、MoArk 先走 OpenAI 工具协议；Claude 单独映射到 Anthropic tool use。
5. ✅️ 补齐工具调用响应回传：上游 `tool_calls` 需要稳定转换为 OpenAI-compatible `/v1/chat/completions` 的 `choices[].message.tool_calls` 或流式 `choices[].delta.tool_calls`，并增加 HTTP 出口形状回归测试，确保 Copilot Agent 模式能继续执行工具结果回合。
6. ✅️ 精确调整 `/api/show` 与 `/api/tags` 能力声明：按 Copilot 当前读取方式返回 `capabilities` 中的 `tools` / `vision`，并提供 `model_info["general.architecture"]`、`model_info["general.basename"]`、`model_info["{architecture}.context_length"]`；移除或降级未被真实使用的自定义能力字段。
7. ✅️ 建立按供应商和模型的能力矩阵：DeepSeek V4 默认 text-only，不声明 `vision`；只有确认支持工具调用的模型才声明 `tools`；thinking / reasoning 先作为内部候选能力，不默认影响 Copilot 模型选择。
8. ✅️ 增加 Copilot 兼容验收清单和最小自动化探针：覆盖模型出现在选择器、普通聊天、Agent 模式工具调用、上游错误脱敏、流式响应结束、模型列表失败降级。
9. ✅️ 在真实 VS Code 请求日志确认 Copilot 会发送 reasoning / thinking 相关字段后，实现 DeepSeek thinking 专用链路：`reasoning_content` 同进程缓存、工具结果跨轮恢复、流式 `reasoning_content` 映射和 400 reasoning 错误修复提示；该链路仅由请求字段或 DeepSeek thinking 模型触发，不默认对 Copilot 能力声明暴露。
10. ✅️ 完成 Ollama 官方 `/api/chat` 的 `tools` / `think` / `message.thinking` 兼容评估与最小实现：`tools` 已走现有管道，新增 `think` 请求字段、历史消息 `message.thinking` 读取、响应 `message.thinking` 输出和 DeepSeek `think: "high"` 到 `reasoning_effort` 的桥接；Copilot 主路径仍保持 `/v1/chat/completions`，不因该兼容面默认虚报 thinking/reasoning 能力。
11. ✅️ 加固 OpenAI-compatible SSE 转发：纯 `reasoning_content` 流式分块也会写给客户端，避免推理模型在普通 content 为空时被 Copilot 误判为无响应。
12. ✅️ 内置 VS2026 本地 HTTPS 证书流程：发布版自动准备 `https://127.0.0.1:5443`，生成只覆盖 `localhost/127.0.0.1/::1` 的当前用户证书，写入 `My` / `Root` 证书库并提供给 Kestrel，AOT 单文件不再依赖 `dotnet dev-certs`。
13. ✅️ 增加设置页 VS2026 面板：顶部 `VS2026` 按钮直达 Azure BYOM 填写信息，自动读取 `/internal/vs2026/byom`，展示 Provider、HTTPS Endpoint、Model ID、API Key 占位值和校验/聊天 URL，并支持逐项复制。
14. ✅️ 修复 Copilot 错误分类兼容：OpenAI-compatible 出口将 `provider_unavailable` 映射为 502 / `api_error`，避免 Copilot 把上游网络不可用误报为限流；真实 `provider_rate_limited` 继续保持 429 / `rate_limit_error`。

验收标准：

- VS Code Copilot Chat 通过 `vscs` Ollama Provider 能发现模型并发起 `/v1/chat/completions` 请求。
- OpenAI-compatible 客户端可通过 `/v1/models` 发现与 `/api/tags` 一致的可调用模型 ID。
- 普通聊天和流式聊天均可通过当前 UI 启用的真实供应商返回结果。
- Agent 模式下 Copilot 能向代理发送工具定义，代理能把上游工具调用稳定回传给 Copilot。
- `/api/show` 和 `/api/tags` 不虚报 `vision`、`tools`、thinking/reasoning 等能力，避免 Copilot 发送当前模型无法处理的输入。
- 所有上游错误和本地请求日志都继续脱敏 API Key、Authorization Header、Cookie 和代理密码。

## 阶段 6：稳定性与路由

目标：让多供应商聚合具备生产可用的容错能力，并在 UI 中清楚解释当前路由状态。

- 🔵 后做 熔断器：失败阈值、半开探测、自动恢复。
- 🔵 后做 重试策略：可按供应商、模型和错误类型配置。
- 🟡 可并行 超时控制：连接超时、首 token 超时、总请求超时；当前 Adapter 层已有基础超时配置，但 UI 还未接真实策略。
- 🔵 后做 备用路由：主模型失败后切换到备用模型。
- 🔵 后做 限流策略：按供应商、模型和 API Key 控制并发。
- 🟡 可并行 健康检查：API Key、模型列表和聊天探测；需先完成阶段 5.5 的启用供应商真实路由。
- ✅️ 本地日志：脱敏记录请求摘要、耗时、失败原因、真实 usage、估算 Token 和按本地单价表计算的费用。
- 🔵 后做 UI 展示熔断、重试、降级和健康状态，并避免用模糊状态误导用户。

验收标准：

- 单个供应商故障不会拖垮整个本地代理。
- 熔断状态在 UI 和托盘菜单中可见。
- 敏感信息不会出现在日志中。

## 阶段 7：OmniHost + Vue 3 单体应用

目标：提供 Windows、macOS、Linux、WSL 可运行的桌面体验，并用 Vue 3 SPA 承载主要 UI。

- ✅️ 集成 OmniHost 工程结构，宿主项目直接引用 `OmniHost`、`OmniHost.Windows`、`OmniHost.NativeWebView2` 三个源码项目。
- ✅️ 新增 `OmniApplication` 组合宿主生成器：外层 API 对齐 ASP.NET Core 的 `CreateBuilder` / `CreateSlimBuilder` / `CreateEmptyBuilder`，内部组合本地 Web 服务和桌面窗口生命周期，避免应用入口手动维护两套启动/退出流程。
- ✅️ 新增 Native WebView2 适配器：基于 `WebView2Aot` 生成式 COM binding 创建环境和 HWND controller，提供导航、尺寸同步和 JS bridge，并嵌入 WebView2 Loader 以支持 AOT 单文件发布。
- ✅️ 收敛 Native AOT JSON 警告：主程序、Provider 配置、OpenAI-compatible / Claude Provider、VS Code 配置写入和 OmniHost 窗口事件均改为源生成或显式 `JsonNode.WriteTo`，AOT 单文件发布不再出现 IL2026 / IL3050。
- ✅️ 修复 Native WebView2 JS bridge 初始化闪退：不再释放 `AddScriptToExecuteOnDocumentCreated` 完成回调返回的脚本 ID，发布版窗口可持续运行。
- ✅️ 修复嵌入式 SPA 静态资源路由：发布版 `/assets/*.js`、`/assets/*.css`、favicon 均由内嵌资源返回，避免窗口白屏。
- ✅️ 优化 Win32 + Native WebView2 首屏显示：启动时先完成 WebView2 初始化和首次导航，再显示主窗口，减少空白窗口闪现。
- ✅️ 优化 VS Code 配置预览失败诊断：后端返回明确的 JSON、权限和文件占用错误，前端直接展示具体原因，并补充无效 JSON 测试。
- ✅️ 接入 Win32 原生托盘和窗口图标：标题栏/任务栏、发布版 exe 和系统托盘使用 VSCopilotSwitch 图标，托盘菜单支持打开主界面和退出程序。
- ✅️ 用 `src/assets/logo.svg` 重新生成 `public/favicon.ico`，统一浏览器 favicon、程序 ICO、窗口和托盘图标来源。
- ✅️ 实现主窗口生命周期：关闭按钮隐藏到托盘，托盘打开恢复聚焦，托盘退出才停止本地代理。
- ✅️ 实现 Windows 托盘图标最小增强：打开或聚焦主界面。
- ✅️ 实现 Windows 托盘菜单增强：右键菜单支持打开主界面、显示当前供应商和模型、快速切换真实供应商、退出程序。
- ✅️ 实现托盘菜单：快速切换当前提供商。
- ✅️ 实现 Windows 托盘菜单最小增强：退出程序并触发宿主关闭流程，随后停止本地代理。
- ✅️ 配置 SPA 构建产物输出目录，供宿主发布流程收集。
- ✅️ 将 Vue 3 SPA 构建产物作为嵌入式资源打包到单体应用。
- ✅️ 开发模式下可通过 HTTP Visual Studio SPA Proxy 连接 npm 调试服务。
- ✅️ 前端 JSON 配置文件使用无 BOM UTF-8，避免 Vite/PostCSS 解析失败。
- ✅️ 接入 VS Code Workbench 风格主题层，预留 OmniHost 宿主主题变量注入接口。
- ✅️ 宿主标题栏切换为系统原生样式，页面配色默认跟随操作系统深浅色偏好。
- ✅️ 运行时优先从嵌入式资源加载 SPA。
- ✅️ 支持 AOT 发布：当前已能生成 Native AOT 单文件主程序，并切换到 Native WebView2；业务层 JSON/JsonNode 的 AOT 警告已收敛。
- ✅️ 修复 OpenAI-compatible `tool_choice` 字符串序列化的 Native AOT 分析警告，改用源生成 `JsonTypeInfo` 路径。
- ✅️ 完成 Windows `win-x64` AOT 单文件发布最小冒烟：唯一 exe 复制到干净目录后可启动本地服务并通过 `/health` 返回 Production/ok。
- ✅️ 收紧 AOT 发布体积：显式启用 full trim、Native AOT size 优化和必要的运行时功能裁剪，当前单文件 exe 约 17.3 MB。

UI 方向：

- 参考 `cc switch` 的快速切换体验，但所有关键写入动作必须有明确预览和确认。
- 使用 Vue 3 组织 SPA 页面、状态管理和路由。
- 强调当前生效模型、上游供应商、健康状态和 VS Code 配置状态。
- 避免复杂设置淹没主流程，高级配置集中到展开区域。

## 阶段 8：安全与发布

目标：让项目可持续分发和维护。

- 🔧 API Key 本地加密存储：供应商配置已用 Windows 当前用户保护数据加密；仍需抽象为跨平台 Secret Store，并补配置导入导出策略。
- 🟡 可并行 日志脱敏和崩溃报告脱敏。
- ✅️ 配置导出时默认不包含密钥。
- ✅️ 自动更新策略：从 GitHub Release 检查更高版本，自动下载匹配 Windows 单文件发布资产到本地缓存，更新替换仍保留为用户可控步骤。
- ✅️ 发布 CI：GitHub Actions 覆盖 npm install、SPA build、嵌入式资源生成检查、.NET 测试和 AOT 单体应用打包；分支/PR 只构建，`v*` 标签才发布 Release 资产。
- ✅️ 发布流程包含 npm install、SPA build、嵌入式资源生成、AOT 单体应用打包。
- ✅️ 修复发布 CI 版本解析：标签版本、预发布版本、build metadata 和非 SemVer 分支名均可稳定生成 .NET 包版本、程序集版本和 Release 标签。
- ✅️ 移除发布 CI 中启动完整桌面 exe 的 `/health` 冒烟步骤；运行时冒烟保留为本地发布前人工验收，避免无交互 Windows runner 上的 WebView2、托盘和窗口会话差异阻塞打包。
- 🔵 后做 安装包签名策略。
- 🔵 后做 用户迁移说明。
- 🟡 可并行 示例配置和故障排查文档。

## 关键风险

- VS Code / Copilot Chat 的配置格式可能变化，需要保持文档追踪和版本兼容。
- 各供应商的流式响应语义不同，工具调用、多模态、推理内容字段需要逐步兼容。
- WSL 场景路径复杂，需要避免误改用户不期望的配置文件。
- 中转站协议差异较大，需要通过可扩展 Adapter 处理站点私有字段。
- 本地代理如果处理不当，可能泄露 API Key 或请求内容，必须优先设计脱敏和权限边界。
- SPA 资源嵌入单体应用后，需要处理缓存、版本号和本地调试资源切换，避免开发资源泄露到正式包。
- AOT 和单文件发布可能影响反射、动态加载和资源访问方式，需要在早期验证。
- UI 如果把“预览”和“写入”混在一起，用户可能误改 VS Code 配置，必须拆成独立步骤。
