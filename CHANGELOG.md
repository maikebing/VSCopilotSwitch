# Changelog

本项目遵循“面向用户可见变化”的变更日志记录方式。版本号在进入可运行版本后再正式启用。

## Unreleased

### Added

- 初始化 .NET 解决方案和 OmniHost-ready 宿主骨架。
- 新增 VSCopilotSwitch.Core，包含 Ollama 协议模型、Provider 抽象和内置占位 Provider。
- 新增 VSCopilotSwitch.VsCodeConfig，支持 VS Code User 目录发现、settings.json / chatLanguageModels.json 干运行预览、安全写入和备份。
- 新增 `src/VSCopilotSwitch` 宿主项目，最终可执行文件名为 `VSCopilotSwitch`，提供 `/health`、`/api/tags`、`/api/chat` 和内部 VS Code 配置 API。
- 新增 VSCopilotSwitch.Ui Vue 3 + Vite SPA 工程骨架。

- 建立项目 README，明确 VSCopilotSwitch 的目标和核心能力。
- 新增路线图，覆盖项目基线、VS Code 配置管理、Ollama 兼容代理、Provider Adapter、稳定性、OmniHost 四端界面和发布安全。
- 新增智能体协作要求，定义安全、架构、Provider、VS Code 配置、UI、测试和文档维护规则。
- 明确首批计划支持的模型供应商：sub2api、OpenAI Official、Claude Official、DeepSeek、NVIDIA NIM / build.nvidia.com、Moark。
- 补充 Vue 3 SPA 作为界面技术方向，并要求通过项目内 npm 脚本调试和构建。
- 补充 AOT 单体应用发布目标，要求将 SPA 构建产物作为嵌入式资源打包。
- 补充系统托盘能力规划：打开主界面、显示和选择当前提供商、显示代理状态、退出程序。

### Changed

- 将宿主项目从 src/VSCopilotSwitch.Host 改名为 src/VSCopilotSwitch，只调整项目名称和输出文件名，命名空间与代码职责保持不变。
- 完善 Vue 3 SPA 管理界面，加入供应商切换、代理状态、模型列表、VS Code 配置 dry-run 和托盘菜单规划。
- 按 AGENTS 工作原则中的跨平台差异和 UI 防误操作要求重新整理路线图。


- 将原始 README 中的项目方向整理为 UTF-8 中文文档，并补充跨平台和安全要求。

### Security

- 明确 API Key 脱敏、本地代理默认监听 `127.0.0.1`、VS Code 配置写入前备份和回滚等安全基线。





