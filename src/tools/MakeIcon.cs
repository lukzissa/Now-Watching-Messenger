// Converte a imagem do icone (png com transparencia, ou fundo branco) em icon.ico multi-tamanho.
// Obs.: o decodificador WebP do Windows descarta o canal alfa; converter webp para png antes (ex.: Edge headless).
// Uso: MakeIcon.exe <entrada> <saida.ico> [preview.png]
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

static class MakeIcon
{
    static void Main(string[] args)
    {
        var dec = BitmapDecoder.Create(new Uri(Path.GetFullPath(args[0])), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var src = new FormatConvertedBitmap(dec.Frames[0], System.Windows.Media.PixelFormats.Bgra32, null, 0);
        int w = src.PixelWidth, h = src.PixelHeight;
        var px = new byte[w * h * 4];
        src.CopyPixels(px, w * 4, 0);

        bool hasAlpha = false;
        for (int i = 3; i < px.Length; i += 4) if (px[i] < 255) { hasAlpha = true; break; }
        if (!hasAlpha) RemoveBackground(px, w, h);

        // recorta ao conteudo e centraliza num quadrado com margem
        int minX = w, minY = h, maxX = 0, maxY = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (px[(y * w + x) * 4 + 3] > 8)
                {
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                }
        int cw = maxX - minX + 1, ch = maxY - minY + 1;
        int side = (int)(Math.Max(cw, ch) * 1.02);

        var full = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var bd = full.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(px, 0, bd.Scan0, px.Length);
        full.UnlockBits(bd);

        var master = new Bitmap(side, side, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(master))
        {
            g.Clear(Color.Transparent);
            g.DrawImage(full, (side - cw) / 2, (side - ch) / 2, new Rectangle(minX, minY, cw, ch), GraphicsUnit.Pixel);
        }
        if (args.Length > 2) Resize(master, 256).Save(args[2], ImageFormat.Png);

        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        var images = new List<byte[]>();
        foreach (int s in sizes)
        {
            var bmp = Resize(master, s);
            images.Add(s >= 64 ? Png(bmp) : Dib(bmp));
        }

        using (var fs = File.Create(args[1]))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write((short)0); bw.Write((short)1); bw.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                bw.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                bw.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                bw.Write((byte)0); bw.Write((byte)0);
                bw.Write((short)1); bw.Write((short)32);
                bw.Write(images[i].Length); bw.Write(offset);
                offset += images[i].Length;
            }
            foreach (var img in images) bw.Write(img);
        }
    }

    // Flood fill a partir das bordas: branco/quase branco vira transparente; borda antialias vira semitransparente.
    static void RemoveBackground(byte[] px, int w, int h)
    {
        var visited = new bool[w * h];
        var q = new Queue<int>();
        for (int x = 0; x < w; x++) { q.Enqueue(x); q.Enqueue((h - 1) * w + x); }
        for (int y = 0; y < h; y++) { q.Enqueue(y * w); q.Enqueue(y * w + w - 1); }
        while (q.Count > 0)
        {
            int i = q.Dequeue();
            if (visited[i]) continue;
            visited[i] = true;
            int b = px[i * 4], g = px[i * 4 + 1], r = px[i * 4 + 2];
            int min = Math.Min(r, Math.Min(g, b));
            if (min < 200) continue; // chegou no contorno
            // alpha proporcional a "distancia do branco"; desmultiplica a cor contra fundo branco
            int a = min >= 245 ? 0 : (int)((245 - min) * 255.0 / 45);
            px[i * 4 + 3] = (byte)a;
            if (a > 0)
            {
                double af = a / 255.0;
                for (int c = 0; c < 3; c++)
                    px[i * 4 + c] = (byte)Math.Max(0, Math.Min(255, (px[i * 4 + c] - 255 * (1 - af)) / af));
            }
            int x0 = i % w, y0 = i / w;
            if (x0 > 0) q.Enqueue(i - 1);
            if (x0 < w - 1) q.Enqueue(i + 1);
            if (y0 > 0) q.Enqueue(i - w);
            if (y0 < h - 1) q.Enqueue(i + w);
        }
    }

    static Bitmap Resize(Bitmap src, int s)
    {
        var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        using (var ia = new ImageAttributes())
        {
            ia.SetWrapMode(WrapMode.TileFlipXY);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.Clear(Color.Transparent);
            g.DrawImage(src, new Rectangle(0, 0, s, s), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
        }
        return bmp;
    }

    static byte[] Png(Bitmap bmp)
    {
        using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); return ms.ToArray(); }
    }

    // Entrada BMP classica (BITMAPINFOHEADER + BGRA bottom-up + mascara AND), compativel com System.Drawing.Icon
    static byte[] Dib(Bitmap bmp)
    {
        int s = bmp.Width;
        int maskStride = ((s + 31) / 32) * 4;
        using (var ms = new MemoryStream())
        using (var bw = new BinaryWriter(ms))
        {
            bw.Write(40); bw.Write(s); bw.Write(s * 2); bw.Write((short)1); bw.Write((short)32);
            bw.Write(0); bw.Write(s * s * 4 + maskStride * s); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
            for (int y = s - 1; y >= 0; y--)
                for (int x = 0; x < s; x++)
                {
                    var c = bmp.GetPixel(x, y);
                    bw.Write(c.B); bw.Write(c.G); bw.Write(c.R); bw.Write(c.A);
                }
            for (int y = s - 1; y >= 0; y--)
            {
                var row = new byte[maskStride];
                for (int x = 0; x < s; x++)
                    if (bmp.GetPixel(x, y).A == 0) row[x / 8] |= (byte)(0x80 >> (x % 8));
                bw.Write(row);
            }
            return ms.ToArray();
        }
    }
}
