# 旧 C# 启动器（参考）

这是九野便携版早期使用的 WPF 启动器源码，当前发布版已由 `../../launcher/`（MyGo）替代。
保留它是为了行为对照：账号库、PlayerPrefs、服务启停、启动凭据、Dummy 窗口隐藏等逻辑
都可以在这里找到原始实现。

- 目标框架：.NET Framework 4.8（WPF）
- 构建：`python build.py`（需要 Roslyn `csc.exe` 与 .NET 4.8 参考程序集）
- 不参与本仓库的 GitHub Actions 发布流程
