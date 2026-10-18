namespace TriconexToTia.Core;

public static class TiaSchemas
{
    public static string GetSchemaVersion(int tiaVersion) => tiaVersion <= 18 ? "4.0" : "5.0";
    public static string GetTargetXmlVersion(int tiaVersion) => tiaVersion <= 18 ? "V4" : "V5";
}
