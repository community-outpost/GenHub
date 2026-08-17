package main

import (
	"crypto/md5"
	"encoding/binary"
	"encoding/hex"
	"encoding/json"
	"flag"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"runtime"
	"sort"
	"strings"
	"time"
)

// Single-thread constraint
func init() {
	runtime.GOMAXPROCS(1)
}

type FileHashResult struct {
	Path string `json:"path"`
	MD5  string `json:"md5"`
	Size int64  `json:"size"`
}

func BenchmarkMD5Files(files []string, bufferSize int) (time.Duration, int64, []FileHashResult) {
	buf := make([]byte, bufferSize)
	results := make([]FileHashResult, 0, len(files))
	var totalBytes int64 = 0

	start := time.Now()
	for _, path := range files {
		f, err := os.Open(path)
		if err != nil {
			continue
		}
		stat, _ := f.Stat()
		size := stat.Size()
		totalBytes += size

		h := md5.New()
		_, _ = io.CopyBuffer(h, f, buf)
		f.Close()

		results = append(results, FileHashResult{
			Path: path,
			MD5:  hex.EncodeToString(h.Sum(nil)),
			Size: size,
		})
	}
	elapsed := time.Since(start)
	return elapsed, totalBytes, results
}

type BIGEntry struct {
	Offset   uint32
	Size     uint32
	RelPath  string
	FullData []byte
}

func BenchmarkCreateBIG(outputBigPath string, sourceFiles []string, baseDir string) (time.Duration, int64) {
	start := time.Now()

	entries := make([]BIGEntry, 0, len(sourceFiles))
	var totalPayloadSize uint32 = 0

	// 1. Collect and sort paths
	sort.Strings(sourceFiles)

	for _, fullPath := range sourceFiles {
		rel, err := filepath.Rel(baseDir, fullPath)
		if err != nil {
			rel = filepath.Base(fullPath)
		}
		rel = strings.ReplaceAll(rel, "\\", "/")

		data, err := os.ReadFile(fullPath)
		if err != nil {
			continue
		}

		entries = append(entries, BIGEntry{
			Size:     uint32(len(data)),
			RelPath:  rel,
			FullData: data,
		})
		totalPayloadSize += uint32(len(data))
	}

	// 2. Calculate header size
	// Header: 16 bytes. Each entry: 4 (offset) + 4 (size) + len(RelPath) + 1 (null byte)
	var headerTableSize uint32 = 16
	for _, entry := range entries {
		headerTableSize += 4 + 4 + uint32(len(entry.RelPath)) + 1
	}

	totalArchiveSize := headerTableSize + totalPayloadSize

	// 3. Write BIG archive
	f, err := os.Create(outputBigPath)
	if err != nil {
		return 0, 0
	}
	defer f.Close()

	// Magic: BIG4
	f.Write([]byte("BIG4"))
	binary.Write(f, binary.BigEndian, totalArchiveSize)
	binary.Write(f, binary.BigEndian, uint32(len(entries)))
	binary.Write(f, binary.BigEndian, headerTableSize)

	// Write Index Table
	currentOffset := headerTableSize
	for i := range entries {
		entries[i].Offset = currentOffset
		binary.Write(f, binary.BigEndian, currentOffset)
		binary.Write(f, binary.BigEndian, entries[i].Size)
		f.WriteString(entries[i].RelPath)
		f.Write([]byte{0})
		currentOffset += entries[i].Size
	}

	// Write Payloads
	for _, entry := range entries {
		f.Write(entry.FullData)
	}

	elapsed := time.Since(start)
	return elapsed, int64(totalArchiveSize)
}

type CSFLabel struct {
	Name  string
	Value string
}

func BenchmarkCompileCSF(outputCsfPath string, labels []CSFLabel) time.Duration {
	start := time.Now()

	f, err := os.Create(outputCsfPath)
	if err != nil {
		return 0
	}
	defer f.Close()

	// Header: Magic " FSC" (0x43534620), Version 3, NumLabels, NumStrings, Unused 0, Language 0
	f.Write([]byte(" FSC"))
	binary.Write(f, binary.LittleEndian, uint32(3))
	binary.Write(f, binary.LittleEndian, uint32(len(labels)))
	binary.Write(f, binary.LittleEndian, uint32(len(labels)))
	binary.Write(f, binary.LittleEndian, uint32(0))
	binary.Write(f, binary.LittleEndian, uint32(0))

	for _, lbl := range labels {
		// LBL chunk
		f.Write([]byte(" LBL"))
		binary.Write(f, binary.LittleEndian, uint32(1))
		nameBytes := []byte(lbl.Name)
		binary.Write(f, binary.LittleEndian, uint32(len(nameBytes)))
		f.Write(nameBytes)

		// STR chunk with ~c inverted UTF-16LE characters
		f.Write([]byte(" STR"))
		runes := []rune(lbl.Value)
		binary.Write(f, binary.LittleEndian, uint32(len(runes)))
		for _, r := range runes {
			inv := uint16(^uint16(r))
			binary.Write(f, binary.LittleEndian, inv)
		}
	}

	return time.Since(start)
}

type CacheItem struct {
	Path   string                 `json:"path"`
	Mtime  int64                  `json:"mtime"`
	MD5    string                 `json:"md5"`
	Params map[string]interface{} `json:"params"`
}

func BenchmarkCacheSerialization(cachePath string, count int) (time.Duration, time.Duration) {
	data := make(map[string]CacheItem, count)
	for i := 0; i < count; i++ {
		key := fmt.Sprintf("Art/Textures/Texture_%04d.dds", i)
		data[key] = CacheItem{
			Path:  key,
			Mtime: time.Now().Unix(),
			MD5:   "d41d8cd98f00b204e9800998ecf8427e",
			Params: map[string]interface{}{
				"format":      "dds",
				"compression": "dxt5",
				"mipmaps":     true,
			},
		}
	}

	// Write benchmark
	tStart := time.Now()
	bytes, _ := json.Marshal(data)
	os.WriteFile(cachePath, bytes, 0644)
	writeTime := time.Since(tStart)

	// Read benchmark
	tStart = time.Now()
	readBytes, _ := os.ReadFile(cachePath)
	var loaded map[string]CacheItem
	json.Unmarshal(readBytes, &loaded)
	readTime := time.Since(tStart)

	return writeTime, readTime
}

func main() {
	benchType := flag.String("bench", "all", "Benchmark type: md5, big, csf, cache, e2e, all")
	dataDir := flag.String("data-dir", "/tmp/modbuilder_test_dataset", "Input dataset directory")
	outDir := flag.String("out-dir", "/tmp/modbuilder_go_bench_out", "Output directory")
	iterations := flag.Int("n", 10, "Number of iterations")
	flag.Parse()

	os.MkdirAll(*outDir, 0755)

	// Discover files
	var files []string
	filepath.Walk(*dataDir, func(path string, info os.FileInfo, err error) error {
		if err == nil && !info.IsDir() {
			files = append(files, path)
		}
		return nil
	})

	fmt.Printf("=== Go ModBuilder Single-Thread Benchmark Suite (GOMAXPROCS=1) ===\n")
	fmt.Printf("Dataset: %s (%d files)\n", *dataDir, len(files))
	fmt.Printf("Iterations: %d\n\n", *iterations)

	// 1. MD5 Hashing
	if *benchType == "all" || *benchType == "md5" {
		var totalElapsed time.Duration
		var totalBytes int64
		for i := 0; i < *iterations; i++ {
			el, b, _ := BenchmarkMD5Files(files, 64*1024)
			totalElapsed += el
			totalBytes = b
		}
		avgTimeMs := float64(totalElapsed.Milliseconds()) / float64(*iterations)
		mbProcessed := float64(totalBytes) / (1024 * 1024)
		thMBs := mbProcessed / (avgTimeMs / 1000.0)
		thFiles := float64(len(files)) / (avgTimeMs / 1000.0)
		fmt.Printf("[Go Micro] MD5 Hashing (64KB Buffer): Mean = %.2f ms | Throughput = %.2f MB/s (%.1f files/s)\n", avgTimeMs, thMBs, thFiles)
	}

	// 2. BIG Archive Creation
	if *benchType == "all" || *benchType == "big" {
		outBig := filepath.Join(*outDir, "GoBenchmarkOutput.big")
		var totalElapsed time.Duration
		var totalSize int64
		for i := 0; i < *iterations; i++ {
			el, sz := BenchmarkCreateBIG(outBig, files, *dataDir)
			totalElapsed += el
			totalSize = sz
		}
		avgTimeMs := float64(totalElapsed.Milliseconds()) / float64(*iterations)
		mbPacked := float64(totalSize) / (1024 * 1024)
		thMBs := mbPacked / (avgTimeMs / 1000.0)
		fmt.Printf("[Go Micro] BIG Packager: Mean = %.2f ms | Output = %.2f MB | Packing Throughput = %.2f MB/s\n", avgTimeMs, mbPacked, thMBs)
	}

	// 3. CSF String Table Compilation
	if *benchType == "all" || *benchType == "csf" {
		labels := make([]CSFLabel, 2000)
		for i := 0; i < 2000; i++ {
			labels[i] = CSFLabel{
				Name:  fmt.Sprintf("GUI:BenchmarkLabel_%05d", i),
				Value: fmt.Sprintf("Generals Strategic Unit Protocol %05d Active and Ready", i),
			}
		}
		outCsf := filepath.Join(*outDir, "GoBenchmarkStrings.csf")
		var totalElapsed time.Duration
		for i := 0; i < *iterations; i++ {
			el := BenchmarkCompileCSF(outCsf, labels)
			totalElapsed += el
		}
		avgTimeMs := float64(totalElapsed.Milliseconds()) / float64(*iterations)
		thLabels := float64(len(labels)) / (avgTimeMs / 1000.0)
		fmt.Printf("[Go Micro] CSF Table Compiler (2,000 labels): Mean = %.2f ms | Throughput = %.1f labels/s\n", avgTimeMs, thLabels)
	}

	// 4. Cache Serialization
	if *benchType == "all" || *benchType == "cache" {
		cachePath := filepath.Join(*outDir, "cache.json")
		var totalWrite, totalRead time.Duration
		for i := 0; i < *iterations; i++ {
			w, r := BenchmarkCacheSerialization(cachePath, 2000)
			totalWrite += w
			totalRead += r
		}
		avgWriteMs := float64(totalWrite.Milliseconds()) / float64(*iterations)
		avgReadMs := float64(totalRead.Milliseconds()) / float64(*iterations)
		fmt.Printf("[Go Micro] Cache Serialization (2,000 entries): JSON Write = %.2f ms | JSON Read = %.2f ms\n", avgWriteMs, avgReadMs)
	}

	fmt.Printf("\nGo Benchmark Suite Run Completed Successfully.\n")
}
