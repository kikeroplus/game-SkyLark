// 案 B の見本: ドット絵をやめて、640x1080 そのものを、なめらかな線とグラデーションで描く (絵本風)。
// いまは 1 枚の画像を書き出すだけの試作。ゲームの状態 (地形・置き物・円盤・自機) は A と同じものを使う。
// 世界の座標 (213x360) のまま描き、最後に 3 倍する (ScaleTransform)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace SkyLark
{
  public partial class Game
  {
    static Color C(uint rgb) { return Color.FromArgb(255, (int)(rgb >> 16) & 255, (int)(rgb >> 8) & 255, (int)rgb & 255); }
    static Color C(uint rgb, int a) { return Color.FromArgb(a, (int)(rgb >> 16) & 255, (int)(rgb >> 8) & 255, (int)rgb & 255); }
    static Color Lighter(uint rgb, float t) { return C(mix(rgb, 0xFFFFFF, t)); }
    static Color Darker(uint rgb, float t) { return C(mix(rgb, 0x101808, t)); }

    // 丸い塊を、左上が明るい放射グラデーションで塗る
    static void Ball(Graphics g, float x, float y, float r, uint c, float light)
    {
      using (GraphicsPath gp = new GraphicsPath())
      {
        gp.AddEllipse(x - r, y - r, r * 2, r * 2);
        using (PathGradientBrush b = new PathGradientBrush(gp))
        {
          b.CenterPoint = new PointF(x - r * 0.35f, y - r * 0.4f);
          b.CenterColor = Lighter(c, light);
          b.SurroundColors = new[] { Darker(c, 0.25f) };
          g.FillPath(b, gp);
        }
      }
    }

    // ぼかした影: 少しずつ大きく薄い楕円を重ねる
    static void SoftShadow(Graphics g, float x, float y, float rx, float ry, int alpha)
    {
      for (int i = 3; i >= 0; i--)
        using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha / 4, 20, 30, 12)))
          g.FillEllipse(b, x - rx - i * 0.5f, y - ry - i * 0.5f, (rx + i * 0.5f) * 2, (ry + i * 0.5f) * 2);
    }
    static void SoftShadowRect(Graphics g, float x, float y, float w, float h, int alpha)
    {
      for (int i = 3; i >= 0; i--)
        using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha / 4, 20, 30, 12)))
        using (GraphicsPath gp = RoundRect(x - i * 0.5f, y - i * 0.5f, w + i, h + i, 1.5f + i * 0.5f))
          g.FillPath(b, gp);
    }

    static GraphicsPath RoundRect(float x, float y, float w, float h, float r)
    {
      GraphicsPath gp = new GraphicsPath();
      r = Math.Min(r, Math.Min(w, h) / 2);
      if (r <= 0.01f)
      {
        gp.AddRectangle(new RectangleF(x, y, w, h));
        return gp;
      }
      gp.AddArc(x, y, r * 2, r * 2, 180, 90);
      gp.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
      gp.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
      gp.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
      gp.CloseFigure();
      return gp;
    }

    static void FillGrad(Graphics g, GraphicsPath gp, RectangleF r, uint top, uint bottom, float angle)
    {
      using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(r.X - 0.5f, r.Y - 0.5f, r.Width + 1, r.Height + 1), C(top), C(bottom), angle))
        g.FillPath(b, gp);
    }

    // ---- 見本を 1 枚描いて保存する ----
    public void RenderIllustrated(string path)
    {
      using (Bitmap bm = new Bitmap(640, 1080, PixelFormat.Format32bppArgb))
      {
        RenderIllustrated(bm);
        bm.Save(path, ImageFormat.Png);
      }
    }

    public void RenderIllustrated(Bitmap bm)
    {
      lerpAll(1);
      const int OW = 640, OH = 1080;
      float k = OH / (float)H; // 3.0
      using (Graphics g = Graphics.FromImage(bm))
      {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(C(0x6E9A48));
        g.TranslateTransform((OW - W * k) / 2, 0);
        g.ScaleTransform(k, k);
        drawGroundB(g);
        drawPropsB(g);
        drawSkyB(g);
        g.ResetTransform();
        drawVignette(g, OW, OH);
        drawHudB(g, OW, OH);
      }
    }

    float rowTop(int r) { return (r - 1 - FAR) * T + rscroll; }

    void drawGroundB(Graphics g)
    {
      Random rnd = new Random(1234);
      // 1. マスの地の色 (境目が出ないように、なめらかにせず少し重ねて塗る)
      g.SmoothingMode = SmoothingMode.None;
      for (int r = 0; r < ROWS; r++)
      {
        float y = rowTop(r);
        if (y < -T || y > H)
          continue;
        for (int c = 0; c < COLS; c++)
        {
          Tile t = map[r][c];
          uint col;
          switch (t.type)
          {
            case FIELD:
              col = CROPS[t.v].b;
              col = mix(col, noise(c * 0.35f, rowId[r] * 0.35f) > 0.5f ? 0xFFFFFFu : 0x2A3A18u, Math.Abs(noise(c * 0.35f, rowId[r] * 0.35f) - 0.5f) * 0.18f);
              break;
            case WOOD: col = 0x2E5226; break;
            case MOOR: col = MOOR_C[1]; break;
            case VGREEN: col = 0x86B45C; break;
            case RAIL: case RAILBRIDGE: col = 0x8E826C; break;
            default: col = 0x78A452; break; // 川や小道の下は土手の草
          }
          using (SolidBrush b = new SolidBrush(C(col)))
            g.FillRectangle(b, c * T, y, T + 0.4f, T + 0.4f);
        }
      }
      g.SmoothingMode = SmoothingMode.AntiAlias;
      // 2. 作物の模様
      for (int r = 0; r < ROWS; r++)
      {
        float y = rowTop(r);
        if (y < -T || y > H)
          continue;
        for (int c = 0; c < COLS; c++)
        {
          Tile t = map[r][c];
          float x = c * T;
          uint h = ph(c * 13 + 5, rowId[r] * 7 + 3);
          if (t.type == FIELD)
            cropTexture(g, t.v, x, y, h);
          else if (t.type == VGREEN)
            using (SolidBrush b = new SolidBrush(Color.FromArgb(28, 255, 255, 255)))
              if ((c & 1) == 0)
                g.FillRectangle(b, x, y, T, T + 0.3f);
        }
      }
      // 3. 山: 高さごとに丸を重ねて、なだらかな丘にする
      for (int lv = 1; lv <= 4; lv++)
        for (int pass = 0; pass < 3; pass++)
          for (int r = 0; r < ROWS; r++)
          {
            float y = rowTop(r);
            if (y < -T * 2 || y > H + T)
              continue;
            for (int c = 0; c < COLS; c++)
              if (map[r][c].type == MOOR && map[r][c].v >= lv)
              {
                float cx = c * T + T / 2f, cy = y + T / 2f, rr = T * (0.82f - lv * 0.04f);
                uint col = MOOR_C[lv];
                if (pass == 0)
                  using (SolidBrush b = new SolidBrush(Color.FromArgb(40, 30, 24, 20)))
                    g.FillEllipse(b, cx - rr + 1.2f, cy - rr + 1.4f, rr * 2, rr * 2);
                else if (pass == 1)
                  using (SolidBrush b = new SolidBrush(C(col)))
                    g.FillEllipse(b, cx - rr, cy - rr, rr * 2, rr * 2);
                else if (lv == 2 && (ph(c, rowId[r]) & 1) == 0)
                  using (SolidBrush b = new SolidBrush(C(0xA06A9A, 150)))
                    g.FillEllipse(b, cx - 2, cy - 1.5f, 4, 3);
              }
          }
      // 4. 川: 行ごとの水の幅をつないだ、なめらかな流れ。岸は明るい砂、まんなかは深い青
      drawRiverB(g);
      // 5. 小道: 小道のマスの真ん中をつないだ曲線
      drawLanesB(g);
      // 6. 線路
      for (int r = 0; r < ROWS; r++)
      {
        float y = rowTop(r);
        if (y < -T || y > H || (map[r][0].type != RAIL && map[r][0].type != RAILBRIDGE))
          continue;
        using (SolidBrush b = new SolidBrush(C(0x8A7E6A)))
          g.FillRectangle(b, 0, y + 0.5f, W, T - 1);
        using (Pen p = new Pen(C(0x5A4630), 1.1f))
          for (float x = 0.6f; x < W; x += 2.6f)
            g.DrawLine(p, x, y + 1.2f, x, y + T - 1.2f);
        using (Pen p = new Pen(C(0x9A9AA4), 0.55f))
        {
          g.DrawLine(p, 0, y + 2.6f, W, y + 2.6f);
          g.DrawLine(p, 0, y + 5.4f, W, y + 5.4f);
        }
      }
      // 7. 雑木林: 木のてっぺんを重ねる
      for (int r = 0; r < ROWS; r++)
      {
        float y = rowTop(r);
        if (y < -T || y > H)
          continue;
        for (int c = 0; c < COLS; c++)
          if (map[r][c].type == WOOD)
          {
            uint h = ph(c * 31 + 3, rowId[r] * 17 + 11);
            for (int i = 0; i < 3; i++)
            {
              float cx = c * T + 1.5f + (h >> (i * 6)) % 6, cy = y + 1.5f + (h >> (i * 6 + 3)) % 6, rr = 2.6f + ((h >> (20 + i)) & 1);
              SoftShadow(g, cx + 1.4f, cy + 1.6f, rr, rr * 0.9f, 70);
              Ball(g, cx, cy, rr, 0x3E6E30, 0.35f);
            }
          }
      }
      // 8. 生け垣と石垣: 小さな茂みを並べ、右下に影を落とす
      for (int pass = 0; pass < 2; pass++)
        for (int r = 0; r < ROWS; r++)
        {
          float y = rowTop(r);
          if (y < -T || y > H)
            continue;
          for (int c = 0; c < COLS; c++)
          {
            Tile t = map[r][c];
            if ((t.flags & (HEDGE_TOP | HEDGE_LEFT)) == 0)
              continue;
            bool wall = (t.flags & STONEWALL) != 0;
            float x = c * T;
            if ((t.flags & HEDGE_TOP) != 0)
              hedgeLine(g, x, y + T - 0.6f, x + T, y + T - 0.6f, wall, pass, (uint)(c * 7 + rowId[r]));
            if ((t.flags & HEDGE_LEFT) != 0)
              hedgeLine(g, x + 0.4f, y, x + 0.4f, y + T, wall, pass, (uint)(c * 11 + rowId[r] * 3));
          }
        }
    }

    void cropTexture(Graphics g, int crop, float x, float y, uint h)
    {
      float wy = -rowIdAt(y) * T; // 世界での縦の位置 (模様を行の境でつなげる)
      switch (crop)
      {
        case PASTURE: // 草の房
          using (Pen p = new Pen(C(0x587E36, 150), 0.35f))
            for (int i = 0; i < 4; i++)
            {
              float px = x + (h >> (i * 4)) % 11 + 0.5f, py = y + (h >> (i * 4 + 2)) % 11 + 0.5f;
              g.DrawLine(p, px, py + 1, px - 0.5f, py - 0.3f);
              g.DrawLine(p, px, py + 1, px + 0.6f, py - 0.2f);
            }
          break;
        case MEADOW: // 小さな花
          for (int i = 0; i < 5; i++)
          {
            uint fc = i % 3 == 0 ? 0xFFFFF0u : i % 3 == 1 ? 0xF4D44Au : 0xEE9AB8u;
            float px = x + (h >> (i * 5)) % 7 + 0.5f, py = y + (h >> (i * 5 + 3)) % 7 + 0.5f;
            using (SolidBrush b = new SolidBrush(C(fc, 230)))
              g.FillEllipse(b, px, py, 0.8f, 0.8f);
          }
          break;
        case WHEAT: // 畝: 明るい線と暗い線
        case 3:
          {
            uint lo = crop == WHEAT ? 0xA88E46u : 0x5A422Cu, hi = crop == WHEAT ? 0xE6D088u : 0xA8865C;
            float ph0 = ((wy % 2.4f) + 2.4f) % 2.4f;
            using (Pen pl = new Pen(C(hi, 140), 0.5f))
            using (Pen pd = new Pen(C(lo, 150), crop == WHEAT ? 0.45f : 0.7f))
              for (float yy = y + ph0; yy < y + T; yy += 2.4f)
              {
                g.DrawLine(pl, x, yy, x + T, yy);
                g.DrawLine(pd, x, yy + 1.1f, x + T, yy + 1.1f);
              }
            break;
          }
        case 4: // 菜の花: ふわふわの明るいかたまり
          using (SolidBrush b = new SolidBrush(C(0xF4E66A, 120)))
            for (int i = 0; i < 4; i++)
              g.FillEllipse(b, x + (h >> (i * 4)) % 9, y + (h >> (i * 4 + 2)) % 9, 3, 2.4f);
          break;
        case 5: // 野菜: 株の列
          for (float xx = x + 1; xx < x + T; xx += 3)
            for (float yy = y + 1; yy < y + T; yy += 3)
              Ball(g, xx + 1, yy + 1, 1.1f, 0x5A8A40, 0.4f);
          break;
        default: // 水田: 空が映り、苗が並ぶ
          using (SolidBrush b = new SolidBrush(C(0xC8E8F0, 70)))
            g.FillEllipse(b, x + (h % 5), y + ((h >> 4) % 5), 6, 3);
          using (SolidBrush b = new SolidBrush(C(0x4E8A3A, 220)))
            for (float xx = x + 1; xx < x + T; xx += 2)
              for (float yy = y + 1; yy < y + T; yy += 2)
                g.FillEllipse(b, xx, yy, 0.6f, 0.6f);
          break;
      }
    }

    int rowIdAt(float y)
    {
      int r = (int)Math.Floor((y - rscroll) / T) + 1 + FAR;
      return r >= 0 && r < ROWS ? rowId[r] : 0;
    }

    void hedgeLine(Graphics g, float x0, float y0, float x1, float y1, bool wall, int pass, uint seed2)
    {
      float len = (float)Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
      int n = (int)(len / 0.8f) + 1;
      for (int i = 0; i <= n; i++)
      {
        float t = (float)i / n, x = x0 + (x1 - x0) * t, y = y0 + (y1 - y0) * t;
        uint h = ph((int)seed2 * 5 + i, 77);
        float j = ((h & 7) - 3.5f) * 0.1f, r = wall ? 0.75f : 1.25f + (h >> 3 & 3) * 0.12f;
        if (pass == 0)
          using (SolidBrush b = new SolidBrush(Color.FromArgb(28, 20, 30, 12)))
            g.FillEllipse(b, x - r + 1.0f, y - r + 1.2f + j, r * 2.2f, r * 2.2f);
        else if (wall)
          using (SolidBrush b = new SolidBrush(C((h & 1) == 0 ? 0xA8A498u : 0x8C887Cu)))
            g.FillEllipse(b, x - r, y - r * 0.7f, r * 2, r * 1.4f);
        else
          Ball(g, x + j, y + j, r, 0x34582A, 0.45f);
      }
    }

    void drawRiverB(Graphics g)
    {
      // 水のある行が続くところごとに、左右の岸の点を集めて閉じた曲線にする
      List<PointF> left = new List<PointF>(), right = new List<PointF>();
      for (int r = ROWS - 1; r >= -1; r--)
      {
        int l = -1, rr = -1;
        if (r >= 0)
          for (int c = 0; c < COLS; c++)
          {
            int t = map[r][c].type;
            if (t == WATER || t == BRIDGE || t == RAILBRIDGE)
            {
              if (l < 0) l = c;
              rr = c;
            }
          }
        bool water = r >= 0 && l >= 0 && (map[r][l].type == WATER || (rr > l));
        if (water)
        {
          float y = rowTop(r) + T / 2f;
          left.Add(new PointF(l * T + 0.6f, y));
          right.Add(new PointF((rr + 1) * T - 0.6f, y));
        }
        if ((!water || r == -1) && left.Count >= 2)
        {
          List<PointF> pts = new List<PointF>(left);
          right.Reverse();
          pts.AddRange(right);
          using (GraphicsPath gp = new GraphicsPath())
          {
            gp.AddClosedCurve(pts.ToArray(), 0.5f);
            using (Pen bank = new Pen(C(0xC8B888), 3.2f))
              g.DrawPath(bank, gp);
            using (Pen reeds = new Pen(C(0x5A7A36), 1.2f))
              g.DrawPath(reeds, gp);
            using (SolidBrush b = new SolidBrush(C(0x3E6E9C)))
              g.FillPath(b, gp);
            // まんなかは明るく (空の映り込み)
            GraphicsState st = g.Save();
            g.SetClip(gp);
            using (Pen p = new Pen(C(0x6A9ACA, 130), 3.5f))
            {
              List<PointF> mid = new List<PointF>();
              for (int i = 0; i < left.Count; i++)
                mid.Add(new PointF((left[i].X + right[right.Count - 1 - i].X) / 2 - 1, left[i].Y));
              if (mid.Count >= 2)
                g.DrawCurve(p, mid.ToArray(), 0.5f);
            }
            using (Pen p = new Pen(C(0xD8F0FC, 200), 0.45f)) // さざ波
              for (int i = 0; i < left.Count; i++)
              {
                float x = left[i].X + 2 + (ph(i, frame / 6) % 5), y = left[i].Y - 2 + (ph(i, 3) % 4);
                g.DrawLine(p, x, y, x + 2.5f, y);
              }
            g.Restore(st);
          }
        }
        if (!water)
        {
          left.Clear();
          right.Clear();
        }
      }
      // 石の橋
      for (int r = 0; r < ROWS; r++)
      {
        float y = rowTop(r);
        for (int c = 0; c < COLS; c++)
          if (map[r][c].type == BRIDGE && (c == 0 || map[r][c - 1].type != BRIDGE))
          {
            int e = c;
            while (e + 1 < COLS && map[r][e + 1].type == BRIDGE)
              e++;
            float x0 = c * T - 1, x1 = (e + 1) * T + 1;
            SoftShadowRect(g, x0 + 1, y + 1.5f, x1 - x0, T, 90);
            using (GraphicsPath gp = RoundRect(x0, y, x1 - x0, T, 1.2f))
              FillGrad(g, gp, new RectangleF(x0, y, x1 - x0, T), 0xC4BCAA, 0x9A9282, 90);
            using (Pen p = new Pen(C(0x6A665A), 0.5f))
            {
              g.DrawLine(p, x0, y + 1, x1, y + 1);
              g.DrawLine(p, x0, y + T - 1, x1, y + T - 1);
            }
          }
      }
    }

    void drawLanesB(Graphics g)
    {
      // 縦の小道: 行ごとの真ん中をつなぐ
      List<PointF> pts = new List<PointF>();
      for (int r = ROWS - 1; r >= -1; r--)
      {
        int l = -1, rr = -1;
        if (r >= 0)
          for (int c = 0; c < COLS; c++)
            if (map[r][c].type == LANE_V)
            {
              if (l < 0) l = c;
              rr = c;
            }
        if (l >= 0)
          pts.Add(new PointF((l + rr + 1) * T / 2f, rowTop(r) + T / 2f));
        else
        {
          if (pts.Count >= 2)
          {
            PointF[] a = pts.ToArray();
            using (Pen verge = new Pen(C(0x6E9846, 160), 8f))
              g.DrawCurve(verge, a, 0.5f);
            using (Pen sand = new Pen(C(0xC2AE84), 6.4f))
              g.DrawCurve(sand, a, 0.5f);
            using (Pen rut = new Pen(C(0xA08E66), 0.7f))
            {
              g.TranslateTransform(-1.7f, 0);
              g.DrawCurve(rut, a, 0.5f);
              g.TranslateTransform(3.4f, 0);
              g.DrawCurve(rut, a, 0.5f);
              g.TranslateTransform(-1.7f, 0);
            }
            using (Pen grass = new Pen(C(0x8AA05A, 170), 0.9f))
              g.DrawCurve(grass, a, 0.5f);
          }
          pts.Clear();
        }
      }
      // 横切る小道
      for (int r = 0; r < ROWS; r++)
      {
        float y = rowTop(r);
        int n = 0;
        for (int c = 0; c < COLS; c++)
          if (map[r][c].type == LANE_H)
            n++;
        if (n < COLS / 2)
          continue;
        using (SolidBrush b = new SolidBrush(C(0xC2AE84)))
          g.FillRectangle(b, 0, y + 1, W, T - 2);
        using (Pen rut = new Pen(C(0xA08E66), 0.7f))
        {
          g.DrawLine(rut, 0, y + 3, W, y + 3);
          g.DrawLine(rut, 0, y + T - 3, W, y + T - 3);
        }
      }
    }

    // ---- 置き物 ----
    void drawPropsB(Graphics g)
    {
      foreach (Prop p in props)
      {
        if (!p.on || p.ry < -40 || p.ry > H + 40)
          continue;
        float x = p.rx, y = p.ry;
        switch (p.type)
        {
          case TREE:
            {
              float r = p.v != 0 ? 5.2f : 4.2f;
              SoftShadow(g, x + 2.6f, y + 3, r, r * 0.85f, 110);
              Ball(g, x, y, r, 0x3A6A2E, 0.4f);
              uint h = ph((int)(p.ax * 7), (int)(p.ay * 3));
              for (int i = 0; i < 4; i++) // 葉のかたまり
              {
                float a = i * 1.6f + (h & 7), d = r * 0.5f;
                Ball(g, x + (float)Math.Cos(a) * d - 0.5f, y + (float)Math.Sin(a) * d - 0.5f, r * 0.45f, 0x4A7E36, 0.45f);
              }
              break;
            }
          case COTTAGE:
          case PUB:
          case BARN:
          case CHURCH:
            drawBuildingB(g, p, x, y);
            break;
          case BALE:
            SoftShadow(g, x + 1, y + 1.2f, 2.2f, 2, 90);
            Ball(g, x, y, 2.1f, 0xD8B860, 0.4f);
            using (Pen pen = new Pen(C(0xA88A42), 0.35f))
              g.DrawArc(pen, x - 1.2f, y - 1.2f, 2.4f, 2.4f, 0, 270);
            break;
          case ROCK:
            SoftShadow(g, x + 1, y + 1, 3, 2, 90);
            Ball(g, x, y, 2.6f, 0xA8A498, 0.45f);
            break;
          case SHEEP:
            drawSheepB(g, p, x, y);
            break;
          case COW:
            {
              float ly = y - p.lift * 20;
              SoftShadow(g, x + 0.8f, y + 1.2f, 3.2f, 1.6f, 90);
              using (GraphicsPath body = new GraphicsPath())
              {
                body.AddEllipse(x - 3.2f, ly - 1.8f, 6.4f, 3.6f);
                FillGrad(g, body, new RectangleF(x - 3.2f, ly - 1.8f, 6.4f, 3.6f), 0xFFFFFA, 0xD8D4C8, 90);
                GraphicsState st = g.Save();
                g.SetClip(body);
                using (SolidBrush b = new SolidBrush(C(0x2A2A2A)))
                {
                  g.FillEllipse(b, x - 2.2f, ly - 2, 2, 1.8f);
                  g.FillEllipse(b, x + 0.4f, ly + 0.2f, 2.2f, 1.6f);
                }
                g.Restore(st);
              }
              float hx = p.v != 0 ? x + 3.2f : x - 3.2f;
              using (SolidBrush b = new SolidBrush(C(0x2E2A28)))
                g.FillEllipse(b, hx - 1.1f, ly - 1, 2.2f, 2);
              break;
            }
          case DOG:
            SoftShadow(g, x + 0.6f, y + 1, 2, 1, 80);
            using (SolidBrush b = new SolidBrush(C(0x1E1E22)))
              g.FillEllipse(b, x - 2, y - 1, 4, 2);
            using (SolidBrush b = new SolidBrush(C(0xF4F2EA)))
              g.FillEllipse(b, (p.v != 0 ? x + 0.8f : x - 1.8f), y - 0.6f, 1, 1.2f);
            break;
          case DUCK:
            using (SolidBrush b = new SolidBrush(C(p.v == 0 ? 0x8A6A44u : 0xF0D860u)))
              g.FillEllipse(b, x - (p.v == 0 ? 1.8f : 1), y - (p.v == 0 ? 1 : 0.7f), p.v == 0 ? 3.6f : 2, p.v == 0 ? 2 : 1.4f);
            break;
        }
      }
      // 汽車
      if (train.on)
      {
        float y = train.ry, d = train.dir, fx_ = train.rx;
        int total = 37 + train.cars * 26;
        float len = total / K;
        float x0 = d > 0 ? fx_ - len : fx_;
        SoftShadowRect(g, x0 + 2, y - 0.5f, len, 6, 110);
        for (int c = train.cars - 1; c >= -1; c--)
        {
          float off = c < 0 ? 0 : (37 + c * 26) / K, l = c < 0 ? 24 / K : 24 / K;
          float cx = d > 0 ? fx_ - off - l : fx_ + off;
          using (GraphicsPath gp = RoundRect(cx, y - 3, l, 6, 1.2f))
            FillGrad(g, gp, new RectangleF(cx, y - 3, l, 6), c < 0 ? 0x4A8A5Au : 0x9A3A36u, c < 0 ? 0x1E4028u : 0x5A1E1Eu, 90);
          if (c >= 0)
            using (SolidBrush b = new SolidBrush(C(0xE8DCC0)))
              g.FillRectangle(b, cx + 0.5f, y - 3, l - 1, 0.7f);
        }
      }
      // 機関車の湯気と煙
      foreach (Puff q in puffs)
        if (q.on)
        {
          float f = (float)q.t / q.life, r = q.r * (f > 0.7f ? (1 - f) / 0.3f : 1);
          using (SolidBrush b = new SolidBrush(q.smoke ? Color.FromArgb((int)(170 * (1 - f)), 70, 70, 70) : Color.FromArgb((int)(210 * (1 - f)), 248, 248, 248)))
            g.FillEllipse(b, q.rx - r, q.ry - r, r * 2, r * 2);
        }
    }

    void drawSheepB(Graphics g, Prop p, float x, float y)
    {
      float ly = y - p.lift * 20 - (p.hop > 0 ? Math.Abs((float)Math.Sin(p.hop * 0.42f)) * 4 : 0);
      SoftShadow(g, x + 0.6f, y + 1.1f, 2.4f, 1.2f, p.lift > 0 ? 60 : 90);
      if (p.lift > 0) // じたばたする脚
        using (Pen pen = new Pen(C(0x2A2A2A), 0.4f))
          for (int i = -1; i <= 1; i += 2)
            g.DrawLine(pen, x + i * 1.2f, ly + 1.2f, x + i * 1.4f, ly + 2.6f + ((frame / 4 + i) % 2) * 0.5f);
      // もこもこの毛: 小さな丸を重ねる
      float[] bx = { -1.4f, 0, 1.3f, -0.7f, 0.7f, 0 };
      float[] by = { 0.2f, -0.6f, 0.2f, 0.8f, 0.8f, 0.1f };
      foreach (int pass in new[] { 0, 1 })
        for (int i = 0; i < bx.Length; i++)
          if (pass == 0)
            using (SolidBrush b = new SolidBrush(C(0xC8C4B6)))
              g.FillEllipse(b, x + bx[i] - 1.15f, ly + by[i] - 1.0f, 2.3f, 2.3f);
          else
            Ball(g, x + bx[i] - 0.15f, ly + by[i] - 0.2f, 0.95f, 0xF8F6EE, 0.5f);
      float hx = p.v != 0 ? x + 2.3f : x - 2.3f;
      using (SolidBrush b = new SolidBrush(C(0x2A2A2A)))
        g.FillEllipse(b, hx - 0.9f, ly - 0.6f, 1.8f, 1.5f);
      using (SolidBrush b = new SolidBrush(C(0x4A4040)))
      {
        g.FillEllipse(b, hx - 0.8f, ly - 1.2f, 0.8f, 0.6f);
        g.FillEllipse(b, hx - 0.8f, ly + 0.7f, 0.8f, 0.6f);
      }
    }

    void drawBuildingB(Graphics g, Prop p, float x, float y)
    {
      uint roofT, roofB;
      float w, h, rx0, ry0;
      switch (p.type)
      {
        case COTTAGE:
          bool slate = (p.v & 1) != 0;
          roofT = slate ? 0x8A90A0u : 0xDCB872u;
          roofB = slate ? 0x4A5060u : 0x9A7840u;
          w = 11; h = 8; rx0 = x - 5.5f; ry0 = y - 4;
          break;
        case PUB:
          roofT = 0x7A8090; roofB = 0x3E4450; w = 15; h = 8; rx0 = x - 7.5f; ry0 = y - 5.5f;
          break;
        case BARN:
          roofT = 0xC06A4E; roofB = 0x7A3A2A; w = 14; h = 9; rx0 = x - 7; ry0 = y - 5.5f;
          break;
        default: // 教会の身廊
          roofT = 0xA8ACB4; roofB = 0x5A5E66; w = 7; h = 14; rx0 = x - 3.5f; ry0 = y - 9.5f;
          break;
      }
      SoftShadowRect(g, rx0 + 2, ry0 + 2, w, h + 2, 120);
      // 壁 (手前に少し見える)
      if (p.type != CHURCH)
        using (GraphicsPath wall = RoundRect(rx0 + 0.6f, ry0 + h - 1, w - 1.2f, 3, 0.5f))
          FillGrad(g, wall, new RectangleF(rx0, ry0 + h - 1, w, 3), p.type == BARN ? 0x8A5A3Au : 0xF4ECD8u, p.type == BARN ? 0x6A3A22u : 0xD8CCB0u, 90);
      using (GraphicsPath roof = RoundRect(rx0, ry0, w, h, p.type == COTTAGE && (p.v & 1) == 0 ? 2.6f : 1.0f))
      {
        if (p.type == CHURCH)
        {
          using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(rx0 - 0.5f, ry0, w + 1, h), C(roofT), C(roofB), 0f))
            g.FillPath(b, roof);
        }
        else
          FillGrad(g, roof, new RectangleF(rx0, ry0, w, h), roofT, roofB, 90);
        GraphicsState st = g.Save();
        g.SetClip(roof);
        if (p.type == COTTAGE && (p.v & 1) == 0) // 茅の筋
          using (Pen pen = new Pen(C(0x8A6A36, 90), 0.3f))
            for (float xx = rx0; xx < rx0 + w; xx += 0.9f)
              g.DrawLine(pen, xx, ry0, xx + 0.3f, ry0 + h);
        else if (p.type == BARN) // トタンの波
          using (Pen pen = new Pen(C(0x5A2A1E, 110), 0.35f))
            for (float xx = rx0; xx < rx0 + w; xx += 1.1f)
              g.DrawLine(pen, xx, ry0, xx, ry0 + h);
        else // 瓦の段
          using (Pen pen = new Pen(C(0x2E323C, 80), 0.3f))
            for (float yy = ry0 + 1; yy < ry0 + h; yy += 1.4f)
              g.DrawLine(pen, rx0, yy, rx0 + w, yy);
        using (Pen ridge = new Pen(C(mix(roofB, 0x000000, 0.25f)), 0.6f))
          if (p.type == CHURCH)
            g.DrawLine(ridge, x, ry0, x, ry0 + h);
          else
            g.DrawLine(ridge, rx0, ry0 + h / 2, rx0 + w, ry0 + h / 2);
        g.Restore(st);
      }
      if (p.type == CHURCH) // 塔と尖塔
      {
        float tx = x - 4, ty = y + 3;
        SoftShadowRect(g, tx + 2, ty + 2, 8, 8, 120);
        using (GraphicsPath tw = RoundRect(tx, ty, 8, 8, 0.6f))
          FillGrad(g, tw, new RectangleF(tx, ty, 8, 8), 0xC8C4B6, 0x8A867A, 45);
        PointF apex = new PointF(x, ty + 4);
        using (SolidBrush b = new SolidBrush(C(0x9A968A)))
          g.FillPolygon(b, new[] { new PointF(tx + 1, ty + 1), new PointF(tx + 1, ty + 7), apex });
        using (SolidBrush b = new SolidBrush(C(0x5A564C)))
          g.FillPolygon(b, new[] { new PointF(tx + 7, ty + 1), new PointF(tx + 7, ty + 7), apex });
        using (SolidBrush b = new SolidBrush(C(0x86827A)))
          g.FillPolygon(b, new[] { new PointF(tx + 1, ty + 1), new PointF(tx + 7, ty + 1), apex });
        using (SolidBrush b = new SolidBrush(C(GOLD)))
          g.FillEllipse(b, x - 0.6f, ty + 3.4f, 1.2f, 1.2f);
      }
      else
      {
        // 煙突と煙
        float cx = p.type == PUB ? rx0 + 1 : rx0 + w * 0.65f;
        using (GraphicsPath ch = RoundRect(cx, ry0 - 1.2f, 2, 2.4f, 0.3f))
          FillGrad(g, ch, new RectangleF(cx, ry0 - 1.2f, 2, 2.4f), 0xB06048, 0x7A3A2A, 90);
        for (int i = 0; i < 4; i++)
        {
          float s = ((frame / 4 + i * 6) % 24) / 24f;
          using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(150 * (1 - s)), 240, 240, 236)))
            g.FillEllipse(b, cx + 1 + s * 3 - (0.6f + s * 1.6f), ry0 - 2 - s * 9 - (0.6f + s * 1.6f), (0.6f + s * 1.6f) * 2, (0.6f + s * 1.6f) * 2);
        }
        // 窓と戸
        using (SolidBrush b = new SolidBrush(C(0x3A4A5A)))
          for (float wx = rx0 + 1.6f; wx < rx0 + w - 1.5f; wx += w / 3.2f)
            g.FillRectangle(b, wx, ry0 + h - 0.2f, 1.2f, 1.1f);
        if (p.type == PUB) // 羊の看板
        {
          float sx = p.v != 0 ? rx0 + w + 1 : rx0 - 4;
          using (Pen pen = new Pen(C(0x2A2420), 0.35f))
            g.DrawLine(pen, sx + 1.5f, ry0 + h, p.v != 0 ? rx0 + w : rx0, ry0 + h);
          using (GraphicsPath sign = RoundRect(sx, ry0 + h + 0.5f, 3, 3.6f, 0.4f))
          {
            using (SolidBrush b = new SolidBrush(C(0x2A4A2A)))
              g.FillPath(b, sign);
            using (Pen pen = new Pen(C(0xD8B048), 0.35f))
              g.DrawPath(pen, sign);
          }
          using (SolidBrush b = new SolidBrush(C(0xF4F2EA)))
            g.FillEllipse(b, sx + 0.6f, ry0 + h + 1.8f, 1.6f, 1.1f);
        }
      }
      if (p.type == COTTAGE && (p.v & LAUNDRY) != 0) // 洗濯物
      {
        float lx0 = x + 6.5f, ly0 = y - 2, lx1 = x + 16.5f, ly1 = y - 0.8f;
        using (Pen pen = new Pen(C(0xF0F0E8), 0.25f))
          g.DrawCurve(pen, new[] { new PointF(lx0, ly0), new PointF((lx0 + lx1) / 2, (ly0 + ly1) / 2 + 0.6f), new PointF(lx1, ly1) });
        for (int i = 0; i < 4; i++)
        {
          float t = (i + 1) / 5f, cx = lx0 + (lx1 - lx0) * t, cy = ly0 + (ly1 - ly0) * t + 0.5f;
          float sway = (float)Math.Sin(frame * 0.15f + i * 1.3f) * 0.5f;
          using (SolidBrush b = new SolidBrush(C(CLOTHES[i % CLOTHES.Length])))
            g.FillPolygon(b, new[] { new PointF(cx - 0.8f, cy), new PointF(cx + 0.8f, cy), new PointF(cx + 0.9f + sway, cy + 2.2f), new PointF(cx - 0.7f + sway, cy + 2.2f) });
        }
      }
    }

    // ---- 空のもの: 影、雲、円盤、光線、自機、弾 ----
    void drawSkyB(Graphics g)
    {
      // 雲の影 (地面にうっすら)
      foreach (Cloud c in clouds)
        if (c.r > 0)
          cloudB(g, c.rx + 24, c.ry + 36, c.r, true);
      // 飛ぶものの影
      foreach (Ufo u in ufos)
        if (u.on && u.t >= 0)
          SoftShadow(g, u.rx + 10, u.ry + 14, UFO_R[u.type] * 0.95f, UFO_R[u.type] * 0.7f, 120);
      bool planeOn = deadT == 0 && !gone;
      if (planeOn)
        planeB(g, rpx + 12, rpy + 16, true);
      // 雲
      foreach (Cloud c in clouds)
        if (c.r > 0)
          cloudB(g, c.rx, c.ry, c.r, false);
      // 光線と円盤
      foreach (Ufo u in ufos)
        if (u.on && u.t >= 0 && u.type == ABDUCTOR && u.state == 1 && u.prop >= 0)
        {
          Prop p = props[u.prop];
          float ty = p.ry - p.lift * 20 + 2, top = u.ry + UFO_R[u.type] - 2;
          for (int layer = 0; layer < 3; layer++)
          {
            float wTop = 2.5f + layer * 1.2f, wBot = 6 + layer * 2.5f;
            PointF[] beam = { new PointF(u.rx - wTop, top), new PointF(u.rx + wTop, top), new PointF(p.rx + wBot, ty), new PointF(p.rx - wBot, ty) };
            using (LinearGradientBrush b = new LinearGradientBrush(new PointF(0, top - 1), new PointF(0, ty + 1), C(0xF0FFC0, 90 - layer * 25), C(0xE8F8A0, 30 - layer * 8)))
              g.FillPolygon(b, beam);
          }
          using (SolidBrush b = new SolidBrush(C(0xF8FFD8, 70))) // 足もとの光の輪
            g.FillEllipse(b, p.rx - 7, ty - 2.5f, 14, 5);
        }
      foreach (Ufo u in ufos)
        if (u.on && u.t >= 0)
          saucerB(g, u.rx, u.ry, UFO_R[u.type] * 1.05f, u.flash > 0);
      // 自機の弾
      foreach (Shot s in mine)
        if (s.on)
          using (LinearGradientBrush b = new LinearGradientBrush(new PointF(s.rx, s.ry - 0.5f), new PointF(s.rx, s.ry + 4.5f), C(0xFFFFE8), C(0xF0C860, 0)))
            g.FillEllipse(b, s.rx - 0.5f, s.ry - 0.5f, 1, 5);
      if (planeOn)
        planeB(g, rpx, rpy, false);
      // 円盤の弾: ぼんやり光る桃色の玉
      foreach (Shot s in theirs)
        if (s.on)
        {
          using (SolidBrush b = new SolidBrush(C(PLASMA, 60)))
            g.FillEllipse(b, s.rx - 3.2f, s.ry - 3.2f, 6.4f, 6.4f);
          Ball(g, s.rx, s.ry, 1.9f, 0xFF5AC8, 0.75f);
        }
    }

    void cloudB(Graphics g, float x, float y, int r, bool shadow)
    {
      float R = r;
      float[,] blobs = { { 0, 0, 1 }, { -1, 1, 0.66f }, { 1, 2, 0.66f }, { -0.45f, -0.5f, 0.6f }, { 0.5f, -0.35f, 0.55f }, { 0.1f, 0.6f, 0.8f } };
      for (int i = 0; i < blobs.GetLength(0); i++)
      {
        float bx = x + blobs[i, 0] * R, by = y + blobs[i, 1] * (i >= 3 ? R : 1), br = blobs[i, 2] * R;
        if (shadow)
        {
          using (SolidBrush b = new SolidBrush(Color.FromArgb(22, 20, 30, 12)))
            g.FillEllipse(b, bx - br, by - br, br * 2, br * 2);
          continue;
        }
        using (GraphicsPath gp = new GraphicsPath())
        {
          gp.AddEllipse(bx - br, by - br, br * 2, br * 2);
          using (PathGradientBrush b = new PathGradientBrush(gp))
          {
            b.CenterPoint = new PointF(bx - br * 0.3f, by - br * 0.45f);
            b.CenterColor = Color.FromArgb(250, 255, 255, 255);
            b.SurroundColors = new[] { Color.FromArgb(235, 214, 224, 236) };
            g.FillPath(b, gp);
          }
        }
      }
    }

    void saucerB(Graphics g, float x, float y, float R, bool flash)
    {
      // 皿: 銀の放射グラデーション、縁は暗い金物
      using (GraphicsPath gp = new GraphicsPath())
      {
        gp.AddEllipse(x - R, y - R, R * 2, R * 2);
        using (PathGradientBrush b = new PathGradientBrush(gp))
        {
          b.CenterPoint = new PointF(x - R * 0.4f, y - R * 0.45f);
          b.CenterColor = flash ? Color.White : C(0xF4F6FA);
          b.SurroundColors = new[] { C(0x6A6E7A) };
          g.FillPath(b, gp);
        }
      }
      using (Pen p = new Pen(C(0x2E323C), 0.5f))
        g.DrawEllipse(p, x - R, y - R, R * 2, R * 2);
      using (Pen p = new Pen(C(0x4A505C, 160), 0.35f))
        g.DrawEllipse(p, x - R * 0.68f, y - R * 0.68f, R * 1.36f, R * 1.36f);
      // ドーム: ガラスの水色、左上に照り
      float d = R * 0.38f;
      using (GraphicsPath gp = new GraphicsPath())
      {
        gp.AddEllipse(x - d, y - d, d * 2, d * 2);
        using (PathGradientBrush b = new PathGradientBrush(gp))
        {
          b.CenterPoint = new PointF(x - d * 0.3f, y - d * 0.3f);
          b.CenterColor = C(0xB8F4F8);
          b.SurroundColors = new[] { C(0x2A8A9A) };
          g.FillPath(b, gp);
        }
      }
      using (SolidBrush b = new SolidBrush(Color.FromArgb(220, 255, 255, 255)))
        g.FillEllipse(b, x - d * 0.6f, y - d * 0.7f, d * 0.6f, d * 0.4f);
      // 縁の灯り: 色のついた光がにじむ
      for (int i = 0; i < 6; i++)
      {
        float a = frame * 0.15f + i * (PI / 3), lx = x + (float)Math.Cos(a) * R * 0.86f, ly = y + (float)Math.Sin(a) * R * 0.86f;
        uint rc = RIM[(i + frame / 8) % 4];
        using (SolidBrush b = new SolidBrush(C(rc, 80)))
          g.FillEllipse(b, lx - 1.3f, ly - 1.3f, 2.6f, 2.6f);
        using (SolidBrush b = new SolidBrush(C(mix(rc, 0xFFFFFF, 0.5f))))
          g.FillEllipse(b, lx - 0.5f, ly - 0.5f, 1, 1);
      }
    }

    // 自機: 楕円の主翼に迷彩、白に赤の点の印、風防。プロペラは回って透ける円
    void planeB(Graphics g, float x, float y, bool shadow)
    {
      using (GraphicsPath wing = new GraphicsPath())
      using (GraphicsPath tail = new GraphicsPath())
      using (GraphicsPath body = new GraphicsPath())
      {
        wing.AddEllipse(x - 9.5f, y - 1.6f, 19, 5.4f);
        tail.AddEllipse(x - 4.2f, y + 5, 8.4f, 2.4f);
        body.AddEllipse(x - 1.9f, y - 7.5f, 3.8f, 15.5f);
        if (shadow)
        {
          using (SolidBrush b = new SolidBrush(Color.FromArgb(70, 20, 30, 12)))
          {
            g.FillPath(b, wing);
            g.FillPath(b, tail);
            g.FillPath(b, body);
          }
          return;
        }
        FillGrad(g, wing, new RectangleF(x - 9.5f, y - 1.6f, 19, 5.4f), 0x8E9458, 0x50562E, 90);
        GraphicsState st = g.Save();
        g.SetClip(wing);
        using (SolidBrush b = new SolidBrush(C(0x6A5434, 200))) // 迷彩
        {
          g.FillEllipse(b, x - 7.5f, y - 0.5f, 4, 2);
          g.FillEllipse(b, x + 3.5f, y + 0.6f, 4.5f, 1.8f);
          g.FillEllipse(b, x - 2.5f, y + 1.8f, 3, 1.4f);
        }
        g.Restore(st);
        using (Pen p = new Pen(C(0x2E3420, 180), 0.35f))
          g.DrawPath(p, wing);
        FillGrad(g, tail, new RectangleF(x - 4.2f, y + 5, 8.4f, 2.4f), 0x7E844C, 0x50562E, 90);
        FillGrad(g, body, new RectangleF(x - 1.9f, y - 7.5f, 3.8f, 15.5f), 0xB4B482, 0x6A6A40, 0);
        for (int sd = -1; sd <= 1; sd += 2) // 印
        {
          using (SolidBrush b = new SolidBrush(C(0xF4F4F0)))
            g.FillEllipse(b, x + sd * 6 - 1.3f, y + 1.1f - 1.3f, 2.6f, 2.6f);
          using (SolidBrush b = new SolidBrush(C(POPPY)))
            g.FillEllipse(b, x + sd * 6 - 0.65f, y + 1.1f - 0.65f, 1.3f, 1.3f);
        }
        using (GraphicsPath canopy = new GraphicsPath()) // 風防
        {
          canopy.AddEllipse(x - 0.9f, y - 3, 1.8f, 3.4f);
          FillGrad(g, canopy, new RectangleF(x - 0.9f, y - 3, 1.8f, 3.4f), 0xA8D0E8, 0x2A3A4A, 90);
        }
        using (SolidBrush b = new SolidBrush(Color.FromArgb(60, 230, 230, 225))) // 回るプロペラ
          g.FillEllipse(b, x - 4.5f, y - 8.6f, 9, 1.6f);
        using (Pen p = new Pen(Color.FromArgb(150, 210, 210, 205), 0.4f))
          g.DrawLine(p, x - 4, y - 7.6f + (frame & 1) * 0.4f, x + 4, y - 8.2f);
        using (SolidBrush b = new SolidBrush(C(0x3A3A3A)))
          g.FillEllipse(b, x - 0.7f, y - 8.6f, 1.4f, 1.4f);
      }
    }

    // 画面のふちを少し暗くして、絵本の 1 ページのように
    static void drawVignette(Graphics g, int w, int h)
    {
      using (GraphicsPath gp = new GraphicsPath())
      {
        gp.AddEllipse(-w * 0.35f, -h * 0.2f, w * 1.7f, h * 1.4f);
        using (PathGradientBrush b = new PathGradientBrush(gp))
        {
          b.CenterColor = Color.FromArgb(0, 0, 0, 0);
          b.SurroundColors = new[] { Color.FromArgb(110, 20, 24, 10) };
          b.FocusScales = new PointF(0.72f, 0.8f);
          g.FillRectangle(b, 0, 0, w, h);
        }
      }
    }

    // 文字: 絵本らしいセリフ体を、濃い緑で縁どる
    static void outlined(Graphics g, string s, float size, float x, float y, Color fill, StringAlignment align)
    {
      using (GraphicsPath gp = new GraphicsPath())
      using (FontFamily ff = new FontFamily("Georgia"))
      using (StringFormat sf = new StringFormat())
      {
        sf.Alignment = align;
        gp.AddString(s, ff, (int)FontStyle.Bold, size, new PointF(x, y), sf);
        using (Pen p = new Pen(C(INKG), size * 0.18f))
        {
          p.LineJoin = LineJoin.Round;
          g.DrawPath(p, gp);
        }
        using (SolidBrush b = new SolidBrush(fill))
          g.FillPath(b, gp);
      }
    }

    void drawHudB(Graphics g, int w, int h)
    {
      g.TextRenderingHint = TextRenderingHint.AntiAlias;
      outlined(g, "1UP", 18, 16, 8, C(GOLD), StringAlignment.Near);
      outlined(g, score.ToString("D6"), 34, 14, 26, C(CREAM), StringAlignment.Near);
      outlined(g, "HI-SCORE", 18, w / 2 + 40, 8, C(GOLD), StringAlignment.Center);
      outlined(g, Math.Max(score, Table[0].Score).ToString("D6"), 34, w / 2 + 40, 26, C(CREAM), StringAlignment.Center);
      for (int i = 0; i < Math.Min(lives, 5); i++) // 残りの飛行機
      {
        float x = 28 + i * 30, y = h - 26;
        using (SolidBrush b = new SolidBrush(C(CREAM)))
        {
          g.FillEllipse(b, x - 11, y - 3, 22, 7);
          g.FillEllipse(b, x - 3, y - 12, 6, 24);
        }
      }
      foreach (Pop q in pops)
        if (q.on)
          outlined(g, q.text, 24, q.rx * h / (float)H, q.ry * h / (float)H, C(q.col), StringAlignment.Center);
    }
  }
}
