namespace CamTurn.Simulator;

public readonly record struct Vec3(double X, double Y, double Z)
{
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Vec3 operator *(double k, Vec3 v) => new(k * v.X, k * v.Y, k * v.Z);

    public static Vec3 operator -(Vec3 v) => new(-v.X, -v.Y, -v.Z);

    public double Dot(Vec3 o) => (X * o.X) + (Y * o.Y) + (Z * o.Z);

    /// <summary>Rotation about the world X axis by <paramref name="radians"/> (right-hand rule).</summary>
    public Vec3 RotateX(double radians)
    {
        double c = Math.Cos(radians), s = Math.Sin(radians);
        return new(X, (Y * c) - (Z * s), (Y * s) + (Z * c));
    }
}
