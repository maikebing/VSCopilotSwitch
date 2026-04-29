<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';

type HealthStatus = {
  name: string;
  status: string;
  mode: string;
};

type AboutInfo = {
  Title: string;
  Version: string;
  GitHubUrl: string;
  EnterpriseWeChatQrPath: string;
};

type ModelDetails = {
  parent_model?: string;
  family: string;
  parameter_size: string;
  quantization_level: string;
};

type ModelInfo = {
  name: string;
  model: string;
  modified_at: string;
  size: number;
  digest: string;
  details: ModelDetails;
};

type TagsResponse = {
  models: ModelInfo[];
};

type VsCodeUserDirectory = {
  Path: string;
  Profile: string;
  Exists: boolean;
  Description: string;
};

type FileChange = {
  FilePath: string;
  ExistedBefore: boolean;
  Changed: boolean;
  BackupPath: string | null;
  BeforeContent: string;
  AfterContent: string;
  FieldChanges: FieldChange[];
};

type FieldChange = {
  Path: string;
  BeforeValue: string;
  AfterValue: string;
  Changed: boolean;
};

type ApplyResult = {
  UserDirectory: string;
  DryRun: boolean;
  Changes: FileChange[];
};

type ConfigBackup = {
  FilePath: string;
  BackupPath: string;
  FileName: string;
  CreatedAt: string;
  SizeBytes: number;
};

type RestoreResult = {
  UserDirectory: string;
  FilePath: string;
  BackupPath: string;
  SafetyBackupPath: string | null;
  Restored: boolean;
};

type VsCodeOllamaConfigStatus = {
  UserDirectory: string;
  Enabled: boolean;
  SettingsManaged: boolean;
  ChatLanguageModelsManaged: boolean;
  Message: string;
};

type PortStatus = {
  Port: number;
  Available: boolean;
  Message: string;
};

type RecoveryAdvice = {
  title: string;
  steps: string[];
};

type ProviderCard = {
  id: string;
  name: string;
  remark: string;
  url: string;
  apiUrl: string;
  model: string;
  avatar: string;
  vendor: string;
  active: boolean;
  selected: boolean;
  hasApiKey: boolean;
  apiKeyPreview: string | null;
  sortOrder: number;
};

type ProviderConnectionTestStep = {
  Name: string;
  Label: string;
  Success: boolean;
  Message: string;
  ElapsedMilliseconds: number;
};

type ProviderConnectionTestResult = {
  Success: boolean;
  Message: string;
  ModelCount: number;
  SelectedModel: string | null;
  Models: string[];
  Steps: ProviderConnectionTestStep[];
};

type AnalyticsSummary = {
  TotalRequests: number;
  TotalTokens: number;
  InputTokens: number;
  OutputTokens: number;
  TotalCost: number;
  Currency: string;
  PricedRequests: number;
  UnpricedRequests: number;
  AverageLatencySeconds: number;
};

type AnalyticsListener = {
  Url: string;
  Port: number;
  Status: string;
};

type RequestLogEntry = {
  Timestamp: string;
  Method: string;
  Path: string;
  Model: string | null;
  StatusCode: number;
  DurationMilliseconds: number;
  InputTokens: number;
  OutputTokens: number;
  TotalTokens: number;
  UsageSource: string;
  Cost: number;
  Currency: string;
  CostSource: string;
  PricingRule: string | null;
  UserAgent: string;
  RequestHeaders: Record<string, string>;
  RequestBody: string | null;
  ResponseHeaders: Record<string, string>;
  ResponseBody: string | null;
};

type AnalyticsSnapshot = {
  Summary: AnalyticsSummary;
  Listener: AnalyticsListener;
  Requests: RequestLogEntry[];
};

type UpdateAssetInfo = {
  Name: string;
  DownloadUrl: string;
  SizeBytes: number;
  ContentType: string | null;
  Sha256: string | null;
};

type UpdateReleaseInfo = {
  SourceName: string;
  TagName: string;
  Version: string;
  Name: string;
  Body: string;
  PageUrl: string;
  PublishedAt: string | null;
  Prerelease: boolean;
  Asset: UpdateAssetInfo | null;
};

type UpdateSourceCheckResult = {
  SourceName: string;
  Success: boolean;
  Message: string;
  Release: UpdateReleaseInfo | null;
};

type UpdateCheckResult = {
  CurrentVersion: string;
  CheckedAt: string;
  UpdateAvailable: boolean;
  LatestRelease: UpdateReleaseInfo | null;
  Sources: UpdateSourceCheckResult[];
};

type UpdateDownloadResult = {
  Downloaded: boolean;
  Message: string;
  FilePath: string | null;
  SizeBytes: number;
  Release: UpdateReleaseInfo | null;
};

type OmniBridge = {
  invoke: (handler: string, data?: unknown) => Promise<unknown>;
};

const health = ref<HealthStatus | null>(null);
const models = ref<ModelInfo[]>([]);
const directories = ref<VsCodeUserDirectory[]>([]);
const selectedDirectory = ref('');
const preview = ref<ApplyResult | null>(null);
const applyResult = ref<ApplyResult | null>(null);
const backups = ref<ConfigBackup[]>([]);
const selectedBackupPath = ref('');
const restoreResult = ref<RestoreResult | null>(null);
const vscodeOllamaStatus = ref<VsCodeOllamaConfigStatus | null>(null);
const portStatus = ref<PortStatus | null>(null);
const analytics = ref<AnalyticsSnapshot | null>(null);
const updateCheck = ref<UpdateCheckResult | null>(null);
const updateDownload = ref<UpdateDownloadResult | null>(null);
const loading = ref(false);
const analyticsLoading = ref(false);
const updateChecking = ref(false);
const updateDownloading = ref(false);
const previewLoading = ref(false);
const applyLoading = ref(false);
const exportConfigLoading = ref(false);
const backupsLoading = ref(false);
const restoreLoading = ref(false);
const vscodeConfigSwitchLoading = ref(false);
const vscodeSetupPromptVisible = ref(false);
const portChecking = ref(false);
const modelsLoading = ref(false);
const providerTestLoadingId = ref('');
const providerDraftTestLoading = ref(false);
const expandedAnalyticsKey = ref('');
const applyConfirmationArmed = ref(false);
const restoreConfirmationArmed = ref(false);
const errorMessage = ref('');
const modelErrorMessage = ref('');
const providerConnectionResult = ref<ProviderConnectionTestResult | null>(null);
const modelsRefreshedAt = ref<string | null>(null);
const recoveryAdvice = ref<RecoveryAdvice | null>(null);
const currentView = ref<'list' | 'edit' | 'settings' | 'analytics'>('list');
const settingsTab = ref<'general' | 'updates' | 'backups' | 'about'>('general');
const aboutInfo = ref<AboutInfo | null>(null);
const showApiKey = ref(false);
const showAdvancedOptions = ref(false);
const isCreatingProvider = ref(false);
const editingProviderId = ref<string | null>(null);
const draggedProviderId = ref('');
const dragOverProviderId = ref('');

const providers = ref<ProviderCard[]>([]);

const protocolOptions = [
  { value: 'sub2api', label: 'sub2api 中转协议' },
  { value: 'openai-compatible', label: 'OpenAI-compatible' },
  { value: 'openai', label: 'OpenAI Official' },
  { value: 'deepseek', label: 'DeepSeek' },
  { value: 'claude', label: 'Claude Official' },
  { value: 'nvidia-nim', label: 'NVIDIA NIM' },
  { value: 'moark', label: 'MoArk' }
];

const providerName = ref('无极限超大杯');
const providerRemark = ref('');
const providerWebsite = ref('https://2030.wujixian.fun');
const providerApiKey = ref('sk-demo-placeholder-c087');
const providerApiUrl = ref('https://2030.wujixian.fun');
const providerModel = ref('gpt-5.5');
const providerProtocol = ref('sub2api');
const proxyAddress = ref('http://127.0.0.1:5124');
const circuitBreakerThreshold = ref(5);
const retryCount = ref(2);
const fallbackRoute = ref('暂不启用备用路由');
const authJson = computed(
  () => `{
  "OPENAI_API_KEY": "${providerApiKey.value}"
}`
);

const existingDirectories = computed(() => directories.value.filter((directory) => directory.Exists));
const selectedDirectoryDescription = computed(() => {
  const directory = directories.value.find((item) => item.Path === selectedDirectory.value);
  return directory?.Description ?? '请选择一个 VS Code User 配置目录。';
});
const healthState = computed(() => health.value?.status ?? 'unknown');
const activeModel = computed(() => displayModelName(models.value[0]) || providerModel.value);
const activeProvider = computed(() => providers.value.find((provider) => provider.active));
const modelRefreshText = computed(() => {
  if (modelsLoading.value) {
    return '刷新中';
  }

  return models.value.length > 0 ? `${models.value.length} 个模型` : '未获取模型';
});
const modelRefreshTimeText = computed(() => {
  if (!modelsRefreshedAt.value) {
    return '尚未刷新';
  }

  return new Date(modelsRefreshedAt.value).toLocaleTimeString('zh-CN', {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit'
  });
});
const usingFallbackModelProvider = computed(() =>
  models.value.some((model) => model.details?.family === 'vscopilotswitch' || model.name.startsWith('vscopilotswitch/'))
);
const providerCountText = computed(() => `${providers.value.length} 个供应商 · ${existingDirectories.value.length} 个 VS Code 配置目录`);
const updateStatusText = computed(() => {
  if (!updateCheck.value) {
    return '尚未检查';
  }

  return updateCheck.value.UpdateAvailable
    ? `发现 ${updateCheck.value.LatestRelease?.Version ?? '新版'}`
    : `当前 ${updateCheck.value.CurrentVersion} 已是最新`;
});
const visibleProviderRows = computed(() => Math.min(providers.value.length, 5));
const providerListMaxHeight = computed(() => {
  const cardHeight = 128;
  const rowGap = 18;
  return `${visibleProviderRows.value * cardHeight + Math.max(0, visibleProviderRows.value - 1) * rowGap}px`;
});
const analyticsSummary = computed(() => analytics.value?.Summary ?? {
  TotalRequests: 0,
  TotalTokens: 0,
  InputTokens: 0,
  OutputTokens: 0,
  TotalCost: 0,
  Currency: 'USD',
  PricedRequests: 0,
  UnpricedRequests: 0,
  AverageLatencySeconds: 0
});
const analyticsRequests = computed(() => analytics.value?.Requests ?? []);
const previewChangedCount = computed(() => preview.value?.Changes.filter((change) => change.Changed).length ?? 0);
const canApplyVsCodeConfig = computed(() => Boolean(preview.value?.DryRun && selectedDirectory.value && !applyResult.value));
const selectedBackup = computed(() => backups.value.find((backup) => backup.BackupPath === selectedBackupPath.value));
const applyConfirmText = computed(() => (applyConfirmationArmed.value ? '已预览风险，再次点击写入' : '确认写入 VS Code Ollama 配置'));
const restoreConfirmText = computed(() => (restoreConfirmationArmed.value ? '已确认风险，再次点击恢复' : '恢复选中备份'));
const vscodeConfigSwitchText = computed(() => {
  if (vscodeConfigSwitchLoading.value) {
    return '检查中';
  }

  return vscodeOllamaStatus.value?.Enabled ? '已开启' : '未开启';
});
const vscodeConfigSwitchTitle = computed(() => vscodeOllamaStatus.value?.Message ?? '检查 VS Code Ollama 配置状态');
const editTitle = computed(() => (isCreatingProvider.value ? '新增供应商' : '编辑供应商'));
const editDescription = computed(() =>
  isCreatingProvider.value
    ? '请填写新供应商信息；保存前不会写入 VS Code 配置。'
    : '保存前仅在界面中预览，后续写入配置需经过差异预览和确认。'
);

function clearError() {
  errorMessage.value = '';
  recoveryAdvice.value = null;
}

function displayModelName(model?: ModelInfo) {
  if (!model) {
    return '';
  }

  const name = model.name || model.model || '';
  const separatorIndex = name.indexOf('/');
  return separatorIndex >= 0 ? name.slice(separatorIndex + 1) : name;
}

function formatNumber(value: number) {
  return new Intl.NumberFormat('zh-CN').format(value);
}

function formatCompactNumber(value: number) {
  return new Intl.NumberFormat('zh-CN', {
    notation: 'compact',
    maximumFractionDigits: 2
  }).format(value);
}

function formatCost(value: number, currency = analyticsSummary.value.Currency) {
  const normalized = currency || 'USD';
  const prefix = normalized === 'USD' ? '$' : normalized === 'CNY' ? '¥' : `${normalized} `;
  return `${prefix}${value.toFixed(6)}`;
}

function formatUsageSource(source: string) {
  return source === 'provider' ? '实际' : '估算';
}

function formatCostSource(entry: RequestLogEntry) {
  if (entry.CostSource === 'configured') {
    return entry.PricingRule ? `单价：${entry.PricingRule}` : '已配置单价';
  }

  return '未配置单价';
}

function formatSeconds(milliseconds: number) {
  return `${(milliseconds / 1000).toFixed(2)}s`;
}

function formatAnalyticsTime(value: string) {
  return new Date(value).toLocaleString('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit'
  });
}

function analyticsEntryKey(entry: RequestLogEntry) {
  return `${entry.Timestamp}-${entry.Method}-${entry.Path}-${entry.DurationMilliseconds}-${entry.StatusCode}`;
}

function toggleAnalyticsEntry(entry: RequestLogEntry) {
  const key = analyticsEntryKey(entry);
  expandedAnalyticsKey.value = expandedAnalyticsKey.value === key ? '' : key;
}

function formatHeaders(headers: Record<string, string> | null | undefined) {
  const entries = Object.entries(headers ?? {});
  if (entries.length === 0) {
    return '无';
  }

  return entries
    .sort(([left], [right]) => left.localeCompare(right))
    .map(([key, value]) => `${key}: ${value}`)
    .join('\n');
}

function formatBody(body: string | null | undefined) {
  return body?.trim() ? body : '无';
}

function clearProviderConnectionResult() {
  providerConnectionResult.value = null;
}

function setError(message: string, advice?: RecoveryAdvice) {
  errorMessage.value = message;
  recoveryAdvice.value = advice ?? buildRecoveryAdvice(message);
}

function messageFromError(error: unknown, fallback: string) {
  return error instanceof Error ? error.message : fallback;
}

async function readPublicError(response: Response, fallback: string) {
  try {
    const payload = (await response.json()) as Record<string, unknown>;
    const message = payload.error ?? payload.Error;
    return typeof message === 'string' && message.trim() ? message : fallback;
  } catch {
    return fallback;
  }
}

function buildRecoveryAdvice(message: string): RecoveryAdvice {
  const normalized = message.toLowerCase();

  if (normalized.includes('json')) {
    return {
      title: 'JSON 文件格式异常',
      steps: [
        '先不要写入配置，手动打开对应的 chatLanguageModels.json。',
        '检查是否存在多余逗号、未闭合引号或注释；当前阶段暂不支持 JSON with comments。',
        '修复后重新点击“生成差异预览”。'
      ]
    };
  }

  if (normalized.includes('权限') || normalized.includes('access') || normalized.includes('unauthorized')) {
    return {
      title: '目录或文件权限不足',
      steps: [
        '确认 VS Code User 目录属于当前 Windows 用户。',
        '检查文件是否为只读，或被安全软件拦截写入。',
        '必要时关闭 VS Code 后重试，避免配置文件被占用。'
      ]
    };
  }

  if (normalized.includes('占用') || normalized.includes('being used') || normalized.includes('locked')) {
    return {
      title: '配置文件可能被占用',
      steps: [
        '关闭正在编辑 chatLanguageModels.json 的窗口。',
        '退出 VS Code 后重新打开 VSCopilotSwitch 再试一次。',
        '如果仍失败，先复制备份路径，手动恢复配置。'
      ]
    };
  }

  if ((normalized.includes('端口') || normalized.includes('port') || normalized.includes('listen'))
    && !normalized.includes('端口号')) {
    return {
      title: '本地端口不可用',
      steps: [
        '确认 127.0.0.1 本地代理没有被防火墙拦截。',
        '如果手动指定了 5124，请确认没有其他 VSCopilotSwitch 或代理进程占用该端口。',
        '可在高级选项中使用“检测端口占用”确认目标端口状态。'
      ]
    };
  }

  return {
    title: '可以尝试的修复步骤',
    steps: [
      '确认本地代理窗口仍在运行。',
      '点击刷新后重新选择 Windows VS Code User 目录。',
      '如果涉及配置写入，先生成 dry-run 预览再执行确认操作。'
    ]
  };
}

async function loadDashboard() {
  loading.value = true;
  clearError();

  try {
    const [healthResponse, directoriesResponse, providersResponse, aboutResponse] = await Promise.all([
      fetch('/health'),
      fetch('/internal/vscode/user-directories'),
      fetch('/internal/providers'),
      fetch('/internal/about')
    ]);

    if (!healthResponse.ok || !directoriesResponse.ok || !providersResponse.ok || !aboutResponse.ok) {
      throw new Error('后端 API 返回异常，请确认本地代理已经启动。');
    }

    health.value = await healthResponse.json();
    directories.value = await directoriesResponse.json();
    providers.value = mapProviderViews(await providersResponse.json());
    aboutInfo.value = await aboutResponse.json();
    selectedDirectory.value = existingDirectories.value[0]?.Path ?? directories.value[0]?.Path ?? '';
    preview.value = null;
    applyResult.value = null;
    backups.value = [];
    selectedBackupPath.value = '';
    restoreResult.value = null;
    await refreshModels(false);
    await refreshVsCodeOllamaStatus(false);
  } catch (error) {
    setError(messageFromError(error, '加载状态失败。'));
  } finally {
    loading.value = false;
  }
}

async function refreshModels(showGlobalError = true) {
  modelsLoading.value = true;
  modelErrorMessage.value = '';

  try {
    const response = await fetch('/api/tags');
    if (!response.ok) {
      throw new Error(await readPublicError(response, '模型列表刷新失败，请检查当前供应商 API 地址、API Key 和模型权限。'));
    }

    const tagResult = (await response.json()) as TagsResponse;
    models.value = Array.isArray(tagResult.models) ? tagResult.models : [];
    modelsRefreshedAt.value = new Date().toISOString();
  } catch (error) {
    const message = messageFromError(error, '模型列表刷新失败。');
    modelErrorMessage.value = message;
    models.value = [];
    if (showGlobalError) {
      setError(message);
    }
  } finally {
    modelsLoading.value = false;
  }
}

async function loadAnalytics(showGlobalError = true) {
  analyticsLoading.value = true;
  if (showGlobalError) {
    clearError();
  }

  try {
    const response = await fetch('/internal/analytics');
    if (!response.ok) {
      throw new Error(await readPublicError(response, '读取分析统计失败，请确认本地代理仍在运行。'));
    }

    analytics.value = (await response.json()) as AnalyticsSnapshot;
  } catch (error) {
    if (showGlobalError) {
      setError(messageFromError(error, '读取分析统计失败。'));
    }
  } finally {
    analyticsLoading.value = false;
  }
}

async function clearAnalytics() {
  analyticsLoading.value = true;
  clearError();

  try {
    const response = await fetch('/internal/analytics/clear', {
      method: 'POST'
    });
    if (!response.ok) {
      throw new Error(await readPublicError(response, '清空分析统计失败。'));
    }

    analytics.value = (await response.json()) as AnalyticsSnapshot;
  } catch (error) {
    setError(messageFromError(error, '清空分析统计失败。'));
  } finally {
    analyticsLoading.value = false;
  }
}

async function checkUpdates(reportError = true) {
  updateChecking.value = true;
  if (reportError) {
    clearError();
  }

  try {
    const response = await fetch('/internal/updates/check');
    if (!response.ok) {
      throw new Error(await readPublicError(response, '检查更新失败，请稍后重试。'));
    }

    updateCheck.value = (await response.json()) as UpdateCheckResult;
    updateDownload.value = null;
  } catch (error) {
    if (reportError) {
      setError(messageFromError(error, '检查更新失败。'));
    }
  } finally {
    updateChecking.value = false;
  }
}

async function downloadLatestUpdate() {
  updateDownloading.value = true;
  clearError();

  try {
    const response = await fetch('/internal/updates/download-latest', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({})
    });
    if (!response.ok) {
      throw new Error(await readPublicError(response, '下载更新失败，请确认发布包可访问。'));
    }

    updateDownload.value = (await response.json()) as UpdateDownloadResult;
    await checkUpdates(false);
  } catch (error) {
    setError(messageFromError(error, '下载更新失败。'));
  } finally {
    updateDownloading.value = false;
  }
}

async function exportProviderConfig() {
  exportConfigLoading.value = true;
  clearError();

  try {
    const response = await fetch('/internal/providers/export');
    if (!response.ok) {
      throw new Error(await readPublicError(response, '导出供应商配置失败。'));
    }

    const payload = await response.json();
    const content = `${JSON.stringify(payload, null, 2)}\n`;
    const blob = new Blob([content], { type: 'application/json;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    const timestamp = new Date().toISOString().replace(/[:.]/g, '-');
    link.href = url;
    link.download = `vscopilotswitch-providers-${timestamp}.json`;
    link.click();
    URL.revokeObjectURL(url);
  } catch (error) {
    setError(messageFromError(error, '导出供应商配置失败。'));
  } finally {
    exportConfigLoading.value = false;
  }
}

function mapProviderViews(items: unknown): ProviderCard[] {
  if (!Array.isArray(items)) {
    return [];
  }

  return items.map((item) => {
    const provider = item as Record<string, unknown>;
    const vendor = normalizeProtocol(String(provider.Vendor ?? ''));
    const active = provider.Active === true;
    return {
      id: String(provider.Id ?? ''),
      name: String(provider.Name ?? ''),
      remark: String(provider.Remark ?? ''),
      url: String(provider.Url ?? ''),
      apiUrl: String(provider.ApiUrl ?? ''),
      model: String(provider.Model ?? ''),
      avatar: String(provider.Avatar ?? '?'),
      vendor,
      active,
      selected: active,
      hasApiKey: provider.HasApiKey === true,
      apiKeyPreview: provider.ApiKeyPreview ? String(provider.ApiKeyPreview) : null,
      sortOrder: Number(provider.SortOrder ?? 0)
    };
  });
}

function normalizeProtocol(value: string) {
  const normalized = value.trim().toLowerCase();
  if (!normalized || normalized === 'codex') {
    return 'openai-compatible';
  }

  if (normalized === 'nvidia') {
    return 'nvidia-nim';
  }

  return normalized;
}

function protocolLabel(value: string) {
  const normalized = normalizeProtocol(value);
  return protocolOptions.find((option) => option.value === normalized)?.label ?? normalized;
}

async function previewVsCodeConfig() {
  if (!selectedDirectory.value) {
    setError('没有可用的 VS Code User 配置目录，无法生成预览。');
    return;
  }

  previewLoading.value = true;
  clearError();
  vscodeSetupPromptVisible.value = false;
  preview.value = null;
  applyResult.value = null;

  try {
    const response = await fetch('/internal/vscode/apply-ollama', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        UserDirectory: selectedDirectory.value,
        Config: null,
        DryRun: true
      })
    });

    if (!response.ok) {
      throw new Error(await readPublicError(response, 'VS Code 配置预览失败，请检查目录权限和 JSON 文件格式。'));
    }

    preview.value = await response.json();
  } catch (error) {
    setError(messageFromError(error, 'VS Code 配置预览失败。'));
  } finally {
    previewLoading.value = false;
  }
}

async function applyVsCodeConfig() {
  if (!selectedDirectory.value || !preview.value) {
    setError('请先生成差异预览，再确认写入 VS Code 配置。');
    return;
  }

  if (!applyConfirmationArmed.value) {
    applyConfirmationArmed.value = true;
    clearError();
    return;
  }

  applyLoading.value = true;
  clearError();
  applyResult.value = null;

  try {
    const response = await fetch('/internal/vscode/apply-ollama', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        UserDirectory: selectedDirectory.value,
        Config: null,
        DryRun: false
      })
    });

    if (!response.ok) {
      throw new Error(await readPublicError(response, 'VS Code 配置写入失败，原文件已保留，请检查权限或文件占用。'));
    }

    applyResult.value = await response.json();
    preview.value = applyResult.value;
    applyConfirmationArmed.value = false;
    vscodeSetupPromptVisible.value = false;
    await refreshVsCodeOllamaStatus(false);
    await loadBackups();
  } catch (error) {
    setError(messageFromError(error, 'VS Code 配置写入失败。'));
  } finally {
    applyLoading.value = false;
  }
}

async function refreshVsCodeOllamaStatus(reportError = true) {
  if (!selectedDirectory.value) {
    vscodeOllamaStatus.value = null;
    return;
  }

  try {
    const response = await fetch('/internal/vscode/ollama-status', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        UserDirectory: selectedDirectory.value
      })
    });

    if (!response.ok) {
      throw new Error(await readPublicError(response, 'VS Code Ollama 配置状态检查失败，请检查目录权限。'));
    }

    vscodeOllamaStatus.value = await response.json();
    if (vscodeOllamaStatus.value?.Enabled) {
      vscodeSetupPromptVisible.value = false;
    }
  } catch (error) {
    vscodeOllamaStatus.value = null;
    if (reportError) {
      setError(messageFromError(error, 'VS Code Ollama 配置状态检查失败。'));
    }
  }
}

async function removeVsCodeOllamaConfig() {
  if (!selectedDirectory.value) {
    setError('没有可用的 VS Code User 配置目录，无法撤销配置。');
    return;
  }

  clearError();

  try {
    const response = await fetch('/internal/vscode/remove-ollama', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        UserDirectory: selectedDirectory.value,
        DryRun: false
      })
    });

    if (!response.ok) {
      throw new Error(await readPublicError(response, '撤销 VS Code Ollama 配置失败，原文件已保留，请检查权限或文件占用。'));
    }

    applyResult.value = await response.json();
    preview.value = applyResult.value;
    applyConfirmationArmed.value = false;
    await refreshVsCodeOllamaStatus(false);
    await loadBackups();
  } catch (error) {
    setError(messageFromError(error, '撤销 VS Code Ollama 配置失败。'));
  }
}

async function handleVsCodeConfigSwitchChanged(event: Event) {
  const checked = (event.target as HTMLInputElement).checked;
  vscodeConfigSwitchLoading.value = true;
  clearError();

  try {
    if (checked) {
      await refreshVsCodeOllamaStatus(true);
      if (!vscodeOllamaStatus.value?.Enabled) {
        openVsCodeConfigWizardFromSwitch();
      }
      return;
    }

    await removeVsCodeOllamaConfig();
  } finally {
    vscodeConfigSwitchLoading.value = false;
  }
}

function openVsCodeConfigWizardFromSwitch() {
  settingsTab.value = 'general';
  currentView.value = 'settings';
  vscodeSetupPromptVisible.value = true;
  applyConfirmationArmed.value = false;
  setError('未检测到完整的 VS Code Ollama 配置。已为你打开写入向导，请先生成差异预览，再二次确认写入。', {
    title: '需要明确确认后才会写入',
    steps: [
      '点击“生成差异预览”查看将修改的 chatLanguageModels.json 条目。',
      '确认只会维护 vscs Ollama Provider 后，再点击“确认写入 VS Code Ollama 配置”。',
      '写入前会自动创建备份；未确认前不会修改 VS Code 配置。'
    ]
  });
}

async function loadBackups() {
  if (!selectedDirectory.value) {
    setError('请先选择 VS Code User 配置目录，再查看备份。');
    return;
  }

  backupsLoading.value = true;
  clearError();

  try {
    const response = await fetch('/internal/vscode/backups', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        UserDirectory: selectedDirectory.value
      })
    });

    if (!response.ok) {
      throw new Error('读取 VS Code 配置备份失败，请检查目录权限。');
    }

    backups.value = await response.json();
    selectedBackupPath.value = backups.value[0]?.BackupPath ?? '';
  } catch (error) {
    setError(messageFromError(error, '读取 VS Code 配置备份失败。'));
  } finally {
    backupsLoading.value = false;
  }
}

async function restoreBackup() {
  if (!selectedDirectory.value || !selectedBackupPath.value) {
    setError('请选择一个可恢复的备份。');
    return;
  }

  if (!restoreConfirmationArmed.value) {
    restoreConfirmationArmed.value = true;
    clearError();
    return;
  }

  restoreLoading.value = true;
  clearError();
  restoreResult.value = null;

  try {
    const response = await fetch('/internal/vscode/restore-backup', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        UserDirectory: selectedDirectory.value,
        BackupPath: selectedBackupPath.value
      })
    });

    if (!response.ok) {
      throw new Error('恢复备份失败，当前文件已保留，请确认备份属于所选目录。');
    }

    const result = (await response.json()) as RestoreResult;
    resetVsCodeWizard();
    restoreResult.value = result;
    restoreConfirmationArmed.value = false;
    await loadBackups();
  } catch (error) {
    setError(messageFromError(error, '恢复备份失败。'));
  } finally {
    restoreLoading.value = false;
  }
}

async function checkProxyPort() {
  const parsedPort = parseProxyPort(proxyAddress.value);

  portChecking.value = true;
  clearError();
  portStatus.value = null;

  try {
    if (!parsedPort.valid) {
      portStatus.value = {
        Port: 0,
        Available: false,
        Message: parsedPort.message
      };
      return;
    }

    const port = parsedPort.port;
    const response = await fetch(`/internal/network/port-status?port=${encodeURIComponent(port)}`);
    if (!response.ok) {
      const message = await readPublicError(response, '端口检测失败，请确认端口号在 1 到 65535 之间。');
      portStatus.value = {
        Port: port,
        Available: false,
        Message: message
      };
      return;
    }

    portStatus.value = await response.json();
  } catch (error) {
    setError(messageFromError(error, '端口检测失败。'));
  } finally {
    portChecking.value = false;
  }
}

function parseProxyPort(value: string): { valid: true; port: number } | { valid: false; message: string } {
  const raw = value.trim();
  if (!raw) {
    return { valid: false, message: '请输入本地代理端口，例如 5124 或 http://127.0.0.1:5124。' };
  }

  if (/^\d+$/.test(raw)) {
    return normalizePort(Number(raw));
  }

  const withScheme = /^[a-z][a-z\d+.-]*:\/\//i.test(raw) ? raw : `http://${raw}`;
  try {
    const parsedUrl = new URL(withScheme);
    if (!parsedUrl.hostname) {
      return { valid: false, message: '本地代理地址缺少主机名，请填写 127.0.0.1:5124。' };
    }

    if (!parsedUrl.port) {
      return { valid: false, message: '本地代理地址缺少端口，请填写 127.0.0.1:5124。' };
    }

    return normalizePort(Number(parsedUrl.port));
  } catch {
    return { valid: false, message: '本地代理地址格式无效，请填写 5124、127.0.0.1:5124 或 http://127.0.0.1:5124。' };
  }
}

function normalizePort(port: number): { valid: true; port: number } | { valid: false; message: string } {
  if (!Number.isInteger(port) || port < 1 || port > 65535) {
    return { valid: false, message: '端口号必须是 1 到 65535 之间的整数。' };
  }

  if (port === 11434) {
    return { valid: false, message: '11434 是 Ollama 默认端口，VSCopilotSwitch 请使用 5124 或其他非 11434 端口。' };
  }

  return { valid: true, port };
}

function resetVsCodeWizard() {
  preview.value = null;
  applyResult.value = null;
  restoreResult.value = null;
  vscodeSetupPromptVisible.value = false;
  applyConfirmationArmed.value = false;
  clearError();
}

function resetProviderForm() {
  editingProviderId.value = null;
  clearProviderConnectionResult();
  providerName.value = '';
  providerRemark.value = '';
  providerWebsite.value = '';
  providerApiKey.value = '';
  providerApiUrl.value = '';
  providerModel.value = '';
  providerProtocol.value = 'sub2api';
  showApiKey.value = false;
  showAdvancedOptions.value = false;
  portStatus.value = null;
}

function openCreateProvider() {
  resetVsCodeWizard();
  resetProviderForm();
  isCreatingProvider.value = true;
  currentView.value = 'edit';
}

function openEdit(provider?: ProviderCard) {
  resetVsCodeWizard();
  clearProviderConnectionResult();

  if (provider) {
    isCreatingProvider.value = false;
    editingProviderId.value = provider.id;
    providerName.value = provider.name;
    providerWebsite.value = provider.url;
    providerApiUrl.value = provider.apiUrl;
    providerRemark.value = provider.remark;
    providerApiKey.value = '';
    providerModel.value = provider.model;
    providerProtocol.value = normalizeProtocol(provider.vendor);
  } else {
    isCreatingProvider.value = true;
    resetProviderForm();
  }

  currentView.value = 'edit';
}

async function saveProvider() {
  clearError();
  clearProviderConnectionResult();

  try {
    ensureProviderDefaults();
    if (!providerModel.value.trim() && providerApiUrl.value.trim()) {
      await testProviderConnection();
    }

    if (!providerModel.value.trim()) {
      throw new Error('请先点击“测试连接”获取远程模型列表，或手动填写模型名称。');
    }

    const response = await fetch('/internal/providers', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        Id: editingProviderId.value,
        Name: providerName.value,
        Remark: providerRemark.value,
        Url: providerWebsite.value,
        ApiUrl: providerApiUrl.value,
        Model: providerModel.value,
        Vendor: providerProtocol.value,
        ApiKey: providerApiKey.value || null,
        Active: providers.value.length === 0 || providers.value.some((provider) => provider.id === editingProviderId.value && provider.active)
      })
    });

    if (!response.ok) {
      throw new Error(await readPublicError(response, '保存供应商失败，请检查名称、官网链接和 API 地址。'));
    }

    providers.value = mapProviderViews(await response.json());
    await refreshModels(false);
    openList();
  } catch (error) {
    setError(messageFromError(error, '保存供应商失败。'));
  }
}

function ensureProviderDefaults() {
  const apiUrl = providerApiUrl.value.trim();
  if (!apiUrl) {
    return;
  }

  if (!providerWebsite.value.trim()) {
    providerWebsite.value = apiUrl;
  }

  if (!providerName.value.trim()) {
    providerName.value = providerNameFromUrl(apiUrl);
  }
}

function providerNameFromUrl(value: string) {
  try {
    const parsed = new URL(value);
    return parsed.hostname.replace(/^www\./, '') || '自定义供应商';
  } catch {
    return '自定义供应商';
  }
}

function buildProviderConnectionRequest(provider?: ProviderCard) {
  if (provider) {
    return {
      Id: provider.id,
      Name: provider.name,
      ApiUrl: provider.apiUrl,
      Model: provider.model,
      Vendor: provider.vendor,
      ApiKey: null
    };
  }

  return {
    Id: editingProviderId.value,
    Name: providerName.value,
    ApiUrl: providerApiUrl.value,
    Model: providerModel.value,
    Vendor: providerProtocol.value,
    ApiKey: providerApiKey.value || null
  };
}

async function testProviderConnection(provider?: ProviderCard) {
  clearError();
  clearProviderConnectionResult();

  if (provider) {
    providerTestLoadingId.value = provider.id;
  } else {
    providerDraftTestLoading.value = true;
  }

  try {
    const response = await fetch('/internal/providers/test-connection', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify(buildProviderConnectionRequest(provider))
    });

    if (!response.ok) {
      throw new Error(await readPublicError(response, '测试连接失败，请检查 API 地址、API Key 和模型名称。'));
    }

    providerConnectionResult.value = (await response.json()) as ProviderConnectionTestResult;
    if (!provider && providerConnectionResult.value.SelectedModel) {
      providerModel.value = providerConnectionResult.value.SelectedModel;
    }
  } catch (error) {
    setError(messageFromError(error, '测试连接失败。'));
  } finally {
    if (provider) {
      providerTestLoadingId.value = '';
    } else {
      providerDraftTestLoading.value = false;
    }
  }
}

function openList() {
  currentView.value = 'list';
}

function openSettings() {
  settingsTab.value = 'general';
  currentView.value = 'settings';
  vscodeSetupPromptVisible.value = false;
}

async function openUpdates() {
  settingsTab.value = 'updates';
  currentView.value = 'settings';
  vscodeSetupPromptVisible.value = false;
  await checkUpdates(false);
}

async function openAnalytics() {
  currentView.value = 'analytics';
  await loadAnalytics(false);
}

async function activateProvider(providerId: string) {
  clearError();

  try {
    const response = await fetch(`/internal/providers/${encodeURIComponent(providerId)}/activate`, {
      method: 'POST'
    });

    if (!response.ok) {
      throw new Error('启用供应商失败，请刷新后重试。');
    }

    providers.value = mapProviderViews(await response.json());
    await refreshModels(false);
  } catch (error) {
    setError(messageFromError(error, '启用供应商失败。'));
  }
}

async function openExternalUrl(url: string) {
  try {
    const omni = (globalThis as { omni?: OmniBridge }).omni;
    if (omni) {
      await omni.invoke('host.openExternal', { Url: url });
      return;
    }
  } catch (error) {
    setError(messageFromError(error, '打开供应商链接失败。'));
    return;
  }

  window.open(url, '_blank', 'noopener,noreferrer');
}

function beginProviderDrag(providerId: string, event: DragEvent) {
  draggedProviderId.value = providerId;
  dragOverProviderId.value = providerId;
  event.dataTransfer?.setData('text/plain', providerId);
  if (event.dataTransfer) {
    event.dataTransfer.effectAllowed = 'move';
  }
}

function moveProviderNear(targetProviderId: string, event: DragEvent) {
  const sourceProviderId = draggedProviderId.value;
  if (!sourceProviderId || sourceProviderId === targetProviderId) {
    return;
  }

  const nextProviders = [...providers.value];
  const sourceIndex = nextProviders.findIndex((provider) => provider.id === sourceProviderId);
  const targetIndex = nextProviders.findIndex((provider) => provider.id === targetProviderId);
  if (sourceIndex < 0 || targetIndex < 0) {
    return;
  }

  const targetElement = event.currentTarget as HTMLElement | null;
  const targetRect = targetElement?.getBoundingClientRect();
  const insertAfterTarget = targetRect ? event.clientY > targetRect.top + targetRect.height / 2 : false;

  const movedProvider = nextProviders[sourceIndex];
  if (!movedProvider) {
    return;
  }

  nextProviders.splice(sourceIndex, 1);
  let insertionIndex = targetIndex + (insertAfterTarget ? 1 : 0);
  if (sourceIndex < insertionIndex) {
    insertionIndex -= 1;
  }

  nextProviders.splice(insertionIndex, 0, movedProvider);
  providers.value = nextProviders;
  dragOverProviderId.value = targetProviderId;
}

async function finishProviderDrag() {
  const shouldPersist = Boolean(draggedProviderId.value);
  draggedProviderId.value = '';
  dragOverProviderId.value = '';

  if (!shouldPersist) {
    return;
  }

  try {
    const response = await fetch('/internal/providers/reorder', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        ProviderIds: providers.value.map((provider) => provider.id)
      })
    });

    if (!response.ok) {
      throw new Error('保存供应商排序失败，请刷新后重试。');
    }

    providers.value = mapProviderViews(await response.json());
  } catch (error) {
    setError(messageFromError(error, '保存供应商排序失败。'));
  }
}

async function deleteProvider(providerId: string) {
  const provider = providers.value.find((item) => item.id === providerId);
  if (!provider || !window.confirm(`删除供应商“${provider.name}”？`)) {
    return;
  }

  clearError();

  try {
    const response = await fetch(`/internal/providers/${encodeURIComponent(providerId)}`, {
      method: 'DELETE'
    });

    if (!response.ok) {
      throw new Error('删除供应商失败，请刷新后重试。');
    }

    providers.value = mapProviderViews(await response.json());
    await refreshModels(false);
  } catch (error) {
    setError(messageFromError(error, '删除供应商失败。'));
  }
}

function handleDirectoryChanged() {
  resetVsCodeWizard();
  backups.value = [];
  selectedBackupPath.value = '';
  restoreConfirmationArmed.value = false;
  void refreshVsCodeOllamaStatus(false);
}

function handleBackupChanged() {
  restoreConfirmationArmed.value = false;
  restoreResult.value = null;
}

onMounted(loadDashboard);
</script>

<template>
  <div class="cc-shell">
    <header class="app-bar">
      <section class="brand-area" aria-label="应用状态">
        <img class="brand-mark" src="./assets/logo.svg" alt="VSCopilotSwitch logo" />
        <div>
          <h1>VSCopilotSwitch</h1>
          <p>{{ providerCountText }}</p>
        </div>
        <button class="icon-button" type="button" title="设置" @click="openSettings">⚙</button>
        <button class="icon-button ghost" type="button" title="统计">▥</button>
      </section>

      <section class="quick-switch" aria-label="快速切换">
        <button class="pill active" type="button"><span class="ide-icon vs-icon">VS</span> VS2026</button>
        <button class="pill selected" type="button"><span class="ide-icon vscode-icon">⌁</span> VSCode</button>
      </section>

      <label class="top-config-switch" :title="vscodeConfigSwitchTitle">
        <input
          type="checkbox"
          :checked="vscodeOllamaStatus?.Enabled === true"
          :disabled="vscodeConfigSwitchLoading || !selectedDirectory"
          @change="handleVsCodeConfigSwitchChanged"
        />
        <span aria-hidden="true"></span>
        <strong>VS Code Ollama</strong>
        <small>{{ vscodeConfigSwitchText }}</small>
      </label>

      <section class="toolbar" aria-label="工具栏">
        <button class="icon-button" type="button" title="代理设置" @click="openSettings">🔧</button>
        <button class="icon-button" type="button" title="配置预览" @click="openSettings">▣</button>
        <button class="icon-button" type="button" title="检查更新" @click="openUpdates">⇧</button>
        <button class="icon-button" type="button" title="分析统计" @click="openAnalytics">▤</button>
        <button class="icon-button" type="button" :disabled="loading" title="刷新" @click="loadDashboard">↻</button>
        <button class="icon-button" type="button" title="附件">⌁</button>
        <button class="add-button" type="button" title="添加供应商" @click="openCreateProvider">＋</button>
      </section>
    </header>

    <main class="page-surface" :class="currentView === 'list' ? 'list-surface' : 'edit-surface'">
      <p v-if="errorMessage" class="notice error">{{ errorMessage }}</p>
      <div
        v-if="providerConnectionResult"
        class="connection-result"
        :class="{ success: providerConnectionResult.Success, failed: !providerConnectionResult.Success }"
        aria-label="供应商连接测试结果"
      >
        <strong>{{ providerConnectionResult.Message }}</strong>
        <span v-if="providerConnectionResult.SelectedModel">
          探测模型：{{ providerConnectionResult.SelectedModel }} · 模型数：{{ providerConnectionResult.ModelCount }}
        </span>
        <ol>
          <li
            v-for="step in providerConnectionResult.Steps"
            :key="step.Name"
            :class="{ done: step.Success }"
          >
            <b>{{ step.Success ? '✓' : '!' }}</b>
            <span>{{ step.Label }}</span>
            <small>{{ step.Message }}</small>
          </li>
        </ol>
      </div>
      <section v-if="recoveryAdvice" class="recovery-advice" aria-label="失败修复建议">
        <strong>{{ recoveryAdvice.title }}</strong>
        <ol>
          <li v-for="step in recoveryAdvice.steps" :key="step">{{ step }}</li>
        </ol>
      </section>

      <section v-if="currentView === 'list'" class="provider-page">
        <div class="status-strip">
          <article>
            <span>当前供应商</span>
            <strong>{{ activeProvider?.name ?? '未选择' }}</strong>
          </article>
          <article>
            <span>本地代理</span>
            <strong>{{ healthState }}</strong>
          </article>
          <article>
            <span>当前模型</span>
            <strong>{{ activeModel }}</strong>
          </article>
          <article>
            <span>VS Code 配置</span>
            <strong>{{ selectedDirectoryDescription }}</strong>
          </article>
        </div>

        <section class="model-panel" aria-label="当前供应商模型列表">
          <div class="model-panel-head">
            <div>
              <span>模型列表</span>
              <strong>{{ modelRefreshText }}</strong>
              <small>上次刷新：{{ modelRefreshTimeText }}</small>
            </div>
            <div class="model-panel-actions">
              <button class="secondary-button compact" type="button" :disabled="exportConfigLoading" @click="exportProviderConfig">
                {{ exportConfigLoading ? '导出中...' : '导出配置（不含密钥）' }}
              </button>
              <button class="secondary-button compact" type="button" :disabled="modelsLoading" @click="refreshModels(true)">
                {{ modelsLoading ? '刷新中...' : '刷新模型列表' }}
              </button>
            </div>
          </div>
          <p v-if="modelErrorMessage" class="model-error">{{ modelErrorMessage }}</p>
          <p v-else-if="usingFallbackModelProvider" class="model-warning">未保存真实 API Key 或当前供应商不可用于运行时路由，正在显示内置占位模型。</p>
          <div v-else-if="models.length > 0" class="model-list" aria-label="真实模型">
            <button
              v-for="model in models.slice(0, 8)"
              :key="model.name"
              class="model-chip"
              type="button"
              :title="displayModelName(model)"
            >
              {{ displayModelName(model) }}
            </button>
          </div>
          <p v-else class="model-empty">当前供应商尚未返回可用模型。</p>
        </section>

        <div class="provider-list" :style="{ maxHeight: providerListMaxHeight }" aria-label="供应商列表">
          <article
            v-for="provider in providers"
            :key="provider.id"
            class="provider-card"
            :class="{
              active: provider.active,
              selected: provider.selected,
              dragging: draggedProviderId === provider.id,
              'drag-over': dragOverProviderId === provider.id && draggedProviderId !== provider.id
            }"
            draggable="true"
            @dragstart="beginProviderDrag(provider.id, $event)"
            @dragover.prevent="moveProviderNear(provider.id, $event)"
            @drop.prevent="finishProviderDrag"
            @dragend="finishProviderDrag"
          >
            <button class="drag-handle" type="button" title="拖拽排序" draggable="true">⠿</button>
            <div class="avatar" :class="provider.vendor">{{ provider.avatar }}</div>
            <div class="provider-main">
              <h2>{{ provider.name }}</h2>
              <a :href="provider.url" rel="noreferrer" @click.prevent="openExternalUrl(provider.url)">{{ provider.url }}</a>
            </div>
            <div class="provider-meta">
              <span>{{ provider.model }}</span>
              <span>{{ protocolLabel(provider.vendor) }}</span>
              <span>
                密钥：<strong class="money">{{ provider.hasApiKey ? (provider.apiKeyPreview ?? '已保存') : '未保存' }}</strong>
              </span>
            </div>
            <div class="provider-actions">
              <button class="enable-button" type="button" :disabled="provider.active" @click="activateProvider(provider.id)">
                ▷ {{ provider.active ? '已启用' : '启用' }}
              </button>
              <button class="icon-button small" type="button" title="编辑" @click="openEdit(provider)">✎</button>
              <button class="icon-button small" type="button" title="复制">▣</button>
              <button
                class="icon-button small"
                type="button"
                :disabled="providerTestLoadingId === provider.id"
                title="测试连接"
                @click="testProviderConnection(provider)"
              >
                {{ providerTestLoadingId === provider.id ? '…' : '↗' }}
              </button>
              <button class="icon-button small" type="button" title="统计">▥</button>
              <button class="icon-button small" type="button" title="删除" @click="deleteProvider(provider.id)">♲</button>
            </div>
          </article>
        </div>
      </section>

      <section v-else-if="currentView === 'analytics'" class="analytics-page">
        <div class="edit-header">
          <button class="back-button" type="button" @click="openList">←</button>
          <div>
            <h2>分析统计</h2>
            <p>查看本地代理当前接收的请求、端口状态和使用日志。</p>
          </div>
        </div>

        <div class="analytics-cards">
          <article>
            <span>总请求数</span>
            <strong>{{ formatNumber(analyticsSummary.TotalRequests) }}</strong>
            <small>内存窗口内</small>
          </article>
          <article>
            <span>总 Token</span>
            <strong>{{ formatCompactNumber(analyticsSummary.TotalTokens) }}</strong>
            <small>输入：{{ formatCompactNumber(analyticsSummary.InputTokens) }} / 输出：{{ formatCompactNumber(analyticsSummary.OutputTokens) }}</small>
          </article>
          <article>
            <span>总消费</span>
            <strong>{{ formatCost(analyticsSummary.TotalCost, analyticsSummary.Currency) }}</strong>
            <small>已计价：{{ formatNumber(analyticsSummary.PricedRequests) }} / 未计价：{{ formatNumber(analyticsSummary.UnpricedRequests) }}</small>
          </article>
          <article>
            <span>平均耗时</span>
            <strong>{{ analyticsSummary.AverageLatencySeconds.toFixed(2) }}s</strong>
            <small>每次请求</small>
          </article>
        </div>

        <section class="analytics-filter">
          <div>
            <span>监听端口</span>
            <strong>{{ analytics?.Listener.Url ?? '未连接' }}</strong>
            <small>
              状态：{{ analytics?.Listener.Status ?? '未知' }} · 专用端口：{{ analytics?.Listener.Port ?? 0 }}
            </small>
          </div>
          <div class="analytics-actions">
            <button class="secondary-button" type="button" :disabled="analyticsLoading" @click="loadAnalytics(true)">
              {{ analyticsLoading ? '刷新中...' : '刷新' }}
            </button>
            <button class="secondary-button" type="button" :disabled="analyticsLoading" @click="clearAnalytics">重置</button>
          </div>
        </section>

        <section class="analytics-table-wrap" aria-label="当前请求日志">
          <table class="analytics-table">
            <thead>
              <tr>
                <th>方法</th>
                <th>模型</th>
                <th>端点</th>
                <th>状态</th>
                <th>Token</th>
                <th>费用</th>
                <th>耗时</th>
                <th>时间</th>
                <th>User-Agent</th>
                <th>详情</th>
              </tr>
            </thead>
            <tbody>
              <template v-for="entry in analyticsRequests" :key="analyticsEntryKey(entry)">
                <tr>
                  <td>{{ entry.Method }}</td>
                  <td>{{ entry.Model ?? '-' }}</td>
                  <td>{{ entry.Path }}</td>
                  <td>
                    <span class="status-badge" :class="{ ok: entry.StatusCode < 400 }">{{ entry.StatusCode }}</span>
                  </td>
                  <td>
                    <span>↓ {{ formatNumber(entry.InputTokens) }}</span>
                    <small>↑ {{ formatNumber(entry.OutputTokens) }} · {{ formatUsageSource(entry.UsageSource) }}</small>
                  </td>
                  <td class="money">
                    <span>{{ formatCost(entry.Cost, entry.Currency) }}</span>
                    <small>{{ formatCostSource(entry) }}</small>
                  </td>
                  <td>{{ formatSeconds(entry.DurationMilliseconds) }}</td>
                  <td>{{ formatAnalyticsTime(entry.Timestamp) }}</td>
                  <td>{{ entry.UserAgent }}</td>
                  <td>
                    <button class="link-button detail-toggle" type="button" @click="toggleAnalyticsEntry(entry)">
                      {{ expandedAnalyticsKey === analyticsEntryKey(entry) ? '收起' : '展开' }}
                    </button>
                  </td>
                </tr>
                <tr v-if="expandedAnalyticsKey === analyticsEntryKey(entry)" class="analytics-detail-row">
                  <td colspan="10">
                    <div class="analytics-detail-grid">
                      <section>
                        <h3>请求头</h3>
                        <pre>{{ formatHeaders(entry.RequestHeaders) }}</pre>
                      </section>
                      <section>
                        <h3>请求体</h3>
                        <pre>{{ formatBody(entry.RequestBody) }}</pre>
                      </section>
                      <section>
                        <h3>响应头</h3>
                        <pre>{{ formatHeaders(entry.ResponseHeaders) }}</pre>
                      </section>
                      <section>
                        <h3>响应体</h3>
                        <pre>{{ formatBody(entry.ResponseBody) }}</pre>
                      </section>
                    </div>
                  </td>
                </tr>
              </template>
              <tr v-if="analyticsRequests.length === 0">
                <td colspan="10" class="analytics-empty">暂无请求日志。刷新模型、测试连接或调用 `/api/chat` 后会出现在这里。</td>
              </tr>
            </tbody>
          </table>
        </section>
      </section>

      <section v-else-if="currentView === 'edit'" class="edit-page">
        <div class="edit-header">
          <button class="back-button" type="button" @click="openList">←</button>
          <div>
            <h2>{{ editTitle }}</h2>
            <p>{{ editDescription }}</p>
          </div>
        </div>

        <form class="provider-form" @submit.prevent="saveProvider">
          <div class="logo-preview" aria-hidden="true">{{ providerName.slice(0, 1) || '无' }}</div>

          <div class="form-grid two">
            <label class="form-field">
              <span>供应商名称</span>
              <input v-model="providerName" type="text" autocomplete="off" />
            </label>
            <label class="form-field">
              <span>协议类型</span>
              <select v-model="providerProtocol">
                <option
                  v-for="option in protocolOptions"
                  :key="option.value"
                  :value="option.value"
                >
                  {{ option.label }}
                </option>
              </select>
            </label>
          </div>

          <label class="form-field">
            <span>备注</span>
            <input v-model="providerRemark" type="text" placeholder="例如：公司专用账号" autocomplete="off" />
          </label>

          <label class="form-field">
            <span>官网链接</span>
            <input v-model="providerWebsite" type="url" autocomplete="off" />
          </label>

          <label class="form-field api-key-field">
            <span>API Key</span>
            <input
              v-model="providerApiKey"
              :type="showApiKey ? 'text' : 'password'"
              :placeholder="isCreatingProvider ? '请输入 API Key' : '留空则保留已保存密钥'"
              autocomplete="off"
            />
            <button type="button" @click="showApiKey = !showApiKey">{{ showApiKey ? '隐藏' : '显示' }}</button>
          </label>

          <div class="field-head">
            <label>API 请求地址 <span>完整 URL</span></label>
            <button class="link-button" type="button">⚡ 管理与测速</button>
          </div>
          <label class="form-field">
            <input v-model="providerApiUrl" type="url" autocomplete="off" />
          </label>
          <p class="hint">💡 填写兼容 OpenAI Response 格式的服务端点地址</p>

          <div class="field-head">
            <label>模型名称</label>
            <button class="secondary-button" type="button" :disabled="providerDraftTestLoading" @click="testProviderConnection()">
              {{ providerDraftTestLoading ? '测试中...' : '获取模型并测试' }}
            </button>
          </div>
          <label class="form-field">
            <input v-model="providerModel" type="text" list="provider-model-options" autocomplete="off" />
            <datalist id="provider-model-options">
              <option
                v-for="modelName in providerConnectionResult?.Models ?? []"
                :key="modelName"
                :value="modelName"
              />
            </datalist>
          </label>
          <p class="helper">可留空后点击获取模型；优先自动选中 gpt-5.5 或 sonnet-4.6，否则选中远程返回的第一个模型。</p>

          <section class="advanced-options" :class="{ open: showAdvancedOptions }" aria-label="高级选项">
            <button class="advanced-toggle" type="button" @click="showAdvancedOptions = !showAdvancedOptions">
              <span>高级选项</span>
              <strong>{{ showAdvancedOptions ? '收起' : '展开' }}</strong>
            </button>
            <p class="helper">代理、熔断、重试和备用路由默认折叠，避免干扰日常供应商切换。</p>

            <div v-if="showAdvancedOptions" class="advanced-grid">
              <label class="form-field">
                <span>本地代理地址</span>
                <input v-model="proxyAddress" type="text" inputmode="url" autocomplete="off" />
              </label>
              <div class="port-check-card">
                <button class="secondary-button" type="button" :disabled="portChecking" @click="checkProxyPort">
                  {{ portChecking ? '检测中...' : '检测端口占用' }}
                </button>
                <small v-if="portStatus" :class="portStatus.Available ? 'ok' : 'danger'">{{ portStatus.Message }}</small>
                <small v-else>默认检查代理 URL 中的本地端口，避免和 Ollama 或其他代理冲突。</small>
              </div>
              <label class="form-field">
                <span>熔断失败阈值</span>
                <input v-model.number="circuitBreakerThreshold" type="number" min="1" max="20" />
              </label>
              <label class="form-field">
                <span>重试次数</span>
                <input v-model.number="retryCount" type="number" min="0" max="10" />
              </label>
              <label class="form-field">
                <span>备用路由</span>
                <input v-model="fallbackRoute" type="text" autocomplete="off" />
              </label>
            </div>
          </section>

          <section class="json-editor" aria-label="auth.json 编辑器">
            <div class="json-title">auth.json (JSON) *</div>
            <pre><code><span>1</span> {{ authJson.split('\n')[0] }}
<span>2</span>   "OPENAI_API_KEY": "{{ showApiKey ? providerApiKey : 'sk-••••••••••••••••••••••••••••••••••••••••••••c087' }}"
<span>3</span> {{ authJson.split('\n')[2] }}</code></pre>
            <button class="link-button format" type="button">✣ 格式化</button>
          </section>

          <footer class="form-footer">
            <button class="secondary-button" type="button" @click="openList">取消</button>
            <button class="save-button" type="submit">▣ 保存</button>
          </footer>
        </form>
      </section>

      <section v-else class="settings-page">
        <div class="edit-header">
          <button class="back-button" type="button" @click="openList">←</button>
          <div>
            <h2>设置</h2>
            <p>集中管理本地代理状态、VS Code 配置写入和配置回滚。</p>
          </div>
        </div>

        <div class="settings-layout">
          <nav class="settings-tabs" aria-label="设置选项卡">
            <button type="button" :class="{ active: settingsTab === 'general' }" @click="settingsTab = 'general'">
              <strong>常规</strong>
              <span>VS Code 配置写入、代理基础状态</span>
            </button>
            <button type="button" :class="{ active: settingsTab === 'updates' }" @click="openUpdates">
              <strong>更新</strong>
              <span>{{ updateStatusText }}</span>
            </button>
            <button type="button" :class="{ active: settingsTab === 'backups' }" @click="settingsTab = 'backups'">
              <strong>备份</strong>
              <span>配置备份列表和回滚恢复</span>
            </button>
            <button type="button" :class="{ active: settingsTab === 'about' }" @click="settingsTab = 'about'">
              <strong>关于</strong>
              <span>版本、项目地址和企业微信</span>
            </button>
          </nav>

          <div class="settings-panel">
            <template v-if="settingsTab === 'general'">
              <section class="settings-section">
                <div>
                  <span>常规</span>
                  <h3>VS Code Ollama 配置</h3>
                </div>
                <p>先选择 VS Code User 目录并生成 dry-run 差异预览，确认 vscs Ollama Provider 条目变化后再写入。</p>
              </section>

              <section v-if="vscodeSetupPromptVisible" class="switch-guidance" aria-label="VS Code Ollama 开关写入向导">
                <div>
                  <span>来自顶部开关</span>
                  <h3>检测到 VS Code Ollama 配置缺失</h3>
                  <p>顶部开关不会直接写入配置。请先生成差异预览，确认目标文件和 vscs Provider 条目后，再二次确认写入。</p>
                </div>
                <div class="wizard-actions">
                  <button class="secondary-button" type="button" :disabled="previewLoading || !selectedDirectory" @click="previewVsCodeConfig">
                    {{ previewLoading ? '生成预览中...' : '生成差异预览' }}
                  </button>
                  <button class="save-button" type="button" :disabled="applyLoading || !canApplyVsCodeConfig" @click="applyVsCodeConfig">
                    {{ applyLoading ? '写入中...' : applyConfirmText }}
                  </button>
                </div>
              </section>

              <section class="config-wizard" aria-label="VS Code Ollama 配置写入向导">
                <div class="wizard-title">
                  <div>
                    <span>VS Code 配置向导</span>
                    <h3>写入前必须先预览差异</h3>
                  </div>
                  <button class="link-button" type="button" @click="resetVsCodeWizard">重置</button>
                </div>

                <ol class="wizard-steps">
                  <li :class="{ done: selectedDirectory }">
                    <strong>1</strong>
                    <span>选择目录</span>
                  </li>
                  <li :class="{ done: preview }">
                    <strong>2</strong>
                    <span>预览差异</span>
                  </li>
                  <li :class="{ done: applyResult }">
                    <strong>3</strong>
                    <span>确认写入</span>
                  </li>
                  <li :class="{ done: applyResult }">
                    <strong>4</strong>
                    <span>查看结果</span>
                  </li>
                </ol>

                <label class="form-field">
                  <span>目标 VS Code User 目录</span>
                  <select v-model="selectedDirectory" @change="handleDirectoryChanged">
                    <option v-for="directory in directories" :key="directory.Path" :value="directory.Path">
                      {{ directory.Description }} · {{ directory.Exists ? '已存在' : '将创建' }}
                    </option>
                  </select>
                </label>
                <p class="helper">{{ selectedDirectory || '当前没有发现 Windows VS Code User 配置目录。' }}</p>

                <div class="wizard-actions">
                  <button class="secondary-button" type="button" :disabled="previewLoading || !selectedDirectory" @click="previewVsCodeConfig">
                    {{ previewLoading ? '生成预览中...' : '生成差异预览' }}
                  </button>
                  <button class="save-button" type="button" :disabled="applyLoading || !canApplyVsCodeConfig" @click="applyVsCodeConfig">
                    {{ applyLoading ? '写入中...' : applyConfirmText }}
                  </button>
                </div>

                <div v-if="applyConfirmationArmed" class="risk-confirmation">
                  <strong>请再次确认写入</strong>
                  <span>将修改所选 VS Code User 目录中的 vscs Ollama Provider 条目；已存在文件会先创建备份，未知 Provider 会保留。</span>
                </div>

                <div v-if="preview" class="wizard-summary">
                  <strong>{{ applyResult ? '写入完成' : '预览完成' }}</strong>
                  <span>{{ previewChangedCount }} 个文件需要更新，{{ preview.Changes.length - previewChangedCount }} 个文件无需变更。</span>
                  <small>写入前会备份已存在文件；只维护 vscs Ollama Provider 条目，避免配置漂移。</small>
                </div>

                <div v-if="preview" class="preview-list">
                  <div v-for="change in preview.Changes" :key="change.FilePath" class="preview-item">
                    <strong>{{ change.Changed ? (applyResult ? '已更新' : '将更新') : '无需变更' }}</strong>
                    <span>{{ change.FilePath }}</span>
                    <small>{{ change.ExistedBefore ? '保留未知 Provider，只调整本项目托管的 vscs Ollama 条目' : '文件不存在，确认写入时会创建' }}</small>
                    <small v-if="change.BackupPath">备份位置：{{ change.BackupPath }}</small>
                    <div class="field-diff-list" aria-label="Provider 条目差异">
                      <div v-for="field in change.FieldChanges" :key="field.Path" class="field-diff" :class="{ changed: field.Changed }">
                        <code>{{ field.Path }}</code>
                        <span>{{ field.Changed ? '将变更' : '不变' }}</span>
                        <small>原值：{{ field.BeforeValue }}</small>
                        <small>新值：{{ field.AfterValue }}</small>
                      </div>
                    </div>
                  </div>
                </div>
              </section>

            </template>

            <template v-else-if="settingsTab === 'updates'">
              <section class="settings-section">
                <div>
                  <span>更新</span>
                  <h3>GitHub / Gitee 自动更新</h3>
                </div>
                <p>后台会按策略检查 GitHub 和 Gitee Release，发现更高版本时只下载发布包到本地缓存，不会静默替换正在运行的程序。</p>
              </section>

              <section class="update-panel" aria-label="自动更新状态">
                <div class="wizard-title">
                  <div>
                    <span>版本状态</span>
                    <h3>{{ updateStatusText }}</h3>
                  </div>
                  <div class="wizard-actions">
                    <button class="secondary-button" type="button" :disabled="updateChecking" @click="checkUpdates(true)">
                      {{ updateChecking ? '检查中...' : '检查更新' }}
                    </button>
                    <button
                      class="save-button"
                      type="button"
                      :disabled="updateDownloading || !updateCheck?.UpdateAvailable || !updateCheck.LatestRelease?.Asset"
                      @click="downloadLatestUpdate"
                    >
                      {{ updateDownloading ? '下载中...' : '下载最新版本' }}
                    </button>
                  </div>
                </div>

                <div v-if="updateCheck" class="update-summary">
                  <article>
                    <span>当前版本</span>
                    <strong>{{ updateCheck.CurrentVersion }}</strong>
                  </article>
                  <article>
                    <span>最新版本</span>
                    <strong>{{ updateCheck.LatestRelease?.Version ?? '未发现' }}</strong>
                  </article>
                  <article>
                    <span>来源</span>
                    <strong>{{ updateCheck.LatestRelease?.SourceName ?? '无' }}</strong>
                  </article>
                </div>

                <div v-if="updateCheck?.LatestRelease" class="update-release">
                  <strong>{{ updateCheck.LatestRelease.Name || updateCheck.LatestRelease.TagName }}</strong>
                  <span>{{ updateCheck.LatestRelease.SourceName }} · {{ updateCheck.LatestRelease.TagName }}</span>
                  <small v-if="updateCheck.LatestRelease.Asset">
                    发布包：{{ updateCheck.LatestRelease.Asset.Name }} · {{ Math.max(1, Math.round(updateCheck.LatestRelease.Asset.SizeBytes / 1024 / 1024)) }} MB
                  </small>
                  <small v-else>该 Release 没有匹配到 Windows 单文件发布包。</small>
                  <button
                    v-if="updateCheck.LatestRelease.PageUrl"
                    class="link-button"
                    type="button"
                    @click="openExternalUrl(updateCheck.LatestRelease.PageUrl)"
                  >
                    打开发布页
                  </button>
                </div>

                <div v-if="updateDownload" class="wizard-summary">
                  <strong>{{ updateDownload.Message }}</strong>
                  <span v-if="updateDownload.FilePath">本地缓存：{{ updateDownload.FilePath }}</span>
                  <small v-if="updateDownload.Release">版本：{{ updateDownload.Release.Version }} · 来源：{{ updateDownload.Release.SourceName }}</small>
                </div>

                <div v-if="updateCheck?.Sources.length" class="update-source-list">
                  <div v-for="source in updateCheck.Sources" :key="source.SourceName" class="update-source-item" :class="{ failed: !source.Success }">
                    <strong>{{ source.SourceName }}</strong>
                    <span>{{ source.Message }}</span>
                    <small v-if="source.Release">版本：{{ source.Release.Version }} · {{ source.Release.Asset?.Name ?? '未匹配发布包' }}</small>
                  </div>
                </div>
              </section>
            </template>

            <template v-else-if="settingsTab === 'backups'">
              <section class="settings-section">
                <div>
                  <span>备份</span>
                  <h3>VS Code 配置回滚</h3>
                </div>
                <p>只恢复 VSCopilotSwitch 创建的备份；恢复前会再次备份当前文件，方便撤销误操作。</p>
              </section>

              <section class="rollback-panel" aria-label="VS Code 配置回滚">
                <div class="wizard-title">
                  <div>
                    <span>回滚入口</span>
                    <h3>从最近备份恢复配置</h3>
                  </div>
                  <button class="secondary-button" type="button" :disabled="backupsLoading || !selectedDirectory" @click="loadBackups">
                    {{ backupsLoading ? '读取中...' : '刷新备份' }}
                  </button>
                </div>

                <p class="helper">只显示 VSCopilotSwitch 为 `chatLanguageModels.json` 创建的备份，恢复前会先为当前文件创建安全备份。</p>

                <div v-if="backups.length" class="rollback-list">
                  <label v-for="backup in backups" :key="backup.BackupPath" class="rollback-item">
                    <input v-model="selectedBackupPath" type="radio" name="vscode-backup" :value="backup.BackupPath" @change="handleBackupChanged" />
                    <span>
                      <strong>{{ backup.FileName }}</strong>
                      <small>{{ new Date(backup.CreatedAt).toLocaleString() }} · {{ Math.max(1, Math.round(backup.SizeBytes / 1024)) }} KB</small>
                      <small>{{ backup.BackupPath }}</small>
                    </span>
                  </label>
                </div>
                <p v-else class="empty-backups">当前目录还没有可回滚的 VSCopilotSwitch 备份。</p>

                <div class="wizard-actions">
                  <button class="save-button danger" type="button" :disabled="restoreLoading || !selectedBackup" @click="restoreBackup">
                    {{ restoreLoading ? '恢复中...' : restoreConfirmText }}
                  </button>
                </div>

                <div v-if="restoreConfirmationArmed" class="risk-confirmation danger">
                  <strong>请再次确认恢复</strong>
                  <span>将用选中备份覆盖当前 {{ selectedBackup?.FileName }}；恢复前会为当前文件创建一份安全备份。</span>
                </div>

                <div v-if="restoreResult" class="wizard-summary rollback-result">
                  <strong>恢复完成</strong>
                  <span>{{ restoreResult.FilePath }}</span>
                  <small>使用备份：{{ restoreResult.BackupPath }}</small>
                  <small v-if="restoreResult.SafetyBackupPath">恢复前安全备份：{{ restoreResult.SafetyBackupPath }}</small>
                </div>
              </section>
            </template>

            <template v-else-if="settingsTab === 'about'">
              <section class="settings-section">
                <div>
                  <span>关于</span>
                  <h3>{{ aboutInfo?.Title ?? 'VSCopilotSwitch' }}</h3>
                </div>
                <p>本地模型供应商切换与 Ollama 兼容协议转换工具，面向 VS Code / GitHub Copilot Chat 使用场景。</p>
              </section>

              <section class="about-panel" aria-label="关于 VSCopilotSwitch">
                <div class="about-card">
                  <img class="about-logo" src="./assets/logo.svg" alt="VSCopilotSwitch logo" />
                  <div>
                    <span>应用名称</span>
                    <strong>{{ aboutInfo?.Title ?? 'VSCopilotSwitch' }}</strong>
                  </div>
                </div>

                <div class="about-info-grid">
                  <article>
                    <span>当前版本</span>
                    <strong>{{ aboutInfo?.Version ?? '读取中' }}</strong>
                  </article>
                  <article>
                    <span>GitHub</span>
                    <button class="link-button about-link" type="button" @click="openExternalUrl(aboutInfo?.GitHubUrl ?? 'https://github.com/maikebing/VSCopilotSwitch')">
                      {{ aboutInfo?.GitHubUrl ?? 'https://github.com/maikebing/VSCopilotSwitch' }}
                    </button>
                  </article>
                </div>

                <div class="wechat-card">
                  <div>
                    <span>企业微信</span>
                    <strong>扫码联系和反馈</strong>
                    <small>二维码图片来自发布包内置资源。</small>
                  </div>
                  <img :src="aboutInfo?.EnterpriseWeChatQrPath ?? '/VSCopilotSwitch.png'" alt="企业微信二维码" />
                </div>
              </section>
            </template>

          </div>
        </div>
      </section>
    </main>
  </div>
</template>
