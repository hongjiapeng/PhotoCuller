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

所有评分均基于 **传统 CV 算法（OpenCvSharp）**，无需下载 AI 模型，也不依赖 GPU。

| 维度 | 默认权重 | 算法 | 说明 |
|------|----------|------|------|
| **清晰度** | 35% | Laplacian 方差 | 连拍组内最关键的区分指标 |
| **曝光** | 20% | HSV V 通道直方图，截黑 >15% 和过曝 >15% 均扣分 | 理想均值亮度：90–200 |
| **对比度** | 15% | 灰度标准差 | 理想标准差：35–90 |
| **噪点** | 10% | 占位返回 70 分，接口已预留给小波评估器 | 未来版本替换 |
| **分辨率** | 10% | 像素总量对数映射 | <0.5 MP → 低分；≥8 MP → 高分 |
| **构图** | 10% | 占位返回 65 分，接口已预留给 SAMP-Net | 未来版本替换 |
| **色彩** | 0% | HSV S 通道饱和度均值 | 默认关闭，可在设置中调高权重 |

默认权重合计 **100**，所有权重均可在「设置 → 评分权重」中调整。

### 🗂️ 安全文件处理
- 照片移入"**待删除目录**"——绝不自动永久删除原图
- 在待删除目录中保留相对目录结构
- 防碰撞重命名（`photo.jpg` → `photo(1).jpg`）
- 操作日志 + **Ctrl+Z 撤销**（当前运行期有效）

### 🤖 AI 模型管理

核心评分流程是**纯算法**实现，AI 是可选增强层，默认关闭：

| 层次 | 技术 | 何时需要 |
|------|------|---------|
| 核心评分 | OpenCvSharp（Laplacian、直方图、HSV） | 始终可用，无需 GPU 和模型 |
| 照片描述 & 推荐理由 | 任意 OpenAI 兼容 API 或本地 Ollama | 可选，默认关闭 |
| 语义搜索向量 | CLIP / SigLIP（ONNX 或 API） | 可选，路线图中 |
| 人脸质量评分 | InsightFace（ONNX） | 可选，路线图中 |

#### 本地 ONNX 美学评分

PhotoCuller 可以把本地美学模型作为传统技术评分的增强信号：

1. 在「AI 模型管理」中添加本地模型，类型选择「美学评分」并选择 `.onnx` 文件。
2. 根据模型输出选择 `0–1`、`-1–1` 或 `1–10 AVA` 预设。
3. 在「设置 → 本地模型评分」中启用，并设置美学评分占比（建议 20–30%）。

首版模型契约为单个 `float32` RGB 图像输入，布局支持 NCHW/NHWC，采用
CLIP 均值和标准差归一化，并输出单个标量评分。模型不可用或推理失败时会自动
回退到原有技术评分，不影响扫描。

「AI 模型管理」页面提供了推荐模型入口，方便用户打开下载页面。常见的美学评分
模型（如 LAION improved aesthetic predictor、Aesthetics Predictor V2）通常发布为
`.pth` / `.safetensors` 权重，需要先导出为符合上述契约的 `.onnx` 文件后才能用于
本地评分。仓库提供了导出脚本，见 `tools/model-export/README.md`。

当前推荐的本地美学模型路线是 **CLIP ViT-L/14 + LAION / improved-aesthetic-predictor
MLP 评分头**。这类模型在开源图像数据清洗和生成式 AI 数据集筛选中很常见：
LAION 曾用 aesthetic predictor 为大规模图文数据集预测 1–10 的大众审美分，并构建
LAION-Aesthetics 子集；`improved-aesthetic-predictor` 则是社区常用的 CLIP embedding
美学评分基线。

它适合用来做“批量排序”和“辅助选片”，但不应当被当作最终审美裁判：

- 适合：风景、人像、旅行照、生成图等图片的整体观感排序。
- 适合：和清晰度、曝光、对比度等传统评分混合，作为额外的“画面好看程度”信号。
- 不适合：判断照片的纪念意义、个人偏好、家庭成员表情、故事性或专业摄影奖项价值。
- 可能偏好：干净主体、高饱和、商业感、网络图片风格，未必等同于你的个人审美。

因此建议把模型美学权重设为 **20–30%**，让传统技术评分负责基础质量，让模型只补充
“看起来顺眼不顺眼”的部分。默认建议值是 25%。

**支持的 AI 后端**（在 AI 模型管理页面配置）：

| 后端 | 接入方式 |
|------|---------|
| **Ollama**（本地，免费） | BaseURL 填 `http://localhost:11434/v1`，选择任意视觉模型（如 `qwen2.5vl:7b`） |
| **OpenAI / GPT-4o** | BaseURL 填 `https://api.openai.com/v1`，输入 API Key |
| **DeepSeek** | BaseURL 填 `https://api.deepseek.com/v1`，输入 API Key |
| **Google Gemini** | 选择 Gemini 提供商，输入 API Key |
| **自定义 HTTP 端点** | 任何 OpenAI 兼容接口 |

> API Key 在写入磁盘前使用 **AES-256-GCM 加密**，绝不明文存储。云端 AI 分析**默认关闭**，在「设置 → 隐私」中开启。

### ⚙️ 设置
- 深色 / 浅色 / 跟随系统主题
- 扫描、评分、相似度阈值全部可配置
- 下次启动自动恢复上次目录
- 缩略图缓存管理

### 🧭 相似照片设置建议

在「设置 → 相似照片设置」中选择查找力度，点击 **保存设置**，再重新扫描。已生成的分组不会自动回溯更新。

| 查找力度 | 适合场景 |
|----------|----------|
| 精准 | 只找画面非常接近的照片，优先减少误分 |
| 平衡（推荐） | 兼顾准确度，并识别时间接近、文件名连续的同场景照片 |
| 尽量多找 | 召回更多构图变化较大的连续拍摄，适合逐组人工确认 |

应用同时使用 pHash 和 dHash，两种视觉特征都接近时才会合并照片。只有同目录、拍摄时间接近且文件编号相邻时，才会适度放宽判断；组首和组尾还会再次校验，避免主体逐渐变化后串成大组。只有约 2 秒内完成、画面高度一致的连续帧才标为“连拍”，其余同场景照片标为“相似”。

高级设置中仍可覆盖跨时段阈值、连续拍摄阈值和时间窗口。通常不建议修改；感知哈希结果会缓存，但修改后仍需重新扫描以重建分组。

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
# 预期结果：50 个测试，50 个通过
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
- [x] 导出报告（CSV）
- [ ] RAW 格式支持（CR2、NEF、ARW…）

---

## 许可证

MIT License — 详见 [LICENSE](LICENSE)。
