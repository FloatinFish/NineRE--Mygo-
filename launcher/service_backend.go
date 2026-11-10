package main

import (
	"encoding/json"
	"os"
	"path/filepath"
)

// 本地服务端内核选择：node（默认、完整）| go（实验，仍在内测迭代）。
// 独立小文件保存，避免污染游戏共用的 palr_local_store.txt。
type serviceBackendFile struct {
	Backend string `json:"backend"`
}

func serviceBackendPath(p Paths) string {
	root := p.Root
	if root == "" {
		root = p.ExeDir
	}
	return filepath.Join(root, "launcher_service.json")
}

func readServiceBackend(p Paths) string {
	b, err := os.ReadFile(serviceBackendPath(p))
	if err != nil {
		return "node"
	}
	var f serviceBackendFile
	if err := json.Unmarshal(b, &f); err != nil {
		return "node"
	}
	if f.Backend == "go" {
		return "go"
	}
	return "node"
}

func writeServiceBackend(p Paths, backend string) error {
	if backend != "go" {
		backend = "node"
	}
	b, _ := json.MarshalIndent(serviceBackendFile{Backend: backend}, "", "  ")
	return os.WriteFile(serviceBackendPath(p), append(b, '\n'), 0o644)
}
