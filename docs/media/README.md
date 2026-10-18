# 演示资源

这里的 GIF 来自 Windows 11 上实际运行的 ClipNest 0.3.1 窗口。
记录中的文字、来源标签与图片均为专门创建的演示数据，不包含用户剪贴板历史。
字幕和场景转场由脚本添加；界面本身未重绘。

- `overview.gif`：约 22 秒，展示列表、图片预览、搜索、收藏、设置和快捷键录入。
- `shortcut.gif`：约 7 秒，展示直接按组合键录入。
- `preview.png`：图片预览静态截图。
- `social-preview.png`：带说明的静态展示图。

## 复现

制作脚本需要 Python 3 和 Pillow。先退出正在运行的拾贴，为演示选择全新目录：

```powershell
python build/demo/seed_demo.py C:/Temp/ClipNest-Demo
$env:CLIPNEST_DATA_ROOT = 'C:/Temp/ClipNest-Demo'
# 启动本地构建的 ClipNest.exe，操作并捕获仅包含应用窗口的 PNG。
python build/demo/make_readme_media.py C:/Temp/ClipNest-Captures docs/media
```

捕获文件依次命名为 `01-history.png`、`02-image.png`、`03-search.png`、
`04-favorites.png`、`05-settings.png`、`06-shortcut.png`。
种子脚本会拒绝覆盖已有数据库；演示结束后移除测试环境变量，再启动正式程序。
