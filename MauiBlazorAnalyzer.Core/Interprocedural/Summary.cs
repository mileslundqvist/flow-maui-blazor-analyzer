using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorAnalyzer.Core.Interprocedural;
public class Summary<TFact>
{
    private readonly Dictionary<TFact, HashSet<TFact>> _mapping = new Dictionary<TFact, HashSet<TFact>>();

    public void AddMapping(TFact input, TFact output)
    {
        if (!_mapping.TryGetValue(input, out var outputs))
        {
            outputs = new HashSet<TFact>();
            _mapping[input] = outputs;
        }
        outputs.Add(output);
    }

    public IEnumerable<TFact> Apply(TFact input)
    {
        return _mapping.TryGetValue(input, out var outputs) ? outputs : new List<TFact> { input };
    }
}
