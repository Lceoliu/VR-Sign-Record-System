# SignVR 录制主机便携包

这个包只包含运行录制主机需要的内容：后端、已经构建好的网页、便携 Python 和启动脚本。新电脑不需要安装 Python、Node.js 或 Unity。

整个目录可以放在任意本地盘、任意映射盘符或 UNC 共享路径。程序通过 BAT 自身位置查找组件和 `data`，不依赖固定盘符。直接从 NAS 启动时，程序文件会缓存到当前 Windows 用户的 `%LOCALAPPDATA%\SignVR\HostRuntime`，录制数据仍写入便携包的 `data`。

## 启动

1. 先关闭旧电脑上的 SignVR Host，避免两台主机同时控制同一台 Quest。
2. 可以直接打开 NAS 上的完整 `SignVR-Host-Portable` 文件夹，也可以将整个文件夹复制到新电脑。不要只复制 BAT 文件。
3. 确认新电脑和 Quest 3 都连接 `RhythMo`，并将 USB 脚踏板和外接摄像头连接到新电脑。
4. 正常双击最外层的 `Start-SignVR-Host.bat`，不要右键选择“以管理员身份运行”。映射盘在管理员会话中可能不可见。
5. 第一次启动会弹出 Windows 管理员确认，用于开放局域网 TCP 8011 和 UDP 5011；随后会准备本机运行缓存，NAS 较慢时可能需要一分钟。以后启动会直接复用缓存。
6. 浏览器会自动打开 `http://127.0.0.1:8011`。如果没有自动打开，手动输入该地址。

启动窗口必须在录制期间保持打开。按 `Ctrl+C` 可停止服务。

## 录制设备

- Quest：打开已经安装的 SignVR 录制应用。Host 每 3 秒自动发现 Quest，并在首次发现时把上传地址刷新为新电脑 IP，无需重新烧录 APK。
- 脚踏板：脚踏板作为 USB HID 键盘使用，必须输出空格键。网页需要保持焦点，光标不要停在输入框中。短踩开始/结束，长踩 1.2 秒重录。
- 外接摄像头：第一次使用时允许浏览器访问摄像头，然后在网页中选择对应设备。视频由浏览器录制并上传。

## 数据位置

默认数据保存在便携包内：

```text
data/recordings/{batch_id}/{round_id}/{sentence_id}/{take_id}/
```

每个 Take 最终应包含：

```text
take_NNN.pose.jsonl
take_NNN.meta.json
take_NNN.camera.webm
```

迁移、备份或交付数据时，复制完整的 `data` 文件夹。不要在录制过程中移动或同步这个目录。

## 启动失败

- 提示端口 8011 被占用：关闭旧的 SignVR 窗口或占用该端口的程序，再重新双击 BAT。
- 防火墙配置失败：查看 `%LOCALAPPDATA%\SignVR\Firewall\setup-firewall-error.log`。映射盘上的包必须普通双击启动，由 BAT 单独申请防火墙权限。
- 网页看不到 Quest：确认旧 Host 已关闭、Quest 应用正在前台、两台设备都连接 `RhythMo`，然后在网页点击扫描。
- Quest 能显示但 Pose 不上传：确认首次启动时已经允许管理员弹窗，并检查 Windows 防火墙中存在 `SignVR Host HTTP 8011` 和 `SignVR Host UDP 5011`。
- 脚踏板没有反应：先用记事本确认踩下时会输入空格，再点击网页空白处让页面获得焦点。
