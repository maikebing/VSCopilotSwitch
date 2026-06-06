# MewUI 界面迁移说明

本文记录将 VSCopilotSwitch 管理界面迁移到 [aprillz/MewUI](https://github.com/aprillz/MewUI) 的边界、阶段和当前主入口状态。

## 背景

MewUI 是 code-first 的 .NET GUI 框架，不是 Vue 组件库。它适合 Native AOT、小体积桌面界面和 C# fluent markup，但当前仍是 experimental prototype，API 可能变化。因此写入型工作流仍需要沿用现有 `/internal` API、dry-run、备份、差异预览和二次确认边界逐步迁移。

## 当前原生入口

MewUI 原生入口已经并入主项目 `src/VSCopilotSwitch`：

- 依赖 `Aprillz.MewUI.Windows` `0.15.2`。
- 进程内启动 ASP.NET Core 本地代理 API，默认监听 `http://127.0.0.1:5124/`。
- MewUI 窗口直接读取同进程本地 API，不需要先启动 Vue、OmniHost、SpaProxy 或 npm 调试服务。
- 当前窗口只调用 `/health`、`/internal/providers`、`/api/tags` 和 `/internal/vscode/user-directories` 展示状态。
- 本地代理已在同一程序内提供 Ollama / OpenAI-compatible / `/internal` API。
- 当前窗口仍不保存供应商、不写入 VS Code 配置、不导出密钥；写入流程继续复用现有安全服务后续迁移。
- Win32 托盘已经接入主项目：关闭窗口会隐藏到托盘，托盘可打开或聚焦主界面、查看当前供应商和模型、快速切换真实供应商并退出程序。
- Release `win-x64` 发布通过 Native AOT 生成单个 `VSCopilotSwitch.exe`，不再需要构建或嵌入 Vue SPA。

运行 MewUI 原生入口：

```powershell
dotnet run --project src\VSCopilotSwitch
```

也可使用仓库脚本：

```powershell
npm run mewui:dev
```

这里的 `npm run mewui:dev` 只是兼容旧命令名的工作区脚本包装，实际执行的是 `dotnet run --project src/VSCopilotSwitch`；MewUI 本身不依赖 npm。

## 迁移原则

- 主应用默认使用 MewUI 原生窗口；Vue / OmniHost 旧链路不再参与默认构建和发布。
- MewUI 写入 VS Code 配置前必须复用现有 dry-run、备份、差异预览和二次确认流程。
- MewUI 供应商编辑不得显示或回传 API Key 原文，只能使用脱敏预览和受保护本地存储。
- MewUI 与后端交互优先复用现有 `/internal` API，不把 Provider 私有协议写进 UI 层。
- MewUI 发布前必须验证 Windows AOT、托盘行为、窗口生命周期和本地服务启动，不让正式包依赖 npm 或前端开发服务器。

## 后续阶段

1. ✅️ 单进程原生入口：MewUI 启动时同步启动本地代理 API，并展示代理状态、当前供应商、模型列表、VS Code 配置目录。
2. 供应商管理：新增、编辑、测试连接、启用、删除、排序，全部复用现有 API 和脱敏规则。
3. VS Code 配置向导：目录选择、dry-run 差异、二次确认写入、备份列表、回滚。
4. 分析统计与 VS2026 面板：迁移请求日志、费用统计、BYOM 填写信息复制。
5. ✅️ 宿主接管：MewUI 已作为默认窗口入口，同时保留必要的本地 HTTP API 和 Ollama / OpenAI-compatible 代理端点。
6. ✅️ 单体 AOT 发布：主项目可发布为唯一 `VSCopilotSwitch.exe`，发布链路不依赖 npm、Node.js、Vue 或 WebView2。
