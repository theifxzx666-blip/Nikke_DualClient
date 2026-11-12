# 双服切换 · 工位契约

> 工位 ｜ 域：客户端运维 ｜ 上位契约：`D:\Codex\Nikke\AGENTS.md`
> 记忆：`核心目录\agent\agent.md` ｜ 独立仓：`Nikke_DualClient`

## 边界

**做**：官服 / 本机私服一键切换（hosts、证书、服务、新闻页），自包含单文件客户端。
**不做**：不改游戏内容；不绕过登录。

## 入口

| 入口 | 说明 |
| :--- | :--- |
| `NikkeDualClient.csproj` | .NET 工程，`dotnet build` 出包 |
| `Program.cs` | 主流程（切换 / 还原 / `--verify`） |

## 目录

`Core\` 切换逻辑 ｜ `Ui\` 界面 ｜ `Assets\` 图标与资源 ｜ `正式产物\` 已验收 exe ｜ `bin\` `obj\` 构建产物（不入库）
