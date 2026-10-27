using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Pt2Conv.Cfb;

namespace Pt2Conv;

/// <summary>
/// Builds <c>Current/Elements/Configuration/HwConfiguration.xml</c> for the project described by
/// the PLCopen XML's <c>&lt;ts:HARDWARE_ALLOCATION&gt;</c>.
///
/// The chassis/board skeleton is TriStation module-library data (board type name, point count and
/// the binary <c>SetupInfo</c> blob), so it cannot be derived from the XML. A catalogue of board
/// templates is captured from real containers (<see cref="Capture"/>) and stored per module id;
/// the writer clones the right board class, then patches the slot and the chassis placement.
/// Module ids without a template get a minimal board class, which TriStation resolves from the
/// module id when the project is opened.
/// </summary>
public static class HwConfig
{
    public sealed class Catalog
    {
        public Dictionary<string, string> boards { get; set; } = new();   // moduleId -> <Class> board
        public Dictionary<string, string> chassis { get; set; } = new();  // "TYPE|ID" -> <Class> chassis
    }

    private static Catalog? _cached;

    public static Catalog Load()
    {
        if (_cached != null) return _cached;
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        Catalog? cat = null;
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Pt2Conv.Resources.hw_board_catalog.json"))
        {
            if (s != null)
            {
                using var r = new StreamReader(s);
                cat = JsonSerializer.Deserialize<Catalog>(r.ReadToEnd(), options);
            }
        }
        cat ??= new Catalog();
        foreach (var dir in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            if (dir == null) continue;
            string path = Path.Combine(dir, "hw_board_catalog.json");
            if (!File.Exists(path)) continue;
            try
            {
                var extra = JsonSerializer.Deserialize<Catalog>(File.ReadAllText(path), options);
                if (extra != null)
                {
                    foreach (var (k, v) in extra.boards) cat.boards[k] = v;
                    foreach (var (k, v) in extra.chassis) cat.chassis[k] = v;
                }
            }
            catch { /* keep the embedded catalogue */ }
            break;
        }
        return _cached = cat;
    }

    // ------------------------------------------------------------------ capture

    /// <summary>Captures board and chassis templates from a container (or an extracted folder).</summary>
    public static int Capture(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: captureboard <project.pt2x|extractDir> [<out.json>] [--merge]");
            return 1;
        }
        string input = args[1];
        string outPath = args.Length > 2 && !args[2].StartsWith("--", StringComparison.Ordinal)
            ? args[2] : Path.Combine(AppContext.BaseDirectory, "hw_board_catalog.json");
        bool merge = args.Any(a => a.Equals("--merge", StringComparison.OrdinalIgnoreCase));

        byte[]? hw = null;
        if (Directory.Exists(input))
        {
            string p = Path.Combine(input, "Current", "Elements", "Configuration", "HwConfiguration.xml");
            if (File.Exists(p)) hw = File.ReadAllBytes(p);
        }
        else if (File.Exists(input))
        {
            var cf = new CompoundFile(File.ReadAllBytes(input));
            hw = cf.ListStreams().FirstOrDefault(s =>
                s.Path.Equals("Current/Elements/Configuration/HwConfiguration.xml", StringComparison.OrdinalIgnoreCase))?.Data;
        }
        if (hw == null)
        {
            Console.Error.WriteLine("no Current/Elements/Configuration/HwConfiguration.xml found");
            return 2;
        }

        var root = XDocument.Parse(Encoding.UTF8.GetString(hw), LoadOptions.PreserveWhitespace).Root;
        if (root == null) return 2;
        var hwCol = root.Elements().FirstOrDefault(e => e.Name.LocalName == "Collection" && (string?)e.Attribute("Name") == "HwChassis");
        if (hwCol == null) return 2;

        var result = merge && File.Exists(outPath)
            ? JsonSerializer.Deserialize<Catalog>(File.ReadAllText(outPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new Catalog()
            : new Catalog();

        foreach (var ch in hwCol.Elements().Where(e => e.Name.LocalName == "Class"))
        {
            int num = int.TryParse((string?)ch.Attribute("ChassisNum"), out int n) ? n : -1;
            if (num == -1) continue;
            string id = (string?)ch.Attribute("ChassisID") ?? "";
            string type = ChassisType((string?)ch.Attribute("Name") ?? "");
            var clone = new XElement(ch);
            var boards = BoardsCollection(clone);
            if (boards != null)
            {
                foreach (var b in boards.Elements().Where(e => e.Name.LocalName == "Class").ToList())
                {
                    string mid = (string?)b.Attribute("ModuleID") ?? "";
                    if (mid.Length > 0 && !result.boards.ContainsKey(mid)) result.boards[mid] = b.ToString(SaveOptions.DisableFormatting);
                    b.Remove();
                }
            }
            string key = type + "|" + id;
            if (!result.chassis.ContainsKey(key)) result.chassis[key] = clone.ToString(SaveOptions.DisableFormatting);
        }

        File.WriteAllText(outPath, JsonSerializer.Serialize(result));
        Console.WriteLine($"wrote {outPath}: {result.boards.Count} board template(s), {result.chassis.Count} chassis template(s)");
        return 0;
    }

    // ------------------------------------------------------------------ writer

    /// <summary>
    /// Rewrites the hardware configuration when the XML's chassis/board layout differs from the
    /// template's. Returns true when the stream was replaced.
    /// </summary>
    public static bool Apply(List<(string Path, byte[] Data)> tree, XDocument doc)
    {
        const string hwPath = "Current/Elements/Configuration/HwConfiguration.xml";
        int hi = tree.FindIndex(t => t.Path.Equals(hwPath, StringComparison.OrdinalIgnoreCase));
        if (hi < 0) return false;

        var desired = ReadLayout(doc);
        if (desired.Count == 0) return false;

        XDocument hwDoc;
        try { hwDoc = XDocument.Parse(Encoding.UTF8.GetString(tree[hi].Data), LoadOptions.PreserveWhitespace); }
        catch { return false; }
        var root = hwDoc.Root;
        if (root == null) return false;
        var hwCol = root.Elements().FirstOrDefault(e => e.Name.LocalName == "Collection" && (string?)e.Attribute("Name") == "HwChassis");
        if (hwCol == null) return false;

        var existing = ReadLayout(hwCol);
        if (Environment.GetEnvironmentVariable("PT2_HWDBG") == "1")
        {
            Console.WriteLine("  hw desired: " + string.Join(" | ", desired.Select(c =>
                $"{c.Number}/{c.Type}/{c.Id}: " + string.Join(",", c.Modules.OrderBy(m => m.Slot).Select(m => m.Slot + ":" + m.ModuleId)))));
            Console.WriteLine("  hw existing: " + string.Join(" | ", existing.Select(c =>
                $"{c.Number}/{c.Type}/{c.Id}: " + string.Join(",", c.Modules.OrderBy(m => m.Slot).Select(m => m.Slot + ":" + m.ModuleId)))));
        }
        if (SameLayout(desired, existing)) return false;

        var catalog = Load();
        var missing = desired.SelectMany(c => c.Modules)
            .Select(m => m.ModuleId.ToString(CultureInfo.InvariantCulture))
            .Distinct().Where(id => !catalog.boards.ContainsKey(id)).ToList();

        var built = new List<XElement>();
        var memory = hwCol.Elements().FirstOrDefault(e =>
            e.Name.LocalName == "Class" && (string?)e.Attribute("ChassisNum") == "-1");
        if (memory != null) built.Add(new XElement(memory));
        foreach (var chassis in desired)
        {
            // Prefer a template chassis with the same number/type/id: it carries the main-processor
            // boards and empty-slot placeholders, which the XML does not list. Otherwise a clean
            // chassis for that type (board catalogue / same type / generic) is used.
            var template = hwCol.Elements().FirstOrDefault(e => e.Name.LocalName == "Class"
                && (string?)e.Attribute("ChassisNum") == chassis.Number.ToString(CultureInfo.InvariantCulture)
                && (string?)e.Attribute("ChassisID") == chassis.Id
                && ChassisType((string?)e.Attribute("Name") ?? "") == chassis.Type);
            string key = chassis.Type + "|" + chassis.Id;
            XElement? ch = template != null ? new XElement(template)
                : catalog.chassis.TryGetValue(key, out var xml) ? XElement.Parse(xml, LoadOptions.PreserveWhitespace)
                : hwCol.Elements().FirstOrDefault(e => e.Name.LocalName == "Class"
                    && ChassisType((string?)e.Attribute("Name") ?? "") == chassis.Type) is { } sameType
                    ? new XElement(sameType)
                    : hwCol.Elements().FirstOrDefault(e => e.Name.LocalName == "Class"
                        && (string?)e.Attribute("ChassisNum") == "0") is { } generic
                        ? new XElement(generic)
                        : null;
            if (ch == null)
            {
                Console.WriteLine($"  hardware: no chassis template for {key}; kept the template configuration");
                return false;
            }
            bool cleanChassis = template == null;

            ch.SetAttributeValue("ChassisNum", chassis.Number.ToString(CultureInfo.InvariantCulture));
            ch.SetAttributeValue("ChassisID", chassis.Id);
            ch.SetAttributeValue("Name", chassis.Number == 0
                ? "Main Chassis: " + chassis.Type
                : $"Chassis {chassis.Number}: {chassis.Type}");

            var boards = BoardsCollection(ch);
            if (boards == null)
            {
                boards = new XElement("Collection", new XAttribute("Name", "Boards"),
                    new XAttribute("ClassType", "CPPDHwBoard"), new XAttribute("CollectionType", "1"));
                ch.Add(boards);
            }
            if (cleanChassis) boards.RemoveNodes();
            else
            {
                // Drop template boards the XML does not place in this chassis (main-processor
                // boards at negative slots and empty-slot placeholders are kept).
                var wanted = new HashSet<string>(chassis.Modules.Where(m => !IsEmptyModule(m.ModuleId))
                    .Select(m => (m.Slot - 1).ToString(CultureInfo.InvariantCulture)));
                foreach (var b in boards.Elements().Where(e => e.Name.LocalName == "Class").ToList())
                {
                    if (!int.TryParse((string?)b.Attribute("SlotID"), out int sid) || sid < 0) continue;
                    int bid = int.TryParse((string?)b.Attribute("ModuleID"), out int m) ? m : 0;
                    if (!IsEmptyModule(bid) && !wanted.Contains(sid.ToString(CultureInfo.InvariantCulture))) b.Remove();
                }
            }
            foreach (var mod in chassis.Modules)
            {
                if (IsEmptyModule(mod.ModuleId)) continue; // empty-slot placeholder, not a board
                string moduleKey = mod.ModuleId.ToString(CultureInfo.InvariantCulture);
                XElement board = catalog.boards.TryGetValue(moduleKey, out var boardXml)
                    ? XElement.Parse(boardXml, LoadOptions.PreserveWhitespace)
                    : MinimalBoard(moduleKey);
                board.SetAttributeValue("SlotID", (mod.Slot - 1).ToString(CultureInfo.InvariantCulture));
                string name = (string?)board.Attribute("Name") ?? "";
                if (name.StartsWith("Slot ", StringComparison.Ordinal) && name.Length > 5 && char.IsDigit(name[5]))
                {
                    int colon = name.IndexOf(':');
                    if (colon > 0) board.SetAttributeValue("Name", $"Slot {mod.Slot}: " + name[(colon + 1)..]);
                }

                var slots = boards.Elements().Where(e => e.Name.LocalName == "Class").ToList();
                int at = slots.FindIndex(b => (string?)b.Attribute("SlotID") == (mod.Slot - 1).ToString(CultureInfo.InvariantCulture));
                if (at >= 0) slots[at].ReplaceWith(board);
                else if (slots.Count == 0) boards.Add(board);
                else
                {
                    var before = slots.FirstOrDefault(b =>
                        int.TryParse((string?)b.Attribute("SlotID"), out int bs) && bs > mod.Slot - 1);
                    if (before != null) before.AddBeforeSelf(board);
                    else slots[^1].AddAfterSelf(board);
                }
            }
            built.Add(ch);
        }

        hwCol.ReplaceNodes(built);
        string text = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n"
                      + root.ToString(SaveOptions.DisableFormatting) + "\r\n";
        tree[hi] = (tree[hi].Path, Encoding.UTF8.GetBytes(text));
        Console.WriteLine($"  hardware: rebuilt chassis configuration ({(missing.Count == 0 ? "all boards from catalogue" : "minimal board(s) for module(s) " + string.Join(",", missing))})");
        return true;
    }

    private sealed class Layout
    {
        public int Number;
        public string Id = "";
        public string Type = "";
        public List<(int Slot, int ModuleId)> Modules = new();
    }

    private static List<Layout> ReadLayout(XDocument doc)
    {
        var result = new List<Layout>();
        var hw = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "HARDWARE_ALLOCATION");
        if (hw == null) return result;
        var config = hw.Descendants().FirstOrDefault(e => e.Name.LocalName == "CHASSIS_CONFIGURATION") ?? hw;
        Layout? current = null;
        foreach (var el in config.Descendants())
        {
            if (el.Name.LocalName is "MAIN_CHASSIS" or "EXP_CHASSIS")
            {
                string type = (string?)el.Attribute("Type") ?? "";
                current = new Layout
                {
                    Number = el.Name.LocalName == "MAIN_CHASSIS" ? 0
                        : int.TryParse((string?)el.Attribute("ChassisNumber"), out int n) ? n : 0,
                    Id = (string?)el.Attribute("Id") ?? (el.Name.LocalName == "MAIN_CHASSIS" ? "12" : ""),
                    Type = type,
                };
                result.Add(current);
            }
            else if (el.Name.LocalName == "MODULE" && current != null)
            {
                int mid = int.TryParse((string?)el.Attribute("ID"), out int m) ? m : 0;
                int slot = el.Elements().FirstOrDefault(x => x.Name.LocalName == "SLOT_ADDRESS") is { } s
                           && int.TryParse(s.Value.Trim(), out int sv) ? sv : 0;
                current.Modules.Add((slot, mid));
            }
        }
        return result;
    }

    private static List<Layout> ReadLayout(XElement hwCol)
    {
        var result = new List<Layout>();
        foreach (var ch in hwCol.Elements().Where(e => e.Name.LocalName == "Class"))
        {
            int num = int.TryParse((string?)ch.Attribute("ChassisNum"), out int n) ? n : -1;
            if (num == -1) continue;
            var layout = new Layout
            {
                Number = num,
                Id = (string?)ch.Attribute("ChassisID") ?? "",
                Type = ChassisType((string?)ch.Attribute("Name") ?? ""),
            };
            var boards = BoardsCollection(ch);
            if (boards != null)
                foreach (var b in boards.Elements().Where(e => e.Name.LocalName == "Class"))
                {
                    int mid = int.TryParse((string?)b.Attribute("ModuleID"), out int m) ? m : 0;
                    int slot = int.TryParse((string?)b.Attribute("SlotID"), out int sl) ? sl + 1 : 0;
                    if (mid != 0) layout.Modules.Add((slot, mid));
                }
            result.Add(layout);
        }
        return result;
    }

    private static bool SameLayout(List<Layout> a, List<Layout> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].Number != b[i].Number || a[i].Type != b[i].Type || a[i].Id != b[i].Id) return false;
            // The container may carry extra boards the XML does not list (main processor, empty
            // slots); only the XML's real modules must match slot-for-slot.
            var existing = b[i].Modules.Where(m => !IsEmptyModule(m.ModuleId)).ToDictionary(m => m.Slot, m => m.ModuleId);
            foreach (var m in a[i].Modules.Where(m => !IsEmptyModule(m.ModuleId)))
                if (!existing.TryGetValue(m.Slot, out int id) || id != m.ModuleId) return false;
        }
        return true;
    }

    private static XElement? TemplateChassis(XElement hwCol, Layout wanted)
    {
        return hwCol.Elements().FirstOrDefault(e => e.Name.LocalName == "Class"
            && ChassisType((string?)e.Attribute("Name") ?? "") == wanted.Type
            && (string?)e.Attribute("ChassisID") == wanted.Id);
    }

    private static bool IsEmptyModule(int moduleId) => moduleId is 96 or 259;

    private static XElement? BoardsCollection(XElement chassis) =>
        chassis.Elements().FirstOrDefault(e => e.Name.LocalName == "Collection" && (string?)e.Attribute("Name") == "Boards");

    private static XElement MinimalBoard(string moduleId) =>
        new XElement("Class",
            new XAttribute("SlotID", "0"),
            new XAttribute("ModuleID", moduleId),
            new XAttribute("Points", "0"),
            new XAttribute("MasterTimeSync", "False"),
            new XAttribute("ModifiedFlags", "0"),
            new XAttribute("Downloaded", "False"),
            new XAttribute("Type", "3"),
            new XAttribute("Name", $"Slot 1:  Module {moduleId}"),
            new XElement("Collection", new XAttribute("Name", "SetupInfo"), new XAttribute("ClassType", "Byte"), new XAttribute("CollectionType", "5")),
            new XElement("Collection", new XAttribute("Name", "SlotFlags"), new XAttribute("ClassType", "xWORD"),
                new XAttribute("CollectionType", "2"), new XAttribute("IsMemberBase", "1"),
                new XElement("Class", new XAttribute("Value", "1")),
                new XElement("Class", new XAttribute("Value", "1"))),
            new XElement("Collection", new XAttribute("Name", "Points"), new XAttribute("ClassType", "CPPDHwPoint"), new XAttribute("CollectionType", "4")),
            new XElement("Collection", new XAttribute("Name", "DownloadedPoints"), new XAttribute("ClassType", "CDownloadedPointsXml"), new XAttribute("CollectionType", "1")));

    private static string ChassisType(string name)
    {
        int i = name.LastIndexOf(": ", StringComparison.Ordinal);
        return i >= 0 ? name[(i + 2)..] : name;
    }
}
