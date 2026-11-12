# NIKKE 双服切换

官服 / 本机私服一键切换的 Windows 小工具。自包含单文件，目标机器不用装 .NET 运行时。

![界面](docs/screenshot.png)

## 怎么跑

**[下载最新版](https://github.com/theifxzx666-blip/Nikke_DualClient/releases/latest)** → 解压 → 双击 `NikkeDualClient.exe`（会弹 UAC，要改 hosts 和本机证书库）。

只有两个主按钮：

| 点击 | 工具做的事 |
| :--- | :--- |
| 启动单机版 | 体检 → 缺 hosts 补 hosts / 缺证书装证书 / 新闻页脏就重写 / 服务没起就起 → 开启动器 |
| 启动官服 | 体检 → 有私服残留就停服务、还原 hosts、卸载证书 → 开官服启动器 |

首次运行自动扫官服 / 私服目录，扫不到会弹框让你各点一次，选完记住。

## 不做什么

- **不注入、不改包、不读游戏内存**，不做任何游戏内自动化。
- **不读写 `auth.txt`**（卖家的激活凭证）。工具只重启 EpinelPS，而校验只在服务端启动时跑一次 —— 「换设备后要求激活」的变量在这里，不在工具。
- **不强杀游戏进程**。有 `nikke` / `nikke_launcher` / `tbs_browser` / `intl_service` 在跑就停下来提示你关。
- **不支持双服同时在线**。hosts 和 443 是 Windows 级设置，拆目录解决不了。

## 目录

```
Program.cs       入口 + CLI 分发
Core/            配置 / 定位 / hosts / 证书 / 进程 / 新闻页 / 编排
Ui/              主窗体 / 设置页 / 裁切画布 / 主题
正式产物/         当前可运行副本 + 分发包（不进版本库）
```

## 构建

需要 .NET 10 SDK。临时目录指到项目盘，别往 C 盘写缓存：

```powershell
$root = $PWD
$env:TEMP = "$root\临时车间"; $env:TMP = "$root\临时车间"
$env:NUGET_PACKAGES = "F:\Codex\依赖\nuget-dualclient"
dotnet publish NikkeDualClient.csproj -c Release -o .\正式产物
```

产物是 47 MB 单文件。**不要开 `PublishTrimmed`**，WinForms 不支持。

## 更多

命令行接口（`--verify` / `--start` / `--restore` …）、设计取舍、两个必踩的坑：见 [`核心目录/技术说明.md`](核心目录/技术说明.md)。

## 许可

未附开源许可文件；对外分发只走 Releases 里的 exe。图标与背景素材自制。
