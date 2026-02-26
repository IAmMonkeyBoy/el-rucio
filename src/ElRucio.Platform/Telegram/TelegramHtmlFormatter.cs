using System.Text;
using System.Text.RegularExpressions;

namespace ElRucio.Platform.Telegram;

public static class TelegramHtmlFormatter
{
    public static string ToSafeHtml(string text)
    {
        var encoded = System.Net.WebUtility.HtmlEncode(text);
        encoded = Regex.Replace(encoded, "\\*\\*(.+?)\\*\\*", "<b>$1</b>");
        encoded = Regex.Replace(encoded, "\\*(.+?)\\*", "<i>$1</i>");
        encoded = Regex.Replace(encoded, "`(.+?)`", "<code>$1</code>");
        return encoded;
    }

    public static List<string> SplitForTelegram(string text, int maxChars)
    {
        if (text.Length <= maxChars)
        {
            return [text];
        }

        var lines = text.Split('\n');
        var chunks = new List<string>();
        var builder = new StringBuilder();

        foreach (var line in lines)
        {
            var candidateLength = builder.Length + line.Length + 1;
            if (candidateLength > maxChars && builder.Length > 0)
            {
                chunks.Add(builder.ToString());
                builder.Clear();
            }

            if (line.Length > maxChars)
            {
                var i = 0;
                while (i < line.Length)
                {
                    var take = Math.Min(maxChars, line.Length - i);
                    chunks.Add(line.Substring(i, take));
                    i += take;
                }

                continue;
            }

            builder.AppendLine(line);
        }

        if (builder.Length > 0)
        {
            chunks.Add(builder.ToString());
        }

        return chunks;
    }
}
