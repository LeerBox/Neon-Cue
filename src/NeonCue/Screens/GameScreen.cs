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

    static double R => Table.R;

    readonly IHost host;
    readonly AppSettings cfg;
    readonly GameRules rules;
    readonly Random rng = new();
    readonly TableView view = new();
    readonly Particles fx = new();
    readonly BallRenderer balls3d = new();
    readonly BallRenderer hudBalls = new();
    readonly Mat3[] orient = new Mat3[BallRenderer.MaxBalls];
    readonly Vec2[] lastPos = new Vec2[BallRenderer.MaxBalls];
    Bitmap? shadowSprite;
    float shadowRadius;
    int rollUpdates;
    readonly List<Toast> toasts = new();
    readonly List<Sink> sinks = new();
    readonly List<PocketFlash> flashes = new();
    readonly List<UiButton> buttons = new();
    readonly List<ShotMark> history = new();
    GameState state;

    /// <summary>Practice only: an object ball being carried to a new spot. -1 when none.</summary>
    int held = -1;
    Vec2 heldHome;
    /// <summary>Bumped when a practice ball is set down, so the aim guide resimulates.</summary>
    int layout;

    Phase phase;
    bool paused;
    double phaseTime, rollTime, physicsAcc, clock, overlayTimer;
    double aimAngle = Math.PI, kbPower = 0.45, dragPower, spinSide, spinTop;
    bool dragging, spinDrag, fastForward;
    /// <summary>Button-down aiming, or a committed draw along the cue. Release strikes only from Pulling.</summary>
    enum CueStroke { Aiming, Pulling }
    CueStroke cueStroke;
    /// <summary>World units of backward draw that equal a full-power strike.</summary>
    const double MaxPull = 260;
    /// <summary>Backward drag, in world units, that commits a draw. Shorter than this is not a shot.</summary>
    const double PullCommit = 4;
    /// <summary>Easing back inside this distance cancels the draw.</summary>
    const double PullRelease = 2;
    double cueFollow = -1, followAim, followPower;
    Point mouse;
    Vec2 mouseWorld, dragOrigin;
    double strokeAim;
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
        Look.Snooker = rules.Mode == GameMode.Snooker;
        state = NewMatch(0);
        for (int i = 0; i < orient.Length; i++) orient[i] = Mat3.RandomResting(rng);
        SyncRolling();
        BeginTurn();
    }

    /// <summary>
    /// Rolls each ball's 3D orientation by the distance it moved since last frame (used for moves
    /// outside the physics, like dragging the cue ball in hand; during a shot the real spin drives it).
    /// </summary>
    void SyncRolling()
    {
        bool tidy = ++rollUpdates % 120 == 0;
        foreach (var b in state.Balls)
        {
            int n = b.Number;
            Vec2 d = b.Pos - lastPos[n];
            double len = d.Length;
            if (phase != Phase.Rolling && !b.Pocketed && len > 1e-6 && len < 3 * R)
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
        if (rules.IsSolo) return cfg.Opponent == OpponentType.CpuVsCpu ? [Bot("Neon")] : [Human("You")];
        return cfg.Opponent switch
        {
            OpponentType.Cpu => [Human("You"), Bot("Neon")],
            OpponentType.TwoPlayer => [Human("Player 1"), Human("Player 2")],
            _ => [Bot("Alpha"), Bot("Omega")],
        };
    }

    bool HumanTurn => !state.CurrentPlayer.IsAI && !state.RackOver;
    /// <summary>At the cue-ball centre the direction is undefined. Anywhere else the cue points at the pointer.</summary>
    const double AimCenterEpsilon = 0.75;
    Keys? aimModifiersOverride;
    Vec2 lastMouseWorld;
    double lastAimMoveTime;

    /// <summary>Background simulation of the current aim at 20 power levels.</summary>
    sealed class ShotPreview
    {
        public int Version;
        public int Target = -1;
        public readonly bool[] Pots = new bool[PreviewSteps + 1];
        public readonly Vec2?[] Stops = new Vec2?[PreviewSteps + 1];
        public bool Any => Pots.Any(x => x);
    }
    const int PreviewSteps = 20;
    volatile ShotPreview? preview;
    int previewVersion;
    (double aim, double side, double top, Vec2 cue, int shot, int layout) previewKey;
    bool pressedOnCue;

    // ================================================================== flow

    void BeginTurn()
    {
        dragging = false;
        cueStroke = CueStroke.Aiming;
        dragPower = 0;
        cueFollow = -1;
        aiShot = null;
        phaseTime = 0;
        lastMouseWorld = mouseWorld;
        if (state.RackOver)
        {
            phase = Phase.RackOver;
            overlayTimer = 0;
            BuildRackOverButtons();
            return;
        }
        spinSide = spinTop = 0;
        var p = state.CurrentPlayer;
        if (p.IsAI && rules.ShouldConcede(state, state.Current))
        {
            Say($"{p.Name} concedes the frame.", Theme.Amber, 3);
            rules.ConcedeFrame(state, state.Current);
            host.Sound.Win();
            BeginTurn();
            return;
        }
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
                Say($"{p.Name} to play.", Theme.PlayerColor(state.Current), 1.6);
        }
    }

    void Shoot(double angle, double power, double side, double top)
    {
        if (held >= 0) return;
        ShotTrace.Clear();
        if (!state.CurrentPlayer.IsAI) RememberShot();
        // Cheaper cues wobble: human strokes pick up the cue's aim error (AI noise already includes it).
        var stick = state.CurrentPlayer.Cue;
        if (cfg.StrokeError && !state.CurrentPlayer.IsAI && stick.AimError > 0) angle += Gauss() * stick.WobbleDeg(power) * Math.PI / 180;
        Physics.Strike(state.Cue, angle, power, side, top, stick, state.IsBreakShot);
        events = new ShotEvents();
        physicsAcc = 0;
        SnapInterpolation();
        rollTime = 0;
        phase = Phase.Rolling;
        phaseTime = 0;
        dragging = false;
        cueStroke = CueStroke.Aiming;
        followAim = angle;
        followPower = power;
        cueFollow = 0;
        dragPower = 0;
        state.BallInHand = false;
        hintTask = null;
        host.Sound.Cue(power, state.Cue.Pos.X / Table.Width, stick.Id);
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
        string shooterName = state.CurrentPlayer.Name;
        int runBefore = state.Run, breakBefore = state.CurrentBreak;
        var o = rules.ApplyShot(state, events, rng);
        LogShot(shooterName, o, runBefore + events.PocketedObjects.Count(), breakBefore + o.PointsScored);
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
            Say($"Ball in hand. {state.CurrentPlayer.Name} to place.", Theme.Amber);
        if (coins > 0) Say($"+{coins} coins.", Theme.Amber, o.RackOver ? 4 : 1.6);
        if (o.RackOver) host.Sound.Win();
        if (!o.RackOver && !o.Foul && shooter != state.Current && state.PlayerCount > 1 && events.PocketedObjects.Any() == false)
            fx.Pulse(state.Cue.Pos, Theme.PlayerColor(state.Current), 26);
        BeginTurn();
    }

    void ContinueAfterRack()
    {
        if (state.MatchOver) state = NewMatch(1 - state.Breaker);
        else rules.StartNextRack(state, rng);
        history.Clear();
        toasts.Clear();
        BeginTurn();
    }

    void Restart()
    {
        state = NewMatch(0);
        paused = false;
        history.Clear();
        held = -1;
        toasts.Clear();
        sinks.Clear();
        flashes.Clear();
        BeginTurn();
    }

    /// <summary>Table, score and coins from just before one of your strokes. The opponent's reply is not stored on its own, so taking a shot back also undoes whatever they played after it.</summary>
    sealed class ShotMark
    {
        public required GameState State;
        public required Mat3[] Orient;
        public required List<string> Log;
        public int Coins;
        public double Aim, Power, Side, Top;
    }

    const int MaxHistory = 8;

    void RememberShot()
    {
        history.Add(new ShotMark
        {
            State = state.Clone(),
            Orient = orient.ToArray(),
            Log = [.. eventLog],
            Coins = cfg.Coins,
            Aim = aimAngle,
            Power = kbPower,
            Side = spinSide,
            Top = spinTop,
        });
        if (history.Count > MaxHistory) history.RemoveAt(0);
    }

    bool UndoLastShot()
    {
        if (phase == Phase.Rolling)
        {
            Note("Wait until the balls have stopped.");
            return false;
        }
        if (history.Count == 0)
        {
            Note("There is no shot to restore.");
            return false;
        }
        if (held >= 0) CancelHeld();
        var mark = history[^1];
        history.RemoveAt(history.Count - 1);
        state = mark.State;
        Array.Copy(mark.Orient, orient, Math.Min(orient.Length, mark.Orient.Length));
        cfg.Coins = mark.Coins;
        cfg.Save();
        eventLog.Clear();
        eventLog.AddRange(mark.Log);
        aiTask = null;
        hintTask = null;
        aiShot = null;
        sinks.Clear();
        flashes.Clear();
        toasts.Clear();
        paused = false;
        dragging = false;
        dragPower = 0;
        foreach (var ball in state.Balls)
            lastPos[ball.Number] = prevPos[ball.Number] = ball.Pos;
        InvalidateGuides();
        BeginTurn();
        if (!state.CurrentPlayer.IsAI && !state.RackOver)
        {
            kbPower = mark.Power;
            spinSide = mark.Side;
            spinTop = mark.Top;
            PlaceAim(mark.Aim);
        }
        Say("The previous shot has been restored.", Theme.Cyan, 1.6);
        return true;
    }

    void Note(string text)
    {
        if (toasts.Any(t => t.Text == text)) return;
        Say(text, Theme.TextDim, 1.2);
    }

    void InvalidateGuides()
    {
        previewVersion++;
        trajVersion++;
        preview = null;
        trajectory = null;
        previewKey = default;
        trajKey = default;
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
        if (cueFollow >= 0 && (cueFollow += dt) > 0.22) cueFollow = -1;
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
        for (int i = flashes.Count - 1; i >= 0; i--)
        {
            flashes[i].Age += dt;
            if (flashes[i].Age > PocketFlash.Life) flashes.RemoveAt(i);
        }

        SyncRolling();
        UpdatePreview();
        UpdateTrajectory();
        UpdateSuggestion();
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

    /// <summary>
    /// Turns each ball by its true angular velocity, so a drawn ball visibly spins backwards while it
    /// slides forwards, and side spin turns the numbers around the vertical.
    /// </summary>
    void ApplySpin(double dt)
    {
        foreach (var b in state.Balls)
        {
            if (b.Pocketed) continue;
            // Physics frame has z down into the cloth; the renderer's z points up at the viewer.
            double wx = -b.Wx, wy = -b.Wy, wz = b.Wz;
            double w = Math.Sqrt(wx * wx + wy * wy + wz * wz);
            if (w < 1e-6) continue;
            orient[b.Number] = Mat3.Rotation(new Vec3(wx / w, wy / w, wz / w), w * dt) * orient[b.Number];
        }
    }

    // Physics runs in fixed 1/480 s steps, frames don't line up with them; drawing each ball between
    // its last two physics positions (by the leftover time) keeps motion perfectly even at any frame rate.
    readonly Vec2[] prevPos = new Vec2[BallRenderer.MaxBalls];
    double interpAlpha = 1;

    void SnapInterpolation()
    {
        foreach (var b in state.Balls) prevPos[b.Number] = b.Pos;
        interpAlpha = 1;
    }

    Vec2 DrawPos(Ball b) => phase == Phase.Rolling && !b.Pocketed ? Vec2.Lerp(prevPos[b.Number], b.Pos, interpAlpha) : b.Pos;

    void UpdateRolling(double dt)
    {
        physicsAcc += dt * (fastForward ? 4 : 1);
        bool moving = true;
        int guard = 0;
        while (physicsAcc >= Physics.Dt && guard++ < 4000)
        {
            physicsAcc -= Physics.Dt;
            rollTime += Physics.Dt;
            foreach (var b in state.Balls) prevPos[b.Number] = b.Pos;
            if (!Physics.Step(state.Balls, events, this)) { moving = false; break; }
            ApplySpin(Physics.Dt);
        }
        if (rollTime > Physics.MaxSimTime)
        {
            foreach (var b in state.Balls) b.Stop();
            moving = false;
        }
        interpAlpha = Math.Clamp(physicsAcc / Physics.Dt, 0, 1);
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
        host.Sound.Clack(impactSpeed, (a.Pos.X + b.Pos.X) / 2 / Table.Width);
        if (impactSpeed > 700 && !Look.Realistic) fx.Burst((a.Pos + b.Pos) / 2, Theme.Mix(Theme.Cyan, Color.White, 0.3), 4, impactSpeed * 0.12, 1.4f);
    }

    public void CushionHit(Ball ball, double impactSpeed) => host.Sound.Cushion(impactSpeed, ball.Pos.X / Table.Width);

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
        host.Sound.Pocket(pocket.DrawCenter.X / Table.Width);
        flashes.Add(new PocketFlash { At = pocket.DrawCenter, Radius = (float)pocket.DrawRadius });
    }

    sealed class PocketFlash
    {
        public const double Life = 0.5;
        public Vec2 At;
        public float Radius;
        public double Age;
    }

    // ================================================================== hint (AI coach)

    void RequestHint()
    {
        if (held >= 0 || !HumanTurn || hintTask != null || phase is not (Phase.Aiming or Phase.Placing)) return;
        Say("The coach is selecting a shot.", Theme.TextDim, 1.4);
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
        kbPower = Math.Round(h.Power, 2);
        spinSide = h.SpinSide;
        spinTop = h.SpinTop;
        PlaceAim(h.Angle);
        Say($"Coach: {h.Plan}. Press Space to play.", Theme.Lime, 4);
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
        if (held >= 0)
        {
            state.Balls[held].Pos = new Vec2(
                Math.Clamp(mouseWorld.X, R, Table.Width - R),
                Math.Clamp(mouseWorld.Y, R, Table.Height - R));
            lastMouseWorld = mouseWorld;
            return;
        }
        if (!HumanTurn) return;

        if (phase == Phase.Placing)
        {
            MoveCueTo(mouseWorld);
        }
        else if (phase == Phase.Aiming)
        {
            if (dragging) ApplyDrag();
            else OnAimMouseMove(stroke: false);
        }
        lastMouseWorld = mouseWorld;
    }

    Keys AimModifiers => aimModifiersOverride ?? Control.ModifierKeys;

    /// <summary>
    /// Button held. Sideways motion keeps aiming, one to one with the pointer. A drag back along
    /// the cue becomes the draw: the angle stays, and power is that backward distance over a full
    /// draw. Easing forward, or swinging off the draw, cancels it. Only the release strikes.
    /// </summary>
    void ApplyDrag()
    {
        Vec2 axis = Vec2.FromAngle(strokeAim);
        Vec2 delta = mouseWorld - dragOrigin;
        double back = -Vec2.Dot(delta, axis);
        double side = Math.Abs(Vec2.Cross(axis, delta));

        if (cueStroke == CueStroke.Pulling)
        {
            bool swungOff = side > 12 && side > back * 1.5;
            if (back < PullRelease || swungOff)
            {
                cueStroke = CueStroke.Aiming;
                dragPower = 0;
                OnAimMouseMove(stroke: false);
                dragOrigin = mouseWorld;
                strokeAim = aimAngle;
                return;
            }
            aimAngle = strokeAim;
            dragPower = state.CurrentPlayer.Cue.ShapePull(back / MaxPull);
            NoteAim(applied: false, stroke: true);
            return;
        }

        // Gathering a straight draw. Do not retarget, or the pointer crossing the ball flips the cue.
        if (back > PullCommit && back >= side)
        {
            cueStroke = CueStroke.Pulling;
            aimAngle = strokeAim;
            dragPower = state.CurrentPlayer.Cue.ShapePull(back / MaxPull);
            host.Sound.CueDraw(state.CurrentPlayer.Cue.Id, state.Cue.Pos.X / Table.Width);
            NoteAim(applied: false, stroke: true);
            return;
        }
        if (back > 0 && back >= side)
        {
            aimAngle = strokeAim;
            dragPower = 0;
            NoteAim(applied: false, stroke: true);
            return;
        }

        dragPower = 0;
        OnAimMouseMove(stroke: false);
        if (side > 3 && side > back)
        {
            dragOrigin = mouseWorld;
            strokeAim = aimAngle;
        }
    }

    void CancelStroke()
    {
        dragging = false;
        cueStroke = CueStroke.Aiming;
        dragPower = 0;
    }

    /// <summary>
    /// The cue points at the pointer. The angle is atan2 of the cursor relative to the cue ball,
    /// in the same table space the stick, the guide, and the shot use. No offset, no gain, no snap.
    /// </summary>
    void OnAimMouseMove(bool stroke)
    {
        lastAimMoveTime = clock;
        Vec2 rel = mouseWorld - state.Cue.Pos;
        if (rel.LengthSq <= AimCenterEpsilon * AimCenterEpsilon)
        {
            NoteAim(applied: false, stroke);
            return;
        }
        aimAngle = Math.Atan2(rel.Y, rel.X);
        NoteAim(applied: true, stroke);
    }

    /// <summary>Arrow and wheel nudge. The next mouse sample replaces this with the pointer angle.</summary>
    void PlaceAim(double angle) => aimAngle = Math.IEEERemainder(angle, 2 * Math.PI);

    void NudgeAim(double delta) => PlaceAim(aimAngle + delta);

    void NoteAim(bool applied, bool stroke) =>
        AimDiag.Add(new AimDiag.Sample(aimAngle, applied, dragging, stroke, phase.ToString()));

    public void PointerCaptureLost() => CancelStroke();

    /// <summary>
    /// Re-simulates the aimed shot (exact physics, no wobble) at every power step whenever the aim,
    /// spin or table changes, so the HUD can show which powers actually pot the ball.
    /// </summary>
    void UpdatePreview()
    {
        if (held >= 0 || !HumanTurn || phase != Phase.Aiming || cfg.Guide == AimGuide.Off)
        {
            preview = null;
            return;
        }
        var key = (aim: aimAngle, side: spinSide, top: spinTop, cue: state.Cue.Pos, shot: state.ShotNumber, layout);
        bool same = Math.Abs(key.aim - previewKey.aim) < 1e-6 && key.side == previewKey.side && key.top == previewKey.top
            && Vec2.DistanceSq(key.cue, previewKey.cue) < 1e-6 && key.shot == previewKey.shot && key.layout == previewKey.layout;
        if (same) return; // up to date, or a run for this exact shot is in flight
        previewKey = key;

        var hit = PredictedHit();
        int version = ++previewVersion;
        if (hit is null) { preview = new ShotPreview { Version = version }; return; }
        int target = hit.Value.ball;
        var snap = state.Clone();
        snap.BallInHand = false;
        double angle = aimAngle, side = spinSide, top = spinTop;
        var stick = state.CurrentPlayer.Cue;
        bool legal = rules.IsLegalFirstContact(state, target);
        preview = null;
        Task.Run(() =>
        {
            var res = new ShotPreview { Version = version, Target = target };
            for (int i = 1; i <= PreviewSteps; i++)
            {
                if (version != previewVersion) return;
                var c = snap.Clone();
                Physics.Strike(c.Cue, angle, i / (double)PreviewSteps, side, top, stick, c.IsBreakShot);
                var ev = Physics.Simulate(c.Balls);
                var tb = c.Balls[target];
                res.Pots[i] = legal && ev.FirstContact == target && tb.Pocketed && !ev.CueScratched;
                res.Stops[i] = tb.Pocketed ? null : tb.Pos;
            }
            if (version == previewVersion) preview = res;
        });
    }

    /// <summary>The aimed shot played out in the real physics: what the guide draws is what will happen.</summary>
    sealed class Trajectory
    {
        public readonly List<Vec2> CuePath = new();
        public readonly List<Vec2> ObjPath = new();
        public int ContactIndex = -1;
        public Vec2? Contact;
        public int Target = -1;
        public bool TargetPotted, CuePotted;
        public Pocket? TargetPocket, CuePocket;
    }

    sealed class PocketWatch : IPhysicsListener
    {
        public readonly Dictionary<int, Pocket> Pockets = new();
        public void BallCollision(Ball a, Ball b, double impactSpeed) { }
        public void CushionHit(Ball ball, double impactSpeed) { }
        public void BallPocketed(Ball ball, Pocket pocket) => Pockets[ball.Number] = pocket;
    }

    volatile Trajectory? trajectory;
    int trajVersion;
    (double aim, double power, double side, double top, Vec2 cue, int shot, int layout) trajKey;

    void UpdateTrajectory()
    {
        if (held >= 0 || !HumanTurn || phase != Phase.Aiming || cfg.Guide == AimGuide.Off)
        {
            trajectory = null;
            trajKey = default;
            return;
        }
        double power = Math.Max(0.03, dragging ? dragPower : kbPower);
        var key = (aim: aimAngle, power: Math.Round(power, 3), side: spinSide, top: spinTop, cue: state.Cue.Pos, shot: state.ShotNumber, layout);
        bool same = Math.Abs(key.aim - trajKey.aim) < 1e-7 && key.power == trajKey.power && key.side == trajKey.side
            && key.top == trajKey.top && Vec2.DistanceSq(key.cue, trajKey.cue) < 1e-8 && key.shot == trajKey.shot && key.layout == trajKey.layout;
        if (same) return;
        trajKey = key;
        int version = ++trajVersion;
        var snap = state.Clone();
        snap.BallInHand = false;
        var stick = state.CurrentPlayer.Cue;
        bool brk = state.IsBreakShot;
        double angle = aimAngle, side = spinSide, top = spinTop;
        Task.Run(() =>
        {
            var t = new Trajectory();
            var balls = snap.Balls;
            var watch = new PocketWatch();
            var ev = new ShotEvents();
            Physics.Strike(snap.Cue, angle, power, side, top, stick, brk);
            t.CuePath.Add(snap.Cue.Pos);
            for (int i = 0; i < 480 * 10; i++)
            {
                if (version != trajVersion) return;
                bool moving = Physics.Step(balls, ev, watch);
                if (t.Target < 0 && ev.FirstContact >= 0)
                {
                    t.Target = ev.FirstContact;
                    t.Contact = snap.Cue.Pos;
                    t.CuePath.Add(snap.Cue.Pos);
                    t.ContactIndex = t.CuePath.Count - 1;
                    t.ObjPath.Add(balls[t.Target].Pos);
                }
                if (i % 2 == 0 || !moving)
                {
                    if (!snap.Cue.Pocketed) t.CuePath.Add(snap.Cue.Pos);
                    if (t.Target > 0 && !balls[t.Target].Pocketed) t.ObjPath.Add(balls[t.Target].Pos);
                }
                if (!moving) break;
            }
            t.TargetPotted = t.Target > 0 && balls[t.Target].Pocketed;
            t.CuePotted = snap.Cue.Pocketed;
            if (t.Target > 0) t.TargetPocket = watch.Pockets.GetValueOrDefault(t.Target);
            t.CuePocket = watch.Pockets.GetValueOrDefault(0);
            if (t.TargetPotted && t.TargetPocket != null) t.ObjPath.Add(t.TargetPocket.DrawCenter);
            if (version == trajVersion) trajectory = t;
        });
    }

    int PowerStep(double power) => Math.Clamp((int)Math.Round(power * PreviewSteps), 1, PreviewSteps);

    readonly record struct SuggestId(int Target, int Pocket, double Power, double Side, double Top, int Shot, int Layout, double CueX, double CueY);
    SuggestId suggestId;
    bool suggestValid;
    double suggestShown = double.NaN;
    int suggestVersion;
    readonly object suggestGate = new();

    /// <summary>
    /// Aim assist draws a recommended line. It never writes aimAngle. While the hand is moving the
    /// line is the jaw-clearing pocket direction; once the hand rests, throw is corrected in the
    /// real solver. The white guide remains the shot that will actually be played.
    /// </summary>
    void UpdateSuggestion()
    {
        if (held >= 0 || !HumanTurn || phase != Phase.Aiming || cfg.Assist == AimAssist.Off || dragging)
        {
            suggestValid = false;
            lock (suggestGate) { suggestShown = double.NaN; suggestVersion++; }
            return;
        }
        double cursorPx = Math.Max(1, Vec2.Distance(mouseWorld, state.Cue.Pos) * view.Scale);
        var line = FindPocketLine(aimAngle, cursorPx);
        if (line is null)
        {
            suggestValid = false;
            lock (suggestGate) { suggestShown = double.NaN; suggestVersion++; }
            return;
        }
        var id = new SuggestId(line.Value.Target, line.Value.Pocket, Math.Round(Math.Max(0.05, kbPower), 3),
            spinSide, spinTop, state.ShotNumber, layout, state.Cue.Pos.X, state.Cue.Pos.Y);
        if (clock - lastAimMoveTime <= 0.12)
        {
            if (!suggestValid || !suggestId.Equals(id))
                lock (suggestGate) suggestShown = line.Value.Stick;
            return;
        }
        if (suggestValid && suggestId.Equals(id)) return;
        suggestId = id;
        suggestValid = true;
        // Throw correction runs off the UI thread. The geometric line is already showing, and a late
        // result is dropped if the player has aimed somewhere else.
        int version;
        lock (suggestGate) { version = ++suggestVersion; suggestShown = line.Value.Stick; }
        var snap = state.Clone();
        var found = line.Value;
        double power = Math.Max(0.05, kbPower), side = spinSide, top = spinTop;
        var stick = state.CurrentPlayer.Cue;
        Task.Run(() =>
        {
            double refined = ShotSolver.RefineAim(snap, found.Target, found.Aim, found.Stick, power, side, top, stick);
            lock (suggestGate)
            {
                if (version == suggestVersion) suggestShown = refined;
            }
        });
    }

    /// <summary>
    /// Nearest legal pocket line inside the assist window, or null when nothing is close. The window
    /// only decides whether a suggestion is drawn. It is not an angle the cue is allowed to sit on.
    /// </summary>
    (int Target, int Pocket, double Stick, Vec2 Aim)? FindPocketLine(double raw, double cursorDistPx)
    {
        double window = cfg.Assist switch
        {
            AimAssist.Strong => Math.Clamp(4.0 / Math.Max(1, cursorDistPx), 1.0 * Math.PI / 180, 4.5 * Math.PI / 180),
            AimAssist.Light => Math.Clamp(2.0 / Math.Max(1, cursorDistPx), 0.3 * Math.PI / 180, 1.5 * Math.PI / 180),
            _ => 0,
        };
        if (window <= 0) return null;
        var stick = state.CurrentPlayer.Cue;
        Vec2 c = state.Cue.Pos;
        double bestD = window;
        (int Target, int Pocket, double Stick, Vec2 Aim)? best = null;
        for (int pi = 0; pi < Table.Pockets.Length; pi++)
        {
            var p = Table.Pockets[pi];
            foreach (var t in state.Balls)
            {
                if (t.Number == 0 || t.Pocketed || !rules.IsLegalFirstContact(state, t.Number) || !rules.IsGoodPot(state, t.Number)) continue;
                if (!PocketEntry.TryGet(p, t.Pos, out Vec2 aim)) continue;
                Vec2 dir = (aim - t.Pos).Normalized();
                double approach = Math.Acos(Math.Clamp(Vec2.Dot(dir, -p.Inward), -1, 1));
                if (approach > (p.IsCorner ? 1.15 : 0.95)) continue;
                Vec2 ghost = t.Pos - dir * (2 * R);
                Vec2 cg = ghost - c;
                if (cg.Length < 1 || Vec2.Dot(cg.Normalized(), dir) < 0.17) continue;
                if (!PathClear(c, ghost, 0, t.Number) || !PathClear(t.Pos, aim, t.Number, 0)) continue;
                double stickAngle = cg.Angle + spinSide * stick.Squirt * Math.PI / 180;
                double d = Math.Abs(Math.IEEERemainder(stickAngle - raw, 2 * Math.PI));
                double pocketRun = Vec2.Distance(t.Pos, aim);
                double cut = Math.Acos(Math.Clamp(Vec2.Dot(cg.Normalized(), dir), -1, 1));
                double ease = 1.0 / (1.0 + pocketRun / 850.0 + cut * 0.85);
                double allow = window * (0.5 + 0.5 * ease);
                if (d < allow && d < bestD) { bestD = d; best = (t.Number, pi, stickAngle, aim); }
            }
        }
        return best;
    }

    bool PathClear(Vec2 a, Vec2 b, int ex1, int ex2)
    {
        foreach (var o in state.Balls)
        {
            if (o.Pocketed || o.Number == ex1 || o.Number == ex2) continue;
            if (Vec2.DistanceToSegment(o.Pos, a, b) < 2 * R * 0.98) return false;
        }
        return true;
    }

    bool CanMoveBalls() => rules.IsSolo && HumanTurn && phase is Phase.Aiming or Phase.Placing;

    void TryPickBall()
    {
        int best = -1;
        double bestD = R * 1.7;
        foreach (var b in state.Balls)
        {
            if (b.Pocketed || b.Number == 0) continue;
            double d = Vec2.Distance(b.Pos, mouseWorld);
            if (d < bestD) { bestD = d; best = b.Number; }
        }
        if (best < 0) return;
        held = best;
        heldHome = state.Balls[best].Pos;
        InvalidateGuides();
    }

    void CancelHeld()
    {
        if (held < 0) return;
        state.Balls[held].Pos = heldHome;
        lastPos[held] = heldHome;
        held = -1;
        InvalidateGuides();
    }

    bool Placeable(int number, Vec2 p)
    {
        if (!Table.InsidePlayArea(p)) return false;
        double min = (2 * R + 0.5) * (2 * R + 0.5);
        foreach (var b in state.Balls)
        {
            if (b.Pocketed || b.Number == number) continue;
            if (Vec2.DistanceSq(b.Pos, p) < min) return false;
        }
        return true;
    }

    void MoveCueTo(Vec2 w)
    {
        if (state.KitchenOnly && Table.HasD)
        {
            // Keep the cue ball inside the D: behind the baulk line, within the semicircle.
            Vec2 v = w - Table.DCenter;
            if (v.X > 0) v = new Vec2(0, v.Y);
            double max = Table.DRadius - 0.5;
            if (v.Length > max) v = v.Normalized() * max;
            var d = Table.DCenter + v;
            if (state.IsValidCuePlacement(d)) state.Cue.Pos = d;
            return;
        }
        double maxX = state.KitchenOnly ? Table.HeadStringX : Table.Width - R;
        var p = new Vec2(Math.Clamp(w.X, R, maxX), Math.Clamp(w.Y, R, Table.Height - R));
        if (state.IsValidCuePlacement(p)) state.Cue.Pos = p;
    }

    public void MouseDown(MouseEventArgs e)
    {
        mouse = e.Location;
        mouseWorld = view.ToWorld(e.Location);
        if (paused || phase == Phase.RackOver)
        {
            if (UiButton.Hit(buttons, e.Location)) host.Sound.Click();
            return;
        }
        if (e.Button == MouseButtons.Right)
        {
            // A pull in progress cancels. A bare right-click, in Practice, picks up a ball.
            bool pulling = dragging;
            CancelStroke();
            if (!pulling && CanMoveBalls())
            {
                if (held >= 0) CancelHeld();
                else TryPickBall();
            }
            return;
        }
        if (Distance(e.Location, spinCenter) <= spinRadius * 1.15f)
        {
            if (HumanTurn && held < 0) { spinDrag = true; SetSpinFrom(e.Location); }
            return;
        }
        if (held >= 0)
        {
            if (Placeable(held, state.Balls[held].Pos))
            {
                held = -1;
                layout++;
                InvalidateGuides();
                host.Sound.Click();
            }
            else Say("That position overlaps another ball.", Theme.Red, 1.2);
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
            // The angle at the press is the angle that will be played. Pressing does not move the cue.
            double toCue = Vec2.Distance(mouseWorld, state.Cue.Pos);
            pressedOnCue = state.BallInHand && toCue < R * 1.6;
            dragging = true;
            cueStroke = CueStroke.Aiming;
            dragOrigin = mouseWorld;
            strokeAim = aimAngle;
            dragPower = 0;
        }
    }

    public void MouseUp(MouseEventArgs e)
    {
        spinDrag = false;
        if (!dragging) return;
        bool strike = cueStroke == CueStroke.Pulling && dragPower >= PullCommit / MaxPull;
        double angle = aimAngle, power = dragPower;
        CancelStroke();
        if (strike && HumanTurn && phase == Phase.Aiming)
            Shoot(angle, power, spinSide, spinTop);
        else if (pressedOnCue && HumanTurn && phase == Phase.Aiming)
            phase = Phase.Placing;
        pressedOnCue = false;
    }

    public void MouseWheel(MouseEventArgs e)
    {
        if (held >= 0 || !HumanTurn || phase != Phase.Aiming || cueStroke == CueStroke.Pulling) return;
        bool coarse = (AimModifiers & Keys.Shift) != 0;
        bool ultra = (AimModifiers & Keys.Alt) != 0;
        lastAimMoveTime = clock;
        NudgeAim(Math.Sign(e.Delta) * AimStep(coarse, ultra));
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
            if (held >= 0) { CancelHeld(); return true; }
            if (phase == Phase.RackOver) return true;
            paused = !paused;
            if (paused) BuildPauseButtons();
            return true;
        }
        if (k == Keys.Back || (k == Keys.Z && (keyData & Keys.Control) != 0))
        {
            UndoLastShot();
            return true;
        }
        if (phase == Phase.RackOver && k is Keys.Enter or Keys.Space)
        {
            ContinueAfterRack();
            return true;
        }
        if (paused) return k is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Space;

        bool aiming = HumanTurn && phase == Phase.Aiming && cueStroke != CueStroke.Pulling;
        bool aimFree = aiming && !dragging;
        switch (k)
        {
            case Keys.Left:
            case Keys.Right:
                if (aiming)
                {
                    bool ultra = (keyData & Keys.Alt) != 0;
                    lastAimMoveTime = clock;
                    NudgeAim((k == Keys.Right ? 1 : -1) * AimStep(shift, ultra));
                }
                return true;
            case Keys.Up:
            case Keys.Down:
                if (aimFree) kbPower = Math.Clamp(kbPower + (k == Keys.Up ? 1 : -1) * (shift ? 0.1 : 0.02), 0.02, 1);
                return true;
            case Keys.Space:
                if (aimFree) Shoot(aimAngle, kbPower, spinSide, spinTop);
                return true;
            case Keys.W: if (HumanTurn) NudgeSpin(0, 0.1); return true;
            case Keys.S: if (HumanTurn) NudgeSpin(0, -0.1); return true;
            case Keys.A: if (HumanTurn) NudgeSpin(-0.1, 0); return true;
            case Keys.D: if (HumanTurn) NudgeSpin(0.1, 0); return true;
            case Keys.R: if (HumanTurn) spinSide = spinTop = 0; return true;
            case Keys.H: RequestHint(); return true;
            case Keys.G:
                cfg.Guide = (AimGuide)(((int)cfg.Guide + 1) % 3);
                Say($"Aim guide: {cfg.Guide switch { AimGuide.Off => "off", AimGuide.GhostBall => "ghost ball", _ => "full path" }}.", Theme.Cyan, 1.5);
                return true;
            case Keys.F:
                fastForward = !fastForward;
                Say(fastForward ? "Play accelerated." : "Standard speed.", Theme.Cyan, 1.2);
                return true;
            case Keys.Tab:
                showStats = !showStats;
                return true;
            case Keys.V:
                ShotTrace.Show = !ShotTrace.Show;
                if (!ShotTrace.Show) ShotTrace.Clear();
                Say(ShotTrace.Show ? "Physics trace on." : "Physics trace off.", Theme.Cyan, 1.2);
                return true;
            case Keys.M:
                cfg.Sound = !cfg.Sound;
                host.Sound.Enabled = cfg.Sound;
                Say(cfg.Sound ? "Sound enabled." : "Sound muted.", Theme.Cyan, 1.2);
                return true;
        }
        return false;
    }

    void BuildPauseButtons()
    {
        buttons.Clear();
        buttons.Add(new UiButton { Text = "RESUME", Primary = true, OnClick = () => paused = false });
        if (history.Count > 0)
            buttons.Add(new UiButton { Text = "RESTORE SHOT", OnClick = () => UndoLastShot() });
        buttons.Add(new UiButton { Text = "RESTART MATCH", OnClick = Restart });
        buttons.Add(new UiButton { Text = "MAIN MENU", Accent = Theme.Magenta, OnClick = host.ShowMenu });
    }

    readonly List<string> eventLog = new();
    bool showStats;

    /// <summary>Tab overlay: full match statistics, live odds and the story of the match so far.</summary>
    void DrawScoreboard(Graphics g)
    {
        Ui.Scrim(g, size, 160);
        float cx = size.Width / 2f, pw = 720 * u, ph = 470 * u;
        var r = new RectangleF(cx - pw / 2, size.Height / 2f - ph / 2, pw, ph);
        Ui.Shadow(g, r, 6 * u, 18 * u);
        Ui.Card(g, r, 6 * u, 248);
        using (var top = new SolidBrush(Ui.Gold)) g.FillRectangle(top, r.X, r.Y, r.Width, 3 * u);
        Ui.Heading(g, "Match record", cx, r.Y + 40 * u, u, 22);
        Ui.Tracked(g, $"{rules.Name.ToUpperInvariant()}   ·   {(rules.Mode == GameMode.Snooker ? "FRAME" : "RACK")} {state.RackNumber}",
            Ui.Head(10.5f * u), Ui.Text3, cx, r.Y + 72 * u, 2 * u, StringAlignment.Center);

        bool snooker = rules.Mode == GameMode.Snooker, points = rules.IsPointsGame;
        var rows = new List<(string label, Func<int, string> value)>
        {
            ("CUE", i => state.Players[i].Cue.Name),
            (snooker ? "FRAMES" : "RACKS WON", i => state.Players[i].RacksWon.ToString()),
        };
        if (points) rows.Add(("SCORE", i => state.Players[i].Score.ToString()));
        if (snooker)
        {
            rows.Add(("CURRENT BREAK", i => state.Current == i ? state.CurrentBreak.ToString() : "–"));
            rows.Add(("HIGHEST BREAK", i => state.Players[i].HighBreak.ToString()));
        }
        else
        {
            rows.Add(("CURRENT RUN", i => state.Current == i ? state.Run.ToString() : "–"));
            rows.Add(("BEST RUN", i => state.Players[i].BestRun.ToString()));
        }
        rows.Add(("SHOTS", i => state.Players[i].Shots.ToString()));
        rows.Add(("POT RATE", i => state.Players[i].Shots == 0 ? "–" : $"{100.0 * state.Players[i].Pots / state.Players[i].Shots:0}%"));
        rows.Add(("FOULS", i => state.Players[i].Fouls.ToString()));

        int n = state.PlayerCount;
        float labelX = r.X + 40 * u, colW = 170 * u, col0 = r.X + 290 * u, y = r.Y + 100 * u, rowH = 24 * u;
        for (int i = 0; i < n; i++)
        {
            float c = col0 + i * colW + colW / 2;
            Ui.Tracked(g, state.Players[i].Name.ToUpperInvariant(), Ui.Head(14 * u), Ui.Player(i), c, y + 10 * u, 1.6f * u, StringAlignment.Center);
        }
        y += 28 * u;
        Ui.Hairline(g, labelX, y, r.Right - 40 * u, y);
        y += 4 * u;
        foreach (var (label, value) in rows)
        {
            Ui.Tracked(g, label, Ui.Head(10.5f * u), Ui.Text3, labelX, y + rowH / 2, 1.6f * u);
            for (int i = 0; i < n; i++)
                Ui.Write(g, value(i), Ui.Num(15 * u), Ui.Text, new RectangleF(col0 + i * colW, y, colW, rowH), StringAlignment.Center);
            y += rowH;
        }

        if (n == 2)
        {
            y += 10 * u;
            void OddsRow(string label, double p0)
            {
                Ui.Tracked(g, label, Ui.Head(10.5f * u), Ui.Text2, labelX, y + rowH / 2, 1.6f * u);
                float bx = col0 + 10 * u, bw = colW * 2 - 20 * u, by = y + rowH / 2 - 2 * u;
                using (var b1 = new SolidBrush(Color.FromArgb(200, Ui.Blue))) g.FillRectangle(b1, bx, by, bw, 4 * u);
                using (var b0 = new SolidBrush(Ui.Gold)) g.FillRectangle(b0, bx, by, (float)(bw * p0), 4 * u);
                Ui.Write(g, $"{p0 * 100:0}%", Ui.Num(13 * u), Ui.Gold, new RectangleF(bx - 52 * u, y, 46 * u, rowH), StringAlignment.Far);
                Ui.Write(g, $"{(1 - p0) * 100:0}%", Ui.Num(13 * u), Ui.Blue, new RectangleF(bx + bw + 6 * u, y, 50 * u, rowH));
                y += rowH + 2 * u;
            }
            OddsRow(snooker ? "WIN THIS FRAME" : "WIN THIS RACK", WinOdds.Rack(state, rules, 0));
            if (rules.UsesRaceTo) OddsRow("WIN THE MATCH", WinOdds.Match(state, rules, 0));
            if (snooker)
            {
                string note = $"{SnookerRules.PointsRemaining(state)} points remaining";
                for (int i = 0; i < 2; i++) if (SnookerRules.NeedsSnookers(state, i)) note += $"   ·   {state.Players[i].Name} requires snookers";
                Ui.Write(g, note, Ui.Body(12.5f * u), Ui.Warn, new RectangleF(labelX, y, r.Width - 80 * u, rowH));
                y += rowH;
            }
        }

        y += 6 * u;
        Ui.Hairline(g, labelX, y, r.Right - 40 * u, y);
        y += 14 * u;
        Ui.Tracked(g, "EVENT LOG", Ui.Head(10 * u), Ui.Gold, labelX, y, 2 * u);
        y += 14 * u;
        var recent = eventLog.Skip(Math.Max(0, eventLog.Count - 3)).Reverse().ToList();
        if (recent.Count == 0) Ui.Write(g, "No events recorded.", Ui.Body(12.5f * u), Ui.Text3, new RectangleF(labelX, y, r.Width - 80 * u, 18 * u));
        foreach (var e in recent)
        {
            Ui.Write(g, e, Ui.Body(12.5f * u), Ui.Text2, new RectangleF(labelX, y, r.Width - 80 * u, 18 * u));
            y += 18 * u;
        }
        Ui.Tracked(g, "PRESS TAB TO CLOSE", Ui.Head(9.5f * u), Ui.Text3, cx, r.Bottom - 18 * u, 1.8f * u, StringAlignment.Center);
    }

    /// <summary>Keeps the notable moments (fouls, big runs, rack results) for the scoreboard.</summary>
    void LogShot(string who, ShotOutcome o, int runIncludingShot, int breakIncludingShot)
    {
        foreach (var m in o.Messages)
            if (m.StartsWith("FOUL") || m.Contains("wins") || m.Contains("sinks") || m.Contains("concedes")
                || m.Contains("reaches") || m.Contains("early") || m.Contains("is SOLIDS") || m.Contains("is STRIPES"))
                AddEvent(m.StartsWith("FOUL") ? $"{who}: {m}" : m);
        bool visitOver = o.Foul || !o.TurnContinues || o.RackOver;
        if (visitOver && rules.Mode == GameMode.Snooker && breakIncludingShot >= 20) AddEvent($"{who}: break of {breakIncludingShot}.");
        else if (visitOver && rules.Mode != GameMode.Snooker && !o.Foul && runIncludingShot >= 3) AddEvent($"{who}: run of {runIncludingShot}.");
        if (o.RackOver && state.RackWinner >= 0)
            AddEvent($"{(rules.Mode == GameMode.Snooker ? "Frame" : "Rack")} {state.RackNumber} awarded to {state.Players[state.RackWinner].Name}.");
    }

    void AddEvent(string text)
    {
        eventLog.Add(text);
        if (eventLog.Count > 40) eventLog.RemoveAt(0);
    }

    void BuildRackOverButtons()
    {
        buttons.Clear();
        buttons.Add(new UiButton { Text = state.MatchOver ? "REMATCH" : "NEXT RACK", Primary = true, OnClick = ContinueAfterRack });
        if (history.Count > 0)
            buttons.Add(new UiButton { Text = "RESTORE SHOT", OnClick = () => UndoLastShot() });
        buttons.Add(new UiButton { Text = "MAIN MENU", Accent = Theme.Magenta, OnClick = host.ShowMenu });
    }

    /// <summary>
    /// One wheel or arrow tick, added to the current angle. It is a small double, not a slot on a
    /// fixed compass. Distance scaling keeps a tick the same length on the cloth for a short or a long shot.
    /// </summary>
    double AimStep(bool coarse, bool ultra)
    {
        double dist = Math.Max(70, Vec2.Distance(state.Cue.Pos, mouseWorld));
        double travel = ultra ? 0.055 : coarse ? 1.15 : 0.22;
        return travel / dist;
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
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        view.DrawBackground(g, s);
        long t1 = System.Diagnostics.Stopwatch.GetTimestamp();

        var saved = g.Save();
        view.ApplyWorld(g);
        DrawWorld(g);
        g.Restore(saved);
        long t2 = System.Diagnostics.Stopwatch.GetTimestamp();

        DrawHud(g);
        DrawBottomBar(g);
        DrawToasts(g);
        long t3 = System.Diagnostics.Stopwatch.GetTimestamp();
        double f = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        Timing.Background += (t1 - t0) * f;
        Timing.World += (t2 - t1) * f;
        Timing.Hud += (t3 - t2) * f;
        Timing.Frames++;
        if (showStats && phase != Phase.RackOver && !paused) DrawScoreboard(g);
        if (phase == Phase.RackOver) DrawRackOver(g);
        else if (paused) DrawPause(g);
    }

    void DrawWorld(Graphics g)
    {
        bool humanAim = HumanTurn && phase == Phase.Aiming;

        if (phase == Phase.Placing && state.KitchenOnly && HumanTurn)
        {
            using var kb = new SolidBrush(Look.Realistic ? Color.FromArgb(20, 255, 255, 255) : Color.FromArgb(22, 0, 240, 255));
            if (Table.HasD)
            {
                float dr = (float)Table.DRadius;
                g.FillPie(kb, (float)Table.DCenter.X - dr, (float)Table.DCenter.Y - dr, dr * 2, dr * 2, 90, 180);
            }
            else
            {
                g.FillRectangle(kb, 0, 0, (float)Table.HeadStringX, (float)Table.Height);
                Gfx.GlowLine(g, new PointF((float)Table.HeadStringX, 0), new PointF((float)Table.HeadStringX, (float)Table.Height), Theme.Cyan, 1.2f, 2, 3, 30);
            }
        }

        if (humanAim) DrawSuggestion(g);
        if (humanAim && cfg.Guide != AimGuide.Off) DrawAimGuide(g);

        // Soft shadows cast away from the lamp above the table centre, then the balls.
        float invScale = 1f / view.Scale;
        // Sprites are drawn at their native size: plain bilinear is enough and much cheaper.
        g.InterpolationMode = InterpolationMode.Bilinear;
        float pr = (float)(R * view.Scale);
        if (shadowSprite == null || Math.Abs(shadowRadius - pr) > 0.01f)
        {
            shadowSprite?.Dispose();
            shadowSprite = BallRenderer.MakeShadow(pr);
            shadowRadius = pr;
        }
        float sw = shadowSprite.Width * invScale;
        // Balls and shadows are blitted 1:1 in screen space (no resampling): each ball sprite is
        // rendered at its quarter-pixel phase, so motion stays sub-pixel smooth.
        var toDraw = new List<(int, Mat3, int)>();
        var screenPos = new Dictionary<int, PointF>();
        foreach (var b in state.Balls)
        {
            if (b.Pocketed) continue;
            var sp = view.ToScreen(DrawPos(b));
            screenPos[b.Number] = sp;
            toDraw.Add((b.Number, orient[b.Number], BallRenderer.Frac(sp)));
        }
        foreach (var sk in sinks) toDraw.Add((sk.Number, orient[sk.Number], 0));
        balls3d.Prepare(toDraw, pr);

        var world = g.Transform;
        g.ResetTransform();
        int half = shadowSprite.Width / 2;
        foreach (var b in state.Balls)
        {
            if (b.Pocketed) continue;
            Vec2 at = DrawPos(b);
            var sp = view.ToScreen(at + (at - Table.CenterSpot) * 0.012 + new Vec2(1.5, 3));
            g.DrawImageUnscaled(shadowSprite, (int)Math.Round(sp.X) - half, (int)Math.Round(sp.Y) - half);
        }
        foreach (var b in state.Balls)
        {
            if (b.Pocketed) continue;
            var sp = screenPos[b.Number];
            var bmp = balls3d.Get(b.Number, orient[b.Number], pr);
            int c = bmp.Width / 2; // matches the sprite's integer centre
            g.DrawImageUnscaled(bmp, (int)Math.Floor(sp.X) - c, (int)Math.Floor(sp.Y) - c);
        }
        g.Transform = world;
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
            Gfx.GlowPath(g, ring, ok ? Ui.Good : Ui.Bad, 1.2f, 1.6f, 1, 30);
        }

        if (held >= 0)
        {
            bool ok = Placeable(held, state.Balls[held].Pos);
            float rr = (float)(R + 4 + Math.Sin(clock * 6) * 1.5);
            var at = state.Balls[held].Pos;
            using var ring = new GraphicsPath();
            ring.AddEllipse((float)at.X - rr, (float)at.Y - rr, rr * 2, rr * 2);
            Gfx.GlowPath(g, ring, ok ? Ui.Good : Ui.Bad, 1.2f, 1.6f, 1, 30);
        }

        if (cueFollow >= 0)
        {
            double t = Math.Clamp(cueFollow / 0.22, 0, 1);
            DrawCue(g, followAim, followPower * (1 - t) - 0.15 * t);
        }
        else if (humanAim || phase == Phase.AIAiming)
            DrawCue(g, aimAngle, phase == Phase.AIAiming || cueStroke == CueStroke.Pulling ? dragPower : 0);

        DrawFlashes(g);
        if (ShotTrace.Show) DrawTrace(g);
        fx.Draw(g);
    }

    void DrawFlashes(Graphics g)
    {
        foreach (var f in flashes)
        {
            double a = 1 - f.Age / PocketFlash.Life;
            if (a <= 0) continue;
            float rad = f.Radius + (float)(f.Age * 46);
            Color ink = Look.Realistic ? Color.White : Theme.Cyan;
            using var pen = new Pen(Color.FromArgb((int)(200 * a), ink), Look.Realistic ? 1.6f : 2.2f);
            g.DrawEllipse(pen, (float)f.At.X - rad, (float)f.At.Y - rad, rad * 2, rad * 2);
        }
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

    /// <summary>The assist mark. Dim and dashed so it cannot be mistaken for the shot line.</summary>
    void DrawSuggestion(Graphics g)
    {
        double shown;
        lock (suggestGate) shown = suggestShown;
        if (double.IsNaN(shown)) return;
        double gap = Math.Abs(Math.IEEERemainder(shown - aimAngle, 2 * Math.PI));
        if (gap < 0.12 * Math.PI / 180) return;
        var stick = state.CurrentPlayer.Cue;
        Vec2 c = state.Cue.Pos;
        Vec2 d = Vec2.FromAngle(Physics.LaunchAngle(shown, spinSide, stick));
        var (hit, tHit) = FirstHit(c, d);
        double len = Math.Min(hit != null ? tHit : RayToRail(c, d), 340);
        if (len < 8) return;
        using var pen = new Pen(Color.FromArgb(150, 212, 168, 64), 1.15f) { DashStyle = DashStyle.Dash };
        g.DrawLine(pen, P(c), P(c + d * len));
    }

    void DrawAimGuide(Graphics g)
    {
        var tr = trajectory;
        bool full = cfg.Guide == AimGuide.Full;
        var stick = state.CurrentPlayer.Cue;
        double vision = stick.Vision;
        Vec2 c = state.Cue.Pos, d = Vec2.FromAngle(Physics.LaunchAngle(aimAngle, spinSide, stick));

        if (cfg.StrokeError)
        {
            var (h0, t0) = FirstHit(c, d);
            DrawAccuracyCone(g, c, d, h0 != null ? t0 : RayToRail(c, d), stick);
        }

        if (tr == null)
        {
            // Simulation still running for this aim: plain launch line until it lands.
            var (hit, tHit) = FirstHit(c, d);
            Vec2 end2 = c + d * (hit != null ? tHit : RayToRail(c, d));
            Gfx.GlowLine(g, P(c), P(end2), Theme.A(GuideColor, 150), 1f, 1.4f, 2, 22, DashStyle.Dash);
            return;
        }

        bool legal = tr.Target <= 0 || rules.IsLegalFirstContact(state, tr.Target);
        Color col = legal ? GuideColor : Theme.Red;

        // Cue ball to first contact (exactly where the physics puts it).
        DrawPath(g, tr.CuePath, 0, tr.ContactIndex >= 0 ? tr.ContactIndex : tr.CuePath.Count - 1,
            full ? double.MaxValue : 900 * vision, Theme.A(col, 220), 1.1f, DashStyle.Dash);

        if (tr.Contact is Vec2 ghost)
        {
            var gr = new RectangleF((float)(ghost.X - R), (float)(ghost.Y - R), (float)(2 * R), (float)(2 * R));
            using (var gb = new SolidBrush(Theme.A(col, 30))) g.FillEllipse(gb, gr);
            using (var gp = new Pen(Theme.A(col, 220), 1.3f)) g.DrawEllipse(gp, gr);
            // The point where the two balls actually meet, halfway between their centres at contact.
            Vec2 touch = tr.ObjPath.Count > 0 ? (ghost + tr.ObjPath[0]) * 0.5 : ghost + d * R;
            float trr = 2.2f;
            using (var tb = new SolidBrush(Theme.A(col, 230)))
                g.FillEllipse(tb, (float)touch.X - trr, (float)touch.Y - trr, trr * 2, trr * 2);
            if (!legal)
            {
                using var xp = new Pen(Theme.Red, 2f);
                float k = (float)(R * 0.55);
                g.DrawLine(xp, (float)ghost.X - k, (float)ghost.Y - k, (float)ghost.X + k, (float)ghost.Y + k);
                g.DrawLine(xp, (float)ghost.X - k, (float)ghost.Y + k, (float)ghost.X + k, (float)ghost.Y - k);
                return;
            }

            // Object ball's real path (throw included), and the cue ball's path after contact
            // (draw, follow and stun curves come straight from the spin).
            double objBudget = full ? (stick.Has(CuePerk.EagleEye) ? double.MaxValue : 1400 * vision) : 90 * vision;
            double cueBudget = full ? 700 * vision : 45 * vision;
            DrawPath(g, tr.ObjPath, 0, tr.ObjPath.Count - 1, objBudget, Color.FromArgb(230, 255, 255, 255), 1.4f, DashStyle.Solid);
            if (tr.ContactIndex >= 0)
                DrawPath(g, tr.CuePath, tr.ContactIndex, tr.CuePath.Count - 1, cueBudget, TangentColor, 1.1f, DashStyle.Solid);

            if (full)
            {
                if (!tr.TargetPotted && tr.ObjPath.Count > 1) DrawStop(g, tr.ObjPath[^1], Theme.A(Color.White, 170));
                if (!tr.CuePotted && tr.CuePath.Count > 1) DrawStop(g, tr.CuePath[^1], Theme.A(TangentColor, 200));
            }
        }

        // Pocket verdicts come from the simulation, so green means this exact shot drops it.
        if (tr.TargetPotted && tr.TargetPocket != null) Ring(g, tr.TargetPocket, Theme.Lime);
        if (tr.CuePotted && tr.CuePocket != null)
        {
            Ring(g, tr.CuePocket, Theme.Red);
            var at = tr.CuePocket.DrawCenter;
            using var f = new Font(Theme.Family, 13, FontStyle.Bold, GraphicsUnit.Pixel);
            using var br = new SolidBrush(Theme.Red);
            g.DrawString("SCRATCH", f, br, (float)at.X - 28, (float)at.Y + (at.Y < Table.Height / 2 ? 20 : -36));
        }
    }

    void Ring(Graphics g, Pocket pk, Color color)
    {
        using var ring = new GraphicsPath();
        float pr = (float)pk.DrawRadius + 3;
        ring.AddEllipse((float)pk.DrawCenter.X - pr, (float)pk.DrawCenter.Y - pr, pr * 2, pr * 2);
        if (Look.Realistic)
        {
            // Broadcast style: a crisp ring, no glow.
            using var pen = new Pen(Color.FromArgb(230, Ui.Accent(color)), 2f);
            g.DrawPath(pen, ring);
        }
        else Gfx.GlowPath(g, ring, color, 2f, 2.5f, 4, 50);
    }

    void DrawTrace(Graphics g)
    {
        using var vel = new Pen(Color.FromArgb(210, 90, 170, 230), 1.5f);
        using var hit = new Pen(Color.FromArgb(230, 232, 196, 108), 1.7f);
        using var rail = new Pen(Color.FromArgb(230, 120, 190, 255), 1.7f);
        foreach (var b in state.Balls)
        {
            if (b.Pocketed || b.Vel.LengthSq < 4) continue;
            var tip = b.Pos + b.Vel * 0.045;
            g.DrawLine(vel, (float)b.Pos.X, (float)b.Pos.Y, (float)tip.X, (float)tip.Y);
        }
        foreach (var m in ShotTrace.Marks)
        {
            var tip = m.At + m.Dir * (R * 3.2);
            g.DrawLine(m.Cushion ? rail : hit, (float)m.At.X, (float)m.At.Y, (float)tip.X, (float)tip.Y);
        }
    }

    void DrawStop(Graphics g, Vec2 at, Color color)
    {
        float rr = (float)R;
        using var pen = new Pen(color, 1.1f) { DashStyle = DashStyle.Dot };
        g.DrawEllipse(pen, (float)at.X - rr, (float)at.Y - rr, rr * 2, rr * 2);
    }

    /// <summary>Draws path[from..to] up to <paramref name="budget"/> units of length.</summary>
    static void DrawPath(Graphics g, List<Vec2> path, int from, int to, double budget, Color color, float width, DashStyle dash)
    {
        if (to <= from || from < 0) return;
        var pts = new List<PointF> { P(path[from]) };
        double used = 0;
        for (int i = from + 1; i <= to && i < path.Count; i++)
        {
            double seg = Vec2.Distance(path[i - 1], path[i]);
            if (used + seg > budget)
            {
                double f = (budget - used) / seg;
                pts.Add(P(Vec2.Lerp(path[i - 1], path[i], f)));
                break;
            }
            used += seg;
            pts.Add(P(path[i]));
        }
        if (pts.Count < 2) return;
        using var glow = new Pen(Theme.A(color, 40), width + 3) { LineJoin = LineJoin.Round, DashStyle = dash };
        using var pen = new Pen(color, width) { LineJoin = LineJoin.Round, DashStyle = dash };
        g.DrawLines(glow, pts.ToArray());
        g.DrawLines(pen, pts.ToArray());
    }

    /// <summary>Shaded wedge showing the cue's stroke wobble (2 sigma) at the current power.</summary>
    void DrawAccuracyCone(Graphics g, Vec2 c, Vec2 d, double length, CueSpec stick)
    {
        double power = dragging ? dragPower : kbPower;
        double half = 2 * stick.WobbleDeg(power) * Math.PI / 180;
        if (half < 1e-4) return;
        Vec2 a = c + d.Rotated(half) * length, b = c + d.Rotated(-half) * length;
        Color tint = Look.Realistic ? Color.White : Theme.Cyan;
        using var br = new SolidBrush(Theme.A(tint, 40));
        g.FillPolygon(br, [P(c), P(a), P(b)]);
        using var edge = new Pen(Theme.A(tint, 90), 0.8f);
        g.DrawLine(edge, P(c), P(a));
        g.DrawLine(edge, P(c), P(b));
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
        var stick = state.CurrentPlayer.Cue;
        CueDress? dress = state.CurrentPlayer.IsAI ? null : cfg.DressFor(stick.Id);
        if (dress is { IsBlank: true }) dress = null;
        CueArt.Draw(g, P(tip), P(butt), 2.4f, 6.4f, stick, clock, 1f, dress: dress);
    }

    // ------------------------------------------------------------------ HUD

    /// <summary>
    /// Broadcast-style score strip, top centre: [name | racks | score] [event] [score | racks | name],
    /// a thin accent rule under whoever is at the table, live win odds beneath.
    /// </summary>
    void DrawHud(Graphics g)
    {
        float w = size.Width, cx = w / 2, top = 14 * u, h = 50 * u;
        int n = state.PlayerCount;
        bool points = rules.IsPointsGame || rules.Mode == GameMode.Snooker;
        bool racks = rules.UsesRaceTo && n > 1;
        float nameW = 230 * u, boxW = 52 * u, midW = 190 * u;
        int boxes = (racks ? 1 : 0) + (points ? 1 : 0);
        float sideW = nameW + boxes * boxW;
        float total = n > 1 ? sideW * 2 + midW : nameW + midW;
        float x0 = cx - total / 2;
        var bar = new RectangleF(x0, top, total, h);

        Ui.Shadow(g, bar, 4 * u, 10 * u);
        Ui.Card(g, bar, 4 * u, 240);

        // Centre: event and format.
        var mid = n > 1 ? new RectangleF(x0 + sideW, top, midW, h) : new RectangleF(x0 + nameW, top, midW, h);
        using (var mb = new SolidBrush(Color.FromArgb(120, 0, 0, 0))) g.FillRectangle(mb, mid);
        Ui.Hairline(g, mid.X, top + 6 * u, mid.X, top + h - 6 * u);
        Ui.Hairline(g, mid.Right, top + 6 * u, mid.Right, top + h - 6 * u);
        Ui.Tracked(g, rules.Name.ToUpperInvariant(), Ui.Head(17 * u), Ui.Text, mid.X + mid.Width / 2, top + 19 * u, 2.4f * u, StringAlignment.Center);
        string sub = rules.IsSolo ? $"RACK {state.RackNumber}"
            : rules.Mode == GameMode.Snooker ? (state.RaceTo == 1 ? "ONE FRAME" : $"BEST OF {2 * state.RaceTo - 1}")
            : rules.UsesRaceTo ? (state.RaceTo == 1 ? "ONE RACK" : $"RACE TO {state.RaceTo}")
            : $"TO {state.TargetScore}";
        Ui.Tracked(g, sub, Ui.Head(10.5f * u), Ui.Gold, mid.X + mid.Width / 2, top + 36 * u, 2f * u, StringAlignment.Center);

        for (int i = 0; i < n; i++)
        {
            bool left = i == 0;
            float sx = left ? x0 : mid.Right;
            DrawPlayerSide(g, i, new RectangleF(sx, top, n > 1 ? sideW : nameW, h), left, nameW, boxW, racks, points);
        }

        // Under the strip: ball on / hint, then live odds.
        float y2 = top + h + 14 * u;
        int target = rules.TargetBall(state);
        string hint = state.RackOver ? "" : rules.Hint(state);
        if (target > 0)
        {
            Ui.Tracked(g, "BALL ON", Ui.Head(10 * u), Ui.Text3, cx - 8 * u, y2, 1.8f * u, StringAlignment.Far);
            DrawMiniBall(g, target, new PointF(cx + 6 * u, y2), 7.5f * u);
        }
        else if (!string.IsNullOrEmpty(hint))
        {
            Ui.Tracked(g, hint.ToUpperInvariant(), Ui.Head(10.5f * u), Ui.Text2, cx, y2, 1.6f * u, StringAlignment.Center);
        }
        if (n > 1) DrawWinBar(g, bar.X, bar.Right, top + h + 1 * u);
    }

    void DrawPlayerSide(Graphics g, int i, RectangleF r, bool left, float nameW, float boxW, bool racks, bool points)
    {
        var p = state.Players[i];
        bool active = state.Current == i && !state.RackOver;
        Color accent = Ui.Player(i);
        float h = r.Height;

        // Name block.
        var name = left ? new RectangleF(r.X, r.Y, nameW, h) : new RectangleF(r.Right - nameW, r.Y, nameW, h);
        float pad = 16 * u;
        var align = left ? StringAlignment.Near : StringAlignment.Far;
        float tx = left ? name.X + pad : name.Right - pad;
        if (active)
        {
            using var ab = new SolidBrush(Color.FromArgb(28, accent));
            g.FillRectangle(ab, name);
            using var rule = new SolidBrush(accent);
            g.FillRectangle(rule, name.X, name.Bottom - 3 * u, name.Width, 3 * u);
        }
        Ui.Tracked(g, p.Name.ToUpperInvariant(), Ui.Head(16 * u), active ? Color.White : Ui.Text, tx, r.Y + 18 * u, 1.4f * u, align);
        string status = active && phase == Phase.AIThinking ? "Selecting" + new string('.', 1 + (int)(clock * 3) % 3) : rules.Status(state, i);
        string tag = p.IsAI ? AppSettings.SkillName(p.Level) : "";
        string line = string.IsNullOrEmpty(tag) ? status : string.IsNullOrEmpty(status) ? tag : $"{tag}  ·  {status}";
        Ui.Write(g, line, Ui.Body(11.5f * u), active ? Ui.Text2 : Ui.Text3, new RectangleF(name.X + pad, r.Y + 28 * u, nameW - pad * 2, 16 * u), align);

        // Racks / frames and score boxes, nearest the centre.
        float bx = left ? name.Right : name.X;
        void Box(string value, bool emphasis)
        {
            var b = left ? new RectangleF(bx, r.Y, boxW, h) : new RectangleF(bx - boxW, r.Y, boxW, h);
            if (emphasis)
            {
                using var eb = new SolidBrush(Color.FromArgb(active ? 48 : 22, accent));
                g.FillRectangle(eb, b);
            }
            Ui.Hairline(g, left ? b.X : b.Right, r.Y + 8 * u, left ? b.X : b.Right, r.Bottom - 8 * u);
            Ui.Write(g, value, Ui.Num((emphasis ? 26 : 20) * u), emphasis ? Color.White : Ui.Text2, b, StringAlignment.Center);
            bx += left ? boxW : -boxW;
        }
        if (racks) Box(p.RacksWon.ToString(), !points);
        if (points) Box(p.Score.ToString(), true);

        // 8-ball group under the name.
        var balls = rules.PlayerBalls(state, i);
        float br = 6.5f * u, step = br * 2.4f;
        float y = r.Bottom + 15 * u;
        float startX = left ? name.X + pad + br : name.Right - pad - br;
        for (int k = 0; k < balls.Count; k++)
            DrawMiniBall(g, balls[k], new PointF(startX + (left ? k : -k) * step, y), br);
    }

    /// <summary>Hairline-thin live win-chance bar spanning the score strip.</summary>
    void DrawWinBar(Graphics g, float x1, float x2, float y)
    {
        if (state.PlayerCount < 2) return;
        double p = WinOdds.Match(state, rules, 0);
        float w = x2 - x1, hh = 3 * u;
        using (var b1 = new SolidBrush(Color.FromArgb(200, Ui.Blue))) g.FillRectangle(b1, x1, y, w, hh);
        using (var b0 = new SolidBrush(Ui.Gold)) g.FillRectangle(b0, x1, y, (float)(w * p), hh);
        var f = Ui.Num(11 * u);
        Ui.Write(g, $"{p * 100:0}%", f, Ui.Gold, new RectangleF(x1 - 46 * u, y - 7 * u, 40 * u, 16 * u), StringAlignment.Far);
        Ui.Write(g, $"{(1 - p) * 100:0}%", f, Ui.Blue, new RectangleF(x2 + 6 * u, y - 7 * u, 40 * u, 16 * u));
    }

    void DrawMiniBall(Graphics g, int number, PointF center, float r)
    {
        hudBalls.Draw(g, number, Mat3.Identity, r, center, 1f);
    }

    void DrawBottomBar(Graphics g)
    {
        float h = size.Height, w = size.Width;
        (string lead, string detail) = phase switch
        {
            (Phase.Placing or Phase.Aiming) when held >= 0 => ("Set the ball", "Click to place. Right-click restores it."),
            Phase.Placing when HumanTurn && state.KitchenOnly && Table.HasD => ("Ball in hand", "Place the cue ball inside the D."),
            Phase.Placing when HumanTurn && state.KitchenOnly => ("Ball in hand", "Place the cue ball behind the head string."),
            Phase.Placing when HumanTurn => ("Ball in hand", "Place the cue ball anywhere on the cloth."),
            Phase.Aiming when HumanTurn && cueStroke == CueStroke.Pulling => ("Your shot", "Release to strike. Ease the cue forward to cancel."),
            Phase.Aiming when HumanTurn && state.BallInHand => ("Your shot", "The cue points at the pointer. Draw it back, then release."),
            Phase.Aiming when HumanTurn => ("Your shot", "The cue points at the pointer. Draw it back, then release."),
            Phase.AIThinking or Phase.AIAiming or Phase.AIPlacing => (state.CurrentPlayer.Name, "At the table. F advances play."),
            Phase.Rolling when fastForward => ("Accelerated", "The balls are running ahead."),
            _ => ("", ""),
        };
        if (lead.Length > 0)
        {
            var leadFont = Ui.BodyStrong(14 * u);
            float leadW = g.MeasureString(lead, leadFont).Width;
            Ui.Write(g, lead, leadFont, Ui.Text, new RectangleF(28 * u, h - 64 * u, leadW + 8, 22 * u));
            Ui.Write(g, detail, Ui.Body(13 * u), Ui.Text2, new RectangleF(40 * u + leadW, h - 64 * u, Math.Max(40, w - 560 * u - leadW), 22 * u));
        }

        // Power: a thin track with tenth ticks; gold fill; green marks where the shot pots.
        spinRadius = 24 * u;
        spinCenter = new PointF(w - 34 * u - spinRadius, h - 44 * u);
        powerRect = new RectangleF(spinCenter.X - spinRadius - 330 * u, h - 40 * u, 290 * u, 6 * u);
        Ui.Legend(g, 28 * u, h - 22 * u, powerRect.X - 18 * u, u,
            (Glyph.Score, "Tab", "Record"),
            (Glyph.Pause, "Esc", "Pause"),
            (Glyph.Restore, "Bksp", "Restore"),
            (Glyph.Aim, "G", "Guide"),
            (Glyph.Sound, "M", "Sound"));
        double power = dragging || phase == Phase.AIAiming ? dragPower : kbPower;
        using (var track = new SolidBrush(Color.FromArgb(46, 255, 255, 255))) g.FillRectangle(track, powerRect);
        if (power > 0.003)
        {
            using var fill = new LinearGradientBrush(powerRect, Ui.Gold, Ui.GoldBright, 0f);
            g.FillRectangle(fill, powerRect.X, powerRect.Y, (float)(powerRect.Width * power), powerRect.Height);
        }
        for (int k = 1; k < 10; k++)
            Ui.Hairline(g, powerRect.X + powerRect.Width * k / 10f, powerRect.Y - 2 * u, powerRect.X + powerRect.Width * k / 10f, powerRect.Bottom, Color.FromArgb(70, 0, 0, 0));
        Ui.Tracked(g, "POWER", Ui.Head(10 * u), Ui.Text3, powerRect.X, powerRect.Y - 14 * u, 2 * u);
        Ui.Write(g, $"{power * 100:0}%", Ui.Num(15 * u), Ui.Text, new RectangleF(powerRect.X + 60 * u, powerRect.Y - 24 * u, 60 * u, 20 * u));

        var pv = HumanTurn && phase == Phase.Aiming ? preview : null;
        if (pv != null && pv.Target > 0)
        {
            float step = powerRect.Width / PreviewSteps;
            using (var tick = new SolidBrush(Ui.Good))
                for (int i = 1; i <= PreviewSteps; i++)
                    if (pv.Pots[i]) g.FillRectangle(tick, powerRect.X + (i - 1) * step + 1, powerRect.Bottom + 4 * u, step - 2, 3 * u);
            string msg;
            Color mc;
            if (pv.Pots[PowerStep(power)]) { msg = "POTS"; mc = Ui.Good; }
            else if (pv.Any)
            {
                int lo = Array.FindIndex(pv.Pots, x => x), hi = Array.FindLastIndex(pv.Pots, x => x);
                msg = $"POTS AT {lo * 100 / PreviewSteps}–{hi * 100 / PreviewSteps}%";
                mc = Ui.Warn;
            }
            else { msg = "NO POT ON THIS LINE"; mc = Ui.Text3; }
            Ui.Tracked(g, msg, Ui.Head(10 * u), mc, powerRect.Right, powerRect.Y - 14 * u, 1.8f * u, StringAlignment.Far);
        }

        // Spin: a clean cue ball with a hairline cross and the tip contact point.
        var sr = new RectangleF(spinCenter.X - spinRadius, spinCenter.Y - spinRadius, spinRadius * 2, spinRadius * 2);
        using (var sp = new GraphicsPath())
        {
            sp.AddEllipse(sr);
            using var wb = new PathGradientBrush(sp)
            {
                CenterPoint = new PointF(spinCenter.X - spinRadius * 0.35f, spinCenter.Y - spinRadius * 0.4f),
                CenterColor = Color.FromArgb(255, 250, 249, 245),
                SurroundColors = [Color.FromArgb(255, 150, 152, 158)],
            };
            g.FillPath(wb, sp);
            using var ring = new Pen(Ui.LineStrong, 1f);
            g.DrawPath(ring, sp);
        }
        using (var cross = new Pen(Color.FromArgb(60, 0, 0, 0), 1f))
        {
            g.DrawLine(cross, sr.Left + 3 * u, spinCenter.Y, sr.Right - 3 * u, spinCenter.Y);
            g.DrawLine(cross, spinCenter.X, sr.Top + 3 * u, spinCenter.X, sr.Bottom - 3 * u);
        }
        var dot = new PointF(spinCenter.X + (float)(spinSide * spinRadius), spinCenter.Y - (float)(spinTop * spinRadius));
        using (var db = new SolidBrush(Ui.Bad)) g.FillEllipse(db, dot.X - 4 * u, dot.Y - 4 * u, 8 * u, 8 * u);
        Ui.Tracked(g, "SPIN", Ui.Head(10 * u), Ui.Text3, sr.X - 10 * u, spinCenter.Y, 2 * u, StringAlignment.Far);
    }

    /// <summary>Lower-third style messages: dark plate, coloured edge, clean type.</summary>
    void DrawToasts(Graphics g)
    {
        var anchor = view.ToScreen(new Vec2(Table.Width / 2, 40));
        float y = anchor.Y;
        var font = Ui.BodyStrong(16 * u);
        foreach (var t in toasts)
        {
            double fadeIn = Math.Min(1, t.Age / 0.18), fadeOut = Math.Min(1, (t.Life - t.Age) / 0.4);
            double a = Math.Clamp(Math.Min(fadeIn, fadeOut), 0, 1);
            float slide = (float)((1 - fadeIn) * 8 * u);
            var sz = g.MeasureString(t.Text, font);
            var box = new RectangleF(anchor.X - sz.Width / 2 - 22 * u, y + slide, sz.Width + 44 * u, 34 * u);
            Color accent = Ui.Accent(t.Color);
            using (var br = new SolidBrush(Color.FromArgb((int)(225 * a), Ui.Panel))) g.FillRectangle(br, box);
            using (var edge = new SolidBrush(Color.FromArgb((int)(255 * a), accent))) g.FillRectangle(edge, box.X, box.Y, 3 * u, box.Height);
            using (var line = new Pen(Color.FromArgb((int)(40 * a), 255, 255, 255), 1f)) g.DrawRectangle(line, box.X, box.Y, box.Width, box.Height);
            Ui.Write(g, t.Text, font, Color.FromArgb((int)(255 * a), Ui.Text), new RectangleF(box.X + 6 * u, box.Y, box.Width - 6 * u, box.Height), StringAlignment.Center);
            y += 40 * u;
        }
    }

    void DrawOverlayPanel(Graphics g, string title, string subtitle, Color accent)
    {
        Ui.Scrim(g, size, 175);
        float cx = size.Width / 2f, cy = size.Height / 2f;
        float bh = buttons.Sum(b => b.Height(u)) + Math.Max(0, buttons.Count - 1) * UiButton.Gap * u;
        float ph = 140 * u + bh + 18 * u;
        var r = new RectangleF(cx - 280 * u, cy - ph / 2, 560 * u, ph);
        Ui.Shadow(g, r, 6 * u, 18 * u);
        Ui.Card(g, r, 6 * u, 248);
        using (var top = new SolidBrush(accent)) g.FillRectangle(top, r.X, r.Y, r.Width, 3 * u);
        Ui.Heading(g, title, cx, r.Y + 50 * u, u, 26);
        Ui.Write(g, subtitle, Ui.Body(14 * u), Ui.Text2, new RectangleF(r.X + 20 * u, r.Y + 84 * u, r.Width - 40 * u, 24 * u), StringAlignment.Center);

        float by = r.Y + 134 * u;
        foreach (var b in buttons)
        {
            float h = b.Height(u);
            b.Rect = new RectangleF(cx - 150 * u, by, 300 * u, h);
            b.Draw(g, u);
            by += h + UiButton.Gap * u;
        }
    }

    void DrawPause(Graphics g) =>
        DrawOverlayPanel(g, "Paused", $"{rules.Name}  ·  {string.Join("  vs  ", state.Players.Select(p => p.Name))}", Ui.Gold);

    void DrawRackOver(Graphics g)
    {
        if (overlayTimer < 0.9) return; // let the last ball drop first
        var winner = state.RackWinner >= 0 ? state.Players[state.RackWinner] : null;
        string unit = rules.Mode == GameMode.Snooker ? "frame" : "rack";
        string title = winner == null ? $"{unit} over"
            : state.MatchOver ? $"{winner.Name} wins the match"
            : $"{winner.Name} takes the {unit}";
        string subtitle = rules.UsesRaceTo
            ? string.Join("     ", state.Players.Select(p => $"{p.Name}  {p.RacksWon}"))
            : string.Join("     ", state.Players.Select(p => $"{p.Name}  {p.Score}"));
        if (state.Players.All(p => p.IsAI) && !state.MatchOver) subtitle += $"     ·     next {unit} starting";
        Color accent = state.RackWinner >= 0 ? Ui.Player(state.RackWinner) : Ui.Gold;
        DrawOverlayPanel(g, title, subtitle, accent);
    }

    // ================================================================== screenshots

    // ------------------------------------------------------------------ play-test hooks

    internal GameState State => state;
    internal double AimAngle => aimAngle;
    internal bool ReadyForHuman => HumanTurn && !paused && phase is Phase.Aiming or Phase.Placing;
    internal bool ShotInProgress => phase == Phase.Rolling;
    internal bool IsPlacing => phase == Phase.Placing;
    internal ShotEvents LastEvents => events;
    internal PointF ToScreen(Vec2 w) => view.ToScreen(w);
    internal Vec2 ToWorldPublic(PointF p) => view.ToWorld(p);
    internal void DebugBeginTurn() { SyncRolling(); BeginTurn(); }
    internal void DebugSetSpin(double side, double top) { spinSide = side; spinTop = top; }
    internal void DebugSetGuide(AimGuide guide) => cfg.Guide = guide;
    internal void DebugSetAssist(AimAssist assist) => cfg.Assist = assist;
    internal void DebugAimModifiers(Keys? keys) => aimModifiersOverride = keys;
    internal void DebugShowStats() => showStats = true;
    /// <summary>Where ball <paramref name="n"/> is drawn this frame.</summary>
    internal Vec2 DrawnPos(int n) => DrawPos(state.Balls[n]);

    /// <summary>Accumulated draw time per section (ms), read by the benchmark.</summary>
    internal static class Timing
    {
        public static double Background, World, Hud;
        public static int Frames;
        public static void Reset() { Background = World = Hud = 0; Frames = 0; }
    }
    internal void DebugBreak()
    {
        var apex = state.ObjectBallsOnTable.OrderBy(b => b.Pos.X).First();
        state.Cue.Pos = new Vec2(Table.HeadStringX - 25, Table.Height / 2 - 20);
        Shoot((apex.Pos - state.Cue.Pos).Angle, 1.0, 0, 0);
    }

    /// <summary>What the aim guide currently promises: first ball hit and its departure direction.</summary>
    internal (int ball, Vec2 objDir)? PredictedHit()
    {
        var stick = state.CurrentPlayer.Cue;
        Vec2 c = state.Cue.Pos, d = Vec2.FromAngle(Physics.LaunchAngle(aimAngle, spinSide, stick));
        var (hit, t) = FirstHit(c, d);
        if (hit == null || t > RayToRail(c, d)) return null;
        return (hit.Number, (hit.Pos - (c + d * t)).Normalized());
    }

    /// <summary>Builds a mid-game position with the aim guide showing, for README screenshots.</summary>
    public void SetupDemo()
    {
        var s = state;
        s.Players[0].Cue = CueCatalog.Get(Environment.GetEnvironmentVariable("NEONCUE_DEMO_CUE") ?? "dragon");
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
        Say($"Coach. {plan.Plan}", Theme.Lime, 60);
        fx.Pulse(Table.Pockets[2].DrawCenter, Look.Realistic ? Ui.Good : Theme.Lime, 30, 60);
    }
}

/// <summary>Recent aim decisions, kept for the freeze regression. Recording is off during play.</summary>
static class AimDiag
{
    internal readonly record struct Sample(double Angle, bool Applied, bool Dragging, bool Stroke, string Phase);
    public static bool Record;
    public const int Capacity = 256;
    public static readonly Sample[] Samples = new Sample[Capacity];
    public static int Count;
    public static void Clear() => Count = 0;
    public static void Add(Sample sample)
    {
        if (!Record) return;
        Samples[Count % Capacity] = sample;
        Count++;
    }
}
