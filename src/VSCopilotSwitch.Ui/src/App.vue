<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';

type HealthStatus = {
  name: string;
  status: string;
  mode: string;
};

type ModelDetails = {
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
  url: string;
  avatar: string;
  vendor: 'codex' | 'claude';
  active: boolean;
  selected: boolean;
  balance?: string;
  lastChecked?: string;
  failed?: boolean;
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
const portStatus = ref<PortStatus | null>(null);
const loading = ref(false);
const previewLoading = ref(false);
const applyLoading = ref(false);
const backupsLoading = ref(false);
const restoreLoading = ref(false);
const portChecking = ref(false);
const applyConfirmationArmed = ref(false);
const restoreConfirmationArmed = ref(false);
const errorMessage = ref('');
const recoveryAdvice = ref<RecoveryAdvice | null>(null);
const currentView = ref<'list' | 'edit'>('list');
const showApiKey = ref(false);
const showAdvancedOptions = ref(false);

const providers = ref<ProviderCard[]>([
  {
    id: 'my-codex',
    name: 'My Codex',
    url: 'https://how88.top',
    avatar: 'MC',
    vendor: 'codex',
    active: true,
    selected: true
  },
  {
    id: 'wuji',
    name: '无极限超大杯',
    url: 'https://2030.wujixian.fun',
    avatar: '无',
    vendor: 'codex',
    active: false,
    selected: false,
    failed: true
  },
  {
    id: 'ham',
    name: '哈基米公益站',
    url: 'https://ai.td.ee',
    avatar: '哈',
    vendor: 'codex',
    active: false,
    selected: false,
    balance: '9.81 USD',
    lastChecked: '34 分钟前'
  },
  {
    id: 'openai-official',
    name: 'OpenAI Official',
    url: 'https://chatgpt.com/codex',
    avatar: '◎',
    vendor: 'codex',
    active: false,
    selected: false
  },
  {
    id: 'sonnet-vip',
    name: 'Sonnet VIP',
    url: 'https://sonnet.vip/',
    avatar: 'SV',
    vendor: 'claude',
    active: false,
    selected: false,
    failed: true
  }
]);

const providerName = ref('无极限超大杯');
const providerRemark = ref('');
const providerWebsite = ref('https://2030.wujixian.fun');
const providerApiKey = ref('sk-demo-placeholder-c087');
const providerApiUrl = ref('https://2030.wujixian.fun');
const providerModel = ref('gpt-5.5');
const proxyAddress = ref('http://127.0.0.1:11434');
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
const activeModel = computed(() => models.value[0]?.name ?? providerModel.value);
const activeProvider = computed(() => providers.value.find((provider) => provider.active));
const providerCountText = computed(() => `${providers.value.length} 个供应商 · ${existingDirectories.value.length} 个 VS Code 配置目录`);
const previewChangedCount = computed(() => preview.value?.Changes.filter((change) => change.Changed).length ?? 0);
const canApplyVsCodeConfig = computed(() => Boolean(preview.value?.DryRun && selectedDirectory.value && !applyResult.value));
const selectedBackup = computed(() => backups.value.find((backup) => backup.BackupPath === selectedBackupPath.value));
const applyConfirmText = computed(() => (applyConfirmationArmed.value ? '已预览风险，再次点击写入' : '确认写入 VS Code Ollama 配置'));
const restoreConfirmText = computed(() => (restoreConfirmationArmed.value ? '已确认风险，再次点击恢复' : '恢复选中备份'));

function clearError() {
  errorMessage.value = '';
  recoveryAdvice.value = null;
}

function setError(message: string, advice?: RecoveryAdvice) {
  errorMessage.value = message;
  recoveryAdvice.value = advice ?? buildRecoveryAdvice(message);
}

function messageFromError(error: unknown, fallback: string) {
  return error instanceof Error ? error.message : fallback;
}

function buildRecoveryAdvice(message: string): RecoveryAdvice {
  const normalized = message.toLowerCase();

  if (normalized.includes('json')) {
    return {
      title: 'JSON 文件格式异常',
      steps: [
        '先不要写入配置，手动打开对应的 settings.json 或 chatLanguageModels.json。',
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
        '关闭正在编辑 settings.json 或 chatLanguageModels.json 的窗口。',
        '退出 VS Code 后重新打开 VSCopilotSwitch 再试一次。',
        '如果仍失败，先复制备份路径，手动恢复配置。'
      ]
    };
  }

  if (normalized.includes('端口') || normalized.includes('port') || normalized.includes('listen')) {
    return {
      title: '本地端口不可用',
      steps: [
        '确认 127.0.0.1 本地代理没有被防火墙拦截。',
        '如果手动指定了 11434，请关闭其他 Ollama 或代理进程后重试。',
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
    const [healthResponse, tagsResponse, directoriesResponse] = await Promise.all([
      fetch('/health'),
      fetch('/api/tags'),
      fetch('/internal/vscode/user-directories')
    ]);

    if (!healthResponse.ok || !tagsResponse.ok || !directoriesResponse.ok) {
      throw new Error('后端 API 返回异常，请确认本地代理已经启动。');
    }

    health.value = await healthResponse.json();
    const tagResult = (await tagsResponse.json()) as TagsResponse;
    models.value = tagResult.models;
    directories.value = await directoriesResponse.json();
    selectedDirectory.value = existingDirectories.value[0]?.Path ?? directories.value[0]?.Path ?? '';
    preview.value = null;
    applyResult.value = null;
    backups.value = [];
    selectedBackupPath.value = '';
    restoreResult.value = null;
  } catch (error) {
    setError(messageFromError(error, '加载状态失败。'));
  } finally {
    loading.value = false;
  }
}

async function previewVsCodeConfig() {
  if (!selectedDirectory.value) {
    setError('没有可用的 VS Code User 配置目录，无法生成预览。');
    return;
  }

  previewLoading.value = true;
  clearError();
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
      throw new Error('VS Code 配置预览失败，请检查目录权限和 JSON 文件格式。');
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
      throw new Error('VS Code 配置写入失败，原文件已保留，请检查权限或文件占用。');
    }

    applyResult.value = await response.json();
    preview.value = applyResult.value;
    applyConfirmationArmed.value = false;
    await loadBackups();
  } catch (error) {
    setError(messageFromError(error, 'VS Code 配置写入失败。'));
  } finally {
    applyLoading.value = false;
  }
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
  const parsedUrl = new URL(proxyAddress.value);
  const port = Number(parsedUrl.port || (parsedUrl.protocol === 'https:' ? 443 : 80));

  portChecking.value = true;
  clearError();

  try {
    const response = await fetch(`/internal/network/port-status?port=${encodeURIComponent(port)}`);
    if (!response.ok) {
      throw new Error('端口检测失败，请确认端口号在 1 到 65535 之间。');
    }

    portStatus.value = await response.json();
  } catch (error) {
    setError(messageFromError(error, '端口检测失败。'));
  } finally {
    portChecking.value = false;
  }
}

function resetVsCodeWizard() {
  preview.value = null;
  applyResult.value = null;
  restoreResult.value = null;
  applyConfirmationArmed.value = false;
  clearError();
}

function openEdit(provider?: ProviderCard) {
  resetVsCodeWizard();

  if (provider) {
    providerName.value = provider.name;
    providerWebsite.value = provider.url;
    providerApiUrl.value = provider.url;
    providerRemark.value = provider.active ? '当前启用供应商' : '';
  }

  currentView.value = 'edit';
}

function openList() {
  currentView.value = 'list';
}

function activateProvider(providerId: string) {
  providers.value = providers.value.map((provider) => ({
    ...provider,
    active: provider.id === providerId,
    selected: provider.id === providerId
  }));
}

function handleDirectoryChanged() {
  resetVsCodeWizard();
  backups.value = [];
  selectedBackupPath.value = '';
  restoreConfirmationArmed.value = false;
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
        <button class="icon-button" type="button" title="设置">⚙</button>
        <button class="icon-button ghost" type="button" title="统计">▥</button>
      </section>

      <section class="quick-switch" aria-label="快速切换">
        <button class="pill active" type="button"><span class="ide-icon vs-icon">VS</span> VS2026</button>
        <button class="pill selected" type="button"><span class="ide-icon vscode-icon">⌁</span> VSCode</button>
      </section>

      <section class="toolbar" aria-label="工具栏">
        <button class="icon-button" type="button" title="代理设置">🔧</button>
        <button class="icon-button" type="button" title="配置预览" @click="openEdit()">▣</button>
        <button class="icon-button" type="button" :disabled="loading" title="刷新" @click="loadDashboard">↻</button>
        <button class="icon-button" type="button" title="附件">⌁</button>
        <button class="add-button" type="button" title="添加供应商" @click="openEdit()">＋</button>
      </section>
    </header>

    <main class="page-surface">
      <p v-if="errorMessage" class="notice error">{{ errorMessage }}</p>
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

        <div class="provider-list" aria-label="供应商列表">
          <article
            v-for="provider in providers"
            :key="provider.id"
            class="provider-card"
            :class="{ active: provider.active, selected: provider.selected }"
          >
            <button class="drag-handle" type="button" title="拖拽排序">⠿</button>
            <div class="avatar" :class="provider.vendor">{{ provider.avatar }}</div>
            <div class="provider-main">
              <h2>{{ provider.name }}</h2>
              <a :href="provider.url" target="_blank" rel="noreferrer">{{ provider.url }}</a>
            </div>
            <div class="provider-meta">
              <template v-if="provider.failed">
                <button class="status-button danger" type="button">ⓘ 查询失败</button>
                <button class="icon-button small" type="button" title="重新查询">↻</button>
              </template>
              <template v-else-if="provider.balance">
                <span class="time">◷ {{ provider.lastChecked }}</span>
                <span>余额：<strong class="money">{{ provider.balance }}</strong></span>
              </template>
            </div>
            <div class="provider-actions">
              <button class="enable-button" type="button" :disabled="provider.active" @click="activateProvider(provider.id)">
                ▷ {{ provider.active ? '已启用' : '启用' }}
              </button>
              <button class="icon-button small" type="button" title="编辑" @click="openEdit(provider)">✎</button>
              <button class="icon-button small" type="button" title="复制">▣</button>
              <button class="icon-button small" type="button" title="测试">↗</button>
              <button class="icon-button small" type="button" title="统计">▥</button>
              <button class="icon-button small" type="button" title="删除">♲</button>
            </div>
          </article>
        </div>
      </section>

      <section v-else class="edit-page">
        <div class="edit-header">
          <button class="back-button" type="button" @click="openList">←</button>
          <div>
            <h2>编辑供应商</h2>
            <p>保存前仅在界面中预览，后续写入配置需经过差异预览和确认。</p>
          </div>
        </div>

        <form class="provider-form" @submit.prevent="openList">
          <div class="logo-preview" aria-hidden="true">{{ providerName.slice(0, 1) || '无' }}</div>

          <div class="form-grid two">
            <label class="form-field">
              <span>供应商名称</span>
              <input v-model="providerName" type="text" autocomplete="off" />
            </label>
            <label class="form-field">
              <span>备注</span>
              <input v-model="providerRemark" type="text" placeholder="例如：公司专用账号" autocomplete="off" />
            </label>
          </div>

          <label class="form-field">
            <span>官网链接</span>
            <input v-model="providerWebsite" type="url" autocomplete="off" />
          </label>

          <label class="form-field api-key-field">
            <span>API Key</span>
            <input v-model="providerApiKey" :type="showApiKey ? 'text' : 'password'" autocomplete="off" />
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
            <button class="secondary-button" type="button">⇩ 获取模型列表</button>
          </div>
          <label class="form-field">
            <input v-model="providerModel" type="text" autocomplete="off" />
          </label>
          <p class="helper">指定使用的模型，将自动更新到 config.toml 中</p>

          <section class="advanced-options" :class="{ open: showAdvancedOptions }" aria-label="高级选项">
            <button class="advanced-toggle" type="button" @click="showAdvancedOptions = !showAdvancedOptions">
              <span>高级选项</span>
              <strong>{{ showAdvancedOptions ? '收起' : '展开' }}</strong>
            </button>
            <p class="helper">代理、熔断、重试和备用路由默认折叠，避免干扰日常供应商切换。</p>

            <div v-if="showAdvancedOptions" class="advanced-grid">
              <label class="form-field">
                <span>本地代理地址</span>
                <input v-model="proxyAddress" type="url" autocomplete="off" />
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
              <span>将修改所选 VS Code User 目录中的 Ollama 相关字段；已存在文件会先创建备份，未知字段会保留。</span>
            </div>

            <div v-if="preview" class="wizard-summary">
              <strong>{{ applyResult ? '写入完成' : '预览完成' }}</strong>
              <span>{{ previewChangedCount }} 个文件需要更新，{{ preview.Changes.length - previewChangedCount }} 个文件无需变更。</span>
              <small>写入前会备份已存在文件；未变更文件不会重复写入，避免配置漂移。</small>
            </div>

            <div v-if="preview" class="preview-list">
              <div v-for="change in preview.Changes" :key="change.FilePath" class="preview-item">
                <strong>{{ change.Changed ? (applyResult ? '已更新' : '将更新') : '无需变更' }}</strong>
                <span>{{ change.FilePath }}</span>
                <small>{{ change.ExistedBefore ? '保留未知字段，只调整本项目托管的 Ollama 字段' : '文件不存在，确认写入时会创建' }}</small>
                <small v-if="change.BackupPath">备份位置：{{ change.BackupPath }}</small>
                <div class="field-diff-list" aria-label="字段级差异">
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

            <p class="helper">只显示 VSCopilotSwitch 为 `settings.json` 和 `chatLanguageModels.json` 创建的备份，恢复前会先为当前文件创建安全备份。</p>

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

          <footer class="form-footer">
            <button class="secondary-button" type="button" @click="openList">取消</button>
            <button class="save-button" type="submit">▣ 保存</button>
          </footer>
        </form>
      </section>
    </main>
  </div>
</template>
