package main

import (
	"fmt"
	"os"
	"path/filepath"
	"sort"
	"strconv"
	"strings"
	"time"
)

// Account 是 palr_local_store.txt 里的一条账号记录。
type Account struct {
	Name  string `json:"name"`
	Pwd   string `json:"pwd"`
	Uid   string `json:"uid"`
	Level int    `json:"level"`
	Last  int64  `json:"last"`
	Secs  int64  `json:"secs"`
}

// Store 是账号库的两个镜像（游戏目录 + LocalLow），读取时按 name 合并，写入时双写。
type Store struct {
	Accounts   []Account `json:"accounts"`
	ScreenFull int       `json:"screenFull"`
	ScreenW    int       `json:"screenW"`
	ScreenH    int       `json:"screenH"`
	paths      Paths
}

func NewStore(p Paths) *Store {
	s := &Store{ScreenFull: 1, paths: p}
	s.Load()
	return s
}

func (s *Store) files() []string {
	out := []string{s.paths.StoreGame()}
	if f := s.paths.StoreLocal(); f != "" {
		out = append(out, f)
	}
	return out
}

func (s *Store) Load() {
	s.Accounts = nil
	index := map[string]int{}
	for _, f := range s.files() {
		var text string
		for attempt := 0; attempt < 3 && text == ""; attempt++ {
			b, err := os.ReadFile(f)
			if err == nil {
				text = string(b)
				break
			}
			time.Sleep(80 * time.Millisecond)
		}
		if text == "" {
			continue
		}
		for _, raw := range strings.Split(text, "\n") {
			line := strings.TrimSuffix(raw, "\r")
			if len(line) < 2 || line[1] != '\t' {
				continue
			}
			cols := strings.Split(line, "\t")
			switch line[0] {
			case 'A':
				if len(cols) < 7 {
					continue
				}
				a := Account{
					Name:  cols[1],
					Pwd:   cols[2],
					Uid:   cols[3],
					Level: parseInt(cols[4], 1),
					Last:  parseInt64(cols[5]),
					Secs:  parseInt64(cols[6]),
				}
				if a.Name == "" {
					continue
				}
				if i, ok := index[a.Name]; ok {
					if a.Last >= s.Accounts[i].Last {
						s.Accounts[i] = a
					}
				} else {
					index[a.Name] = len(s.Accounts)
					s.Accounts = append(s.Accounts, a)
				}
			case 'S':
				if len(cols) >= 4 {
					s.ScreenFull = parseInt(cols[1], 1)
					s.ScreenW = parseInt(cols[2], 0)
					s.ScreenH = parseInt(cols[3], 0)
				}
			}
		}
	}
	sort.SliceStable(s.Accounts, func(i, j int) bool { return s.Accounts[i].Last > s.Accounts[j].Last })
}

func (s *Store) Find(name string) *Account {
	for i := range s.Accounts {
		if s.Accounts[i].Name == name {
			return &s.Accounts[i]
		}
	}
	return nil
}

func (s *Store) Upsert(name, pwd string) {
	if a := s.Find(name); a != nil {
		if pwd != "" {
			a.Pwd = pwd
		}
		a.Last = nowUnix()
		return
	}
	s.Accounts = append([]Account{{Name: name, Pwd: pwd, Level: 1, Last: nowUnix()}}, s.Accounts...)
}

func (s *Store) Remove(name string) bool {
	for i := range s.Accounts {
		if s.Accounts[i].Name == name {
			s.Accounts = append(s.Accounts[:i], s.Accounts[i+1:]...)
			return true
		}
	}
	return false
}

func (s *Store) SetScreen(full, w, h int) {
	s.ScreenFull = full
	s.ScreenW = w
	s.ScreenH = h
}

// Save 双写账号库，返回写入失败的路径。
func (s *Store) Save() []string {
	var b strings.Builder
	b.WriteString("V\t1\n")
	for _, a := range s.Accounts {
		fmt.Fprintf(&b, "A\t%s\t%s\t%s\t%d\t%d\t%d\n",
			clean(a.Name), clean(a.Pwd), clean(a.Uid), a.Level, a.Last, a.Secs)
	}
	fmt.Fprintf(&b, "S\t%d\t%d\t%d\n", s.ScreenFull, s.ScreenW, s.ScreenH)
	text := b.String()

	var failed []string
	for _, f := range s.files() {
		if err := writeAtomic(f, text); err != nil {
			failed = append(failed, f+"："+err.Error())
			continue
		}
		back, err := os.ReadFile(f)
		if err != nil || string(back) != text {
			failed = append(failed, f+"（回读不一致）")
		}
	}
	return failed
}

func writeAtomic(path, text string) error {
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		return err
	}
	tmp := path + ".tmp"
	if err := os.WriteFile(tmp, []byte(text), 0o644); err != nil {
		return err
	}
	_ = os.Remove(path)
	return os.Rename(tmp, path)
}

func clean(s string) string {
	s = strings.ReplaceAll(s, "\t", " ")
	s = strings.ReplaceAll(s, "\r", " ")
	s = strings.ReplaceAll(s, "\n", " ")
	return strings.TrimSpace(s)
}

func parseInt(s string, def int) int {
	v, err := strconv.Atoi(strings.TrimSpace(s))
	if err != nil {
		return def
	}
	return v
}

func parseInt64(s string) int64 {
	v, err := strconv.ParseInt(strings.TrimSpace(s), 10, 64)
	if err != nil {
		return 0
	}
	return v
}

func nowUnix() int64 { return time.Now().Unix() }
