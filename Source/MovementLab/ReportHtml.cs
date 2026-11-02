using Qadr.Core;
using System.Globalization;
using System.Net;
using System.Text;

namespace Qadr.MovementLab;
internal static class ReportHtml
{
    public static string Create(IEnumerable<TrialReport> source)
    {
        var b=new StringBuilder("<!doctype html><meta charset='utf-8'><title>Qadr movement quality</title><style>body{font:16px system-ui;margin:32px;color:#172c41;max-width:1200px}table{border-collapse:collapse;width:100%}td,th{padding:10px;border-bottom:1px solid #ddd;text-align:left}svg{background:#f3f7f8;width:100%;max-height:250px}article{margin:30px 0}small{color:#526575}</style><h1>Qadr movement quality</h1><p>Travel is measured against the instructed straight or diagonal path. Still-hold vibration is measured only after the settling period. These are observations, not a diagnosis or proof of intended input.</p><table><tr><th>Task</th><th>Completed</th><th>Cross-track RMS</th><th>Path efficiency</th><th>Endpoint error</th><th>Hold RMS</th><th>Hold drift</th></tr>");
        b.Replace("<th>Hold drift</th>","<th>Hold drift</th><th>Hold RMS after removing linear drift</th>");
        var trials=source.ToArray();
        string N(double? x)=>x?.ToString("F2",CultureInfo.InvariantCulture)??"n/a";
        foreach(var t in trials)
            b.Append($"<tr><td>{WebUtility.HtmlEncode(t.Id)}</td><td>{t.Completed}</td><td>{N(t.Quality.CrossTrackRmsPx)} px</td><td>{N(t.Quality.PathEfficiency*100)}%</td><td>{N(t.Quality.EndpointErrorPx)} px</td><td>{N(t.Quality.HoldJitterRmsPx)} px</td><td>{N(t.Quality.HoldDriftPx)} px</td><td>{N(t.Quality.HoldDetrendedRmsPx)} px</td></tr>");
        b.Append("</table><p><small>Holds use a fixed duration after settling and retain drift outside the circle. Inspect endpoint error alongside path efficiency: a straight path can still miss its target. Cursor-only reports cannot identify tracking losses. Helper mouse input cannot be distinguished.</small></p>");
        foreach(var t in trials)
        {
            var p=t.Samples.Where(p=>p.Valid&&p.Phase=="travel").ToArray();if(p.Length<2)continue;
            double left=Math.Min(t.StartX,Math.Min(t.TargetX,p.Min(p=>p.X)))-20,top=Math.Min(t.StartY,Math.Min(t.TargetY,p.Min(p=>p.Y)))-20;
            double width=Math.Max(40,Math.Max(t.StartX,Math.Max(t.TargetX,p.Max(p=>p.X)))-left+20),height=Math.Max(60,Math.Max(t.StartY,Math.Max(t.TargetY,p.Max(p=>p.Y)))-top+20);
            string D(double x)=>x.ToString("F2",CultureInfo.InvariantCulture);
            b.Append($"<article><h2>{WebUtility.HtmlEncode(t.Id)} · {WebUtility.HtmlEncode(t.Direction)}</h2><svg viewBox='{D(left)} {D(top)} {D(width)} {D(height)}' role='img' aria-label='Recorded cursor trajectory'><line x1='{D(t.StartX)}' y1='{D(t.StartY)}' x2='{D(t.TargetX)}' y2='{D(t.TargetY)}' stroke='#8e9da9' stroke-dasharray='6 5' stroke-width='2' vector-effect='non-scaling-stroke'/><polyline fill='none' stroke='#c26d0c' stroke-width='2' vector-effect='non-scaling-stroke' points='");
            foreach(var row in p)b.Append(D(row.X)+","+D(row.Y)+" ");
            b.Append("'/></svg></article>");
        }
        return b.ToString();
    }
}
