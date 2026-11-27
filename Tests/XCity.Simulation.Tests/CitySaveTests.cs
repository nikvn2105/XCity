using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace XCity.Tests
{
    public sealed class CitySaveTests : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { IncludeFields = true };
        private readonly string _directory = Directory.CreateTempSubdirectory("xcity-saves-").FullName;

        [Fact]
        public void MigrationMovesLegacyPrimaryAndBackupIntoSlotOneAndPreservesSummary()
        {
            var city = BuildCity();
            string legacy = Path.Combine(_directory, "xcity-save.json");
            WriteLegacy(legacy, city);
            WriteLegacy(legacy + ".bak", new CitySimulation());
            var lastSaved = new DateTime(2026, 4, 20, 12, 30, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(legacy, lastSaved);
            CitySave save = NewStore();

            Assert.True(save.MigrateLegacy("Thành phố 1"));
            Assert.False(File.Exists(legacy));
            Assert.False(File.Exists(legacy + ".bak"));
            Assert.True(File.Exists(Path.Combine(_directory, "slot-1.json.bak")));
            Assert.True(save.TryLoad(1, out var loaded, out var summary));
            Assert.Equal(city.TickCount, loaded.TickCount);
            Assert.Equal(city.Population, loaded.Population);
            Assert.Equal("Thành phố 1", summary.name);
            Assert.Equal(lastSaved.Ticks, summary.savedUtcTicks);
            Assert.Equal(summary.savedUtcTicks, save.ReadSummary(1).savedUtcTicks);
            Assert.False(save.HasSave(2));
            Assert.True(NewStore().MigrateLegacy("Thành phố 1"));
        }

        [Fact]
        public void MigrationDoesNotOverwriteAnExistingSlotOrImportAfterDeletion()
        {
            string legacy = Path.Combine(_directory, "xcity-save.json");
            WriteLegacy(legacy, BuildCity());
            CitySave save = NewStore();
            Assert.True(save.Write(1, "Thành phố hiện tại", new CitySimulation()));

            Assert.True(save.MigrateLegacy("Thành phố 1"));
            Assert.Equal("Thành phố hiện tại", save.ReadSummary(1).name);
            Assert.True(File.Exists(legacy));

            Assert.True(save.DeleteConfirmedSlot(1));
            save = NewStore();
            Assert.True(save.MigrateLegacy("Thành phố 1"));
            Assert.False(save.HasSave(1));
            Assert.True(File.Exists(legacy));
        }

        [Fact]
        public void InterruptedMigrationFinishesMovingBackupAndCanLoadIt()
        {
            string legacy = Path.Combine(_directory, "xcity-save.json");
            string slot = Path.Combine(_directory, "slot-1.json");
            File.WriteAllText(slot, "{}");
            WriteLegacy(legacy + ".bak", BuildCity());
            File.WriteAllText(Path.Combine(_directory, "legacy-migration.pending"), "");

            CitySave save = NewStore();
            Assert.True(save.MigrateLegacy("Thành phố 1"));
            Assert.False(File.Exists(legacy + ".bak"));
            Assert.True(File.Exists(Path.Combine(_directory, "legacy-migration.done")));
            Assert.Equal("Thành phố 1", save.ReadSummary(1).name);
            Assert.True(save.TryLoad(1, out var city, out _));
            Assert.Equal(9275, city.Money);
        }

        [Fact]
        public void SlotWritesAndDeletionAreIsolatedIncludingBackgroundSavesAndBackups()
        {
            CitySave save = NewStore();
            var first = BuildCity();
            var second = new CitySimulation();
            Assert.True(save.Write(1, "Hạ Long", second));
            Assert.True(save.TryWriteInBackground(1, "Đà Nẵng", first));
            Assert.True(save.Write(2, "Hội An", second));
            Assert.True(save.WaitForWrite(1));

            save = NewStore();
            Assert.True(save.TryLoad(1, out var loadedFirst, out var firstSummary));
            Assert.True(save.TryLoad(2, out var loadedSecond, out var secondSummary));
            Assert.Equal(first.Money, loadedFirst.Money);
            Assert.Equal(second.Money, loadedSecond.Money);
            Assert.Equal("Đà Nẵng", firstSummary.name);
            Assert.Equal("Hội An", secondSummary.name);

            File.WriteAllText(Path.Combine(_directory, "slot-1.json"), "{}");
            Assert.True(save.TryLoad(1, out _, out var backup));
            Assert.Equal("Hạ Long", backup.name);
            Assert.True(save.DeleteConfirmedSlot(1));
            Assert.False(save.HasSave(1));
            Assert.Equal("Hội An", save.ReadSummary(2).name);
            Assert.False(save.HasSave(3));
        }

        [Fact]
        public void SummaryRoundTripsWithoutDecodingTheCity()
        {
            CitySimulation city = BuildCity();
            DateTime before = DateTime.UtcNow;
            Assert.True(NewStore().Write(3, "Hải Phòng", city));
            int decoded = 0;
            CitySave store = NewStore((json, type) =>
            {
                Assert.Equal(typeof(CitySaveSummary), type);
                decoded++;
                return Decode(json, type);
            });

            CitySaveSummary summary = store.ReadSummary(3);
            Assert.Equal(1, decoded);
            Assert.Equal("Hải Phòng", summary.name);
            Assert.Equal(city.Population, summary.population);
            Assert.Equal(city.Money, summary.money);
            Assert.Equal(TestDay(city.TickCount), summary.day);
            Assert.InRange(summary.savedUtcTicks, before.Ticks, DateTime.UtcNow.Ticks);
        }

        private CitySave NewStore(Func<string, Type, object> decode = null) =>
            new CitySave(_directory, value => JsonSerializer.Serialize(value, value.GetType(), JsonOptions),
                decode ?? Decode, TestDay, _ => { });

        private static int TestDay(long tick) => (int)(tick / 10) + 1;

        private static object Decode(string json, Type type)
        {
            try { return JsonSerializer.Deserialize(json, type, JsonOptions); }
            catch (JsonException ex) { throw new ArgumentException("Invalid test JSON.", ex); }
        }

        private static void WriteLegacy(string path, CitySimulation city) =>
            File.WriteAllText(path, JsonSerializer.Serialize(city.CreateSnapshot(), JsonOptions));

        private static CitySimulation BuildCity()
        {
            int x = CitySimulation.GridSize / 2;
            int z = CitySimulation.GridSize / 2;
            while (!CitySimulation.IsBuildable(x, z)) x++;
            CitySnapshot snapshot = new CitySimulation().CreateSnapshot();
            snapshot.money = 9275;
            snapshot.tick = 72;
            snapshot.cells = new[] { new CityCell { Type = CellType.Residential, Level = 1, Height = 2.7f } };
            snapshot.cellIndices = new[] { CitySimulation.CellIndex(x, z) };
            Assert.True(CitySimulation.TryRestore(snapshot, out var city));
            Assert.True(city.Population > 0);
            return city;
        }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
