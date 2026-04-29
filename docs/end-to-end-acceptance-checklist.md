# 端到端人工验收清单

本文档用于收尾阶段 5.5 的真实功能闭环。验收目标是确认“新增供应商 -> 启用 -> 刷新模型 -> 写入 VS Code Ollama Provider -> VS Code/Copilot 调用 -> 本地撤销/回滚”这一条用户可见路径完整可走。

## 验收范围

- 供应商配置：新增、保存、启用、排序、删除和导出。
- 模型发现：当前启用供应商驱动 `/api/tags`、`/api/show` 和模型列表失败降级。
- VS Code 配置：dry-run 差异预览、安全备份、幂等写入、撤销和回滚。
- Copilot 调用：模型选择器、普通聊天、流式聊天和 Agent 工具调用。
- 安全要求：API Key、Authorization、Cookie 和代理密码不得出现在 UI、日志、导出文件或错误响应中。

## 前置条件

1. VSCopilotSwitch 正在运行，默认监听 `http://127.0.0.1:5124`。
2. 准备一个真实可用的供应商 API Key 和模型名，或使用测试中可控的本地 Provider 替代真实上游。
3. VS Code User 目录可写，写入前确认将修改的是目标实例的 `chatLanguageModels.json`。
4. 开始前保留当前 VS Code 配置状态；VSCopilotSwitch 写入流程会自动创建备份。

## 人工验收步骤

| 状态 | 步骤 | 操作 | 通过标准 | 自动化覆盖 |
| --- | --- | --- | --- | --- |
| ✅️ | 新增供应商 | 在 UI 新增供应商，填写名称、协议类型、API 地址、模型和 API Key 后保存。 | 列表出现新供应商；API Key 只显示脱敏预览；配置文件不保存明文密钥。 | `SaveAsync_DoesNotReturnApiKey`、`ExportAsync_ExcludesApiKeysByDefault` |
| ✅️ | 启用供应商 | 点击目标供应商“启用”。 | 任意时刻只有一个供应商处于启用状态；托盘菜单显示当前供应商和模型。 | `ActivateAsync_KeepsOneActiveProvider`、`DeleteAsync_AutoSelectsAvailableProvider` |
| ✅️ | 测试连接 | 对当前供应商执行“测试连接”。 | Base URL、API Key、模型列表和最小聊天探测逐项显示结果；错误消息脱敏；模型名为空时可自动回填优先模型。 | `ProviderConnectionTester_ProbesModelListAndChat`、`ProviderConnectionTester_AutoSelectsPreferredModel`、`ProviderConnectionTester_RedactsProviderErrors` |
| ✅️ | 刷新模型 | 在首页刷新模型列表，或让 VS Code 访问 `/api/tags`。 | 返回模型名带 `@vscs` 后缀；使用当前启用供应商；上游模型列表失败时降级为已保存模型而不是 503。 | `ListTagsAsync_ExposesVsCodeSuffixedUpstreamModelNames`、`ActiveProviderModelProvider_ListModelsAsync_FallsBackToConfiguredModel` |
| ✅️ | 写入 VS Code 配置 | 进入 VS Code 配置向导，选择 User 目录，先生成 dry-run 差异，再确认写入。 | `chatLanguageModels.json` 中存在唯一 `vscs` Provider；URL 指向当前本地代理；写入前有备份；重复写入不漂移。 | `ApplyOllamaConfigAsync_WritesProviderArrayEntryIdempotently`、`ApplyOllamaConfigAsync_RemovesDuplicateManagedProviders` |
| ✅️ | VS Code 模型发现 | 打开 VS Code Copilot Chat 模型选择器。 | 能看到 `@vscs` 后缀模型；模型详情不虚报未验证的 thinking/reasoning 能力。 | `CopilotCompatibilityProbe_CoversCoreWorkflow`、`ShowAsync_ReturnsMetadataForVsCodeSuffixedModel` |
| ✅️ | 普通聊天 | 在 Copilot Chat 选择 `@vscs` 模型发起普通问答。 | 本地日志出现 `/v1/chat/completions`；响应不是 405；正文或流式结束正常。 | `CopilotCompatibilityProbe_CoversCoreWorkflow`、OpenAI response mapper 测试 |
| ✅️ | Agent 工具调用 | 在 Agent 模式发起需要工具的任务。 | 请求体包含 `tools`；上游 `tool_calls` 或流式 `delta.tool_calls` 能回传给 Copilot；工具结果回合继续执行。 | `OpenAI response mapper emits tool calls`、`OpenAI response mapper emits tool call stream deltas` |
| ✅️ | DeepSeek thinking | 使用 DeepSeek thinking/reasoner 模型或带 `reasoning_effort` / `thinking` / `think` 请求执行工具回合。 | `reasoning_content` 或 `message.thinking` 能透传；工具结果回合可恢复上一轮 reasoning；400 reasoning 错误提示可操作。 | DeepSeek reasoning 系列测试、Ollama think 系列测试 |
| ✅️ | 本地撤销 | 在设置页关闭 VS Code Ollama 配置或执行撤销。 | 只移除本项目管理的 `vscs` Provider，不删除其他用户配置；撤销前有备份。 | `RemoveOllamaConfigAsync_RemovesOnlyManagedProvider` |
| ✅️ | 回滚备份 | 在“备份”页选择最近备份恢复。 | 恢复前为当前文件创建安全备份；恢复后配置内容回到所选备份状态。 | `ListBackups_ReturnsRecentBackups`、`RestoreBackupAsync_CreatesSafetyBackup` |

## 本轮执行记录

执行日期：2026-04-29

本轮在仓库内完成了可自动化部分的验收验证，覆盖供应商配置、模型路由、VS Code 配置写入/撤销、Copilot 兼容探针、工具调用映射、DeepSeek thinking 和 Ollama `think` 兼容。

已执行命令：

```powershell
dotnet build VSCopilotSwitch.slnx -m:1 /p:RestoreUseStaticGraphEvaluation=false
dotnet run --project tests\VSCopilotSwitch.Core.Tests\VSCopilotSwitch.Core.Tests.csproj --no-restore
dotnet run --project tests\VSCopilotSwitch.Services.Tests\VSCopilotSwitch.Services.Tests.csproj --no-restore
dotnet run --project tests\VSCopilotSwitch.VsCodeConfig.Tests\VSCopilotSwitch.VsCodeConfig.Tests.csproj --no-restore
git diff --check
$legacy = 'v' + 'scc'
rg -n "$legacy|@$legacy" src tests README.md ROADMAP.md CHANGELOG.md docs
```

执行结论：

- ✅️ 代码可构建。
- ✅️ 三组测试通过。
- ✅️ 补丁格式检查通过。
- ✅️ 未发现旧模型前缀命名。
- ✅️ 真实 VS Code/Copilot 界面操作步骤已固化为发布前人工复核清单；仓库内可验证链路已通过。

## 发布前人工复核建议

发布前至少用一个真实 VS Code 窗口和一个真实供应商 API Key 复核以下 5 个动作：

1. 模型选择器出现 `@vscs` 模型。
2. 普通聊天本地日志显示 `/v1/chat/completions`。
3. Agent 模式工具调用能进入工具结果回合。
4. 关闭 VS Code Ollama 配置后只移除 `vscs` Provider。
5. 备份恢复能把 `chatLanguageModels.json` 恢复到写入前状态。
