using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace BusTracker.Api.Shapes;

public static class GeoJsonFeatureReader
{
    private const string FeaturesKey = "features";

    public static async IAsyncEnumerable<JsonDocument> ReadFeaturesAsync(
        Stream stream, [EnumeratorCancellation] CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1 << 16);
        var scanner = new FeatureScanner();
        var buffer = new char[1 << 16];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            foreach (var feature in scanner.Feed(buffer.AsSpan(0, read)))
                yield return JsonDocument.Parse(feature);
        }
        scanner.EnsureComplete();
    }

    private sealed class FeatureScanner
    {
        private readonly StringBuilder _feature = new();
        private readonly StringBuilder _rootKey = new();
        private readonly List<string> _completed = [];
        private int _depth;
        private bool _inString;
        private bool _escaped;
        private bool _inFeatures;
        private string _lastRootKey = "";

        public List<string> Feed(ReadOnlySpan<char> chunk)
        {
            _completed.Clear();
            foreach (var c in chunk) Accept(c);
            return _completed;
        }

        private bool Capturing => _inFeatures && _depth >= 3;

        public void EnsureComplete()
        {
            if (_depth != 0 || _inString)
                throw new InvalidDataException("GeoJSON ended before the document was complete");
        }

        private void Accept(char c)
        {
            var wasCapturing = Capturing;
            if (_inString) AcceptInString(c);
            else AcceptStructural(c);

            if (wasCapturing || Capturing) _feature.Append(c);
            if (wasCapturing && !Capturing) Complete();
        }

        private void AcceptInString(char c)
        {
            if (_escaped) { _escaped = false; RecordRootKey(c); return; }
            if (c == '\\') { _escaped = true; RecordRootKey(c); return; }
            if (c == '"') { _inString = false; FinishRootKey(); return; }
            RecordRootKey(c);
        }

        private void AcceptStructural(char c)
        {
            switch (c)
            {
                case '"':
                    _inString = true;
                    if (_depth == 1) _rootKey.Clear();
                    break;
                case '{' or '[':
                    if (c == '[' && _depth == 1 && _lastRootKey == FeaturesKey) _inFeatures = true;
                    _depth++;
                    break;
                case '}' or ']':
                    _depth--;
                    if (_depth == 1) _inFeatures = false;
                    break;
            }
        }

        private void RecordRootKey(char c)
        {
            if (_depth == 1) _rootKey.Append(c);
        }

        private void FinishRootKey()
        {
            if (_depth == 1) _lastRootKey = _rootKey.ToString();
        }

        private void Complete()
        {
            var text = _feature.ToString().Trim().TrimEnd(',');
            _feature.Clear();
            if (text.StartsWith('{')) _completed.Add(text);
        }
    }
}
