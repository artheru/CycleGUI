# 3D/4D Gaussian Splatting 支持

CycleGUI 现已支持 3D 和 4D Gaussian Splatting 渲染！

## 什么是 Gaussian Splatting？

Gaussian Splatting 是一种新型的神经渲染技术，用于从多视角图像重建3D场景。与传统的网格或点云不同，它使用3D高斯分布来表示场景，可以实现：

- 📸 照片级真实感渲染
- ⚡ 实时渲染性能
- 🎯 高质量的视角插值
- 💾 相对紧凑的数据存储

## 快速开始

### 1. 从点云生成简单的高斯场景

```csharp
using CycleGUI.API;
using System.Numerics;

// 准备点云数据
var positions = new Vector3[100];
var colors = new Vector3[100];
var rnd = new Random();

for (int i = 0; i < 100; i++)
{
    positions[i] = new Vector3(
        (float)(rnd.NextDouble() * 10 - 5),
        (float)(rnd.NextDouble() * 10 - 5),
        (float)(rnd.NextDouble() * 10 - 5)
    );
    colors[i] = new Vector3(
        (float)rnd.NextDouble(),
        (float)rnd.NextDouble(),
        (float)rnd.NextDouble()
    );
}

// 自动生成高斯splats
var splats = PutGaussianSplats.FromPointCloud(
    positions, 
    colors, 
    defaultSize: 0.05f, 
    name: "my_gaussian_scene"
);

Workspace.Prop(splats);
```

### 2. 手动创建高斯splats

```csharp
var customSplats = new GaussianSplat[10];

for (int i = 0; i < 10; i++)
{
    customSplats[i] = new GaussianSplat
    {
        position = new Vector3(i, 0, 0),
        rotation = Quaternion.Identity,
        scale = new Vector3(0.1f, 0.05f, 0.05f), // 椭球形状
        opacity = 0.9f,
        color_dc = new Vector3(1, 0, 0) // 红色
    };
}

Workspace.Prop(new PutGaussianSplats
{
    name = "custom_splats",
    splats = customSplats,
    globalOpacityScale = 1.0f,
    globalSizeScale = 1.0f
});
```

### 3. 从PLY文件加载（标准3DGS格式）

```csharp
// 加载标准的3D Gaussian Splatting PLY文件
var splats = PutGaussianSplats.FromPLY("scene.ply", "loaded_scene");
Workspace.Prop(splats);
```

### 4. 创建4D动态场景

```csharp
// 创建带运动的高斯splats
var positions = new Vector3[100];
var colors = new Vector3[100];
var velocities = new Vector3[100];
var timestamps = new float[100];

for (int i = 0; i < 100; i++)
{
    positions[i] = Vector3.Zero; // 从中心开始
    velocities[i] = new Vector3(
        (float)(rnd.NextDouble() - 0.5),
        (float)(rnd.NextDouble() - 0.5),
        (float)(rnd.NextDouble() - 0.5)
    );
    timestamps[i] = (float)(rnd.NextDouble() * 5); // 5秒动画
    colors[i] = new Vector3(
        (float)rnd.NextDouble(),
        (float)rnd.NextDouble(),
        (float)rnd.NextDouble()
    );
}

var splats4d = PutGaussianSplats4D.FromAnimatedPointCloud(
    positions, colors, velocities, timestamps, 
    defaultSize: 0.03f, 
    name: "dynamic_scene"
);

splats4d.currentTime = 0;
splats4d.timeScale = 1.0f;
splats4d.loop = true;

Workspace.Prop(splats4d);

// 在渲染循环中更新时间
// splats4d.currentTime += deltaTime;
// Workspace.Prop(splats4d); // 重新提交以更新
```

## 数据结构

### GaussianSplat

```csharp
public struct GaussianSplat
{
    public Vector3 position;      // 位置 (x, y, z)
    public Quaternion rotation;   // 旋转（四元数）
    public Vector3 scale;         // 缩放 (sx, sy, sz) - 决定椭球形状
    public float opacity;         // 不透明度 (0-1)
    public Vector3 color_dc;      // 基础颜色 RGB (0-1)
    public float[] sh_coefficients; // 可选：球谐系数（高阶光照）
}
```

### GaussianSplat4D

```csharp
public struct GaussianSplat4D
{
    public GaussianSplat baseGaussian; // 基础3D高斯
    public float time;                 // 时间戳
    public Vector3 velocity;           // 运动向量
    public Vector3 acceleration;       // 加速度（可选）
}
```

## PLY 文件格式

标准的3D Gaussian Splatting PLY文件包含以下属性：

```
element vertex N
property float x
property float y
property float z
property float f_dc_0        # 颜色 R (DC component)
property float f_dc_1        # 颜色 G
property float f_dc_2        # 颜色 B
property float opacity       # 不透明度（logit空间）
property float scale_0       # 缩放 X（log空间）
property float scale_1       # 缩放 Y
property float scale_2       # 缩放 Z
property float rot_0         # 旋转四元数 X
property float rot_1         # 旋转四元数 Y
property float rot_2         # 旋转四元数 Z
property float rot_3         # 旋转四元数 W
property float f_rest_0      # 可选：SH系数
...
```

## LearnCycleGUI 中的演示

在 LearnCycleGUI 的第四章（Workspace）中，我们提供了完整的演示：

1. **生成简单高斯场景** - 随机球形分布
2. **生成结构化场景** - 花朵形状
3. **从PLY文件加载** - 支持标准格式
4. **4D动态场景** - 粒子爆炸效果
5. **4D旋转环** - 彩虹色旋转环

运行 LearnCycleGUI 并点击 "Chapter 4 - Workspace"，然后展开 "3D Gaussian Splatting" 和 "4D Gaussian Splatting" 部分。

## 性能优化

### LOD (Level of Detail)

对于大型场景，可以限制显示的splat数量：

```csharp
var splats = new PutGaussianSplats
{
    name = "large_scene",
    splats = allSplats,
    maxSplats = 10000  // 只显示前10000个
};
```

### 全局缩放

调整所有splats的大小和透明度：

```csharp
splats.globalSizeScale = 0.5f;      // 缩小50%
splats.globalOpacityScale = 0.8f;   // 降低透明度
```

## 当前实现

当前版本将Gaussian splats转换为点云进行渲染。这是一个简化的实现，适合：

- ✅ 快速原型验证
- ✅ 数据格式测试
- ✅ 基础可视化

未来可以实现完整的椭球体渲染以获得更好的视觉效果。

## 应用场景

### 3D场景重建
- 从多视角照片重建场景
- NeRF/3DGS 训练结果可视化
- 虚拟旅游和展示

### 动态场景（4D）
- 运动捕捉数据可视化
- 时序点云序列
- 粒子系统和特效

### 科研和开发
- 神经渲染算法验证
- 3DGS 数据集查看器
- 训练过程监控

## 相关资源

- [3D Gaussian Splatting 论文](https://repo-sam.inria.fr/fungraph/3d-gaussian-splatting/)
- [PLY 格式规范](http://paulbourke.net/dataformats/ply/)
- [glTF Gaussian Splatting 扩展](https://github.com/KhronosGroup/glTF/pull/2420)

## 下一步

在 LearnCycleGUI 中尝试：

```bash
cd Examples/LearnCycleGUI
dotnet run
```

点击 "Chapter 4 - Workspace"，找到 "3D Gaussian Splatting" 部分，开始探索！🚀
