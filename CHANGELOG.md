# Changelog

本项目遵循“面向用户可见变化”的变更日志记录方式。版本号在进入可运行版本后再正式启用。

## Unreleased

### Added

- ✅️ 阶段 5 启动首个真实 Provider Adapter：新增 sub2api 中转站协议接入，支持 `/v1/models` 模型列表获取、OpenAI Chat Completions 非流式请求转换和 SSE 流式响应转换。
- ✅️ 宿主支持通过 `Providers:Sub2Api` 配置启用 sub2api；未配置 `BaseUrl` / `ApiKey` 时继续使用内置占位 Provider，便于本地开发安全启动。
- ✅️ 新增 sub2api 最小集成测试，覆盖模型列表、上游模型名转换、流式分块解析和 HTTP 错误映射。
- ✅️ 新增 OpenAI Official Provider Adapter，支持官方 `/v1/models`、`/v1/chat/completions`、SSE 流式响应、OpenAI 组织/项目请求头和错误脱敏。
- ✅️ 抽取 OpenAI-compatible Provider 共享层，供 sub2api、OpenAI Official 以及后续 DeepSeek / NVIDIA NIM 等 Adapter 复用通用 HTTP、SSE 和错误映射逻辑。
- ✅️ 宿主支持同时配置 `Providers:Sub2Api` 与 `Providers:OpenAI`，未配置任何真实 Provider 时继续回退到内置占位 Provider。
- ✅️ 新增 OpenAI Official 最小集成测试，覆盖官方端点路径、鉴权头、组织/项目头、上游模型名转换、流式分块解析和 API Key 脱敏。
- ✅️ 新增 DeepSeek Provider Adapter，支持官方 `/models`、`/chat/completions`、SSE 流式响应、Bearer 鉴权和错误脱敏。
- ✅️ OpenAI-compatible 共享层新增可选 API 路径前缀，OpenAI/sub2api 保持 `/v1` 路径，DeepSeek 默认使用无 `/v1` 的官方路径。
- ✅️ 宿主支持通过 `Providers:DeepSeek` 配置启用 DeepSeek，并可与 sub2api / OpenAI Official 同时注册。
- ✅️ 新增 DeepSeek 最小集成测试，覆盖官方端点路径、上游模型名转换、流式分块解析和 API Key 脱敏。
- ✅️ 新增 NVIDIA NIM Provider Adapter，支持 OpenAI-compatible `/v1/models`、`/v1/chat/completions`、SSE 流式响应、Bearer 鉴权和错误脱敏。
- ✅️ 新增 MoArk Provider Adapter，支持 OpenAI-compatible `/v1/models`、`/v1/chat/completions`、SSE 流式响应、Bearer 鉴权和错误脱敏。
- ✅️ 新增 Claude Official Provider Adapter，支持 Anthropic `/v1/models`、`/v1/messages`、SSE 流式事件、`x-api-key` 鉴权、`anthropic-version` 请求头、system 消息提升和错误脱敏。
- ✅️ 宿主支持通过 `Providers:NvidiaNim`、`Providers:Moark`、`Providers:Claude` 配置启用对应 Provider，并可与已实现 Provider 同时注册。
- ✅️ 新增 NVIDIA NIM、MoArk、Claude Official 最小集成测试，覆盖端点路径、鉴权头、请求转换、流式分块解析和 API Key 脱敏。
- ✅️ 收尾阶段 0 和阶段 1：明确当前阶段只实现 Windows 桌面端，macOS、Linux、WSL 顺延到后续跨平台阶段。
- ✅️ 新增 VS Code Ollama 配置写入向导骨架，要求先选择目录并生成 dry-run 差异预览，再允许确认写入和查看备份结果。
- ✅️ 配置预览结果新增字段级差异，展示 `settings.json` 和 `chatLanguageModels.json` 中本项目托管字段的原值与新值。
- ✅️ 新增 VS Code 配置回滚入口，可列出最近 VSCopilotSwitch 备份并恢复指定备份；恢复前会为当前文件再创建安全备份。
- ✅️ 为 VS Code 配置写入和备份恢复加入二次确认提示，降低误触修改用户配置的风险。
- ✅️ 新增顶部 VS Code Ollama 配置开关：开启时检查配置是否存在，关闭时备份并移除本项目管理的 VS Code Ollama 配置。
- ✅️ 新增供应商配置持久化 API，支持读取、保存、启用、删除和拖拽排序，并使用当前 Windows 用户保护数据加密保存 API Key。
- ✅️ 阶段 5.5 真实功能闭环接入：Ollama `/api/tags`、非流式 `/api/chat` 和流式 `/api/chat` 现在会读取 UI 当前启用供应商，并在内存中构建对应 Provider Adapter。
- ✅️ 首页新增当前供应商模型列表刷新区：实时调用 `/api/tags` 展示模型数量、上次刷新时间、真实模型和脱敏失败原因；启用、保存或删除供应商后会自动刷新模型列表。
- ✅️ 新增供应商“测试连接”闭环：从列表或编辑表单验证 Base URL、API Key、模型列表和最小非流式聊天探测，并在 UI 中展示逐步结果和脱敏错误。
- ✅️ 新增供应商模型自动选择：模型名称可先留空，测试连接会远程拉取模型列表，优先回填 `gpt-5.5` / `sonnet-4.6`，否则回填第一个模型；保存失败会显示后端返回的具体字段原因。
- ✅️ 新增供应商协议类型选择：保存和测试连接可选择 OpenAI-compatible、OpenAI Official、DeepSeek、Claude、NVIDIA NIM、MoArk 或 sub2api，并将该协议用于运行时 Provider Adapter。
- ✅️ 优化高级选项端口检测：支持 `5124`、`127.0.0.1:5124` 和完整 URL，并将端口格式错误显示在检测结果区，避免误触发全局端口冲突修复建议。
- ✅️ 修复 Vite 开发代理端口漂移：开发启动时优先使用 `launchSettings.json` / `ASPNETCORE_URLS` 中的固定后端地址，避免前端代理继续请求已停止的随机端口。
- ✅️ 优化首页模型列表展示：状态面板只显示上游模型名称，不再把内部 Provider 路由前缀显示给用户。
- ✅️ 新增分析统计页面入口：右上角工具栏可打开本地请求统计，查看内存请求日志、监听端口状态、估算 Token、耗时和 User-Agent，日志不记录密钥或请求正文。
- ✅️ 分析统计日志新增调试详情：可展开查看脱敏后的请求头、请求体、响应头和响应体，并限制采样长度避免泄露密钥或撑爆页面。
- ✅️ VS Code 配置预览和写入改为使用当前运行中的本地代理地址与当前可用模型，不再固定写入旧版默认地址和 `vscopilotswitch/default`。
- ✅️ VS Code/Copilot 暴露模型名新增 `@vscc` 后缀，`/api/tags` 直接返回 `gpt-5.5@vscc` 这类公开模型名，避免与原生模型名冲突；本地代理转发上游前会去掉该后缀。
- ✅️ 新增 Ollama 兼容 `/api/version` 接口，返回 0.6.4 以上兼容版本，修复 VS Code 无法验证 Ollama server version 的提示。
- ✅️ 新增 Ollama 兼容 `/api/show` 接口，返回当前 Provider 路由的模型元信息，修复 VS Code Copilot Chat 请求 `/api/show` 时出现 405 的问题。
- ✅️ `/api/show` 模型元信息新增 400K 上下文、工具调用和视觉能力声明，让 VS Code 模型列表能显示上下文大小与功能标签。
- ✅️ 对齐 TrafficPilot 的 Ollama 模型列表结构：`/api/tags` 每个模型现在直接返回 `context_length`、`capabilities`、`model_info`、`supports_tool_calling` 和 `supports_vision`，避免 VS Code 未调用 `/api/show` 时回退到 33K 默认上下文。
- ✅️ `/api/tags` 新增远程模型列表失败降级：上游临时不可用或模型列表端点异常时，优先用当前已保存模型生成 VS Code 可见清单，避免 Copilot 模型选择器收到 503。
- ✅️ `/api/tags` 和 `/api/show` 新增 `thinking` / `reasoning` 能力声明与推理级别候选元信息，用于对齐 Copilot 推理模型选择器的能力识别路径。
- ✅️ 补充 VS Code 语言模型配置实测记录：确认真实入口为 `%APPDATA%\Code\User\chatLanguageModels.json` 的 Provider 数组，Ollama 条目使用 `name` / `vendor` / `url`，为后续重写配置写入逻辑提供依据。
- ✅️ 重写 VS Code 配置写入逻辑：废弃旧版 `settings.json` 自定义字段和静态 `vscopilotswitch.models` 清单，只幂等维护 `chatLanguageModels.json` 数组中的 `vscc` Ollama Provider 条目。
- ✅️ VS Code Provider URL 改用 VSCopilotSwitch 专用端口 `http://127.0.0.1:5124`，取消写入 Ollama 默认 `11434` 的兼容路径，避免与原生 Ollama 服务冲突。
- ✅️ 移除主项目 WinForms 依赖和临时 `NotifyIcon` 托盘实现，避免 `UseWindowsForms` 阻塞 Native AOT 单文件发布；托盘能力后续改由 Win32 原生路径补齐。
- ✅️ 显式引用 `System.Security.Cryptography.ProtectedData`，让 API Key 的 Windows 当前用户保护数据加密不再依赖 WinForms 间接引用。
- ✅️ 宿主发布运行时改为从程序集内嵌 `Spa\...` 资源加载 Vue 静态产物，AOT 单文件包不再依赖外部 `wwwroot` 目录。
- ✅️ 新增 `OmniApplication` 组合宿主入口，提供 `CreateBuilder`、`CreateSlimBuilder`、`CreateEmptyBuilder` / `CreateEmpty`，让本地 ASP.NET Core 服务和 OmniHost 桌面壳由同一个应用生命周期统一启动和停止。
- ✅️ 新增 `OmniHost.NativeWebView2` 项目，基于 `WebView2Aot` 对接原生 WebView2 COM binding，并将 `WebView2Loader.dll` 嵌入资源；VSCopilotSwitch 启动界面已切换到 `NativeWebView2AdapterFactory`，避免 Native AOT 下 classic WebView2 wrapper 的 COM marshalling 问题。
- ✅️ 将主程序、Provider 配置、上游 Provider、VS Code 配置写入和 OmniHost 生命周期事件的 JSON 读写改为 Native AOT 友好的源生成或显式 `JsonNode.WriteTo` 路径，消除 AOT 发布中的 IL2026 / IL3050 警告。
- ✅️ 修复 Native WebView2 启动闪退：JS bridge 注入脚本完成回调返回的脚本 ID 由 WebView2 管理，不再手动释放，避免 WebView2Aot 路径触发原生崩溃。
- ✅️ 修复 AOT 单文件发布版管理界面白屏：嵌入式 SPA 资源改用 GET catch-all 路由提供，确保 `/assets/*.js`、`/assets/*.css` 和 favicon 不再被默认 fallback 的 nonfile 规则漏掉。
- ✅️ 优化 VS Code 配置预览错误提示：配置接口会把 JSON 格式、目录权限、文件占用等可恢复错误作为 400 返回，前端显示后端具体原因，不再统一误报为目录权限或 JSON 格式问题。
- ✅️ 新增 AOT 友好的 Win32 原生窗口图标和托盘图标：发布版 exe、标题栏/任务栏和系统托盘使用同一 VSCopilotSwitch 图标，托盘右键菜单支持打开主界面和退出程序。
- ✅️ 新增失败修复建议面板，针对权限不足、JSON 无效、文件占用和端口冲突给出可执行处理步骤。
- ✅️ 新增默认折叠的高级选项面板，集中放置代理地址、熔断阈值、重试次数和备用路由。
- ✅️ 新增 VS Code 配置最小测试项目，覆盖配置写入幂等、备份列表和恢复前安全备份。
- ✅️ 新增供应商配置 API 最小测试项目，覆盖保存不回传密钥、启用唯一供应商、排序幂等和删除后自动选择可用供应商。
- ✅️ 修复供应商排序 API 仍按旧 SortOrder 回排的问题，拖拽排序现在会按请求顺序稳定归一化。
- ✅️ 新增供应商配置导出 API 和首页导出入口，默认导出不包含 API Key 原文、脱敏预览或加密密文，只保留密钥存在状态。
- ✅️ 新增本地端口占用检测 API 与高级选项中的端口检测提示。
- ✅️ 实现主窗口生命周期：点击关闭按钮时隐藏到托盘并保持本地代理运行，托盘“打开”恢复聚焦，托盘“退出”才真正停止宿主进程。
- ✅️ 新增 Windows 托盘菜单最小增强，支持打开或聚焦主界面、查看当前提供商和代理状态、退出并停止本地代理。
- ✅️ Win32 原生托盘菜单接入当前启用供应商和模型状态，并支持对已保存密钥和模型名的真实供应商做快速切换。
- ✅️ 重新设计 VSCopilotSwitch 专属 SVG logo，并替换首页左上角 `CC Switch` 品牌展示。
- ✅️ 首页右上角快速入口从 `Claude` / `Codex` 调整为 `VS2026` / `VSCode`，并替换为对应 IDE 风格图标。
- ✅️ 接入 OmniHost Windows 原生宿主：`src/VSCopilotSwitch` 直接引用 `OmniHost`、`OmniHost.Windows`、`OmniHost.NativeWebView2`，启动本地 ASP.NET Core 服务后由 Win32 + Native WebView2 窗口承载 Vue 管理界面。
- ✅️ 将 OmniHost 直接依赖和传递依赖加入 `VSCopilotSwitch.slnx`，修复 Visual Studio 生成主项目时未先生成 `OmniHost.Abstractions` / `OmniHost.Core` 等外部项目导致的 `CS0006`。
- ✅️ 设置页移除供应商和高级熔断占位页面，仅保留 VS Code 配置写入与备份回滚入口，减少未完成能力对用户的干扰。
- ✅️ 使用 `src/assets/logo.svg` 重新生成 `public/favicon.ico`，让浏览器 favicon、程序 ICO、窗口和托盘图标统一使用 VSCopilotSwitch 标识。
- ✅️ 顶部 VS Code Ollama 开关打开时只做状态检测；缺失配置时跳转到写入向导并显示明确预览/确认流程，不静默修改 VS Code 配置。
- ✅️ 完成一次 Windows `win-x64` AOT 单文件发布验证：先构建 Vue SPA，再通过 `publish:aot` 生成 Release 自包含发布目录，并把唯一 exe 复制到干净目录通过 `/health` 最小冒烟。
- ✅️ `/api/chat` 支持 Ollama 兼容流式 NDJSON 响应，`stream: true` 时按分块返回并追加最终 `done` 分块。
- ✅️ Ollama 代理新增 Provider 路由和模型别名解析，支持按完整模型名、上游模型名或别名定位目标 Provider。
- ✅️ Ollama 代理新增基础错误映射，未知模型、重复别名、Provider 鉴权、限流、超时和上游错误会返回脱敏后的 `{ error, code }`。
- ✅️ 新增 Ollama 代理核心测试项目，覆盖别名路由、流式分块、未知模型、重复别名和 Provider 错误映射。

- 初始化 .NET 解决方案和 OmniHost-ready 宿主骨架。
- 新增 VSCopilotSwitch.Core，包含 Ollama 协议模型、Provider 抽象和内置占位 Provider。
- 新增 VSCopilotSwitch.VsCodeConfig，支持 VS Code User 目录发现、settings.json / chatLanguageModels.json 干运行预览、安全写入和备份。
- 新增 `src/VSCopilotSwitch` 宿主项目，最终可执行文件名为 `VSCopilotSwitch`，提供 `/health`、`/api/tags`、`/api/chat` 和内部 VS Code 配置 API。
- 新增 VSCopilotSwitch.Ui Vue 3 + Vite SPA 工程骨架。
- 新增 `VSCopilotSwitch.Ui.esproj`，并按 Visual Studio SPA 模式加入解决方案构建和部署。
- ✅️ 将前端切换为 VueApp2 同款 Vue 3 + TypeScript + Vite + ESLint + Oxlint 技术栈，并保留本项目 `/health`、`/api`、`/internal` 后端代理关系。
- ✅️ 将默认 Vue 欢迎页替换为 VSCopilotSwitch 首版管理首页，展示代理状态、模型列表和 VS Code 配置 dry-run 预览入口。
- ✅️ 按 `cc switch` 截图重设计 Vue 管理界面，新增浅色供应商卡片列表、顶部快速切换区、添加/编辑供应商表单和 VS Code 配置预览入口。

- 建立项目 README，明确 VSCopilotSwitch 的目标和核心能力。
- 新增路线图，覆盖项目基线、VS Code 配置管理、Ollama 兼容代理、Provider Adapter、稳定性、OmniHost 四端界面和发布安全。
- 新增智能体协作要求，定义安全、架构、Provider、VS Code 配置、UI、测试和文档维护规则。
- 明确首批计划支持的模型供应商：sub2api、OpenAI Official、Claude Official、DeepSeek、NVIDIA NIM / build.nvidia.com、Moark。
- 补充 Vue 3 SPA 作为界面技术方向，并要求通过项目内 npm 脚本调试和构建。
- 补充 AOT 单体应用发布目标，要求将 SPA 构建产物作为嵌入式资源打包。
- 补充系统托盘能力规划：打开主界面、显示和选择当前提供商、显示代理状态、退出程序。

### Changed

- ✅️ 统一 `ROADMAP.md` 任务状态标记：使用 `✅️`、`🔧`、`⬜`、`🔴 现在做`、`🟡 可并行`、`🔵 后做` 六类状态，并明确当前主线。
- ✅️ 校准路线图诚实状态：新增阶段 5.5 真实功能闭环，把 UI 保存供应商尚未接入 Ollama 代理路由标为当前主线，避免将骨架或占位能力误标为完成。
- ✅️ 桌面宿主标题栏改为系统原生标题栏，默认跟随操作系统窗口风格和深浅色偏好；页面配色同步支持系统主题。
- ✅️ 开发启动配置关闭自动浏览器打开，避免 OmniHost 原生窗口和系统浏览器重复打开同一个管理界面。
- ✅️ 将 VS Code 配置向导从供应商编辑页迁移到独立设置页面，写入向导放入“常规”选项卡，配置回滚放入“备份”选项卡。

- 将宿主项目从 src/VSCopilotSwitch.Host 改名为 src/VSCopilotSwitch，只调整项目名称和输出文件名，命名空间与代码职责保持不变。
- 完善 Vue 3 SPA 管理界面，加入供应商切换、代理状态、模型列表、VS Code 配置 dry-run 和托盘菜单规划。
- ✅️ 将首页从 VS Code Workbench 深色布局调整为更接近 `cc switch` 的轻量卡片式布局，降低供应商启用、编辑和查询状态的识别成本。
- 将宿主项目切换为 Web SDK，增加 `SpaRoot`、`SpaProxyLaunchCommand`、`SpaProxyServerUrl` 和前端项目引用，支持后端启动时联动 Vue 调试服务。
- ✅️ 修复开发模式根路径 404：在 launch profile 和 Development 配置中启用 `Microsoft.AspNetCore.SpaProxy` Hosting Startup，并补充宿主 `wwwroot` 约定目录。
- ✅️ 将开发模式 SpaProxy 和 Vite 调试服务统一切换为 HTTP，不再生成或使用本地 HTTPS 开发证书。
- ✅️ 清理前端 JSON 配置文件的 UTF-8 BOM，修复 Vite 加载 PostCSS 配置时报 `Unexpected token` 的问题。
- ✅️ 接入 VS Code Workbench 风格主题层，使用 `--vscode-*` 语义变量组织标题栏、活动栏、侧边栏、编辑器区域、状态栏和表单控件，便于后续对接 OmniHost 注入主题。
- 按 AGENTS 工作原则中的跨平台差异和 UI 防误操作要求重新整理路线图。
- 将原始 README 中的项目方向整理为 UTF-8 中文文档，并补充跨平台和安全要求。

### Fixed

- ✅️ 修复 VS Code 配置目录发现误返回 `...\Code` 产品根目录的问题；现在会返回 `...\Code\User`，并兼容旧界面传入根目录时自动规范化到 User 子目录，避免生成差异预览误读上一级残留 `chatLanguageModels.json`。
- ✅️ 修复右上角新增供应商入口：新增模式会清空标题和表单内容，编辑已有供应商时才带入原数据。
- ✅️ 修复首页供应商链接打开方式：桌面壳内通过系统默认浏览器打开外部链接，避免在 WebView 内跳走。
- ✅️ 新增首页供应商卡片上下拖拽排序，拖动卡片可调整供应商列表顺序。
- ✅️ 首页供应商列表和添加/编辑供应商表单改为对接宿主供应商配置 API，不再只使用前端内置假数据。
- ✅️ 修复供应商编辑页在窗口高度不足时无法向下滚动的问题，底部配置向导、回滚和保存操作现在可完整访问。
- ✅️ 修复首页供应商列表高度策略：列表会按供应商数量撑开，最多显示 5 个供应商，超过后在列表内部滚动。
- ✅️ 修复宿主开发启动时 `OllamaProxyService` 多构造函数导致依赖注入无法选择构造函数的问题，启动时会显式注入全部已注册 Provider。
- ✅️ 修复 Ollama 代理仍只使用启动时 `appsettings` Provider 的问题；未配置真实供应商或未保存 API Key 时才回退到内置占位 Provider。
- ✅️ 修复模型列表仍来自表单静态模型的问题：运行时 Provider 不再注入静态模型清单，`/api/tags` 会向当前启用供应商实时获取模型列表。

### Security

- ✅️ sub2api 上游错误消息会在返回给 Ollama 代理前脱敏当前 API Key，避免鉴权失败详情泄露密钥原文。
- ✅️ OpenAI Official 上游错误消息会在返回给 Ollama 代理前脱敏当前 API Key，避免鉴权失败详情泄露密钥原文。
- ✅️ DeepSeek 上游错误消息会在返回给 Ollama 代理前脱敏当前 API Key，避免鉴权失败详情泄露密钥原文。
- ✅️ NVIDIA NIM、MoArk 和 Claude Official 上游错误消息会在返回给 Ollama 代理前脱敏当前 API Key，避免鉴权失败详情泄露密钥原文。
- 明确 API Key 脱敏、本地代理默认监听 `127.0.0.1`、VS Code 配置写入前备份和回滚等安全基线。
