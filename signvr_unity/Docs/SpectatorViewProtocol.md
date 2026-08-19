# SignVR 实时旁观视角（实验代码，当前停用）

> 当前 `VRSortingGame` 与 `CoopLiftWorkshop` 场景均不挂载
> `SpectatorViewStreamer`。本文仅保留协议设计记录，不代表当前构建支持串流。

项目提供两种把 Quest 内部画面显示到电脑上的路径：

- 只需要玩家眼中的画面时，Quest 系统投屏或 Meta 的设备管理工具最省性能，也最适合调试。
- 需要场景内固定机位，或需要把画面嵌入自己的电脑端工具时，使用项目内的 `SpectatorViewStreamer`。

项目内方案会在 Quest 上渲染一个独立的单目相机，编码为 JPEG，通过 UDP 小包发到电脑。它不会修改 XR 相机的 `targetTexture`，因此不会打断头显双目渲染。

## 启动电脑端

接收端只使用 Python 标准库，不需要安装新依赖：

最简单的方式是双击：

```text
E:\SignVR_Unity\ExternalClient\StartSpectatorView.cmd
```

它会启动 UDP 接收器并自动打开浏览器页面。

```powershell
cd E:\SignVR_Unity\ExternalClient
python .\spectator_viewer.py --open-browser
```

浏览器打开：

```text
http://127.0.0.1:8090
```

默认监听 `0.0.0.0:5006/UDP`。Windows 首次询问防火墙权限时，只需要允许专用网络。可选参数：

```powershell
python .\spectator_viewer.py `
  --udp-port 5006 `
  --web-port 8090 `
  --frame-timeout 0.75 `
  --verbose
```

Quest 和电脑必须位于同一局域网。接收端启动时会打印电脑可用的局域网 IPv4；正式使用时，应把 Unity 组件的 `Remote Host` 设置为其中一个地址。广播地址 `255.255.255.255` 适合首次验证，但持续广播视频会占用更多无线网络资源。

## Unity 场景接入

`VRSortingGame` 场景生成器会默认创建一个 `SpectatorViewStreamer`，使用 Headset POV、广播地址和 UDP 端口 `5006`，并同时创建已绑定的 `Fixed View Anchor`。切换到 Fixed 模式即可使用该固定机位，不需要增加第二台输出相机。

排序回合不会自动清空。物体放置完成后，状态牌会显示 `SORT COMPLETE / PRESS RESET`；直接捏按场景前方的大号 `RESET ROUND` 按钮即可将所有物体恢复到出生点。物体掉出地板边界后才会自动回收，正常落在地板上的物体会保留重力和动量效果。

其他场景也可以通过下列配置 API 创建组件。未指定 `Camera` 时，它会在运行时使用 `Camera.main`，所以 XR 中心眼相机需要带 `MainCamera` 标签。

玩家视角：

```csharp
using SignVR.Streaming;

GameObject root = new GameObject("Spectator View Streamer");
SpectatorViewStreamer streamer =
    root.AddComponent<SpectatorViewStreamer>();
streamer.Configure(
    SpectatorViewMode.HeadsetPov,
    "192.168.1.20"
);
```

固定视角：

```csharp
Transform fixedAnchor = spectatorCameraAnchor.transform;
streamer.Configure(
    SpectatorViewMode.Fixed,
    "192.168.1.20",
    SpectatorViewStreamer.DefaultPort,
    camera: null,
    fixedAnchor: fixedAnchor
);
```

默认参数是 `640x360`、`10 fps`、JPEG 质量 `55`。这是为 Quest 帧稳定性设置的保守起点。确认 GPU/CPU 余量后，再逐步提高分辨率或帧率；不建议一开始就使用 1080p 或 30 fps。

如果电脑画面上下颠倒，可在组件中启用 `Flip Vertically On Desktop`。接收端只用 CSS 翻转显示，不会在 Quest 上再次处理像素。

## UDP 分片协议 v1

每个 JPEG 帧会被切成不超过 1200 字节的 UDP 数据报，以避免常见 1500 MTU 网络上的 IP 分片。所有多字节整数均为无符号小端序。

接收端和发送端共同限制单帧不超过 `8 MiB`、分片数不超过 `8192`。接收端按声明的完整帧大小累计分片字节数，超限时立即丢弃该帧；不完整帧默认在 `0.75` 秒后超时丢弃。

| 偏移 | 长度 | 字段 | 说明 |
|---:|---:|---|---|
| 0 | 4 | Magic | ASCII `SVR1` |
| 4 | 1 | Version | 当前为 `1` |
| 5 | 1 | Flags | bit 0: Headset POV；bit 1: 电脑端垂直翻转 |
| 6 | 2 | Header size | 当前为 `24` |
| 8 | 4 | Frame ID | 每帧递增，按 `uint32` 回绕 |
| 12 | 2 | Chunk index | 从 `0` 开始 |
| 14 | 2 | Chunk count | 当前帧总分片数 |
| 16 | 2 | Width | 图像宽度 |
| 18 | 2 | Height | 图像高度 |
| 20 | 4 | Frame size | 完整 JPEG 字节数 |
| 24 | 可变 | Payload | 当前 JPEG 分片 |

接收端允许乱序和重复分片。默认在最后一个分片到达后重组 JPEG；不完整帧在 `0.75` 秒后丢弃，不做重传。这样即使 Wi-Fi 短暂丢包，也不会让旧画面阻塞后续实时画面。

协议没有认证、加密或拥塞控制，只适用于可信局域网。需要公网传输、音频或更高画质时，应把编码/传输层替换为 WebRTC；当前 JPEG/UDP 方案的重点是依赖少、延迟可控，并且能支持固定机位。

## 验证

电脑端协议测试：

```powershell
python -m unittest discover -s .\ExternalClient\tests -v
```

Unity EditMode 测试 `SignVR.Spectator.Tests` 会验证 24 字节头布局和分片数量。Quest 真机验证时，可在 Android Logcat 中搜索 `SpectatorViewStreamer`，并对照浏览器底栏的发送者、帧率和超时帧数。
