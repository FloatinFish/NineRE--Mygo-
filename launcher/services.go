package main

import (
	"bytes"
	"net"
	"net/http"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"syscall"
	"time"

	"golang.org/x/sys/windows"
)

const (
	PgPort   = 5432
	WsPort   = 8800
	HttpPort = 4010
	verURL   = "http://127.0.0.1:4010/r/1.0.50/ver"
)

type ServiceState struct {
	Postgres bool   `json:"postgres"`
	Server   bool   `json:"server"`
	Http     bool   `json:"http"`
	Message  string `json:"message"`
	AllReady bool   `json:"allReady"`
}

func (s ServiceState) Ready() bool { return s.Postgres && s.Server && s.Http }

func hiddenCmd(name string, args ...string) *exec.Cmd {
	cmd := exec.Command(name, args...)
	cmd.SysProcAttr = &syscall.SysProcAttr{
		HideWindow:    true,
		CreationFlags: windows.CREATE_NO_WINDOW,
	}
	return cmd
}

func portOpen(port int, timeout time.Duration) bool {
	c, err := net.DialTimeout("tcp", "127.0.0.1:"+itoa(port), timeout)
	if err != nil {
		return false
	}
	_ = c.Close()
	return true
}

func httpOK(url string, timeout time.Duration) bool {
	client := &http.Client{
		Timeout:   timeout,
		Transport: &http.Transport{Proxy: nil},
	}
	resp, err := client.Get(url)
	if err != nil {
		return false
	}
	defer resp.Body.Close()
	return resp.StatusCode >= 200 && resp.StatusCode < 500
}

func pgReady(p Paths) bool {
	exe := filepath.Join(p.PgBin, "pg_isready.exe")
	if !exists(exe) {
		return portOpen(PgPort, 400*time.Millisecond)
	}
	cmd := hiddenCmd(exe, "-h", "127.0.0.1", "-p", "5432", "-U", "postgres")
	cmd.Dir = p.PgBin
	done := make(chan error, 1)
	go func() { done <- cmd.Run() }()
	select {
	case err := <-done:
		return err == nil
	case <-time.After(5 * time.Second):
		_ = cmd.Process.Kill()
		return false
	}
}

func ensurePostgres(p Paths, st *ServiceState) bool {
	if pgReady(p) {
		st.Postgres = true
		return true
	}
	if !exists(filepath.Join(p.PgBin, "pg_ctl.exe")) || !exists(p.PgData) {
		st.Message = "找不到 PostgreSQL（" + p.PgBin + "）或数据目录（" + p.PgData + "）"
		return false
	}
	cmd := hiddenCmd(filepath.Join(p.PgBin, "pg_ctl.exe"),
		"-D", p.PgData, "-l", p.PgLog, "-o", "-p 5432", "start")
	cmd.Dir = p.PgBin
	if err := cmd.Start(); err != nil {
		st.Message = "数据库启动失败：" + err.Error()
		return false
	}
	deadline := time.Now().Add(60 * time.Second)
	for time.Now().Before(deadline) {
		if pgReady(p) {
			st.Postgres = true
			return true
		}
		time.Sleep(400 * time.Millisecond)
	}
	st.Message = "数据库 60 秒内未就绪（看 pgdata.log）"
	return false
}

func ensureServer(p Paths, st *ServiceState, backend string) bool {
	if httpOK(verURL, 1200*time.Millisecond) && portOpen(WsPort, 400*time.Millisecond) {
		st.Server = true
		st.Http = true
		return true
	}
	exe := p.ServerExe()
	if backend == "go" {
		exe = p.GoServerExe()
	}
	if !exists(exe) {
		if backend == "go" {
			st.Message = "找不到 GO 服务端：" + exe + "（把 tools/backend-go/backend-go.exe 放到 Server 目录）"
		} else {
			st.Message = "找不到服务端：" + exe
		}
		return false
	}
	f, err := os.OpenFile(p.RunLog, os.O_APPEND|os.O_CREATE|os.O_WRONLY, 0o644)
	if err != nil {
		st.Message = "无法写 run.log：" + err.Error()
		return false
	}
	cmd := hiddenCmd(exe)
	cmd.Dir = p.ServerDir
	if backend == "go" {
		cmd.Env = append(os.Environ(),
			"HTTP_PORT=4010", "GAME_PORT=8800",
			"SERVER_DIR="+p.ServerDir, "GAME_VER=0.12.786")
	}
	cmd.Stdout = f
	cmd.Stderr = f
	if err := cmd.Start(); err != nil {
		_ = f.Close()
		st.Message = "服务端启动失败：" + err.Error()
		return false
	}
	_ = f.Close()
	deadline := time.Now().Add(60 * time.Second)
	for time.Now().Before(deadline) {
		if httpOK(verURL, 1200*time.Millisecond) && portOpen(WsPort, 400*time.Millisecond) {
			st.Server = true
			st.Http = true
			return true
		}
		time.Sleep(500 * time.Millisecond)
	}
	st.Message = "服务端 60 秒内未就绪（看 run.log）"
	return false
}

func killServer() int {
	for _, name := range []string{"nine-regions-backend.exe", "backend-go.exe"} {
		cmd := hiddenCmd("taskkill", "/F", "/IM", name)
		_ = cmd.Run()
	}
	deadline := time.Now().Add(8 * time.Second)
	for time.Now().Before(deadline) {
		if !portOpen(WsPort, 200*time.Millisecond) && !portOpen(HttpPort, 200*time.Millisecond) {
			break
		}
		time.Sleep(300 * time.Millisecond)
	}
	return 0
}

func stopAll(p Paths) {
	killServer()
	pgCtl := filepath.Join(p.PgBin, "pg_ctl.exe")
	if !exists(pgCtl) || !exists(p.PgData) {
		return
	}
	cmd := hiddenCmd(pgCtl, "-D", p.PgData, "-m", "fast", "stop")
	cmd.Dir = p.PgBin
	done := make(chan error, 1)
	go func() { done <- cmd.Run() }()
	select {
	case <-done:
	case <-time.After(15 * time.Second):
		_ = cmd.Process.Kill()
	}
}

func checkServices(p Paths) ServiceState {
	st := ServiceState{}
	st.Postgres = pgReady(p)
	st.Http = httpOK(verURL, 1200*time.Millisecond)
	st.Server = st.Http && portOpen(WsPort, 400*time.Millisecond)
	st.AllReady = st.Ready()
	if !st.Postgres {
		st.Message = "数据库未就绪"
	} else if !st.Http {
		st.Message = "游戏服未就绪"
	} else if !st.Server {
		st.Message = "游戏服 WS 未监听"
	}
	return st
}

func ensureAll(p Paths, backend string, progress func(string)) ServiceState {
	st := ServiceState{}
	if progress != nil {
		progress("检查本地服务…")
	}
	st.Postgres = pgReady(p)
	if !st.Postgres {
		if progress != nil {
			progress("正在启动数据库…")
		}
		ensurePostgres(p, &st)
	}
	if st.Postgres && !strings.HasPrefix(st.Message, "找不到") {
		if httpOK(verURL, 1200*time.Millisecond) && portOpen(WsPort, 400*time.Millisecond) {
			st.Http = true
			st.Server = true
		} else {
			if progress != nil {
				progress("正在启动游戏服…")
			}
			st.Message = ""
			ensureServer(p, &st, backend)
		}
	}
	st.AllReady = st.Ready()
	if st.AllReady {
		st.Message = ""
	}
	return st
}

func tail(path string, maxLines int, maxBytes int64) string {
	f, err := os.Open(path)
	if err != nil {
		return ""
	}
	defer f.Close()
	st, err := f.Stat()
	if err != nil {
		return ""
	}
	start := st.Size() - maxBytes
	if start < 0 {
		start = 0
	}
	if _, err := f.Seek(start, 0); err != nil {
		return ""
	}
	buf := make([]byte, st.Size()-start)
	n, _ := f.Read(buf)
	text := strings.ReplaceAll(string(buf[:n]), "\r\n", "\n")
	lines := strings.Split(text, "\n")
	if len(lines) > maxLines {
		lines = lines[len(lines)-maxLines:]
	}
	var b bytes.Buffer
	for _, l := range lines {
		if l == "" {
			continue
		}
		b.WriteString(l)
		b.WriteByte('\n')
	}
	return b.String()
}

func itoa(v int) string {
	if v == 0 {
		return "0"
	}
	var b [12]byte
	i := len(b)
	for v > 0 {
		i--
		b[i] = byte('0' + v%10)
		v /= 10
	}
	return string(b[i:])
}
