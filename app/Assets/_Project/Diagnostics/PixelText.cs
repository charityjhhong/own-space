using UnityEngine;

namespace OwnSpace.Diagnostics
{
    /// <summary>
    /// Tiny 3x5 bitmap font drawn as quads, so the overlay needs no font, TextMeshPro asset or string.
    /// All writes go into preallocated buffers: nothing here allocates after construction.
    /// </summary>
    public sealed class QuadBatch
    {
        readonly Vector3[] _verts;
        readonly int[] _tris;
        int _quads;

        public QuadBatch(int maxQuads)
        {
            _verts = new Vector3[maxQuads * 4];
            _tris = new int[maxQuads * 6];
            for (int q = 0; q < maxQuads; q++)
            {
                int v = q * 4, t = q * 6;
                _tris[t] = v; _tris[t + 1] = v + 1; _tris[t + 2] = v + 2;
                _tris[t + 3] = v; _tris[t + 4] = v + 2; _tris[t + 5] = v + 3;
            }
        }

        public int Quads => _quads;
        public void Clear() => _quads = 0;

        public bool AddQuad(float x, float y, float size)
        {
            if (_quads * 4 >= _verts.Length) return false;
            int v = _quads * 4;
            _verts[v] = new Vector3(x, y, 0f);
            _verts[v + 1] = new Vector3(x, y + size, 0f);
            _verts[v + 2] = new Vector3(x + size, y + size, 0f);
            _verts[v + 3] = new Vector3(x + size, y, 0f);
            _quads++;
            return true;
        }

        public void Upload(Mesh mesh)
        {
            mesh.Clear();
            mesh.SetVertices(_verts, 0, _quads * 4);
            mesh.SetIndices(_tris, 0, _quads * 6, MeshTopology.Triangles, 0);
        }
    }

    public static class PixelText
    {
        public const int GlyphWidth = 3, GlyphHeight = 5, Advance = 4;

        /// <summary>Draws text[0..length) with its top-left at (x, y); returns the x after the last glyph.</summary>
        public static float Draw(QuadBatch batch, char[] text, int length, float x, float y, float pixel)
        {
            for (int i = 0; i < length; i++)
            {
                ushort bits = Glyph(text[i]);
                for (int row = 0; row < GlyphHeight; row++)
                    for (int col = 0; col < GlyphWidth; col++)
                        if ((bits & (1 << ((GlyphHeight - 1 - row) * GlyphWidth + (GlyphWidth - 1 - col)))) != 0)
                            batch.AddQuad(x + col * pixel, y - (row + 1) * pixel, pixel);
                x += Advance * pixel;
            }
            return x;
        }

        // One row = 3 bits, top row in the highest bits.
        static ushort Glyph(char c)
        {
            switch (c)
            {
                case '0': return 0b111_101_101_101_111;
                case '1': return 0b010_110_010_010_111;
                case '2': return 0b111_001_111_100_111;
                case '3': return 0b111_001_111_001_111;
                case '4': return 0b101_101_111_001_001;
                case '5': case 'S': return 0b111_100_111_001_111;
                case '6': return 0b111_100_111_101_111;
                case '7': return 0b111_001_001_010_010;
                case '8': return 0b111_101_111_101_111;
                case '9': return 0b111_101_111_001_111;
                case '.': return 0b000_000_000_000_010;
                case '-': return 0b000_000_111_000_000;
                case 'F': return 0b111_100_110_100_100;
                case 'P': return 0b111_101_111_100_100;
                case 'C': return 0b111_100_100_100_111;
                case 'U': return 0b101_101_101_101_111;
                case 'G': return 0b111_100_101_101_111;
                case 'B': return 0b110_101_110_101_110;
                case 'M': return 0b101_111_111_101_101;
                case 'N': return 0b110_101_101_101_101;
                case 'A': return 0b010_101_111_101_101;
                case '/': return 0b001_001_010_100_100;
                default: return 0;
            }
        }

        /// <summary>Writes a non-negative integer into buf at pos; returns the new length. No allocation.</summary>
        public static int WriteInt(char[] buf, int pos, long value)
        {
            if (value < 0) value = 0;
            int digits = 1;
            for (long t = value; t >= 10; t /= 10) digits++;
            for (int i = digits - 1; i >= 0; i--) { buf[pos + i] = (char)('0' + (int)(value % 10)); value /= 10; }
            return pos + digits;
        }

        /// <summary>Writes a value with one decimal, e.g. 8.2. No allocation.</summary>
        public static int WriteTenths(char[] buf, int pos, double value)
        {
            long tenths = (long)(value * 10.0 + 0.5);
            pos = WriteInt(buf, pos, tenths / 10);
            buf[pos++] = '.';
            buf[pos++] = (char)('0' + (int)(tenths % 10));
            return pos;
        }

        public static int WriteLabel(char[] buf, int pos, string label)
        {
            for (int i = 0; i < label.Length; i++) buf[pos++] = label[i];
            return pos;
        }
    }
}
