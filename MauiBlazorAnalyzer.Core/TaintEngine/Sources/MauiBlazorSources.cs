using MauiBlazorAnalyzer.Core.TaintEngine.Interfaces;

namespace MauiBlazorAnalyzer.Core.TaintEngine.Sources;

public static class MauiBlazorSources
{
    public static readonly ITaintSource UrlParameters = new UrlParametersSource();
    public static readonly ITaintSource JsInteropInput = new JsInteropInputSource();
    public static readonly ITaintSource BlazorComponentParameters = new BlazorComponentParametersSource();
    public static readonly ITaintSource PlatformSpecificData = new PlatformSpecificDataSource();

    private class UrlParametersSource : ITaintSource
    {
        public string Name => "MauiBlazorUrlParameters";

        public bool Matches(string methodSignature) =>
            methodSignature.Contains("NavigationManager.Uri") ||
            methodSignature.Contains("NavigationManager.GetUriWithQueryParameter") ||
            methodSignature.Contains("QueryParameterValue");
    }

    private class JsInteropInputSource : ITaintSource
    {
        public string Name => "MauiBlazorJsInteropInput";

        public bool Matches(string methodSignature) =>
            methodSignature.Contains("JSInvokable") ||
            methodSignature.Contains("IJSRuntime.InvokeAsync<") && !methodSignature.Contains("void");
    }

    private class BlazorComponentParametersSource : ITaintSource
    {
        public string Name => "MauiBlazorComponentParameters";

        public bool Matches(string methodSignature) =>
            methodSignature.Contains("[Parameter]") ||
            methodSignature.Contains("ParameterView.TryGetValue") ||
            methodSignature.Contains("SupplyParameterFromQuery");
    }

    private class PlatformSpecificDataSource : ITaintSource
    {
        public string Name => "MauiBlazorPlatformData";

        public bool Matches(string methodSignature) =>
            methodSignature.Contains("DeviceInfo.") ||
            methodSignature.Contains("Preferences.Get") ||
            methodSignature.Contains("SecureStorage.GetAsync");
    }
}
