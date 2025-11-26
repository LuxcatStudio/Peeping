# Peeping
可以通过网页看到你面前在干什么，支持手机和win主机
手机应用=>有人会写的，相信我

# win端：
请先以控制台应用运行（测试环境）
```
.\ActiveWindowMonitor.exe debug
```
目前在写托盘应用并利用IPC连接服务。
解决服务运行在 Session 0，无法访问用户桌面上的窗口管理器、调用 GetForegroundWindow() 问题。
