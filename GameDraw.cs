// SKYLARK を描くところ (高精細版)。ゲームの中身は 213x360 の世界のまま動かし、描くときだけ 1.5 倍して 320x540 の点で描く。
// 地面の 1 マスは 12 点。畑の畝や花、生け垣の葉、石垣の石、水面の岸まで点ごとに描き、行ごとに描き置いておく。
// 重ねる順は ESP32 版と同じ: 地面 → 置き物とその影 → 雲の影 → 飛ぶものの影 → 雲 → 鳥 → 母船・円盤・弾 → 色掛け → 灯りと雨 → 文字
using System;

namespace SkyLark
{
  public partial class Game
  {
    public const int SW = 320, SH = 540; // 画面の点の数 (2 倍で 640x1080)
    const float K = 1.5f;                // 世界 → 画面
    const int TS = 12;                   // 地面の 1 マス (画面の点)
    const uint SHADE = 0x1A2410;          // 影の色

    static int S(float v) { return (int)Math.Floor(v * K + 0.5f); }
    int visTop; // 見える範囲の上の端 (2.5D の地面は画面より上まで描く)
    bool visible(int y0, int y1) { return y1 >= visTop && y0 < SH; } // 画面の y0〜y1 が見えるか

    static uint mulc(uint c, float f) // 明るさを f 倍
    {
      int r = Math.Min(255, (int)(((c >> 16) & 255) * f)), g = Math.Min(255, (int)(((c >> 8) & 255) * f)), b = Math.Min(255, (int)((c & 255) * f));
      return (uint)((r << 16) | (g << 8) | b);
    }
    // 左上から光が当たったときの色。lit は -1 (影) 〜 1 (日なた)
    static uint litc(uint c, float lit) { return lit >= 0 ? mix(c, 0xFFFFFF, lit * 0.32f) : mix(c, 0x10140C, -lit * 0.45f); }

    // 画面の点ごとの乱数 (地形の模様に使う。同じ場所ならいつも同じ)
    uint ph(int x, int y)
    {
      uint h = (uint)x * 374761393u + (uint)y * 668265263u + seed * 31u;
      h = (h ^ (h >> 13)) * 1274126177u;
      return h ^ (h >> 16);
    }

    // ---- 点の絵 ----
    // 上から見たプロペラ機 (主翼は楕円、迷彩、白に赤の点の印、風防、尾翼)。右半分は鏡
    static readonly Sprite PLANE = new Sprite(Sprite.Mirror(new[] {
      ".............s",
      "............NN",
      "............FH",
      "...........fFH",
      "..........EfFH",
      "........ooofFH",
      ".....ooLLLLfFH",
      "...oLLGGGGGfFH",
      "..oLGGBBGGGfFC",
      ".oLGGBBGWWGfCc",
      "oLGGBGGWRRWfCC",
      "oGGGGGGWRRWfFH",
      "ogGGGBBGWWGfFH",
      ".ogGBBBGGGgfFH",
      "..oggGGGgggfFH",
      "....ooggggofFH",
      "........ooofFH",
      "...........fFH",
      "........oTTfFH",
      ".......oTTTTfF",
      "........ooTTfF",
      "...........ofT",
    }), "o=2E3420 G=6E7444 g=565C34 L=8A9058 B=6A5434 F=8A8A58 f=6E6E44 H=A4A472 C=2A3A4A c=A8D0E8 R=D84A3A W=F0F0F0 N=3A3A3A s=9A9A9A E=4A3A2A T=5A5A38", 13, 10);

    static readonly Sprite LIFE = new Sprite(new[] {
      "....C....",
      "...CCC...",
      "CCCCCCCCC",
      ".CCCCCCC.",
      "....C....",
      "..CCCCC..",
    }, "C=FFF4D8", 4, 0);
    static readonly uint[] LIFE_SHADOW = LIFE.Recolor("C=1E2A14");

    static readonly Sprite SHEEP_S = new Sprite(new[] {
      "..wWWw..",
      ".wWWWWwe",
      "gWWWWWkk",
      ".gwWWwwe",
      "..ggg...",
    }, "W=F8F6EE w=DCD8CC g=B4B0A4 k=2A2A2A e=4A4040", 4, 2);

    static readonly Sprite COW_S = new Sprite(new[] {
      "..sWWWbbWs..",
      ".sWbbWWbbWo.",
      ".WbbbWWWWWhh",
      "tWWbWWWbbWhp",
      ".WWWWWbbbWhh",
      ".sWWbbWWWWo.",
      "..ssWWWWss..",
    }, "W=F4F2EA s=CBC7BC b=262626 h=2E2A28 p=D8A0A0 o=E8DCC0 t=3A3A3A", 6, 3);
    static readonly uint[] COW_BROWN = COW_S.Recolor("W=C8925A s=A87442 b=7A4A2A h=6A3E22");
    static readonly uint[] COW_DARK = COW_S.Recolor("W=3A3430 s=2A2624 b=EEEAE0 h=2A2624");

    // 牧羊犬 (ボーダー・コリー)。走る脚は 2 コマ
    static readonly Sprite DOG_A = new Sprite(new[] {
      ".......ee",
      "tkkkkkwkk",
      ".kkwwkwwn",
      ".l.l..l.l",
    }, "k=1E1E22 w=F4F2EA t=2A2A2E n=101010 l=1E1E22 e=2A2A2E", 4, 2);
    static readonly Sprite DOG_B = new Sprite(new[] {
      ".......ee",
      "tkkkkkwkk",
      ".kkwwkwwn",
      "l...ll..l",
    }, "k=1E1E22 w=F4F2EA t=2A2A2E n=101010 l=1E1E22 e=2A2A2E", 4, 2);
    // カモの親と子
    static readonly Sprite DUCK_S = new Sprite(new[] {
      "....hh.",
      "bBBBBho",
      "bBBBBb.",
      ".bbbb..",
    }, "B=9A7A52 b=6A4A2E h=3E5A3A o=E8A040", 3, 2);
    static readonly Sprite DUCKLING = new Sprite(new[] {
      ".Yo",
      "YY.",
    }, "Y=F0D860 o=E8A040", 1, 1);
    // ボドミンの野獣 (大きな黒い山猫)。走るのは 2 コマ、座ってこちらを見る姿
    static readonly Sprite BEAST_A = new Sprite(new[] {
      "T.....................k..k",
      ".T...................kkkkk",
      ".TT..kKKKKKKKKKKKKkkkkkEkk",
      "..Tkkkkkkkkkkkkkkkkkkkkkkn",
      "...kkkkkkkkkkkkkkkkkkkkk..",
      "....kkkk..........kkkk....",
      "..kk..................kk..",
      ".kk....................kk.",
    }, "k=141418 K=2E2E38 T=141418 E=C8B030 n=0A0A0A", 13, 5);
    static readonly Sprite BEAST_B = new Sprite(new[] {
      "T.....................k..k",
      ".T...................kkkkk",
      ".TT..kKKKKKKKKKKKKkkkkkEkk",
      "..Tkkkkkkkkkkkkkkkkkkkkkkn",
      "...kkkkkkkkkkkkkkkkkkkkk..",
      "....kkkk..........kkkk....",
      ".......kk......kk.........",
      "......kk........kk........",
    }, "k=141418 K=2E2E38 T=141418 E=C8B030 n=0A0A0A", 13, 5);
    static readonly Sprite BEAST_SIT = new Sprite(new[] {
      "...k......k...",
      "...kk....kk...",
      "...kkkkkkkk...",
      "..kkEEkkEEkk..",
      "..kkkkkkkkkk..",
      "...kkkknkkk...",
      "....kkkkkk....",
      "...kkkkkkkk...",
      "..kkKKKKKKkk..",
      "..kkKKKKKKkk.T",
      ".kkkKKKKKKkkkT",
      ".kkkkkkkkkkkTT",
      ".kkk.kkkk.kkT.",
    }, "k=141418 K=2E2E38 T=141418 E=E8D040 n=0A0A0A", 7, 12);

    // 助けた羊の上に浮かぶハート
    static readonly Sprite HEART = new Sprite(new[] {
      ".RR.RR.",
      "RWWRRRR",
      "RWRRRRR",
      ".RRRRR.",
      "..RRR..",
      "...R...",
    }, "R=F06A8A W=FFD0DC", 3, 3);

    // ---- 地面: 行ごとに描き置く ----
    int[][] rowCache;
    readonly bool[] rowDirty = new bool[ROWS];
    Gfx rowG;

    void invalidateRows()
    {
      if (rowCache == null)
      {
        rowCache = new int[ROWS][];
        for (int r = 0; r < ROWS; r++)
          rowCache[r] = new int[SW * TS];
        rowG = new Gfx(SW, TS);
      }
      for (int r = 0; r < ROWS; r++)
        rowDirty[r] = true;
    }

    // map の行を 1 つずらしたら、描き置きも一緒にずらす。新しい行と、そのすぐ下の行 (上どなりが変わった) は描きなおす
    void shiftRowCache()
    {
      if (rowCache == null)
        return;
      int[] last = rowCache[ROWS - 1];
      Array.Copy(rowCache, 0, rowCache, 1, ROWS - 1);
      rowCache[0] = last;
      Array.Copy(rowDirty, 0, rowDirty, 1, ROWS - 1);
      rowDirty[0] = rowDirty[1] = true;
    }

    int typeAt(int r, int c)
    {
      if (r < 0 || r >= ROWS || c < 0 || c >= COLS)
        return -1;
      return map[r][c].type;
    }
    static bool wet(int t) { return t == WATER || t == SEA || t == BRIDGE || t == RAILBRIDGE || t < 0; }

    void renderRow(int r)
    {
      rowG.Buf = rowCache[r];
      for (int c = 0; c < COLS; c++)
        renderTile(rowG.Buf, r, c);
      for (int c = 0; c < COLS; c++) // 点の模様の上に描くもの (林の木、土手の葦)
        tileExtras(r, c);
      rowDirty[r] = false;
    }

    void renderTile(int[] buf, int r, int c)
    {
      Tile t = map[r][c];
      int x0 = c * TS, by = -rowId[r] * TS; // by: 世界での縦の位置 (模様を行の境でつなげる)
      uint th = ph(c * 7 + 1, rowId[r] * 13 + 5);
      for (int j = 0; j < TS; j++)
        for (int i = 0; i < TS; i++)
        {
          int ax = x0 + i, ay = by + j;
          if (ax >= SW) // 右端の列は 8 点だけ見える
            break;
          uint h = ph(ax, ay);
          float m = noise(ax * 0.06f, ay * 0.06f + 900); // ゆるいむら
          uint col = 0;
          switch (t.type)
          {
            case FIELD:
              col = fieldPixel(t.v, ax, ay, h, m);
              col = mulc(col, 0.94f + 0.12f * m);
              col = hedgePixel(t, i, j, ax, ay, col);
              break;
            case WOOD:
              col = (h & 7) == 0 ? 0x22401Cu : (h & 7) == 1 ? 0x34582Au : 0x2A4A22u;
              if (region == 2) // 秋の林
                col = (h & 7) == 0 ? 0x3A2A16u : (h & 7) == 1 ? 0x5A3A1Eu : 0x4A3420u;
              break;
            case SEA:
            case SAND:
            case CLIFF:
              col = regionTilePixel(t.type, r, c, i, j, h, m);
              break;
            case WATER:
              col = mix(0x3E6E9C, 0x5284B2, m);
              if (h % 23 == 0)
                col = 0x6A9AC8;
              // 岸: 土手が左上から影を落とし、右下は白く波立つ
              if (!wet(typeAt(r, c - 1)) && i < 2)
                col = i == 0 ? 0x2E5880u : mix(col, 0x2E5880, 0.5f);
              if (!wet(typeAt(r - 1, c)) && j < 2)
                col = j == 0 ? 0x2E5880u : mix(col, 0x2E5880, 0.5f);
              if (!wet(typeAt(r, c + 1)) && i == TS - 1)
                col = 0x8AB8D8;
              if (!wet(typeAt(r + 1, c)) && j == TS - 1)
                col = 0x8AB8D8;
              break;
            case BANK:
              col = (h & 7) == 0 ? 0x5A8A3Cu : (h & 7) == 1 ? 0x7AA454u : 0x6A9446u;
              col = mulc(col, 0.95f + 0.1f * m);
              if (h % 41 == 0)
                col = 0xA8A090;
              break;
            case LANE_V:
            case LANE_H:
              {
                int u = t.type == LANE_V ? i : j;
                col = mulc(0xB9A57E, 0.95f + 0.1f * m);
                if (u == 2 || u == 9)
                  col = 0x948258;
                else if (u == 3 || u == 8)
                  col = 0xA8966C;
                else if ((u == 5 || u == 6) && h % 3 != 0)
                  col = (h & 4) != 0 ? 0x8AA05Au : 0x7A9850u;
                else if ((u == 0 || u == TS - 1) && (h & 1) != 0)
                  col = 0x7A9A4A;
                if (h % 29 == 0)
                  col = 0xD8CCA8;
                break;
              }
            case BRIDGE:
              {
                int row3 = (((ay % 3) + 3) % 3), sx = ax + ((ay / 3) & 1) * 2;
                bool mortar = (sx & 3) == 0 || row3 == 0;
                col = mortar ? 0x8E887Au : mulc(0xA8A090, 0.94f + (ph(sx >> 2, ay / 3) & 3) * 0.04f);
                if (typeAt(r, c - 1) != BRIDGE) // 欄干
                {
                  if (i == 0) col = 0x5A564C;
                  else if (i == 1) col = 0xC8C0B0;
                  else if (i == 2) col = 0x9A9484;
                }
                if (typeAt(r, c + 1) != BRIDGE)
                {
                  if (i == TS - 1) col = 0x4E4A42;
                  else if (i == TS - 2) col = 0xB8B0A0;
                  else if (i == TS - 3) col = 0x8A8476;
                }
                break;
              }
            case MOOR:
              col = moorPixel(t.v, r, c, i, j, h, m);
              break;
            case PLAIN: // 白亜の草原: 明るい草に、ところどころ白い白亜と小さな紫の花
              col = (h & 15) == 0 ? 0x86A858u : (h & 15) == 1 ? 0xB4CC80u : 0x9CBA6Au;
              if (region == 2) // ヨークシャーの谷
                col = (h & 15) == 0 ? 0x5E8A3Cu : (h & 15) == 1 ? 0x96B464u : 0x7AA050u;
              if (region == 1) // 湖水地方は濃い緑の丘
                col = (h & 15) == 0 ? 0x5E8A3Cu : (h & 15) == 1 ? 0x8AB45Eu : 0x76A24Cu;
              if (h % 97 == 0) col = 0xECEADC;
              else if (h % 131 == 0) col = 0xA88AC8;
              col = mulc(col, 0.92f + 0.16f * m);
              break;
            case VGREEN:
              col = ((ax / 6) & 1) != 0 ? 0x84B05Au : 0x7AA850u;
              if ((h & 15) == 0)
                col = 0x6E9C48;
              if ((th & 15) == 0 && j >= 3 && j <= 6 && i >= 3 && i <= 8) // 花壇
                col = (h % 3) == 0 ? POPPY : (h % 3) == 1 ? GOLD : (j == 3 || j == 6 ? 0x6A5438u : 0xF4F0E4u);
              break;
            case RAIL:
            case RAILBRIDGE:
              {
                if (t.type == RAIL)
                  col = (h % 3) == 0 ? 0x7A6E5Cu : (h % 3) == 1 ? 0x9A8E7Au : 0x8A7E6Au;
                else
                  col = mulc(0xA8A090, 0.95f + (h & 3) * 0.03f);
                int sx = ((ax % 4) + 4) % 4;
                if ((sx == 1 || sx == 2) && j >= 1 && j <= 10) // 枕木
                  col = j == 1 ? 0x6E5A40u : j == 10 ? 0x463424u : 0x5A4630u;
                if (j == 3 || j == 7) // レール
                  col = (h & 7) == 0 ? 0xE0E0E8u : 0xB0B0B8u;
                else if (j == 4 || j == 8)
                  col = 0x4A4A50;
                if (t.type == RAILBRIDGE)
                {
                  if (j == 0) col = 0xC8C0B0;
                  else if (j == TS - 1) col = 0x4E4A42;
                }
                break;
              }
          }
          buf[j * SW + ax] = (int)col;
        }
    }

    uint fieldPixel(int crop, int ax, int ay, uint h, float m)
    {
      int k3 = ((ay % 3) + 3) % 3;
      switch (crop)
      {
        case PASTURE:
          if ((h & 15) == 0) return 0x5A8438;
          if ((h & 31) == 1) return 0x84AE58;
          if (h % 53 == 2) return 0x96BE66;
          return 0x6E9A48;
        case MEADOW: // 白・黄・桃・薄紫の小さな花
          {
            int f = (int)((h >> 8) % 37);
            if (f == 0) return 0xF6F2E2;
            if (f == 1) return 0xF0D24A;
            if (f == 2) return 0xE79AB4;
            if (f == 3) return 0xB8A8E8;
            return (h & 7) == 0 ? 0x8AA24Au : 0x9AB257u;
          }
        case WHEAT: // 畝の上は明るく下は暗い。ところどころ穂が光る
          if (h % 13 == 0) return 0xEAD690;
          return k3 == 0 ? 0xDCC478u : k3 == 1 ? 0xC8B060u : 0xA89048u;
        case 3: // 耕した畑: 畝と溝、土くれ
          if (h % 17 == 0) return 0xA8875C;
          return k3 == 0 ? 0x9C7A52u : k3 == 1 ? 0x86663Fu : 0x5E4630u;
        case 4: // 菜の花
          if (h % 5 == 0) return 0xEADC5A;
          if (h % 7 == 0) return 0xB8A830;
          if (h % 67 == 0) return 0x7A8A30;
          return 0xD8C840;
        case 5: // 野菜: 土の筋のあいだに丸い株
          {
            int cx = ((ax % 4) + 4) % 4, cy = ((ay % 4) + 4) % 4;
            if (cx == 0)
              return (h & 3) == 0 ? 0x6A5438u : 0x5A4630u;
            float dx = cx - 2, dy = cy - 1.5f, d = dx * dx + dy * dy;
            return d < 0.8f ? 0x7AAA50u : d < 2.3f ? 0x5A8A40u : 0x3E6A2Eu;
          }
        default: // 水田: 水に空が映り、苗が並ぶ
          {
            int sx = ((ax % 3) + 3) % 3, sy = ((ay % 3) + 3) % 3;
            if (sx == 1 && sy == 1) return 0x4E8A3A;
            if (sx == 1 && sy == 0) return 0x6AA850;
            return m > 0.6f ? 0x88B8C4u : 0x6A9AA8u;
          }
      }
    }

    // 生け垣 (葉がでこぼこ、上と左が明るい) と石垣 (石を積んだ目地)。生け垣の右の畑には影が落ちる
    uint hedgePixel(Tile t, int i, int j, int ax, int ay, uint col)
    {
      bool wall = (t.flags & STONEWALL) != 0;
      if ((t.flags & HEDGE_TOP) != 0)
      {
        int top = wall ? TS - 2 : TS - 3 + ((ph(ax, 7) & 3) == 0 ? 1 : 0);
        if (j >= top)
        {
          int u = j - top;
          if (wall)
          {
            if (((ax + (u == 0 ? 0 : 1)) % 3 + 3) % 3 == 0) return 0x66625A;
            return u == 0 ? (((ax / 3) & 1) == 0 ? 0xB4B0A4u : 0x9E9A8Eu) : (((ax / 3) & 1) == 0 ? 0x86827Au : 0x76726Au);
          }
          uint hh = ph(ax, ay + 3);
          if (u == 0) return (hh % 5) == 0 ? 0x62904Cu : 0x4E7A40u;
          if (u == 1) return (hh % 7) == 0 ? 0x46703Au : 0x2F4E24u;
          return 0x22381A;
        }
        if (j == top - 1 && !wall && (ph(ax, ay) & 1) != 0)
          return 0x3E6A30; // 葉先のはみだし
      }
      if ((t.flags & HEDGE_LEFT) != 0)
      {
        int w = wall ? 2 : 2 + ((ph(5, ay) & 3) == 0 ? 1 : 0);
        if (i < w)
        {
          if (wall)
          {
            if (((ay + (i == 0 ? 0 : 1)) % 3 + 3) % 3 == 0) return 0x66625A;
            return i == 0 ? 0xB4B0A4u : 0x86827Au;
          }
          uint hh = ph(ax + 3, ay);
          if (i == 0) return (hh % 5) == 0 ? 0x62904Cu : 0x4E7A40u;
          if (i == 1) return (hh % 7) == 0 ? 0x46703Au : 0x2F4E24u;
          return 0x22381A;
        }
        if (i == w || (i == w + 1 && (ph(ax, ay) & 1) != 0))
          return mulc(col, 0.78f); // 生け垣の影
      }
      return col;
    }

    uint moorPixel(int lv, int r, int c, int i, int j, uint h, float m)
    {
      uint col = mulc(moorCol(lv), 0.93f + 0.14f * m);
      switch (lv)
      {
        case 1: // 荒れ地の草むら
          if (h % 9 == 0) col = 0x6A7A3E;
          else if (h % 13 == 0) col = 0x9A9A5A;
          break;
        case 2: // ヒースの花のかたまり (湖水地方はさび色のシダ、コーンウォールは黄色いハリエニシダ)
          if (region == 2 && m > 0.35f && (h & 3) != 0)
          {
            col = (h & 4) != 0 ? 0xA06A9Au : 0x8A4A82u;
            break;
          }
          if (region == 3)
          {
            if ((h % 6) == 0) col = 0xF0D040;
            else if ((h & 7) == 1) col = 0x6A7A30;
            break;
          }
          if (region == 1)
          {
            if (m > 0.5f && (h & 3) != 0) col = (h & 4) != 0 ? 0xB08848u : 0x8A6A34u;
            else if (h % 11 == 0) col = 0x6A7A3E;
            break;
          }
          if (m > 0.55f && (h & 3) != 0) col = (h & 4) != 0 ? 0xA06A9Au : 0x8A5A88u;
          else if (h % 11 == 0) col = 0x6A5A4A;
          break;
        case 3: // 岩
          if (h % 7 == 0) col = 0x86807A;
          else if (h % 11 == 0) col = 0xB0AAA0;
          break;
        default: // 頂 (白っぽい。高地は雪)
          if (region == 4)
          {
            col = (h % 7) == 0 ? 0xC8D4E0u : (h % 5) == 0 ? 0xFFFFFFu : 0xEEF2F4u;
            break;
          }
          if (m > 0.55f) col = 0xDCD8D0;
          else if (h % 5 == 0) col = 0xC8C4BA;
          break;
      }
      // 岩肌の凹凸: ゆるいむらを左上から照らした陰影。高いところほど強く、岩場には割れ目
      int ax = c * TS + i, ay = j - rowId[r] * TS;
      float e = noise(ax * 0.22f, ay * 0.22f + 40) - noise(ax * 0.22f + 0.22f, ay * 0.22f + 40.22f);
      col = litc(col, constrain(e * (lv == 1 ? 1.5f : lv == 2 ? 2.0f : 4.5f), -1, 1));
      if (lv >= 3 && Math.Abs(noise(ax * 0.09f, ay * 0.09f + 70) - 0.5f) < 0.018f)
        col = mix(col, INKG, 0.35f);
      // 等高線のような段: 低いほうとの境の上と左は明るく、下と右は暗く (光は左上から)
      int up = moorLevel(r - 1, c), left = moorLevel(r, c - 1), down = moorLevel(r + 1, c), right = moorLevel(r, c + 1);
      if (up >= 0 && up < lv && j < 2)
        col = mix(col, 0xFFFFFF, j == 0 ? 0.35f : 0.18f);
      if (left >= 0 && left < lv && i < 2)
        col = mix(col, 0xFFFFFF, i == 0 ? 0.3f : 0.15f);
      if (down >= 0 && down < lv && j >= TS - 3)
        col = mix(col, INKG, 0.2f + 0.15f * (j - (TS - 3)));
      if (right >= 0 && right < lv && i >= TS - 3)
        col = mix(col, INKG, 0.18f + 0.14f * (i - (TS - 3)));
      return col;
    }

    // 林の木と土手の葦 (マスに収まる範囲で、描き置きの上に描く)
    void tileExtras(int r, int c)
    {
      Tile t = map[r][c];
      int x0 = c * TS;
      uint h = ph(c * 31 + 3, rowId[r] * 17 + 11);
      if (t.type == WOOD)
      {
        for (int k = 0; k < 3; k++)
        {
          int cx = x0 + 3 + (int)((h >> (k * 6)) % 6), cy = 3 + (int)((h >> (k * 6 + 3)) % 6), rr = 3 + (int)((h >> (k * 2 + 20)) & 1);
          bool autumn = region == 2;
          uint[] aut = { 0xB8582A, 0xD8883A, 0x8A3A1A };
          uint top = autumn ? aut[(h >> (k * 3)) % 3] : 0x3E6A30;
          rowG.FillCircle(cx + 1, cy + 1, rr, autumn ? 0x3A2010u : 0x1A3014u);
          rowG.FillCircle(cx, cy, rr, top);
          rowG.FillCircle(cx - 1, cy - 1, rr - 1, autumn ? mix(top, 0xFFE0A0, 0.3f) : 0x4E7E3Au);
          rowG.DrawPixel(cx - 1, cy - 2, 0x6E9E4C);
          rowG.DrawPixel(cx - 2, cy - 1, 0x6E9E4C);
        }
      }
      else if (t.type == BANK)
      {
        for (int k = 0; k < 3; k++)
        {
          int rx = x0 + 1 + (int)((h >> (k * 5)) % 10), top = 1 + (int)((h >> (k * 5 + 4)) % 5);
          rowG.DrawFastVLine(rx, top, 5, 0x4A6A30);
          rowG.DrawPixel(rx + 1, top + 3, 0x5A7A3A);
          if (((h >> (k + 24)) & 1) != 0) // がま
            rowG.FillRect(rx, top, 1, 2, 0x7A5A30);
        }
      }
    }

    // ---- 置き物 ----
    void propShadow(Gfx g, Prop p)
    {
      int x = S(p.rx), y = S(p.ry);
      switch (p.type)
      {
        case SHEEP:
          g.FillEllipse(x + 1, y + 2, 4, 2, 0);
          break;
        case COW:
          g.FillEllipse(x + 1, y + 3, 6, 3, 0);
          break;
        case COTTAGE:
          g.FillRect(x - 5, y - 3, 17, 15, 0);
          break;
        case BARN:
          g.FillRect(x - 8, y - 5, 21, 16, 0);
          break;
        case CHURCH:
          g.FillRect(x - 2, y - 11, 11, 21, 0);
          g.FillRect(x - 3, y + 8, 13, 12, 0);
          g.FillTriangle(x + 7, y + 10, x + 7, y + 18, x + 13, y + 14, 0);
          break;
        case TREE:
          {
            int r = p.v != 0 ? 8 : 6;
            g.FillEllipse(x + 4, y + 4, r, r - 1, 0);
            break;
          }
        case BALE:
          g.FillCircle(x + 2, y + 2, 4, 0);
          break;
        case DOG:
          g.FillEllipse(x + 1, y + 2, 4, 2, 0);
          break;
        case PUB:
          g.FillRect(x - 8, y - 5, 25, 19, 0);
          break;
        case STONEHENGE:
          if (p.kind == 1)
            castleStones(g, x, y, true);
          else if (p.kind == 0)
            drawStones(g, x, y, true);
          break;
        case RABBIT:
          g.FillEllipse(x + 1, y + 1, 3, 1, 0);
          break;
        case BEAST:
          g.FillEllipse(x + 3, y + 3, 12, 3, 0);
          break;
        case VIADUCT:
          viaductShadow(g, y);
          break;
        case ABBEY:
          g.FillRect(x - 21, y - 9, 50, 26, 0);
          break;
        case LIGHTHOUSE:
          g.FillTriangle(x - 4, y, x + 4, y, x + 22, y + 14, 0);
          break;
        case MINE:
          g.FillRect(x - 4, y - 2, 14, 10, 0);
          g.FillTriangle(x + 5, y - 6, x + 8, y - 6, x + 16, y + 6, 0);
          break;
        case ROCK:
          g.FillEllipse(x + 2, y + 2, 5, 3, 0);
          break;
        case TANK:
          g.FillRect(x - 6, y - 3, 16, 11, 0);
          break;
        case AAGUN:
          g.FillEllipse(x + 2, y + 2, 7, 5, 0);
          break;
      }
    }

    void drawProp(Gfx g, Prop p, int idx)
    {
      int x = S(p.rx), y = S(p.ry), reach = p.type == WRECK || p.type == STONEHENGE || p.type == NESSIE || p.type == CROPCIRCLE || p.type == ABBEY || p.type == WHITEHORSE ? 60 : 22;
      if (p.type == WHITEHORSE)
        return; // 地面の模様なので、先に描いてある
      if (!visible(y - reach, y + reach))
        return;
      switch (p.type)
      {
        case DEBRIS:
          g.FillEllipse(x, y + 1, 6, 4, 0x3A2E1E); // 焦げ跡と、ねじれた金物
          g.FillEllipse(x, y + 1, 4, 2, 0x22180E);
          g.FillTriangle(x - 4, y + 1, x + 3, y - 3, x + 4, y + 3, 0x6A6E76);
          g.DrawLine(x - 4, y + 1, x + 3, y - 3, 0xA8ACB4);
          g.FillTriangle(x + 1, y + 2, x + 6, y, x + 5, y + 4, 0x4A4E56);
          break;
        case WRECK:
          drawWreck(g, x, y);
          break;
        case TANK:
          drawTank(g, p, x, y);
          break;
        case AAGUN:
          drawAAGun(g, p, x, y);
          break;
        case SHEEP:
        case COW:
          {
            if (p.lift > 0) // 持ち上げられているときは地面に影
              g.FillEllipse(x, y + 2, 3, 1, 0x2A3A20);
            int ly = y - (int)(p.lift * 30);
            if (p.hop > 0) // うれしそうに跳ねる
              ly -= (int)(Math.Abs(sinf(p.hop * 0.42f)) * 6);
            bool flip = p.v == 0, kick = p.lift > 0 && (frame / 4) % 2 != 0;
            if (p.lift > 0) // じたばたする脚
            {
              int lx = p.type == SHEEP ? 2 : 3;
              g.DrawFastVLine(x - lx, ly + 2, kick ? 3 : 2, 0x2A2A2A);
              g.DrawFastVLine(x + lx, ly + 2, kick ? 2 : 3, 0x2A2A2A);
            }
            if (p.type == SHEEP)
              g.Blit(SHEEP_S, x, ly, flip, sheepPal());
            else
            {
              int v = idx % 3;
              g.Blit(COW_S, x, ly, flip, region == 4 ? COW_HIGHLAND : v == 0 ? COW_S.Pal : v == 1 ? COW_BROWN : COW_DARK);
            }
            if (p.hop > 12 && (p.hop % 16) < 10)
              g.Blit(HEART, x + (flip ? -3 : 3), ly - 7 - (16 - p.hop % 16) / 3, false);
            break;
          }
        case DOG:
          g.Blit((frame / 3) % 2 != 0 ? DOG_A : DOG_B, x, y, p.v == 0);
          break;
        case DUCK:
          drawDuck(g, p, x, y);
          break;
        case PUB:
          drawPub(g, x, y, p.v != 0);
          break;
        case STONEHENGE:
          if (p.kind == 1)
            drawCastlerigg(g, x, y, p);
          else if (p.kind == 2)
            drawMount(g, x, y, p, true, true);
          else if (p.kind == 3)
            drawCastleIsland(g, x, y, p, true, true);
          else
            drawStonehenge(g, x, y, p);
          break;
        case RABBIT:
          g.Blit(RABBIT_S, x, y - (int)(p.ay * K), p.v == 0);
          break;
        case BOAT:
          drawBoat(g, p, x, y);
          break;
        case BEAST:
          if (p.t >= 60 && p.t < 130)
            g.Blit(p.kind == 1 ? SHUCK_SIT : BEAST_SIT, x, y, false);
          else if (p.kind == 1)
            g.Blit((frame / 3) % 2 != 0 ? SHUCK_A : SHUCK_B, x, y, p.v == 0);
          else
            g.Blit((frame / 3) % 2 != 0 ? BEAST_A : BEAST_B, x, y, p.v == 0);
          break;
        case VIADUCT:
          drawViaduct(g, y);
          break;
        case ABBEY:
          drawAbbey(g, x, y);
          break;
        case LIGHTHOUSE:
          drawLighthouse(g, x, y);
          break;
        case MINE:
          drawMine(g, x, y);
          break;
        case CROPCIRCLE:
          drawCropSaucer(g, p, x, y);
          break;
        case NESSIE:
          if (p.kind >= 1)
            drawBownessie(g, p, x, y);
          else
            drawNessie(g, p, x, y);
          break;
        case COTTAGE:
          drawCottage(g, x, y, (p.v & 1) != 0, idx);
          if ((p.v & LAUNDRY) != 0)
            drawLaundry(g, x, y, idx);
          break;
        case BARN:
          if (p.kind == 1)
            drawStoneBarn(g, x, y);
          else
            drawBarn(g, x, y);
          break;
        case CHURCH:
          drawChurch(g, x, y);
          break;
        case TREE:
          drawTree(g, x, y, p.v != 0 ? 8 : 6, idx);
          break;
        case BALE: // 丸めた干し草: 渦の筋
          g.FillCircle(x, y, 3, 0xD8B860);
          g.DrawCircle(x, y, 3, 0xB89848);
          g.DrawPixel(x, y, 0x9A7A38);
          g.DrawPixel(x + 1, y, 0xB89848);
          g.DrawPixel(x, y - 1, 0xB89848);
          g.DrawPixel(x - 2, y - 2, 0xF4DC90);
          g.DrawPixel(x - 1, y - 2, 0xF4DC90);
          break;
        case ROCK:
          for (int j = -3; j <= 3; j++)
            for (int i = -5; i <= 5; i++)
            {
              if (i * i / 25.0f + j * j / 9.0f > 1)
                continue;
              g.DrawPixel(x + i, y + j, litc(0xA8A498, (-i * 0.6f - j * 0.8f) / 4.0f));
            }
          g.DrawLine(x - 1, y - 2, x + 1, y + 1, 0x6A665E);
          break;
      }
    }

    // 茅葺き (金色の藁の筋) かスレート (瓦の段) の屋根。手前に白い壁、窓と戸。煙突から煙
    void drawCottage(Gfx g, int x, int y, bool slate, int idx)
    {
      int x0 = x - 8, y0 = y - 6;
      for (int j = 0; j < 12; j++)
        for (int i = 0; i < 17; i++)
        {
          if ((i == 0 || i == 16) && (j == 0 || j == 11))
            continue;
          bool upper = j < 6;
          uint c;
          if (!slate)
          {
            c = upper ? 0xCCA862u : 0xB08A4Au;
            if ((ph(x0 + i, idx) & 3) == 0)
              c = upper ? 0xB89650u : 0x9A7840u;
            if (j == 5) c = 0xDCB872;
            if (j == 6) c = 0x8A6A36;
          }
          else
          {
            c = upper ? 0x7A8090u : 0x5A6070u;
            if (j % 3 == 2 || ((i + (j / 3) * 2) % 4 == 0))
              c = upper ? 0x687080u : 0x4A5060u;
            if (j == 5) c = 0x8A90A0;
            if (j == 6) c = 0x3E4450;
          }
          if (j == 11 || i == 16)
            c = mulc(c, 0.75f);
          else if (i == 0 || j == 0)
            c = mix(c, 0xFFFFFF, 0.15f);
          g.DrawPixel(x0 + i, y0 + j, c);
        }
      g.FillRect(x0 + 1, y0 + 12, 15, 3, 0xEDE3C8);
      g.DrawFastHLine(x0 + 1, y0 + 14, 15, 0xC8BEA4);
      g.FillRect(x0 + 3, y0 + 12, 2, 2, 0x3A4A5A);
      g.FillRect(x0 + 12, y0 + 12, 2, 2, 0x3A4A5A);
      g.FillRect(x0 + 8, y0 + 12, 2, 3, 0x6A4A2A);
      g.FillRect(x + 3, y - 8, 3, 3, 0x8A4A3A);
      g.DrawFastHLine(x + 3, y - 8, 3, 0xA85A44);
      g.DrawPixel(x + 4, y - 7, 0x2A1A14);
      for (int k = 0; k < 2; k++) // 煙: のぼりながら薄れる
      {
        int s = (frame / 5 + k * 5) % 10, a = 200 - s * 18;
        int sx = x + 4 + s / 3, sy = y - 10 - s * 2;
        g.BlendPixel(sx, sy, 0xE8E8E4, a);
        g.BlendPixel(sx + 1, sy, 0xE8E8E4, a);
        g.BlendPixel(sx, sy - 1, 0xE8E8E4, a);
        g.BlendPixel(sx + 1, sy - 1, 0xE8E8E4, a / 2);
      }
    }

    // 洗濯物: 家の横に張った紐に、色とりどりの服が風に揺れる
    static readonly uint[] CLOTHES = { 0xF4F2EA, 0x8ACAE8, 0xD84A3A, 0xF0C860, 0xF4F2EA, 0x9AB257 };
    void drawLaundry(Gfx g, int x, int y, int idx)
    {
      int x0 = x + 10, y0 = y - 3, x1 = x + 25, y1 = y - 1;
      g.FillRect(x0, y0 - 1, 1, 4, 0x5A4030);
      g.FillRect(x1, y1 - 1, 1, 4, 0x5A4030);
      g.DrawLine(x0, y0, x1, y1, 0xE8E8E0);
      for (int i = 0; i < 4; i++)
      {
        float t = (i + 1) / 5.0f;
        int cx = x0 + (int)((x1 - x0) * t), cy = y0 + (int)((y1 - y0) * t) + 1;
        int sway = (int)Math.Round(sinf(frame * 0.15f + i * 1.3f + idx) * 1.3f), h = (i & 1) == 0 ? 4 : 3;
        uint c = CLOTHES[(i + idx) % CLOTHES.Length];
        for (int j = 0; j < h; j++) // 影
          g.BlendPixel(cx + sway + 2, cy + 2 + j, SHADE, 70);
        g.FillRect(cx - 1 + sway / 2, cy, 3, 1, mulc(c, 0.8f));
        g.FillRect(cx - 1 + sway, cy + 1, 3, h - 1, c);
        g.DrawPixel(cx + sway, cy, 0x8A6A44); // 洗濯ばさみ
      }
    }

    // パブ: 黒い梁の白壁にスレート屋根、両端に煙突。軒先に羊の絵の看板が下がる (夕暮れには灯りがともる)
    void drawPub(Gfx g, int x, int y, bool right)
    {
      int x0 = x - 11, y0 = y - 8;
      for (int j = 0; j < 12; j++)
        for (int i = 0; i < 23; i++)
        {
          bool upper = j < 6;
          uint c = upper ? 0x6A7080u : 0x4A5060u;
          if (j % 3 == 2 || ((i + (j / 3) * 2) % 4 == 0))
            c = mulc(c, 0.86f);
          if (j == 5) c = 0x7E8494;
          if (j == 6) c = 0x343844;
          if (j == 11 || i == 22) c = mulc(c, 0.72f);
          g.DrawPixel(x0 + i, y0 + j, c);
        }
      for (int k = 0; k < 2; k++) // 煙突
      {
        int cx = x0 + (k == 0 ? 2 : 18);
        g.FillRect(cx, y0 - 2, 3, 4, 0x8A4A3A);
        g.DrawFastHLine(cx, y0 - 2, 3, 0xA85A44);
        g.DrawPixel(cx + 1, y0 - 1, 0x2A1A14);
      }
      // 白壁と黒い梁、格子の窓、緑の戸
      g.FillRect(x0, y0 + 12, 23, 5, 0xF0EAD8);
      for (int i = 0; i < 23; i += 4)
        g.DrawFastVLine(x0 + i, y0 + 12, 5, 0x2A2420);
      g.DrawFastHLine(x0, y0 + 12, 23, 0x2A2420);
      g.DrawFastHLine(x0, y0 + 16, 23, 0x3A322A);
      int[] wins = { 2, 6, 14, 18 };
      foreach (int wx in wins)
      {
        g.FillRect(x0 + wx, y0 + 13, 2, 2, 0x3A4A5A);
        g.DrawPixel(x0 + wx, y0 + 13, 0x6A7A8A);
      }
      g.FillRect(x0 + 10, y0 + 13, 3, 4, 0x2E4A30);
      g.DrawPixel(x0 + 12, y0 + 15, GOLD);
      // 看板: 腕木から下がり、少し揺れる
      int sx = right ? x0 + 25 : x0 - 7, bx = right ? x0 + 22 : x0;
      g.DrawFastHLine(Math.Min(sx + 3, bx), y0 + 12, Math.Abs(sx + 3 - bx) + 1, 0x2A2420);
      int sw = (int)Math.Round(sinf(frame * 0.07f) * 0.6f);
      g.DrawFastVLine(sx + 1, y0 + 13, 1, 0x2A2420);
      g.DrawFastVLine(sx + 4, y0 + 13, 1, 0x2A2420);
      g.FillRect(sx + sw, y0 + 14, 6, 7, 0xD8B048);
      g.FillRect(sx + 1 + sw, y0 + 15, 4, 5, 0x2A4A2A);
      g.FillRect(sx + 1 + sw, y0 + 17, 3, 2, 0xF4F2EA); // 羊の絵
      g.DrawPixel(sx + 4 + sw, y0 + 17, 0x1A1A1A);
    }

    // カモ: 水の上では後ろに V 字の波を引き、岸に上がるとよちよち歩く
    void drawDuck(Gfx g, Prop p, int x, int y)
    {
      bool flip = p.vx < 0, onWater = tileTypeAt(p.x, p.y) == WATER;
      int back = flip ? 1 : -1;
      if (onWater)
        for (int k = 2; k <= 6; k++)
        {
          int a = 120 - k * 16;
          g.BlendPixel(x + back * k, y - k / 2 + 1, 0xC8E4F4, a);
          g.BlendPixel(x + back * k, y + k / 2 + 1, 0xC8E4F4, a);
        }
      else if ((frame / 4 + p.v) % 2 != 0)
        y--;
      if (p.v == 0)
        g.Blit(DUCK_S, x, y, flip);
      else
        g.Blit(DUCKLING, x, y, flip);
    }

    // ---- 名所 ----
    // ストーンヘンジ (斜め上から見下ろす): まわりの土手と堀、北東へ延びるアヴェニュー、外のヒールストーン。
    // 石の環は柱の上にまぐさ石を渡し (ところどころ欠けている)、内側に馬蹄形の 5 組の三石塔、小さなブルーストーン、倒れた石。
    // 石は奥 (上) から手前 (下) の順に描いて重ね、影は右下の草に落とす
    const int HR_X = 38, HR_Y = 19;           // 石の環の横と縦の半径 (斜めから見るので縦がつぶれる)
    const int SARSENS = 16;                   // 環の柱の数 (本物より少なく、太く)
    static readonly bool[] SARSEN_GONE = { false, false, false, true, false, false, false, false, true, true, false, false, false, false, true, false };
    static readonly bool[] UPRIGHT_GONE = { false, false, false, false, false, false, false, false, false, true, false, false, false, false, false, false };
    static readonly float[] TRILITHON = { 0.55f, 1.6f, 2.65f, 3.7f, 4.65f }; // 北東 (右上) が開いた馬蹄形
    const uint STONE_TOP = 0xD8D2C2, STONE_FACE = 0xA8A294, STONE_SIDE = 0x7E7A6E, STONE_EDGE = 0x4E4A42;

    // 立っている石を 1 つずつ渡す (底の真ん中 bx, by、幅 w、高さ h)。影と本体の両方で使う
    void forEachStone(int x, int y, Action<int, int, int, int> f)
    {
      for (int i = 0; i < SARSENS; i++)
        if (!UPRIGHT_GONE[i])
        {
          float a = i * (2 * PI / SARSENS) + 0.1f;
          f(x + (int)Math.Round(cosf(a) * HR_X), y + (int)Math.Round(sinf(a) * HR_Y), 7, 16);
        }
      for (int k = 0; k < TRILITHON.Length; k++)
      {
        float a = TRILITHON[k], tx = -sinf(a), ty = cosf(a) * 0.5f, tl = sqrtf(tx * tx + ty * ty);
        tx /= tl;
        ty /= tl;
        int cx = x + (int)Math.Round(cosf(a) * 18), cy = y + (int)Math.Round(sinf(a) * 9), h = k == 2 ? 27 : 23;
        for (int s = -1; s <= 1; s += 2)
          f(cx + (int)Math.Round(tx * 4.5f * s), cy + (int)Math.Round(ty * 4.5f * s), 6, h);
      }
      for (int k = 0; k < 12; k++) // ブルーストーン
      {
        float a = k * (2 * PI / 12) + 0.3f;
        f(x + (int)Math.Round(cosf(a) * 28), y + (int)Math.Round(sinf(a) * 14), 3, 6);
      }
      f(x + 58, y - 30, 7, 15); // ヒールストーン
    }

    // 立った石 1 つ: 正面の面、左の明るい縁と右の暗い縁、上の面、根もとの影。苔の点
    void upright(Gfx g, int bx, int by, int w, int h)
    {
      int x0 = bx - w / 2;
      bool small = w <= 3;
      g.FillRect(x0, by - h, w, h, small ? 0x7E848Au : STONE_FACE);
      if (!small)
      {
        g.DrawFastVLine(x0, by - h, h, mix(STONE_FACE, 0xFFFFFF, 0.2f));
        g.DrawFastVLine(x0 + w - 1, by - h, h, STONE_SIDE);
        g.DrawFastVLine(x0 + 1, by - h + 1, h - 2, mix(STONE_FACE, 0xFFFFFF, 0.1f));
        for (int k = 0; k < 5; k++) // 苔と風化の点
        {
          uint hh = ph(bx * 7 + k, by * 3 + h);
          g.DrawPixel(x0 + 1 + (int)(hh % (uint)(w - 1)), by - h + 2 + (int)((hh >> 4) % (uint)(h - 3)), (hh & 64) != 0 ? 0x8A9A6Au : STONE_SIDE);
        }
      }
      g.DrawFastHLine(x0, by - h, w, small ? 0xA0A6AAu : STONE_TOP);
      g.DrawFastHLine(x0, by - 1, w, STONE_EDGE);
    }

    // まぐさ石: 2 本の柱の上を渡す横の石 (上の面が明るく、下の縁が暗い)
    void lintel(Gfx g, int x0, int y0, int x1, int y1)
    {
      if (x1 < x0)
      {
        int t = x0; x0 = x1; x1 = t;
        t = y0; y0 = y1; y1 = t;
      }
      x0 -= 4;
      x1 += 4;
      for (int xx = x0; xx <= x1; xx++)
      {
        int yy = y0 + (x1 == x0 ? 0 : (y1 - y0) * (xx - x0) / (x1 - x0));
        g.DrawPixel(xx, yy - 3, STONE_TOP);
        g.DrawPixel(xx, yy - 2, STONE_TOP);
        g.DrawPixel(xx, yy - 1, STONE_FACE);
        g.DrawPixel(xx, yy, STONE_FACE);
        g.DrawPixel(xx, yy + 1, STONE_FACE);
        g.DrawPixel(xx, yy + 2, STONE_EDGE);
      }
      g.DrawFastVLine(x0, y0 - 3, 6, STONE_EDGE);
      g.DrawFastVLine(x1, y1 - 3, 6, STONE_EDGE);
    }

    // 影: 柱の根もとから右下へ伸びる平行四辺形 (Mask に描く)
    void drawStones(Gfx g, int x, int y, bool shadow)
    {
      bool keepMask = g.MaskMode;
      g.MaskMode = true;
      forEachStone(x, y, (bx, by, w, h) =>
      {
        int sx = h * 7 / 10, sy = h / 4;
        int x0 = bx - w / 2, x1 = x0 + w;
        g.FillTriangle(x0, by, x1, by, x1 + sx, by + sy, 0);
        g.FillTriangle(x0, by, x1 + sx, by + sy, x0 + sx, by + sy, 0);
      });
      g.FillRect(x + 24, y + 17, 15, 5, 0); // 倒れた石
      g.FillRect(x - 38, y + 10, 13, 5, 0);
      g.MaskMode = keepMask;
    }

    void drawStonehenge(Gfx g, int x, int y, Prop p)
    {
      // 土手と堀 (つぶれた楕円)、踏まれて薄くなった内側の芝
      for (int j = -36; j <= 36; j++)
        for (int i = -66; i <= 66; i++)
        {
          float q = sqrtf((i / 62.0f) * (i / 62.0f) + (j / 32.0f) * (j / 32.0f));
          if (q > 1.04f)
            continue;
          uint h = ph(x + i, y + j);
          if (q > 0.95f)
            g.DrawPixel(x + i, y + j, j < 0 ? 0x7E9A52u : 0x6E8A46u); // 堀
          else if (q > 0.85f)
            g.DrawPixel(x + i, y + j, j < 0 ? 0xA8BE7Eu : 0xC0D290u); // 土手 (手前の斜面に日が当たる)
          else if (q < 0.78f && (h & 3) == 0)
            g.DrawPixel(x + i, y + j, (h & 4) != 0 ? 0xBCCC90u : 0xA8BC7Cu);
        }
      for (int k = 0; k < 64; k++) // アヴェニュー: 右上へ延びる 2 本の土手
      {
        float t = 52 + k;
        for (int s = -1; s <= 1; s += 2)
        {
          int ax = x + (int)(t * 0.88f + s * 4), ay = y - (int)(t * 0.48f) + s * 5;
          g.DrawPixel(ax, ay, 0xB8CC8A);
          g.DrawPixel(ax, ay + 1, 0x7E9A52);
        }
      }
      // 円盤が湧いてくるとき、真ん中が緑に光る
      int ph2 = p.t % 80;
      float glowK = p.spawned < 6 && ph2 >= 18 && ph2 <= 70 ? 1 - Math.Abs(ph2 - 40) / 30.0f : 0;
      if (glowK > 0)
      {
        for (int j = -13; j <= 13; j++)
          for (int i = -26; i <= 26; i++)
          {
            float q = i * i / 676.0f + j * j / 169.0f;
            if (q < 1)
              g.BlendPixel(x + i, y + j, 0xB8FFD0, (int)(230 * glowK * (1 - q * q)));
          }
        for (int j = 0; j < 40; j++) // 立ちのぼる光の柱 (石より手前に描くのはあと)
          for (int i = -4; i <= 4; i++)
            g.BlendPixel(x + i, y - j, 0xD8FFE0, (int)(150 * glowK * (1 - j / 40.0f) * (1 - Math.Abs(i) / 5.0f)));
      }
      drawHengeStones(g, x, y);
    }

    void drawHengeStones(Gfx g, int x, int y)
    {
      // 石を奥から手前へ。まぐさ石は両端の柱のあと (手前のほう) に描く
      var items = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<float, Action>>();
      int[] rx = new int[SARSENS], ry = new int[SARSENS];
      for (int i = 0; i < SARSENS; i++)
      {
        float a = i * (2 * PI / SARSENS) + 0.1f;
        rx[i] = x + (int)Math.Round(cosf(a) * HR_X);
        ry[i] = y + (int)Math.Round(sinf(a) * HR_Y);
      }
      forEachStone(x, y, (bx, by, w, h) => items.Add(new System.Collections.Generic.KeyValuePair<float, Action>(by, () => upright(g, bx, by, w, h))));
      for (int i = 0; i < SARSENS; i++)
      {
        int j = (i + 1) % SARSENS;
        if (SARSEN_GONE[i] || UPRIGHT_GONE[i] || UPRIGHT_GONE[j])
          continue;
        int x0 = rx[i], y0 = ry[i] - 16, x1 = rx[j], y1 = ry[j] - 16;
        items.Add(new System.Collections.Generic.KeyValuePair<float, Action>(Math.Max(ry[i], ry[j]) + 0.5f, () => lintel(g, x0, y0, x1, y1)));
      }
      for (int k = 0; k < TRILITHON.Length; k++)
      {
        float a = TRILITHON[k], tx = -sinf(a), ty = cosf(a) * 0.5f, tl = sqrtf(tx * tx + ty * ty);
        tx /= tl;
        ty /= tl;
        int cx = x + (int)Math.Round(cosf(a) * 18), cy = y + (int)Math.Round(sinf(a) * 9), h = k == 2 ? 27 : 23;
        int ax0 = cx - (int)Math.Round(tx * 4.5f), ay0 = cy - (int)Math.Round(ty * 4.5f) - h, ax1 = cx + (int)Math.Round(tx * 4.5f), ay1 = cy + (int)Math.Round(ty * 4.5f) - h;
        items.Add(new System.Collections.Generic.KeyValuePair<float, Action>(cy + 0.5f, () => lintel(g, ax0, ay0, ax1, ay1)));
      }
      // 寝ている石 (祭壇石と倒れた石) はいちばん先に
      items.Add(new System.Collections.Generic.KeyValuePair<float, Action>(-9999, () =>
      {
        g.FillRect(x - 4, y - 1, 8, 3, STONE_FACE);
        g.DrawFastHLine(x - 4, y - 1, 8, STONE_TOP);
        g.FillRect(x + 24, y + 15, 15, 5, STONE_FACE);
        g.DrawFastHLine(x + 24, y + 15, 15, STONE_TOP);
        g.DrawFastHLine(x + 24, y + 19, 15, STONE_EDGE);
        g.FillRect(x - 38, y + 9, 13, 5, STONE_FACE);
        g.DrawFastHLine(x - 38, y + 9, 13, STONE_TOP);
        g.DrawFastHLine(x - 38, y + 13, 13, STONE_EDGE);
      }));
      items.Sort((ia, ib) => ia.Key.CompareTo(ib.Key));
      foreach (var it in items)
        it.Value();
    }

    // ネッシー (あの有名な写真のように): 湖面から黒褐色の長い首がすっと立ち、先の小さな頭がうつむく。うしろに低いこぶ。
    // 水面には揺れる映り込みと波紋。沈んでいるあいだは泡と、水の下の暗い影だけ
    float[] nessU = new float[17], nessV = new float[17], nessR = new float[17];
    bool nessieAt(float u, float v, float e) // 横から見た形の内側か (u: 前向きを正にした横、v: 水面からの高さ)
    {
      if (v < 0)
        return false;
      float hu = (u + 7) / 12.0f; // こぶ
      if (hu > -1 && hu < 1 && v <= 6 * e * sqrtf(1 - hu * hu))
        return true;
      for (int k = 0; k < nessU.Length; k++) // 首
      {
        float du = u - nessU[k], dv = v - nessV[k];
        if (du * du + dv * dv <= nessR[k] * nessR[k])
          return true;
      }
      float qu = (u - 9.5f) / 3.4f, qv = (v - (26 * e - 1.2f)) / 1.9f; // 頭 (少しうつむく)
      return qu * qu + qv * qv <= 1;
    }

    void drawNessie(Gfx g, Prop p, int x, int y)
    {
      float e = p.lift;
      int dir = p.v != 0 ? 1 : -1;
      if (e <= 0.02f)
      {
        for (int j = -4; j <= 4; j++)
          for (int i = -14; i <= 14; i++)
            if (i * i / 196.0f + j * j / 16.0f < 1)
              g.BlendPixel(x + i, y + j, 0x1E2A30, 55);
        if ((frame / 6) % 3 == 0)
          for (int k = 0; k < 4; k++)
          {
            uint h = ph(frame / 6 + k, p.t / 40);
            g.DrawPixel(x + (int)(h % 15) - 7, y + (int)((h >> 4) % 7) - 3, 0xD8ECF8);
          }
        return;
      }
      // 首の中心線 (ベジェ曲線): 根もとから少しうしろへ反ってから、前へ曲がって頭へ
      for (int k = 0; k < nessU.Length; k++)
      {
        float s = k / (float)(nessU.Length - 1), m = 1 - s;
        float u0 = 2, v0 = 0, u1 = -1.5f, v1 = 12 * e, u2 = 1.5f, v2 = 25 * e, u3 = 7, v3 = 27 * e;
        nessU[k] = m * m * m * u0 + 3 * m * m * s * u1 + 3 * m * s * s * u2 + s * s * s * u3;
        nessV[k] = m * m * m * v0 + 3 * m * m * s * v1 + 3 * m * s * s * v2 + s * s * s * v3;
        nessR[k] = 3.4f - 1.7f * s;
      }
      const uint DARK = 0x281C12, BODY = 0x42301F, RIM = 0x76583A;
      // 波紋: 首の根もととこぶのまわりに、つぶれた輪
      int[] ringU = { 2, -7 };
      for (int r = 0; r < 2; r++)
        for (int w = 0; w < 3; w++)
        {
          float rx_ = 7 + w * 4 + ((frame / 6 + r * 2) % 4), ry_ = rx_ * 0.28f;
          for (int s = 0; s < 40; s++)
          {
            float a = s * (2 * PI / 40);
            g.BlendPixel(x + dir * ringU[r] + (int)(cosf(a) * rx_), y + 1 + (int)(sinf(a) * ry_), 0xE0F0FA, 120 - w * 35);
          }
        }
      // 映り込み: 水面の下に、上下さかさまで揺れる暗い形
      for (int v = 1; v <= 30; v++)
      {
        float wave = sinf(v * 0.9f + frame * 0.25f) * 1.3f;
        int a = (int)(150 * (1 - v / 31.0f));
        for (int u = -20; u <= 14; u++)
          if (nessieAt(u + wave, v, e))
            g.BlendPixel(x + dir * u, y + v, 0x1A140E, a);
      }
      // 本体: 黒褐色のシルエット。上の縁に光が当たり、水ぎわは暗い
      for (int v = 0; v <= 30; v++)
        for (int u = -20; u <= 14; u++)
        {
          if (!nessieAt(u, v, e))
            continue;
          uint c = v <= 1 ? DARK : BODY;
          if (!nessieAt(u, v + 1, e) || !nessieAt(u - 1, v + 1, e))
            c = RIM;
          else if ((ph(u + 40, v + p.t / 90) & 7) == 0)
            c = DARK;
          g.DrawPixel(x + dir * u, y - v, c);
        }
      // 水ぎわの白い泡
      for (int u = -20; u <= 14; u++)
        if (nessieAt(u, 0, e) && !nessieAt(u - 1, 0, e) || nessieAt(u, 0, e) && !nessieAt(u + 1, 0, e))
          g.DrawPixel(x + dir * u, y + 1, 0xF0F8FC);
      // 目 (頭が出ているときだけ)
      if (e > 0.85f)
        g.DrawPixel(x + dir * 10, y - (int)(26 * e), 0xC8B890);
    }

    // ミステリーサークル: 倒れた麦で描いた模様 (真ん中の円、外の輪、3 つの小さな円とそれをつなぐ道)。
    // 倒れた麦は白っぽく、渦の向きに筋が見える。円盤が描いているあいだは、ぐるりと少しずつあらわれる
    const int CROP_DRAW = 170; // 描きおわるまでのフレーム数
    static bool cropShape(float dx, float dy)
    {
      float d = sqrtf(dx * dx + dy * dy);
      if (d < 8.5f || (d > 17.5f && d < 21))
        return true;
      for (int k = 0; k < 3; k++)
      {
        float a = -PI / 2 + k * (2 * PI / 3), cx = cosf(a) * 29, cy = sinf(a) * 29;
        float ex = dx - cx, ey = dy - cy;
        if (ex * ex + ey * ey < 5.5f * 5.5f)
          return true;
        // 輪から小さな円への道 (幅 2)
        float t = (dx * cosf(a) + dy * sinf(a)), side = Math.Abs(-dx * sinf(a) + dy * cosf(a));
        if (t > 20 && t < 25 && side < 1.3f)
          return true;
        // 小さな円のまわりの点
        for (int m = -1; m <= 1; m += 2)
        {
          float b = a + m * 0.42f, px2 = cosf(b) * 31, py2 = sinf(b) * 31;
          if ((dx - px2) * (dx - px2) + (dy - py2) * (dy - py2) < 2.2f * 2.2f)
            return true;
        }
      }
      return false;
    }
    void drawCropCircle(Gfx g, Prop p, int x, int y)
    {
      float reveal = Math.Min(1, p.t / (float)CROP_DRAW);
      for (int j = -38; j <= 38; j++)
        for (int i = -38; i <= 38; i++)
        {
          if (!cropShape(i, j))
            continue;
          float a = atan2f(j, i) + PI / 2;
          if (a < 0) a += 2 * PI;
          if (a / (2 * PI) > reveal)
            continue;
          float d = sqrtf(i * i + j * j);
          bool streak = sinf(atan2f(j, i) * 7 + d * 0.9f) > 0.3f; // 渦の筋
          g.DrawPixel(x + i, y + j, streak ? 0xE8D898u : 0xF8F0C8u);
          if (!cropShape(i, j - 1)) // 倒れたところの上の縁に、立っている麦の影
            g.DrawPixel(x + i, y + j, 0xB89A50);
        }
    }

    // サークルを描く円盤: 高いところ (小さく、影が遠い) を飛び、描いている点に光を落とす。描きおわると飛び去る。撃っても当たらない飾り
    void drawCropSaucer(Gfx g, Prop p, int x, int y)
    {
      int t = p.t;
      if (t > CROP_DRAW + 60)
        return;
      float reveal = Math.Min(1, t / (float)CROP_DRAW), a = -PI / 2 + reveal * 2 * PI;
      float rr = reveal < 1 ? 20 + 9 * sinf(t * 0.21f) : 0;
      int gx = x + (int)(cosf(a) * rr), gy = y + (int)(sinf(a) * rr);
      int sx = gx, sy = gy - 10 - (t > CROP_DRAW ? (t - CROP_DRAW) * 6 : 0);
      if (reveal < 1) // 地面の光の点
        for (int j = -4; j <= 4; j++)
          for (int i = -4; i <= 4; i++)
            if (i * i + j * j <= 16)
              g.BlendPixel(gx + i, gy + j, 0xF8FFD0, 150 - (i * i + j * j) * 8);
      for (int j = -3; j <= 3; j++) // 遠くに落ちる影
        for (int i = -5; i <= 5; i++)
          if (i * i / 25.0f + j * j / 9.0f < 1)
            g.BlendPixel(gx + 16 + i, gy + 18 + j, SHADE, 60);
      if (sy > -20)
        drawSaucer(g, sx, sy, 6, false, t / 4);
    }

    // 赤いトタンの納屋。手前に板壁と、筋かいの入った大きな戸
    void drawBarn(Gfx g, int x, int y)
    {
      int x0 = x - 11, y0 = y - 8;
      for (int j = 0; j < 13; j++)
        for (int i = 0; i < 21; i++)
        {
          bool upper = j < 6;
          uint c = (i & 1) == 0 ? (upper ? 0xB05A40u : 0x9A4E36u) : (upper ? 0x9A4E36u : 0x7A3A2Au);
          if (j == 5) c = 0xC06A4E;
          if (j == 6) c = 0x5A2A1E;
          if (j == 12 || i == 20) c = mulc(c, 0.7f);
          g.DrawPixel(x0 + i, y0 + j, c);
        }
      g.FillRect(x0, y0 + 13, 21, 3, 0x7A4A2A);
      for (int i = 0; i < 21; i += 3)
        g.DrawFastVLine(x0 + i, y0 + 13, 3, 0x5A3A22);
      g.FillRect(x0 + 7, y0 + 13, 7, 3, 0x4A2E1A);
      g.DrawLine(x0 + 7, y0 + 13, x0 + 13, y0 + 15, 0x8A6A44);
      g.DrawLine(x0 + 7, y0 + 15, x0 + 13, y0 + 13, 0x8A6A44);
    }

    // 細長い身廊 (棟が縦に通る屋根) と、手前 (下) の四角い塔。塔は胸壁と四角錐の尖塔、てっぺんに金の十字。わきに墓石
    void drawChurch(Gfx g, int x, int y)
    {
      int nx = x - 5, ny = y - 14;
      for (int j = 0; j < 21; j++)
        for (int i = 0; i < 11; i++)
        {
          uint c = i < 5 ? 0x969AA2u : i == 5 ? 0x5A5E66u : 0x6E727Au;
          if (j % 3 == 2 && i != 5)
            c = mulc(c, 0.85f);
          if (j == 0 || i == 10)
            c = 0x5A5E66;
          g.DrawPixel(nx + i, ny + j, c);
        }
      int tx = x - 6, ty = y + 5;
      for (int j = 0; j < 12; j++)
        for (int i = 0; i < 13; i++)
        {
          uint c;
          if (i < 2 || i > 10 || j < 2 || j > 9)
            c = ((i + j) & 1) == 0 ? 0xC8C4B6u : 0x8A867Au; // 胸壁
          else
          {
            int dx = i - 6, dy = j - 6;
            if (Math.Abs(dx) > Math.Abs(dy))
              c = dx < 0 ? 0x9A968Au : 0x5A564Cu;
            else
              c = dy < 0 ? 0x86827Au : 0x66625Au;
          }
          if (j == 11 || i == 12)
            c = mulc(c, 0.7f);
          g.DrawPixel(tx + i, ty + j, c);
        }
      g.DrawFastHLine(x - 1, y + 11, 3, GOLD);
      g.DrawFastVLine(x, y + 10, 3, GOLD);
      g.DrawPixel(x, y + 11, 0xFFF0B0);
      for (int k = 0; k < 4; k++)
      {
        int gx = x + 10 + (k & 1) * 4, gy = y - 6 + k * 5;
        g.FillRect(gx, gy, 2, 2, 0xB8B8B0);
        g.DrawPixel(gx + 2, gy + 1, 0x4A4A40);
        g.DrawPixel(gx + 1, gy + 2, 0x4A4A40);
      }
    }

    // 丸い樹冠。左上が明るく、葉の粒でざらつかせる
    static readonly uint[] TREE_TONES = { 0x203E1A, 0x2E5A26, 0x3A6A2E, 0x528638, 0x74A84C };
    void drawTree(Gfx g, int x, int y, int r, int idx)
    {
      for (int j = -r; j <= r; j++)
        for (int i = -r; i <= r; i++)
        {
          float d = sqrtf(i * i + j * j);
          uint h = ph(i + idx * 37, j + idx * 11);
          if (d > r - ((h & 1) != 0 ? 0.7f : 0))
            continue;
          float s = (-i * 0.6f - j * 0.8f) / r + 0.35f * (1 - d / r);
          int k = s < -0.55f ? 0 : s < -0.1f ? 1 : s < 0.3f ? 2 : s < 0.65f ? 3 : 4;
          if ((h % 5) == 0 && k > 0) k--;
          else if ((h % 7) == 0 && k < 4) k++;
          g.DrawPixel(x + i, y + j, region == 2 && idx % 3 != 0 ? TREE_AUTUMN[k] : TREE_TONES[k]);
        }
    }

    // 母船の残骸: 焼けた地面にえぐれた跡。真ん中に割れて傾いた母船、跳ねた土と散らばった板
    void drawWreck(Gfx g, int x, int y)
    {
      g.FillEllipse(x, y, 45, 30, 0x3A2E1E);
      g.FillEllipse(x, y, 36, 22, 0x2A2014);
      g.FillEllipse(x, y + 2, 30, 18, 0x22180E);
      for (int i = 0; i < 16; i++)
      {
        float a = i * 0.39f + 0.3f;
        int rr = 46 + (int)(ph(i, 9) % 6);
        g.FillRect(x + (int)(cosf(a) * rr), y + (int)(sinf(a) * rr * 0.68f), 3, 2, (i & 1) != 0 ? 0x6A5232u : 0x5A4428u);
      }
      g.FillEllipse(x + 3, y + 2, 30, 16, 0x3A3E46);
      g.FillEllipse(x + 2, y + 1, 28, 14, 0x4A4E56);
      g.FillEllipse(x - 3, y - 3, 24, 11, 0x6E727A);
      g.FillEllipse(x - 4, y - 4, 18, 7, 0x868A92);
      g.FillEllipse(x - 6, y - 1, 8, 6, 0x1E3236); // 割れたドーム
      g.FillEllipse(x - 8, y - 3, 3, 2, 0x4A8A90);
      g.DrawLine(x - 27, y - 6, x + 9, y + 12, 0x22180E);
      g.DrawLine(x + 6, y - 13, x + 24, y + 3, 0x22180E);
      g.DrawLine(x - 12, y - 13, x - 3, y + 3, 0x22180E);
      for (int i = 0; i < 6; i++) // 縁のこわれた灯り
      {
        float a = i * 1.05f + 0.5f;
        g.FillRect(x + 2 + (int)(cosf(a) * 27), y + 1 + (int)(sinf(a) * 14), 2, 2, 0x5A2A2A);
      }
      g.FillTriangle(x + 30, y - 18, x + 38, y - 22, x + 36, y - 12, 0x6A6E76);
      g.FillTriangle(x - 36, y + 14, x - 28, y + 20, x - 38, y + 22, 0x5A5E66);
    }

    // ---- 自機・円盤・母船 ----
    void drawPlane(Gfx g, int x, int y)
    {
      if (!visible(y - 14, y + 14))
        return;
      g.Blit(PLANE, x, y, false);
      if ((frame / 2) % 2 != 0) // 回るプロペラ
      {
        g.DrawFastHLine(x - 6, y - 11, 13, 0xD8D8D0);
        g.DrawPixel(x - 7, y - 11, 0x9A9A94);
        g.DrawPixel(x + 7, y - 11, 0x9A9A94);
      }
      else
      {
        g.DrawLine(x - 5, y - 13, x + 5, y - 9, 0xB8B8B0);
        g.DrawLine(x - 4, y - 9, x + 4, y - 13, 0x8A8A84);
      }
    }

    // 上から見た円盤: 縁は暗い金物、皿は左上が光り、放射状の継ぎ目が回る。真ん中にガラスのドーム。縁の灯りが回る
    void drawSaucer(Gfx g, int x, int y, float R, bool flash, int phase)
    {
      int ri = (int)Math.Ceiling(R);
      float rot = frame * 0.02f + phase;
      for (int dy = -ri; dy <= ri; dy++)
        for (int dx = -ri; dx <= ri; dx++)
        {
          float d = sqrtf(dx * dx + dy * dy);
          if (d > R + 0.3f)
            continue;
          float t = d / R, lit = (-dx * 0.6f - dy * 0.8f) / R;
          uint c;
          if (t > 0.94f)
            c = UFO_K;
          else if (t > 0.84f)
            c = litc(0x7A7E8A, lit);
          else if (t > 0.79f)
            c = 0x3A3E48;
          else if (t > 0.42f)
          {
            float a = (atan2f(dy, dx) - rot) / (2 * PI) * 8;
            c = litc(0xB8BCC8, lit * 1.4f);
            if (a - floorf(a) < 0.08f)
              c = mulc(c, 0.75f);
          }
          else if (t > 0.34f)
            c = 0x4A505C;
          else
          {
            c = mix(0x2A8A9A, 0x9AEEF4, 0.45f + 0.5f * lit);
            if (dx < 0 && dy < 0 && dx * dx + dy * dy < R * R * 0.04f)
              c = DOME_L;
          }
          if (flash)
            c = mix(c, 0xFFFFFF, 0.75f);
          g.DrawPixel(x + dx, y + dy, c);
        }
      for (int i = 0; i < 6; i++)
      {
        float a = frame * 0.15f + phase + i * (PI / 3);
        int lx = x + (int)Math.Round(cosf(a) * R * 0.89f), ly = y + (int)Math.Round(sinf(a) * R * 0.89f);
        uint rc = RIM[(i + frame / 8) % 4];
        g.DrawPixel(lx, ly, mix(rc, 0xFFFFFF, 0.5f));
        g.BlendPixel(lx + 1, ly, rc, 160);
        g.BlendPixel(lx, ly + 1, rc, 160);
      }
    }

    void drawUfo(Gfx g, Ufo u)
    {
      int x = S(u.rx), y = S(u.ry);
      float R = UFO_R[u.type] * K;
      if (u.t < 0 || !visible(y - 18, y + 45))
        return;
      if (u.type == ABDUCTOR && u.state == 1 && u.prop >= 0)
      {
        // 光線: 円盤から羊へ広がる光の帯。半透明で、ちらちら揺れる
        Prop p = props[u.prop];
        int ty = S(p.ry) - (int)(p.lift * 30), tx = S(p.rx), top = y + (int)R - 3;
        for (int yy = top; yy <= ty + 3; yy++)
        {
          float f = (yy - top) / Math.Max(1.0f, ty + 3 - top);
          float cx = x + (tx - x) * f, hw = 3 + 7 * f;
          for (int xx = (int)(cx - hw); xx <= (int)(cx + hw); xx++)
          {
            float e = 1 - Math.Abs(xx - cx) / hw;
            int a = (int)(e * 120) + (((yy + frame) / 2) % 3 == 0 ? 40 : 0);
            g.BlendPixel(xx, yy, BEAM, Math.Min(220, Math.Max(0, a)));
          }
        }
      }
      if (u.rise < 1)
      {
        drawRising(g, u, x, y, R);
        return;
      }
      if (u.type >= CARRIER)
      {
        drawBeam(g, u, x, y);
        drawUfoArms(g, u, x, y, R);
        return;
      }
      drawSaucer(g, x, y, R, u.flash > 0, (int)(u.a * 3) + u.type);
    }

    // 出てくる途中の円盤
    void drawRising(Gfx g, Ufo u, int x, int y, float R)
    {
      float rs = u.rise;
      if (u.riseKind == 1)
      {
        if (rs < 0.45f) // 湖: まず水の下に黒い影が大きくなり、泡が立つ
        {
          float k = rs / 0.45f, rr = R * (0.5f + 0.5f * k);
          for (int j = -(int)rr; j <= (int)rr; j++)
            for (int i = -(int)rr; i <= (int)rr; i++)
              if (i * i + j * j * 2 < rr * rr)
                g.BlendPixel(x + i, y + j, 0x101C24, (int)(70 + 110 * k));
          for (int k2 = 0; k2 < 5; k2++)
          {
            uint h = ph(frame / 4 + k2 * 13, u.t);
            g.DrawPixel(x + (int)(h % (uint)(rr * 2 + 1)) - (int)rr, y + (int)((h >> 5) % (uint)(rr + 1)) - (int)(rr / 2), 0xE0F0FA);
          }
          return;
        }
        // 水を割って浮上: 広がる水しぶきの輪と、円盤から落ちるしずく
        float k3 = (rs - 0.45f) / 0.55f;
        int ring = (int)(R + 4 + k3 * 14);
        for (int s = 0; s < 40; s++)
        {
          float a = s * (2 * PI / 40);
          g.BlendPixel(x + (int)(cosf(a) * ring), y + 3 + (int)(sinf(a) * ring * 0.45f), 0xF0F8FC, (int)(200 * (1 - k3)));
        }
        drawSaucer(g, x, y, R * (0.75f + 0.25f * k3), u.flash > 0, (int)(u.a * 3) + u.type);
        for (int k2 = 0; k2 < 8; k2++)
        {
          uint h = ph(k2 * 7, u.t / 3);
          g.DrawPixel(x + (int)(h % (uint)(R * 2)) - (int)R, y + (int)R - 2 + (int)((h >> 6) % 6) + (int)(k3 * 6), 0xC8E4F4);
        }
        return;
      }
      // ストーンヘンジ: 緑の光の中から、小さく (低く) 現れて大きく (高く) なる
      for (int j = -(int)(R * 1.6f); j <= (int)(R * 1.6f); j++)
        for (int i = -(int)(R * 1.6f); i <= (int)(R * 1.6f); i++)
        {
          float q = (i * i + j * j) / (R * R * 2.56f);
          if (q < 1)
            g.BlendPixel(x + i, y + j, 0xA8FFC0, (int)(130 * (1 - q) * (1 - rs * 0.7f)));
        }
      drawSaucer(g, x, y, R * (0.35f + 0.65f * rs), false, (int)(u.a * 3) + u.type);
    }

    // 母船: 縁の灯り、回る区画と細かい機械、明るい環、通気口の環、光るドーム。4 つの砲口が回る。
    // 落ちていくあいだは小さくなって傾き (楕円にゆがみ)、焦げて灯りがちらつき、割れ目が入る
    void drawMom(Gfx g)
    {
      float alt = momAlt(), s = 0.65f + 0.35f * alt, k = mom.dead ? 0.8f + 0.2f * sinf(frame * 0.35f) : 1.0f;
      int cx = S(mom.rx), cy = S(mom.ry);
      const float R = 54;
      int rx = (int)(R * s) + 1, ry = (int)(R * s * k) + 1;
      if (!visible(cy - ry, cy + ry))
        return;
      bool lights = !mom.dead;
      float burn = mom.dead ? 0.25f + 0.45f * (1 - alt) : 0, rot = mom.t * 0.01f;
      for (int py_ = -ry; py_ <= ry; py_++)
        for (int px_ = -rx; px_ <= rx; px_++)
        {
          float u = px_ / s, v = py_ / (s * k), d = sqrtf(u * u + v * v), t = d / R;
          if (t > 1)
            continue;
          float lit = (-u * 0.6f - v * 0.8f) / R;
          uint c;
          if (t > 0.975f)
            c = UFO_K;
          else if (t > 0.9f)
            c = litc(0xA8ACB8, lit * 1.3f);
          else if (t > 0.86f)
            c = 0x3A3E48;
          else if (t > 0.62f)
          {
            float a = (atan2f(v, u) - rot) / (2 * PI) * 16, fl = floorf(a), sec = a - fl;
            int si = (((int)fl % 16) + 16) % 16;
            c = litc((si & 1) == 0 ? 0xA8ACB8u : 0x9498A6u, lit);
            if (sec < 0.05f || sec > 0.95f)
              c = 0x5A5E6A;
            else if (t > 0.69f && t < 0.79f) // 区画の中の細かい機械
            {
              uint hh = ph(si * 7 + (int)(sec * 4), (int)(t * 40));
              if ((hh & 3) == 0) c = litc(0x6A6E7A, lit);
              else if ((hh & 3) == 1) c = litc(0xC8CCD6, lit);
            }
          }
          else if (t > 0.56f)
            c = litc(0xD0D4DE, lit * 1.2f);
          else if (t > 0.42f)
          {
            float a = (atan2f(v, u) + rot * 2) / (2 * PI) * 36;
            c = a - floorf(a) < 0.3f ? 0x44484Fu : litc(0x6A6E7A, lit);
          }
          else if (t > 0.38f)
            c = UFO_K;
          else if (lights)
          {
            c = mix(DOME, DOME_L, (1 - t / 0.38f) * 0.45f + Math.Max(0, lit) * 0.7f);
            if ((u + 8) * (u + 8) + (v + 8) * (v + 8) < 16)
              c = 0xFFFFFF;
          }
          else
            c = (frame / 3) % 2 != 0 ? 0x2E4A50u : 0x4A8A90u;
          if (burn > 0)
            c = mix(c, 0x2A2A2A, burn);
          else if (mom.hitT > 0 && t > 0.4f)
            c = mix(c, 0xFFFFFF, 0.3f);
          g.DrawPixel(cx + px_, cy + py_, c);
        }
      Func<float, float, int> PX = (a, r) => cx + (int)(cosf(a) * r * s);
      Func<float, float, int> PY = (a, r) => cy + (int)(sinf(a) * r * s * k);
      for (int i = 0; i < 4; i++) // 砲口
      {
        float a = portAngle(i);
        int x = PX(a, 33), y = PY(a, 33), pr = Math.Max(2, (int)(4.5f * s));
        g.FillCircle(x, y, pr, UFO_K);
        g.DrawCircle(x, y, pr, 0x5A5E6A);
        if (lights)
        {
          g.FillCircle(x, y, Math.Max(1, pr - 2), PLASMA_D);
          g.DrawPixel(x, y, (frame / 3) % 2 != 0 ? 0xFFFFFFu : PLASMA);
        }
      }
      if (mom.dead) // 割れ目
        for (int i = 0; i < 3; i++)
        {
          float a = rot + i * 2.2f + 0.4f;
          g.DrawLine(PX(a, 12), PY(a, 12), PX(a + 0.3f, 51), PY(a + 0.3f, 51), 0x141414);
          g.DrawLine(PX(a, 12) + 1, PY(a, 12), PX(a + 0.3f, 51) + 1, PY(a + 0.3f, 51), 0x3A2A20);
        }
      for (int i = 0; i < 24; i++) // 縁の灯り。落ちていくときはところどころちらつくだけ
      {
        if (mom.dead && (i * 7 + frame / 2) % 5 != 0)
          continue;
        float a = -frame * 0.05f + i * (PI / 12);
        int x = PX(a, 50), y = PY(a, 50);
        uint rc = RIM[(i + frame / 6) % 4];
        g.FillRect(x - 1, y - 1, 3, 3, rc);
        g.DrawPixel(x, y, mix(rc, 0xFFFFFF, 0.6f));
      }
    }

    void drawDebris(Gfx g)
    {
      foreach (DebrisP d in debris)
        if (d.on && visible(S(d.ry) - 9, S(d.ry) + 9))
        {
          float r = 3 + 4.5f * d.alt;
          int x = S(d.rx), y = S(d.ry);
          int x0 = x + (int)(cosf(d.ang) * r), y0 = y + (int)(sinf(d.ang) * r);
          int x1 = x + (int)(cosf(d.ang + 2.3f) * r), y1 = y + (int)(sinf(d.ang + 2.3f) * r);
          int x2 = x + (int)(cosf(d.ang + 4.0f) * r), y2 = y + (int)(sinf(d.ang + 4.0f) * r);
          g.FillTriangle(x0, y0, x1, y1, x2, y2, (frame / 3) % 2 != 0 ? SILVER : SILVER_D);
          g.DrawLine(x0, y0, x1, y1, SILVER_L);
          g.BlendPixel(x, y, 0xFF8A3A, 200); // 燃えている
        }
    }

    // 蒸気機関車: 赤い緩衝梁、黒い煙室と煙突、金の帯を巻いた緑のボイラーと蒸気溜め、運転室。石炭を積んだ炭水車、屋根に通風器が並ぶ客車
    void drawTrain(Gfx g)
    {
      int y = S(train.ry);
      if (!train.on || !visible(y - 9, y + 9))
        return;
      int d = train.dir, fx_ = S(train.rx);
      // 先頭から off 点うしろの、長さ len の区間を塗る
      Action<int, int, int, int, uint> seg = (off, len, dy, h, col) =>
      {
        int x0 = d > 0 ? fx_ - off - len : fx_ + off;
        g.FillRect(x0, y + dy, len, h, col);
      };
      Func<int, int> at = off => d > 0 ? fx_ - off - 1 : fx_ + off; // 先頭から off 点目の x
      seg(0, 2, -4, 9, POPPY);
      seg(0, 2, 4, 1, 0x8A2A22);
      seg(2, 5, -3, 7, 0x1A1A1A);
      seg(2, 5, -3, 1, 0x3A3A3A);
      seg(7, 12, -3, 7, 0x2A5A3A);
      seg(7, 12, -3, 1, 0x5A9A6A);
      seg(7, 12, -2, 1, 0x3A7048);
      seg(7, 12, 3, 1, 0x1E4028);
      g.DrawFastVLine(at(10), y - 3, 7, 0xD8B048);
      g.DrawFastVLine(at(15), y - 3, 7, 0xD8B048);
      g.FillCircle(at(4), y, 2, 0x101010);
      g.DrawPixel(at(4), y, 0x000000);
      g.FillCircle(at(13), y, 2, 0xD8B048);
      g.DrawPixel(at(13) - 1, y - 1, 0xF8E090);
      seg(19, 6, -4, 9, 0x1E3A28);
      seg(20, 4, -3, 7, 0x2E5A3C);
      seg(20, 4, 0, 1, 0x1A2A1E);
      seg(26, 10, -3, 8, 0x2A2A2A);
      for (int i = 0; i < 8; i++)
        for (int j = -2; j <= 2; j++)
          if ((ph(i, j + train.cars * 7) & 1) != 0)
            g.DrawPixel(at(27 + i), y + j, 0x101010);
      for (int n = 0; n < train.cars; n++)
      {
        int off = 37 + n * 26;
        seg(off, 24, -4, 1, 0xE8DCC0);
        seg(off, 24, -3, 7, 0x6A6A70);
        seg(off, 24, -1, 1, 0x8A8A92);
        seg(off, 24, 4, 1, 0x7A2A2A);
        for (int i = 2; i < 23; i += 4)
          g.DrawFastHLine(d > 0 ? at(off + i + 1) : at(off + i), y, 2, 0x3A3A40);
        for (int i = 1; i < 23; i += 3)
          g.DrawPixel(at(off + i), y + 4, 0xC8B890);
        g.DrawFastVLine(at(off), y - 4, 9, 0x4A1A1A);
        g.DrawFastVLine(at(off + 23), y - 4, 9, 0x4A1A1A);
      }
    }

    // 雲: 丸を 5 つ重ねる (真ん中、左右、上の 2 つ)。dx, dy はずらし、shrink は半径を小さくする量
    void cloudShape(Gfx g, int x, int y, float R, int dx, int dy, float shrink, uint c)
    {
      float[] b = { 0, 0, 1, -1, 1.5f / R, 0.66f, 1, 3 / R, 0.66f, -0.45f, -0.5f, 0.6f, 0.5f, -0.35f, 0.55f };
      for (int i = 0; i < b.Length; i += 3)
      {
        int rr = (int)(b[i + 2] * R - shrink);
        if (rr > 0)
          g.FillCircle(x + (int)(b[i] * R) + dx, y + (int)(b[i + 1] * R) + dy, rr, c);
      }
    }

    // ---- 色掛けと光 ----
    readonly byte[] lr = new byte[256], lg = new byte[256], lb = new byte[256];
    void tint(Gfx g)
    {
      float rk = rainK(), sk = sunsetK(), dk = duskK(), fl = flashT > 0 ? 0.35f : 0;
      if (rk == 0 && sk == 0 && dk == 0 && fl == 0)
        return;
      float mr = (1 - 0.25f * rk) * (1 - 0.45f * dk), mg = (1 - 0.22f * rk) * (1 - 0.25f * sk) * (1 - 0.5f * dk),
            mb = (1 - 0.12f * rk) * (1 - 0.45f * sk) * (1 - 0.3f * dk);
      float ar = 0.06f * sk + 0.02f * dk + fl, ag = 0.02f * sk + fl, ab = 0.05f * dk + fl;
      int ds = (int)(0.45f * rk * 256); // 雨は色を少し抜く
      for (int i = 0; i < 256; i++)
      {
        lr[i] = (byte)constrain((int)(i * mr + ar * 255 + 0.5f), 0, 255);
        lg[i] = (byte)constrain((int)(i * mg + ag * 255 + 0.5f), 0, 255);
        lb[i] = (byte)constrain((int)(i * mb + ab * 255 + 0.5f), 0, 255);
      }
      int[] buf = g.Buf;
      for (int n = 0; n < buf.Length; n++)
      {
        int v = buf[n];
        int r = (v >> 16) & 0xFF, gg = (v >> 8) & 0xFF, b = v & 0xFF;
        if (ds != 0)
        {
          int yy = (r * 77 + gg * 150 + b * 29) >> 8;
          r += ((yy - r) * ds) >> 8;
          gg += ((yy - gg) * ds) >> 8;
          b += ((yy - b) * ds) >> 8;
        }
        buf[n] = (lr[r] << 16) | (lg[gg] << 8) | lb[b];
      }
    }

    static void dim(Gfx g)
    {
      int[] buf = g.Buf;
      for (int n = 0; n < buf.Length; n++)
        buf[n] = (buf[n] >> 1) & 0x7F7F7F;
    }

    // 灯り: 真ん中が明るく、まわりにぼんやりにじむ
    static void glow(Gfx g, int x, int y, uint c, int size)
    {
      for (int j = -2; j <= size + 1; j++)
        for (int i = -2; i <= size + 1; i++)
        {
          if (i >= 0 && i < size && j >= 0 && j < size)
            g.DrawPixel(x + i, y + j, c);
          else
          {
            int dx = i < 0 ? -i : i >= size ? i - size + 1 : 0, dy = j < 0 ? -j : j >= size ? j - size + 1 : 0;
            if (dx + dy <= 2)
              g.BlendPixel(x + i, y + j, c, dx + dy <= 1 ? 110 : 45);
          }
        }
    }

    // 色を掛けたあとに描く、明るいままの光: 夕暮れの家の灯り、汽車の前照灯、残骸の炎
    void drawLights(Gfx g)
    {
      bool evening = duskK() > 0.25f || sunsetK() > 0.85f;
      foreach (Prop p in props)
      {
        int x = S(p.rx), y = S(p.ry);
        if (!p.on || !visible(y - 30, y + 30))
          continue;
        if (p.type == WRECK)
          for (int i = 0; i < 9; i++)
          {
            float h = hash2(frame / 3 + i * 7, i);
            int fx2 = x - 21 + (int)(h * 42), fy = y - 12 + (int)(hash2(i, frame / 3) * 24);
            g.FillRect(fx2, fy, 2, 3, h < 0.5f ? 0xFF8A2Au : GOLD);
            g.BlendPixel(fx2, fy - 1, 0xFFE070, 160);
          }
        else if (evening && p.type == COTTAGE)
        {
          glow(g, x - 5, y + 6, 0xFFD070, 2);
          glow(g, x + 4, y + 6, 0xFFD070, 2);
        }
        else if (p.type == BEAST && p.t >= 60 && p.t < 130) // 座った野獣の金色の目
        {
          for (int s = 0; s < 2; s++)
          {
            int ex = x - 3 + s * 4, ey = y - 9;
            for (int j = -2; j <= 2; j++)
              for (int i = -2; i <= 3; i++)
                g.BlendPixel(ex + i, ey + j, 0xF0D850, Math.Max(0, 90 - (i * i + j * j) * 18));
            g.FillRect(ex, ey, 2, 1, 0xFFF4A0);
          }
        }
        else if (p.type == BEAST && p.kind == 1 && p.t >= 60 && p.t < 130) // 座ったブラック・シャックの赤い目
          shuckEyes(g, x - 3, y - 8, 4);
        else if (evening && p.type == LIGHTHOUSE) // 灯台の光が回る
          lighthouseBeam(g, x, y);
        else if (evening && p.type == CHURCH)
          glow(g, x - 6, y + 2, 0xFFD070, 1);
        else if ((evening || sunsetK() > 0.55f) && p.type == PUB) // パブはほかの家より早く灯りがともる
        {
          // 窓は明るく、看板の上のランプが前の道を照らす
          int x0 = x - 11, y0 = y - 8, sx = p.v != 0 ? x0 + 25 : x0 - 7;
          int[] wins = { 2, 6, 14, 18 };
          foreach (int wx in wins)
            glow(g, x0 + wx, y0 + 13, 0xFFC860, 2);
          for (int j = -6; j <= 8; j++)
            for (int i = -9; i <= 9; i++)
            {
              float d = (i * i) / 81.0f + (j * j) / 49.0f;
              if (d < 1)
                g.BlendPixel(sx + 3 + i, y0 + 22 + j, 0xFFD890, (int)(70 * (1 - d)));
            }
          glow(g, sx + 2, y0 + 12, 0xFFF0B0, 2);
        }
      }
      if (evening && train.on && visible(S(train.ry) - 4, S(train.ry) + 4))
      {
        int hx = train.dir > 0 ? S(train.rx) : S(train.rx) - 2;
        glow(g, hx, S(train.ry) - 1, 0xFFF0B0, 2);
        for (int k = 1; k < 14; k++) // 前を照らす光
          for (int j = -k / 3 - 1; j <= k / 3 + 1; j++)
            g.BlendPixel(hx + train.dir * (k + 2), S(train.ry) + j, 0xFFF0B0, 70 - k * 4);
      }
    }

    // ---- 文字と札 ----
    // 影つきの文字。2 倍以上の大きな文字は、まわりを濃い緑で縁どる
    void txt(Gfx g, int x, int y, uint c, string s, int datum = Datum.TL, int size = 1)
    {
      if (size >= 2)
      {
        for (int dy = -1; dy <= 2; dy++)
          for (int dx = -1; dx <= 2; dx++)
            if (dx != 0 || dy != 0)
              g.Text(x + dx, y + dy, INKG, s, datum, size);
      }
      else
        g.Text(x + 1, y + 1, INKG, s, datum, size);
      g.Text(x, y, c, s, datum, size);
    }
    void banner(Gfx g, int y, uint c, string s, int size = 3)
    {
      txt(g, SW / 2, y, c, s, Datum.TC, size);
    }
    // 絵はがき風の枠: 紙の繊維、2 重の線、角の飾り、落ちる影
    void postcard(Gfx g, int y, int h)
    {
      int x0 = 12, w = SW - 24;
      g.FillRect(x0 + 3, y + 3, w, h, 0x1E2A14);
      for (int j = 0; j < h; j++)
        for (int i = 0; i < w; i++)
        {
          uint hh = ph(i * 3 + 7, j * 5 + 1);
          g.DrawPixel(x0 + i, y + j, (hh & 31) == 0 ? 0xE4D8B6u : (hh & 63) == 1 ? 0xF8F0D8u : PAPER);
        }
      g.DrawRect(x0 + 4, y + 4, w - 8, h - 8, DEEP);
      g.DrawRect(x0 + 5, y + 5, w - 10, h - 10, DEEP);
      g.DrawRect(x0 + 8, y + 8, w - 16, h - 16, POPPY);
      int[] cxs = { x0 + 8, x0 + w - 9 }, cys = { y + 8, y + h - 9 };
      foreach (int cx in cxs)
        foreach (int cy in cys)
        {
          g.FillRect(cx - 1, cy - 1, 3, 3, POPPY);
          g.DrawPixel(cx, cy, GOLD);
        }
    }

    void drawTitle(Gfx g)
    {
      int y0 = 112;
      postcard(g, y0, 280);
      // ロゴ: 横に 4 つに切って、上から空色 → 緑。丘の上の空のつもり。縁どりと、帯ごとの照り
      const string logo = "SKYLARK";
      uint[] GRAD = { 0x5AA0D8, 0x3A80C0, 0x5A9A3A, 0x3A7A2A };
      int ly = y0 + 22;
      for (int dy = -2; dy <= 3; dy++)
        for (int dx = -2; dx <= 3; dx++)
          g.Text(SW / 2 + dx, ly + dy, dx > 1 || dy > 1 ? 0xC8BCA0u : 0x2A3A20u, logo, Datum.TC, 5);
      for (int i = 0; i < 4; i++)
      {
        g.SetClipRect(0, ly + i * 10, SW, 10);
        g.Text(SW / 2, ly, GRAD[i], logo, Datum.TC, 5);
        g.SetClipRect(0, ly + i * 10, SW, 2);
        g.Text(SW / 2, ly, mix(GRAD[i], 0xFFFFFF, 0.35f), logo, Datum.TC, 5);
      }
      g.ClearClipRect();
      drawPostcardScene(g, 26, y0 + 74, SW - 52, 84);
      int ty = y0 + 172;
      g.Text(SW / 2, ty, DEEP, "ARROWS : MOVE   Z : SHOT   X : BOMB", Datum.TC);
      g.Text(SW / 2, ty + 14, BROWN, "SHIFT : SLOW    ESC : PAUSE", Datum.TC);
      g.Text(SW / 2, ty + 28, BROWN, "F11 : FULL   F2 : VIEW   M : SOUND", Datum.TC);
      g.Text(SW / 2, ty + 50, BROWN, "(C) 2026", Datum.TC);
      g.Text(SW / 2, ty + 66, DEEP, "MEADOW VALE  -  HIGH FELLS", Datum.TC);
      if ((frame / 15) % 2 != 0)
        banner(g, y0 + 296, GOLD, "PUSH START", 2);
      txt(g, SW / 2, y0 + 322, CREAM, "ENTER / Z / START BUTTON", Datum.TC);
    }

    // 絵はがきの小さな風景: 空の色の移り、日、遠い丘と近い丘、生け垣の線、丘の上の教会と家、羊、飛んでいく飛行機
    void drawPostcardScene(Gfx g, int x0, int y0, int w, int h)
    {
      g.SetClipRect(x0, y0, w, h);
      for (int j = 0; j < h; j++)
        g.DrawFastHLine(x0, y0 + j, w, mix(0x9ACCEA, 0xEAF4F2, (float)j / h));
      int sunx = x0 + w - 40, suny = y0 + 20;
      g.FillCircle(sunx, suny, 9, 0xFFF4C8);
      g.FillCircle(sunx, suny, 7, 0xFFE89A);
      for (int k = 0; k < 2; k++) // 小さな雲
      {
        int cx = x0 + 30 + k * 110 + (frame / 8 + k * 40) % 30, cy = y0 + 14 + k * 6;
        g.FillCircle(cx, cy, 5, 0xFFFFFF);
        g.FillCircle(cx + 6, cy + 1, 4, 0xFFFFFF);
        g.FillCircle(cx - 6, cy + 2, 3, 0xF4F8FA);
      }
      g.FillEllipse(x0 + w * 3 / 4, y0 + h + 10, w / 2, 44, 0x9CC488); // 遠い丘と、その生け垣
      g.FillEllipse(x0 + w / 4, y0 + h + 18, w / 2, 46, 0x86BC5C);
      for (int i = 0; i < w; i += 2)
      {
        double e = (i - w * 3 / 4.0) / (w / 2.0);
        int yy = y0 + h + 20 - (int)(44 * Math.Sqrt(Math.Max(0, 1 - e * e)));
        if (yy > y0 && (i / 18) % 2 == 0)
          g.DrawPixel(x0 + i, yy, 0x6A9A50);
      }
      g.FillEllipse(x0 + w * 2 / 3, y0 + h + 30, w / 2 + 20, 42, 0x5A9A3A); // 近い丘
      g.FillEllipse(x0 + 20, y0 + h + 34, w / 3, 40, 0x6AAA44);
      int chx = x0 + w * 3 / 4 + 4, chy = y0 + 44; // 遠い丘の上の教会
      g.FillRect(chx, chy, 10, 6, 0xC8C0B0);
      g.FillRect(chx + 10, chy - 4, 4, 10, 0xB8B0A0);
      g.FillTriangle(chx + 9, chy - 4, chx + 15, chy - 4, chx + 12, chy - 12, 0x7A7E86);
      g.DrawPixel(chx + 12, chy - 13, GOLD);
      int hx = x0 + w / 2 - 10, hy = y0 + 58; // 近い丘の家
      g.FillRect(hx, hy, 12, 6, 0xEDE3C8);
      g.FillTriangle(hx - 2, hy, hx + 14, hy, hx + 6, hy - 6, 0xB08A4A);
      g.FillRect(hx + 5, hy + 2, 2, 4, 0x6A4A2A);
      g.FillRect(hx + 9, hy - 6, 2, 3, 0x8A4A3A);
      int[] trees = { 24, 40, 150, 200 };
      foreach (int tx in trees)
      {
        int ty = y0 + h - 18 - (tx % 7);
        g.FillCircle(x0 + tx, ty, 5, 0x3A6A2E);
        g.FillCircle(x0 + tx - 1, ty - 1, 3, 0x5A8A3E);
      }
      for (int k = 0; k < 5; k++)
      {
        int sx = x0 + 60 + k * 13 + (k & 1) * 4, sy = y0 + h - 10 - (k % 3) * 3;
        g.FillRect(sx, sy, 3, 2, 0xF8F6EE);
        g.DrawPixel(sx + 3, sy, 0x2A2A2A);
      }
      // 飛んでいく飛行機 (横から見たところ)
      int ax = x0 + (frame * 2) % (w + 30) - 20, ay = y0 + 30 + (int)(3 * sinf(frame * 0.05f));
      g.FillRect(ax, ay, 14, 2, 0x4A5A3A);
      g.FillRect(ax + 9, ay - 3, 2, 8, 0x4A5A3A);
      g.FillRect(ax, ay - 2, 2, 3, 0x4A5A3A);
      g.DrawFastVLine(ax + 14, ay - 2 + (frame & 1), 3, 0x8A8A84);
      g.ClearClipRect();
      g.DrawRect(x0 - 1, y0 - 1, w + 2, h + 2, 0x8A7A5A);
    }

    static readonly string[] ORD = { "1ST", "2ND", "3RD", "4TH", "5TH" };
    void drawRanking(Gfx g)
    {
      int y0 = 130;
      postcard(g, y0, 270);
      txt(g, SW / 2, y0 + 22, POPPY, "BEST 5", Datum.TC, 3);
      for (int i = 0; i < 5; i++)
      {
        string s = ORD[i] + " " + Table[i].Score.ToString("D6") + " " + Table[i].Name.PadRight(3);
        bool mineRow = i == newRank;
        if (!mineRow || (frame / 8) % 2 != 0)
          g.Text(SW / 2, y0 + 70 + i * 30, mineRow ? POPPY : i == 0 ? DEEP : BROWN, s, Datum.TC, 2);
      }
      g.Text(SW / 2, y0 + 234, DEEP, "YOUR SCORE " + score.ToString("D6"), Datum.TC);
    }

    void drawOverlay(Gfx g)
    {
      if (!playing() || (frame / 16) % 2 != 0)
        txt(g, 8, 4, GOLD, "1UP");
      txt(g, 6, 14, CREAM, score.ToString("D6"), Datum.TL, 2);
      txt(g, SW / 2 + 16, 4, GOLD, "HI-SCORE", Datum.TC);
      txt(g, SW / 2 + 16, 14, CREAM, Math.Max(score, Table[0].Score).ToString("D6"), Datum.TC, 2);
      if (!demo)
      {
        txt(g, SW - 8 - Gfx.TextWidth("DAY", 1), 4, GOLD, "DAY");
        string ds = day.ToString();
        txt(g, SW - 8 - Gfx.TextWidth(ds, 2), 14, CREAM, ds, Datum.TL, 2);
      }
      if (momAlive() && mom.t >= 0)
      {
        int bw = SW - 60, fw = (bw - 2) * mom.hp / mom.maxHp;
        g.FillRect(30, 38, bw, 8, INKG);
        g.FillRect(31, 39, fw, 6, PLASMA);
        g.DrawFastHLine(31, 39, fw, 0xFFB0E8);
        g.DrawFastHLine(31, 44, fw, PLASMA_D);
      }
      if (playing() && !gone)
      {
        for (int i = 0; i < lives && i < 6; i++) // 残りの飛行機
        {
          g.Blit(LIFE, 10 + i * 13 + 1, SH - 13 + 1, false, LIFE_SHADOW);
          g.Blit(LIFE, 10 + i * 13, SH - 13, false);
        }
        g.Blit(SHEEP_S, SW - 46, SH - 10, false); // 助けた羊の数
        txt(g, SW - 34, SH - 18, CREAM, "x" + saved, Datum.TL, 2);
      }

      if (frame < TITLE_END)
        drawTitle(g);
      else if (frame < TITLE_END + 90)
      {
        if (day > 1)
          banner(g, 160, SKY, "DAY " + day, 2);
        banner(g, 188, CREAM, "AREA 1");
        banner(g, 222, GOLD, AREA1_NAME[region], 2);
        if (frame > TITLE_END + 30 && (frame / 8) % 2 != 0)
          banner(g, 252, SKY, "READY", 2);
      }
      if (frame >= AREA2_AT - 30 && frame < AREA2_AT + 75)
      {
        banner(g, 188, CREAM, "AREA 2");
        banner(g, 222, GOLD, AREA2_NAME[region], 2);
      }
      if (mom.on && mom.t < 0 && frame % 30 < 22)
      {
        for (int i = 0; i < 2; i++) // ピンクと白の縞の帯
        {
          int y = 198 + i * 72;
          g.FillRect(0, y, SW, 9, 0xFFFFFF);
          for (int x = -18 + frame % 18; x < SW; x += 18)
            g.FillTriangle(x, y + 8, x + 9, y, x + 18, y + 8, PLASMA);
          g.DrawFastHLine(0, y - 1, SW, PLASMA_D);
          g.DrawFastHLine(0, y + 9, SW, PLASMA_D);
        }
        banner(g, 216, (frame / 8) % 2 != 0 ? PLASMA : CREAM, "WARNING", 4);
        banner(g, 252, CREAM, region == 4 ? "GRAND MOTHERSHIP" : "MOTHERSHIP", 2);
      }
      if (!View25)
        foreach (Pop q in pops)
          if (q.on && (q.t / 4) % 3 != 0)
            txt(g, S(q.rx), S(q.ry), q.col, q.text, Datum.TC, q.text == "SAVED" ? 2 : 1);
      if (extendT > 0 && (frame / 6) % 2 != 0)
        banner(g, SH / 2 + 46, CREAM, "EXTEND", 2);
      if (frame >= CLEAR_AT + 15 && frame < RANK_AT && !gone)
      {
        banner(g, 158, CREAM, "THE VALLEY");
        banner(g, 188, GOLD, "IS SAFE");
        if (frame >= BONUS_AT)
        {
          banner(g, 230, CREAM, "SHEEP SAVED", 2);
          banner(g, 254, CREAM, saved + " x 1000", 2);
        }
      }
      if (frame >= RANK_AT && mode == Mode.Attract)
        drawRanking(g);
      if (mode == Mode.Attract && frame >= TITLE_END && frame < RANK_AT)
      {
        // デモのあいだ: 遊び方の案内
        if ((frame / 20) % 2 != 0)
          banner(g, SH - 160, CREAM, "DEMO PLAY", 2);
        banner(g, SH - 134, GOLD, "PUSH START", 2);
      }

      // 幕: 生け垣の緑に斜めの筋、縁に金の線
      int k = frame < openAt + SHUTTER ? openAt + SHUTTER - frame : frame > LOOP_AT - SHUTTER ? frame - (LOOP_AT - SHUTTER) : 0;
      if (mode == Mode.Intro || mode == Mode.Travel || mode == Mode.Ending) // 絵はがきと地図のあいだは幕を閉じておく
        k = SHUTTER;
      if (k > 0)
      {
        int h = (SH / 2) * k / SHUTTER;
        for (int j = 0; j < h; j++)
          for (int i = 0; i < SW; i++)
          {
            uint c = ((i + j) % 8) < 2 ? 0x2E5A22u : DEEP;
            g.DrawPixel(i, j, c);
            g.DrawPixel(i, SH - 1 - j, c);
          }
        g.FillRect(0, h - 3, SW, 3, GOLD);
        g.DrawFastHLine(0, h - 3, SW, 0xFFF0B0);
        g.FillRect(0, SH - h, SW, 3, GOLD);
        g.DrawFastHLine(0, SH - h + 2, SW, 0xA88A30);
      }
    }

    // 遊びの場面ごとの札 (止めている、コンティニュー、GAME OVER、名前、ランキング)
    void drawModeCard(Gfx g)
    {
      if (Paused)
      {
        banner(g, 200, CREAM, "PAUSE", 4);
        banner(g, 254, GOLD, "ESC : RESUME", 2);
        banner(g, 280, GOLD, "Q : QUIT TO TITLE", 2);
        return;
      }
      switch (mode)
      {
        case Mode.Continue:
          {
            int y0 = 160;
            postcard(g, y0, 210);
            txt(g, SW / 2, y0 + 22, POPPY, "CONTINUE?", Datum.TC, 3);
            string n = (Math.Max(0, modeT) / FPS).ToString();
            g.Text(SW / 2 + 3, y0 + 69, 0xC8BCA0, n, Datum.TC, 9);
            g.Text(SW / 2, y0 + 66, DEEP, n, Datum.TC, 9);
            g.Text(SW / 2, y0 + 158, BROWN, "PUSH SHOT BUTTON", Datum.TC);
            g.Text(SW / 2, y0 + 176, BROWN, "SCORE " + score.ToString("D6"), Datum.TC);
            break;
          }
        case Mode.GameOver:
          banner(g, 228, (frame / 10) % 2 != 0 ? CREAM : GOLD, "GAME OVER", 4);
          banner(g, 274, CREAM, "SCORE " + score.ToString("D6"), 2);
          break;
        case Mode.NameEntry:
          {
            int y0 = 110;
            postcard(g, y0, 300);
            txt(g, SW / 2, y0 + 22, POPPY, "NEW RECORD!", Datum.TC, 3);
            int r = RankOf(score);
            g.Text(SW / 2, y0 + 60, DEEP, (r >= 0 ? ORD[r] : "") + "  " + score.ToString("D6"), Datum.TC, 2);
            g.Text(SW / 2, y0 + 90, BROWN, "ENTER YOUR NAME", Datum.TC);
            for (int i = 0; i < 3; i++)
            {
              int x = SW / 2 - 55 + i * 40;
              bool cur = i == entryPos;
              g.Text(x + 2, y0 + 114, 0xC8BCA0, entry[i].ToString(), Datum.TL, 5);
              g.Text(x, y0 + 112, cur ? POPPY : DEEP, entry[i].ToString(), Datum.TL, 5);
              g.FillRect(x - 2, y0 + 158, 30, 3, cur && (frame / 6) % 2 != 0 ? POPPY : BROWN);
            }
            g.Text(SW / 2, y0 + 186, BROWN, "UP / DOWN : LETTER", Datum.TC);
            g.Text(SW / 2, y0 + 202, BROWN, "SPACE / A : NEXT", Datum.TC);
            g.Text(SW / 2, y0 + 218, BROWN, "OR TYPE YOUR NAME + ENTER", Datum.TC);
            g.Text(SW / 2, y0 + 256, DEEP, "TIME " + Math.Max(0, modeT / FPS), Datum.TC, 2);
            break;
          }
        case Mode.Intro:
          drawIntroCard(g);
          break;
        case Mode.Travel:
          drawTravelCard(g);
          break;
        case Mode.Ending:
          drawEndingCard(g);
          break;
        case Mode.Ranking:
          drawRanking(g);
          break;
      }
    }

    // ---- 1 枚描く ----
    public void Draw(Gfx g) { Draw(g, 1); }

    // alpha: 前のフレームから今のフレームまでのどこを描くか (0〜1)。1 秒 30 回の動きのあいだを補って描く
    public void Draw(Gfx g, float alpha)
    {
      if (View25)
      {
        Draw25(g, alpha);
        return;
      }
      lerpAll(alpha);
      g.UseMask();
      drawGroundRows(g);
      foreach (Prop p in props) // ミステリーサークルと白馬 (地面の模様なので、置き物より先に)
        if (p.on && p.type == CROPCIRCLE && visible(S(p.ry) - 40, S(p.ry) + 40))
          drawCropCircle(g, p, S(p.rx), S(p.ry));
        else if (p.on && p.type == WHITEHORSE && visible(S(p.ry) - 40, S(p.ry) + 40))
          drawWhiteHorse(g, S(p.rx), S(p.ry));
      drawWorldRest(g);
    }

    // 地面 (描き置きを写す) と水面のきらめき (時間で動くので毎回描く)。2.5D の地面にも使う
    void drawGroundRows(Gfx g)
    {
      int sy0 = (int)Math.Floor(rscroll * K);
      for (int r = 0; r < ROWS; r++)
      {
        int y = (r - 1 - FAR) * TS + sy0;
        if (y <= visTop - TS || y >= SH)
          continue;
        if (rowDirty[r])
          renderRow(r);
        for (int j = 0; j < TS; j++)
        {
          int dy = y + j + g.OffY;
          if (dy >= 0 && dy < g.H)
            Array.Copy(rowCache[r], j * SW, g.Buf, dy * SW, SW);
        }
        for (int c = 0; c < COLS; c++)
          if (map[r][c].type == WATER || map[r][c].type == SEA)
          {
            uint h = (uint)(hash2(c, rowId[r]) * 65535);
            if ((rowId[r] * 3 + c + frame / 12) % 5 == 0)
            {
              int wx = c * TS + 1 + (int)(h & 3) * 2, wy = y + 2 + (int)((h >> 2) & 7);
              g.DrawFastHLine(wx, wy, 5, 0x9CC8E8);
              g.DrawFastHLine(wx + 1, wy, 2, 0xD8F0FC);
            }
            if ((rowId[r] + c * 2 + frame / 9) % 7 == 0)
              g.DrawFastHLine(c * TS + 5 - (int)((h >> 5) & 3), y + 8 - (int)((h >> 7) & 3), 3, 0x7AAAD8);
          }
      }
    }

    void drawWorldRest(Gfx g)
    {
      // 置き物の影 → 置き物
      g.MaskMode = true;
      foreach (Prop p in props)
        if (p.on && visible(S(p.ry) - 60, S(p.ry) + 60))
          propShadow(g, p);
      if (train.on)
      {
        int total = 37 + train.cars * 26, tx = S(train.rx), ty = S(train.ry);
        g.FillRect(train.dir > 0 ? tx - total + 3 : tx + 3, ty - 1, total, 9, 0);
      }
      g.MaskMode = false;
      g.ApplyShadow(SHADE, 100);
      for (int i = 0; i < props.Length; i++)
        if (props[i].on)
          drawProp(g, props[i], i);
      drawTrain(g);
      foreach (Puff q in puffs) // 湯気は白く、煙は黒っぽく。だんだん薄れて小さくなる
      {
        int x = S(q.rx), y = S(q.ry);
        if (!q.on || !visible(y - 12, y + 12))
          continue;
        float f = (float)q.t / q.life;
        uint c = q.smoke ? mix(0x3A3A3A, 0x9A9A9A, f) : mix(0xF4F4F4, 0xC8CCD0, f);
        int r = Math.Max(1, (int)(q.r * K * (f > 0.7f ? (1 - f) / 0.3f : 1)));
        g.FillCircle(x, y, r, mulc(c, 0.85f));
        g.FillCircle(x - 1, y - 1, Math.Max(1, r - 1), c);
        if (r > 2)
          g.FillCircle(x - r / 3, y - r / 3, r / 3, mix(c, 0xFFFFFF, 0.3f));
      }
      // 雲の影 (地面に薄く) → 空を飛ぶものの影 (右下にずらす。高いものほど遠く)
      g.MaskMode = true;
      foreach (Cloud c in clouds)
        if (c.r > 0)
          cloudShape(g, S(c.rx) + 36, S(c.ry) + 54, c.r * K, 0, 0, 0, 0);
      g.MaskMode = false;
      g.ApplyShadow(SHADE, 55);
      g.MaskMode = true;
      foreach (Ufo u in ufos)
        if (u.on && u.t >= 0 && !(u.riseKind == 1 && u.rise < 0.45f))
        {
          float rs = u.rise;
          int ur = (int)(UFO_R[u.type] * K * (0.4f + 0.6f * rs));
          g.FillEllipse(S(u.rx) + (int)(15 * rs), S(u.ry) + (int)(21 * rs), ur, ur * 3 / 4, 0);
        }
      if (mom.on)
      {
        float a = momAlt(), s = 0.65f + 0.35f * a; // 落ちてくると影は母船の真下へ寄っていく
        g.FillEllipse(S(mom.rx + 14 * a), S(mom.ry + 20 * a), (int)(51 * s), (int)(39 * s), 0);
      }
      foreach (DebrisP d in debris)
        if (d.on)
          g.FillCircle(S(d.rx + 8 * d.alt), S(d.ry + 12 * d.alt), 3, 0);
      if (flock.on)
        drawFlock(g, 27, 39, 0);
      bool planeOn = deadT == 0 && !gone && mode != Mode.Continue;
      if (planeOn)
        g.Blit(PLANE, S(rpx) + 18, S(rpy) + 24, false);
      g.MaskMode = false;
      g.ApplyShadow(SHADE, 105);
      // 雲 (飛行機より低いところ)。下を影色、上を明るく。雨のときは灰色
      float rk = rainK();
      uint cloudL = mix(0xFFFFFF, 0x9AA0A8, rk), cloudM = mix(0xEEF2F6, 0x8A9098, rk), cloudD = mix(0xC8D2DE, 0x646A72, rk);
      foreach (Cloud c in clouds)
        if (c.r > 0 && visible(S(c.ry) - 40, S(c.ry) + 40))
        {
          int x = S(c.rx), y = S(c.ry);
          float cr = c.r * K;
          cloudShape(g, x, y, cr, 0, 4, 0, cloudD);
          cloudShape(g, x, y, cr, 0, 0, 1, cloudM);
          cloudShape(g, x, y, cr, -2, -2, cr * 0.45f, cloudL);
        }
      if (flock.on) // 鳥は雲より上を飛ぶ
        drawFlock(g, 0, 0, region == 3 ? 0xF4F4F4u : 0x2A2A2Au); // コーンウォールは白いカモメ
      if (item.on && visible(S(item.ry) - 12, S(item.ry) + 12))
      {
        int x = S(item.rx), y = S(item.ry);
        uint ic = (frame / 4) % 2 != 0 ? POPPY : GOLD;
        g.FillCircle(x, y, 9, INKG);
        g.FillCircle(x, y, 8, ic);
        g.DrawCircle(x, y, 6, mix(ic, 0xFFFFFF, 0.45f));
        g.DrawPixel(x - 4, y - 5, 0xFFFFFF);
        g.Text(x + 1, y - 6, INKG, "P", Datum.TC, 2);
        g.Text(x, y - 7, CREAM, "P", Datum.TC, 2);
      }
      if (mom.on)
        drawMom(g);
      drawDebris(g);
      foreach (Ufo u in ufos)
        if (u.on)
          drawUfo(g, u);
      foreach (Shot s in mine) // 曳光弾
        if (s.on)
        {
          int x = S(s.rx), y = S(s.ry);
          g.BlendPixel(x - 1, y + 1, GOLD, 110);
          g.BlendPixel(x + 1, y + 1, GOLD, 110);
          g.FillRect(x, y, 1, 6, 0xFFF8C8);
          g.DrawPixel(x, y + 6, GOLD);
          g.BlendPixel(x, y + 7, GOLD, 120);
        }
      drawArmsClassic(g);
      if (planeOn && !(invT > 0 && (frame / 3) % 2 != 0))
      {
        int x = S(rpx), y = S(rpy);
        drawPlane(g, x, y);
        if (muzzleT > 0) // ボタンを押した瞬間、翼の機銃が光る
        {
          glow(g, x - 9, y - 6, 0xFFF0A0, 2);
          glow(g, x + 8, y - 6, 0xFFF0A0, 2);
        }
      }
      foreach (Shot s in theirs) // 円盤の弾: 濃い縁どりにピンクと白の芯、左上に照り
        if (s.on)
        {
          drawEnemyShot(g, s, S(s.rx), S(s.ry), 1);
        }
      fx.Draw(g, CROPS[0].b);
      tint(g);
      drawLights(g);
      drawRainbow(g, SW / 2, SH + 40, 330);
      drawSeasonFx(g, 300);
      if (rk > 0) // 雨粒は色を掛けたあとに描いて、明るく見せる。地面にしぶき
      {
        for (int i = 0, n = (int)(drops.Length * rk); i < n; i++)
        {
          int x = S(drops[i].rx), y = S(drops[i].ry);
          if (region == 4) // 雪
            g.FillRect(x, y, 2, 2, 0xF4F8FC);
          else
            g.DrawLine(x, y, x - 2, y + 8, 0x98A8B8);
          g.DrawPixel(x - 2, y + 8, 0xC8D8E8);
        }
        for (int i = 0, n = (int)(24 * rk); i < n; i++)
        {
          uint h = ph(frame * 7 + i, i * 13);
          int x = (int)(h % SW), y = (int)((h >> 9) % SH);
          g.DrawPixel(x - 1, y, 0xB8C8D8);
          g.DrawPixel(x + 1, y, 0xB8C8D8);
          g.DrawPixel(x, y - 1, 0x98A8B8);
        }
      }
      if (Paused || mode == Mode.Continue)
        dim(g);
      drawOverlay(g);
      drawModeCard(g);
    }

    // 渡り鳥。先頭のうしろに左右交互に V 字に並び、羽ばたく。dx, dy は影のずらし (画面の点)
    void drawFlock(Gfx g, int dx, int dy, uint c)
    {
      float len = sqrtf(flock.vx * flock.vx + flock.vy * flock.vy) + 0.01f;
      float ux = flock.vx / len, uy = flock.vy / len;
      for (int i = 0; i < flock.n; i++)
      {
        int k = (i + 1) / 2, sd = i % 2 != 0 ? -1 : 1;
        int x = S(flock.rx - ux * k * 7 - uy * sd * k * 6) + dx, y = S(flock.ry - uy * k * 7 + ux * sd * k * 6) + dy;
        if (!visible(y - 3, y + 3))
          continue;
        int wing = ((flock.t + i * 5) / 6) % 4; // 0 上 1 水平 2 下 3 水平
        int wy = wing == 0 ? -2 : wing == 2 ? 1 : 0, tip = wy < 0 ? -1 : wy > 0 ? 1 : 0;
        g.FillRect(x - 1, y, 3, 2, c);
        g.DrawPixel(x - 2, y + (wy < 0 ? -1 : 0), c);
        g.DrawPixel(x + 2, y + (wy < 0 ? -1 : 0), c);
        g.DrawPixel(x - 3, y + wy, c);
        g.DrawPixel(x + 3, y + wy, c);
        g.DrawPixel(x - 4, y + wy + tip, c);
        g.DrawPixel(x + 4, y + wy + tip, c);
      }
    }
  }
}
