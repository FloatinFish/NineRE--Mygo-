package main

import (
	"os"
	"path/filepath"
	"strings"
)

// LayoutMode 表示启动器跑在便携包里还是开发仓库里。
type LayoutMode string

const (
	ModePortable   LayoutMode = "portable"
	ModeGameFolder LayoutMode = "gamefolder"
	ModeDev        LayoutMode = "dev"
	ModeUnknown    LayoutMode = "unknown"
)

// Paths 是启动器用到的全部目录与文件位置。
type Paths struct {
	Mode        LayoutMode `json:"mode"`
	ExeDir      string     `json:"exeDir"`
	Root        string     `json:"root"`
	GameDir     string     `json:"gameDir"`
	ServerDir   string     `json:"serverDir"`
	PgBin       string     `json:"pgBin"`
	PgData      string     `json:"pgData"`
	PgLog       string     `json:"pgLog"`
	RunLog      string     `json:"runLog"`
	LocalLowDir string     `json:"localLowDir"`
}

func (p Paths) McExe() string      { return filepath.Join(p.GameDir, "MC.exe") }
func (p Paths) ServerExe() string  { return filepath.Join(p.ServerDir, "nine-regions-backend.exe") }
func (p Paths) StoreGame() string  { return filepath.Join(p.GameDir, "File", "palr_local_store.txt") }
func (p Paths) StoreLocal() string {
	if p.LocalLowDir == "" {
		return ""
	}
	return filepath.Join(p.LocalLowDir, "palr_local_store.txt")
}
func (p Paths) IntentGame() string { return filepath.Join(p.GameDir, "File", "palr_launch.txt") }
func (p Paths) IntentLocal() string {
	if p.LocalLowDir == "" {
		return ""
	}
	return filepath.Join(p.LocalLowDir, "palr_launch.txt")
}
func (p Paths) StateGame() string { return filepath.Join(p.GameDir, "File", "palr_login_state.txt") }
func (p Paths) StateLocal() string {
	if p.LocalLowDir == "" {
		return ""
	}
	return filepath.Join(p.LocalLowDir, "palr_login_state.txt")
}

func exists(path string) bool {
	_, err := os.Stat(path)
	return err == nil
}

// DetectPaths 复刻旧启动器的目录探测：便携包根目录 / 开发仓库 / launcher.ini。
func DetectPaths() Paths {
	p := Paths{Mode: ModeUnknown}
	exeDir, err := os.Executable()
	if err == nil {
		exeDir = filepath.Dir(exeDir)
	} else {
		exeDir, _ = os.Getwd()
	}
	p.ExeDir = exeDir
	p.LocalLowDir = findLocalLow()

	if exists(filepath.Join(exeDir, "Game", "The Nine Regions", "MC.exe")) {
		return fillPortable(p, exeDir)
	}
	if exists(filepath.Join(exeDir, "MC.exe")) {
		// 解压到客户端文件夹：MC.exe 与 Server/、pgsql/、pgdata/ 同级。
		if exists(filepath.Join(exeDir, "Server", "nine-regions-backend.exe")) ||
			exists(filepath.Join(exeDir, "pgsql", "bin", "pg_ctl.exe")) {
			return fillGameFolder(p, exeDir)
		}
		return fillDev(p, filepath.Dir(exeDir), exeDir)
	}

	dir := exeDir
	for i := 0; i < 4 && dir != ""; i++ {
		if exists(filepath.Join(dir, "Game", "The Nine Regions", "MC.exe")) {
			return fillPortable(p, dir)
		}
		if exists(filepath.Join(dir, "The Nine Regions", "MC.exe")) {
			return fillDev(p, dir, filepath.Join(dir, "The Nine Regions"))
		}
		parent := filepath.Dir(dir)
		if parent == dir {
			break
		}
		dir = parent
	}

	ini := filepath.Join(exeDir, "launcher.ini")
	if b, err := os.ReadFile(ini); err == nil {
		for _, raw := range strings.Split(string(b), "\n") {
			t := strings.TrimSpace(raw)
			if len(t) > 5 && strings.EqualFold(t[:5], "root=") {
				root := strings.Trim(strings.TrimSpace(t[5:]), `"`)
				if exists(filepath.Join(root, "Game", "The Nine Regions", "MC.exe")) {
					return fillPortable(p, root)
				}
				if exists(filepath.Join(root, "The Nine Regions", "MC.exe")) {
					return fillDev(p, root, filepath.Join(root, "The Nine Regions"))
				}
			}
		}
	}
	return p
}

func fillPortable(p Paths, root string) Paths {
	p.Mode = ModePortable
	p.Root = root
	p.GameDir = filepath.Join(root, "Game", "The Nine Regions")
	p.ServerDir = filepath.Join(root, "Server")
	p.PgBin = filepath.Join(root, "pgsql", "bin")
	p.PgData = filepath.Join(root, "pgdata")
	p.PgLog = filepath.Join(root, "pgdata.log")
	p.RunLog = filepath.Join(p.ServerDir, "run.log")
	return p
}

func fillGameFolder(p Paths, root string) Paths {
	p.Mode = ModeGameFolder
	p.Root = root
	p.GameDir = root
	p.ServerDir = filepath.Join(root, "Server")
	p.PgBin = filepath.Join(root, "pgsql", "bin")
	p.PgData = filepath.Join(root, "pgdata")
	p.PgLog = filepath.Join(root, "pgdata.log")
	p.RunLog = filepath.Join(p.ServerDir, "run.log")
	return p
}

func fillDev(p Paths, root, gameDir string) Paths {
	p.Mode = ModeDev
	p.Root = root
	p.GameDir = gameDir
	p.ServerDir = filepath.Join(root, "NineRegions-server", "backend", "dist", "pack")
	if !exists(filepath.Join(p.ServerDir, "nine-regions-backend.exe")) {
		dev := filepath.Join(root, "NineRegions-server-dev", "backend", "dist", "pack")
		if exists(filepath.Join(dev, "nine-regions-backend.exe")) {
			p.ServerDir = dev
		}
	}
	p.PgBin = findPgBin(root)
	p.PgData = filepath.Join(root, "NineRegions-server", "backend", "database")
	p.PgLog = filepath.Join(root, "NineRegions-server", "backend", "db.log")
	p.RunLog = filepath.Join(p.ServerDir, "run.log")
	return p
}

func findPgBin(root string) string {
	if env := os.Getenv("PG_BIN"); env != "" && exists(filepath.Join(env, "pg_ctl.exe")) {
		return env
	}
	candidates := []string{
		filepath.Join(root, "pgsql", "bin"),
		`D:\Tools\postgresql-18.6-4-windows-x64-binaries\bin`,
		`C:\Program Files\PostgreSQL\18\bin`,
		`C:\Program Files\PostgreSQL\17\bin`,
		`C:\Program Files\PostgreSQL\16\bin`,
	}
	for _, c := range candidates {
		if exists(filepath.Join(c, "pg_ctl.exe")) {
			return c
		}
	}
	return candidates[0]
}

func findLocalLow() string {
	home, err := os.UserHomeDir()
	if err != nil {
		return ""
	}
	base := filepath.Join(home, "AppData", "LocalLow")
	direct := filepath.Join(base, "SOFTSTAR", "The Nine Regions")
	if exists(direct) {
		return direct
	}
	entries, err := os.ReadDir(base)
	if err == nil {
		for _, e := range entries {
			if !e.IsDir() {
				continue
			}
			cand := filepath.Join(base, e.Name(), "The Nine Regions")
			if exists(cand) {
				return cand
			}
		}
	}
	return direct
}
