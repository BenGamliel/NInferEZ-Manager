using System.Globalization;
using System.Text.RegularExpressions;
using NInferManager.Contracts;

namespace NInferManager.Backend;

public static partial class RequestMetricsParser
{
    [GeneratedRegex(@"^\[(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})\].*?\breq#(?<id>\d+)\s+(?<body>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex LinePattern();
    public static RequestMetricsSnapshot Parse(string text)
    {
        var all = new List<RequestMetric>();
        var active = new Dictionary<int, (DateTime time, string type)>();
        foreach (var line in text.Split(['\r','\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var m = LinePattern().Match(line); if (!m.Success || !int.TryParse(m.Groups["id"].Value, out var id)) continue;
            var body = m.Groups["body"].Value.Trim();
            var time = DateTime.TryParseExact(m.Groups["time"].Value, "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : DateTime.Now;
            if (body.StartsWith("started", StringComparison.OrdinalIgnoreCase)) { var pieces = body.Split('|', StringSplitOptions.TrimEntries); active[id] = (time, pieces.Length > 1 ? pieces[1].Split(' ')[0] : "Request"); continue; }
            if (!active.TryGetValue(id, out var start)) continue;
            if (body.StartsWith("done", StringComparison.OrdinalIgnoreCase))
            {
                var metric = new RequestMetric(start.time, id, start.type, "Completed", Int(body,"prompt"), Int(body,"output"), Int(body,"cache"), Duration(body,"TTFT",true), Rate(body,"prefill"), Rate(body,"decode"), Duration(body,"total",false), Spec(body));
                all.Add(metric); active.Remove(id);
            }
            else if (body.Contains("fail", StringComparison.OrdinalIgnoreCase) || body.Contains("error", StringComparison.OrdinalIgnoreCase)) { all.Add(new(start.time,id,start.type,"Failed",0,0,0,null,null,null,null,"—")); active.Remove(id); }
        }
        all.AddRange(active.Select(x => new RequestMetric(x.Value.time,x.Key,x.Value.type,"Running",0,0,0,null,null,null,null,"—")));
        var completed = all.Where(x => x.Status == "Completed").ToList();
        var decode = completed.Where(x => x.DecodeTokensPerSecond > 0).ToList();
        var seconds = decode.Sum(x => x.GeneratedTokens / x.DecodeTokensPerSecond!.Value);
        return new(all.OrderByDescending(x=>x.StartedAt).Take(250).ToList(), completed.Count, all.Count(x=>x.Status=="Failed"), completed.Sum(x=>x.PromptTokens+x.GeneratedTokens), seconds>0?decode.Sum(x=>x.GeneratedTokens)/seconds:null, completed.Where(x=>x.TtftMs is not null).Select(x=>x.TtftMs!.Value).DefaultIfEmpty().Average() is var avg && avg>0?avg:null);
    }
    private static int Int(string s,string n) { var m=Regex.Match(s,$@"(?:^|\|\s*){n}\s+(?<v>[\d,]+)",RegexOptions.IgnoreCase); return m.Success?int.Parse(m.Groups["v"].Value.Replace(",",""),CultureInfo.InvariantCulture):0; }
    private static double? Rate(string s,string n) { var m=Regex.Match(s,$@"(?:^|\|\s*){n}\s+(?<v>\d+(?:\.\d+)?)(?<k>[kKmM]?)\s+tok/s",RegexOptions.IgnoreCase); if(!m.Success)return null; var v=double.Parse(m.Groups["v"].Value,CultureInfo.InvariantCulture); return m.Groups["k"].Value.ToLowerInvariant() switch {"k"=>v*1000,"m"=>v*1000000,_=>v}; }
    private static double? Duration(string s,string n,bool ms) { var m=Regex.Match(s,$@"(?:^|\|\s*){n}\s+(?<v>\d+(?:\.\d+)?)\s*(?<u>ms|s)",RegexOptions.IgnoreCase); if(!m.Success)return null; var v=double.Parse(m.Groups["v"].Value,CultureInfo.InvariantCulture); return ms?(m.Groups["u"].Value.Equals("s",StringComparison.OrdinalIgnoreCase)?v*1000:v):(m.Groups["u"].Value.Equals("ms",StringComparison.OrdinalIgnoreCase)?v/1000:v); }
    private static string Spec(string s) { var m=Regex.Match(s,@"(?:^|\|\s*)(?<mode>\S+)\s+accepted\s+[\d,]+/[\d,]+\s+\((?<rate>\d+(?:\.\d+)?)%\)",RegexOptions.IgnoreCase); return m.Success?$"{m.Groups["mode"].Value.ToUpperInvariant()} · {m.Groups["rate"].Value}%":"—"; }
}

