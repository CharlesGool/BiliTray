# BiliTray

一个可以缩到系统托盘的 B 站听歌小窗口（Windows）。用 Chrome 的应用模式打开哔哩哔哩，窗口藏起来之后音乐继续播放，不占任务栏。

## 功能

- **最小化 / 点叉 / Alt+F4 / Ctrl+W** → 藏到托盘，继续播放
- **单击托盘图标** → 显示 / 隐藏窗口
- **右键托盘图标** → 播放/暂停、上一首、下一首、省电模式、低内存模式、打开数据文件夹、退出
- 鼠标停在托盘图标上显示当前视频标题
- B 站"在新标签页打开"的链接会留在这个小窗口里打开
- 窗口隐藏后不降速，连播 / 自动下一首正常
- 后台省资源：隐藏时 Chrome 停止渲染和视频解码（GPU ≈ 0%），并每 30 秒把空闲内存交还系统（隐藏时实际占用约 0.2 GB）
- **省电模式**（托盘右键开关，默认开）：画质限制到 480P、关闭弹幕、禁用页面动画，窗口打开时也少占 CPU / GPU
- **低内存模式**（托盘右键开关，默认关，重开后生效）：关闭显卡加速，内存约减半；代价是窗口打开时 CPU 占用明显升高（实测约 10% → 50% 单核）
- **便携**：浏览器数据（登录状态等）保存在 exe 同目录的 `Data\ChromeProfile`，整个文件夹拷到哪里都能用

## 下载

到 [Releases 页面](https://github.com/CharlesGool/BiliTray/releases/latest) 下载最新的 `BiliTray.exe`。

1. 新建一个文件夹（例如 `D:\Apps\BiliTray`），把 `BiliTray.exe` 放进去
   ——浏览器数据会保存在同目录的 `Data\` 里，所以最好单独放一个文件夹
2. 双击 `BiliTray.exe` 运行，第一次打开后在窗口里登录 B 站即可
3. 想开机自启：右键 `BiliTray.exe` →「创建快捷方式」，把快捷方式放进 `shell:startup` 文件夹（Win+R 输入 `shell:startup` 打开）

**运行要求**：Windows 10 / 11，已安装 Google Chrome。不需要安装其他运行库（使用系统自带的 .NET Framework 4.x）。

> 第一次运行时 Windows SmartScreen 可能提示"已保护你的电脑"，因为 exe 没有数字签名。点「更多信息」→「仍要运行」即可；介意的话也可以按下面的「编译」自己从源码编译。

**更新**：在托盘右键「退出」，用新下载的 `BiliTray.exe` 覆盖旧文件，再打开即可，`Data\` 里的登录状态会保留。

## 使用

双击 `BiliTray.exe` 即可。也可以传一个网址作为起始页：

```
BiliTray.exe https://space.bilibili.com/你的UID/favlist
```

要真正退出请用托盘右键「退出」，这样 Chrome 会正常关闭并保存登录状态。

## 编译

只需要 Windows 自带的 .NET Framework 4.x：

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

输出在 `BiliTray\BiliTray.exe`。

## 实现说明

- 用独立的 `--user-data-dir` 启动 Chrome `--app` 窗口，与日常使用的 Chrome 互不影响
- 通过全局低级鼠标 / 键盘钩子拦截关闭按钮和快捷键，改为隐藏窗口
- 通过 Chrome 调试接口（仅监听 `127.0.0.1:29333`）把新开的标签页拉回播放窗口
