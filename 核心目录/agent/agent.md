# 双服切换 · 记忆

> 本文件是该工位的唯一长期记忆。每次实质改动后追加，不覆盖历史。
> 最近更新：2026-11-12（并入工作站，从 `F:\Codex\NikkeDualClient` 迁入）

---

## 一、目标与结果

| 日期 | 目标 | 结果 | 证据 |
| :--- | :--- | :--- | :--- |
| 2026-11-11 | 把「官服/私服切换」从手改 hosts 收敛成 GUI | v1.0 PowerShell 版落在 `[Game]Nikke_Local\双服切换\`（脚本组） | 该目录 7 个文件 |
| 2026-11-11 | 做成可发给买家/群友的自包含单文件 | v1.1 无感化 → v1.11，WinForms + .NET 10 自包含，约 47 MB | `--verify`/`--restore`/`--start` 全通过；GUI 标题与句柄实测 |
| 2026-11-12 | 按工作站规范并入 `02_工位` | 迁入 `50_客户端运维\双服切换`，B 型结构，6 根文件 / 16 文件数达标 | `校验工位结构.py` 16/16 PASS；文件数迁移前后均 1,284 |

**当前版本**：`csproj` = `1.12.0`；GitHub 最新 tag = `v1.12`，Release 已发布（附件 42.33 MB，使用说明已同步到 v1.12 文案）。

---

## 二、关键决策及理由

| # | 决策 | 理由 | 备选与否决原因 |
| :-: | :--- | :--- | :--- |
| 1 | 进程分两类：`EpinelPS.exe` 自动停，客户端进程只提示 | 「无感切换」的前提。用户点哪个按钮就收敛到哪个，不该要求先点「恢复基线」 | 全自动强杀游戏：可能丢进度，否决 |
| 2 | 起服务走 WMI `Win32_Process.Create` | 父进程变 `WmiPrvSE`，不在调用方进程树/作业对象里，工具退出后服务仍活 | `Process.Start` + `CREATE_BREAKAWAY_FROM_JOB`：作业不允许脱离时静默回退，实测失效 |
| 3 | 新闻页污染按**文件内容**判，不看版本号 | EpinelPS 换 EXE 不动 `wwwroot`（EXE 11-05、index.html 10-29），按版本号判会天天重写 | 版本号比对：与真实污染无关 |
| 4 | 目录定位认**特征文件组合**，不认目录名 | 买家目录名各不相同（`[Game]Nikke` / `Nikke_Local` / `NIKKEG`） | 写死目录名：换机即失效 |
| 5 | 配置路径全存相对 root，换机器只改 root | 换设备是已知高频场景 | 存绝对路径：换机后全部失配 |
| 6 | 配置存 exe 同目录，写不进去回落 `%LocalAppData%` | 便携；单文件解压目录可能不可写 | 只存注册表：不可携带、难清理 |
| 7 | 保留 `serverselectorsettings.json` 补写，尽管验证过服务端不读 | 兼容旧选择器下次打开不错路径；只在现有 `GameRoot` 失效时写，不覆盖手选 | 完全去掉：会被误判成回归 |
| 8 | 不开 `PublishTrimmed` | WinForms 不支持裁剪，开了直接编译失败 | 裁剪压体积：不可行 |
| 9 | 并入时保留独立 `.git` | 与 `地图充能计算`、`等级计算\nikke_src` 同例：独立发布节奏的仓留仓，父仓 `.gitignore` 排除防幽灵 gitlink | 拆掉 `.git` 并入根仓：丢失 22 次提交与远端 |
| 10 | `docs\` / `Assets\` 保留原名，不并入 `资源库\` | 分别被 README 图链、`.csproj` 资源引用硬绑 | 改名：GitHub 图裂 + 构建失败 |
| 11 | 发版走 GitHub Actions（push `v*` tag 触发） | 本地手动打包 + API 上传两步容易漏；Actions 里 `dotnet publish` 直接复用 csproj 的自包含单文件配置，不用重复传参 | 继续手动发版：v1.11、v1.12 两次都是手工，容易忘记同步使用说明 |

---

## 三、踩坑

| # | 现象 | 根因 | 解法 |
| :-: | :--- | :--- | :--- |
| 1 | 日志显示「服务就绪 PID xxx」，重开会话后进程没了、443 不监听 | 父进程在作业对象里，会话结束 `KILL_ON_JOB_CLOSE` 带走整棵树 | WMI `Win32_Process.Create` 创建，脱离进程树 |
| 2 | `CreateFromPemFile(path)` 抛「key contents do not contain a PEM」 | 第二参数为 null 时会去**同一个文件**找私钥，而 `myCA.pem` 只有 CERTIFICATE 块 | `X509Certificate2.CreateFromPem(text)` |
| 3 | 私服跑着时点「启动官服」永远点不动，必须先手动恢复基线 | `StartOfficial` 开头 `RequireNoProcess` 把 `EpinelPS.exe` 也算进去了 | 进程分流：工具起的服务自动停，用户客户端才拦 |
| 4 | 游戏没开却提示「双端共存启动器 在运行」 | 只按「路径在游戏根下」判进程，私服包里的第三方启动器全在同一目录 | 加 `ClientNames` 白名单（nikke / nikke_launcher / tbs_browser / intl_service / version_service / assistant） |
| 5 | 官服根 `D:\Game` 把私服 `D:\Game\nikke153.6.10` 也算成「官服已在运行」 | `StartsWith` 判路径没有边界 | 新增 `InRoot`，要求 `root + 分隔符` 前缀 |
| 6 | 443 被别的程序占用时被当成「服务已起」 | 只看 `PortOpen`，没核对监听进程 | 端口开但进程列表里没有 EpinelPS → 明确报错 |
| 7 | 设置页「自动扫描」按钮看不见 | 透明 Label（560×130）重绘自己整块矩形，把上层按钮擦掉 | Label 缩到 84 高、按钮下移 |
| 8 | 圆角按钮四角发暗 | `RoundButton.OnPaint` 按 `Parent.BackColor` 铺底，但父级是 `BackColor=Transparent` 的 `GlassPanel`，取不到真实底色 | 开头调 `base.OnPaintBackground(e)`，让父控件真正画背景 |
| 9 | `<Compile Remove="临时\**">` 失效，废弃文件又被编进产物 | 用 PowerShell `Set-Content -Encoding UTF8` 改 csproj，中文按 GBK 读入写成乱码 | 改 csproj 一律用编辑器整体重写 |
| 10 | A. 搜索结果「未引用」是假阴性 | .NET 程序集字符串常量是 UTF-16，只搜 ASCII 会漏 | 同时用 `Encoding.Unicode` 再检一遍 |
| 11 | 自测发现「换设备后要重新激活」 | 工具不碰 `auth.txt`（grep 零命中），但它**重启** EpinelPS，而校验只在启动时跑一次 | 诊断报告加「授权文件」段：订单号前 4 位 + 绑定机器码 + 本机 CPU ID + 是否匹配 |
| 13 | GitHub 上传附件报「无效的 URI」，`upload_url` 变成空串 | 想用 `-replace '\{.*\}$'` 去掉 `{?name,label}` 尾巴，正则贪婪把整串 URL 吃掉了 | 改用 `release.id` 拼 `https://uploads.github.com/repos/{owner}/{repo}/releases/{id}/assets?name=xxx` |
| 12 | 并入工作站时整目录 `Move-Item` 报「访问被拒绝」，但文件可读写、子目录能改名 | 有进程把该目录当成当前工作目录（句柄持有根目录），`ps` 查不到 | 逐项 `mv` 子项（同盘 rename，秒完成），清空后再 `rmdir` 空壳 |

---

## 四、大文件登记（> 1 GB）

无。最大单文件为 `正式产物\NikkeDualClient.exe` 47.8 MB（L1 发布物）；`bin`+`obj` 合计 123 MB（L3 可重建）；`回收站\cleanup_20261112-1710` 319 MB（缓冲，可授权清空）。

> 该 319 MB 主要是 v1.9–v1.11 的发布 zip 与重复 exe。旧版在 GitHub Releases 有留档，本地这份是并入时的去重缓冲。

---

## 五、待办（工位级，只在本工位维护）

> 完成后就地划掉：`~~项~~ ✅ YYYY-MM-DD`，不删行。

| # | 项 | 前置 | 优先级 |
| :-: | :--- | :--- | :-: |
| 1 | ~~发 v1.12 Release：`csproj` 已是 1.12.0，包已构建，只差 tag + 上传~~ ✅ 2026-11-12（附件已替换为 v1.12 文案） | 用户授权推送 | P1 |
| 2 | ~~`正式产物\使用说明.txt` 更新到 v1.12（现为 v1.10 文案，缺「维护」页与导出日志）~~ ✅ 2026-11-12（补 v1.11/v1.12 记录 + 换设备 FAQ，`docs\使用说明.txt` 纳入版本控制） | 无 | P1 |
| 3 | `--shot` 出图做像素基线，替代人工看图回归 | 无 | P2 |
| 4 | 旧 PowerShell 版 `[Game]Nikke_Local\双服切换\` 是否回收，待用户裁决 | 用户裁决 | P2 |
| 5 | `回收站\cleanup_20261112-1710`（319 MB）按批次授权后清空 | 用户授权 | P3 |
| 6 | 下次发版验证 Actions 是否自动出包（`git tag v1.13 && git push --tags`） | 有真实改动时 | P2 |
