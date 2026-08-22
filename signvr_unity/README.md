# SignVR Quest 客户端

手语录制系统的 Quest 3 端。负责 Meta Pose 动作采集、HMD 内中文提示与录制状态、真镜像角色反馈、VR 内 Take 回看，以及与主机之间的 UDP 控制和 HTTP 上传。

主机端（Python 录制中枢 + React 操作台）在独立仓库 `vr-sign-host`。系统整体设计见 `Docs/VR-Sign-Capture-System-Design.md`。

## 环境

- Unity 6000.5.6f1
- 目标平台 Android / Quest 3，渲染管线 URP
- 构建场景只启用 `Assets/Scenes/Recording.unity`

## 未纳入版本控制的外部依赖

以下内容体积过大或可重新生成，克隆后需要自行还原，否则场景会出现丢失引用。

### Unity Japan Office 素材包（约 6.4 GB）

录制场景的房间、墙地面与固定家具素材。从 Unity 官方渠道下载后导入到 `Assets/UnityJapanOffice/`，目录结构保持原样，GUID 才能与场景引用对上。

素材包自带 `Tools -> UTJOffice` 菜单，但**不要**执行它的 `InitJapanOfficeSetup`——该命令会把项目切回 HDRP，本项目使用 URP。

### 光照烘焙产物（约 150 MB）

`Assets/Scenes/Recording/` 下的 Lightmap 与 `LightingData.asset`。按设计文档 10.8，最终烘焙必须在房间素材摆放完成后执行，因此仓库不保存中间产物。还原方式：

1. 摆放好 `Environment/ImportedEnvironment` 下的房间素材
2. 执行菜单 `SignVR/Lighting/Bake Recording Scene`

### UnitySkills 开发工具包（约 45 MB）

`Packages/com.besty.unity-skills/`。来源已由 `Packages/manifest.json` 声明，Unity 打开工程时会自动拉取。

## 构建

Android IL2CPP 开发包输出到 `Builds/Android/`（该目录不入库）。安装到 Quest 3 后，应用会在局域网内广播 UDP 公告等待主机配对。开始录制使用主机下发的绝对 Unix 毫秒时刻，并在 Pose 真正启动后返回第二次 ACK；主机失联或单条录制达到 10 分钟时，Quest 会把当前 Take 作为中断候选安全落盘。

正式产品身份为：

- Company：`SignVR`
- Product：`SignVR Recorder`
- Android Application ID：`com.signvr.recorder`
- Version：`1.0.0`，Version Code `1`

执行 Unity 菜单 `SignVR -> Release -> Apply Product Identity` 可统一写入这些设置。正式构建的签名密码不进入仓库；设置以下进程环境变量后执行 `SignVR -> Release -> Apply Android Signing From Environment`：

- `SIGNVR_ANDROID_KEYSTORE_PATH`
- `SIGNVR_ANDROID_KEYSTORE_PASSWORD`
- `SIGNVR_ANDROID_KEY_ALIAS`
- `SIGNVR_ANDROID_KEY_ALIAS_PASSWORD`

Keystore 必须在仓库外安全备份。以后所有升级包都必须使用同一密钥，否则 Quest 无法覆盖安装，卸载后还会丢失应用本地尚未上传的数据和设备绑定。

主机与 Quest 必须处于同一可信局域网，端口约定见主机仓库的 README。
