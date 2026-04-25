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
};

type ApplyResult = {
  UserDirectory: string;
  DryRun: boolean;
  Changes: FileChange[];
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
const loading = ref(false);
const previewLoading = ref(false);
const errorMessage = ref('');
const currentView = ref<'list' | 'edit'>('list');
const showApiKey = ref(false);

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

async function loadDashboard() {
  loading.value = true;
  errorMessage.value = '';

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
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : '加载状态失败。';
  } finally {
    loading.value = false;
  }
}

async function previewVsCodeConfig() {
  if (!selectedDirectory.value) {
    errorMessage.value = '没有可用的 VS Code User 配置目录，无法生成预览。';
    return;
  }

  previewLoading.value = true;
  errorMessage.value = '';
  preview.value = null;

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
    errorMessage.value = error instanceof Error ? error.message : 'VS Code 配置预览失败。';
  } finally {
    previewLoading.value = false;
  }
}

function openEdit(provider?: ProviderCard) {
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
        <button class="icon-button" type="button" title="配置预览" @click="previewVsCodeConfig">▣</button>
        <button class="icon-button" type="button" :disabled="loading" title="刷新" @click="loadDashboard">↻</button>
        <button class="icon-button" type="button" title="附件">⌁</button>
        <button class="add-button" type="button" title="添加供应商" @click="openEdit()">＋</button>
      </section>
    </header>

    <main class="page-surface">
      <p v-if="errorMessage" class="notice error">{{ errorMessage }}</p>

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

          <section class="json-editor" aria-label="auth.json 编辑器">
            <div class="json-title">auth.json (JSON) *</div>
            <pre><code><span>1</span> {{ authJson.split('\n')[0] }}
<span>2</span>   "OPENAI_API_KEY": "{{ showApiKey ? providerApiKey : 'sk-••••••••••••••••••••••••••••••••••••••••••••c087' }}"
<span>3</span> {{ authJson.split('\n')[2] }}</code></pre>
            <button class="link-button format" type="button">✣ 格式化</button>
          </section>

          <div class="config-preview">
            <button class="secondary-button" type="button" :disabled="previewLoading || !selectedDirectory" @click="previewVsCodeConfig">
              {{ previewLoading ? '生成预览中...' : '预览 VS Code Ollama 配置' }}
            </button>
            <div v-if="preview" class="preview-list">
              <div v-for="change in preview.Changes" :key="change.FilePath" class="preview-item">
                <strong>{{ change.Changed ? '将更新' : '无需变更' }}</strong>
                <span>{{ change.FilePath }}</span>
                <small>{{ change.ExistedBefore ? '保留现有文件并只调整托管字段' : '文件不存在，将在确认写入时创建' }}</small>
              </div>
            </div>
          </div>

          <footer class="form-footer">
            <button class="secondary-button" type="button" @click="openList">取消</button>
            <button class="save-button" type="submit">▣ 保存</button>
          </footer>
        </form>
      </section>
    </main>
  </div>
</template>
