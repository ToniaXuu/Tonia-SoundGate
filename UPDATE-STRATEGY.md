# Tonia-SoundGate 更新检测方案

> 基于 2026-11-16 迁移后的真实代码库分析。文中每条结论都能在源码中找到对应证据。

## 一、现状技术框架（实测）

| 层 | 选型 | 源码证据 |
|---|---|---|
| 运行时 | .NET 8 / `net8.0-windows` | `Tonia-SoundGate.csproj:5` |
| UI 框架 | WinForms，**无主窗口**，`ApplicationContext` + `NotifyIcon` | `Program.cs:25`、`TrayAppContext.cs:6` |
| 音频层 | NAudio 2.2.1，WASAPI `AudioEndpointVolume.Mute` + `OnVolumeNotification` | `csproj:26`、`README.md:79`、`bin/` 下 `NAudio.Wasapi.dll` |
| 核心状态 | 守护 / 授权双态状态机 + **200ms 巡检兜底** + 5s 设备热插拔检测 | `GateStateMachine.cs`、`TrayAppContext.cs:142-157` |
| UI 呈现 | 全程序化自绘，DWM 圆角 / Win10 Region 回退，**零外部素材** | `UiTheme.cs`、`Rounded*.cs`、`TrayIcons.cs` |
| 配置 | `System.Text.Json`，`%AppData%\Tonia-SoundGate\settings.json` | `AppSettings.cs:8-11` |
| 并发模型 | 单 UI 线程 + 隐藏 `Control` 作 `BeginInvoke` 锚点，把 COM 回调切回 UI 线程 | `TrayAppContext.cs:16-18` |
| 单实例 | 命名 `EventWaitHandle`，二次启动只弹"已在后台运行" | `Program.cs:7-22` |
| 打包 | NSIS 3 / MUI2，自包含单文件（~58MB），装 `Program Files`，**需 UAC** | `packaging/setup.nsi:24-26` |
| 分发 | GitHub 仓库（`git@github.com:ToniaXuu/Tonia-SoundGate.git`），安装包本地生成、**未建 Release** | `git remote -v`、`.gitignore` |

**架构一句话**：一个单进程、单 UI 线程、纯本地、靠托盘图标与自绘弹窗驱动的常驻守护程序。没有服务、没有数据库、没有任何网络代码。

## 二、三个硬约束（决定方案形态）

1. **隐私承诺是产品卖点**。NSIS 欢迎页明确写着"完全在本机运行，不联网、不收集任何数据"（`setup.nsi:44`）。默认联网检查更新会直接背叛这句话。
2. **更新 = 重跑安装包**。程序装在 `Program Files`，任何版本替换都需要 UAC 提权；"无感静默后台自更新"在这个架构下**做不到**，别往那个方向设计。
3. **版本号双写**。csproj 里 `Version`/`FileVersion`/`AssemblyVersion` 三处 + setup.nsi 里 `APP_VERSION`/`VIProductVersion` 两处，全靠手工同步，迟早不一致。

## 三、技术路线对比

| 路线 | 检测端点 | 国内可用性 | 后端成本 | 评价 |
|---|---|---|---|---|
| A. GitHub Releases API | `api.github.com/.../releases/latest` | 时好时坏 | 零 | ✅ 仓库已有，零成本起步；需 8s 超时兜底 |
| B. 静态 version.json | Gitee Pages / COS / CDN 上的 JSON | 稳定快 | 零 | ✅ 做 A 的镜像兜底，或独立使用 |
| C. 自建 API | `/version/check?current=...` | 取决于托管 | 高 | ❌ 灰度/强制更新才需要，当前过度设计 |

**推荐：A 为主 + B 兜底 + 用户 opt-in 触发**。不上 C。

## 四、推荐方案设计

### 4.1 触发时机（隐私第一）

- **P0**：只在托盘面板点「检查更新」时才联网。默认零网络行为，承诺不变。
- **P1**：`settings.json` 增加 `CheckUpdatesOnStartup`（**默认 `false`**）。用户显式开启后，启动延迟 10 秒后台检查，每天最多一次；失败静默跳过。

> ⚠️ 一旦引入自动检查，`setup.nsi:44` 的文案必须同步改成"核心功能完全离线；仅在你开启自动检查后才联网"——否则就是虚假承诺。

### 4.2 版本清单格式

GitHub Release 的 `tag_name` 可直接用；若自建 JSON 镜像，字段如下：

```json
{
  "version": "1.1.0",
  "publishedAt": "2026-11-20",
  "changelog": "修复 xxx；优化 yyy",
  "downloadUrl": "https://.../Tonia-SoundGate-Setup-1.1.0.exe",
  "sha256": "0123456789abcdef...",
  "minVersion": "1.0.0"
}
```

### 4.3 版本比对：必须用 `System.Version`

本地版本从 `Assembly.GetExecutingAssembly().GetName().Version` 或 `Application.ProductVersion` 读取。

```csharp
var local = Assembly.GetExecutingAssembly().GetName().Version;
if (Version.TryParse(latest.Version, out var remote)
    && remote > (local ?? new Version(0, 0, 0)))
{
    // 发现新版本
}
```

**绝不能用字符串比较**——`"1.10.0" < "1.9.0"` 会让你怀疑人生。`System.Version` 是逐段数值比较，天然正确。

### 4.4 网络请求要点

```csharp
private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
static UpdateChecker() =>
    Http.DefaultRequestHeaders.UserAgent.ParseAdd("Tonia-SoundGate");
// GET https://api.github.com/repos/ToniaXuu/Tonia-SoundGate/releases/latest
```

- GitHub API **强制要求 User-Agent**，否则 403。
- `HttpClient` 复用单例，不要每次 new（socket 耗尽）。
- 8 秒超时，失败即静默返回"检查失败"——**更新检查绝不能阻塞或拖垮主程序启动**。

### 4.5 更新落地（P2）

1. 提示"发现新版本 v1.1.0"→ 打开浏览器到 Release 页（P0 就够用）；
2. 进阶：后台下载 `setup.exe` 到 `%TEMP%` → SHA256 校验 → 提示"已下载，点击安装"；
3. 用户点击 → `ProcessStartInfo { Verb = "runas" }` 触发 UAC → NSIS `/S` 静默安装；
4. `setup.nsi:67-72` 的 `.onInit` 已经 `taskkill` 旧实例，**天然为更新设计好了**，无需额外处理。

### 4.6 代码落点

| 动作 | 文件 |
|---|---|
| 新增 `UpdateChecker.cs`（检测 + 比对 + 清单解析） | 新建 |
| 新增 `UpdateInfo.cs`（清单 DTO） | 新建 |
| `settings.json` 增加 `CheckUpdatesOnStartup` | `AppSettings.cs:13-17` |
| 托盘面板加「检查更新」菜单项 | `TrayPopupForm.cs` + `TrayAppContext.cs:120-138` |
| 检查结果回 UI 线程（复用现有锚点） | `TrayAppContext.cs:96`、`_uiAnchor.BeginInvoke` |
| 安装页文案改隐私描述 | `packaging/setup.nsi:44` |
| 版本号收敛为单一真源 | `csproj` + `packaging/setup.nsi` + 打包脚本 |

### 4.7 版本单一真源

以 csproj `<Version>` 为准，打包脚本读出来传给 makensis，消除双写：

```powershell
$ver = (Select-Xml -Path Tonia-SoundGate.csproj -XPath '//Project/PropertyGroup/Version').Node.InnerText
makensis /DAPP_VERSION=$ver /INPUTCHARSET UTF8 setup.nsi
```

`setup.nsi` 顶部改为 `!ifndef APP_VERSION` / `!define APP_VERSION "1.0.1"` / `!endif` 保留默认值。

## 五、分阶段实施

| 阶段 | 内容 | 隐私代价 | 价值 | 状态 |
|---|---|---|---|---|
| **P0** | 手动「检查更新」+ 发现新版本弹中性提示，用户点「前往下载页」才开浏览器 | 零（用户主动才联网） | 立刻可用，不破坏卖点 | ✅ **已实现 2026-11-16** |
| **P1** | opt-in 自动检查 + 文案修正 | 可控（默认关） | 懒人友好 | ⬜ 未做（与零联网承诺冲突，暂不实施） |
| **P2** | 内置下载 + SHA256 校验 + 静默安装 | 可控 | 一键更新体验 | ⬜ 未做 |

**落地决策**：用户选择**保持零联网承诺**，故只实施 P0，P1/P2 冻结。
P0 的实现严格限定为：常规路径零网络请求 → 仅托盘菜单「检查更新」触发一次 GitHub API 请求 →
8s 超时静默降级 → `System.Version` 三段比对 → 发现新版本弹 `UpdateNoticeForm`（默认「稍后再说」）→
**用户明确点击才打开浏览器**，程序不下载、不安装、不改文件。

## 六、风险与边界

- **国内网络**：`api.github.com` 不通时必须有 JSON 镜像兜底，且超时静默——用户不应因为检查更新失败而感知到程序异常。
- **SmartScreen**：自签名/无签名安装包更新时会被拦；后续上代码签名证书（EV 证书可消除警告，但需成本）。
- **多设备场景无关**：更新检测与音频守护完全解耦，不要动 `AudioController` / `GateStateMachine` 一行代码。
- **单实例冲突**：更新安装时 NSIS 会 `taskkill` 旧实例，与 `EventWaitHandle` 单实例机制不冲突（进程是被强杀，非正常 `Exit()`）。
