# SignVR 电脑端工具

## 当前串流状态

玩家视角串流已在当前版本中停用。`VRSortingGame` 和
`CoopLiftWorkshop` 场景都不包含 `SpectatorViewStreamer`，
`StartSpectatorView.cmd` 也不会启动接收器。

`spectator_viewer.py` 和对应协议代码暂时保留为实验代码，方便以后重新研究，
但当前场景不会挂载或启动它们，也不应作为本轮验收方式。

## 骨架数据工具

原有骨架数据查看器与视频串流相互独立，可按需启动：

```powershell
cd E:\SignVR_Unity\ExternalClient
python .\motion_viewer.py --udp-port 5005 --web-port 8080
```

页面地址为 `http://127.0.0.1:8080`。它只显示关节和骨架数据，
不会显示 Quest 内部画面。
