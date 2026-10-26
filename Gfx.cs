// 画面の絵を描くところ。ESP32 版で使っていた LovyanGFX と同じ描き方 (円・楕円の点の並びも同じ) を、
// 1 枚の int 配列 (0xRRGGBB) に描く。窓に出すときは Program が整数倍に引き伸ばす
using System;

namespace SkyLark
{
  public static class Datum
  {
    public const int TL = 0; // 左上
    public const int TC = 1; // 上の真ん中
  }

  public class Gfx
  {
    public readonly int W, H;
    public int[] Buf;
    int clipL, clipT, clipR, clipB; // 描ける範囲 (端も含む)
    // 影: MaskMode のあいだは色を塗らずに Mask に印をつけ、ApplyShadow でまとめて暗くする (重なっても濃くならない)
    public byte[] Mask;
    public bool MaskMode;
    // 台紙: Key の色のところは透明。半透明の重ね塗りは透明のところには塗らない
    public int Key = -1;
    public int OffY; // 描くときに y に足す (画面より上まで描く台に使う)
    int[] cutCol = new int[0];
    byte[] cutMask = new byte[0];

    public Gfx(int w, int h)
    {
      W = w;
      H = h;
      Buf = new int[w * h];
      ClearClipRect();
    }

    public void UseMask()
    {
      if (Mask == null)
        Mask = new byte[W * H];
    }

    // 印をつけたところを shade の色へ a (0〜256) だけ寄せて暗くし、印を消す
    public void ApplyShadow(uint shade, int a)
    {
      int sr = (int)(shade >> 16) & 255, sg = (int)(shade >> 8) & 255, sb = (int)shade & 255, na = 256 - a;
      for (int i = 0; i < Mask.Length; i++)
        if (Mask[i] != 0)
        {
          Mask[i] = 0;
          int v = Buf[i];
          int r = (((v >> 16) & 255) * na + sr * a) >> 8, g = (((v >> 8) & 255) * na + sg * a) >> 8, b = ((v & 255) * na + sb * a) >> 8;
          Buf[i] = (r << 16) | (g << 8) | b;
        }
    }

    // 半透明に重ねる (a は 0〜256)
    public void BlendPixel(int x, int y, uint c, int a)
    {
      y += OffY;
      if (x < clipL || x > clipR || y < clipT || y > clipB)
        return;
      int i = y * W + x, v = Buf[i], na = 256 - a;
      if (v == Key)
        return;
      int r = (((v >> 16) & 255) * na + (int)((c >> 16) & 255) * a) >> 8;
      int g = (((v >> 8) & 255) * na + (int)((c >> 8) & 255) * a) >> 8;
      int b = ((v & 255) * na + (int)(c & 255) * a) >> 8;
      Buf[i] = (r << 16) | (g << 8) | b;
    }

    // 台紙の (0, 0)〜(sw, sh) を、紙の張りぼてとして描く: 左上 (dx, dy) に scx, scy 倍で置き、
    // まわりに 1 点の白い紙のふち、その右下に厚紙の切り口の暗い線をつける
    public void BlitCutout(Gfx src, int sw, int sh, float dx, float dy, float scx, float scy, uint paper, uint edge)
    {
      if (scx <= 0.05f || scy <= 0.05f)
        return;
      int dw = Math.Max(1, (int)Math.Round(sw * scx)), dh = Math.Max(1, (int)Math.Round(sh * scy));
      int mw = dw + 4, mh = dh + 4, n = mw * mh;
      if (cutMask.Length < n)
      {
        cutMask = new byte[n * 2];
        cutCol = new int[n * 2];
      }
      Array.Clear(cutMask, 0, n);
      int key = src.Key;
      bool any = false;
      for (int j = 0; j < dh; j++)
      {
        int syy = Math.Min(sh - 1, (int)(j / scy)) * src.W;
        for (int i = 0; i < dw; i++)
        {
          int c = src.Buf[syy + Math.Min(sw - 1, (int)(i / scx))];
          if (c == key)
            continue;
          int m = (j + 2) * mw + i + 2;
          cutMask[m] = 1;
          cutCol[m] = c;
          any = true;
        }
      }
      if (!any)
        return;
      for (int j = 1; j < mh - 1; j++) // 紙のふち
        for (int i = 1; i < mw - 1; i++)
        {
          int m = j * mw + i;
          if (cutMask[m] == 0 && (cutMask[m - 1] == 1 || cutMask[m + 1] == 1 || cutMask[m - mw] == 1 || cutMask[m + mw] == 1))
            cutMask[m] = 2;
        }
      for (int j = mh - 1; j >= 1; j--) // 切り口
        for (int i = mw - 1; i >= 1; i--)
        {
          int m = j * mw + i;
          if (cutMask[m] == 0 && cutMask[m - mw - 1] != 0 && cutMask[m - mw - 1] != 3)
            cutMask[m] = 3;
        }
      int x0 = (int)Math.Floor(dx) - 2, y0 = (int)Math.Floor(dy) - 2;
      int p = (int)(paper & 0xFFFFFF), e = (int)(edge & 0xFFFFFF);
      for (int j = 0; j < mh; j++)
      {
        int yy = y0 + j;
        if (yy < clipT || yy > clipB)
          continue;
        for (int i = 0; i < mw; i++)
        {
          int xx = x0 + i;
          byte m = cutMask[j * mw + i];
          if (m == 0 || xx < clipL || xx > clipR)
            continue;
          Buf[yy * W + xx] = m == 1 ? cutCol[j * mw + i] : m == 2 ? p : e;
        }
      }
    }

    // 点の絵を描く。flip で左右反転。原点 (OX, OY) が (x, y) に来る
    public void Blit(Sprite s, int x, int y, bool flip)
    {
      x -= flip ? s.W - 1 - s.OX : s.OX;
      y -= s.OY;
      for (int r = 0; r < s.H; r++)
      {
        string row = s.Rows[r];
        for (int c = 0; c < row.Length; c++)
        {
          char ch = row[c];
          if (ch == '.')
            continue;
          DrawPixel(flip ? x + s.W - 1 - c : x + c, y + r, s.Pal[ch & 127]);
        }
      }
    }
    public void Blit(Sprite s, int x, int y, bool flip, uint[] pal) // 色を差しかえて描く
    {
      uint[] keep = s.Pal;
      s.Pal = pal;
      Blit(s, x, y, flip);
      s.Pal = keep;
    }

    public void SetClipRect(int x, int y, int w, int h)
    {
      clipL = Math.Max(0, x);
      clipT = Math.Max(0, y);
      clipR = Math.Min(W - 1, x + w - 1);
      clipB = Math.Min(H - 1, y + h - 1);
    }
    public void ClearClipRect()
    {
      clipL = 0;
      clipT = 0;
      clipR = W - 1;
      clipB = H - 1;
    }

    public void FillRect(int x, int y, int w, int h, uint c)
    {
      y += OffY;
      if (w < 0) { x += w + 1; w = -w; }
      if (h < 0) { y += h + 1; h = -h; }
      int x0 = Math.Max(x, clipL), y0 = Math.Max(y, clipT);
      int x1 = Math.Min(x + w - 1, clipR), y1 = Math.Min(y + h - 1, clipB);
      if (x0 > x1 || y0 > y1)
        return;
      if (MaskMode)
      {
        for (int yy = y0; yy <= y1; yy++)
          for (int xx = x0; xx <= x1; xx++)
            Mask[yy * W + xx] = 1;
        return;
      }
      int col = (int)(c & 0xFFFFFF);
      for (int yy = y0; yy <= y1; yy++)
      {
        int p = yy * W + x0;
        for (int xx = x0; xx <= x1; xx++)
          Buf[p++] = col;
      }
    }
    public void DrawPixel(int x, int y, uint c)
    {
      y += OffY;
      if (x < clipL || x > clipR || y < clipT || y > clipB)
        return;
      if (MaskMode)
        Mask[y * W + x] = 1;
      else
        Buf[y * W + x] = (int)(c & 0xFFFFFF);
    }
    public void DrawFastHLine(int x, int y, int w, uint c) { FillRect(x, y, w, 1, c); }
    public void DrawFastVLine(int x, int y, int h, uint c) { FillRect(x, y, 1, h, c); }
    public void DrawRect(int x, int y, int w, int h, uint c)
    {
      DrawFastHLine(x, y, w, c);
      DrawFastHLine(x, y + h - 1, w, c);
      DrawFastVLine(x, y + 1, h - 2, c);
      DrawFastVLine(x + w - 1, y + 1, h - 2, c);
    }

    // ブレゼンハムの線 (LovyanGFX と同じく、横か縦の一続きの点をまとめて塗る)
    public void DrawLine(int x0, int y0, int x1, int y1, uint c)
    {
      bool steep = Math.Abs(y1 - y0) > Math.Abs(x1 - x0);
      int t;
      if (steep)
      {
        t = x0; x0 = y0; y0 = t;
        t = x1; x1 = y1; y1 = t;
      }
      if (x0 > x1)
      {
        t = x0; x0 = x1; x1 = t;
        t = y0; y0 = y1; y1 = t;
      }
      int dy = Math.Abs(y1 - y0), ystep = y1 > y0 ? 1 : -1, dx = x1 - x0, err = dx >> 1;
      int xs = x0, len = 0;
      for (; x0 <= x1; x0++)
      {
        len++;
        if ((err -= dy) < 0)
        {
          if (steep) FillRect(y0, xs, 1, len, c); else FillRect(xs, y0, len, 1, c);
          err += dx;
          xs = x0 + 1;
          len = 0;
          y0 += ystep;
        }
      }
      if (len > 0)
      {
        if (steep) FillRect(y0, xs, 1, len, c); else FillRect(xs, y0, len, 1, c);
      }
    }

    public void DrawCircle(int x, int y, int r, uint c)
    {
      if (r <= 0)
      {
        DrawPixel(x, y, c);
        return;
      }
      int f = 1 - r, ddFy = -(r << 1), ddFx = 1, i = 0, j = -1;
      do
      {
        while (f < 0)
        {
          ++i;
          f += (ddFx += 2);
        }
        f += (ddFy += 2);
        DrawFastHLine(x - i, y + r, i - j, c);
        DrawFastHLine(x - i, y - r, i - j, c);
        DrawFastHLine(x + j + 1, y - r, i - j, c);
        DrawFastHLine(x + j + 1, y + r, i - j, c);
        DrawFastVLine(x + r, y + j + 1, i - j, c);
        DrawFastVLine(x + r, y - i, i - j, c);
        DrawFastVLine(x - r, y - i, i - j, c);
        DrawFastVLine(x - r, y + j + 1, i - j, c);
        j = i;
      } while (i < --r);
    }

    public void FillCircle(int x, int y, int r, uint c)
    {
      DrawFastHLine(x - r, y, (r << 1) + 1, c);
      if (r <= 0)
        return;
      int f = 1 - r, ddFy = -(r << 1), ddFx = 1, i = 0;
      do
      {
        int len = 0;
        while (f < 0)
        {
          f += (ddFx += 2);
          ++len;
        }
        i += len;
        f += (ddFy += 2);
        if (len > 0) FillRect(x - r, y + i - len + 1, (r << 1) + 1, len, c);
        DrawFastHLine(x - i, y + r, (i << 1) + 1, c);
        DrawFastHLine(x - i, y - r, (i << 1) + 1, c);
        if (len > 0) FillRect(x - r, y - i, (r << 1) + 1, len, c);
      } while (i < --r);
    }

    public void FillEllipse(int x, int y, int rx, int ry, uint c)
    {
      if (ry == 0)
      {
        DrawFastHLine(x - rx, y, (rx << 1) + 1, c);
        return;
      }
      if (rx == 0)
      {
        DrawFastVLine(x, y - ry, (ry << 1) + 1, c);
        return;
      }
      if (rx < 0 || ry < 0)
        return;
      long rx2 = rx * rx, ry2 = ry * ry, s;
      int xt, yt, i;
      DrawFastHLine(x - rx, y, (rx << 1) + 1, c);
      i = 0;
      yt = 0;
      xt = rx;
      s = (rx2 << 1) + ry2 * (1 - (rx << 1));
      do
      {
        while (s < 0) s += rx2 * ((++yt << 2) + 2);
        FillRect(x - xt, y - yt, (xt << 1) + 1, yt - i, c);
        FillRect(x - xt, y + i + 1, (xt << 1) + 1, yt - i, c);
        i = yt;
        s -= (--xt) * ry2 << 2;
      } while (rx2 * yt <= ry2 * xt);
      xt = 0;
      yt = ry;
      s = (ry2 << 1) + rx2 * (1 - (ry << 1));
      do
      {
        while (s < 0) s += ry2 * ((++xt << 2) + 2);
        DrawFastHLine(x - xt, y - yt, (xt << 1) + 1, c);
        DrawFastHLine(x - xt, y + yt, (xt << 1) + 1, c);
        s -= (--yt) * rx2 << 2;
      } while (ry2 * xt <= rx2 * yt);
    }

    public void FillRoundRect(int x, int y, int w, int h, int r, uint c)
    {
      FillRect(x, y + r, w, h - 2 * r, c);
      for (int k = 0; k < r; k++)
      {
        int dy = r - k;
        int dx = r - (int)Math.Sqrt(r * r - (dy - 0.5) * (dy - 0.5));
        FillRect(x + dx, y + k, w - 2 * dx, 1, c);
        FillRect(x + dx, y + h - 1 - k, w - 2 * dx, 1, c);
      }
    }

    // 三角形の塗り。上から 1 行ずつ、左右の辺のあいだを塗る
    public void FillTriangle(int x0, int y0, int x1, int y1, int x2, int y2, uint c)
    {
      int t;
      if (y0 > y1) { t = y0; y0 = y1; y1 = t; t = x0; x0 = x1; x1 = t; }
      if (y1 > y2) { t = y2; y2 = y1; y1 = t; t = x2; x2 = x1; x1 = t; }
      if (y0 > y1) { t = y0; y0 = y1; y1 = t; t = x0; x0 = x1; x1 = t; }
      if (y0 == y2)
      {
        int a = Math.Min(x0, Math.Min(x1, x2)), b = Math.Max(x0, Math.Max(x1, x2));
        DrawFastHLine(a, y0, b - a + 1, c);
        return;
      }
      int dx01 = x1 - x0, dy01 = y1 - y0, dx02 = x2 - x0, dy02 = y2 - y0, dx12 = x2 - x1, dy12 = y2 - y1;
      int sa = 0, sb = 0, last = y1 == y2 ? y1 : y1 - 1, yy;
      for (yy = y0; yy <= last; yy++)
      {
        int a = x0 + (dy01 != 0 ? sa / dy01 : 0), b = x0 + sb / dy02;
        sa += dx01;
        sb += dx02;
        if (a > b) { t = a; a = b; b = t; }
        DrawFastHLine(a, yy, b - a + 1, c);
      }
      sa = dx12 * (yy - y1);
      sb = dx02 * (yy - y0);
      for (; yy <= y2; yy++)
      {
        int a = x1 + (dy12 != 0 ? sa / dy12 : 0), b = x0 + sb / dy02;
        sa += dx12;
        sb += dx02;
        if (a > b) { t = a; a = b; b = t; }
        DrawFastHLine(a, yy, b - a + 1, c);
      }
    }

    // ---- 文字 (6x8 の点の文字を size 倍。Font0 と同じ字形) ----
    public static int TextWidth(string s, int size) { return s.Length * 6 * size; }

    public void Text(int x, int y, uint c, string s, int datum, int size)
    {
      if (datum == Datum.TC)
        x -= TextWidth(s, size) / 2;
      for (int n = 0; n < s.Length; n++, x += 6 * size)
      {
        int ch = s[n];
        if (ch < 32 || ch > 127)
          continue;
        int b = (ch - 32) * 5;
        for (int col = 0; col < 5; col++)
        {
          int line = Font[b + col];
          for (int row = 0; row < 8; row++)
            if ((line >> row & 1) != 0)
              FillRect(x + col * size, y + row * size, size, size, c);
        }
      }
    }
    public void Text(int x, int y, uint c, string s) { Text(x, y, c, s, Datum.TL, 1); }
    public void Text(int x, int y, uint c, string s, int datum) { Text(x, y, c, s, datum, 1); }

    // Adafruit GFX の 'classic' 5x7 文字 (BSD License, Copyright (c) 2012 Adafruit Industries)。' ' 〜 0x7F
    static readonly byte[] Font = {
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x5F, 0x00, 0x00, 0x00, 0x07, 0x00, 0x07, 0x00, 0x14, 0x7F, 0x14, 0x7F, 0x14,
    0x24, 0x2A, 0x7F, 0x2A, 0x12, 0x23, 0x13, 0x08, 0x64, 0x62, 0x36, 0x49, 0x56, 0x20, 0x50, 0x00, 0x08, 0x07, 0x03, 0x00,
    0x00, 0x1C, 0x22, 0x41, 0x00, 0x00, 0x41, 0x22, 0x1C, 0x00, 0x2A, 0x1C, 0x7F, 0x1C, 0x2A, 0x08, 0x08, 0x3E, 0x08, 0x08,
    0x00, 0x80, 0x70, 0x30, 0x00, 0x08, 0x08, 0x08, 0x08, 0x08, 0x00, 0x00, 0x60, 0x60, 0x00, 0x20, 0x10, 0x08, 0x04, 0x02,
    0x3E, 0x51, 0x49, 0x45, 0x3E, 0x00, 0x42, 0x7F, 0x40, 0x00, 0x72, 0x49, 0x49, 0x49, 0x46, 0x21, 0x41, 0x49, 0x4D, 0x33,
    0x18, 0x14, 0x12, 0x7F, 0x10, 0x27, 0x45, 0x45, 0x45, 0x39, 0x3C, 0x4A, 0x49, 0x49, 0x31, 0x41, 0x21, 0x11, 0x09, 0x07,
    0x36, 0x49, 0x49, 0x49, 0x36, 0x46, 0x49, 0x49, 0x29, 0x1E, 0x00, 0x00, 0x14, 0x00, 0x00, 0x00, 0x40, 0x34, 0x00, 0x00,
    0x00, 0x08, 0x14, 0x22, 0x41, 0x14, 0x14, 0x14, 0x14, 0x14, 0x00, 0x41, 0x22, 0x14, 0x08, 0x02, 0x01, 0x59, 0x09, 0x06,
    0x3E, 0x41, 0x5D, 0x59, 0x4E, 0x7C, 0x12, 0x11, 0x12, 0x7C, 0x7F, 0x49, 0x49, 0x49, 0x36, 0x3E, 0x41, 0x41, 0x41, 0x22,
    0x7F, 0x41, 0x41, 0x41, 0x3E, 0x7F, 0x49, 0x49, 0x49, 0x41, 0x7F, 0x09, 0x09, 0x09, 0x01, 0x3E, 0x41, 0x41, 0x51, 0x73,
    0x7F, 0x08, 0x08, 0x08, 0x7F, 0x00, 0x41, 0x7F, 0x41, 0x00, 0x20, 0x40, 0x41, 0x3F, 0x01, 0x7F, 0x08, 0x14, 0x22, 0x41,
    0x7F, 0x40, 0x40, 0x40, 0x40, 0x7F, 0x02, 0x1C, 0x02, 0x7F, 0x7F, 0x04, 0x08, 0x10, 0x7F, 0x3E, 0x41, 0x41, 0x41, 0x3E,
    0x7F, 0x09, 0x09, 0x09, 0x06, 0x3E, 0x41, 0x51, 0x21, 0x5E, 0x7F, 0x09, 0x19, 0x29, 0x46, 0x26, 0x49, 0x49, 0x49, 0x32,
    0x03, 0x01, 0x7F, 0x01, 0x03, 0x3F, 0x40, 0x40, 0x40, 0x3F, 0x1F, 0x20, 0x40, 0x20, 0x1F, 0x3F, 0x40, 0x38, 0x40, 0x3F,
    0x63, 0x14, 0x08, 0x14, 0x63, 0x03, 0x04, 0x78, 0x04, 0x03, 0x61, 0x59, 0x49, 0x4D, 0x43, 0x00, 0x7F, 0x41, 0x41, 0x41,
    0x02, 0x04, 0x08, 0x10, 0x20, 0x00, 0x41, 0x41, 0x41, 0x7F, 0x04, 0x02, 0x01, 0x02, 0x04, 0x40, 0x40, 0x40, 0x40, 0x40,
    0x00, 0x03, 0x07, 0x08, 0x00, 0x20, 0x54, 0x54, 0x78, 0x40, 0x7F, 0x28, 0x44, 0x44, 0x38, 0x38, 0x44, 0x44, 0x44, 0x28,
    0x38, 0x44, 0x44, 0x28, 0x7F, 0x38, 0x54, 0x54, 0x54, 0x18, 0x00, 0x08, 0x7E, 0x09, 0x02, 0x18, 0xA4, 0xA4, 0x9C, 0x78,
    0x7F, 0x08, 0x04, 0x04, 0x78, 0x00, 0x44, 0x7D, 0x40, 0x00, 0x20, 0x40, 0x40, 0x3D, 0x00, 0x7F, 0x10, 0x28, 0x44, 0x00,
    0x00, 0x41, 0x7F, 0x40, 0x00, 0x7C, 0x04, 0x78, 0x04, 0x78, 0x7C, 0x08, 0x04, 0x04, 0x78, 0x38, 0x44, 0x44, 0x44, 0x38,
    0xFC, 0x18, 0x24, 0x24, 0x18, 0x18, 0x24, 0x24, 0x18, 0xFC, 0x7C, 0x08, 0x04, 0x04, 0x08, 0x48, 0x54, 0x54, 0x54, 0x24,
    0x04, 0x04, 0x3F, 0x44, 0x24, 0x3C, 0x40, 0x40, 0x20, 0x7C, 0x1C, 0x20, 0x40, 0x20, 0x1C, 0x3C, 0x40, 0x30, 0x40, 0x3C,
    0x44, 0x28, 0x10, 0x28, 0x44, 0x4C, 0x90, 0x90, 0x90, 0x7C, 0x44, 0x64, 0x54, 0x4C, 0x44, 0x00, 0x08, 0x36, 0x41, 0x00,
    0x00, 0x00, 0x77, 0x00, 0x00, 0x00, 0x41, 0x36, 0x08, 0x00, 0x02, 0x01, 0x02, 0x04, 0x02, 0x3C, 0x26, 0x23, 0x26, 0x3C,
    };
  }

  // 文字で描いた点の絵。'.' は透明、ほかの文字は pal の色。pal は "o=2E3420 G=6E7444 ..." の形で渡す
  public class Sprite
  {
    public readonly string[] Rows;
    public readonly int W, H;
    public int OX, OY;
    public uint[] Pal = new uint[128];

    public Sprite(string[] rows, string pal, int ox, int oy)
    {
      Rows = rows;
      H = rows.Length;
      foreach (string r in rows)
        W = Math.Max(W, r.Length);
      OX = ox;
      OY = oy;
      foreach (string e in pal.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
        Pal[e[0] & 127] = Convert.ToUInt32(e.Substring(2), 16);
    }

    // 左半分 (真ん中の列まで) だけ書いて、右半分は鏡に映して作る
    public static string[] Mirror(string[] halves)
    {
      string[] o = new string[halves.Length];
      for (int i = 0; i < halves.Length; i++)
      {
        string h = halves[i];
        char[] right = h.Substring(0, h.Length - 1).ToCharArray();
        Array.Reverse(right);
        o[i] = h + new string(right);
      }
      return o;
    }

    public uint[] Recolor(string pal) // 一部の色だけ差しかえた色の表
    {
      uint[] p = (uint[])Pal.Clone();
      foreach (string e in pal.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
        p[e[0] & 127] = Convert.ToUInt32(e.Substring(2), 16);
      return p;
    }
  }
}
