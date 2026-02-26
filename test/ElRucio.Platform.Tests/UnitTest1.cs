using ElRucio.Platform.Telegram;

namespace ElRucio.Platform.Tests;

public class UnitTest1
{
    [Fact]
    public void ToSafeHtml_EncodesAndFormatsBasicMarkdown()
    {
        var input = "**bold** and *italic* with `code`";

        var result = TelegramHtmlFormatter.ToSafeHtml(input);

        Assert.Contains("<b>bold</b>", result);
        Assert.Contains("<i>italic</i>", result);
        Assert.Contains("<code>code</code>", result);
    }

    [Fact]
    public void SplitForTelegram_SplitsLongText()
    {
        var input = new string('a', 9000);

        var chunks = TelegramHtmlFormatter.SplitForTelegram(input, 4096);

        Assert.True(chunks.Count >= 3);
        Assert.All(chunks, chunk => Assert.True(chunk.Length <= 4096));
    }
}
