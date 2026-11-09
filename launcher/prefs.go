package main

import (
	"fmt"
	"strconv"
	"strings"

	"golang.org/x/sys/windows/registry"
)

// 游戏把 Unity PlayerPrefs 写在 HKCU\Software\SOFTSTAR\The Nine Regions，
// 键名是「原文_h<djb2-xor 哈希>」。这里复刻旧启动器的读写口径。
const prefsSubKey = `Software\SOFTSTAR\The Nine Regions`

type Prefs struct {
	BGMVolume    float64 `json:"bgmVolume"`
	SEVolume     float64 `json:"seVolume"`
	BGMMute      bool    `json:"bgmMute"`
	SEMute       bool    `json:"seMute"`
	Quality      int     `json:"quality"`
	AntiAliasing bool    `json:"antiAliasing"`
	Bloom        bool    `json:"bloom"`
	FPSHigh      bool    `json:"fpsHigh"`
}

func prefsHash(key string) uint32 {
	var h uint32 = 5381
	for i := 0; i < len(key); i++ {
		h = (h * 33) & 0xFFFFFFFF
		h = (h ^ uint32(key[i])) & 0xFFFFFFFF
	}
	return h
}

func regName(key string) string {
	return key + "_h" + strconv.FormatUint(uint64(prefsHash(key)), 10)
}

func regOpen() (registry.Key, error) {
	return registry.OpenKey(registry.CURRENT_USER, prefsSubKey, registry.QUERY_VALUE|registry.SET_VALUE)
}

func readInt(key string, def int) int {
	k, err := regOpen()
	if err != nil {
		return def
	}
	defer k.Close()
	name := regName(key)
	if v, _, err := k.GetIntegerValue(name); err == nil {
		return int(v)
	}
	if s, _, err := k.GetStringValue(name); err == nil {
		if v, err := strconv.Atoi(strings.TrimSpace(s)); err == nil {
			return v
		}
	}
	return def
}

func writeInt(key string, value int) error {
	k, err := registry.OpenKey(registry.CURRENT_USER, prefsSubKey, registry.QUERY_VALUE|registry.SET_VALUE)
	if err != nil {
		k, _, err = registry.CreateKey(registry.CURRENT_USER, prefsSubKey, registry.QUERY_VALUE|registry.SET_VALUE)
		if err != nil {
			return err
		}
	}
	defer k.Close()
	return k.SetDWordValue(regName(key), uint32(value))
}

func readFloat(key string, def float64) float64 {
	k, err := regOpen()
	if err != nil {
		return def
	}
	defer k.Close()
	name := regName(key)
	if s, _, err := k.GetStringValue(name); err == nil {
		if v, err := strconv.ParseFloat(strings.TrimSpace(s), 64); err == nil {
			return v
		}
	}
	if v, _, err := k.GetIntegerValue(name); err == nil {
		return float64(v)
	}
	return def
}

func writeFloat(key string, value float64) error {
	k, err := registry.OpenKey(registry.CURRENT_USER, prefsSubKey, registry.QUERY_VALUE|registry.SET_VALUE)
	if err != nil {
		k, _, err = registry.CreateKey(registry.CURRENT_USER, prefsSubKey, registry.QUERY_VALUE|registry.SET_VALUE)
		if err != nil {
			return err
		}
	}
	defer k.Close()
	// Unity 在 Windows 上把 float 存成字符串。
	s := strconv.FormatFloat(value, 'f', -1, 64)
	return k.SetStringValue(regName(key), s)
}

func readBool(key string, def bool) bool {
	d := 0
	if def {
		d = 1
	}
	return readInt(key, d) != 0
}

func writeBool(key string, value bool) error {
	v := 0
	if value {
		v = 1
	}
	return writeInt(key, v)
}

func ReadPrefs() Prefs {
	return Prefs{
		BGMVolume:    clamp01(readFloat("BGMVolume", 1)),
		SEVolume:     clamp01(readFloat("SEVolume", 1)),
		BGMMute:      readBool("BGMMute", false),
		SEMute:       readBool("SEMute", false),
		Quality:      readInt("Quality", 3),
		AntiAliasing: readBool("AntiAliasing", true),
		Bloom:        readBool("Bloom", true),
		FPSHigh:      readBool("FPSHigh", true),
	}
}

func clamp01(v float64) float64 {
	if v < 0 {
		return 0
	}
	if v > 1 {
		return 1
	}
	return v
}

func (p Prefs) String() string {
	return fmt.Sprintf("BGM=%.2f SE=%.2f mute=%v/%v q=%d", p.BGMVolume, p.SEVolume, p.BGMMute, p.SEMute, p.Quality)
}
