# VSCopilotSwitch 与 cc switch 差距分析

本文用于校准当前实现和目标产品之间的差距。对照对象是 cc switch 的公开说明与用户手册；结论只取其适合 VSCopilotSwitch 的部分，不把多 CLI 管理能力直接照搬到本项目。

## 对照基准

cc switch 的核心定位不是单纯的“换模型按钮”，而是 AI 编程工具配置中枢：它集中管理 Claude Code、Codex、Gemini CLI、OpenCode、OpenClaw 等工具的供应商配置，并扩展到 MCP、Prompts、Skills、本地代理、故障转移、备份、用量统计和同步。

公开资料里可以归纳出几个关键体验：

- 快速切换优先：主界面和托盘都服务于“当前用哪个供应商、能否立刻切换、切换是否生效”。
- 供应商预设优先：用 50+ 预设、导入现有配置、Deep Link 等方式降低新增供应商成本。
- 配套资产统一管理：MCP、Prompts、Skills、Workspace、Session 不分散在多个目录里手工维护。
- 高可用代理：本地代理不只是转协议，还承担健康监控、故障转移、熔断和请求修正。
- 写入安全：自动备份、原子写入、权限保护和配置版本管理是基础能力。
- 观测闭环：切换后能看请求、会话、Token、费用、余额或配额，验证切换是否真的跑通。

VSCopilotSwitch 的目标更窄也更明确：优先服务 VS Code / GitHub Copilot Chat，通过 Ollama Provider 和 OpenAI-compatible 本地代理把多供应商接入 Copilot，不以多 CLI 全家桶为第一阶段目标。

## 当前我们已经有的东西

- 本地 Ollama 兼容接口：`/api/version`、`/api/tags`、`/api/show`、`/api/chat`。
- OpenAI-compatible 接口：`/v1/models`、`/v1/models/{modelId}`、`/v1/chat/completions` 及常见路径别名。
- 首批 Provider Adapter：sub2api、OpenAI Official、OpenAI-compatible、DeepSeek、Claude、NVIDIA NIM、MoArk。
- VS Code 配置安全写入：`chatLanguageModels.json` 的 `vscs` Ollama Provider 条目、dry-run、字段级 diff、备份、撤销和回滚。
- MewUI 原生工作台：概览、供应商、VS Code、分析、VS2026 面板。
- Win32 托盘：打开主界面、查看当前供应商/模型、快速切换真实供应商、退出。
- 请求分析：内存日志、usage 解析、费用估算、脱敏后的请求/响应摘要。
- VS2026 BYOM 试验：本地 HTTPS、建议填写信息、校验 URL 和聊天 URL。
- Windows Native AOT 单文件发布链路。

这些能力说明底层链路已经不是空壳，但产品形态仍偏“管理后台 + 表单”，还没有形成 cc switch 那种日常切换的操作中枢。

## 主要差距

| 方向 | cc switch 的参照点 | 我们当前状态 | 要补的工作 |
| --- | --- | --- | --- |
| 首屏体验 | 当前供应商、用量、模块入口和快速切换集中呈现 | 概览页能展示状态，但主要信息被拆成卡片和列表，切换动作不够突出 | 重做首屏为“当前生效链路控制台”：供应商、模型、健康、VS Code 配置状态、最近请求、关键动作一屏完成 |
| 快速切换 | 主窗口和托盘都能完成日常切换 | 托盘能切供应商，主窗口切换仍像列表维护动作 | 提供供应商/模型快速选择、切换后自动刷新模型与健康状态，并提示 VS Code 是否需要重新发现 |
| 供应商预设 | 50+ 预设、导入、复用配置片段 | 只有手填表单和一个默认示例 Provider | 增加供应商模板、常见中转站模板、Base URL 规范化、模型推荐、已有配置导入 |
| 模型测试比较 | 延迟、可用性、成本适合测试切换 | 连接测试可用，但没有速度/流式/工具调用对比 | 增加模型测速、流式首 token、工具调用探针、测试结果保存 |
| 高可用路由 | 本地代理、健康监控、故障转移、熔断 | 路由能转发，但熔断、重试、备用路由仍在路线图后续项 | 先实现阶段 6：健康检查、重试、熔断、备用供应商/备用模型、UI 状态解释 |
| 配置写入安全 | 自动备份、权限 600、原子写入、版本保留 | VS Code 写入有备份和回滚，但写入仍是普通写文件；JSONC、跨平台和 WSL 策略待补 | 引入真正原子写入、备份保留策略、JSON with comments 策略、Windows/macOS/Linux/WSL 路径测试 |
| 密钥安全 | 加密、权限保护、多平台安全存储 | 当前是 Windows DPAPI，跨平台 Secret Store 未抽象 | 抽象 Credential Store，后续接 Windows Credential Manager、macOS Keychain、Linux Secret Service |
| 观测闭环 | 请求、会话、费用、趋势、配额集中查看 | 有内存请求日志和费用估算，但无持久会话、趋势和余额/配额 | 增加持久化请求历史、按供应商/模型过滤、趋势图、余额/配额查询模板 |
| MCP / Prompts / Skills | 跨应用统一管理和同步 | 本项目未实现，也不是 VS Code Ollama MVP 的必要条件 | 暂列后做；除非产品范围扩展为多 AI 编程工具配置中心，否则不阻塞当前目标 |
| 多工具/多平台 | 多 CLI、三端安装包、云同步、Deep Link | 当前聚焦 Windows VS Code / VS2026 | 后续再做 macOS/Linux/WSL、安装包签名、Deep Link、WebDAV/云盘同步 |

## 当前最应该做的工作

1. 重做首页为“快速切换控制台”。
   首页第一屏必须回答：当前 Copilot 实际会走哪个供应商、哪个模型、代理是否健康、VS Code 是否已经写入、最近一次请求是否成功。新增、编辑、备份可以继续放在二级区域。

2. 把“启用供应商”升级为“启用路由配置”。
   当前只切 active provider。下一步应支持当前模型、公开模型名、备用模型、能力声明和 VS Code 可见状态一起组成一个路由配置，避免切了供应商但 Copilot 侧感知不清。

3. 优先补阶段 6 稳定性。
   熔断、重试、健康检查、备用路由、限流和 UI 可解释状态，是我们相比 cc switch 最大的硬能力缺口，也是让 Copilot 长任务不中断的关键。

4. 增加供应商预设和测试比较。
   用户不应该每次手填协议类型、Base URL 和模型名。先做少量高质量预设和模型测速，比盲目追 50+ 更符合当前阶段。

5. 加固配置写入和密钥存储。
   VS Code 配置写入已经有安全流程，但还需要真正原子写入、JSONC 策略、备份保留和跨平台 Secret Store 抽象，避免未来扩平台时返工。

## 暂不照搬的范围

- 不把 Claude Code、Codex、Gemini CLI、OpenCode、OpenClaw 的配置管理作为当前主线。
- 不在当前阶段引入 MCP / Prompts / Skills 同步，除非它们直接服务于 VS Code / Copilot 的模型接入。
- 不采用路由劫持、TLS 中间人、域名劫持或复用第三方 Token 的方式换取热切换。
- 不默认暴露局域网代理，也不把真实上游 API Key 写进 VS Code 或其他客户端配置。

## 下一阶段验收口径

- 用户不用进表单页，也能在首页完成一次供应商/模型切换并确认生效。
- 首页能明确显示 VS Code 配置状态：未配置、已配置、URL 不一致、代理不可达、模型列表失败。
- 单个供应商失败时，代理能按策略重试、熔断或切到备用路由，且 UI 和托盘能解释发生了什么。
- 配置写入继续满足 dry-run、备份、差异预览、二次确认和回滚；新增写入路径必须有幂等测试。
- 所有错误、日志、导出和测试结果继续脱敏 API Key、Authorization Header、Cookie 和代理密码。

## 参考资料

- CC Switch 官方英文站：https://cc-switch.cc/en
- CC Switch 产品说明站：https://ccswitch.ai
- CC Switch 用户手册目录：https://github.com/farion1231/cc-switch/blob/main/docs/user-manual/en/README.md
- MoleAPI 的 CC Switch 集成说明：https://docs.moleapi.com/en-US/docs/apps/cc-switch
