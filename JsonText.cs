using System;
using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace DesktopHtmlHost
{
    // Messages posted to the page, written with Utf8JsonWriter: escaping and number formatting
    // come from System.Text.Json instead of hand-built strings. Accented text stays readable.
    internal static class JsonText
    {
        static readonly JsonWriterOptions Options = new JsonWriterOptions { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };
        [ThreadStatic] static ArrayBufferWriter<byte>? buffer;

        internal static string Write(Action<Utf8JsonWriter> body)
        {
            var output = buffer ??= new ArrayBufferWriter<byte>(8192);
            output.ResetWrittenCount();
            using (var writer = new Utf8JsonWriter(output, Options)) body(writer);
            return Encoding.UTF8.GetString(output.WrittenSpan);
        }
    }
}
