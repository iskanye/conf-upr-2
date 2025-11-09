using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace ConfigUpr2
{
    public static class MermaidVisualizer
    {
        // Generate Mermaid graph text for a directed graph (adjacency list). Focused on readability.
        public static string GenerateMermaid(Dictionary<string, List<string>> adjacency)
        {
            var sb = new StringBuilder();
            sb.AppendLine("graph");

            foreach (var kv in adjacency)
            {
                var parent = kv.Key;
                foreach (var child in kv.Value)
                {
                    sb.AppendLine($"    {Escape(parent)} --> {Escape(child)}");
                }
            }

            return sb.ToString();
        }

        static string Escape(string s)
        {
            if (s == null) 
                return "";
            return s.Replace("\"", "\\\"").Replace("@", "");
        }

        public static async Task WriteHtmlAndOpenAsync(string mermaidSource, string nameHint)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!doctype html>");
            sb.AppendLine("<html>");
            sb.AppendLine("<head>");
            sb.AppendLine("<meta charset=\"utf-8\" />");
            sb.AppendLine("<title>Mermaid Diagram - " + System.Net.WebUtility.HtmlEncode(nameHint) + "</title>");
            sb.AppendLine("<script type=\"module\" src=\"https://cdn.jsdelivr.net/npm/mermaid@10/dist/mermaid.esm.min.mjs\"></script>");
            sb.AppendLine("<script>");
            sb.AppendLine("mermaid.initialize({ startOnLoad: true });");
            sb.AppendLine("</script>");
            sb.AppendLine("<style>body { width:200%; }</style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine("<div class=\"mermaid\">");
            sb.AppendLine(mermaidSource);
            sb.AppendLine("</div>");
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");

            var tmp = Path.Combine(Path.GetTempPath(), $"mermaid_{Guid.NewGuid():N}.html");
            await File.WriteAllTextAsync(tmp, sb.ToString(), Encoding.UTF8);

            // Open in default browser
            try
            {
                var psi = new ProcessStartInfo(tmp) { UseShellExecute = true };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Failed to open browser: " + ex.Message);
                Console.WriteLine("HTML output written to: " + tmp);
            }
        }
    }
}
