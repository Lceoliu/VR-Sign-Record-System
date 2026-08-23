# 指代数据录制工作流

## 句子与固定视角

`VRroom` 使用 `RecordingSentenceSequence` 中可在 Inspector 编辑的句子数组。每句绑定一个固定世界视角；正常完成一条 Take 后自动加载下一句并切换视角，重置或其他中断不会推进序列。最后一句完成后停留在 `Completed`。

| 序号 | 视角 ID | 录制内容 | 相机眼位 `(x, y, z)` | 观察目标 `(x, y, z)` |
| --- | --- | --- | --- | --- |
| 1 | `state_01` | 选对箱子，拿起箱子，输入密码 | `(-3.20, 1.55, -3.70)` | `(-4.30, 1.20, -5.20)` |
| 2 | `state_02` | 拿起金币，选对盘子，把金币放到盘子 | `(-4.10, 1.55, -3.50)` | `(-4.55, 1.15, -5.10)` |
| 3 | `state_03` | 选对画框，拿下画框，找到密码 | `(-0.90, 1.55, -3.75)` | `(-0.888, 2.15, -6.00)` |
| 4 | `state_04` | 输入密码，打开箱子，选对钥匙 | `(3.60, 1.55, -3.65)` | `(4.35, 1.10, -5.15)` |
| 5 | `state_05` | 插入钥匙，打开柜子，选对按钮 | `(4.25, 1.55, -2.50)` | `(5.78, 1.20, -3.00)` |
| 6 | `state_06` | 按下按钮，选对电闸，拉下电闸 | `(4.30, 1.60, -0.90)` | `(6.03, 1.65, -0.75)` |

表中的眼位和观察目标共同定义相机世界姿态。场景内 `RecordingViewpoints` 下的六个 Camera 是可编辑的参考相机；Quest 实际渲染仍使用唯一的 `CenterEyeAnchor`。

`state_02` 还会直接切换保险柜的确定性状态：`Object_5` 门板及其子级密码锁围绕门板左边线打开 `105°`；切换到其他视角时恢复精确的闭合姿态。该切换没有交互和动画，多次往返不会累计位移或旋转。

## 操作方式

- PC Editor：进入 Play Mode，短按 `Space` 开始或结束当前 Take；长按 `Space` 1.2 秒重置当前句；`Left Arrow`/`Right Arrow` 切换上一句/下一句。也可使用 `Tools/SignVR/Simulation/Toggle Recording`、`Previous Sentence` 和 `Next Sentence`。
- Quest 3：右手控制器 `A` 键短按开始或结束，长按 1.2 秒重置当前句；未录制时右手 `B` 切换下一句，左手 `X` 切换上一句。录制、倒计时或保存过程中不会接受切句。
- 场景配置：打开 `Assets/Scenes/VRroom.unity` 后依次运行 `Tools/SignVR/Configure VRroom Player and Physics` 与 `Tools/SignVR/Configure Pointing Recording`；对应的两个 `Validate` 菜单检查固定道具、六个视角、保险柜状态、相机标签和追踪原点设置。
- 头显左上角只保留一套圆角气泡，显示当前句、序号和录制状态；底、描边和文字均使用不受场景深度遮挡的 Overlay 材质。句子加载时会同步选择其绑定视角。

## 主机与脚踏控制

主机沿用克隆仓库的协议：HTTP/WebSocket `8000` 用于操作界面、上传和预览；UDP `5005` 用于设备公告、ACK 和实时姿态；UDP `5006` 用于 Quest 发现与控制。

本项目运行 `scripts/start-local.ps1` 时默认加载 `pointing_sentence_catalog.json`，其中是与 Unity Inspector 一致的 6 条文本及 `state_01` 到 `state_06` 显式视角 ID；原来的 300 条语料文件仍保留，不会被破坏。新建指代录制轮次时请使用新的数据目录或批次，300 条旧轮次与 6 条指代轮次不会混用。

控制动作包括 `start_take`、`stop_take`、`reset_take`、`pedal` 和 `set_guidance`。`start_take` 的视角解析顺序是显式 `viewpoint_id`、可编辑序列中的 `sentence_id` 映射、最后才是 JSON 中明确存在的 `sentence_index`。缺失的整数不会再被误判为索引 0。配对后主机拥有句子和 Take 编号的控制权。脚踏的 `down/hold/up` 阶段同步到头显中的按下反馈和长按进度，最终开始、停止或重置仍走同一 `RecordingCoordinator` 状态机。

## 固定世界坐标

- OVR 使用 `FloorLevel`，关闭 `resetTrackerOnLoad` 和 `AllowRecenter`，避免 Quest 根据身高或自动重定位改变场景世界原点。
- 每次 Take 倒计时前都会重新套用对应的世界相机姿态，再通过 `VRPlayerRig.SetSpawnPoint(..., true)` 对齐 XR Rig，使当前受追踪的头部到达目标眼位；倒计时结束和创建文件前还会各校验一次误差。这会补偿佩戴者初始头高，但不会移动房间或参考相机。
- 录制模式持续锁定玩家根节点的世界位置与朝向，抵消 Quest 在地面高度校准后对根节点的二次偏移；头、手和房间尺度内的真实追踪运动仍然保留。录制者可以按需求转头观察，因此起录校验固定眼位且要求位置追踪有效，但不强锁头部朝向。
- 每次视角对齐完成后还会记录 XR Origin 的局部基准；录制期间若运行时重定位或地面校准改写 Origin，会在 `LateUpdate` 恢复该基准，并在元数据中记录修正次数。锁定的是世界参考系，不锁 CenterEyeAnchor 的头部旋转和房间尺度内的手/头运动。
- 录制期间关闭摇杆移动和 `CharacterController`，禁用录制者层级内的非 Trigger Collider 与手部物理限制器。Trigger、头部追踪和手部追踪仍保留，因此录制者不参与场景碰撞，但本地控制与姿态采集继续工作。
- `box`、`box (1)`、`box (2)`、三个盘子和三枚金币是视觉指代锚点。配置器会禁用它们全部 Collider 与抓取组件；已有 Rigidbody 固定为 Kinematic、关闭重力和碰撞并冻结所有轴，确保录制期间始终停在初始位置。
- 青色手骨架覆盖层选择左右主 `OVRHandVisual`，沿用 Meta `OVRSkeletonRenderer` 的逐父子骨骼 LineRenderer 逻辑，每只手生成 25 根骨骼线。原手部网格隐藏，但同一套真实手追踪仍驱动物理关节与 pose 录制；追踪失效时自动隐藏对应手。

## 输出与模拟

每条 Take 以 30 Hz 写入：

```text
Application.persistentDataPath/Recordings/{session}/{sentence}/
  {stem}.pose.jsonl
  {stem}.meta.json
  {stem}.uploaded       # 上传成功后
```

`.meta.json` 的 `spatial_context` 固定保存 Take 开始时的视角 ID/index/name、参考相机世界位置与旋转、XR Origin/Head 世界姿态、Origin 基准锁定状态与修正次数、地面世界 Y、Tracking Origin、`VRroom-world-v1` 和 UTC；同时写入 `viewpoint_position_aligned`、实际误差和允许误差。这样即使之后切换视角，也不会污染已完成 Take 的坐标。

正常完成至少需要 15 个采样帧；Quest 上还要求至少 90% 的帧包含有效、非空姿态。过短、追踪质量不足、应用暂停、进入透视、采样异常或写盘失败都不会推进句子，而是保留当前句并提示重录。元数据额外记录 `valid_pose_frame_count` 与 `valid_pose_ratio`。元数据先写临时文件再原子改名，避免上传器读取半写入 JSON。

不连接主机时，Quest 每次启动会生成独立的 `quest-local-{UTC}-{random}` 会话 ID；Editor 固定使用 `editor-session` 便于反复模拟。主机提供的正式 session ID 始终优先。

Editor 在 Meta 姿态无效时仍可模拟完整的开始、采样、停止、落盘和自动下一句流程。模拟帧保持现有 `.pose.jsonl` 格式，但写入 `pose_valid=false`、`joint_count=0`；元数据标记 `editor_simulation=true` 和 `pose_source_simulated=true`，上传器会将这类文件保留为本地联调产物而不排入 Quest 正式上传队列。非 Editor 构建仍要求有效的真实 Meta 姿态才允许开始。

为便于设备占用期间验收，Editor 无真实手数据时会在当前参考相机前显示两只仅用于渲染检查的模拟骨架；它们不会写入 pose。Quest 构建只显示真实左右手关节。

当前 Editor 侧的脚本、场景配置和模拟链路已完成；Quest 3 上的按键、头高补偿、固定视角、手骨架显示、真实姿态文件和主机联调仍需等设备空闲后做真机验收。
