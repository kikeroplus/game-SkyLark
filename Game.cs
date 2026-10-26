// オリジナルの縦シューティング「SKYLARK」。イギリスの片田舎の上を、プロペラ機がのんびり飛ぶ。
// ESP32 のデモ (TFT-Tester-XEV の src/skylark.cpp) を移して、人が操作して遊べるようにしたもの。
// 生け垣で区切られたパッチワークの畑、川と石の橋、小道と村、教会、羊や牛。2 面は山がちで、生け垣は石垣になる。
// ときどき空飛ぶ円盤がやってくる。羊をさらおうとする円盤は、光線を当てているあいだに落とせば羊が助かる。
// 1 日 約 3 分: AREA 1 (牧草地の谷) → AREA 2 (山と湖。最後に母船) → THE VALLEY IS SAFE → 次の日 (少しずつ手強くなる)
// 1 日のあいだに時間と天気が移る: 朝 → にわか雨 (稲光) → 晴れ → 夕焼け → 夕暮れ (家に灯りがともる)。
// ときどき渡り鳥の群れが空を横切り、線路を蒸気機関車が煙を上げて走る。母船は落とすと、壊れながら墜落して地面に残骸が残る。
// 何も押さずにいるとデモ (自動操縦) が流れる。残機がなくなるとコンティニュー、やめればネームエントリー
using System;

namespace SkyLark
{
  // 1 フレームぶんの操作。Program がキーボードとゲームパッドからまとめる
  public class Input
  {
    public bool Left, Right, Up, Down, Fire, Slow;
    public float AX, AY; // アナログスティック (-1〜1。使っていなければ 0)
    public bool FirePressed, StartPressed, PausePressed, QuitPressed, BackPressed;
    public bool UpPressed, DownPressed, LeftPressed, RightPressed;
    public char Typed; // ネームエントリーで打った文字 (なければ 0)
  }

  public class Rank
  {
    public int Score;
    public string Name;
    public Rank(int s, string n)
    {
      Score = s;
      Name = n;
    }
  }

  public partial class Game
  {
    public const int W = 213, H = 360; // 3 倍にすると 639x1080
    public const int FPS = 30;
    const int CY = (H - 320) / 2; // ESP32 版 (高さ 320) で決めていた文字や枠の高さを、真ん中へ寄せるずらし
    const int T = 8;                  // 地面の 1 マス
    const int COLS = (W + T - 1) / T; // 27 (右端の列は 5px だけ見える)
    // 地面は画面の上の端より FAR 行ぶん奥まで前もって作る (2.5D で遠くまで見えるように)。
    // そのぶん地形を作るのを LEAD フレーム先回りさせて、画面に入ってくる時刻は前と同じにする
    const int FAR = 21;
    const int ROWS = H / T + 2 + FAR;
    const int LEAD = (int)(FAR * T / SPEED);
    const float SPEED = 0.7f; // 地面が流れる速さ (px/フレーム)
    const int PY0 = H - 44;
    const int MOM_Y = 96;           // 母船が止まる高さ
    const int MOM_STAY = 24 * FPS;

    // ---- 1 日の流れ (フレーム) ----
    const int TITLE_END = 7 * FPS;
    const int FELL_AT = 80 * FPS;  // ここから 20 秒かけて山がちになる
    const int AREA2_AT = 92 * FPS;
    const int MOM_AT = 140 * FPS;
    const int CLEAR_AT = 178 * FPS;
    const int BONUS_AT = CLEAR_AT + 45;
    const int RANK_AT = CLEAR_AT + 4 * FPS;
    const int LOOP_AT = CLEAR_AT + 12 * FPS;
    const int SHUTTER = 20;
    // 時間と天気
    const int RAIN_AT = 44 * FPS;   // ここから 6 秒かけて降りだす
    const int RAIN_END = 70 * FPS;  // ここから 6 秒かけて上がる
    const int SUNSET_AT = 100 * FPS; // ここから 35 秒かけて夕焼けに
    const int DUSK_AT = 150 * FPS;   // ここから 25 秒かけて夕暮れに
    // 名所: 雨が上がったあとの白亜の草原にストーンヘンジ、山あいの湖にネッシー
    const int STONE_AT = 76 * FPS;
    const int LOCH_AT = 106 * FPS;
    const int CROP_AT = 30 * FPS;  // ミステリーサークル (朝の谷の麦畑)
    const int BEAST_AT = 96 * FPS; // ボドミンの野獣 (AREA 2 の荒れ地)
    const int CROP_ROWS = 8;
    const int PLAIN_ROWS = 10, LOCH_ROWS = 26;
    const int CRASH_T = 150;         // 母船が落とされてから地面に落ちるまでのフレーム数

    // 文字と飾り: クリームの紙に深緑とひなげしの赤、空色
    const uint CREAM = 0xFFF4D8, PAPER = 0xF0E6C8, INKG = 0x1E2A14, DEEP = 0x3A6A2A, POPPY = 0xD84A3A, SKY = 0x8ACAE8,
               GOLD = 0xF0C860, BROWN = 0x7A6A4A;
    const uint SHADOW = 0x34482A; // 飛んでいるものの地面の影
    // 地面
    const uint HEDGE = 0x2F4E24, HEDGE_L = 0x46703A, WALL = 0x8E8A7C, WALL_D = 0x66625A;
    const uint WATER_C = 0x4A7AA8, WATER_L = 0x7AA8D0, BANK_C = 0x6A9446, REED = 0x4A6A30;
    const uint LANE_C = 0xB9A57E, LANE_D = 0x9A8A62, STONE = 0xA8A090, STONE_D = 0x6A665A;
    const uint WOOD_C = 0x2A4A22, WOOD_L = 0x3E6A30, WOOD_H = 0x5A8A3E, GREEN_C = 0x7AA850;
    static readonly uint[] MOOR_C = { 0, 0x7A8A4A, 0x86707A, 0x9A9488, 0xB8B4AA }; // 山の高さ 1〜4 (荒れ地 → ヒース → 岩 → 頂)
    // 円盤
    const uint SILVER = 0xB8BCC8, SILVER_L = 0xDCE0E8, SILVER_D = 0x7A7E8A, UFO_K = 0x2E323C, DOME = 0x5AC8D8,
               DOME_L = 0xC8F4F8, BEAM = 0xE8F8A0, PLASMA = 0xFF5AC8, PLASMA_D = 0x5A1040;
    static readonly uint[] RIM = { 0xFF5050, 0xFFD040, 0x60FF80, 0x60C0FF };

    // 地面のマス
    const byte FIELD = 0, WOOD = 1, WATER = 2, BANK = 3, LANE_V = 4, LANE_H = 5, BRIDGE = 6, MOOR = 7, VGREEN = 8, RAIL = 9, RAILBRIDGE = 10;
    const byte PLAIN = 11; // 白亜の草原 (生け垣も林もない、なだらかな草地)
    const byte HEDGE_TOP = 1, HEDGE_LEFT = 2, STONEWALL = 4; // 下の縁に生け垣 / 左の縁に生け垣 / 生け垣のかわりに石垣
    struct Tile
    {
      public byte type, v, flags;
      public Tile(byte t, int v_, int f)
      {
        type = t;
        v = (byte)v_;
        flags = (byte)f;
      }
    }

    // 畑の作物。pat: 0 = 無地に点、1 = 横の畝、2 = 縦の畝、3 = 水田 (水に苗の点)
    struct Crop
    {
      public uint b, line;
      public int pat;
      public Crop(uint b_, uint l, int p)
      {
        b = b_;
        line = l;
        pat = p;
      }
    }
    static readonly Crop[] CROPS = {
      new Crop(0x6E9A48, 0x5E8A3C, 0), // 牧草地 (羊がいる)
      new Crop(0x9AB257, 0xE8E0A0, 0), // 花の咲く草地 (牛がいる)
      new Crop(0xC8B060, 0xA89048, 1), // 麦 (干し草ロール)
      new Crop(0x8A6A44, 0x6E5232, 1), // 耕した畑
      new Crop(0xD8C840, 0xB8A830, 0), // 菜の花
      new Crop(0x4E7A3A, 0x3E6A2E, 2), // 野菜
      new Crop(0x6A9AA8, 0x4E8A3A, 3), // 水田
    };
    const int PASTURE = 0, MEADOW = 1, WHEAT = 2;

    const int SHEEP = 0, COW = 1, COTTAGE = 2, BARN = 3, CHURCH = 4, TREE = 5, BALE = 6, ROCK = 7, DEBRIS = 8, WRECK = 9;
    const int DOG = 10, DUCK = 11, PUB = 12; // 羊を追う牧羊犬 / 川を渡るカモの親子 (v = 0 が親) / 村のパブ
    const int STONEHENGE = 13, NESSIE = 14;  // 名所 (ネッシーは lift = 水から出ている度合い、t = 泳いでいる時間)
    const int BEAST = 15, CROPCIRCLE = 16;   // ボドミンの野獣 (v = 走る向き) / ミステリーサークル (t = 描きはじめてからの時間)
    const int LAUNDRY = 2;                   // COTTAGE の v: 1 の位 = スレート屋根、2 = 洗濯物を干している

    // 動くものの位置。描くときは前のフレーム (ox, oy) とのあいだを補い、1 秒 60 コマでなめらかに見せる (rx, ry = 描く位置)
    class Body
    {
      public float x, y, ox, oy, rx, ry;
    }
    class Prop : Body
    {
      public float lift;           // 円盤の光線で持ち上げられている量 (0〜1)
      public float vx, vy, ax, ay; // 跳ねて戻る羊の向き、カモの泳ぐ向き / 牧羊犬が走りまわる中心
      public int type, v, t, hop;  // hop: 助けられた羊がうれしそうに跳ねている残り
      public int spawned;          // 名所から湧いて出た円盤の数
      public bool on, held;
    }

    const int SCOUT = 0, TRIO = 1, ABDUCTOR = 2; // 小型 / 3 機で輪になって回る / 羊や牛を光線でさらう
    static readonly int[] UFO_HP = { 2, 2, 6 };
    static readonly int[] UFO_PTS = { 200, 300, 1500 };
    static readonly int[] UFO_R = { 6, 6, 9 };
    class Ufo : Body
    {
      public float vx, vy, home, a; // TRIO: vx = 輪の中心の x、home = 輪が止まる高さ、a = 輪の中の角度
      public int type, t, hp, flash, group, state, prop;
      public bool on;
      public float rise = 1; // 地面や湖から出てくる途中 (0〜1。1 = ふつうに飛んでいる)
      public int riseKind;   // 0 = ストーンヘンジから、1 = 湖から
    }
    class Shot : Body
    {
      public float vx, vy;
      public bool on;
    }
    class Cloud : Body
    {
      public int r;
    }
    class Pop : Body // 「SAVED」「BAA...」などの浮かぶ文字
    {
      public int t;
      public bool on;
      public string text;
      public uint col;
    }
    class Item : Body
    {
      public int t;
      public bool on;
    }
    class Flock : Body // 渡り鳥の群れ。先頭のうしろに V 字に並ぶ
    {
      public float vx, vy;
      public int n, t;
      public bool on;
    }
    class Train : Body // 蒸気機関車。x は先頭、dir の向きへ走る
    {
      public int dir, cars;
      public bool on;
    }
    class Puff : Body // 機関車の湯気や残骸の煙
    {
      public float vx, r;
      public int t, life;
      public bool smoke, on;
    }
    class DebrisP : Body // 母船から飛び散って落ちていくかけら
    {
      public float vx, vy, alt, da, ang;
      public bool on;
    }
    class Drop : Body // 雨粒
    {
    }
    class Mother : Body
    {
      public bool on, dead;
      public int t, hp, maxHp, hitT, deadT;
    }

    // ---- 小さな道具 (ESP32 版と同じ名前にして、移したところを読みやすくする) ----
    const float PI = 3.14159265f;
    static float sinf(float x) { return (float)Math.Sin(x); }
    static float cosf(float x) { return (float)Math.Cos(x); }
    static float sqrtf(float x) { return (float)Math.Sqrt(x); }
    static float fabsf(float x) { return Math.Abs(x); }
    static float atan2f(float y, float x) { return (float)Math.Atan2(y, x); }
    static float floorf(float x) { return (float)Math.Floor(x); }
    static float constrain(float v, float a, float b) { return v < a ? a : v > b ? b : v; }
    static int constrain(int v, int a, int b) { return v < a ? a : v > b ? b : v; }

    public static uint mix(uint a, uint b, float t) // a と b を t (0〜1) で混ぜる
    {
      t = constrain(t, 0.0f, 1.0f);
      uint o = 0;
      for (int sh = 0; sh <= 16; sh += 8)
      {
        int ca = (int)(a >> sh) & 0xFF, cb = (int)(b >> sh) & 0xFF;
        o |= (uint)(ca + (cb - ca) * t) << sh;
      }
      return o;
    }

    // 乱数 (xorshift)。毎日の始めに種を変えるので、地形も敵の出方も毎回違う
    uint rngState = 1;
    uint rnd()
    {
      rngState ^= rngState << 13;
      rngState ^= rngState >> 17;
      rngState ^= rngState << 5;
      return rngState;
    }
    float frand(float a, float b) { return a + (b - a) * (rnd() / 4294967295.0f); }
    int irand(int a, int b) { return a + (int)(rnd() % (uint)(b - a + 1)); } // a 以上 b 以下

    // 爆発などの火花
    class Particles
    {
      class P : Body
      {
        public float vx, vy;
        public int life, max;
        public uint rgb;
      }
      readonly P[] p = new P[200];
      readonly Game gm;
      public Particles(Game g)
      {
        gm = g;
        for (int i = 0; i < p.Length; i++)
          p[i] = new P();
      }
      public void Clear()
      {
        foreach (P q in p)
          q.life = 0;
      }
      public void Burst(float x, float y, uint rgb, int n, float speed, int life)
      {
        foreach (P q in p)
        {
          if (n <= 0)
            break;
          if (q.life > 0)
            continue;
          float a = gm.frand(0, 2 * PI), s = gm.frand(0.3f, 1.0f) * speed;
          q.x = q.ox = x;
          q.y = q.oy = y;
          q.vx = cosf(a) * s;
          q.vy = sinf(a) * s;
          q.life = gm.irand(life / 2, life);
          q.max = life;
          q.rgb = rgb;
          n--;
        }
      }
      public void Keep()
      {
        foreach (P q in p)
          keep(q);
      }
      public void Lerp(float a)
      {
        foreach (P q in p)
          lerp(q, a);
      }
      public void Draw25(Gfx g, uint bg) // 2.5D: 飛ぶものの高さで、奥ほど小さく
      {
        foreach (P q in p)
          if (q.life > 0)
          {
            float sx, sy, sc;
            gm.proj(q.rx, q.ry, AIR, out sx, out sy, out sc);
            float k = (float)q.life / q.max;
            int sz = Math.Max(1, (int)((k > 0.6f ? 3 : 2) * sc / K + 0.5f));
            g.FillRect((int)sx - sz / 2, (int)sy - sz / 2, sz, sz, mix(bg, q.rgb, k * 1.5f));
          }
      }
      public void Step()
      {
        foreach (P q in p)
          if (q.life > 0)
          {
            q.x += q.vx;
            q.y += q.vy;
            q.life--;
          }
      }
      public void Draw(Gfx g, uint bg) // 画面の点で描く (世界の座標を 1.5 倍)。燃えはじめは大きく
      {
        foreach (P q in p)
          if (q.life > 0)
          {
            float k = (float)q.life / q.max;
            int sz = k > 0.6f ? 3 : 2;
            g.FillRect(S(q.rx) - sz / 2, S(q.ry) - sz / 2, sz, sz, mix(bg, q.rgb, k * 1.5f));
            if (k > 0.75f)
              g.DrawPixel(S(q.rx), S(q.ry), mix(q.rgb, 0xFFFFFF, 0.5f));
          }
      }
    }

    // ---- 遊びの状態 ----
    public enum Mode
    {
      Attract,   // デモ (自動操縦)
      Play,
      Continue,  // 残機がなくなった。数えているあいだに押せば続けられる
      GameOver,
      NameEntry,
      Ranking,   // 遊び終わったあとのランキング
    }
    public Mode mode = Mode.Attract;
    public bool Paused;
    public readonly Rank[] Table = { new Rank(80000, "MOL"), new Rank(60000, "BRK"), new Rank(45000, "HAY"), new Rank(30000, "OWL"), new Rank(15000, "EWE") };
    public string LastName = "AAA";
    public Action TableChanged; // ランキングが変わったら Program が保存する
    public Sound Snd;
    public bool God; // 確かめる用: やられない

    bool demo = true;      // 自動操縦
    int day;               // 何日目か (2 日目からは少しずつ手強くなる)
    int openAt;            // 幕が開きはじめたフレーム
    int modeT;             // コンティニューの残り、GAME OVER やランキングを出している残り
    bool outOfLives, gone; // 残機なしでやられた / 自機がもういない (遊び終わったあと)
    int enterT, fireCd, continues, nextExtend;
    char[] entry = new char[3];
    int entryPos, entryRepeat;
    const string LETTERS = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.-! ";
    bool enemyShotSnd;
    bool fixedSeed; // 確かめる用: 乱数の種を時刻で変えない
    bool railPending; // 遠くに線路ができて、汽車が来るのを待っている
    float railY;
    float enginePitch = 1;
    bool pubPlaced; // いまの村にもうパブを建てた
    // 1 秒 60 コマで描くための補い
    bool worldMoved; // このフレームで世界が進んだ
    float scrollAdv, rscroll, opx, opy, rpx, rpy;
    int thunderT;

    Tile[][] map = new Tile[ROWS][];
    int[] rowId = new int[ROWS];
    float scrollY;
    int rowCount;
    uint seed;
    // 地形を作る途中の状態
    int[] segW = new int[COLS], segLeft = new int[COLS], segCrop = new int[COLS]; // 畑の区画。segW > 0 は区画の左端の列
    bool riverOn, laneOn;
    float riverC, riverW, riverDrift;
    int riverLen, laneLen, laneCol, villageLen, churchAt;
    bool railRow; // いま作った行が線路
    // 名所を作る途中の状態
    bool stonePlanned, lochPlanned, stoneFound, nessieFound;
    int plainLen, stoneCol, lochLen;
    bool cropPlanned, cropTopNext, cropFound, beastDone, beastFound;
    int cropLen, cropC;
    float lochC, lochR;

    Prop[] props = New<Prop>(160);
    Ufo[] ufos = New<Ufo>(18);
    Shot[] mine = New<Shot>(80), theirs = New<Shot>(110);
    Cloud[] clouds = New<Cloud>(5); // 晴れは 3 つ、雨のときは 5 つ
    Flock flock = new Flock();
    Train train = new Train();
    Puff[] puffs = New<Puff>(32);
    DebrisP[] debris = New<DebrisP>(10);
    Drop[] drops = New<Drop>(64);
    int birdT, flashT; // flashT: 稲光や墜落で画面が光っている残りフレーム
    Pop[] pops = New<Pop>(4);
    Item item = new Item();
    Mother mom = new Mother();
    float px, py;
    int frame, tapT, taps, tapsMax, muzzleT, waveT, groupN, invT, deadT;
    int lives, power, score, saved, extendT, newRank;
    Particles fx;

    static TT[] New<TT>(int n) where TT : new()
    {
      TT[] a = new TT[n];
      for (int i = 0; i < n; i++)
        a[i] = new TT();
      return a;
    }

    public Game()
    {
      fx = new Particles(this);
      for (int r = 0; r < ROWS; r++)
        map[r] = new Tile[COLS];
      rngState = (uint)Environment.TickCount | 1;
      StartDemo();
    }

    int area() { return frame < AREA2_AT ? 1 : 2; }
    bool playing() { return frame >= TITLE_END && frame < CLEAR_AT; }
    bool active() { return frame > TITLE_END + 90 && frame < CLEAR_AT - 150 && !gone; }
    bool momAlive() { return mom.on && !mom.dead; }
    float fellK() { return constrain((frame + LEAD - FELL_AT) / (20.0f * FPS), 0.0f, 1.0f); } // 山がちになる度合い (地形を作るとき用なので先回り)
    float ramp(int a, int b) { return constrain((frame - a) / (float)(b - a), 0.0f, 1.0f); }
    float rainK() { return ramp(RAIN_AT, RAIN_AT + 6 * FPS) - ramp(RAIN_END, RAIN_END + 6 * FPS); }
    float sunsetK() { return ramp(SUNSET_AT, SUNSET_AT + 35 * FPS); }
    float duskK() { return ramp(DUSK_AT, DUSK_AT + 25 * FPS); }
    // 手強さ: 2 日目から弾が速く、円盤が多く、母船が硬くなる
    float bulletK() { return Math.Min(1.6f, 1 + 0.12f * (day - 1)); }
    float waveK() { return Math.Max(0.55f, 1 - 0.1f * (day - 1)); }

    void Sfx(Sfx s) { Sfx(s, 1); }
    void Sfx(Sfx s, float vol)
    {
      if (Snd != null)
        Snd.Play(s, demo ? vol * 0.5f : vol);
    }

    // ---- 地形 ----
    float hash2(int x, int y)
    {
      uint h = (uint)x * 374761393u + (uint)y * 668265263u + seed;
      h = (h ^ (h >> 13)) * 1274126177u;
      return ((h ^ (h >> 16)) & 0xFFFF) / 65535.0f;
    }
    float noise(float x, float y)
    {
      int ix = (int)floorf(x), iy = (int)floorf(y);
      float fx_ = x - ix, fy = y - iy;
      fx_ = fx_ * fx_ * (3 - 2 * fx_);
      fy = fy * fy * (3 - 2 * fy);
      float a = hash2(ix, iy), b = hash2(ix + 1, iy), c = hash2(ix, iy + 1), d = hash2(ix + 1, iy + 1);
      return a + (b - a) * fx_ + (c - a) * fy + (a - b - c + d) * fx_ * fy;
    }

    static readonly byte[] VALE = { 0, 0, 0, 0, 0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6 };
    static readonly byte[] FELL = { 0, 0, 0, 0, 0, 0, 1, 1, 2, 3 };
    int pickCrop()
    {
      // 山のほうは牧草地ばかり。谷は色とりどり
      return fellK() > 0.5f ? FELL[irand(0, 9)] : VALE[irand(0, 19)];
    }

    void setSeg(int p, int w)
    {
      segW[p] = w;
      segLeft[p] = irand(3, 9);
      segCrop[p] = pickCrop();
      for (int k = p + 1; k < p + w; k++)
        segW[k] = 0;
    }
    // [c, e) を幅 3〜8 の区画に分け直す
    void splitSegs(int c, int e)
    {
      for (int p = c; p < e;)
      {
        int w = Math.Min(irand(3, 8), e - p);
        if (e - (p + w) < 3)
          w = e - p;
        setSeg(p, w);
        p += w;
      }
    }

    // 畑の 1 行。区画が終わったら、ときどき右どなりとまとめて分け直すので、境の生け垣の位置が動いていく
    void fieldRow(Tile[] row)
    {
      int wall = fellK() > 0.5f ? STONEWALL : 0;
      for (int c = 0; c < COLS;)
      {
        int e = c + segW[c];
        bool top = false;
        if (--segLeft[c] < 0)
        {
          if (e < COLS && irand(0, 1) != 0)
            e += segW[e];
          splitSegs(c, e);
          top = true;
        }
        for (int p = c; p < e; p += segW[p])
          for (int k = p; k < p + segW[p]; k++)
            row[k] = new Tile(FIELD, segCrop[p], (top ? HEDGE_TOP : 0) | (k == p && k > 0 ? HEDGE_LEFT : 0) | wall);
        c = e;
      }
    }

    void genRow(Tile[] row, int n)
    {
      float f = fellK();
      fieldRow(row);
      for (int c = 0; c < COLS; c++)
      {
        // 雑木林
        if (noise(c * 0.25f, n * 0.08f + 77) > 0.76f)
          row[c] = new Tile(WOOD, 0, 0);
        // 山: 高さが閾値を超えたところ。山がちになるほど閾値が下がる
        float h = noise(c * 0.16f, n * 0.05f + 300);
        float thr = 0.86f - 0.36f * f;
        if (h > thr)
          row[c] = new Tile(MOOR, constrain(1 + (int)((h - thr) / 0.06f), 1, 4), 0);
      }
      // 川: くねりながら流れ、両岸は葦の生えた土手
      if (riverOn)
      {
        riverDrift = constrain(riverDrift + frand(-0.06f, 0.06f), -0.3f, 0.3f);
        riverC += riverDrift;
        if (riverC < 2 || riverC > COLS - 3)
          riverDrift = -riverDrift;
        riverC = constrain(riverC, 2.0f, COLS - 3.0f);
        riverW = constrain(riverW + frand(-0.12f, 0.12f), 1.4f, 3.2f);
        for (int c = 0; c < COLS; c++)
        {
          float d = fabsf(c + 0.5f - riverC);
          if (d < riverW / 2)
            row[c] = new Tile(WATER, 0, 0);
          else if (d < riverW / 2 + 1)
            row[c] = new Tile(BANK, 0, 0);
        }
        if (--riverLen <= 0)
          riverOn = false;
      }
      else if (irand(0, 70) == 0)
      {
        riverOn = true;
        riverC = frand(3, COLS - 4);
        riverW = frand(1.6f, 2.8f);
        riverDrift = 0;
        riverLen = irand(60, 140);
      }
      // 小道: ときどき 1 列ずれながら続く。川の上は石の橋
      if (laneOn)
      {
        int old = laneCol;
        if (irand(0, 11) == 0)
          laneCol = constrain(laneCol + (irand(0, 1) != 0 ? 1 : -1), 2, COLS - 4);
        for (int c = Math.Min(old, laneCol); c <= Math.Max(old, laneCol); c++)
          row[c] = new Tile(row[c].type == WATER ? BRIDGE : LANE_V, 0, 0);
        if (--laneLen <= 0)
          laneOn = false;
      }
      else if (lochLen == 0 && irand(0, 60) == 0)
      {
        laneOn = true;
        laneCol = irand(3, COLS - 5);
        laneLen = irand(40, 100);
      }
      if (irand(0, 90) == 0) // 横切る小道
      {
        for (int c = 0; c < COLS; c++)
          if (row[c].type != LANE_V)
            row[c] = new Tile(row[c].type == WATER ? BRIDGE : LANE_H, 0, 0);
      }
      else if (villageLen == 0 && plainLen == 0 && lochLen == 0 && irand(0, 110) == 0) // 横切る線路。小道とは踏切で交わる
      {
        for (int c = 0; c < COLS; c++)
          row[c] = new Tile(row[c].type == WATER ? RAILBRIDGE : RAIL, 0, 0);
        railRow = true;
      }
      // 村: 小道の両側を芝にして小屋を並べる。真ん中あたりに教会
      if (villageLen > 0 && laneOn)
      {
        for (int c = laneCol - 2; c <= laneCol + 3; c++)
          if (c >= 0 && c < COLS && (row[c].type == FIELD || row[c].type == WOOD))
            row[c] = new Tile(VGREEN, 0, 0);
        villageLen--;
      }
      else if (laneOn && plainLen == 0 && lochLen == 0 && irand(0, f < 0.7f ? 50 : 110) == 0) // 山あいにも、ときどき小さな集落
      {
        villageLen = irand(7, 12);
        churchAt = villageLen / 2;
        pubPlaced = false;
      }
      // ミステリーサークルの麦畑: 生け垣で囲った大きな麦畑 (川や小道のマスはそのまま)
      if (cropLen > 0)
      {
        for (int c = cropC - 6; c <= cropC + 6; c++)
          if (c >= 0 && c < COLS)
          {
            byte t = row[c].type;
            if (t == FIELD || t == WOOD || t == MOOR || t == VGREEN || t == PLAIN)
              row[c] = new Tile(FIELD, WHEAT, (c == cropC - 6 ? HEDGE_LEFT : 0) | (cropLen == CROP_ROWS ? HEDGE_TOP : 0));
          }
        if (cropC + 7 < COLS && row[cropC + 7].type == FIELD)
          row[cropC + 7].flags |= HEDGE_LEFT;
        if (--cropLen == 0)
          cropTopNext = true;
      }
      else if (cropTopNext) // 麦畑の上の縁にも生け垣
      {
        for (int c = Math.Max(0, cropC - 6); c <= Math.Min(COLS - 1, cropC + 6); c++)
          if (row[c].type == FIELD)
            row[c].flags |= HEDGE_TOP;
        cropTopNext = false;
      }
      // 白亜の草原: 畑も生け垣も林も消えて、一面のなだらかな草地 (小道と川はそのまま)
      if (plainLen > 0)
      {
        for (int c = 0; c < COLS; c++)
        {
          byte t = row[c].type;
          if (t == FIELD || t == WOOD || t == MOOR || t == VGREEN)
            row[c] = new Tile(PLAIN, 0, 0);
        }
        plainLen--;
      }
      // 湖 (ロッホ): 上下にふくらんだ楕円の水。まわりは葦の土手
      if (lochLen > 0)
      {
        float tt = (LOCH_ROWS - lochLen + 0.5f) / LOCH_ROWS, hw = lochR * sqrtf(Math.Max(0, 1 - (2 * tt - 1) * (2 * tt - 1)));
        lochC = constrain(lochC + frand(-0.15f, 0.15f), lochR + 1, COLS - lochR - 2);
        for (int c = 0; c < COLS; c++)
        {
          float dd = fabsf(c + 0.5f - lochC);
          if (dd < hw)
            row[c] = new Tile(WATER, 0, 0);
          else if (dd < hw + 1.2f)
            row[c] = new Tile(BANK, 0, 0);
        }
        lochLen--;
      }
    }

    Prop addProp(float x, float y, int type, int v = 0)
    {
      foreach (Prop p in props)
        if (!p.on)
        {
          p.x = p.ox = x;
          p.y = p.oy = y;
          p.lift = p.vx = p.vy = 0;
          p.ax = x;
          p.ay = y;
          p.type = type;
          p.v = v;
          p.t = p.hop = p.spawned = 0;
          p.on = true;
          p.held = false;
          return p;
        }
      return null;
    }

    void newRow()
    {
      Tile[] last = map[ROWS - 1];
      Array.Copy(map, 0, map, 1, ROWS - 1);
      map[0] = last;
      Array.Copy(rowId, 0, rowId, 1, ROWS - 1);
      shiftRowCache();
      rowId[0] = rowCount++;
      if (!stonePlanned && frame + LEAD >= STONE_AT) // ストーンヘンジの草原を作りはじめる (川と小道を避けた列に立てる)
      {
        stonePlanned = true;
        plainLen = PLAIN_ROWS;
        int best = COLS / 2, bestD = -1;
        for (int c = 5; c <= COLS - 6; c++)
        {
          int dd = Math.Min(riverOn ? (int)Math.Abs(c - riverC) : 99, laneOn ? Math.Abs(c - laneCol) : 99);
          dd = Math.Min(dd, 8) * 4 - Math.Abs(c - COLS / 2);
          if (dd > bestD)
          {
            bestD = dd;
            best = c;
          }
        }
        stoneCol = best;
      }
      if (!cropPlanned && frame + LEAD >= CROP_AT) // ミステリーサークルの麦畑を作りはじめる (川と小道を避けた列に)
      {
        cropPlanned = true;
        cropLen = CROP_ROWS;
        int best = COLS / 2, bestD = -1;
        for (int c = 7; c <= COLS - 8; c++)
        {
          int dd = Math.Min(riverOn ? (int)Math.Abs(c - riverC) : 99, laneOn ? Math.Abs(c - laneCol) : 99);
          dd = Math.Min(dd, 9) * 4 - Math.Abs(c - COLS / 2);
          if (dd > bestD)
          {
            bestD = dd;
            best = c;
          }
        }
        cropC = best;
      }
      if (!lochPlanned && frame + LEAD >= LOCH_AT) // 湖を作りはじめる
      {
        lochPlanned = true;
        lochLen = LOCH_ROWS;
        lochR = 7.0f;
        lochC = frand(lochR + 2, COLS - lochR - 3);
        laneOn = false;
      }
      genRow(map[0], rowId[0]);
      // 畑や村の上に置くもの
      float y = scrollY - T + 4 - FAR * T;
      if (railRow)
      {
        railRow = false;
        if (!train.on && !railPending && irand(0, 9) < 8) // 線路ができたら、たいてい汽車が来る (線路が画面の上の端まで来たら走りだす)
        {
          railPending = true;
          railY = y;
        }
      }
      bool village = villageLen > 0 && laneOn;
      if (village && (rowId[0] & 1) != 0)
      {
        int sd = irand(0, 1) != 0 ? 1 : -1;
        float x = (laneCol + 0.5f + sd * 2.0f) * T + (sd > 0 ? 4 : 0);
        if (villageLen == churchAt)
          addProp((laneCol + 0.5f - sd * 3.5f) * T, y, CHURCH);
        if (!pubPlaced && villageLen <= 3 && villageLen != churchAt) // 村のはずれにパブ
        {
          addProp(x + sd * 2, y, PUB, sd > 0 ? 1 : 0);
          pubPlaced = true;
        }
        else
          addProp(x, y, COTTAGE, (irand(0, 2) == 0 ? 1 : 0) | (irand(0, 2) == 0 ? LAUNDRY : 0));
      }
      for (int c = 0; c < COLS; c++)
      {
        Tile t = map[0][c];
        float x = c * T + 4, r = frand(0, 1);
        if (t.type == FIELD)
        {
          if (t.v == PASTURE && r < 0.02f + 0.02f * fellK())
          {
            for (int i = 0, n = irand(3, 5); i < n; i++)
              addProp(x + frand(-8, 8), y + frand(-6, 6), SHEEP, irand(0, 1));
            if (irand(0, 4) == 0) // ときどき牧羊犬がついている
            {
              Prop d = addProp(x, y, DOG);
              if (d != null)
                d.t = irand(0, 300);
            }
          }
          else if (t.v == MEADOW && r < 0.012f)
            for (int i = 0, n = irand(2, 3); i < n; i++)
              addProp(x + frand(-8, 8), y + frand(-6, 6), COW, irand(0, 1));
          else if (t.v == WHEAT && r < 0.015f)
            for (int i = 0, n = irand(2, 4); i < n; i++)
              addProp(x - 6 + i * 6, y, BALE);
          else if ((t.flags & (HEDGE_TOP | HEDGE_LEFT)) != 0 && (t.flags & STONEWALL) == 0 && r < 0.05f)
            addProp(x - 3 + irand(0, 6), y + ((t.flags & HEDGE_TOP) != 0 ? 3 : 0), TREE, irand(0, 1));
          else if (laneOn && !village && Math.Abs(c - laneCol) == 2 && r < 0.012f) // 小道ぞいの一軒家と納屋
          {
            addProp(x, y, COTTAGE, irand(0, 1) | (irand(0, 1) != 0 ? LAUNDRY : 0));
            addProp(x + (c > laneCol ? 14 : -14), y - 4, BARN);
          }
        }
        else if (t.type == MOOR && t.v >= 3 && r < 0.05f)
          addProp(x + frand(-3, 3), y, ROCK);
      }
      if (cropPlanned && cropLen == CROP_ROWS / 2 && !hasProp(CROPCIRCLE))
        addProp(cropC * T + 4, y, CROPCIRCLE);
      if (stonePlanned && plainLen == PLAIN_ROWS / 2 - 1 && !hasProp(STONEHENGE))
        addProp(stoneCol * T + 4, y, STONEHENGE);
      if (lochPlanned && lochLen == LOCH_ROWS / 2 && !hasProp(NESSIE))
      {
        Prop n = addProp(lochC * T, y, NESSIE);
        if (n != null)
          n.ax = lochC * T;
      }
      for (int c = 0; c < COLS; c++) // 草原にも羊
        if (map[0][c].type == PLAIN && frand(0, 1) < 0.012f && Math.Abs(c - stoneCol) > 3)
          for (int i = 0, n = irand(3, 6); i < n; i++)
            addProp(c * T + 4 + frand(-8, 8), y + frand(-6, 6), SHEEP, irand(0, 1));
      // カモの親子: 川の岸から向こう岸へ、親を先頭に一列で泳いで渡る
      int wl = -1, wr = -1;
      for (int c = 0; c < COLS; c++)
        if (map[0][c].type == WATER)
        {
          if (wl < 0)
            wl = c;
          wr = c;
        }
      if (wl >= 0 && wr > wl && lochLen == 0 && irand(0, 40) == 0)
      {
        int dir = irand(0, 1) != 0 ? 1 : -1;
        float x0 = dir > 0 ? wl * T - 2 : (wr + 1) * T + 2;
        for (int i = 0, n = irand(3, 5); i <= n; i++)
        {
          Prop d = addProp(x0 - dir * i * 5, y + 3, DUCK, i);
          if (d != null)
            d.vx = dir * 0.16f;
        }
      }
    }

    bool hasProp(int type)
    {
      foreach (Prop p in props)
        if (p.on && p.type == type)
          return true;
      return false;
    }

    // 世界の座標の地面のマス (なければ -1)
    int tileTypeAt(float x, float y)
    {
      int r = (int)Math.Floor((y - scrollY) / T) + 1 + FAR, c = (int)Math.Floor(x / T);
      if (r < 0 || r >= ROWS || c < 0 || c >= COLS)
        return -1;
      return map[r][c].type;
    }

    void addPop(float x, float y, string text, uint col)
    {
      foreach (Pop q in pops)
        if (!q.on)
        {
          q.x = q.ox = x;
          q.y = q.oy = y;
          q.t = 0;
          q.text = text;
          q.col = col;
          q.on = true;
          return;
        }
    }

    int moorLevel(int r, int c)
    {
      if (r < 0 || r >= ROWS || c < 0 || c >= COLS)
        return -1;
      return map[r][c].type == MOOR ? map[r][c].v : 0;
    }

    // ---- 弾 ----
    void enemyShot(float x, float y, float vx, float vy)
    {
      float k = bulletK();
      foreach (Shot s in theirs)
        if (!s.on)
        {
          s.x = s.ox = x;
          s.y = s.oy = y;
          s.vx = vx * k;
          s.vy = vy * k;
          s.on = true;
          enemyShotSnd = true;
          return;
        }
    }
    void shootAt(float x, float y, float ang, float speed) { enemyShot(x, y, cosf(ang) * speed, sinf(ang) * speed); }
    void aimed(float x, float y, float speed) { shootAt(x, y, atan2f(py - y, px - x), speed); }

    // ---- 円盤 ----
    Ufo addUfo(int type, float x, float y, int t)
    {
      foreach (Ufo u in ufos)
        if (!u.on)
        {
          u.x = u.ox = x;
          u.y = u.oy = y;
          u.vx = u.vy = 0;
          u.home = x;
          u.a = 0;
          u.type = type;
          u.t = t;
          u.hp = UFO_HP[type];
          u.flash = 0;
          u.group = -1;
          u.state = 0;
          u.prop = -1;
          u.rise = 1;
          u.riseKind = 0;
          u.on = true;
          return u;
        }
      return null;
    }

    bool spawnAbductor()
    {
      int best = -1;
      for (int i = 0; i < props.Length; i++)
      {
        Prop p = props[i];
        if (p.on && !p.held && (p.type == SHEEP || p.type == COW) && p.y > 30 && p.y < 130 && p.x > 20 && p.x < W - 20)
        {
          best = i;
          break;
        }
      }
      if (best < 0)
        return false;
      Ufo u = addUfo(ABDUCTOR, props[best].x < W / 2 ? W + 14 : -14, 30, 0);
      if (u != null)
      {
        u.prop = best;
        props[best].held = true;
        return true;
      }
      return false;
    }

    void spawnWave()
    {
      int kind = irand(0, 3);
      if (kind == 3 && spawnAbductor())
        return;
      if (kind == 2 || kind == 3)
      {
        // 3 機の輪
        float cx = frand(50, W - 50), home = frand(70, 140);
        groupN++;
        for (int i = 0; i < 3; i++)
        {
          Ufo u = addUfo(TRIO, cx, -20, 0);
          if (u != null)
          {
            u.vx = cx;
            u.home = home;
            u.a = i * (2 * PI / 3);
            u.group = groupN;
          }
        }
        return;
      }
      // 偵察機が 2〜4 機 (日がたつと増える)、少しずつ遅れて
      for (int i = 0, n = irand(2, 3 + area() - 1) + Math.Min(day - 1, 2); i < n; i++)
      {
        Ufo u = addUfo(SCOUT, 0, -12, -i * 22);
        if (u != null)
        {
          u.home = frand(30, W - 30);
          u.x = u.home;
          u.vy = frand(1.1f, 1.5f);
          u.a = frand(0, 2 * PI);
        }
      }
    }

    void stepUfo(Ufo u)
    {
      if (++u.t < 0)
        return;
      if (u.flash > 0)
        u.flash--;
      if (u.rise < 1) // 地面 (湖) から出てくるところ: 地面と一緒に流れながら浮かびあがる
      {
        u.rise = Math.Min(1, u.rise + 1.0f / (u.riseKind == 1 ? 60 : 45));
        u.y += SPEED;
        if (u.rise >= 1) // 出きったら、ふつうの偵察機として蛇行しながら降りてくる
        {
          u.home = u.x;
          u.a = -(u.t + 1) * 0.05f;
        }
        return;
      }
      switch (u.type)
      {
        case SCOUT:
          u.y += u.vy;
          u.x = u.home + 28 * sinf(u.t * 0.05f + u.a);
          if (u.t % 70 == 40 && u.y > 20 && u.y < H - 120)
            aimed(u.x, u.y, 1.8f);
          break;
        case TRIO:
          {
            float cy = u.t < 120 ? -20 + (u.home + 20) * (u.t / 120.0f) : u.t < 360 ? u.home : u.home - (u.t - 360) * 1.5f;
            float cx = u.vx + 30 * sinf(u.t * 0.012f);
            float ang = u.t * 0.04f + u.a;
            u.x = cx + 20 * cosf(ang);
            u.y = cy + 20 * sinf(ang);
            if (u.t > 120 && u.t < 360 && (u.t + (int)(u.a * 30)) % 90 == 0)
              aimed(u.x, u.y, 1.7f);
            break;
          }
        case ABDUCTOR:
          {
            Prop p = u.prop >= 0 && props[u.prop].on ? props[u.prop] : null;
            if (u.state < 2 && p == null)
              u.state = 2;
            if (u.state == 0)
            {
              // 羊の少し上 (画面では上) へ近づく
              float dx = p.x - u.x, dy = p.y - 26 - u.y, d = sqrtf(dx * dx + dy * dy) + 0.01f;
              if (d < 2.5f)
                u.state = 1;
              else
              {
                u.x += dx / d * 1.6f;
                u.y += dy / d * 1.6f + SPEED;
              }
              if (u.t % 80 == 60 && u.y > 20)
                aimed(u.x, u.y, 1.6f);
            }
            else if (u.state == 1)
            {
              // 光線で持ち上げる。上がりきったら連れ去る
              u.x = p.x;
              u.y = p.y - 26;
              if ((p.lift += 1.0f / 150) >= 1)
              {
                p.on = false;
                u.state = 2;
                // 連れ去られる: 遠ざかる鳴き声
                addPop(p.x, p.y - 22, p.type == SHEEP ? "BAA..." : "MOO...", 0xD8DCE0);
                Sfx(p.type == SHEEP ? SkyLark.Sfx.BaaAway : SkyLark.Sfx.MooAway);
              }
            }
            else
            {
              u.vy = Math.Max(u.vy - 0.08f, -3.0f);
              u.y += u.vy;
            }
            break;
          }
      }
      if (u.y > H + 20 || u.y < -40 || u.x < -30 || u.x > W + 30)
      {
        u.on = false;
        if (u.type == ABDUCTOR && u.prop >= 0)
          props[u.prop].held = false;
      }
    }

    void damageUfo(Ufo u, int n)
    {
      u.flash = 3;
      if ((u.hp -= n) > 0)
      {
        Sfx(SkyLark.Sfx.Hit);
        return;
      }
      u.on = false;
      score += UFO_PTS[u.type];
      Sfx(SkyLark.Sfx.Boom, u.type == ABDUCTOR ? 1 : 0.7f);
      fx.Burst(u.x, u.y, SILVER_L, 12, 1.6f, 18);
      fx.Burst(u.x, u.y, RIM[irand(0, 3)], 6, 1.0f, 14);
      if (u.type == ABDUCTOR && u.prop >= 0)
      {
        Prop p = props[u.prop];
        p.held = false; // 光線が切れて、そっと地面に戻る
        if (u.state == 1 && p.on)
        {
          saved++;
          score += 1000;
          Sfx(SkyLark.Sfx.Saved);
          addPop(p.x, p.y - 10, "SAVED", GOLD);
          // うれしそうに跳ねながら、いちばん近い仲間のところへ戻る
          float best = 60 * 60, tx = p.x, ty = p.y;
          foreach (Prop o in props)
            if (o != p && o.on && o.type == p.type && !o.held)
            {
              float d2 = (o.x - p.x) * (o.x - p.x) + (o.y - p.y) * (o.y - p.y);
              if (d2 < best)
              {
                best = d2;
                tx = o.x;
                ty = o.y;
              }
            }
          p.hop = 80;
          p.vx = constrain((tx - p.x) / 70, -0.4f, 0.4f);
          p.vy = constrain((ty - p.y) / 70, -0.3f, 0.3f);
          p.v = tx >= p.x ? 1 : 0;
        }
      }
      if (u.type == TRIO)
      {
        foreach (Ufo o in ufos)
          if (o.on && o.group == u.group)
            return;
        if (!item.on) // 輪を全部落とすと P
        {
          item.x = item.ox = u.x;
          item.y = item.oy = u.y;
          item.t = 0;
          item.on = true;
        }
      }
    }

    // ---- 母船 ----
    void startMom()
    {
      mom.on = true;
      mom.dead = false;
      mom.x = W / 2.0f;
      mom.y = -50;
      mom.t = -1;
      mom.maxHp = mom.hp = 160 + 50 * (day - 1);
      mom.hitT = mom.deadT = 0;
      Sfx(SkyLark.Sfx.Warning);
    }

    void addPuff(float x, float y, bool smoke, float vx = 0)
    {
      foreach (Puff q in puffs)
        if (!q.on)
        {
          q.x = q.ox = x;
          q.y = q.oy = y;
          q.vx = vx;
          q.r = 1.5f;
          q.t = 0;
          q.life = smoke ? irand(50, 70) : irand(30, 45);
          q.smoke = smoke;
          q.on = true;
          return;
        }
    }

    void damageMom(int n)
    {
      mom.hitT = 2;
      if ((mom.hp -= n) > 0)
      {
        Sfx(SkyLark.Sfx.MomHit);
        return;
      }
      // 落とした: 火を噴き、かけらを撒きながら地面へ落ちていく
      mom.dead = true;
      mom.deadT = CRASH_T;
      score += 30000;
      Sfx(SkyLark.Sfx.BigBoom);
      foreach (Shot s in theirs)
        s.on = false;
      fx.Burst(mom.x, mom.y, 0xFFFFFF, 20, 2.4f, 26);
      foreach (DebrisP d in debris)
      {
        d.x = mom.x + frand(-20, 20);
        d.y = mom.y + frand(-16, 16);
        d.vx = frand(-1.6f, 1.6f);
        d.vy = frand(-1.2f, 1.2f);
        d.alt = 1;
        d.da = 1.0f / irand(40, 100);
        d.ang = frand(0, 2 * PI);
        d.on = true;
        keep(d);
      }
    }

    void stepDebris()
    {
      foreach (DebrisP d in debris)
      {
        if (!d.on)
          continue;
        d.x += d.vx;
        d.y += d.vy + SPEED * (1 - d.alt); // 低くなるほど地面と一緒に流れる
        d.vx *= 0.98f;
        d.vy *= 0.98f;
        d.ang += 0.3f;
        if (frame % 4 == 0)
          addPuff(d.x, d.y, true);
        if ((d.alt -= d.da) <= 0)
        {
          d.on = false;
          addProp(d.x, d.y, DEBRIS);
          fx.Burst(d.x, d.y, 0xFF8A3A, 5, 0.8f, 12);
          Sfx(SkyLark.Sfx.Boom, 0.35f);
        }
      }
    }

    float portAngle(int i) { return mom.t * 0.01f + i * (PI / 2); } // 回る 4 つの砲口

    void stepMom()
    {
      if (mom.hitT > 0)
        mom.hitT--;
      if (mom.dead)
      {
        // 傾いてぐるぐる回りながら、煙の尾を引いて落ちる。低くなるほど地面と一緒に流れる
        float alt = (float)mom.deadT / CRASH_T;
        mom.y += 0.25f + SPEED * (1 - alt);
        mom.x += mom.x < W / 2 ? -0.15f : 0.15f;
        mom.t += 3;
        if (mom.deadT % 3 == 0)
          addPuff(mom.x + frand(-20, 20), mom.y + frand(-14, 14), true);
        if (mom.deadT % 6 == 0)
        {
          fx.Burst(mom.x + frand(-28, 28), mom.y + frand(-20, 20), mom.deadT % 12 != 0 ? GOLD : 0xFF6A30, 6, 1.2f, 18);
          if (mom.deadT % 24 == 0)
            Sfx(SkyLark.Sfx.Boom, 0.5f);
        }
        if (--mom.deadT <= 0)
        {
          // 地面に落ちた: 大きな爆発と光。残骸が残って煙を上げる
          fx.Burst(mom.x, mom.y, 0xFFFFFF, 30, 3.0f, 40);
          fx.Burst(mom.x, mom.y, 0xFF8A3A, 30, 2.2f, 36);
          fx.Burst(mom.x, mom.y, 0x6A5232, 20, 1.4f, 30);
          flashT = 4;
          addProp(mom.x, mom.y, WRECK);
          for (int i = 0; i < 8; i++)
            addPuff(mom.x + frand(-20, 20), mom.y + frand(-12, 12), true);
          Sfx(SkyLark.Sfx.Crash);
          mom.on = false;
        }
        return;
      }
      if (mom.t < 0)
      {
        mom.y += 1.0f;
        if (mom.y >= MOM_Y)
          mom.t = 0;
        return;
      }
      if (++mom.t > MOM_STAY || frame > CLEAR_AT - 4 * FPS)
      {
        mom.y -= 1.2f;
        if (mom.y < -70)
          mom.on = false;
        return;
      }
      mom.x = W / 2 + 26 * sinf(mom.t * 0.012f);
      bool angry = mom.hp < mom.maxHp / 2;
      int u = mom.t % 300, ring = day >= 3 ? 18 : 14;
      if (u % (angry ? 36 : 50) == 0) // 輪
        for (int i = 0; i < ring; i++)
          shootAt(mom.x, mom.y, i * (2 * PI / ring) + mom.t * 0.05f, 1.2f);
      if (u >= 100 && u < 170 && u % 14 == 0) // 回る砲口から狙い撃ち
      {
        float a = portAngle((u / 14) % 4);
        aimed(mom.x + cosf(a) * 22, mom.y + sinf(a) * 22, 2.0f);
      }
      if (u == 220 || u == 260) // ハッチから偵察機を出す
        for (int sd = -1; sd <= 1; sd += 2)
        {
          Ufo s = addUfo(SCOUT, mom.x + sd * 22, mom.y + 10, 0);
          if (s != null)
          {
            s.home = mom.x + sd * 22;
            s.vy = 1.3f;
            s.a = sd > 0 ? PI : 0;
          }
        }
    }

    // ---- 自機 ----
    void killPlayer()
    {
      fx.Burst(px, py, GOLD, 30, 2.0f, 30);
      fx.Burst(px, py, 0xFFFFFF, 12, 1.0f, 20);
      fx.Burst(px, py, 0x6A6A6A, 10, 0.6f, 30); // 煙
      Sfx(SkyLark.Sfx.Death);
      deadT = 60;
      if (demo) // デモは残りがなくてもゲームオーバーにはしない
        lives = Math.Max(0, lives - 1);
      else if (lives == 0)
        outOfLives = true;
      else
        lives--;
      power = Math.Max(1, power - 1);
    }

    void shoot(float x, float y, float vx)
    {
      foreach (Shot s in mine)
        if (!s.on)
        {
          s.x = s.ox = x;
          s.y = s.oy = y;
          s.vx = vx;
          s.vy = -7;
          s.on = true;
          return;
        }
    }

    // 1 回押したぶんの弾。強くなるほど本数が増える
    void volley()
    {
      shoot(px - 5, py - 3, 0);
      shoot(px + 5, py - 3, 0);
      if (power >= 2)
      {
        shoot(px - 8, py - 1, -0.6f);
        shoot(px + 8, py - 1, 0.6f);
      }
      if (power >= 3)
        shoot(px, py - 9, 0);
      muzzleT = 2;
      Sfx(SkyLark.Sfx.Shot);
    }

    void think()
    {
      // 行きたいところ: P があれば取りに行く。なければ羊をさらっている円盤、母船、一番下の円盤の真下
      float tx = W / 2, ty = PY0;
      bool urgent = false;
      if (item.on && item.y > 30 && item.y < H - 20)
      {
        tx = item.x;
        ty = constrain(item.y + 10, PY0 - 70.0f, (float)PY0);
      }
      else
      {
        float lowest = -1e9f;
        foreach (Ufo u in ufos)
          if (u.on && u.t >= 0 && u.type == ABDUCTOR && u.state < 2 && u.y > 0)
          {
            tx = u.x;
            urgent = true;
          }
        if (!urgent)
        {
          if (momAlive() && mom.t >= 0)
          {
            tx = mom.x;
            urgent = true;
          }
          else
            foreach (Ufo u in ufos)
              if (u.on && u.t >= 0 && u.y > 0 && u.y < py - 40 && u.y > lowest)
              {
                lowest = u.y;
                tx = u.x;
              }
        }
      }

      // 候補の位置ごとに、そこへ動くあいだ 1.3 秒先まで弾や円盤に当たらないかを見て、危なさを点数にする。
      // 横と縦は動ける速さが違うので、軸ごとに t フレームでどこまで行けるかを見る
      float best = 1e9f, bx = px, by = py;
      float tw = urgent ? 0.07f : 0.03f; // 羊を助けるときと母船のときは、真下に付くのを強く意識する
      for (int j = -2; j <= 2; j++)
        for (int k = -6; k <= 6; k++)
        {
          float cx = constrain(px + k * 6, 10.0f, W - 10.0f), cy = constrain(py + j * 6, H / 2.0f, H - 18.0f);
          float dx = cx - px, dy = cy - py, danger = 0;
          float cost = fabsf(cx - tx) * tw + fabsf(cy - ty) * 0.02f + (Math.Abs(k) + Math.Abs(j)) * 0.04f;
          foreach (Shot s in theirs)
          {
            if (!s.on || fabsf(s.x - cx) > 90 || (s.y > cy + 20 && s.vy >= 0))
              continue;
            for (int t = 2; t <= 40; t += 2)
            {
              float mx = px + constrain(dx, -2.2f * t, 2.2f * t), my = py + constrain(dy, -1.8f * t, 1.8f * t);
              float ex = s.x + s.vx * t - mx, ey = s.y + s.vy * t - my;
              if (ex * ex + ey * ey < 100)
              {
                danger += 20.0f / (1 + t * 0.15f);
                break;
              }
            }
            if (cost + danger >= best)
              break;
          }
          foreach (Ufo u in ufos) // 体当たりも先読みして避ける (動きはおおよそ)
          {
            if (!u.on || u.t < 0 || cost + danger >= best)
              continue;
            float vy = u.type == SCOUT ? u.vy : u.type == ABDUCTOR ? (u.state == 2 ? u.vy : SPEED) : 0;
            for (int t = 0; t <= 30; t += 10)
            {
              float mx = px + constrain(dx, -2.2f * t, 2.2f * t), my = py + constrain(dy, -1.8f * t, 1.8f * t);
              if (fabsf(u.x - mx) < 16 && fabsf(u.y + vy * t - my) < 18)
              {
                danger += 12.0f / (1 + t * 0.1f);
                break;
              }
            }
          }
          if (cost + danger < best)
          {
            best = cost + danger;
            bx = cx;
            by = cy;
          }
        }
      px += constrain(bx - px, -2.2f, 2.2f);
      py += constrain(by - py, -1.8f, 1.8f);
    }

    // ボタンを押して撃っているように: 3〜6 回連打しては少し指を休める。撃つ相手がいないときは押さない
    void trigger()
    {
      if (tapT > 0)
      {
        tapT--;
        return;
      }
      bool target = momAlive() && mom.t >= 0;
      foreach (Ufo u in ufos)
        if (u.on && u.t >= 0 && u.y > 0 && u.y < py)
          target = true;
      if (!target)
        return;
      volley();
      if (++taps >= tapsMax)
      {
        taps = 0;
        tapsMax = irand(3, 6);
        tapT = irand(12, 24);
      }
      else
        tapT = irand(4, 7);
    }

    // 人の操作: 十字で動かし (Shift / X でゆっくり)、Z を押しているあいだ撃ちつづける。押した瞬間はすぐ撃つ
    void control(Input inp)
    {
      float mx = (inp.Right ? 1 : 0) - (inp.Left ? 1 : 0), my = (inp.Down ? 1 : 0) - (inp.Up ? 1 : 0);
      if (mx == 0 && my == 0)
      {
        mx = inp.AX;
        my = inp.AY;
      }
      float len = sqrtf(mx * mx + my * my);
      if (len > 1)
      {
        mx /= len;
        my /= len;
      }
      float sp = inp.Slow ? 1.4f : 2.9f;
      px = constrain(px + mx * sp, 8.0f, W - 8.0f);
      py = constrain(py + my * sp * 0.9f, 40.0f, H - 14.0f);
      if (fireCd > 0)
        fireCd--;
      if (inp.FirePressed && fireCd <= 2)
        fireCd = 0;
      if (inp.Fire && fireCd == 0)
      {
        volley();
        fireCd = 4;
      }
    }

    // ---- 遊びの切り替え ----
    // 世界を作り直す (地形、空のもの、天気)。自機の点や残機はそのまま
    void resetWorld()
    {
      seed = rnd() * 2654435761u;
      frame = 0;
      rowCount = 0;
      riverOn = laneOn = false;
      villageLen = 0;
      stonePlanned = lochPlanned = stoneFound = nessieFound = false;
      cropPlanned = cropTopNext = cropFound = beastDone = beastFound = false;
      cropLen = 0;
      plainLen = lochLen = 0;
      splitSegs(0, COLS);
      foreach (Prop p in props)
        p.on = false;
      foreach (Ufo u in ufos)
        u.on = false;
      foreach (Shot s in mine)
        s.on = false;
      foreach (Shot s in theirs)
        s.on = false;
      foreach (Pop q in pops)
        q.on = false;
      for (int i = 0; i < clouds.Length; i++)
      {
        clouds[i].x = frand(0, W);
        clouds[i].y = frand(-H, H);
        clouds[i].r = i < 3 ? irand(8, 14) : 0;
      }
      railRow = railPending = false;
      flock.on = train.on = false;
      foreach (Puff q in puffs)
        q.on = false;
      foreach (DebrisP d in debris)
        d.on = false;
      foreach (Drop d in drops)
      {
        d.x = frand(0, W + 60);
        d.y = frand(0, H);
      }
      birdT = 12 * FPS;
      flashT = thunderT = 0;
      item.on = false;
      mom.on = false;
      scrollY = 0;
      for (int r = ROWS - 1; r >= 0; r--) // 下の行から作る (上が新しい)
      {
        rowId[r] = rowCount++;
        genRow(map[r], rowId[r]);
      }
      tapT = taps = muzzleT = 0;
      tapsMax = 4;
      waveT = 4 * FPS;
      groupN = 0;
      saved = 0;
      extendT = 0;
      newRank = -1;
      fx.Clear();
      openAt = 0;
      invalidateRows();
    }

    void StartDemo()
    {
      mode = Mode.Attract;
      demo = true;
      Paused = false;
      gone = outOfLives = false;
      day = 1;
      if (!fixedSeed)
        rngState ^= (uint)Environment.TickCount * 2654435761u;
      if (rngState == 0)
        rngState = 1;
      resetWorld();
      px = W / 2;
      py = H + 30;
      invT = deadT = enterT = 0;
      lives = 3;
      power = 1;
      score = 0;
      nextExtend = 40000;
    }

    void NewGame()
    {
      mode = Mode.Play;
      demo = false;
      Paused = false;
      gone = outOfLives = false;
      day = 1;
      continues = 0;
      resetWorld();
      frame = openAt = TITLE_END; // タイトルは飛ばして、幕を開けながら AREA 1 から
      lives = 2;
      power = 1;
      score = 0;
      nextExtend = 40000;
      deadT = invT = 0;
      fireCd = 0;
      flyIn();
      Sfx(SkyLark.Sfx.Start);
    }

    // 次の日: 地形も天気も新しくして、朝の AREA 1 から。点と残機と強さは持ちこす
    void NextDay()
    {
      day++;
      resetWorld();
      frame = openAt = TITLE_END;
      deadT = 0;
      flyIn();
      Sfx(SkyLark.Sfx.Start);
    }

    // 画面の下から飛んで入ってくる
    void flyIn()
    {
      px = W / 2;
      py = H + 20;
      enterT = 40;
      invT = 40 + 60;
    }

    void Continue()
    {
      mode = Mode.Play;
      outOfLives = false;
      lives = 2;
      continues++;
      if (score % 10 < 9) // 点のいちばん下の桁は、コンティニューした回数
        score++;
      flyIn();
      Sfx(SkyLark.Sfx.Start);
    }

    void EndGame()
    {
      gone = true;
      mode = Mode.GameOver;
      modeT = 4 * FPS;
      Sfx(SkyLark.Sfx.GameOver);
    }

    int RankOf(int s)
    {
      for (int i = 0; i < 5; i++)
        if (s > Table[i].Score)
          return i;
      return -1;
    }

    void FinishEntry()
    {
      string name = new string(entry).TrimEnd();
      if (name.Length == 0)
        name = "---";
      LastName = new string(entry);
      int i = RankOf(score);
      if (i >= 0)
      {
        for (int j = 4; j > i; j--)
          Table[j] = Table[j - 1];
        Table[i] = new Rank(score, name);
        newRank = i;
        if (TableChanged != null)
          TableChanged();
      }
      mode = Mode.Ranking;
      modeT = 8 * FPS;
      Sfx(SkyLark.Sfx.Extend);
    }

    // ---- 1 フレーム進める (1 秒 30 回) ----
    // 確かめる用: いま画面にいる牧羊犬・カモ・パブ・洗濯物・跳ねている羊の数
    public string Census()
    {
      int dog = 0, duck = 0, pub = 0, laundry = 0, hop = 0, beam = 0, stone = 0, nessie = 0, beast = 0, crop = 0;
      foreach (Prop p in props)
        if (p.on && p.y > 10 && p.y < H - 10)
        {
          if (p.type == DOG) dog++;
          if (p.type == DUCK) duck++;
          if (p.type == PUB) pub++;
          if (p.type == COTTAGE && (p.v & LAUNDRY) != 0) laundry++;
          if (p.hop > 0) hop++;
          if (p.type == STONEHENGE) stone++;
          if (p.type == NESSIE) nessie++;
          if (p.type == BEAST) beast++;
          if (p.type == CROPCIRCLE) crop++;
        }
      int riseS = 0, riseL = 0;
      foreach (Ufo u in ufos)
      {
        if (u.on && u.type == ABDUCTOR && u.state == 1)
          beam++;
        if (u.on && u.rise < 1)
        {
          if (u.riseKind == 0) riseS++; else riseL++;
        }
      }
      return "dog " + dog + " duck " + duck + " pub " + pub + " laundry " + laundry + " hop " + hop + " beam " + beam + " stone " + stone + " nessie " + nessie + " beast " + beast + " crop " + crop + " riseS " + riseS + " riseL " + riseL + (stoneFound ? " FOUND-STONE" : "") + (nessieFound ? " FOUND-NESSIE" : "");
    }

    public void Reseed(uint s) // 確かめる用: 乱数の種を決めてデモを始めなおす
    {
      rngState = s | 1;
      fixedSeed = true;
      StartDemo();
    }

    public int Score { get { return score; } }
    public int Day { get { return day; } }

    public void Step(Input inp)
    {
      enemyShotSnd = false;
      worldMoved = false;
      switch (mode)
      {
        case Mode.Attract:
          if (inp.StartPressed || inp.FirePressed)
          {
            NewGame();
            break;
          }
          StepWorld(null);
          if (frame >= LOOP_AT)
            StartDemo();
          break;
        case Mode.Play:
          if (inp.PausePressed)
          {
            Paused = !Paused;
            Sfx(SkyLark.Sfx.Pause);
          }
          if (Paused)
          {
            if (inp.QuitPressed)
              StartDemo();
            break;
          }
          StepWorld(inp);
          if (frame == RANK_AT) // 遊んでいるときはランキングを出さずに、幕を閉じて次の日へ
            frame = LOOP_AT - SHUTTER;
          if (frame >= LOOP_AT)
            NextDay();
          break;
        case Mode.Continue:
          if (inp.FirePressed || inp.StartPressed)
          {
            Continue();
            break;
          }
          if (inp.PausePressed || inp.QuitPressed) // やめる
            modeT = 1;
          if (modeT % FPS == 0 && modeT > 0)
            Sfx(SkyLark.Sfx.Tick);
          if (--modeT <= 0)
            EndGame();
          break;
        case Mode.GameOver:
          StepWorld(null);
          if (--modeT <= 0 || (modeT < 3 * FPS && (inp.FirePressed || inp.StartPressed)))
          {
            if (RankOf(score) >= 0)
            {
              mode = Mode.NameEntry;
              modeT = 40 * FPS;
              for (int i = 0; i < 3; i++)
                entry[i] = i < LastName.Length && LETTERS.IndexOf(LastName[i]) >= 0 ? LastName[i] : 'A';
              entryPos = 0;
            }
            else
            {
              mode = Mode.Ranking;
              newRank = -1;
              modeT = 6 * FPS;
            }
          }
          break;
        case Mode.NameEntry:
          StepWorld(null);
          StepEntry(inp);
          break;
        case Mode.Ranking:
          StepWorld(null);
          if (--modeT <= 0 || (modeT < 7 * FPS && (inp.FirePressed || inp.StartPressed)))
            StartDemo();
          break;
      }
      if (!worldMoved) // 止まっているあいだは、補わずにそのまま描く
      {
        keepAll();
        scrollAdv = 0;
      }
      UpdateSound();
    }

    // ---- 1 秒 60 コマで描くための補い ----
    static void keep(Body b)
    {
      b.ox = b.x;
      b.oy = b.y;
    }
    // 描く位置: 前と今のあいだを a (0〜1) で補う。大きく飛んだとき (出てきた、上へ回りこんだ) は今の位置
    static void lerp(Body b, float a)
    {
      if (Math.Abs(b.x - b.ox) < 24 && Math.Abs(b.y - b.oy) < 24)
      {
        b.rx = b.ox + (b.x - b.ox) * a;
        b.ry = b.oy + (b.y - b.oy) * a;
      }
      else
      {
        b.rx = b.x;
        b.ry = b.y;
      }
    }
    static void keepAll(Body[] a)
    {
      foreach (Body b in a)
        keep(b);
    }
    static void lerpAll(Body[] a, float t)
    {
      foreach (Body b in a)
        lerp(b, t);
    }
    void keepAll()
    {
      keepAll(props);
      keepAll(ufos);
      keepAll(mine);
      keepAll(theirs);
      keepAll(clouds);
      keepAll(pops);
      keepAll(puffs);
      keepAll(debris);
      keepAll(drops);
      keep(item);
      keep(flock);
      keep(train);
      keep(mom);
      fx.Keep();
      opx = px;
      opy = py;
    }
    void lerpAll(float a)
    {
      lerpAll(props, a);
      lerpAll(ufos, a);
      lerpAll(mine, a);
      lerpAll(theirs, a);
      lerpAll(clouds, a);
      lerpAll(pops, a);
      lerpAll(puffs, a);
      lerpAll(debris, a);
      lerpAll(drops, a);
      lerp(item, a);
      lerp(flock, a);
      lerp(train, a);
      lerp(mom, a);
      fx.Lerp(a);
      bool near = Math.Abs(px - opx) < 24 && Math.Abs(py - opy) < 24;
      rpx = near ? opx + (px - opx) * a : px;
      rpy = near ? opy + (py - opy) * a : py;
      rscroll = scrollY - scrollAdv * (1 - a);
    }

    // 名前の 3 文字: 上下で文字を選び、Z で決めて次の文字へ (X / Back で戻る)。キーボードで直接打ってもよい
    void StepEntry(Input inp)
    {
      if (--modeT <= 0)
      {
        FinishEntry();
        return;
      }
      int dir = 0;
      if (inp.UpPressed)
        dir = 1;
      else if (inp.DownPressed)
        dir = -1;
      else if (inp.Up || inp.Down)
      {
        if (++entryRepeat > 8 && entryRepeat % 3 == 0)
          dir = inp.Up ? 1 : -1;
      }
      if (!inp.Up && !inp.Down)
        entryRepeat = 0;
      if (dir != 0)
      {
        int k = LETTERS.IndexOf(entry[entryPos]);
        entry[entryPos] = LETTERS[(k + dir + LETTERS.Length) % LETTERS.Length];
        Sfx(SkyLark.Sfx.Select);
      }
      if (inp.Typed != 0)
      {
        char ch = char.ToUpperInvariant(inp.Typed);
        if (ch == '\b')
        {
          entryPos = Math.Max(0, entryPos - 1);
          Sfx(SkyLark.Sfx.Select);
        }
        else if (ch == '\r')
        {
          FinishEntry();
          return;
        }
        else if (LETTERS.IndexOf(ch) >= 0)
        {
          entry[entryPos] = ch;
          Sfx(SkyLark.Sfx.Select);
          if (++entryPos > 2)
          {
            FinishEntry();
            return;
          }
        }
        return;
      }
      if (inp.LeftPressed || inp.BackPressed)
      {
        entryPos = Math.Max(0, entryPos - 1);
        Sfx(SkyLark.Sfx.Select);
      }
      if (inp.RightPressed && entryPos < 2)
      {
        entryPos++;
        Sfx(SkyLark.Sfx.Select);
      }
      if (inp.FirePressed)
      {
        Sfx(SkyLark.Sfx.Select);
        if (++entryPos > 2)
          FinishEntry();
      }
      else if (inp.StartPressed)
        FinishEntry();
    }

    // 鳴りつづける音の大きさを決める
    void UpdateSound()
    {
      if (Snd == null)
        return;
      bool frozen = Paused || mode == Mode.Continue;
      float k = frozen ? 0 : demo ? 0.5f : 1;
      bool plane = !gone && deadT == 0 && py < H + 8 && (mode == Mode.Play || mode == Mode.Attract);
      Snd.Engine = plane ? k : 0;
      // プロペラの音程: 前へ出ると上がり、うしろへ下がると下がり、横へ旋回すると少し上ずる (動いた量から決めるので、デモも同じ)
      if (plane && !frozen)
      {
        float mx = px - opx, my = py - opy;
        float target = 1 - constrain(my, -3, 3) * 0.075f + Math.Min(3, Math.Abs(mx)) * 0.03f;
        enginePitch += (target - enginePitch) * 0.15f;
        Snd.EnginePitch = enginePitch;
        Snd.EngineBank = Math.Min(1, Math.Abs(mx) / 2.9f);
      }
      Snd.Rain = rainK() * k;
      int n = 0;
      bool beam = false;
      foreach (Ufo u in ufos)
        if (u.on && u.t >= 0 && u.y > -10 && u.y < H)
        {
          n++;
          if (u.type == ABDUCTOR && u.state == 1)
            beam = true;
        }
      Snd.UfoHum = Math.Min(1, n / 3.0f) * k;
      Snd.Beam = beam ? k : 0;
      Snd.MomHum = (momAlive() ? 1 : mom.on && mom.dead ? momAlt() : 0) * k;
      Snd.Birds = (1 - rainK()) * (1 - duskK()) * (1 - 0.6f * sunsetK()) * (momAlive() ? 0.2f : 1) * k;
      if (frozen)
        return;
      if (enemyShotSnd)
        Sfx(SkyLark.Sfx.EnemyShot);
      if (thunderT > 0 && --thunderT == 0)
        Sfx(SkyLark.Sfx.Thunder);
    }

    // 世界を 1 フレーム進める。inp が null のときは自動操縦 (デモ) か、自機のいない世界
    void StepWorld(Input inp)
    {
      if (frame >= LOOP_AT || ++frame >= LOOP_AT) // 1 日の終わり。次への切り替えは Step がする
        return;
      keepAll();
      scrollAdv = SPEED;
      worldMoved = true;
      fx.Step();
      if (extendT > 0)
        extendT--;
      if (muzzleT > 0)
        muzzleT--;
      scrollY += SPEED;
      if (scrollY >= T)
      {
        scrollY -= T;
        newRow();
      }
      foreach (Prop p in props)
      {
        if (!p.on)
          continue;
        p.y += SPEED;
        if (!p.held && p.lift > 0)
          p.lift = Math.Max(0.0f, p.lift - 0.03f);
        if (p.hop > 0) // 跳ねながら仲間のところへ
        {
          p.hop--;
          if (p.lift == 0)
          {
            p.x += p.vx;
            p.y += p.vy;
          }
        }
        else if ((p.type == SHEEP || p.type == COW) && !p.held) // のんびり歩く
          p.x += 0.06f * sinf((frame + p.v * 97 + (int)p.y * 13) * 0.01f);
        if (p.type == DOG) // 群れのまわりを 8 の字に走りまわる
        {
          p.ay += SPEED;
          p.t++;
          float nx = p.ax + 17 * cosf(p.t * 0.045f), ny = p.ay + 8 * sinf(p.t * 0.09f);
          p.v = nx >= p.x ? 1 : 0;
          p.x = nx;
          p.y = ny;
          if (irand(0, 170) == 0 && p.y > 0 && p.y < H)
            Sfx(SkyLark.Sfx.Woof, 0.6f);
        }
        else if (p.type == BEAST) // ボドミンの野獣: 駆けてきて、途中で座ってこちらを見て、また走り去る
        {
          p.t++;
          if (p.t < 60 || p.t >= 130)
            p.x += (p.v != 0 ? 1 : -1) * 1.6f;
          if (p.t == 60 && p.y > 0 && p.y < H)
            Sfx(SkyLark.Sfx.Growl);
        }
        else if (p.type == CROPCIRCLE && p.y > -20) // 画面の上の端まで来てから描きはじめる
          p.t++;
        else if (p.type == NESSIE) // 湖をゆっくり回りながら、ときどき水から出て、また沈む
        {
          p.ay += SPEED;
          p.t++;
          float ang = p.t * 0.009f, nx = p.ax + 30 * cosf(ang), ny = p.ay + 26 * sinf(ang);
          p.v = nx >= p.x ? 1 : 0;
          p.x = nx;
          p.y = ny;
          int ph_ = p.t % 560;
          p.lift = ph_ < 50 ? ph_ / 50.0f : ph_ < 360 ? 1 : ph_ < 420 ? 1 - (ph_ - 360) / 60.0f : 0;
          if (ph_ == 1 && p.y > 0 && p.y < H)
            Sfx(SkyLark.Sfx.Splash);
        }
        else if (p.type == DUCK)
        {
          if (p.y > -10) // 画面の上の端まで来てから泳ぎだす
            p.x += p.vx;
          if (p.v == 0 && irand(0, 220) == 0 && p.y > 0 && p.y < H)
            Sfx(SkyLark.Sfx.Quack, 0.6f);
        }
        if (p.y > H + (p.type == STONEHENGE ? 40 : 16) || p.x < -30 || p.x > W + 30)
          p.on = false;
      }
      if (!beastDone && frame >= BEAST_AT && !gone) // ボドミンの野獣が画面の端から駆けてくる
      {
        beastDone = true;
        int dir = irand(0, 1) != 0 ? 1 : -1;
        Prop b = addProp(dir > 0 ? -12 : W + 12, frand(50, 110), BEAST, dir > 0 ? 1 : 0);
        if (b != null)
          b.ox = b.x;
      }
      foreach (Prop p in props) // 名所から円盤が湧いてくる: ストーンヘンジは真ん中が光って地面から、湖は水の下から浮上
      {
        if (!p.on || (p.type != STONEHENGE && p.type != NESSIE))
          continue;
        if (p.type == STONEHENGE)
          p.t++;
        bool canSpawn = active() && !momAlive() && p.y > 30 && p.y < H - 130 && p.spawned < (p.type == STONEHENGE ? 6 : 5);
        int period = p.type == STONEHENGE ? 80 : 95;
        if (canSpawn && (p.type == STONEHENGE ? p.t : p.t + 30) % period == period / 2)
        {
          float sx = p.type == STONEHENGE ? p.x : p.ax + frand(-26, 26), sy = p.type == STONEHENGE ? p.y - 2 : p.ay + frand(-30, 30);
          if (p.type == NESSIE && tileTypeAt(sx, sy) != WATER)
            continue;
          Ufo u = addUfo(SCOUT, sx, sy, 0);
          if (u != null)
          {
            u.rise = 0;
            u.riseKind = p.type == STONEHENGE ? 0 : 1;
            u.vy = frand(0.8f, 1.1f);
            u.home = sx;
            p.spawned++;
            Sfx(p.type == STONEHENGE ? SkyLark.Sfx.Emerge : SkyLark.Sfx.Splash, 0.8f);
          }
        }
      }
      if (!gone && deadT == 0 && playing()) // 名所の真上を飛ぶと、見つけたボーナス (1 日 1 回ずつ)
        foreach (Prop p in props)
        {
          if (!p.on)
            continue;
          if (p.type == STONEHENGE && !stoneFound && fabsf(px - p.x) < 14 && fabsf(py - p.y) < 14)
          {
            stoneFound = true;
            score += 3000;
            addPop(p.x, p.y - 24, "STONEHENGE", GOLD);
            Sfx(SkyLark.Sfx.Discover);
          }
          else if (p.type == BEAST && !beastFound && fabsf(px - p.x) < 16 && fabsf(py - p.y) < 16)
          {
            beastFound = true;
            score += 4000;
            addPop(p.x, p.y - 16, "BODMIN BEAST", 0xE8D040);
            Sfx(SkyLark.Sfx.Discover);
          }
          else if (p.type == CROPCIRCLE && !cropFound && p.t > 50 && fabsf(px - p.x) < 20 && fabsf(py - p.y) < 20)
          {
            cropFound = true;
            score += 3000;
            addPop(p.x, p.y - 26, "CROP CIRCLE", GOLD);
            Sfx(SkyLark.Sfx.Discover);
          }
          else if (p.type == NESSIE && !nessieFound && p.lift > 0.6f && fabsf(px - p.x) < 18 && fabsf(py - p.y) < 22)
          {
            nessieFound = true;
            score += 5000;
            addPop(p.x, p.y - 20, "NESSIE!", 0x9AF0B0);
            Sfx(SkyLark.Sfx.Discover);
          }
        }
      foreach (Prop d in props) // 牧羊犬や野獣が近づくと、羊は逃げる (野獣からは遠くから、速く)
        if (d.on && (d.type == DOG || d.type == BEAST))
          foreach (Prop sh in props)
            if (sh.on && sh.type == SHEEP && !sh.held && sh.hop == 0)
            {
              float dx = sh.x - d.x, dy = sh.y - d.y, d2 = dx * dx + dy * dy;
              float far = d.type == BEAST ? 26 : 12, push = d.type == BEAST ? 0.7f : 0.35f;
              if (d2 < far * far && d2 > 0.01f)
              {
                float dist = sqrtf(d2);
                sh.x += dx / dist * push;
                sh.y += dy / dist * push * 0.7f;
                sh.v = dx > 0 ? 1 : 0;
              }
            }
      float rk = rainK();
      for (int i = 0; i < clouds.Length; i++) // 雲は地面より速く流れる。雨のときは雲が増え、大きくなる (r = 0 は出ていない雲)
      {
        Cloud c = clouds[i];
        if ((c.y += 1.3f + rk * 0.5f) > H + 30)
        {
          c.x = frand(10, W - 10);
          c.y = -30 - frand(0, rk > 0.3f ? 60 : 200);
          c.r = i < 3 || rk > 0.3f ? irand(8, 14) + (int)(rk * 6) : 0;
        }
      }
      if (flashT > 0)
        flashT--;
      if (rk > 0.8f && irand(0, 140) == 0) // 稲光。少し遅れて雷が鳴る
      {
        flashT = 3;
        thunderT = irand(8, 40);
      }
      if (rk > 0)
        foreach (Drop d in drops)
        {
          d.x -= 1.5f;
          if ((d.y += 9) > H || d.x < -4)
          {
            d.x = frand(0, W + 60);
            d.y = frand(-20, 0);
          }
        }

      // 渡り鳥: ときどき群れが横切る
      if (!flock.on && frame > TITLE_END && --birdT <= 0)
      {
        int dir = irand(0, 1) != 0 ? 1 : -1;
        flock.x = dir > 0 ? -30.0f : W + 30.0f;
        flock.y = frand(40, H - 120);
        flock.vx = dir * frand(0.8f, 1.1f);
        flock.vy = frand(-0.2f, 0.4f);
        flock.n = irand(5, 9);
        flock.t = 0;
        flock.on = true;
        birdT = irand(15 * FPS, 25 * FPS);
      }
      if (flock.on)
      {
        flock.x += flock.vx;
        flock.y += flock.vy;
        flock.t++;
        if (flock.x < -90 || flock.x > W + 90)
          flock.on = false;
      }

      if (railPending && (railY += SPEED) >= -2)
      {
        railPending = false;
        int dir = irand(0, 1) != 0 ? 1 : -1;
        train.x = train.ox = dir > 0 ? -4.0f : W + 4.0f;
        train.y = train.oy = railY;
        train.dir = dir;
        train.cars = irand(2, 4);
        train.on = true;
        Sfx(SkyLark.Sfx.Whistle);
      }
      // 蒸気機関車
      if (train.on)
      {
        train.y += SPEED;
        train.x += train.dir * 1.1f;
        if (frame % 5 == 0)
          addPuff(train.x - train.dir * 4, train.y, false, -train.dir * 0.3f);
        if (train.y > H + 10 || (train.dir > 0 ? train.x > W + 100 : train.x < -100))
          train.on = false;
      }
      foreach (Puff q in puffs)
        if (q.on)
        {
          q.y += SPEED - (q.smoke ? 0.15f : 0);
          q.x += q.vx;
          q.r += q.smoke ? 0.1f : 0.12f;
          if (++q.t > q.life)
            q.on = false;
        }
      stepDebris();
      foreach (Prop p in props) // 残骸から煙
        if (p.on && p.type == WRECK && frame % 5 == 0)
          addPuff(p.x + frand(-14, 14), p.y + frand(-8, 8), true);
      foreach (Pop q in pops)
        if (q.on)
        {
          q.y -= 0.4f;
          if (++q.t > 50)
            q.on = false;
        }
      if (frame == BONUS_AT && !gone)
        score += saved * 1000;
      if (frame == RANK_AT)
        newRank = -1;

      // 円盤: ときどき来る。母船のあいだは来ない
      if (!mom.on && frame == MOM_AT && !gone)
        startMom();
      if (mom.on)
        stepMom();
      if (active() && !momAlive() && --waveT <= 0)
      {
        spawnWave();
        waveT = (int)((area() == 1 ? irand(5 * FPS, 9 * FPS) : irand(4 * FPS, 7 * FPS)) * waveK());
      }
      foreach (Ufo u in ufos)
        if (u.on)
          stepUfo(u);

      if (God && invT < 2)
        invT = 2;
      bool alive = !gone && deadT == 0 && enterT == 0 && playing();
      if (item.on)
      {
        item.t++;
        item.y += 0.6f;
        item.x += sinf(item.t * 0.08f) * 0.5f;
        if (item.y > H + 10)
          item.on = false;
        else if (alive && fabsf(item.x - px) < 11 && fabsf(item.y - py) < 11)
        {
          item.on = false;
          if (power < 3)
            power++;
          else
            score += 2000;
          fx.Burst(item.x, item.y, GOLD, 8, 1.2f, 12);
          Sfx(SkyLark.Sfx.Power);
        }
      }

      foreach (Shot s in theirs)
        if (s.on)
        {
          s.x += s.vx;
          s.y += s.vy;
          if (s.y > H + 4 || s.y < -4 || s.x < -4 || s.x > W + 4)
            s.on = false;
          else if (alive && invT == 0 && fabsf(s.x - px) < 3 && fabsf(s.y - py) < 4)
          {
            s.on = false;
            killPlayer();
            alive = false;
          }
        }
      foreach (Shot s in mine)
      {
        if (!s.on)
          continue;
        s.x += s.vx;
        s.y += s.vy;
        if (s.y < -6 || s.x < -4 || s.x > W + 4)
        {
          s.on = false;
          continue;
        }
        foreach (Ufo u in ufos)
          if (u.on && u.t >= 0 && u.rise >= 0.7f && fabsf(s.x - u.x) < UFO_R[u.type] && fabsf(s.y - u.y) < UFO_R[u.type])
          {
            s.on = false;
            damageUfo(u, 1);
            break;
          }
        if (s.on && momAlive() && mom.t >= 0)
        {
          float dx = s.x - mom.x, dy = s.y - mom.y;
          if (dx * dx + dy * dy < 34 * 34)
          {
            s.on = false;
            damageMom(1);
            if ((mom.hp & 3) == 0)
              fx.Burst(s.x, s.y, GOLD, 1, 0.8f, 6);
          }
        }
      }
      if (!gone && score >= nextExtend) // 4 万点と、そのあと 10 万点ごとに 1 機ふえる
      {
        nextExtend += 100000;
        lives++;
        extendT = 60;
        Sfx(SkyLark.Sfx.Extend);
      }

      // 自機
      if (gone)
        return;
      if (deadT > 0)
      {
        if (--deadT == 0)
        {
          if (outOfLives)
          {
            mode = Mode.Continue;
            modeT = 10 * FPS - 1;
            return;
          }
          if (demo)
          {
            px = W / 2;
            py = PY0;
            invT = 100;
          }
          else
            flyIn();
        }
        return;
      }
      if (invT > 0)
        invT--;
      if (frame < TITLE_END)
      {
        if (frame > 110)
          py = Math.Max((float)PY0, py - 1.0f);
        return;
      }
      if (frame >= CLEAR_AT)
      {
        // 真ん中に寄って、前へ飛び去る
        px += constrain(W / 2 - px, -1.0f, 1.0f);
        if (frame > CLEAR_AT + 45)
          py -= Math.Min(5.0f, (frame - CLEAR_AT - 45) * 0.12f);
        return;
      }
      if (enterT > 0)
      {
        enterT--;
        py = Math.Max((float)PY0, py - 3.0f);
        if (py <= PY0)
          enterT = 0;
        return;
      }
      if (inp == null)
      {
        think();
        trigger();
      }
      else
        control(inp);
      foreach (Ufo u in ufos) // 体当たり
        if (u.on && u.t >= 0 && u.rise >= 1 && invT == 0 && fabsf(u.x - px) < 8 && fabsf(u.y - py) < 8)
        {
          damageUfo(u, 10);
          killPlayer();
          break;
        }
    }

    float momAlt() { return mom.dead ? (float)mom.deadT / CRASH_T : 1.0f; } // 母船の高さ (1 = 浮いている 〜 0 = 地面)
  }
}
