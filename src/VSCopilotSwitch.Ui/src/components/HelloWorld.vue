<script lang="ts">
import { defineComponent } from 'vue';

type HealthStatus = {
  name: string;
  status: string;
  mode: string;
};

interface Data {
  loading: boolean;
  post: null | HealthStatus;
}

export default defineComponent({
  data(): Data {
    return {
      loading: false,
      post: null
    };
  },
  async created() {
    await this.fetchData();
  },
  methods: {
    async fetchData() {
      this.post = null;
      this.loading = true;

      const response = await fetch('/health');
      if (response.ok) {
        this.post = await response.json();
      }

      this.loading = false;
    }
  }
});
</script>

<template>
  <div class="status-component">
    <h1>VSCopilotSwitch</h1>
    <p>此组件使用 VueApp2 的 TypeScript SPA 技术栈，并通过代理读取当前宿主状态。</p>

    <div v-if="loading" class="loading">
      正在连接 ASP.NET 后端，请确认宿主项目已经启动。
    </div>

    <div v-if="post" class="content">
      <table>
        <thead>
          <tr>
            <th>服务</th>
            <th>状态</th>
            <th>模式</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td>{{ post.name }}</td>
            <td>{{ post.status }}</td>
            <td>{{ post.mode }}</td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<style scoped>
th {
  font-weight: bold;
}

th,
td {
  padding-left: 0.5rem;
  padding-right: 0.5rem;
}

.status-component {
  text-align: center;
}

table {
  margin-left: auto;
  margin-right: auto;
}
</style>
