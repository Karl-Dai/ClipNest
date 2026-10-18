# 安装与自动更新

## 目录与快捷方式

应用包 ID 固定为 `ClipNest.Windows`，版本为 SemVer，更新通道为 `win`。
默认安装位置是 `%LOCALAPPDATA%\ClipNest.Windows`；桌面和开始菜单快捷方式显示为“拾贴 ClipNest”。
程序、更新器和下载包位于安装目录；历史默认位于 `%LOCALAPPDATA%\ClipNest`，位置指针位于
`%LOCALAPPDATA%\ClipNest-config`。这几个目录必须分离，卸载与更新不会移除独立的数据目录。

## 更新行为

`Program.Main` 在 WinRT、单实例登记和 UI 初始化之前调用 `VelopackApp.Build().Run()`，
处理安装 / 更新 / 卸载钩子，并自动应用已下载的更新。

安装版通过无令牌的 `GithubSource` 读取 `Karl-Dai/ClipNest` 的稳定版 Releases。
`AppUpdates` 在启动约 15 秒后、之后每 6 小时检查一次，串行下载并由 Velopack 校验更新包。
不会发布剪贴板内容或日志。自动检查开关保存在数据目录的 `appearance.json`。

下载完成后，用户可选择立即重启，也可继续使用并在下次启动时更新。
立即更新使用 `WaitExitThenApplyUpdates`，先安排更新器等待退出，再正常关闭剪贴板监听、数据库任务与日志。
下载失败或校验失败不会提供更新重启入口；便携版不进行自动检查或下载。

## 构建及发布

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File build/package.ps1 -Version 0.4.0
```

脚本固定使用 Velopack 1.2.161，生成：

- `ClipNest-版本号-Setup-x64.exe`：用户安装程序。
- `ClipNest-版本号-win-x64.zip`：原有便携包。
- `ClipNest.Windows-版本号-full.nupkg`：更新器下载的完整更新包。
- `releases.win.json`：包含版本、文件名、大小和摘要的更新清单。
- `RELEASES`：兼容格式的清单。
- `SHA256SUMS-版本号.txt`：所有发布文件的 SHA256 校验值。

必须把更新包与更新清单和安装程序一起上传到**同一个**稳定版 Release，
保持 `.nupkg` 的文件名与 JSON 清单一致。先上传到草稿 Release，全部文件上传并核验后再发布。
仅上传 Setup.exe 无法为现有用户提供自动更新。

手动运行 `release.yml` 工作流并输入版本号时，会构建全部产物，先上传到草稿再发布。
发布前更新 `Directory.Build.props`、应用清单、`build/package.ps1` 默认版本、发布说明及更新记录。
版本必须递增，不覆盖已发布版本的更新包。安装程序当前未代码签名；加入签名时应通过受保护的 CI 机密配置签名服务。

## 测试

核心测试覆盖便携版不联网、下载失败不更新且可重试、已就绪更新不重复下载及无新版本的状态。
发版还需执行安装 / 升级冒烟测试：检查快捷方式、`sq.version`、更新器文件、历史和外观设置，
并验证下载的更新包摘要。请使用独立测试数据目录，不以用户真实历史作测试样本。

`build/smoke-update` 是开发者本地升级测试工具，只用于预先准备的测试安装目录与本地更新源：

```powershell
dotnet run --project build/smoke-update -c Release -- C:/Test/ClipNest.Windows C:/Test/update-feed
```

它以 0.3.9 包元数据作为基线，下载更新并独立核对 SHA256，再由真实 `Update.exe` 应用更新。
运行前必须关闭测试安装的应用。0.4.0 的实际验证结果见 [测试记录](testing-0.4.0.md)。

参考：[Velopack 集成](https://docs.velopack.io/integrating/overview)、
[快捷方式](https://docs.velopack.io/integrating/shortcuts)、
[自托管分发](https://docs.velopack.io/distributing/self-hosting)。
