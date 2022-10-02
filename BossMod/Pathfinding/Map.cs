﻿using System;
using System.Collections.Generic;
using System.Numerics;

namespace BossMod.Pathfinding
{
    // 'map' used for running pathfinding algorithms
    // this is essentially a square grid representing an arena (or immediate neighbourhood of the player) where we rasterize forbidden/desired zones
    // area covered by each pixel can be in one of the following states:
    // - default: safe to traverse but non-goal
    // - danger: unsafe to traverse after X seconds (X >= 0); instead of X, we store max 'g' value (distance travelled assuming constant speed) for which pixel is still considered unblocked
    // - goal: destination with X priority (X > 0); 'default' is considered a goal with priority 0
    // - goal and danger are mutually exclusive, 'danger' overriding 'goal' state
    // typically we try to find a path to goal with highest priority; if that fails, try lower priorities; if no paths can be found (e.g. we're currently inside an imminent aoe) we find direct path to closest safe pixel
    public class Map
    {
        [Flags]
        public enum Coverage
        {
            None = 0,
            Inside = 1,
            Border = 2,
            Outside = 4
        }

        public struct Pixel
        {
            public float MaxG; // MaxValue if not dangerous
            public int Priority; // >0 if goal
        }

        public float Resolution { get; private init; } // pixel size, in world units
        public int Width { get; private init; } // always even
        public int Height { get; private init; } // always even
        public Pixel[] Pixels { get; private init; }

        public WPos Center { get; private init; } // position of map center in world units
        public Angle Rotation { get; private init; } // rotation relative to world space (=> ToDirection() is equal to direction of local 'height' axis in world space)
        private WDir _localZDivRes { get; init; }

        public float MaxG { get; private set; } // maximal 'maxG' value of all blocked pixels
        public int MaxPriority { get; private set; } // maximal 'priority' value of all blocked pixels

        //public float Speed = 6; // used for converting activation time into max g-value: num world units that player can move per second

        public Pixel this[int x, int y] => x >= 0 && x < Width && y >= 0 && y < Height ? Pixels[y * Width + x] : new() { MaxG = float.MaxValue, Priority = 0 };

        public Map(float resolution, WPos center, float worldHalfWidth, float worldHalfHeight, Angle rotation = new())
        {
            Resolution = resolution;
            Width = 2 * (int)MathF.Ceiling(worldHalfWidth / resolution);
            Height = 2 * (int)MathF.Ceiling(worldHalfHeight / resolution);
            Pixels = new Pixel[Width * Height];
            Array.Fill(Pixels, new Pixel() { MaxG = float.MaxValue, Priority = 0 });

            Center = center;
            Rotation = rotation;
            _localZDivRes = rotation.ToDirection() / Resolution;
        }

        public Vector2 WorldToGridFrac(WPos world)
        {
            var offset = world - Center;
            var x = offset.Dot(_localZDivRes.OrthoL());
            var y = offset.Dot(_localZDivRes);
            return new(Width / 2 + x, Height / 2 + y);
        }

        public (int x, int y) FracToGrid(Vector2 frac) => ((int)MathF.Floor(frac.X), (int)MathF.Floor(frac.Y));
        public (int x, int y) WorldToGrid(WPos world) => FracToGrid(WorldToGridFrac(world));

        public WPos GridToWorld(int gx, int gy, float fx, float fy)
        {
            var rsq = Resolution * Resolution; // since we then multiply by _localZDivRes, end result is same as * res * rotation.ToDir()
            float ax = (gx - Width / 2 + fx) * rsq;
            float az = (gy - Height / 2 + fy) * rsq;
            return Center + ax * _localZDivRes.OrthoL() + az * _localZDivRes;
        }

        public void BlockPixels(IEnumerable<(int x, int y, Coverage cv)> pixels, float maxG, Coverage coverage)
        {
            MaxG = MathF.Max(MaxG, maxG);
            foreach (var (x, y, cv) in pixels)
            {
                if (coverage.HasFlag(cv))
                {
                    ref var pixel = ref Pixels[y * Width + x];
                    pixel.MaxG = MathF.Min(pixel.MaxG, maxG);
                }
            }
        }

        public IEnumerable<(int x, int y, Coverage cv)> RasterizeCircle(WPos origin, float radius) => RasterizeDonut(origin, 0, radius);
        public IEnumerable<(int x, int y, Coverage cv)> RasterizeDonut(WPos origin, float innerRadius, float outerRadius)
        {
            if (outerRadius <= 0 || innerRadius >= outerRadius)
                yield break;

            var delta = Resolution * 0.707107f;
            var r1 = outerRadius + delta; // d >= r1 => fully outside
            r1 *= r1;
            var r2 = Math.Max(0, outerRadius - delta);
            r2 *= r2;
            var r3 = innerRadius > 0 ? innerRadius + delta : 0; // r2 > d >= r3 => fully inside
            r3 *= r3;
            var r4 = Math.Max(0, innerRadius - delta);
            r4 *= r4;

            foreach (var p in EnumeratePixels())
            {
                var d = (p.center - origin).LengthSq();
                var c = (d >= r1 || d < r4) ? Coverage.Outside : (d >= r3 && d < r2) ? Coverage.Inside : Coverage.Border;
                yield return (p.x, p.y, c);
            }
        }

        public IEnumerable<(int x, int y, Coverage cv)> RasterizeDonutSector(WPos origin, float innerRadius, float outerRadius, Angle centerDir, Angle halfAngle)
        {
            if (halfAngle.Rad <= 0 || outerRadius <= 0 || innerRadius >= outerRadius)
                yield break;

            if (halfAngle.Rad >= MathF.PI)
            {
                foreach (var p in RasterizeDonut(origin, innerRadius, outerRadius))
                    yield return p;
                yield break;
            }

            var delta = Resolution * 0.707107f;
            var r1 = outerRadius + delta; // d >= r1 => fully outside
            var r2 = Math.Max(0, outerRadius - delta);
            var r3 = innerRadius > 0 ? innerRadius + delta : 0; // r2 > d >= r3 => fully inside
            var r4 = Math.Max(0, innerRadius - delta);

            foreach (var p in EnumeratePixels())
            {
                var off = p.center - origin;
                var d = off.Length();
                if (d >= r1 || d < r4)
                {
                    yield return (p.x, p.y, Coverage.Outside);
                    continue;
                }

                var dir = (Angle.FromDirection(off) - centerDir).Normalized();
                var angularDist = MathF.Abs(dir.Rad);
                var sideDist = (angularDist - halfAngle.Rad) * d;
                if (sideDist >= delta)
                {
                    yield return (p.x, p.y, Coverage.Outside);
                    continue;
                }

                yield return (p.x, p.y, (sideDist <= -delta && d >= r3 && d < r2) ? Coverage.Inside : Coverage.Border);
            }
        }

        public IEnumerable<(int x, int y, Coverage cv)> RasterizeRect(WPos origin, Angle direction, float lenFront, float lenBack, float halfWidth)
        {
            var delta = Resolution * 0.707107f;
            var dir = direction.ToDirection();
            var normal = dir.OrthoL();
            foreach (var p in EnumeratePixels())
            {
                var offset = p.center - origin;
                var dotDir = offset.Dot(dir);
                var dotNormal = MathF.Abs(offset.Dot(normal));
                var c = dotDir < -lenBack - delta || dotDir > lenFront + delta || dotNormal > halfWidth + delta ? Coverage.Outside
                    : dotDir >= -lenBack + delta && dotDir <= lenFront - delta && dotNormal <= halfWidth - delta ? Coverage.Inside
                    : Coverage.Border;
                yield return (p.x, p.y, c);
            }
        }

        public IEnumerable<(int x, int y, Coverage cv)> RasterizeCross(WPos origin, Angle direction, float length, float halfWidth)
        {
            var delta = Resolution * 0.707107f;
            var dir = direction.ToDirection();
            var normal = dir.OrthoL();
            foreach (var p in EnumeratePixels())
            {
                var offset = p.center - origin;
                var dotDir = MathF.Abs(offset.Dot(dir));
                var dotNormal = MathF.Abs(offset.Dot(normal));
                var minDot = Math.Min(dotDir, dotNormal);
                var c = dotDir > length + delta || dotNormal > length + delta || minDot > halfWidth + delta ? Coverage.Outside
                    : dotDir > length - delta || dotNormal > length - delta || minDot > halfWidth + delta ? Coverage.Border
                    : Coverage.Inside;
                yield return (p.x, p.y, c);
            }
        }

        private IEnumerable<(int x, int y, WPos center)> EnumeratePixels()
        {
            var rsq = Resolution * Resolution; // since we then multiply by _localZDivRes, end result is same as * res * rotation.ToDir()
            var dx = _localZDivRes.OrthoL() * rsq;
            var dy = _localZDivRes * rsq;
            var cy = Center + (-Width / 2 + 0.5f) * dx + (-Height / 2 + 0.5f) * dy;
            for (int y = 0; y < Height; y++)
            {
                var cx = cy;
                for (int x = 0; x < Width; ++x)
                {
                    yield return (x, y, cx);
                    cx += dx;
                }
                cy += dy;
            }
        }
    }
}
