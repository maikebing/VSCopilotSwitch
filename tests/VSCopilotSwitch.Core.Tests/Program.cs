using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using VSCopilotSwitch.Core.Ollama;
using VSCopilotSwitch.Core.Providers;
using VSCopilotSwitch.Core.Providers.Claude;
using VSCopilotSwitch.Core.Providers.DeepSeek;
using VSCopilotSwitch.Core.Providers.Moark;
using VSCopilotSwitch.Core.Providers.Nvidia;
using VSCopilotSwitch.Core.Providers.OpenAI;
using VSCopilotSwitch.Core.Providers.Sub2Api;

var tests = new (string Name, Func<Task> Run)[]
{
    ("ListTagsAsync exposes VS Code suffixed upstream model names", ListTagsAsync_ExposesVsCodeSuffixedUpstreamModelNames),
    ("ChatAsync routes aliases to upstream model", ChatAsync_RoutesAliasesToUpstreamModel),
    ("ChatAsync preserves tool metadata in core request", ChatAsync_PreservesToolMetadataInCoreRequest),
    ("ChatAsync preserves Ollama think and message thinking", ChatAsync_PreservesOllamaThinkAndMessageThinking),
    ("ChatAsync returns tool calls and usage", ChatAsync_ReturnsToolCallsAndUsage),
    ("ShowAsync returns Ollama metadata for VS Code suffixed model", ShowAsync_ReturnsMetadataForVsCodeSuffixedModel),
    ("ChatStreamAsync emits chunks and final done", ChatStreamAsync_EmitsChunksAndFinalDone),
    ("ChatStreamAsync preserves delta tool calls and usage", ChatStreamAsync_PreservesDeltaToolCallsAndUsage),
    ("ChatStreamAsync emits Ollama message thinking", ChatStreamAsync_EmitsOllamaMessageThinking),
    ("ListTagsAsync applies provider capability matrix", ListTagsAsync_AppliesProviderCapabilityMatrix),
    ("ChatAsync strips VS Code suffix before upstream forwarding", ChatAsync_StripsVsCodeSuffixBeforeUpstreamForwarding),
    ("ChatAsync rejects unknown model", ChatAsync_RejectsUnknownModel),
    ("ChatAsync rejects ambiguous alias", ChatAsync_RejectsAmbiguousAlias),
    ("ChatAsync maps provider errors", ChatAsync_MapsProviderErrors),
    ("Provider connection tester probes model list and chat", ProviderConnectionTester_ProbesModelListAndChat),
    ("Provider connection tester auto-selects preferred model", ProviderConnectionTester_AutoSelectsPreferredModel),
    ("Provider connection tester rejects missing configured model", ProviderConnectionTester_RejectsMissingConfiguredModel),
    ("Provider connection tester redacts provider errors", ProviderConnectionTester_RedactsProviderErrors),
    ("Sub2Api ListModelsAsync fetches OpenAI-compatible models", Sub2Api_ListModelsAsync_FetchesOpenAiCompatibleModels),
    ("Sub2Api ChatAsync sends upstream model", Sub2Api_ChatAsync_SendsUpstreamModel),
    ("Sub2Api ChatStreamAsync parses SSE chunks", Sub2Api_ChatStreamAsync_ParsesSseChunks),
    ("Sub2Api maps HTTP errors and redacts API key", Sub2Api_MapsHttpErrorsAndRedactsApiKey),
    ("OpenAI ListModelsAsync uses official endpoint and headers", OpenAI_ListModelsAsync_UsesOfficialEndpointAndHeaders),
    ("OpenAI ChatAsync sends upstream model", OpenAI_ChatAsync_SendsUpstreamModel),
    ("OpenAI ChatAsync forwards tools and parses tool calls", OpenAI_ChatAsync_ForwardsToolsAndParsesToolCalls),
    ("OpenAI ChatStreamAsync parses SSE chunks", OpenAI_ChatStreamAsync_ParsesSseChunks),
    ("OpenAI ChatStreamAsync parses tool call deltas", OpenAI_ChatStreamAsync_ParsesToolCallDeltas),
    ("OpenAI maps HTTP errors and redacts API key", OpenAI_MapsHttpErrorsAndRedactsApiKey),
    ("DeepSeek ListModelsAsync uses official endpoint", DeepSeek_ListModelsAsync_UsesOfficialEndpoint),
    ("DeepSeek ChatAsync sends upstream model", DeepSeek_ChatAsync_SendsUpstreamModel),
    ("DeepSeek ChatAsync strips reasoning content outside thinking path", DeepSeek_ChatAsync_StripsReasoningContentOutsideThinkingPath),
    ("DeepSeek ChatAsync maps Ollama think signal", DeepSeek_ChatAsync_MapsOllamaThinkSignal),
    ("DeepSeek ChatAsync restores reasoning content for tool result turns", DeepSeek_ChatAsync_RestoresReasoningContentForToolResultTurns),
    ("DeepSeek ChatStreamAsync parses SSE chunks", DeepSeek_ChatStreamAsync_ParsesSseChunks),
    ("DeepSeek ChatStreamAsync maps reasoning content deltas", DeepSeek_ChatStreamAsync_MapsReasoningContentDeltas),
    ("DeepSeek ChatStreamAsync rewrites reasoning errors", DeepSeek_ChatStreamAsync_RewritesReasoningErrors),
    ("DeepSeek maps HTTP errors and redacts API key", DeepSeek_MapsHttpErrorsAndRedactsApiKey),
    ("NVIDIA NIM uses OpenAI-compatible v1 endpoints", NvidiaNim_UsesOpenAiCompatibleV1Endpoints),
    ("NVIDIA NIM maps HTTP errors and redacts API key", NvidiaNim_MapsHttpErrorsAndRedactsApiKey),
    ("MoArk uses configured v1 base endpoint", Moark_UsesConfiguredV1BaseEndpoint),
    ("MoArk maps HTTP errors and redacts API key", Moark_MapsHttpErrorsAndRedactsApiKey),
    ("Claude ListModelsAsync uses Anthropic headers", Claude_ListModelsAsync_UsesAnthropicHeaders),
    ("Claude ChatAsync converts system and messages", Claude_ChatAsync_ConvertsSystemAndMessages),
    ("Claude ChatAsync maps tools and tool results", Claude_ChatAsync_MapsToolsAndToolResults),
    ("Claude ChatStreamAsync parses message stream", Claude_ChatStreamAsync_ParsesMessageStream),
    ("Claude maps HTTP errors and redacts API key", Claude_MapsHttpErrorsAndRedactsApiKey)
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}");
    }
}

return failed == 0 ? 0 : 1;

static async Task ListTagsAsync_ExposesVsCodeSuffixedUpstreamModelNames()
{
    var provider = new RecordingProvider(
        "openai",
        new ProviderModel("openai/gpt-4.1", "openai", "gpt-4.1", "GPT 4.1"),
        "ok");
    var service = new OllamaProxyService(new[] { provider });

    var response = await service.ListTagsAsync();

    Assert.Equal("gpt-4.1@vscs", response.Models[0].Name, "tags name 应使用 VS Code 可见的后缀模型名。");
    Assert.Equal("gpt-4.1@vscs", response.Models[0].Model, "tags model 应使用 VS Code 可见的后缀模型名。");
    Assert.Equal("gpt-4.1", response.Models[0].Details.ParentModel, "原始上游模型名应保留在 parent_model 供 UI 展示和调试。");
    Assert.Equal(400000, response.Models[0].ContextLength, "tags 响应应在模型列表直接声明 400K 上下文。");
    var capabilities = string.Join(",", response.Models[0].Capabilities);
    Assert.Contains("tools", capabilities, "tags 响应应声明工具能力。");
    Assert.Contains("vision", capabilities, "tags 响应应声明视觉能力。");
    Assert.DoesNotContain("thinking", capabilities, "tags 响应不应在能力列表虚报 thinking。");
    Assert.DoesNotContain("reasoning", capabilities, "tags 响应不应在能力列表虚报 reasoning。");
    Assert.True(response.Models[0].SupportsToolCalling, "tags 响应应在顶层声明工具调用能力。");
    Assert.True(response.Models[0].SupportsVision, "tags 响应应在顶层声明视觉能力。");
    Assert.False(response.Models[0].SupportsThinking, "tags 响应不应在顶层声明支持 thinking。");
    Assert.False(response.Models[0].SupportsReasoning, "tags 响应不应在顶层声明支持 reasoning。");
    Assert.Equal("llama", response.Models[0].ModelInfo["general.architecture"].ToString() ?? string.Empty, "tags model_info 应声明通用架构。");
    Assert.Equal("gpt-4.1", response.Models[0].ModelInfo["general.basename"].ToString() ?? string.Empty, "tags model_info 应声明原始上游模型 basename。");
    Assert.Equal(400000, Convert.ToInt32(response.Models[0].ModelInfo["llama.context_length"]), "tags model_info 应声明 400K 上下文。");
    Assert.True(!response.Models[0].ModelInfo.ContainsKey("llama.thinking.enabled"), "tags model_info 不应声明未验证的 thinking enabled。");
    Assert.True(!response.Models[0].ModelInfo.ContainsKey("vscopilotswitch.supports_tool_calling"), "tags model_info 不应声明自定义工具能力字段。");
    Assert.True(!response.Models[0].ModelInfo.ContainsKey("vscopilotswitch.supports_vision"), "tags model_info 不应声明自定义视觉能力字段。");
    Assert.True(!response.Models[0].ModelInfo.ContainsKey("vscopilotswitch.supports_reasoning"), "tags model_info 不应声明自定义 reasoning 能力字段。");
}

static async Task ChatAsync_RoutesAliasesToUpstreamModel()
{
    var provider = new RecordingProvider(
        "alpha",
        new ProviderModel("alpha/default", "alpha", "upstream/gpt", "Alpha Default", new[] { "fast" }),
        "ok");
    var service = new OllamaProxyService(new[] { provider });

    var response = await service.ChatAsync(new OllamaChatRequest(
        "fast",
        new[] { new OllamaChatMessage("user", "hello") },
        false));

    Assert.Equal("fast", response.Model, "Ollama 响应应保留用户请求的模型别名。");
    Assert.NotNull(provider.LastRequest, "Provider 应收到路由后的请求。");
    var lastRequest = provider.LastRequest!;
    Assert.Equal("alpha/default", lastRequest.Model, "Provider 请求应使用内部规范模型名。");
    Assert.Equal("alpha", lastRequest.Provider, "Provider 名称应来自路由模型。");
    Assert.Equal("upstream/gpt", lastRequest.UpstreamModel, "Provider 请求应携带上游模型名。");
}

static async Task ChatAsync_PreservesToolMetadataInCoreRequest()
{
    var provider = new RecordingProvider(
        "alpha",
        new ProviderModel("alpha/default", "alpha", "upstream/gpt", "Alpha Default", new[] { "fast" }),
        "ok");
    var service = new OllamaProxyService(new[] { provider });
    using var parametersDocument = JsonDocument.Parse("""{"type":"object","properties":{"query":{"type":"string"}}}""");
    var parameters = parametersDocument.RootElement.Clone();
    var requestToolCall = new ChatToolCall(
        "call_1",
        "function",
        new ChatFunctionCall("lookup", """{"query":"ping"}"""));

    await service.ChatAsync(new OllamaChatRequest(
        "fast",
        new[]
        {
            new OllamaChatMessage("assistant", string.Empty, new[] { requestToolCall }),
            new OllamaChatMessage("tool", "lookup result", ToolCallId: "call_1", Name: "lookup")
        },
        false,
        new[] { new ChatTool("function", new ChatFunctionTool("lookup", "Search docs", parameters)) },
        new ChatToolChoice("function", "lookup")));

    Assert.NotNull(provider.LastRequest, "Provider 应收到扩展后的工具请求。");
    var lastRequest = provider.LastRequest!;
    Assert.Equal("function", lastRequest.ToolChoice?.Type ?? string.Empty, "tool_choice 类型应透传到核心请求。");
    Assert.Equal("lookup", lastRequest.ToolChoice?.FunctionName ?? string.Empty, "tool_choice 函数名应透传到核心请求。");
    Assert.Equal("lookup", lastRequest.Tools?[0].Function.Name ?? string.Empty, "工具定义应透传到核心请求。");
    Assert.Equal(parameters.GetRawText(), lastRequest.Tools?[0].Function.Parameters?.GetRawText() ?? string.Empty, "工具 JSON schema 应保留。");
    Assert.Equal("call_1", lastRequest.Messages[0].ToolCalls?[0].Id ?? string.Empty, "assistant tool_calls 应保留。");
    Assert.Equal("call_1", lastRequest.Messages[1].ToolCallId ?? string.Empty, "tool 结果消息应保留 tool_call_id。");
    Assert.Equal("lookup", lastRequest.Messages[1].Name ?? string.Empty, "tool 结果消息应保留 name。");
}

static async Task ChatAsync_PreservesOllamaThinkAndMessageThinking()
{
    var provider = new RecordingProvider(
        "alpha",
        new ProviderModel("alpha/default", "alpha", "upstream/gpt", "Alpha Default", new[] { "fast" }),
        string.Empty,
        responseReasoningContent: "provider thinking");
    var service = new OllamaProxyService(new[] { provider });
    using var thinkDocument = JsonDocument.Parse("true");

    var response = await service.ChatAsync(new OllamaChatRequest(
        "fast",
        new[]
        {
            new OllamaChatMessage("user", "hello"),
            new OllamaChatMessage("assistant", string.Empty, Thinking: "history thinking")
        },
        false,
        Think: thinkDocument.RootElement.Clone()));

    Assert.NotNull(provider.LastRequest, "Provider 应收到 Ollama thinking 请求。");
    var lastRequest = provider.LastRequest!;
    Assert.Equal(JsonValueKind.True, lastRequest.Think?.ValueKind ?? JsonValueKind.Undefined, "Ollama 官方 think 字段应透传到核心请求。");
    Assert.Equal("history thinking", lastRequest.Messages[1].ReasoningContent ?? string.Empty, "Ollama message.thinking 应映射到核心 reasoning 内容。");
    Assert.Equal("provider thinking", response.Message.Thinking ?? string.Empty, "Provider reasoning 应映射为 Ollama 官方 message.thinking。");
    Assert.Equal("provider thinking", response.Message.ReasoningContent ?? string.Empty, "Provider reasoning 仍应保留为 reasoning_content 兼容字段。");
}

static async Task ChatAsync_ReturnsToolCallsAndUsage()
{
    var responseToolCall = new ChatToolCall(
        "call_2",
        "function",
        new ChatFunctionCall("lookup", """{"query":"pong"}"""));
    var usage = new ChatUsage(10, 5, 15);
    var provider = new RecordingProvider(
        "alpha",
        new ProviderModel("alpha/default", "alpha", "upstream/gpt", "Alpha Default"),
        string.Empty,
        doneReason: "tool_calls",
        responseToolCalls: new[] { responseToolCall },
        responseUsage: usage);
    var service = new OllamaProxyService(new[] { provider });

    var response = await service.ChatAsync(new OllamaChatRequest(
        "alpha/default",
        new[] { new OllamaChatMessage("user", "hello") },
        false));

    Assert.Equal("tool_calls", response.DoneReason ?? string.Empty, "工具调用结束原因应透传。");
    Assert.Equal("call_2", response.Message.ToolCalls?[0].Id ?? string.Empty, "assistant tool_calls 应进入 Ollama 响应消息。");
    Assert.Equal(10, response.Usage?.PromptTokens ?? 0, "usage.prompt_tokens 应透传。");
    Assert.Equal(5, response.Usage?.CompletionTokens ?? 0, "usage.completion_tokens 应透传。");
    Assert.Equal(15, response.Usage?.TotalTokens ?? 0, "usage.total_tokens 应透传。");
}

static async Task ShowAsync_ReturnsMetadataForVsCodeSuffixedModel()
{
    var provider = new RecordingProvider(
        "openai",
        new ProviderModel("openai/gpt-4.1", "openai", "gpt-4.1", "GPT 4.1"),
        "ok");
    var service = new OllamaProxyService(new[] { provider });

    var response = await service.ShowAsync(new OllamaShowRequest("gpt-4.1@vscs"));

    Assert.Equal("gpt-4.1", response.Details.ParentModel, "show 响应应暴露原始上游模型名。");
    Assert.Equal("llama", response.Details.Family, "show 响应应使用 VS Code/Ollama 更易识别的模型架构族。");
    var capabilities = string.Join(",", response.Capabilities);
    Assert.Contains("completion", capabilities, "show 响应应声明基础补全能力。");
    Assert.Contains("tools", capabilities, "show 响应应声明工具调用能力。");
    Assert.Contains("vision", capabilities, "show 响应应声明视觉能力。");
    Assert.DoesNotContain("thinking", capabilities, "show 响应不应在能力列表虚报 thinking。");
    Assert.DoesNotContain("reasoning", capabilities, "show 响应不应在能力列表虚报 reasoning。");
    Assert.Equal("llama", response.ModelInfo["general.architecture"].ToString() ?? string.Empty, "show model_info 应声明通用架构。");
    Assert.Equal("gpt-4.1", response.ModelInfo["general.basename"].ToString() ?? string.Empty, "show model_info 应声明原始上游模型 basename。");
    Assert.Equal(400000, Convert.ToInt32(response.ModelInfo["general.context_length"]), "show 响应应声明通用 400K 上下文。");
    Assert.Equal(400000, Convert.ToInt32(response.ModelInfo["llama.context_length"]), "show 响应应声明 400K 上下文。");
    Assert.True(!response.ModelInfo.ContainsKey("vscopilotswitch.supports_tool_calling"), "show model_info 不应声明自定义工具能力字段。");
    Assert.True(!response.ModelInfo.ContainsKey("vscopilotswitch.supports_vision"), "show model_info 不应声明自定义视觉能力字段。");
    Assert.True(!response.ModelInfo.ContainsKey("vscopilotswitch.supports_reasoning"), "show model_info 不应声明自定义 reasoning 能力字段。");
}

static async Task ChatStreamAsync_EmitsChunksAndFinalDone()
{
    var provider = new RecordingProvider(
        "alpha",
        new ProviderModel("alpha/default", "alpha", "upstream/gpt", "Alpha Default"),
        "ignored",
        new[]
        {
            new ChatStreamChunk("alpha/default", "hel", Done: false),
            new ChatStreamChunk("alpha/default", "lo", Done: false)
        });
    var service = new OllamaProxyService(new[] { provider });

    var chunks = new List<OllamaChatResponse>();
    await foreach (var chunk in service.ChatStreamAsync(new OllamaChatRequest(
        "alpha/default",
        new[] { new OllamaChatMessage("user", "hello") },
        true)))
    {
        chunks.Add(chunk);
    }

    Assert.Equal(3, chunks.Count, "流式响应应包含两个内容分块和一个结束分块。");
    Assert.Equal("hel", chunks[0].Message.Content, "第一个分块内容不正确。");
    Assert.False(chunks[0].Done, "内容分块不应标记 done。");
    Assert.True(chunks[^1].Done, "最后一个分块应标记 done。");
    Assert.Equal("stop", chunks[^1].DoneReason ?? string.Empty, "结束原因应透传。");
}

static async Task ChatStreamAsync_PreservesDeltaToolCallsAndUsage()
{
    var streamToolCall = new ChatToolCall(
        "call_stream",
        "function",
        new ChatFunctionCall("lookup", """{"query":"stream"}"""),
        Index: 0);
    var usage = new ChatUsage(7, 3, 10);
    var provider = new RecordingProvider(
        "alpha",
        new ProviderModel("alpha/default", "alpha", "upstream/gpt", "Alpha Default"),
        "ignored",
        new[]
        {
            new ChatStreamChunk(
                "alpha/default",
                string.Empty,
                Done: false,
                Delta: new ChatDelta("assistant", null, new[] { streamToolCall })),
            new ChatStreamChunk(
                "alpha/default",
                string.Empty,
                Done: true,
                DoneReason: "tool_calls",
                Usage: usage)
        });
    var service = new OllamaProxyService(new[] { provider });

    var chunks = new List<OllamaChatResponse>();
    await foreach (var chunk in service.ChatStreamAsync(new OllamaChatRequest(
        "alpha/default",
        new[] { new OllamaChatMessage("user", "hello") },
        true)))
    {
        chunks.Add(chunk);
    }

    Assert.Equal("assistant", chunks[0].Message.Role, "流式 delta role 应透传。");
    Assert.Equal("call_stream", chunks[0].Message.ToolCalls?[0].Id ?? string.Empty, "流式 tool_calls delta 应透传。");
    Assert.Equal("tool_calls", chunks[^1].DoneReason ?? string.Empty, "流式 finish reason 应透传。");
    Assert.Equal(10, chunks[^1].Usage?.TotalTokens ?? 0, "流式 usage 应透传。");
}

static async Task ChatStreamAsync_EmitsOllamaMessageThinking()
{
    var provider = new RecordingProvider(
        "alpha",
        new ProviderModel("alpha/default", "alpha", "upstream/gpt", "Alpha Default"),
        "ignored",
        new[]
        {
            new ChatStreamChunk(
                "alpha/default",
                string.Empty,
                Done: false,
                Delta: new ChatDelta("assistant", null, ReasoningContent: "stream thinking")),
            new ChatStreamChunk("alpha/default", "answer", Done: false),
            new ChatStreamChunk("alpha/default", string.Empty, Done: true, "stop")
        });
    var service = new OllamaProxyService(new[] { provider });

    var chunks = new List<OllamaChatResponse>();
    await foreach (var chunk in service.ChatStreamAsync(new OllamaChatRequest(
        "alpha/default",
        new[] { new OllamaChatMessage("user", "hello") },
        true)))
    {
        chunks.Add(chunk);
    }

    Assert.Equal("stream thinking", chunks[0].Message.Thinking ?? string.Empty, "流式 reasoning 应映射为 Ollama 官方 message.thinking。");
    Assert.Equal("stream thinking", chunks[0].Message.ReasoningContent ?? string.Empty, "流式 reasoning 仍应保留为 reasoning_content 兼容字段。");
    Assert.Equal("answer", chunks[1].Message.Content, "流式正文内容应继续透传。");
}

static async Task ListTagsAsync_AppliesProviderCapabilityMatrix()
{
    var providers = new IModelProvider[]
    {
        new RecordingProvider(
            "deepseek",
            new ProviderModel("deepseek/v4", "deepseek", "deepseek-v4", "DeepSeek V4"),
            "ok"),
        new RecordingProvider(
            "openai",
            new ProviderModel("openai/gpt-4.1", "openai", "gpt-4.1", "GPT 4.1"),
            "ok"),
        new RecordingProvider(
            "custom",
            new ProviderModel(
                "custom/tool-model",
                "custom",
                "custom-tool-model",
                "Custom Tool Model",
                Capabilities: new ProviderModelCapabilities(SupportsTools: true, SupportsVision: false, ContextLength: 128000, Architecture: "qwen")),
            "ok")
    };
    var service = new OllamaProxyService(providers);

    var response = await service.ListTagsAsync();
    var deepSeek = response.Models.Single(model => model.Details.ParentModel == "deepseek-v4");
    var openAi = response.Models.Single(model => model.Details.ParentModel == "gpt-4.1");
    var custom = response.Models.Single(model => model.Details.ParentModel == "custom-tool-model");

    var deepSeekCapabilities = string.Join(",", deepSeek.Capabilities);
    Assert.DoesNotContain("tools", deepSeekCapabilities, "DeepSeek V4 默认应按 text-only 暴露，不声明工具能力。");
    Assert.DoesNotContain("vision", deepSeekCapabilities, "DeepSeek V4 默认不应声明视觉能力。");
    Assert.False(deepSeek.SupportsToolCalling, "DeepSeek V4 顶层工具能力应为 false。");
    Assert.False(deepSeek.SupportsVision, "DeepSeek V4 顶层视觉能力应为 false。");

    var openAiCapabilities = string.Join(",", openAi.Capabilities);
    Assert.Contains("tools", openAiCapabilities, "已知 OpenAI Chat 模型应声明工具能力。");
    Assert.Contains("vision", openAiCapabilities, "已知 OpenAI 多模态模型应声明视觉能力。");
    Assert.True(openAi.SupportsToolCalling, "OpenAI 顶层工具能力应为 true。");
    Assert.True(openAi.SupportsVision, "OpenAI 顶层视觉能力应为 true。");

    var customCapabilities = string.Join(",", custom.Capabilities);
    Assert.Contains("tools", customCapabilities, "显式模型能力应可覆盖保守默认值。");
    Assert.DoesNotContain("vision", customCapabilities, "显式关闭视觉时不应声明 vision。");
    Assert.Equal("qwen", custom.Details.Family, "显式架构应进入 details.family。");
    Assert.Equal(128000, Convert.ToInt32(custom.ModelInfo["qwen.context_length"]), "显式上下文长度应进入架构相关 context_length。");
}

static async Task ChatAsync_StripsVsCodeSuffixBeforeUpstreamForwarding()
{
    var provider = new RecordingProvider(
        "alpha",
        new ProviderModel("alpha/gpt-5.5", "alpha", "gpt-5.5", "GPT 5.5"),
        "ok");
    var service = new OllamaProxyService(new[] { provider });

    var response = await service.ChatAsync(new OllamaChatRequest(
        "gpt-5.5@vscs",
        new[] { new OllamaChatMessage("user", "hello") },
        false));

    Assert.Equal("gpt-5.5@vscs", response.Model, "Ollama 响应应保留 VS Code 请求的模型名。");
    Assert.NotNull(provider.LastRequest, "Provider 应收到去后缀后的请求。");
    Assert.Equal("gpt-5.5", provider.LastRequest!.UpstreamModel, "转发给上游时必须去掉 @vscs 后缀。");
}

static async Task ChatAsync_RejectsUnknownModel()
{
    var provider = new RecordingProvider(
        "alpha",
        new ProviderModel("alpha/default", "alpha", "upstream/gpt", "Alpha Default"),
        "ok");
    var service = new OllamaProxyService(new[] { provider });

    var exception = await Assert.ThrowsAsync<OllamaProxyException>(() => service.ChatAsync(new OllamaChatRequest(
        "missing",
        Array.Empty<OllamaChatMessage>(),
        false)));

    Assert.Equal(OllamaProxyErrorKind.ModelNotFound, exception.Kind, "未知模型应映射为 ModelNotFound。");
    Assert.Equal("model_not_found", exception.Code, "未知模型错误码不正确。");
}

static async Task ChatAsync_RejectsAmbiguousAlias()
{
    var providers = new IModelProvider[]
    {
        new RecordingProvider("alpha", new ProviderModel("alpha/default", "alpha", "a", "Alpha", new[] { "fast" }), "ok"),
        new RecordingProvider("beta", new ProviderModel("beta/default", "beta", "b", "Beta", new[] { "fast" }), "ok")
    };
    var service = new OllamaProxyService(providers);

    var exception = await Assert.ThrowsAsync<OllamaProxyException>(() => service.ChatAsync(new OllamaChatRequest(
        "fast",
        Array.Empty<OllamaChatMessage>(),
        false)));

    Assert.Equal(OllamaProxyErrorKind.AmbiguousModel, exception.Kind, "重复别名应要求用户使用完整模型名。");
    Assert.Equal("ambiguous_model_alias", exception.Code, "重复别名错误码不正确。");
}

static async Task ChatAsync_MapsProviderErrors()
{
    var provider = new RecordingProvider(
        "alpha",
        new ProviderModel("alpha/default", "alpha", "upstream/gpt", "Alpha Default"),
        "ok",
        chatException: new ProviderException(ProviderErrorKind.Unauthorized, "提供商鉴权失败，请检查密钥。"));
    var service = new OllamaProxyService(new[] { provider });

    var exception = await Assert.ThrowsAsync<OllamaProxyException>(() => service.ChatAsync(new OllamaChatRequest(
        "alpha/default",
        Array.Empty<OllamaChatMessage>(),
        false)));

    Assert.Equal(OllamaProxyErrorKind.ProviderUnauthorized, exception.Kind, "Provider 鉴权错误应映射为 ProviderUnauthorized。");
    Assert.Equal("provider_unauthorized", exception.Code, "Provider 鉴权错误码不正确。");
    Assert.Equal("提供商鉴权失败，请检查密钥。", exception.PublicMessage, "错误消息应使用 Provider 提供的脱敏说明。");
}

static async Task ProviderConnectionTester_ProbesModelListAndChat()
{
    var provider = new ConnectionProbeProvider(new[]
    {
        new ProviderModel("alpha/default", "alpha", "gpt-test", "GPT Test", new[] { "test" })
    });
    var tester = new ProviderConnectionTester((_, _) => provider);

    var result = await tester.TestAsync(new ProviderAdapterConfig(
        "alpha",
        "Alpha",
        "https://alpha.example/v1",
        "test",
        "openai-compatible",
        "sk-connection-secret"));

    Assert.True(result.Success, "连接测试应在模型列表和聊天探测都通过后成功。");
    Assert.Equal(1, result.ModelCount, "连接测试应返回模型数量。");
    Assert.Equal("gpt-test", result.SelectedModel ?? string.Empty, "连接测试应按别名选中上游模型。");
    Assert.True(provider.ListModelsCalled, "连接测试应先请求模型列表。");
    Assert.True(provider.ChatCalled, "连接测试应执行最小聊天探测。");
    Assert.Equal("gpt-test", provider.LastRequest?.UpstreamModel ?? string.Empty, "聊天探测应使用选中的上游模型。");
}

static async Task ProviderConnectionTester_AutoSelectsPreferredModel()
{
    var provider = new ConnectionProbeProvider(new[]
    {
        new ProviderModel("alpha/first", "alpha", "first-model", "First Model"),
        new ProviderModel("alpha/sonnet", "alpha", "claude-sonnet-4-6", "Claude Sonnet 4.6"),
        new ProviderModel("alpha/gpt", "alpha", "gpt-5.5", "GPT 5.5")
    });
    var tester = new ProviderConnectionTester((_, _) => provider);

    var result = await tester.TestAsync(new ProviderAdapterConfig(
        "alpha",
        "Alpha",
        "https://alpha.example/v1",
        string.Empty,
        "openai-compatible",
        "sk-connection-secret"));

    Assert.True(result.Success, "模型名称留空时应自动选择模型并继续聊天探测。");
    Assert.Equal("gpt-5.5", result.SelectedModel ?? string.Empty, "自动选择应优先使用 gpt-5.5。");
    Assert.Equal("gpt-5.5", provider.LastRequest?.UpstreamModel ?? string.Empty, "聊天探测应使用自动选中的模型。");
    Assert.Equal(3, result.Models.Count, "连接测试应把远程模型列表返回给 UI 作为候选项。");
}

static async Task ProviderConnectionTester_RejectsMissingConfiguredModel()
{
    var provider = new ConnectionProbeProvider(new[]
    {
        new ProviderModel("alpha/default", "alpha", "gpt-test", "GPT Test")
    });
    var tester = new ProviderConnectionTester((_, _) => provider);

    var result = await tester.TestAsync(new ProviderAdapterConfig(
        "alpha",
        "Alpha",
        "https://alpha.example/v1",
        "missing-model",
        "openai-compatible",
        "sk-connection-secret"));

    Assert.False(result.Success, "配置模型不在模型列表中时连接测试应失败。");
    Assert.False(provider.ChatCalled, "模型不匹配时不应继续发起聊天探测。");
    Assert.Contains("missing-model", result.Message + string.Join(" ", result.Steps.Select(step => step.Message)), "失败说明应指出缺失的模型名。");
}

static async Task ProviderConnectionTester_RedactsProviderErrors()
{
    var provider = new ConnectionProbeProvider(
        Array.Empty<ProviderModel>(),
        listException: new ProviderException(ProviderErrorKind.Unauthorized, "invalid key sk-connection-secret"));
    var tester = new ProviderConnectionTester((_, _) => provider);

    var result = await tester.TestAsync(new ProviderAdapterConfig(
        "alpha",
        "Alpha",
        "https://alpha.example/v1",
        "gpt-test",
        "openai-compatible",
        "sk-connection-secret"));

    Assert.False(result.Success, "模型列表鉴权失败时连接测试应失败。");
    var joinedMessages = result.Message + string.Join(" ", result.Steps.Select(step => step.Message));
    Assert.DoesNotContain("sk-connection-secret", joinedMessages, "连接测试公开错误不得包含 API Key 原文。");
    Assert.Contains("[已脱敏密钥]", joinedMessages, "连接测试错误应保留脱敏提示。");
}

static async Task Sub2Api_ListModelsAsync_FetchesOpenAiCompatibleModels()
{
    var handler = new RecordingHttpMessageHandler();
    handler.EnqueueJson(HttpStatusCode.OK, """
        {
          "object": "list",
          "data": [
            { "id": "gpt-4.1-mini", "object": "model" },
            { "id": "claude-sonnet-4.5", "object": "model" }
          ]
        }
        """);
    var provider = CreateSub2ApiProvider(handler);

    var models = await provider.ListModelsAsync();

    Assert.Equal(2, models.Count, "sub2api 应从 /v1/models 映射模型列表。");
    Assert.Equal("sub2api/gpt-4.1-mini", models[0].Name, "模型名应带 Provider 前缀，避免和其他供应商冲突。");
    Assert.Equal("gpt-4.1-mini", models[0].UpstreamModel, "上游模型名应保持原样。");
    Assert.Equal("/gateway/v1/models", handler.Requests[0].PathAndQuery, "BaseUrl 不带 /v1 时应自动补齐 OpenAI 兼容路径。");
    Assert.Equal("Bearer sk-test-secret", handler.Requests[0].Authorization, "sub2api 请求应使用 Bearer API Key 鉴权。");
}

static async Task Sub2Api_ChatAsync_SendsUpstreamModel()
{
    var handler = new RecordingHttpMessageHandler();
    handler.EnqueueJson(HttpStatusCode.OK, """
        {
          "choices": [
            {
              "message": { "role": "assistant", "content": "pong" },
              "finish_reason": "stop"
            }
          ]
        }
        """);
    var provider = CreateSub2ApiProvider(handler);

    var response = await provider.ChatAsync(new ChatRequest(
        "sub2api/gpt",
        new[] { new ChatMessage("user", "ping") },
        false,
        "sub2api",
        "gpt-4.1"));

    Assert.Equal("pong", response.Content, "非流式响应应提取 choices[0].message.content。");
    var body = JsonDocument.Parse(handler.Requests[0].Body ?? "{}").RootElement;
    Assert.Equal("/gateway/v1/chat/completions", handler.Requests[0].PathAndQuery, "聊天请求应发送到 /v1/chat/completions。");
    Assert.Equal("gpt-4.1", body.GetProperty("model").GetString() ?? string.Empty, "请求体应使用路由后的上游模型名。");
    Assert.False(body.GetProperty("stream").GetBoolean(), "非流式请求应显式发送 stream=false。");
}

static async Task Sub2Api_ChatStreamAsync_ParsesSseChunks()
{
    var handler = new RecordingHttpMessageHandler();
    handler.EnqueueText(HttpStatusCode.OK, """
        data: {"choices":[{"delta":{"content":"hel"}}]}

        data: {"choices":[{"delta":{"content":"lo"}}]}

        data: {"choices":[{"delta":{},"finish_reason":"stop"}]}

        data: [DONE]

        """, "text/event-stream");
    var provider = CreateSub2ApiProvider(handler);

    var chunks = new List<ChatStreamChunk>();
    await foreach (var chunk in provider.ChatStreamAsync(new ChatRequest(
        "sub2api/gpt",
        new[] { new ChatMessage("user", "ping") },
        true,
        "sub2api",
        "gpt-4.1")))
    {
        chunks.Add(chunk);
    }

    Assert.Equal(3, chunks.Count, "流式响应应包含两个内容分块和一个结束分块。");
    Assert.Equal("hel", chunks[0].Content, "第一个 SSE delta 解析不正确。");
    Assert.Equal("lo", chunks[1].Content, "第二个 SSE delta 解析不正确。");
    Assert.True(chunks[^1].Done, "finish_reason 应转换为 done 分块。");
}

static async Task Sub2Api_MapsHttpErrorsAndRedactsApiKey()
{
    var handler = new RecordingHttpMessageHandler();
    handler.EnqueueJson(HttpStatusCode.Unauthorized, """
        {
          "error": {
            "type": "authentication_error",
            "message": "invalid key sk-test-secret"
          }
        }
        """);
    var provider = CreateSub2ApiProvider(handler);

    var exception = await Assert.ThrowsAsync<ProviderException>(() => provider.ListModelsAsync());

    Assert.Equal(ProviderErrorKind.Unauthorized, exception.Kind, "401/403 应映射为 Provider 鉴权错误。");
    Assert.DoesNotContain("sk-test-secret", exception.PublicMessage, "公开错误消息不得包含 API Key 原文。");
    Assert.Contains("[已脱敏密钥]", exception.PublicMessage, "错误消息应保留可理解的脱敏提示。");
}

static async Task OpenAI_ListModelsAsync_UsesOfficialEndpointAndHeaders()
{
    var handler = new RecordingHttpMessageHandler();
    handler.EnqueueJson(HttpStatusCode.OK, """
        {
          "object": "list",
          "data": [
            { "id": "gpt-4.1-mini", "object": "model" },
            { "id": "gpt-4.1", "object": "model" }
          ]
        }
        """);
    var provider = CreateOpenAiProvider(handler);

    var models = await provider.ListModelsAsync();

    Assert.Equal(2, models.Count, "OpenAI 应从 /v1/models 映射模型列表。");
    Assert.Equal("openai/gpt-4.1-mini", models[0].Name, "OpenAI 模型名应带 Provider 前缀。");
    Assert.Equal("/v1/models", handler.Requests[0].PathAndQuery, "OpenAI 默认 BaseUrl 应自动补齐 /v1/models。");
    Assert.Equal("Bearer sk-openai-secret", handler.Requests[0].Authorization, "OpenAI 请求应使用 Bearer API Key 鉴权。");
    Assert.Equal("org-test", handler.Requests[0].Headers["OpenAI-Organization"], "OpenAI 组织头应透传。");
    Assert.Equal("proj-test", handler.Requests[0].Headers["OpenAI-Project"], "OpenAI 项目头应透传。");
}

static async Task OpenAI_ChatAsync_SendsUpstreamModel()
{
    var handler = new RecordingHttpMessageHandler();
    handler.EnqueueJson(HttpStatusCode.OK, """
        {
          "choices": [
            {
              "message": { "role": "assistant", "content": "pong from openai" },
              "finish_reason": "stop"
            }
          ]
        }
        """);
    var provider = CreateOpenAiProvider(handler);

    var response = await provider.ChatAsync(new ChatRequest(
        "openai/gpt",
        new[] { new ChatMessage("user", "ping") },
        false,
        "openai",
        "gpt-4.1"));

    Assert.Equal("pong from openai", response.Content, "OpenAI 非流式响应应提取 choices[0].message.content。");
    var body = JsonDocument.Parse(handler.Requests[0].Body ?? "{}").RootElement;
    Assert.Equal("/v1/chat/completions", handler.Requests[0].PathAndQuery, "OpenAI 聊天请求应发送到 /v1/chat/completions。");
    Assert.Equal("gpt-4.1", body.GetProperty("model").GetString() ?? string.Empty, "OpenAI 请求体应使用路由后的上游模型名。");
    Assert.False(body.GetProperty("stream").GetBoolean(), "OpenAI 非流式请求应显式发送 stream=false。");
}

static async Task OpenAI_ChatAsync_ForwardsToolsAndParsesToolCalls()
{
    var handler = new RecordingHttpMessageHandler();
    handler.EnqueueJson(HttpStatusCode.OK, """
        {
          "choices": [
            {
              "message": {
                "role": "assistant",
                "tool_calls": [
                  {
                    "id": "call_lookup",
                    "type": "function",
                    "function": {
                      "name": "lookup",
                      "arguments": "{\"query\":\"pong\"}"
                    }
                  }
                ]
              },
              "finish_reason": "tool_calls"
            }
          ],
          "usage": {
            "prompt_tokens": 11,
            "completion_tokens": 3,
            "total_tokens": 14
          }
        }
        """);
    var provider = CreateOpenAiProvider(handler);
    using var parametersDocument = JsonDocument.Parse("""{"type":"object","properties":{"query":{"type":"string"}}}""");
    var parameters = parametersDocument.RootElement.Clone();
    var previousToolCall = new ChatToolCall(
        "call_previous",
        "function",
        new ChatFunctionCall("lookup", """{"query":"ping"}"""));

    var response = await provider.ChatAsync(new ChatRequest(
        "openai/gpt",
        new[]
        {
            new ChatMessage("user", "ping"),
            new ChatMessage("assistant", string.Empty, new[] { previousToolCall }),
            new ChatMessage("tool", "lookup result", ToolCallId: "call_previous", Name: "lookup")
        },
        false,
        "openai",
        "gpt-4.1",
        new[] { new ChatTool("function", new ChatFunctionTool("lookup", "Search docs", parameters)) },
        new ChatToolChoice("function", "lookup")));

    var body = JsonDocument.Parse(handler.Requests[0].Body ?? "{}").RootElement;
    Assert.Equal("lookup", body.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString() ?? string.Empty, "OpenAI 请求应透传工具定义。");
    Assert.Equal(parameters.GetRawText(), body.GetProperty("tools")[0].GetProperty("function").GetProperty("parameters").GetRawText(), "OpenAI 请求应保留工具 JSON schema。");
    Assert.Equal("function", body.GetProperty("tool_choice").GetProperty("type").GetString() ?? string.Empty, "OpenAI 请求应透传工具选择类型。");
    Assert.Equal("lookup", body.GetProperty("tool_choice").GetProperty("function").GetProperty("name").GetString() ?? string.Empty, "OpenAI 请求应透传指定工具名。");
    Assert.Equal("call_previous", body.GetProperty("messages")[1].GetProperty("tool_calls")[0].GetProperty("id").GetString() ?? string.Empty, "OpenAI 请求应透传 assistant tool_calls。");
    Assert.Equal("call_previous", body.GetProperty("messages")[2].GetProperty("tool_call_id").GetString() ?? string.Empty, "OpenAI 请求应透传 tool result 的 tool_call_id。");
    Assert.Equal("tool_calls", response.DoneReason, "OpenAI tool_calls finish_reason 应进入核心响应。");
    Assert.Equal("call_lookup", response.ToolCalls?[0].Id ?? string.Empty, "OpenAI 响应 tool_calls 应进入核心响应。");
    Assert.Equal(14, response.Usage?.TotalTokens ?? 0, "OpenAI usage 应进入核心响应。");
}

static async Task OpenAI_ChatStreamAsync_ParsesSseChunks()
{
    var handler = new RecordingHttpMessageHandler();
    handler.EnqueueText(HttpStatusCode.OK, """
        data: {"choices":[{"delta":{"content":"hel"}}]}

        data: {"choices":[{"delta":{"content":"lo"}}]}

        data: {"choices":[{"delta":{},"finish_reason":"stop"}]}

        data: [DONE]

        """, "text/event-stream");
    var provider = CreateOpenAiProvider(handler);

    var chunks = new List<ChatStreamChunk>();
    await foreach (var chunk in provider.ChatStreamAsync(new ChatRequest(
        "openai/gpt",
        new[] { new ChatMessage("user", "ping") },
        true,
        "openai",
        "gpt-4.1")))
    {
        chunks.Add(chunk);
    }

    Assert.Equal(3, chunks.Count, "OpenAI 流式响应应包含两个内容分块和一个结束分块。");
    Assert.Equal("hel", chunks[0].Content, "OpenAI 第一个 SSE delta 解析不正确。");
    Assert.Equal("lo", chunks[1].Content, "OpenAI 第二个 SSE delta 解析不正确。");
    Assert.True(chunks[^1].Done, "OpenAI finish_reason 应转换为 done 分块。");
}

static async Task OpenAI_ChatStreamAsync_ParsesToolCallDeltas()
{
    var handler = new RecordingHttpMessageHandler();
    handler.EnqueueText(HttpStatusCode.OK, """
        data: {"choices":[{"delta":{"role":"assistant","tool_calls":[{"index":0,"id":"call_stream","type":"function","function":{"name":"lookup","arguments":""}}]}}]}

        data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"query\":\"stream\"}"}}]}}]}

        data: {"choices":[{"delta":{},"finish_reason":"tool_calls"}],"usage":{"prompt_tokens":1,"completion_tokens":2,"total_tokens":3}}

        data: [DONE]

        """, "text/event-stream");
    var provider = CreateOpenAiProvider(handler);

    var chunks = new List<ChatStreamChunk>();
    await foreach (var chunk in provider.ChatStreamAsync(new ChatRequest(
        "openai/gpt",
        new[] { new ChatMessage("user", "ping") },
        true,
        "openai",
        "gpt-4.1")))
    {
        chunks.Add(chunk);
    }

    Assert.Equal(3, chunks.Count, "OpenAI 工具流应包含 tool_call 起始、参数 delta 和结束分块。");
    Assert.Equal("assistant", chunks[0].Delta?.Role ?? string.Empty, "OpenAI 工具流应透传 assistant role delta。");
    Assert.Equal("call_stream", chunks[0].Delta?.ToolCalls?[0].Id ?? string.Empty, "OpenAI 工具流应透传 tool_call id。");
    Assert.Equal("""{"query":"stream"}""", chunks[1].Delta?.ToolCalls?[0].Function.Arguments ?? string.Empty, "OpenAI 工具流应透传 arguments delta。");
    Assert.Equal("tool_calls", chunks[^1].DoneReason ?? string.Empty, "OpenAI 工具流 finish_reason 应透传。");
    Assert.Equal(3, chunks[^1].Usage?.TotalTokens ?? 0, "OpenAI 工具流 usage 应透传。");
}

static async Task OpenAI_MapsHttpErrorsAndRedactsApiKey()
{
    var handler = new RecordingHttpMessageHandler();
    handler.EnqueueJson(HttpStatusCode.Unauthorized, """
        {
          "error": {
            "type": "invalid_request_error",
            "message": "Incorrect API key provided: sk-openai-secret."
          }
        }
        """);
    var provider = CreateOpenAiProvider(handler);

    var exception = await Assert.ThrowsAsync<ProviderException>(() => provider.ListModelsAsync());

    Assert.Equal(ProviderErrorKind.Unauthorized, exception.Kind, "OpenAI 401/403 应映射为 Provider 鉴权错误。");
    Assert.DoesNotContain("sk-openai-secret", exception.PublicMessage, "OpenAI 公开错误消息不得包含 API Key 原文。");
    Assert.Contains("[已脱敏密钥]", exception.PublicMessage, "OpenAI 错误消息应保留可理解的脱敏提示。");
}

static async Task DeepSeek_ListModelsAsync_UsesOfficialEndpoint()
{
    var handler = new RecordingHttpMessageHandler();
    handler.EnqueueJson(HttpStatusCode.OK, """
        {
          "object": "list",
          "data": [
            { "id": "deepseek-chat", "object": "model" },
            { "id": "deepseek-reasoner", "object": "model" }
          ]
        }
        """);
    var provider = CreateDeepSeekProvider(handler);

    var models = await provider.ListModelsAsync();

    Assert.Equal(2, models.Count, "DeepSeek 应从 /models 映射模型列表。");
    Assert.Equal("deepseek/deepseek-chat", models[0].Name, "DeepSeek 模型名应带 Provider 前缀。");
    Assert.Equal("/models", handler.Requests[0].PathAndQuery, "DeepSeek 默认 BaseUrl 不应额外补 /v1。");
    Assert.Equal("Bearer sk-deepseek-secret", handler.Requests[0].Authorization, "DeepSeek 请求应使用 Bearer API Key 鉴权。");
}

static async Task DeepSeek_ChatAsync_SendsUpstreamModel()
{
    var handler = new RecordingHttpMessageHandler();
    handler.EnqueueJson(HttpStatusCode.OK, """
        {
          "choices": [
            {
              "message": { "role": "assistant", "content": "pong from deepseek" },
              "finish_reason": "stop"
            }
          ]
        }
        """);
    var provider = CreateDeepSeekProvider(handler);

    var response = await provider.ChatAsync(new ChatRequest(
        "deepseek/chat",
        new[] { new ChatMessage("user", "ping") },
        false,
        "deepseek",
        "deepseek-chat"));

    Assert.Equal("pong from deepseek", response.Content, "DeepSeek 非流式响应应提取 choices[0].message.content。");
    var body = JsonDocument.Parse(handler.Requests[0].Body ?? "{}").RootElement;
    Assert.Equal("/chat/completions", handler.Requests[0].PathAndQuery, "DeepSeek 聊天请求应发送到 /chat/completions。");
    Assert.Equal("deepseek-chat", body.GetProperty("model").GetString() ?? string.Empty, "DeepSeek 请求体应使用路由后的上游模型名。");
    Assert.False(body.GetProperty("stream").GetBoolean(), "DeepSeek 非流式请求应显式发送 stream=false。");
}

static async Task DeepSeek_ChatAsync_StripsReasoningContentOutsideThinkingPath()
{
    var handler = new RecordingHttpMessageHandler();
    handler.EnqueueJson(HttpStatusCode.OK, """
        {
          "choices": [
            {
              "message": { "role": "assistant", "content": "pong" },
              "finish_reason": "stop"
            }
          ]
        }
        """);
    var provider = CreateDeepSeekProvider(handler);

    await provider.ChatAsync(new ChatRequest(
        "deepseek/chat",
        new[]
        {
            new ChatMessage("user", "ping"),
            new ChatMessage("assistant", "history", ReasoningContent: "should not be sent")
        },
        false,
        "deepseek",
        "deepseek-chat"));

    var body = JsonDocument.Parse(handler.Requests[0].Body ?? "{}").RootElement;
    var assistantMessage = body.GetProperty("messages")[1];
    Assert.False(assistantMessage.TryGetProperty("reasoning_content", out _), "普通 DeepSeek chat 请求不应携带历史 reasoning_content。");
}

static async Task DeepSeek_ChatAsync_MapsOllamaThinkSignal()
{
    var handler = new RecordingHttpMessageHandler();
    handler.EnqueueJson(HttpStatusCode.OK, """
        {
          "choices": [
            {
              "message": { "role": "assistant", "content": "pong" },
              "finish_reason": "stop"
            }
          ]
        }
        """);
    var provider = CreateDeepSeekProvider(handler);
    using var thinkDocument = JsonDocument.Parse("\"high\"");

    await provider.ChatAsync(new ChatRequest(
        "deepseek/chat",
        new[] { new ChatMessage("user", "ping") },
        false,
        "deepseek",
        "deepseek-chat",
        Think: thinkDocument.RootElement.Clone()));

    var body = JsonDocument.Parse(handler.Requests[0].Body ?? "{}").RootElement;
    Assert.Equal("high", body.GetProperty("reasoning_effort").GetString() ?? string.Empty, "Ollama think 字符串应转换为 reasoning_effort。");
    Assert.Equal("high", body.GetProperty("thinking").GetString() ?? string.Empty, "Ollama think 字段应转发为 OpenAI-compatible thinking。");
}

static async Task DeepSeek_ChatAsync_RestoresReasoningContentForToolResultTurns()
{
    var handler = new RecordingHttpMessageHandler();
    handler.EnqueueJson(HttpStatusCode.OK, """
        {
          "choices": [
            {
              "message": {
                "role": "assistant",
                "content": "",
                "reasoning_content": "I should call lookup.",
                "tool_calls": [
                  {
                    "id": "call_lookup",
                    "type": "function",
                    "function": {
                      "name": "lookup",
                      "arguments": "{\"query\":\"ping\"}"
                    }
                  }
                ]
              },
              "finish_reason": "tool_calls"
            }
          ]
        }
        """);
    handler.EnqueueJson(HttpStatusCode.OK, """
        {
          "choices": [
            {
              "message": { "role": "assistant", "content": "done" },
              "finish_reason": "stop"
            }
          ]
        }
        """);
    var provider = CreateDeepSeekProvider(handler);
    using var thinkingDocument = JsonDocument.Parse("""{"type":"enabled"}""");
    using var parametersDocument = JsonDocument.Parse("""{"type":"object","properties":{"query":{"type":"string"}}}""");
    var parameters = parametersDocument.RootElement.Clone();

    var first = await provider.ChatAsync(new ChatRequest(
        "deepseek/v4",
        new[] { new ChatMessage("user", "call lookup") },
        false,
        "deepseek",
        "deepseek-v4",
        new[] { new ChatTool("function", new ChatFunctionTool("lookup", "Search docs", parameters)) },
        new ChatToolChoice("auto"),
        "high",
        thinkingDocument.RootElement.Clone()));
    var toolCall = first.ToolCalls?[0] ?? throw new InvalidOperationException("DeepSeek 响应应包含 tool_call。");

    var second = await provider.ChatAsync(new ChatRequest(
        "deepseek/v4",
        new[]
        {
            new ChatMessage("user", "call lookup"),
            new ChatMessage("assistant", string.Empty, new[] { toolCall }),
            new ChatMessage("tool", "lookup result", ToolCallId: toolCall.Id, Name: "lookup")
        },
        false,
        "deepseek",
        "deepseek-v4",
        ReasoningEffort: "high",
        Thinking: thinkingDocument.RootElement.Clone()));

    Assert.Equal("I should call lookup.", first.ReasoningContent ?? string.Empty, "DeepSeek reasoning_content 应进入核心响应。");
    Assert.Equal("done", second.Content, "第二轮工具结果请求应正常完成。");
    var firstBody = JsonDocument.Parse(handler.Requests[0].Body ?? "{}").RootElement;
    Assert.Equal("high", firstBody.GetProperty("reasoning_effort").GetString() ?? string.Empty, "DeepSeek 请求应透传 reasoning_effort。");
    Assert.Equal("enabled", firstBody.GetProperty("thinking").GetProperty("type").GetString() ?? string.Empty, "DeepSeek 请求应透传 thinking。");
    var secondBody = JsonDocument.Parse(handler.Requests[1].Body ?? "{}").RootElement;
    var restoredReasoning = secondBody.GetProperty("messages")[1].GetProperty("reasoning_content").GetString();
    Assert.Equal("I should call lookup.", restoredReasoning ?? string.Empty, "工具结果回合应自动恢复上一轮 reasoning_content。");
}

static async Task DeepSeek_ChatStreamAsync_ParsesSseChunks()
{
    var handler = new RecordingHttpMessageHandler();
    handler.EnqueueText(HttpStatusCode.OK, """
        data: {"choices":[{"delta":{"content":"hel"}}]}

        data: {"choices":[{"delta":{"content":"lo"}}]}

        data: {"choices":[{"delta":{},"finish_reason":"stop"}]}

        data: [DONE]

        """, "text/event-stream");
    var provider = CreateDeepSeekProvider(handler);

    var chunks = new List<ChatStreamChunk>();
    await foreach (var chunk in provider.ChatStreamAsync(new ChatRequest(
        "deepseek/chat",
        new[] { new ChatMessage("user", "ping") },
        true,
        "deepseek",
        "deepseek-chat")))
    {
        chunks.Add(chunk);
    }

    Assert.Equal(3, chunks.Count, "DeepSeek 流式响应应包含两个内容分块和一个结束分块。");
    Assert.Equal("hel", chunks[0].Content, "DeepSeek 第一个 SSE delta 解析不正确。");
    Assert.Equal("lo", chunks[1].Content, "DeepSeek 第二个 SSE delta 解析不正确。");
    Assert.True(chunks[^1].Done, "DeepSeek finish_reason 应转换为 done 分块。");
}

static async Task DeepSeek_ChatStreamAsync_MapsReasoningContentDeltas()
{
    var handler = new RecordingHttpMessageHandler();
    handler.EnqueueText(HttpStatusCode.OK, """
        data: {"choices":[{"delta":{"reasoning_content":"think "}}]}

        data: {"choices":[{"delta":{"content":"answer"}}]}

        data: {"choices":[{"delta":{},"finish_reason":"stop"}]}

        data: [DONE]

        """, "text/event-stream");
    var provider = CreateDeepSeekProvider(handler);
    using var thinkingDocument = JsonDocument.Parse("""{"type":"enabled"}""");

    var chunks = new List<ChatStreamChunk>();
    await foreach (var chunk in provider.ChatStreamAsync(new ChatRequest(
        "deepseek/v4",
        new[] { new ChatMessage("user", "stream with thinking") },
        true,
        "deepseek",
        "deepseek-v4",
        ReasoningEffort: "high",
        Thinking: thinkingDocument.RootElement.Clone())))
    {
        chunks.Add(chunk);
    }

    Assert.Equal("think ", chunks[0].Delta?.ReasoningContent ?? string.Empty, "DeepSeek 流式 reasoning_content 应进入 ChatDelta。");
    Assert.Equal("answer", chunks[1].Content, "DeepSeek 流式正文内容应继续正常解析。");
    Assert.True(chunks[^1].Done, "DeepSeek thinking 流式响应应正常结束。");
}

static async Task DeepSeek_ChatStreamAsync_RewritesReasoningErrors()
{
    var handler = new RecordingHttpMessageHandler();
    handler.EnqueueJson(HttpStatusCode.BadRequest, """
        {
          "error": {
            "type": "invalid_request_error",
            "message": "Missing required reasoning_content for previous assistant tool call."
          }
        }
        """);
    var provider = CreateDeepSeekProvider(handler);

    var exception = await Assert.ThrowsAsync<ProviderException>(async () =>
    {
        await foreach (var _ in provider.ChatStreamAsync(new ChatRequest(
            "deepseek/v4",
            new[] { new ChatMessage("user", "stream thinking") },
            true,
            "deepseek",
            "deepseek-v4",
            ReasoningEffort: "high")))
        {
        }
    });

    Assert.Equal(ProviderErrorKind.InvalidRequest, exception.Kind, "DeepSeek 400 reasoning 错误应保持 InvalidRequest 类型。");
    Assert.Contains("DeepSeek thinking 请求被上游拒绝", exception.PublicMessage, "DeepSeek reasoning 错误应改写为可操作提示。");
    Assert.Contains("reasoning_content", exception.PublicMessage, "改写后的错误提示应保留 reasoning_content 线索。");
}

static async Task DeepSeek_MapsHttpErrorsAndRedactsApiKey()
{
    var handler = new RecordingHttpMessageHandler();
    handler.EnqueueJson(HttpStatusCode.Unauthorized, """
        {
          "error": {
            "type": "authentication_error",
            "message": "Invalid API key sk-deepseek-secret."
          }
        }
        """);
    var provider = CreateDeepSeekProvider(handler);

    var exception = await Assert.ThrowsAsync<ProviderException>(() => provider.ListModelsAsync());

    Assert.Equal(ProviderErrorKind.Unauthorized, exception.Kind, "DeepSeek 401/403 应映射为 Provider 鉴权错误。");
    Assert.DoesNotContain("sk-deepseek-secret", exception.PublicMessage, "DeepSeek 公开错误消息不得包含 API Key 原文。");
    Assert.Contains("[已脱敏密钥]", exception.PublicMessage, "DeepSeek 错误消息应保留可理解的脱敏提示。");
}

static async Task NvidiaNim_UsesOpenAiCompatibleV1Endpoints()
{
    var handler = new RecordingHttpMessageHandler();
    handler.EnqueueJson(HttpStatusCode.OK, """
        {
          "data": [
            { "id": "meta/llama-3.1-8b-instruct" }
          ]
        }
        """);
    handler.EnqueueJson(HttpStatusCode.OK, """
        {
          "choices": [
            {
              "message": { "role": "assistant", "content": "nim pong" },
              "finish_reason": "stop"
            }
          ]
        }
        """);
    var provider = CreateNvidiaNimProvider(handler);

    var models = await provider.ListModelsAsync();
    var response = await provider.ChatAsync(new ChatRequest(
        "nvidia-nim/llama",
        new[] { new ChatMessage("user", "ping") },
        false,
        "nvidia-nim",
        "meta/llama-3.1-8b-instruct"));

    Assert.Equal("nvidia-nim/meta/llama-3.1-8b-instruct", models[0].Name, "NVIDIA NIM 模型名应带 Provider 前缀。");
    Assert.Equal("/v1/models", handler.Requests[0].PathAndQuery, "NVIDIA NIM 默认应使用 /v1/models。");
    Assert.Equal("/v1/chat/completions", handler.Requests[1].PathAndQuery, "NVIDIA NIM 聊天请求应使用 /v1/chat/completions。");
    Assert.Equal("nim pong", response.Content, "NVIDIA NIM 非流式响应应提取 choices[0].message.content。");
}

static async Task NvidiaNim_MapsHttpErrorsAndRedactsApiKey()
{
    var handler = new RecordingHttpMessageHandler();
    handler.EnqueueJson(HttpStatusCode.Unauthorized, """
        {
          "error": {
            "message": "Invalid API key sk-nvidia-secret"
          }
        }
        """);
    var provider = CreateNvidiaNimProvider(handler);

    var exception = await Assert.ThrowsAsync<ProviderException>(() => provider.ListModelsAsync());

    Assert.Equal(ProviderErrorKind.Unauthorized, exception.Kind, "NVIDIA NIM 401/403 应映射为 Provider 鉴权错误。");
    Assert.DoesNotContain("sk-nvidia-secret", exception.PublicMessage, "NVIDIA NIM 公开错误消息不得包含 API Key 原文。");
    Assert.Contains("[已脱敏密钥]", exception.PublicMessage, "NVIDIA NIM 错误消息应保留可理解的脱敏提示。");
}

static async Task Moark_UsesConfiguredV1BaseEndpoint()
{
    var handler = new RecordingHttpMessageHandler();
    handler.EnqueueJson(HttpStatusCode.OK, """
        {
          "data": [
            { "id": "claude-sonnet-4-5" }
          ]
        }
        """);
    handler.EnqueueJson(HttpStatusCode.OK, """
        {
          "choices": [
            {
              "message": { "role": "assistant", "content": "moark pong" },
              "finish_reason": "stop"
            }
          ]
        }
        """);
    var provider = CreateMoarkProvider(handler);

    var models = await provider.ListModelsAsync();
    var response = await provider.ChatAsync(new ChatRequest(
        "moark/sonnet",
        new[] { new ChatMessage("user", "ping") },
        false,
        "moark",
        "claude-sonnet-4-5"));

    Assert.Equal("moark/claude-sonnet-4-5", models[0].Name, "MoArk 模型名应带 Provider 前缀。");
    Assert.Equal("/v1/models", handler.Requests[0].PathAndQuery, "MoArk 默认 BaseUrl 已含 /v1，不应重复拼接。");
    Assert.Equal("/v1/chat/completions", handler.Requests[1].PathAndQuery, "MoArk 聊天请求应使用 /v1/chat/completions。");
    Assert.Equal("moark pong", response.Content, "MoArk 非流式响应应提取 choices[0].message.content。");
}

static async Task Moark_MapsHttpErrorsAndRedactsApiKey()
{
    var handler = new RecordingHttpMessageHandler();
    handler.EnqueueJson(HttpStatusCode.Unauthorized, """
        {
          "error": {
            "message": "Invalid API key sk-moark-secret"
          }
        }
        """);
    var provider = CreateMoarkProvider(handler);

    var exception = await Assert.ThrowsAsync<ProviderException>(() => provider.ListModelsAsync());

    Assert.Equal(ProviderErrorKind.Unauthorized, exception.Kind, "MoArk 401/403 应映射为 Provider 鉴权错误。");
    Assert.DoesNotContain("sk-moark-secret", exception.PublicMessage, "MoArk 公开错误消息不得包含 API Key 原文。");
    Assert.Contains("[已脱敏密钥]", exception.PublicMessage, "MoArk 错误消息应保留可理解的脱敏提示。");
}

static async Task Claude_ListModelsAsync_UsesAnthropicHeaders()
{
    var handler = new RecordingHttpMessageHandler();
    handler.EnqueueJson(HttpStatusCode.OK, """
        {
          "data": [
            { "id": "claude-sonnet-4-5", "display_name": "Claude Sonnet 4.5" }
          ]
        }
        """);
    var provider = CreateClaudeProvider(handler);

    var models = await provider.ListModelsAsync();

    Assert.Equal("claude/claude-sonnet-4-5", models[0].Name, "Claude 模型名应带 Provider 前缀。");
    Assert.Equal("/v1/models", handler.Requests[0].PathAndQuery, "Claude 模型列表应请求 /v1/models。");
    Assert.Equal("sk-claude-secret", handler.Requests[0].Headers["x-api-key"], "Claude 应使用 x-api-key 鉴权头。");
    Assert.Equal("2023-06-01", handler.Requests[0].Headers["anthropic-version"], "Claude 应携带 anthropic-version。");
}

static async Task Claude_ChatAsync_ConvertsSystemAndMessages()
{
    var handler = new RecordingHttpMessageHandler();
    handler.EnqueueJson(HttpStatusCode.OK, """
        {
          "content": [
            { "type": "text", "text": "claude pong" }
          ],
          "stop_reason": "end_turn"
        }
        """);
    var provider = CreateClaudeProvider(handler);

    var response = await provider.ChatAsync(new ChatRequest(
        "claude/sonnet",
        new[]
        {
            new ChatMessage("system", "Be concise."),
            new ChatMessage("user", "ping")
        },
        false,
        "claude",
        "claude-sonnet-4-5"));

    Assert.Equal("claude pong", response.Content, "Claude 非流式响应应拼接 content[].text。");
    Assert.Equal("stop", response.DoneReason, "Claude end_turn 应转换为 Ollama stop。");
    Assert.Equal("/v1/messages", handler.Requests[0].PathAndQuery, "Claude 聊天请求应发送到 /v1/messages。");
    var body = JsonDocument.Parse(handler.Requests[0].Body ?? "{}").RootElement;
    Assert.Equal("claude-sonnet-4-5", body.GetProperty("model").GetString() ?? string.Empty, "Claude 请求体应使用路由后的上游模型名。");
    Assert.Equal(1024, body.GetProperty("max_tokens").GetInt32(), "Claude 请求体应包含配置的 max_tokens。");
    Assert.Equal("Be concise.", body.GetProperty("system").GetString() ?? string.Empty, "Claude system 消息应提升到顶层。");
    Assert.Equal("user", body.GetProperty("messages")[0].GetProperty("role").GetString() ?? string.Empty, "Claude 普通消息应保留 user/assistant 角色。");
}

static async Task Claude_ChatAsync_MapsToolsAndToolResults()
{
    var handler = new RecordingHttpMessageHandler();
    handler.EnqueueJson(HttpStatusCode.OK, """
        {
          "content": [
            {
              "type": "tool_use",
              "id": "toolu_lookup",
              "name": "lookup",
              "input": { "query": "pong" }
            }
          ],
          "stop_reason": "tool_use",
          "usage": {
            "input_tokens": 20,
            "output_tokens": 6
          }
        }
        """);
    var provider = CreateClaudeProvider(handler);
    using var parametersDocument = JsonDocument.Parse("""{"type":"object","properties":{"query":{"type":"string"}}}""");
    var parameters = parametersDocument.RootElement.Clone();
    var previousToolCall = new ChatToolCall(
        "toolu_previous",
        "function",
        new ChatFunctionCall("lookup", """{"query":"ping"}"""));

    var response = await provider.ChatAsync(new ChatRequest(
        "claude/sonnet",
        new[]
        {
            new ChatMessage("system", "Use tools when useful."),
            new ChatMessage("user", "ping"),
            new ChatMessage("assistant", string.Empty, new[] { previousToolCall }),
            new ChatMessage("tool", "lookup result", ToolCallId: "toolu_previous", Name: "lookup")
        },
        false,
        "claude",
        "claude-sonnet-4-5",
        new[] { new ChatTool("function", new ChatFunctionTool("lookup", "Search docs", parameters)) },
        new ChatToolChoice("function", "lookup")));

    var body = JsonDocument.Parse(handler.Requests[0].Body ?? "{}").RootElement;
    Assert.Equal("lookup", body.GetProperty("tools")[0].GetProperty("name").GetString() ?? string.Empty, "Claude 请求应将 function tool 映射为 Anthropic tools。");
    Assert.Equal(parameters.GetRawText(), body.GetProperty("tools")[0].GetProperty("input_schema").GetRawText(), "Claude 请求应保留工具 input_schema。");
    Assert.Equal("tool", body.GetProperty("tool_choice").GetProperty("type").GetString() ?? string.Empty, "Claude 请求应把指定 function 映射为 tool_choice.tool。");
    Assert.Equal("lookup", body.GetProperty("tool_choice").GetProperty("name").GetString() ?? string.Empty, "Claude 请求应透传指定工具名。");
    Assert.Equal("tool_use", body.GetProperty("messages")[1].GetProperty("content")[0].GetProperty("type").GetString() ?? string.Empty, "Claude 请求应把 assistant tool_calls 映射为 tool_use。");
    Assert.Equal("toolu_previous", body.GetProperty("messages")[2].GetProperty("content")[0].GetProperty("tool_use_id").GetString() ?? string.Empty, "Claude 请求应把 tool result 映射为 tool_result。");
    Assert.Equal("tool_calls", response.DoneReason, "Claude tool_use stop_reason 应映射为 tool_calls。");
    Assert.Equal("toolu_lookup", response.ToolCalls?[0].Id ?? string.Empty, "Claude tool_use 响应应进入核心 tool_calls。");
    using var argumentsDocument = JsonDocument.Parse(response.ToolCalls?[0].Function.Arguments ?? "{}");
    Assert.Equal("pong", argumentsDocument.RootElement.GetProperty("query").GetString() ?? string.Empty, "Claude tool_use input 应转回 function arguments。");
    Assert.Equal(26, response.Usage?.TotalTokens ?? 0, "Claude usage 应转换为核心 usage。");
}

static async Task Claude_ChatStreamAsync_ParsesMessageStream()
{
    var handler = new RecordingHttpMessageHandler();
    handler.EnqueueText(HttpStatusCode.OK, """
        event: message_start
        data: {"type":"message_start","message":{"id":"msg_1"}}

        event: content_block_delta
        data: {"type":"content_block_delta","delta":{"type":"text_delta","text":"hel"}}

        event: content_block_delta
        data: {"type":"content_block_delta","delta":{"type":"text_delta","text":"lo"}}

        event: message_delta
        data: {"type":"message_delta","delta":{"stop_reason":"end_turn"}}

        event: message_stop
        data: {"type":"message_stop"}

        """, "text/event-stream");
    var provider = CreateClaudeProvider(handler);

    var chunks = new List<ChatStreamChunk>();
    await foreach (var chunk in provider.ChatStreamAsync(new ChatRequest(
        "claude/sonnet",
        new[] { new ChatMessage("user", "ping") },
        true,
        "claude",
        "claude-sonnet-4-5")))
    {
        chunks.Add(chunk);
    }

    Assert.Equal(3, chunks.Count, "Claude 流式响应应包含两个内容分块和一个结束分块。");
    Assert.Equal("hel", chunks[0].Content, "Claude 第一个 text_delta 解析不正确。");
    Assert.Equal("lo", chunks[1].Content, "Claude 第二个 text_delta 解析不正确。");
    Assert.True(chunks[^1].Done, "Claude message_stop 应转换为 done 分块。");
    Assert.Equal("stop", chunks[^1].DoneReason ?? string.Empty, "Claude end_turn 应转换为 stop。");
}

static async Task Claude_MapsHttpErrorsAndRedactsApiKey()
{
    var handler = new RecordingHttpMessageHandler();
    handler.EnqueueJson(HttpStatusCode.Unauthorized, """
        {
          "type": "error",
          "error": {
            "type": "authentication_error",
            "message": "Invalid API key sk-claude-secret."
          }
        }
        """);
    var provider = CreateClaudeProvider(handler);

    var exception = await Assert.ThrowsAsync<ProviderException>(() => provider.ListModelsAsync());

    Assert.Equal(ProviderErrorKind.Unauthorized, exception.Kind, "Claude 401/403 应映射为 Provider 鉴权错误。");
    Assert.DoesNotContain("sk-claude-secret", exception.PublicMessage, "Claude 公开错误消息不得包含 API Key 原文。");
    Assert.Contains("[已脱敏密钥]", exception.PublicMessage, "Claude 错误消息应保留可理解的脱敏提示。");
}

static Sub2ApiModelProvider CreateSub2ApiProvider(RecordingHttpMessageHandler handler)
{
    return new Sub2ApiModelProvider(
        new HttpClient(handler),
        new Sub2ApiProviderOptions
        {
            BaseUrl = "https://sub2api.example/gateway",
            ApiKey = "sk-test-secret",
            Timeout = TimeSpan.FromSeconds(5)
        });
}

static OpenAiModelProvider CreateOpenAiProvider(RecordingHttpMessageHandler handler)
{
    return new OpenAiModelProvider(
        new HttpClient(handler),
        new OpenAiProviderOptions
        {
            ApiKey = "sk-openai-secret",
            OrganizationId = "org-test",
            ProjectId = "proj-test",
            Timeout = TimeSpan.FromSeconds(5)
        });
}

static DeepSeekModelProvider CreateDeepSeekProvider(RecordingHttpMessageHandler handler)
{
    return new DeepSeekModelProvider(
        new HttpClient(handler),
        new DeepSeekProviderOptions
        {
            ApiKey = "sk-deepseek-secret",
            Timeout = TimeSpan.FromSeconds(5)
        });
}

static NvidiaNimModelProvider CreateNvidiaNimProvider(RecordingHttpMessageHandler handler)
{
    return new NvidiaNimModelProvider(
        new HttpClient(handler),
        new NvidiaNimProviderOptions
        {
            ApiKey = "sk-nvidia-secret",
            Timeout = TimeSpan.FromSeconds(5)
        });
}

static MoarkModelProvider CreateMoarkProvider(RecordingHttpMessageHandler handler)
{
    return new MoarkModelProvider(
        new HttpClient(handler),
        new MoarkProviderOptions
        {
            ApiKey = "sk-moark-secret",
            Timeout = TimeSpan.FromSeconds(5)
        });
}

static ClaudeModelProvider CreateClaudeProvider(RecordingHttpMessageHandler handler)
{
    return new ClaudeModelProvider(
        new HttpClient(handler),
        new ClaudeProviderOptions
        {
            ApiKey = "sk-claude-secret",
            MaxTokens = 1024,
            Timeout = TimeSpan.FromSeconds(5)
        });
}

internal sealed class RecordingProvider : IModelProvider
{
    private readonly IReadOnlyList<ProviderModel> _models;
    private readonly string _content;
    private readonly IReadOnlyList<ChatStreamChunk>? _streamChunks;
    private readonly Exception? _chatException;
    private readonly string _doneReason;
    private readonly IReadOnlyList<ChatToolCall>? _responseToolCalls;
    private readonly ChatUsage? _responseUsage;
    private readonly string? _responseReasoningContent;

    public RecordingProvider(
        string name,
        ProviderModel model,
        string content,
        IReadOnlyList<ChatStreamChunk>? streamChunks = null,
        Exception? chatException = null,
        string doneReason = "stop",
        IReadOnlyList<ChatToolCall>? responseToolCalls = null,
        ChatUsage? responseUsage = null,
        string? responseReasoningContent = null)
    {
        Name = name;
        _models = new[] { model };
        _content = content;
        _streamChunks = streamChunks;
        _chatException = chatException;
        _doneReason = doneReason;
        _responseToolCalls = responseToolCalls;
        _responseUsage = responseUsage;
        _responseReasoningContent = responseReasoningContent;
    }

    public string Name { get; }

    public ChatRequest? LastRequest { get; private set; }

    public Task<IReadOnlyList<ProviderModel>> ListModelsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_models);

    public Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        if (_chatException is not null)
        {
            throw _chatException;
        }

        return Task.FromResult(new ChatResponse(request.Model, _content, _doneReason, _responseToolCalls, _responseUsage, _responseReasoningContent));
    }

    public async IAsyncEnumerable<ChatStreamChunk> ChatStreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        foreach (var chunk in _streamChunks ?? new[] { new ChatStreamChunk(request.Model, _content, Done: false), new ChatStreamChunk(request.Model, string.Empty, Done: true, "stop") })
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return chunk;
        }
    }
}

internal sealed class ConnectionProbeProvider : IModelProvider
{
    private readonly IReadOnlyList<ProviderModel> _models;
    private readonly Exception? _listException;
    private readonly Exception? _chatException;

    public ConnectionProbeProvider(
        IReadOnlyList<ProviderModel> models,
        Exception? listException = null,
        Exception? chatException = null)
    {
        _models = models;
        _listException = listException;
        _chatException = chatException;
    }

    public string Name => "connection-probe";

    public bool ListModelsCalled { get; private set; }

    public bool ChatCalled { get; private set; }

    public ChatRequest? LastRequest { get; private set; }

    public Task<IReadOnlyList<ProviderModel>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        ListModelsCalled = true;
        if (_listException is not null)
        {
            throw _listException;
        }

        return Task.FromResult(_models);
    }

    public Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        ChatCalled = true;
        LastRequest = request;
        if (_chatException is not null)
        {
            throw _chatException;
        }

        return Task.FromResult(new ChatResponse(request.Model, "pong"));
    }

    public async IAsyncEnumerable<ChatStreamChunk> ChatStreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        yield return new ChatStreamChunk(request.Model, "pong", Done: false);
        yield return new ChatStreamChunk(request.Model, string.Empty, Done: true, "stop");
    }
}

internal sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<RecordedHttpRequest, HttpResponseMessage>> _responses = new();

    public List<RecordedHttpRequest> Requests { get; } = new();

    public void EnqueueJson(HttpStatusCode statusCode, string json)
        => EnqueueText(statusCode, json, "application/json");

    public void EnqueueText(HttpStatusCode statusCode, string text, string mediaType)
    {
        _responses.Enqueue(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(text, Encoding.UTF8, mediaType)
        });
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("No fake HTTP response was queued.");
        }

        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        var recorded = new RecordedHttpRequest(
            request.Method.Method,
            request.RequestUri?.PathAndQuery ?? string.Empty,
            request.Headers.Authorization?.ToString(),
            request.Headers.ToDictionary(header => header.Key, header => string.Join(",", header.Value), StringComparer.OrdinalIgnoreCase),
            body);
        Requests.Add(recorded);
        return _responses.Dequeue()(recorded);
    }
}

internal sealed record RecordedHttpRequest(
    string Method,
    string PathAndQuery,
    string? Authorization,
    IReadOnlyDictionary<string, string> Headers,
    string? Body);

internal static class Assert
{
    public static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void False(bool condition, string message)
        => True(!condition, message);

    public static void NotNull(object? value, string message)
    {
        if (value is null)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected: {expected}; Actual: {actual}");
        }
    }

    public static void Contains(string expectedSubstring, string actual, string message)
    {
        if (!actual.Contains(expectedSubstring, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{message} Expected substring: {expectedSubstring}; Actual: {actual}");
        }
    }

    public static void DoesNotContain(string unexpectedSubstring, string actual, string message)
    {
        if (actual.Contains(unexpectedSubstring, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{message} Unexpected substring: {unexpectedSubstring}; Actual: {actual}");
        }
    }

    public static async Task<TException> ThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException ex)
        {
            return ex;
        }

        throw new InvalidOperationException($"Expected exception {typeof(TException).Name} was not thrown.");
    }
}
