using System.Collections.Generic;
using System.Numerics;
using Yafc.Model;
using Yafc.UI;

namespace Yafc;

public class SearchableList<TData>(float height, Vector2 elementSize, VirtualScrollList<TData>.Drawer drawer, SearchableList<TData>.Filter filter, IComparer<TData>? comparer = null)
    : VirtualScrollList<TData>(height, elementSize, drawer) {

    private readonly List<TData> list = [];

    public delegate bool Filter(TData data, SearchQuery searchTokens);

    private readonly Filter filterFunc = filter;

    // TODO (https://github.com/Yafc-CE/yafc-ce/issues/293) investigate set()
    public new IEnumerable<TData> data {
        get;
        set {
            field = value ?? [];
            RefreshData();
        }
    } = [];

    public SearchQuery filter {
        get;
        set {
            field = value;
            RefreshData();
        }
    } = default;

    private void RefreshData() {
        list.Clear();
        if (!filter.empty) {
            foreach (var element in data) {
                if (filterFunc(element, filter)) {
                    list.Add(element);
                }
            }
        }
        else {
            list.AddRange(data);
        }

        if (comparer != null) {
            list.Sort(comparer);
        }

        base.data = list;
    }
}
