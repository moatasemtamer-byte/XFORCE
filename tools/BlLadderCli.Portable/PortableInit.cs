using System.Runtime.CompilerServices;
using System.Text;

namespace BlLadderCli
{
    /// <summary>
    /// Modern .NET only ships Unicode encodings; the BL pipeline (and the VB runtime it
    /// touches) asks for legacy code pages such as IBM437, so register them up front.
    /// </summary>
    internal static class PortableInit
    {
        [ModuleInitializer]
        internal static void Init()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }
    }
}
