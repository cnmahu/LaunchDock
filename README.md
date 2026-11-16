# 启坞 LaunchDock

> 你的桌面启动坞 —— 常用应用，一坞直达。

启坞是一款 Windows 桌面快捷菜单与启动效率工具。你可以把常用应用、文件、文件夹、网页链接和命令统一收纳进启动坞，按工作、学习、设计、开发等场景自由分组；需要时通过全局热键一键呼出，点击即可启动，不必回到桌面翻找。

本项目基于 **.NET 8 + Avalonia** 从零实现，用于学习桌面 UI 框架与 Windows Shell 集成。

## 功能

- **分组与布局**：多标签分组、拖拽排序、右键菜单（重命名 / 删除 / 打开位置等），网格与列表两种布局。
- **应用库**：自动枚举系统工具、开始菜单应用、Microsoft Store 应用与常用网页，支持拖拽添加到启动坞。
- **文件夹分组（LiveFolder）**：绑定任意目录，目录内容变化时实时刷新。
- **全局热键**：默认 `Alt+Space` 唤出 / 收起，可自定义；支持鼠标扩展键与中键唤出。
- **搜索**：按名称、拼音首字母或路径跨分组搜索。
- **便签**：面板内左侧列表 + 右侧编辑区，输入自动保存，可自定义存储目录。
- **外观**：主题（跟随系统 / 浅色 / 深色）、窗口材质（亚克力 / 云母 / 纯色）、不透明度、图标大小与间距、名称位置。
- **唤起与收起**：面板唤出位置（当前位置 / 屏幕中央 / 鼠标位置）、自动收起、Esc 收起、任务栏与桌面快捷方式。
- **维护**：修复图标、按名称排序、清理重复项 / 失效项 / 空标签。
- **其他**：密码锁、开机自启、托盘图标、贴边吸附、单击打开或双击运行、配置自动保存。

## 技术栈

| 项目 | 说明 |
| --- | --- |
| 运行时 | .NET 8（`net8.0-windows10.0.19041.0`） |
| UI | Avalonia 11.3（Fluent 主题、ThemeDictionaries 明暗主题） |
| 平台 | Win32 P/Invoke（图标提取、全局热键、鼠标钩子、窗口材质） |
| 商店应用 | WinRT `PackageManager` / `Package.GetAppListEntries()` |

## 目录结构

```
LaunchDock.sln
src/
  LaunchDock.Core/      # 配置模型、JSON 存储、密码哈希、便签目录规则
  LaunchDock.Windows/   # Windows 平台：图标、热键、鼠标钩子、启动、应用库、快捷方式
  LaunchDock.App/       # Avalonia 界面：启动板、设置、应用库、便签、关于
```

## 下载

最新 Windows x64 便携版请到 [Releases](https://github.com/cnmahu/LaunchDock/releases/latest) 下载，解压后运行 `LaunchDock.exe` 即可。

## 构建与运行

要求：Windows 10 1809+（内部版本 19041 及以上）、.NET 8 SDK。

```powershell
dotnet build LaunchDock.sln -c Debug
dotnet run --project src/LaunchDock.App
```

发布单文件（可选）：

```powershell
dotnet publish src/LaunchDock.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

> 注意：`IncludeNativeLibrariesForSelfExtract` 必须开启，否则 SkiaSharp 原生库不会随单文件包解出，启动时会抛 `DllNotFoundException`。

## 数据位置

| 内容 | 路径 |
| --- | --- |
| 配置文件 | `%APPDATA%\LaunchDock\config.json` |
| 图标缓存 | `%LOCALAPPDATA%\LaunchDock\icons` |
| 便签（默认） | 程序所在目录下的 `Notes` 文件夹，可在「设置 → 概要 → 便签」修改 |

配置采用原子写入（临时文件 + 替换），图标缓存会按目标路径与修改时间失效重建。

## 说明

- 界面与交互为本项目原创设计，代码为本项目原创实现。
- 程序集名与配置目录统一使用 `LaunchDock`，界面显示名称为「启坞 LaunchDock」。

## 许可证

[MIT](LICENSE)
