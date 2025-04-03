using MauiBlazorAnalyzer.Core.TaintEngine.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorAnalyzer.Core.TaintEngine.Sinks;

public static class SqlSinks
{
    public static readonly ITaintSink SqlCommand = new SqlCommandSink();
    public static readonly ITaintSink EntityFramework = new EntityFrameworkSink();
    public static readonly ITaintSink Dapper = new DapperSink();

    private class SqlCommandSink : ITaintSink
    {
        public string Name => "SqlCommand";
        public string Severity => "High";

        public bool Matches(string methodSignature) =>
            methodSignature.Contains("SqlCommand") &&
            (methodSignature.Contains("ExecuteNonQuery") ||
             methodSignature.Contains("ExecuteReader") ||
             methodSignature.Contains("ExecuteScalar"));
    }

    private class EntityFrameworkSink : ITaintSink
    {
        public string Name => "EntityFramework";
        public string Severity => "High";

        public bool Matches(string methodSignature) =>
            methodSignature.Contains("DbContext.Database.ExecuteSqlRaw") ||
            methodSignature.Contains("DbSet<") && methodSignature.Contains("FromSqlRaw");
    }

    private class DapperSink : ITaintSink
    {
        public string Name => "Dapper";
        public string Severity => "High";

        public bool Matches(string methodSignature) =>
            methodSignature.Contains("Dapper.SqlMapper.Query") ||
            methodSignature.Contains("Dapper.SqlMapper.Execute");
    }
}
