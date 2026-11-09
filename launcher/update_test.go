package main

import "testing"

func TestVersionGreater(t *testing.T) {
	cases := []struct {
		a, b string
		want bool
	}{
		{"0.1.1", "0.1.0", true},
		{"v0.1.1", "0.1.0", true},
		{"0.1.0", "0.1.0", false},
		{"0.1.0", "0.1.1", false},
		{"1.0.0", "0.9.9", true},
		{"0.10.0", "0.9.0", true},
		{"0.1", "0.1.0", false},
		{"0.2.0-beta.1", "0.1.9", true},
	}
	for _, c := range cases {
		if got := versionGreater(c.a, c.b); got != c.want {
			t.Fatalf("versionGreater(%q,%q)=%v want %v", c.a, c.b, got, c.want)
		}
	}
}

func TestTrustedDownloadURL(t *testing.T) {
	ok := []string{
		"https://github.com/FloatinFish/NineRE--Mygo-/releases/download/v0.1.1/NineRE-Launcher.exe",
		"https://objects.githubusercontent.com/x/y.exe",
		"https://api.github.com/repos/FloatinFish/NineRE--Mygo-/releases/assets/123",
	}
	bad := []string{
		"http://github.com/x/y.exe",
		"https://example.com/y.exe",
		"https://github.com.evil.example/y.exe",
		"file:///C:/y.exe",
	}
	for _, u := range ok {
		if !trustedDownloadURL(u) {
			t.Fatalf("trustedDownloadURL(%q)=false, want true", u)
		}
	}
	for _, u := range bad {
		if trustedDownloadURL(u) {
			t.Fatalf("trustedDownloadURL(%q)=true, want false", u)
		}
	}
}
