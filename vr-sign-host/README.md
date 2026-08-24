# VR 手语录制主机

本项目是 SignVR 的本地录制工作站：FastAPI 负责 Quest 发现、录制控制、文件接收和实时转发，React 负责外置相机、句子队列、脚踏空格交互与操作台显示。每台工作站使用唯一 `station_id`，固定绑定一台 Quest，并将数据保存在各自的本地目录。

## 启动

使用预构建部署 ZIP 时，`frontend\dist` 已经包含网页成品，只需安装后端依赖：

```powershell
cd .\backend
py -3.12 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements-lock.txt
cd ..
```

随后按下文配置工作站身份并运行 `scripts\start-local.ps1`，无需安装 Node.js 或重新构建网页。

首次安装：

```powershell
cd D:\SignVR\vr-sign-host\backend
py -3.12 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements-lock.txt

cd D:\SignVR\vr-sign-host\frontend
pnpm install
pnpm build
```

首次作为录制工作站使用时，先设置工作站身份和独立数据目录：

```powershell
cd D:\SignVR\vr-sign-host
.\scripts\configure-station.ps1 -StationId Station-01 -DataRoot D:\SignVRData\Station-01
```

第二台主机使用不同的身份和目录，例如 `Station-02` 与 `D:\SignVRData\Station-02`。不要复制 `config\station.json` 到另一台主机。

之后运行：

```powershell
cd D:\SignVR\vr-sign-host
.\scripts\start-local.ps1
```

浏览器打开 `http://127.0.0.1:8011`。服务以前台方式运行，按 `Ctrl+C` 停止。

首次使用真机前，请在“以管理员身份运行”的 PowerShell 中配置只允许本地子网访问的端口规则：

```powershell
cd D:\SignVR\vr-sign-host
.\scripts\setup-firewall.ps1
```

如需用 Windows Unity Editor 代替 Quest 做完整 UDP 控制联调，执行：

```powershell
.\scripts\setup-firewall.ps1 -EnableUnityEditorSimulation
```

第二种模式会禁用该 Unity Editor 可执行文件现有的入站 Block 规则，再新增仅限 `LocalSubnet`、UDP 5006 的允许规则；Quest 真机联调不需要这一步。

## 端口

| 端口 | 协议 | 用途 |
|---|---|---|
| 8011 | TCP/HTTP/WebSocket | React、控制 API、Pose/Meta/视频上传、Quest JPEG 预览 |
| 5005 | UDP | Quest 设备公告、命令 ACK、现有实时 Pose 数据 |
| 5006 | UDP | Quest 控制端口，接收发现、配对和录制命令 |

Quest 和主机必须位于同一可信局域网。Windows 主机需要允许 Python 的 TCP 8011 和 UDP 5005 入站；UDP 5006 位于 Quest 端，仅在 Unity Editor 本机模拟时需要 Windows 入站规则。Unity 项目允许明文 HTTP，仅用于这个受信任的本地录制网络。

## 数据目录

文件保存在 `config\station.json` 指定的数据根目录中：

```text
data/recordings/{batch_id}/{round_id}/
  round.json
  {sentence_id}/{take_id}/
  {take_id}.pose.jsonl
  {take_id}.meta.json
  {take_id}.camera.webm
```

`round.json` 会记录所属 `station_id`。另一工作站直接打开该轮次时会快速失败，避免双机数据被误写到一起。离线汇总时应保留两个工作站的顶层目录。

Quest 在上传成功前始终保留 `Application.persistentDataPath/Recordings` 中的本地 Pose/Meta；成功后写入 `.uploaded` 标记。重新配对时会继续上传未标记文件。

每次开始录制前，网页必须打开批次并选择轮次。`scripts/start-local.ps1` 默认加载 `backend/app/pointing_sentence_catalog.json` 的 31 条指代语料；需要旧数据集时可设置 `SIGNVR_SENTENCE_CATALOG` 指向 `backend/app/sentence_catalog.json`。每个轮次独立保存当前语料的位置和完成进度，可随时切换后继续；后端从当前轮次与句子的磁盘目录中原子预留下一个新的 `take_NNN`，并拒绝覆盖已经存在的 Pose、Meta 或相机文件。同一个 Take 的 Pose、Meta 和相机视频全部到齐后，该句自动标记完成。

旧版 300 条固定语料位于 `backend/app/sentence_catalog.json`，编号顺序为 `social 001–100`、`collaborate 101–180`、`spatial 181–220`、`question 221–250`、`stress 251–300`。指代录制的 31 条语料、目标标签、高亮对象和电闸顺序位于 `backend/app/pointing_sentence_catalog.json`。其中第 19–25 条是三个按钮的七个非空子集，第 26–31 条是三个电闸的六种排列。

## 默认录制流程

1. 网页打开录制批次目录，选择已有轮次或新建下一个 `round_NNN`。
2. 网页扫描并选择 Quest。
3. 网页选择一台外置相机；默认的 31 条指代语料会自动载入。重复句子通过目标标签区分。
4. 让操作台网页保持焦点。短按空格开始 2 秒倒计时，再同步启动浏览器视频和 Quest Pose。
5. 再短按空格结束并前进到下一句，浏览器上传 WebM，Quest 上传 Pose/Meta，主机把新句子和视角同步回 Quest。
6. 任意状态下长按空格 1.2 秒会显示进度并重置当前句；已经产生的 Take 保留为候选。
7. 左右方向键、网页上下句按钮、句子列表和编号跳转都可切句。场景随句子一起由主机同步到 Quest；Quest 内没有切句或切场景按钮。
8. 可按编号跳转、点击任意句、筛选完成状态或直接前往下一条未完成句；切换轮次时各自进度互不影响。

脚踏板作为连接主机电脑的 USB HID 键盘输入，不直接连接 Quest；单踏板映射 `Space`，短踩开始/结束、踩住 1.2 秒重录。网页失去焦点或光标停在输入框时不会接收快捷键。Quest 与主机必须处于同一局域网。VR 录制不使用 Touch 手柄，Quest 电源键和音量键也不作为应用内录制按键。

## 开发检查

```powershell
cd backend
.\.venv\Scripts\python.exe -m pytest -q

cd ..\frontend
pnpm lint
pnpm build
```
