using System.Text.Json;
using System.Xml.Linq;

namespace Pt2Conv;

/// <summary>
/// Captures the per-module <c>SETUP_PARAMETERS</c> / <c>MEMORYALLOCATION_PARAMETERS</c> defaults
/// from a TriStation XML export. The defaults are module-library data (not project data), so one
/// capture per module id is enough to let the decompiler emit the full parameter blocks for any
/// project that uses that module. The result can be dropped next to the executable as
/// <c>hw_module_defaults.json</c>; it is merged over the embedded catalogue at run time.
/// </summary>
public static class HwCapture
{
    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: capturehw <export.xml> [<out.json>]");
            Console.Error.WriteLine("  merges one entry per <ts:MODULE ID> into the output file");
            return 1;
        }
        string xmlPath = args[1];
        string outPath = args.Length > 2 && !args[2].StartsWith("--", StringComparison.Ordinal)
            ? args[2]
            : Path.Combine(AppContext.BaseDirectory, "hw_module_defaults.json");

        var doc = XmlRepair.Load(xmlPath);
        var hw = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "HARDWARE_ALLOCATION");
        if (hw == null)
        {
            Console.Error.WriteLine("no <ts:HARDWARE_ALLOCATION> in the XML");
            return 2;
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        HwFile result = File.Exists(outPath)
            ? JsonSerializer.Deserialize<HwFile>(File.ReadAllText(outPath), options) ?? new HwFile()
            : new HwFile();

        foreach (var ch in hw.Descendants().Where(e => e.Name.LocalName is "MAIN_CHASSIS" or "EXP_CHASSIS"))
        {
            string type = (string?)ch.Attribute("Type") ?? "";
            string desc = (string?)ch.Attribute("Description") ?? "";
            if (type.Length > 0 && desc.Length > 0) result.chassisDescriptions[type] = desc;
        }

        int added = 0;
        foreach (var mod in hw.Descendants().Where(e => e.Name.LocalName == "MODULE"))
        {
            string id = (string?)mod.Attribute("ID") ?? "";
            if (id.Length == 0 || result.modules.ContainsKey(id)) continue;

            var setup = new List<string[]>();
            var mem = new List<string>();
            foreach (var el in mod.Elements())
            {
                if (el.Name.LocalName == "SETUP_PARAMETERS")
                {
                    foreach (var a in el.Elements().Where(e => e.Name.LocalName == "HardwareModuleAttribute"))
                        setup.Add(new[] { (string?)a.Attribute("Name") ?? "", (string?)a.Attribute("Value") ?? "" });
                }
                else if (el.Name.LocalName == "MEMORYALLOCATION_PARAMETERS")
                {
                    foreach (var a in el.Elements().Where(e => e.Name.LocalName == "HardwareModuleAttribute"))
                        mem.Add((string?)a.Attribute("Name") ?? "");
                }
            }
            result.modules[id] = new HwFileModule { setup = setup, memory = mem };
            added++;
        }

        File.WriteAllText(outPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = false }));
        Console.WriteLine($"wrote {outPath}: +{added} module(s), {result.modules.Count} total, {result.chassisDescriptions.Count} chassis type(s)");
        return 0;
    }

    private sealed class HwFile
    {
        public int integerSlots { get; set; } = 32;
        public int discreteSlots { get; set; } = 32;
        public int emptyModuleId { get; set; } = 96;
        public string pointUsedValue { get; set; } = "Not-Used";
        public string pointUnusedValue { get; set; } = "Non-Supervised";
        public Dictionary<string, string> chassisDescriptions { get; set; } = new();
        public Dictionary<string, HwFileModule> modules { get; set; } = new();
    }

    private sealed class HwFileModule
    {
        public List<string[]> setup { get; set; } = new();
        public List<string> memory { get; set; } = new();
    }
}
