package main

import (
	"bufio"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"time"

	"github.com/egoist/mygo"
)

const (
	repoSlug      = "FloatinFish/NineRE--Mygo-"
	releaseAPI    = "https://api.github.com/repos/" + repoSlug + "/releases/latest"
	launcherAsset = "NineRE-Starter.exe"
	sumsAsset     = "SHA256SUMS"
)

type UpdateInfo struct {
	Available bool   `json:"available"`
	Current   string `json:"current"`
	Version   string `json:"version"`
	Notes     string `json:"notes"`
	URL       string `json:"url"`
	SumsURL   string `json:"sumsUrl"`
	AssetName string `json:"assetName"`
	AssetSize int64  `json:"assetSize"`
	Error     string `json:"error"`
}

// CheckUpdate 读取 GitHub 仓库的最新 Release，并与 appVersion 比较。
func (l *Launcher) CheckUpdate() UpdateInfo {
	info := UpdateInfo{Current: appVersion}
	client := &http.Client{Timeout: 8 * time.Second}
	req, err := http.NewRequest("GET", releaseAPI, nil)
	if err != nil {
		info.Error = "检查更新失败：" + err.Error()
		return info
	}
	req.Header.Set("Accept", "application/vnd.github+json")
	req.Header.Set("User-Agent", "NineRE-Starter/"+appVersion)
	resp, err := client.Do(req)
	if err != nil {
		info.Error = "检查更新失败：" + err.Error()
		return info
	}
	defer resp.Body.Close()
	if resp.StatusCode == http.StatusNotFound {
		return info // 仓库还没有发布 Release
	}
	if resp.StatusCode != http.StatusOK {
		info.Error = fmt.Sprintf("检查更新失败：HTTP %d", resp.StatusCode)
		return info
	}

	var release struct {
		TagName string `json:"tag_name"`
		Body    string `json:"body"`
		Assets  []struct {
			Name string `json:"name"`
			Size int64  `json:"size"`
			URL  string `json:"browser_download_url"`
		} `json:"assets"`
	}
	if err := json.NewDecoder(resp.Body).Decode(&release); err != nil {
		info.Error = "检查更新失败：" + err.Error()
		return info
	}
	info.Version = strings.TrimPrefix(release.TagName, "v")
	info.Notes = strings.TrimSpace(release.Body)
	for _, a := range release.Assets {
		switch a.Name {
		case launcherAsset:
			info.URL, info.AssetName, info.AssetSize = a.URL, a.Name, a.Size
		case sumsAsset:
			info.SumsURL = a.URL
		}
	}
	info.Available = info.URL != "" && versionGreater(info.Version, appVersion)
	return info
}

// ApplyUpdate 下载新版本，写一个等待本进程退出的替换脚本，然后退出让脚本替换并重启。
func (l *Launcher) ApplyUpdate(downloadURL, sumsURL string) (string, error) {
	if gameRunning() {
		return "", errors.New("游戏运行中不能更新启动器")
	}
	if !trustedDownloadURL(downloadURL) {
		return "", errors.New("更新地址不合法")
	}

	tmp := filepath.Join(os.TempDir(), "NineRE-Starter-update.exe")
	if err := downloadTo(downloadURL, tmp, 5*time.Minute); err != nil {
		return "", errors.New("下载更新失败：" + err.Error())
	}
	if sumsURL != "" && trustedDownloadURL(sumsURL) {
		expected, err := fetchExpectedHash(sumsURL, launcherAsset)
		if err == nil && expected != "" {
			actual, err := fileSHA256(tmp)
			if err != nil || !strings.EqualFold(actual, expected) {
				_ = os.Remove(tmp)
				return "", errors.New("更新包校验失败，已取消更新")
			}
		}
	}

	exe, err := os.Executable()
	if err != nil {
		return "", errors.New("找不到当前启动器路径：" + err.Error())
	}
	exe, _ = filepath.Abs(exe)

	helper := filepath.Join(os.TempDir(), "NineRE-Starter-update.cmd")
	script := fmt.Sprintf(`@echo off
setlocal
:wait
tasklist /FI "PID eq %d" 2>nul | find "%d" >nul
if not errorlevel 1 (
  timeout /t 1 /nobreak >nul
  goto wait
)
copy /y "%s" "%s" >nul
if errorlevel 1 (
  timeout /t 2 /nobreak >nul
  copy /y "%s" "%s" >nul
)
start "" "%s"
del "%%~f0"
`, os.Getpid(), os.Getpid(), tmp, exe, tmp, exe, exe)
	if err := os.WriteFile(helper, []byte(script), 0o644); err != nil {
		return "", errors.New("写入更新脚本失败：" + err.Error())
	}

	cmd := hiddenCmd("cmd.exe", "/c", helper)
	if err := cmd.Start(); err != nil {
		return "", errors.New("启动更新脚本失败：" + err.Error())
	}
	go func() {
		time.Sleep(900 * time.Millisecond)
		mygo.App.Quit()
	}()
	return "更新已开始，启动器将自动重启", nil
}

func trustedDownloadURL(raw string) bool {
	u, err := url.Parse(raw)
	if err != nil || u.Scheme != "https" {
		return false
	}
	host := strings.ToLower(u.Hostname())
	return host == "github.com" || strings.HasSuffix(host, ".githubusercontent.com")
}

func downloadTo(rawURL, path string, timeout time.Duration) error {
	client := &http.Client{Timeout: timeout}
	req, err := http.NewRequest("GET", rawURL, nil)
	if err != nil {
		return err
	}
	req.Header.Set("User-Agent", "NineRE-Starter/"+appVersion)
	resp, err := client.Do(req)
	if err != nil {
		return err
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		return fmt.Errorf("HTTP %d", resp.StatusCode)
	}
	f, err := os.Create(path)
	if err != nil {
		return err
	}
	defer f.Close()
	if _, err := io.Copy(f, resp.Body); err != nil {
		return err
	}
	return nil
}

func fetchExpectedHash(rawURL, name string) (string, error) {
	client := &http.Client{Timeout: 15 * time.Second}
	req, err := http.NewRequest("GET", rawURL, nil)
	if err != nil {
		return "", err
	}
	req.Header.Set("User-Agent", "NineRE-Starter/"+appVersion)
	resp, err := client.Do(req)
	if err != nil {
		return "", err
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		return "", fmt.Errorf("HTTP %d", resp.StatusCode)
	}
	sc := bufio.NewScanner(resp.Body)
	for sc.Scan() {
		fields := strings.Fields(sc.Text())
		if len(fields) >= 2 && fields[1] == name {
			return strings.ToLower(fields[0]), nil
		}
	}
	return "", errors.New("SHA256SUMS 里没有 " + name)
}

func fileSHA256(path string) (string, error) {
	f, err := os.Open(path)
	if err != nil {
		return "", err
	}
	defer f.Close()
	h := sha256.New()
	if _, err := io.Copy(h, f); err != nil {
		return "", err
	}
	return hex.EncodeToString(h.Sum(nil)), nil
}

func versionParts(s string) []int {
	s = strings.TrimPrefix(strings.TrimSpace(s), "v")
	if i := strings.IndexAny(s, "-+"); i >= 0 {
		s = s[:i]
	}
	var out []int
	for _, p := range strings.Split(s, ".") {
		n, _ := strconv.Atoi(p)
		out = append(out, n)
	}
	return out
}

func versionGreater(a, b string) bool {
	pa, pb := versionParts(a), versionParts(b)
	n := len(pa)
	if len(pb) > n {
		n = len(pb)
	}
	for i := 0; i < n; i++ {
		var x, y int
		if i < len(pa) {
			x = pa[i]
		}
		if i < len(pb) {
			y = pb[i]
		}
		if x != y {
			return x > y
		}
	}
	return false
}
