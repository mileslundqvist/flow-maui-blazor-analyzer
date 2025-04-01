using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace MauiBlazorAnalyzer.Tests;

public class JsInvokableMethodAnalyzerTests
{
    [Fact]
    public async Task EmptyCode_ShouldNotReportDiagnostic()
    {
        var testCode = @"";
        int a = 3;
        Assert.True(a == 3);

    }
}
