using Xunit;

namespace CaptionStudio.Tests;

public sealed class CaptionTranscriptTests
{
    [Fact]
    public void ReplacesAnInterimReadingAndPunctuation()
    {
        var transcript = new CaptionTranscript();

        Assert.True(transcript.Update("きしゃ"));
        Assert.True(transcript.Update("記者が来ました"));
        Assert.True(transcript.Update("記者が来ます。"));

        Assert.Equal("記者が来ます。", transcript.Text);
    }

    [Fact]
    public void KeepsTextThatScrollsAwayWhileTheTailIsRewritten()
    {
        var transcript = new CaptionTranscript();

        transcript.Update("先ほどの話。昨日はあめで");
        transcript.Update("昨日は雨でした。今日は");
        transcript.Update("雨でした。今日は晴れです。");

        Assert.Equal("先ほどの話。昨日は雨でした。今日は晴れです。", transcript.Text);
    }

    [Fact]
    public void AViewportShrinkDoesNotDuplicateOrLoseText()
    {
        var transcript = new CaptionTranscript();

        transcript.Update("先ほどの話。次の話です。");
        Assert.False(transcript.Update("次の話です。"));
        transcript.Update("次の話です。続きます。");

        Assert.Equal("先ほどの話。次の話です。続きます。", transcript.Text);
    }

    [Fact]
    public void KeepsTheFirstLineWhenTheNextLineStartsWithTheSameWords()
    {
        var transcript = new CaptionTranscript();

        transcript.Update("これは一つ目です。\nこれは");
        transcript.Update("これは二つ目です。\n次に進みます。");

        Assert.Equal($"これは一つ目です。{Environment.NewLine}これは二つ目です。{Environment.NewLine}次に進みます。", transcript.Text);
    }

    [Fact]
    public void IgnoresAnUnchangedSampleButKeepsRepeatedSpeech()
    {
        var transcript = new CaptionTranscript();

        transcript.Update("はい。");
        Assert.False(transcript.Update(" はい。 "));
        Assert.False(transcript.Update("\r\n"));
        Assert.False(transcript.Update("はい。"));
        Assert.True(transcript.Update("はい。はい。"));

        Assert.Equal("はい。はい。", transcript.Text);
    }

    [Fact]
    public void KeepsRepeatedUtterancesWhenTheFirstScrollsAway()
    {
        var transcript = new CaptionTranscript();

        transcript.Update("はい。\nはい。");
        Assert.False(transcript.Update("はい。"));
        transcript.Update("はい。\n次に進みます。");

        Assert.Equal($"はい。{Environment.NewLine}はい。{Environment.NewLine}次に進みます。", transcript.Text);
    }

    [Fact]
    public void KeepsCompletedSentencesWhenTheNextSnapshotHasNoOverlap()
    {
        var transcript = new CaptionTranscript();

        transcript.Update("「最初の発言です。」きょうは");
        transcript.Update("今日は晴れです。");

        Assert.Equal($"「最初の発言です。」{Environment.NewLine}今日は晴れです。", transcript.Text);
    }

    [Fact]
    public void EndsAnIndependentSegmentWithoutLosingItsUnfinishedTail()
    {
        var transcript = new CaptionTranscript();

        transcript.Update("こんにちは");
        transcript.EndSegment();
        Assert.Equal("こんにちは", transcript.Text);

        transcript.Update("こんにちは");

        Assert.Equal($"こんにちは{Environment.NewLine}こんにちは", transcript.Text);
    }

    [Fact]
    public void KeepsAnUnfinishedTailWhenTheResumedSnapshotHasNoOverlap()
    {
        var transcript = new CaptionTranscript();

        transcript.Update("最後に確認すること");
        transcript.Pause();
        Assert.False(transcript.Update("最後に確認すること"));
        transcript.Update("続きから話します");

        Assert.Equal($"最後に確認すること{Environment.NewLine}続きから話します", transcript.Text);
    }
}
