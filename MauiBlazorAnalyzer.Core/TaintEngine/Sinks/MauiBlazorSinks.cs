using MauiBlazorAnalyzer.Core.TaintEngine.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorAnalyzer.Core.TaintEngine.Sinks;

public static class MauiBlazorSinks
{
    // JavaScript Interop sinks - where untrusted data could be executed as JS
    public static readonly ITaintSink JsInvoke = new JsInvokeSink();
    public static readonly ITaintSink JsInvokeAsync = new JsInvokeAsyncSink();
    public static readonly ITaintSink JsEval = new JsEvalSink();

    // WebView injection sinks
    public static readonly ITaintSink WebViewSource = new WebViewSourceSink();
    public static readonly ITaintSink WebViewNavigate = new WebViewNavigateSink();
    public static readonly ITaintSink HtmlInjection = new HtmlInjectionSink();

    // Local storage access
    public static readonly ITaintSink LocalStorage = new LocalStorageSink();

    // File system operations
    public static readonly ITaintSink FileSystemWrite = new FileSystemWriteSink();

    // Platform-specific invocations
    public static readonly ITaintSink PlatformInvoke = new PlatformInvokeSink();

    // Navigation sinks
    public static readonly ITaintSink NavigationToUri = new NavigationToUriSink();

    // Implementation details
    private class JsInvokeSink : ITaintSink
    {
        public string Name => "MauiBlazorJsInvoke";
        public string Severity => "High";

        public bool Matches(string methodSignature) =>
            methodSignature.Contains("IJSRuntime.InvokeVoid") ||
            methodSignature.Contains("IJSRuntime.Invoke<") ||
            methodSignature.Contains("JSRuntime.InvokeVoid") ||
            methodSignature.Contains("JSRuntime.Invoke<");
    }

    private class JsInvokeAsyncSink : ITaintSink
    {
        public string Name => "MauiBlazorJsInvokeAsync";
        public string Severity => "High";

        public bool Matches(string methodSignature) =>
            methodSignature.Contains("IJSRuntime.InvokeVoidAsync") ||
            methodSignature.Contains("IJSRuntime.InvokeAsync<") ||
            methodSignature.Contains("JSRuntime.InvokeVoidAsync") ||
            methodSignature.Contains("JSRuntime.InvokeAsync<");
    }

    private class JsEvalSink : ITaintSink
    {
        public string Name => "MauiBlazorJsEval";
        public string Severity => "Critical";

        public bool Matches(string methodSignature) =>
            (methodSignature.Contains("IJSRuntime") || methodSignature.Contains("JSRuntime")) &&
            methodSignature.Contains("eval") || methodSignature.Contains("ExecuteScript");
    }

    private class WebViewSourceSink : ITaintSink
    {
        public string Name => "MauiBlazorWebViewSource";
        public string Severity => "High";

        public bool Matches(string methodSignature) =>
            methodSignature.Contains("WebView.Source") ||
            methodSignature.Contains("set_Source") && methodSignature.Contains("WebView");
    }

    private class WebViewNavigateSink : ITaintSink
    {
        public string Name => "MauiBlazorWebViewNavigate";
        public string Severity => "High";

        public bool Matches(string methodSignature) =>
            methodSignature.Contains("WebView.Navigate") ||
            methodSignature.Contains("WebView.GoTo") ||
            methodSignature.Contains("WebView.LoadUrl");
    }

    private class HtmlInjectionSink : ITaintSink
    {
        public string Name => "MauiBlazorHtmlInjection";
        public string Severity => "High";

        public bool Matches(string methodSignature) =>
            methodSignature.Contains("MarkupString") ||
            methodSignature.Contains("RenderFragment") ||
            (methodSignature.Contains("Html") && methodSignature.Contains("Raw"));
    }

    private class LocalStorageSink : ITaintSink
    {
        public string Name => "MauiBlazorLocalStorage";
        public string Severity => "Medium";

        public bool Matches(string methodSignature) =>
            methodSignature.Contains("Blazored.LocalStorage") ||
            methodSignature.Contains("ILocalStorageService") ||
            methodSignature.Contains("LocalStorage.SetItem") ||
            methodSignature.Contains("Preferences.Set");
    }

    private class FileSystemWriteSink : ITaintSink
    {
        public string Name => "MauiBlazorFileSystemWrite";
        public string Severity => "High";

        public bool Matches(string methodSignature) =>
            (methodSignature.Contains("FileSystem") || methodSignature.Contains("IFileSystem")) &&
            (methodSignature.Contains("WriteAllText") ||
             methodSignature.Contains("WriteAllBytes") ||
             methodSignature.Contains("OpenWrite") ||
             methodSignature.Contains("SaveAs"));
    }

    private class PlatformInvokeSink : ITaintSink
    {
        public string Name => "MauiBlazorPlatformInvoke";
        public string Severity => "High";

        public bool Matches(string methodSignature) =>
            methodSignature.Contains("DeviceInfo.Platform") ||
            methodSignature.Contains("DependencyService.Get") ||
            methodSignature.Contains("IPlatformSpecificService");
    }

    private class NavigationToUriSink : ITaintSink
    {
        public string Name => "MauiBlazorNavigation";
        public string Severity => "Medium";

        public bool Matches(string methodSignature) =>
            methodSignature.Contains("NavigationManager.NavigateTo") ||
            methodSignature.Contains("Shell.Current.GoToAsync") ||
            methodSignature.Contains("Launcher.OpenAsync");
    }
}
