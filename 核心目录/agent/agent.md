# 双服切换 · agent 记录

## 目标
官服 / 本机私服一键切换，自包含单文件客户端，目标机不需 .NET 运行时。

## 本轮（2026-11-12 v6 归位）
- 从 `Nikke_DualClient\` 迁入 `02_工位\50_客户端运维\双服切换\`。
- 独立仓 `.git` 随目录迁移，未改动。

## 本轮（2026-11-12 v1.12.1 启动器误报修复）
- 现象：`D:\Backup\Downloads` 明明有 `NIKKE\Launcher\nikke_launcher.exe`，工具报「私服客户端启动器缺失」。
- 根因：`PrivateLauncherRel` 写死 `NIKKEG\Launcher\nikke_launcher.exe`，一键包目录名不叫 NIKKEG 就必然误判。
- 决策：新增 `Core\LauncherFinder.cs`，三级回退——配置相对路径 → 6 条常见布局 → BFS 浅搜 3 层（上限 2000 目录，跳过系统目录，排除官服启动器）。
- 决策：`TargetFramework` net10.0-windows → net8.0-windows。本机只有 SDK 8.0.401，net10 编不了；自包含单文件发布不依赖目标机运行时。
- 改点：`EnvController.Check/GoPrivate`、`Runtime.StartClient`、`SelectorSync`（gameRoot 改为按目录名判断，不再盲砍两层）、`SettingsForm` 路径页显示实际认到的启动器、`Diagnostics`。
- 新增 CLI：`--find-launcher <root>`，只探不写，验收/排障用。
- 验收：`D:\Codex\Nikke\临时\_launcher_test\` 四个用例（NIKKEG / NIKKE / 自建中文目录 / 空目录）全部符合预期；真实 `E:\[3]Nikke` 命中 `NIKKEG\Launcher\nikke_launcher.exe`；`--diag` 与 `--shot` 均正常。
- 踩坑：exe 带 `requireAdministrator` manifest，非提权 shell 直接 `Permission denied`（exit 126）→ CLI 验收必须走 `dotnet NikkeDualClient.dll ...`。

## 本轮（2026-11-12 v1.12.2 官服解析 + 噪声进程）
- 现象：官服根填 `D:\NIKKE`（游戏目录本身），工具找 `D:\NIKKE\NIKKE\Launcher\nikke_launcher.exe` → 「官服 ×」。
- 决策：`ResolveOfficial` 与私服同一套三级回退，额外覆盖「少一层 Launcher」和 `nikkeminiloader*.exe` 通配兜底。
- 决策：`ProcessScanner.IsClient` 排除 `crashpad_handler` 与 `GameViewer*`（网易 UU 加速器）。这两类进程在启动器退出后常成孤儿，算进「要关的客户端」会让用户永远切不了。
- 验收：官服 off1(标准)/off2(少一层)/off3(miniloader)/off_empty 全符合；私服 case1~3 回归通过；真实 E 盘两个根都命中；`--diag` 两行 √；`--shot` 显示「启动器 正常 官服√ 私服√」。

## 待办
- [x] `dotnet build` 复核可编译（net8，0 警告 0 错误）
- [ ] 切换/还原各跑一次，确认 hosts 与证书状态可回滚
- [ ] 老包回归：确认 NIKKEG 标准包仍走第一条路径，不走搜索
- [ ] 现场核对：一台机的 EpinelPS 实际运行在 `D:\nikke153.6.10\EpinelPS`，但工具配置的私服根是 `D:\极速下载好的文件\nikke153.6.10`，两处不是同一份，需让用户确认哪份在用
