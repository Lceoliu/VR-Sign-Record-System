# Codex + Meta XR Simulator 本地无人值守开发测试闭环评估

Updated: 2026-08-27

## 范围与结论

本报告评估：在本项目已经能让 Meta **Standalone XR Simulator** 连接并渲染
`InteractionLab` 的前提下，Codex 能否在 PC 本地独立执行“修改代码、启动 Unity、驱动 XR
交互、判定结果、继续修复”的闭环。

**结论是有条件可行。** 可以建立一条长期无人值守的工程回归管线，让 Codex 独立完成
大量有明确验收条件的开发与测试工作；但它不能把 Simulator 通过等同于 Quest 真机通过，
也不能独立承担人体工学、舒适性、真实手追质量或正式发布验收。

本项目最稳妥的路线不是先依赖 Meta XR Operator，而是：

1. 复用现有 EditMode、PlayMode 和 saved-scene 测试作为快速逻辑门禁；
2. 为 `InteractionLab` 增加测试专用、确定性的连续 6DoF 输入场景；
3. 用 Standalone XR Simulator 的 Session Capture / Record & Replay 覆盖真实 OpenXR
   输入链和视觉回归；
4. 用 Unity Test Framework 的 XML、完整日志、状态证据和截图共同判定；
5. 最后保留少量 Quest 真机 smoke、性能和人工 UX 门禁。

完成前三层后，日常的“发现失败 → 修改 → 重跑 → 收集证据 → 继续修复”可以不需要人类在场。
第一次建立可靠的输入基线、定义产品期望以及最终真机验收仍需要人类。

## 信息分级

本报告明确区分三类信息：

- **项目实测**：本轮在当前工作站和当前仓库上观察到的行为，不声称是 Meta 的通用保证。
- **官方事实**：来自 Meta 或 Unity 的一手文档。
- **工程判断**：基于上述事实提出的管线设计与风险判断。

## 当前项目基线与本轮实测

仓库中的版本事实如下：

- Unity `6000.5.6f1`
- Meta XR SDK All-in-One `205.0.0`
- OpenXR Plugin `1.17.1`
- Input System `1.20.0`
- Unity Test Framework `1.7.0`
- 主 Unity 项目位于 `signvr_unity/`
- 目标 saved scene 为 `Assets/Scenes/InteractionLab.unity`

本轮已经得到以下项目实测事实：

- v205 Standalone XR Simulator 可以作为 OpenXR runtime 连接 `InteractionLab` 并正确渲染。
- “Simulator 窗口里能看到画面”只证明 OpenXR 会话和渲染链已建立，不证明 XR 交互链可用。
- `InteractionLab` 的主要控制是世界空间 UI。普通桌面坐标点击不是 XR Select；UI 需要来自
  控制器射线或手部 interactor 的位姿、命中和 Select/Poke/Pinch 事件链。
- 对这种世界空间 UI，可靠自动化需要连续的 6DoF 控制器或手部输入，而不是一次性的屏幕
  坐标点击。输入必须保持若干帧，使 tracked pose、raycast、hover 和 select 按正确顺序收敛。
- 项目主动禁用了 Standalone 的 `XR_APILAYER_METAX_operator`。当前项目注释记录的原因是
  v205 下该实验层可能在退出 Play Mode、拆卸 OpenXR 时导致 Unity 崩溃。该层不是 Simulator
  渲染或普通输入所必需，因此当前稳定主线不应重新启用它。

最后一点与 Meta 的官方定位一致：
[Meta XR Operator](https://developers.meta.com/horizon/documentation/unity/meta-xr-operator/)
目前明确标为 **Experimental feature**，Meta 提醒工具、API 和行为可能变化，并建议生产应用
不要依赖它。项目观察到的退出崩溃是本地实测，不是 Meta 文档声明的普遍缺陷。

## 官方能力核实

### Standalone XR Simulator

Meta 将最新的
[Standalone XR Simulator](https://developers.meta.com/horizon/documentation/unreal/xrsim-intro/)
描述为一个轻量级 OpenXR runtime。它在 API 层模拟 Quest 设备，支持双目渲染、头显和控制器
追踪、手追、输入、合成环境，并明确把“简化测试环境、扩展自动化和 CI/CD”列为用途。

这对本项目意味着：

- 可以测试应用通过 OpenXR 看到的设备和功能，而不只是操作 Unity 场景里的假 Transform；
- Windows 上 Simulator 成为当前系统的 active OpenXR runtime，应用无需为 Simulator 改成
  另一套业务逻辑；
- 同一时间只能有一个 active OpenXR runtime。Simulator 激活时会暂时代替 Meta Horizon
  Link，结束后要恢复原 runtime。官方
  [Getting Started](https://developers.meta.com/horizon/documentation/native/xrsim-getting-started/)
  说明了这一行为。

Simulator 不是 Quest 硬件模拟器。它让应用面对相似的 OpenXR API 和模拟输入，但应用仍在
PC、PC 图形驱动和桌面操作系统上运行。

### 输入模拟与世界空间 UI

最新 Standalone Simulator 的 Inputs 与 Input Bindings 面板可以：

- 选择头显、左手/控制器、右手/控制器哪些 tracking source 生效；
- 用键鼠、Xbox 控制器或真实 Quest 控制器驱动模拟设备；
- 模拟控制器、手以及 pinch、poke、grab 等手势；
- 查看和自定义移动、旋转、抓取等动作绑定；
- 使用 Point + Click，让控制器 ray 跟随鼠标方向。

这些能力记录在
[Standalone XR Simulator overview](https://developers.meta.com/horizon/documentation/unreal/xrsim-intro/)
中。旧版 Point + Click 文档还明确说明：该模式是让 controller ray 跟随鼠标，Interaction SDK
Ray Interactor 可能需要额外 pointer offset；它不是把桌面像素直接变成 Unity UI click。
由于该页面已标为旧版，具体开关名称只能作为概念说明，v205 应以当前 Inputs 面板为准：
[Using Point-and-Click Input（旧版）](https://developers.meta.com/horizon/documentation/native/xrsim-point-and-click-1.0/)。

因此，本轮“桌面坐标点击不等于 XR Select”不是反例，而是正确暴露了 XR UI 的输入语义：

```text
6DoF source pose
  -> controller/hand interactor
  -> world-space ray or poke hit
  -> hover/select state transition
  -> PointableCanvas / UGUI event chain
  -> application callback
```

一个可靠测试应该观察这条链上的多个状态，而不应只判断“鼠标是否点在按钮像素上”。

### Record & Replay / Session Capture

Meta 的
[Session Capture](https://developers.meta.com/horizon/documentation/native/xrsim-session-capture/)
可以记录输入和精确时序截图到 VRS 文件，并在后续会话中重放。官方给出的自动重放配置支持：

- 延迟开始，等待应用和场景准备完成；
- 指定 recording 和 replay 输出文件；
- 重放结束后自动退出；
- 将相同输入序列用于快速手工测试、自动化、Continuous Automation 和多人测试。

最新 Standalone overview 仍把 Session Capture、零代码自动化和 CI/CD 集成列为主要功能。
不过，独立的 Session Capture 配置页比 Standalone 新 UI 更早发布。因此在正式写脚本前，
必须先对当前 v205 可执行文件验证 `persistent_data.json` 的实际位置、字段和退出行为，不能把
旧路径硬编码为永久契约。

Meta 还提供了
[No-Code Automation](https://developers.meta.com/horizon/documentation/native/xrsim-native-automated-testing/)
流程：录制输入和 snapshot、生成期望 replay、在测试机重放，再从 VRS 中取图做像素比较。
这一流程适合 smoke 和视觉回归，但图像只能证明输出，不足以解释隐藏对象、动态状态或业务
副作用。

### Programmatic 与 Hybrid Automation

Meta 的旧版官方教程分别展示了：

- [Programmatic Automation](https://developers.meta.com/horizon/documentation/unity/xrsim-unity-automated-testing-programmatic-1.0/)
  用 Unity PlayMode 测试加载真实场景、等待 XR 数据、断言场景语义对象，并通过 CLI 输出结果；
- [Hybrid Automation](https://developers.meta.com/horizon/documentation/unity/xrsim-unity-automated-testing-hybrid-1.0/)
  先重放一段 XR 输入把应用导航到目标状态，再由程序化测试做精细断言。

这两个页面已明确标记为旧版 Simulator，不应直接复制其中的 v65 下载地址、配置文件路径或
固定等待时间。它们仍然提供了有效的架构信号：**重放负责到达状态，代码断言负责判断状态**。
本项目建议采用同一模式，但要重新验证 v205 的启动和配置接口。

### Unity Test Framework 和 CLI

Unity 官方支持从命令行运行 EditMode、PlayMode 或 Player 测试，并输出 NUnit XML。
典型命令形态是：

```powershell
Unity.exe `
  -runTests `
  -batchmode `
  -projectPath <signvr_unity> `
  -testPlatform PlayMode `
  -testFilter <fully-qualified-test-name> `
  -testResults <results.xml> `
  -logFile <editor.log>
```

参数和 XML 结果格式见
[Unity Test Framework command-line reference](https://docs.unity3d.com/Packages/com.unity.test-framework@1.4/manual/reference-command-line.html)。
本项目实际安装的是 1.7.0，建立 runner 时应先用当前包实测参数兼容性；不应只依赖进程退出
码，因为 Unity 文档指出各被测组件没有统一的退出码语义。管线需要同时解析：

- 进程退出码；
- NUnit XML 的 test-case 结果；
- `Editor.log` / 指定 `-logFile` 中的编译错误、异常和崩溃迹象；
- 预期的项目状态证据与截图。

[Unity 6 Editor command-line arguments](https://docs.unity3d.com/6000.0/Manual/EditorCommandLineArguments.html)
还给出两个重要约束：同一项目同一时刻只能由一个 Editor 实例打开；`-nographics` 不创建图形
设备。因而 XR 渲染和截图 job 不能加 `-nographics`，并行 job 必须使用独立 project copy 或
worktree，而不能共享同一 `signvr_unity/Library`。

### Unity Input System

本项目的 Input System 1.20 官方文档明确支持“无需物理设备、由代码驱动输入测试”，并说明
生成的输入与平台 backend 在运行时产生的输入同等处理：
[Testing input, Input System 1.20](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.20/manual/Testing.html)。

这非常适合普通 action、UI 和 action-based XR 代码。不过本项目当前真实交互链主要经过
Meta OpenXR / Interaction SDK。对来自 OpenXR runtime 的 tracking source，不能未经验证就假设
`InputTestFixture` 的虚拟设备一定替代 Simulator 设备。推荐把它用于：

- 纯 Input Action 映射和业务响应测试；
- 非 OpenXR 的 UGUI 下游测试；
- 测试专用驱动创建的设备；

而 Meta runtime 端到端输入应由 Simulator replay、真实 controller forwarding 或经验证的
项目测试 seam 驱动。

### Meta XR Operator

Meta XR Operator 的官方目标与本问题高度吻合。它通过 OpenXR API layer 在运行中的应用内启动
MCP server，让 agent 可以：

- 读取 session、frame、scene graph 和 world-space UI；
- 设置头和控制器的连续位姿；
- 设置 button、trigger、grip 和 thumbstick；
- 捕获合成图像；
- 调用应用注册的自定义工具。

官方称它可在 Unity Editor、Meta XR Simulator 和 Quest 上使用：
[Operator overview](https://developers.meta.com/horizon/documentation/unity/meta-xr-operator/)。
其先决条件 Unity 6000、Core SDK v205 和 OpenXR Plugin 1.17 与当前项目吻合。

但当前不应把它放进稳定 MVP：

- 官方仍标记为 Experimental；
- 当前项目已经观察到退出 Play Mode 时的崩溃风险；
- API layer 位于 OpenXR 调用链中，它失效时会同时影响测试对象和测试控制面；
- 当前关闭它并不影响 Simulator 的渲染、普通输入或 Session Capture。

推荐未来建立单独的 **Operator canary lane**：使用项目副本、单独 Unity 进程、超时和崩溃转储；
只有连续多轮“启动、驱动、退出”稳定后，才考虑把它升级为默认 agent 控制面。不要为了获得 MCP
控制而取消当前稳定保护。

## 推荐的分层管线

| 层 | 目标 | 驱动方式 | 主要判据 | 是否可无人值守 |
| --- | --- | --- | --- | --- |
| L0 编译与静态门禁 | 快速阻止破坏性改动 | Unity import/compile、EditMode | 编译、NUnit XML、日志 | 是 |
| L1 saved-scene PlayMode | 验证真实场景 wiring 和业务流程 | 现有 test driver | 状态、对象、文件、副作用 | 是 |
| L2 确定性 6DoF 场景 | 验证 world-space UI 与 XR 交互顺序 | 测试专用 pose/select driver | tracked、hover、select、exactly-once | 是 |
| L3 Simulator OpenXR E2E | 验证 runtime、输入链和渲染 | v205 Session Capture replay | 应用证据、VRS、截图、日志 | 基线建立后是 |
| L4 视觉与稳定性 | 防止提示、字体、遮挡和退出回归 | 关键帧 PNG/VRS、重复运行 | 图像差异、异常、退出状态 | 是，但需容差 |
| L5 Quest 真机 | 验证真实产品 | APK、Quest、真实控制器/手 | smoke、性能、追踪、UX | 部分自动，仍需人工 |

### L0：编译与 EditMode

每次 Codex 改动后先运行最小相关测试，再运行完整 EditMode 套件。失败时保留 XML 和日志，
不进入 XR。这个层级速度最快，适合 Codex 的红绿循环。

### L1：复用现有 saved-scene PlayMode 测试

当前最合适的第一关是：

`SignVR.Interaction.PlayMode.Tests.Orchestration.W8LocalUnityIntegrationPlayModeTests.SavedInteractionLabStartButtonConsumesOneRunAndStaysActive`

它已经复用保存的 `InteractionLab`，等待真实 manifest 与 startup recovery，确认 Start 可用，
启动一个 Run，检查只消费一个 Run Plan、只创建一个 Run 目录、重复点击不重复消费，并保持 active
状态。相邻测试还覆盖：

- 第一 Run 显示中文 signer text 与 pointing assistance；
- Start 后注入失败时清理测试拥有的资源；
- world-space UGUI graphic raycast 和 downstream pointer click；
- hand-ray 需要的 `RayInteractable` / `PointableCanvasModule` 拓扑。

这些测试价值很高，但它们有意绕过了设备拥有的 live hand-ray source，并在测试 seam 内调用
UGUI click。因此它们是“saved scene + 下游流程”的强门禁，不是“Simulator 控制器真实 Select”
的端到端证明。报告建议保留这个边界，而不是把测试名称扩大解释。

### L2：增加最小 6DoF 自动化场景

最小新增能力应是一个仅在测试/EngineeringLocal 中可用的 XR scenario driver，而不是第二套产品
输入系统。它至少需要表达：

```text
SetHeadPose(position, rotation)
SetControllerPose(left/right, position, rotation, tracked)
SetSelect(left/right, pressed)
SetPokeOrPinch(left/right, state)       # 仅在目标用手时
WaitFrames(count)
WaitUntil(condition, timeout)
```

第一条场景只做一件事：

1. 等待 `InteractionLab` 的 Start surface ready；
2. 连续若干帧将右控制器 ray 对准按钮中心；
3. 断言进入 hover；
4. press、保持一帧以上、release；
5. 断言只触发一次 Start，Run Plan 和 Run 目录均只有一个；
6. 抓取状态证据与截图；
7. 正常退出并确认没有 OpenXR teardown crash。

具体输入后端应通过一个窄接口封装。优先顺序是：

1. v205 Session Capture replay，覆盖最真实的 Simulator 输入；
2. 经验证的 Meta/Unity test-only provider，允许代码写入 pose 和 select；
3. Input System 虚拟设备，仅在它确实驱动当前 Meta Interaction SDK source 时采用；
4. OS SendKeys/鼠标移动只作为诊断工具，不作为 CI 主线。

原因是 OS 输入依赖窗口焦点、鼠标捕获、分辨率和布局；而世界空间 XR 目标需要连续的时域状态。

### L3：Simulator replay + 程序化断言

推荐把 Meta 所说的 Hybrid Automation 落成以下同步协议：

1. 应用输出 `scenario_ready` 证据，而不是依赖固定 `WaitForSeconds(10)`；
2. runner 看到 ready 后启动 VRS replay；
3. replay 在关键点触发应用可观察状态；
4. PlayMode 或运行时测试等待 `scenario_complete`；
5. 程序断言 Run 状态、exactly-once、副作用和错误；
6. 收集 replay VRS、截图、应用日志和测试 XML；
7. 退出 Simulator、Unity/Player，验证进程真正结束并恢复 active runtime。

如果 v205 Session Capture 暂时没有稳定的非交互启动接口，MVP 可先把一次人工录制的 VRS 当成
版本化测试夹具。此后重放可以完全无人值守。若要求连首次输入生成也无人参与，则需要 L2 的
程序化 provider，或待 Operator canary 稳定后由 Operator 生成/执行动作。

### L4：证据与视觉判定

每次 XR job 至少保留：

- NUnit `results.xml`
- Unity/Player 完整日志
- Meta XR Simulator 日志或 Console 导出
- 输入 recording/replay VRS 或 scenario JSON
- Start 前、hover、select 后、首个 instruction 的关键帧
- 进程退出码、超时原因、active OpenXR runtime 的前后值
- 失败时的 Unity crash dump / Simulator crash dump 路径

Unity 可用
[`ScreenCapture.CaptureScreenshot`](https://docs.unity3d.com/6000.0/ScriptReference/ScreenCapture.CaptureScreenshot.html)
保存 PNG。视觉比较应遮蔽时间戳、随机 ID 和动画区域，采用区域和容差；不能把全帧逐像素相等
当成唯一 oracle。业务状态断言始终优先于截图。

## Codex 的闭环方式

当上述 runner 存在后，Codex 可以独立执行：

```text
读取任务与既有测试
  -> 修改最小代码/测试
  -> 运行 L0/L1
  -> 运行相关 L2/L3
  -> 解析 XML、日志、状态证据和截图
  -> 定位失败并继续修改
  -> 全量回归
  -> 输出 diff、证据和剩余风险
```

runner 必须具备硬边界：

- 单个步骤和整轮超时；
- 最大自动重试次数；
- 编译失败、测试失败、基础设施失败、视觉差异分别归类；
- 禁止两个 Unity 实例同时打开同一 project；
- 每轮使用唯一 artifact 目录；
- 无论成功失败都清理 Simulator/Player 进程并恢复 OpenXR runtime；
- 不自动接受新的视觉 baseline；
- 不自动重新启用 Operator layer；
- 不自动覆盖真实研究数据或 participant artifact。

这些约束让 Codex 可以“无人看守地工作”，但不会在基础设施卡死或产品期望不清时无限修改。

## 具体 MVP 阶段

### MVP 0：可重复 runner，0.5–1 天

目标：不改业务输入，先证明 Codex 能可靠调用 Unity 并得到机器可读结果。

- 固定 Unity `6000.5.6f1` 路径；
- 为单个 saved-scene 测试提供 PowerShell 入口；
- 输出唯一的 XML 和 log；
- 对进程超时并清理；
- 解析失败 test case 和编译错误；
- 连续运行三次，结果一致且 Editor 正常退出。

验收：现有 `SavedInteractionLabStartButtonConsumesOneRunAndStaysActive` 连续通过，失败时 runner
能返回非零结果和可定位证据。

### MVP 1：saved-scene 业务闭环，1–2 天

- 串联三个现有 saved-scene PlayMode 测试；
- 添加首屏和首个 instruction 截图采集；
- 检查唯一 Run 目录、临时存储清理和失败路径；
- Codex 用一次小型、可逆变更演示“测试失败 → 修复 → 通过”。

验收：不依赖 Simulator 输入，也能完整覆盖真实场景、真实 wiring 和业务副作用。

### MVP 2：真实 XR Select，2–5 天探索

- 先验证 v205 Point + Click、controller bindings 和 Session Capture 能否稳定命中 Start；
- 记录最小 VRS：ready → aim → hover → select → release；
- 自动 replay 并将结果与现有 exactly-once 断言关联；
- 连续运行十次，禁止偶发 double-select、miss 或 teardown crash。

若 VRS replay 不稳定，停止继续堆固定 delay，转向测试专用 6DoF provider。

### MVP 3：Hybrid 与首阶段回归，3–7 天

- replay 负责进入第一 Run；
- 程序断言 signer、中文提示、pointing assistance 和 capture 初始化；
- 增加 hover/select/first-instruction 关键帧；
- 让 Codex 根据 XML、日志和截图独立修复一个真实回归。

验收：一条命令完成启动、XR 输入、业务断言、证据收集和清理。

### MVP 4：夜间无人值守与真机门禁，后续

- 对相关场景运行 Simulator suite；
- 失败保留完整 artifact 并生成摘要；
- 增加独立 Quest smoke job；
- 仅在人工批准后更新 VRS/图像 baseline；
- 单独观察 Operator canary，不阻塞稳定主线。

## 最小改造建议

以下是后续实现建议，本报告没有实施这些改动：

1. **新增一个外部 runner，而不是改业务状态机。** 负责 Unity 路径、filter、artifact、超时、
   进程清理和 XML 解析。
2. **复用现有 saved-scene test driver。** 不复制 `InteractionLab` 或建立第二份测试场景。
3. **增加窄的 `IXrAutomationDriver` 测试 seam。** 只暴露 pose、tracked、select/pinch 和按帧等待，
   不让测试直接操纵业务 Controller。
4. **增加 ready/complete 证据。** 用明确状态同步替代固定秒数。
5. **分别标记 direct UGUI test 与 XR E2E test。** 避免“调用 `Button.onClick`”被误判为
   “手/控制器完成了 Select”。
6. **为 screenshot 提供稳定相机和遮罩。** 保留参与者视角，同时屏蔽随机 ID、时间和动画噪声。
7. **保持 Operator layer 关闭。** 只有隔离 canary 证明稳定后才重新评估。
8. **不要同时引入另一套 XRI Simulator prefab。** 当前目标是验证 Meta OpenXR / Interaction SDK
   的产品链；双 Simulator 容易产生重复设备和不清晰的输入权威。

## Codex 可独立完成的范围

在验收标准和 runner 已存在时，Codex 可以独立：

- 阅读和修改 C#、测试、场景 setup 工具与外部脚本；
- 运行编译、EditMode、PlayMode 和 Simulator replay；
- 驱动确定性的头/控制器/手场景；
- 解析 XML、日志、状态文件和截图；
- 修复编译、状态机、wiring、UI 可见性、exactly-once 和清理回归；
- 建立和扩展测试案例；
- 在失败时保留证据、回退到较小测试并继续定位；
- 生成候选 Windows/Quest build 并进行静态审计。

这类工作最适合“明确输入、明确期望、失败可机器判定”的任务。

## 必须保留真机或人工参与的边界

Simulator 不能最终证明：

- Quest 上的 Android、Horizon OS、厂商 OpenXR runtime、权限和原生插件行为；
- 真实控制器 profile、触觉、手追置信度、遮挡、丢跟和恢复；
- Quest GPU/CPU/内存、热降频、电量、刷新率、双目合成和延迟；
- Passthrough、空间锚、房间扫描和真实物理空间差异；
- UI 在头显里的角尺寸、清晰度、深度感、可达性和手臂疲劳；
- 晕动、舒适性、安全边界和真人参与者是否理解流程；
- 手语动作、教学内容和研究流程是否在语义上正确；
- 摄像头、Host、Quest 与实验现场设备的完整联调。

可进一步自动化真机的 build、安装、启动、日志和少量脚本输入，但“穿戴体验”和真实人手动作
仍需要人类。正式研究运行、visual baseline 更新和发布签字不得交给 Codex 单独决定。

## 社区实践信号（非官方经验）

以下只反映个别开发者的经验，不能替代官方文档或本项目实测：

- 一个 Meta 社区帖子报告旧版 Simulator 左手按钮状态与右手不一致，切换 OpenXR provider 后
  情况变化，但发帖者仍因其他问题没有采用 Simulator。这提示输入 profile、SDK 和 runtime
  版本组合必须纳入测试矩阵：
  [Meta Community Forums](https://communityforums.atmeta.com/discussions/dev-unity/meta-xr-simulator---left-hand-does-not-receive-controller-button-input/1204963)。
- 一位开发者报告“Simulator 内按钮正常、Quest 真机控制器不工作”，说明模拟通过不能证明
  真机 action/profile 设置正确：
  [Reddit / r/Unity3D](https://www.reddit.com/r/Unity3D/comments/1i1da7a)。
- 另一讨论描述 PlayMode、FPS 和日志测试可工作，但自动 pinch/poke/grab 仍是主要阻塞，和本轮
  “需要连续 6DoF/手势输入”的观察一致：
  [Reddit / r/vrdev](https://www.reddit.com/r/vrdev/comments/1n6b32u)。
- Unity 讨论区长期存在“世界空间 UI 可见、ray 可见，但 controller click 不触发”的配置问题。
  这再次说明视觉 ray、raycast module、interactor 和 select action 必须分层断言：
  [Unity Discussions](https://discussions.unity.com/t/meta-quest-controller-button-click-not-working-with-world-space-ui-ovrcamerarig-dual-pointers/1668261)。

共同信号不是“Simulator 不可用”，而是：真实 XR 输入比桌面 UI 自动化多一个时域和设备层，
必须测试完整链路，并保留真机门禁。

## 最终建议

立即推进 **MVP 0 → MVP 1 → MVP 2**。第一条门禁直接复用现有 saved-scene PlayMode 测试；
第二条才补真正的 Simulator 6DoF Select。不要先重启 Operator，也不要用桌面坐标点击冒充 XR
输入。

达到以下条件后，可以合理地称为“Codex 在 PC 本地可无人值守完成开发测试闭环”：

- 一条命令能启动、测试、输出证据并彻底清理；
- 至少一条世界空间 UI 场景通过真实 6DoF + Select/Pinch 链；
- 失败可以由 XML、日志和业务证据明确分类；
- 视觉检查有稳定 baseline 和容差；
- 连续多轮运行没有焦点依赖、双击、超时或退出崩溃；
- Codex 能针对一次真实回归独立完成失败、修复和全量通过；
- Quest 真机门禁仍被明确保留。

这条路线能让 Codex 承担大部分日常工程迭代，但不会把“PC 上模拟成功”错误升级为“XR 产品
已经由 AI 完整验收”。
