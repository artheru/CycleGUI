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

本仓库旨在用清晰的示例展示CycleGUI的功能，让用户快速上手，使用CycleGUI创造自己的应用。教程包含以下内容：

+ Controls
    + CollapsingHeader
    + Label
    + Separator
    + Button
    + SameLine
    + CheckBox
    + RadioButtons
    + Toggle
    + DragFloat
    + DropdownBox
    + ButtonGroups
    + TextInput
    + ListBox
    + ChatBox
    + Table
    + DisplayFileLink
    + OpenFile
    + SelectFolder
+ Panels
+ Chart
+ Workspace
