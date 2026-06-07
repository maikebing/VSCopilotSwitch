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

提示词：

```text
你在仓库 /opt/data/VSCopilotSwitch，当前分支必须是 feature/mewui-native-shell。请只实现原型 2：阶段 6 健康状态解释入口。

必须遵守 AGENTS.md：保护用户配置，不绕过 /internal API，不泄露密钥，注释用中文且只在必要处写；完成后更新 ROADMAP.md 和 CHANGELOG.md。

背景：原型 1 已把 MewUI 概览页升级为“当前生效链路控制台”。ROADMAP.md 阶段 5.7 当前剩余主线是衔接阶段 6：健康检查、重试、熔断、备用供应商/备用模型和 UI 状态解释必须进入真实路由链路。当前原型不要实现完整熔断/备用路由，只先做可用的健康状态解释入口。

实现范围：
- 主要修改 src/VSCopilotSwitch/NativeUi/NativeWorkbench.Overview.cs、NativeWorkbench.cs，必要时新增 NativeUi partial 文件。
- 在概览页新增或改造“路由健康解释”区块，基于已有真实数据解释当前链路状态：
  - /health 是否可达、运行模式、监听地址。
  - 当前供应商是否真实可路由：是否启用、是否有 API Key、是否有模型名。
  - /api/tags 模型列表是否为空；为空时解释可能原因和下一步（测试连接、检查 API Key、刷新模型）。
  - 最近请求是否成功；失败时按 HTTP 状态给出用户可执行建议：401/403 密钥或权限，404 模型/路径，429 限流，5xx/502/503/504 上游或网络，其他状态提示查看分析页。
  - Copilot 探针最近一次运行结果可在概览页显示摘要；没有运行时显示“尚未运行”。
- 增加“运行健康探针”按钮，复用现有 /internal/copilot/probe，不新增新后端 API；运行后刷新概览显示探针步骤摘要。
- 不新增直接修改用户 VS Code 配置的写入路径；不实现完整熔断、重试配置或备用路由切换，只做解释和入口。
- 保持 Native AOT 友好，不引入反射型 JSON 或新 npm/前端依赖。
- 如果要保存 UI 内存状态，可在 NativeWorkbench 字段中保存最近一次 CopilotCompatibilityProbeResult，不落盘。

验证：
- 使用 /opt/data/home/.dotnet10/dotnet build src/VSCopilotSwitch/VSCopilotSwitch.csproj -m:1 /p:RestoreUseStaticGraphEvaluation=false
- 运行：
  /opt/data/home/.dotnet10/dotnet run --project tests/VSCopilotSwitch.Core.Tests/VSCopilotSwitch.Core.Tests.csproj --no-restore
  /opt/data/home/.dotnet10/dotnet run --project tests/VSCopilotSwitch.VsCodeConfig.Tests/VSCopilotSwitch.VsCodeConfig.Tests.csproj --no-restore
- 可尝试 Services 测试，但 Linux 下 Windows ProtectedData 相关用例可能因平台不支持失败；如失败需如实说明。

交付：
- 提交前不要 git commit。
- 最后输出：改了哪些文件、健康解释覆盖了哪些状态、验证命令和真实结果、后续原型 3/4 未做。
```

## 原型 3：供应商预设与导入

目标：增加常见 OpenAI-compatible / 官方 Provider 预设模板和安全导入预览，不直接导入密钥。

提示词：

```text
你在仓库 /opt/data/VSCopilotSwitch，当前分支必须是 feature/mewui-native-shell。请只实现原型 3：供应商预设与导入。

必须遵守 AGENTS.md：保护用户配置，不绕过 /internal API，不泄露密钥，注释用中文且只在必要处写；完成后更新 ROADMAP.md 和 CHANGELOG.md。

背景：原型 1/2 已完成 MewUI 当前生效链路控制台和健康状态解释入口。ROADMAP.md 阶段 5.7 当前“现在做”是供应商预设与导入：常见 OpenAI-compatible、中转站、官方 Provider 的 Base URL、协议类型、模型推荐和能力声明模板。

实现范围：
- 主要修改 src/VSCopilotSwitch/NativeUi/NativeWorkbench.Providers.cs、NativeWorkbenchModels.cs，必要时新增 NativeUi partial 文件。
- 在供应商页新增“预设与导入”区块：
  - 提供常见预设：OpenAI Official、Claude Official、DeepSeek、NVIDIA NIM、MoArk、sub2api、OpenAI-compatible 中转站模板。
  - 每个预设展示名称、协议类型、Base URL、推荐模型、能力声明摘要（文本、工具、视觉、长上下文等口径即可，不要求后端能力矩阵新增字段）。
  - 点击预设只填充供应商编辑表单和显示预览，不自动保存、不自动启用、不写入 VS Code 配置。
- 增加安全导入预览：
  - 支持用户在 UI 输入/粘贴 JSON 文本，解析 provider 配置导出结构或简单数组/对象。
  - 只预览可导入项：名称、协议类型、Base URL、模型、是否声明存在密钥；不得显示、保存或自动导入密钥原文。
  - “应用导入项”只把选中/第一项填入编辑表单，API Key 字段保持空，并在状态中提示需要用户手动填写密钥后再保存。
  - JSON 无效或字段缺失时给出明确错误，不崩溃。
- 不新增后端 API，除非现有 API 无法满足；优先保持 UI 内存态和已有保存供应商接口。
- 不改变运行时路由、不改变 VS Code 配置写入、不实现批量保存；这次只做预设选择、导入预览、填表。
- 保持 Native AOT 友好：如果新增 JSON 反序列化类型，必须加入 MewUiJsonContext 源生成上下文，避免反射序列化。

验证：
- 使用 /opt/data/home/.dotnet10/dotnet build src/VSCopilotSwitch/VSCopilotSwitch.csproj -m:1 /p:RestoreUseStaticGraphEvaluation=false
- 运行：
  /opt/data/home/.dotnet10/dotnet run --project tests/VSCopilotSwitch.Core.Tests/VSCopilotSwitch.Core.Tests.csproj --no-restore
  /opt/data/home/.dotnet10/dotnet run --project tests/VSCopilotSwitch.VsCodeConfig.Tests/VSCopilotSwitch.VsCodeConfig.Tests.csproj --no-restore
- 可尝试 Services 测试，但 Linux 下 Windows ProtectedData 相关用例可能因平台不支持失败；如失败需如实说明。

交付：
- 提交前不要 git commit。
- 最后输出：改了哪些文件、预设和导入覆盖了哪些场景、验证命令和真实结果、后续原型 4 未做。
```

## 原型 4：模型测试比较

目标：保存模型探针结果，用于快速切换时比较延迟、首 token、工具调用和费用估算。

提示词待前置健康状态与分析数据结构稳定后再展开。
