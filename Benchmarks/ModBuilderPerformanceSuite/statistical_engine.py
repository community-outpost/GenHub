#!/usr/bin/env python3
"""
Statistical Analysis & Telemetry Engine
Captures high-precision execution timings, CPU time, memory RSS, page faults, and I/O counters.
Computes statistical distributions, confidence intervals, Welch's t-test, speedup ratios, and parity validation.
"""

import os
import sys
import time
import math
import resource
import struct
import hashlib
from typing import List, Dict, Any, Tuple, Optional
from dataclasses import dataclass, field, asdict

@dataclass
class ProcessMetrics:
    wall_time_ms: float
    user_cpu_time_ms: float
    sys_cpu_time_ms: float
    total_cpu_time_ms: float
    cpu_utilization_percent: float
    peak_rss_mb: float
    minor_page_faults: int
    major_page_faults: int
    read_bytes: int
    write_bytes: int
    read_syscalls: int
    write_syscalls: int
    items_processed: int = 0
    bytes_processed: int = 0
    throughput_mb_s: float = 0.0
    throughput_items_s: float = 0.0


@dataclass
class StatisticalSummary:
    name: str
    sample_size: int
    mean: float
    std_dev: float
    cv_percent: float
    median: float
    p90: float
    p95: float
    p99: float
    min_val: float
    max_val: float
    ci95_lower: float
    ci95_upper: float
    peak_rss_mb: float
    cpu_util_mean: float
    throughput_mb_s_mean: float
    throughput_items_s_mean: float


class TelemetryCollector:
    """Collects OS-level process and child resource telemetry."""
    
    @staticmethod
    def _read_proc_io(pid: int) -> Tuple[int, int, int, int]:
        """Reads read_bytes, write_bytes, syscr, syscw from /proc/[pid]/io."""
        rbytes, wbytes, syscr, syscw = 0, 0, 0, 0
        io_path = f"/proc/{pid}/io"
        if os.path.exists(io_path):
            try:
                with open(io_path, "r") as f:
                    for line in f:
                        if line.startswith("read_bytes:"):
                            rbytes = int(line.split()[1])
                        elif line.startswith("write_bytes:"):
                            wbytes = int(line.split()[1])
                        elif line.startswith("syscr:"):
                            syscr = int(line.split()[1])
                        elif line.startswith("syscw:"):
                            syscw = int(line.split()[1])
            except (IOError, PermissionError):
                pass
        return rbytes, wbytes, syscr, syscw

    @classmethod
    def measure_callable(cls, fn, *args, items: int = 0, data_bytes: int = 0, **kwargs) -> Tuple[Any, ProcessMetrics]:
        """Executes a callable, measuring exact wall-clock and rusage telemetry."""
        pid = os.getpid()
        rbytes_start, wbytes_start, syscr_start, syscw_start = cls._read_proc_io(pid)
        
        usage_self_start = resource.getrusage(resource.RUSAGE_SELF)
        usage_children_start = resource.getrusage(resource.RUSAGE_CHILDREN)
        
        t_start = time.perf_counter_ns()
        result = fn(*args, **kwargs)
        t_end = time.perf_counter_ns()
        
        usage_self_end = resource.getrusage(resource.RUSAGE_SELF)
        usage_children_end = resource.getrusage(resource.RUSAGE_CHILDREN)
        rbytes_end, wbytes_end, syscr_end, syscw_end = cls._read_proc_io(pid)
        
        wall_sec = (t_end - t_start) / 1e9
        wall_ms = wall_sec * 1000.0
        
        user_sec = (
            (usage_self_end.ru_utime - usage_self_start.ru_utime) +
            (usage_children_end.ru_utime - usage_children_start.ru_utime)
        )
        sys_sec = (
            (usage_self_end.ru_stime - usage_self_start.ru_stime) +
            (usage_children_end.ru_stime - usage_children_start.ru_stime)
        )
        total_cpu_sec = user_sec + sys_sec
        cpu_util = (total_cpu_sec / wall_sec * 100.0) if wall_sec > 0 else 0.0
        
        # On Linux, ru_maxrss is in Kilobytes
        peak_rss_mb = max(usage_self_end.ru_maxrss, usage_children_end.ru_maxrss) / 1024.0
        
        minor_flt = (
            (usage_self_end.ru_minflt - usage_self_start.ru_minflt) +
            (usage_children_end.ru_minflt - usage_children_start.ru_minflt)
        )
        major_flt = (
            (usage_self_end.ru_majflt - usage_self_start.ru_majflt) +
            (usage_children_end.ru_majflt - usage_children_start.ru_majflt)
        )
        
        th_mb_s = (data_bytes / (1024.0 * 1024.0)) / wall_sec if wall_sec > 0 else 0.0
        th_items_s = items / wall_sec if wall_sec > 0 else 0.0
        
        metrics = ProcessMetrics(
            wall_time_ms=wall_ms,
            user_cpu_time_ms=user_sec * 1000.0,
            sys_cpu_time_ms=sys_sec * 1000.0,
            total_cpu_time_ms=total_cpu_sec * 1000.0,
            cpu_utilization_percent=cpu_util,
            peak_rss_mb=peak_rss_mb,
            minor_page_faults=minor_flt,
            major_page_faults=major_flt,
            read_bytes=max(0, rbytes_end - rbytes_start),
            write_bytes=max(0, wbytes_end - wbytes_start),
            read_syscalls=max(0, syscr_end - syscr_start),
            write_syscalls=max(0, syscw_end - syscw_start),
            items_processed=items,
            bytes_processed=data_bytes,
            throughput_mb_s=th_mb_s,
            throughput_items_s=th_items_s
        )
        
        return result, metrics


class StatisticalEngine:
    """Computes rigorous statistical distributions over benchmark metric runs."""
    
    # Student's t-value lookup table for 95% two-tailed CI
    T_TABLE_95 = {
        1: 12.706, 2: 4.303, 3: 3.182, 4: 2.776, 5: 2.571,
        6: 2.447, 7: 2.365, 8: 2.306, 9: 2.262, 10: 2.228,
        15: 2.131, 20: 2.086, 25: 2.060, 30: 2.042, 40: 2.021,
        50: 2.009, 100: 1.984, 1000: 1.962
    }
    
    @classmethod
    def get_t_crit(cls, df: int) -> float:
        if df in cls.T_TABLE_95:
            return cls.T_TABLE_95[df]
        for k in sorted(cls.T_TABLE_95.keys()):
            if df <= k:
                return cls.T_TABLE_95[k]
        return 1.960
        
    @classmethod
    def calculate_percentile(cls, sorted_vals: List[float], p: float) -> float:
        """NIST Linear Interpolation (Type 7)."""
        n = len(sorted_vals)
        if n == 0:
            return 0.0
        if n == 1:
            return sorted_vals[0]
        k = (n - 1) * p
        f = math.floor(k)
        c = math.ceil(k)
        if f == c:
            return sorted_vals[int(k)]
        d0 = sorted_vals[int(f)] * (c - k)
        d1 = sorted_vals[int(c)] * (k - f)
        return d0 + d1

    @classmethod
    def analyze_metrics(cls, name: str, metrics_list: List[ProcessMetrics]) -> StatisticalSummary:
        """Computes statistical summary across multiple benchmark runs."""
        times = [m.wall_time_ms for m in metrics_list]
        n = len(times)
        if n == 0:
            raise ValueError("metrics_list cannot be empty")
            
        mean_val = sum(times) / n
        variance = sum((x - mean_val) ** 2 for x in times) / (n - 1) if n > 1 else 0.0
        std_dev = math.sqrt(variance)
        cv = (std_dev / mean_val * 100.0) if mean_val > 0 else 0.0
        
        sorted_times = sorted(times)
        median_val = cls.calculate_percentile(sorted_times, 0.50)
        p90_val = cls.calculate_percentile(sorted_times, 0.90)
        p95_val = cls.calculate_percentile(sorted_times, 0.95)
        p99_val = cls.calculate_percentile(sorted_times, 0.99)
        
        df = max(1, n - 1)
        t_crit = cls.get_t_crit(df)
        margin_of_error = t_crit * (std_dev / math.sqrt(n))
        
        peak_rss = max(m.peak_rss_mb for m in metrics_list)
        cpu_util_mean = sum(m.cpu_utilization_percent for m in metrics_list) / n
        th_mb_s_mean = sum(m.throughput_mb_s for m in metrics_list) / n
        th_items_s_mean = sum(m.throughput_items_s for m in metrics_list) / n
        
        return StatisticalSummary(
            name=name,
            sample_size=n,
            mean=mean_val,
            std_dev=std_dev,
            cv_percent=cv,
            median=median_val,
            p90=p90_val,
            p95=p95_val,
            p99=p99_val,
            min_val=sorted_times[0],
            max_val=sorted_times[-1],
            ci95_lower=max(0.0, mean_val - margin_of_error),
            ci95_upper=mean_val + margin_of_error,
            peak_rss_mb=peak_rss,
            cpu_util_mean=cpu_util_mean,
            throughput_mb_s_mean=th_mb_s_mean,
            throughput_items_s_mean=th_items_s_mean
        )

    @classmethod
    def welch_t_test(cls, baseline: List[float], target: List[float]) -> Tuple[float, float, bool]:
        """Performs Welch's t-test to determine if performance difference is statistically significant."""
        n1, n2 = len(baseline), len(target)
        if n1 < 2 or n2 < 2:
            return 0.0, 1.0, False
            
        m1, m2 = sum(baseline) / n1, sum(target) / n2
        v1 = sum((x - m1) ** 2 for x in baseline) / (n1 - 1)
        v2 = sum((x - m2) ** 2 for x in target) / (n2 - 1)
        
        denom = math.sqrt((v1 / n1) + (v2 / n2))
        if denom == 0:
            return 0.0, 1.0, False
            
        t_stat = (m1 - m2) / denom
        df = ((v1 / n1 + v2 / n2) ** 2) / (((v1 / n1) ** 2 / (n1 - 1)) + ((v2 / n2) ** 2 / (n2 - 1)))
        
        # Approximate p-value
        p_val = math.erfc(abs(t_stat) / math.sqrt(2))
        is_significant = (p_val < 0.01)
        return t_stat, p_val, is_significant


class ParityVerifier:
    """Verifies bit-exact and semantic output parity across engines."""
    
    @staticmethod
    def verify_big_archive(file_path: str) -> Dict[str, Any]:
        """Parses BIG archive and extracts file table and payload SHA-256 hashes."""
        if not os.path.exists(file_path):
            return {"error": f"File not found: {file_path}", "valid": False}
            
        with open(file_path, "rb") as f:
            data = f.read()
            
        if len(data) < 16:
            return {"error": "Invalid BIG header size (<16 bytes)", "valid": False}
            
        magic = data[0:4]
        if magic not in (b"BIG4", b"BIGF"):
            return {"error": f"Invalid BIG magic: {magic}", "valid": False}
            
        archive_size, num_files, header_size = struct.unpack(">III", data[4:16])
        offset = 16
        entries = {}
        
        for _ in range(num_files):
            if offset + 8 > len(data):
                break
            f_offset, f_size = struct.unpack(">II", data[offset:offset+8])
            offset += 8
            # null-terminated path
            null_pos = data.find(b"\x00", offset)
            if null_pos == -1:
                break
            rel_path = data[offset:null_pos].decode("ascii", errors="ignore").replace("\\", "/")
            offset = null_pos + 1
            
            # Hash payload
            payload = data[f_offset:f_offset + f_size]
            payload_sha256 = hashlib.sha256(payload).hexdigest()
            entries[rel_path] = {
                "size": f_size,
                "offset": f_offset,
                "sha256": payload_sha256
            }
            
        return {
            "valid": True,
            "magic": magic.decode("ascii"),
            "archive_size": archive_size,
            "num_files": num_files,
            "header_size": header_size,
            "entries": entries
        }

    @staticmethod
    def verify_csf_file(file_path: str) -> Dict[str, Any]:
        """Parses CSF binary table and extracts decoded labels and strings."""
        if not os.path.exists(file_path):
            return {"error": f"File not found: {file_path}", "valid": False}
            
        with open(file_path, "rb") as f:
            data = f.read()
            
        if len(data) < 24:
            return {"error": "Invalid CSF header size (<24 bytes)", "valid": False}
            
        magic, version, num_labels, num_strings, unused, lang_id = struct.unpack("<4sIIIII", data[0:24])
        if magic != b" FSC":
            return {"error": f"Invalid CSF magic: {magic}", "valid": False}
            
        offset = 24
        labels = {}
        
        for _ in range(num_labels):
            if offset + 12 > len(data):
                break
            lbl_magic, str_count, name_len = struct.unpack("<4sII", data[offset:offset+12])
            offset += 12
            lbl_name = data[offset:offset+name_len].decode("ascii", errors="ignore")
            offset += name_len
            
            str_magic, val_len = struct.unpack("<4sI", data[offset:offset+8])
            offset += 8
            
            # Decrypt inverted UTF-16LE characters (~c)
            chars = []
            for _ in range(val_len):
                char_code = struct.unpack("<H", data[offset:offset+2])[0]
                offset += 2
                plain_code = (~char_code) & 0xFFFF
                chars.append(chr(plain_code))
                
            labels[lbl_name] = "".join(chars)
            
        return {
            "valid": True,
            "version": version,
            "num_labels": num_labels,
            "num_strings": num_strings,
            "language_id": lang_id,
            "labels": labels
        }
