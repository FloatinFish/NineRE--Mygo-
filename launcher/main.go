package main

import (
	"log"
	"net"
	"net/http"
	"os"
	"path/filepath"

	"github.com/egoist/mygo"
)

var mainWindow *mygo.Window

func main() {
	l := NewLauncher()
	setupLog(l.paths.ExeDir)
	log.Printf("NineRE Launcher 启动：mode=%s root=%s exeDir=%s", l.paths.Mode, l.paths.Root, l.paths.ExeDir)

	// 大体积的 mp4/m4a 不走 WebView2 的自定义协议，走 127.0.0.1 回环 HTTP。
	if l.assetDir != "" {
		if ln, err := net.Listen("tcp", "127.0.0.1:0"); err == nil {
			l.SetAssetBase("http://" + ln.Addr().String())
			go func() {
				_ = http.Serve(ln, http.FileServer(http.Dir(l.assetDir)))
			}()
			log.Printf("资源服务：%s -> %s", l.assetBase, l.assetDir)
		} else {
			log.Printf("资源服务启动失败：%v", err)
		}
	} else {
		log.Printf("找不到 assets 目录，背景视频/音乐将不可用")
	}

	if !mygo.App.RequestSingleInstanceLock() {
		return
	}

	mygo.Bind(l)
	mygo.App.OnSecondInstance(func([]string, string) {
		if mainWindow != nil {
			mainWindow.Show()
			mainWindow.Focus()
		}
	})

	mygo.App.WhenReady(func() {
		mainWindow = mygo.NewWindow(mygo.WindowOptions{
			Title:           "NineRE Launcher",
			Width:           1180,
			Height:          720,
			MinWidth:        1000,
			MinHeight:       640,
			DisableResize:   true,
			DisableMaximize: true,
			Frameless:       true,
			BackgroundColor: "#0b0d10",
			StateKey:        "main",
			Page:            mygo.PageOptions{Autoplay: mygo.AutoplayAllow, ZoomFactor: 1},
			URL:             "/",
		})
	})

	if err := mygo.App.Run(); err != nil {
		log.Printf("运行失败：%v", err)
		log.Fatal(err)
	}
	// 与旧启动器一致：关闭启动器时停掉全部本地服务。
	l.StopServices()
	log.Printf("启动器已退出")
}

func setupLog(dir string) {
	if dir == "" {
		return
	}
	f, err := os.OpenFile(filepath.Join(dir, "launcher-mygo.log"), os.O_APPEND|os.O_CREATE|os.O_WRONLY, 0o644)
	if err != nil {
		return
	}
	log.SetOutput(f)
	log.SetFlags(log.LstdFlags)
}
