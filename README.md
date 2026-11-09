# NineRE Launcher (MyGo)

NineRE 便携版的启动器与便携版驱动文件。

启动器用 [MyGo](https://github.com/egoist/mygo)（Go + WebView2）编写：功能布局沿用旧 C# 版，
界面在 MyGo 前端里重写。更新通道走本仓库的 GitHub Releases：启动器检查最新 Release，
下载 `NineRE-Launcher.exe`，校验 `SHA256SUMS` 后自替换并重启。

## 仓库结构

```
launcher/                 MyGo 启动器源码（Go 绑定 + Vite 前端）
portable/                 便携包驱动文件（环境检查、服务监视器、服务启停脚本）
server/                   服务端说明（二进制与数据不在此仓库）
legacy/csharp-launcher/   旧 C# WPF 启动器（仅行为参考，不参与当前构建）
docs/UPDATE-CHANNEL.md    更新通道说明
.github/workflows/        构建与发布（tag v* → Release）
```

## 构建启动器

前置：Go 1.27+、Bun 1.4+、WebView2 Runtime（Windows 11 自带）。

```powershell
cd launcher
$env:GOFLAGS = "-buildvcs=false"
bun install
bun run build -- -skip-installer
```

产物：`launcher/build/windows-amd64/NineRE Launcher.exe`（约 12 MB，前端已嵌入）。

也可以直接用脚本：

```powershell
cd launcher
.\build.ps1 -DeployDir D:\NineRegions-Portable
```

`-DeployDir` 会把产物复制为便携包根目录的 `NineRE Launcher.exe`，不会覆盖现有启动器。

## 发布与更新通道

1. 修改版本号：`launcher/version.go` 的 `appVersion` 与 `launcher/mygo.config.ts` 的 `version` 保持一致。
2. 提交并打 tag：

   ```bash
   git tag v0.1.2
   git push origin main --tags
   ```

3. `.github/workflows/release.yml` 在 Windows runner 上构建，生成 Release，并上传：
   - `NineRE-Launcher.exe`（启动器本体 / 自动更新下载源）
   - `NineRE-Portable.zip`（手动上传的便携整合包）
   - `SHA256SUMS`

4. 已安装的启动器读取
   `https://api.github.com/repos/FloatinFish/NineRE--Mygo-/releases/latest`，
   比较 `tag_name` 与自身版本；有新版时按钮变为「更新到 vX.Y.Z」，确认后下载、校验、
   替换并重启。

详细流程与失败处理见 [`docs/UPDATE-CHANNEL.md`](docs/UPDATE-CHANNEL.md)。

## 反馈

启动器公告右侧的 GitHub 图标打开反馈弹窗；提交时在默认浏览器打开预填好的 Issue 页面
（当前指向主仓库 Issues），不需要在启动器内绑定账号。反馈正文会自动带
`NineRE Launcher <version>` 与平台信息。

## 便携整合包（傻瓜版）

`portable/tools/make-portable-zip.ps1` 从一份完整便携包组装 `NineRE-Portable.zip`：
启动器 + Server + pgsql + pgdata-template + tools + assets。用户把 ZIP 解压到游戏文件夹
（`MC.exe` 同级）或便携包根目录，首次启动会自动初始化数据库并拉起服务。

- `pgdata-template` 由 `portable/tools/make-clean-pgdata.ps1` 生成：`initdb` + `CREATE SCHEMA mc`，
  后端首次启动自动建表；不含任何玩家数据。
- 游戏客户端（`Game/`，约 3.2 GB）不包含在 ZIP 内，用户需自备。
- 旧版 WPF 启动器（根目录、体积 < 2 MB）会在首次启动时被接管并移到 `%TEMP%\NineRE-legacy`。

## 组装便携包

仓库不包含游戏本体、服务端二进制、PostgreSQL 与 `assets/`。把本仓库的产物放进便携包：

```
NineRegions-Portable/
├─ NineRE Launcher.exe                  ← launcher/build/windows-amd64/NineRE Launcher.exe
├─ assets/                       ← 背景视频、BGM、LOGO（随游戏包提供）
├─ Game/The Nine Regions/        ← 游戏客户端
├─ Server/                       ← nine-regions-backend.exe 与运行数据
├─ pgsql/                        ← 便携版 PostgreSQL
├─ pgdata/                       ← 数据库数据目录
└─ tools/                        ← portable/tools/ 的内容
```

`portable/` 里的文件按原路径覆盖：

- `portable/环境检查.bat` → 便携包根目录
- `portable/tools/env_setup.ps1` → `tools\env_setup.ps1`
- `portable/watch_client.ps1`、`portable/服务端监视器（可选）.vbs` → 便携包根目录
- `portable/tools/start-services.ps1` / `stop-services.ps1` / `check-services.ps1` → `tools\`

## 服务端

服务端二进制、数据库和游戏数据由便携包提供，不在本仓库。`server/README.md` 说明端口、
日志和独立启停脚本；`portable/tools/` 里的脚本可以在不启动启动器的情况下管理服务。

## 许可证

MIT，见 [LICENSE](LICENSE)。
