namespace TriconexToTia.Core.Models;

/// <summary>Root of the in-memory domain model built by the parser.</summary>
public sealed class PlcProject
{
    public string Name { get; set; } = "TriconexProject";

    public string SourceFile { get; set; } = string.Empty;

    public List<StructType> DataTypes { get; } = new();

    public List<PlcBlock> Blocks { get; } = new();

    public List<GlobalVariable> GlobalVariables { get; } = new();

    public List<IoPoint> IoPoints { get; } = new();

    public List<UnrecognizedElement> UnrecognizedElements { get; } = new();

    public ProjectHeader Header { get; } = new();

    public HardwareInfo Hardware { get; } = new();

    /// <summary>ts: libraries referenced by the project (TX1LIB, STDLIB, ...).</summary>
    public List<string> Libraries { get; } = new();

    /// <summary>Task interval in ms from the resource configuration.</summary>
    public int? TaskIntervalMs { get; set; }

    /// <summary>Distinct library call names used anywhere in the project.</summary>
    public List<string> LibraryCalls { get; } = new();

    public IEnumerable<FunctionBlock> FunctionBlocks => Blocks.OfType<FunctionBlock>();

    public IEnumerable<Function> Functions => Blocks.OfType<Function>();

    public IEnumerable<Program> Programs => Blocks.OfType<Program>();
}
