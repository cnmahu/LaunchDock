# 启坞 LaunchDock — Rust + Tauri 2 迁移计划

> 目标：把现有 .NET 8 + Avalonia 实现（约 7,100 行，44 个文件）按层迁移到 Rust + Tauri 2，
> 保持配置文件、图标缓存、用户数据向后兼容，界面与交互达到功能对等（feature parity）。
>
> 参照实现：[LaunchDock/src](../src)（迁移完成前不删除，作为对照基线）。

---

## 0. 结论与前提

- **可以迁移，但属于重写而非翻译**：UI 层（XAML + code-behind + ViewModel，约 4,750 行）必须重做，
  平台层（Win32 / WinRT / COM，约 1,520 行）全部换成 `windows` crate 调用，Core 层（约 300 行）可逐字段照搬。
- **代价必须提前接受**：Tauri 的内核很小（exe 约 8–15 MB），但 WebView2 是多进程模型，
  **常驻内存约 80–150 MB**，冷启动约 200–500 ms。启动器是高频呼出的工具，因此必须做
  “启动即创建隐藏窗口预热”来掩盖冷启动（见 M4）。
- **如果内存指标不可接受，就不要选 Tauri**，改 Rust + Slint（纯原生渲染，无 WebView）。
- 迁移期间 C# 版本保持可构建，用于图标/配置的 A/B 对照；M5 通过后才讨论删除。

---

## 1. 目标工程结构

建议在独立目录新建工程，避免与现有 `src/` 冲突：

```
启坞/
  LaunchDock/                    # 现有 C# 工程：保留为对照实现
  launchdock-tauri/              # 新工程（独立分支 / 独立仓库）
    package.json
    index.html
    src/                         # 前端：Vite + TypeScript + React
      main.tsx
      ipc.ts                     # 所有 invoke / listen 的类型化封装
      views/Launcher.tsx
      views/Settings.tsx
      views/AppLibrary.tsx
      views/NewItem.tsx
      views/About.tsx
      components/...
      styles/tokens.css          # 颜色 / 字号 / 圆角 / 间距 token
    src-tauri/
      Cargo.toml                 # [workspace]
      tauri.conf.json
      capabilities/default.json
      icons/app.ico
      crates/
        core/                    # launchdock-core：模型、配置、密码、路径（纯 Rust，无 Win32）
        win/                     # launchdock-win：图标、Shell、热键、钩子、UWP、DWM
      src/
        main.rs
        state.rs                 # AppState：Mutex<AppConfig> 单一数据源
        commands/                # 按域拆分的 #[tauri::command]
        windows.rs               # 各窗口创建（隐藏主窗、设置、库、新建、关于）
        tray.rs
```

**状态归属原则**：Rust 持有 `AppConfig` 为唯一数据源，前端只做显示与发起变更（命令），
避免两侧各存一份导致配置漂移。变更后由 Rust 落盘（200 ms 防抖 + 原子写），并广播
`config://changed` 让其他窗口刷新。

---

## 2. 能力映射表（C# → Rust / Tauri）

| 现有实现 | 行数 | Rust / Tauri 方案 |
| --- | --- | --- |
| `ConfigStore`（JSON、camelCase、枚举字符串、原子写 + `.bak`） | 86 | `serde` + `serde_json`；`#[serde(rename_all = "camelCase")]`；临时文件 + `ReplaceFileW`/`rename`，保留 `.bak` |
| `PasswordHasher`（PBKDF2-SHA256 / 100k / 16B salt / 32B） | 38 | `pbkdf2` + `sha2` + `base64` + `rand` + `subtle`（固定时间比较），格式 `base64(salt):base64(hash)` 不变 |
| `IconExtractor`（`IShellItemImageFactory` → HBITMAP → GetDIBits → PNG） | 199 | `windows`：`SHParseDisplayName` / `SHCreateItemFromIDList` / `SHCreateItemFromParsingName` + `GetImage` + `GetDIBits` + `image` 编码 PNG |
| `IconService`（STA 工作线程 + SHA1 缓存键 + 256px） | 178 | 独立 STA 线程（`CoInitializeEx(COINIT_APARTMENTTHREADED)`）+ 通道；`sha1`；缓存目录不变 |
| `AppLauncher`（`Process.Start` / `runas`） | 49 | `ShellExecuteW`（`open` / `runas`）；URL 可走 `tauri-plugin-opener` |
| `UwpActivator`（`IApplicationActivationManager`） | 43 | `windows` 的 `IApplicationActivationManager` + `ApplicationActivationManager` coclass |
| `AppLibraryService`（开始菜单 `.lnk`、商店应用、驱动器、系统工具） | 183 | `known-folders`/`dirs` + `walkdir`；商店应用用 `windows` 的 `Management::Deployment::PackageManager` |
| `BuiltinActions`（音量、媒体、锁屏、关机…） | 163 | `SendInput`、`SHEmptyRecycleBinW`、`LockWorkStation`、`SetSuspendState`、`ExitWindowsEx` + `AdjustTokenPrivileges` |
| `GlobalHotkey`（`RegisterHotKey` + 独立消息循环 + 手势解析/校验） | 204 | `tauri-plugin-global-shortcut` 注册；**手势语法与校验自己移植**（保证 `Alt+Space`、``Ctrl+` ``、F1–F24 行为一致） |
| `GlobalMouseHook`（`WH_MOUSE_LL`，XButton1/2 + 中键） | 122 | `windows` 的 `SetWindowsHookExW(WH_MOUSE_LL)` + 独立线程消息循环 + 通道转发 |
| `WindowFrame`（DWM 暗色 / 圆角 / 1px 边框） | 33 | `DwmSetWindowAttribute`（`DWMWA_USE_IMMERSIVE_DARK_MODE`、`DWMWA_WINDOW_CORNER_PREFERENCE`）+ `DwmExtendFrameIntoClientArea` |
| 亚克力 / 云母材质 | — | `tauri.conf.json` 的 `windowEffects` + 运行时 `window.setEffects()`（Tauri 2 内置，不额外引 `window-vibrancy`） |
| `SystemFontLister`（GDI 枚举字体） | 91 | **不迁移**：前端 `font-family` 直接使用系统字体；如需列表再补注册表读取 |
| `ShortcutService`（`IShellLinkW` 建 .lnk） | 89 | `mslnk` crate，或 `windows` 的 `IShellLinkW` + `IPersistFile` |
| `AutostartManager`（HKCU Run 键） | 57 | `tauri-plugin-autostart`（支持 `--hidden` 参数） |
| 单实例 / 托盘 / 文件对话框 / 剪贴板 / 打开外部程序 | — | `tauri-plugin-single-instance`、`TrayIconBuilder`、`tauri-plugin-dialog`、`tauri-plugin-clipboard-manager`、`tauri-plugin-opener` |
| `FileSystemWatcher`（LiveFolder 实时刷新） | — | `notify` crate + 300 ms 去抖 |
| 搜索（名称 / 前两字 / 路径） | — | 前端过滤；“拼音首字母”实际未实现（`BuildInitials` 只取前两个字符），可选用 `pinyin` crate 真正补齐 |

### 依赖清单（起步版）

`src-tauri/Cargo.toml`

```toml
[workspace]
members = [".", "crates/core", "crates/win"]
resolver = "2"

[package]
name = "launchdock"
edition = "2021"

[dependencies]
tauri = { version = "2", features = ["tray-icon"] }
tauri-plugin-single-instance = "2"
tauri-plugin-autostart = "2"
tauri-plugin-global-shortcut = "2"
tauri-plugin-dialog = "2"
tauri-plugin-clipboard-manager = "2"
tauri-plugin-opener = "2"
serde = { version = "1", features = ["derive"] }
serde_json = "1"
launchdock-core = { path = "crates/core" }
launchdock-win = { path = "crates/win" }
```

`crates/core`：`serde`, `serde_json`, `chrono`, `pbkdf2`, `sha2`, `base64`, `rand`, `subtle`, `sha1`, `thiserror`
`crates/win`：`windows`（`Win32_UI_Shell`, `Win32_UI_WindowsAndMessaging`, `Win32_UI_Input_KeyboardAndMouse`,
`Win32_Graphics_Gdi`, `Win32_Graphics_Dwm`, `Win32_System_Com`, `Win32_System_Power`,
`Win32_System_Shutdown`, `Win32_Security`, `Management_Deployment`, `ApplicationModel`, `Foundation_Collections`）、
`image`, `walkdir`, `notify`, `mslnk`

---

## 3. 数据兼容契约（迁移的硬约束）

### 3.1 配置

| 项 | 现有值 | Rust 要求 |
| --- | --- | --- |
| 目录 | `%APPDATA%\LaunchDock`（代码内已有旧目录 → `LaunchDock` 的迁移逻辑） | 显式使用同一路径；**不要用 Tauri 默认的 `%APPDATA%\<identifier>`** |
| 文件 | `config.json`，UTF-8、缩进、camelCase、枚举小写驼峰（`followSystem` / `singlePage` / `minimizeToTray`） | 字段名、枚举串逐字一致；反序列化加 `alias` 或大小写不敏感兜底 |
| 写入 | 临时文件 → `File.Replace` + `.bak`；解析失败备份为 `.broken-yyyyMMddHHmmss` 后重置 | 同样策略，失败不阻断启动 |
| `schemaVersion` | `1` | 保留；升级时先备份再迁移 |
| `window.left/top` | **物理像素**；`width/height` 为逻辑尺寸 | Rust 侧按 `scale_factor` 换算，位置用 `PhysicalPosition`，避免 125%/150% DPI 下漂移 |
| `lastUsed` | `DateTime?`（带时区偏移的 ISO 8601） | `Option<chrono::DateTime<FixedOffset>>`，不要转 UTC 丢失原偏移 |
| 可空字段 | `arguments`、`customIcon`、`sourcePath`、`fontFamily`、`notesDirectory`、`passwordHash` | 一律 `Option<String>`，禁止用空串代替 `null` |

**兼容性验收（M1 核心）**：
1. 取现有 `%APPDATA%\LaunchDock\config.json` 作为 fixture；
2. Rust 读入 → 写回 → 与原文做 `serde_json::Value` 深比较，必须完全相等（键序不比较）；
3. 用 Rust 生成的 `passwordHash` 能被 C# `PasswordHasher.Verify` 通过，反之亦然；
4. 手工写坏的 JSON 能被识别、备份、重建。

### 3.2 图标缓存

- 目录：`%LOCALAPPDATA%\LaunchDock\icons`（与现有代码一致）。
- 现有键：`sha1("{小写路径}|{修改时间Ticks}|256|v3")`。
- Rust 不必复刻 Ticks 语义：**把版本位升到 `v4`**，首次运行清空旧文件即可；目录与 PNG 格式保持不变。
- 提取尺寸固定 256 px，`SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK`，失败回落 `SHGetFileInfoW` 32 px。

### 3.3 便签

- 默认目录现有实现是 **exe 所在目录\Notes**；Tauri 安装到 `Program Files` 时不可写。
- 迁移策略：优先 `exe_dir\Notes`，不可写则回落 `%APPDATA%\LaunchDock\Notes`，并在设置页提示实际路径。
- 文件格式、命名规则保持一致（迁移前先读一遍 `NotesViewModel` 的保存/重命名逻辑并写测试）。

---

## 4. 分阶段执行计划

规模标记：S（小）/ M（中）/ L（大），用于比较相对工作量，不代表工期。

### M0 骨架与窗口壳（S）

**交付物**
- `create-tauri-app` 初始化，workspace 拆分为 `core` / `win` / 主程序。
- 主窗口配置：`transparent: true`、`decorations: false`、`visible: false`、`skipTaskbar: true`、
  `windowEffects: { effects: ["acrylic", "mica"], state: "active" }`。
- 插件接线：single-instance（必须第一个注册）、tray、autostart、global-shortcut、dialog、clipboard、opener。
- `capabilities/default.json` 只开必要权限：`core:window:allow-show/hide/set-position/set-size/start-dragging`、
  `core:event:*`、各插件对应权限。
- 托盘菜单：显示面板 / 设置 / 退出；左键切换显隐。
- 前端路由：`?view=launcher|settings|library|new-item|about`，共用一套 token。

**验收**
- `Alt+Space` 能显隐一块空面板；再启动一次进程不会产生第二个实例而是唤出已有窗口；
- 托盘可退出，退出后热键注销；无黑边、无任务栏图标、圆角与材质可见。

### M1 Core 层移植（S）

**交付物**
- `core::model`：`AppConfig` / `TabModel` / `ItemModel` / `SettingsModel` / `WindowStateModel` 及全部枚举。
- `core::config`：加载、原子保存、损坏备份、`LaunchDock → LaunchDock` 目录迁移。
- `core::password`：PBKDF2 哈希与校验（与 C# 双向兼容）。
- `core::notes`：目录解析与文件读写规则。
- `core::paths`：配置 / 缓存 / 便签路径统一出口，禁止散落 `dirs::` 调用。

**验收**：第 3.1 节 4 条兼容性测试全部通过；Rust 生成的配置能被 C# 版本正常打开。

### M2 平台层（L，风险最高）

按依赖顺序做，每完成一块就与 C# 版本对照：

1. **图标流水线**（先做，UI 全部依赖它）
   - 专职 STA 线程 + 请求通道（2 个 worker，与现有实现一致）；
   - `IShellItemImageFactory::GetImage` → `GetDIBits` 32bpp（负高度、自上而下）→ `image` 编码 PNG；
   - 处理无 alpha 的图标（补 255）与 HBITMAP/HDC 释放；
   - 支持 `shell:...`、`::{CLSID}`、UWP AUMID（转 `shell:AppsFolder\<AUMID>`）、PATH 中裸命令名。
   - **验收**：对同一组 20 个目标（exe / lnk / msc / 文件夹 / `shell:Downloads` / UWP）产出 PNG，
     与 C# 版本逐像素或肉眼比对无黑底、无模糊、无通用文档图标。
2. **启动能力**：`ShellExecuteW`（open / runas）、UWP 激活、内置动作 16 项、`shell:` 路径。
3. **枚举**：开始菜单 + 桌面 `.lnk`、商店应用（WinRT）、驱动器、系统工具清单。
4. **输入**：手势解析/校验移植 + 全局热键注册/冲突回滚；`WH_MOUSE_LL` 钩子（XButton1/2、中键）。
5. **窗口外观**：DWM 暗色、圆角、1px 边框、材质切换、透明度；贴边吸附（120 ms 去抖计时器）。
6. **系统集成**：桌面/任务栏快捷方式、开机自启、崩溃日志。

**关键实现注意**
- 所有 COM 调用前必须 `CoInitializeEx`；图标提取线程必须是 STA（MTA 线程取 `.msc` 图标会退化成空白文档）。
- WinRT 的 `PackageManager` 放独立 MTA 线程调用并缓存结果，避免拖慢启动。
- 钩子回调里**只发通道消息**，不做任何耗时操作，否则系统输入会卡顿。
- 热键插件若不发 `MOD_NOREPEAT`，长按会重复触发；实测不符就退回自写 `RegisterHotKey` 线程。

**验收**：一张“平台能力对照表”，16 项内置动作、3 类枚举、3 类启动方式逐项打勾，并附 C#/Rust 截图对比。

### M3 UI 重写（L）

按窗口推进，先静态后交互：

1. **启动面板**：标签栏（新建/重命名/删除/拖拽排序）、网格与列表两种布局、搜索框、便签抽屉、锁定遮罩、
   空状态、右键菜单（打开 / 管理员运行 / 打开位置 / 复制路径 / 重命名 / 删除）。
2. **设置窗口**：六个分区与现有 `SettingsWindow` 对齐——常规、外观、偏好、唤出与收起、安全与隐私、维护。
   所有 20+ 个设置项都要有对应控件，且即时生效（`config://changed` → 主窗应用）。
3. **应用库**：四个来源分标签、搜索、拖拽到启动坞、双击添加。
4. **新建条目 / 关于**：与现有字段一致（名称、目标、参数、工作目录、自定义图标、管理员）。
5. **交互细节**：单击/双击策略、`Esc` 收起、失焦隐藏（仅在热键注册成功时生效）、`Ctrl` 抑制隐藏、
   拖入文件/文件夹自动建条目、DPI 缩放、浅色/深色主题。

**设计方向**（沿用现有气质，不做无谓改版）：暖棕强调色 `#8A6A5C`，圆角 6/8/12，
字号阶梯 11/11.5/12/13/15/18，中等密度、弱边框、以留白分组。字体优先
`MiSans` / `HarmonyOS Sans SC` / `微软雅黑 UI` 回退栈，避免 Inter 带来的“通用 AI 风”。

**验收**：README 功能清单逐条走查（见第 6 节），并覆盖 100%/125%/150% DPI、三档材质、两种主题。

### M4 打磨与兼容（M）

- **冷启动掩盖**：开机/启动时立即创建隐藏窗口（`skipTaskbar`、不抢焦点），热键只做 `show`，避免首次呼出 300 ms 空白。
- 单实例二次启动 → 唤出已有窗口；`startHidden` 与“热键未注册时不隐藏”的规则保持一致。
- 关闭行为（退出 / 最小化到托盘）、托盘双击、`--hidden` 自启。
- LiveFolder 的 `notify` 监听与去抖、外部删除后的自愈。
- 配置写入防抖、退出前强制落盘；旧图标缓存一次性清理。
- 内存与启动耗时基线记录（空闲、100 条目、便签打开三种场景）。

**验收**：连续两版自用无回归；配置/便签/图标缓存与 C# 版本可来回切换不丢数据。

### M5 打包发布（S）

- NSIS 安装包（per-user，免管理员）、便携 zip；图标复用 `Assets/app.ico`。
- WebView2 安装策略：优先系统已有运行时；离线场景用 bootstrapper。
- 卸载保留用户配置；升级不清 `config.json`。
- 干净 Win10 19041 与 Win11 各跑一遍安装/升级/卸载/自启。

---

## 5. 命令与事件契约（前端只通过这一层访问系统）

| 命令 | 入参 | 出参 |
| --- | --- | --- |
| `config_get` | — | `AppConfig` |
| `config_update` | `patch` | `AppConfig`（落盘后广播） |
| `tab_add` / `tab_rename` / `tab_delete` / `tab_reorder` | `tabId`、`name`、`order` | `AppConfig` |
| `item_add` / `item_update` / `item_delete` / `item_reorder` | `tabId`、`ItemModel` | `AppConfig` |
| `item_launch` | `itemId`、`forceAdmin` | `bool` |
| `item_open_location` / `item_copy_path` | `itemId` | — |
| `icon_get` | `target`、`customIcon` | 缓存 PNG 路径（前端 `convertFileSrc`） |
| `library_list` | `kind: folders/tools/start-menu/store` | `Vec<LibraryEntry>` |
| `hotkey_validate` / `hotkey_apply` | `gesture` | `bool` |
| `hooks_apply` | `x1`、`x2`、`middle` | `bool` |
| `maintenance_run` | `RepairIcons / SortByName / RemoveDuplicates / RemoveInvalid / RemoveEmptyTabs` | 报告 |
| `autostart_set` / `shortcut_create` | `bool` / `desktop\|taskbar` | `bool` |
| `password_set` / `password_verify` / `password_clear` | `pwd` | `bool` |
| `notes_list` / `notes_read` / `notes_save` / `notes_delete` | `name`、`content` | 文件列表 / 内容 |
| `panel_show` / `panel_hide` / `panel_toggle` | — | — |
| `app_exit` | — | — |

| 事件（Rust → 前端） | 触发 |
| --- | --- |
| `config://changed` | 任意窗口改了配置 |
| `hotkey://pressed` | 全局热键 |
| `mouse://show` | 扩展键 / 中键 |
| `library://dropped` | 拖入文件路径（可选，也可用 Tauri 原生 drag-drop 事件） |
| `live-folder://changed` | 监听目录变化 |

**性能约定**：`icon_get` 走 `async` + `spawn_blocking`，禁止在 IPC 主线程做 COM 调用；
批量场景前端并发上限 4，避免同时挤占 2 个 STA worker。

---

## 6. 功能对等验收清单（对照 README）

| # | 功能 | 验收点 |
| --- | --- | --- |
| 1 | 分组与布局 | 多标签、拖拽排序、右键菜单、网格/列表切换 |
| 2 | 应用库 | 四个来源、搜索、拖拽添加 |
| 3 | LiveFolder | 绑定目录、目录变化实时刷新、重新绑定/打开源目录 |
| 4 | 全局热键 | `Alt+Space` 默认、自定义、冲突提示与回滚、禁用开关 |
| 5 | 鼠标唤出 | XButton1 / XButton2 / 中键分别可控 |
| 6 | 搜索 | 名称 / 前两字 / 路径；`Esc` 退出搜索 |
| 7 | 便签 | 列表 + 编辑、自动保存、自定义目录 |
| 8 | 主题 | 跟随系统 / 浅色 / 深色 |
| 9 | 材质与透明度 | 亚克力 / 云母 / 纯色；透明度只对亚克力与纯色生效 |
| 10 | 图标与名称 | 图标尺寸、间距、名称位置（下/右/无） |
| 11 | 唤出位置 | 保持当前位置 / 屏幕中央 / 鼠标附近 |
| 12 | 收起策略 | 失焦、自动收起、`Esc`、`Ctrl` 抑制 |
| 13 | 任务栏 / 桌面快捷方式 | 创建成功且指向正确 exe |
| 14 | 维护 | 修复图标、按名称排序、清理重复/失效/空标签 |
| 15 | 密码锁 | 设置/修改/清除、自动锁定、分组锁定 |
| 16 | 开机自启 | Run 键写入/清理、`--hidden` 生效 |
| 17 | 托盘 | 图标、菜单、双击显示、关闭行为 |
| 18 | 贴边吸附 | 四边吸附、锁定位置 |
| 19 | 单击/双击 | 运行策略全局生效 |
| 20 | 配置自动保存 | 变更后落盘、崩溃后不丢 |

---

## 7. 风险与对策

| 风险 | 表现 | 对策 |
| --- | --- | --- |
| COM 套间错误 | `.msc` 等图标变成空白文档 | 专用 STA 线程 + `CoInitializeEx(APARTMENTTHREADED)`；与 C# 输出比对 |
| HBITMAP/alpha | 图标带黑底或边缘发黑 | `GetDIBits` 32bpp 负高度读取、补 alpha、及时释放；PNG 与 C# 基线比对 |
| WebView2 内存/冷启动 | 常驻 80–150 MB、首次呼出延迟 | 启动即建隐藏窗口预热；关闭不必要的后台定时器；指标写入 README |
| 透明 + 材质 | 黑边、圆角失效、Win10 无 Mica | `transparent` + `windowEffects` + `DWMWA_WINDOW_CORNER_PREFERENCE`；Win10 回落 Acrylic/纯色 |
| 热键语义差异 | 长按重复触发、注册失败不回滚 | 实测插件行为，不符则自写 `RegisterHotKey` 线程；保留“注册失败恢复旧键” |
| 低级鼠标钩子 | 输入卡顿、被杀软拦截 | 回调只发通道；钩子独立线程；设置项可关闭 |
| WinRT 调用阻塞 | 启动或打开应用库卡顿 | 独立 MTA 线程 + 缓存枚举结果（首次后台预热） |
| DPI 换算 | 窗口位置偏移、尺寸抖动 | 位置用物理像素、尺寸用逻辑像素，配置读写统一转换 |
| 安装目录不可写 | 便签保存失败 | 优先 exe 目录，失败回落 AppData 并提示 |
| 配置并发写 | 多窗口互相覆盖 | Rust 单一数据源 + Mutex + 防抖写 + 原子替换 |
| 管理员目标 | UAC 弹窗、启动失败 | `runas` verb；程序自身不要求管理员运行 |

---

## 8. 明确不迁移 / 主动降级的项

1. **`SystemFontLister`**：Web 端不需要字体枚举，`font-family` 直接用系统字体；设置页保留“手动填写字体名”。
2. **拼音首字母搜索**：现有实现只取名称前两字，并未真正支持拼音；如需补齐，用 `pinyin` crate（属于增强，不属于对等）。
3. **Avalonia 控件模板**：全部重建为 CSS，不复刻 Fluent 模板细节。
4. **`Avalonia.Diagnostics`**：用 DevTools + `tauri-plugin-log` 替代。
5. **README 中的旧路径描述**（`%APPDATA%\LaunchDock`）：迁移后统一更正为 `LaunchDock`。

---

## 9. 里程碑门禁

| 门禁 | 通过条件 |
| --- | --- |
| M1 → M2 | 配置双向兼容测试全绿，C# 能读 Rust 写的配置 |
| M2 → M3 | 图标/启动/枚举/热键/钩子五项对照表全绿 |
| M3 → M4 | 第 6 节 20 项功能中至少 18 项可用，无阻塞级缺陷 |
| M4 → M5 | 20/20 通过，配置可来回切换，连续两版自用无回归 |
| M5 之后 | 才允许把 C# 工程移入 `legacy/` 或归档 |

---

## 10. 第一批具体任务（可直接开工）

1. `npm create tauri-app@latest launchdock-tauri -- --template react-ts`，按第 1 节拆 workspace。
2. 写 `core::model` + `core::config`，用现有 `config.json` 做 round-trip 测试（最先拿到确定性收益）。
3. 主窗口按 M0 参数配置，接单实例 + 托盘 + `Alt+Space`，先跑通“空面板显隐”。
4. 实现 `icon_get` 的 STA 提取链路，拿 5 个目标与 C# 输出比对，确认没有黑底/空白图标。
5. 再动 UI：先做启动面板的静态布局，再接命令，最后补交互。
