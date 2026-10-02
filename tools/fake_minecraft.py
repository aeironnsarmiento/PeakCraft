"""Stand-in for the Fabric mod, for testing a host plugin (PEAK) without Minecraft.

Opens the shared mapping the host created, stamps the Minecraft heartbeat and writes a scripted
player state (walking a small circle). Prints the host's heartbeat and state. Nothing else.

    python tools/fake_minecraft.py [seconds]
"""

import ctypes
import math
import mmap
import os
import struct
import sys
import time

MAGIC = 0x43594B53
VERSION = 11
NAME = os.environ.get("SKYCRAFT_LINK", "Local\\SkyCraft_v1")
OFF_SKY = 0x100
OFF_MC = 0x200
SIZE = 0x20000 + (32 << 20) + 3840 * 2160 * 4 * 3 + (64 << 20)

k32 = ctypes.windll.kernel32
k32.GetTickCount64.restype = ctypes.c_uint64
k32.OpenFileMappingW.restype = ctypes.c_void_p
k32.OpenFileMappingW.argtypes = [ctypes.c_uint32, ctypes.c_int, ctypes.c_wchar_p]
k32.CloseHandle.argtypes = [ctypes.c_void_p]


def exists():
    handle = k32.OpenFileMappingW(0xF001F, 0, NAME)
    if handle:
        k32.CloseHandle(handle)
    return bool(handle)


def read_sky(m):
    """Seqlock read of the host state."""
    for _ in range(100):
        (s1,) = struct.unpack_from("<I", m, OFF_SKY)
        if s1 & 1:
            continue
        fields = struct.unpack_from("<IIIdddffIIIf", m, OFF_SKY + 4)
        if struct.unpack_from("<I", m, OFF_SKY)[0] == s1:
            return (s1,) + fields
    return None


def write_mc(m, seq, flags, pos, yaw, pitch, frame):
    struct.pack_into("<I", m, OFF_MC, seq * 2 - 1)
    struct.pack_into("<IdddffffIIQfffIddd", m, OFF_MC + 4, flags, pos[0], pos[1], pos[2], yaw, pitch, 1.62, 0.5, 0, 2, frame,
                     70.0, 0.0, 0.0, 0, pos[0], pos[1] + 1.62, pos[2])
    struct.pack_into("<I", m, OFF_MC, seq * 2)


def main():
    seconds = float(sys.argv[1]) if len(sys.argv) > 1 else 30
    print(f"fake Minecraft: waiting for {NAME} ...")
    while not exists():
        time.sleep(0.5)
    m = mmap.mmap(-1, SIZE, tagname=NAME)
    magic, version, host_pid = struct.unpack_from("<III", m, 0)
    if magic != MAGIC or version != VERSION:
        print(f"protocol mismatch: magic {magic:#x} version {version}, expected {MAGIC:#x} / {VERSION}")
        return 1
    struct.pack_into("<I", m, 0x0C, os.getpid())
    print(f"linked to host pid {host_pid}")

    start = time.time()
    last_print = 0.0
    frame = 0
    while time.time() - start < seconds:
        t = time.time() - start
        frame += 1
        struct.pack_into("<Q", m, 0x18, k32.GetTickCount64())
        pos = (100.5 + 3 * math.cos(t), 64.0, -20.5 + 3 * math.sin(t))
        write_mc(m, frame, 1 | 4, pos, math.degrees(t) % 360 - 180, 0.0, frame)
        if time.time() - last_print > 1.0:
            last_print = time.time()
            (beat,) = struct.unpack_from("<Q", m, 0x10)
            age = k32.GetTickCount64() - beat
            sky = read_sky(m)
            state = "torn" if sky is None else f"seq={sky[0]} flags={sky[1]:#x} epoch={sky[3]} pos=({sky[4]:.2f}, {sky[5]:.2f}, {sky[6]:.2f}) yaw={sky[7]:.1f} viewport={sky[10]}x{sky[11]}"
            print(f"t={t:5.1f} host heartbeat {age} ms ago ({'alive' if age < 3000 else 'STALE'}); host state {state}; sent pos=({pos[0]:.2f}, {pos[1]:.2f}, {pos[2]:.2f})")
        time.sleep(1 / 60)
    print("fake Minecraft: done (heartbeat stops now)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
