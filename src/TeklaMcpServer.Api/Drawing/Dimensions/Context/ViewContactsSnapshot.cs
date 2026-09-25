using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SolidContacts;
using TeklaMcpServer.Api.Diagnostics;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>Lazy, view-scoped contact data shared by every dimension-filter context.</summary>
internal sealed class ViewContactsSnapshot
{
    private readonly int _viewId;
    private readonly int[] _modelIds;
    private readonly IReadOnlyList<UnreadPart> _selectionUnread;
    private readonly Func<int, PartSolidGeometryInViewResult> _read;
    private readonly Dictionary<int, PartSolidGeometryInViewResult> _solids = new();
    private ViewContactCandidatePointsResult? _result;

    public ViewContactsSnapshot(int viewId, IReadOnlyList<int> modelIds,
        IReadOnlyList<UnreadPart> selectionUnread, Func<int, PartSolidGeometryInViewResult> read)
    {
        _viewId = viewId;
        _modelIds = modelIds.Distinct().ToArray();
        _selectionUnread = selectionUnread;
        _read = read;
    }

    public void Seed(IReadOnlyDictionary<int, PartSolidGeometryInViewResult> solids)
    {
        foreach (var pair in solids)
            if (_modelIds.Contains(pair.Key) && !_solids.ContainsKey(pair.Key))
                _solids.Add(pair.Key, pair.Value);
    }

    public ViewContactCandidatePointsResult Get()
    {
        if (_result != null) return _result;

        var timer = Stopwatch.StartNew();
        var unread = _selectionUnread.ToList();
        var adapters = new List<ISolid>();
        foreach (var modelId in _modelIds)
        {
            if (!_solids.TryGetValue(modelId, out var geometry))
            {
                try
                {
                    geometry = _read(modelId);
                    _solids[modelId] = geometry;
                }
                catch (Exception exception)
                {
                    unread.Add(new UnreadPart(modelId, exception.Message));
                    continue;
                }
            }

            if (!geometry.Success)
            {
                unread.Add(new UnreadPart(modelId, geometry.Error ?? "geometry read failed"));
                continue;
            }

            var adapter = ViewSolidAdapter.FromGeometry(geometry);
            if (adapter == null)
            {
                unread.Add(new UnreadPart(modelId, "no usable faces after translation"));
                continue;
            }
            if (adapter.DroppedFaces > 0)
                unread.Add(new UnreadPart(modelId, $"{adapter.DroppedFaces} face(s) dropped, body searched anyway"));
            adapters.Add(adapter);
        }

        try
        {
            var graph = ContactGraph.Build(adapters, new ContactOptions());
            var contacts = new ViewContactsResult(_viewId, graph, unread, requestedIds: _modelIds);
            _result = DrawingContactCandidatePointBuilder.Build(ContactGeometryInViewBuilder.Build(contacts));
        }
        catch (Exception exception)
        {
            _result = new ViewContactCandidatePointsResult(_viewId, Array.Empty<DrawingPartCandidatePoint>(),
                Array.Empty<UnflattenedRegion>(), Array.Empty<ContactShapeInView>(), unread,
                searchComplete: false, error: $"contact search failed: {exception.Message}", requestedIds: _modelIds);
        }
        finally
        {
            PerfTrace.Write("api-geometry", "view_context_contacts_build", timer.ElapsedMilliseconds,
                $"viewId={_viewId};parts={_modelIds.Length};cachedSolids={_solids.Count};points={_result?.Points.Count ?? 0}");
        }
        return _result;
    }
}
