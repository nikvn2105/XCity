using System;
using System.IO;
using System.Threading.Tasks;

namespace XCity
{
    [Serializable]
    internal sealed class CitySaveSummary
    {
        public string name;
        public int population;
        public int money;
        public int day;
        public long savedUtcTicks;
    }

    internal sealed class CitySave
    {
        public const int SlotCount = 3;
        private const string HeaderPrefix = "{\"summary\":";
        private const string CityPrefix = ",\"city\":";

        [Serializable]
        private sealed class SaveDocument
        {
            // Populated by the JSON codec on load. The writer emits the header on its own line.
            public CitySaveSummary summary = null;
            public CitySnapshot city = null;
        }

        private sealed class SaveSlot
        {
            public readonly string Path;
            public string Backup => Path + ".bak";
            public string Temporary => Path + ".tmp";
            public bool PrimaryValid;
            public Task<bool> Pending = Task.FromResult(true);

            public SaveSlot(string path) => Path = path;
        }

        private readonly string _directory;
        private readonly Func<object, string> _encode;
        private readonly Func<string, Type, object> _decode;
        private readonly Func<long, int> _day;
        private readonly Action<string> _warn;
        private readonly SaveSlot[] _slots = new SaveSlot[SlotCount];

        public CitySave(string directory, Func<object, string> encode, Func<string, Type, object> decode,
            Func<long, int> day, Action<string> warn)
        {
            _directory = directory;
            _encode = encode;
            _decode = decode;
            _day = day;
            _warn = warn;
            for (int i = 0; i < SlotCount; i++)
                _slots[i] = new SaveSlot(Path.Combine(directory, $"slot-{i + 1}.json"));
        }

        private SaveSlot GetSlot(int number)
        {
            if (number < 1 || number > SlotCount) throw new ArgumentOutOfRangeException(nameof(number));
            return _slots[number - 1];
        }

        public bool HasSave(int number)
        {
            SaveSlot slot = GetSlot(number);
            return File.Exists(slot.Path) || File.Exists(slot.Backup);
        }

        public bool MigrateLegacy(string defaultName)
        {
            var legacy = new SaveSlot(Path.Combine(_directory, "xcity-save.json"));
            SaveSlot slot = GetSlot(1);
            string pending = Path.Combine(_directory, "legacy-migration.pending");
            string done = Path.Combine(_directory, "legacy-migration.done");
            try
            {
                if (File.Exists(done)) return true;
                Directory.CreateDirectory(_directory);

                if (!File.Exists(pending))
                {
                    if (HasSave(1) || (!File.Exists(legacy.Path) && !File.Exists(legacy.Backup)))
                    {
                        File.WriteAllText(done, "");
                        return true;
                    }
                    File.WriteAllText(pending, "");
                }

                // Move without overwriting. The marker lets an interrupted migration resume.
                MoveIfAbsent(legacy.Path, slot.Path);
                MoveIfAbsent(legacy.Backup, slot.Backup);
                AddLegacyHeader(slot.Path, defaultName);
                AddLegacyHeader(slot.Backup, defaultName);
                File.Move(pending, done);
                return true;
            }
            catch (Exception ex) when (IsSaveError(ex))
            {
                _warn($"Could not migrate XCity save: {ex.Message}");
                return false;
            }
        }

        private static void MoveIfAbsent(string source, string destination)
        {
            if (File.Exists(source) && !File.Exists(destination)) File.Move(source, destination);
        }

        private void AddLegacyHeader(string path, string defaultName)
        {
            if (!File.Exists(path)) return;
            using (var reader = File.OpenText(path))
                if (ReadPrefix(reader)) return;

            string json = File.ReadAllText(path);
            CitySimulation city = DecodeCity(json, path, out _);
            if (city == null) return;
            var summary = Summarize(city, defaultName, File.GetLastWriteTimeUtc(path));
            string temporary = path + ".migration.tmp";
            WriteDocument(temporary, summary, json);
            File.Replace(temporary, path, null);
        }

        public CitySaveSummary ReadSummary(int number)
        {
            SaveSlot slot = GetSlot(number);
            return ReadHeader(slot.Path) ?? ReadHeader(slot.Backup);
        }

        private CitySaveSummary ReadHeader(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using var reader = File.OpenText(path);
                if (!ReadPrefix(reader)) return null;
                string header = reader.ReadLine();
                return reader.ReadLine() == CityPrefix && !string.IsNullOrEmpty(header)
                    ? (CitySaveSummary)_decode(header, typeof(CitySaveSummary)) : null;
            }
            catch (Exception ex) when (IsSaveError(ex))
            {
                _warn($"Could not read XCity save summary: {ex.Message}");
                return null;
            }
        }

        private static bool ReadPrefix(StreamReader reader)
        {
            foreach (char ch in HeaderPrefix)
                if (reader.Read() != ch) return false;
            if (reader.Peek() == '\r') reader.Read();
            return reader.Read() == '\n';
        }

        public bool TryLoad(int number, out CitySimulation city, out CitySaveSummary summary)
        {
            SaveSlot slot = GetSlot(number);
            slot.Pending.GetAwaiter().GetResult();
            city = Read(slot.Path, out summary);
            slot.PrimaryValid = city != null;
            if (city == null) city = Read(slot.Backup, out summary);
            return city != null;
        }

        private CitySimulation Read(string path, out CitySaveSummary summary)
        {
            summary = null;
            try
            {
                return File.Exists(path) ? DecodeCity(File.ReadAllText(path), path, out summary) : null;
            }
            catch (Exception ex) when (IsSaveError(ex))
            {
                _warn($"Could not read XCity save: {ex.Message}");
                return null;
            }
        }

        private CitySimulation DecodeCity(string json, string path, out CitySaveSummary summary)
        {
            summary = null;
            try
            {
                CitySnapshot snapshot;
                if (json.StartsWith(HeaderPrefix, StringComparison.Ordinal))
                {
                    var document = (SaveDocument)_decode(json, typeof(SaveDocument));
                    snapshot = document?.city;
                    summary = document?.summary;
                }
                else snapshot = (CitySnapshot)_decode(json, typeof(CitySnapshot));

                if (CitySimulation.TryRestore(snapshot, out var city))
                {
                    if (city.DroppedCellsOnLoad > 0)
                        _warn($"XCity save {Path.GetFileName(path)}: dropped {city.DroppedCellsOnLoad} cells that no longer fit the terrain.");
                    return city;
                }
                _warn($"Invalid XCity save: {Path.GetFileName(path)}");
            }
            catch (ArgumentException ex)
            {
                _warn($"Invalid XCity save {Path.GetFileName(path)}: {ex.Message}");
            }
            summary = null;
            return null;
        }

        public bool WaitForWrite(int number) => GetSlot(number).Pending.GetAwaiter().GetResult();

        public bool HasFailedWrite(int number)
        {
            Task<bool> pending = GetSlot(number).Pending;
            return pending.IsCompleted && !pending.GetAwaiter().GetResult();
        }

        public bool Write(int number, string name, CitySimulation city)
        {
            SaveSlot slot = GetSlot(number);
            slot.Pending.GetAwaiter().GetResult();
            bool written = WriteSnapshot(slot, city.CreateSnapshot(), Summarize(city, name, DateTime.UtcNow));
            slot.Pending = Task.FromResult(written);
            return written;
        }

        public bool TryWriteInBackground(int number, string name, CitySimulation city)
        {
            SaveSlot slot = GetSlot(number);
            if (!slot.Pending.IsCompleted) return false;
            CitySnapshot snapshot = city.CreateSnapshot();
            CitySaveSummary summary = Summarize(city, name, DateTime.UtcNow);
            // JsonUtility supports worker threads with a private snapshot.
            slot.Pending = Task.Run(() =>
            {
                try { return WriteSnapshot(slot, snapshot, summary); }
                catch (Exception ex)
                {
                    _warn($"Could not save XCity in the background: {ex.Message}");
                    return false;
                }
            });
            return true;
        }

        private CitySaveSummary Summarize(CitySimulation city, string name, DateTime savedAt) =>
            new CitySaveSummary
            {
                name = name,
                population = city.Population,
                money = city.Money,
                day = _day(city.TickCount),
                savedUtcTicks = savedAt.Ticks
            };

        private bool WriteSnapshot(SaveSlot slot, CitySnapshot snapshot, CitySaveSummary summary)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                WriteDocument(slot.Temporary, summary, _encode(snapshot));
                if (File.Exists(slot.Path))
                    File.Replace(slot.Temporary, slot.Path, slot.PrimaryValid ? slot.Backup : null);
                else
                    File.Move(slot.Temporary, slot.Path);
                slot.PrimaryValid = true;
                return true;
            }
            catch (Exception ex) when (IsSaveError(ex))
            {
                _warn($"Could not save XCity: {ex.Message}");
                return false;
            }
        }

        private void WriteDocument(string path, CitySaveSummary summary, string cityJson)
        {
            using var writer = File.CreateText(path);
            writer.WriteLine(HeaderPrefix);
            writer.WriteLine(_encode(summary));
            writer.WriteLine(CityPrefix);
            writer.WriteLine(cityJson);
            writer.Write('}');
        }

        public bool DeleteConfirmedSlot(int number)
        {
            SaveSlot slot = GetSlot(number);
            slot.Pending.GetAwaiter().GetResult();
            try
            {
                File.Delete(slot.Temporary);
                File.Delete(slot.Backup);
                File.Delete(slot.Path);
                slot.PrimaryValid = false;
                slot.Pending = Task.FromResult(true);
                return true;
            }
            catch (Exception ex) when (IsSaveError(ex))
            {
                _warn($"Could not delete XCity save: {ex.Message}");
                return false;
            }
        }

        private static bool IsSaveError(Exception ex) =>
            ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException;
    }
}
