using System.Runtime.CompilerServices;
using VSCopilotSwitch.Core.Providers;

namespace VSCopilotSwitch.Services;

public sealed class ActiveProviderModelProvider : IModelProvider
{
    private static readonly TimeSpan RuntimeTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan DefaultCircuitBreakDuration = TimeSpan.FromMinutes(1);
    private const int DefaultFailureThreshold = 3;

    private readonly IProviderConfigService _providerConfigService;
    private readonly InMemoryModelProvider _fallbackProvider = new();
    private readonly Func<ProviderRuntimeConfig, IModelProvider> _providerFactory;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _circuitLock = new();
    private readonly Dictionary<string, CircuitState> _circuits = new(StringComparer.OrdinalIgnoreCase);

    public ActiveProviderModelProvider(IProviderConfigService providerConfigService)
        : this(providerConfigService, CreateProvider, () => DateTimeOffset.UtcNow)
    {
    }

    internal ActiveProviderModelProvider(
        IProviderConfigService providerConfigService,
        Func<ProviderRuntimeConfig, IModelProvider> providerFactory,
        Func<DateTimeOffset> clock)
    {
        _providerConfigService = providerConfigService;
        _providerFactory = providerFactory;
        _clock = clock;
    }

    public string Name => "active-provider";

    public async Task<IReadOnlyList<ProviderModel>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        var config = await _providerConfigService.GetActiveRuntimeConfigAsync(cancellationToken);
        if (!HasUsableRuntimeConfig(config))
        {
            return await _fallbackProvider.ListModelsAsync(cancellationToken);
        }

        var provider = _providerFactory(config!);
        try
        {
            var models = await provider.ListModelsAsync(cancellationToken);
            RecordSuccess(config!.Id);
            return models.Count > 0 ? models : CreateConfiguredModelFallback(config!);
        }
        catch (ProviderException ex) when (CanUseConfiguredModelFallback(ex))
        {
            RecordFailure(config!.Id, ex.Kind);
            return CreateConfiguredModelFallback(config!);
        }
    }

    public async Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        var (config, provider) = await CreateActiveProviderAsync(cancellationToken);
        if (config is null)
        {
            return await provider.ChatAsync(request, cancellationToken);
        }

        EnsureCircuitAllowsRequest(config.Id);
        try
        {
            var response = await provider.ChatAsync(request, cancellationToken);
            RecordSuccess(config.Id);
            return response;
        }
        catch (ProviderException ex) when (ShouldRecordCircuitFailure(ex.Kind))
        {
            RecordFailure(config.Id, ex.Kind);
            throw;
        }
    }

    public async IAsyncEnumerable<ChatStreamChunk> ChatStreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (config, provider) = await CreateActiveProviderAsync(cancellationToken);
        if (config is not null)
        {
            EnsureCircuitAllowsRequest(config.Id);
        }

        var sawChunk = false;
        try
        {
            await foreach (var chunk in provider.ChatStreamAsync(request, cancellationToken).WithCancellation(cancellationToken))
            {
                sawChunk = true;
                yield return chunk;
            }
        }
        finally
        {
            if (config is not null && sawChunk)
            {
                RecordSuccess(config.Id);
            }
        }
    }

    private async Task<(ProviderRuntimeConfig? Config, IModelProvider Provider)> CreateActiveProviderAsync(CancellationToken cancellationToken)
    {
        var config = await _providerConfigService.GetActiveRuntimeConfigAsync(cancellationToken);
        // 没有真实密钥时只启用本地占位 Provider，避免把半配置供应商误当成可用上游。
        if (!HasUsableRuntimeConfig(config))
        {
            return (null, _fallbackProvider);
        }

        return (config, _providerFactory(config!));
    }

    private void EnsureCircuitAllowsRequest(string providerId)
    {
        lock (_circuitLock)
        {
            if (!_circuits.TryGetValue(providerId, out var circuit) || circuit.State != CircuitBreakerState.Open)
            {
                return;
            }

            var now = _clock();
            if (circuit.OpenedAt is not null && now - circuit.OpenedAt.Value >= DefaultCircuitBreakDuration)
            {
                circuit.State = CircuitBreakerState.HalfOpen;
                return;
            }

            throw new ProviderException(ProviderErrorKind.Unavailable, $"提供商 `{providerId}` 熔断中，请稍后重试。");
        }
    }

    private void RecordSuccess(string providerId)
    {
        lock (_circuitLock)
        {
            _circuits[providerId] = new CircuitState();
        }
    }

    private void RecordFailure(string providerId, ProviderErrorKind kind)
    {
        if (!ShouldRecordCircuitFailure(kind))
        {
            return;
        }

        lock (_circuitLock)
        {
            if (!_circuits.TryGetValue(providerId, out var circuit))
            {
                circuit = new CircuitState();
                _circuits[providerId] = circuit;
            }

            if (circuit.State == CircuitBreakerState.HalfOpen)
            {
                OpenCircuit(circuit);
                return;
            }

            circuit.FailureCount++;
            if (circuit.FailureCount >= DefaultFailureThreshold)
            {
                OpenCircuit(circuit);
            }
        }
    }

    private void OpenCircuit(CircuitState circuit)
    {
        circuit.State = CircuitBreakerState.Open;
        circuit.OpenedAt = _clock();
        circuit.FailureCount = DefaultFailureThreshold;
    }

    private static bool HasUsableRuntimeConfig(ProviderRuntimeConfig? config)
        => config is not null
            && !string.IsNullOrWhiteSpace(config.ApiKey)
            && !string.IsNullOrWhiteSpace(config.ApiUrl)
            && !string.IsNullOrWhiteSpace(config.Model);

    private static IModelProvider CreateProvider(ProviderRuntimeConfig config)
        => ProviderAdapterFactory.Create(new ProviderAdapterConfig(
            config.Id,
            config.Name,
            config.ApiUrl,
            config.Model,
            config.Vendor,
            config.ApiKey!), RuntimeTimeout);

    private static bool CanUseConfiguredModelFallback(ProviderException exception)
        => exception.Kind is ProviderErrorKind.Timeout
            or ProviderErrorKind.Unavailable
            or ProviderErrorKind.UpstreamError
            or ProviderErrorKind.InvalidRequest;

    private static bool ShouldRecordCircuitFailure(ProviderErrorKind kind)
        => kind is ProviderErrorKind.Timeout
            or ProviderErrorKind.Unavailable
            or ProviderErrorKind.UpstreamError
            or ProviderErrorKind.RateLimited;

    private static IReadOnlyList<ProviderModel> CreateConfiguredModelFallback(ProviderRuntimeConfig config)
    {
        var model = config.Model.Trim();
        return new[]
        {
            new ProviderModel(
                $"{config.Id}/{model}",
                config.Id,
                model,
                model,
                new[] { model })
        };
    }

    private enum CircuitBreakerState
    {
        Closed,
        Open,
        HalfOpen
    }

    private sealed class CircuitState
    {
        public int FailureCount { get; set; }

        public DateTimeOffset? OpenedAt { get; set; }

        public CircuitBreakerState State { get; set; }
    }
}
