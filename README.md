# BiliTray

一个可以缩到系统托盘的 B 站听歌小窗口（Windows）。用 Chrome 的应用模式打开哔哩哔哩，窗口藏起来之后音乐继续播放，不占任务栏。

## 功能

- **最小化 / 点叉 / Alt+F4 / Ctrl+W** → 藏到托盘，继续播放
- **单击托盘图标** → 显示 / 隐藏窗口
- **右键托盘图标** → 播放/暂停、上一首、下一首、打开数据文件夹、退出
- 鼠标停在托盘图标上显示当前视频标题
- B 站"在新标签页打开"的链接会留在这个小窗口里打开
- 窗口隐藏后不降速，连播 / 自动下一首正常
- **便携**：浏览器数据（登录状态等）保存在 exe 同目录的 `Data\ChromeProfile`，整个文件夹拷到哪里都能用

## 使用

需要已安装 Google Chrome。双击 `BiliTray.exe` 即可。也可以传一个网址作为起始页：

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
