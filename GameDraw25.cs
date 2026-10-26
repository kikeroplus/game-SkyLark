// SKYLARK の 2.5D 表示 (F2 で切り替え)。飛び出す絵本のような、紙の張りぼての世界。
// ・地面: A と同じ真上から見た地面の絵を 1 枚に描き、奥へ傾けて映す (遠くは小さく、近くは大きく)
// ・画面の上には空と、紙を重ねた遠い丘とお日さま
// ・羊や家や木、円盤や自機は、白いふちと厚紙の切り口のある張りぼて。奥から流れてくると、パタンと起き上がる
// ゲームの中身 (当たり判定など) は真上から見た世界のまま。描くときだけ、世界の点を画面に映す
using System;
using System.Collections.Generic;

namespace SkyLark
{
  public partial class Game
  {
    public bool View25;

    const int HZ = 70;                  // 地面のいちばん奥が来る画面の高さ。これより上は空
    const float YB = 368, YT = -FAR * T + 4; // 画面の下の端と、地面の奥の端の世界の y (画面の上の端より遠くまで見える)
    const int GL_TOP = (int)(-YT * K) + 4;  // 地面の台の、画面より上にはみ出す高さ
    // 遠近: カメラで撮ったように、倍率は奥行きに反比例 (まっすぐな線はまっすぐのまま奥へすぼまる)。
    // 手前 (画面の下) で 1.62 倍、地面の奥の端で 0.85 倍になるように決めた
    const float SC0 = 1.62f, SC1 = 0.85f;
    const float PD = SC1 * (YB - YT) / (SC0 - SC1);      // カメラから手前の端までの奥行き
    const float PF = SC0 * PD;                           // 倍率 = PF / 奥行き
    const float PH = (SH - HZ) / (SC0 - SC1);            // 画面の高さ = PSY0 + PH * 倍率
    const float PSY0 = SH - PH * SC0;
    const float AIR = 14;               // 飛ぶもの (自機、円盤、弾) の高さ (世界の点)
    const float PAPER_K = 1.25f;        // 張りぼては少し大きめに
    const int KEY = 0xFF00FF;           // 台紙の透明の色
    const uint PAPER_C = 0xF6F0DE, EDGE_C = 0x5E584C, FOG_C = 0xC4D8D4;

    Gfx gl;                             // 真上から見た地面 (A の絵)
    Gfx cv;                             // 張りぼてを 1 枚ずつ描く台紙
    readonly int[] rowGy = new int[SH], rowGyF = new int[SH];
    readonly float[] rowA = new float[SH], rowB = new float[SH];
    readonly int[] rowFog = new int[SH];
    bool rowsReady;

    static float uOf(float wy) { return (YB - wy) / (YB - YT); }
    static float scAt(float wy) { return PF / (PD + (YB - wy)); }
    static float syAt(float sc) { return PSY0 + PH * sc; }

    // 世界の点 (wx, wy) の高さ alt を、画面の点へ。sc はその場所の倍率 (画面の点 / 世界の点)
    void proj(float wx, float wy, float alt, out float sx, out float sy, out float sc)
    {
      sc = scAt(wy);
      sx = SW / 2f + (wx - W / 2f) * sc;
      sy = syAt(sc) - alt * sc;
    }

    // 画面の行ごとに、地面のどこを映すかを先に決めておく
    void prepRows()
    {
      for (int sy = 0; sy < SH; sy++)
      {
        rowGy[sy] = -1;
        float sc = (sy - PSY0) / PH;
        if (sc <= 0.01f)
          continue;
        float wy = YB - (PF / sc - PD), u = uOf(wy);
        if (wy < YT - 0.5f)
          continue;
        float gyf = constrain(wy * K + GL_TOP - 0.5f, 0, SH + GL_TOP - 2);
        rowGy[sy] = (int)gyf;
        rowGyF[sy] = (int)((gyf - (int)gyf) * 256);
        rowA[sy] = K / sc;
        rowB[sy] = K * W / 2f - SW / 2f * K / sc;
        rowFog[sy] = (int)(constrain((u - 0.62f) / 0.38f, 0, 1) * 110);
      }
      rowsReady = true;
    }

    static int mirrorX(int x)
    {
      if (x < 0)
        x = -x - 1;
      else if (x >= SW)
        x = 2 * SW - 1 - x;
      return x < 0 ? 0 : x >= SW ? SW - 1 : x;
    }

    // 4 つの点を、横 fx・縦 fy (0〜255) の割合で混ぜる
    static int bilerp(int c00, int c10, int c01, int c11, int fx, int fy)
    {
      int ifx = 256 - fx, ify = 256 - fy;
      int r = ((((c00 >> 16) & 255) * ifx + ((c10 >> 16) & 255) * fx) * ify + (((c01 >> 16) & 255) * ifx + ((c11 >> 16) & 255) * fx) * fy) >> 16;
      int g = ((((c00 >> 8) & 255) * ifx + ((c10 >> 8) & 255) * fx) * ify + (((c01 >> 8) & 255) * ifx + ((c11 >> 8) & 255) * fx) * fy) >> 16;
      int b = (((c00 & 255) * ifx + (c10 & 255) * fx) * ify + ((c01 & 255) * ifx + (c11 & 255) * fx) * fy) >> 16;
      return (r << 16) | (g << 8) | b;
    }

    static int blendI(int c, int t, int a)
    {
      int na = 256 - a;
      return ((((c >> 16) & 255) * na + ((t >> 16) & 255) * a) >> 8 << 16) | ((((c >> 8) & 255) * na + ((t >> 8) & 255) * a) >> 8 << 8) | (((c & 255) * na + (t & 255) * a) >> 8);
    }

    // ---- 1 枚描く ----
    void Draw25(Gfx g, float alpha)
    {
      lerpAll(alpha);
      if (!rowsReady)
        prepRows();
      if (gl == null)
      {
        gl = new Gfx(SW, SH + GL_TOP);
        gl.OffY = GL_TOP;
        gl.UseMask();
        cv = new Gfx(240, 200);
        cv.Key = KEY;
      }
      g.UseMask();
      drawGroundLayer25(gl);
      drawSky25(g);
      // 地面を奥へ傾けて映す。となりあう 4 つの点を混ぜて、ギザギザを目立たなくする。
      // 左右にはみ出たところは鏡に映して、少しかすませる。遠くはもやに溶ける
      int[] src = gl.Buf, dst = g.Buf;
      for (int sy = 0; sy < SH; sy++)
      {
        int gy = rowGy[sy];
        if (gy < 0)
          continue;
        int off0 = gy * SW, off1 = off0 + SW, fy = rowGyF[sy], fog = rowFog[sy], o = sy * SW;
        float a = rowA[sy], b = rowB[sy] - 0.5f;
        for (int sx = 0; sx < SW; sx++)
        {
          int fx8 = (int)Math.Floor((b + sx * a) * 256), gx = fx8 >> 8, fxx = fx8 & 255;
          bool mirror = gx < 0 || gx >= SW - 1;
          int gx0 = mirrorX(gx), gx1 = mirrorX(gx + 1);
          int c = bilerp(src[off0 + gx0], src[off0 + gx1], src[off1 + gx0], src[off1 + gx1], fxx, fy);
          if (mirror)
            c = blendI(c, (int)FOG_C, 45);
          if (fog > 0)
            c = blendI(c, (int)FOG_C, fog);
          dst[o + sx] = c;
        }
      }
      for (int sy = HZ - 2; sy < HZ + 10; sy++) // 地平線のもや
        for (int sx = 0; sx < SW; sx++)
          g.BlendPixel(sx, sy, FOG_C, 200 - (sy - HZ + 2) * 16);
      // 張りぼて: 地面に立つものを奥から手前へ。そのあと飛ぶものを奥から手前へ
      List<KeyValuePair<float, Action>> items = new List<KeyValuePair<float, Action>>();
      for (int i = 0; i < props.Length; i++)
      {
        Prop p = props[i];
        if (!p.on || p.type == DEBRIS || p.type == WRECK || p.type == CROPCIRCLE || p.ry < YT - 4 || p.ry > H + 50)
          continue;
        int idx = i;
        items.Add(new KeyValuePair<float, Action>(p.ry, () => paperProp(g, p, idx)));
      }
      if (train.on)
        items.Add(new KeyValuePair<float, Action>(train.ry, () => paperTrain(g)));
      items.Sort((ia, ib) => ia.Key.CompareTo(ib.Key));
      foreach (var it in items)
        it.Value();
      drawAir25(g);
      fx.Draw25(g, CROPS[0].b);
      tint(g);
      drawLights25(g);
      drawRainbow(g, SW / 2, HZ + 260, 300);
      float rk = rainK();
      if (rk > 0) // 雨粒 (画面に直接)
        for (int i = 0, n = (int)(drops.Length * rk); i < n; i++)
        {
          int x = S(drops[i].rx), y = S(drops[i].ry);
          g.DrawLine(x, y, x - 2, y + 8, 0x98A8B8);
        }
      if (Paused || mode == Mode.Continue)
        dim(g);
      drawOverlay(g);
      foreach (Pop q in pops)
        if (q.on && (q.t / 4) % 3 != 0)
        {
          float sx, sy, sc;
          proj(q.rx, q.ry, AIR + 6, out sx, out sy, out sc);
          txt(g, (int)sx, (int)sy, q.col, q.text, Datum.TC, q.text == "SAVED" ? 2 : 1);
        }
      drawModeCard(g);
    }

    // 真上から見た地面: A の地面、ミステリーサークル、ストーンヘンジの土手、残骸、そして影 (置き物、雲、飛ぶもの)
    void drawGroundLayer25(Gfx g)
    {
      visTop = -GL_TOP;
      drawGroundRows(g);
      foreach (Prop p in props)
      {
        if (!p.on)
          continue;
        int x = S(p.rx), y = S(p.ry);
        if (p.type == CROPCIRCLE)
        {
          drawCropCircle(g, p, x, y);
          float gx, gy;
          if (cropSpot(p, out gx, out gy)) // 円盤が麦を倒している点
            for (int j = -4; j <= 4; j++)
              for (int i = -4; i <= 4; i++)
                if (i * i + j * j <= 16)
                  g.BlendPixel(S(gx) + i, S(gy) + j, 0xF8FFD0, 150 - (i * i + j * j) * 8);
        }
        else if (p.type == STONEHENGE)
          if (p.kind == 1)
            for (int j = -50; j <= 50; j++) // キャッスルリッグ: 踏まれて薄くなった芝の輪
              for (int i = -50; i <= 50; i++)
              {
                float q = sqrtf(i * i + j * j);
                if (q > 40 && q < 50)
                  g.BlendPixel(x + i, y + j, 0xD8E8B0, 60);
              }
          else
            hengeGround25(g, x, y);
        else if (p.type == DEBRIS || p.type == WRECK)
          drawProp(g, p, 0);
      }
      foreach (Ufo u in ufos) // 湖の下から浮かんでくる円盤の黒い影
        if (u.on && u.t >= 0 && u.rise < 0.45f && u.riseKind == 1)
          drawRising(g, u, S(u.rx), S(u.ry), UFO_R[u.type] * K);
      g.MaskMode = true;
      foreach (Prop p in props)
        if (p.on && visible(S(p.ry) - 60, S(p.ry) + 60) && p.type != STONEHENGE)
          propShadow(g, p);
      foreach (Prop p in props) // ストーンヘンジ (キャッスルリッグ) の石の影 (斜めに伸ばす)
        if (p.on && p.type == STONEHENGE)
          for (int k = 0, n = p.kind == 1 ? 36 : 16; k < n; k++)
          {
            float a = k * (2 * PI / n) + 0.1f, rr = p.kind == 1 ? 34 : 38;
            int bx = S(p.rx) + (int)(cosf(a) * rr), by = S(p.ry) + (int)(sinf(a) * rr);
            g.FillTriangle(bx - 3, by, bx + 3, by, bx + 14, by + 8, 0);
            g.FillTriangle(bx - 3, by, bx + 14, by + 8, bx + 8, by + 8, 0);
          }
      if (train.on)
      {
        int total = 37 + train.cars * 26, tx = S(train.rx), ty = S(train.ry);
        g.FillRect(train.dir > 0 ? tx - total + 4 : tx + 4, ty + 1, total, 7, 0);
      }
      g.MaskMode = false;
      g.ApplyShadow(SHADE, 100);
      g.MaskMode = true;
      foreach (Cloud c in clouds)
        if (c.r > 0)
          cloudShape(g, S(c.rx) + 18, S(c.ry) + 26, c.r * K, 0, 0, 0, 0);
      g.MaskMode = false;
      g.ApplyShadow(SHADE, 55);
      g.MaskMode = true;
      foreach (Ufo u in ufos)
        if (u.on && u.t >= 0 && !(u.riseKind == 1 && u.rise < 0.45f))
        {
          float rs = u.rise;
          int ur = (int)(UFO_R[u.type] * K * (0.4f + 0.6f * rs));
          g.FillEllipse(S(u.rx) + (int)(8 * rs), S(u.ry) + (int)(12 * rs), ur, ur * 2 / 3, 0);
        }
      if (mom.on)
      {
        float a = momAlt(), s = 0.65f + 0.35f * a;
        g.FillEllipse(S(mom.rx + 8 * a), S(mom.ry + 12 * a), (int)(51 * s), (int)(36 * s), 0);
      }
      foreach (DebrisP d in debris)
        if (d.on)
          g.FillCircle(S(d.rx + 6 * d.alt), S(d.ry + 9 * d.alt), 3, 0);
      bool planeOn = deadT == 0 && !gone && mode != Mode.Continue;
      if (planeOn)
        g.Blit(PLANE, S(rpx) + 10, S(rpy) + 15, false);
      g.MaskMode = false;
      g.ApplyShadow(SHADE, 105);
      visTop = 0;
    }

    // ストーンヘンジの土手と堀、アヴェニュー (真上から。傾けると楕円に見える)
    void hengeGround25(Gfx g, int x, int y)
    {
      for (int j = -60; j <= 60; j++)
        for (int i = -60; i <= 60; i++)
        {
          float d = sqrtf(i * i + j * j);
          if (d > 60)
            continue;
          uint h = ph(x + i, y + j);
          if (d > 55)
            g.DrawPixel(x + i, y + j, j < 0 ? 0x7E9A52u : 0x6E8A46u);
          else if (d > 49)
            g.DrawPixel(x + i, y + j, j < 0 ? 0xA8BE7Eu : 0xC0D290u);
          else if (d < 46 && (h & 3) == 0)
            g.DrawPixel(x + i, y + j, (h & 4) != 0 ? 0xBCCC90u : 0xA8BC7Cu);
        }
      for (int k = 0; k < 70; k++)
        for (int s = -1; s <= 1; s += 2)
        {
          float t = 60 + k;
          int ax = x + (int)(t * 0.7071f + s * 7 * 0.7071f), ay = y + (int)(-t * 0.7071f + s * 7 * 0.7071f);
          g.DrawPixel(ax, ay, 0xB8CC8A);
          g.DrawPixel(ax + 1, ay + 1, 0x7E9A52);
        }
    }

    // ミステリーサークルを描いている円盤の下の点 (世界の座標)。描きおえていたら false
    bool cropSpot(Prop p, out float gx, out float gy)
    {
      float reveal = Math.Min(1, p.t / (float)CROP_DRAW), a = -PI / 2 + reveal * 2 * PI;
      float rr = (20 + 9 * sinf(p.t * 0.21f)) / K;
      gx = p.rx + cosf(a) * rr;
      gy = p.ry + sinf(a) * rr;
      return reveal < 1;
    }

    // ---- 空: 色の移り、お日さま、紙を重ねた遠い丘、紙の雲 ----
    void drawSky25(Gfx g)
    {
      for (int y = 0; y < HZ + 2; y++)
        g.DrawFastHLine(0, y, SW, mix(0x5E9ED0, 0xD4EAF2, (float)y / HZ));
      // お日さま (夕方は沈んでいく)
      float sk = sunsetK(), dk = duskK();
      int sunX = 238, sunY = 30 + (int)(sk * 26 + dk * 20);
      if (sunY < HZ)
      {
        g.FillCircle(sunX, sunY, 15, PAPER_C);
        g.FillCircle(sunX, sunY, 13, mix(0xFFE890, 0xFF9A50, sk));
        g.FillCircle(sunX - 3, sunY - 3, 5, mix(0xFFF6C8, 0xFFC890, sk));
      }
      if (dk > 0.4f) // 夕暮れには月
      {
        g.FillCircle(70, 30, 9, PAPER_C);
        g.FillCircle(70, 30, 7, 0xF4F0D8);
        g.FillCircle(73, 28, 6, mix(0x5E9ED0, 0xD4EAF2, 28f / HZ));
      }
      // 空の紙の雲 (ゆっくり横へ)
      for (int k = 0; k < 3; k++)
      {
        int cx = (int)((k * 120 + frame * (0.08f + k * 0.03f)) % (SW + 80)) - 40, cy = 26 + k * 9;
        paperBlob(g, cx, cy, 6 + k);
      }
      // 遠い丘 (2 枚の紙)。奥は青く、手前は緑で生け垣の点
      hills25(g, HZ - 3, 17, 0x9DBAAE, 0.021f, 1.7f, false);
      hills25(g, HZ + 2, 10, 0x7EA86E, 0.034f, 0.4f, true);
    }

    void paperBlob(Gfx g, int x, int y, int r)
    {
      g.FillCircle(x, y + 1, r + 1, PAPER_C);
      g.FillCircle(x - r, y + 3, r * 2 / 3 + 1, PAPER_C);
      g.FillCircle(x + r, y + 3, r * 2 / 3 + 1, PAPER_C);
      g.FillCircle(x, y, r, 0xFFFFFF);
      g.FillCircle(x - r, y + 2, r * 2 / 3, 0xF4F8FA);
      g.FillCircle(x + r, y + 2, r * 2 / 3, 0xF4F8FA);
      g.DrawFastHLine(x - r - r * 2 / 3, y + r * 2 / 3 + 3, r * 2 + r * 4 / 3, 0xC8D4E0);
    }

    // 丘の紙: 上の縁を波打たせ、白い紙のふちをつける
    void hills25(Gfx g, int baseY, int amp, uint col, float freq, float phase, bool hedges)
    {
      for (int x = 0; x < SW; x++)
      {
        float h = amp * (0.55f + 0.3f * sinf(x * freq + phase) + 0.15f * sinf(x * freq * 2.7f + phase * 2));
        int top = baseY - (int)h;
        g.DrawPixel(x, top - 1, PAPER_C);
        g.DrawFastVLine(x, top, baseY - top + 1, col);
        if (hedges && (x / 9) % 3 == 0 && ((x + 3) % 4) == 0)
          g.DrawPixel(x, top + 3 + (x % 5), mulc(col, 0.7f));
      }
    }

    // ---- 張りぼて ----
    void cutBegin(int w, int h)
    {
      for (int j = 0; j < h; j++)
      {
        int o = j * cv.W;
        for (int i = 0; i < w; i++)
          cv.Buf[o + i] = KEY;
      }
    }

    // 台紙の (ax, ay) が世界の (wx, wy) の高さ alt に来るように置く。地面に立つものは、奥ではまだ寝ていて、近づくと起き上がる
    void cutBlit(Gfx g, int w, int h, int ax, int ay, float wx, float wy, float alt, float sizeK, bool popUp, float squash = 1)
    {
      float sx, sy, sc;
      proj(wx, wy, alt, out sx, out sy, out sc);
      float s = sc / K * sizeK, vs = s * squash;
      if (popUp)
      {
        float k = constrain((syAt(scAt(wy)) - HZ) / 70f, 0, 1);
        vs *= Math.Max(0.1f, k * k * (3 - 2 * k));
      }
      g.BlitCutout(cv, w, h, sx - ax * s, sy - ay * vs, s, vs, PAPER_C, EDGE_C);
    }

    void cutSprite(Gfx g, Sprite sp, bool flip, uint[] pal, float wx, float wy, float alt, float sizeK, bool popUp, float squash = 1)
    {
      int w = sp.W + 2, h = sp.H + 2;
      cutBegin(w, h);
      int x = 1 + (flip ? sp.W - 1 - sp.OX : sp.OX), y = 1 + sp.OY;
      if (pal == null)
        cv.Blit(sp, x, y, flip);
      else
        cv.Blit(sp, x, y, flip, pal);
      cutBlit(g, w, h, w / 2, h - 1, wx, wy, alt, sizeK, popUp, squash);
    }

    void paperProp(Gfx g, Prop p, int idx)
    {
      float wx = p.rx, wy = p.ry;
      switch (p.type)
      {
        case SHEEP:
        case COW:
          {
            float alt = p.lift * 20 + (p.hop > 0 ? Math.Abs(sinf(p.hop * 0.42f)) * 4 : 0);
            bool flip = p.v == 0;
            if (p.type == SHEEP)
              cutSprite(g, SHEEP_S, flip, region == 1 ? HERDWICK : null, wx, wy, alt, PAPER_K, true);
            else
            {
              int v = idx % 3;
              cutSprite(g, COW_S, flip, v == 0 ? null : v == 1 ? COW_BROWN : COW_DARK, wx, wy, alt, PAPER_K, true);
            }
            if (p.hop > 12 && (p.hop % 16) < 10)
            {
              float sx, sy, sc;
              proj(wx, wy, alt + 9, out sx, out sy, out sc);
              g.Blit(HEART, (int)sx, (int)sy, false);
            }
            break;
          }
        case DOG:
          cutSprite(g, (frame / 3) % 2 != 0 ? DOG_A : DOG_B, p.v == 0, null, wx, wy, 0, PAPER_K, true);
          break;
        case RABBIT:
          cutSprite(g, RABBIT_S, p.v == 0, null, wx, wy, p.ay, PAPER_K, true);
          break;
        case BOAT:
          cutBegin(23, 18);
          boatSide(cv, p);
          cutBlit(g, 23, 18, 11, 16, wx, wy, 0, PAPER_K, true);
          break;
        case DUCK:
          cutSprite(g, p.v == 0 ? DUCK_S : DUCKLING, p.vx < 0, null, wx, wy, 0, PAPER_K, true);
          break;
        case BEAST:
          if (p.t >= 60 && p.t < 130)
            cutSprite(g, BEAST_SIT, false, null, wx, wy, 0, PAPER_K, true);
          else
            cutSprite(g, (frame / 3) % 2 != 0 ? BEAST_A : BEAST_B, p.v == 0, null, wx, wy, 0, PAPER_K, true);
          break;
        case TREE:
          {
            int r = p.v != 0 ? 8 : 6;
            cutBegin(20, 27);
            cv.FillRect(9, 15, 3, 11, 0x6A4A2E);
            cv.DrawFastVLine(11, 15, 11, 0x4A3220);
            drawTree(cv, 10, 10, r, idx);
            cutBlit(g, 20, 27, 10, 26, wx, wy, 0, PAPER_K, true);
            break;
          }
        case BALE:
          cutBegin(11, 10);
          cv.FillCircle(5, 5, 4, 0xD8B860);
          cv.DrawCircle(5, 5, 4, 0xB89848);
          cv.DrawCircle(5, 5, 2, 0xA88A42);
          cv.DrawPixel(5, 5, 0x9A7A38);
          cv.DrawPixel(3, 2, 0xF4DC90);
          cutBlit(g, 11, 10, 5, 9, wx, wy, 0, PAPER_K, true);
          break;
        case ROCK:
          cutBegin(14, 9);
          for (int j = -3; j <= 3; j++)
            for (int i = -6; i <= 6; i++)
              if (i * i / 36f + j * j / 9f <= 1)
                cv.DrawPixel(7 + i, 4 + j, litc(0xA8A498, (-i * 0.6f - j * 0.8f) / 4f));
          cutBlit(g, 14, 9, 7, 8, wx, wy, 0, PAPER_K, true);
          break;
        case COTTAGE:
          {
            bool laundry = (p.v & LAUNDRY) != 0;
            int w = laundry ? 42 : 27;
            cutBegin(w, 25);
            cottageFront(cv, (p.v & 1) != 0, laundry, idx);
            cutBlit(g, w, 25, 13, 24, wx, wy, 0, PAPER_K, true);
            break;
          }
        case BARN:
          cutBegin(30, 25);
          barnFront(cv);
          cutBlit(g, 30, 25, 15, 24, wx, wy, 0, PAPER_K, true);
          break;
        case CHURCH:
          cutBegin(38, 48);
          churchFront(cv);
          cutBlit(g, 38, 48, 19, 47, wx, wy, 0, PAPER_K, true);
          break;
        case PUB:
          cutBegin(42, 27);
          pubFront(cv);
          cutBlit(g, 42, 27, 18, 26, wx, wy, 0, PAPER_K, true);
          break;
        case STONEHENGE:
          {
            if (p.kind == 1) // キャッスルリッグ
            {
              cutBegin(90, 46);
              castleStones(cv, 45, 30, false);
              cutBlit(g, 90, 46, 45, 30, wx, wy, 0, 1.1f, true);
            }
            else
            {
              cutBegin(130, 78);
              drawHengeStones(cv, 54, 50);
              cutBlit(g, 130, 78, 54, 50, wx, wy, 0, 1.0f, true);
            }
            int ph2 = p.t % 80; // 円盤が湧いてくるとき、真ん中が緑に光る
            float glowK = p.spawned < 6 && ph2 >= 18 && ph2 <= 70 ? 1 - Math.Abs(ph2 - 40) / 30.0f : 0;
            if (glowK > 0)
            {
              float sx, sy, sc;
              proj(wx, wy, 0, out sx, out sy, out sc);
              for (int j = -40; j <= 6; j++)
                for (int i = -20; i <= 20; i++)
                {
                  float q = i * i / 400f + (j > 0 ? j * j / 36f : 0);
                  if (q < 1)
                    g.BlendPixel((int)sx + i, (int)sy + j, 0xC8FFD8, (int)(160 * glowK * (1 - q) * (j < 0 ? 1 + j / 40f : 1)));
                }
            }
            break;
          }
        case NESSIE:
          {
            if (p.lift <= 0.02f)
              break;
            cutBegin(42, 36);
            if (p.kind == 1)
              drawBownessie(cv, p, 21, 33);
            else
              drawNessie(cv, p, 21, 33);
            cutBlit(g, 42, 36, 21, 33, wx, wy, 0, PAPER_K, false);
            float sx, sy, sc;
            proj(wx, wy, 0, out sx, out sy, out sc);
            for (int w2 = 0; w2 < 2; w2++) // 波紋
            {
              float rx = (9 + w2 * 5 + (frame / 6) % 4) * sc / K * PAPER_K, ry = rx * 0.25f;
              for (int s = 0; s < 40; s++)
              {
                float a = s * (2 * PI / 40);
                g.BlendPixel((int)(sx + cosf(a) * rx), (int)(sy + 1 + sinf(a) * ry), 0xE0F0FA, 130 - w2 * 50);
              }
            }
            break;
          }
      }
    }

    // ---- 正面から見た建物 (張りぼての絵) ----
    // 家: 白い壁に戸と窓。茅葺きは丸みのある金色の屋根、スレートは灰色の瓦。煙突。洗濯物があれば右に紐と服
    void cottageFront(Gfx g, bool slate, bool laundry, int idx)
    {
      g.FillRect(3, 12, 20, 11, 0xEDE3C8);
      g.DrawFastHLine(3, 22, 20, 0xC8BEA4);
      g.DrawFastVLine(22, 12, 11, 0xD0C6AC);
      g.FillRect(11, 16, 3, 7, 0x6A4A2A);
      g.DrawPixel(13, 19, GOLD);
      for (int k = 0; k < 2; k++)
      {
        int wx = k == 0 ? 5 : 17;
        g.FillRect(wx - 1, 14, 5, 5, 0xF8F4E8);
        g.FillRect(wx, 15, 3, 3, 0x3A4A5A);
        g.DrawPixel(wx, 15, 0x7A8A9A);
      }
      // 屋根 (台形)
      for (int y = 2; y <= 12; y++)
      {
        float t = (y - 2) / 10f;
        int x0 = (int)(8 - 7 * t), x1 = (int)(18 + 7 * t);
        for (int x = x0; x <= x1; x++)
        {
          uint c;
          if (!slate)
          {
            c = y < 7 ? 0xD4B06Au : 0xB89254u;
            if ((ph(x, idx) & 3) == 0) c = mulc(c, 0.85f);
            if ((x == x0 || x == x1) && y > 3) c = 0x9A7840;
          }
          else
          {
            c = y < 7 ? 0x7A8090u : 0x5E6474u;
            if (y % 3 == 0 || ((x + (y / 3) * 2) % 4 == 0)) c = mulc(c, 0.85f);
          }
          if (y == 12) c = mulc(c, 0.7f);
          if (y == 2) c = mix(c, 0xFFFFFF, 0.25f);
          g.DrawPixel(x, y, c);
        }
      }
      g.FillRect(16, 0, 3, 5, 0x8A4A3A);
      g.DrawFastHLine(16, 0, 3, 0xA85A44);
      if (laundry) // 洗濯物
      {
        g.DrawFastVLine(26, 11, 12, 0x5A4030);
        g.DrawFastVLine(40, 11, 12, 0x5A4030);
        g.DrawLine(26, 12, 33, 13, 0xE8E8E0);
        g.DrawLine(33, 13, 40, 12, 0xE8E8E0);
        for (int i = 0; i < 4; i++)
        {
          int cx = 28 + i * 3, sway = (int)Math.Round(sinf(frame * 0.15f + i * 1.3f + idx) * 1.0f);
          uint c = CLOTHES[(i + idx) % CLOTHES.Length];
          g.FillRect(cx + sway, 14, 2, (i & 1) == 0 ? 5 : 4, c);
          g.DrawPixel(cx, 13, 0x8A6A44);
        }
      }
    }

    // 納屋: 赤い板壁と腰折れ屋根、白い縁どりの大きな戸
    void barnFront(Gfx g)
    {
      g.FillRect(3, 10, 24, 13, 0x9A4E36);
      for (int x = 3; x < 27; x += 3)
        g.DrawFastVLine(x, 10, 13, 0x7A3A2A);
      int[] rx = { 1, 6, 15, 24, 29 }, ry = { 11, 4, 0, 4, 11 };
      for (int k = 0; k < 4; k++)
      {
        g.FillTriangle(rx[k], ry[k], rx[k + 1], ry[k + 1], rx[k + 1], 11, 0x6A3A2E);
        g.FillTriangle(rx[k], ry[k], rx[k + 1], 11, rx[k], 11, 0x6A3A2E);
      }
      g.DrawLine(1, 11, 6, 4, 0x8A5A44);
      g.DrawLine(6, 4, 15, 0, 0x8A5A44);
      g.FillRect(10, 13, 10, 10, 0x6A2A1E);
      g.DrawRect(10, 13, 10, 10, 0xF0E8D8);
      g.DrawLine(10, 13, 19, 22, 0xF0E8D8);
      g.DrawLine(19, 13, 10, 22, 0xF0E8D8);
      g.FillRect(13, 6, 4, 3, 0x3A2A22);
    }

    // 教会: 尖塔のある四角い塔 (時計、アーチの窓、胸壁) と、うしろに続く身廊
    void churchFront(Gfx g)
    {
      g.FillRect(14, 30, 22, 17, 0xA8A496);
      for (int y = 32; y < 46; y += 3)
        g.DrawFastHLine(14, y, 22, 0x968F82);
      for (int k = 0; k < 3; k++)
      {
        int wx = 18 + k * 6;
        g.FillRect(wx, 35, 2, 6, 0x3A4A5A);
        g.DrawPixel(wx, 34, 0x3A4A5A);
      }
      for (int y = 24; y <= 30; y++) // 身廊の屋根
      {
        float t = (y - 24) / 6f;
        g.DrawFastHLine(15 - (int)(2 * t), y, 22 + (int)(3 * t), y < 27 ? 0x7A8090u : 0x5E6474u);
      }
      g.FillRect(3, 18, 12, 29, 0xBCB8AA);
      g.DrawFastVLine(14, 18, 29, 0x8A867A);
      for (int y = 20; y < 46; y += 3)
        for (int x = 3 + (y / 3 % 2) * 2; x < 14; x += 4)
          g.DrawPixel(x, y, 0xA09C8E);
      for (int x = 3; x < 15; x += 3) // 胸壁
        g.FillRect(x, 16, 2, 2, 0xBCB8AA);
      g.FillCircle(8, 23, 2, 0xF8F4E8); // 時計
      g.DrawPixel(8, 22, 0x2A2A2A);
      g.DrawPixel(9, 23, 0x2A2A2A);
      g.FillRect(7, 30, 3, 6, 0x3A4A5A);
      g.DrawPixel(8, 29, 0x3A4A5A);
      g.FillRect(6, 40, 5, 7, 0x5A3A22); // 戸
      g.FillTriangle(3, 16, 14, 16, 8, 1, 0x6E727A); // 尖塔
      g.FillTriangle(3, 16, 8, 16, 8, 1, 0x8A8E96);
      g.DrawFastVLine(8, 0, 2, GOLD);
      g.DrawPixel(7, 0, GOLD);
      g.DrawPixel(9, 0, GOLD);
    }

    // パブ: 黒い梁の白壁、スレート屋根、緑の戸。腕木に羊の絵の看板
    void pubFront(Gfx g)
    {
      g.FillRect(3, 11, 28, 14, 0xF0EAD8);
      for (int x = 3; x < 31; x += 5)
        g.DrawFastVLine(x, 11, 14, 0x2A2420);
      g.DrawFastHLine(3, 11, 28, 0x2A2420);
      g.DrawFastHLine(3, 18, 28, 0x2A2420);
      for (int k = 0; k < 3; k++)
      {
        int wx = k == 0 ? 5 : k == 1 ? 20 : 25;
        g.FillRect(wx, 13, 3, 3, 0x3A4A5A);
        g.DrawPixel(wx, 13, 0x6A7A8A);
      }
      g.FillRect(13, 19, 4, 6, 0x2E4A30);
      g.DrawPixel(16, 22, GOLD);
      for (int y = 3; y <= 11; y++)
      {
        float t = (y - 3) / 8f;
        g.DrawFastHLine(7 - (int)(6 * t), y, 20 + (int)(12 * t), y < 7 ? 0x6A7080u : 0x4A5060u);
      }
      g.FillRect(5, 0, 3, 5, 0x8A4A3A);
      g.FillRect(24, 0, 3, 5, 0x8A4A3A);
      g.DrawFastHLine(31, 12, 8, 0x2A2420); // 腕木と看板
      g.DrawFastVLine(33, 12, 2, 0x2A2420);
      g.DrawFastVLine(37, 12, 2, 0x2A2420);
      int sw = (int)Math.Round(sinf(frame * 0.07f) * 0.6f);
      g.FillRect(32 + sw, 14, 7, 8, 0xD8B048);
      g.FillRect(33 + sw, 15, 5, 6, 0x2A4A2A);
      g.FillRect(34 + sw, 17, 3, 2, 0xF4F2EA);
      g.DrawPixel(37 + sw, 17, 0x1A1A1A);
    }

    // 汽車を横から: 赤い緩衝梁、黒い煙室と高い煙突、金の帯の緑のボイラーと蒸気溜め、運転室、炭水車、窓の並ぶ客車、車輪
    void paperTrain(Gfx g)
    {
      int total = 37 + train.cars * 26, w = total + 4, h = 18, d = train.dir;
      int fx_ = d > 0 ? w - 2 : 1, by = h - 2;
      cutBegin(w, h);
      Action<int, int, int, int, uint> rect = (off, len, top, hh, c) =>
        cv.FillRect(d > 0 ? fx_ - off - len + 1 : fx_ + off, by + top, len, hh, c);
      Func<int, int> at = off => d > 0 ? fx_ - off : fx_ + off;
      rect(0, 24, -4, 1, 0x1A1A1A);
      rect(0, 1, -7, 3, POPPY);
      rect(1, 5, -11, 7, 0x1A1A1A);
      rect(2, 2, -15, 4, 0x1A1A1A);
      rect(1, 4, -15, 1, 0x2A2A2A);
      rect(6, 11, -11, 7, 0x2A5A3A);
      rect(6, 11, -11, 1, 0x5A9A6A);
      cv.DrawFastVLine(at(9), by - 11, 7, 0xD8B048);
      cv.DrawFastVLine(at(14), by - 11, 7, 0xD8B048);
      rect(11, 3, -13, 2, 0xD8B048);
      rect(17, 7, -15, 11, 0x1E3A28);
      rect(16, 9, -16, 1, 0x101010);
      rect(19, 3, -13, 3, 0xC8D8E0);
      for (int k = 0; k < 3; k++)
      {
        cv.FillCircle(at(4 + k * 6), by - 2, 2, 0x2A2A2A);
        cv.DrawPixel(at(4 + k * 6), by - 2, 0x9A9A9A);
      }
      rect(25, 10, -10, 7, 0x1E3A28);
      rect(25, 10, -11, 1, 0x101010);
      cv.FillCircle(at(28), by - 2, 2, 0x2A2A2A);
      cv.FillCircle(at(33), by - 2, 2, 0x2A2A2A);
      for (int n = 0; n < train.cars; n++)
      {
        int off = 37 + n * 26;
        rect(off, 24, -13, 10, 0x7A2A2A);
        rect(off, 24, -13, 3, 0xE8DCC0);
        rect(off + 1, 22, -14, 1, 0x6A6A70);
        for (int i = 2; i < 22; i += 4)
          rect(off + i, 2, -12, 2, 0x3A3A44);
        rect(off, 24, -3, 1, 0x101010);
        cv.FillCircle(at(off + 4), by - 2, 2, 0x2A2A2A);
        cv.FillCircle(at(off + 19), by - 2, 2, 0x2A2A2A);
      }
      cutBlit(g, w, h, fx_, by, train.rx, train.ry, 0, PAPER_K, true);
    }

    // ---- 飛ぶもの ----
    // 自機をうしろの少し上から: 横に広がる楕円の主翼 (迷彩と白に赤の点の印)、短く見える胴と風防、
    // 手前の水平尾翼と立った垂直尾翼、先頭に黒い機首。台紙の (19, 14) が機体の真ん中。プロペラは画面に直接描く
    void planeBack(Gfx g)
    {
      for (int j = -4; j <= 4; j++) // 主翼
        for (int i = -17; i <= 17; i++)
        {
          float e = i * i / 289f + j * j / 11f;
          if (e > 1)
            continue;
          uint c = j < -1 ? 0x8A9058u : j > 1 ? 0x565C34u : 0x6E7444u;
          g.DrawPixel(19 + i, 14 + j, c);
        }
      g.FillEllipse(11, 14, 3, 1, 0x6A5434); // 迷彩
      g.FillEllipse(26, 13, 3, 1, 0x6A5434);
      for (int s = -1; s <= 1; s += 2) // 印
      {
        g.FillEllipse(19 + s * 10, 14, 2, 1, 0xF0F0F0);
        g.DrawPixel(19 + s * 10, 14, POPPY);
      }
      for (int j = -8; j <= 8; j++) // 胴
        for (int i = -3; i <= 3; i++)
        {
          if (i * i / 9f + j * j / 64f > 1)
            continue;
          g.DrawPixel(19 + i, 14 + j, i < 0 ? 0xA4A472u : i == 0 ? 0x8A8A58u : 0x6E6E44u);
        }
      g.FillEllipse(19, 11, 2, 2, 0x2A3A4A); // 風防
      g.DrawPixel(18, 10, 0xA8D0E8);
      g.FillEllipse(19, 6, 2, 1, 0x3A3A3A); // 機首
      g.DrawPixel(16, 8, 0x4A3A2A);
      g.DrawPixel(22, 8, 0x4A3A2A);
      for (int j = -1; j <= 1; j++) // 水平尾翼
        for (int i = -7; i <= 7; i++)
          if (i * i / 49f + j * j / 2.5f <= 1)
            g.DrawPixel(19 + i, 21 + j, j < 0 ? 0x7A8048u : 0x5E6438u);
      g.FillRect(18, 15, 2, 7, 0x5A5A38); // 垂直尾翼
      g.DrawFastVLine(18, 15, 7, 0x7A7A50);
    }

    // 円盤を横から少し見下ろしたところ: 上の面 (銀の皿と溝の輪) が見え、縁は灯りの並ぶ帯、下は厚みのある胴と光る底。
    // 上にガラスのドーム。(x, y) は胴の真ん中。高さは R の 1.25 倍ほど
    static void saucerBox(float R, out int w, out int h, out int cy)
    {
      w = (int)(R * 2) + 6;
      h = (int)(R * 1.3f) + 8;
      cy = (int)(R * 0.66f) + 4;
    }

    void saucerSide(Gfx g, int x, int y, float R, bool flash, int phase, float burn)
    {
      Func<uint, uint> fin = c =>
      {
        if (flash) c = mix(c, 0xFFFFFF, 0.75f);
        if (burn > 0) c = mix(c, 0x2A2A2A, burn);
        return c;
      };
      // 底の光るところ
      float hy = y + R * 0.42f, hrx = R * 0.34f, hry = R * 0.17f;
      for (int j = -(int)hry - 1; j <= (int)hry + 1; j++)
        for (int i = -(int)hrx - 1; i <= (int)hrx + 1; i++)
        {
          float e = i * i / (hrx * hrx) + j * j / (hry * hry);
          if (e <= 1)
            g.DrawPixel(x + i, (int)hy + j, fin(e < 0.35f && burn < 0.3f ? ((frame / 4) % 2 != 0 ? 0xC8FAFFu : 0x7ADCE8u) : 0x4A4E5Au));
        }
      // 胴 (厚み): 下へふくらむ暗めの金物
      float by = y + R * 0.1f, bry = R * 0.4f;
      for (int j = -(int)bry; j <= (int)bry; j++)
        for (int i = -(int)R; i <= (int)R; i++)
        {
          float e = i * i / (R * R) + j * j / (bry * bry);
          if (e > 1)
            continue;
          uint c = litc(0x7E828E, -0.1f - j / bry * 0.5f - i / R * 0.25f);
          if (e > 0.9f) c = UFO_K;
          g.DrawPixel(x + i, (int)by + j, fin(c));
        }
      // 縁の帯
      float ry = R * 0.36f;
      for (int i = -(int)R; i <= (int)R; i++)
      {
        float t = i / R, yy = ry * sqrtf(Math.Max(0, 1 - t * t));
        for (int k = 0; k < 3; k++)
          g.DrawPixel(x + i, y + (int)yy + k - 1, fin(k == 2 ? UFO_K : 0x4A4E58));
      }
      // 上の面: 明るい銀の皿と、溝の輪
      float ty = y - R * 0.04f, trx = R * 0.94f, tryy = R * 0.34f;
      for (int j = -(int)tryy; j <= (int)tryy; j++)
        for (int i = -(int)trx; i <= (int)trx; i++)
        {
          float e = i * i / (trx * trx) + j * j / (tryy * tryy);
          if (e > 1)
            continue;
          uint c = litc(0xC8CCD6, 0.55f - j / tryy * 0.35f - i / trx * 0.35f);
          if (e > 0.42f && e < 0.52f) c = 0x8A8E9A;
          if (e > 0.93f) c = 0x9A9EAA;
          g.DrawPixel(x + i, (int)ty + j, fin(c));
        }
      // ドーム
      float dr = R * 0.44f, dcy = y - R * 0.16f;
      for (int j = -(int)(dr * 1.1f); j <= (int)(dr * 0.25f); j++)
        for (int i = -(int)dr; i <= (int)dr; i++)
        {
          float e = i * i / (dr * dr) + j * j / (dr * dr * 1.2f);
          if (e > 1)
            continue;
          uint c = burn > 0.3f ? mix(0x2E4A50, 0x1A1A1A, burn) : mix(0x2A8A9A, 0xC8F8FC, 0.35f + (-i * 0.5f - j * 0.8f) / dr * 0.5f);
          if (e > 0.88f) c = mulc(c, 0.7f);
          g.DrawPixel(x + i, (int)dcy + j, fin(c));
        }
      if (burn < 0.3f)
      {
        g.FillRect(x - (int)(dr * 0.45f), (int)(dcy - dr * 0.7f), Math.Max(1, (int)(dr * 0.35f)), Math.Max(1, (int)(dr * 0.2f)), 0xFFFFFF);
      }
      // 縁の灯り (手前の帯を回る)
      for (int k = 0; k < 8; k++)
      {
        if (burn > 0 && (k * 7 + frame / 2) % 5 != 0)
          continue;
        float a = ((k + frame * 0.12f + phase) % 8) / 8f * PI;
        int lx = x + (int)(cosf(a) * R * 0.9f), ly = y + (int)(sinf(a) * ry) - 1;
        uint rc = RIM[(k + frame / 8) % 4];
        int sz = R > 10 ? 2 : 1;
        g.FillRect(lx, ly, sz + 1, sz, rc);
        g.DrawPixel(lx, ly, mix(rc, 0xFFFFFF, 0.6f));
      }
    }

    void drawAir25(Gfx g)
    {
      List<KeyValuePair<float, Action>> items = new List<KeyValuePair<float, Action>>();
      // 煙と湯気 (地面から少しずつ上がる)
      foreach (Puff q in puffs)
        if (q.on)
        {
          Puff qq = q;
          items.Add(new KeyValuePair<float, Action>(q.ry - 0.5f, () =>
          {
            float f = (float)qq.t / qq.life, sx, sy, sc;
            proj(qq.rx, qq.ry, 4 + qq.t * 0.18f, out sx, out sy, out sc);
            uint c = qq.smoke ? mix(0x3A3A3A, 0x9A9A9A, f) : mix(0xF4F4F4, 0xC8CCD0, f);
            int r = Math.Max(1, (int)(qq.r * sc * (f > 0.7f ? (1 - f) / 0.3f : 1)));
            g.FillCircle((int)sx, (int)sy, r, mulc(c, 0.85f));
            g.FillCircle((int)sx - 1, (int)sy - 1, Math.Max(1, r - 1), c);
          }));
        }
      // 雲 (飛行機より低い。紙の雲)
      float rk = rainK();
      uint cloudL = mix(0xFFFFFF, 0x9AA0A8, rk), cloudM = mix(0xEEF2F6, 0x8A9098, rk), cloudD = mix(0xC8D2DE, 0x646A72, rk);
      foreach (Cloud c in clouds)
        if (c.r > 0)
        {
          Cloud cc = c;
          items.Add(new KeyValuePair<float, Action>(c.ry, () =>
          {
            float R = cc.r * K;
            int w = (int)(R * 3.4f) + 4, h = (int)(R * 2.6f) + 4;
            if (w > cv.W || h > cv.H)
              return;
            cutBegin(w, h);
            int x = w / 2, y = h / 2;
            cloudShape(cv, x, y, R, 0, 4, 0, cloudD);
            cloudShape(cv, x, y, R, 0, 0, 1, cloudM);
            cloudShape(cv, x, y, R, -2, -2, R * 0.45f, cloudL);
            cutBlit(g, w, h, x, y, cc.rx, cc.ry, 7, 1.0f, false);
          }));
        }
      // 円盤と光線
      foreach (Ufo u in ufos)
        if (u.on && u.t >= 0 && !(u.riseKind == 1 && u.rise < 0.45f))
        {
          Ufo uu = u;
          items.Add(new KeyValuePair<float, Action>(u.ry, () => paperUfo(g, uu)));
        }
      if (mom.on)
        items.Add(new KeyValuePair<float, Action>(mom.ry, () =>
        {
          float a = momAlt(), s = 0.65f + 0.35f * a, R = 54 * s;
          int w, h, cy;
          saucerBox(R, out w, out h, out cy);
          cutBegin(w, h);
          float burn = mom.dead ? 0.25f + 0.45f * (1 - a) : 0;
          saucerSide(cv, w / 2, cy, R, mom.hitT > 0 && !mom.dead, 0, burn);
          for (int i = 0; i < 4 && !mom.dead; i++) // 砲口
          {
            float pa = portAngle(i);
            if (sinf(pa) < 0)
              continue;
            int px2 = w / 2 + (int)(cosf(pa) * R * 0.6f), py2 = cy + (int)(R * 0.2f) + (int)(sinf(pa) * R * 0.15f);
            cv.FillCircle(px2, py2, 2, UFO_K);
            cv.DrawPixel(px2, py2, (frame / 3) % 2 != 0 ? 0xFFFFFFu : PLASMA);
          }
          cutBlit(g, w, h, w / 2, cy, mom.rx, mom.ry, AIR * a + 4, 1.0f, false);
        }));
      foreach (DebrisP d in debris)
        if (d.on)
        {
          DebrisP dd = d;
          items.Add(new KeyValuePair<float, Action>(d.ry, () =>
          {
            float sx, sy, sc;
            proj(dd.rx, dd.ry, AIR * dd.alt, out sx, out sy, out sc);
            float r = (3 + 4.5f * dd.alt) * sc / K;
            g.FillTriangle((int)(sx + cosf(dd.ang) * r), (int)(sy + sinf(dd.ang) * r), (int)(sx + cosf(dd.ang + 2.3f) * r), (int)(sy + sinf(dd.ang + 2.3f) * r),
                           (int)(sx + cosf(dd.ang + 4) * r), (int)(sy + sinf(dd.ang + 4) * r), (frame / 3) % 2 != 0 ? SILVER : SILVER_D);
          }));
        }
      // ミステリーサークルを描く円盤 (高いところの飾り)
      foreach (Prop p in props)
        if (p.on && p.type == CROPCIRCLE && p.t <= CROP_DRAW + 60)
        {
          Prop pp = p;
          items.Add(new KeyValuePair<float, Action>(p.ry, () =>
          {
            float gx, gy;
            cropSpot(pp, out gx, out gy);
            float alt = 26 + (pp.t > CROP_DRAW ? (pp.t - CROP_DRAW) * 5 : 0);
            int w, h, cy;
            saucerBox(7, out w, out h, out cy);
            cutBegin(w, h);
            saucerSide(cv, w / 2, cy, 7, false, pp.t / 4, 0);
            cutBlit(g, w, h, w / 2, cy, gx, gy, alt, 1.0f, false);
          }));
        }
      items.Sort((ia, ib) => ia.Key.CompareTo(ib.Key));
      foreach (var it in items)
        it.Value();
      // P、自機の弾、自機、円盤の弾 (いつも手前に)
      if (item.on)
      {
        float sx, sy, sc;
        proj(item.rx, item.ry, AIR, out sx, out sy, out sc);
        int r = Math.Max(4, (int)(8 * sc / K)), x = (int)sx, y = (int)sy;
        uint ic = (frame / 4) % 2 != 0 ? POPPY : GOLD;
        g.FillCircle(x, y, r + 1, PAPER_C);
        g.FillCircle(x, y, r, ic);
        g.Text(x, y - 7, CREAM, "P", Datum.TC, 2);
      }
      foreach (Shot s in mine)
        if (s.on)
        {
          float sx, sy, sc;
          proj(s.rx, s.ry, AIR, out sx, out sy, out sc);
          int l = Math.Max(3, (int)(6 * sc / K));
          g.FillRect((int)sx, (int)sy, 1, l, 0xFFF8C8);
          g.BlendPixel((int)sx - 1, (int)sy + 1, GOLD, 110);
          g.BlendPixel((int)sx + 1, (int)sy + 1, GOLD, 110);
        }
      bool planeOn = deadT == 0 && !gone && mode != Mode.Continue;
      if (planeOn && !(invT > 0 && (frame / 3) % 2 != 0))
      {
        cutBegin(38, 26);
        planeBack(cv);
        cutBlit(g, 38, 26, 19, 14, rpx, rpy, AIR, 1.15f, false);
        float sx, sy, sc;
        proj(rpx, rpy, AIR, out sx, out sy, out sc);
        float k = sc / K * 1.15f;
        // プロペラ: 機首の前で回る、透けた円盤と 2 枚の羽根
        float nx = sx, ny = sy - 10.5f * k, prx = 8.5f * k, pry = 2.6f * k;
        for (int j = -(int)pry - 1; j <= (int)pry + 1; j++)
          for (int i = -(int)prx - 1; i <= (int)prx + 1; i++)
          {
            float e = i * i / (prx * prx) + j * j / (pry * pry);
            if (e <= 1)
              g.BlendPixel((int)nx + i, (int)ny + j, 0xECECE4, e > 0.75f ? 110 : 55);
          }
        float th = frame * 0.9f;
        for (int b = 0; b < 2; b++)
        {
          float t2 = th + b * 0.35f;
          int x1 = (int)(nx + cosf(t2) * prx), y1 = (int)(ny + sinf(t2) * pry), x2 = (int)(nx - cosf(t2) * prx), y2 = (int)(ny - sinf(t2) * pry);
          if (b == 0)
            g.DrawLine(x1, y1, x2, y2, 0x3A3A36);
          else
          {
            g.BlendPixel(x1, y1, 0x3A3A36, 90);
            g.BlendPixel(x2, y2, 0x3A3A36, 90);
          }
        }
        g.FillRect((int)nx - 1, (int)ny - 1, 2, 2, 0xB8B8B0); // スピナー
        if (muzzleT > 0)
        {
          glow(g, (int)(sx - 10 * k), (int)(sy - 1 * k), 0xFFF0A0, 2);
          glow(g, (int)(sx + 9 * k), (int)(sy - 1 * k), 0xFFF0A0, 2);
        }
      }
      foreach (Shot s in theirs)
        if (s.on)
        {
          float sx, sy, sc;
          proj(s.rx, s.ry, AIR, out sx, out sy, out sc);
          int r = Math.Max(2, (int)(3.5f * sc / K));
          bool blink = (frame / 2) % 2 != 0;
          g.FillCircle((int)sx, (int)sy, r + 1, PLASMA_D);
          g.FillCircle((int)sx, (int)sy, r, blink ? 0xFFFFFFu : PLASMA);
          g.DrawPixel((int)sx - r / 2, (int)sy - r / 2, 0xFFFFFF);
        }
      // 渡り鳥 (いちばん高いところ)
      if (flock.on)
      {
        float len = sqrtf(flock.vx * flock.vx + flock.vy * flock.vy) + 0.01f, ux = flock.vx / len, uy = flock.vy / len;
        for (int i = 0; i < flock.n; i++)
        {
          int k = (i + 1) / 2, sd = i % 2 != 0 ? -1 : 1;
          float sx, sy, sc;
          proj(flock.rx - ux * k * 7 - uy * sd * k * 6, flock.ry - uy * k * 7 + ux * sd * k * 6, 32, out sx, out sy, out sc);
          int x = (int)sx, y = (int)sy, wy = ((flock.t + i * 5) / 6) % 4 == 0 ? -2 : ((flock.t + i * 5) / 6) % 4 == 2 ? 1 : 0;
          g.FillRect(x - 1, y, 3, 2, 0x2A2A2A);
          g.DrawPixel(x - 2, y + (wy < 0 ? -1 : 0), 0x2A2A2A);
          g.DrawPixel(x + 2, y + (wy < 0 ? -1 : 0), 0x2A2A2A);
          g.DrawPixel(x - 3, y + wy, 0x2A2A2A);
          g.DrawPixel(x + 3, y + wy, 0x2A2A2A);
        }
      }
    }

    void paperUfo(Gfx g, Ufo u)
    {
      float rs = u.rise, R = UFO_R[u.type] * K * (u.rise < 1 ? 0.35f + 0.65f * rs : 1);
      float alt = AIR * (u.rise < 1 ? rs : 1);
      // 光線: 円盤から、持ち上げられている羊や牛へ
      if (u.type == ABDUCTOR && u.state == 1 && u.prop >= 0)
      {
        Prop p = props[u.prop];
        float ux, uy, usc, tx, ty, tsc;
        proj(u.rx, u.ry, alt, out ux, out uy, out usc);
        proj(p.rx, p.ry, p.lift * 20, out tx, out ty, out tsc);
        for (int yy = (int)uy; yy <= (int)ty + 2; yy++)
        {
          float f = (yy - uy) / Math.Max(1, ty + 2 - uy), cx = ux + (tx - ux) * f, hw = (3 + 8 * f) * tsc / K;
          for (int xx = (int)(cx - hw); xx <= (int)(cx + hw); xx++)
          {
            float e = 1 - Math.Abs(xx - cx) / hw;
            g.BlendPixel(xx, yy, BEAM, Math.Min(200, (int)(e * 120) + (((yy + frame) / 2) % 3 == 0 ? 40 : 0)));
          }
        }
      }
      int w, h, cy;
      saucerBox(R, out w, out h, out cy);
      cutBegin(w, h);
      saucerSide(cv, w / 2, cy, R, u.flash > 0, (int)(u.a * 3) + u.type, 0);
      cutBlit(g, w, h, w / 2, cy, u.rx, u.ry, alt, 1.0f, false);
      if (u.rise < 1)
      {
        float sx, sy, sc;
        proj(u.rx, u.ry, 0, out sx, out sy, out sc);
        if (u.riseKind == 1) // 水しぶきの輪
        {
          float k3 = (rs - 0.45f) / 0.55f, ring = (R + 4 + k3 * 14) * sc / K;
          for (int s = 0; s < 40; s++)
          {
            float a = s * (2 * PI / 40);
            g.BlendPixel((int)(sx + cosf(a) * ring), (int)(sy + sinf(a) * ring * 0.3f), 0xF0F8FC, (int)(200 * (1 - k3)));
          }
        }
        else // 緑の光の柱
          for (int j = (int)(-alt * sc) - 6; j <= 3; j++)
            for (int i = -5; i <= 5; i++)
              g.BlendPixel((int)sx + i, (int)sy + j, 0xC8FFD8, (int)(140 * (1 - rs) * (1 - Math.Abs(i) / 6f)));
      }
    }

    // ---- 色掛けのあとの光: 夕暮れの窓、汽車の前照灯、残骸の炎、座った野獣の目 ----
    void drawLights25(Gfx g)
    {
      bool evening = duskK() > 0.25f || sunsetK() > 0.85f;
      foreach (Prop p in props)
      {
        if (!p.on)
          continue;
        float sx, sy, sc;
        proj(p.rx, p.ry, 0, out sx, out sy, out sc);
        float k = sc / K * PAPER_K;
        if (sy < HZ || sy > SH + 30)
          continue;
        if (evening && p.type == COTTAGE)
        {
          glow(g, (int)(sx - 7 * k), (int)(sy - 9 * k), 0xFFD070, 2);
          glow(g, (int)(sx + 5 * k), (int)(sy - 9 * k), 0xFFD070, 2);
        }
        else if ((evening || sunsetK() > 0.55f) && p.type == PUB)
        {
          glow(g, (int)(sx - 12 * k), (int)(sy - 12 * k), 0xFFC860, 2);
          glow(g, (int)(sx + 3 * k), (int)(sy - 12 * k), 0xFFC860, 2);
          glow(g, (int)(sx + 8 * k), (int)(sy - 12 * k), 0xFFC860, 2);
          glow(g, (int)(sx + 17 * k), (int)(sy - 14 * k), 0xFFF0B0, 2);
        }
        else if (evening && p.type == CHURCH)
          glow(g, (int)(sx - 11 * k), (int)(sy - 16 * k), 0xFFD070, 1);
        else if (p.type == WRECK)
          for (int i = 0; i < 7; i++)
          {
            float h = hash2(frame / 3 + i * 7, i);
            g.FillRect((int)(sx + (h - 0.5f) * 40 * sc / K), (int)(sy - 4 - hash2(i, frame / 3) * 10), 2, 3, h < 0.5f ? 0xFF8A2Au : GOLD);
          }
        else if (p.type == BEAST && p.t >= 60 && p.t < 130)
          for (int s = 0; s < 2; s++)
          {
            int ex = (int)(sx + (-3 + s * 4) * k), ey = (int)(sy - 10 * k);
            g.BlendPixel(ex - 1, ey, 0xF0D850, 120);
            g.BlendPixel(ex + 2, ey, 0xF0D850, 120);
            g.FillRect(ex, ey, 2, 1, 0xFFF4A0);
          }
      }
      if (evening && train.on)
      {
        float sx, sy, sc;
        proj(train.rx, train.ry, 0, out sx, out sy, out sc);
        glow(g, (int)sx - (train.dir > 0 ? 1 : 0), (int)(sy - 8 * sc / K * PAPER_K), 0xFFF0B0, 2);
      }
    }
  }
}
