package main

import (
	"io"
	"net/http"
	"strings"
	"time"
)

const statsURL = "http://127.0.0.1:4010/r/1.0.50/palrstats"

type AccountStat struct {
	Species int `json:"species"`
	Copies  int `json:"copies"`
	Tier    int `json:"tier"`
	Uid     int `json:"uid"`
	Level   int `json:"level"`
	Stages  int `json:"stages"`
	Money   int `json:"money"`
	Jade    int `json:"jade"`
	Star    int `json:"star"`
	Ok      bool `json:"ok"`
}

var rankNames = []string{
	"化身 壹", "化身 贰", "化身 叁", "人位 壹", "人位 贰", "人位 叁", "人位 肆", "人位 伍",
	"地位 壹", "地位 贰", "地位 叁", "地位 肆", "地位 伍",
	"天位 壹", "天位 贰", "天位 叁", "天位 肆", "天位 伍",
	"圣位 壹", "圣位 贰", "圣位 叁", "圣位 肆", "圣位 伍", "圣位 陆", "至尊",
}

func rankName(tier int) string {
	if tier <= 0 {
		return ""
	}
	if tier <= len(rankNames) {
		return rankNames[tier-1]
	}
	return "段位 " + itoa(tier)
}

func fetchStats(timeout time.Duration) map[string]AccountStat {
	out := map[string]AccountStat{}
	client := &http.Client{Timeout: timeout, Transport: &http.Transport{Proxy: nil}}
	resp, err := client.Get(statsURL)
	if err != nil {
		return out
	}
	defer resp.Body.Close()
	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return out
	}
	for _, raw := range strings.Split(string(body), "\n") {
		line := strings.TrimSpace(raw)
		if line == "" {
			continue
		}
		c := strings.Split(line, "\t")
		if len(c) < 4 {
			continue
		}
		st := AccountStat{Ok: true}
		st.Species = parseInt(c[1], 0)
		st.Copies = parseInt(c[2], 0)
		st.Tier = parseInt(c[3], 0)
		if len(c) >= 5 {
			st.Uid = parseInt(c[4], 0)
		}
		if len(c) >= 6 {
			st.Level = parseInt(c[5], 0)
		}
		if len(c) >= 7 {
			st.Stages = parseInt(c[6], 0)
		}
		if len(c) >= 8 {
			st.Money = parseInt(c[7], 0)
		}
		if len(c) >= 9 {
			st.Jade = parseInt(c[8], 0)
		}
		if len(c) >= 10 {
			st.Star = parseInt(c[9], 0)
		}
		out[c[0]] = st
	}
	return out
}
