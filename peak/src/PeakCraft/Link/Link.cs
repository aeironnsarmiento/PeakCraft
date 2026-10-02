using System;
using System.Runtime.InteropServices;
using System.Threading;
using BepInEx.Logging;

namespace PeakCraft.Link;

/// <summary>
/// The PEAK end of the shared-memory link. PEAK creates the mapping; Minecraft opens it when it
/// appears (SkyLink.java) and both sides watch each other's heartbeat.
/// Mirrors skse/src/Link.cpp.
/// </summary>
internal sealed unsafe class PeakLink
{
    // Same clocks as SkyLink.java: GetTickCount64 for heartbeats, QueryPerformanceCounter for tick
    // timestamps. Environment.TickCount64 and Stopwatch are not guaranteed to be the same clocks on Mono.
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr CreateFileMappingW(IntPtr file, IntPtr attributes, uint protect, uint sizeHigh, uint sizeLow, string name);

    [DllImport("kernel32", SetLastError = true)]
    private static extern IntPtr MapViewOfFile(IntPtr mapping, uint access, uint offsetHigh, uint offsetLow, UIntPtr bytes);

    [DllImport("kernel32")]
    private static extern bool UnmapViewOfFile(IntPtr view);

    [DllImport("kernel32")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32")]
    private static extern ulong GetTickCount64();

    [DllImport("kernel32")]
    private static extern bool QueryPerformanceCounter(out long count);

    [DllImport("kernel32")]
    private static extern bool QueryPerformanceFrequency(out long frequency);

    [DllImport("kernel32")]
    private static extern uint GetCurrentProcessId();

    private const uint PageReadWrite = 0x04;
    private const uint FileMapAllAccess = 0xF001F;
    private const int ErrorAlreadyExists = 183;
    private const ulong GuestTimeoutMs = 3000;

    private readonly ManualLogSource log;
    private IntPtr mapping;
    private byte* @base;
    private Proto.HostState hostState;
    private int overlayFront = 2;

    public PeakLink(ManualLogSource log)
    {
        this.log = log;
        QueryPerformanceFrequency(out long frequency);
        QpcFrequency = frequency;
    }

    public bool Created => @base != null;
    public long QpcFrequency { get; }

    public static ulong TickCount => GetTickCount64();

    public static long Qpc()
    {
        QueryPerformanceCounter(out long count);
        return count;
    }

    private Proto.Header* Header => (Proto.Header*)(@base + Proto.OffHeader);

    public bool Create(string name)
    {
        if (@base != null)
        {
            return true;
        }
        long size = Proto.MappingBytes;
        mapping = CreateFileMappingW(new IntPtr(-1), IntPtr.Zero, PageReadWrite, (uint)(size >> 32), (uint)(size & 0xFFFFFFFF), name);
        int created = Marshal.GetLastWin32Error();
        if (mapping == IntPtr.Zero)
        {
            log.LogError($"shared memory {name}: CreateFileMapping failed (Windows error {created})");
            return false;
        }
        @base = (byte*)MapViewOfFile(mapping, FileMapAllAccess, 0, 0, UIntPtr.Zero);
        if (@base == null)
        {
            log.LogError($"shared memory {name}: MapViewOfFile failed (Windows error {Marshal.GetLastWin32Error()})");
            CloseHandle(mapping);
            mapping = IntPtr.Zero;
            return false;
        }

        // A stale mapping survives if Minecraft still has it open from a previous PEAK run.
        // Reset everything the host owns so rings and the overlay swap start from a known state.
        Clear(Proto.OffHostState, sizeof(Proto.HostState));
        Clear(Proto.OffOverlayCtl, 0x100);
        Clear(Proto.OffWaterGrid, 0x10);
        Clear(Proto.OffInputRing, Proto.RingDataOff);
        Clear(Proto.OffCollisionRing, Proto.RingDataOff);
        Clear(Proto.OffActorTable, 0x40);
        Clear(Proto.OffEventRing, Proto.RingDataOff);
        Clear(Proto.OffWorldEntities, 0x40);
        Clear(Proto.OffRenderRing, Proto.RingDataOff);
        hostState = default;
        overlayFront = 2;
        Header->version = Proto.Version;
        Header->hostPid = GetCurrentProcessId();
        Header->hostHeartbeatMs = GetTickCount64();
        Volatile.Write(ref Header->magic, Proto.Magic);

        log.LogInfo($"shared memory {name} ({size >> 20} MB, {(created == ErrorAlreadyExists ? "reused" : "created")})");
        return true;
    }

    public void Close()
    {
        if (@base != null)
        {
            Volatile.Write(ref Header->magic, 0);
            UnmapViewOfFile((IntPtr)@base);
            @base = null;
        }
        if (mapping != IntPtr.Zero)
        {
            CloseHandle(mapping);
            mapping = IntPtr.Zero;
        }
    }

    private void Clear(long offset, long bytes) => new Span<byte>(@base + offset, (int)bytes).Clear();

    public void Heartbeat() => Volatile.Write(ref Header->hostHeartbeatMs, GetTickCount64());

    public bool GuestAlive
    {
        get
        {
            ulong last = Volatile.Read(ref Header->guestHeartbeatMs);
            return last != 0 && GetTickCount64() - last < GuestTimeoutMs;
        }
    }

    public uint GuestPid => Header->guestPid;

    /// <summary>Publishes the host state. Minecraft paces its frames on this seq, so call it every frame.</summary>
    public void WriteHostState(in Proto.HostState state)
    {
        hostState = state;
        Rings.SeqlockWrite(@base + Proto.OffHostState, state);
    }

    public bool ReadGuestState(out Proto.GuestState state) => Rings.SeqlockRead(@base + Proto.OffGuestState, out state);

    public void PushInput(Proto.InputType type, ushort code = 0, int a = 0, int b = 0, int c = 0)
    {
        Rings.EntryPush(@base + Proto.OffInputRing, Proto.InputRingEntries,
            new Proto.InputEvent { type = (ushort)type, code = code, a = a, b = b, c = c });
    }

    public bool PopEvent(out Proto.GuestEvent e) => Rings.EntryPop(@base + Proto.OffEventRing, Proto.EventRingEntries, out e);

    public bool WriteCollision(uint type, void* payload, uint bytes)
        => Rings.ByteWrite(@base + Proto.OffCollisionRing, Proto.ColRingDataBytes, type, payload, bytes);

    /// <summary>Bytes Minecraft has not consumed yet.</summary>
    public long CollisionBacklog
    {
        get
        {
            byte* ring = @base + Proto.OffCollisionRing;
            return (long)(*(ulong*)(ring + Proto.RingHeadOff) - Volatile.Read(ref *(ulong*)(ring + Proto.RingTailOff)));
        }
    }

    public void DrainRender(long maxBytes, Rings.MessageHandler handler)
        => Rings.ByteDrain(@base + Proto.OffRenderRing, Proto.RenRingDataBytes, maxBytes, handler);

    /// <summary>The block-selection box from the world-entities table (Minecraft coords), if Minecraft shows one.</summary>
    public bool ReadSelection(out UnityEngine.Vector3 min, out UnityEngine.Vector3 max)
    {
        byte* table = @base + Proto.OffWorldEntities;
        for (int attempt = 0; attempt < 16; attempt++)
        {
            uint s1 = Volatile.Read(ref *(uint*)table);
            if ((s1 & 1) != 0)
            {
                continue;
            }
            uint has = *(uint*)(table + 8);
            float* box = (float*)(table + 12);
            min = new UnityEngine.Vector3(box[0], box[1], box[2]);
            max = new UnityEngine.Vector3(box[3], box[4], box[5]);
            Thread.MemoryBarrier();
            if (Volatile.Read(ref *(uint*)table) == s1)
            {
                return has != 0;
            }
        }
        min = max = default;
        return false;
    }

    /// <summary>One entry of the world-entities table (see WorldEntity in the protocol header).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct WorldEntity
    {
        public uint kind;
        public uint id;
        public float x, y, z;
        public float yaw, pitch;
        public float scale;
        public float ext0, ext1, ext2;
        public float uv0, uv1, uv2, uv3, uv4, uv5, uv6, uv7, uv8, uv9, uv10, uv11; // three atlas rects {u0, v0, u1, v1}
        public uint tint;
    }

    /// <summary>Seqlock copy of the world-entities table. False on a torn read (keep last frame's).</summary>
    public bool ReadWorldEntities(System.Collections.Generic.List<WorldEntity> output)
    {
        byte* table = @base + Proto.OffWorldEntities;
        for (int attempt = 0; attempt < 16; attempt++)
        {
            uint s1 = Volatile.Read(ref *(uint*)table);
            if ((s1 & 1) != 0)
            {
                continue;
            }
            output.Clear();
            uint count = Math.Min(*(uint*)(table + 4), (uint)Proto.MaxWorldEntities);
            var records = (WorldEntity*)(table + 0x40);
            for (uint i = 0; i < count; i++)
            {
                output.Add(records[i]);
            }
            Thread.MemoryBarrier();
            if (Volatile.Read(ref *(uint*)table) == s1)
            {
                return true;
            }
        }
        return false;
    }

    // ---- overlay triple buffer: we own the front slot, Minecraft the back, state names the middle ----

    public bool AcquireOverlayFrame()
    {
        ref int state = ref *(int*)(@base + Proto.OffOverlayCtl);
        if ((Volatile.Read(ref state) & (int)Proto.OverlayDirty) == 0)
        {
            return false;
        }
        int old = Interlocked.Exchange(ref state, overlayFront);
        overlayFront = old & 3;
        return true;
    }

    public void ResetOverlay()
    {
        Volatile.Write(ref *(int*)(@base + Proto.OffOverlayCtl), 0);
        overlayFront = 2;
    }

    public byte* FrontPixels => @base + Proto.OffOverlayPixels + Proto.OverlaySlotBytes * overlayFront;

    public Proto.OverlaySlotHdr* FrontHeader => (Proto.OverlaySlotHdr*)(@base + Proto.OffOverlaySlotHdr + sizeof(Proto.OverlaySlotHdr) * overlayFront);
}
