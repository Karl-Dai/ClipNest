<div align="center">

<img src="design/clipnest-512.png" alt="ClipNest icon" width="96" />

# ClipNest · 拾贴

[简体中文](README.md) · **English**

**Find what you copied, whenever you need it.**

A native clipboard manager for Windows 11. Keep text, images and frequently used snippets in a floating panel.

[![Windows 11](https://img.shields.io/badge/Windows_11-22H2%2B-08786E?logo=windows&logoColor=white)](#download-and-install)
[![Native WinUI 3](https://img.shields.io/badge/Native-WinUI_3-08786E)](#development-and-builds)
[![Release](https://img.shields.io/github/v/release/Karl-Dai/ClipNest?color=08786E)](https://github.com/Karl-Dai/ClipNest/releases/latest)
[![Build Windows](https://github.com/Karl-Dai/ClipNest/actions/workflows/build-windows.yml/badge.svg)](https://github.com/Karl-Dai/ClipNest/actions/workflows/build-windows.yml)

[**Download for Windows**](https://github.com/Karl-Dai/ClipNest/releases/latest) · [Demo](#demo) · [User guide](#user-guide) · [Report an issue](https://github.com/Karl-Dai/ClipNest/issues)

</div>

The app interface is currently in Simplified Chinese. This guide includes the original labels where needed; bilingual documentation does not mean the app UI has an English translation yet.

## Why ClipNest

A link disappears after the next copy. You have to paste a screenshot just to check it. You keep looking up the same reply. These small tasks should not interrupt your work.

ClipNest keeps clipboard history on your computer. Press **Ctrl + Shift + V**, search or select an entry with the arrow keys, check its preview, and press **Enter** to paste. Hide the panel and carry on.

## Demo

![ClipNest in use: clipboard history, image previews with arrow keys, search, favorites and shortcut recording](docs/media/overview.gif)

*An approximately 22-second loop recorded from the app on Windows 11. All entries shown were generated specifically for the demo.*

| Feature | What it does |
| --- | --- |
| Floating panel | Opens near the pointer, with a draggable header; stays out of the taskbar and Alt+Tab list |
| Keyboard navigation | ↑ / ↓ selects entries and updates the right-hand preview; Enter pastes |
| Image previews | Thumbnails in the list, a scaled full-image preview on the right, plus source and timestamp |
| Search and filters | Search content or source app; filter by text, images, files, colors or rich text |
| Favorites | Keep links, replies and snippets; favorites do not count toward the ordinary history limit |
| Record a shortcut | Focus the field and press a key combination; includes conflict detection and a reset option |
| Choose a data folder | Migrate history, favorites, images and settings to an empty folder, retaining the original as a backup |
| Native appearance | WinUI 3, Acrylic background, light / dark / system theme |
| System tray | Keeps recording while hidden; supports pausing capture and starting at Windows sign-in |
| Installation and updates | Desktop / Start menu shortcuts; installed builds check for and download stable updates |

### Supported content

| Type | Capture and preview | Copy / paste |
| --- | --- | --- |
| Text, links, code snippets | Original text preview and search | Text |
| Images | List thumbnails and a right-hand preview | Images |
| Files and folders | A list of paths | References to the original files and folders |
| Color text | Recognizes and categorizes values such as `#08786E` | Color text |
| RTF rich text | Formatted preview | Rich text and plain text |

File entries store path references: **they do not back up the files themselves**. If a file is moved or deleted, ClipNest cannot restore it from history. Captured images are saved as separate local files.

## Download and install

**Requirements: Windows 11 22H2 or later, x64.**

Version 0.4.1 fixes long-text loading, history pagination and history-limit settings. See the [test report, in Chinese](docs/testing-0.4.1.md) for the validation scope. [Download 0.4.1](https://github.com/Karl-Dai/ClipNest/releases/tag/v0.4.1). This release is not code-signed.

| Edition | File on the release page | Best for |
| --- | --- | --- |
| **Installer (recommended)** | `ClipNest-VERSION-Setup-x64.exe` | Desktop shortcuts and automatic updates |
| Portable | `ClipNest-VERSION-win-x64.zip` | Extracting and running the app, with manual updates |

**Installer:** Open [Releases](https://github.com/Karl-Dai/ClipNest/releases/latest), download `Setup-x64.exe`, and run it. It installs for the current user at `%LOCALAPPDATA%\ClipNest.Windows`, creates desktop and Start menu shortcuts named “拾贴 ClipNest”, and can be uninstalled from Windows Installed apps.

**Portable:** Download the ZIP, extract the entire archive to a permanent folder, and run `ClipNest/ClipNest.exe` inside it.

The portable package includes the .NET and Windows App SDK runtimes. Keep the DLLs, resources and subfolders beside the executable; do not move just the EXE out of the package.

Use `SHA256SUMS-VERSION.txt` on the release page to verify the installer and update package. The portable ZIP also has a `.zip.sha256` file. For example, calculate the installer hash and compare it with the manifest:

```powershell
Get-FileHash .\ClipNest-0.4.1-Setup-x64.exe -Algorithm SHA256
```

### Updating

**Installed builds** check for updates about 15 seconds after launch, then every 6 hours. A new stable release is downloaded and verified in the background, and applied at the next launch. You can also choose **Restart to update (重启更新)**. Settings offers **Check for updates (检查更新)** and an automatic-check toggle. Network errors can be retried later and do not prevent clipboard use.

**Portable builds:** Exit ClipNest from the tray menu, extract the new version, and run it. Portable builds do not replace themselves automatically. You can also run the installer to switch editions; by default, the existing data folder is still used. If sign-in startup is enabled and you move the portable app, disable and re-enable startup in Settings to update its path.

Data is stored outside the application folder by default. Updating or uninstalling the installed edition preserves the separate data folder. Do not place your data inside the installation folder. Disabling automatic checks stops further checks and downloads; an update already downloaded can still be applied at the next launch.

Installation and updates use [Velopack](https://docs.velopack.io/integrating/overview), with this repository’s public GitHub Releases as the update source. The `.nupkg`, `releases.win.json` and `RELEASES` files are used by the updater; most users only need the installer. Current installers are unsigned. Verify downloads against the published SHA256 manifest.

## User guide

### Find and paste an entry

1. Copy content normally in any application.
2. Switch to the destination app and press **Ctrl + Shift + V** to open ClipNest.
3. Search by keyword, or use **↑ / ↓** to select an entry.
4. Check the right-hand preview, then press **Enter** or double-click the entry to paste.

**Copy (复制)** only writes the selected entry to the clipboard, so you can switch apps and paste manually. **Preview (预览)** at the bottom right toggles the preview pane.

| Action | Default behavior |
| --- | --- |
| Ctrl + Shift + V | Show / hide the panel; configurable in Settings |
| ↑ / ↓ | Select the previous / next entry, including while the search field has focus |
| Enter / double-click | Paste into the app that was active before ClipNest opened |
| Esc / click outside / close button | Hide the panel and keep running in the tray |
| Star button | Add / remove the selected entry from favorites |
| Right-click the tray icon → Exit (退出) | Quit the app completely |

Finish or cancel modal dialogs, such as Settings, before hiding the panel.

### Set a shortcut by pressing it

![Recording a keyboard shortcut directly in Settings](docs/media/shortcut.gif)

Open **Settings (设置)** at the top right. Focus **Summon hotkey (唤出快捷键)**, press your preferred combination, then choose **Save (保存)**. **Esc** cancels the current recording; **Tab** moves to the next control.

- The combination must include **Ctrl or Alt**; Shift is optional.
- The main key can be a letter, digit, or F1–F11.
- If registration conflicts with another app, ClipNest reports the conflict and keeps the previous shortcut.
- Choose **Restore Ctrl + Shift + V (恢复 Ctrl + Shift + V)** and save to restore the default.

### Manage history and appearance

Settings includes the history limit, data folder, theme and sign-in startup. Ordinary history defaults to **100 entries**, adjustable from **10 to 10,000**. Favorites do not count toward this limit. The list loads in batches of 100: scroll to the bottom, choose **Load more (加载更多)**, or continue selecting with ↓ to access older entries. The status bar shows the loaded count and matching total.

Lowering the limit and saving immediately removes excess older ordinary entries and their image files; favorites remain. The list reads summaries only. Selecting an entry loads its full content, and copying preserves the complete content.

**Clear history (清理历史)** asks for confirmation, then removes ordinary entries while retaining favorites. The pause button temporarily stops capture. Capture resumes when the app restarts.

## Data and privacy

Clipboard history stays on your computer. ClipNest does not provide clipboard cloud uploads, accounts or synchronization. Installed builds contact GitHub to check versions and download updates; they do not send clipboard entries. **The local database and image files are not encrypted**, so protect them like other personal files.

Default data layout:

```text
%LOCALAPPDATA%\ClipNest\
├─ db\clipnest.db     # History, favorites and settings such as the history limit
├─ blobs\            # Captured images
├─ logs\             # Local application logs
├─ cache\            # Cache folder
├─ appearance.json   # Theme, preview layout and custom hotkey
└─ settings.json     # Initial configuration
```

### Change the data location

Select or enter an **empty folder** in Settings and save. ClipNest waits for pending capture writes, copies the database, images and settings, validates the database, and restarts. The original folder remains as a backup. If migration fails, the app keeps using the original folder and does not overwrite existing destination data.

Old logs and caches remain in the original location; new logs use the new location. The selected location is recorded in `%LOCALAPPDATA%\ClipNest-config\location.json`. If a custom location is unavailable, ClipNest displays a warning and temporarily uses the default folder.

To back up your data, exit ClipNest first and copy the entire data folder. Copying only the database does not include images.

## FAQ

<details>
<summary><strong>The shortcut does nothing. What should I check?</strong></summary>

Open the panel from the tray icon and check the bottom of the panel for a hotkey conflict. Close the app using that combination, or choose a different combination in Settings. Opening ClipNest from the tray retries registration of the current hotkey.

</details>

<details>
<summary><strong>Why does automatic paste fail in some windows?</strong></summary>

Windows running with administrator privileges may restrict input simulation from ordinary apps. Choose **Copy (复制)**, switch to the destination window, and press Ctrl + V manually. Automatic paste can also fail if the destination has closed or focus has changed.

</details>

<details>
<summary><strong>Why is an image missing from history?</strong></summary>

First check whether capture is paused. Some applications do not provide a readable image format when copying. PNG images larger than 16 MB are not captured. If an image file is manually removed from the data folder, its history entry reports the missing file.

</details>

<details>
<summary><strong>Does ClipNest include OCR, AI, screenshot editing or sync?</strong></summary>

These features are not included in the current version. RTF search operates on the raw rich-text content; for Chinese text in an RTF entry, copying it as plain text first is recommended if you need to search it.

</details>

## Development and builds

Build on Windows with the **.NET 8 SDK**. `global.json` selects an available .NET 8 SDK; NuGet dependencies are restored on the first build.

```powershell
git clone https://github.com/Karl-Dai/ClipNest.git
cd ClipNest

dotnet build ClipNest.Windows.sln -c Release
dotnet test tests/ClipNest.Core.Tests -c Release
dotnet run --project src/ClipNest.App

# Build, test, and package the installer, portable edition and update files
powershell.exe -NoProfile -ExecutionPolicy Bypass -File build/package.ps1
```

Output goes to `dist/`: installer, portable archive, update package, update feeds and SHA256 manifests. The packaging script restores the pinned Velopack CLI from `.config/dotnet-tools.json`; pass `-SkipInstaller` to create only the portable package. GitHub Actions also builds, tests and packages the app; download artifacts from [workflow runs](https://github.com/Karl-Dai/ClipNest/actions). See [installation and update documentation, in Chinese](docs/updates.md) for the release process.

```text
src/ClipNest.App/          WinUI 3 UI, tray, hotkeys and settings
src/ClipNest.Core/         Models, SQLite, deduplication and data migration
src/ClipNest.Platform/     Windows clipboard monitoring and reading
tests/ClipNest.Core.Tests/ Core tests
build/                    Packaging, icon and demo-generation scripts
design/                   Original SVG and PNG icon assets
docs/                     Product demos and upstream design documents
```

Set `CLIPNEST_DATA_ROOT` during development to isolate test data from your normal history. Initial data goes in its `ClipNest` subfolder; the location configuration uses a sibling `ClipNest-config` folder. Demo fixture data and recording instructions are described in [the media guide, in Chinese](docs/media/README.md).

## Changelog

See [CHANGELOG.md, in Chinese](CHANGELOG.md) for the full history. **0.4.1** fixes long-text loading, history pagination, immediate history-limit changes and large-image previews; see the [test report](docs/testing-0.4.1.md) for validation details. **0.4.0** adds an installer, desktop / Start menu shortcuts and GitHub automatic updates. **0.3.1** introduces direct hotkey recording. **0.3.0** adds data-folder migration.

## Origins and acknowledgments

The Windows foundation comes from the Windows project in [Karl-Dai/VeloxClip](https://github.com/Karl-Dai/VeloxClip). ClipNest continues development as a separate product. Source history, revision information and licensing background are recorded in [UPSTREAM.md](UPSTREAM.md).

ClipNest’s “note and nest” icon was designed specifically for this app; source assets are in [design](design/). The UI draws on [WinUI Gallery](https://github.com/microsoft/WinUI-Gallery) and the [Microsoft WinUI design skill](https://github.com/microsoft/winappCli/tree/main/plugins/winui/agent-plugin/skills/winui-design).

Report bugs or suggest improvements through [Issues](https://github.com/Karl-Dai/ClipNest/issues). Include the Windows version, ClipNest version and steps to reproduce. Remove private content from logs and screenshots before sharing them.
