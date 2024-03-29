namespace LvStreamStore.ApplicationToolkit {
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading.Tasks;

    using Microsoft.Extensions.Options;

    public class StreamStoreRepository : IStreamStoreRepository {
        private readonly IEventStreamClient _client;
        private readonly StreamStoreRepositoryOptions _options;

        public StreamStoreRepository(IEventStreamClient client, IOptions<StreamStoreRepositoryOptions> options) {
            _client = client;
            _options = options.Value ?? new StreamStoreRepositoryOptions();
        }

        public async ValueTask<bool> Save<TAggregate>(TAggregate aggregate) where TAggregate : AggregateRoot, new() {
            try {
                var streamId = CreateStreamId<TAggregate>(aggregate.Id);

                // aggregate.
                var version = aggregate.Version == -1 ? ExpectedVersion.NoStream : aggregate.Version;
                var events = aggregate.TakeMessages().Select(msg => Serialize(streamId, msg)).ToArray();

                await _client.AppendToStreamAsync(streamId, version, events);
            }
            catch (Exception) {
                return false;
            }

            return true;
        }

        public async ValueTask<TAggregate> TryGetById<TAggregate>(Guid aggregateId) where TAggregate : AggregateRoot, new() {
            TAggregate aggregate = null;

            try {
                var streamId = CreateStreamId<TAggregate>(aggregateId);
                var stream = (await _client.ReadStreamAsync(streamId).ToListAsync())
                    .Select(Deserialize)
                    .ToArray();
                aggregate = new TAggregate();
                aggregate.RestoreFromMessages(stream);
            }
            catch (StreamDoesNotExistException) {
                throw new AggregateNotFoundException<TAggregate>();
            }
            catch (Exception) {
                throw;
            }

            return aggregate;
        }

        public IDisposable Subscribe<TEvent>(Messaging.IReceiver<TEvent> receiver) where TEvent : Event {
            var deserialize = new EventDeserializationReceiver<TEvent>(receiver, _options.JsonOptions);
            var upcast = new Messaging.TypeCastReceiver<StreamMessage, RecordedEvent>(deserialize);
            return AsyncHelper.RunSync(() => _client.SubscribeToStreamAsync(upcast));
        }

        public async IAsyncEnumerable<Event> ReadAsync(StreamKey key) {
            await foreach (var recorded in _client.ReadStreamAsync(key)) {
                var e = Deserialize(recorded);
                if (e is Event @event) { yield return @event; }
            }
        }

        public async IAsyncEnumerable<Event> ReadAsync<TAggregate>() where TAggregate : AggregateRoot, new() {
            var key = CreateStreamKey<TAggregate>();

            await foreach (var recorded in _client.ReadStreamAsync(key)) {
                var e = Deserialize(recorded);
                if (e is Event @event) { yield return @event; }
            }
        }

        private static StreamId CreateStreamId<TAggregate>(Guid aggregateId) where TAggregate : AggregateRoot, new() {
            // use namespace(s) for the categories between the tenant and aggregate id.  (e.g. "Gift", "Card"), so the StreamKey will be: []{"azdkf", "Gift", "Card", "azkdf"}
            var typeOfAggregate = typeof(TAggregate);
            var namespaceParts = typeOfAggregate.FullName!.Split('.', StringSplitOptions.RemoveEmptyEntries);

            //todo: pull this from a tenant resolver implementation.
            var streamId = new StreamId(Guid.Empty.ToString("N"), namespaceParts, aggregateId.ToString("N"));
            return streamId;
        }
        private static StreamKey CreateStreamKey<TAggregate>() where TAggregate : AggregateRoot, new() {
            // use namespace(s) for the categories between the tenant and aggregate id.  (e.g. "Gift", "Card"), so the StreamKey will be: []{"azdkf", "Gift", "Card", "azkdf"}
            var typeOfAggregate = typeof(TAggregate);
            var namespaceParts = typeOfAggregate.FullName!.Split('.', StringSplitOptions.RemoveEmptyEntries);

            //todo: pull this from a tenant resolver implementation.
            // Splat ("*") signifies all tenants (for now).
            var streamKey = new StreamKey(new[] { "*" }.Union(namespaceParts).ToArray());
            return streamKey;
        }

        private Messaging.Message Deserialize(RecordedEvent @event) {
            // get dictionary.
            var eventOptions = JsonSerializer.Deserialize<Dictionary<string, string>>(new ReadOnlySpan<byte>(@event.Metadata), _options.JsonOptions)!;

            // find key/value for event type
            if (!eventOptions.TryGetValue("AssemblyQualifiedName", out var aqName)) throw new InvalidOperationException("Missing clr type from serialized event information.");

            // get the clr type that should be decoded.
            var resolvedClrType = Type.GetType(aqName, true, true)!;

            //todo: custom deserializer to pickup messageid.
            return (Event)JsonSerializer.Deserialize(new ReadOnlySpan<byte>(@event.Data), resolvedClrType, _options.JsonOptions)!;
        }

        private EventData Serialize(StreamId streamId, object @event) {
            BeforeSerialization(@event);

            var type = @event.GetType();

            // create metadata
            // todo: investigate if we can reduce allocations here.
            var metaDataDict = new Dictionary<string, string> {
                { nameof(type.FullName), type.FullName! },
                { nameof(type.Namespace), type.Namespace! },
                { nameof(type.AssemblyQualifiedName), type.AssemblyQualifiedName! },
            };
            var metadata = JsonSerializer.Serialize(metaDataDict, _options.JsonOptions);

            // create data
            // todo: investigate if we can reduce memory allocations here.
            var data = JsonSerializer.Serialize(@event, _options.JsonOptions);

            // get name of event
            var eventType = type.Name.Contains("+") ? type.Name.Substring(type.Name.IndexOf("+") + 1) : type.Name;

            // create event id
            return new EventData(streamId, Guid.NewGuid(), eventType, Encoding.UTF8.GetBytes(metadata), Encoding.UTF8.GetBytes(data));
        }

        protected virtual void BeforeSerialization(object @object) { }
    }
}
