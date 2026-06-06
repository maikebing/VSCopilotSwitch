# Codex 执行提示词拆分

本文件用于监督 `feature/mewui-native-shell` 分支的 Codex 小步实现。每个提示词都应控制在一个可验证闭环内，避免单次会话上下文过大。

## 原型 1：MewUI 当前生效链路控制台

目标：落实 `ROADMAP.md` 阶段 5.7 的两个“现在做”：把概览页从信息列表升级为 cc switch 风格的快速切换控制台，并让供应商启用动作成为可解释的路由切换。

提示词：

```text
你在仓库 /opt/data/VSCopilotSwitch，当前分支必须是 feature/mewui-native-shell。请只实现原型 1：MewUI 当前生效链路控制台。

必须遵守 AGENTS.md：保护用户配置，不绕过 /internal API，不泄露密钥，注释用中文且只在必要处写；完成后更新 ROADMAP.md 和 CHANGELOG.md。

背景：ROADMAP.md 阶段 5.7 当前主线要求：
1. 重做 MewUI 首页为“当前生效链路控制台”：当前供应商、当前模型、公开模型名、代理健康、VS Code 配置状态、最近请求结果和关键操作在第一屏可见。
2. 将供应商启用动作升级为可解释的路由切换：切换后自动刷新模型、健康检查、VS Code 状态，并提示用户 Copilot 侧是否需要重新发现模型。

实现范围：
- 主要修改 src/VSCopilotSwitch/NativeUi/NativeWorkbench.cs 和必要的 NativeUi partial 文件。
- 概览页第一屏应新增/重组以下区块：
  - 当前链路摘要：供应商名称、供应商协议、上游模型、VS Code/Copilot 可见公开模型名（@vscs）、代理健康。
  - 路由切换动作：列出可用真实供应商，支持在概览页直接启用；不可用供应商要解释“缺少密钥或模型”。
  - VS Code 配置状态：展示已发现目录数量、当前选中目录、下一步动作（去 VS Code 页生成 dry-run / 写入）。不要直接写入配置。
  - 最近请求结果：从已有 analytics 快照提取最近 1 条请求的状态、模型、耗时、失败原因；没有请求时给出清晰空状态。
  - Copilot 重新发现提示：供应商或公开模型变化后提示用户在 VS Code Copilot 模型选择器刷新/重新选择模型。
- 激活供应商后必须刷新 providers、/api/tags、/health、VS Code 目录和 analytics，并在状态栏显示“已切换到 X，模型列表已刷新/刷新失败...”这类结果。
- 不新增直接修改用户 VS Code 配置的写入路径；只允许跳转/引导用户到现有 VS Code 页 dry-run 流程。
- 保持 Native AOT 友好，不引入反射型 JSON 或新前端/npm 依赖。
- 如果需要新小 helper，可以放在 NativeWorkbench.Ui.cs 或 NativeWorkbench.cs，但不要大规模重构无关代码。

验证：
- 运行 dotnet build src/VSCopilotSwitch/VSCopilotSwitch.csproj -m:1 /p:RestoreUseStaticGraphEvaluation=false
- 运行三组测试：
  dotnet run --project tests/VSCopilotSwitch.Core.Tests/VSCopilotSwitch.Core.Tests.csproj --no-restore
  dotnet run --project tests/VSCopilotSwitch.Services.Tests/VSCopilotSwitch.Services.Tests.csproj --no-restore
  dotnet run --project tests/VSCopilotSwitch.VsCodeConfig.Tests/VSCopilotSwitch.VsCodeConfig.Tests.csproj --no-restore

交付：
- 提交前不要 git commit。
- 最后输出：改了哪些文件、实现了哪些闭环、验证命令和真实结果、还有哪些后续原型未做。
```

## 原型 2：阶段 6 健康状态解释入口

目标：在不完整实现熔断/备用路由前，先把健康检查、失败解释和 UI 状态口径打通，为阶段 6 主线铺路。

提示词待原型 1 验证后再展开。

## 原型 3：供应商预设与导入

目标：增加常见 OpenAI-compatible / 官方 Provider 预设模板和安全导入预览，不直接导入密钥。

提示词待原型 1 验证后再展开。

## 原型 4：模型测试比较

目标：保存模型探针结果，用于快速切换时比较延迟、首 token、工具调用和费用估算。

提示词待前置健康状态与分析数据结构稳定后再展开。
