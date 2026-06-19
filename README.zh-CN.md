# PhotoCuller

**智能识别重复与相似照片，帮你保留每一张值得珍藏的记忆。**

PhotoCuller 是一款本地照片优选工具，专为 Windows 设计。它扫描本地照片目录，自动检测重复照片和相似照片，从多个质量维度给每张照片评分，并在每组相似照片中推荐最佳保留项——让你安全地清理其余照片。

所有功能均 **100% 本地运行**，无需上传、无需订阅、无需 API Key。

---

## 功能特性

### 🔍 智能扫描
- 递归扫描本地目录，支持 JPEG、PNG、HEIC、WEBP、BMP、TIFF
- 读取 EXIF 元数据（拍摄时间、尺寸）
- 增量扫描——未变化的文件在重新扫描时自动跳过
- 生成并缓存缩略图，加快浏览速度

### 🔗 重复与相似检测

| 方法 | 用途 |
|------|------|
| SHA-256 | 完全重复检测 |
| pHash（DCT） | 视觉相似照片 |
| dHash | 快速预筛选 |
| 时间窗口 + pHash | 连拍照片分组 |

### ⭐ 质量评分（0–100）

| 维度 | 算法 |
|------|------|
| 清晰度 | Laplacian 方差 |
| 曝光 | 亮度直方图分析 |
| 对比度 | 亮度标准差 |
| 噪点 | 占位实现（接口已预留） |
| 分辨率 | 像素数量梯度 |
| 构图 | 占位实现（预留给 SAMP-Net） |
| 色彩 | HSV 饱和度分布 |

各维度权重可配置，总和须等于 100。

### 🗂️ 安全文件处理
- 照片移入"**待删除目录**"——绝不自动永久删除原图
- 在待删除目录中保留相对目录结构
- 防碰撞重命名（`photo.jpg` → `photo(1).jpg`）
- 操作日志 + **Ctrl+Z 撤销**（当前运行期有效）

### 🤖 AI 模型管理
- 支持配置本地 ONNX 模型或云端 API（OpenAI 兼容、Gemini、自定义 HTTP）
- API Key 使用 **AES-256-GCM 加密**后存储，绝不明文保存
- 保存前可测试连接

### ⚙️ 设置
- 深色 / 浅色 / 跟随系统主题
- 扫描、评分、相似度阈值全部可配置
- 下次启动自动恢复上次目录
- 缩略图缓存管理

---

## 环境要求

| 要求 | 版本 |
|------|------|
| Windows | 10 1809（内部版本 17763）或更高 |
| .NET SDK | 10.0.203 或更高 |
| Visual Studio | 2026（v18），需安装 WinUI 工作负载 |
| Windows App SDK | 1.7（自包含，随应用发布） |
| 架构 | x64 |

---

## 快速开始

### 从源码构建

```powershell
git clone https://github.com/hongjiapeng/PhotoCuller.git
cd PhotoCuller

# 需要 VS MSBuild（WinUI XAML 编译器依赖它）
$msbuild = & "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe" `
    -latest -requires Microsoft.Component.MSBuild `
    -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1

& $msbuild PhotoCuller.slnx /p:Configuration=Debug /p:Platform="Any CPU" /m
```

### 运行

```powershell
# 构建完成后直接运行：
.\src\PhotoCuller.App.WinUI\bin\Any CPU\Debug\net10.0-windows10.0.19041.0\win-x64\PhotoCuller.exe
```

或者用 Visual Studio 2026 打开 `PhotoCuller.slnx`，将 `PhotoCuller.App.WinUI` 设为启动项目，按 **F5** 运行。

### 运行测试

```powershell
dotnet test src\PhotoCuller.Tests\PhotoCuller.Tests.csproj --configuration Debug
# 预期结果：47 个测试，47 个通过
```

---

## 项目结构

```
PhotoCuller/
├── src/
│   ├── PhotoCuller.Core/               # 领域模型、接口、评分权重
│   │   ├── Models/                     # PhotoItem, PhotoGroup, AppSettings …
│   │   ├── Enums/                      # RecommendationStatus, GroupType …
│   │   ├── Interfaces/                 # 所有服务契约（平台无关）
│   │   └── Scoring/                    # ScoringWeights 值对象
│   │
│   ├── PhotoCuller.Infrastructure/     # 具体实现
│   │   ├── Database/                   # EF Core DbContext、迁移、初始化器
│   │   ├── Repositories/               # EF Core + 原生 SQL 仓储
│   │   ├── Scanning/                   # 扫描器、元数据读取器、图片解码器
│   │   ├── Hashing/                    # SHA-256、pHash、dHash、Union-Find 分组
│   │   ├── ImageProcessing/            # 质量评分器（OpenCvSharp）
│   │   ├── ThumbnailCache/             # 磁盘分桶缩略图服务
│   │   ├── FileSystem/                 # FileMoveService、UndoService
│   │   └── Configuration/              # DI 扩展、AES-256 Key 保护器
│   │
│   ├── PhotoCuller.App.WinUI/          # WinUI 3 应用（Unpackaged）
│   │   ├── Views/                      # 主窗口、页面、自定义控件
│   │   ├── ViewModels/                 # MVVM ViewModel（CommunityToolkit.Mvvm）
│   │   ├── Converters/                 # XAML 值转换器
│   │   └── Services/                   # 平台服务实现
│   │
│   └── PhotoCuller.Tests/              # xUnit + FluentAssertions + NSubstitute
│       ├── CoreTests/                  # 评分权重、pHash、相似度、分组
│       └── InfrastructureTests/        # 文件移动、设置持久化
│
├── Directory.Build.props               # 共享 TFM、nullable、分析器
├── Directory.Packages.props            # 中央包版本管理（CPM）
└── global.json                         # SDK 版本锁定（10.0.203）
```

---

## 架构原则

- **`PhotoCuller.Core` 零 Windows/UI 依赖** — 所有平台能力通过接口注入
- **EF Core 默认 NoTracking** — 读多写少场景；写操作显式调用 `Update()`
- **SQLite WAL 模式** — 后台扫描写入不阻塞 UI 读取
- **流式扫描管道** — `IAsyncEnumerable<PhotoItem>` 让 UI 边扫描边显示结果
- **Channel 进度推送** — `IScanProgressService` 用 `System.Threading.Channels` 保证线程安全
- **Union-Find 分组** — 路径压缩 + 按秩合并，近线性时间复杂度

---

## 数据存储位置

| 数据 | 位置 |
|------|------|
| SQLite 数据库 | `%LocalAppData%\PhotoCuller\photoculler.db` |
| 设置文件 | `%LocalAppData%\PhotoCuller\settings.json` |
| 缩略图缓存 | `%LocalAppData%\PhotoCuller\thumbnails\{xx}\{xx}\{hash}.jpg` |
| 日志 | `%LocalAppData%\PhotoCuller\logs\log-YYYYMMDD.txt` |
| API Key 密钥库 | `%LocalAppData%\PhotoCuller\.keystore`（隐藏文件，AES-256 密钥） |

---

## 隐私说明

- 默认情况下**所有数据留在本机**，不上传任何内容
- 云端 AI 分析需在「设置 → 隐私」中显式开启
- API Key 加密（AES-256-GCM）后才写入磁盘
- 缩略图和原图不会离开本机，除非你主动配置云端模型

---

## 快捷键

| 快捷键 | 操作 |
|--------|------|
| `Ctrl+Z` | 撤销上一次文件移动 |

---

## 待实现功能（Roadmap）

- [ ] 人脸检测与聚类（InsightFace ONNX）
- [ ] CLIP 语义搜索
- [ ] 真实噪声评分（小波分解）
- [ ] 构图评分（SAMP-Net）
- [ ] 通过配置模型生成中文照片描述
- [ ] 导出报告（CSV/JSON）
- [ ] RAW 格式支持（CR2、NEF、ARW…）

---

## 许可证

MIT License — 详见 [LICENSE](LICENSE)。
