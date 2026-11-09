package main

import (
	"crypto/rand"
	"encoding/hex"
	"fmt"
	"os"
	"os/exec"
	"strings"
	"syscall"
	"time"
	"unsafe"

	"golang.org/x/sys/windows"
)

// percentEncode 复刻旧启动器：name/pwd 里可能有空格、中文、制表符。
func percentEncode(s string) string {
	var b strings.Builder
	for i := 0; i < len(s); i++ {
		c := s[i]
		safe := (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
			(c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.' || c == '~'
		if safe {
			b.WriteByte(c)
		} else {
			fmt.Fprintf(&b, "%%%02X", c)
		}
	}
	return b.String()
}

func writeIntent(p Paths, name, pwd string) []string {
	var nonceBytes [6]byte
	_, _ = rand.Read(nonceBytes[:])
	nonce := hex.EncodeToString(nonceBytes[:])
	text := fmt.Sprintf("V\t1\nT\t%d\nN\t%s\nA\t%s\nP\t%s\n",
		nowUnix(), nonce, percentEncode(name), percentEncode(pwd))
	var failed []string
	for _, path := range []string{p.IntentGame(), p.IntentLocal()} {
		if path == "" {
			continue
		}
		if err := writeAtomic(path, text); err != nil {
			failed = append(failed, path+"："+err.Error())
		}
	}
	return failed
}

func clearState(p Paths) {
	for _, path := range []string{p.StateGame(), p.StateLocal()} {
		if path != "" {
			_ = os.Remove(path)
		}
	}
}

func startGame(p Paths) (int, error) {
	cmd := exec.Command(p.McExe())
	cmd.Dir = p.GameDir
	if err := cmd.Start(); err != nil {
		return 0, err
	}
	return cmd.Process.Pid, nil
}

func gameRunning() bool {
	cmd := hiddenCmd("tasklist", "/FI", "IMAGENAME eq MC.exe", "/NH")
	out, err := cmd.Output()
	if err != nil {
		return false
	}
	return strings.Contains(strings.ToLower(string(out)), "mc.exe")
}

var (
	user32                       = windows.NewLazySystemDLL("user32.dll")
	procEnumWindows              = user32.NewProc("EnumWindows")
	procGetWindowThreadProcessID = user32.NewProc("GetWindowThreadProcessId")
	procGetWindowTextW           = user32.NewProc("GetWindowTextW")
	procShowWindow               = user32.NewProc("ShowWindow")
)

// hideDummyWindows 隐藏游戏启动/播片时挂出来的标题为 Dummy 的辅助窗口。
func hideDummyWindows(pid int) int {
	hidden := 0
	cb := syscall.NewCallback(func(hwnd uintptr, _ uintptr) uintptr {
		var wpid uint32
		procGetWindowThreadProcessID.Call(hwnd, uintptr(unsafe.Pointer(&wpid)))
		if int(wpid) != pid {
			return 1
		}
		var buf [256]uint16
		n, _, _ := procGetWindowTextW.Call(hwnd, uintptr(unsafe.Pointer(&buf[0])), uintptr(len(buf)))
		if n > 0 && windows.UTF16ToString(buf[:n]) == "Dummy" {
			procShowWindow.Call(hwnd, 0)
			hidden++
		}
		return 1
	})
	procEnumWindows.Call(cb, 0)
	return hidden
}

func watchAndHideDummy(pid int) {
	go func() {
		deadline := time.Now().Add(2 * time.Minute)
		for time.Now().Before(deadline) {
			hideDummyWindows(pid)
			time.Sleep(700 * time.Millisecond)
		}
	}()
}
