# CycleGUI

一个轻量、高性能、跨平台的即时模式 GUI 库。

## 快速开始

### 运行 LearnCycleGUI

**LearnCycleGUI 是最好的文档** - 它是一个可运行的交互式示例，展示了 CycleGUI 的所有功能。

```bash
cd Examples/LearnCycleGUI
dotnet run
```

打开程序后，点击左侧的章节按钮：

1. **Chapter 1 - Panel Controls** - 所有 UI 控件的实时演示
2. **Chapter 2 - Panel Layout** - 面板布局和管理
3. **Chapter 3 - Plot** - 图表和数据可视化
4. **Chapter 4 - Workspace** - 3D 渲染和交互
5. **Chapter 5 - Utilities** - 高级功能（手势控制、多线程 UI、SLAM 地图）

### Web 版本

LearnCycleGUI 也可以在浏览器中运行。程序启动后会自动开启 Web 服务器，在浏览器中访问显示的地址即可。

## 特性

- ✅ **即时模式 GUI** - 简洁直观的 API
- ✅ **跨平台** - Windows、Linux、Web (WebAssembly)
- ✅ **3D 渲染** - 点云、模型、自定义网格、**Gaussian Splatting** ✨
- ✅ **丰富交互** - 对象选择、3D 变换、手势控制
- ✅ **实时更新** - 适合监控和控制系统
- ✨ **NEW: 3D/4D Gaussian Splatting** - 神经渲染支持

## 示例代码

### 创建 UI

```csharp
var panel = GUI.DeclarePanel()
    .ShowTitle("我的面板")
    .InitPos(false, 100, 100);

int counter = 0;
panel.Define(pb =>
{
    pb.Label("欢迎使用 CycleGUI！");
    if (pb.Button("点击我"))
        counter++;
    pb.Label($"点击次数: {counter}");
});
```

### 显示 3D 点云

```csharp
Workspace.AddProp(new PutPointCloud()
{
    name = "cloud",
    xyzSzs = points,  // Vector4[] { x, y, z, size }
    colors = colors   // uint[] ABGR 格式
});
```

### 加载 3D 模型

```csharp
Workspace.Prop(new LoadModel()
{
    name = "model",
    detail = new Workspace.ModelDetail(File.ReadAllBytes("model.glb"))
});

Workspace.AddProp(new PutModelObject()
{
    clsName = "model",
    name = "instance",
    newPosition = Vector3.Zero
});
```

### 显示 3D Gaussian Splats ✨

```csharp
// 从点云自动生成
var splats = PutGaussianSplats.FromPointCloud(
    positions, colors, defaultSize: 0.05f, "gaussian_scene"
);
Workspace.Prop(splats);

// 或从PLY文件加载
var splats = PutGaussianSplats.FromPLY("scene.ply", "loaded_scene");
Workspace.Prop(splats);
```

详见 [Gaussian Splatting 文档](GAUSSIAN_SPLATTING.md)

## 编译

### 前置要求

- .NET 8.0 SDK
- Windows: Visual Studio 2019+, vcpkg
- Linux: GCC/Clang, Make

### 编译步骤

```bash
# Windows
.\compile.bat Release

# Linux
make
dotnet build -c Release
```

## 应用场景

- 工业软件（设备控制、监控系统）
- 数字孪生（传感器数据可视化）
- 机器人（控制面板、SLAM 可视化）
- 科研工具（数据标注、算法可视化）

## 链接

- [Gitee 仓库](https://gitee.com/Fairyland_1/CycleGUI)
- [发布页面](https://gitee.com/Fairyland_1/CycleGUI/releases)

---

**最好的文档就是 LearnCycleGUI 本身 - 运行它，探索它，修改它！** 🚀
