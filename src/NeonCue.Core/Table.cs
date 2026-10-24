namespace NeonCue.Core;

public readonly struct Segment
{
    public readonly Vec2 A;
    public readonly Vec2 B;

    public Segment(Vec2 a, Vec2 b) { A = a; B = b; }

    public Vec2 Closest(Vec2 p)
    {
        Vec2 ab = B - A;
        double t = Vec2.Dot(p - A, ab) / ab.LengthSq;
        if (t < 0) t = 0; else if (t > 1) t = 1;
        return A + ab * t;
    }
}

public sealed class Pocket
{
    public required int Index { get; init; }
    public required string Name { get; init; }
    /// <summary>A ball whose centre comes within <see cref="CaptureRadius"/> of this point drops.</summary>
    public required Vec2 Center { get; init; }
    public required double CaptureRadius { get; init; }
    public required Vec2 DrawCenter { get; init; }
    public required double DrawRadius { get; init; }
    /// <summary>Point an object ball should be sent towards to drop cleanly.</summary>
    public required Vec2 Aim { get; init; }
    /// <summary>Unit vector pointing from the pocket into the table.</summary>
    public required Vec2 Inward { get; init; }
    public required bool IsCorner { get; init; }
}

/// <summary>
/// Geometry of a 9-foot table in abstract units (1000 x 500 playing surface,
/// measured between cushion noses). Ball centres live in [R, W-R] x [R, H-R].
/// </summary>
public static class Table
{
    public const double Width = 1000;
    public const double Height = 500;
    public const double R = 11.25;
    public const double CornerGap = 3.0 * R;
    public const double SideGap = 2.15 * R;
    public const double CushionDepth = 30;
    public const double FrameOuter = 66;
    public const double HeadStringX = Width * 0.25;

    public static readonly Vec2 FootSpot = new(Width * 0.75, Height / 2);
    public static readonly Vec2 HeadSpot = new(Width * 0.25, Height / 2);
    public static readonly Vec2 CenterSpot = new(Width / 2, Height / 2);

    public static readonly Segment[] Cushions;
    public static readonly Pocket[] Pockets;
    /// <summary>Cushion rubber polygons for drawing: [noseA, noseB, backB, backA].</summary>
    public static readonly Vec2[][] CushionPolygons;

    static Table()
    {
        const double W = Width, H = Height, cg = CornerGap, sg = SideGap;

        static Vec2 Mx(Vec2 p) => new(W - p.X, p.Y);
        static Vec2 My(Vec2 p) => new(p.X, H - p.Y);
        static Vec2 Mxy(Vec2 p) => new(W - p.X, H - p.Y);
        Func<Vec2, Vec2>[] mirrors = [p => p, Mx, My, Mxy];

        var segs = new List<Segment>();
        var polys = new List<Vec2[]>();

        // Top-left long-rail piece and its jaws; mirrored to the other three long-rail pieces.
        Vec2 ta = new(cg, 0), tb = new(W / 2 - sg, 0);
        Vec2 jawCorner = new(-1.25 * R, -1.45 * R);
        Vec2 jawSide = new(0.35 * R, -1.30 * R);
        Vec2 backA = ta + jawCorner * (CushionDepth / (1.45 * R));
        Vec2 backB = tb + jawSide * (CushionDepth / (1.30 * R));
        foreach (var m in mirrors)
        {
            segs.Add(new Segment(m(ta), m(tb)));
            segs.Add(new Segment(m(ta), m(ta + jawCorner)));
            segs.Add(new Segment(m(tb), m(tb + jawSide)));
            polys.Add([m(ta), m(tb), m(backB), m(backA)]);
        }

        // Short rails (left, mirrored to right) and their corner jaws.
        Vec2 la = new(0, cg), lb = new(0, H - cg);
        Vec2 jawShortTop = new(-1.45 * R, -1.25 * R);
        Vec2 jawShortBottom = new(-1.45 * R, 1.25 * R);
        double s = CushionDepth / (1.45 * R);
        foreach (var m in new Func<Vec2, Vec2>[] { p => p, Mx })
        {
            segs.Add(new Segment(m(la), m(lb)));
            segs.Add(new Segment(m(la), m(la + jawShortTop)));
            segs.Add(new Segment(m(lb), m(lb + jawShortBottom)));
            polys.Add([m(la), m(lb), m(lb + jawShortBottom * s), m(la + jawShortTop * s)]);
        }

        Cushions = segs.ToArray();
        CushionPolygons = polys.ToArray();

        Pocket Corner(int index, string name, double sx, double sy)
        {
            // sx/sy are 0 or 1: which corner.
            Vec2 C(double x, double y) => new(sx == 0 ? x : W - x, sy == 0 ? y : H - y);
            return new Pocket
            {
                Index = index,
                Name = name,
                Center = C(-0.55 * R, -0.55 * R),
                CaptureRadius = 1.85 * R,
                DrawCenter = C(-9, -9),
                DrawRadius = 27,
                Aim = C(cg * 0.4, cg * 0.4),
                Inward = new Vec2(sx == 0 ? 1 : -1, sy == 0 ? 1 : -1).Normalized(),
                IsCorner = true,
            };
        }

        Pocket Side(int index, string name, bool top) => new()
        {
            Index = index,
            Name = name,
            Center = new Vec2(W / 2, top ? -1.25 * R : H + 1.25 * R),
            CaptureRadius = 1.3 * R,
            DrawCenter = new Vec2(W / 2, top ? -18 : H + 18),
            DrawRadius = 21,
            Aim = new Vec2(W / 2, top ? 0 : H),
            Inward = new Vec2(0, top ? 1 : -1),
            IsCorner = false,
        };

        Pockets =
        [
            Corner(0, "top-left corner", 0, 0),
            Side(1, "top side", true),
            Corner(2, "top-right corner", 1, 0),
            Corner(3, "bottom-left corner", 0, 1),
            Side(4, "bottom side", false),
            Corner(5, "bottom-right corner", 1, 1),
        ];
    }

    public static bool InsidePlayArea(Vec2 p) =>
        p.X >= R && p.X <= Width - R && p.Y >= R && p.Y <= Height - R;

    public static Pocket NearestPocket(Vec2 p)
    {
        Pocket best = Pockets[0];
        double bd = double.MaxValue;
        foreach (var pk in Pockets)
        {
            double d = Vec2.DistanceSq(p, pk.Center);
            if (d < bd) { bd = d; best = pk; }
        }
        return best;
    }
}
