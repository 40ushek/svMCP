namespace TeklaMcpServer.Api.Algorithms.Packing;

public enum MaxRectsHeuristic
{
    BestShortSideFit,
    BestLongSideFit,
    BestAreaFit
}

public readonly struct PackedRectangle
{
    public PackedRectangle(double x, double y, double width, double height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }
}

public sealed class MaxRectsBinPacker
{
    private readonly bool _allowRotation;
    private readonly List<PackedRectangle> _freeRectangles = new();

    public MaxRectsBinPacker(
        double binWidth,
        double binHeight,
        bool allowRotation = false,
        IEnumerable<PackedRectangle>? blockedRectangles = null)
    {
        if (binWidth <= 0) throw new System.ArgumentOutOfRangeException(nameof(binWidth));
        if (binHeight <= 0) throw new System.ArgumentOutOfRangeException(nameof(binHeight));

        _allowRotation = allowRotation;
        _freeRectangles.Add(new PackedRectangle(0, 0, binWidth, binHeight));

        if (blockedRectangles == null)
            return;

        foreach (var blocked in blockedRectangles)
        {
            if (!TryClipToBin(blocked, binWidth, binHeight, out var clipped))
                continue;

            PlaceRectangle(clipped);
        }
    }

    public bool TryInsert(double width, double height, MaxRectsHeuristic heuristic, out PackedRectangle placement)
    {
        if (width <= 0 || height <= 0)
        {
            placement = default;
            return false;
        }

        var best = FindBestNode(width, height, heuristic, out var bestScore1, out var bestScore2);
        if (bestScore1 == double.MaxValue || best.Width <= 0 || best.Height <= 0)
        {
            placement = default;
            return false;
        }

        PlaceRectangle(best);
        placement = best;
        return true;
    }

    public bool TryInsertClosestToPoint(double width, double height, double targetCenterX, double targetCenterY, out PackedRectangle placement)
    {
        if (width <= 0 || height <= 0)
        {
            placement = default;
            return false;
        }

        var bestDistance = double.MaxValue;
        var bestNode = default(PackedRectangle);

        foreach (var free in _freeRectangles)
        {
            TryScoreClosestCandidate(width, height, targetCenterX, targetCenterY, free, ref bestDistance, ref bestNode);

            if (_allowRotation)
                TryScoreClosestCandidate(height, width, targetCenterX, targetCenterY, free, ref bestDistance, ref bestNode);
        }

        if (bestDistance == double.MaxValue || bestNode.Width <= 0 || bestNode.Height <= 0)
        {
            placement = default;
            return false;
        }

        PlaceRectangle(bestNode);
        placement = bestNode;
        return true;
    }

    private PackedRectangle FindBestNode(double width, double height, MaxRectsHeuristic heuristic, out double bestScore1, out double bestScore2)
    {
        bestScore1 = double.MaxValue;
        bestScore2 = double.MaxValue;
        var bestNode = default(PackedRectangle);

        foreach (var free in _freeRectangles)
        {
            TryScoreCandidate(width, height, free, heuristic, ref bestScore1, ref bestScore2, ref bestNode);

            if (_allowRotation)
                TryScoreCandidate(height, width, free, heuristic, ref bestScore1, ref bestScore2, ref bestNode);
        }

        return bestNode;
    }

    private static void TryScoreCandidate(
        double width,
        double height,
        PackedRectangle free,
        MaxRectsHeuristic heuristic,
        ref double bestScore1,
        ref double bestScore2,
        ref PackedRectangle bestNode)
    {
        if (free.Width < width || free.Height < height)
            return;

        var leftoverHoriz = System.Math.Abs(free.Width - width);
        var leftoverVert = System.Math.Abs(free.Height - height);
        var shortSideFit = System.Math.Min(leftoverHoriz, leftoverVert);
        var longSideFit = System.Math.Max(leftoverHoriz, leftoverVert);
        var areaFit = free.Width * free.Height - width * height;

        var score1 = heuristic switch
        {
            MaxRectsHeuristic.BestShortSideFit => shortSideFit,
            MaxRectsHeuristic.BestLongSideFit => longSideFit,
            _ => areaFit
        };
        var score2 = heuristic switch
        {
            MaxRectsHeuristic.BestShortSideFit => longSideFit,
            MaxRectsHeuristic.BestLongSideFit => shortSideFit,
            _ => shortSideFit
        };

        if (score1 < bestScore1 || (score1 == bestScore1 && score2 < bestScore2))
        {
            bestNode = new PackedRectangle(free.X, free.Y, width, height);
            bestScore1 = score1;
            bestScore2 = score2;
        }
    }

    private static void TryScoreClosestCandidate(
        double width,
        double height,
        double targetCenterX,
        double targetCenterY,
        PackedRectangle free,
        ref double bestDistance,
        ref PackedRectangle bestNode)
    {
        if (free.Width < width || free.Height < height)
            return;

        var minX = free.X;
        var maxX = free.X + free.Width - width;
        var minY = free.Y;
        var maxY = free.Y + free.Height - height;

        var candidateX = Clamp(targetCenterX - (width / 2.0), minX, maxX);
        var candidateY = Clamp(targetCenterY - (height / 2.0), minY, maxY);
        var centerX = candidateX + (width / 2.0);
        var centerY = candidateY + (height / 2.0);
        var dx = centerX - targetCenterX;
        var dy = centerY - targetCenterY;
        var distance = (dx * dx) + (dy * dy);

        if (distance < bestDistance)
        {
            bestDistance = distance;
            bestNode = new PackedRectangle(candidateX, candidateY, width, height);
        }
    }

    private void PlaceRectangle(PackedRectangle used)
    {
        for (var i = 0; i < _freeRectangles.Count; i++)
        {
            if (SplitFreeRectangle(_freeRectangles[i], used))
            {
                _freeRectangles.RemoveAt(i);
                i--;
            }
        }

        PruneFreeList();
    }

    private bool SplitFreeRectangle(PackedRectangle free, PackedRectangle used)
    {
        if (!Intersects(free, used))
            return false;

        if (used.X < free.X + free.Width && used.X + used.Width > free.X)
        {
            if (used.Y > free.Y && used.Y < free.Y + free.Height)
                _freeRectangles.Add(new PackedRectangle(free.X, free.Y, free.Width, used.Y - free.Y));

            if (used.Y + used.Height < free.Y + free.Height)
                _freeRectangles.Add(new PackedRectangle(
                    free.X,
                    used.Y + used.Height,
                    free.Width,
                    free.Y + free.Height - (used.Y + used.Height)));
        }

        if (used.Y < free.Y + free.Height && used.Y + used.Height > free.Y)
        {
            if (used.X > free.X && used.X < free.X + free.Width)
                _freeRectangles.Add(new PackedRectangle(free.X, free.Y, used.X - free.X, free.Height));

            if (used.X + used.Width < free.X + free.Width)
                _freeRectangles.Add(new PackedRectangle(
                    used.X + used.Width,
                    free.Y,
                    free.X + free.Width - (used.X + used.Width),
                    free.Height));
        }

        return true;
    }

    private static bool Intersects(PackedRectangle a, PackedRectangle b)
    {
        if (b.X >= a.X + a.Width || b.X + b.Width <= a.X || b.Y >= a.Y + a.Height || b.Y + b.Height <= a.Y)
            return false;

        return true;
    }

    private void PruneFreeList()
    {
        for (var i = 0; i < _freeRectangles.Count; i++)
        {
            for (var j = i + 1; j < _freeRectangles.Count; j++)
            {
                if (Contains(_freeRectangles[i], _freeRectangles[j]))
                {
                    _freeRectangles.RemoveAt(j);
                    j--;
                    continue;
                }

                if (Contains(_freeRectangles[j], _freeRectangles[i]))
                {
                    _freeRectangles.RemoveAt(i);
                    i--;
                    break;
                }
            }
        }

        _freeRectangles.RemoveAll(r => r.Width <= 0 || r.Height <= 0);
    }

    private static bool TryClipToBin(PackedRectangle rectangle, double binWidth, double binHeight, out PackedRectangle clipped)
    {
        var minX = System.Math.Max(0, rectangle.X);
        var minY = System.Math.Max(0, rectangle.Y);
        var maxX = System.Math.Min(binWidth, rectangle.X + rectangle.Width);
        var maxY = System.Math.Min(binHeight, rectangle.Y + rectangle.Height);

        if (maxX <= minX || maxY <= minY)
        {
            clipped = default;
            return false;
        }

        clipped = new PackedRectangle(minX, minY, maxX - minX, maxY - minY);
        return true;
    }

    private static bool Contains(PackedRectangle a, PackedRectangle b)
    {
        return b.X >= a.X
            && b.Y >= a.Y
            && b.X + b.Width <= a.X + a.Width
            && b.Y + b.Height <= a.Y + a.Height;
    }

    private static double Clamp(double value, double min, double max)
    {
        if (value < min)
            return min;
        if (value > max)
            return max;
        return value;
    }
}
