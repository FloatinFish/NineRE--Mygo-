package main

import (
	"strings"
	"testing"
)

func TestFeedbackURL(t *testing.T) {
	if _, err := feedbackURL("   "); err == nil {
		t.Fatal("empty feedback should fail")
	}
	u, err := feedbackURL("希望增加一个快速重连按钮\n第二行")
	if err != nil {
		t.Fatal(err)
	}
	if !strings.HasPrefix(u, feedbackIssueBase+"?title=") {
		t.Fatalf("unexpected base: %s", u)
	}
	if !strings.Contains(u, "%5B%E5%8F%8D%E9%A6%88%5D") {
		t.Fatalf("missing [反馈] title prefix: %s", u)
	}
	if !strings.Contains(u, "NineRE+Launcher+"+appVersion) {
		t.Fatalf("missing version signature: %s", u)
	}
	if !strings.Contains(u, "%E5%B8%8C%E6%9C%9B") {
		t.Fatalf("missing encoded body: %s", u)
	}
}
