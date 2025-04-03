using MauiBlazorAnalyzer.Core.TaintEngine.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorAnalyzer.Core.TaintEngine.Sanitizers;

public static class SqlSanitizers
{
    public static readonly ITaintSanitizer ParameterizedQuery = new ParameterizedQuerySanitizer();
    public static readonly ITaintSanitizer OrmFramework = new OrmFrameworkSanitizer();

    private class ParameterizedQuerySanitizer : ITaintSanitizer
    {
        public string Name => "ParameterizedQuery";
        public string TaintType => "Sql";

        public bool Matches(string methodSignature) =>
            methodSignature.Contains("SqlCommand.Parameters.Add") ||
            methodSignature.Contains("SqlCommand.Parameters.AddWithValue");
    }

    private class OrmFrameworkSanitizer : ITaintSanitizer
    {
        public string Name => "OrmFramework";
        public string TaintType => "Sql";

        public bool Matches(string methodSignature) =>
            methodSignature.Contains("DbContext.Find") ||
            methodSignature.Contains("DbSet<") && !methodSignature.Contains("FromSqlRaw");
    }
}