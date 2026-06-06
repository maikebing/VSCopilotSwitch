# MewUI 界面迁移说明

本文记录将 VSCopilotSwitch 管理界面迁移到 [aprillz/MewUI](https://github.com/aprillz/MewUI) 的边界、阶段和当前主入口状态。

## 背景

MewUI 是 code-first 的 .NET GUI 框架，不是 Vue 组件库。它适合 Native AOT、小体积桌面界面和 C# fluent markup，但当前仍是 experimental prototype，API 可能变化。因此原生工作台即使承接写入型工作流，也必须沿用现有 `/internal` API、dry-run、备份、差异预览和二次确认边界。

## 当前原生入口

MewUI 原生入口已经并入主项目 `src/VSCopilotSwitch`：

- 依赖 `Aprillz.MewUI.Windows` `0.15.2`。
- 进程内启动 ASP.NET Core 本地代理 API，默认监听 `http://127.0.0.1:5124/`。
- MewUI 窗口直接读取同进程本地 API，不需要先启动 Vue、OmniHost、SpaProxy 或 npm 调试服务。
- 原生工作台包含概览、供应商、VS Code、分析和 VS2026 选项卡，状态读取和写入动作均通过同进程 `/internal` API 完成。
- 本地代理已在同一程序内提供 Ollama / OpenAI-compatible / `/internal` API。
- 供应商页支持新增、编辑、测试连接、启用、删除和排序；API Key 使用密码输入框，只提交给后端加密保存，列表只显示脱敏预览。
- VS Code 页支持目录选择、dry-run 写入/撤销预览、确认写入/撤销、备份列表和二次确认回滚，继续复用配置服务的备份和失败保护。
- 分析页支持请求统计、日志清空和 Copilot 探针；VS2026 页支持 BYOM 填写信息刷新和复制。
- Win32 托盘已经接入主项目：关闭窗口会隐藏到托盘，托盘可打开或聚焦主界面、查看当前供应商和模型、快速切换真实供应商并退出程序。
- Release `win-x64` 发布通过 Native AOT 生成单个 `VSCopilotSwitch.exe`，不再需要构建或嵌入 Vue SPA；旧 Vue 项目、OmniHost submodule 和 npm 工作区脚本已经清理。

运行 MewUI 原生入口：

```powershell
dotnet run --project src\VSCopilotSwitch
```

## 迁移原则

- 主应用默认使用 MewUI 原生窗口；Vue / OmniHost 旧链路已移除，不再参与默认构建、开发和发布。
- MewUI 写入 VS Code 配置前必须复用现有 dry-run、备份、差异预览和二次确认流程。
- MewUI 供应商编辑不得显示或回传 API Key 原文，只能使用脱敏预览和受保护本地存储。
- MewUI 与后端交互优先复用现有 `/internal` API，不把 Provider 私有协议写进 UI 层。
- MewUI 发布前必须验证 Windows AOT、托盘行为、窗口生命周期和本地服务启动，不让正式包依赖 npm 或前端开发服务器。

## 阶段状态

1. ✅️ 单进程原生入口：MewUI 启动时同步启动本地代理 API，并展示代理状态、当前供应商、模型列表、VS Code 配置目录。
2. ✅️ 供应商管理：新增、编辑、测试连接、启用、删除、排序，全部复用现有 API 和脱敏规则。
3. ✅️ VS Code 配置向导：目录选择、dry-run 差异、二次确认写入、备份列表、回滚。
4. ✅️ 分析统计与 VS2026 面板：迁移请求日志、费用统计、Copilot 探针、BYOM 填写信息复制。
5. ✅️ 宿主接管：MewUI 已作为默认窗口入口，同时保留必要的本地 HTTP API 和 Ollama / OpenAI-compatible 代理端点。
6. ✅️ 单体 AOT 发布：主项目可发布为唯一 `VSCopilotSwitch.exe`，发布链路不依赖 npm、Node.js、Vue 或 WebView2。
