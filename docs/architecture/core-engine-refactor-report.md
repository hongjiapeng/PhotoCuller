# PhotoCuller 核心 Engine 重构报告

日期：2026-09-04  
实施分支：`codex/core-engine-decoupling`  
详细审计与方案：[core-engine-refactor.md](core-engine-refactor.md)

## 1. Before

```text
PhotoCuller.App.WinUI
    -> PhotoCuller.Core
    -> PhotoCuller.Infrastructure
        -> PhotoCuller.Core
```

`Core` 已通过基于项目名称的 MSBuild 条件使用 `net10.0`，且没有 WinUI/WinRT 类型；但是默认 TFM 仍是 `net10.0-windows`，可移植性不够显式。更关键的问题是完整分析用例位于 `MainViewModel.StartScanAsync`：UI 依次调用扫描、缩略图、SHA-256、pHash、评分和分组服务。任何 CLI、Skill 或 Agent 客户端都必须复制这一顺序和错误处理。

Infrastructure 同时引用 `OpenCvSharp4` managed API 与 `OpenCvSharp4.runtime.win` native runtime，使 Windows runtime 通过基础设施项目传递给所有客户端。项目也没有面向机器调用的版本化输出协议。

另一个既有问题是 WinUI 会在 scanner 执行 repository 增量判断前先清空数据库，因此当前 WinUI 路径虽保留“增量扫描”代码，实际行为仍是全量扫描。本轮没有混入扫描语义变更。

## 2. After

```text
PhotoCuller.Core (net10.0, BCL only)
        ^
        |
PhotoCuller.Application (net10.0)
    - IPhotoAnalysisEngine / PhotoAnalysisEngine
    - PhotoAnalysisOptions
    - PhotoAnalysisRun
    - PhotoAnalysisReport v1.0
        ^
        |
PhotoCuller.Infrastructure (net10.0)
    - scanner, metadata, imaging, OpenCV managed implementation
    - storage, file operations, optional existing model providers

PhotoCuller.App.WinUI (net10.0-windows)
    -> Application + Infrastructure
    -> OpenCvSharp4.runtime.win

PhotoCuller.Cli (net10.0)
    -> Application + Infrastructure
    -> OpenCvSharp4.runtime.win（当前 Windows 发行组合）
```

WinUI 的扫描命令现在调用 `IPhotoAnalysisEngine.AnalyzeAsync`，只继续负责目录选择、进度/通知、结果持久化与 ViewModel 映射。CLI 直接解析参数、构建同一 Engine、序列化同一运行结果中的稳定 Report，不包含分析算法副本。

Engine 保留了原调用次序与实现：

```text
scan -> thumbnail -> SHA-256 -> pHash -> scoring -> grouping/recommendation
```

算法类、阈值和推荐规则没有重写。

### CLI

```powershell
dotnet run --project src\PhotoCuller.Cli -- analyze "D:\Photos\Test"
dotnet run --project src\PhotoCuller.Cli -- analyze "D:\Photos\Test" --output result.json
```

也保留 `scan` 作为 `analyze` 的别名。成功结果使用 camelCase、字符串 enum 与显式 `version: "1.0"`，包含照片元数据、各质量维度、错误、推荐状态以及分组成员/推荐照片 ID。

## 3. 跨平台状态

| Project | Cross-platform | 原因 |
| --- | --- | --- |
| `PhotoCuller.Core` | **Yes** | 显式 `net10.0`；无 PackageReference、Windows SDK、WinUI、WinRT 或 OpenCV 类型 |
| `PhotoCuller.Application` | **Yes** | 显式 `net10.0`；只依赖 Core 与 logging abstractions；输入、进度、结果均无 UI 类型 |
| `PhotoCuller.Infrastructure` | **Partial** | 项目可用 `net10.0` 编译且不再携带 Windows native runtime；实际图像分析仍要求最终客户端提供对应 OS/CPU 的 OpenCV native runtime，并需逐平台验证编解码能力 |
| `PhotoCuller.App.WinUI` | **No** | `net10.0-windows`、Windows App SDK、WinUI/WinRT Picker 与 Windows shell 集成 |
| `PhotoCuller.Cli` | **Partial** | CLI/API 本身使用 `net10.0`，但当前发行组合显式引用 `OpenCvSharp4.runtime.win`；替换为目标平台 runtime 后无需改 Engine |

## 4. 剩余 Windows-specific dependencies

| Package / API | Project | 原因 |
| --- | --- | --- |
| `Microsoft.WindowsAppSDK` | App.WinUI | WinUI 3 窗口、页面、控件与 Dispatcher |
| `Microsoft.Windows.SDK.BuildTools` | App.WinUI | Windows App SDK 构建工具链 |
| `WinUIEx` | App.WinUI | WinUI 窗口扩展 |
| `Windows.Storage.Pickers`, `WinRT.Interop` | App.WinUI | Folder/File Picker 与 HWND 初始化 |
| `BitmapImage`, `DispatcherQueue`, Microsoft.UI XAML | App.WinUI | 图片呈现与 UI 线程派发 |
| `Windows.System` / Explorer shell | App.WinUI | 打开文件或所在目录 |
| `RuntimeIdentifier=win-x64` | App.WinUI | 当前 Windows x64 发行目标 |
| `OpenCvSharp4.runtime.win` | App.WinUI, CLI, Tests | 当前 Windows OpenCV native binaries；已从 Infrastructure 移出 |

`OpenCvSharp4` managed 类型仅存在于 Infrastructure 实现内部，没有穿透到 Core、Application API 或结果 DTO。

## 5. Future blockers

### Windows

- 当前 runtime 组合可用；仍需针对真实 RAW/HEIC/大图数据集验证编解码器、内存占用与算法准确率。
- 将 scanner 的增量过滤从当前“先清库再判断”矛盾中拆出，明确提供全量替换与增量更新两个 Application 用例。
- NuGet 还原报告 ImageSharp 3.1.10 的中等级 advisory 和 SQLitePCLRaw.lib.e_sqlite3 2.1.11 的高等级 advisory；应在独立依赖升级中处理并做图像/数据库回归。

### Linux

- CLI/未来服务组合根需改用并验证 Linux OpenCV runtime（glibc/musl 与 CPU 架构需分别考虑）。
- 验证 HEIC/HEIF 等格式所需的系统 codec/native library。
- 验证 SQLite native assets、ONNX runtime provider，以及大小写敏感路径/权限/符号链接行为。
- WinUI 客户端不可复用；Engine、Application contract 与 CLI 参数层可复用。

### macOS

- CLI/未来客户端组合根需选择并验证 macOS arm64/x64 OpenCV runtime。
- 验证 ImageSharp/MetadataExtractor 对目标照片格式与 EXIF 的实际支持，尤其 HEIC。
- 验证 SQLite、ONNX runtime provider、应用沙盒与照片目录授权。
- 若未来开发 GUI，只新增 macOS client；不改变 Engine contract。

## 6. 验证结果

| 验收项 | 结果 |
| --- | --- |
| Core 脱离 `-windows` 独立编译 | 通过：`dotnet build src/PhotoCuller.Core/PhotoCuller.Core.csproj --no-restore`，0 error |
| Core/Application Windows API 扫描 | 通过：未发现 Microsoft.UI、Windows、WinRT、WinUI 类型或 OpenCvSharp 类型 |
| CLI 空目录 | 通过：返回 `version: 1.0`、空 `photos` 与空 `groups` |
| CLI 单张真实 PNG | 通过：输出尺寸、文件信息与完整技术质量字段 |
| CLI 完全重复照片 | 通过：2 张照片形成 1 个 `exactDuplicate` group，并产生推荐 ID |
| CLI `--output` | 通过：写入并重新解析 JSON，版本和计数正确 |
| 完整 Release build | 通过：`dotnet build PhotoCuller.slnx --configuration Release --no-restore --verbosity minimal`，0 error（保留 187 个既有 warning） |
| 自动化测试 | 通过：`dotnet test PhotoCuller.slnx --configuration Release --no-build --no-restore --verbosity minimal`，81 passed、0 failed、0 skipped |
| WinUI Release 启动与 DI | 通过：Release 可执行文件成功启动，数据库初始化完成，并由重构后的依赖注入图解析 `MainViewModel` 与 `IPhotoAnalysisEngine` |
| WinUI 结果客户端 | 通过：可访问性树显示目录选择、开始扫描、40 张既有照片、8 个分组、推荐项及评分字段，说明 WinUI 仍可加载和呈现已有结果 |

Debug solution build 的首次验证被用户已运行的 `PhotoCuller` 进程锁定现有 Debug DLL，并非编译错误；最终验证使用独立 Release 输出，避免中断该进程。

WinUI 验证没有对用户当前照片目录重新执行扫描：现有客户端在开始扫描前会清空分析数据库，这会覆盖当前 40 张照片/8 个分组的缓存。为避免把 UI 冒烟测试变成用户数据变更，本轮用 Release 启动与已有结果呈现验证客户端装配，用 CLI 真实图片冒烟和 Engine 自动化测试验证重构后的完整 pipeline。目录选择、重扫和原图移动/删除操作应在专用测试配置中继续做端到端验收。
