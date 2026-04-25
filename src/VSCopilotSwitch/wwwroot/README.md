# ASP.NET Core 静态资源根目录

此目录用于满足 Web SDK 的默认 WebRootPath 约定。开发模式下 SPA 页面由 `Microsoft.AspNetCore.SpaProxy` 转发到 `src/VSCopilotSwitch.Ui` 的 Vite 服务；发布模式后续会优先使用嵌入式 SPA 资源。
