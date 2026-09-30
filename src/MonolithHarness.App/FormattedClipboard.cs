using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace MonolithHarness.App;

internal static class FormattedClipboard
{
    [DllImport("user32.dll", SetLastError = true)] static extern bool OpenClipboard(nint owner);
    [DllImport("user32.dll")] static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true)] static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError = true)] static extern nint SetClipboardData(uint format, nint handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterClipboardFormat(string format);
    [DllImport("user32.dll")] static extern nint GetActiveWindow();
    [DllImport("user32.dll")] static extern nint GetForegroundWindow();
    [DllImport("kernel32.dll", SetLastError = true)] static extern nint GlobalAlloc(uint flags, nuint size);
    [DllImport("kernel32.dll")] static extern nint GlobalFree(nint handle);
    [DllImport("kernel32.dll")] static extern nint GlobalLock(nint handle);
    [DllImport("kernel32.dll")] static extern bool GlobalUnlock(nint handle);

    internal static string HtmlFormat(string html)
    {
        const string prefix = "<html><head><meta charset=\"utf-8\"></head><body><!--StartFragment-->";
        const string suffix = "<!--EndFragment--></body></html>";
        const string headerFormat = "Version:1.0\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        var startHtml = Encoding.UTF8.GetByteCount(string.Format(headerFormat, 0, 0, 0, 0));
        var startFragment = startHtml + Encoding.UTF8.GetByteCount(prefix);
        var endFragment = startFragment + Encoding.UTF8.GetByteCount(html);
        var endHtml = endFragment + Encoding.UTF8.GetByteCount(suffix);
        return string.Format(headerFormat, startHtml, endHtml, startFragment, endFragment) + prefix + html + suffix;
    }
    internal static async Task WriteAsync(string html, string text)
    {
        var htmlHandle = Allocate(Encoding.UTF8.GetBytes(HtmlFormat(html) + '\0'));
        nint textHandle = 0; var opened = false;
        try
        {
            textHandle = Allocate(Encoding.Unicode.GetBytes(text + '\0'));
            var owner = GetActiveWindow(); if (owner == 0) owner = GetForegroundWindow();
            for (var attempt = 0; attempt < 5 && !(opened = OpenClipboard(owner)); attempt++) await Task.Delay(30);
            if (!opened) throw new IOException("Le presse-papiers est occupé. Réessayez. / Clipboard busy; please retry.");
            var format = RegisterClipboardFormat("HTML Format");
            if (format == 0 || !EmptyClipboard()) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (SetClipboardData(format, htmlHandle) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            htmlHandle = 0; // Ownership has been transferred to Windows.
            if (SetClipboardData(13, textHandle) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            textHandle = 0;
        }
        finally { if (opened) CloseClipboard(); if (htmlHandle != 0) GlobalFree(htmlHandle); if (textHandle != 0) GlobalFree(textHandle); }
    }
    static nint Allocate(byte[] bytes)
    {
        var handle = GlobalAlloc(0x0002, (nuint)bytes.Length);
        if (handle == 0) throw new OutOfMemoryException();
        var pointer = GlobalLock(handle);
        if (pointer == 0) { GlobalFree(handle); throw new Win32Exception(Marshal.GetLastWin32Error()); }
        try { Marshal.Copy(bytes, 0, pointer, bytes.Length); }
        finally { GlobalUnlock(handle); }
        return handle;
    }
}
