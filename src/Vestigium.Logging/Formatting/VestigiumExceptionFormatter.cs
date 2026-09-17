using System.Text;

namespace Vestigium.Logging;

internal static class VestigiumExceptionFormatter
{
    public const int HardMaxChars = 64 * 1024;

    public static string? Format(Exception? exception, VestigiumExceptionDetail detail, int maxChars)
    {
        if (exception is null || detail == VestigiumExceptionDetail.None)
            return null;

        var text = detail == VestigiumExceptionDetail.TypeAndMessage
            ? TypeAndMessage(exception)
            : exception.ToString();

        var cap = maxChars <= 0 ? HardMaxChars : Math.Min(maxChars, HardMaxChars);
        return text.Length <= cap ? text : text[..cap];
    }

    private static string TypeAndMessage(Exception exception)
    {
        var sb = new StringBuilder();
        var current = exception;
        var first = true;
        while (current is not null)
        {
            if (!first)
                sb.Append(" ---> ");
            sb.Append(current.GetType().FullName);
            sb.Append(": ");
            sb.Append(current.Message);
            first = false;
            current = current.InnerException;
        }

        return sb.ToString();
    }
}
