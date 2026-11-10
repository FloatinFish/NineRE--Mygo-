package main

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"net/url"
	"os"
	"path/filepath"
	"strconv"
	"time"

	"github.com/jackc/pgx/v5/pgxpool"
)

// 第一段接管：启动器直接读真实账号数据，不再依赖 Node 的 /palrstats。
// 数据源与 Node 的 PalrStatsRoute 完全一致：
//   Server/data/accounts.json  账号名 → uid
//   Server/data/ladder.json    uid → 段位
//   Server/data/pveProgress.json uid → PVE 进度
//   PostgreSQL mc.player / mc.player_info / mc.card_library
// 写入：删除/对账已由 Go 直接写 accounts.json（server_accounts.go）；Node 的
// AccountStore 每次读写前检查 mtime 自动重载。新增账号仍在首次登录时由游戏服
// /logindev 自动建档（战斗引擎逻辑未动）。

type dbConfig struct {
	Host     string `json:"host"`
	Username string `json:"username"`
	Password string `json:"password"`
	Database string `json:"database"`
	Port     int    `json:"port"`
}

type serverAccount struct {
	Uid int `json:"uid"`
}

type ladderRec struct {
	Level int `json:"level"`
}

type pveRec struct {
	Level  int               `json:"level"`
	Stages []json.RawMessage `json:"stages"`
}

func loadJSON(path string, v any) error {
	b, err := os.ReadFile(path)
	if err != nil {
		return err
	}
	return json.Unmarshal(b, v)
}

func (l *Launcher) closeDBPool() {
	l.dbMu.Lock()
	if l.dbPool != nil {
		l.dbPool.Close()
		l.dbPool = nil
	}
	l.dbMu.Unlock()
}

func (l *Launcher) dbPoolFor() (*pgxpool.Pool, error) {
	l.dbMu.Lock()
	defer l.dbMu.Unlock()
	if l.dbPool != nil {
		return l.dbPool, nil
	}
	cfgPath := filepath.Join(l.paths.ServerDir, "setting", "db.json")
	var cfg dbConfig
	if err := loadJSON(cfgPath, &cfg); err != nil {
		return nil, fmt.Errorf("读取 db.json 失败：%w", err)
	}
	host := cfg.Host
	if host == "" {
		host = "localhost"
	}
	port := cfg.Port
	if port == 0 {
		port = 5432
	}
	db := cfg.Database
	if db == "" {
		db = "postgres"
	}
	dsn := fmt.Sprintf("postgres://%s:%s@%s:%d/%s?sslmode=disable",
		url.QueryEscape(cfg.Username), url.QueryEscape(cfg.Password), host, port, db)
	ctx, cancel := context.WithTimeout(context.Background(), 3*time.Second)
	defer cancel()
	pool, err := pgxpool.New(ctx, dsn)
	if err != nil {
		return nil, err
	}
	if err := pool.Ping(ctx); err != nil {
		pool.Close()
		return nil, err
	}
	l.dbPool = pool
	return pool, nil
}

func (l *Launcher) fetchStatsDB() (map[string]AccountStat, error) {
	pool, err := l.dbPoolFor()
	if err != nil {
		return nil, err
	}
	ctx, cancel := context.WithTimeout(context.Background(), 2*time.Second)
	defer cancel()

	if err := pool.Ping(ctx); err != nil {
		l.closeDBPool()
		return nil, err
	}

	cards := map[string]struct{ species, copies int }{}
	if rows, err := pool.Query(ctx,
		`SELECT uid, COUNT(*)::int, COALESCE(SUM("count"),0)::int FROM mc.card_library GROUP BY uid`); err == nil {
		for rows.Next() {
			var uid string
			var species, copies int
			if rows.Scan(&uid, &species, &copies) == nil {
				cards[uid] = struct{ species, copies int }{species, copies}
			}
		}
		rows.Close()
	}

	players := map[string]struct{ money, jade, level int }{}
	rows, err := pool.Query(ctx, `SELECT id, money, jade, level FROM mc.player`)
	if err != nil {
		l.closeDBPool()
		return nil, err
	}
	for rows.Next() {
		var id string
		var money, jade, level int
		if rows.Scan(&id, &money, &jade, &level) == nil {
			players[id] = struct{ money, jade, level int }{money, jade, level}
		}
	}
	rows.Close()

	stars := map[string]int{}
	if rows, err := pool.Query(ctx, `SELECT uid, "ladderStar" FROM mc.player_info`); err == nil {
		for rows.Next() {
			var uid string
			var star int
			if rows.Scan(&uid, &star) == nil {
				stars[uid] = star
			}
		}
		rows.Close()
	}

	var accounts map[string]serverAccount
	if err := loadJSON(filepath.Join(l.paths.ServerDir, "data", "accounts.json"), &accounts); err != nil {
		if !errors.Is(err, os.ErrNotExist) {
			return nil, err
		}
		accounts = map[string]serverAccount{}
	}
	var ladder map[string]ladderRec
	_ = loadJSON(filepath.Join(l.paths.ServerDir, "data", "ladder.json"), &ladder)
	var pve map[string]pveRec
	_ = loadJSON(filepath.Join(l.paths.ServerDir, "data", "pveProgress.json"), &pve)

	out := map[string]AccountStat{}
	for name, rec := range accounts {
		uid := strconv.Itoa(rec.Uid)
		st := AccountStat{Uid: rec.Uid}
		if v, ok := cards[uid]; ok {
			st.Species, st.Copies = v.species, v.copies
		}
		if v, ok := players[uid]; ok {
			st.Money, st.Jade, st.Level = v.money, v.jade, v.level
			st.Ok = true
		}
		if v, ok := stars[uid]; ok {
			st.Star = v
		}
		if v, ok := ladder[uid]; ok {
			st.Tier = v.Level
		}
		if v, ok := pve[uid]; ok {
			if v.Level > 0 {
				st.Level = v.Level
			}
			st.Stages = len(v.Stages)
		}
		out[name] = st
	}
	return out, nil
}
