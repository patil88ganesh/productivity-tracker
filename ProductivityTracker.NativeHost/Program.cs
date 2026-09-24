using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Web.Script.Serialization;

namespace ProductivityTracker.NativeHost;

internal static class Program
{
    private const string PipeName = "ProductivityTracker.SocialMediaPause";
    private const int MaximumMessageLength = 1024 * 1024;

    private static readonly JavaScriptSerializer Serializer = new();
    private static NamedPipeClientStream pipe;

    private static int Main()
    {
        var input = Console.OpenStandardInput();
        var output = Console.OpenStandardOutput();

        try
        {
            while (TryReadMessage(input, out var message))
            {
                var request = Serializer.Deserialize<BrowserState>(message);
                var visitToken = request?.visitToken as string;
                if (visitToken?.Length > 64)
                {
                    visitToken = null;
                }
                var site = NormalizeSite(request);
                var connected = SendToApplication(site);
                WriteMessage(
                    output,
                    Serializer.Serialize(new
                    {
                        ok = true,
                        active = site != BrowserSite.None,
                        site = ToMessageValue(site),
                        visitToken,
                        appConnected = connected,
                    }));
            }

            SendToApplication(BrowserSite.None);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        finally
        {
            pipe?.Dispose();
        }
    }

    private static bool TryReadMessage(Stream input, out string message)
    {
        message = null;
        var lengthBytes = new byte[4];
        var firstByte = input.ReadByte();
        if (firstByte < 0)
        {
            return false;
        }

        lengthBytes[0] = (byte)firstByte;
        ReadExactly(input, lengthBytes, 1, 3);
        var length = BitConverter.ToInt32(lengthBytes, 0);
        if (length <= 0 || length > MaximumMessageLength)
        {
            throw new InvalidDataException("Native message length is invalid.");
        }

        var payload = new byte[length];
        ReadExactly(input, payload, 0, length);
        message = Encoding.UTF8.GetString(payload);
        return true;
    }

    private static void WriteMessage(Stream output, string message)
    {
        var payload = Encoding.UTF8.GetBytes(message);
        var length = BitConverter.GetBytes(payload.Length);
        output.Write(length, 0, length.Length);
        output.Write(payload, 0, payload.Length);
        output.Flush();
    }

    private static void ReadExactly(
        Stream input,
        byte[] buffer,
        int offset,
        int count)
    {
        while (count > 0)
        {
            var bytesRead = input.Read(buffer, offset, count);
            if (bytesRead == 0)
            {
                throw new EndOfStreamException("Native message ended unexpectedly.");
            }

            offset += bytesRead;
            count -= bytesRead;
        }
    }

    private static BrowserSite NormalizeSite(BrowserState request)
    {
        if (request == null || !request.active)
        {
            return BrowserSite.None;
        }

        return string.Equals(
            request.site,
            "youtube",
            StringComparison.OrdinalIgnoreCase)
                ? BrowserSite.YouTube
                : string.Equals(
                    request.site,
                    "other",
                    StringComparison.OrdinalIgnoreCase)
                    ? BrowserSite.Other
                    : BrowserSite.Unknown;
    }

    private static string ToMessageValue(BrowserSite site)
    {
        switch (site)
        {
            case BrowserSite.YouTube:
                return "youtube";
            case BrowserSite.Other:
                return "other";
            case BrowserSite.Unknown:
                return "unknown";
            default:
                return "none";
        }
    }

    private static bool SendToApplication(BrowserSite site)
    {
        if (pipe == null || !pipe.IsConnected)
        {
            pipe?.Dispose();
            pipe = TryConnect();
        }

        if (pipe == null)
        {
            return false;
        }

        try
        {
            var signal = Encoding.ASCII.GetBytes(
                site == BrowserSite.YouTube ? "y\n" :
                site == BrowserSite.Other ? "1\n" :
                site == BrowserSite.Unknown ? "u\n" :
                "0\n");
            pipe.Write(signal, 0, signal.Length);
            pipe.Flush();
            return true;
        }
        catch (IOException)
        {
            pipe.Dispose();
            pipe = null;
            return false;
        }
    }

    private static NamedPipeClientStream TryConnect()
    {
        var candidate = new NamedPipeClientStream(
            ".",
            PipeName,
            PipeDirection.Out,
            PipeOptions.Asynchronous);
        try
        {
            candidate.Connect(750);
            return candidate;
        }
        catch (TimeoutException)
        {
            candidate.Dispose();
            return null;
        }
        catch (IOException)
        {
            candidate.Dispose();
            return null;
        }
    }

    private sealed class BrowserState
    {
        public bool active { get; set; }

        public string site { get; set; }

        public object visitToken { get; set; }
    }

    private enum BrowserSite
    {
        None,
        YouTube,
        Other,
        Unknown,
    }
}
