package main

import (
	"errors"
	"net/url"
	"runtime"
	"strings"

	"github.com/egoist/mygo"
)

// 反馈先落到主仓库的 Issues。以后如果有独立的 NineRE-Feedback 仓库，
// 只改这一行即可。
const feedbackIssueBase = "https://github.com/FloatinFish/NineRE--Mygo-/issues/new"

func feedbackURL(text string) (string, error) {
	text = strings.TrimSpace(text)
	if text == "" {
		return "", errors.New("反馈内容不能为空")
	}
	runes := []rune(text)
	if len(runes) > 1500 {
		text = string(runes[:1500]) + "…"
	}

	title := "反馈"
	for _, line := range strings.Split(text, "\n") {
		line = strings.TrimSpace(line)
		if line != "" {
			title = line
			break
		}
	}
	titleRunes := []rune(title)
	if len(titleRunes) > 60 {
		title = string(titleRunes[:60]) + "…"
	}

	body := text + "\n\n---\nNineRE Launcher " + appVersion + "\n平台: " + runtime.GOOS + "/" + runtime.GOARCH
	return feedbackIssueBase +
		"?title=" + url.QueryEscape("[反馈] "+title) +
		"&body=" + url.QueryEscape(body), nil
}

// SubmitFeedback 在默认浏览器打开预填好的 GitHub Issue 页面。
// 启动器不保存任何 GitHub 凭据；用户在浏览器里确认后提交。
func (l *Launcher) SubmitFeedback(text string) (string, error) {
	u, err := feedbackURL(text)
	if err != nil {
		return "", err
	}
	if err := mygo.Shell.OpenExternal(u); err != nil {
		return u, err
	}
	return u, nil
}
