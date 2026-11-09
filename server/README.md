# 服务端说明

本仓库不放服务端二进制、PostgreSQL 运行环境与数据库数据；这些由九野便携包提供。
这里放的是**驱动文件**：检查环境、启动/停止/检查服务，以及服务端监视器。

## 便携包里的服务端结构

```
NineRegions-Portable/
├─ Server/
│  ├─ nine-regions-backend.exe   游戏服（WS 8800 / HTTP 4010）
│  ├─ run.log                    服务端日志
│  └─ ...                        服务端运行数据
├─ pgsql/bin/                    PostgreSQL 可执行文件
├─ pgdata/                       数据库数据目录
├─ pgdata.log                    数据库日志
└─ tools/
   ├─ env_setup.ps1              环境检查 / WebView2 安装
   ├─ start-services.ps1         启动 PostgreSQL + 游戏服
   ├─ stop-services.ps1          停止两者
   └─ check-services.ps1         只检查状态
```

## 端口

| 端口 | 用途 |
| --- | --- |
| 5432 | PostgreSQL |
| 8800 | 游戏服 WebSocket |
| 4010 | 游戏服 HTTP（`/r/1.0.50/ver`、`/palrstats`） |

## 独立脚本

在便携包根目录执行：

```powershell
powershell -ExecutionPolicy Bypass -File tools\start-services.ps1
powershell -ExecutionPolicy Bypass -File tools\check-services.ps1
powershell -ExecutionPolicy Bypass -File tools\stop-services.ps1
```

脚本默认从自身位置向上两级推断便携包根目录；也可以用 `-Root D:\NineRegions-Portable` 指定。

## 启动器如何驱动服务

MyGo 启动器的 `launcher/services.go` 与上述脚本同口径：

1. `pg_isready` 或 5432 端口判断数据库；
2. `pg_ctl -D pgdata -l pgdata.log -o "-p 5432" start` 启动数据库；
3. `http://127.0.0.1:4010/r/1.0.50/ver` + 8800 端口判断游戏服；
4. 拉起 `nine-regions-backend.exe`，stdout/stderr 追加到 `Server/run.log`；
5. 关闭启动器时先结束游戏服，再 `pg_ctl -m fast stop` 停数据库。

## 监视器（可选）

`portable/watch_client.ps1` + `服务端监视器（可选）.vbs` 用于直接双击 `MC.exe` 的场景：
检测到客户端启动且端口未监听时，自动拉起数据库和服务端。由 VBS 以隐藏窗口运行。
