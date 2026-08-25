# 指代数据录制工作流

## 31 句与六个固定视角

`VRroom/RecordingViewpoints` 上的 `RecordingSentenceSequence` 保存 31 条可编辑
数据。每条数据包含句子、固定视角、目标路径和可选的顺序数字。Quest 启动后
先显示第 1 句，避免局域网首次握手前出现空白；主机配对后仍由主机选择句子，
Quest 只镜像相同 `sentence_id` 的本地目标配置。

| 视角 | 数量 | 规则 | 头显句子 |
| --- | ---: | --- | --- |
| `state_01` | 3 | 三个箱子分别一条 | 找到那个箱子下面的密码 |
| `state_02` | 9 | 三枚金币 × 三个盘子 | 把这个金币放到那个盘子里面 |
| `state_03` | 3 | 三个画框分别一条 | 把那个画框拿下来，找到它背后的密码 |
| `state_04` | 3 | 三把钥匙分别一条 | 输入密码，打开箱子，拿起那把钥匙 |
| `state_05` | 7 | 三个按钮的全部非空子集 | 用钥匙打开柜子，按下那些按钮 |
| `state_06` | 6 | 三个电闸的全部排列 | 按顺序依次拉下对应电闸 |

完整逐句目标、世界坐标和电闸数字映射见仓库根目录
`POINTING_SENTENCE_CATALOG.md`。数量必须保持
`3 + 9 + 3 + 3 + 7 + 6 = 31`。

六个参考 Camera 只定义世界眼位。Android 上它们始终禁用，Quest 仍由唯一的
`CenterEyeAnchor` 渲染。选择句子时 `RecordingViewpointController` 对齐 XR Rig，
让当前追踪头部到达参考眼位。

## 录制者与监督者操作

- Quest 不使用 Touch 手柄、电源键或音量键。
- 录制者踩连接主机的 USB HID 脚踏板。短踩等价于 `Space`：开始、取消倒计时
  或结束录制；踩住 1.2 秒重录上一句/当前句。
- 外部监督者在网页中用左右方向键、上下句按钮、句子列表或编号跳转选择句子。
- Quest 内没有上一句、下一句或切场景按钮。所有句子和场景切换由主机完成。
- 网页必须保持焦点，且输入焦点不能停在输入框、下拉框或按钮内。

`stop_take` 完成后主机前进一条，再向 Quest 发送 `select_sentence`。如果 Quest
仍在保存，它会排队并在回到空闲状态后应用。长按重录会回到刚录完的句子，
但不会覆盖旧 Take。

## 头显提示、高亮和食指射线

左上角只显示一套非交互圆角气泡；底色、描边和文字使用 Overlay 材质，避免被
场景遮挡。句子文本保留“这个、那个、那些”等指代形式。

倒计时和录制期间：

- 当前目标显示浅青色发光包围框。
- 金币/盘子句同时高亮两件物体。
- 按钮句高亮选中的 1 至 3 个按钮。
- 电闸句高亮全部三个电闸，并在每个电闸旁显示该句的 `1/2/3` 顺序。
- 左右手直接使用 Meta Interaction SDK 的 `HandRayInteractor` 和
  `RayInteractorRayVisual`。运行时将原生射线延长到 1.5 米，并允许它在固定物品
  没有 Meta Ray Interactable 时仍然显示。

高亮基于目标子 Renderer 的合并 Bounds，不依赖 Collider，也不修改共享材质。
Ready、保存、重置等非录制状态会隐藏高亮和数字；原生手部射线随 Quest 手部
追踪状态显示。

手部外观直接复用 Meta Interaction SDK 的 `HandVisual` 和 Quest 手部追踪数据；
录制栈会关闭 `MetaSourceDataProvider.DebugDrawSkeleton`，也不会再生成自绘蓝色线
手。Editor 没有 Quest 跟踪输入时不伪造手或射线。

## 固定物体与场景状态

31 句涉及的全部目标都是不可移动的视觉参照：

- Rigidbody 设为 Kinematic、关闭重力和碰撞检测、冻结全部轴。
- `Grabbable` 禁用。
- 运行时缓存初始局部姿态，并在 LateUpdate 防止第三方交互脚本造成位移。

门的状态不依赖交互或动画：

- `state_02`：保险柜门围绕左边线打开 `105°`。
- `state_05/06`：衣柜左右门分别围绕外侧边线打开 `+105°/-105°`。
- 其他视角恢复精确闭合姿态；每次先恢复再旋转，不累计漂移。

## 固定世界坐标

- OVR 使用 `FloorLevel`，关闭 `resetTrackerOnLoad` 和 `AllowRecenter`。
- 起录前重新套用句子视角并校验头部位置误差，补偿佩戴者初始头高。
- `VRPlayerRig` 锁定玩家根节点和已对齐 XR Origin 的基准，允许正常转头和双手
  追踪，但不允许摇杆移动、角色碰撞或运行时重定位改变数据世界坐标。
- Meta 中写入 `world_frame_id=VRroom-world-v1`、视角、参考/实际头部姿态、
  Floor Y、对齐误差、Tracking Origin 和 Origin 修正次数。

## 主机目录与协议

`scripts/start-local.ps1` 默认加载 31 条
`backend/app/pointing_sentence_catalog.json`。旧 300 句目录仍保留为
`sentence_catalog.json`，不可与同一个 batch/round 混用。

控制动作包括 `start_take`、`stop_take`、`reset_take`、`select_sentence`、
`pedal` 和 `set_guidance`。已删除 Quest 发往主机的 `sentence_offset` 信号。

| 端口 | 协议 | 用途 |
| --- | --- | --- |
| 8000 | HTTP / WebSocket | 网页、上传、预览 |
| 5005 | UDP | 公告、ACK、实时 Pose |
| 5006 | UDP | Quest 发现与主机命令 |

## 输出和模拟

每条 Take 以 30 Hz 写入：

```text
Application.persistentDataPath/Recordings/{session}/{sentence}/
  {stem}.pose.jsonl
  {stem}.meta.json
  {stem}.meta.json.uploaded
```

正常完成至少 15 帧；Quest 上至少 90% 帧必须包含有效非空姿态。过短、追踪质量
不足、应用暂停、透视中断、采样异常或写盘失败都不前进。Editor 模拟文件标记
`editor_simulation=true`、`pose_source_simulated=true`，只用于联调且不会上传。

## 配置与检查

打开 `Assets/Scenes/VRroom.unity` 后执行：

```text
Tools/SignVR/Configure VRroom Player and Physics
Tools/SignVR/Configure Pointing Recording
Tools/SignVR/Validate VRroom Player and Physics
Tools/SignVR/Validate Pointing Recording
```

指代配置命令会写入 31 句、冻结目标物理、创建门铰链、保存场景并立即校验。
