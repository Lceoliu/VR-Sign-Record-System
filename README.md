# VR 手语录制主机

本项目是 SignVR 的本地主机端：FastAPI 负责 Quest 发现、录制控制、文件接收和实时转发，React 负责外置相机、句子队列、脚踏空格交互与操作台显示。

## 启动

首次安装：

```powershell
cd D:\SignVR\vr-sign-host\backend
py -3.12 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements.txt

cd D:\SignVR\vr-sign-host\frontend
pnpm install
pnpm build
```

之后运行：

```powershell
cd D:\SignVR\vr-sign-host
.\scripts\start-local.ps1
```

浏览器打开 `http://127.0.0.1:8000`。服务以前台方式运行，按 `Ctrl+C` 停止。

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
| 8000 | TCP/HTTP/WebSocket | React、控制 API、Pose/Meta/视频上传、Quest JPEG 预览 |
| 5005 | UDP | Quest 设备公告、命令 ACK、现有实时 Pose 数据 |
| 5006 | UDP | Quest 控制端口，接收发现、配对和录制命令 |

Quest 和主机必须位于同一可信局域网。Windows 主机需要允许 Python 的 TCP 8000 和 UDP 5005 入站；UDP 5006 位于 Quest 端，仅在 Unity Editor 本机模拟时需要 Windows 入站规则。Unity 项目允许明文 HTTP，仅用于这个受信任的本地录制网络。

## 数据目录

文件保存在：

```text
data/recordings/{batch_id}/{round_id}/
  round.json
  {sentence_id}/{take_id}/
  {take_id}.pose.jsonl
  {take_id}.meta.json
  {take_id}.camera.webm
```

Quest 在上传成功前始终保留 `Application.persistentDataPath/Recordings` 中的本地 Pose/Meta；成功后写入 `.uploaded` 标记。重新配对时会继续上传未标记文件。

每次开始录制前，网页必须打开批次并选择轮次。每个轮次独立保存 300 句的当前位置和完成进度，可随时切换后继续；后端从当前轮次与句子的磁盘目录中原子预留下一个新的 `take_NNN`，并拒绝覆盖已经存在的 Pose、Meta 或相机文件。同一个 Take 的 Pose、Meta 和相机视频全部到齐后，该句自动标记完成。

固定语料位于 `backend/app/sentence_catalog.json`，编号顺序为 `social 001–100`、`collaborate 101–180`、`spatial 181–220`、`question 221–250`、`stress 251–300`。

## 默认录制流程

1. 网页打开录制批次目录，选择已有轮次或新建下一个 `round_NNN`。
2. 网页扫描并选择 Quest。
3. 网页选择一台外置相机；固定的 300 句语料会自动载入。
4. 短按空格开始 2 秒倒计时，再同步启动浏览器视频和 Quest Pose。
5. 再短按空格结束并前进到下一句，浏览器上传 WebM，Quest 上传 Pose/Meta。
6. 任意状态下长按空格 1.2 秒会显示进度并重置当前句；已经产生的 Take 保留为候选。
7. 可按编号跳转、点击任意句、筛选完成状态或直接前往下一条未完成句；切换轮次时各自进度互不影响。

## 开发检查

```powershell
cd backend
.\.venv\Scripts\python.exe -m pytest -q

cd ..\frontend
pnpm lint
pnpm build
```
