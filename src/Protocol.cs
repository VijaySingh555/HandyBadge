using System;
using System.Collections.Generic;

namespace HandyBadgeRemote
{
    // The badge's "MyWatch" framing, as used by HandyBadge (see HandyBadge protocol notes).
    // Request: CD | len(2) = total-3 | module | 01 | cmd | payloadLen(2) | payload
    // Ack for every CD frame received: DC 00 05 module cmd frameLen(2) 01
    class Packet
    {
        public bool IsCd;
        public byte Module, Cmd;
        public byte[] Payload = new byte[0];
        public byte[] Raw;
    }

    static class Proto
    {
        public const byte ModFaceReply = 0x20, CmdFaces = 0x03;

        public static byte[] Make(byte module, byte cmd, byte[] payload = null)
        {
            payload = payload ?? new byte[0];
            int total = 8 + payload.Length;
            var p = new byte[total];
            p[0] = 0xcd; p[1] = (byte)((total - 3) >> 8); p[2] = (byte)((total - 3) & 255);
            p[3] = module; p[4] = 1; p[5] = cmd;
            p[6] = (byte)(payload.Length >> 8); p[7] = (byte)(payload.Length & 255);
            Array.Copy(payload, 0, p, 8, payload.Length);
            return p;
        }

        public static byte[] Head(byte[] b, int n) { var h = new byte[Math.Min(n, b.Length)]; Array.Copy(b, h, h.Length); return h; }

        public static byte[] AckFor(Packet p)
        {
            int L = p.Raw.Length;
            return new byte[] { 0xdc, 0, 5, p.Module, p.Cmd, (byte)(L >> 8), (byte)(L & 255), 1 };
        }

        // Stored-faces reply payload: count | entryLen | entries of id(4) flags(1) size(3).
        // Returns the footprint of the face marked "in use" (or the first), or 0.
        public static int ActiveFootprint(byte[] g)
        {
            if (g == null || g.Length < 2) return 0;
            int L = g[1];
            if (L < 8) return 0;
            int first = 0;
            for (int o = 2; o + L <= g.Length; o += L)
            {
                int size = (g[o + 5] << 16) | (g[o + 6] << 8) | g[o + 7];
                bool inUse = (g[o + 4] & 1) != 0;
                if (inUse) return size;
                if (first == 0) first = size;
            }
            return first;
        }
    }

    // Reassembles notification fragments into frames.
    class Parser
    {
        readonly List<byte> buf = new List<byte>();
        readonly Action<Packet> onPacket;
        public Parser(Action<Packet> onPacket) { this.onPacket = onPacket; }

        public void Feed(byte[] bytes)
        {
            buf.AddRange(bytes);
            for (;;)
            {
                while (buf.Count > 0 && buf[0] != 0xcd && buf[0] != 0xdc) buf.RemoveAt(0);
                if (buf.Count < 3) return;
                int L = (buf[1] << 8) | buf[2], total = L + 3;
                if (L < 2 || total > 4096) { buf.RemoveAt(0); continue; }
                if (buf.Count < total) return;
                var raw = buf.GetRange(0, total).ToArray();
                buf.RemoveRange(0, total);
                var p = new Packet { Raw = raw, IsCd = raw[0] == 0xcd, Module = raw[3] };
                if (p.IsCd)
                {
                    p.Cmd = raw[5];
                    int plen = raw.Length >= 8 ? (raw[6] << 8) | raw[7] : 0;
                    plen = Math.Max(0, Math.Min(plen, raw.Length - 8));
                    p.Payload = new byte[plen];
                    Array.Copy(raw, 8, p.Payload, 0, plen);
                }
                else p.Cmd = raw[4];
                onPacket(p);
            }
        }
    }
}
