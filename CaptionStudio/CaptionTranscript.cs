using System.Text;

namespace CaptionStudio;

internal sealed class CaptionTranscript
{
    private readonly StringBuilder _text = new();
    private string _visible = string.Empty;
    private int _visibleStart;
    private bool _preserveTailOnResume;

    public string Text => _text.ToString();

    public bool Update(string caption)
    {
        caption = caption.Trim().ReplaceLineEndings();
        if (caption.Length == 0 || caption == _visible)
        {
            return false;
        }

        var preserveTail = _preserveTailOnResume;
        _preserveTailOnResume = false;

        if (_visible.Length == 0)
        {
            StartVisibleText();
        }
        else
        {
            ReadOnlySpan<char> previous = _visible;
            ReadOnlySpan<char> next = caption;
            var commonPrefix = previous.CommonPrefixLength(next);
            var slide = FindSlide(previous, next, commonPrefix);

            // Preserve the text that scrolled out. The visible portion can still be corrected.
            _visibleStart += slide;
            if (slide > 0 && previous[slide..].SequenceEqual(next))
            {
                _visible = caption;
                return false;
            }

            if (slide == 0 && commonPrefix < Math.Min(2, Math.Min(previous.Length, next.Length)))
            {
                // A pause separates otherwise unrelated readings, even when the old tail is unfinished.
                var retainedLength = preserveTail ? previous.Length : CompletedTextLength(previous);
                if (retainedLength > 0)
                {
                    _text.Length = _visibleStart + retainedLength;
                    StartVisibleText();
                }
            }
        }

        _text.Length = _visibleStart;
        _text.Append(caption);
        _visible = caption;
        return true;
    }

    public void Pause() => _preserveTailOnResume = true;

    public void EndSegment()
    {
        _visible = string.Empty;
        _preserveTailOnResume = false;
    }

    private void StartVisibleText()
    {
        if (_text.Length > 0 && _text[^1] != '\n')
        {
            _text.AppendLine();
        }

        _visibleStart = _text.Length;
    }

    private static int FindSlide(ReadOnlySpan<char> previous, ReadOnlySpan<char> next, int commonPrefix)
    {
        // If a repeated utterance is both the old head and tail, retain the first utterance.
        var suffixStart = previous.Length - next.Length;
        if (suffixStart > 0 && IsSentenceEnd(previous[suffixStart - 1]) && previous[suffixStart..].SequenceEqual(next))
        {
            return suffixStart;
        }

        if (commonPrefix == Math.Min(previous.Length, next.Length))
        {
            return 0;
        }

        var slide = 0;
        for (var offset = 1; offset <= previous.Length - 2; offset++)
        {
            var remaining = previous[offset..];
            var overlap = remaining.CommonPrefixLength(next);
            var isLineRoll = overlap == remaining.Length && previous[offset - 1] == '\n';
            if (overlap < 2 || overlap < commonPrefix || (overlap == commonPrefix && !isLineRoll))
            {
                continue;
            }

            // A sentence at the new leading edge can keep its prefix while its tail changes.
            if (overlap == remaining.Length || (overlap >= 3 && IsSentenceEnd(previous[offset - 1])))
            {
                slide = offset;
                commonPrefix = overlap;
            }
        }

        return slide;
    }

    private static int CompletedTextLength(ReadOnlySpan<char> text)
    {
        for (var index = text.Length - 1; index >= 0; index--)
        {
            if (IsSentenceEnd(text[index]) && (text[index] != '.' || index == text.Length - 1 || char.IsWhiteSpace(text[index + 1])))
            {
                var end = index + 1;
                while (end < text.Length && text[end] is '」' or '』' or '）' or '】' or '”' or '’')
                {
                    end++;
                }

                return end;
            }
        }

        return 0;
    }

    private static bool IsSentenceEnd(char value) => value is '。' or '！' or '？' or '.' or '!' or '?' or '\n';
}
