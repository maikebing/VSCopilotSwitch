# Changelog

本项目遵循“面向用户可见变化”的变更日志记录方式。版本号在进入可运行版本后再正式启用。

## Unreleased

### Added

- ✅️ 接入 OmniHost Windows 原生宿主：`src/VSCopilotSwitch` 直接引用 `OmniHost`、`OmniHost.Windows`、`OmniHost.WebView2`，启动本地 ASP.NET Core 服务后由 Win32 + WebView2 窗口承载 Vue 管理界面。

- 初始化 .NET 解决方案和 OmniHost-ready 宿主骨架。
- 新增 VSCopilotSwitch.Core，包含 Ollama 协议模型、Provider 抽象和内置占位 Provider。
- 新增 VSCopilotSwitch.VsCodeConfig，支持 VS Code User 目录发现、settings.json / chatLanguageModels.json 干运行预览、安全写入和备份。
- 新增 `src/VSCopilotSwitch` 宿主项目，最终可执行文件名为 `VSCopilotSwitch`，提供 `/health`、`/api/tags`、`/api/chat` 和内部 VS Code 配置 API。
- 新增 VSCopilotSwitch.Ui Vue 3 + Vite SPA 工程骨架。
- 新增 `VSCopilotSwitch.Ui.esproj`，并按 Visual Studio SPA 模式加入解决方案构建和部署。
- ✅️ 将前端切换为 VueApp2 同款 Vue 3 + TypeScript + Vite + ESLint + Oxlint 技术栈，并保留本项目 `/health`、`/api`、`/internal` 后端代理关系。
- ✅️ 将默认 Vue 欢迎页替换为 VSCopilotSwitch 首版管理首页，展示代理状态、模型列表和 VS Code 配置 dry-run 预览入口。

- 建立项目 README，明确 VSCopilotSwitch 的目标和核心能力。
- 新增路线图，覆盖项目基线、VS Code 配置管理、Ollama 兼容代理、Provider Adapter、稳定性、OmniHost 四端界面和发布安全。
- 新增智能体协作要求，定义安全、架构、Provider、VS Code 配置、UI、测试和文档维护规则。
- 明确首批计划支持的模型供应商：sub2api、OpenAI Official、Claude Official、DeepSeek、NVIDIA NIM / build.nvidia.com、Moark。
- 补充 Vue 3 SPA 作为界面技术方向，并要求通过项目内 npm 脚本调试和构建。
- 补充 AOT 单体应用发布目标，要求将 SPA 构建产物作为嵌入式资源打包。
- 补充系统托盘能力规划：打开主界面、显示和选择当前提供商、显示代理状态、退出程序。

### Changed

- ✅️ 开发启动配置关闭自动浏览器打开，避免 OmniHost 原生窗口和系统浏览器重复打开同一个管理界面。

- 将宿主项目从 src/VSCopilotSwitch.Host 改名为 src/VSCopilotSwitch，只调整项目名称和输出文件名，命名空间与代码职责保持不变。
- 完善 Vue 3 SPA 管理界面，加入供应商切换、代理状态、模型列表、VS Code 配置 dry-run 和托盘菜单规划。
- 将宿主项目切换为 Web SDK，增加 `SpaRoot`、`SpaProxyLaunchCommand`、`SpaProxyServerUrl` 和前端项目引用，支持后端启动时联动 Vue 调试服务。
- ✅️ 修复开发模式根路径 404：在 launch profile 和 Development 配置中启用 `Microsoft.AspNetCore.SpaProxy` Hosting Startup，并补充宿主 `wwwroot` 约定目录。
- ✅️ 将开发模式 SpaProxy 和 Vite 调试服务统一切换为 HTTP，不再生成或使用本地 HTTPS 开发证书。
- ✅️ 清理前端 JSON 配置文件的 UTF-8 BOM，修复 Vite 加载 PostCSS 配置时报 `Unexpected token` 的问题。
- ✅️ 接入 VS Code Workbench 风格主题层，使用 `--vscode-*` 语义变量组织标题栏、活动栏、侧边栏、编辑器区域、状态栏和表单控件，便于后续对接 OmniHost 注入主题。
- 按 AGENTS 工作原则中的跨平台差异和 UI 防误操作要求重新整理路线图。


- 将原始 README 中的项目方向整理为 UTF-8 中文文档，并补充跨平台和安全要求。

### Security

- 明确 API Key 脱敏、本地代理默认监听 `127.0.0.1`、VS Code 配置写入前备份和回滚等安全基线。












