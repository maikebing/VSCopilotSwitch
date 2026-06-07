using System.Text.Json;
using System.Text.Json.Serialization;
using VSCopilotSwitch.Services;

namespace VSCopilotSwitch;

internal sealed record DashboardSnapshot(
    HealthResponse Health,
    IReadOnlyList<DashboardProviderConfigView> Providers,
    DashboardTagsResponse Tags,
    IReadOnlyList<DashboardVsCodeUserDirectory> Directories);

internal sealed record DashboardProviderConfigView(
    string Id,
    string Name,
    string Remark,
    string Url,
    string ApiUrl,
    string Model,
    string Vendor,
    string Avatar,
    bool Active,
    bool HasApiKey,
    string? ApiKeyPreview,
    int SortOrder);

internal sealed record DashboardTagsResponse(IReadOnlyList<DashboardModelInfo> Models);

internal sealed record DashboardModelInfo(
    string Name,
    string Model,
    DateTimeOffset ModifiedAt,
    long Size,
    string Digest,
    DashboardModelDetails? Details);

internal sealed record DashboardModelDetails(
    [property: JsonPropertyName("parent_model")] string? ParentModel,
    string Family,
    [property: JsonPropertyName("parameter_size")] string ParameterSize,
    [property: JsonPropertyName("quantization_level")] string QuantizationLevel);

internal sealed record DashboardVsCodeUserDirectory(
    string Path,
    string Profile,
    bool Exists,
    string Description);

internal sealed record ProviderEditorState(
    string? Id,
    string Name,
    string Remark,
    string Url,
    string ApiUrl,
    string Model,
    string Vendor,
    string ApiKey,
    bool Active,
    bool IsNew)
{
    public static ProviderEditorState CreateNew()
        => new(
            null,
            string.Empty,
            string.Empty,
            "https://example.com",
            "https://api.example.com/v1",
            string.Empty,
            "openai-compatible",
            string.Empty,
            Active: true,
            IsNew: true);

    public SaveProviderConfigRequest ToSaveRequest()
        => new(
            Id,
            Name,
            Remark,
            Url,
            ApiUrl,
            Model,
            Vendor,
            string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey,
            Active);

    public TestProviderConnectionRequest ToTestRequest()
        => new(
            Id,
            Name,
            ApiUrl,
            Model,
            Vendor,
            string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey);
}

internal sealed record ProviderPreset(
    string Name,
    string Vendor,
    string ApiUrl,
    string Model,
    string Url,
    string CapabilitySummary,
    string Remark);

internal sealed record ProviderImportPreview(
    string Name,
    string Vendor,
    string ApiUrl,
    string Model,
    string Url,
    string Remark,
    bool HasApiKey)
{
    public ProviderEditorState ToEditorState()
        => new(
            null,
            Name,
            Remark,
            Url,
            ApiUrl,
            Model,
            Vendor,
            ApiKey: string.Empty,
            Active: true,
            IsNew: true);
}

internal sealed record ProviderImportDocument(
    int? Version,
    IReadOnlyList<ProviderImportItem>? Providers);

internal sealed record ProviderImportItem(
    string? Id,
    string? Name,
    string? Remark,
    string? Url,
    string? ApiUrl,
    string? Model,
    string? Vendor,
    string? Avatar,
    bool? Active,
    int? SortOrder,
    bool? HasApiKey,
    string? ApiKey,
    string? ApiKeyPreview,
    JsonElement? EncryptedApiKey);

internal enum VsCodePreviewKind
{
    None,
    Apply,
    Remove
}
