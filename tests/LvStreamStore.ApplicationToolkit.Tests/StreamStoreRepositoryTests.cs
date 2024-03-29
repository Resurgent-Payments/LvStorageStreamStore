namespace LvStreamStore.ApplicationToolkit.Tests {
    using System;
    using System.Linq;
    using System.Threading.Tasks;

    using LvStreamStore.Test;

    using Xunit;

    public class StreamStoreRepositoryTests : StreamStoreTestSpecification {
        public StreamStoreRepositoryTests() : base() {
        }

        public async override Task InitializeAsync() {
            await base.InitializeAsync();
            var agg = new TestAggregate(Guid.NewGuid(), "name", "description");
            await Repository.Save(agg);
        }

        [Fact]
        public async Task ARepositoryCanBeRead() {
            var reader = Repository.ReadAsync(StreamKey.All);
            Assert.Single(await reader.ToListAsync());
        }

        [Fact]
        public async Task AggregateEventsCanBeRead() {
            var reader = Repository.ReadAsync<TestAggregate>();
            Assert.Single(await reader.ToListAsync());
        }
    }
}
