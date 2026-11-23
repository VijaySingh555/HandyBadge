using System;
using System.Collections.Generic;

namespace HandyBadgeRemote
{
    // Direct port of HandyBadge's baseline JPEG encoder (HBJpeg.encodeJPEG), which this badge is known to accept:
    // JFIF, baseline, two quantisation tables, standard Huffman tables, no restart markers.
    // Output is byte-for-byte identical to the JavaScript version for the same pixels.
    static class JpegEncoder
    {
        static readonly int[] ZZ = {0,1,8,16,9,2,3,10,17,24,32,25,18,11,4,5,12,19,26,33,40,48,41,34,27,20,13,6,7,14,21,28,
            35,42,49,56,57,50,43,36,29,22,15,23,30,37,44,51,58,59,52,45,38,31,39,46,53,60,61,54,47,55,62,63};
        static readonly int[] QY = {16,11,10,16,24,40,51,61,12,12,14,19,26,58,60,55,14,13,16,24,40,57,69,56,14,17,22,29,51,87,80,62,
            18,22,37,56,68,109,103,77,24,35,55,64,81,104,113,92,49,64,78,87,103,121,120,101,72,92,95,98,112,100,103,99};
        static readonly int[] QC = {17,18,24,47,99,99,99,99,18,21,26,66,99,99,99,99,24,26,56,99,99,99,99,99,47,66,99,99,99,99,99,99,
            99,99,99,99,99,99,99,99,99,99,99,99,99,99,99,99,99,99,99,99,99,99,99,99,99,99,99,99,99,99,99,99};
        static readonly int[] DC_L_N = {0,1,5,1,1,1,1,1,1,0,0,0,0,0,0,0}, DC_L_V = {0,1,2,3,4,5,6,7,8,9,10,11};
        static readonly int[] DC_C_N = {0,3,1,1,1,1,1,1,1,1,1,0,0,0,0,0}, DC_C_V = {0,1,2,3,4,5,6,7,8,9,10,11};
        static readonly int[] AC_L_N = {0,2,1,3,3,2,4,3,5,5,4,4,0,0,1,0x7d};
        static readonly int[] AC_L_V = {0x01,0x02,0x03,0x00,0x04,0x11,0x05,0x12,0x21,0x31,0x41,0x06,0x13,0x51,0x61,0x07,0x22,0x71,0x14,0x32,0x81,0x91,0xa1,0x08,
            0x23,0x42,0xb1,0xc1,0x15,0x52,0xd1,0xf0,0x24,0x33,0x62,0x72,0x82,0x09,0x0a,0x16,0x17,0x18,0x19,0x1a,0x25,0x26,0x27,0x28,0x29,0x2a,0x34,0x35,0x36,
            0x37,0x38,0x39,0x3a,0x43,0x44,0x45,0x46,0x47,0x48,0x49,0x4a,0x53,0x54,0x55,0x56,0x57,0x58,0x59,0x5a,0x63,0x64,0x65,0x66,0x67,0x68,0x69,0x6a,
            0x73,0x74,0x75,0x76,0x77,0x78,0x79,0x7a,0x83,0x84,0x85,0x86,0x87,0x88,0x89,0x8a,0x92,0x93,0x94,0x95,0x96,0x97,0x98,0x99,0x9a,0xa2,0xa3,0xa4,
            0xa5,0xa6,0xa7,0xa8,0xa9,0xaa,0xb2,0xb3,0xb4,0xb5,0xb6,0xb7,0xb8,0xb9,0xba,0xc2,0xc3,0xc4,0xc5,0xc6,0xc7,0xc8,0xc9,0xca,0xd2,0xd3,0xd4,0xd5,
            0xd6,0xd7,0xd8,0xd9,0xda,0xe1,0xe2,0xe3,0xe4,0xe5,0xe6,0xe7,0xe8,0xe9,0xea,0xf1,0xf2,0xf3,0xf4,0xf5,0xf6,0xf7,0xf8,0xf9,0xfa};
        static readonly int[] AC_C_N = {0,2,1,2,4,4,3,4,7,5,4,4,0,1,2,0x77};
        static readonly int[] AC_C_V = {0x00,0x01,0x02,0x03,0x11,0x04,0x05,0x21,0x31,0x06,0x12,0x41,0x51,0x07,0x61,0x71,0x13,0x22,0x32,0x81,0x08,0x14,0x42,0x91,
            0xa1,0xb1,0xc1,0x09,0x23,0x33,0x52,0xf0,0x15,0x62,0x72,0xd1,0x0a,0x16,0x24,0x34,0xe1,0x25,0xf1,0x17,0x18,0x19,0x1a,0x26,0x27,0x28,0x29,0x2a,
            0x35,0x36,0x37,0x38,0x39,0x3a,0x43,0x44,0x45,0x46,0x47,0x48,0x49,0x4a,0x53,0x54,0x55,0x56,0x57,0x58,0x59,0x5a,0x63,0x64,0x65,0x66,0x67,0x68,
            0x69,0x6a,0x73,0x74,0x75,0x76,0x77,0x78,0x79,0x7a,0x82,0x83,0x84,0x85,0x86,0x87,0x88,0x89,0x8a,0x92,0x93,0x94,0x95,0x96,0x97,0x98,0x99,0x9a,
            0xa2,0xa3,0xa4,0xa5,0xa6,0xa7,0xa8,0xa9,0xaa,0xb2,0xb3,0xb4,0xb5,0xb6,0xb7,0xb8,0xb9,0xba,0xc2,0xc3,0xc4,0xc5,0xc6,0xc7,0xc8,0xc9,0xca,0xd2,
            0xd3,0xd4,0xd5,0xd6,0xd7,0xd8,0xd9,0xda,0xe2,0xe3,0xe4,0xe5,0xe6,0xe7,0xe8,0xe9,0xea,0xf2,0xf3,0xf4,0xf5,0xf6,0xf7,0xf8,0xf9,0xfa};

        static int[][] BuildHuff(int[] nrcodes, int[] values)
        {
            var table = new int[256][];
            int code = 0, k = 0;
            for (int len = 1; len <= 16; len++)
            {
                for (int j = 0; j < nrcodes[len - 1]; j++) { table[values[k++]] = new[] { code, len }; code++; }
                code <<= 1;
            }
            return table;
        }
        static readonly int[][] HdcY = BuildHuff(DC_L_N, DC_L_V), HdcC = BuildHuff(DC_C_N, DC_C_V);
        static readonly int[][] HacY = BuildHuff(AC_L_N, AC_L_V), HacC = BuildHuff(AC_C_N, AC_C_V);

        static int[] ScaleQ(int[] b, int quality)
        {
            quality = Math.Max(1, Math.Min(100, quality));
            int s = quality < 50 ? 5000 / quality : 200 - quality * 2;
            var r = new int[64];
            for (int i = 0; i < 64; i++) r[i] = Math.Min(255, Math.Max(1, (int)Math.Floor((b[i] * s + 50) / 100.0)));
            return r;
        }

        static readonly double[] AAN = { 1.0, 1.387039845, 1.306562965, 1.175875602, 1.0, 0.785694958, 0.541196100, 0.275899379 };
        static double[] Fdtbl(int[] q)
        {
            var t = new double[64];
            for (int r = 0; r < 8; r++) for (int c = 0; c < 8; c++) t[r * 8 + c] = 1.0 / (q[r * 8 + c] * AAN[r] * AAN[c] * 8.0);
            return t;
        }

        static void Fdct(double[] d)
        {
            for (int i = 0; i < 64; i += 8)
            {
                double t0 = d[i] + d[i+7], t7 = d[i] - d[i+7], t1 = d[i+1] + d[i+6], t6 = d[i+1] - d[i+6];
                double t2 = d[i+2] + d[i+5], t5 = d[i+2] - d[i+5], t3 = d[i+3] + d[i+4], t4 = d[i+3] - d[i+4];
                double t10 = t0 + t3, t13 = t0 - t3, t11 = t1 + t2, t12 = t1 - t2;
                d[i] = t10 + t11; d[i+4] = t10 - t11;
                double z1 = (t12 + t13) * 0.707106781; d[i+2] = t13 + z1; d[i+6] = t13 - z1;
                t10 = t4 + t5; t11 = t5 + t6; t12 = t6 + t7;
                double z5 = (t10 - t12) * 0.382683433, z2 = 0.541196100 * t10 + z5, z4 = 1.306562965 * t12 + z5, z3 = t11 * 0.707106781;
                double z11 = t7 + z3, z13 = t7 - z3;
                d[i+5] = z13 + z2; d[i+3] = z13 - z2; d[i+1] = z11 + z4; d[i+7] = z11 - z4;
            }
            for (int i = 0; i < 8; i++)
            {
                double t0 = d[i] + d[i+56], t7 = d[i] - d[i+56], t1 = d[i+8] + d[i+48], t6 = d[i+8] - d[i+48];
                double t2 = d[i+16] + d[i+40], t5 = d[i+16] - d[i+40], t3 = d[i+24] + d[i+32], t4 = d[i+24] - d[i+32];
                double t10 = t0 + t3, t13 = t0 - t3, t11 = t1 + t2, t12 = t1 - t2;
                d[i] = t10 + t11; d[i+32] = t10 - t11;
                double z1 = (t12 + t13) * 0.707106781; d[i+16] = t13 + z1; d[i+48] = t13 - z1;
                t10 = t4 + t5; t11 = t5 + t6; t12 = t6 + t7;
                double z5 = (t10 - t12) * 0.382683433, z2 = 0.541196100 * t10 + z5, z4 = 1.306562965 * t12 + z5, z3 = t11 * 0.707106781;
                double z11 = t7 + z3, z13 = t7 - z3;
                d[i+40] = z13 + z2; d[i+24] = z13 - z2; d[i+8] = z11 + z4; d[i+56] = z11 - z4;
            }
        }

        class Writer
        {
            public readonly List<byte> Out = new List<byte>(8192);
            int bitBuf, bitCnt;
            public void W8(int v) { Out.Add((byte)(v & 255)); }
            public void W16(int v) { W8(v >> 8); W8(v); }
            public void Bits(int code, int len)
            {
                for (int i = len - 1; i >= 0; i--)
                {
                    bitBuf = (bitBuf << 1) | ((code >> i) & 1); bitCnt++;
                    if (bitCnt == 8) { Out.Add((byte)bitBuf); if (bitBuf == 0xff) Out.Add(0); bitBuf = 0; bitCnt = 0; }
                }
            }
            public void Flush() { if (bitCnt > 0) Bits((1 << (8 - bitCnt)) - 1, 8 - bitCnt); }
        }

        // rgba: width*height*4 bytes in R,G,B,A order. sub420: 4:2:0 chroma subsampling (needs multiples of 16).
        public static byte[] Encode(byte[] rgba, int width, int height, int quality, bool sub420)
        {
            int mcu = sub420 ? 16 : 8;
            if (width % mcu != 0 || height % mcu != 0) throw new ArgumentException("Size must be a multiple of " + mcu);
            int[] qy = ScaleQ(QY, quality), qc = ScaleQ(QC, quality);
            double[] fy = Fdtbl(qy), fc = Fdtbl(qc);
            var w = new Writer();
            w.W16(0xffd8);
            w.W16(0xffe0); w.W16(16); foreach (var c in new[] { 0x4a, 0x46, 0x49, 0x46, 0 }) w.W8(c); w.W8(1); w.W8(1); w.W8(0); w.W16(1); w.W16(1); w.W8(0); w.W8(0);
            w.W16(0xffdb); w.W16(132);
            w.W8(0); for (int i = 0; i < 64; i++) w.W8(qy[ZZ[i]]);
            w.W8(1); for (int i = 0; i < 64; i++) w.W8(qc[ZZ[i]]);
            w.W16(0xffc0); w.W16(17); w.W8(8); w.W16(height); w.W16(width); w.W8(3);
            w.W8(1); w.W8(sub420 ? 0x22 : 0x11); w.W8(0);
            w.W8(2); w.W8(0x11); w.W8(1);
            w.W8(3); w.W8(0x11); w.W8(1);
            w.W16(0xffc4); w.W16(0x01a2);
            WriteHuff(w, 0x00, DC_L_N, DC_L_V); WriteHuff(w, 0x10, AC_L_N, AC_L_V);
            WriteHuff(w, 0x01, DC_C_N, DC_C_V); WriteHuff(w, 0x11, AC_C_N, AC_C_V);
            w.W16(0xffda); w.W16(12); w.W8(3); w.W8(1); w.W8(0); w.W8(2); w.W8(0x11); w.W8(3); w.W8(0x11); w.W8(0); w.W8(0x3f); w.W8(0);

            int N = width * height;
            var Y = new double[N]; var Cb = new double[N]; var Cr = new double[N];
            for (int i = 0, p = 0; i < N; i++, p += 4)
            {
                double r = rgba[p], g = rgba[p + 1], b = rgba[p + 2];
                Y[i] = 0.299 * r + 0.587 * g + 0.114 * b - 128;
                Cb[i] = -0.168736 * r - 0.331264 * g + 0.5 * b;
                Cr[i] = 0.5 * r - 0.418688 * g - 0.081312 * b;
            }
            var blk = new double[64];
            int dY = 0, dCb = 0, dCr = 0;
            for (int my = 0; my < height; my += mcu)
                for (int mx = 0; mx < width; mx += mcu)
                {
                    if (mcu == 8)
                    {
                        Luma(Y, width, mx, my, blk); dY = Block(w, blk, dY, fy, HdcY, HacY);
                        Chroma(Cb, width, mx, my, 1, blk); dCb = Block(w, blk, dCb, fc, HdcC, HacC);
                        Chroma(Cr, width, mx, my, 1, blk); dCr = Block(w, blk, dCr, fc, HdcC, HacC);
                    }
                    else
                    {
                        Luma(Y, width, mx, my, blk); dY = Block(w, blk, dY, fy, HdcY, HacY);
                        Luma(Y, width, mx + 8, my, blk); dY = Block(w, blk, dY, fy, HdcY, HacY);
                        Luma(Y, width, mx, my + 8, blk); dY = Block(w, blk, dY, fy, HdcY, HacY);
                        Luma(Y, width, mx + 8, my + 8, blk); dY = Block(w, blk, dY, fy, HdcY, HacY);
                        Chroma(Cb, width, mx, my, 2, blk); dCb = Block(w, blk, dCb, fc, HdcC, HacC);
                        Chroma(Cr, width, mx, my, 2, blk); dCr = Block(w, blk, dCr, fc, HdcC, HacC);
                    }
                }
            w.Flush();
            w.W16(0xffd9);
            return w.Out.ToArray();
        }

        static void WriteHuff(Writer w, int id, int[] n, int[] v) { w.W8(id); foreach (var x in n) w.W8(x); foreach (var x in v) w.W8(x); }

        static void Luma(double[] Y, int width, int x0, int y0, double[] blk)
        {
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) blk[y * 8 + x] = Y[(y0 + y) * width + x0 + x];
        }

        static void Chroma(double[] src, int width, int x0, int y0, int step, double[] blk)
        {
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
            {
                if (step == 1) blk[y * 8 + x] = src[(y0 + y) * width + x0 + x];
                else
                {
                    int sx = x0 + x * 2, sy = y0 + y * 2, i = sy * width + sx;
                    blk[y * 8 + x] = (src[i] + src[i + 1] + src[i + width] + src[i + width + 1]) / 4;
                }
            }
        }

        static int Cat(int v) { int a = Math.Abs(v), n = 0; while (a != 0) { n++; a >>= 1; } return n; }
        static int BitsOf(int v, int n) { return v < 0 ? v + (1 << n) - 1 : v; }

        static int Block(Writer w, double[] blk, int prevDC, double[] fdt, int[][] dcT, int[][] acT)
        {
            Fdct(blk);
            var q = new int[64];
            for (int i = 0; i < 64; i++) q[i] = (int)Math.Floor(blk[i] * fdt[i] + 0.5); // matches JavaScript Math.round
            var zz = new int[64];
            for (int i = 0; i < 64; i++) zz[i] = q[ZZ[i]];
            int dc = zz[0], diff = dc - prevDC;
            int n = Cat(diff); w.Bits(dcT[n][0], dcT[n][1]); if (n != 0) w.Bits(BitsOf(diff, n), n);
            int last = 63; while (last > 0 && zz[last] == 0) last--;
            int run = 0;
            for (int i = 1; i <= last; i++)
            {
                if (zz[i] == 0) { run++; if (run == 16) { w.Bits(acT[0xf0][0], acT[0xf0][1]); run = 0; } continue; }
                n = Cat(zz[i]); int sym = (run << 4) | n;
                w.Bits(acT[sym][0], acT[sym][1]); w.Bits(BitsOf(zz[i], n), n); run = 0;
            }
            if (last < 63) w.Bits(acT[0][0], acT[0][1]);
            return dc;
        }
    }
}
