using System.Drawing.Drawing2D;
using NeonCue.Core;
using NeonCue.Rendering;

namespace NeonCue.Screens;

sealed class GameScreen : IScreen, IPhysicsListener
{
    enum Phase { Placing, Aiming, Rolling, AIThinking, AIPlacing, AIAiming, RackOver }

    sealed class Toast
    {
        public string Text = "";
        public Color Color;
        public double Age;
        public double Life = 2.8;
    }

    sealed class Sink
    {
        public int Number;
        public Vec2 From, To;
        public double T;
    }

    const double R = Table.R;

    readonly IHost host;
    readonly AppSettings cfg;
    readonly GameRules rules;
    readonly Random rng = new();
    readonly TableView view = new();
    readonly Particles fx = new();
    readonly BallRenderer balls3d = new();
    readonly BallRenderer hudBalls = new();
    readonly Mat3[] orient = new Mat3[16];
    readonly Vec2[] lastPos = new Vec2[16];
    Bitmap? shadowSprite;
    float shadowRadius;
    int rollUpdates;
    readonly List<Toast> toasts = new();
    readonly List<Sink> sinks = new();
    readonly List<UiButton> buttons = new();
    GameState state;

    Phase phase;
    bool paused;
    double phaseTime, rollTime, physicsAcc, clock, overlayTimer;
    double aimAngle = Math.PI, kbPower = 0.45, dragPower, spinSide, spinTop;
    bool dragging, spinDrag, fastForward, aimLocked;
    Point aimLockPoint, mouse;
    Vec2 dragStart, mouseWorld;
    ShotEvents events = new();
    Task<AIShot>? aiTask, hintTask;
    int hintShotNumber;
    AIShot? aiShot;
    double aiFromAngle;
    Vec2 aiFromCue;
    float u = 1;
    Size size;
    PointF spinCenter;
    float spinRadius;
    RectangleF powerRect;

    public GameScreen(IHost host)
    {
        this.host = host;
        cfg = host.Settings;
        rules = GameRules.Create(cfg.Mode);
        state = NewMatch(0);
        for (int i = 0; i < orient.Length; i++) orient[i] = Mat3.RandomResting(rng);
        SyncRolling();
        BeginTurn();
    }

    /// <summary>Rolls each ball's 3D orientation by the distance it moved since last frame.</summary>
    void SyncRolling()
    {
        bool tidy = ++rollUpdates % 120 == 0;
        foreach (var b in state.Balls)
        {
            int n = b.Number;
            Vec2 d = b.Pos - lastPos[n];
            double len = d.Length;
            if (!b.Pocketed && len > 1e-6 && len < 3 * R)
            {
                var axis = new Vec3(-d.Y / len, d.X / len, 0);
                orient[n] = Mat3.Rotation(axis, len / R) * orient[n];
            }
            if (tidy) orient[n] = orient[n].Orthonormalized();
            lastPos[n] = b.Pos;
        }
    }

    Color GuideColor => Look.Realistic ? Color.FromArgb(235, 255, 255, 255) : Theme.Cyan;
    Color TangentColor => Look.Realistic ? Color.FromArgb(190, 255, 214, 120) : Theme.A(Theme.Magenta, 200);

    GameState NewMatch(int breaker)
    {
        var s = new GameState
        {
            Mode = cfg.Mode,
            RaceTo = cfg.RaceTo,
            TargetScore = cfg.Mode == GameMode.Rotation ? 61 : cfg.TargetScore,
            Breaker = breaker,
            Players = MakePlayers(),
        };
        rules.NewRack(s, rng);
        return s;
    }

    PlayerState[] MakePlayers()
    {
        PlayerState Human(string name) => new() { Name = name, Cue = cfg.Equipped };
        PlayerState Bot(string name) => new() { Name = name, IsAI = true, Level = cfg.Level, Cue = CueCatalog.ForAI(cfg.Level) };
        if (rules.IsSolo) return cfg.Opponent == OpponentType.CpuVsCpu ? [Bot("NEON-BOT")] : [Human("You")];
        return cfg.Opponent switch
        {
            OpponentType.Cpu => [Human("You"), Bot("NEON-BOT")],
            OpponentType.TwoPlayer => [Human("Player 1"), Human("Player 2")],
            _ => [Bot("ALPHA-BOT"), Bot("OMEGA-BOT")],
        };
    }

    bool HumanTurn => !state.CurrentPlayer.IsAI && !state.RackOver;

    // ================================================================== flow

    void BeginTurn()
    {
        dragging = false;
        dragPower = 0;
        aiShot = null;
        phaseTime = 0;
        aimLocked = false;
        if (state.RackOver)
        {
            phase = Phase.RackOver;
            overlayTimer = 0;
            BuildRackOverButtons();
            return;
        }
        spinSide = spinTop = 0;
        var p = state.CurrentPlayer;
        if (p.IsAI)
        {
            phase = Phase.AIThinking;
            var snap = state.Clone();
            var level = p.Level;
            int seed = rng.Next();
            aiTask = Task.Run(() => AIPlayer.Plan(snap, rules, level, seed));
        }
        else
        {
            phase = state.BallInHand ? Phase.Placing : Phase.Aiming;
            if (cfg.Opponent == OpponentType.TwoPlayer && state.PlayerCount > 1)
                Say($"{p.Name} to shoot", Theme.PlayerColor(state.Current), 1.6);
        }
    }

    void Shoot(double angle, double power, double side, double top)
    {
        // Cheaper cues wobble: human strokes pick up the cue's aim error (AI noise already includes it).
        var stick = state.CurrentPlayer.Cue;
        if (!state.CurrentPlayer.IsAI && stick.AimError > 0) angle += Gauss() * stick.AimError * Math.PI / 180;
        Physics.Strike(state.Cue, angle, power, side, top, stick, state.IsBreakShot);
        events = new ShotEvents();
        physicsAcc = 0;
        rollTime = 0;
        phase = Phase.Rolling;
        phaseTime = 0;
        dragging = false;
        dragPower = 0;
        state.BallInHand = false;
        hintTask = null;
        host.Sound.Cue(power);
        if (!Look.Realistic) fx.Burst(state.Cue.Pos - Vec2.FromAngle(angle) * R, Theme.Cyan, 8 + (int)(power * 14), 90 + power * 160, 1.8f);
    }

    double Gauss()
    {
        double u1 = 1 - rng.NextDouble(), u2 = rng.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }

    /// <summary>Coins are only earned against the CPU or in solo practice (no farming in hot-seat).</summary>
    bool Earns(int player) => !state.Players[player].IsAI && (rules.IsSolo || cfg.Opponent == OpponentType.Cpu);

    int Rewards(int shooter, ShotOutcome o)
    {
        int coins = 0;
        if (Earns(shooter) && !o.Foul) coins += events.PocketedObjects.Count() * (rules.IsSolo ? 2 : 5);
        if (o.RackOver && !rules.IsSolo)
        {
            int human = Array.FindIndex(state.Players, p => !p.IsAI);
            if (human >= 0 && Earns(human))
            {
                int tier = (int)cfg.Level + 1;
                if (o.Winner == human) coins += 60 * tier + (state.MatchOver ? 100 * tier : 0);
                else coins += 15;
            }
        }
        if (coins > 0)
        {
            cfg.Coins += coins;
            if (o.RackOver) cfg.Save();
        }
        return coins;
    }

    void FinishShot()
    {
        int shooter = state.Current;
        var o = rules.ApplyShot(state, events, rng);
        int coins = Rewards(shooter, o);
        foreach (var m in o.Messages)
        {
            Color c = m.StartsWith("FOUL") || m.Contains("loses") || m.Contains("early") ? Theme.Red
                : m.Contains("sinks") || m.Contains("reaches") ? Theme.Lime
                : m.StartsWith('+') ? Theme.Amber
                : Theme.Cyan;
            Say(m, c);
        }
        if (!o.RackOver && state.BallInHand && state.PlayerCount > 1 && o.Foul)
            Say($"Ball in hand — {state.CurrentPlayer.Name}", Theme.Amber);
        if (coins > 0) Say($"+{coins} coins", Theme.Amber, o.RackOver ? 4 : 1.6);
        if (o.RackOver) host.Sound.Win();
        if (!o.RackOver && !o.Foul && shooter != state.Current && state.PlayerCount > 1 && events.PocketedObjects.Any() == false)
            fx.Pulse(state.Cue.Pos, Theme.PlayerColor(state.Current), 26);
        BeginTurn();
    }

    void ContinueAfterRack()
    {
        if (state.MatchOver) state = NewMatch(1 - state.Breaker);
        else rules.StartNextRack(state, rng);
        toasts.Clear();
        BeginTurn();
    }

    void Restart()
    {
        state = NewMatch(0);
        paused = false;
        toasts.Clear();
        sinks.Clear();
        BeginTurn();
    }

    void Say(string text, Color color, double life = 2.8)
    {
        toasts.Add(new Toast { Text = text, Color = color, Life = life });
        if (toasts.Count > 4) toasts.RemoveAt(0);
    }

    AIShot Fallback()
    {
        var target = state.ObjectBallsOnTable.FirstOrDefault(b => rules.IsLegalFirstContact(state, b.Number)) ?? state.ObjectBallsOnTable.First();
        return new AIShot { Angle = (target.Pos - state.Cue.Pos).Angle, Power = 0.45, Plan = "Fallback" };
    }

    // ================================================================== update

    public void Update(double dt)
    {
        clock += dt;
        fx.Update(dt);
        for (int i = toasts.Count - 1; i >= 0; i--)
        {
            toasts[i].Age += dt;
            if (toasts[i].Age > toasts[i].Life) toasts.RemoveAt(i);
        }
        for (int i = sinks.Count - 1; i >= 0; i--)
        {
            sinks[i].T += dt / 0.28;
            if (sinks[i].T >= 1) sinks.RemoveAt(i);
        }

        SyncRolling();
        if (hintTask is { IsCompleted: true }) ApplyHint();
        if (paused) return;
        phaseTime += dt;
        double ff = fastForward ? 0.25 : 1;

        switch (phase)
        {
            case Phase.Rolling:
                UpdateRolling(dt);
                break;

            case Phase.AIThinking:
                if (aiTask is { IsCompleted: true } && phaseTime > 0.55 * ff)
                {
                    aiShot = aiTask.IsCompletedSuccessfully ? aiTask.Result : Fallback();
                    AIPlayer.ApplyNoise(aiShot, state.CurrentPlayer.Level, state.CurrentPlayer.Cue, rng);
                    spinSide = aiShot.SpinSide;
                    spinTop = aiShot.SpinTop;
                    aiFromAngle = aimAngle;
                    aiFromCue = state.Cue.Pos;
                    phase = aiShot.Placement.HasValue && state.BallInHand ? Phase.AIPlacing : Phase.AIAiming;
                    phaseTime = 0;
                }
                break;

            case Phase.AIPlacing:
            {
                double T = 0.6 * ff;
                state.Cue.Pos = Vec2.Lerp(aiFromCue, aiShot!.Placement!.Value, Smooth(phaseTime / T));
                if (phaseTime >= T)
                {
                    state.Cue.Pos = aiShot.Placement.Value;
                    phase = Phase.AIAiming;
                    phaseTime = 0;
                }
                break;
            }

            case Phase.AIAiming:
            {
                double t1 = 0.65 * ff, t2 = 0.45 * ff;
                if (phaseTime < t1)
                {
                    aimAngle = LerpAngle(aiFromAngle, aiShot!.Angle, Smooth(phaseTime / t1));
                }
                else
                {
                    aimAngle = aiShot!.Angle;
                    dragPower = aiShot.Power * Math.Min(1, (phaseTime - t1) / t2);
                    if (phaseTime >= t1 + t2) Shoot(aiShot.Angle, aiShot.Power, aiShot.SpinSide, aiShot.SpinTop);
                }
                break;
            }

            case Phase.RackOver:
                overlayTimer += dt;
                if (state.Players.All(p => p.IsAI) && !state.MatchOver && overlayTimer > 4) ContinueAfterRack();
                break;
        }
    }

    void UpdateRolling(double dt)
    {
        physicsAcc += dt * (fastForward ? 4 : 1);
        bool moving = true;
        int guard = 0;
        while (physicsAcc >= Physics.Dt && guard++ < 4000)
        {
            physicsAcc -= Physics.Dt;
            rollTime += Physics.Dt;
            if (!Physics.Step(state.Balls, events, this)) { moving = false; break; }
        }
        if (rollTime > Physics.MaxSimTime)
        {
            foreach (var b in state.Balls) b.Vel = Vec2.Zero;
            moving = false;
        }
        if (!moving) FinishShot();
    }

    static double Smooth(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }

    static double LerpAngle(double a, double b, double t)
    {
        double d = Math.IEEERemainder(b - a, Math.PI * 2);
        return a + d * t;
    }

    // ================================================================== physics events

    public void BallCollision(Ball a, Ball b, double impactSpeed)
    {
        if (impactSpeed > 40) host.Sound.Clack(impactSpeed);
        if (impactSpeed > 700 && !Look.Realistic) fx.Burst((a.Pos + b.Pos) / 2, Theme.Mix(Theme.Cyan, Color.White, 0.3), 4, impactSpeed * 0.12, 1.4f);
    }

    public void CushionHit(Ball ball, double impactSpeed) => host.Sound.Cushion(impactSpeed);

    public void BallPocketed(Ball ball, Pocket pocket)
    {
        sinks.Add(new Sink { Number = ball.Number, From = ball.Pos, To = pocket.DrawCenter });
        Color c = ball.Number == 0 ? Theme.Red : ball.Number == 8 ? Theme.Violet : Theme.BallColor(ball.Number);
        if (!Look.Realistic)
        {
            fx.Burst(pocket.DrawCenter, c, 22, 260, 2.6f);
            fx.Burst(pocket.DrawCenter, Theme.Magenta, 10, 180, 1.6f);
            fx.Pulse(pocket.DrawCenter, c, 30);
        }
        host.Sound.Pocket();
    }

    // ================================================================== hint (AI coach)

    void RequestHint()
    {
        if (!HumanTurn || hintTask != null || phase is not (Phase.Aiming or Phase.Placing)) return;
        Say("Coach is reading the table…", Theme.TextDim, 1.4);
        var snap = state.Clone();
        int seed = rng.Next();
        hintShotNumber = state.ShotNumber;
        hintTask = Task.Run(() => AIPlayer.Plan(snap, rules, AILevel.Pro, seed));
    }

    void ApplyHint()
    {
        var task = hintTask!;
        hintTask = null;
        if (!task.IsCompletedSuccessfully || !HumanTurn || state.ShotNumber != hintShotNumber) return;
        var h = task.Result;
        if (h.Placement is Vec2 p && state.BallInHand && state.IsValidCuePlacement(p)) state.Cue.Pos = p;
        if (phase == Phase.Placing) phase = Phase.Aiming;
        aimAngle = h.Angle;
        kbPower = Math.Round(h.Power, 2);
        spinSide = h.SpinSide;
        spinTop = h.SpinTop;
        aimLocked = true;
        aimLockPoint = mouse;
        Say($"Coach: {h.Plan} — press Space", Theme.Lime, 4);
    }

    // ================================================================== input

    public void MouseMove(MouseEventArgs e)
    {
        mouse = e.Location;
        mouseWorld = view.ToWorld(e.Location);
        if (paused || phase == Phase.RackOver)
        {
            UiButton.UpdateHover(buttons, e.Location);
            return;
        }
        if (spinDrag) { SetSpinFrom(e.Location); return; }
        if (!HumanTurn) return;

        if (phase == Phase.Placing)
        {
            MoveCueTo(mouseWorld);
        }
        else if (phase == Phase.Aiming)
        {
            if (dragging)
            {
                Vec2 d = mouseWorld - dragStart;
                double pull = -Vec2.Dot(d, Vec2.FromAngle(aimAngle));
                dragPower = Math.Clamp(pull / 260.0, 0, 1);
            }
            else
            {
                if (aimLocked && Distance(e.Location, aimLockPoint) < 30 * u) return;
                aimLocked = false;
                Vec2 v = mouseWorld - state.Cue.Pos;
                if (v.Length > 4) aimAngle = v.Angle;
            }
        }
    }

    void MoveCueTo(Vec2 w)
    {
        double maxX = state.KitchenOnly ? Table.HeadStringX : Table.Width - R;
        var p = new Vec2(Math.Clamp(w.X, R, maxX), Math.Clamp(w.Y, R, Table.Height - R));
        if (state.IsValidCuePlacement(p)) state.Cue.Pos = p;
    }

    public void MouseDown(MouseEventArgs e)
    {
        if (paused || phase == Phase.RackOver)
        {
            if (UiButton.Hit(buttons, e.Location)) host.Sound.Click();
            return;
        }
        if (e.Button == MouseButtons.Right)
        {
            dragging = false;
            dragPower = 0;
            return;
        }
        if (Distance(e.Location, spinCenter) <= spinRadius * 1.15f)
        {
            if (HumanTurn) { spinDrag = true; SetSpinFrom(e.Location); }
            return;
        }
        if (!HumanTurn) return;

        if (phase == Phase.Placing)
        {
            if (state.IsValidCuePlacement(state.Cue.Pos))
            {
                phase = Phase.Aiming;
                host.Sound.Click();
            }
            return;
        }
        if (phase == Phase.Aiming)
        {
            if (state.BallInHand && Vec2.Distance(mouseWorld, state.Cue.Pos) < R * 1.6)
            {
                phase = Phase.Placing;
                return;
            }
            dragging = true;
            dragStart = mouseWorld;
            dragPower = 0;
        }
    }

    public void MouseUp(MouseEventArgs e)
    {
        spinDrag = false;
        if (!dragging) return;
        dragging = false;
        if (HumanTurn && phase == Phase.Aiming && dragPower > 0.01) Shoot(aimAngle, dragPower, spinSide, spinTop);
        else dragPower = 0;
    }

    public void MouseWheel(MouseEventArgs e)
    {
        if (!HumanTurn || phase != Phase.Aiming || dragging) return;
        bool coarse = (Control.ModifierKeys & Keys.Shift) != 0;
        aimAngle += Math.Sign(e.Delta) * (coarse ? 0.012 : 0.0015);
        aimLocked = true;
        aimLockPoint = mouse;
    }

    void SetSpinFrom(Point p)
    {
        double dx = (p.X - spinCenter.X) / spinRadius, dy = (p.Y - spinCenter.Y) / spinRadius;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len > 0.85) { dx *= 0.85 / len; dy *= 0.85 / len; }
        spinSide = dx;
        spinTop = -dy;
    }

    void NudgeSpin(double side, double top)
    {
        spinSide = Math.Clamp(spinSide + side, -0.85, 0.85);
        spinTop = Math.Clamp(spinTop + top, -0.85, 0.85);
    }

    public bool KeyDown(Keys keyData)
    {
        var k = keyData & Keys.KeyCode;
        bool shift = (keyData & Keys.Shift) != 0;

        if (k == Keys.Escape)
        {
            if (phase == Phase.RackOver) return true;
            paused = !paused;
            if (paused) BuildPauseButtons();
            return true;
        }
        if (phase == Phase.RackOver && k is Keys.Enter or Keys.Space)
        {
            ContinueAfterRack();
            return true;
        }
        if (paused) return k is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Space;

        bool aiming = HumanTurn && phase == Phase.Aiming && !dragging;
        switch (k)
        {
            case Keys.Left:
            case Keys.Right:
                if (aiming)
                {
                    aimAngle += (k == Keys.Right ? 1 : -1) * (shift ? 0.012 : 0.0015);
                    aimLocked = true;
                    aimLockPoint = mouse;
                }
                return true;
            case Keys.Up:
            case Keys.Down:
                kbPower = Math.Clamp(kbPower + (k == Keys.Up ? 1 : -1) * (shift ? 0.1 : 0.02), 0.02, 1);
                return true;
            case Keys.Space:
                if (aiming) Shoot(aimAngle, kbPower, spinSide, spinTop);
                return true;
            case Keys.W: if (HumanTurn) NudgeSpin(0, 0.1); return true;
            case Keys.S: if (HumanTurn) NudgeSpin(0, -0.1); return true;
            case Keys.A: if (HumanTurn) NudgeSpin(-0.1, 0); return true;
            case Keys.D: if (HumanTurn) NudgeSpin(0.1, 0); return true;
            case Keys.R: if (HumanTurn) spinSide = spinTop = 0; return true;
            case Keys.H: RequestHint(); return true;
            case Keys.G:
                cfg.Guide = (AimGuide)(((int)cfg.Guide + 1) % 3);
                Say($"Aim guide: {cfg.Guide switch { AimGuide.Off => "off", AimGuide.GhostBall => "ghost ball", _ => "full prediction" }}", Theme.Cyan, 1.5);
                return true;
            case Keys.F:
                fastForward = !fastForward;
                Say(fastForward ? "Fast forward ON" : "Fast forward off", Theme.Cyan, 1.2);
                return true;
            case Keys.M:
                cfg.Sound = !cfg.Sound;
                host.Sound.Enabled = cfg.Sound;
                Say(cfg.Sound ? "Sound on" : "Sound off", Theme.Cyan, 1.2);
                return true;
        }
        return false;
    }

    void BuildPauseButtons()
    {
        buttons.Clear();
        buttons.Add(new UiButton { Text = "RESUME", Primary = true, OnClick = () => paused = false });
        buttons.Add(new UiButton { Text = "RESTART MATCH", OnClick = Restart });
        buttons.Add(new UiButton { Text = "MAIN MENU", Accent = Theme.Magenta, OnClick = host.ShowMenu });
    }

    void BuildRackOverButtons()
    {
        buttons.Clear();
        buttons.Add(new UiButton { Text = state.MatchOver ? "REMATCH" : "NEXT RACK", Primary = true, OnClick = ContinueAfterRack });
        buttons.Add(new UiButton { Text = "MAIN MENU", Accent = Theme.Magenta, OnClick = host.ShowMenu });
    }

    static float Distance(PointF a, PointF b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    // ================================================================== drawing

    public void Draw(Graphics g, Size s)
    {
        size = s;
        u = Math.Clamp(Math.Min(s.Width / 1280f, s.Height / 800f), 0.72f, 2.2f);
        Theme.Setup(g);
        float top = 100 * u, bottom = 88 * u;
        view.Layout(new RectangleF(14 * u, top, s.Width - 28 * u, s.Height - top - bottom));
        view.DrawBackground(g, s);

        var saved = g.Save();
        view.ApplyWorld(g);
        DrawWorld(g);
        g.Restore(saved);

        DrawHud(g);
        DrawBottomBar(g);
        DrawToasts(g);
        if (phase == Phase.RackOver) DrawRackOver(g);
        else if (paused) DrawPause(g);
    }

    void DrawWorld(Graphics g)
    {
        bool humanAim = HumanTurn && phase == Phase.Aiming;

        if (phase == Phase.Placing && state.KitchenOnly && HumanTurn)
        {
            using var kb = new SolidBrush(Color.FromArgb(22, 0, 240, 255));
            g.FillRectangle(kb, 0, 0, (float)Table.HeadStringX, (float)Table.Height);
            Gfx.GlowLine(g, new PointF((float)Table.HeadStringX, 0), new PointF((float)Table.HeadStringX, (float)Table.Height), Theme.Cyan, 1.2f, 2, 3, 30);
        }

        if (humanAim && cfg.Guide != AimGuide.Off) DrawAimGuide(g);

        // Soft shadows cast away from the lamp above the table centre, then the balls.
        float invScale = 1f / view.Scale;
        float pr = (float)(R * view.Scale);
        if (shadowSprite == null || Math.Abs(shadowRadius - pr) > 0.01f)
        {
            shadowSprite?.Dispose();
            shadowSprite = BallRenderer.MakeShadow(pr);
            shadowRadius = pr;
        }
        float sw = shadowSprite.Width * invScale;
        foreach (var b in state.Balls)
        {
            if (b.Pocketed) continue;
            Vec2 off = (b.Pos - Table.CenterSpot) * 0.012 + new Vec2(1.5, 3);
            g.DrawImage(shadowSprite, (float)(b.Pos.X + off.X) - sw / 2, (float)(b.Pos.Y + off.Y) - sw / 2, sw, sw);
        }
        foreach (var b in state.Balls)
        {
            if (b.Pocketed) continue;
            balls3d.Draw(g, b.Number, orient[b.Number], pr, new PointF((float)b.Pos.X, (float)b.Pos.Y), invScale);
        }
        foreach (var sk in sinks)
        {
            var p = Vec2.Lerp(sk.From, sk.To, Smooth(sk.T));
            balls3d.Draw(g, sk.Number, orient[sk.Number], pr, new PointF((float)p.X, (float)p.Y), invScale * (float)(1 - 0.7 * sk.T));
        }

        if (HumanTurn && (phase == Phase.Placing || (phase == Phase.Aiming && state.BallInHand)))
        {
            bool ok = state.IsValidCuePlacement(state.Cue.Pos);
            float rr = (float)(R + 4 + Math.Sin(clock * 6) * 1.5);
            using var ring = new GraphicsPath();
            ring.AddEllipse((float)state.Cue.Pos.X - rr, (float)state.Cue.Pos.Y - rr, rr * 2, rr * 2);
            Gfx.GlowPath(g, ring, ok ? Theme.Lime : Theme.Red, 1.2f, 1.6f, 3, 50);
        }

        if (humanAim || phase == Phase.AIAiming)
            DrawCue(g, aimAngle, phase == Phase.AIAiming || dragging ? dragPower : 0);

        fx.Draw(g);
    }

    (Ball? ball, double t) FirstHit(Vec2 c, Vec2 d)
    {
        Ball? hit = null;
        double best = double.MaxValue;
        foreach (var b in state.Balls)
        {
            if (b.Number == 0 || b.Pocketed) continue;
            Vec2 m = b.Pos - c;
            double proj = Vec2.Dot(m, d);
            if (proj <= 0) continue;
            double perp2 = m.LengthSq - proj * proj;
            double rr = 4 * R * R;
            if (perp2 > rr) continue;
            double t = proj - Math.Sqrt(rr - perp2);
            if (t < best) { best = t; hit = b; }
        }
        return (hit, best);
    }

    static double RayToRail(Vec2 c, Vec2 d)
    {
        double tx = d.X > 1e-9 ? (Table.Width - R - c.X) / d.X : d.X < -1e-9 ? (R - c.X) / d.X : double.MaxValue;
        double ty = d.Y > 1e-9 ? (Table.Height - R - c.Y) / d.Y : d.Y < -1e-9 ? (R - c.Y) / d.Y : double.MaxValue;
        return Math.Max(0, Math.Min(tx, ty));
    }

    static PointF P(Vec2 v) => new((float)v.X, (float)v.Y);

    void DrawAimGuide(Graphics g)
    {
        // The guide shows where the ball really goes: side spin squirts it off the stick line.
        var stick = state.CurrentPlayer.Cue;
        Vec2 c = state.Cue.Pos, d = Vec2.FromAngle(Physics.LaunchAngle(aimAngle, spinSide, stick));
        bool full = cfg.Guide == AimGuide.Full;
        bool eagle = stick.Has(CuePerk.EagleEye);
        double vision = stick.Vision;
        double tRail = RayToRail(c, d);
        var (hit, tHit) = FirstHit(c, d);

        if (hit != null && tHit < tRail)
        {
            Vec2 ghost = c + d * tHit;
            bool legal = rules.IsLegalFirstContact(state, hit.Number);
            Color col = legal ? GuideColor : Theme.Red;
            Gfx.GlowLine(g, P(c), P(ghost), Theme.A(col, 210), 1.1f, 1.6f, 2, 30, DashStyle.Dash);

            var gr = new RectangleF((float)(ghost.X - R), (float)(ghost.Y - R), (float)(2 * R), (float)(2 * R));
            using (var gb = new SolidBrush(Theme.A(col, 30))) g.FillEllipse(gb, gr);
            using (var gp = new Pen(Theme.A(col, 220), 1.3f)) g.DrawEllipse(gp, gr);
            if (!legal)
            {
                using var xp = new Pen(Theme.Red, 2f);
                float k = (float)(R * 0.55);
                g.DrawLine(xp, (float)ghost.X - k, (float)ghost.Y - k, (float)ghost.X + k, (float)ghost.Y + k);
                g.DrawLine(xp, (float)ghost.X - k, (float)ghost.Y + k, (float)ghost.X + k, (float)ghost.Y - k);
                return;
            }

            Vec2 n = (hit.Pos - ghost).Normalized();
            double toRail = RayToRail(hit.Pos, n);
            double objLen = full ? Math.Min(toRail + R, 700 * vision) : 70 * vision;
            Vec2 objEnd = hit.Pos + n * objLen;
            Gfx.GlowLine(g, P(hit.Pos), P(objEnd), Color.FromArgb(230, 255, 255, 255), 1.4f, 2, 3, 36);
            if (eagle && toRail < 700 * vision) DrawBounce(g, hit.Pos + n * toRail, n, 260, Color.White);

            if (full)
            {
                foreach (var pk in Table.Pockets)
                {
                    Vec2 rel = pk.Aim - hit.Pos;
                    double along = Vec2.Dot(rel, n);
                    if (along <= 0 || along > objLen + 3 * R) continue;
                    if (Math.Abs(Vec2.Cross(n, rel)) < 0.9 * R)
                    {
                        using var ring = new GraphicsPath();
                        float pr = (float)pk.DrawRadius + 3;
                        ring.AddEllipse((float)pk.DrawCenter.X - pr, (float)pk.DrawCenter.Y - pr, pr * 2, pr * 2);
                        Gfx.GlowPath(g, ring, Theme.Lime, 2f, 2.5f, 4, 50);
                    }
                }
            }

            Vec2 tan = d - n * Vec2.Dot(d, n);
            if (tan.Length > 0.02)
            {
                double len = (full ? 150 : 45) * vision * tan.Length;
                Gfx.GlowLine(g, P(ghost), P(ghost + tan.Normalized() * len), TangentColor, 1.1f, 1.5f, 2, 30);
            }
            else if (full && Math.Abs(spinTop) > 0.05)
            {
                // Straight-in: show follow / draw direction.
                Vec2 dir = d * Math.Sign(spinTop);
                Gfx.GlowLine(g, P(ghost), P(ghost + dir * (40 + 80 * Math.Abs(spinTop))), TangentColor, 1.1f, 1.5f, 2, 30);
            }
        }
        else
        {
            Vec2 p = c + d * tRail;
            Gfx.GlowLine(g, P(c), P(p), Theme.A(GuideColor, 200), 1.1f, 1.6f, 2, 30, DashStyle.Dash);
            if (full || eagle) DrawBounce(g, p, d, (eagle ? 320 : 130) * vision, GuideColor);
        }
    }

    /// <summary>Reflects a guide line off the cushion it reached at <paramref name="p"/>.</summary>
    void DrawBounce(Graphics g, Vec2 p, Vec2 d, double length, Color color)
    {
        bool vertical = p.X <= R + 0.01 || p.X >= Table.Width - R - 0.01;
        Vec2 d2 = vertical ? new Vec2(-d.X, d.Y) : new Vec2(d.X, -d.Y);
        double len = Math.Min(length, RayToRail(p, d2));
        Gfx.GlowLine(g, P(p), P(p + d2 * len), Theme.A(color, 120), 1f, 1.4f, 2, 22, DashStyle.Dash);
    }

    void DrawCue(Graphics g, double angle, double power)
    {
        Vec2 d = Vec2.FromAngle(angle);
        Vec2 tip = state.Cue.Pos - d * (R + 4 + power * 115);
        Vec2 butt = tip - d * 560;
        CueArt.Draw(g, P(tip), P(butt), 2.4f, 6.4f, state.CurrentPlayer.Cue, clock, 1f);
    }

    // ------------------------------------------------------------------ HUD

    void DrawHud(Graphics g)
    {
        float w = size.Width;
        float cardW = Math.Min(400 * u, w * 0.31f), cardH = 72 * u, y = 14 * u;
        int n = state.PlayerCount;
        for (int i = 0; i < n; i++)
        {
            var rect = i == 0 ? new RectangleF(18 * u, y, cardW, cardH) : new RectangleF(w - 18 * u - cardW, y, cardW, cardH);
            DrawPlayerCard(g, i, rect);
        }

        // Centre block.
        float cx = w / 2;
        Gfx.GlowText(g, rules.Name.ToUpperInvariant(), Theme.F(26 * u, FontStyle.Bold), Theme.Magenta, Color.White, new PointF(cx, y + 18 * u), spread: 3 * u, layers: 3);
        string sub = rules.IsSolo ? $"RACK {state.RackNumber}"
            : rules.UsesRaceTo ? (state.RaceTo == 1 ? "SINGLE RACK" : $"RACE TO {state.RaceTo}")
            : $"FIRST TO {state.TargetScore}";
        Gfx.Text(g, sub, Theme.F(13 * u, FontStyle.Bold), Theme.TextDim, new RectangleF(cx - 150 * u, y + 34 * u, 300 * u, 20 * u), StringAlignment.Center);

        int target = rules.TargetBall(state);
        if (target > 0)
        {
            Gfx.Text(g, "NEXT", Theme.F(12 * u, FontStyle.Bold), Theme.Cyan, new RectangleF(cx - 60 * u, y + 52 * u, 50 * u, 22 * u), StringAlignment.Far);
            float r = 10 * u;
            DrawMiniBall(g, target, new PointF(cx + 4 * u, y + 63 * u), r);
        }
        else if (!string.IsNullOrEmpty(rules.Hint(state)) && !state.RackOver)
        {
            Gfx.Text(g, rules.Hint(state), Theme.F(13 * u), Theme.Cyan, new RectangleF(cx - 170 * u, y + 52 * u, 340 * u, 22 * u), StringAlignment.Center);
        }
    }

    void DrawPlayerCard(Graphics g, int i, RectangleF rect)
    {
        var p = state.Players[i];
        bool active = state.Current == i && !state.RackOver;
        Color accent = Theme.PlayerColor(i);
        using var path = Gfx.RoundRect(rect, 14 * u);
        using (var br = new SolidBrush(Theme.A(Theme.Panel, 235))) g.FillPath(br, path);
        if (active) Gfx.GlowPath(g, path, accent, 1.8f * u, 3 * u, 4, 40);
        else using (var pen = new Pen(Theme.A(accent, 60), 1.2f * u)) g.DrawPath(pen, path);

        float px = rect.X + 18 * u;
        string tag = (p.IsAI ? $"CPU • {p.Level.ToString().ToUpperInvariant()}" : "HUMAN") + $" • {p.Cue.Name.ToUpperInvariant()}";
        Gfx.Text(g, p.Name, Theme.F(22 * u, FontStyle.Bold), active ? Color.White : Theme.Text, new RectangleF(px, rect.Y + 8 * u, rect.Width * 0.55f, 30 * u));
        var nameSize = g.MeasureString(p.Name, Theme.F(22 * u, FontStyle.Bold));
        Gfx.Text(g, tag, Theme.F(11 * u, FontStyle.Bold), Theme.A(CueArt.TierColor(p.Cue.Tier) == Theme.TextDim ? accent : CueArt.TierColor(p.Cue.Tier), 220),
            new RectangleF(px + nameSize.Width + 4 * u, rect.Y + 14 * u, rect.Right - 70 * u - (px + nameSize.Width + 4 * u), 20 * u));

        string status = rules.Status(state, i);
        if (active && phase == Phase.AIThinking)
            status = "Thinking" + new string('.', 1 + (int)(clock * 3) % 3);
        Gfx.Text(g, status, Theme.F(14 * u), active ? accent : Theme.TextDim, new RectangleF(px, rect.Y + 40 * u, rect.Width * 0.6f, 24 * u));

        // Racks won.
        float right = rect.Right - 16 * u;
        if (rules.UsesRaceTo && state.PlayerCount > 1)
        {
            Gfx.Text(g, p.RacksWon.ToString(), Theme.F(36 * u, FontStyle.Bold), accent, new RectangleF(right - 50 * u, rect.Y, 50 * u, rect.Height), StringAlignment.Far);
            right -= 56 * u;
        }

        var balls = rules.PlayerBalls(state, i);
        float r = 8.5f * u;
        float bx = right - r;
        for (int k = balls.Count - 1; k >= 0 && bx > rect.X + rect.Width * 0.55f; k--)
        {
            DrawMiniBall(g, balls[k], new PointF(bx, rect.Y + rect.Height - 20 * u), r);
            bx -= r * 2.3f;
        }
    }

    void DrawMiniBall(Graphics g, int number, PointF center, float r)
    {
        hudBalls.Draw(g, number, Mat3.Identity, r, center, 1f);
    }

    void DrawBottomBar(Graphics g)
    {
        float h = size.Height, w = size.Width;
        string hint = phase switch
        {
            Phase.Placing when HumanTurn => state.KitchenOnly
                ? "BALL IN HAND — place the cue ball behind the head string, then click"
                : "BALL IN HAND — move the cue ball anywhere, then click to place",
            Phase.Aiming when HumanTurn => state.BallInHand
                ? "Drag back & release to shoot  •  click the cue ball to move it  •  H: coach"
                : "Aim with the mouse  •  drag back & release to shoot  •  wheel / ←→ fine aim  •  H: coach",
            Phase.AIThinking or Phase.AIAiming or Phase.AIPlacing => $"{state.CurrentPlayer.Name} is planning the shot…  (F: fast forward)",
            Phase.Rolling => fastForward ? "Fast forward ON" : "",
            _ => "",
        };
        Gfx.Text(g, hint, Theme.F(15 * u), Theme.TextDim, new RectangleF(24 * u, h - 62 * u, w - 470 * u, 40 * u));
        Gfx.Text(g, "Esc: menu   G: guide   M: sound", Theme.F(12 * u), Theme.A(Theme.TextDim, 140), new RectangleF(24 * u, h - 30 * u, 400 * u, 20 * u));

        // Power meter.
        spinRadius = 30 * u;
        spinCenter = new PointF(w - 30 * u - spinRadius, h - 44 * u);
        powerRect = new RectangleF(spinCenter.X - spinRadius - 300 * u, h - 52 * u, 270 * u, 16 * u);
        double power = dragging || phase == Phase.AIAiming ? dragPower : kbPower;
        using (var bgPath = Gfx.RoundRect(powerRect, 8 * u))
        {
            using var bb = new SolidBrush(Theme.A(Theme.Panel, 240));
            g.FillPath(bb, bgPath);
            using var bp = new Pen(Theme.A(Theme.Violet, 110), 1f * u);
            g.DrawPath(bp, bgPath);
        }
        if (power > 0.003)
        {
            var fill = new RectangleF(powerRect.X, powerRect.Y, Math.Max(powerRect.Height, (float)(powerRect.Width * power)), powerRect.Height);
            using var fp = Gfx.RoundRect(fill, 8 * u);
            using var lg = new LinearGradientBrush(powerRect, Theme.Lime, Theme.Red, 0f)
            {
                InterpolationColors = new ColorBlend { Colors = [Theme.Lime, Theme.Cyan, Theme.Magenta, Theme.Red], Positions = [0f, 0.35f, 0.75f, 1f] },
            };
            g.FillPath(lg, fp);
            Gfx.GlowPath(g, fp, Theme.A(Theme.Mix(Theme.Cyan, Theme.Red, power), 200), 0.8f * u, 2 * u, 3, 30);
        }
        string label = dragging || phase == Phase.AIAiming ? "POWER" : "POWER  (↑↓ / Space)";
        Gfx.Text(g, $"{label}  {power * 100:0}%", Theme.F(12 * u, FontStyle.Bold), Theme.Text, new RectangleF(powerRect.X, powerRect.Y - 22 * u, powerRect.Width, 20 * u));

        // Spin widget.
        var sr = new RectangleF(spinCenter.X - spinRadius, spinCenter.Y - spinRadius, spinRadius * 2, spinRadius * 2);
        using (var sp = new GraphicsPath())
        {
            sp.AddEllipse(sr);
            using var wb = new PathGradientBrush(sp)
            {
                CenterPoint = new PointF(spinCenter.X - spinRadius * 0.3f, spinCenter.Y - spinRadius * 0.35f),
                CenterColor = Color.White,
                SurroundColors = [Theme.Hex("#9A98B0")],
            };
            g.FillPath(wb, sp);
            Gfx.GlowPath(g, sp, Theme.Cyan, 1.2f * u, 2 * u, 3, 34);
        }
        using (var cross = new Pen(Color.FromArgb(70, 20, 20, 40), 1f))
        {
            g.DrawLine(cross, sr.Left + 4 * u, spinCenter.Y, sr.Right - 4 * u, spinCenter.Y);
            g.DrawLine(cross, spinCenter.X, sr.Top + 4 * u, spinCenter.X, sr.Bottom - 4 * u);
        }
        var dot = new PointF(spinCenter.X + (float)(spinSide * spinRadius), spinCenter.Y - (float)(spinTop * spinRadius));
        using (var db = new SolidBrush(Theme.Red)) g.FillEllipse(db, dot.X - 5 * u, dot.Y - 5 * u, 10 * u, 10 * u);
        using (var dp = new Pen(Color.White, 1.2f * u)) g.DrawEllipse(dp, dot.X - 5 * u, dot.Y - 5 * u, 10 * u, 10 * u);
        Gfx.Text(g, "SPIN", Theme.F(11 * u, FontStyle.Bold), Theme.TextDim, new RectangleF(sr.X - 60 * u, sr.Bottom - 16 * u, 56 * u, 16 * u), StringAlignment.Far);
    }

    void DrawToasts(Graphics g)
    {
        var tableTop = view.ToScreen(new Vec2(Table.Width / 2, 60));
        float y = tableTop.Y;
        foreach (var t in toasts)
        {
            double fadeIn = Math.Min(1, t.Age / 0.15), fadeOut = Math.Min(1, (t.Life - t.Age) / 0.4);
            double a = Math.Clamp(Math.Min(fadeIn, fadeOut), 0, 1);
            float rise = (float)((1 - fadeIn) * 10 * u);
            var font = Theme.F(25 * u, FontStyle.Bold);
            var sz = g.MeasureString(t.Text, font);
            var box = new RectangleF(tableTop.X - sz.Width / 2 - 20 * u, y - 22 * u + rise, sz.Width + 40 * u, 44 * u);
            using (var path = Gfx.RoundRect(box, 22 * u))
            using (var br = new SolidBrush(Color.FromArgb((int)(170 * a), 7, 6, 15)))
                g.FillPath(br, path);
            Gfx.GlowText(g, t.Text, font, Theme.A(t.Color, (int)(255 * a)), Theme.A(Theme.Mix(t.Color, Color.White, 0.55), (int)(255 * a)),
                new PointF(tableTop.X, y + rise), spread: 3 * u, layers: 3, alpha: (int)(36 * a));
            y += 50 * u;
        }
    }

    void DrawOverlayPanel(Graphics g, string title, string subtitle, Color accent)
    {
        using (var dim = new SolidBrush(Color.FromArgb(175, 4, 3, 10))) g.FillRectangle(dim, new Rectangle(Point.Empty, size));
        float cx = size.Width / 2f, cy = size.Height / 2f;
        float bh = buttons.Sum(b => (b.Primary ? 70 : 58) * u);
        float ph = 150 * u + bh + 20 * u;
        var r = new RectangleF(cx - 300 * u, cy - ph / 2, 600 * u, ph);
        using var path = Gfx.RoundRect(r, 22 * u);
        using (var br = new SolidBrush(Theme.A(Theme.Panel, 245))) g.FillPath(br, path);
        Gfx.GlowPath(g, path, accent, 1.6f * u, 3 * u, 5, 30);
        Gfx.GlowText(g, title, Theme.F(36 * u, FontStyle.Bold), accent, Color.White, new PointF(cx, r.Y + 62 * u), spread: 4 * u);
        Gfx.Text(g, subtitle, Theme.F(16 * u), Theme.TextDim, new RectangleF(r.X, r.Y + 98 * u, r.Width, 26 * u), StringAlignment.Center);

        float by = r.Y + 150 * u;
        foreach (var b in buttons)
        {
            b.Rect = new RectangleF(cx - 150 * u, by, 300 * u, (b.Primary ? 56 : 46) * u);
            b.Draw(g, u);
            by += (b.Primary ? 70 : 58) * u;
        }
    }

    void DrawPause(Graphics g) =>
        DrawOverlayPanel(g, "PAUSED", $"{rules.Name} • {string.Join(" vs ", state.Players.Select(p => p.Name))}", Theme.Cyan);

    void DrawRackOver(Graphics g)
    {
        if (overlayTimer < 0.9) return; // let the last ball drop first
        var winner = state.RackWinner >= 0 ? state.Players[state.RackWinner] : null;
        string title = winner == null ? "RACK OVER"
            : state.MatchOver ? $"{winner.Name.ToUpperInvariant()} WINS!"
            : $"{winner.Name.ToUpperInvariant()} TAKES THE RACK";
        string subtitle = rules.UsesRaceTo
            ? string.Join("   •   ", state.Players.Select(p => $"{p.Name} {p.RacksWon}"))
            : string.Join("   •   ", state.Players.Select(p => $"{p.Name} {p.Score} pts"));
        if (state.Players.All(p => p.IsAI) && !state.MatchOver) subtitle += "   —   next rack starting…";
        Color accent = state.RackWinner >= 0 ? Theme.PlayerColor(state.RackWinner) : Theme.Cyan;
        DrawOverlayPanel(g, title, subtitle, accent);
        if (state.MatchOver && winner != null)
        {
            var rnd = new Random((int)(clock * 7));
            if (rnd.NextDouble() < 0.12)
                fx.Burst(new Vec2(rnd.NextDouble() * Table.Width, rnd.NextDouble() * Table.Height), rnd.Next(2) == 0 ? Theme.Cyan : Theme.Magenta, 18, 220);
        }
    }

    // ================================================================== screenshots

    /// <summary>Builds a mid-game position with the aim guide showing, for README screenshots.</summary>
    public void SetupDemo()
    {
        var s = state;
        s.Players[0].Cue = CueCatalog.Get("dragon");
        var cue = s.Cue;
        cue.Pos = new Vec2(Table.HeadStringX - 25, Table.Height / 2 - 30);
        var apex = s.ObjectBallsOnTable.OrderBy(b => b.Pos.X).First();
        Physics.Strike(cue, (apex.Pos - cue.Pos).Angle, 1.0, 0, 0, s.CurrentPlayer.Cue, true);
        s.BallInHand = false;
        var ev = Physics.Simulate(s.Balls);
        rules.ApplyShot(s, ev, rng);
        s.Current = 0;
        s.BallInHand = false;
        s.TableOpen = false;
        s.Players[0].Group = Group.Solids;
        s.Players[1].Group = Group.Stripes;
        var plan = AIPlayer.Plan(s, rules, AILevel.Pro, 4);
        aimAngle = plan.Angle;
        phase = Phase.Aiming;
        dragging = true;
        dragPower = 0.55;
        kbPower = 0.55;
        spinTop = -0.3;
        Say($"Coach: {plan.Plan}", Theme.Lime, 60);
        fx.Pulse(Table.Pockets[2].DrawCenter, Theme.Lime, 30, 60);
    }
}
