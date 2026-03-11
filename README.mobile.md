# LyoMir2Client 手机端（Android）使用说明

## 一、手机端项目结构

```
MirClient.Android/
├── MirClient.Android.csproj      # 项目文件（net8.0-android）
├── MauiProgram.cs                # MAUI 应用启动入口
├── App.xaml                      # 应用主文件（全局主题/资源）
├── App.xaml.cs                   # 应用主文件代码后台
├── AppShell.xaml                 # Shell 导航配置（页面路由）
├── AppShell.xaml.cs              # Shell 导航代码后台
├── Pages/
│   ├── LoginPage.xaml            # 登录页面（UI 布局）
│   ├── LoginPage.xaml.cs         # 登录页面代码后台（TCP 连接测试）
│   ├── GamePage.xaml             # 游戏渲染页面（UI 布局）
│   └── GamePage.xaml.cs          # 游戏渲染页面代码后台（SkiaSharp）
└── Controls/
    └── VirtualJoystick.cs        # 虚拟摇杆自定义控件
```

### 依赖的现有跨平台项目

| 项目 | 用途 |
|------|------|
| `MirClient.Core` | 游戏核心逻辑层 |
| `MirClient.Net` | 网络通信层 |
| `MirClient.Protocol` | 传奇私服通信协议 |
| `MirClient.Assets` | 游戏资源加载 |

### 新增 NuGet 包

| 包名 | 版本 | 用途 |
|------|------|------|
| `Microsoft.Maui.Controls` | 8.0.100 | MAUI 跨平台 UI 框架 |
| `SkiaSharp.Views.Maui.Controls` | 2.88.8 | 游戏画布渲染（替代 PC 端 Direct3D11） |
| `Plugin.Maui.Audio` | 3.0.0 | 音频播放（替代 PC 端 DirectSound） |

---

## 二、如何在 Visual Studio 中编译 Android APK

### 前置条件

1. 安装 **Visual Studio 2022**（Community 版本免费）
   - 下载地址：https://visualstudio.microsoft.com/zh-hans/downloads/
   
2. 在 Visual Studio 安装程序中，勾选以下工作负载：
   - ☑ **.NET Multi-platform App UI 开发**（MAUI，必须）
   - ☑ **.NET 桌面开发**（可选）

3. 安装 **Android SDK**（Visual Studio 会自动提示安装）
   - 需要 Android API Level 21 或以上（Android 5.0+）

4. 安装 **JDK 11** 或以上版本（编译 Android 需要）

### 编译步骤

#### 方法一：直接编译（连接真机）

1. 打开 `LyoMir2Client.sln` 解决方案
2. 在 Visual Studio 顶部工具栏，将启动项目设置为 **MirClient.Android**
3. 用 USB 数据线连接 Android 手机（需开启开发者选项和 USB 调试）
4. 在目标设备下拉列表中选择你的手机
5. 点击 **运行（F5）** 或 **调试（Ctrl+F5）**
6. 等待编译完成，APK 会自动安装到手机并启动

#### 方法二：导出 APK 文件

1. 在 Visual Studio 中，右键点击 **MirClient.Android** 项目
2. 选择 **发布** → **发布到文件夹**
3. 配置签名：首次需要创建 Android 密钥库（Keystore）
4. 点击 **发布**，等待构建完成
5. APK 文件会保存在指定输出目录

#### 方法三：命令行编译

```bash
# 在项目根目录运行
dotnet build MirClient.Android/MirClient.Android.csproj -f net8.0-android

# 生成发布版 APK
dotnet publish MirClient.Android/MirClient.Android.csproj -f net8.0-android -c Release
```

---

## 三、功能说明

### 登录页面

- 输入**服务器地址**（格式：`IP地址:端口`，如 `192.168.1.1:7000`）
- 输入**账号**和**密码**
- 点击**登录**按钮：
  - 程序会使用 TCP 连接测试是否能连通服务器
  - 连接成功后自动跳转到游戏页面
  - 如果连接失败，会显示具体错误信息

### 游戏页面

- **黑色渲染画布**：使用 SkiaSharp（替代 Direct3D11）渲染游戏画面
- **左侧虚拟摇杆**：控制角色移动方向
- **右侧技能按钮**：4个圆形技能按钮（技能1/2/3/4）

### 虚拟摇杆

- 触摸并按住摇杆区域，拖动控制方向
- 支持 8 个方向（实际上是连续的 360° 方向）
- 松开手指摇杆自动归位到中心
- 方向向量范围：X 和 Y 各 -1.0 到 1.0

---

## 四、注意事项

### 开发环境

- 需要 **.NET 8 SDK** 和 **MAUI Workload**：
  ```bash
  # 安装 MAUI 工作负载
  dotnet workload install maui-android
  ```
- 建议使用 **Visual Studio 2022 17.8+** 以获得最佳 MAUI 支持

### Android 手机设置

- 目标 Android 版本：**Android 5.0（API 21）或以上**
- 若要在真机调试，需要在手机的开发者选项中开启 **USB 调试**
- 部分手机需要允许安装未知来源应用

### 编译耗时

- 首次编译约需 **5~15 分钟**（需要下载 Android 工具链）
- 后续增量编译约需 **1~3 分钟**

### 已知限制

- 当前版本登录后仅测试 TCP 连接，未实现完整的传奇登录协议
- 游戏渲染页面当前为占位状态，需接入 `MirClient.Core` 实现完整渲染
- 技能按钮当前为占位，需实现实际技能逻辑

### 不修改 PC 端

本 Android 项目所有文件均存放在 `MirClient.Android/` 目录下，  
**不对任何现有 PC 端项目文件做任何修改**。

---

## 五、后续开发计划

1. **接入登录协议**：使用 `MirClient.Protocol` 和 `MirClient.Net` 实现完整的传奇私服登录流程
2. **游戏画面渲染**：将 `MirClient.Core` 的渲染逻辑接入 SkiaSharp 画布
3. **音频实现**：使用 `Plugin.Maui.Audio` 替代 PC 端的 DirectSound 播放背景音乐和音效
4. **游戏逻辑**：接入角色移动、战斗、NPC 交互等功能
5. **UI 优化**：设计更精美的登录界面和游戏 HUD

---

*本文档最后更新：2024年*  
*作者：LyoMir2Client Android 移植项目*
