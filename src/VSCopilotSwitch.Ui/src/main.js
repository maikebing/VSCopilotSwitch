import { createApp } from 'vue';
import './style.css';

const providers = [
  { id: 'sub2api', name: 'sub2api', status: 'planned', accent: '#d97706', models: ['gpt-compatible', 'claude-compatible'] },
  { id: 'openai', name: 'OpenAI Official', status: 'planned', accent: '#0f766e', models: ['gpt-4.1', 'o-series'] },
  { id: 'claude', name: 'Claude Official', status: 'planned', accent: '#b45309', models: ['claude-sonnet', 'claude-opus'] },
  { id: 'deepseek', name: 'DeepSeek', status: 'planned', accent: '#2563eb', models: ['deepseek-chat', 'deepseek-reasoner'] },
  { id: 'nvidia', name: 'NVIDIA NIM', status: 'planned', accent: '#65a30d', models: ['nim-build'] },
  { id: 'moark', name: 'Moark', status: 'planned', accent: '#be123c', models: ['moark-default'] }
];

const App = {
  data() {
    return {
      health: 'checking',
      models: [],
      directories: [],
      selectedProvider: providers[0].id,
      dryRunResult: null,
      error: ''
    };
  },
  computed: {
    providers() {
      return providers;
    },
    activeProvider() {
      return providers.find(provider => provider.id === this.selectedProvider) ?? providers[0];
    },
    activeModels() {
      return this.models.length > 0 ? this.models : [{ name: 'vscopilotswitch/default', details: { family: 'local-mvp' } }];
    },
    existingDirectories() {
      return this.directories.filter(directory => directory.exists);
    }
  },
  async mounted() {
    await Promise.all([this.loadHealth(), this.loadModels(), this.loadDirectories()]);
  },
  methods: {
    async loadHealth() {
      try {
        const response = await fetch('/health');
        const data = await response.json();
        this.health = data.status ?? 'unknown';
      } catch (error) {
        this.health = 'offline';
        this.error = String(error);
      }
    },
    async loadModels() {
      try {
        const response = await fetch('/api/tags');
        const data = await response.json();
        this.models = data.models ?? [];
      } catch (error) {
        this.error = String(error);
      }
    },
    async loadDirectories() {
      try {
        const response = await fetch('/internal/vscode/user-directories');
        this.directories = await response.json();
      } catch (error) {
        this.error = String(error);
      }
    },
    async previewConfig(directory) {
      try {
        const response = await fetch('/internal/vscode/apply-ollama', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            userDirectory: directory.path,
            dryRun: true,
            config: {
              baseUrl: 'http://127.0.0.1:11434',
              models: this.activeModels.map(model => ({
                id: model.name,
                displayName: model.name,
                providerModelId: model.model ?? model.name
              }))
            }
          })
        });
        this.dryRunResult = await response.json();
      } catch (error) {
        this.error = String(error);
      }
    }
  },
  template: `
    <main class="shell">
      <section class="hero panel">
        <div>
          <p class="eyebrow">OmniHost-ready Vue 3 SPA</p>
          <h1>VSCopilotSwitch</h1>
          <p class="lead">把第三方模型供应商统一转换为 Ollama 兼容入口，并安全维护 VS Code 配置。</p>
        </div>
        <div class="hero-status">
          <span class="pulse" :class="health"></span>
          <strong>{{ health }}</strong>
          <small>Local proxy 127.0.0.1:11434</small>
        </div>
      </section>

      <section class="switchboard">
        <aside class="provider-list panel">
          <div class="section-title">
            <span>Provider</span>
            <strong>当前提供商</strong>
          </div>
          <button
            v-for="provider in providers"
            :key="provider.id"
            class="provider-button"
            :class="{ active: selectedProvider === provider.id }"
            :style="{ '--accent': provider.accent }"
            @click="selectedProvider = provider.id"
          >
            <span>{{ provider.name }}</span>
            <small>{{ provider.status }}</small>
          </button>
        </aside>

        <section class="workspace panel">
          <div class="section-title">
            <span>Routing</span>
            <strong>{{ activeProvider.name }}</strong>
          </div>
          <div class="route-card" :style="{ '--accent': activeProvider.accent }">
            <p>当前 UI 已按 cc switch 的快速切换思路组织。真实 Provider Adapter 接入后，这里会直接控制路由目标、熔断状态和 VS Code 写入配置。</p>
            <div class="chips">
              <span v-for="model in activeProvider.models" :key="model">{{ model }}</span>
            </div>
          </div>

          <div class="model-grid">
            <article v-for="model in activeModels" :key="model.name" class="model-card">
              <span class="label">Ollama Model</span>
              <strong>{{ model.name }}</strong>
              <p>{{ model.details?.family ?? 'provider-adapter' }}</p>
            </article>
          </div>
        </section>
      </section>

      <section class="bottom-grid">
        <article class="panel vscode-panel">
          <div class="section-title">
            <span>VS Code</span>
            <strong>配置预览</strong>
          </div>
          <p>只做 dry-run 预览，不会写入真实配置。正式写入时后端会先备份已有文件。</p>
          <div class="directory-list">
            <button v-for="directory in directories" :key="directory.path" @click="previewConfig(directory)">
              <span>{{ directory.description }}</span>
              <small>{{ directory.exists ? 'found' : 'missing' }} · {{ directory.path }}</small>
            </button>
          </div>
        </article>

        <article class="panel tray-panel">
          <div class="section-title">
            <span>Tray</span>
            <strong>托盘菜单规划</strong>
          </div>
          <ul>
            <li>打开或聚焦主界面</li>
            <li>显示当前提供商与模型</li>
            <li>快速切换提供商</li>
            <li>安全退出并停止代理</li>
          </ul>
        </article>
      </section>

      <section v-if="dryRunResult" class="panel diff-panel">
        <div class="section-title">
          <span>Dry Run</span>
          <strong>{{ dryRunResult.userDirectory }}</strong>
        </div>
        <div class="diff-list">
          <article v-for="change in dryRunResult.changes" :key="change.filePath">
            <strong>{{ change.changed ? 'will change' : 'no change' }}</strong>
            <p>{{ change.filePath }}</p>
          </article>
        </div>
      </section>

      <p v-if="error" class="error">{{ error }}</p>
    </main>
  `
};

createApp(App).mount('#app');
