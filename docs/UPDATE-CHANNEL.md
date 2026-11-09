# 更新通道

启动器把本仓库的 GitHub Releases 当作更新源。仓库公开，启动器无需 Token。

## 发布一个版本

1. 同步版本号：
   - `launcher/version.go`：`const appVersion = "0.1.1"`
   - `launcher/mygo.config.ts`：`version: "0.1.1"`
2. 提交。
3. 打 tag 并推送：

   ```bash
   git tag v0.1.1
   git push origin main --tags
   ```

4. GitHub Actions（`.github/workflows/release.yml`）在 `windows-latest` 上：
   - `bun install`
   - `bun run build -- -skip-installer`
   - 把 `build/windows-amd64/NineRE Launcher.exe` 复制为 `dist/NineRE-Launcher.exe`；便携整合包 `NineRE-Portable.zip` 由 `make-portable-zip.ps1` 生成后手动上传
   - 生成 `dist/SHA256SUMS`
   - 用 `softprops/action-gh-release` 创建 Release 并上传全部资产

## 启动器如何检查

`launcher/update.go` 请求：

```
GET https://api.github.com/repos/FloatinFish/NineRE--Mygo-/releases/latest
Accept: application/vnd.github+json
User-Agent: NineRE-Launcher/<version>
```

- 解析 `tag_name`（去掉前缀 `v`）与 `body`；
- 在 `assets[]` 里找 `NineRE-Launcher.exe` 与 `SHA256SUMS`；旧版（0.1.1）找 `NineRE-Starter.exe`，旧版（0.1.0）找 `NineRegionsLauncher.exe`，发布时三份都带；
- 下载用资产的 `url`（`https://api.github.com/repos/.../releases/assets/<id>`）并带 `Accept: application/octet-stream`，不依赖 `browser_download_url`；`github.com` 被阻断但 `api.github.com` 可达时仍能更新；
- 按语义化数字段比较 `tag_name` 与 `appVersion`；
- 有新版时返回 `available=true`，前端把「更新启动器」按钮改为「更新到 vX.Y.Z」。

检查在启动 2.5 秒后后台执行一次；手动点按钮时再检查。

## 启动器如何安装

1. 用户确认后，`ApplyUpdate` 下载 exe 到 `%TEMP%\NineRE-Launcher-update.exe`；
2. 若 Release 带 `SHA256SUMS`，计算 SHA256 并比对；不一致则取消；
3. 在 `%TEMP%` 写一个替换脚本，等待当前进程退出后：
   - `copy /y` 覆盖当前 exe；
   - `start ""` 重启；
   - 删除脚本自身；
4. 启动器调用 `mygo.App.Quit()`，`App.Run` 返回后停掉本地服务；
5. 新版本启动时重新拉起服务。

游戏运行中不允许更新（`ApplyUpdate` 会返回错误），避免把服务端在游戏中停掉。

## 失败与边界

| 情况 | 行为 |
| --- | --- |
| 仓库没有 Release | `available=false`，不报错 |
| GitHub API 限流 / 断网 | 返回 `error`，手动检查时提示 |
| Release 没有 exe 资产 | `available=false` |
| SHA256 不匹配 | 取消更新，删除临时文件 |
| 下载地址不是 github.com / githubusercontent.com | 拒绝 |
| 替换时 exe 仍被占用 | 脚本重试一次；仍失败则新版未启动，旧版文件保留 |
| 游戏运行中 | 拒绝更新 |

## 安全边界

- 只接受 HTTPS 且主机为 `github.com` 或 `*.githubusercontent.com` 的下载地址；
- 用 `SHA256SUMS` 校验下载内容；
- 没有做代码签名。若要进一步收紧，可改用 MyGo 内置的签名更新（`mygo keygen` +
  `updates.publicKey`），但那需要把私钥放进 CI Secret，并改由 `mygo build -upload` 发布。
