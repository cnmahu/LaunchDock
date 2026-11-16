# 启坞 LaunchDock · .NET 8 → .NET 10 升级报告

> 版本：v1.0 · 生成日期：2026-11-17
> 评估范围：`D:\TRAE CN Work\启坞\LaunchDock`（commit 截至 2026-11-16）
> 结论先行：**建议直接升级到 .NET 10.0 LTS，跳过 .NET 9**。.NET 8 已于 **2026-11-10 EOL**，当前已不在官方支持窗口。

---

## 一、执行摘要

| 项目 | 当前 | 目标 |
| --- | --- | --- |
| 运行时 | .NET 8.0 LTS（`net8.0` / `net8.0-windows10.0.19041.0`） | **.NET 10.0 LTS**（`net10.0` / `net10.0-windows10.0.19041.0`） |
| 语言版本 | `LangVersion=latest`（实际 C# 12） | C# 14（`net10.0` 默认） |
| UI 框架 | Avalonia 11.3.23 | 保持 11.3.23（官方文档明确支持 `net10.0`） |
| 关键三方库 | `System.Drawing.Common` 8.0.0 | 升至 **10.0.14**（与运行时对齐） |

**紧迫性**
- .NET 8（LTS）发布于 2023-11-14，**EOL = 2026-11-10**。
- .NET 9（STS）EOL 同样是 2026-11-10。
- .NET 10（LTS）发布于 2025-11-11，**支持至 2028-11-14**，是当前唯一受支持的桌面 LTS。
- 本报告生成时（2026-11-17）.NET 8 已脱保 7 天，应尽快完成迁移以恢复安全更新通道。

**整体评估**
- 升级工作量：**低至中等**。6 个 csproj 都是机械改 TFM，无 EF Core / ASP.NET Core / WebHost 等重型栈，破坏性变更面很小。
- 主要风险点：`System.Drawing.Common` 版本对齐、WinRT `PackageManager` 投影、单文件发布时的 Skia 原生库解压、`System.Text.Json` 属性名冲突检查这三项需实测验证。
- 预估改造：1 个工作日内完成编码与本地构建，外加 0.5 天用于功能回归与发布验证。

---

## 二、当前项目源码核对

升级前先确认 TFM 与依赖现状，避免"听说项目是 .NET 8"的盲改。

### 2.1 解决方案结构

```
LaunchDock.sln
src/
  LaunchDock.Core/      net8.0                              配置模型、JSON 存储、密码哈希
  LaunchDock.Windows/   net8.0-windows10.0.19041.0          Win32 P/Invoke、热键、鼠标钩子、应用库、快捷方式
  LaunchDock.App/       net8.0-windows10.0.19041.0          Avalonia 界面、启动板、设置、便签、关于
tests/
  LaunchDock.Core.Tests/      net8.0
  LaunchDock.Windows.Tests/   net8.0-windows10.0.19041.0
  LaunchDock.App.Tests/       net8.0-windows10.0.19041.0
```

> 注：仓库 `obj/` 内仍残留 `RolanClone.*.AssemblyInfo.cs`，应为改名前的产物，本次升级无影响，可在后续清理时一并删除。

### 2.2 TFM 与关键 PackageReference 清单

| 项目 | TFM | 包引用 | 版本 |
| --- | --- | --- | --- |
| `LaunchDock.Core` | `net8.0` | （无三方包） | — |
| `LaunchDock.Windows` | `net8.0-windows10.0.19041.0` | `System.Drawing.Common` | 8.0.0 |
| `LaunchDock.App` | `net8.0-windows10.0.19041.0` | `Avalonia` / `Avalonia.Desktop` / `Avalonia.Themes.Fluent` / `Avalonia.Diagnostics` | 11.3.23 |
| `LaunchDock.Core.Tests` | `net8.0` | `Microsoft.NET.Test.Sdk` / `xunit` / `xunit.runner.visualstudio` | 17.11.1 / 2.9.2 / 2.8.2 |
| `LaunchDock.Windows.Tests` | `net8.0-windows10.0.19041.0` | 同上 | 同上 |
| `LaunchDock.App.Tests` | `net8.0-windows10.0.19041.0` | 同上 + `Avalonia` / `Avalonia.Headless.XUnit` / `Avalonia.Skia` | 11.3.23 / 17.11.1 / 2.9.2 / 2.8.2 |

> `LaunchDock.sln` 未引用 `global.json`、`Directory.Build.props`、`Directory.Packages.props`，TFM 散落在各 csproj，需要逐个改。

### 2.3 实际用到的 .NET / 平台特性

| 特性 | 位置 | 与升级的关系 |
| --- | --- | --- |
| WinRT 投影 `Windows.Management.Deployment.PackageManager` | `src/LaunchDock.Windows/Library/AppLibraryService.cs:166` | 需 `net10.0-windows10.0.19041.0` TFM 提供 CsWinRT 投影 |
| COM 互操作（`[ComImport]`） | `UwpActivator.cs`（`IApplicationActivationManager`）、`ShellInterop.cs`（`IShellItemImageFactory`） | 已开启 `BuiltInComInteropSupport=true`，.NET 10 下行为一致 |
| Win32 P/Invoke（`DllImport`） | `NativeMethods.cs`、`ShellInterop.cs`、`GlobalHotkey.cs`、`GlobalMouseHook.cs`、`IconExtractor.cs` | 跨版本兼容，无需改动 |
| `System.Drawing`（GDI+） | `IconExtractor.cs`（`Bitmap`/`Icon`/`ImageFormat`/`LockBits`/`GetDIBits`） | 必须升级 `System.Drawing.Common` 至 10.0.x |
| `System.Text.Json` | `ConfigStore.cs`（`JsonSerializerOptions` + `JsonStringEnumConverter`） | 注意 .NET 10 的"属性名冲突检查"破坏性变更 |
| `Microsoft.Win32.Registry` | `AutostartManager.cs`、`MicaBackdrop.cs` | 跨版本兼容 |
| `Thread` + `volatile` 消息循环 | `GlobalHotkey.cs`、`GlobalMouseHook.cs` | 跨版本兼容 |
| `record` / `init` / `required` / `namespace ...;` 文件范围命名空间 | 多处（如 `LibraryEntry`） | C# 14 全部支持，无破坏 |
| 单文件发布（`PublishSingleFile` + `IncludeNativeLibrariesForSelfExtract`） | README 说明 | 必须实测 Skia/Avalonia 原生库解压仍正常 |

---

## 三、目标栈与依赖兼容性

### 3.1 .NET 10 LTS 时间线

- 发布：2025-11-11
- 类型：**LTS**（3 年支持）
- EOL：2028-11-14
- 当前最新补丁：10.0.14（2026-10-25）

### 3.2 关键依赖对 .NET 10 的支持情况

| 依赖 | 是否支持 .NET 10 | 建议版本 | 备注 |
| --- | --- | --- | --- |
| **Avalonia 11.3.x** | ✅ 官方明确支持 | 11.3.23（保持） | Avalonia 官方 Windows 平台文档原文："Your target framework is simply `net9.0` or `net10.0`" |
| **System.Drawing.Common** | ✅ | **10.0.14** | 自 .NET 7 起仅 Windows 可用，本项目 TFM 已带 `-windows`，无影响 |
| **Microsoft.NET.Test.Sdk** | ✅ | 17.12+（建议 17.13.x） | 17.11.1 仍可用，但建议跟进 |
| **xunit / xunit.runner.visualstudio** | ✅ | 2.9.3 / 3.0.2 视情况 | 2.9.2 在 .NET 10 下可运行，但建议同步到 2.9.3+ |
| **WinRT 投影（`Windows.Management.Deployment`）** | ✅ | 通过 `net10.0-windows10.0.19041.0` TFM 提供 | 不需额外引用 CsWinRT 包 |
| **Avalonia.Headless.XUnit / Avalonia.Skia** | ✅ | 11.3.23（跟随 Avalonia） | — |

---

## 四、影响 LaunchDock 的破坏性变更（逐项评估）

.NET 8 → .NET 10 跨两个版本，**.NET 9 与 .NET 10 的破坏性变更都要看**。下表只列对本项目实际有影响的条目，已剔除 Web/EF/容器类无关项。

| # | 变更 | 来源版本 | 影响本项目 | 处理方式 |
| --- | --- | --- | --- | --- |
| 1 | `System.Drawing.Common` 仅 Windows 可用，移除 `System.Drawing.EnableUnixSupport` 运行时开关 | .NET 7 | ❌ 无影响（项目本就是 `-windows` TFM） | 仅升级版本号 |
| 2 | `System.Text.Json` 在反序列化时检测属性名冲突（不区分大小写时重复属性会抛 `JsonException`） | .NET 10 | ⚠️ 低风险 | `ConfigStore` 配置 `PropertyNameCaseInsensitive=true`，若 `config.json` 出现重复键以前静默取最后一个，现在会抛异常；但 `TryBackupBrokenFile` 已能兜底 |
| 3 | `PackageReference` 不带版本号会报 `NU1015` 错误 | .NET 10 | ❌ 无影响（所有 PackageReference 都已显式带版本） | 无需处理 |
| 4 | NuGet 漏洞审计默认 `all`，传递依赖也产生 `NU1901`~`NU1904` 警告 | .NET 10 | ⚠️ 注意 | 若开启 `TreatWarningsAsErrors` 首次还原可能失败；本项目未开启，但应 review 一次审计输出 |
| 5 | `System.Linq.AsyncEnumerable` 进框架；引用 `System.Linq.Async` 旧版本会产生歧义 | .NET 10 | ❌ 无影响（项目未引用 `System.Linq.Async`） | 无需处理 |
| 6 | `BinaryFormatter` 全部抛异常 | .NET 9 | ❌ 无影响（项目用 `System.Text.Json`） | 无需处理 |
| 7 | C# 14：部分基于 span 的重载会被优先选择，重载解析结果可能变化 | .NET 10 | ⚠️ 低风险 | 项目里 `Marshal.Copy(buffer, 0, data.Scan0, buffer.Length)` 等调用重新编译观察警告即可 |
| 8 | `System.Drawing` 中 `OutOfMemoryException` 改为 `ExternalException` | .NET 10 | ⚠️ 低风险 | `IconExtractor` 的 `catch` 块多为裸 `catch`，不受异常类型收紧影响；若后续细化 catch 请注意 |
| 9 | `BackgroundService.ExecuteAsync` 全程在后台线程执行 | .NET 9 | ❌ 无影响（项目无 `BackgroundService`） | 无需处理 |
| 10 | ASP.NET Core 一系列废弃（`WebHost`、`WithOpenApi`、Cookie 重定向等） | .NET 9 / 10 | ❌ 无影响（无 Web 栈） | 无需处理 |
| 11 | EF Core 10（`Contains` 翻译、参数命名等） | .NET 10 | ❌ 无影响（无 EF） | 无需处理 |
| 12 | 默认容器基础镜像改为 Ubuntu 24.04、无 Debian | .NET 10 | ❌ 无影响（不打包镜像） | 无需处理 |

**结论**：本项目需重点验证的破坏性变更只有 3 条——#2（JSON 属性冲突）、#7（C# 重载解析）、#8（GDI+ 异常类型）。均为低风险，且都已被现有"裸 catch + 备份兜底"的防御式写法覆盖。

---

## 五、升级步骤（按顺序执行）

### 步骤 0 · 准备
- 在 git 上拉一条 `chore/net10-upgrade` 分支，所有改动隔离在其上。
- 先在 .NET 8 上跑一次全量测试，记下基线绿条数（防止升级混入既有失败）。
- 安装 **.NET 10 SDK**（10.0.100 或更高）。`dotnet --list-sdks` 确认。

### 步骤 1 · 改 TFM（6 个 csproj）

```diff
// src/LaunchDock.Core/LaunchDock.Core.csproj
- <TargetFramework>net8.0</TargetFramework>
+ <TargetFramework>net10.0</TargetFramework>
```

```diff
// src/LaunchDock.Windows/LaunchDock.Windows.csproj
- <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
+ <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
```

```diff
// src/LaunchDock.App/LaunchDock.App.csproj
- <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
+ <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
```

三个测试项目同步：

```diff
// tests/LaunchDock.Core.Tests/LaunchDock.Core.Tests.csproj
- <TargetFramework>net8.0</TargetFramework>
+ <TargetFramework>net10.0</TargetFramework>
```

```diff
// tests/LaunchDock.Windows.Tests/LaunchDock.Windows.Tests.csproj
- <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
+ <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
```

```diff
// tests/LaunchDock.App.Tests/LaunchDock.App.Tests.csproj
- <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
+ <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
```

### 步骤 2 · 升级 `System.Drawing.Common`

```diff
// src/LaunchDock.Windows/LaunchDock.Windows.csproj
- <PackageReference Include="System.Drawing.Common" Version="8.0.0" />
+ <PackageReference Include="System.Drawing.Common" Version="10.0.14" />
```

> 与运行时版本对齐可以避免传递依赖 `Microsoft.Win32.SystemEvents` 出现"8.0 配 10.0 runtime"的版本错配。

### 步骤 3 · 升级测试 SDK（可选但建议）

```diff
// 三个 *Tests.csproj
- <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
+ <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.13.0" />
- <PackageReference Include="xunit" Version="2.9.2" />
+ <PackageReference Include="xunit" Version="2.9.3" />
```

### 步骤 4 · 还原并审计

```powershell
dotnet restore LaunchDock.sln
dotnet list LaunchDock.sln package --outdated
dotnet list LaunchDock.sln package --vulnerable
```

- 若 `--vulnerable` 出现传递依赖告警，逐一研判；本项目未开 `TreatWarningsAsErrors`，不会卡构建。
- `Avalonia.*` 仍保持 11.3.23 即可，无需跟进到 12.x（12.x 是另一条主线，会带来 XAML 兼容性变更，不在本次范围）。

### 步骤 5 · 构建与排错

```powershell
dotnet build LaunchDock.sln -c Debug -warnaserror-stdin
```

重点关注：
- `IconExtractor.cs` 的 `Marshal.Copy(buffer, 0, data.Scan0, buffer.Length)` 是否被解析到 span 重载（通常不会，因为参数是 `byte[]` 而非 `Span<byte>`）。
- 任何 `Obsolete` 警告，原则上不压制，逐条消除。

### 步骤 6 · 跑测试

```powershell
dotnet test LaunchDock.sln -c Debug
```

期望：基线绿条数保持或增加，无新增失败。

### 步骤 7 · 实测 Win32 / WinRT 路径（手测）

自动化测试覆盖不到原生路径，必须手测：

| 路径 | 操作 | 通过标准 |
| --- | --- | --- |
| 图标提取 | 添加一个 `.exe`、一个 `.lnk`、一个 `shell:Downloads` 路径到启动板 | 256px 大图标正常显示，无黑底 |
| 全局热键 | 默认 `Alt+Space` 唤出/收起 | 注册成功且 `Pressed` 事件触发 |
| 鼠标扩展键 | XButton1 / XButton2 / 中键唤出 | 钩子安装且事件触发 |
| 商店应用枚举 | 打开"应用库 → 商店应用" | 列表非空（WinRT `PackageManager.FindPackagesForUser` 正常） |
| UWP 启动 | 点击任意商店应用项 | `IApplicationActivationManager.ActivateApplication` 成功 |
| 窗口材质 | 切换主题 / 亚克力 / 云母 / 不透明度 | 自绘云母背景、暗色边框、圆角均正常 |
| 自启 | 开关"开机自启" | `HKCU\...\Run\LaunchDock` 读写正常 |
| 配置原子写 | 改任意设置后查看 `%APPDATA%\LaunchDock\config.json` | 文件写入且无 `.tmp` 残留 |

### 步骤 8 · 重新发布并验证单文件包

```powershell
dotnet publish src/LaunchDock.App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:EnableCompressionInSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true
```

- `IncludeNativeLibrariesForSelfExtract=true` **必须保留**，否则 `SkiaSharp` 原生库不会随单文件包解出，启动时 `DllNotFoundException`。
- 在干净机器（无 .NET 10 运行时）上运行 `dist\publish\LaunchDock.exe`，确认能正常启动。
- 对比包大小（应与 .NET 8 单文件包接近），冷启动耗时记录一次。

### 步骤 9 · 文档同步

- `README.md` 第 7 行 "基于 **.NET 8 + Avalonia**" → "基于 **.NET 10 + Avalonia**"
- `README.md` 第 26 行技术栈表格 "`.NET 8（net8.0-windows10.0.19041.0）`" → "`.NET 10（net10.0-windows10.0.19041.0）`"
- `README.md` 第 47 行构建要求 ".NET 8 SDK" → ".NET 10 SDK"

---

## 六、风险与回滚

### 6.1 主要风险

| 风险 | 概率 | 影响 | 缓解 |
| --- | --- | --- | --- |
| WinRT 投影 `PackageManager` 在 .NET 10 上行为变化（极低，Microsoft 维护兼容） | 低 | 应用库商店标签为空 | 已有 `try/catch` 兜底，不会崩；最坏降级为空列表 |
| `System.Drawing.Common` 10.x 在 GDI `GetDIBits` / `LockBits` 路径上的细微行为差异 | 低 | 个别图标透明通道异常 | `IconExtractor.FromHBitmap` 已有 alpha 兜底逻辑 + 落回 `Image.FromHbitmap` |
| 单文件发布 Skia/Avalonia 原生库解压问题 | 中 | 启动时 `DllNotFoundException` | 步骤 8 必须在干净机器验证；保留 .NET 8 的发布包作为回滚备份 |
| 用户已存在的 `%APPDATA%\LaunchDock\config.json` 含重复键，.NET 10 严格 JSON 检查启动失败 | 极低 | 应用启动失败 | `ConfigStore.Load` 已 try/catch + 备份损坏文件 + 落回默认配置 |
| CI / 构建机未装 .NET 10 SDK | 中 | 构建失败 | 同步更新 CI setup 步骤；`global.json` 可选固定到 `10.0.100` |

### 6.2 回滚策略

- 全程在独立分支进行，`main` 始终保留 .NET 8 可构建状态。
- .NET 8 的最后一次 Release 发布包保留至少一个 LTS 周期，便于紧急回滚分发。
- 若步骤 5/6/7 任一失败且无法快速修复：`git checkout main`、删除 `chore/net10-upgrade` 分支、回到 .NET 8 维护，待定位后再起一次升级。

---

## 七、验证清单（Checklist）

升级完成时逐项打勾：

- [ ] `dotnet --list-sdks` 输出包含 `10.0.xxx`
- [ ] 6 个 csproj 的 `<TargetFramework>` 均为 `net10.0` 或 `net10.0-windows10.0.19041.0`
- [ ] `System.Drawing.Common` = 10.0.14
- [ ] `dotnet restore LaunchDock.sln` 无错误
- [ ] `dotnet build LaunchDock.sln -c Debug` 无错误、无新增警告
- [ ] `dotnet test LaunchDock.sln` 全绿
- [ ] 手测清单 8 项全部通过（步骤 7）
- [ ] 干净机器运行单文件 `LaunchDock.exe` 启动正常
- [ ] README 已更新为 .NET 10
- [ ] 发布包上传至 Releases，并在 Release Notes 注明"运行时升级至 .NET 10 LTS"
- [ ] CI 构建机已安装 .NET 10 SDK（若有 CI）

---

## 八、备注与延伸

1. **本报告与 `docs/tauri2-migration-plan.md` 是两条独立路线**。后者讨论的是把整个 Avalonia + Win32 应用迁移到 Tauri 2（Rust + WebView），属于跨框架重写，工作量与影响面都远大于本次"运行时小版本升级"。如果决定走 Tauri，本次 .NET 10 升级**仍然建议先做**——在重写期间维持 .NET 主线在受支持版本上，避免脱保期出现的安全补丁盲区。
2. **暂不升级 Avalonia 12.x**。11.3 → 12 是 Avalonia 主版本升级，会带来 ThemeDictionaries、XAML 命名空间、控件 API 等变更，与 .NET 运行时升级不应混在一起。建议先稳定 .NET 10，再单独评估 Avalonia 升级。
3. **`obj/` 中的 `RolanClone.*` 残留**可在升级合并时一并清理（删除各项目 `bin/`、`obj/` 后重新还原生成），不影响功能。
4. **`global.json` 是否要加**：如果团队多人/多机构建，建议在仓库根加一份 `global.json` 固定 SDK 主版本：
   ```json
   { "sdk": { "version": "10.0.100", "rollForward": "latestFeature" } }
   ```
5. **后续可以考虑的 .NET 10 新特性**（本次升级**不强制使用**，仅为可选项）：
   - `System.Frozen` 集合（`FrozenDictionary` / `FrozenSet`）用于 `GlobalHotkey.SymbolKeys` 等只读映射，热路径更快；
   - `SearchValues<char>` 加速热键解析中的字符匹配；
   - `TimeProvider` 用于未来测试可控的时间相关逻辑（如 `File.GetLastWriteTimeUtc` 比较）。
   这些都是锦上添花，可在 .NET 10 升级稳定后再单独评估。

---

**报告版本**：v1.0
**生成者**：UI Designer（像素君专家）
**关联文档**：`docs/tauri2-migration-plan.md`（跨框架迁移，独立路线）
