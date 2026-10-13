# 拾贴 ClipNest

面向 Windows 11 的本地剪贴板管理器。

## 使用

解压整个便携包，打开 `ClipNest/ClipNest.exe`。请保留同目录下的 DLL 和资源文件。
无需另外安装 .NET 或 Windows App SDK。支持 Windows 11 22H2 及以上，x64。

- `Ctrl + Shift + V`：唤出历史。输入关键词可搜索内容或来源应用。
- 方向键选择，`Enter` 或双击：粘贴到唤出前的应用。
- “复制”：只写入剪贴板；“收藏”：保留常用内容。
- `Esc` 或关闭按钮：隐藏窗口，继续在托盘运行。右键托盘选择“退出”。
- 支持文本、图片、文件列表、颜色文本和 RTF 预览与复制。
- 设置中可调整历史上限（默认 100，范围 10–10000）、启用登录启动、清理未收藏记录。
- 收藏不会被历史上限或“清理历史”删除。每次搜索最多显示 500 条。
- “暂停记录”用于临时停止采集，重启后恢复记录。

数据位于 `%LOCALAPPDATA%\ClipNest`，不会上传或同步。图片独立存储，文本和设置存于 SQLite。
目前不包含 OCR、AI、截图编辑和自定义快捷键。RTF 搜索针对原始 RTF 内容；富文本中的中文建议先以纯文本复制。
自动粘贴可能受到管理员权限窗口限制；若失败，使用“复制”后手动 Ctrl+V。
快捷键冲突时可用托盘打开。关闭冲突程序后重启拾贴即可重新注册。

## 开发

安装 .NET 8 SDK（`global.json` 会选择可用的 8.0 SDK）。

```powershell
dotnet build ClipNest.Windows.sln -c Release
dotnet test tests/ClipNest.Core.Tests -c Release
dotnet run --project src/ClipNest.App
./build/package.ps1
```

`build/package.ps1` 构建、测试并生成包含运行时的便携 ZIP。
设置环境变量 `CLIPNEST_DATA_ROOT` 可将测试数据隔离到指定目录下的 `ClipNest` 子目录。

## 来源

Windows 基础工程源自 [Karl-Dai/VeloxClip](https://github.com/Karl-Dai/VeloxClip)，现作为独立产品维护。
原始来源及署名见 [UPSTREAM.md](UPSTREAM.md)。
