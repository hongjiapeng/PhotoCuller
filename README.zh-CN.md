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
| 拍摄上下文 + ORB / RANSAC | 连拍照片复核 |
| BK-Tree + Union-Find | 相似候选搜索与分组 |

#### 完全重复照片如何识别

程序以 64 KB 数据块读取文件，并为每张照片计算 **SHA-256**。只要两张照片的
SHA-256 完全相同，就说明其文件内容逐字节一致；即使文件名或存放目录不同，也会
进入同一个“完全重复”组。已经进入完全重复组的照片不会再参与后续视觉分组。

#### 相似照片如何识别

对连拍、压缩、缩放或轻微调整过的照片，文件哈希通常完全不同，因此程序会额外
生成两种 64 位感知哈希：

- **pHash**：将图片转为 32×32 灰度图，执行 DCT，并从左上角 8×8 低频区域生成
  哈希，主要描述画面的整体结构。
- **dHash**：将图片缩放到 9×8，逐行比较相邻像素的明暗关系，生成描述画面变化
  趋势的哈希。

两个哈希之间使用汉明距离比较，即统计二进制位中有多少位不同；距离越小，画面越
接近。跨目录或跨时段的照片必须同时满足较严格的 pHash 和 dHash 阈值，才会建立
相似关系。

```text
原图
 ├─ pHash：比较整体构图
 └─ dHash：比较明暗变化
             ↓
       两者同时满足阈值
             ↓
         相似照片候选
```

#### 连拍照片如何复核

连拍过程中人物动作或相机位置可能发生轻微变化，使用严格阈值容易漏检。程序只在
照片处于同一目录、拍摄时间接近，并且文件名序号连续时放宽感知哈希阈值；如果文件
名带有连续编号，还要求前缀相同且编号相差不超过 1。

通过上述条件的候选照片还要接受 **ORB 局部特征 + RANSAC 几何校验**。ORB 从
两张图片中提取角点和纹理特征，RANSAC 检查匹配点能否形成一致的空间变换；只有
匹配数量、内点比例和画面覆盖范围都满足要求，才认为它们属于同一拍摄场景。这一
复核可以减少“颜色、构图接近，但实际内容不同”的误判。

设置中的三档查找力度会使用不同阈值：精准模式优先减少误分，平衡模式兼顾准确率
和召回率，尽量多找模式会覆盖更长的拍摄时间窗口和更大的画面变化。

#### 相似关系如何形成照片组

程序使用 **BK-Tree** 按汉明距离快速查询相似哈希，避免对整个照片库进行低效的
两两比较。随后把每张照片视为一个节点，把通过相似判断的照片对连接起来，再通过
带路径压缩和按秩合并的 **Union-Find（并查集）** 聚合为候选组：

```text
照片 A ──相似── 照片 B ──相似── 照片 C
                         ↓
                    同一候选组
```

候选组生成后还会检查组首、组尾及相邻照片的一致性，并拆分不连贯的集合，避免出现
“A 像 B、B 像 C，但 A 和 C 已不是同一场景”的链式误分。

最终，如果组内照片均为约 2 秒内拍摄、时间连续，并且相邻照片都满足严格的 pHash
与 dHash 条件，就标记为“连拍”组；否则标记为“相似”组。每个组优先采用用户手动
选择的保留项；没有手动选择时，则按综合质量评分排序，并以分辨率作为同分情况下的
次级判断，推荐一张照片保留。其余照片只会标为“待确认”，不会被自动删除。

### ⭐ 质量评分（0–100）

基础技术评分使用 **通过 OpenCvSharp 实现的传统计算机视觉算法**，无需训练模型、
下载模型或使用 GPU。OpenCvSharp 是 OpenCV 的 .NET 封装，负责提供底层图像处理
能力，本身并不是 AI 模型。

| 维度 | 默认权重 | 当前计算依据 | 状态 |
|------|---------:|--------------|------|
| **清晰度** | 35% | 读取灰度图，执行 OpenCV Laplacian，计算响应方差 `v`，再按 `100 × v / (v + 500)` 映射 | 已实现 |
| **曝光** | 20% | 统计 HSV V 通道的 256 档直方图；平均亮度在 85～200 时基础分为 100，偏低或偏高均扣分；0～15 的死黑像素或 240～255 的过曝像素超过 15% 时追加惩罚 | 已实现 |
| **对比度** | 15% | 统计灰度标准差 `s`：低于 20 映射到 0～60，20～75 映射到 60～100，75～90 得 100，高于 90 逐步扣分 | 已实现 |
| **噪点** | 10% | 固定返回中性分 70 | **占位实现**，尚未检测真实噪点 |
| **分辨率** | 10% | 按 `宽 × 高` 分段映射：<0.5 MP → 0～30，0.5～2 MP → 30～70，2～8 MP → 70～95，8～24 MP → 95～100，≥24 MP → 100 | 已实现，不使用 OpenCV |
| **构图** | 10% | 固定返回 65 | **占位实现**，尚未检测真实构图 |
| **色彩** | 0% | 统计 HSV S 通道平均饱和度；低饱和度得分较低，140～180 得 100，高于 180 作为可能过饱和逐步扣分 | 已实现，默认权重关闭 |

技术分是所有可用维度的加权平均值。如果某个分析器失败并返回 `null`，程序会按其余
有效权重重新归一化。默认技术权重合计 **100**，可在「设置 → 评分权重」中调整。

启用本地美学评分后，最终总分为：

```text
最终总分 = 技术分 ×（1 - 美学占比）+ 模型美学分 × 美学占比
```

例如建议的 25% 美学占比表示技术分保留 75%、模型分占 25%。模型美学不参与重复或
相似照片识别，也不参与组的构建；但组内默认推荐总体评分最高的照片，所以它会影响
最终排序和建议保留结果。

OpenCV/OpenCvSharp 具体负责图片解码、缩放和颜色转换、DCT pHash、dHash 预处理、
Laplacian 清晰度、HSV 统计、ORB 特征、描述符匹配、RANSAC 单应性校验以及美学模型
的图片预处理。SHA-256 使用 .NET 加密库；分辨率直接读取宽高；BK-Tree 和 Union-Find
由本项目实现；模型推理由 ONNX Runtime 执行。

这些分数是实用型无参考启发式指标，并不是摄影质量的通用真值。当前阈值和 0～100
映射曲线属于 PhotoCuller 的工程设计选择，仍需通过更多真实照片数据持续校准。

### 🗂️ 安全文件处理
- 照片移入"**待删除目录**"——绝不自动永久删除原图
- 在待删除目录中保留相对目录结构
- 防碰撞重命名（`photo.jpg` → `photo(1).jpg`）
- 操作日志 + **Ctrl+Z 撤销**（当前运行期有效）

### 🤖 AI 模型管理

核心评分流程使用**传统计算机视觉算法**实现，AI 是可选增强层，默认关闭：

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

## 技术来源与致谢

PhotoCuller 没有把工作流委托给其他照片筛选引擎。阈值、0～100 分数映射、多阶段匹配
策略、连贯分组拆分以及评分混合均为本项目的具体实现；但项目建立在以下成熟算法、
第三方库、论文和开源模型成果之上：

- **OpenCV / OpenCvSharp**：用于图像解码与变换、直方图、Laplacian、DCT、ORB
  描述符、特征匹配和 RANSAC 单应性计算。参考
  [OpenCV 官方文档](https://docs.opencv.org/4.x/)和
  [OpenCvSharp 项目](https://github.com/shimat/opencvsharp)。
- **感知哈希**：DCT pHash 的设计参考 Christoph Zauner 的
  [《Implementation and Benchmarking of Perceptual Image Hash Functions》](https://www.phash.org/docs/pubs/thesis_zauner.pdf)；
  相邻像素差分哈希参考 Neal Krawetz 的
  [dHash 说明](https://www.hackerfactor.com/blog/index.php?/archives/529-Kind-of-Like-That.html)。
- **ORB 与 RANSAC**：ORB 来自 Rublee 等人的论文
  [《ORB: An efficient alternative to SIFT or SURF》](https://doi.org/10.1109/ICCV.2011.6126544)；
  几何一致性复核基于 Fischler 与 Bolles 提出的
  [RANSAC 方法](https://doi.org/10.1145/358669.358692)。
- **BK-Tree 与 Union-Find**：度量距离搜索基于 Burkhard 与 Keller 的
  [《Some Approaches to Best-Match File Searching》](https://scholarship.claremont.edu/hmc_fac_pub/170/)；
  分组使用带按秩合并和路径压缩的并查集，参考 Tarjan 的
  [《Efficiency of a Good But Not Linear Set Union Algorithm》](https://doi.org/10.1145/321879.321884)。
- **本地美学模型**：推荐导出方案组合
  [OpenCLIP](https://github.com/mlfoundations/open_clip) ViT-L/14 与兼容 Christoph
  Schuhmann [improved-aesthetic-predictor](https://github.com/christophschuhmann/improved-aesthetic-predictor)
  思路的 MLP 评分头；早期的
  [LAION aesthetic predictor](https://github.com/LAION-AI/aesthetic-predictor)
  也是重要上游参考。推理使用
  [ONNX Runtime](https://onnxruntime.ai/docs/)。

运行时和测试依赖统一声明在
[`Directory.Packages.props`](Directory.Packages.props) 中。上游库、模型权重和数据集
保留各自的许可证和使用条款；重新分发导出的模型或第三方权重前，请单独确认其授权。

---

## 许可证

MIT License — 详见 [LICENSE](LICENSE)。
