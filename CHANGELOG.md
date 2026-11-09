# Changelog

## 0.1.4 - 2026-11-09

- 公告旁新增 GitHub 反馈入口（Octocat 图标）。
- 弹窗输入反馈，一键在浏览器打开预填好的 GitHub Issue；启动器不保存 GitHub 凭据。
- 反馈自动带 NineRE Launcher 版本与平台信息。

## 0.1.3 - 2026-11-09

- 首次启动自动部署：`pgdata-template` → `pgdata`，补 `Server/data` 与默认 `setting/db.json`。
- 支持解压到游戏文件夹的新布局：`MC.exe` 与 `Server/`、`pgsql/`、`pgdata/` 同级。
- 旧版 WPF 启动器自动接管：结束其进程并移到 `%TEMP%\NineRE-legacy`。
- 新增 `portable/tools/make-clean-pgdata.ps1` 与 `make-portable-zip.ps1`。
- Release 只保留 `NineRE-Launcher.exe`、`NineRE-Portable.zip` 与 `SHA256SUMS`；移除 0.1.0/0.1.1 兼容副本。

## 0.1.2 - 2026-11-09

- 启动器正式定名 NineRE Launcher；窗口标题、应用名、exe 名称同步更新。
- 更新下载改用 GitHub API 资产端点（`Accept: application/octet-stream`），
  在 `github.com` 下载被阻断但 `api.github.com` 可达的网络下仍可更新。
- 发布携带 `NineRE-Launcher.exe`，以及 `NineRE-Starter.exe`（0.1.1 兼容）
  与 `NineRegionsLauncher.exe`（0.1.0 兼容）。

## 0.1.1 - 2026-11-09

- 启动器更名，窗口标题、应用名、exe 名称同步更新。
- 更新资产包含主程序与 0.1.0 兼容副本。
- 文档与界面文字统一使用 NineRE。

## 0.1.0 - 2026-11-09

- 首个 MyGo 启动器版本：沿用 C# 版功能布局，界面在 MyGo 前端重写。
- 账号库双镜像、游戏 PlayerPrefs 音量/画质、PostgreSQL + 游戏服启停与就绪。
- 背景视频 + BGM 预览（游戏音量联动，进入游戏时停止）。
- 服务端控制台、公告、画面设置、进入游戏、关窗停全部服务。
- 更新通道：检查本仓库最新 Release，下载、SHA256 校验、自替换重启。
