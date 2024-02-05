namespace LvStreamStore.LocalStorage.Tests;

using FakeItEasy;

using LvStreamStore.Serialization.Json;
using LvStreamStore.Tests;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public class LocalStorageEventStreamTestsWithCaching : ClientTestBase {
    private EventStream? _stream;
    private ILoggerFactory _loggerFactory = LoggerFactory.Create((builder) => {
        builder.AddDebug();
        builder.SetMinimumLevel(LogLevel.Trace);
    });

    protected override EventStream Stream {
        get {
            if (_stream == null) {
                var diskOptions = new LocalStorageEventStreamOptions {
                    BaseDataPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
                    FileReadBlockSize = 4096, // 4k block size.
                    UseCaching = true,
                };
                var diskOptionsAccessor = A.Fake<IOptions<LocalStorageEventStreamOptions>>();
                A.CallTo(() => diskOptionsAccessor.Value)
                    .Returns(diskOptions);
                var serializerOptions = A.Fake<IOptions<JsonSerializationOptions>>();
                A.CallTo(() => serializerOptions.Value)
                    .Returns(new JsonSerializationOptions());

                _stream = new LocalStorageEventStream(_loggerFactory, new JsonEventSerializer(serializerOptions), diskOptionsAccessor);
                AsyncHelper.RunSync(() => _stream.StartAsync());
            }
            return _stream;
        }
    }
}