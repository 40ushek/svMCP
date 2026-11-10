using System;
using System.Collections.Generic;
using System.Linq;

namespace TeklaMcpServer.Api.Drawing.ViewLayout.Constraints;

internal enum LayoutRelationKind
{
    /// <summary>First is to the left of Second, at least one gap apart.</summary>
    LeftOf,

    /// <summary>First is below Second, at least one gap apart.</summary>
    Below,

    /// <summary>Second's center is Offset to the right of First's; zero puts them on one vertical line.</summary>
    AlignCenterX,

    /// <summary>Second's center is Offset above First's; zero puts them on one horizontal line.</summary>
    AlignCenterY
}

internal readonly struct LayoutRelation
{
    public LayoutRelation(LayoutRelationKind kind, int firstId, int secondId, double offset = 0.0)
    {
        Kind = kind;
        FirstId = firstId;
        SecondId = secondId;
        Offset = offset;
    }

    public LayoutRelationKind Kind { get; }

    public int FirstId { get; }

    public int SecondId { get; }

    /// <summary>Used by the alignment kinds only.</summary>
    public double Offset { get; }
}

internal readonly struct ConstraintLayoutView
{
    public ConstraintLayoutView(int id, double width, double height)
    {
        Id = id;
        Width = width;
        Height = height;
    }

    public int Id { get; }

    /// <summary>Frame width on the sheet.</summary>
    public double Width { get; }

    /// <summary>Frame height on the sheet.</summary>
    public double Height { get; }
}

/// <summary>One topology: the views, how they relate, and the sheet area they must fit.</summary>
internal sealed class ConstraintLayoutProblem
{
    public ConstraintLayoutProblem(
        ReservedRect bounds,
        double gap,
        IReadOnlyList<ConstraintLayoutView> views,
        IReadOnlyList<LayoutRelation> relations,
        int? anchorViewId = null)
    {
        Bounds = bounds ?? throw new ArgumentNullException(nameof(bounds));
        Gap = gap;
        Views = views ?? throw new ArgumentNullException(nameof(views));
        Relations = relations ?? throw new ArgumentNullException(nameof(relations));
        AnchorViewId = anchorViewId;
    }

    /// <summary>Usable sheet area, margins already taken off.</summary>
    public ReservedRect Bounds { get; }

    public double Gap { get; }

    public IReadOnlyList<ConstraintLayoutView> Views { get; }

    public IReadOnlyList<LayoutRelation> Relations { get; }

    /// <summary>The view the others gather around, normally the base view.</summary>
    public int? AnchorViewId { get; }
}

internal sealed class ConstraintLayoutSolution
{
    public AxisFailure HorizontalFailure { get; set; }

    public AxisFailure VerticalFailure { get; set; }

    public double RequiredWidth { get; set; }

    public double RequiredHeight { get; set; }

    /// <summary>Frame rectangles in sheet coordinates. Empty when either axis is contradictory.</summary>
    public IReadOnlyDictionary<int, ReservedRect> RectsById { get; set; } = new Dictionary<int, ReservedRect>();

    /// <summary>
    /// Pairs the relations leave closer than one gap on both axes. The topology has
    /// to separate every pair on some axis; these are the ones it missed.
    /// </summary>
    public IReadOnlyList<(int FirstId, int SecondId)> UnseparatedPairs { get; set; } = Array.Empty<(int, int)>();

    public bool IsFeasible =>
        HorizontalFailure == AxisFailure.None &&
        VerticalFailure == AxisFailure.None &&
        UnseparatedPairs.Count == 0;
}

/// <summary>Solves view rectangles for one topology. X and Y are independent.</summary>
internal static class ConstraintLayoutSolver
{
    private const double Epsilon = 1e-6;

    public static ConstraintLayoutSolution Solve(ConstraintLayoutProblem problem)
    {
        if (problem == null)
            throw new ArgumentNullException(nameof(problem));

        var horizontal = SolveAxis(
            problem, problem.Bounds.MinX, problem.Bounds.MaxX,
            static view => view.Width, LayoutRelationKind.LeftOf, LayoutRelationKind.AlignCenterX);
        var vertical = SolveAxis(
            problem, problem.Bounds.MinY, problem.Bounds.MaxY,
            static view => view.Height, LayoutRelationKind.Below, LayoutRelationKind.AlignCenterY);

        var solution = new ConstraintLayoutSolution
        {
            HorizontalFailure = horizontal.Failure,
            VerticalFailure = vertical.Failure,
            RequiredWidth = horizontal.RequiredSize,
            RequiredHeight = vertical.RequiredSize
        };
        if (horizontal.Failure == AxisFailure.Contradiction || vertical.Failure == AxisFailure.Contradiction)
            return solution;

        var rects = problem.Views.ToDictionary(
            static view => view.Id,
            view => new ReservedRect(
                horizontal.Centers[view.Id] - (view.Width / 2.0),
                vertical.Centers[view.Id] - (view.Height / 2.0),
                horizontal.Centers[view.Id] + (view.Width / 2.0),
                vertical.Centers[view.Id] + (view.Height / 2.0)));
        solution.RectsById = rects;
        solution.UnseparatedPairs = FindUnseparatedPairs(problem.Views, rects, problem.Gap);
        return solution;
    }

    private static AxisSolution SolveAxis(
        ConstraintLayoutProblem problem,
        double min,
        double max,
        Func<ConstraintLayoutView, double> size,
        LayoutRelationKind separationKind,
        LayoutRelationKind alignmentKind)
        => AxisConstraintSolver.Solve(
            min,
            max,
            problem.Views.Select(view => new AxisItem(view.Id, size(view))).ToList(),
            problem.Relations
                .Where(relation => relation.Kind == separationKind)
                .Select(relation => new AxisSeparation(relation.FirstId, relation.SecondId, problem.Gap))
                .ToList(),
            problem.Relations
                .Where(relation => relation.Kind == alignmentKind)
                .Select(static relation => new AxisAlignment(relation.FirstId, relation.SecondId, relation.Offset))
                .ToList(),
            problem.AnchorViewId);

    private static List<(int FirstId, int SecondId)> FindUnseparatedPairs(
        IReadOnlyList<ConstraintLayoutView> views,
        IReadOnlyDictionary<int, ReservedRect> rects,
        double gap)
    {
        var pairs = new List<(int, int)>();
        for (var i = 0; i < views.Count; i++)
        for (var j = i + 1; j < views.Count; j++)
        {
            var first = rects[views[i].Id];
            var second = rects[views[j].Id];
            var clearX = Math.Max(second.MinX - first.MaxX, first.MinX - second.MaxX);
            var clearY = Math.Max(second.MinY - first.MaxY, first.MinY - second.MaxY);
            if (clearX < gap - Epsilon && clearY < gap - Epsilon)
                pairs.Add((views[i].Id, views[j].Id));
        }

        return pairs;
    }
}
