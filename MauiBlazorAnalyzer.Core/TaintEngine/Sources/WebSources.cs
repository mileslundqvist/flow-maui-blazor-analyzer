using MauiBlazorAnalyzer.Core.TaintEngine.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorAnalyzer.Core.TaintEngine.Sources;

public static class WebSources
{
    public static readonly ITaintSource RequestQuery = new RequestQuerySource();
    public static readonly ITaintSource RequestBody = new RequestBodySource();
    public static readonly ITaintSource RequestHeader = new RequestHeaderSource();
    public static readonly ITaintSource HttpRequest = new HttpRequestSource();

    private class RequestQuerySource : ITaintSource
    {
        public string Name => "RequestQuery";

        public bool Matches(string methodSignature) =>
            methodSignature.Contains("Request.Query") ||
            methodSignature.Contains("Request.QueryString");
    }

    private class RequestBodySource : ITaintSource
    {
        public string Name => "RequestBody";

        public bool Matches(string methodSignature) =>
            methodSignature.Contains("Request.Body") ||
            methodSignature.Contains("Request.Form");
    }

    private class RequestHeaderSource : ITaintSource
    {
        public string Name => "RequestHeader";

        public bool Matches(string methodSignature) =>
            methodSignature.Contains("Request.Headers");
    }

    private class HttpRequestSource : ITaintSource
    {
        public string Name => "HttpRequest";

        public bool Matches(string methodSignature) =>
            methodSignature.Contains("HttpContext.Request");
    }
}
