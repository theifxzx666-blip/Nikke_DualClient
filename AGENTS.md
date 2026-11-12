# 双服切换 · 协作契约

> 所属域：`02_工位\50_客户端运维\` ｜ 形态：**B 工具开发**（根层文件 ≤8，现 6）｜ 状态：🟢 活跃 ｜ 最后更新：2026-11-12
> 上级契约：`F:\Codex\Nikke\AGENTS.md`（冲突时以上级为准）｜ 形态定义：`00_规范\架构总纲_v5.md` §4
> 独立 git 仓：`github.com/theifxzx666-blip/Nikke_DualClient`（`origin` / `main`）

---

## 1. 边界

| 项 | 值 |
| :--- | :--- |
| 做什么 | 官服 / 本机私服一键切换：hosts 区块、根证书、EpinelPS 服务、启动器新闻页 |
| 不做什么 | 不注入、不改包、不读游戏内存；不做游戏内自动化；不读写 `auth.txt`；不强杀用户进程 |
| 消费母体 | 无 |
| 被谁消费 | 对外分发（买家 / 群友，单文件 exe）；GM 工具经 CLI 调用 |

**与同域 `单机版处理` 的分工**：

- `单机版处理` 管**部署内容**——用上游 checkout 覆盖 `EpinelPS` 本地部署（默认 dry-run，`-Apply` 才落地）；
- `双服切换` 管**运行环境**——hosts / 证书 / 进程 / 新闻页的开关，不碰 `wwwroot` 里的服务端文件本体。

两者可同时使用，作用面不重叠。**注意：`单机版处理` 的 `-Apply` 会用未打补丁的上游 exe 覆盖部署，可能改变 hosts 改写行为**——覆盖后要重跑一次本工具 `--verify`。

**与游戏目录里同名脚本的区别**：`F:\[Game]Nikke_Local\双服切换\` 是 v1.0 的 PowerShell 版（`config.json` + `NikkeSwitch.ps1` + 5 个 cmd），**不在本仓**，功能已被本工位 GUI 版覆盖。改这边不影响那边，反之亦然。

---

## 2. 入口

| 用途 | 路径 / 命令 |
| :--- | :--- |
| 用户入口 | `正式产物\NikkeDualClient.exe`（自包含单文件，弹 UAC） |
| 构建 | 见 §4 |
| 线上 | `https://github.com/theifxzx666-blip/Nikke_DualClient/releases` |
| 技术细节 | `核心目录\技术说明.md`（设计取舍 + 两个必踩的坑 + v1.1–v1.11 逐版记录） |

**CLI**（WinExe 自 `AttachConsole`，输出 UTF-8 JSON，供 GM 工具调）：
`--verify` `--restore` `--repair` `--start private|official` `--scan` `--diag <path>` `--probe` `--settings` `--shot`

退出码：`0` 官服基线 ｜ `1` 私服或中间态 ｜ `2` 出错。

---

## 3. 目录

```
双服切换/                         # B 型，根层文件 6/8
├─ AGENTS.md                      本契约
├─ README.md                      对外说明（≤60 行，给下载用户看）
├─ Program.cs                     入口 + CLI 分发（269 行）
├─ NikkeDualClient.csproj         net10.0-windows / WinForms / v1.12.0
├─ app.manifest                   requireAdministrator
├─ .gitignore                     产物 / 临时 / 回收站 / 运行态不入库
├─ Core/                          13 个 .cs：配置 / 定位 / hosts / 证书 / 进程 / 新闻页 / 编排
├─ Ui/                            6 个 .cs：主窗体 / 设置页 / 裁切画布 / 主题 / 图标 / 原生互操作
├─ Assets/                        app.ico（7 尺寸）+ app-icon.png
├─ docs/                          screenshot.png（README 引用）
├─ 核心目录/agent/agent.md        本工位记忆（决策 / 踩坑 / 待办）
├─ 核心目录/技术说明.md           原 README 全文
├─ 核心目录/报告/                 诊断导出（v1.8 / v1.10 的 diag）
├─ 正式产物/                      当前可运行副本 + v1.12 分发包（兼作运行目录）
├─ log/                           dotnet SDK 安装日志 68 份
├─ 临时车间/                      构建临时目录（TEMP/TMP 指向此）
├─ 回收站/cleanup_20261112-1710/  并入时清出的旧包（v1.9–v1.11 zip、重复 exe）
├─ bin/ obj/                      构建输出（L3，不入库）
└─ .git/                          独立仓，1.3 MB
```

**规模（2026-11-12 实测）**：源码 4,262 行（`Program.cs` 269 + `Core`/`Ui` 3,993）；全目录 399 文件 / 532 MB，其中 `bin`+`obj` 123 MB、`回收站` 319 MB、`正式产物` 91 MB，源码与资源仅约 1 MB。

**`docs\` 与 `Assets\` 不归入 `资源库\` 的理由**：`docs/screenshot.png` 是 README 的图片引用路径，改名即 GitHub 图裂；`Assets\*.ico` 被 `.csproj` 的 `ApplicationIcon` / `EmbeddedResource` 直接引用，改名即构建失败。两者属**构建与渲染约定目录**，不是散落素材。

---

## 4. 验证顺序

在工位根执行（`$root` = 本目录）：

```powershell
$root = $PWD
$env:TEMP  = "$root\临时车间"; $env:TMP = "$root\临时车间"
$env:DOTNET_CLI_HOME = "F:\Codex\依赖\dotnet-home-dualclient"
$env:NUGET_PACKAGES  = "F:\Codex\依赖\nuget-dualclient"
dotnet build NikkeDualClient.csproj -c Release
```

1. `dotnet build` → 期望 `0 Error(s)`，且无 `WFO1000` 报错
2. `正式产物\NikkeDualClient.exe --verify` → 期望 JSON 有 `"ok": true`，`mode` 为 `official`/`private`，退出码 0 或 1
3. `正式产物\NikkeDualClient.exe --restore` → 期望 hosts 无 `NIKKE-PRIVATE` 区块、证书 0 张、退出码 0

> `--start private|official` 会改本机 hosts、动证书库、起停 EpinelPS，**验收不要随手跑**；要跑先确认游戏与启动器全部退出。
> 「能跑起来」不算通过：GUI 改动用 `--shot` 出图核对，别只看进程是否存活。

---

## 5. 技能表

| 技能组 | status | 能力点 | 载体 |
| :--- | :-: | :--- | :--- |
| `[Skill]环境切换` | ready | 两个入口各自收敛到目标态，幂等；已完整在跑时短路不折腾 | `Core\EnvController.cs` |
| `[Skill]hosts治理` | ready | 标记区块独占解析，Latin1 字节透明读写，每次写前备份 | `Core\HostsManager.cs` |
| `[Skill]证书装卸` | ready | 按指纹精确装卸私服根证书，不清库 | `Core\CertManager.cs` |
| `[Skill]新闻页修复` | ready | 按内容判污染（`taobao.com` / `毛哥` / `9.png` / `QQ群`），重写 HTML + 重算 `.gz` + 移走 `.br` | `Core\NewsRepair.cs` |
| `[Skill]目录定位` | ready | 认特征文件组合，先常见名再扫盘两层，都失败才弹选择框 | `Core\Locator.cs` |
| `[Skill]进程脱管` | ready | WMI `Win32_Process.Create` 起服务，父进程变 `WmiPrvSE` | `Core\ProcessLauncher.cs` |
| `[Skill]诊断导出` | ready | 环境状态 + 日志 + `auth.txt` 机器码比对（订单号前 4 位脱敏） | `Core\Diagnostics.cs` |
| `[Skill]CLI集成` | ready | JSON + 退出码，供 GM 工具解析 | `Program.cs` |

---

## 6. 待办（工位级）

> 只记本工位内部技术债。跨工位 / 需用户裁决 / 有前置依赖的，上提 `PROJECT_STATUS.md` §六。

| # | 项 | 前置 | 优先级 |
| :-: | :--- | :--- | :-: |
| 1 | GitHub 标签停在 `v1.11`，`csproj` 已是 `1.12.0`，v1.12 包已构建但未发 Release | 用户授权推送 / 发版 | P1 |
| 2 | `正式产物\使用说明.txt` 仍是 **v1.10** 文案（GBK 编码），与 v1.12 设置页不符 | 无 | P1 |
| 3 | GUI 回归靠 `--shot` 人工看图，无像素基线，改版后要靠肉眼比 | 无 | P2 |
| 4 | 旧 PowerShell 版 `[Game]Nikke_Local\双服切换\` 与 GUI 版功能重叠，未裁决是否回收 | 用户裁决 | P2 |
| 5 | `bin`+`obj`（123 MB）与 `回收站`（319 MB）可释放；`回收站` 需按批次授权清空 | 用户授权 | P3 |

---

## 7. 红线

1. **起 EpinelPS 必须走 WMI**（`ProcessLauncher`）。用 `Process.Start` 会被父进程的作业对象带走，工具退出后服务静默消失。
2. **不读写 `auth.txt`**。工具只重启服务，重启会重新执行服务端启动校验——这是「换设备后要求激活」的唯一变量，别往工具身上引。
3. **不强杀** `nikke` / `nikke_launcher` / `tbs_browser` / `intl_service`：强杀可能丢进度。只有工具自己起的 `EpinelPS.exe` 可以自动停。
4. **改 `.csproj` 一律用编辑器整体重写**，不要走 PowerShell 文本管道——中文按 GBK 读进 UTF-8 会乱码，`<Compile Remove="临时\**">` 变成乱码后规则失效，废弃文件又被编进产物。
5. **`docs\` 与 `Assets\` 的路径不得改**（README 图链 / csproj 资源引用）。
6. **不开 `PublishTrimmed`**：WinForms 不支持。
7. `使用说明.txt` 面向买家，保持 **GBK** 编码（用户侧记事本默认按 ANSI 打开）；仓库内 `.md` 用 UTF-8。
