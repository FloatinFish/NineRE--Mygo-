package main

import (
	"context"
	"errors"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"time"

	"github.com/egoist/mygo"
	"github.com/jackc/pgx/v5/pgxpool"
)

type AccountView struct {
	Name       string `json:"name"`
	Registered bool   `json:"registered"`
	Level      int    `json:"level"`
	Last       int64  `json:"last"`
	Secs       int64  `json:"secs"`
	Uid        string `json:"uid"`
	HasStat    bool   `json:"hasStat"`
	Species    int    `json:"species"`
	Copies     int    `json:"copies"`
	Tier       int    `json:"tier"`
	TierName   string `json:"tierName"`
	StatLevel  int    `json:"statLevel"`
	Stages     int    `json:"stages"`
	Money      int    `json:"money"`
	Jade       int    `json:"jade"`
	Star       int    `json:"star"`
}

type Snapshot struct {
	Mode         string        `json:"mode"`
	Root         string        `json:"root"`
	GameDir      string        `json:"gameDir"`
	AssetBase    string        `json:"assetBase"`
	Accounts     []AccountView `json:"accounts"`
	Selected     string        `json:"selected"`
	Services     ServiceState  `json:"services"`
	Prefs        Prefs         `json:"prefs"`
	ScreenFull   int           `json:"screenFull"`
	ScreenW      int           `json:"screenW"`
	ScreenH      int           `json:"screenH"`
	GameRunning  bool          `json:"gameRunning"`
	Announcement string        `json:"announcement"`
	ServiceBackend string      `json:"serviceBackend"`
	StatsReady   bool          `json:"statsReady"`
	Error        string        `json:"error"`
}

// Launcher 是暴露给前端的绑定服务。
type Launcher struct {
	mu        sync.Mutex
	paths     Paths
	store     *Store
	selected  string
	assetDir  string
	assetBase string

	stats     map[string]AccountStat
	statsAt   time.Time
	dbMu      sync.Mutex
	dbPool    *pgxpool.Pool
	accountMu sync.Mutex
	svc       ServiceState
	svcAt     time.Time
	game      bool
	gameAt    time.Time
	starting  bool
}

func NewLauncher() *Launcher {
	p := DetectPaths()
	l := &Launcher{paths: p, store: NewStore(p), stats: map[string]AccountStat{}}
	l.assetDir = findAssetDir(p)
	takeoverLegacyLaunchers(p)
	return l
}

func findAssetDir(p Paths) string {
	cands := []string{
		filepath.Join(p.ExeDir, "assets"),
		filepath.Join(p.Root, "assets"),
	}
	if cwd, err := os.Getwd(); err == nil {
		cands = append(cands,
			filepath.Join(cwd, "assets"),
			filepath.Join(cwd, "..", "assets"),
			filepath.Join(cwd, "..", "..", "assets"),
		)
	}
	for _, c := range cands {
		if c == "" {
			continue
		}
		if exists(filepath.Join(c, "login_bg.mp4")) || exists(filepath.Join(c, "announce.txt")) {
			return c
		}
	}
	return ""
}

func (l *Launcher) SetAssetBase(base string) { l.assetBase = base }

func (l *Launcher) AssetBase() string { return l.assetBase }

func (l *Launcher) Snapshot() Snapshot {
	l.mu.Lock()
	defer l.mu.Unlock()
	return l.snapshotLocked()
}

func (l *Launcher) snapshotLocked() Snapshot {
	l.store.Load()

	if l.selected == "" && len(l.store.Accounts) > 0 {
		l.selected = l.store.Accounts[0].Name
	}
	if l.selected != "" && l.store.Find(l.selected) == nil {
		l.selected = ""
		if len(l.store.Accounts) > 0 {
			l.selected = l.store.Accounts[0].Name
		}
	}

	if time.Since(l.svcAt) > 1500*time.Millisecond {
		l.svc = checkServices(l.paths)
		l.svcAt = time.Now()
	}
	if time.Since(l.gameAt) > 2*time.Second {
		l.game = gameRunning()
		l.gameAt = time.Now()
	}
	statsReady := false
	if time.Since(l.statsAt) > 2*time.Second {
		if l.svc.Postgres {
			if st, err := l.fetchStatsDB(); err == nil {
				l.stats = st
				l.statsAt = time.Now()
			} else if l.svc.Http {
				l.stats = fetchStats(1500 * time.Millisecond)
				l.statsAt = time.Now()
			}
		} else if l.svc.Http {
			l.stats = fetchStats(1500 * time.Millisecond)
			l.statsAt = time.Now()
		}
	}
	statsReady = len(l.stats) > 0

	snap := Snapshot{
		Mode:        string(l.paths.Mode),
		Root:        l.paths.Root,
		GameDir:     l.paths.GameDir,
		AssetBase:   l.assetBase,
		Selected:    l.selected,
		Services:    l.svc,
		Prefs:       ReadPrefs(),
		ScreenFull:  l.store.ScreenFull,
		ScreenW:     l.store.ScreenW,
		ScreenH:     l.store.ScreenH,
		GameRunning: l.game,
		StatsReady:  statsReady,
	}
	if l.paths.Mode == ModeUnknown {
		snap.Error = "找不到游戏目录：请把启动器放在便携包根目录，或在 launcher.ini 里写 root=。"
	}
	for _, a := range l.store.Accounts {
		v := AccountView{
			Name: a.Name, Level: a.Level, Last: a.Last, Secs: a.Secs, Uid: a.Uid,
		}
		if st, ok := l.stats[a.Name]; ok {
			v.Registered = true
			v.HasStat = st.Ok
			v.Species, v.Copies, v.Tier = st.Species, st.Copies, st.Tier
			v.TierName = rankName(st.Tier)
			v.StatLevel, v.Stages = st.Level, st.Stages
			v.Money, v.Jade, v.Star = st.Money, st.Jade, st.Star
		}
		snap.Accounts = append(snap.Accounts, v)
	}
	snap.Announcement = l.announcementLocked()
	snap.ServiceBackend = readServiceBackend(l.paths)
	return snap
}

func (l *Launcher) announcementLocked() string {
	if l.assetDir == "" {
		return ""
	}
	b, err := os.ReadFile(filepath.Join(l.assetDir, "announce.txt"))
	if err != nil {
		return ""
	}
	return strings.TrimSpace(strings.ReplaceAll(string(b), "\r\n", "\n"))
}

func (l *Launcher) AddAccount(name, pwd string) (Snapshot, error) {
	name = strings.TrimSpace(name)
	if name == "" {
		return Snapshot{}, errors.New("账号名不能为空")
	}
	l.mu.Lock()
	defer l.mu.Unlock()
	l.store.Load()
	l.store.Upsert(name, pwd)
	if failed := l.store.Save(); len(failed) > 0 {
		return l.snapshotLocked(), errors.New("账号写入失败：" + failed[0])
	}
	l.selected = name
	return l.snapshotLocked(), nil
}

func (l *Launcher) DeleteAccount(name string) (Snapshot, error) {
	l.mu.Lock()
	defer l.mu.Unlock()
	l.store.Load()
	if gameRunning() {
		return l.snapshotLocked(), errors.New("游戏运行中不能删除账号")
	}
	if !l.store.Remove(name) {
		return l.snapshotLocked(), errors.New("没有找到这个账号")
	}
	if failed := l.store.Save(); len(failed) > 0 {
		return l.snapshotLocked(), errors.New("删除失败：" + failed[0])
	}
	l.selected = ""
	if len(l.store.Accounts) > 0 {
		l.selected = l.store.Accounts[0].Name
	}
	names := make([]string, 0, len(l.store.Accounts))
	for _, a := range l.store.Accounts {
		names = append(names, a.Name)
	}
	/** 2026-11-09：账号表对账改由 Go 直接写 accounts.json（不再经 Node /palracc）。 */
	l.pruneServerAccounts(names)
	return l.snapshotLocked(), nil
}

func (l *Launcher) SelectAccount(name string) Snapshot {
	l.mu.Lock()
	defer l.mu.Unlock()
	if l.store.Find(name) != nil {
		l.selected = name
	}
	return l.snapshotLocked()
}

func (l *Launcher) EnsureServices() (ServiceState, error) {
	l.mu.Lock()
	if l.starting {
		l.mu.Unlock()
		return checkServices(l.paths), nil
	}
	l.starting = true
	l.mu.Unlock()
	defer func() {
		l.mu.Lock()
		l.starting = false
		l.svcAt = time.Time{}
		l.mu.Unlock()
	}()
	if l.paths.Mode == ModeUnknown {
		return ServiceState{Message: "找不到游戏目录"}, errors.New("找不到游戏目录")
	}
	if err := ensureRuntime(l.paths); err != nil {
		l.mu.Lock()
		l.svc = ServiceState{Message: err.Error()}
		l.svcAt = time.Now()
		l.mu.Unlock()
		return l.svc, err
	}
	return ensureAll(l.paths, readServiceBackend(l.paths), nil), nil
}

func (l *Launcher) RestartServer() ServiceState {
	if err := ensureRuntime(l.paths); err != nil {
		return ServiceState{Message: err.Error()}
	}
	killServer()
	return ensureAll(l.paths, readServiceBackend(l.paths), nil)
}

// SetServiceBackend 选择本地服务端内核（node | go）。GO 分支仍在内测迭代，仅用于试验。
func (l *Launcher) SetServiceBackend(backend string) Snapshot {
	if err := writeServiceBackend(l.paths, backend); err != nil {
		return l.snapshotLocked()
	}
	return l.snapshotLocked()
}

func (l *Launcher) StopServices() {
	if l.paths.Mode == ModeUnknown {
		return
	}
	stopAll(l.paths)
}

func (l *Launcher) EnterGame(ctx context.Context, name string) (string, error) {
	l.mu.Lock()
	l.store.Load()
	a := l.store.Find(name)
	if a == nil {
		l.mu.Unlock()
		return "", errors.New("先选择一个账号")
	}
	accountName, accountPwd := a.Name, a.Pwd
	p := l.paths
	l.mu.Unlock()

	if gameRunning() {
		return "", errors.New("游戏已在运行")
	}
	failed := writeIntent(p, accountName, accountPwd)
	if len(failed) > 0 {
		return "", errors.New("启动凭据写入失败：" + failed[0])
	}
	clearState(p)
	l.mu.Lock()
	failedStore := l.store.Save()
	l.mu.Unlock()
	_ = failedStore // 不阻断启动，和旧启动器一致。
	pid, err := startGame(p)
	if err != nil {
		return "", errors.New("启动游戏失败：" + err.Error())
	}
	watchAndHideDummy(pid)
	if win := mygo.CallerWindow(ctx); win != nil {
		win.Minimize()
	}
	return "正在启动游戏…", nil
}

func (l *Launcher) SetVolume(kind string, value float64) Prefs {
	value = clamp01(value)
	switch kind {
	case "se":
		_ = writeFloat("SEVolume", value)
	default:
		_ = writeFloat("BGMVolume", value)
	}
	return ReadPrefs()
}

func (l *Launcher) SetMute(kind string, mute bool) Prefs {
	switch kind {
	case "se":
		_ = writeBool("SEMute", mute)
	default:
		_ = writeBool("BGMMute", mute)
	}
	return ReadPrefs()
}

func (l *Launcher) SetScreen(full, w, h int) Snapshot {
	l.mu.Lock()
	defer l.mu.Unlock()
	l.store.Load()
	l.store.SetScreen(full, w, h)
	_ = l.store.Save()
	return l.snapshotLocked()
}

func (l *Launcher) SetQuality(q int) Prefs {
	if q < 1 {
		q = 1
	}
	if q > 4 {
		q = 4
	}
	_ = writeInt("Quality", q)
	return ReadPrefs()
}

func (l *Launcher) SetToggle(key string, on bool) Prefs {
	switch key {
	case "AntiAliasing", "Bloom", "FPSHigh":
		_ = writeBool(key, on)
	}
	return ReadPrefs()
}

func (l *Launcher) Logs(mode string) string {
	path := l.paths.RunLog
	if mode == "pg" {
		path = l.paths.PgLog
	}
	return tail(path, 200, 64*1024)
}
