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
  <main class="app-shell">
    <section class="hero panel">
      <div>
        <p class="eyebrow">Ollama compatible model switcher</p>
        <h1>VSCopilotSwitch</h1>
        <p class="hero-text">
          统一管理多供应商模型，安全预览 VS Code Copilot 配置变更，再由本地代理转换为 Ollama 兼容协议。
        </p>
      </div>
      <button class="ghost-button" type="button" :disabled="loading" @click="loadDashboard">
        {{ loading ? '刷新中...' : '刷新状态' }}
      </button>
    </section>

    <p v-if="errorMessage" class="alert">{{ errorMessage }}</p>

    <section class="grid">
      <article class="panel status-card">
        <span class="card-label">代理状态</span>
        <strong>{{ health?.status ?? 'unknown' }}</strong>
        <p>{{ health?.name ?? 'VSCopilotSwitch' }} · {{ health?.mode ?? '未连接' }}</p>
      </article>

      <article class="panel status-card">
        <span class="card-label">当前供应商</span>
        <strong>内置占位 Provider</strong>
        <p>后续会接入 sub2api、OpenAI、Claude、DeepSeek、NVIDIA NIM 和 Moark。</p>
      </article>

      <article class="panel status-card">
        <span class="card-label">VS Code 配置</span>
        <strong>{{ existingDirectories.length }} 个可用目录</strong>
        <p>{{ selectedDirectoryDescription }}</p>
      </article>
    </section>

    <section class="content-grid">
      <article class="panel">
        <div class="section-title">
          <div>
            <p class="eyebrow">models</p>
            <h2>模型列表</h2>
          </div>
          <span>{{ models.length }} 个模型</span>
        </div>

        <div class="model-list">
          <div v-for="model in models" :key="model.digest" class="model-item">
            <div>
              <strong>{{ model.name }}</strong>
              <p>{{ model.details.family }} · {{ model.details.parameter_size }}</p>
            </div>
            <span>{{ model.details.quantization_level }}</span>
          </div>
          <p v-if="models.length === 0" class="muted">暂无模型数据，请刷新状态。</p>
        </div>
      </article>

      <article class="panel">
        <div class="section-title">
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
  </main>
</template>
