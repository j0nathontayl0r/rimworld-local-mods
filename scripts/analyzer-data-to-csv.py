#!/usr/bin/env python3
"""Convert Dubs Performance Analyzer saved entries (.data) to CSV.

The Analyzer's Save tab writes binary files to <RimWorld save data>/Analyzer/
(format: Source/Profiling/Utility/FileUtility.cs in the mod's repo). This tool
dumps the raw per-update samples to CSV and can print/compare the same summary
stats the in-game comparison view shows.

Usage:
  analyzer-data-to-csv.py entry-0.data [more.data ...]   # writes entry-0.csv next to each
  analyzer-data-to-csv.py --summary *.data               # print stats table, no CSV
  analyzer-data-to-csv.py --compare linux.data windows.data
"""

import argparse
import csv
import statistics
import struct
import sys
from pathlib import Path

MAGIC = 440985710


class ParseError(Exception):
    pass


def read_7bit_int(f):
    """.NET BinaryWriter 7-bit-encoded length prefix."""
    result = shift = 0
    while True:
        b = f.read(1)
        if not b:
            raise ParseError("truncated file (string length)")
        result |= (b[0] & 0x7F) << shift
        if not b[0] & 0x80:
            return result
        shift += 7


def read_string(f):
    return f.read(read_7bit_int(f)).decode("utf-8")


def read_entry_file(path):
    with open(path, "rb") as f:
        magic, ver = struct.unpack("<ii", f.read(8))
        if magic != MAGIC:
            raise ParseError(f"bad magic {magic} (corrupt or not an Analyzer file)")
        if ver not in (1, 2):
            raise ParseError(f"unknown scribing version {ver}")
        method = read_string(f)
        name = read_string(f)
        entry_per_call, only_with_values = struct.unpack("<??", f.read(2))
        entries, target = struct.unpack("<ii", f.read(8))
        times, calls = [], []
        for _ in range(entries):
            times.append(struct.unpack("<d", f.read(8))[0])
            calls.append(1 if entry_per_call else struct.unpack("<i", f.read(4))[0])
    return {
        "path": Path(path),
        "method": method,
        "name": name or method,
        "entry_per_call": entry_per_call,
        "only_with_values": only_with_values,
        "target": target,
        "times": times,
        "calls": calls,
    }


def stats(e):
    times, calls = e["times"], e["calls"]
    n = len(times)
    total_time, total_calls = sum(times), sum(calls)
    return {
        "Entries": n,
        "Total Calls": total_calls,
        "Total Time": total_time,
        "Avg Time/Call": total_time / total_calls if total_calls else 0.0,
        "Avg Calls/Update": total_calls / n if n else 0.0,
        "Avg Time/Update": total_time / n if n else 0.0,
        "Median Calls": statistics.median(calls) if n else 0.0,
        "Median Time": statistics.median(times) if n else 0.0,
        "Max Time": max(times, default=0.0),
        "Max Calls/Update": max(calls, default=0),
    }


def fmt(v):
    return str(v) if isinstance(v, int) else f"{v:.3f}"


def dump_csv(e):
    out = e["path"].with_suffix(".csv")
    with open(out, "w", newline="") as f:
        w = csv.writer(f)
        w.writerow(["sample", "time_ms", "calls"])
        for i, (t, c) in enumerate(zip(e["times"], e["calls"])):
            w.writerow([i, repr(t), c])
    print(f"{e['path']} -> {out}  ({e['name']}, {len(e['times'])} samples)")


def print_summary(entries):
    for e in entries:
        print(f"\n{e['path']}  [{e['name']}]  method: {e['method']}")
        for k, v in stats(e).items():
            print(f"  {k:<18} {fmt(v)}")


def print_compare(lhs, rhs):
    ls, rs = stats(lhs), stats(rhs)
    print(f"{'':<18} {'A: ' + lhs['path'].name:>20} {'B: ' + rhs['path'].name:>20} {'delta (B-A)':>22}")
    for k in ls:
        l, r = ls[k], rs[k]
        delta = r - l
        perc = f" ({delta / l * 100:+.2f}%)" if l else ""
        print(f"{k:<18} {fmt(l):>20} {fmt(r):>20} {'+' if delta >= 0 else ''}{fmt(delta):>14}{perc}")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("files", nargs="+", help=".data files saved by the Analyzer")
    ap.add_argument("--summary", action="store_true", help="print stats instead of writing CSVs")
    ap.add_argument("--compare", action="store_true", help="compare exactly two files (A vs B)")
    args = ap.parse_args()

    if args.compare and len(args.files) != 2:
        ap.error("--compare needs exactly two files")

    try:
        entries = [read_entry_file(p) for p in args.files]
    except (ParseError, OSError) as e:
        sys.exit(f"error: {e}")

    if args.compare:
        print_compare(*entries)
    elif args.summary:
        print_summary(entries)
    else:
        for e in entries:
            dump_csv(e)


if __name__ == "__main__":
    main()
