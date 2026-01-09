# CycleGUI

A lightweight, high-performance, cross-platform immediate mode GUI library.

[中文文档](README.zh-CN.md)

## Quick Start

### Run LearnCycleGUI

**LearnCycleGUI is the best documentation** - it's a runnable interactive example showcasing all CycleGUI features.

```bash
cd Examples/LearnCycleGUI
dotnet run
```

Open the program and click the chapter buttons on the left:

1. **Chapter 1 - Panel Controls** - Live demo of all UI widgets
2. **Chapter 2 - Panel Layout** - Panel layout and management
3. **Chapter 3 - Plot** - Charts and data visualization
4. **Chapter 4 - Workspace** - 3D rendering and interactions
5. **Chapter 5 - Utilities** - Advanced features (gestures, multi-threading, SLAM)

### Web Version

LearnCycleGUI can also run in browsers. After starting, access the displayed URL in your browser.

## Features

- ✅ **Immediate Mode GUI** - Simple, intuitive API
- ✅ **Cross-Platform** - Windows, Linux, Web (WebAssembly)
- ✅ **3D Rendering** - Point clouds, models, custom meshes, **Gaussian Splatting** ✨
- ✅ **Rich Interactions** - Object selection, 3D transforms, gestures
- ✅ **Real-time Updates** - Perfect for monitoring systems
- ✨ **NEW: 3D/4D Gaussian Splatting** - Neural rendering support

## Example Code

### Create UI

```csharp
var panel = GUI.DeclarePanel()
    .ShowTitle("My Panel")
    .InitPos(false, 100, 100);

int counter = 0;
panel.Define(pb =>
{
    pb.Label("Welcome to CycleGUI!");
    if (pb.Button("Click Me"))
        counter++;
    pb.Label($"Clicks: {counter}");
});
```

### Display 3D Point Cloud

```csharp
Workspace.AddProp(new PutPointCloud()
{
    name = "cloud",
    xyzSzs = points,  // Vector4[] { x, y, z, size }
    colors = colors   // uint[] ABGR format
});
```

### Load 3D Model

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

### Display 3D Gaussian Splats ✨

```csharp
// Auto-generate from point cloud
var splats = PutGaussianSplats.FromPointCloud(
    positions, colors, defaultSize: 0.05f, "gaussian_scene"
);
Workspace.Prop(splats);

// Or load from PLY file
var splats = PutGaussianSplats.FromPLY("scene.ply", "loaded_scene");
Workspace.Prop(splats);
```

See [Gaussian Splatting Documentation](GAUSSIAN_SPLATTING.md)

## Building

### Prerequisites

- .NET 8.0 SDK
- Windows: Visual Studio 2019+, vcpkg
- Linux: GCC/Clang, Make

### Build Steps

```bash
# Windows
.\compile.bat Release

# Linux
make
dotnet build -c Release
```

## Use Cases

- Industrial Software (device control, monitoring)
- Digital Twin (sensor data visualization)
- Robotics (control panels, SLAM visualization)
- Scientific Tools (data annotation, algorithm visualization)

## Links

- [Gitee Repository](https://gitee.com/Fairyland_1/CycleGUI)
- [Releases](https://gitee.com/Fairyland_1/CycleGUI/releases)

---

**The best documentation is LearnCycleGUI itself - run it, explore it, modify it!** 🚀
