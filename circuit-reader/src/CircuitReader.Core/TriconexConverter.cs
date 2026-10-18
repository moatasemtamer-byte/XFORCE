// Adapts a TriconexDocument into the shared PageLadder model so the existing
// reporters / exporters / cross-reference index all work unchanged.
//
// A Triconex "network" maps onto one logical rung group; we bucket networks by
// the PDF page on which their header appeared, then copy each network's rungs
// (one per output coil) into that page.  Address labels harvested from the
// +===+ comment boxes are attached per page for the addresses used there.

namespace CircuitReader.Core;

public static class TriconexConverter
{
    public static IReadOnlyList<PageLadder> ToPageLadders(TriconexDocument doc)
    {
        var byPage = new SortedDictionary<int, PageLadder>();

        foreach (var net in doc.Networks)
        {
            if (!byPage.TryGetValue(net.Page, out var pl))
            {
                pl = new PageLadder { PageNumber = net.Page };
                byPage[net.Page] = pl;
            }

            foreach (var r in net.Rungs)
            {
                pl.Rungs.Add(r);
                AddLabel(pl, doc, r.Output?.Address);
                AddLabel(pl, doc, r.Output?.Label);
                foreach (var inp in r.Inputs)
                {
                    AddLabel(pl, doc, inp.Address);
                    AddLabel(pl, doc, inp.Label);
                }
            }
        }

        return byPage.Values.ToList();
    }

    private static void AddLabel(PageLadder pl, TriconexDocument doc, string? name)
    {
        if (string.IsNullOrEmpty(name)) return;
        if (pl.AddressLabels.ContainsKey(name)) return;
        if (doc.AddressLabels.TryGetValue(name, out var desc))
            pl.AddressLabels[name] = desc;
    }
}
