# LearnCycleGUI

| 文档版本 | 修订内容 | 作者   | 日期      |
| -------- | ---- | ------ | --------- |
| 0.1      | 创建文档，初步编写内容。 | 周睿锋 | 2024/10/31 |
| 0.2      | 添加“编译和运行步骤”。 | 周睿锋 | 2024/12/02 |

## 什么是CycleGUI？

**CycleGUI**是一个无状态的即时模式图形化用户界面库，且具备以下特性：

+ **轻量化开发**：代码中的图形界面组件声明所见即所得，能够极快地将“显示需求变更”实现到界面开发；
+ **一次开发跨平台通用**：支持Windows部署、Linux部署、客户端访问、网页访问；
+ **高性能渲染**：支持大规模图表数据、点云数据、3D模型的显示和交互。

基于上述特性，用户可使用**CycleGUI**轻松构建以下应用：

+ **工业软件**：产品调试界面，可视化日志软件，运维软件；
+ **数字孪生**：实时数据投射，多种类、高密度图元渲染；
+ **科研工具**：数据标注工具，算法可视化界面。

## 编译和运行步骤

+ 下载本仓库。

```
git clone https://gitee.com/Fairyland_1/LearnCycleGUI.git
```

+ 使用NuGet还原程序所需安装的引用包。

+ 使用[下载器](https://gitee.com/ruifeng-zhou/Distributor/releases/download/1.0.0/Distributor.zip)，勾选Dependencies，下载编译所需的CycleGUI.dll。

+ 编译LearnCycleGUI并运行。

+ 遇到闪退情况，请安装[dotnet 8.0 运行时](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)。

+ 遇到“字体找不到（font not found）”一类的错误，请[下载字体](https://gitee.com/ruifeng-zhou/Distributor/releases/download/1.0.0/CascadiaMono.ttf)，右键管理员权限安装（为所有用户安装）。

## 教程目录

**LearnCycleGUI 是最好的文档** - 这是一个可运行的交互式示例，展示了 CycleGUI 的所有功能。

运行后点击左侧的章节按钮，每个章节都包含实时可交互的演示。

本教程覆盖了 **~85%** 的 CycleGUI API（详见 `API_COVERAGE_CHECK.md`）。

### 第一章：Panel Controls (面板控件) - 47个控件

在这一章中，您将学习 CycleGUI 提供的所有基础 UI 控件：

#### 文本和布局 (6个)
+ Label, SeparatorText, Separator, CollapsingHeader, SameLine, ToolTip

#### 按钮和交互 (3个)
+ Button, ButtonGroups, PopMenuButton

#### 输入控件 (11个)
+ CheckBox, RadioButtons, Toggle, DragFloat, DragVector2, DragMatrix, SliderInt, SliderFloat, TextInput, ColorEdit, BezierEditor

#### 选择控件 (3个)
+ DropdownBox, ListBox, TabButtons

#### 高级控件 (6个)
+ ChatBox, Table, SelectableText, Progress, Plot2D, RealtimePlot

#### 文件操作 (4个)
+ DisplayFileLink, OpenFile, SaveFile, SelectFolder

#### 其他功能 (4个)
+ Alert, Icons (ForkAwesome), OpenWebview, SetImGUIStyle

### 第二章：Panel Layout (面板布局)

学习如何创建、定位、停靠和管理多个面板：

+ **InitPos** - 设置面板的绝对初始位置
+ **InitPosRelative** - 设置面板相对于其他面板的位置
+ **SetDefaultDocking** - 设置面板的默认停靠位置
+ **面板生命周期管理** - 创建、显示、关闭面板
+ **BringToFront** - 将面板置于最前

### 第三章：Plot (图表绘制)

展示各种数据可视化功能：

+ **RealtimePlot** - 实时数据曲线图
+ **MiniPlot** - 迷你状态指示器
+ **Image** - 显示图像
+ **ImageList** - 图像列表/相册控件
+ **PutRGBA** - 上传 RGBA 图像数据到 Workspace

### 第四章：Workspace (3D 工作空间) - 50+个功能

这是 CycleGUI 最强大的功能，提供 3D 对象显示和交互：

#### 基础渲染 (9个)
+ Painter (DrawVector, DrawDot, DrawRegion3D)
+ PutPointCloud, PutImage, PutRGBA, DeclareSVG
+ DefineMesh, PutModelObject, LoadModel

#### 几何和标注 (6个)
+ PutStraightLine, PutBezierCurve, PutVector
+ PutHandleIcon, PutTextAlongLine, SpotText

#### 相机和视口 (6个)
+ SetCamera, SetFullScreen, QueryViewportState
+ CaptureRenderedViewport, 多视口, FrameToFit

#### 外观和渲染 (6个)
+ SetAppearance, SetOperatingGridAppearance, SetObjectApperance
+ SetCustomBackgroundShader, SetCustomBackgroundEnvmap, SetImGUIStyle

#### 3D 交互 (7个)
+ GetPosition, SelectObject, GuizmoAction, FollowMouse
+ TransformObject, TransformSubObject, SetObjectMoonTo

#### 动画和高级 (6个)
+ SetModelObjectProperty (动画控制)
+ SetWorkspacePropDisplayMode, RemoveNamePattern
+ QueryGraphics, QueryInputState, Painter.Clear

#### 新增：Gaussian Splatting (2个)
+ **PutGaussianSplats** - 3D 高斯喷溅渲染
+ **PutGaussianSplats4D** - 4D 动态高斯喷溅（带时序）

### 第五章：Utilities (实用工具)

+ **UseGesture** - 手势控制和虚拟遥控器
  - ThrottleWidget - 油门滑块
  - StickWidget - 虚拟摇杆
  - ButtonWidget - 虚拟按钮
  - ToggleWidget - 虚拟开关
+ **Delegater** - 面板 UI 代理（多线程 UI 更新）
+ **SoftwareBitmap** - 软件位图绘制（绘制线、矩形、圆、多边形、文字）
+ **SLAM 地图加载** - 加载和显示 2D LiDAR SLAM 地图

## 项目结构

```
LearnCycleGUI/
├── Program.cs                  # 主程序入口
├── Demo/
│   ├── DemoControlsHandler.cs     # 第一章：控件示例
│   ├── DemoPanelLayoutHandler.cs  # 第二章：面板布局示例
│   ├── DemoPlotHandler.cs         # 第三章：图表示例
│   ├── DemoWorkspaceHandler.cs    # 第四章：3D 工作空间示例
│   └── DemoUtilities.cs           # 第五章：实用工具示例
├── Utilities/
│   └── UsbCamera.cs               # USB 摄像头工具类
└── README.md                   # 本文档
```

## Web 版本运行

LearnCycleGUI 支持在浏览器中运行（WebAssembly 版本）。

### 启动方式

1. 确保已编译 webVRender（参考根目录的 `BUILDING.zh-CN.md`）
2. 在 `Program.cs` 中已配置 WebTerminal：

```csharp
Task.Run(() => {
    LeastServer.AddServingFiles("/debug", "D:\\src\\CycleGUI\\Emscripten\\WebDebug"); 
    WebTerminal.Use(ico: icoBytes);
});
```

3. 运行 LearnCycleGUI 本地版本
4. 在浏览器中访问 WebTerminal 提供的地址（默认通常是 `http://localhost:xxxx`）

### Web 版本特点

+ 完整的 3D 渲染支持
+ 所有 UI 控件在浏览器中可用
+ 可以通过 JavaScript 模拟鼠标和触摸事件
+ 适合远程访问和演示

## 快速开始示例

### 创建简单的 Panel

```csharp
using CycleGUI;

// 声明一个 Panel
var panel = GUI.DeclarePanel()
    .ShowTitle("我的第一个面板")
    .InitPos(pinned: false, left: 100, top: 100);

// 定义 Panel 的 UI
panel.Define(pb =>
{
    pb.Label("Hello, CycleGUI!");
    
    if (pb.Button("点击我"))
    {
        Console.WriteLine("按钮被点击了！");
    }
});
```

### 在 Workspace 中显示点云

```csharp
using CycleGUI.API;
using System.Numerics;

// 创建点云数据
var points = new Vector4[100];
var colors = new uint[100];

for (int i = 0; i < 100; i++)
{
    points[i] = new Vector4(i * 0.1f, (float)Math.Sin(i * 0.1), 0, 5); // x, y, z, size
    colors[i] = 0xff00ff00; // ABGR 格式，绿色
}

// 添加到 Workspace
Workspace.AddProp(new PutPointCloud()
{
    name = "my_pointcloud",
    xyzSzs = points,
    colors = colors
});
```

### 选择 3D 对象

```csharp
var selectAction = new SelectObject()
{
    feedback = (selections, _) =>
    {
        if (selections.Length > 0)
        {
            Console.WriteLine($"选中了对象: {selections[0].name}");
        }
    }
};

selectAction.Start();
selectAction.SetObjectSelectable("my_pointcloud");
```

## 学习建议

1. **从第一章开始** - 熟悉基础 UI 控件的使用
2. **逐章练习** - 每章都包含可交互的示例，边看边试
3. **查看源代码** - `Demo/` 文件夹中的代码都有详细注释
4. **尝试修改** - 修改示例代码中的参数，观察变化
5. **结合 API 文档** - 查看 `ImplementationHelpForAI.md` 了解更深层的实现细节

## 常见问题

### 运行时闪退

+ 确保已安装 .NET 8.0 运行时
+ 确保 libVRender.dll 存在且版本匹配

### 字体显示问题

+ 安装 [CascadiaMono 字体](https://gitee.com/ruifeng-zhou/Distributor/releases/download/1.0.0/CascadiaMono.ttf)（管理员权限，为所有用户安装）

### 3D 模型不显示

+ 检查模型文件路径是否正确
+ 确保模型是 GLTF/GLB 格式
+ 检查模型的缩放和位置参数

## 相关资源

+ [CycleGUI 编译指南](../../BUILDING.zh-CN.md)
+ [CycleGUI 主仓库](https://gitee.com/Fairyland_1/CycleGUI)
+ [API 实现指南](../../ImplementationHelpForAI.md)

## 贡献

欢迎提交示例代码、改进文档或报告问题！

---

**开始您的 CycleGUI 学习之旅吧！** 🚀
