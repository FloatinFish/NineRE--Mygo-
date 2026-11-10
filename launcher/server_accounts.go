package main

import (
	"encoding/json"
	"log"
	"os"
	"path/filepath"
	"sort"
)

// 2026-11-09：账号表（Server/data/accounts.json）的写操作从 Node 收回到 Go。
//
// 读：account_db.go 直接读同一份文件（/palrstats 只作回退）。
// 写：启动器删除账号时，按启动器名单直接对账；Node 的 AccountStore 会在每次
// 读写前检查文件 mtime，看到 Go 的改动后自动重载，不会再拿内存旧表覆盖。
//
// 只保留账号登记，不动 mc.player 等数据库存档（与 Node /palracc 的语义一致，可恢复）。

func serverAccountsFile(p Paths) string {
	return filepath.Join(p.ServerDir, "data", "accounts.json")
}

func loadRawAccounts(file string) (map[string]json.RawMessage, error) {
	b, err := os.ReadFile(file)
	if err != nil {
		return nil, err
	}
	var m map[string]json.RawMessage
	if err := json.Unmarshal(b, &m); err != nil {
		return nil, err
	}
	if m == nil {
		m = map[string]json.RawMessage{}
	}
	return m, nil
}

func writeRawAccounts(file string, m map[string]json.RawMessage) error {
	b, err := json.MarshalIndent(m, "", "  ")
	if err != nil {
		return err
	}
	b = append(b, '\n')
	if err := os.MkdirAll(filepath.Dir(file), 0o755); err != nil {
		return err
	}
	// 先留一份可恢复备份；accounts.json 很小，直接覆盖写（Node 侧有 mtime 重载）。
	if old, err := os.ReadFile(file); err == nil {
		_ = os.WriteFile(file+".bak", old, 0o644)
	}
	return os.WriteFile(file, b, 0o644)
}

// pruneServerAccounts 按启动器名单删除服务端账号表里的幽灵条目。
// 名单为空时不做任何事（防呆：启动器账号库丢失时不能清空服务端）。
// 返回被删除的账号名。
func (l *Launcher) pruneServerAccounts(names []string) []string {
	keep := map[string]bool{}
	for _, n := range names {
		if n != "" {
			keep[n] = true
		}
	}
	if len(keep) == 0 {
		return nil
	}

	l.accountMu.Lock()
	defer l.accountMu.Unlock()

	file := serverAccountsFile(l.paths)
	m, err := loadRawAccounts(file)
	if err != nil {
		if !os.IsNotExist(err) {
			log.Printf("[accounts] 读取 %s 失败：%v", file, err)
		}
		return nil
	}

	removed := make([]string, 0)
	for name := range m {
		if !keep[name] {
			delete(m, name)
			removed = append(removed, name)
		}
	}
	if len(removed) == 0 {
		return nil
	}
	if err := writeRawAccounts(file, m); err != nil {
		log.Printf("[accounts] 写 %s 失败：%v", file, err)
		return nil
	}
	sort.Strings(removed)
	log.Printf("[accounts] Go 直接对账 accounts.json：保留 %d，删除 %v", len(keep), removed)
	return removed
}

// deleteServerAccount 删除单个服务端账号登记（供后续「彻底删除」入口使用）。
func (l *Launcher) deleteServerAccount(name string) bool {
	if name == "" {
		return false
	}
	l.accountMu.Lock()
	defer l.accountMu.Unlock()

	file := serverAccountsFile(l.paths)
	m, err := loadRawAccounts(file)
	if err != nil {
		return false
	}
	if _, ok := m[name]; !ok {
		return false
	}
	delete(m, name)
	if err := writeRawAccounts(file, m); err != nil {
		log.Printf("[accounts] 写 %s 失败：%v", file, err)
		return false
	}
	return true
}
