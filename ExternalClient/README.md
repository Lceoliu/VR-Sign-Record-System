# Quest 实时骨架客户端

这个客户端接收 Unity/Quest 通过 UDP 发来的原始身体追踪数据，并在 Viser 的网页 3D 场景中显示关节和骨骼连线。

## 启动

在电脑上打开 PowerShell：

```powershell
cd ExternalClient
python -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install -r requirements.txt
python motion_viewer.py
```

然后用浏览器打开：

```text
http://127.0.0.1:8080
```

启动 Quest 应用后，终端会显示 `Skeleton received` 和接收帧率，网页中的骨架会实时运动。

网页左侧的 `Motion Stream Diagnostics` 面板会显示完整链路状态。Unity 每秒发送一个 `status` 心跳包，因此即使身体追踪尚未产生数据，客户端也应该先显示绿色的 `UDP receiving`。

需要打印更多单包信息时运行：

```powershell
python motion_viewer.py --verbose
```

## 网络设置

- Quest 和运行客户端的电脑需要连接同一个局域网。
- Unity 场景默认向 `255.255.255.255:5005` 广播，不需要预先填写电脑 IP。
- Windows 第一次询问网络权限时，请允许 Python 使用“专用网络”。
- 如果路由器禁止广播，在 Unity 场景中选择 `MotionRecorder`，把 `Meta Body Motion Streamer > Remote Host` 改成电脑的局域网 IPv4 地址，例如 `192.168.1.20`。
- 自定义 UDP 或网页端口时可运行：`python motion_viewer.py --udp-port 5005 --web-port 8080`。

## 没有显示骨架时

按诊断面板从上到下检查：

1. `No UDP packet received`：网络链路未通。把面板显示的 `PC IPv4 candidates` 地址填入 Unity 的 `Remote Host`，优先用单播排除路由器禁止广播的问题；同时允许 Python 通过 Windows 专用网络防火墙。
2. `UDP receiving`，但 `BodyState available = False`：网络已通，问题在 Quest 身体追踪。检查身体追踪权限、运行时 Full Body 设置和头显是否能够看到身体。
3. `BodyState available = True`，但 `Pose valid = False` 或有效关节为 0：追踪数据已经建立，但当前姿态还不可用。
4. `Skeleton received by client = False`：动作帧到了但骨架结构包丢失，等待最多 2 秒；Unity 会周期重发。
5. `Sequence gaps` 持续增加：Wi-Fi 正在丢 UDP 帧，建议把 `Remote Host` 从广播地址改成电脑 IPv4 地址。

Quest 画面里的录制状态区域也会显示 `UDP BROADCAST ON`/`UDP ON`、已发送动作帧数，以及有效关节数。Unity 日志可通过 Android Logcat 搜索 `MetaBodyMotionStreamer`，每 5 秒会输出一次完整发送统计。

## Build And Run 后查看 Quest 日志

Quest 上运行的是 Android 应用，因此 `Debug.Log` 默认进入头显的 Android Logcat，不会自动回到 Unity Console。调试时建议同时打开三个窗口：Python 客户端、浏览器诊断面板和 ADB Logcat。

### 1. 确认客户端正在监听

先启动客户端：

```powershell
cd E:\rensh\UnityProjects\Quest_test\ExternalClient
.\.venv\Scripts\python.exe -u .\motion_viewer.py --verbose
```

终端必须出现：

```text
[CLIENT READY] Listening on UDP 0.0.0.0:5005
```

另开 PowerShell 检查端口：

```powershell
netstat -ano -p udp | Select-String ":5005"
```

预期看到 `UDP 0.0.0.0:5005`。如果没有，客户端没有成功进入接收状态，先检查终端中的 Python 异常。

### 2. 使用 ADB 实时读取 Unity 日志

Unity 6000.5.6f1 自带的 ADB 位于：

```text
C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe
```

在点击 Build And Run 之前，打开 PowerShell 并运行：

```powershell
$questAdb = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"

& $questAdb devices -l
& $questAdb logcat -c
& $questAdb logcat -v time Unity:V AndroidRuntime:E "*:S" |
    Select-String "MetaBodyMotionStreamer|AndroidRuntime"
```

保持该窗口运行，再在 Unity 中 Build And Run。正常情况下会依次看到：

```text
[MetaBodyMotionStreamer][UDP STARTED]
[MetaBodyMotionStreamer][BODY DATA AVAILABLE]
[MetaBodyMotionStreamer][SKELETON SENT]
[MetaBodyMotionStreamer][UDP DIAGNOSTICS]
```

`UDP DIAGNOSTICS` 每 5 秒输出一次，重点关注：

- `destination`：目标电脑 IP 和端口是否正确。
- `packets`：包含状态包、骨架包和动作包的总发送数。
- `frames`：成功交给本地 UDP Socket 的动作帧数。
- `failures`：本地 Socket 发送失败数。
- `bodyState`：Meta Body Tracking 是否已经产生数据。
- `validJoints` 和 `confidence`：当前追踪质量。

UDP 的本地发送成功不等于客户端一定收到，因此必须把 Unity 的 `frames` 与客户端的接收帧率、`Sequence gaps` 一起判断。

### 3. 已经启动应用后，只看该应用日志

项目当前 Android 包名是 `com.UnityTechnologies.com.unity.template.urpblank`。应用已经运行时可按进程过滤：

```powershell
$questAppId = "com.UnityTechnologies.com.unity.template.urpblank"
$questProcessId = (& $questAdb shell pidof $questAppId).Trim()

& $questAdb logcat --pid=$questProcessId -v time |
    Select-String "MetaBodyMotionStreamer|AndroidRuntime"
```

如果 `$questProcessId` 为空，说明应用当前没有运行。

### 4. 日志和现象对照

| Unity/客户端状态 | 含义 | 下一步 |
|---|---|---|
| 没有 `UDP STARTED` | 新 APK 未启动、场景未包含组件或组件被禁用 | 确认 Build Profile 中使用 `SignTrackingRecorder`，重新 Build And Run |
| `UDP START FAILED` | UDP Socket 创建失败 | 查看同行的异常信息和目标地址 |
| `BODY DATA WAITING` | 网络心跳可以发送，但身体追踪没有数据 | 检查 Body Tracking 权限、Full Body 设置和头显视野 |
| Unity `frames` 增长，客户端没有 `status` | Unity 本地发送正常，但网络链路未通 | 检查 Remote Host、同一局域网和 Windows 防火墙 |
| 客户端有 `status`，没有 `frame` | 网络已通，但没有可发送的身体动作 | 查看 `BodyState available`、`Pose valid` 和有效关节数 |
| 客户端 `Sequence gaps` 增长 | UDP 动作帧丢失 | 使用电脑 IPv4 单播代替广播，改善 Wi-Fi |
| Quest UI 显示 `UDP ON` 且 `Sent` 增长 | Quest 发送端正在工作 | 对照客户端接收帧率继续检查 |

也可以在 Unity Package Manager 安装 `Android Logcat`，再通过 `Window > Analysis > Android Logcat` 查看；命令行 ADB 通常更适合保留完整、可搜索的调试记录。

这是局域网原型协议，采用无连接 UDP，不包含认证、加密或丢包重传。
