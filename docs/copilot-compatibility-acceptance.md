# Copilot 兼容验收清单

本文档记录阶段 5.6 的 Copilot Ollama Provider 最小验收路径。目标是确认 VS Code Copilot Chat 通过 `vscs` Provider 能完成模型发现、普通聊天和 Agent 工具调用闭环。

## 自动化探针

运行中的宿主提供内部探针：

- `POST /internal/copilot/probe`

探针会按顺序验证：

- 模型选择器：`/api/tags` 至少返回一个带 `@vscs` 后缀的模型。
- 模型元信息：`/api/show` 返回 `general.architecture`、`general.basename`，且不默认声明未验证的 thinking / reasoning。
- 普通聊天：非流式聊天返回 `done=true`。
- Agent 工具调用：仅当模型声明 `tools` 能力时，发送最小 function tool 定义并确认代理链路接受工具字段。
- 流式结束：流式聊天最终返回 `done=true` 分块。

自动化测试覆盖：

- `CopilotCompatibilityProbe_CoversCoreWorkflow`：覆盖探针主流程、普通聊天、工具字段回合和流式结束。
- `ActiveProviderModelProvider_ListModelsAsync_FallsBackToConfiguredModel`：覆盖模型列表失败时用当前已保存模型降级，避免模型选择器收到 503。
- Provider 错误脱敏测试：覆盖 OpenAI-compatible、OpenAI Official、DeepSeek、NVIDIA NIM、MoArk、Claude 的上游错误消息脱敏。

## 人工验收

1. 启动 VSCopilotSwitch，并确认监听地址为 `http://127.0.0.1:5124`。
2. 在供应商页保存并启用一个真实供应商，确认模型名不为空。
3. 打开 VS Code 配置向导，生成 dry-run 差异预览，确认 `chatLanguageModels.json` 中写入的 Provider 名称为 `vscs`，URL 指向当前本地代理地址。
4. 确认 VS Code Copilot Chat 模型选择器中出现 `@vscs` 后缀模型。
5. 在 Copilot Chat 发起普通问答，确认本地请求日志出现 `/v1/chat/completions`，响应不是 405。
6. 在 Agent 模式发起需要工具的任务，确认请求体包含 `tools`，并确认代理能把上游 `tool_calls` 或流式 `delta.tool_calls` 回传给 Copilot。
7. 临时让上游模型列表失败，确认 `/api/tags` 仍能用当前已保存模型返回可选清单，不向 Copilot 暴露 503。
8. 临时使用错误 API Key，确认界面和 HTTP 错误不会出现 API Key 原文，只能出现 `[已脱敏密钥]` 或不含密钥的公开错误。
9. 流式聊天时确认响应以 `data: [DONE]` 结束，并且最后一个 OpenAI chunk 带有非空 `finish_reason`。

## 当前限制

- thinking / reasoning 仍不默认声明；DeepSeek thinking 专用链路和 Ollama `/api/chat` 的 `think` / `message.thinking` 兼容面已经实现，但只有请求字段或推理模型名触发时才启用。
- Copilot Chat 当前主验收路径仍是 `/v1/chat/completions`；`/api/chat` 的 thinking 兼容主要服务于 Ollama 官方客户端和后续兼容场景。
- 未知供应商和未知模型默认只声明文本聊天能力。确认某个模型支持工具或视觉后，应通过能力矩阵显式打开。
