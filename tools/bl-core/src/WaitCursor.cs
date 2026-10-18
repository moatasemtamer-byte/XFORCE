using System;

namespace BL
{
    /// <summary>
    /// Cross-platform stand-in for the original WPF wait cursor. The headless host has
    /// no UI thread, so showing and restoring a cursor is a no-op.
    /// </summary>
    public class WaitCursor : IDisposable
    {
        public WaitCursor()
        {
        }

        public void Dispose()
        {
        }
    }
}
