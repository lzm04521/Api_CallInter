# ApiCallInter

[![Release](https://img.shields.io/github/v/release/lzm04521/Api_CallInter)](https://github.com/lzm04521/Api_CallInter/releases)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D4)](#系统要求)
[![License](https://img.shields.io/github/license/lzm04521/Api_CallInter)](LICENSE)

Windows 桌面常驻的 **API 定时保活工具**：托盘程序 + 本机 Web 管理页，按项目级固定间隔（含随机抖动）定时调用你配置的 HTTP 接口，记录每次请求结果。适用于保持登录会话活跃、定时探活、周期性触发远端接口等场景。

## 特性

- **定时调用**：项目级间隔秒数 + 毫秒级随机抖动（±）打散执行，避免定点并发；启动即执行一轮，之后按间隔循环，错过不补发；支持对单个接口"立即请求"验证。
- **托盘常驻**：开机自启（当前用户注册表 Run 键）、单实例，双击托盘图标打开管理页。
- **Web 管理页**：概览（版本/内存/下次调度）、项目管理、请求日志、系统设置、关于，五页面；Vue 3 驱动，纯静态无前端构建链。
- **项目排序**：管理页拖拽行首 ⠿ 把手调整项目顺序并持久化，概览页按同序展示。
- **请求日志**：每次调用的状态码、耗时、错误信息落 SQLite，管理页可查。
- **应用内自升级**：检查 GitHub Release 新版本，一键升级（下载 → 校验 → 自动重启替换），失败不影响当前版本运行。
- **绿色部署**：发布为自包含单文件程序（目录仅 `ApiCallInter.exe`、`update.ps1`、`appsettings.json` 三个文件），解压即用，无需安装 .NET 运行时。

## 系统要求

- Windows 10 / 11（x64）
- 无需预装 .NET 运行时（自包含发布）；从源码构建需 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

## 安装

1. 从 [Releases](https://github.com/lzm04521/Api_CallInter/releases) 下载 `ApiCallInter-win-x64.zip`；
2. 解压到任意目录（如 `D:\Tools\ApiCallInter`）；
3. 双击 `ApiCallInter.exe`，托盘出现图标；
4. （可选）托盘右键菜单勾选**开机自启**。

## 快速上手

1. 双击托盘图标（或右键 → 打开管理页），浏览器访问 `http://127.0.0.1:61121`；
2. **项目管理**：新建项目（设间隔秒数与随机抖动），在项目下新建接口（URL、方法、请求头、请求体等），可先"立即请求"验证连通性；项目行首 ⠿ 把手可拖拽调整顺序，概览页将按此顺序展示；
3. **概览**查看运行时长、内存、下次调度时间；**请求日志**查看每次调用的状态码、耗时与错误信息。

## 配置

| 配置项 | 位置 | 说明 |
|---|---|---|
| 监听 host | `appsettings.json` → `Urls` | 模板如 `http://127.0.0.1:61121`，可改 `0.0.0.0` 供内网访问；改后需重启 |
| 监听端口 | 管理页"系统设置" | 存数据库，改后需重启（设置页有"立即重启程序"按钮） |
| 日志保留天数 | `appsettings.json` → `Scheduler:LogRetentionDays` | 默认 90 天，后台每日清理 |
| 更新源仓库 | `appsettings.json` → `Update:Repo` | 默认 `lzm04521/Api_CallInter` |
| 数据目录 | 环境变量 `APICALLINTER_DATA_DIR` | 默认 `%ProgramData%\ApiCallInter\` |

所有数据（SQLite 数据库与日志）存于数据目录，与应用目录分离：

| 路径 | 说明 |
|---|---|
| `app.db` | SQLite 数据库（项目、接口、请求日志、系统设置） |
| `logs\app-*.log` | 主程序日志（按天滚动，保留 30 天） |
| `logs\bootstrap-*.log` | 启动早期日志 |
| `logs\update.log` | 升级过程日志 |
| `updates\` | 升级包暂存（启动时清理残留） |

## 升级

- **一键升级**：管理页"关于"页 → 检查更新 → 有新版本时点"一键升级"，程序自动下载 zip、校验、重启完成替换，全过程写 `logs\update.log`。
- **手动升级**：下载新版 `ApiCallInter-win-x64.zip`，退出程序后解压覆盖原目录，再启动即可（`appsettings.json` 不会被升级覆盖）。

## 从源码构建

```bash
# 克隆后构建解决方案
dotnet build ApiCallInter.sln

# 运行测试（xUnit，无外部依赖）
dotnet test

# 发布自包含单文件（与 Release 流水线同一命令）
dotnet publish src/ApiCallInter -c Release -r win-x64 --self-contained
```

技术栈：.NET 10 WinForms（托盘）+ ASP.NET Core 最小 API（内嵌 Kestrel）+ EF Core SQLite + Vue 3 全局构建版（无前端构建步骤）+ Serilog。管理页静态资源嵌入 exe 内随包分发。

## 排障

- 接口不通：查管理页**请求日志**中的错误信息；
- 管理页打不开：确认端口未被占用（见上方端口配置）；
- 升级异常：看数据目录 `logs\update.log`；
- 启动即弹错误框：看数据目录 `logs\bootstrap-*.log`。

## 安全声明

本工具**无任何鉴权**，管理页与 API 仅限本机或可信内网使用，请勿暴露到公网。若需远程访问，请自行在网络层做好访问控制。

## 贡献

欢迎 [Issue](https://github.com/lzm04521/Api_CallInter/issues) 与 Pull Request。提交 PR 前请确保 `dotnet test` 全部通过，改动较大时建议先开 Issue 讨论。

## License

[MIT](LICENSE) © lzm04521
