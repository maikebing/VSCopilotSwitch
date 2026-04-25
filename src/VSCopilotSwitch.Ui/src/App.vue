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

const health = ref<HealthStatus | null>(null);
const models = ref<ModelInfo[]>([]);
const directories = ref<VsCodeUserDirectory[]>([]);
const selectedDirectory = ref('');
const preview = ref<ApplyResult | null>(null);
const loading = ref(false);
const previewLoading = ref(false);
const errorMessage = ref('');

const existingDirectories = computed(() => directories.value.filter((directory) => directory.Exists));
const selectedDirectoryDescription = computed(() => {
  const directory = directories.value.find((item) => item.Path === selectedDirectory.value);
  return directory?.Description ?? '请选择一个 VS Code User 配置目录。';
});
const healthState = computed(() => health.value?.status ?? 'unknown');
const activeModel = computed(() => models.value[0]?.name ?? 'vscopilotswitch/default');

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

onMounted(loadDashboard);
</script>

<template>
  <div class="workbench-shell">
    <header class="title-bar">
      <div class="traffic-lights" aria-hidden="true">
        <span></span>
        <span></span>
        <span></span>
      </div>
      <div class="command-center">VSCopilotSwitch · VS Code Theme</div>
      <button class="title-action" type="button" :disabled="loading" @click="loadDashboard">
        {{ loading ? '刷新中' : '刷新' }}
      </button>
    </header>

    <div class="workbench-body">
      <nav class="activity-bar" aria-label="主导航">
        <button class="activity-item active" type="button" title="模型">⌘</button>
        <button class="activity-item" type="button" title="VS Code 配置">{} </button>
        <button class="activity-item" type="button" title="日志">▤</button>
        <button class="activity-item bottom" type="button" title="设置">⚙</button>
      </nav>

      <aside class="side-bar">
        <div class="side-title">VSCOPILOT SWITCH</div>
        <section class="tree-group">
          <div class="tree-heading">STATUS</div>
          <button class="tree-item active" type="button">代理状态 · {{ healthState }}</button>
          <button class="tree-item" type="button">当前模型 · {{ activeModel }}</button>
          <button class="tree-item" type="button">配置目录 · {{ existingDirectories.length }}</button>
        </section>
        <section class="tree-group">
          <div class="tree-heading">PROVIDERS</div>
          <button class="tree-item" type="button">内置占位 Provider</button>
          <button class="tree-item disabled" type="button">OpenAI / Claude / DeepSeek</button>
        </section>
      </aside>

      <main class="editor-area">
        <div class="tabs">
          <div class="tab active">dashboard.vue</div>
          <div class="tab">vscode-config.preview</div>
        </div>

        <section class="editor-content">
          <p v-if="errorMessage" class="notification error">{{ errorMessage }}</p>

          <section class="hero-panel">
            <p class="eyebrow">Ollama compatible model switcher</p>
            <h1>像在 VS Code 里切换模型一样管理 Copilot 后端</h1>
            <p>
              使用 VS Code Workbench 风格主题承载主要操作：查看本地代理、模型列表、Provider 状态，并先 dry-run 预览配置变更。
            </p>
          </section>

          <section class="metric-grid">
            <article class="metric-card">
              <span>代理状态</span>
              <strong>{{ healthState }}</strong>
              <p>{{ health?.name ?? 'VSCopilotSwitch' }} · {{ health?.mode ?? '未连接' }}</p>
            </article>
            <article class="metric-card">
              <span>当前模型</span>
              <strong>{{ activeModel }}</strong>
              <p>Ollama 兼容名称会写入 VS Code 相关配置。</p>
            </article>
            <article class="metric-card">
              <span>VS Code 配置</span>
              <strong>{{ existingDirectories.length }} 个可用目录</strong>
              <p>{{ selectedDirectoryDescription }}</p>
            </article>
          </section>

          <section class="split-view">
            <article class="panel">
              <div class="panel-header">
                <div>
                  <p class="eyebrow">models</p>
                  <h2>模型列表</h2>
                </div>
                <span>{{ models.length }} 个</span>
              </div>

              <div class="model-list">
                <div v-for="model in models" :key="model.digest" class="model-item">
                  <div>
                    <strong>{{ model.name }}</strong>
                    <p>{{ model.details.family }} · {{ model.details.parameter_size }}</p>
                  </div>
                  <code>{{ model.details.quantization_level }}</code>
                </div>
                <p v-if="models.length === 0" class="muted">暂无模型数据，请刷新状态。</p>
              </div>
            </article>

            <article class="panel">
              <div class="panel-header">
                <div>
                  <p class="eyebrow">safe write</p>
                  <h2>VS Code 配置预览</h2>
                </div>
              </div>

              <label class="field">
                <span>目标 User 目录</span>
                <select v-model="selectedDirectory">
                  <option v-for="directory in directories" :key="directory.Path" :value="directory.Path">
                    {{ directory.Profile }} · {{ directory.Exists ? '存在' : '待创建' }} · {{ directory.Path }}
                  </option>
                </select>
              </label>

              <button class="primary-button" type="button" :disabled="previewLoading || !selectedDirectory" @click="previewVsCodeConfig">
                {{ previewLoading ? '生成预览中...' : '预览 VS Code Ollama 配置' }}
              </button>

              <div v-if="preview" class="preview-list">
                <div v-for="change in preview.Changes" :key="change.FilePath" class="preview-item">
                  <strong>{{ change.Changed ? '将更新' : '无需变更' }}</strong>
                  <span>{{ change.FilePath }}</span>
                  <small>{{ change.ExistedBefore ? '保留现有文件并只调整托管字段' : '文件不存在，将在确认写入时创建' }}</small>
                </div>
              </div>
            </article>
          </section>
        </section>
      </main>
    </div>

    <footer class="status-bar">
      <span>$(main) main</span>
      <span>HTTP :5124</span>
      <span>SPA :5173</span>
      <span class="right">{{ healthState }}</span>
    </footer>
  </div>
</template>