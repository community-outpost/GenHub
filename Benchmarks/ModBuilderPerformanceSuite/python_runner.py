#!/usr/bin/env python3
"""
Python ModBuilder Single-Thread Benchmark Runner
Implements the exact algorithms from GeneralsModBuilder (util.py, copy.py, engine.py)
under strict single-threaded execution for benchmarking against Go and C#.
"""

import os
import sys
import time
import struct
import hashlib
import pickle
import argparse
from typing import List, Dict, Tuple
from PIL import Image

def benchmark_md5_files(files: List[str], buffer_size: int = 64 * 1024) -> Tuple[float, int, List[dict]]:
    """Single-threaded MD5 hashing matching GeneralsModBuilder util.py:GetFileHash."""
    results = []
    total_bytes = 0
    t_start = time.perf_counter()
    
    for path in files:
        if not os.path.isfile(path):
            continue
        size = os.path.getsize(path)
        total_bytes += size
        
        md5_hash = hashlib.md5()
        with open(path, "rb") as f:
            while chunk := f.read(buffer_size):
                md5_hash.update(chunk)
                
        results.append({
            "path": path,
            "md5": md5_hash.hexdigest(),
            "size": size
        })
        
    t_end = time.perf_counter()
    return (t_end - t_start) * 1000.0, total_bytes, results


def benchmark_create_big(output_big_path: str, source_files: List[str], base_dir: str) -> Tuple[float, int]:
    """Single-threaded BIG archive creation matching GeneralsModBuilder BIG format."""
    t_start = time.perf_counter()
    
    sorted_files = sorted([f for f in source_files if os.path.isfile(f)])
    entries = []
    total_payload_size = 0
    
    for full_path in sorted_files:
        rel = os.path.relpath(full_path, base_dir).replace("\\", "/")
        with open(full_path, "rb") as f:
            data = f.read()
        entries.append({
            "rel": rel,
            "size": len(data),
            "data": data
        })
        total_payload_size += len(data)
        
    header_table_size = 16
    for e in entries:
        header_table_size += 4 + 4 + len(e["rel"].encode("ascii")) + 1
        
    total_archive_size = header_table_size + total_payload_size
    
    with open(output_big_path, "wb") as f:
        # Magic: BIG4
        f.write(b"BIG4")
        f.write(struct.pack(">III", total_archive_size, len(entries), header_table_size))
        
        current_offset = header_table_size
        for e in entries:
            e["offset"] = current_offset
            f.write(struct.pack(">II", current_offset, e["size"]))
            f.write(e["rel"].encode("ascii") + b"\x00")
            current_offset += e["size"]
            
        for e in entries:
            f.write(e["data"])
            
    t_end = time.perf_counter()
    return (t_end - t_start) * 1000.0, total_archive_size


def benchmark_compile_csf(output_csf_path: str, labels: List[Tuple[str, str]]) -> float:
    """Single-threaded CSF compilation matching GeneralsModBuilder CSF handling."""
    t_start = time.perf_counter()
    
    with open(output_csf_path, "wb") as f:
        # Header: Magic " FSC", Version 3, NumLabels, NumStrings, Unused 0, Language 0
        f.write(struct.pack("<4sIIIII", b" FSC", 3, len(labels), len(labels), 0, 0))
        
        for lbl_name, lbl_val in labels:
            lbl_bytes = lbl_name.encode("ascii")
            f.write(struct.pack("<4sII", b" LBL", 1, len(lbl_bytes)))
            f.write(lbl_bytes)
            
            val_chars = [ord(c) for c in lbl_val]
            inverted_bytes = bytearray()
            for c in val_chars:
                inv_c = (~c) & 0xFFFF
                inverted_bytes.extend(struct.pack("<H", inv_c))
                
            f.write(struct.pack("<4sI", b" STR", len(val_chars)))
            f.write(inverted_bytes)
            
    t_end = time.perf_counter()
    return (t_end - t_start) * 1000.0


def benchmark_image_resize_channel_split(input_image_path: str, output_image_path: str, target_w: int = 1024, target_h: int = 1024) -> float:
    """Single-threaded RGBA channel-split and resize matching GeneralsModBuilder copy.py:__ResizeImageWithParams."""
    t_start = time.perf_counter()
    
    with Image.open(input_image_path) as img:
        img = img.convert("RGBA")
        r, g, b, a = img.split()
        
        # Resize each channel independently with Bilinear interpolation
        r_resized = r.resize((target_w, target_h), Image.Resampling.BILINEAR)
        g_resized = g.resize((target_w, target_h), Image.Resampling.BILINEAR)
        b_resized = b.resize((target_w, target_h), Image.Resampling.BILINEAR)
        a_resized = a.resize((target_w, target_h), Image.Resampling.BILINEAR)
        
        merged = Image.merge("RGBA", (r_resized, g_resized, b_resized, a_resized))
        merged.save(output_image_path, format="TGA")
        
    t_end = time.perf_counter()
    return (t_end - t_start) * 1000.0


def benchmark_cache_serialization(cache_path: str, count: int = 2000) -> Tuple[float, float]:
    """Single-threaded cache serialization matching GeneralsModBuilder pickle format."""
    data = {}
    for i in range(count):
        key = f"Art/Textures/Texture_{i:04d}.dds"
        data[key] = {
            "path": key,
            "mtime": time.time(),
            "md5": "d41d8cd98f00b204e9800998ecf8427e",
            "params": {
                "format": "dds",
                "compression": "dxt5",
                "mipmaps": True
            }
        }
        
    # Write benchmark (Pickle HIGHEST_PROTOCOL as in GeneralsModBuilder)
    tStart = time.perf_counter()
    with open(cache_path, "wb") as f:
        pickle.dump(data, f, protocol=pickle.HIGHEST_PROTOCOL)
    write_ms = (time.perf_counter() - tStart) * 1000.0
    
    # Read benchmark
    tStart = time.perf_counter()
    with open(cache_path, "rb") as f:
        loaded = pickle.load(f)
    read_ms = (time.perf_counter() - tStart) * 1000.0
    
    return write_ms, read_ms


def main():
    parser = argparse.ArgumentParser(description="Python ModBuilder Single-Thread Benchmark Runner")
    parser.add_argument("--bench", default="all", choices=["md5", "big", "csf", "image", "cache", "all"])
    parser.add_argument("--data-dir", default="/tmp/modbuilder_test_dataset")
    parser.add_argument("--out-dir", default="/tmp/modbuilder_py_bench_out")
    parser.add_argument("-n", "--iterations", type=int, default=10)
    args = parser.parse_args()
    
    os.makedirs(args.out_dir, exist_ok=True)
    
    # Discover files
    files = []
    tga_files = []
    for root, _, filenames in os.walk(args.data_dir):
        for fn in filenames:
            p = os.path.join(root, fn)
            files.append(p)
            if fn.endswith(".tga"):
                tga_files.append(p)
                
    print(f"=== Python ModBuilder Single-Thread Benchmark Suite (CPython 3.10) ===")
    print(f"Dataset: {args.data_dir} ({len(files)} files)")
    print(f"Iterations: {args.iterations}\n")
    
    # 1. MD5 Hashing
    if args.bench in ("all", "md5"):
        times = []
        total_bytes = 0
        for _ in range(args.iterations):
            ms, tb, _ = benchmark_md5_files(files, 64 * 1024)
            times.append(ms)
            total_bytes = tb
        avg_ms = sum(times) / len(times)
        mb_proc = total_bytes / (1024 * 1024)
        th_mb_s = mb_proc / (avg_ms / 1000.0)
        th_files_s = len(files) / (avg_ms / 1000.0)
        print(f"[Python Micro] MD5 Hashing (64KB Buffer): Mean = {avg_ms:.2f} ms | Throughput = {th_mb_s:.2f} MB/s ({th_files_s:.1f} files/s)")
        
    # 2. BIG Archive Creation
    if args.bench in ("all", "big"):
        out_big = os.path.join(args.out_dir, "PythonBenchmarkOutput.big")
        times = []
        total_size = 0
        for _ in range(args.iterations):
            ms, sz = benchmark_create_big(out_big, files, args.data_dir)
            times.append(ms)
            total_size = sz
        avg_ms = sum(times) / len(times)
        mb_packed = total_size / (1024 * 1024)
        th_mb_s = mb_packed / (avg_ms / 1000.0)
        print(f"[Python Micro] BIG Packager: Mean = {avg_ms:.2f} ms | Output = {mb_packed:.2f} MB | Packing Throughput = {th_mb_s:.2f} MB/s")
        
    # 3. CSF String Table Compilation
    if args.bench in ("all", "csf"):
        labels = [
            (f"GUI:BenchmarkLabel_{i:05d}", f"Generals Strategic Unit Protocol {i:05d} Active and Ready")
            for i in range(2000)
        ]
        out_csf = os.path.join(args.out_dir, "PythonBenchmarkStrings.csf")
        times = []
        for _ in range(args.iterations):
            ms = benchmark_compile_csf(out_csf, labels)
            times.append(ms)
        avg_ms = sum(times) / len(times)
        th_lbl = len(labels) / (avg_ms / 1000.0)
        print(f"[Python Micro] CSF Table Compiler (2,000 labels): Mean = {avg_ms:.2f} ms | Throughput = {th_lbl:.1f} labels/s")
        
    # 4. RGBA Channel-Split Resizing (Image Processing)
    if args.bench in ("all", "image") and tga_files:
        test_img = tga_files[0]
        out_img = os.path.join(args.out_dir, "resized_python.tga")
        times = []
        for _ in range(args.iterations):
            ms = benchmark_image_resize_channel_split(test_img, out_img, 1024, 1024)
            times.append(ms)
        avg_ms = sum(times) / len(times)
        print(f"[Python Micro] Image RGBA Channel-Split Resize (Pillow): Mean = {avg_ms:.2f} ms/image")
        
    # 5. Cache Serialization (Pickle)
    if args.bench in ("all", "cache"):
        cache_path = os.path.join(args.out_dir, "cache.pickle")
        write_times, read_times = [], []
        for _ in range(args.iterations):
            w_ms, r_ms = benchmark_cache_serialization(cache_path, 2000)
            write_times.append(w_ms)
            read_times.append(r_ms)
        avg_w = sum(write_times) / len(write_times)
        avg_r = sum(read_times) / len(read_times)
        print(f"[Python Micro] Cache Serialization (2,000 entries): Pickle Write = {avg_w:.2f} ms | Pickle Read = {avg_r:.2f} ms")
        
    print("\nPython Benchmark Suite Run Completed Successfully.\n")


if __name__ == "__main__":
    main()
