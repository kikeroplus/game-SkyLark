// 物語「ひばり号と羊どろぼうの円盤」: 1 日ごとにイギリスの別の地方へ、円盤の母船を追って旅をする。
// 日の始まりに旅の絵はがき (地方の絵と短い便り)、日の終わりにイギリスの地図の上を次の地方へ飛ぶ場面。
// 地方ごとに地形の作り方、羊の種類、天気、名所が変わる。いまできているのは DAY 1 ウィルトシャーと DAY 2 湖水地方
using System;

namespace SkyLark
{
  public partial class Game
  {
    const int REGIONS = 5;          // ウィルトシャー、湖水地方、ヨークシャー、コーンウォール、スコットランド高地
    const int REGIONS_READY = 2;    // できている地方の数 (それまではできている地方をくり返す)
    const int INTRO_T = 7 * FPS, TRAVEL_T = 6 * FPS;
    const int RABBIT = 17, BOAT = 18; // 野うさぎ (ay = 跳ねている高さ) / 湖のボート (v: 0 = 手こぎ、1 = ヨット)

    int region;                     // いまの地方
    int travelFrom, travelTo;

    static readonly string[] REGION_NAME = { "WILTSHIRE", "THE LAKES", "YORKSHIRE", "CORNWALL", "HIGHLANDS" };
    static readonly string[] AREA1_NAME = { "MEADOW VALE", "WINDERMERE", "THE DALES", "THE COAST", "THE GLENS" };
    static readonly string[] AREA2_NAME = { "HIGH FELLS", "HELVELLYN", "THE MOORS", "BODMIN MOOR", "LOCH NESS" };
    static readonly string[][] STORY = {
      new[] { "SAUCERS OVER WILTSHIRE!", "THEY ARE AFTER OUR SHEEP.", "UP YOU GO, SKYLARK!" },
      new[] { "THE MOTHERSHIP FLED NORTH", "TO THE MISTY LAKES.", "MIND THE BOATS - AND BOWNESSIE!" },
      new[] { "ON THE PURPLE MOORS", "THE SAUCERS LIE IN WAIT.", "FOLLOW THE OLD RAILWAY." },
      new[] { "DOWN TO THE SEA!", "THE SAUCERS HIDE BY THE CLIFFS.", "WATCH OUT FOR THE BEAST." },
      new[] { "THE LAST STAND IN THE GLENS.", "THEY WANT EVERY SHEEP -", "AND NESSIE TOO!" },
    };
    // 地図の上の地方の場所 (地図の幅と高さに対する割合)
    static readonly float[] REGION_MX = { 0.57f, 0.48f, 0.64f, 0.37f, 0.49f }, REGION_MY = { 0.80f, 0.49f, 0.53f, 0.88f, 0.21f };

    int regionOf(int d) { return (d - 1) % REGIONS_READY; }

    // ---- 旅の流れ ----
    void startIntro()
    {
      mode = Mode.Intro;
      modeT = INTRO_T;
      openAt = -9999;
      Sfx(SkyLark.Sfx.Start);
    }
    void stepIntro(Input inp)
    {
      if (--modeT <= 0 || (modeT < INTRO_T - 30 && (inp.FirePressed || inp.StartPressed)))
      {
        mode = Mode.Play; // 幕を開けて飛び立つ
        openAt = frame;
        flyIn();
      }
    }
    void startTravel()
    {
      mode = Mode.Travel;
      modeT = TRAVEL_T;
      travelFrom = region;
      travelTo = regionOf(day + 1);
      Sfx(SkyLark.Sfx.Discover);
    }
    void stepTravel(Input inp)
    {
      if (--modeT <= 0 || (modeT < TRAVEL_T - 30 && (inp.FirePressed || inp.StartPressed)))
        NextDay();
    }

    // ---- 天気: 湖水地方は霧雨 (早めに降りだして長く、弱い)、そのあと虹 ----
    int rainAt() { return region == 1 ? 30 * FPS : RAIN_AT; }
    int rainEnd() { return region == 1 ? 64 * FPS : RAIN_END; }
    float rainbowK()
    {
      if (region != 1)
        return 0;
      int s = rainEnd() + 8 * FPS;
      return ramp(s, s + 4 * FPS) - ramp(s + 16 * FPS, s + 22 * FPS);
    }

    static readonly uint[] RAINBOW = { 0xE84A4A, 0xF0943A, 0xF4E04A, 0x6AC85A, 0x4A9AE0, 0x5A5AC8, 0x9A5AC0 };
    void drawRainbow(Gfx g, int cx, int cy, int r)
    {
      float k = rainbowK();
      if (k <= 0)
        return;
      int top = Math.Max(0, cy - r - 2), bot = Math.Min(SH - 1, cy);
      for (int y = top; y <= bot; y++)
        for (int x = 0; x < SW; x++)
        {
          float d = sqrtf((x - cx) * (x - cx) + (y - cy) * (y - cy)), b = (r - d) / 3.2f;
          if (b < 0 || b >= 7)
            continue;
          int band = (int)b;
          float edge = Math.Min(1, Math.Min(b, 7 - b) * 1.5f);
          float fade = Math.Min(1, (cy - y) / 120f); // 地面に近いところは薄く
          g.BlendPixel(x, y, RAINBOW[band], (int)(85 * k * edge * fade));
        }
    }

    // ---- 湖水地方のもの ----
    static readonly uint[] HERDWICK = SHEEP_S.Recolor("W=948E82 w=767268 g=5E5A52 k=F2F0E8 e=D8D4CC"); // 灰色の毛に白い顔
    uint[] sheepPal() { return region == 1 ? HERDWICK : SHEEP_S.Pal; }

    static readonly Sprite RABBIT_S = new Sprite(new[] {
      "....e.",
      "...bbe",
      "Wbbbbk",
      ".l..l.",
    }, "b=8A6A48 e=6A4A30 W=F4F2EA k=1A1A1A l=5A3E28", 3, 2);

    static readonly uint[] MOOR_LAKES = { 0, 0x86904E, 0x9A7440, 0x8E8E86, 0xBCBCB8 }; // 荒れ地、さび色のシダ、岩、頂
    uint moorCol(int lv) { return region == 1 ? MOOR_LAKES[lv] : MOOR_C[lv]; }

    // ボート (真上から): 手こぎは茶色の舟に漕ぎ手とオール、ヨットは白い舟に帆。うしろに V 字の波
    void drawBoat(Gfx g, Prop p, int x, int y)
    {
      int dir = p.vx >= 0 ? 1 : -1;
      for (int k = 3; k <= 9; k++)
      {
        int a = 110 - k * 11;
        g.BlendPixel(x - dir * (k + 4), y - k / 2, 0xD8ECF8, a);
        g.BlendPixel(x - dir * (k + 4), y + k / 2, 0xD8ECF8, a);
      }
      if (p.v == 0)
      {
        g.FillEllipse(x, y, 7, 3, 0x5A3A22);
        g.FillEllipse(x, y, 6, 2, 0x9A6A3E);
        g.DrawFastVLine(x - 2, y - 2, 5, 0x6A4A2A);
        g.FillCircle(x + 1, y, 1, 0x3A4A6A); // 漕ぎ手
        g.DrawPixel(x + 1, y - 1, 0xE8C8A8);
        int st = (frame / 8) % 2;
        g.DrawLine(x + 1, y - 2, x + 1 - dir * (2 + st * 2), y - 6, 0xC8A878); // オール
        g.DrawLine(x + 1, y + 2, x + 1 - dir * (2 + st * 2), y + 6, 0xC8A878);
      }
      else
      {
        for (int j = 0; j < 4; j++) // 帆の影
          g.BlendPixel(x + 3 + j, y + 3 + j / 2, 0x1A2A3A, 70);
        g.FillEllipse(x, y, 7, 3, 0x8A8A84);
        g.FillEllipse(x, y, 6, 2, 0xF4F2EA);
        g.FillTriangle(x - dir * 4, y, x + dir * 3, y, x - dir * 1, y - 9, 0xFFFFFF); // 帆
        g.DrawLine(x - dir * 1, y - 9, x - dir * 1, y, 0x8A6A44);
        g.DrawPixel(x - dir * 1, y - 10, POPPY);
      }
    }

    // ボート (横から、2.5D の張りぼて用)。台紙の (11, 16) が水ぎわ
    void boatSide(Gfx g, Prop p)
    {
      bool right = p.vx >= 0;
      for (int y = 13; y <= 16; y++) // 舟
      {
        int inset = y - 13;
        g.DrawFastHLine(3 + inset, y, 17 - inset * 2, p.v == 0 ? (y == 13 ? 0xB07A48u : 0x7A4A28u) : (y == 13 ? 0xFFFFFFu : 0xD8D8D0u));
      }
      if (p.v == 0)
      {
        g.FillRect(10, 9, 3, 4, 0x3A4A6A); // 漕ぎ手
        g.FillCircle(11, 7, 1, 0xE8C8A8);
        int st = (frame / 8) % 2;
        g.DrawLine(right ? 8 : 14, 11, right ? 3 - st * 2 : 19 + st * 2, 15, 0xC8A878);
      }
      else
      {
        g.DrawFastVLine(11, 1, 12, 0x8A6A44);
        g.FillTriangle(right ? 10 : 12, 1, right ? 10 : 12, 12, right ? 3 : 19, 12, 0xFFFFFF);
        g.FillTriangle(right ? 12 : 10, 3, right ? 12 : 10, 12, right ? 18 : 4, 12, 0xF0EEE6);
        g.DrawFastHLine(right ? 4 : 13, 8, 6, POPPY);
        g.DrawPixel(11, 0, POPPY);
      }
    }

    // キャッスルリッグの石の環 (斜め上から): 不ぞろいな 38 個の石が楕円に並び、北が入り口。内側の東に小さな四角の石組み
    void castleStones(Gfx g, int x, int y, bool shadow)
    {
      bool keepMask = g.MaskMode;
      if (shadow)
        g.MaskMode = true;
      var items = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<float, Action>>();
      for (int i = 0; i < 38; i++)
      {
        float a = i * (2 * PI / 38) + 0.05f;
        if (Math.Abs(a - 3 * PI / 2) < 0.18f) // 北の入り口
          continue;
        uint h = ph(i * 13 + 7, 91);
        int bx = x + (int)Math.Round(cosf(a) * 32), by = y + (int)Math.Round(sinf(a) * 16);
        int w = 3 + (int)(h & 1), hh = 5 + (int)(h % 6);
        addStone(g, items, bx, by, w, hh, shadow, h);
      }
      for (int i = 0; i < 10; i++) // サンクチュアリ
      {
        int bx = x + 8 + (i < 5 ? i * 3 : (i - 5) * 3), by = y + (i < 5 ? -4 : 5);
        addStone(g, items, bx, by, 2, 4, shadow, ph(i, 33));
      }
      items.Sort((ia, ib) => ia.Key.CompareTo(ib.Key));
      foreach (var it in items)
        it.Value();
      g.MaskMode = keepMask;
    }

    void addStone(Gfx g, System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<float, Action>> items, int bx, int by, int w, int hh, bool shadow, uint h)
    {
      items.Add(new System.Collections.Generic.KeyValuePair<float, Action>(by, () =>
      {
        int x0 = bx - w / 2;
        if (shadow)
        {
          int sx = hh * 7 / 10, sy = hh / 4;
          g.FillTriangle(x0, by, x0 + w, by, x0 + w + sx, by + sy, 0);
          g.FillTriangle(x0, by, x0 + w + sx, by + sy, x0 + sx, by + sy, 0);
          return;
        }
        uint face = mulc(0xA8A498, 0.9f + (h >> 8 & 3) * 0.05f);
        g.FillRect(x0, by - hh, w, hh, face);
        g.DrawFastVLine(x0, by - hh + 1, hh - 1, mix(face, 0xFFFFFF, 0.25f));
        g.DrawFastVLine(x0 + w - 1, by - hh + 1, hh - 1, mulc(face, 0.72f));
        g.DrawFastHLine(x0 + 1, by - hh, Math.Max(1, w - 2), mix(face, 0xFFFFFF, 0.35f)); // 丸い頭
        g.DrawPixel(x0, by - hh, (h & 2) != 0 ? 0x8A9A6Au : mulc(face, 0.8f)); // 苔
        g.DrawFastHLine(x0, by - 1, w, 0x4E4A42);
      }));
    }

    void drawCastlerigg(Gfx g, int x, int y, Prop p)
    {
      for (int j = -26; j <= 26; j++) // 踏まれて薄くなった芝の輪
        for (int i = -48; i <= 48; i++)
        {
          float q = sqrtf((i / 44f) * (i / 44f) + (j / 23f) * (j / 23f));
          if (q > 0.86f && q < 1.04f)
            g.BlendPixel(x + i, y + j, 0xD8E8B0, 60);
        }
      int ph2 = p.t % 80; // 円盤が湧いてくるとき、真ん中が緑に光る
      float glowK = p.spawned < 6 && ph2 >= 18 && ph2 <= 70 ? 1 - Math.Abs(ph2 - 40) / 30.0f : 0;
      if (glowK > 0)
        for (int j = -10; j <= 10; j++)
          for (int i = -20; i <= 20; i++)
          {
            float q = i * i / 400.0f + j * j / 100.0f;
            if (q < 1)
              g.BlendPixel(x + i, y + j, 0xB8FFD0, (int)(230 * glowK * (1 - q * q)));
          }
      castleStones(g, x, y, false);
    }

    // ボウネッシー (ウィンダミア湖の怪物): ネッシーよりずっと低く、水面に 3 つのこぶと小さな頭が並ぶ。緑がかった灰色
    bool bownessieAt(float u, float v, float e)
    {
      if (v < 0)
        return false;
      float[] hu = { -17, -9, -1 }, hw = { 4.5f, 5.5f, 5.5f }, hv = { 3.5f, 5, 5.5f };
      for (int k = 0; k < 3; k++)
      {
        float t = (u - hu[k]) / hw[k];
        if (t > -1 && t < 1 && v <= hv[k] * e * sqrtf(1 - t * t))
          return true;
      }
      if (u > 3.2f && u < 5.8f && v <= 6.2f * e) // 短い首
        return true;

      float qu = (u - 7.5f) / 3.2f, qv = (v - 6.5f * e) / 1.9f; // 頭
      return qu * qu + qv * qv <= 1;
    }

    void drawBownessie(Gfx g, Prop p, int x, int y)
    {
      float e = p.lift;
      int dir = p.v != 0 ? 1 : -1;
      if (e <= 0.02f)
      {
        for (int j = -4; j <= 4; j++)
          for (int i = -16; i <= 16; i++)
            if (i * i / 256.0f + j * j / 16.0f < 1)
              g.BlendPixel(x + i, y + j, 0x1E2A30, 55);
        return;
      }
      const uint DARK = 0x222A20, BODY = 0x3A4636, RIM = 0x6E7E62;
      for (int r = 0; r < 4; r++) // こぶのまわりの波紋
      {
        float cu = r == 3 ? 7 : -17 + r * 8;
        for (int w = 0; w < 2; w++)
        {
          float rx_ = 6 + w * 4 + ((frame / 6 + r) % 4), ry_ = rx_ * 0.28f;
          for (int s = 0; s < 32; s++)
          {
            float a = s * (2 * PI / 32);
            g.BlendPixel(x + (int)(dir * cu + cosf(a) * rx_), y + 1 + (int)(sinf(a) * ry_), 0xE0F0FA, 110 - w * 40);
          }
        }
      }
      for (int v = 1; v <= 12; v++) // 映り込み
      {
        float wave = sinf(v * 0.9f + frame * 0.25f) * 1.3f;
        int a = (int)(140 * (1 - v / 13.0f));
        for (int u = -22; u <= 12; u++)
          if (bownessieAt(u + wave, v, e))
            g.BlendPixel(x + dir * u, y + v, 0x141A12, a);
      }
      for (int v = 0; v <= 10; v++)
        for (int u = -22; u <= 12; u++)
        {
          if (!bownessieAt(u, v, e))
            continue;
          uint c = v <= 1 ? DARK : BODY;
          if (!bownessieAt(u, v + 1, e) || !bownessieAt(u - 1, v + 1, e))
            c = RIM;
          g.DrawPixel(x + dir * u, y - v, c);
        }
      if (e > 0.85f)
        g.DrawPixel(x + dir * 9, y - (int)(7 * e), 0xD8E0B0);
    }

    // ---- 絵はがき (日の始まり) ----
    void drawIntroCard(Gfx g)
    {
      int y0 = 92;
      postcard(g, y0, 340);
      txt(g, SW / 2, y0 + 18, GOLD, "DAY " + day, Datum.TC, 2);
      txt(g, SW / 2, y0 + 40, POPPY, REGION_NAME[region], Datum.TC, 3);
      regionScene(g, region, 26, y0 + 76, SW - 52, 112);
      string[] lines = STORY[region];
      for (int i = 0; i < lines.Length; i++)
        g.Text(SW / 2, y0 + 208 + i * 16, i == lines.Length - 1 ? DEEP : BROWN, lines[i], Datum.TC);
      if ((frame / 15) % 2 != 0 || modeT % 30 < 18)
        g.Text(SW / 2, y0 + 300, DEEP, "PUSH SHOT TO FLY", Datum.TC);
    }

    void regionScene(Gfx g, int reg, int x0, int y0, int w, int h)
    {
      if (reg == 1)
        lakesScene(g, x0, y0, w, h);
      else
        drawPostcardScene(g, x0, y0, w, h);
    }

    // 湖水地方の小さな風景: 霧の空、重なる山 (さび色のシダ)、湖と映り込み、ヨット、白い家、石垣と灰色の羊
    void lakesScene(Gfx g, int x0, int y0, int w, int h)
    {
      g.SetClipRect(x0, y0, w, h);
      for (int j = 0; j < h; j++)
        g.DrawFastHLine(x0, y0 + j, w, mix(0xA8C4D8, 0xE4ECEE, (float)j / h));
      int lakeY = y0 + 62;
      uint[] fellC = { 0x9AAEA8, 0x7E9A7A, 0x6A8A5E };
      for (int layer = 0; layer < 3; layer++) // 山
      {
        for (int x = 0; x < w; x++)
        {
          float fx = x * (0.03f + layer * 0.012f) + layer * 2.1f;
          int top = lakeY - 8 - (int)((26 - layer * 7) * (0.5f + 0.35f * sinf(fx) + 0.15f * sinf(fx * 2.3f + 1)));
          g.DrawFastVLine(x0 + x, top, lakeY - top, fellC[layer]);
          if (layer == 2 && ((x / 6 + (x * 7 % 5)) % 4 == 0))
            g.DrawFastVLine(x0 + x, top + 3, Math.Max(1, (lakeY - top) / 3), 0xA07840); // シダ
          if (layer == 0)
            g.DrawPixel(x0 + x, top, 0xC8D4D0);
        }
      }
      for (int j = 0; j < 22; j++) // 湖と、山の映り込み
        for (int x = 0; x < w; x++)
        {
          uint c = mix(0x6A94BC, 0x4E7AA6, j / 22f);
          if (j < 8 && ((x * 3 + j) % 9) < 4)
            c = mix(c, 0x6A8A5E, 0.35f);
          if ((x + j * 5 + frame / 6) % 23 == 0)
            c = 0xD8ECF8;
          g.DrawPixel(x0 + x, lakeY + j, c);
        }
      int bx = x0 + 60 + (frame / 4) % (w - 120); // ヨット
      g.FillTriangle(bx, lakeY + 6, bx + 7, lakeY + 6, bx + 3, lakeY - 6, 0xFFFFFF);
      g.FillRect(bx - 1, lakeY + 6, 10, 2, 0xF0EEE6);
      g.DrawFastHLine(bx, lakeY + 9, 8, 0x8AB0D0);
      int hx = x0 + w - 70; // 白い家
      g.FillRect(hx, lakeY - 9, 14, 8, 0xF4F2EA);
      g.FillTriangle(hx - 2, lakeY - 9, hx + 16, lakeY - 9, hx + 7, lakeY - 15, 0x5E6474);
      g.FillRect(hx + 6, lakeY - 6, 2, 5, 0x3A4A5A);
      for (int j = 0; j < h - 84; j++) // 手前の牧草地と石垣
        g.DrawFastHLine(x0, lakeY + 22 + j, w, mix(0x7AA850, 0x6A9446, j / (float)(h - 84)));
      for (int x = 0; x < w; x += 2)
      {
        g.DrawPixel(x0 + x, lakeY + 30 + (x / 40) % 2, 0xA8A498);
        g.DrawPixel(x0 + x + 1, lakeY + 30 + (x / 40) % 2, 0x86827A);
      }
      for (int k = 0; k < 5; k++)
      {
        int sx = x0 + 40 + k * 15 + (k & 1) * 4, sy = lakeY + 38 + (k % 3) * 3;
        g.FillRect(sx, sy, 4, 2, 0x948E82);
        g.DrawPixel(sx + 4, sy, 0xF2F0E8);
      }
      g.ClearClipRect();
      g.DrawRect(x0 - 1, y0 - 1, w + 2, h + 2, 0x8A7A5A);
    }

    // ---- 地図 (日の終わり): イギリスの上を、次の地方へ飛ぶ ----
    static readonly float[] GB_X = { 0.50f, 0.60f, 0.56f, 0.47f, 0.58f, 0.66f, 0.66f, 0.60f, 0.57f, 0.62f, 0.66f, 0.68f, 0.72f, 0.76f, 0.80f, 0.88f, 0.90f, 0.85f, 0.92f, 0.82f, 0.70f, 0.60f, 0.50f, 0.40f, 0.30f, 0.28f, 0.38f, 0.48f, 0.40f, 0.30f, 0.34f, 0.36f, 0.30f, 0.38f, 0.46f, 0.46f, 0.42f, 0.46f, 0.36f, 0.40f, 0.36f, 0.34f, 0.38f, 0.30f, 0.36f, 0.32f, 0.38f };
    static readonly float[] GB_Y = { 0.03f, 0.04f, 0.10f, 0.13f, 0.15f, 0.17f, 0.22f, 0.28f, 0.31f, 0.33f, 0.38f, 0.44f, 0.51f, 0.58f, 0.63f, 0.66f, 0.71f, 0.75f, 0.79f, 0.83f, 0.84f, 0.86f, 0.87f, 0.89f, 0.93f, 0.90f, 0.84f, 0.80f, 0.79f, 0.78f, 0.73f, 0.67f, 0.62f, 0.60f, 0.60f, 0.53f, 0.50f, 0.45f, 0.44f, 0.40f, 0.36f, 0.31f, 0.27f, 0.22f, 0.17f, 0.12f, 0.06f };

    // 多角形の塗り (上から 1 行ずつ、辺との交わりのあいだを塗る)
    static void fillPoly(Gfx g, float[] xs, float[] ys, uint c)
    {
      float minY = 1e9f, maxY = -1e9f;
      foreach (float y in ys)
      {
        minY = Math.Min(minY, y);
        maxY = Math.Max(maxY, y);
      }
      float[] hits = new float[xs.Length];
      for (int y = (int)minY; y <= (int)maxY; y++)
      {
        float yc = y + 0.5f;
        int n = 0;
        for (int i = 0, j = xs.Length - 1; i < xs.Length; j = i++)
          if ((ys[i] <= yc) != (ys[j] <= yc))
            hits[n++] = xs[i] + (yc - ys[i]) / (ys[j] - ys[i]) * (xs[j] - xs[i]);
        Array.Sort(hits, 0, n);
        for (int k = 0; k + 1 < n; k += 2)
          g.DrawFastHLine((int)hits[k], y, (int)hits[k + 1] - (int)hits[k] + 1, c);
      }
    }

    void drawTravelCard(Gfx g)
    {
      int y0 = 56;
      postcard(g, y0, 428);
      string title = travelTo == 0 ? "BACK TO WILTSHIRE" : "ON TO " + REGION_NAME[travelTo];
      txt(g, SW / 2, y0 + 20, POPPY, title, Datum.TC, 2);
      int mx = 60, my = y0 + 52, mw = 200, mh = 330;
      // 海
      for (int j = 0; j < mh; j++)
        for (int i = 0; i < mw; i++)
          g.DrawPixel(mx + i, my + j, ((i + j * 3 + frame / 4) % 29 == 0) ? 0xC8E4F4u : 0x8AC0E0u);
      g.DrawRect(mx - 1, my - 1, mw + 2, mh + 2, 0x8A7A5A);
      // 島: 紙のふち → 陸 → 海岸の線
      float[] xs = new float[GB_X.Length], ys = new float[GB_Y.Length];
      for (int i = 0; i < xs.Length; i++)
      {
        xs[i] = mx + GB_X[i] * mw;
        ys[i] = my + 8 + GB_Y[i] * (mh - 16);
      }
      for (int i = 0, j = xs.Length - 1; i < xs.Length; j = i++)
        g.DrawLine((int)xs[j], (int)ys[j] + 2, (int)xs[i], (int)ys[i] + 2, 0x5E8AA8); // 海に落ちる影
      fillPoly(g, xs, ys, 0x86BC5C);
      for (int k = 0; k < 40; k++) // 山や森の点
      {
        uint h = ph(k * 17, 5);
        float px2 = GB_X[(int)(h % (uint)GB_X.Length)] * 0.6f + 0.2f + ((h >> 8) % 20) / 100f, py2 = ((h >> 16) % 90) / 100f;
        int ix = (int)(mx + px2 * mw), iy = (int)(my + 8 + py2 * (mh - 16));
        if (g.Buf[iy * SW + ix] == 0x86BC5C)
          g.DrawPixel(ix, iy, py2 < 0.35f ? 0x6A8A5Eu : 0x6EA04Au);
      }
      for (int i = 0, j = xs.Length - 1; i < xs.Length; j = i++)
        g.DrawLine((int)xs[j], (int)ys[j], (int)xs[i], (int)ys[i], PAPER_C);
      // 地方の点と、たどった道
      Func<int, int> RX = r => (int)(mx + REGION_MX[r] * mw);
      Func<int, int> RY = r => (int)(my + 8 + REGION_MY[r] * (mh - 16));
      for (int r = 0; r + 1 < REGIONS; r++)
        for (int s = 0; s <= 20; s += 2)
        {
          float t = s / 20f;
          g.DrawPixel((int)(RX(r) + (RX(r + 1) - RX(r)) * t), (int)(RY(r) + (RY(r + 1) - RY(r)) * t), 0xA89A78);
        }
      for (int r = 0; r < REGIONS; r++)
      {
        bool visited = r <= travelFrom || (day >= REGIONS);
        uint c = r == travelTo && (frame / 8) % 2 != 0 ? GOLD : visited ? POPPY : 0xA89A78;
        g.FillCircle(RX(r), RY(r), 3, PAPER_C);
        g.FillCircle(RX(r), RY(r), 2, c);
      }
      g.Text(RX(travelFrom) + 6, RY(travelFrom) - 3, DEEP, REGION_NAME[travelFrom]);
      g.Text(RX(travelTo) + 6, RY(travelTo) - 3, POPPY, REGION_NAME[travelTo]);
      // 飛んでいくスカイラーク号
      float pr = constrain(1 - (modeT - 20) / (float)(TRAVEL_T - 50), 0, 1);
      pr = pr * pr * (3 - 2 * pr);
      int fx2 = (int)(RX(travelFrom) + (RX(travelTo) - RX(travelFrom)) * pr), fy2 = (int)(RY(travelFrom) + (RY(travelTo) - RY(travelFrom)) * pr) - 6;
      g.Blit(LIFE, fx2 + 1, fy2 + 1, false, LIFE_SHADOW);
      g.Blit(LIFE, fx2, fy2, false, LIFE.Recolor("C=" + (0x6E7444).ToString("X6")));
      g.Text(SW / 2, y0 + 396, BROWN, "PUSH SHOT TO GO", Datum.TC);
    }
  }
}
