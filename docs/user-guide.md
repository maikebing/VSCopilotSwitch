# VSCopilotSwitch 使用指南

本文档面向实际使用者，说明如何把 VSCopilotSwitch 作为本地模型代理，接入 VS Code Copilot Chat、VS2026 BYOM，以及其他兼容 OpenAI / Ollama 协议的工具。

VSCopilotSwitch 的基本思路是：先在本工具里保存并启用一个真实模型供应商，再把本地代理地址写入客户端。客户端只看到一个本地 Ollama 或 OpenAI-compatible 服务，真实请求由 VSCopilotSwitch 转发到当前启用的供应商。

## 使用前准备

- 当前优先支持 Windows 桌面端。
- 准备一个可用的供应商 API Key，例如 sub2api 中转站、OpenAI-compatible 网关、OpenAI Official、DeepSeek、Claude、NVIDIA NIM 或 MoArk。
- 如需接入 VS Code Copilot Chat，确保 VS Code 用户配置目录可写。
- 如需源码运行，需要本机安装 .NET SDK；发布版单文件运行不需要用户安装 .NET SDK。

## 10 分钟快速开始

1. 启动 VSCopilotSwitch。

   发布版直接运行 `VSCopilotSwitch.exe`。源码开发时可在仓库根目录运行：

   ```powershell
   dotnet run --project src/VSCopilotSwitch
   ```

   默认本地代理地址是：

   ```text
   http://127.0.0.1:5124
   ```

2. 新增供应商。

   在首页点击右上角新增按钮，填写供应商名称、协议类型、API 请求地址、模型名称和 API Key。

   API Key 只会以当前 Windows 用户保护数据加密保存，界面和导出配置默认只显示脱敏状态，不会回显明文密钥。

3. 测试连接。

   点击“测试连接”。工具会依次检查 Base URL、API Key、模型列表和一次最小聊天探测。

   如果模型名称先留空，测试连接会尝试从远程模型列表里优先选择 `gpt-5.5`，其次选择 `sonnet-4.6`，否则选择第一个可用模型并回填。

4. 启用供应商。

   在供应商列表点击“启用”。同一时间只会有一个供应商生效。

5. 刷新模型。

   首页模型列表会通过当前启用供应商刷新。暴露给 VS Code / Copilot 的模型名会带 `@vscs` 后缀，例如：

   ```text
   gpt-5.5@vscs
   ```

6. 写入 VS Code 配置。

   进入右上角 `VSCode` 入口，选择 VS Code User 目录，先生成 dry-run 差异预览。确认只会维护 `vscs` Ollama Provider 条目后，再点击“确认写入 VS Code Ollama 配置”。

7. 在 VS Code Copilot Chat 中选择模型。

   打开 Copilot Chat 模型选择器，选择带 `@vscs` 后缀的模型并开始使用。

## 供应商填写说明

| 协议类型 | 适用场景 | API 请求地址示例 | 备注 |
| --- | --- | --- | --- |
| `sub2api` | sub2api 中转站协议 | `https://your-sub2api.example` | 适合中转站统一模型入口。 |
| `openai-compatible` | 通用 OpenAI-compatible 网关 | `https://your-api.example/v1` | 大多数兼容 `/v1/models` 和 `/v1/chat/completions` 的站点可选它。 |
| `openai` | OpenAI 官方接口 | `https://api.openai.com` | 默认访问 `/v1/models` 和 `/v1/chat/completions`。 |
| `deepseek` | DeepSeek 官方接口 | `https://api.deepseek.com` | 默认访问 `/models` 和 `/chat/completions`。 |
| `claude` | Anthropic Claude 官方接口 | `https://api.anthropic.com` | 使用 Messages API，工具调用会转换为 Anthropic tool use。 |
| `nvidia-nim` | NVIDIA NIM / build.nvidia.com | `https://integrate.api.nvidia.com` | 也可填写私有 NIM 网关地址。 |
| `moark` | MoArk 平台 | `https://moark.ai/v1` | 按 OpenAI-compatible 形态接入。 |

填写中转站或公益站地址时，优先确认它提供的是 OpenAI-compatible 还是 sub2api 协议。如果教程里给的是类似 `/v1/chat/completions` 的接口，一般选择 `openai-compatible`；如果明确写了 sub2api，则选择 `sub2api`。

## 接入 VS Code Copilot Chat

VSCopilotSwitch 当前通过 VS Code 的 Ollama Provider 配置接入，不需要安装额外 VS Code 扩展。

写入前工具会读取并备份：

```text
%APPDATA%\Code\User\chatLanguageModels.json
```

写入内容只维护本项目的 Provider 条目：

```json
{
  "name": "vscs",
  "vendor": "ollama",
  "url": "http://127.0.0.1:5124"
}
```

注意事项：

- 必须先生成 dry-run 差异预览，再二次确认写入。
- 工具会保留其他未知 Provider 和用户自定义配置。
- 重复写入不会产生重复条目。
- 撤销时只移除 `vscs` 条目，不删除其他配置。
- 备份页可以恢复最近的 VSCopilotSwitch 配置备份。

VS Code 模型发现会访问 `/api/version`、`/api/tags` 和 `/api/show`。真实聊天当前主要走 OpenAI-compatible 的 `/v1/chat/completions`，所以请求日志里看到该路径是正常现象。

## 接入 VS2026 BYOM

发布版默认会尝试启用本机 HTTPS 入口：

```text
https://127.0.0.1:5443
```

右上角 `VS2026` 入口会展示 Manage Models 建议填写值：

| 字段 | 填写值 |
| --- | --- |
| Provider | `Azure` |
| Resource Endpoint / Custom URL | `https://127.0.0.1:5443/v1` |
| Model ID | 当前模型名，例如 `gpt-5.5@vscs` |
| API Key | `vscs-local` |

该 HTTPS 证书只覆盖 `localhost`、`127.0.0.1` 和 `::1`，并写入当前用户证书库。开发环境默认不自动启用 HTTPS，可用环境变量显式开启：

```powershell
$env:VSCOPILOTSWITCH_HTTPS_URL = "https://127.0.0.1:5443"
dotnet run --project src/VSCopilotSwitch
```

如果 `5443` 被占用，可改成其他本机回环端口。

## 接入 Codex CLI、VS Code 插件或其他 OpenAI-compatible 客户端

如果外部工具支持自定义 OpenAI-compatible Base URL，可以直接指向 VSCopilotSwitch：

```text
Base URL: http://127.0.0.1:5124/v1
Model: gpt-5.5@vscs
API Key: vscs-local
```

本地 OpenAI-compatible 接口包括：

- `GET /v1/models`
- `GET /v1/models/{modelId}`
- `POST /v1/chat/completions`

部分客户端会把基址拼成 `/openai/v1/...`，VSCopilotSwitch 也提供了兼容别名。

API Key 字段目前只是给客户端通过本地校验用的占位值；真实上游 API Key 仍在 VSCopilotSwitch 的供应商配置里加密保存。不要把真实上游密钥填进外部客户端日志可见的位置。

## 接入 Ollama-compatible 客户端

如果工具支持 Ollama 地址，可填写：

```text
http://127.0.0.1:5124
```

可用接口包括：

- `GET /api/version`
- `GET /api/tags`
- `POST /api/show`
- `POST /api/chat`

`/api/chat` 支持非流式和流式响应，也支持 Ollama 官方 `tools`、`think` 和 `message.thinking` 的最小兼容面。

## 托盘与日常切换

VSCopilotSwitch 运行后会出现在 Windows 系统托盘。

- 关闭主窗口只会隐藏到托盘，本地代理继续运行。
- 托盘菜单可以打开或聚焦主界面。
- 托盘菜单会显示当前供应商和模型。
- 已保存 API Key 和模型名的真实供应商可在托盘里快速切换。
- 只有托盘“退出”会停止宿主进程和本地代理。

## 分析统计和费用估算

右上角“分析统计”入口可以查看本地请求日志、监听端口、耗时、User-Agent、usage 和费用估算。

日志会脱敏 Authorization、Cookie、API Key、Token 等敏感字段；请求体和响应体也会限制采样长度。

费用按本地 `UsagePricing` 单价表计算，单位是每百万 Token。示例：

```json
{
  "UsagePricing": {
    "Currency": "USD",
    "Models": [
      {
        "ModelPattern": "gpt-5.5",
        "Label": "gpt-5.5 custom",
        "InputPerMillionTokens": 2.0,
        "OutputPerMillionTokens": 10.0
      }
    ]
  }
}
```

没有配置单价时，请求会显示为“未计价”。

## 常见问题

### VS Code 模型选择器看不到 `@vscs` 模型

先确认 VSCopilotSwitch 正在运行，再在浏览器访问：

```text
http://127.0.0.1:5124/api/tags
```

如果这里没有模型，回到首页测试当前供应商连接。如果这里有模型，重新打开 VS Code Copilot Chat 模型选择器，或重启 VS Code。

### 写入 VS Code 配置失败

优先看界面提示的具体原因。常见处理方式：

- JSON 无效：先修复 `chatLanguageModels.json` 的格式。
- 文件占用：关闭 VS Code 后重试。
- 权限不足：确认选择的是当前用户可写的 `Code\User` 目录。
- 目录选错：重新选择 `%APPDATA%\Code\User`。

### 上游模型列表失败

如果已经保存模型名，VSCopilotSwitch 会尽量用当前配置模型生成降级清单，避免 VS Code 直接收到 503。后续仍建议检查 Base URL、API Key、网络代理和供应商额度。

### Copilot 报限流或网络错误

打开分析统计查看本地错误分类。真实限流会返回 429；供应商网络不可用会映射为 502，避免被误判成限流。错误消息会脱敏，不会展示 API Key 原文。

### DeepSeek thinking 或 Agent 工具回合失败

DeepSeek thinking 专用链路只在请求携带 `reasoning_effort`、`thinking`、Ollama `think`，或模型名匹配推理模型时启用。若 Agent 工具回合遇到 reasoning 相关 400，重新发起任务通常可恢复；VSCopilotSwitch 会在同一进程内尝试缓存并补回上一轮 reasoning 内容。

## 安全边界

- 本地代理默认只监听 `127.0.0.1`。
- 不要把代理地址暴露到局域网或公网。
- 上游 API Key 只应保存在 VSCopilotSwitch 的供应商配置中。
- 导出配置默认不包含密钥原文、脱敏预览或加密密文。
- 修改 VS Code 配置前必须看 dry-run 差异，并确认备份路径。
