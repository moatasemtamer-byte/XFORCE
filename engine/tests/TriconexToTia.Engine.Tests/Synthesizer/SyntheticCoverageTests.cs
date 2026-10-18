using System.IO;
using System.Text;
using TriconexToTia.Core.Results;
using TriconexToTia.Engine;
using Xunit;

namespace TriconexToTia.Engine.Tests.Synthesizer;

public class SyntheticCoverageTests
{
    [Fact]
    public void Synthetic_Cases_GenerateSuccessfully()
    {
        var engine = new ConversionEngine();
        foreach (var c in EdgeCaseLibrary.GetCases())
        {
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(c.Xml));
            var res = engine.Run(ms, new ConverterOptions { TiaVersion = 16 });
            Assert.True(res.Success, $"Case {c.Case} ({c.Name}) failed: {res.FatalError}");
        }
    }
}
