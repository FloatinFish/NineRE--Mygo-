# 九野启动器 · MyGo 版

用 MyGo（Go + WebView2）重写的九野便携版启动器。功能布局沿用 C# 版：
左侧 LOGO + 账号，中间账号详情，右侧画面/音量/服务设置，底部服务端控制台。
UI 样式在 MyGo 前端里重新实现（深色金箔 + 玻璃面板 + 背景视频）。

## 运行环境

- Go 1.27+
- Bun 1.4+（前端构建）
- WebView2 Runtime（Windows 11 自带；Windows 10 由便携包的「环境检查.bat」检测/安装）

## 构建

```powershell
# 在 launcher-mygo 目录
$env:GOFLAGS = "-buildvcs=false"   # 仓库外层没有可用 git 元数据时必须
bun run build -- -skip-installer
```

产物：`build/windows-amd64/九野启动器.exe`（约 12 MB，前端已嵌入）。

`-skip-installer` 跳过 NSIS 安装包；便携版只需要 exe。

## 部署

把 exe 放到便携包根目录（与 `Game/`、`Server/`、`assets/` 同级），
目录探测会自动进入 portable 模式：

```powershell
.\build.ps1 -DeployDir D:\NineRegions-Portable
```

默认复制为 `九野启动器-mygo.exe`，不会覆盖现有 `启动九野.exe`。

## 结构

```
main.go         窗口、单实例、资源回环 HTTP、退出时停服务
paths.go        便携包 / 开发仓库目录探测
store.go        账号库双镜像（palr_local_store.txt）
prefs.go        游戏 PlayerPrefs（HKCU 注册表，djb2-xor 键名）
services.go     PostgreSQL + 游戏服 启停/就绪/日志
stats.go        /palrstats 账号资料
launch.go       启动凭据、启动游戏、隐藏 Dummy 窗口
launcher.go     暴露给前端的绑定服务（mygo.Bind）
index.html      页面骨架
src/main.ts     交互与状态轮询
src/style.css   全部样式
```

视频/音乐走 `127.0.0.1` 回环 HTTP（MyGo 官方建议；WebKitGTK 不支持自定义协议播媒体）。
BGM 用 HTML `<audio>`，自动播放靠 `PageOptions.Autoplay: AutoplayAllow`。

## 与 C# 版的差异（尚未做）

- 「更新启动器」仍是占位（C# 版也是占位）。
- 未显示热修写回的登录状态（`palr_login_state.txt`）。
- 服务未就绪时不会自动展开控制台。
- 启动器自身不写 `launcher.log`，只写 `launcher-mygo.log`。

## 注意

- 关闭启动器会停掉 PostgreSQL 和游戏服（与 C# 版一致）。
- 构建时的 `-buildvcs=false` 是必须的：`PALReborn` 外层目录的 git 状态会让 Go 编译报
  `error obtaining VCS status`。
