package main

import (
	"fmt"
	"io"
	"log"
	"os"
	"path/filepath"
	"strings"
	"time"
)

// ensureRuntime 首次部署：数据库模板 → pgdata，补齐 Server/data 与最小配置。
// 便携整合包解压后第一次启动会走这里；重复调用是幂等的。
func ensureRuntime(p Paths) error {
	if p.Mode == ModeUnknown {
		return fmt.Errorf("找不到游戏目录：请把启动器放在游戏文件夹，或便携包根目录")
	}
	if !exists(p.ServerExe()) {
		return fmt.Errorf("缺少服务端：%s（请使用完整便携包）", p.ServerExe())
	}
	if !exists(filepath.Join(p.PgBin, "pg_ctl.exe")) {
		return fmt.Errorf("缺少 PostgreSQL：%s", p.PgBin)
	}

	if !exists(filepath.Join(p.PgData, "PG_VERSION")) {
		template := filepath.Join(p.Root, "pgdata-template")
		if !exists(filepath.Join(template, "PG_VERSION")) {
			return fmt.Errorf("缺少数据库模板 pgdata-template，无法初始化")
		}
		log.Printf("首次部署：复制 %s -> %s", template, p.PgData)
		if err := copyDir(template, p.PgData); err != nil {
			return fmt.Errorf("复制数据库模板失败：%w", err)
		}
		log.Printf("数据库模板已复制")
	}

	if !exists(filepath.Join(p.ServerDir, "data")) {
		if err := os.MkdirAll(filepath.Join(p.ServerDir, "data"), 0o755); err != nil {
			return fmt.Errorf("创建 Server/data 失败：%w", err)
		}
	}

	dbJSON := filepath.Join(p.ServerDir, "setting", "db.json")
	if !exists(dbJSON) {
		if err := os.MkdirAll(filepath.Dir(dbJSON), 0o755); err != nil {
			return fmt.Errorf("创建 Server/setting 失败：%w", err)
		}
		body := "{\n  \"host\": \"localhost\",\n  \"username\": \"postgres\",\n  \"password\": \"postgres\",\n  \"database\": \"postgres\"\n}\n"
		if err := os.WriteFile(dbJSON, []byte(body), 0o644); err != nil {
			return fmt.Errorf("写 db.json 失败：%w", err)
		}
		log.Printf("已写入默认 Server/setting/db.json")
	}

	envFile := filepath.Join(p.ServerDir, ".env")
	if !exists(envFile) {
		if err := os.WriteFile(envFile, []byte("DEFAULT_UID=10540\n"), 0o644); err != nil {
			return fmt.Errorf("写 Server/.env 失败：%w", err)
		}
	}
	return nil
}

// takeoverLegacyLaunchers 检测旧版 WPF 启动器，结束其进程并把文件移出便携包。
// 只处理根目录下体积 < 2MB 的已知文件名，避免误删新版 Go 启动器。
func takeoverLegacyLaunchers(p Paths) {
	if p.Root == "" {
		return
	}
	names := []string{"启动九野.exe", "PalrLauncher.exe", "九野启动器.exe"}
	for _, name := range names {
		path := filepath.Join(p.Root, name)
		info, err := os.Stat(path)
		if err != nil || info.IsDir() {
			continue
		}
		if info.Size() > 2*1024*1024 {
			continue // 新版 MyGo 约 12MB，不碰
		}
		base := strings.TrimSuffix(name, filepath.Ext(name))
		_ = hiddenCmd("taskkill", "/F", "/IM", base+".exe").Run()

		backupDir := filepath.Join(os.TempDir(), "NineRE-legacy")
		if err := os.MkdirAll(backupDir, 0o755); err != nil {
			log.Printf("旧版启动器接管：创建备份目录失败：%v", err)
			continue
		}
		dest := filepath.Join(backupDir, fmt.Sprintf("%s-%d%s", base, time.Now().Unix(), filepath.Ext(name)))
		if err := os.Rename(path, dest); err != nil {
			if data, readErr := os.ReadFile(path); readErr == nil {
				if writeErr := os.WriteFile(dest, data, 0o644); writeErr == nil {
					_ = os.Remove(path)
				}
			}
		}
		if exists(path) {
			log.Printf("旧版启动器接管：无法移除 %s（可能被占用）", path)
		} else {
			log.Printf("旧版启动器接管：%s -> %s", path, dest)
		}
	}
}

func copyDir(src, dst string) error {
	return filepath.Walk(src, func(path string, info os.FileInfo, err error) error {
		if err != nil {
			return err
		}
		rel, err := filepath.Rel(src, path)
		if err != nil {
			return err
		}
		target := filepath.Join(dst, rel)
		if info.IsDir() {
			return os.MkdirAll(target, info.Mode())
		}
		if !info.Mode().IsRegular() {
			return nil
		}
		return copyFile(path, target, info.Mode())
	})
}

func copyFile(src, dst string, mode os.FileMode) error {
	in, err := os.Open(src)
	if err != nil {
		return err
	}
	defer in.Close()
	if err := os.MkdirAll(filepath.Dir(dst), 0o755); err != nil {
		return err
	}
	out, err := os.OpenFile(dst, os.O_CREATE|os.O_WRONLY|os.O_TRUNC, mode)
	if err != nil {
		return err
	}
	defer out.Close()
	_, err = io.Copy(out, in)
	return err
}
