# LyoMir2 手机端（Android）说明文档

## 项目结构

```
MirClient.Android/
├── MirClient.Android.csproj          # 项目文件（net8.0-android，MAUI）
├── MauiProgram.cs                    # 应用入口，配置依赖注入
├── App.xaml / App.xaml.cs            # 应用根类
├── AppShell.xaml / AppShell.xaml.cs  # 导航外壳（Shell路由）
│
├── Services/
│   ├── AssetDownloadManager.cs       # 资源按需HTTP下载管理器（单例）
│   └── AssetManifest.cs              # 资源清单系统（manifest.json 管理）
│
├── Pages/
│   ├── LoadingPage.xaml/.cs          # 启动加载页（下载核心资源）
│   ├── LoginPage.xaml/.cs            # 登录页（服务器/账号/密码）
│   ├── GamePage.xaml/.cs             # 游戏主渲染页（SkiaSharp画布）
│   └── SettingsPage.xaml/.cs         # 设置页（服务器地址/缓存管理）
│
├── Controls/
│   └── VirtualJoystick.cs            # 虚拟摇杆控件
│
├── Platforms/
│   └── Android/
│       ├── MainActivity.cs           # Android 主 Activity
│       └── AndroidManifest.xml       # Android 清单文件（权限声明）
│
└── Resources/
    ├── Styles/
    │   ├── Colors.xaml               # 颜色主题定义
    │   └── Styles.xaml               # 控件样式定义
    ├── Images/                        # 图片资源
    ├── Fonts/                         # 字体文件
    └── Raw/                           # 原始资源文件
```

## 资源服务器搭建说明

### 方案：使用 nginx 搭建静态HTTP资源服务器

#### 1. 安装 nginx

**Linux（Ubuntu/Debian）：**
```bash
sudo apt update
sudo apt install nginx
```

**Windows：**
从 https://nginx.org 下载，解压后运行 `nginx.exe`。

#### 2. 准备资源文件目录

将游戏资源文件整理到服务器目录，例如 `/var/www/mir2/`：

```
/var/www/mir2/
├── manifest.json       # 资源清单文件（见下方生成方法）
├── Data/
│   ├── Prguse.wil
│   ├── ChrSel.wil
│   └── ...
├── Map/
│   ├── 1.map
│   └── ...
└── Wav/
    ├── ...
```

#### 3. 配置 nginx

编辑 `/etc/nginx/sites-available/mir2`：

```nginx
server {
    listen 80;
    server_name your-server-ip;

    root /var/www/mir2;
    index index.html;

    # 允许大文件下载，禁用缓冲
    location / {
        autoindex on;
        
        # 允许跨域（手机可能通过内网IP访问）
        add_header Access-Control-Allow-Origin *;
        
        # 支持断点续传
        add_header Accept-Ranges bytes;
    }
    
    # 针对大资源文件优化
    location ~* \.(wil|wzl|map|wis)$ {
        gzip off;          # 这类文件已压缩，不需要再gzip
        sendfile on;
        tcp_nopush on;
    }
}
```

启用配置并重启：
```bash
sudo ln -s /etc/nginx/sites-available/mir2 /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl reload nginx
```

#### 4. 验证服务器

在浏览器访问 `http://your-server-ip/Data/Prguse.wil`，能下载文件即配置成功。

---

## 生成 manifest.json

以下是生成资源清单的 Python 脚本示例：

```python
#!/usr/bin/env python3
"""
生成 LyoMir2 资源清单文件 manifest.json
使用方法：python generate_manifest.py /path/to/mir2/resources
"""

import os
import json
import hashlib
import sys
from datetime import datetime

def compute_md5(filepath):
    """计算文件的 MD5 哈希值"""
    md5 = hashlib.md5()
    with open(filepath, 'rb') as f:
        for chunk in iter(lambda: f.read(65536), b''):
            md5.update(chunk)
    return md5.hexdigest()

def generate_manifest(root_dir, version=None):
    """
    扫描目录，生成资源清单
    
    Args:
        root_dir: 资源根目录路径
        version: 版本号，默认使用当前日期
    """
    if version is None:
        version = datetime.now().strftime('%Y%m%d')
    
    files = []
    
    # 递归扫描所有文件
    for dirpath, _, filenames in os.walk(root_dir):
        for filename in filenames:
            if filename == 'manifest.json':
                continue
            
            filepath = os.path.join(dirpath, filename)
            # 计算相对路径（统一使用正斜杠）
            relpath = os.path.relpath(filepath, root_dir).replace('\\', '/')
            
            size = os.path.getsize(filepath)
            
            print(f'  处理: {relpath} ({size:,} bytes)', end='', flush=True)
            md5 = compute_md5(filepath)
            print(f' MD5: {md5}')
            
            files.append({
                'path': relpath,
                'size': size,
                'md5': md5
            })
    
    manifest = {
        'version': version,
        'generated': datetime.now().isoformat(),
        'files': files
    }
    
    return manifest

def main():
    if len(sys.argv) < 2:
        print('用法: python generate_manifest.py <资源目录> [版本号]')
        print('示例: python generate_manifest.py /var/www/mir2 1.0.0')
        sys.exit(1)
    
    root_dir = sys.argv[1]
    version = sys.argv[2] if len(sys.argv) > 2 else None
    
    if not os.path.isdir(root_dir):
        print(f'错误：目录不存在: {root_dir}')
        sys.exit(1)
    
    print(f'正在扫描目录: {root_dir}')
    manifest = generate_manifest(root_dir, version)
    
    output_path = os.path.join(root_dir, 'manifest.json')
    with open(output_path, 'w', encoding='utf-8') as f:
        json.dump(manifest, f, ensure_ascii=False, indent=2)
    
    print(f'\n清单已生成: {output_path}')
    print(f'版本: {manifest["version"]}')
    print(f'文件数量: {len(manifest["files"])}')

if __name__ == '__main__':
    main()
```

**使用方法：**
```bash
python generate_manifest.py /var/www/mir2 1.0.0
```

---

## 如何在 Visual Studio 编译 APK

### 前提条件

1. 安装 Visual Studio 2022（勾选「.NET Multi-platform App UI 开发」工作负载）
2. 安装 Android SDK（通过 Visual Studio Installer 或 Android Studio）
3. 安装 JDK 11+

### 编译步骤

1. 打开 `LyoMir2Client.sln`

2. 在解决方案资源管理器中，右键单击 `MirClient.Android` 项目

3. 选择「属性」→「Android」→ 配置包名（`com.lyomir2.client`）

4. **调试版 APK（用于测试）：**
   - 将目标框架切换为 `net8.0-android`
   - 按 F5 或点击「调试」按钮
   - 如果连接了 Android 设备（USB调试已开启），会自动安装到设备

5. **发布版 APK（用于分发）：**
   - 菜单「生成」→「发布」
   - 选择「Ad Hoc」或「Google Play」
   - 配置签名密钥（需要 keystore 文件）
   - 点击「发布」，APK 保存在 `bin/Release/net8.0-android/` 目录

### 使用 dotnet CLI 编译

```bash
cd MirClient.Android
dotnet build -f net8.0-android -c Debug
dotnet publish -f net8.0-android -c Release
```

---

## 把资源文件放到手机本地（备用方案）

如果无法搭建HTTP服务器，可以手动将资源文件复制到手机：

### 方法一：通过数据线

1. 用 USB 数据线连接手机和电脑
2. 手机上选择「文件传输」模式
3. 在电脑上打开手机存储，找到 `/sdcard/`
4. 创建目录 `LyoMir2/Cache/`
5. 将 `Data/`、`Map/`、`Wav/` 文件夹复制到 `/sdcard/LyoMir2/Cache/`

### 方法二：通过 adb 命令

```bash
# 安装 adb 并连接手机（需开启 USB 调试）
adb devices

# 推送资源目录到手机
adb push Data/ /sdcard/LyoMir2/Cache/Data/
adb push Map/ /sdcard/LyoMir2/Cache/Map/
adb push Wav/ /sdcard/LyoMir2/Cache/Wav/
```

### 预期目录结构

手机上资源文件的存放位置：
```
/sdcard/LyoMir2/Cache/
├── Data/
│   ├── Prguse.wil
│   ├── ChrSel.wil
│   └── ...
├── Map/
│   ├── 1.map
│   └── ...
└── Wav/
    └── ...
```

---

## 注意事项

- **Android 10+**：`WRITE_EXTERNAL_STORAGE` 权限已被限制，应用优先使用 `/sdcard/LyoMir2/` 目录，如无权限则回退到应用私有目录。
- **Android 13+**：不需要存储权限即可访问应用自己的外部目录。
- **资源服务器地址**：在 APP 设置页面可随时修改，修改后重启加载页面生效。
- **缓存清理**：设置页面提供「清理缓存」功能，清理后再次进入游戏会重新下载所有资源。
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
