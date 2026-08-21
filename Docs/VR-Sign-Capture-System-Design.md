# VR 手语录制系统与场景设计

- 文档状态：实现基线 v1.2
- 更新日期：2026-08-21
- 适用项目：SignVR / Quest 3
- 当前阶段：双工作站录制链路与首日数据采集已完成；当前代码基线同时包含录制系统、本地数据审核台和实时翻译 Demo 的前期场景资产
- 目标读者：Unity、Python、React、数据处理与现场采集团队

## 1. 目标与范围

本系统用于同步录制手语老师的外置摄像机视频与 Quest 3 动作数据。

输入与运行设备包括：

- 一台外置摄像机，负责录制老师的面部、身体外观和现场视频。
- 一台 Quest 3，负责身体及双手动作采集，并向老师提供实时镜像反馈。
- 一台主机，运行 React 网页操作台与 Python 录制中枢。
- 一个模拟空格键的脚踏开关。
- 主机与 Quest 之间的局域网。

系统必须保证每句话、每次录制和每次重录均可追溯，不覆盖旧 Take，并能够把视频、动作数据和录制元数据关联起来。

## 2. 已确认的产品决策

1. 镜像角色采用真正照镜子式的左右翻转。
2. 镜像只影响视觉显示，保存的原始 Meta Pose 不进行左右翻转。
3. 正常停止后，主机将已录 Take 保留为候选并把操作台推进到下一句；下一次短按开始下一句。若随后长按，则返回刚刚录完的句子等待重录。
4. 默认开始倒计时为 2 秒，并允许通过主机配置调整。
5. 长按脚踏键时显示环形进度条；进度满后，将当前句的录制流程重置到开始前状态，不自动重新开始。
6. 长按重录不会覆盖或删除旧 Take。
7. 每个 Take 使用不同文件名和唯一标识，并在元数据中保留可追溯状态。
8. 已完成的旧 Take 保留为候选 Take，之后可在网页操作台中选择最终采用版本。
9. 动作文件以现有导出的 Meta Pose 格式为权威格式。
10. 面部信息由外置摄像机视频负责。
11. 外置摄像机、脚踏键和录制协调由主机上的 React 网页前端与 Python 后端管理。
12. Quest 发现、控制确认和实时 Pose 使用 UDP；完整 Pose/Meta 与压缩预览使用 HTTP。
13. 网页监控端同时提供 30 FPS 原始 Meta 骨架和低负担 Quest 场景画面：React 用 Canvas 绘制骨架，Quest 另以 480 × 270、3 FPS、JPEG 35 上传侧后方画面。
14. 外置相机第一版由浏览器 `getUserMedia` 与 `MediaRecorder` 管理，不录制声音。
15. 脚踏长按阈值第一版固定为 1.2 秒，短按在开始与结束之间切换。
16. 正式录制不使用 Quest 控制器，只使用裸手追踪；脚踏键仍是高频录制操作的唯一入口。
17. 裸手不使用隐藏手势快捷键，避免手语动作被误识别；低频操作只通过可见的 Poke 按钮触发。
18. 动作回看在 Quest 的 VR 场景内完成，由真镜像机器人播放最近一次本地 Meta Pose Take。
19. 翻译文本固定显示在桌面上方的全息句子板；录制状态、REC、倒计时和长按重录反馈保留在 HMD 视野边缘，不再允许拖动。
20. 网页动作视图使用正面正交投影绘制骨架，取景框平滑跟随身体；监控目标是动作捕捉质量本身，环境与角色外观由外置相机画面承担。
21. `TouchScreenDevice_03` 的 `SignVRControls` 当前包含四个裸手 Poke 按钮：重播/退出重播、教程/关闭教程、退出/恢复场景（透视）和呼叫帮助。
22. 手部越界提示使用 Quest 实际的左右手追踪状态与置信度，再叠加相对 HMD 的保守安全工作区；不把 RGB 透视相机视锥误当成 Meta 手部追踪的精确硬边界。
23. 设备配对不再使用上传密钥鉴权；系统只部署在可信录制网络，并以持久化 `station_id` 防止两台工作站抢占同一 Quest。
24. Python 后端是批次、轮次、句子和 Take 状态的权威来源；外置相机采集仍由 React 页面中的 `MediaRecorder` 执行。

## 3. 当前 Unity 工程基线

截至 2026-08-18，当前工程已经具备以下基础：

- Meta XR Movement 动作源和实时角色重定向。
- 普通角色和 StylizedCharacterMirrored 镜像角色。
- Quest 本地 Meta Pose 录制器。
- Quest 到主机的 UDP 动作数据发送器。
- World Space Canvas 和基础调试录制 UI。
- 左右手数据源对象。

Unity 第一阶段实现已经完成：

- 新建独立 `Assets/Scenes/Recording.unity`，并将其设为唯一启用的构建场景。
- 复用现有 Meta 数据源、动作重定向、录制器和真镜像角色；原始角色只保留骨架数据，不再渲染。
- 新增显式录制状态机、2 秒倒计时、教师提示 UI、录制状态、Take 编号和长按重录进度环。
- 新增编辑器/开发包空格键调试入口；正式脚踏输入仍由后续 Python 主机控制通道提供。
- 本地 Pose 帧内容保持现有格式，文件改为按 Session、Sentence、Take 唯一命名，并写入可追溯元数据。
- 长按重录会保留已产生的 Take，并把正在录制的 Take 标记为 `interrupted_by_retake`。

第二阶段已完成：

- 新增 Quest UDP 5006 控制网关，支持发现、配对、开始、停止、重置与命令 ACK。
- 现有实时 Pose UDP 在配对后切换为主机单播；完整 Meta Pose 文件仍是权威数据。
- Quest 本地 Pose/Meta 通过 HTTP multipart 上传；上传成功前保留本地文件，重新配对后继续上传。
- Quest 独立侧后方摄像机以 GPU 回读压缩成低帧率 JPEG，通过 HTTP 与 WebSocket 进入 React。
- 新增 FastAPI 主机服务和 React 操作台，外置相机 WebM 与 Quest Take 使用同一 take_id。
- 正式脚踏语义已经由网页空格键状态机实现：短按开始/结束，长按 1.2 秒重录。
- 主机必须等待 Quest 的命令 ACK 才推进状态；超时或拒绝时回滚到上一状态，避免虚假录制成功。
- 已生成并安装 Quest 3 Android 开发包：`Builds/Android/SignVRRecording.apk`。
- 主机端提供 `scripts/setup-firewall.ps1`，只为 Python TCP 8000、UDP 5005 开放 `LocalSubnet` 入站。

第三阶段 Unity 录制体验已完成：

- `TeacherUI` 已改为中文录制状态、操作提示、全视野录制边框和长按重录百分比反馈。
- 引入动态多图集 `Noto Sans SC` TMP 字体资产，覆盖中文提示词和运行时主机下发文本。
- `TeacherUI` 已挂到 `CenterEyeAnchor`，只承载 REC、录制状态、倒计时、长按反馈和追踪告警；翻译文本已迁移到桌面全息句子板，旧拖动把手和归位功能已移除。
- 复用用户摆放的 `TouchScreenDevice_03`，`SignVRControls` 当前连接重播、教程、透视和求助四个 Meta Poke 按钮；旧 `VRDeskToolPanel` 已在第四阶段删除。
- 最近一次 Take 在 Quest 本地读取 `.pose.jsonl`，临时接管现有 `CharacterRetargeter`；既有 `MirrorTransforms` 继续输出真正镜像角色。
- 新增可重复执行的录制场景光照准备流程，生成混合主光、两盏烘焙顶灯、Light Probe 网格、Reflection Probe、URP Volume、Lighting Settings 与一组低饱和环境材质模板。
- 新增 HMD 内手部追踪边界提示：接近边界和瞬时低置信度保持静默，真实越界使用低强度琥珀色方向提示，持续丢失追踪则使用四边红色脉冲和明确中文提示。
- 新增 `Reviewing` 状态；回看按钮、网页停止命令和脚踏短按均可退出回看并恢复实时身体驱动，Quest 裸手显示和 Poke 交互对象始终保持启用。
- Quest 预览使用运行时创建的独立侧后方摄像机，固定 480 × 270、3 FPS、JPEG 35、FOV 60，同时包含角色和桌面触屏。
- 编辑器脚本编译错误为 0，短时 Play Mode 验证未发现 SignVR 运行时异常。
- 2026-08-18 已重新构建并安装 Android Development APK；Quest 3 上 OpenXR、HMD 和双手骨架初始化成功，应用进程稳定运行。
- 2026-08-18 的阶段验收曾持续收到 640 × 360 预览帧和 Pose 包；2026-08-19 发布基线随后把预览降为当前的 480 × 270、3 FPS、JPEG 35。

第四阶段场景整理与网页动作视图已完成：

- `LeftEyeAnchor` 的 `MainCamera` 标签已清除，场景中只剩 `CenterEyeAnchor` 一个主相机，`Camera.main` 不再有歧义。
- 已停用的 `VRDeskToolPanel` 及其子树从场景中删除，低频操作入口统一放在 `TouchScreenDevice_03/ScreenArea/SignVRControls` 的四个 Poke 按钮中。
- 网页监控端的 Quest 画面改为骨架实时渲染：主机转发 30 FPS 的 Meta 骨架包，React 在 Canvas 上做正面正交投影。Unity 端无需改动，`MetaBodyMotionStreamer` 原有的 UDP 数据直接复用。
- 主机新增 `/ws/pose/{device_id}`，`UdpService` 按源 IP 解析 `device_id` 后转发 `skeleton`、`frame`、`status` 三类包，并缓存最近一份拓扑供中途连入的客户端使用。
- 后端 6 项 pytest、前端 lint 与 build 均通过；已用真实 UDP 包与 WebSocket 客户端完成端到端验证。

首日采集已经证明双工作站可以完成批次、轮次、视频与 Pose 落盘。当前仍需持续验收的项目：

- 骨架视图在真机长时间录制下的帧率与延迟表现，以及主机 CPU 占用。
- 长时间连续录制后的 Quest 温度、电量、画面稳定性和自动补传表现。
- 外置相机的最终型号、720p 帧率、驱动稳定性和现场 USB 带宽。
- Quest 3 真机上的四个桌面 Poke 按钮触达范围、按钮尺寸和 HMD 文本物理尺寸。
- 较长 Take 在 Quest 上的载入时延、内存占用与回看帧稳定性。

## 4. 总体架构

~~~mermaid
flowchart LR
    K["脚踏空格键"] --> P["Python 录制中枢<br/>唯一录制状态源"]
    R["React 网页操作台"] <-->|"WebSocket"| P
    L["句子清单"] --> P
    R --> C["浏览器外置摄像机"]
    P <-->|"可靠控制命令与确认"| Q["Quest Unity 客户端"]
    Q --> M["Meta Pose 采集"]
    M -->|"UDP 实时动作帧"| P
    Q -->|"HTTP Pose/Meta 与 JPEG"| P
    P --> D["Session / Sentence / Take 文件"]
    Q --> V["句子面板 / 镜像角色 / 状态 UI"]
~~~

### 4.1 Python 录制中枢

Python 是录制上下文和流程状态的唯一权威来源，负责：

- 读取脚踏键输入并区分短按与长按。
- 管理 Session、句子和 Take。
- 生成统一 Take 与计划开始时刻，接收浏览器相机文件。
- 向 Quest 发送准备、开始、停止和重置命令。
- 接收 Quest 状态确认和 Meta Pose 数据。
- 保存视频、动作和元数据。
- 把实时状态推送给 React。
- 维护候选 Take 列表和最终选择结果。

浏览器不是状态权威，但当前外置相机由 React 的 `MediaRecorder` 和 `setTimeout` 执行，因此浏览器冻结、切后台或定时器漂移仍会影响视频实际起点；这是后续升级需要消除的限制。

### 4.2 React 网页操作台

React 负责：

- 创建和选择批次、轮次，并恢复每轮独立的当前句位置。
- 导入、显示和定位句子清单。
- 显示相机、Quest、网络和录制状态。
- 显示每句话的候选 Take。
- 通过 `/review` 回看视频与 Pose，并标记视频问题或句子问题。
- 提供人工开始、停止、重置和故障处理入口。
- 使用浏览器 `getUserMedia` 与 `MediaRecorder` 采集无声 WebM，并上传到后端预留的 Take。

### 4.3 Quest Unity 客户端

Quest 负责：

- 接收主机发送的当前句子和录制命令。
- 显示固定方向的句子面板。
- 显示真正左右翻转的实时镜像角色。
- 显示倒计时、录制、保存、重置和异常状态。
- 按现有 Meta Pose 格式采集动作。
- 在录制期间向主机发送带 Take 标识的动作帧。
- 保留本地动作文件作为网络异常时的备份。
- 对收到的配对、开始、停止、重置和提示命令返回 ACK；完整文件是否到达由主机上传接口和磁盘状态判断。

## 5. 录制状态机

主流程如下：

~~~mermaid
stateDiagram-v2
    [*] --> Disconnected
    Disconnected --> Ready: 载入句子
    Ready --> Countdown: 开始 Take
    Countdown --> Recording: 本地倒计时结束
    Countdown --> Ready: 取消
    Recording --> Finalizing: 停止
    Finalizing --> Completed: Pose/Meta 已关闭
    Ready --> Reviewing: 回看最近 Take
    Completed --> Reviewing: 回看最近 Take
    Reviewing --> Ready: 退出回看
    Reviewing --> Completed: 退出回看
    Ready --> Resetting: 长按完成
    Countdown --> Resetting: 长按完成
    Recording --> Resetting: 长按完成
    Finalizing --> Resetting: 长按完成
    Completed --> Resetting: 长按完成
    Resetting --> Ready: 当前句重置完成
~~~

状态含义：

- Disconnected：Quest、相机或主机服务尚未建立有效连接。
- Ready：当前句已经显示，等待短按。
- Countdown：显示默认 2 秒倒计时。
- Recording：视频和 Meta Pose 正在录制。
- Finalizing：Quest 正在关闭 Pose/Meta；主机相机上传独立完成。
- Completed：Quest 当前句已有至少一个本地可追溯 Take；主机正常停止后会把操作台推进到下一句。
- Reviewing：VR 内播放最近一次本地 Pose Take。
- Resetting：处理长按重置，安全终止当前工作后返回 Ready。
- Error：显示明确原因，禁止把未真实录制的状态显示为成功。

网络健康状态与录制流程状态相互独立。长按可以重置当前句的录制流程，但不能掩盖 Quest 断线、相机不可用或磁盘写入失败。

## 6. 脚踏键交互

### 6.1 输入判定

脚踏键按下后进入 PressedPending：

- 在长按阈值前松开：执行一次短按。
- 保持按下：显示环形重录进度。
- 进度满：执行一次 ResetCurrentSentence。
- 长按已经触发后再松开：不得继续触发短按。
- 长按阈值尚未最终确认，需要通过老师实机试踩确定。

### 6.2 短按行为

- Ready：进入 2 秒倒计时，然后开始当前句。
- Countdown：默认不响应额外短按，避免重复触发。
- Recording：停止当前 Take 并进入 Finalizing。
- Finalizing：不接受新的开始命令。
- Completed：下一次主机开始命令会载入操作台当前句并进入倒计时。
- Error 或 Disconnected：不开始录制。

### 6.3 长按行为

长按在所有录制流程状态下都可以发起：

- 立即锁定当前 sentence_id，不切换句子。
- 显示环形进度与“按住以重新录制”。
- 进度满后进入 Resetting。
- 若正在倒计时，则取消倒计时。
- 若正在录制，则安全停止相机和 Quest 录制。
- 若正在保存，则先完成文件关闭。
- 保留已经产生的文件和 Take 记录。
- 清空当前 UI 计时和录制提示。
- 返回当前句 Ready，不自动开始下一次录制。

## 7. Take 保留与追溯

### 7.1 不可覆盖原则

每次进入 Recording 都创建新的 take_id 和递增的 take_index。

任何情况下都不使用新文件覆盖旧文件，包括：

- 正常重录。
- 录制中长按。
- 保存中长按。
- 网络异常后的再次录制。
- 相机失败而 Quest 数据已经产生。
- Quest 失败而视频已经产生。

### 7.2 两组状态

每个 Take 同时保存采集状态和审核状态。

采集状态 capture_status：

- completed：视频与动作均完成。
- interrupted_by_retake：因长按重录中断。
- partial：只得到视频或只得到动作。
- failed：未形成可用数据，但保留错误记录。

审核状态 review_status：

- candidate：候选 Take，默认状态。
- selected：最终采用。
- rejected：人工确认不采用。

长按重录不会自动把旧的完整 Take 标记为 rejected。旧 Take 继续保持 candidate，直到网页操作台中的人工选择发生变化。

### 7.3 文件组织

建议目录：

~~~text
Sessions/
  {session_id}/
    session.json
    {sentence_id}/
      sentence.json
      {sentence_id}__take-001__{take_id}.video.mp4
      {sentence_id}__take-001__{take_id}.pose.jsonl
      {sentence_id}__take-001__{take_id}.meta.json
      {sentence_id}__take-002__{take_id}.video.mp4
      {sentence_id}__take-002__{take_id}.pose.jsonl
      {sentence_id}__take-002__{take_id}.meta.json
~~~

文件名不包含完整句子文本，避免过长文件名、非法字符和中文路径兼容问题。

每个 meta.json 至少记录：

- session_id
- sentence_id
- sentence_text 或句子清单引用
- take_id
- take_index
- capture_status
- review_status
- reset_reason
- host_start_time
- host_stop_time
- quest_first_pose_time
- quest_last_pose_time
- camera_first_frame_time
- camera_last_frame_time
- pose_frame_count
- video_file
- pose_file
- Quest 应用版本
- Python 服务版本
- 错误或丢帧说明

## 8. Meta Pose 数据约定

现有导出的 Meta Pose 是动作 Payload 的权威格式。

设计要求：

- 原始 Pose 不因镜像显示而修改。
- 本地备份和主机保存使用同一种 Pose Schema。
- UDP 包外层可以增加 Session、Sentence、Take、帧序号和时钟信息。
- 不另行设计一套与现有导出不兼容的骨架字段。
- 正式实现前，必须用一份真实导出样本冻结 Schema。
- 当前 UDP 发送内容与本地导出内容存在差异，实现时必须统一，不能默认它们已经等价。

面部表情、嘴型和现场画面由外置摄像机视频保存，不进入 Meta Pose 文件。

## 9. 跨设备同步

主机是时序协调者，当前协议版本为 3。实际流程如下：

1. Python 在当前批次、轮次和句子目录中原子预留新的 `take_NNN`。
2. Python 生成 `command_id` 和绝对计划时间 `start_at_unix_ms`，把同一 Take 上下文返回 React，并经 UDP 向 Quest 发送开始命令。
3. Quest 验证 Pose 是否可用，载入句子并返回命令 ACK。
4. React 按绝对计划时间启动浏览器 `MediaRecorder`。
5. Quest 当前不使用绝对时间，而是在收到命令后执行 `countdown_seconds` 的本地倒计时，再开始 Pose。
6. 停止时，React 先关闭相机，Python 再发送 Quest 停止命令；两端分别上传 WebM 与 Pose/Meta。
7. 主机目录同时具有 `.camera.webm`、`.pose.jsonl` 和 `.meta.json` 后，该 Take 才被判定完整，该句才计入完成进度。

控制命令具备 `command_id`、ACK、超时和主机状态回滚，但尚未实现命令去重。浏览器与 Quest 的启动依据不同，因此视频和 Pose 之间仍存在至少一个网络往返与定时器调度量级的起点误差；Meta 也尚未记录相机真实第一帧时间。后续同步升级应统一使用可校准的时钟或在两端记录可对齐的真实时间戳。

## 10. VR 场景设计

### 10.1 空间

场景采用稳定、安静、低干扰的训练室风格：

- 固定地面和稳定地平线。
- 不使用移动摄像机、环境动画或强烈粒子效果。
- 使用中性低饱和背景。
- 地面提供与外置相机取景一致的站位标记。
- 提供一次性的朝向校准或重新居中入口。

### 10.2 镜像角色

- 复用现有 StylizedCharacterMirrored。
- 对角色显示层进行真正的左右翻转。
- 保持一比一比例和正确落地位置。
- 增加简洁镜框、浅灰镜面背景和接触阴影。
- 不使用真实反射摄像机作为第一版方案。
- Prompt、状态文字和进度环不得放入负缩放镜像层级。
- 保存动作时始终使用未镜像的原始 Meta Pose。

### 10.3 句子面板

- 固定在 `Table_01A` 桌面上方，采用低饱和深青半透明面板、浅色中文和细线框，形成克制的全息终端效果。
- 句子板不跟随头显、不提供拖动入口；站回录制位置即可像查看桌面显示器一样阅读。
- 使用普通深度测试，手和场景物体可以自然遮挡它；不使用 HMD Always-On-Top 材质。
- 支持中文与两到三行长句。
- 字号应保证不前倾也能清楚阅读。
- 使用动态多图集 `SignVRChinese SDF`，源字体为 OFL 授权的 Noto Sans SC。
- 句子板使用 `Overlay UI` 层以排除网页预览，但其 Canvas 仍是普通 World Space 渲染。
- 录制状态放在 HMD 顶部左侧小区域；REC 使用红点、计时器和短操作提示，不使用覆盖中央视野的大面板。

### 10.4 状态 UI

- Ready：准备好，短按开始。
- Countdown：显示 2、1，并明确即将开始。
- Recording：红点、录制中文字和计时器。
- Finalizing：正在保存，请稍候。
- Completed：已保存；短按下一句，长按重录。
- Reviewing：VR 动作回看、加载/播放进度、暂停/继续/重播和退出。
- Resetting：正在重置当前句。
- Error：全宽明显提示，并显示可操作的故障原因。

技术诊断信息，例如 IP、端口、FPS、丢包和帧数，仅出现在可隐藏的 Diagnostics 层，不占用老师的主要视野。

### 10.5 长按进度环

- 作为 HMD World Space HUD 元素在长按期间短暂进入中央视野，平时完全隐藏。
- 使用克制的橙色或琥珀色。
- 文案为“继续按住以重新录制”。
- 未满松开时立即取消。
- 满圈后显示一次明确的完成反馈。
- 进度环表示长按手势完成度，不表示相机和文件已经完成关闭。

### 10.6 裸手工具与 VR 回看

- 正式操作不显示控制器射线，不依赖 XRI Controller 输入。
- 场景复用 Meta Interaction SDK 的左右手 PokeInteractor；所有低频操作都有可见、可触碰的大尺寸按钮。
- 用户摆放的 `TouchScreenDevice_03` 是唯一低频工具入口；录制倒计时开始后按钮自动不可用。
- 第一个按钮常态为“重播动作”，进入回看后原位改为“退出重播”；不另设一套回看控制面板。
- 第二个按钮位于回看按钮下方，切换“查看教程/关闭教程”；回看期间隐藏，避免操作冲突。
- 回看只读取最近一次本地候选 Take，不修改、不删除、不重新写入动作文件。
- 重新启动应用或主机重新下发同一句后，从 `persistentDataPath/Recordings/<session>/<sentence>` 的 meta 文件恢复最新可追溯 Take，并把下一次 take_index 推进到它之后。
- 播放时临时停用实时 `CharacterRetargeter.Update`，逐帧将保存的 Meta NativeTransform 输入同一重定向器；结束后恢复实时身体追踪。
- 载入进度、文件缺失、JSON 损坏、关节数不匹配和重定向失败均在 HMD 内给出可操作的中文结果，不再静默失败。
- 回看期间不禁用 Meta 左右手追踪、手部渲染或 PokeInteractor，因此老师本人的手势与退出按钮始终可见、可用。
- 真镜像角色仍由现有 `MirrorTransforms` 从被驱动角色复制，因此实时与回看保持相同的左右翻转规则。
- 新手教程在 VR 内分五步自动播放，也允许裸手点击“下一步”或“关闭教程”。

### 10.7 网页动作视图

网页监控端同时显示实时骨架和低帧率 Quest 场景画面，两条链路用途不同。

- `MetaBodyMotionStreamer` 以 30 FPS 向主机单播原始 Meta 骨架：拓扑包含 `joint_names` 与 `parent_indices`，每帧包含 `positions`、`valid` 与 `confidence`。
- 主机 `UdpService` 按源 IP 解析 `device_id` 后转发到 `RealtimeHub`，再经 `/ws/pose/{device_id}` 推送给 React；拓扑包只在数秒一次，因此主机缓存最近一份，供中途连入的客户端立即取用。
- React 用 Canvas 做正面正交投影：屏幕横轴取骨架 X、纵轴取骨架 Y，取景框由有效关节包围盒指数平滑得出，避免画面抖动。
- 无效或低置信度关节以灰色降透明度绘制，操作员可以直接看出丢手或遮挡。
- 骨架链路不占用 Quest 的 GPU 回读与 JPEG 编码，适合判断动作捕捉状态。

- `QuestPreviewStreamer` 运行时创建不参与 XR 的 URP 摄像机，从角色侧后方同时取到主角和桌面触屏，以 480 × 270、3 FPS、JPEG 35 上传到 `/api/devices/{id}/preview-frame`，React 通过 `/ws/preview/{id}` 显示。
- 当前每帧仍是独立 HTTP POST，并依赖 `WaitForEndOfFrame` 与 GPU Readback；它是操作员的低频环境确认画面，不作为动作质量或同步的权威来源。

### 10.8 光照、材质与烘焙工作流

- 用户导入或摆放的房间、墙面、地面和固定家具统一放在 `Environment/ImportedEnvironment` 下。
- `Environment/SignVR Lighting` 包含一盏 Mixed Directional Key、两盏 Baked Rectangle Ceiling Softbox、围绕镜像角色工作区生成的 8 × 3 × 7 Light Probe 网格、一枚 128 分辨率 Baked Reflection Probe 和全局 URP Volume。
- 旧的三盏 Directional Light 已禁用，旧 `Environment/LightProbe` 已停用；原场景 Spot Light 与 DownLight 改为 Baked，运行时只保留主方向光阴影。
- 环境材质模板位于 `Assets/Materials/SignVR Environment`，提供 Wall、Ceiling、Floor、Plastic、Metal 和 ScreenOff 六类 URP Lit 基线材质；导入素材不强制替换原材质。
- `SignVR/Rendering/Repair Recording Scene for URP` 会扫描当前场景实际使用的 Japan Office 材质；对 HDRP Lit、Layered Lit 和旧 HDRP Shader Graph 材质读取仍保存在材质文件中的 `_BaseColorMap`、`_MaskMap`、`_NormalMap`、颜色、金属度、粗糙度、透明与裁切参数，再映射到 URP Lit。当前场景共检查 32 个 Japan Office 材质，其中 19 个完成修复。
- 录制发布版曾使用 URP Forward、4× MSAA、15 米主光阴影、2 级 Cascade，并关闭 HDR、Depth/Opaque Texture、额外灯阴影和 SSAO。
- 2026-08-21 工作区为实时翻译 Demo 改成全局 HDR、Depth/Opaque Texture、SSAO、4096 阴影、80 米阴影距离、4 级 Cascade，并关闭 MSAA。这些设置也会影响唯一构建场景 `Recording`，尚未完成 Quest 性能与稳定性回归，不能视为新的录制发布参数。
- `SignVR/Rendering/Bake Repaired Recording Scene` 会再次执行确定性修复，将 `ImportedEnvironment` 标记为 GI、遮挡剔除、批处理和 Reflection Probe 静态对象，清除旧烘焙并启动 Progressive CPU Lightmapper。
- 最终 Lighting Settings 为 24 texels/m、1024 最大图集、3 次反弹、64 Direct Samples、256 Indirect Samples、128 Environment Samples，并启用 1.1 米烘焙 AO。
- 2026-08-18 完成当前房间的正式烘焙：生成 18 组 1024 Directional Lightmap、LightingData 和 2 枚 Reflection Probe 资源；源 Lightmap 文件合计约 148.5 MB，Android 构建使用平台纹理压缩后的体积需在真机包中继续监测。
- 日间外景使用复制到 `Assets/Materials/M_SignVR_DaylightSkybox.mat` 的 Meta `SkyboxGradient`，以低饱和蓝灰天空、浅灰地平线和暖灰地面构成办公室窗外环境；场景继续使用既有 Trilight 环境光参数。
- `Outside_WorkSpace` 与 `Outside_Entrance` 分别使用 `M_SignVR_Outside_WorkSpace` 和 `M_SignVR_Outside_Entrance`，读取 Japan Office 原有两枚 EXR Cubemap。专用 `SignVR/URP Exterior Cubemap` 为双面 Unlit、低强度输出，不投射或接收阴影，不采样 Light/Reflection Probe，不写入烘焙 GI。
- `SignVR/Rendering/Install Daylight Sky and Exteriors` 可重复恢复天空盒、两份外景材质及 Renderer 设置；`SignVR/Rendering/Bake Reflection Probes Only` 只更新 Baked Reflection Probe，不清除或重烘焙 Lightmap。
- 动态角色、双手、HMD UI 和交互按钮不得放入 `ImportedEnvironment`，它们使用 Light Probe 或实时主光，不进入静态 Lightmap。
- Japan Office 原导入脚本仍保留 `Tools/UTJOffice/InitJapanOfficeSetup` 手动菜单；当项目已经明确配置 URP 时不再在每次脚本域重载后弹出切换 HDRP/Build Scenes 的询问。

### 10.10 聋人被试的沟通通道

戴上头显后，聋人失去了听人默认拥有的旁路通道：旁边人喊一句就能解决的问题，
对聋人不存在。摘下头显又意味着中断录制、丢失站位、重新校准。因此：

- 任何“出问题就摘头显问老师”的流程都视为失败设计。
- `TouchScreenDevice_03` 上的四个 Poke 按钮构成完整的自救入口：重播动作、
  查看教程、退出/恢复场景（透视）、呼叫帮助。呼叫帮助在所有状态下都可用，
  包括录制中；其余按钮在录制与倒计时期间隐藏，避免误触。
- 脚踏没有可听见的回执，因此踩下必须产生独立于状态变化的视觉脉冲，否则老师
  会因为不确定是否踩到而反复试踩。

仍待补的通道：操作员到头显的预设消息、手语化的提示（复用本系统录制的手语
Take 由镜像角色播放）、站位偏移守护、电量预警。

### 10.9 手部可捕捉区域提示

- `HandCaptureBoundaryMonitor` 直接读取左右 `OVRHand` 的 `IsTracked`、`IsDataValid`、`IsDataHighConfidence` 和 `HandConfidence`，因此真实丢手或遮挡造成的低置信度优先于几何估计。
- 几何预警使用 HMD 局部坐标中的保守工作区，默认深度 0.18–1.20 米、水平半角 58°、上方半角 50°、下方半角 58°。接近边界不显示提示；真正越界才出现强度 0.14–0.38 的低亮度琥珀色方向光晕，持续 0.65 秒后才显示文字。
- 追踪无效持续 0.35 秒才判定为真正丢失；判定后立即显示强红色四边脉冲，并在 0.12 秒内显示左手、右手或双手的明确文字。恢复稳定 0.35 秒后隐藏，短暂遮挡和手部交叠不会频繁打断老师。
- 这些数值是便于现场标定的安全区，不是 Quest 内部手部追踪算法公开的精确相机联合视锥。选中 `_Recording` 上的组件时，Scene 视图会绘制该安全区线框。
- Quest 3/3S 可以通过 MRUK `PassthroughCameraAccess` 取得左右 RGB 相机的内参、位姿并完成视口/射线换算，但这代表透视 RGB 画面范围，不等同于内部手部追踪的多相机有效体积；正式提示因此不申请 Headset Camera 权限，也不启动额外相机流。

## 11. Unity 场景装配契约

`Recording` 场景的实际根对象如下，共 7 个：

- `[BuildingBlock] Camera Rig`
  - Meta XR Rig。`TrackingSpace/CenterEyeAnchor` 是唯一带 `MainCamera` 标签的相机；`LeftEyeAnchor` 与 `RightEyeAnchor` 必须保持 `Untagged`，否则 `Camera.main` 的返回值不确定。
  - `CenterEyeAnchor/TeacherUI`：`Overlay UI` 层的 HMD 固定 World Space Canvas，承载倒计时、REC、录制状态、视野边框、长按进度和错误提示。它明确不挂 `OVROverlayCanvas`，避免 Quest 上整层 UI 发灰。
  - `CenterEyeAnchor/VRTutorialPanel`：VR 内新手教程与导航按钮。
  - `[BuildingBlock] Interaction` 下为左右手追踪与 Poke 交互器。
- `_Recording`
  - 唯一的录制系统入口，靠 `RecordingCoordinator` 的 `[DefaultExecutionOrder(-10000)]` 保证初始化顺序。
  - 承载 `RecordingCoordinator`、`QuestDeviceGateway`、`QuestTakeUploader`、`QuestPreviewStreamer`、`RecordingReplayController`、`RecordingTutorialController`、`RecordingTouchscreenPresenter`、`HandCaptureBoundaryMonitor`、`RecordingDebugInput`。
- `Objects`
  - `StylizedCharacter`：被 `CharacterRetargeter` 驱动的骨架源，只提供数据不渲染。其 `Animator` 没有 Controller，由重定向器直接写骨骼，属预期状态。
  - `MotionRecorder`：`MetaBodyMotionRecorder` 与 `MetaBodyMotionStreamer`。
- `MirroredObjects`
  - `StylizedCharacterMirrored`：由 `MirrorTransforms` 从被驱动角色复制并做左右翻转，是老师看到的镜像角色。
- `Environment`
  - `SignVR Lighting`：混合主光、两盏烘焙顶灯、Light Probe 网格、Reflection Probe 与全局 URP Volume。
  - `ImportedEnvironment`：房间、墙地面与固定家具，烘焙时统一标记静态。
  - `DeskPromptCanvas`：桌面全息翻译文本，普通 World Space Canvas、正常深度测试，使用 `Overlay UI` 层以排除网页预览。
  - `TouchScreenDevice_03/ScreenArea/SignVRControls`：桌面触屏上的 `ReplayToggle` 与 `TutorialToggle` 两个 Poke 按钮。
  - 旧的三盏 Directional Light 与 `LightProbe` 仍在场景中，但 Light 组件已 `enabled = false`。
- `UI`
  - Meta Movement SDK 示例菜单，整棵树 `isActive = false`，不参与录制流程。
- `EventSystem`

尚未建立的是 `Diagnostics` 层：IP、端口、FPS、丢包和帧数目前没有可隐藏的集中展示入口。

场景引用通过 Inspector 显式连接，不依赖多层运行时 Find。运行时状态放在状态机中，不把可变录制状态保存在 ScriptableObject。网页预览摄像机不是场景对象，由 `QuestPreviewStreamer` 在运行时创建并在禁用时销毁。

## 12. 主机网页与 Python 服务

### 12.1 React 页面实现

第一版位于 `D:/SignVR/vr-sign-host/frontend`，已经实现：

- Quest 扫描、选择与连接状态。
- 单台外置相机选择、720p 预览与 WebM 录制。
- UTF-8 逐行句子文本导入、当前句显示与句子队列。
- Quest 30 FPS 骨架实时动作视图，显示有效关节数与实际帧率。
- 2 秒倒计时、录制计时、候选 Take 和三类文件接收状态。
- 人工开始、结束、重新录制按钮。
- 空格键短按与 1.2 秒长按进度反馈。

### 12.2 Python 模块实现

第一版位于 `D:/SignVR/vr-sign-host/backend`，保持小型明确模块：

- `RecordingService`：Session、句子、Take 与命令状态。
- `UdpService`：5005 设备公告、命令 ACK 与现有 Pose 包入口。
- `DeviceRegistry`：设备在线状态、选择、工作站绑定与接收计数；固定兼容 token 不承担安全鉴权。
- `RecordingRepository`：Pose、Meta 与相机视频分层落盘。
- `RealtimeHub`：状态事件、Quest 骨架包与 JPEG 预览的 WebSocket 转发，并缓存最近一份骨架拓扑。
- `main`：HTTP API、上传入口与 React 静态文件服务。
- `ReviewRepository`：扫描既有老师/轮次/句子/Take 数据，并原子保存仅含问题项的 `review_labels.json`。

不在第一版引入大型消息总线、复杂依赖注入框架或分布式服务。

## 13. 关键验收条件

系统实现后至少满足：

- 2 秒倒计时可见且主机与 Quest 状态一致。
- 短按不会同时被识别成长按。
- 长按进度满只触发一次重置。
- 任意录制流程状态下，长按都能最终回到当前句开始前状态。
- 长按不会切换句子。
- 长按不会覆盖或删除旧 Take。
- 每个 Take 的视频、Pose 和元数据可通过 take_id 关联。
- 同一句话的多个候选 Take 文件名均不同。
- Quest Meta 保留 completed 与中断原因，主机操作台正确区分三文件齐全的 complete 与待补文件的 candidate。
- 本地审核台只记录视频问题和句子问题，不修改原始 Take；候选 Take 的 selected/rejected 工作流尚未实现。
- 镜像角色左右翻转，但原始 Meta Pose 不翻转。
- 中文句子无缺字、镜像或裁切。
- 不拿控制器时，左右手食指可点击所有低频工具按钮。
- 桌面句子板不遮挡镜像角色头部和手部主要动作区域；HMD REC 与状态仅占视野边缘。
- 短暂低置信度和接近边界不触发告警；真实越界出现弱方向提示，持续追踪丢失出现能立即察觉的四边红色脉冲和中文提示。
- 手部重新稳定回到安全区后，提示不会闪烁并能在 0.35 秒内自动消失。
- 完成录制后，VR 回看可在镜像机器人上播放最近一次 Take；同一按钮切换为“退出重播”，退出后实时身体驱动恢复。
- 回看期间老师本人的 Quest 手部追踪、手部显示和 Poke 交互保持可用。
- 网页动作视图以 30 FPS 绘制骨架；侧后方场景画面以 480 × 270、3 FPS 显示角色与桌面触屏。
- 回看期间不得开始新录制；任意时刻长按脚踏键仍可重置当前句。
- 网络断开时不得继续显示虚假的录制成功状态。
- Quest UDP 丢包或断线时，本地备份仍可用于恢复。

## 14. 尚待确认

以下内容不阻塞当前纵向链路，但正式采集前仍需确认：

- 外置相机型号、驱动方式及 Python 控制接口。
- Session 中需要记录的老师身份字段。
- 视频与 Pose 的统一时钟、真实第一帧时间及后处理对齐字段。
- 候选 Take 的最终选择是否允许撤销。
- Quest 的显式解绑/更换工作站流程。
- 实时翻译 Demo 与录制系统是否继续共用同一 URP/XR 全局配置。

## 15. 变更记录

### v1.2 - 2026-08-21

- 同步协议 v3、持久化 `station_id`、可信网络无上传密钥、双工作站批次/轮次和 300 句进度实现。
- 同步正常停止自动推进下一句、长按返回刚录句子、主机预留 `take_NNN` 和三文件齐全才判定完成的实际语义。
- 同步 30 FPS 骨架加 480 × 270、3 FPS 侧后方 JPEG 的双监控链路，以及桌面四个裸手 Poke 按钮。
- 记录浏览器 `MediaRecorder`、Quest 相对倒计时、缺少真实第一帧时间和命令去重的现有限制。
- 记录本地 `/review` 审核台与实时翻译 Demo/全局高画质设置进入当前代码基线。

### v1.1 - 2026-08-18

面向聋人被试的第一批交互改造。核心约束：戴上头显后，聋人与外界的沟通通道
被完全切断——看不见翻译老师的手，听不见任何提示，摘头显又会丢失站位与校准。
因此所有异常都必须能在 VR 内自救。

- 新增彩色透视切换（`RecordingPassthroughController`）。桌子、触屏与灯光
  保留，否则老师既看不见也按不到把场景切回来的按钮；虚拟房间与镜像角色隐藏。
  透视等同暂停：录制中切入会先安全停止当前 Take，暂停期间不允许开始新录制。
  按钮文案在“退出场景”与“恢复场景”之间切换。
- 新增求助按钮（`RecordingHelpController`）。Quest 经 UDP `signal` 包置位，
  操作台弹出横幅并可确认清除。这是聋人在头显内唯一的呼救通道。
- 新增脚踏视觉回执。`pedal` 命令把踩下、长按进度、松开镜像进头显：踩下瞬间
  无条件闪一次全视野边框，长按进度环在 HMD 内填充。此前长按进度只存在于
  操作台浏览器，Quest 直到长按完成才收到 `reset_take`。
- 手部边界提示升级为追踪质量框架：
  - 新增方向性边缘光晕（`RecordingBoundaryGlow`），手往哪边逼近边界，对应
    视野边缘就渐强变红。强度随剩余余量连续变化，读起来是距离而非开关告警。
  - 提示可由操作台 `/api/guidance/{enabled}` 远程开关，用于同一位老师分别
    录制开启与关闭提示的两组数据。追踪质量在两种情况下都照常采集。
  - 每帧写入 `hand_capture`（每只手的 tracked、confidence、是否在安全区、
    归一化边界余量），整条 Take 的 `hand_capture_quality` 汇总写入 meta.json，
    操作台直接显示洁净率，操作员据此决定是否重录。
- 启用 Insight Passthrough 支持并在 AndroidManifest 声明 PASSTHROUGH feature。
- 手部追踪频率由 `LOW` 改为 `HIGH`。手语是快速精细动作，LOW 频率会直接压低
  采集质量；JPEG 预览关闭后腾出的功耗预算正好用于此处。
- `TeacherUI` 保持 `CenterEyeAnchor` 下的 World Space Canvas，不使用曾导致 Quest UI
  发灰的 `OVROverlayCanvas`；翻译文本迁移到桌面全息句子板，HMD 只保留状态反馈。
- 降低边界告警干扰：接近边界和短暂低置信度静默，真实越界弱提示，持续丢失
  追踪才升级为四边强红脉冲。
- 修复回放跨应用重启/重复下发句子后丢失内存中 Take 引用的问题；回放现在从本地
  meta 恢复最新 Take，并显示载入进度与明确失败原因。倒计时在进入录制后显示一次
  “开始”并彻底清空，不再残留旧数字。

### v1.0 - 2026-08-18

- 网页监控端的 Quest 画面由 JPEG 推流改为骨架实时渲染。主机 `UdpService` 转发 `skeleton`、`frame`、`status` 三类包到新增的 `/ws/pose/{device_id}`，`RealtimeHub` 缓存最近一份拓扑；React 新增 `SkeletonPanel`，用 Canvas 做正面正交投影并标注有效关节数与实际帧率。
- 原 JPEG 预览链路在 URP + XR 下依赖 `WaitForEndOfFrame` 触发离屏渲染，时序不可靠，是画面显示异常的根因；该链路代码保留但网页不再订阅，重启用时需改写为 `Camera.SubmitRenderRequest`。
- 清除 `LeftEyeAnchor` 的 `MainCamera` 标签，消除 `Camera.main` 的双候选歧义。
- 删除已停用的 `VRDeskToolPanel` 子树。
- 按场景实际结构重写第 11 节装配契约，并同步第 10.7 节；`Diagnostics` 层仍未建立，保留为待办。
- 后端 6 项 pytest 通过，前端 lint 与 build 通过，并用真实 UDP 包与 WebSocket 客户端验证了转发链路。

### v0.9 - 2026-08-18

- 复制 Meta 渐变天空盒到 `Assets/Materials`，配置为低饱和日间办公天空，并保持 Recording 的 Trilight 环境光不变。
- 新增 Quest 轻量 URP Cubemap Unlit Shader 与两份独立外景材质，恢复 Japan Office 的 Workspace/Entrance EXR 窗外内容；外景关闭阴影、探针接收、运动矢量和 GI 参与。
- 独立重烘焙当前 Baked Reflection Probe；18 组 Directional Lightmap 文件时间戳保持不变，Shader 与 C# 编译检查均无错误。

### v0.8 - 2026-08-18

- 修复 Japan Office 从 HDRP 切到 URP 后丢失 Base、Mask、Normal、光滑度等材质映射的问题；当前 Recording 场景 19 个实际使用材质已恢复为 URP Lit PBR 表面。
- 将 Renderer 从 Deferred 改为 Quest 适用的 Forward，启用 4× MSAA，关闭 HDR、Depth/Opaque Texture、额外灯阴影与 SSAO，并保留 15 米双 Cascade 主光阴影。
- 按当前办公室与镜像角色位置自适应重建烘焙顶灯、168 个 Light Probe 和 Reflection Probe，完成 18 组 1024 Directional Lightmap 的最终烘焙。
- Unity 6000.5 脚本编译无错误；8 秒无头显 Play Mode 观察完成，仅出现预期的 Meta XR Form Factor Unavailable 提示。
- 修正 Japan Office 导入器在已配置 URP 时重复弹出 HDRP Project Settings 询问的问题，保留原手动 Setup 菜单。

### v0.7 - 2026-08-18

- 为 `Recording` 场景建立可重复执行的 URP 光照准备与最终烘焙菜单，生成混合/烘焙灯光、Light Probe、Reflection Probe、Volume、Lighting Settings 和六类环境材质模板。
- 新增基于 `OVRHand` 真实追踪/置信度与 HMD 保守安全工作区的手部越界提示，不启用透视相机权限或额外 RGB 相机流。
- 完成 Unity 6000.5 编译检查和 8 秒 Play Mode 检查；新增代码无编译或运行时错误，无头显 Editor 仅出现预期的 Meta XR Form Factor 不可用提示。

### v0.6 - 2026-08-18

- 将提示词、录制状态、REC、倒计时和长按反馈迁移为 `CenterEyeAnchor` 下的 HMD 固定 UI，并移除提示板拖动与归位入口。
- 按用户摆放复用 `TouchScreenDevice_03`，配置“重播动作/退出重播”和“查看教程/关闭教程”两个裸手 Poke 按钮。
- 修复网页停止与脚踏短按无法退出 `Reviewing` 的状态机路径；退出回看后恢复实时身体驱动，回看期间不关闭 Quest 手部追踪。
- 新增正面锁定镜像角色头部到腰部的独立网页预览摄像机，并从预览中排除 HMD Overlay UI。
- Unity 编译和短时 Play 检查通过；Android Development APK 已成功构建、安装并在 Quest 3 启动，UDP 配对、Pose、预览上传和浏览器实时更新均已验证。

### v0.5 - 2026-08-18

- 确认正式录制完全不使用 Quest 控制器，低频操作采用可见裸手 Poke 按钮。
- 完成中文动态 TMP 字体、中文状态文案、录制视野边框与更强长按反馈。
- 完成可 Poke 按住拖动的提示板、提示板归位和 VR 内新手教程。
- 完成最近一次 Meta Pose Take 的 VR 镜像机器人回看、暂停、重播和退出流程。
- 新增 `Reviewing` 状态并完成无头显 Editor Play Mode 运行时检查；仅保留预期的 Editor OpenXR 无设备提示。

### v0.4 - 2026-08-17

- 增加命令 ACK 等待、拒绝处理和主机状态回滚；新增对应 UDP 单元测试。
- 修复自定义录制圆环在 Android Player 构建中的 `OnValidate` 编译错误。
- Android IL2CPP 开发包构建成功，并已通过 ADB 安装和启动到 Quest 3。
- 增加仅限本地子网的 Windows 防火墙配置脚本和操作说明。
- 完成 React 1600 × 1000、1280 桌面与 760 窄屏浏览器验证。

### v0.3 - 2026-08-17

- 完成 FastAPI + React 本地主机端、Quest UDP 发现/选择与 WebSocket 状态链路。
- 完成 Quest 控制网关、Pose 单播切换、Pose/Meta 可靠上传和断线待上传保留。
- 完成 640 × 360、8 FPS、JPEG 60 的 Quest 实时预览上传。
- 完成网页外置相机 WebM 录制、句子导入、Take 文件状态与脚踏空格交互。
- Unity 允许受信任录制局域网内的明文 HTTP；生产网络若跨越受信任 LAN 必须改为 HTTPS。

### v0.2 - 2026-08-17

- 完成 Unity 第一阶段纵向切片：独立 Recording 场景、状态机、教师 UI、倒计时与长按重录。
- 复用并保留现有 Meta Pose、UDP 和真镜像角色基础。
- 为本地 Pose Take 增加唯一文件名、候选状态和中断原因元数据。
- 将 Recording 设为当前唯一启用的 Android 构建场景。

### v0.1 - 2026-08-17

- 建立系统、录制状态机与场景设计基线。
- 确认真镜像显示。
- 确认默认 2 秒倒计时。
- 确认长按进度满后回到当前句开始前状态。
- 确认旧 Take 保留为候选且不覆盖。
- 确认 Meta Pose 为动作数据权威格式。
- 确认主机采用 React 网页前端与 Python 录制中枢。
