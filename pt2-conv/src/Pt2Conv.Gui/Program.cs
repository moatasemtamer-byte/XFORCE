using System;
using System.Windows.Forms;

namespace Pt2Conv.Gui;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try { Application.SetHighDpiMode(HighDpiMode.SystemAware); } catch { }
        Application.Run(new MainForm());
    }
}
