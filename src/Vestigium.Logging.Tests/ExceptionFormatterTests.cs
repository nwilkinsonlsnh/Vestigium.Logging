namespace Vestigium.Logging.Tests;

public sealed class ExceptionFormatterTests
{
    [Fact]
    public void NullExceptionIsNull()
    {
        Assert.Null(VestigiumExceptionFormatter.Format(null, VestigiumExceptionDetail.Full, 100));
        Assert.Null(VestigiumExceptionFormatter.Format(new Exception("x"), VestigiumExceptionDetail.None, 100));
    }

    [Fact]
    public void TypeAndMessageWithoutInnerHasNoArrow()
    {
        var text = VestigiumExceptionFormatter.Format(
            new InvalidOperationException("solo"),
            VestigiumExceptionDetail.TypeAndMessage,
            0);
        Assert.Equal("System.InvalidOperationException: solo", text);
        Assert.DoesNotContain("--->", text);
        Assert.DoesNotContain("at ", text);
    }

    [Fact]
    public void ZeroMaxCharsUsesHardCapNotUnlimitedBeyond64KiB()
    {
        var text = VestigiumExceptionFormatter.Format(
            new Exception("short"),
            VestigiumExceptionDetail.Full,
            maxChars: 0);
        Assert.NotNull(text);
        Assert.True(text!.Length <= VestigiumExceptionFormatter.HardMaxChars);
        Assert.Contains("short", text);
    }

    [Fact]
    public void HardCapBeatsSofterLargerMax()
    {
        var huge = new Exception(new string('x', VestigiumExceptionFormatter.HardMaxChars + 50));
        var text = VestigiumExceptionFormatter.Format(huge, VestigiumExceptionDetail.TypeAndMessage, 200_000)!;
        Assert.Equal(VestigiumExceptionFormatter.HardMaxChars, text.Length);
    }
}
