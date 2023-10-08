namespace LvStreamStore;

using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;

public class LocalStorageEventStream : EventStream {
    private readonly LocalStorageEventStreamOptions _options;
    private readonly string _dataFile;

    public LocalStorageEventStream(IOptions<EventStreamOptions> options) : base(options) {
        _options = options.Value as LocalStorageEventStreamOptions ?? new LocalStorageEventStreamOptions();
        _dataFile = Path.Combine(_options.BaseDataPath, "chunk.dat");

        if (!Directory.Exists(_options.BaseDataPath)) {
            Directory.CreateDirectory(_options.BaseDataPath);
        }

        if (!File.Exists(_dataFile)) {
            File.Create(_dataFile).Dispose();
        }
    }

    protected override async IAsyncEnumerable<StreamItem> ReadAsync() {
        var buffer = new byte[4096];
        var ms = new MemoryStream();

        using (var fStream = new FileStream(_dataFile, new FileStreamOptions { Access = FileAccess.Read, Mode = FileMode.Open, Options = FileOptions.Asynchronous, Share = FileShare.ReadWrite })) {
            int offset;
            do {
                Array.Clear(buffer);
                offset = await fStream.ReadAsync(buffer, 0, buffer.Length);

                for (var idx = 0; idx < offset; idx++) {
                    if (buffer[idx] == Constants.NULL) break; // if null, then no further data exists.

                    if (buffer[idx] == Constants.EndOfRecord) { // found a point whereas we need to deserialize what we have in the buffer, yield it back to the caller, then advance the index by 1.
                        ms.Seek(0, SeekOrigin.Begin);

                        yield return JsonSerializer.Deserialize<StreamItem>(ms, _options.JsonOptions)!;

                        ms?.Dispose();
                        ms = new MemoryStream();

                        continue;
                    }

                    ms.WriteByte(buffer[idx]);
                }
            } while (offset != 0);
        }

        if (ms.Length > 0) {
            yield return JsonSerializer.Deserialize<StreamItem>(ms, _options.JsonOptions)!;
        }
    }

    protected override async Task WriteAsync(byte[] data) {
        var endOfData = data.Length;

        for (var i = 0; i < data.Length; i++) {
            if (data[i] == 0x00) {
                endOfData = i;
                break;
            }
        }

        using (var fileWriter = new FileStream(_dataFile, new FileStreamOptions { Access = FileAccess.Write, Mode = FileMode.Append, Options = FileOptions.Asynchronous, Share = FileShare.Read })) {
            fileWriter.Seek(0, SeekOrigin.End);
            await fileWriter.WriteAsync(data, 0, data.Length);
            await fileWriter.FlushAsync();
        }

        Checkpoint += endOfData;
    }

    bool _disposed = false;
    protected override void Dispose(bool disposing) {
        base.Dispose(disposing);
        if (_disposed || !disposing) return;

        _disposed = true;
    }
}
