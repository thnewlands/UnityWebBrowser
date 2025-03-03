using UnityWebBrowser.Engine.Shared.Core;
using Xilium.CefGlue;

namespace UnityWebBrowser.Engine.Cef.Browser;

/// <summary>
///     <see cref="CefApp" /> for CefBrowserProcess
/// </summary>
public class UwbCefApp : CefApp
{
    private readonly bool mediaStreamingEnabled;
    private readonly bool noProxyServer;

    private UwbCefBrowserProcessHandler browserProcessHandler;

    public UwbCefApp(LaunchArguments launchArguments)
    {
        mediaStreamingEnabled = launchArguments.WebRtc;
        noProxyServer = !launchArguments.ProxyEnabled;
    }

    protected override void OnBeforeCommandLineProcessing(string processType, CefCommandLine commandLine)
    {
        if (noProxyServer && !commandLine.HasSwitch("--no-proxy-server"))
            commandLine.AppendSwitch("--no-proxy-server");

        if (mediaStreamingEnabled && !commandLine.HasSwitch("--enable-media-stream"))
            commandLine.AppendSwitch("--enable-media-stream");

#if LINUX || MACOS
        if (!commandLine.HasSwitch("--no-zygote")) commandLine.AppendSwitch("--no-zygote");
#endif
    }

    protected override CefBrowserProcessHandler GetBrowserProcessHandler()
    {
        browserProcessHandler = new UwbCefBrowserProcessHandler();
        return browserProcessHandler;
    }

    protected override CefRenderProcessHandler GetRenderProcessHandler()
    {
        return new UwbCefRenderProcessHandler();
    }

    protected override void Dispose(bool disposing)
    {
        browserProcessHandler.Dispose();
        base.Dispose(disposing);
    }
}