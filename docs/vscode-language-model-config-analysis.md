# VS Code 语言模型配置观察

记录时间：2026-04-28

本次只读检查 Windows Stable VS Code 配置，目标是为后续重写 VS Code 配置写入功能确定真实文件格式和边界。以下路径使用环境变量表达，避免把固定用户名写入实现。

## 真实生效文件

VS Code 语言模型界面实际维护的是：

- `%APPDATA%\Code\User\chatLanguageModels.json`

当前文件是 JSON 数组，不是对象。已观察到的结构如下：

```json
[
  {
    "name": "OpenAI",
    "vendor": "openai"
  },
  {
    "name": "OpenRouter",
    "vendor": "openrouter"
  },
  {
    "name": "Anthropic",
    "vendor": "anthropic"
  },
  {
    "name": "Google",
    "vendor": "gemini"
  },
  {
    "name": "Copilot",
    "vendor": "copilot",
    "settings": {
      "gpt-5.4": {
        "reasoningEffort": "xhigh"
      },
      "gpt-5.5": {
        "reasoningEffort": "xhigh"
      }
    }
  },
  {
    "name": "vscc",
    "vendor": "ollama",
    "url": "http://localhost:5124"
  }
]
```

这说明外部 Ollama Provider 只需要写入 Provider 条目：`name`、`vendor`、`url`。模型列表不是静态写入到该文件，而是 VS Code 通过该 `url` 调用 Ollama 兼容接口动态发现。

## 当前项目残留文件

本次还观察到 `%APPDATA%\Code\settings.json` 与 `%APPDATA%\Code\chatLanguageModels.json` 存在 VSCopilotSwitch 早期写入残留：

```json
{
  "vscopilotswitch.managedBy": "VSCopilotSwitch",
  "vscopilotswitch.ollama.baseUrl": "http://localhost:5124",
  "vscopilotswitch.ollama.enabled": true
}
```

以及包含 `vscopilotswitch.models` 的自定义模型清单，例如 `gpt-5.2@vscc`。这类根目录文件不是 VS Code User 配置目录里的真实语言模型 Provider 文件，后续重写不应继续依赖它们。

## 现有实现差异

当前 `VsCodeConfigService` 的问题：

- 写入 `settings.json` 的 `vscopilotswitch.*` 自定义字段，VS Code 语言模型界面不会直接消费这些字段。
- 写入 `chatLanguageModels.json` 时假设根节点是对象，并创建顶层 `vscopilotswitch` 节点；真实文件根节点是数组。
- 写入静态模型清单；真实模型由 Ollama `/api/tags`、`/api/show` 动态发现。
- 目录选择 UI 需要明确目标是 `%APPDATA%\Code\User`，不能把 `%APPDATA%\Code` 根目录当作 User 配置目录。

## 重写建议

后续实现应围绕 `Code\User\chatLanguageModels.json` 做最小、幂等、可回滚修改：

- 读取数组，保留所有未知 Provider 条目和 Copilot 的 `settings`。
- 只管理 `vendor == "ollama"` 且 `name` 等于本项目配置名的条目，建议默认名称继续使用 `vscc`。
- 写入值为 `{ "name": "vscc", "vendor": "ollama", "url": "<当前本地代理地址>" }`。
- 如果同名条目已存在，只更新 `url`；如果不存在，追加一个条目；重复执行不能产生重复 `vscc`。
- 撤销时只删除本项目管理的 `vscc` Ollama 条目，不删除用户手工添加的其他 Ollama Provider。
- 状态检测应检查数组中是否存在匹配 Provider，并验证 `url` 与当前代理地址是否一致。
- 字段级 diff 应展示 Provider 条目的新增、更新或删除，而不是展示静态模型列表。
- 旧版 `%APPDATA%\Code\*.vscopilotswitch.*` 残留只能作为迁移/清理提示，不应默认静默删除。

