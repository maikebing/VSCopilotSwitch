# Changelog

本项目遵循“面向用户可见变化”的变更日志记录方式。版本号在进入可运行版本后再正式启用。

## Unreleased

### Added

- 建立项目 README，明确 VSCopilotSwitch 的目标和核心能力。
- 新增路线图，覆盖项目基线、VS Code 配置管理、Ollama 兼容代理、Provider Adapter、稳定性、OmniHost 四端界面和发布安全。
- 新增智能体协作要求，定义安全、架构、Provider、VS Code 配置、UI、测试和文档维护规则。
- 明确首批计划支持的模型供应商：sub2api、OpenAI Official、Claude Official、DeepSeek、NVIDIA NIM / build.nvidia.com、Moark。
- 补充 Vue 3 SPA 作为界面技术方向，并要求通过项目内 npm 脚本调试和构建。
- 补充 AOT 单体应用发布目标，要求将 SPA 构建产物作为嵌入式资源打包。
- 补充系统托盘能力规划：打开主界面、显示和选择当前提供商、显示代理状态、退出程序。

### Changed

- 将原始 README 中的项目方向整理为 UTF-8 中文文档，并补充跨平台和安全要求。

### Security

- 明确 API Key 脱敏、本地代理默认监听 `127.0.0.1`、VS Code 配置写入前备份和回滚等安全基线。


