# LinuxDo for Windows

LinuxDo 2.0 的 Windows 版，基于 WPF（Fluent 主题）和 Microsoft Edge WebView2。窗口、工具栏、侧栏和设置是原生控件，帖子、登录及回复由 linux.do 网页承载。功能与 [macOS 版](https://github.com/245678000000/LinuxDo-mac) 对齐。

这是独立客户端项目，与 linux.do 官方的关联关系未经确认。

## 下载

在 [Releases](https://github.com/245678000000/LinuxDo-windows/releases) 中下载：

| 文件 | 说明 |
| --- | --- |
| `LinuxDo-<版本>-Setup-x64.exe` | 安装程序，适用于绝大多数 Windows 电脑 |
| `LinuxDo-<版本>-Setup-arm64.exe` | 安装程序，适用于 ARM 版 Windows（如骁龙笔记本） |
| `LinuxDo-<版本>-win-<架构>.zip` | 便携版，解压后直接运行 `LinuxDo.exe` |

- 需要 Windows 10 1809 或更新版本。程序自带 .NET 运行时，不需要另外安装 .NET。
- 需要 WebView2 运行时。Windows 11 和更新过的 Windows 10 已经自带；缺少时应用会给出微软的下载链接。
- 安装程序默认装在当前用户目录，不需要管理员权限。
- 安装程序没有代码签名。首次运行若出现“Windows 已保护你的电脑”，点“更多信息” → “仍要运行”。

## 功能

- 原生工具栏：侧栏开关、后退、前进、社区搜索、刷新、下载、复制链接和更多操作。
- 可收起的快捷侧栏；展开时隐藏网页中重复的侧栏。默认收起，可通过工具栏最左边的按钮或 `Ctrl+Shift+S` 打开。
- Windows 11 Mica 窗口材质和 Fluent 控件；浅色、深色或跟随系统，网页同步收到配色偏好。
- 页内查找使用 WebView2 自带的查找栏；多窗口；正文阅读字号和行距设置。
- 阅读位置记录、回复草稿提醒（切换页面、关闭窗口前确认）、下载面板和每次询问保存位置的选项。
- 任务栏图标上显示未读数。
- 可选 HTTP / SOCKS5 独立代理，保存后重新打开应用生效。
- 网站数据保存在 `%LOCALAPPDATA%\com.pake.acea3c3`，沿用原 Pake 版 LinuxDo 的应用标识。用过 Pake 版 Windows 客户端的话，通常可以直接沿用登录状态。

与 macOS 版的差异：

- macOS 的“分享”在 Windows 上改为“复制页面链接”。
- 独立代理在所有支持的 Windows 版本上可用，但保存后需要关闭全部窗口、重新打开应用才生效（WebView2 每个进程只读取一次代理参数）。
- 社区搜索快捷键为 `Ctrl+L`。`Ctrl+K` 在网站编辑器中是“插入链接”，不占用。

## 构建

需要 .NET 10 SDK。程序只能在 Windows 上运行；在 macOS 或 Linux 上也可以编译和发布 Windows 程序（项目开启了 `EnableWindowsTargeting`），但不能生成安装程序。

```powershell
# 发布 x64 程序目录、便携版 zip 和安装程序（安装程序需要 Inno Setup 6）
./build.ps1 release -Arch x64

# ARM64
./build.ps1 release -Arch arm64
```

产物：

```text
artifacts/publish/<架构>/LinuxDo.exe
artifacts/LinuxDo-<版本>-win-<架构>.zip
artifacts/LinuxDo-<版本>-Setup-<架构>.exe
```

图标由 `tools/make_icon.py` 生成（只依赖 Python 标准库）：

```sh
python3 tools/make_icon.py src/LinuxDo/Resources/AppIcon.ico
```

## 开发与验证

```powershell
# 核心逻辑测试：域名、搜索编码、代理、下载文件名、标题徽标及阅读位置（macOS / Linux 上也能运行）
./build.ps1 test

# 调试版
./build.ps1 debug

# 离线界面预览，不连接 linux.do
LinuxDo.exe --preview

# 离线集成测试：结果写入 %TEMP%\LinuxDoSelfTest.txt，截图写入 %TEMP%\LinuxDoSelfTest\
LinuxDo.exe --preview --self-test
```

预览使用临时目录中的独立设置和网站数据，每次启动都会清空。GitHub Actions 在每次推送时于 Windows x64 和 Windows 11 ARM64 上运行核心测试、离线集成测试，并静默安装、卸载一次安装程序。真实账号浏览测试需单独执行。

## 快捷键

| 操作 | 快捷键 |
| --- | --- |
| 新建窗口 | `Ctrl+N` |
| 关闭窗口 | `Ctrl+W` |
| 搜索社区 | `Ctrl+L` |
| 页内查找 | `Ctrl+F` |
| 重新加载 | `F5` / `Ctrl+R` |
| 强制重新加载 | `Ctrl+F5` / `Ctrl+Shift+R` |
| 后退 / 前进 | `Alt+←` / `Alt+→` |
| 首页 | `Alt+Home` |
| 显示或收起侧栏 | `Ctrl+Shift+S` |
| 下载 | `Ctrl+J` |
| 放大 / 缩小 / 实际大小 | `Ctrl++` / `Ctrl+-` / `Ctrl+0` |
| 设置 | `Ctrl+,` |
| 在新窗口打开站内链接 | `Ctrl+点击` |

## 代码结构

```text
src/LinuxDo/          WPF 窗口、工具栏、侧栏、设置、对话框及 WebView2 桥接
  Core.cs             与界面无关的配置、设置存储、代理解析、阅读位置（测试项目直接引用）
  PageBridge.cs       注入页面的脚本：阅读排版、网页侧栏、阅读位置、草稿检测
  SelfTest.cs         离线集成测试
  Resources/          图标与离线预览页面
tests/LinuxDo.Tests/  核心逻辑测试
installer/            Inno Setup 安装脚本
tools/                图标生成工具
build.ps1             测试、调试、发布及安装程序构建
```

页面桥接只传递阅读位置和是否存在草稿的布尔值，不传递 Cookie 或草稿正文。真实账号会话由 WebView2 的用户数据目录管理，不属于仓库内容。
