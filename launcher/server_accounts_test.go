package main

import (
	"encoding/json"
	"os"
	"testing"
)

func TestPruneServerAccounts(t *testing.T) {
	dir := t.TempDir()
	p := Paths{ServerDir: dir}
	file := serverAccountsFile(p)

	seed := map[string]json.RawMessage{
		"A": json.RawMessage(`{"uid":1,"passwordMd5":"x","createdAt":1}`),
		"B": json.RawMessage(`{"uid":2,"passwordMd5":"y","createdAt":2,"fresh":true}`),
		"C": json.RawMessage(`{"uid":3,"passwordMd5":"z","createdAt":3}`),
	}
	if err := writeRawAccounts(file, seed); err != nil {
		t.Fatal(err)
	}

	l := &Launcher{paths: p}
	removed := l.pruneServerAccounts([]string{"A", "C"})
	if len(removed) != 1 || removed[0] != "B" {
		t.Fatalf("removed=%v, want [B]", removed)
	}

	b, err := os.ReadFile(file)
	if err != nil {
		t.Fatal(err)
	}
	var got map[string]json.RawMessage
	if err := json.Unmarshal(b, &got); err != nil {
		t.Fatal(err)
	}
	if len(got) != 2 || got["A"] == nil || got["C"] == nil || got["B"] != nil {
		t.Fatalf("accounts after prune=%v", got)
	}
	if _, err := os.Stat(file + ".bak"); err != nil {
		t.Fatalf("backup missing: %v", err)
	}

	// 空名单防呆：不能清空服务端账号表
	before := len(got)
	if l.pruneServerAccounts(nil) != nil {
		t.Fatal("empty keep list must be a no-op")
	}
	b, _ = os.ReadFile(file)
	got = nil
	_ = json.Unmarshal(b, &got)
	if len(got) != before {
		t.Fatalf("empty list changed accounts: %v", got)
	}

	// 单条删除
	if !l.deleteServerAccount("A") {
		t.Fatal("delete A failed")
	}
	if l.deleteServerAccount("A") {
		t.Fatal("delete A twice should be false")
	}
	b, _ = os.ReadFile(file)
	got = nil
	_ = json.Unmarshal(b, &got)
	if _, ok := got["A"]; ok {
		t.Fatalf("A still present: %v", got)
	}
}
