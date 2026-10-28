using System.Drawing;
using NeonCue.Core;
using NeonCue.Screens;

namespace NeonCue;

/// <summary>Headless check for take-back and Practice ball placement. Run: NeonCue.exe --undo-check</summary>
static class UndoCheck
{
    sealed class Host : IHost
    {
        public AppSettings Settings { get; } = new() { Sound = false, Mode = GameMode.Practice, Guide = AimGuide.Off, Coins = 12345 };
        public SoundFx Sound { get; } = new() { Enabled = false };
        public void ShowMenu() { }
        public void StartGame() { }
        public void ShowShop() { }
        public void Quit() { }
    }

    public static bool Run()
    {
        var failures = new List<string>();
        CheckTakeBack(failures);
        CheckPracticePlacement(failures);
        CheckPlacementStaysInMatch(failures);
        if (failures.Count == 0)
        {
            Console.WriteLine("undo-check: PASS");
            return true;
        }
        foreach (var f in failures) Console.WriteLine("FAIL: " + f);
        return false;
    }

    static void CheckTakeBack(List<string> failures)
    {
        var host = new Host();
        var gs = new GameScreen(host);
        using var bmp = new Bitmap(1280, 800);
        using var g = Graphics.FromImage(bmp);
        var s = gs.State;
        foreach (var b in s.Balls) if (b.Number > 1) b.Pocketed = true;
        s.Balls[1].Pos = new Vec2(700, 250);
        s.Cue.Pos = new Vec2(300, 250);
        s.BallInHand = false;
        s.KitchenOnly = false;
        s.IsBreakShot = false;
        gs.DebugBeginTurn();
        gs.Draw(g, bmp.Size);

        var before = s.Balls.Select(b => b.Clone()).ToArray();
        int shot = s.ShotNumber;
        int shots = s.CurrentPlayer.Shots;

        gs.KeyDown(Keys.Space);
        for (int i = 0; i < 4000 && gs.ShotInProgress; i++) gs.Update(1 / 60.0);
        if (gs.ShotInProgress) failures.Add("shot did not finish");
        if (gs.State.ShotNumber == shot) failures.Add("shot did not advance the table");

        gs.KeyDown(Keys.Back);
        var now = gs.State;
        if (now.ShotNumber != shot) failures.Add($"shot number after take-back is {now.ShotNumber}, expected {shot}");
        if (now.CurrentPlayer.Shots != shots) failures.Add("shot count was not restored");
        if (host.Settings.Coins != 12345) failures.Add($"coins after take-back are {host.Settings.Coins}");
        for (int i = 0; i < before.Length; i++)
        {
            var b = now.Balls[i];
            if (b.Pocketed != before[i].Pocketed || Vec2.DistanceSq(b.Pos, before[i].Pos) > 1e-6)
                failures.Add($"ball {i} was not put back");
        }

        // A second take-back has nothing behind it and must leave the restored table alone.
        gs.KeyDown(Keys.Back);
        if (gs.State.ShotNumber != shot) failures.Add("empty take-back changed the table");
    }

    static void CheckPracticePlacement(List<string> failures)
    {
        var host = new Host();
        var gs = new GameScreen(host);
        using var bmp = new Bitmap(1280, 800);
        using var g = Graphics.FromImage(bmp);
        var s = gs.State;
        s.Balls[1].Pos = new Vec2(700, 250);
        s.Cue.Pos = new Vec2(300, 250);
        s.BallInHand = false;
        s.KitchenOnly = false;
        gs.DebugBeginTurn();
        gs.Draw(g, bmp.Size);

        Point P(Vec2 w)
        {
            var p = gs.ToScreen(w);
            return new Point((int)Math.Round(p.X), (int)Math.Round(p.Y));
        }
        static MouseEventArgs M(Point p, MouseButtons button = MouseButtons.Left) => new(button, 1, p.X, p.Y, 0);

        var ball = P(new Vec2(700, 250));
        gs.MouseMove(M(ball));
        gs.MouseDown(M(ball, MouseButtons.Right));
        var carry = P(new Vec2(500, 250));
        gs.MouseMove(M(carry));
        if (Vec2.Distance(gs.State.Balls[1].Pos, new Vec2(500, 250)) > 2)
            failures.Add("right-click did not pick the ball up");

        // Dropping on top of the cue ball is refused; the ball stays in hand.
        var onCue = P(gs.State.Cue.Pos);
        gs.MouseMove(M(onCue));
        gs.MouseDown(M(onCue));
        var free = P(new Vec2(600, 200));
        gs.MouseMove(M(free));
        if (Vec2.Distance(gs.State.Balls[1].Pos, new Vec2(600, 200)) > 2)
            failures.Add("an overlapping drop was accepted");

        gs.MouseDown(M(free));
        if (Vec2.Distance(gs.State.Balls[1].Pos, new Vec2(600, 200)) > 2)
            failures.Add("click did not set the ball down");

        // Pick it up again and cancel: it returns to where it was set down.
        gs.MouseMove(M(free));
        gs.MouseDown(M(free, MouseButtons.Right));
        gs.MouseMove(M(P(new Vec2(800, 300))));
        gs.MouseDown(M(P(new Vec2(800, 300)), MouseButtons.Right));
        if (Vec2.Distance(gs.State.Balls[1].Pos, new Vec2(600, 200)) > 2)
            failures.Add("cancel did not put the ball back");
    }

    static void CheckPlacementStaysInMatch(List<string> failures)
    {
        var host = new Host();
        host.Settings.Mode = GameMode.EightBall;
        var gs = new GameScreen(host);
        using var bmp = new Bitmap(1280, 800);
        using var g = Graphics.FromImage(bmp);
        gs.Draw(g, bmp.Size);
        var ball = gs.State.Balls[1];
        var home = ball.Pos;
        Point P(Vec2 w)
        {
            var p = gs.ToScreen(w);
            return new Point((int)Math.Round(p.X), (int)Math.Round(p.Y));
        }
        static MouseEventArgs M(Point p, MouseButtons button = MouseButtons.Left) => new(button, 1, p.X, p.Y, 0);
        var at = P(home);
        gs.MouseMove(M(at));
        gs.MouseDown(M(at, MouseButtons.Right));
        gs.MouseMove(M(P(new Vec2(400, 250))));
        if (Vec2.DistanceSq(gs.State.Balls[1].Pos, home) > 1e-6)
            failures.Add("a match let a ball be moved");
    }
}
