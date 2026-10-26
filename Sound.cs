// 音。素材のファイルは使わず、全部その場で合成する。winmm の waveOut に、裏のスレッドで混ぜた音を流し続ける。
// 鳴らしきりの音 (機銃、爆発、汽笛など) は起動時に波形を作っておき、鳴りつづける音 (プロペラ、雨、円盤のうなり、
// 小鳥のさえずり) はゲームが毎フレーム音量を決めて、混ぜるときに作る
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace SkyLark
{
  public enum Sfx
  {
    Shot,
    Hit,
    MomHit,
    Boom,
    BigBoom,
    Crash,
    Death,
    Power,
    Saved,
    Extend,
    Warning,
    Whistle,
    Thunder,
    EnemyShot,
    Start,
    GameOver,
    Tick,
    Select,
    Pause,
    BaaAway,
    MooAway,
    Woof,
    Quack,
    Discover,
    Splash,
    Emerge,
    Growl,
    Count
  }

  public class Sound : IDisposable
  {
    const int SR = 22050;
    const int BUF = 512;  // 1 つの送り枠の長さ (約 23ms)
    const int NBUF = 4;

    // ---- 鳴りつづける音の大きさ (0〜1)。ゲームのスレッドが書き、音のスレッドが読む ----
    public volatile float Engine, Rain, UfoHum, Beam, MomHum, Birds;
    public volatile float EnginePitch = 1, EngineBank; // プロペラの音程 (1 = ふつう) と旋回の度合い (0〜1)
    public volatile float Master = 1;
    public volatile bool Muted;

    readonly float[][] waves = new float[(int)Sfx.Count][];
    readonly float[] waveVol = new float[(int)Sfx.Count];
    class Voice
    {
      public float[] w;
      public int pos;
      public float vol;
    }
    readonly List<Voice> voices = new List<Voice>();
    readonly List<Voice> pending = new List<Voice>();
    readonly object lk = new object();
    readonly Random rng = new Random();

    IntPtr hwo;
    IntPtr[] hdrs = new IntPtr[NBUF];
    IntPtr[] datas = new IntPtr[NBUF];
    int flagsOfs;
    Thread th;
    volatile bool running;
    readonly short[] pcm = new short[BUF];
    readonly float[] mixBuf = new float[BUF];

    // 鳴りつづける音の作りかけの状態
    double engPh, engLfo, ufoPh, ufoLfo, beamPh, momPh;
    float rainLp, rainHp, rainLp2;
    float engOut;
    float bird; // 小鳥: 鳴いている声の残り
    double birdPh, birdF, birdDf;
    int birdLeft, birdGap = SR;
    float curEngine, curRain, curUfo, curBeam, curMom, curBirds;
    float curPitch = 1, curBank;

    public Sound()
    {
      MakeWaves();
      try
      {
        Open();
      }
      catch (Exception)
      {
        hwo = IntPtr.Zero; // 音が出せない PC でも遊べるようにする
      }
    }

    public void Play(Sfx s) { Play(s, 1); }
    public void Play(Sfx s, float vol)
    {
      if (hwo == IntPtr.Zero || Muted)
        return;
      float[] w = waves[(int)s];
      lock (lk)
      {
        // 同じ音を同時にいくつも重ねない (機銃や円盤の弾が鳴りすぎないように)
        int same = 0;
        foreach (Voice v in voices)
          if (v.w == w && v.pos < SR / 20)
            same++;
        foreach (Voice v in pending)
          if (v.w == w)
            same++;
        if (same >= 2)
          return;
        Voice nv = new Voice();
        nv.w = w;
        nv.vol = vol * waveVol[(int)s];
        pending.Add(nv);
      }
    }

    // ---- waveOut ----
    [StructLayout(LayoutKind.Sequential)]
    struct WAVEFORMATEX
    {
      public ushort wFormatTag, nChannels;
      public uint nSamplesPerSec, nAvgBytesPerSec;
      public ushort nBlockAlign, wBitsPerSample, cbSize;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct WAVEHDR
    {
      public IntPtr lpData;
      public uint dwBufferLength, dwBytesRecorded;
      public IntPtr dwUser;
      public uint dwFlags, dwLoops;
      public IntPtr lpNext, reserved;
    }
    [DllImport("winmm.dll")] static extern int waveOutOpen(out IntPtr hwo, int dev, ref WAVEFORMATEX fmt, IntPtr cb, IntPtr inst, int flags);
    [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr hwo, IntPtr hdr, int size);
    [DllImport("winmm.dll")] static extern int waveOutUnprepareHeader(IntPtr hwo, IntPtr hdr, int size);
    [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr hwo, IntPtr hdr, int size);
    [DllImport("winmm.dll")] static extern int waveOutReset(IntPtr hwo);
    [DllImport("winmm.dll")] static extern int waveOutClose(IntPtr hwo);
    const uint WHDR_DONE = 1;

    void Open()
    {
      WAVEFORMATEX f = new WAVEFORMATEX();
      f.wFormatTag = 1;
      f.nChannels = 1;
      f.nSamplesPerSec = SR;
      f.wBitsPerSample = 16;
      f.nBlockAlign = 2;
      f.nAvgBytesPerSec = SR * 2;
      if (waveOutOpen(out hwo, -1, ref f, IntPtr.Zero, IntPtr.Zero, 0) != 0)
        throw new Exception("waveOutOpen");
      int hs = Marshal.SizeOf(typeof(WAVEHDR));
      flagsOfs = (int)Marshal.OffsetOf(typeof(WAVEHDR), "dwFlags");
      for (int i = 0; i < NBUF; i++)
      {
        datas[i] = Marshal.AllocHGlobal(BUF * 2);
        hdrs[i] = Marshal.AllocHGlobal(hs);
        WAVEHDR h = new WAVEHDR();
        h.lpData = datas[i];
        h.dwBufferLength = BUF * 2;
        Marshal.StructureToPtr(h, hdrs[i], false);
        waveOutPrepareHeader(hwo, hdrs[i], hs);
        Marshal.WriteInt32(hdrs[i], flagsOfs, Marshal.ReadInt32(hdrs[i], flagsOfs) | (int)WHDR_DONE); // 最初は全部空き
      }
      running = true;
      th = new Thread(Loop);
      th.IsBackground = true;
      th.Priority = ThreadPriority.AboveNormal;
      th.Start();
    }

    void Loop()
    {
      int hs = Marshal.SizeOf(typeof(WAVEHDR));
      while (running)
      {
        bool wrote = false;
        for (int i = 0; i < NBUF; i++)
        {
          int fl = Marshal.ReadInt32(hdrs[i], flagsOfs);
          if ((fl & WHDR_DONE) == 0)
            continue;
          Mix();
          Marshal.Copy(pcm, 0, datas[i], BUF);
          Marshal.WriteInt32(hdrs[i], flagsOfs, fl & ~(int)WHDR_DONE);
          waveOutWrite(hwo, hdrs[i], hs);
          wrote = true;
        }
        if (!wrote)
          Thread.Sleep(2);
      }
    }

    public void Dispose()
    {
      if (hwo == IntPtr.Zero)
        return;
      running = false;
      if (th != null)
        th.Join(200);
      waveOutReset(hwo);
      int hs = Marshal.SizeOf(typeof(WAVEHDR));
      for (int i = 0; i < NBUF; i++)
      {
        waveOutUnprepareHeader(hwo, hdrs[i], hs);
        Marshal.FreeHGlobal(hdrs[i]);
        Marshal.FreeHGlobal(datas[i]);
      }
      waveOutClose(hwo);
      hwo = IntPtr.Zero;
    }

    // ---- 混ぜる ----
    void Mix()
    {
      Array.Clear(mixBuf, 0, BUF);
      lock (lk)
      {
        voices.AddRange(pending);
        pending.Clear();
      }
      bool mute = Muted;
      for (int vi = voices.Count - 1; vi >= 0; vi--)
      {
        Voice v = voices[vi];
        int n = Math.Min(BUF, v.w.Length - v.pos);
        if (!mute)
          for (int i = 0; i < n; i++)
            mixBuf[i] += v.w[v.pos + i] * v.vol;
        v.pos += n;
        if (v.pos >= v.w.Length)
          voices.RemoveAt(vi);
      }
      LoopSounds(mute);
      float m = mute ? 0 : Master;
      for (int i = 0; i < BUF; i++)
      {
        float x = mixBuf[i] * m;
        x = x / (1 + Math.Abs(x) * 0.6f); // やわらかく頭を抑える
        pcm[i] = (short)(Math.Max(-1f, Math.Min(1f, x)) * 30000);
      }
    }

    static float Toward(float cur, float target) { return cur + (target - cur) * 0.08f; }

    void LoopSounds(bool mute)
    {
      // 1 枠ごとに少しずつ目標の音量へ寄せる (急に切れてプツッと鳴らないように)
      curEngine = Toward(curEngine, mute ? 0 : Engine);
      curPitch += (EnginePitch - curPitch) * 0.2f;
      curBank += (EngineBank - curBank) * 0.2f;
      curRain = Toward(curRain, mute ? 0 : Rain);
      curUfo = Toward(curUfo, mute ? 0 : UfoHum);
      curBeam = Toward(curBeam, mute ? 0 : Beam);
      curMom = Toward(curMom, mute ? 0 : MomHum);
      curBirds = Toward(curBirds, mute ? 0 : Birds);
      for (int i = 0; i < BUF; i++)
      {
        float s = 0;
        if (curEngine > 0.001f)
        {
          // プロペラ: 低いうなりに、羽根が空気をたたく脈を重ねる
          // 音程は操作で変わる (前進で高く、後退で低く)。旋回中は翼が風を切って、うなりが少し揺れる
          engLfo += 2 * Math.PI * 7 / SR;
          double f = 62 * curPitch * (1 + 0.012 * curBank * Math.Sin(engLfo));
          engPh += f / SR;
          double ph = engPh - Math.Floor(engPh);
          float saw = (float)(ph * 2 - 1);
          float puls = (float)Math.Pow(Math.Max(0, Math.Sin(engPh * 2 * Math.PI * 2)), 6);
          engOut += (saw * 0.5f + puls - engOut) * 0.25f;
          s += engOut * 0.35f * curEngine * (0.85f + 0.3f * Math.Min(1.5f, curPitch - 0.5f));
        }
        if (curRain > 0.001f)
        {
          float n = (float)(rng.NextDouble() * 2 - 1);
          rainLp += (n - rainLp) * 0.35f;
          rainHp = rainLp - rainLp2;
          rainLp2 += (rainLp - rainLp2) * 0.02f;
          s += rainHp * 0.9f * curRain;
        }
        if (curUfo > 0.001f)
        {
          // 円盤: ゆらゆら揺れる高い音
          ufoLfo += 2 * Math.PI * 6.5 / SR;
          ufoPh += (420 + 70 * Math.Sin(ufoLfo)) / SR;
          s += (float)Math.Sin(ufoPh * 2 * Math.PI) * 0.22f * curUfo;
        }
        if (curBeam > 0.001f)
        {
          beamPh += (880 + 40 * Math.Sin(ufoLfo * 3)) / SR;
          double b = Math.Sin(beamPh * 2 * Math.PI) + 0.5 * Math.Sin(beamPh * 3 * 2 * Math.PI);
          s += (float)b * 0.12f * curBeam;
        }
        if (curMom > 0.001f)
        {
          // 母船: 低くうねる音
          momPh += 48.0 / SR;
          double env = 0.6 + 0.4 * Math.Sin(momPh * 2 * Math.PI / 24);
          s += (float)((Math.Sin(momPh * 2 * Math.PI) + 0.4 * Math.Sin(momPh * 4.02 * 2 * Math.PI)) * env) * 0.35f * curMom;
        }
        if (curBirds > 0.001f)
          s += BirdSample() * 0.18f * curBirds;
        mixBuf[i] += s;
      }
    }

    // 小鳥 (ヒバリのつもり): 間をおいて、高い声が速く上下するさえずりを鳴らす
    float BirdSample()
    {
      if (birdLeft <= 0)
      {
        if (--birdGap > 0)
          return 0;
        birdLeft = (int)(SR * (0.04 + rng.NextDouble() * 0.09));
        birdF = 2600 + rng.NextDouble() * 2400;
        birdDf = (rng.NextDouble() - 0.5) * 120000.0 / SR;
        bird = 0;
        birdGap = rng.Next(4) == 0 ? (int)(SR * (0.6 + rng.NextDouble() * 1.8)) : (int)(SR * (0.02 + rng.NextDouble() * 0.06));
      }
      birdLeft--;
      birdF = Math.Max(1800, Math.Min(6500, birdF + birdDf));
      birdPh += (birdF + 300 * Math.Sin(birdPh * 0.004)) / SR;
      bird = Math.Min(1, bird + 0.01f);
      float env = Math.Min(bird, Math.Min(1, birdLeft / (SR * 0.01f)));
      return (float)Math.Sin(birdPh * 2 * Math.PI) * env;
    }

    // 確かめる用: 鳴らしきりの音を全部、0.3 秒ずつあけて WAV に並べる。返すのは音ごとの一番大きい振れ幅
    public string DumpWav(string path)
    {
      System.Text.StringBuilder sb = new System.Text.StringBuilder();
      List<short> all = new List<short>();
      for (int s = 0; s < (int)Sfx.Count; s++)
      {
        float peak = 0;
        foreach (float v in waves[s])
        {
          float x = v * waveVol[s];
          peak = Math.Max(peak, Math.Abs(x));
          all.Add((short)(Math.Max(-1f, Math.Min(1f, x / (1 + Math.Abs(x) * 0.6f))) * 30000));
        }
        all.AddRange(new short[SR * 3 / 10]);
        sb.Append(((Sfx)s).ToString()).Append(' ').Append(peak.ToString("0.00")).Append('\n');
      }
      using (System.IO.BinaryWriter w = new System.IO.BinaryWriter(System.IO.File.Create(path)))
      {
        w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + all.Count * 2);
        w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(SR);
        w.Write(SR * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        w.Write(all.Count * 2);
        foreach (short v in all)
          w.Write(v);
      }
      return sb.ToString();
    }

    // ---- 鳴らしきりの音の波形 ----
    float[] New(double sec) { return new float[(int)(SR * sec)]; }
    float Noise() { return (float)(rng.NextDouble() * 2 - 1); }
    static float Square(double ph) { return ph - Math.Floor(ph) < 0.5 ? 1f : -1f; }

    // 音符を並べる (矩形波に少し三角を混ぜた、やわらかい笛の音)。notes は半音 (A4 = 0)、len は秒
    float[] Melody(int[] notes, double[] len, double duty)
    {
      double total = 0;
      foreach (double l in len)
        total += l;
      float[] w = New(total + 0.1);
      int p = 0;
      for (int k = 0; k < notes.Length; k++)
      {
        int n = (int)(SR * len[k]);
        if (notes[k] > -100)
        {
          double f = 440 * Math.Pow(2, notes[k] / 12.0), ph = 0;
          for (int i = 0; i < n && p + i < w.Length; i++)
          {
            ph += f / SR;
            double fr = ph - Math.Floor(ph);
            float sq = fr < duty ? 1f : -1f, tri = (float)(1 - 4 * Math.Abs(fr - 0.5));
            float env = Math.Min(1, i / (SR * 0.005f)) * (float)Math.Exp(-2.5 * i / n);
            w[p + i] += (sq * 0.35f + tri * 0.65f) * env;
          }
        }
        p += n;
      }
      return w;
    }

    void Set(Sfx s, float[] w, float vol)
    {
      waves[(int)s] = w;
      waveVol[(int)s] = vol;
    }

    void MakeWaves()
    {
      float[] w;
      // 機銃: 短い雑音のパチッと、下がる低い音
      w = New(0.07);
      for (int i = 0; i < w.Length; i++)
      {
        double t = (double)i / SR;
        w[i] = (Noise() * 0.6f + Square(t * (520 - 2600 * t)) * 0.4f) * (float)Math.Exp(-t * 55);
      }
      Set(Sfx.Shot, w, 0.22f);
      // 弾が円盤に当たった
      w = New(0.05);
      for (int i = 0; i < w.Length; i++)
      {
        double t = (double)i / SR;
        w[i] = Square(t * 1700) * (float)Math.Exp(-t * 80) * 0.6f + Noise() * (float)Math.Exp(-t * 120) * 0.4f;
      }
      Set(Sfx.Hit, w, 0.18f);
      // 母船に当たった: 金属を打つ音
      w = New(0.09);
      for (int i = 0; i < w.Length; i++)
      {
        double t = (double)i / SR;
        w[i] = (float)((Math.Sin(t * 2 * Math.PI * 1230) + Math.Sin(t * 2 * Math.PI * 1847) * 0.7) * Math.Exp(-t * 45)) * 0.5f;
      }
      Set(Sfx.MomHit, w, 0.14f);
      Set(Sfx.Boom, Explosion(0.55, 0.12f, 70), 0.3f);
      Set(Sfx.BigBoom, Explosion(1.1, 0.06f, 50), 0.42f);
      Set(Sfx.Crash, Explosion(3.2, 0.035f, 38), 0.5f);
      // 自機がやられた: 爆発に下がっていく音を重ねる
      w = Explosion(1.2, 0.08f, 60);
      for (int i = 0; i < w.Length; i++)
      {
        double t = (double)i / SR;
        w[i] += Square(t * (700 - 260 * t)) * (float)Math.Exp(-t * 3) * 0.25f;
      }
      Set(Sfx.Death, w, 0.42f);
      Set(Sfx.Power, Melody(new[] { 3, 7, 10, 15, 19 }, new[] { 0.05, 0.05, 0.05, 0.05, 0.16 }, 0.25), 0.3f);
      // 羊を助けた: メエと鳴いて、鈴の音
      w = New(0.75);
      for (int i = 0; i < (int)(SR * 0.38); i++)
      {
        double t = (double)i / SR, f = 300 + 25 * Math.Sin(t * 2 * Math.PI * 22);
        double ph = t * f, saw = (ph - Math.Floor(ph)) * 2 - 1;
        float env = (float)(Math.Min(1, t / 0.03) * Math.Min(1, (0.38 - t) / 0.08));
        w[i] = (float)(saw * (0.6 + 0.4 * Math.Sin(t * 2 * Math.PI * 22))) * env * 0.5f;
      }
      double[] bells = { 1568, 2093 };
      for (int b = 0; b < 2; b++)
      {
        int st = (int)(SR * (0.36 + b * 0.1));
        for (int i = 0; st + i < w.Length; i++)
        {
          double t = (double)i / SR;
          w[st + i] += (float)((Math.Sin(t * 2 * Math.PI * bells[b]) + 0.3 * Math.Sin(t * 2 * Math.PI * bells[b] * 2.76)) * Math.Exp(-t * 9)) * 0.45f;
        }
      }
      Set(Sfx.Saved, w, 0.4f);
      Set(Sfx.Extend, Melody(new[] { 3, 7, 10, 7, 10, 15 }, new[] { 0.09, 0.09, 0.09, 0.09, 0.09, 0.35 }, 0.5), 0.35f);
      // 警報: 高い・低いをくり返す
      w = New(2.6);
      for (int i = 0; i < w.Length; i++)
      {
        double t = (double)i / SR;
        double f = (int)(t * 4) % 2 == 0 ? 740 : 554;
        w[i] = (Square(t * f) * 0.5f + (float)Math.Sin(t * f * 2 * Math.PI) * 0.5f) * (float)Math.Min(1, (2.6 - t) * 4) * 0.6f;
      }
      Set(Sfx.Warning, w, 0.3f);
      // 汽笛: 3 つの音の和音に息の雑音。少し遅れて鳴り出し、ふっと消える
      w = New(1.3);
      double[] wf = { 466, 587, 698 };
      float lp = 0;
      for (int i = 0; i < w.Length; i++)
      {
        double t = (double)i / SR, env = Math.Min(1, t / 0.08) * Math.Min(1, (1.3 - t) / 0.3), bend = 1 - 0.03 * Math.Exp(-t * 8);
        double v = 0;
        foreach (double f in wf)
          v += Math.Sin(t * f * bend * 2 * Math.PI);
        lp += (Noise() - lp) * 0.3f;
        w[i] = (float)(v * 0.28 + lp * 0.25) * (float)env;
      }
      Set(Sfx.Whistle, w, 0.3f);
      // 雷: 遠くでゴロゴロ。揺れながら弱まる低い雑音
      w = New(2.8);
      float a = 0, b2 = 0, am = 1;
      for (int i = 0; i < w.Length; i++)
      {
        double t = (double)i / SR;
        a += (Noise() - a) * 0.04f;
        b2 += (a - b2) * 0.1f;
        if (i % 1500 == 0)
          am = (float)(0.4 + rng.NextDouble() * 0.6);
        float crack = t < 0.15 ? Noise() * (float)(1 - t / 0.15) * 0.6f : 0;
        w[i] = (b2 * 9 * am + crack) * (float)(Math.Min(1, t / 0.05) * Math.Exp(-t * 1.3));
      }
      Set(Sfx.Thunder, w, 0.45f);
      // 円盤の弾: ふわっと下がる小さな音
      w = New(0.12);
      double eph = 0;
      for (int i = 0; i < w.Length; i++)
      {
        double t = (double)i / SR;
        eph += (1400 - 7000 * t) / SR;
        w[i] = (float)Math.Sin(eph * 2 * Math.PI) * (float)Math.Exp(-t * 25);
      }
      Set(Sfx.EnemyShot, w, 0.09f);
      // 出発: のどかな短い節
      Set(Sfx.Start, Melody(new[] { -2, 2, 5, 10, 9, 5, 7, 10, 14 }, new[] { 0.14, 0.14, 0.14, 0.28, 0.14, 0.14, 0.14, 0.14, 0.5 }, 0.5), 0.35f);
      Set(Sfx.GameOver, Melody(new[] { 10, 9, 5, 2, 0, -2 }, new[] { 0.22, 0.22, 0.22, 0.22, 0.22, 0.7 }, 0.5), 0.35f);
      Set(Sfx.Tick, Melody(new[] { 15 }, new[] { 0.08 }, 0.5), 0.25f);
      Set(Sfx.Select, Melody(new[] { 22 }, new[] { 0.04 }, 0.25), 0.2f);
      Set(Sfx.Pause, Melody(new[] { 15, 10 }, new[] { 0.06, 0.1 }, 0.5), 0.25f);
      Set(Sfx.BaaAway, Bleat(1.3, 330, 210, 22), 0.5f);
      Set(Sfx.MooAway, Bleat(1.6, 150, 105, 6), 0.55f);
      // 牧羊犬: ワンワン
      w = New(0.42);
      for (int k = 0; k < 2; k++)
      {
        int st = (int)(SR * k * 0.2);
        float lpw = 0;
        double bph = 0;
        for (int i = 0; st + i < w.Length && i < SR * 0.13; i++)
        {
          double t = (double)i / SR;
          bph += (480 - 1500 * t) / SR;
          double saw = (bph - Math.Floor(bph)) * 2 - 1;
          lpw += (Noise() - lpw) * 0.4f;
          double env = Math.Min(1, t / 0.006) * Math.Exp(-t * 22);
          w[st + i] += (float)((saw * 0.7 + lpw * 0.6) * env);
        }
      }
      Set(Sfx.Woof, w, 0.35f);
      // カモ: クワッ クワッ (鼻にかかった音)
      w = New(0.36);
      for (int k = 0; k < 2; k++)
      {
        int st = (int)(SR * k * 0.17);
        double qph = 0;
        for (int i = 0; st + i < w.Length && i < SR * 0.11; i++)
        {
          double t = (double)i / SR;
          qph += (620 - 900 * t) / SR;
          double fr = qph - Math.Floor(qph);
          double pulse = fr < 0.18 ? 1 : -0.25;
          double env = Math.Min(1, t / 0.008) * Math.Exp(-t * 18);
          w[st + i] += (float)(pulse * env * (0.7 + 0.3 * Math.Sin(t * 2 * Math.PI * 60)));
        }
      }
      Set(Sfx.Quack, w, 0.25f);
      // 名所を見つけた: 竪琴をつま弾くような、少し不思議な和音の分散
      int[] harp = { -12, -5, 0, 3, 7, 12, 14 };
      w = New(2.2);
      for (int k = 0; k < harp.Length; k++)
      {
        double f = 440 * Math.Pow(2, harp[k] / 12.0);
        int st = (int)(SR * k * 0.09);
        for (int i = 0; st + i < w.Length; i++)
        {
          double t = (double)i / SR;
          w[st + i] += (float)((Math.Sin(t * f * 2 * Math.PI) + 0.35 * Math.Sin(t * f * 4 * Math.PI) + 0.15 * Math.Sin(t * f * 6 * Math.PI)) * Math.Exp(-t * 2.6) * Math.Min(1, t / 0.003)) * 0.28f;
        }
      }
      Set(Sfx.Discover, w, 0.4f);
      // ネッシーが水から出る: ザバッという水音と、低いあぶくの音
      w = New(1.2);
      float slp = 0;
      for (int i = 0; i < w.Length; i++)
      {
        double t = (double)i / SR;
        slp += (Noise() - slp) * 0.25f;
        double bubble = Math.Sin(t * 2 * Math.PI * (90 + 40 * Math.Sin(t * 30))) * Math.Exp(-t * 3) * 0.4;
        w[i] = (float)(slp * 1.6 * Math.Exp(-t * 4) * Math.Min(1, t / 0.02) + bubble);
      }
      Set(Sfx.Splash, w, 0.35f);
      // ストーンヘンジから円盤が湧く: 低いところから上ずっていく、揺れる不思議な音
      w = New(1.4);
      double eph2 = 0;
      for (int i = 0; i < w.Length; i++)
      {
        double t = (double)i / SR;
        eph2 += (120 + 700 * t * t + 30 * Math.Sin(t * 2 * Math.PI * 9)) / SR;
        w[i] = (float)((Math.Sin(eph2 * 2 * Math.PI) + 0.4 * Math.Sin(eph2 * 3.01 * 2 * Math.PI)) * Math.Min(1, t / 0.3) * Math.Min(1, (1.4 - t) / 0.4) * 0.5);
      }
      Set(Sfx.Emerge, w, 0.3f);
      // ボドミンの野獣が座ってうなる: 低くごろごろ震える声
      w = New(1.0);
      float glp = 0;
      double gph = 0;
      for (int i = 0; i < w.Length; i++)
      {
        double t = (double)i / SR;
        glp += (Noise() - glp) * 0.08f;
        gph += (75 - 15 * t) / SR;
        double saw = (gph - Math.Floor(gph)) * 2 - 1;
        double trem = 0.55 + 0.45 * Math.Sin(t * 2 * Math.PI * 24);
        double env = Math.Min(1, t / 0.15) * Math.Min(1, (1.0 - t) / 0.35);
        w[i] = (float)((saw * 0.5 + glp * 3.5) * trem * env);
      }
      Set(Sfx.Growl, w, 0.45f);
    }

    // 連れ去られる鳴き声: 震えるメエ (モー) の声が、高さを下げながら遠ざかる (だんだん小さく、こもり、こだまが残る)
    float[] Bleat(double sec, double f0, double f1, double trem)
    {
      float[] w = New(sec + 0.4);
      double ph = 0;
      float lp = 0;
      for (int i = 0; i < (int)(SR * sec); i++)
      {
        double t = (double)i / SR, k = t / sec;
        double f = f0 + (f1 - f0) * k + 12 * Math.Sin(t * 2 * Math.PI * trem);
        ph += f / SR;
        double saw = (ph - Math.Floor(ph)) * 2 - 1;
        double voice = saw * (0.6 + 0.4 * Math.Sin(t * 2 * Math.PI * trem));
        lp += ((float)voice - lp) * (float)(0.5 - 0.42 * k); // 遠ざかるほどこもる
        double env = Math.Min(1, t / 0.04) * Math.Pow(1 - k, 1.6);
        w[i] += (float)(lp * env);
      }
      int echo = (int)(SR * 0.22);
      for (int i = w.Length - 1; i >= echo; i--)
        w[i] += w[i - echo] * 0.3f;
      return w;
    }

    // 爆発: 低いほうへこもっていく雑音と、ドンという低い音
    float[] Explosion(double sec, float lpStart, double thump)
    {
      float[] w = New(sec);
      float lp = 0, lp2 = 0;
      for (int i = 0; i < w.Length; i++)
      {
        double t = (double)i / SR, k = t / sec;
        float cut = (float)(lpStart * 4 * (1 - k) + lpStart * 0.3);
        lp += (Noise() - lp) * Math.Min(0.9f, cut * 3);
        lp2 += (lp - lp2) * Math.Min(0.9f, cut * 3);
        double env = Math.Min(1, t / 0.004) * Math.Pow(1 - k, 2.2);
        w[i] = (float)((lp2 * 2.2 + Math.Sin(t * 2 * Math.PI * thump * (1 - 0.5 * k)) * 0.6 * Math.Exp(-t * 6)) * env);
      }
      return w;
    }
  }
}
