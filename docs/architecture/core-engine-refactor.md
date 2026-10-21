# PhotoCuller 核心 Engine 解耦审计与实施方案

日期：2026-09-04  
分支：`codex/core-engine-decoupling`

## 目标与验收标准

本轮只建立可复用的照片分析 Engine，不迁移 WinUI、不引入跨平台 GUI，也不新增 AI/VLM 集成。验收标准按优先级为：

1. `PhotoCuller.Core` 使用非 Windows TFM，可以独立编译，且不引用 WinUI、WinRT、Windows SDK 或 native runtime。
2. WinUI 只拥有客户端职责；扫描、哈希、评分和分组流水线由 UI 无关的 Application Engine 编排。
3. `PhotoCuller.Cli` 调用与 WinUI 相同的 Engine，并可输出版本化 JSON。

## Before：当前结构与依赖

```text
PhotoCuller.App.WinUI (net10.0-windows10.0.19041.0)
    -> PhotoCuller.Core (net10.0，通过 Directory.Build.props 项目名条件覆盖)
    -> PhotoCuller.Infrastructure (net10.0，通过项目名条件覆盖)
        -> PhotoCuller.Core

PhotoCuller.Tests (net10.0，通过项目名条件覆盖)
    -> PhotoCuller.Core
    -> PhotoCuller.Infrastructure
```

### 项目审计

| 项目 | 当前职责 | 平台状态 | 关键依赖 |
| --- | --- | --- | --- |
| `PhotoCuller.Core` | Model、enum、接口、评分权重 | 理论及实际可跨平台 | CommunityToolkit.Mvvm、Microsoft.Extensions DI/Logging/Options（当前源码未使用） |
| `PhotoCuller.Infrastructure` | 扫描、元数据、图像处理、哈希、分组、SQLite、文件操作、AI provider | 部分可跨平台 | ImageSharp、MetadataExtractor、OpenCvSharp4、`OpenCvSharp4.runtime.win`、EF Core SQLite、ONNX Runtime |
| `PhotoCuller.App.WinUI` | WinUI 页面、ViewModel、Picker、通知、主题、Explorer 集成，同时编排完整分析流水线 | Windows-only | Microsoft.WindowsAppSDK、Windows SDK BuildTools、WinUIEx |
| `PhotoCuller.Tests` | Core/Infrastructure 测试 | 测试代码可用 `net10.0` 编译；OpenCV 用例需要宿主 native runtime | xUnit、FluentAssertions、NSubstitute |

### Windows API / WinRT 泄漏检查

`PhotoCuller.Core` 和普通分析接口中未发现以下类型或命名空间：

```text
Microsoft.UI.*
Windows.*
WinRT.*
StorageFile / StorageFolder
BitmapImage / SoftwareBitmap
DispatcherQueue / AppWindow
```

这些依赖只存在于 `PhotoCuller.App.WinUI`：

- `Windows.Storage.Pickers` 与 `WinRT.Interop`：文件和目录选择。
- `BitmapImage`：缩略图与预览呈现。
- `DispatcherQueue`：UI 线程派发。
- `Windows.System.Launcher` / Explorer：shell 集成。
- Microsoft.UI XAML 类型：窗口、页面、控件、转换器、主题和通知。

因此，Windows UI 类型没有泄漏进 Core；真正的边界问题是业务用例编排仍位于 `MainViewModel.StartScanAsync`。

### 当前流水线位置

```text
MainViewModel.StartScanAsync
    -> IPhotoScanner.ScanAsync
    -> IThumbnailService.GetOrCreateAsync
    -> IHashService.ComputeSha256Async
    -> IPerceptualHashService.ComputePHashAsync
    -> IPhotoScoringService.ScoreRangeAsync
    -> IDuplicateGroupingService.BuildGroupsAsync
    -> repositories（持久化）
    -> ViewModel collections（展示）
```

这会造成：

- CLI、Skill 或 MCP 若要分析照片，只能复制 `MainViewModel` 的调用顺序。
- 流水线进度、取消、空目录和错误处理只能通过 WinUI 间接测试。
- UI 同时承担应用用例、持久化顺序和呈现状态，难以保持多个客户端行为一致。
- 没有稳定、版本化、可序列化的分析结果协议。
- 本次重构时，WinUI 在调用 scanner 前清空照片/分组表，增量过滤因此实际上总会把所有文件视为新文件。该历史限制已在后续的[增量分析与安全提交](incremental-analysis.md)中解决。

### 第三方与 native 依赖

- `OpenCvSharp4` 是跨平台 managed API，但 `OpenCvSharp4.runtime.win` 是 Windows native runtime。二者目前同时由 Infrastructure 引用，导致任何 Infrastructure 消费方都隐式获得 Windows runtime。
- ImageSharp 与 MetadataExtractor 是 managed、平台无关实现。
- EF Core SQLite 的 managed API 跨平台；最终 native SQLite 资产由 NuGet runtime assets 决定。
- `Microsoft.ML.OnnxRuntime` 提供多平台 runtime assets，但实际模型文件与执行 provider 仍需逐平台验证。
- 名为 `DpapiApiKeyProtector` 的现有实现实际使用 BCL 的 AES-256-GCM 和用户 LocalApplicationData 中的密钥文件，并未调用 DPAPI 或 Windows cryptography API；类名和 Core 接口注释存在历史命名偏差，但实现本身不构成 Windows-only 依赖。
- 还原阶段报告了既有依赖风险：ImageSharp 3.1.10 对应 `GHSA-rxmq-m78w-7wmc`（中），传递依赖 SQLitePCLRaw.lib.e_sqlite3 2.1.11 对应 `GHSA-2m69-gcr7-jv3q`（高）。本轮不混入依赖升级；升级与图像/数据库回归应单独处理。

## 最小实施方案

新增两个项目，不拆分现有算法类：

```text
PhotoCuller.Core (net10.0, BCL only)
        ^
PhotoCuller.Application (net10.0)
    - PhotoAnalysisEngine
    - PhotoAnalysisOptions
    - versioned analysis result DTO
        ^
PhotoCuller.Infrastructure (net10.0)
    - existing scanner/imaging/OpenCV/metadata/storage implementations

PhotoCuller.App.WinUI (net10.0-windows)
    -> Application + Infrastructure
    - picker, navigation, dialogs, UI state, persistence, ViewModel mapping
    - Windows OpenCV runtime

PhotoCuller.Cli (net10.0)
    -> Application + Infrastructure
    - argument parsing, composition root, JSON output
    - current Windows OpenCV runtime
```

### Engine 边界

`PhotoAnalysisEngine` 原样编排现有算法步骤：扫描 → 可选缩略图 → SHA-256/pHash → 评分 → 分组。它不引用 UI 类型、不弹窗、不操作 ViewModel，也不决定数据库展示状态。

Engine 返回一次 `PhotoAnalysisRun`：

- 内含 WinUI 持久化及映射所需的现有 domain models；
- 内含由同一次运行生成的 `PhotoAnalysisReport`；
- `PhotoAnalysisReport` 使用显式 schema version、camelCase JSON、字符串 enum、照片质量维度、分组成员与推荐照片 ID，作为 CLI/Agent/Skill 的稳定边界。

本轮保留现有算法类与阈值行为，避免架构迁移和算法调整同时发生。

### Native runtime 放置

从 `PhotoCuller.Infrastructure` 移除 `OpenCvSharp4.runtime.win`，由当前实际运行 OpenCV 的组合根显式引用：

- `PhotoCuller.App.WinUI`
- `PhotoCuller.Cli`
- `PhotoCuller.Tests`

未来 Linux/macOS 客户端可选择相应 runtime package，而无需修改 Core、Application 或 OpenCV managed 实现。

## 测试与验证计划

- 保留并运行现有评分、排序/推荐、similarity threshold 和 grouping 测试，验证算法没有无意变化。
- 为 `PhotoAnalysisEngine` 添加流水线顺序与统一结果测试。
- 覆盖空目录结果、损坏图片继续出现在带错误信息的结果中、不支持扩展名被 scanner 过滤，以及取消令牌传播。
- 验证 `PhotoCuller.Core.csproj` 可单独 `dotnet build`。
- 验证完整 solution `dotnet build`、`dotnet test`。
- 创建临时 fixture 目录调用 CLI，验证 stdout JSON 与 `--output` 文件。
- 尝试启动 WinUI 可执行文件并确认进程进入消息循环；目录选择和人工 UI 操作不在无人工输入的自动验证范围内，需在最终报告中明确区分。

## 本轮明确不做

- 不迁移 Avalonia、MAUI 或其他 GUI。
- 不引入 MediatR、Event Bus、Repository Pattern 扩张或按类拆项目。
- 不重写相似度、质量评分或推荐算法。
- 不新增 GPT、Gemini、Claude、本地 VLM 或 Agent framework 集成。
- 不承诺当前 OpenCV Infrastructure 组合已经能在 macOS/Linux 运行；本轮只移除阻碍替换 runtime 的依赖方向。
