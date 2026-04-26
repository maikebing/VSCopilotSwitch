# Changelog

本项目遵循“面向用户可见变化”的变更日志记录方式。版本号在进入可运行版本后再正式启用。

## Unreleased

### Added

- ✅️ 阶段 5 启动首个真实 Provider Adapter：新增 sub2api 中转站协议接入，支持 `/v1/models` 模型列表获取、OpenAI Chat Completions 非流式请求转换和 SSE 流式响应转换。
- ✅️ 宿主支持通过 `Providers:Sub2Api` 配置启用 sub2api；未配置 `BaseUrl` / `ApiKey` 时继续使用内置占位 Provider，便于本地开发安全启动。
- ✅️ 新增 sub2api 最小集成测试，覆盖模型列表、上游模型名转换、流式分块解析和 HTTP 错误映射。
- ✅️ 收尾阶段 0 和阶段 1：明确当前阶段只实现 Windows 桌面端，macOS、Linux、WSL 顺延到后续跨平台阶段。
- ✅️ 新增 VS Code Ollama 配置写入向导骨架，要求先选择目录并生成 dry-run 差异预览，再允许确认写入和查看备份结果。
- ✅️ 配置预览结果新增字段级差异，展示 `settings.json` 和 `chatLanguageModels.json` 中本项目托管字段的原值与新值。
- ✅️ 新增 VS Code 配置回滚入口，可列出最近 VSCopilotSwitch 备份并恢复指定备份；恢复前会为当前文件再创建安全备份。
- ✅️ 为 VS Code 配置写入和备份恢复加入二次确认提示，降低误触修改用户配置的风险。
- ✅️ 新增失败修复建议面板，针对权限不足、JSON 无效、文件占用和端口冲突给出可执行处理步骤。
- ✅️ 新增默认折叠的高级选项面板，集中放置代理地址、熔断阈值、重试次数和备用路由。
- ✅️ 新增 VS Code 配置最小测试项目，覆盖配置写入幂等、备份列表和恢复前安全备份。
- ✅️ 新增本地端口占用检测 API 与高级选项中的端口检测提示。
- ✅️ 新增 Windows 托盘菜单最小增强，支持打开或聚焦主界面、查看当前提供商和代理状态、退出并停止本地代理。
- ✅️ 重新设计 VSCopilotSwitch 专属 SVG logo，并替换首页左上角 `CC Switch` 品牌展示。
- ✅️ 首页右上角快速入口从 `Claude` / `Codex` 调整为 `VS2026` / `VSCode`，并替换为对应 IDE 风格图标。
- ✅️ 接入 OmniHost Windows 原生宿主：`src/VSCopilotSwitch` 直接引用 `OmniHost`、`OmniHost.Windows`、`OmniHost.WebView2`，启动本地 ASP.NET Core 服务后由 Win32 + WebView2 窗口承载 Vue 管理界面。
- ✅️ 将 OmniHost 直接依赖和传递依赖加入 `VSCopilotSwitch.slnx`，修复 Visual Studio 生成主项目时未先生成 `OmniHost.Abstractions` / `OmniHost.Core` 等外部项目导致的 `CS0006`。
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
- ✅️ 桌面宿主标题栏改为系统原生标题栏，默认跟随操作系统窗口风格和深浅色偏好；页面配色同步支持系统主题。
- ✅️ 开发启动配置关闭自动浏览器打开，避免 OmniHost 原生窗口和系统浏览器重复打开同一个管理界面。

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

### Security

- ✅️ sub2api 上游错误消息会在返回给 Ollama 代理前脱敏当前 API Key，避免鉴权失败详情泄露密钥原文。
- 明确 API Key 脱敏、本地代理默认监听 `127.0.0.1`、VS Code 配置写入前备份和回滚等安全基线。
