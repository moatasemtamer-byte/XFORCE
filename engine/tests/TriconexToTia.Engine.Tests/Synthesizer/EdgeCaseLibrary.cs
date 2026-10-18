using System.Collections.Generic;

namespace TriconexToTia.Engine.Tests.Synthesizer;

public static class EdgeCaseLibrary
{
    public static IEnumerable<(int Case, string Name, string Xml)> GetCases()
    {
        var cases = new List<(int Case, string Name, string Xml)>();
        cases.Add((1, "EmptyProject", PlcOpenXmlSynthesizer.CreateMinimal()));
        for (int i = 2; i <= 14; i++)
        {
            cases.Add((i, $"Case{i}", PlcOpenXmlSynthesizer.CreateWithBlock($"Block{i}", $"// case {i}")));
        }
        cases.Add((15, "Case15_EmptyBody", PlcOpenXmlSynthesizer.CreateWithBlock("Block15", "")));
        cases.Add((16, "Case16_SfcDefensive", PlcOpenXmlSynthesizer.CreateWithBlock("Block16", "// SFC defensive")));
        cases.Add((17, "Case17_CaseInsensitiveCollision", PlcOpenXmlSynthesizer.CreateWithBlock("block17", "// collision")));
        for (int i = 18; i <= 20; i++)
        {
            cases.Add((i, $"Case{i}", PlcOpenXmlSynthesizer.CreateWithBlock($"Block{i}", $"// case {i}")));
        }
        return cases;
    }
}
