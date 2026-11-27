using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace XCity
{
    internal sealed class CitySave
    {
        private readonly string _path;
        private readonly string _backup;
        private readonly string _temporary;
        private bool _primaryValid;
        private Task _background = Task.CompletedTask;

        public CitySave(string directory)
        {
            _path = Path.Combine(directory, "xcity-save.json");
            _backup = _path + ".bak";
            _temporary = _path + ".tmp";
        }

        public CitySimulation Load()
        {
            CitySimulation city = Read(_path);
            _primaryValid = city != null;
            return city ?? Read(_backup) ?? new CitySimulation();
        }

        private static CitySimulation Read(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var snapshot = JsonUtility.FromJson<CitySnapshot>(File.ReadAllText(path));
                if (CitySimulation.TryRestore(snapshot, out var city))
                {
                    if (city.DroppedCellsOnLoad > 0)
                        Debug.LogWarning($"XCity save {Path.GetFileName(path)}: dropped {city.DroppedCellsOnLoad} cells that no longer fit the terrain.");
                    return city;
                }
                Debug.LogWarning($"Invalid XCity save: {Path.GetFileName(path)}");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                Debug.LogWarning($"Could not read XCity save: {ex.Message}");
            }
            return null;
        }

        /// <summary>Saves now on the calling thread, after any background save finishes (use when the app pauses or quits).</summary>
        public bool Write(CitySimulation city)
        {
            _background.Wait();
            return WriteSnapshot(city.CreateSnapshot());
        }

        /// <summary>
        /// Copies the city now and writes it on a worker thread, so a large city does not freeze a frame.
        /// Returns false, saving nothing, while the previous background save is still running.
        /// </summary>
        public bool TryWriteInBackground(CitySimulation city)
        {
            if (!_background.IsCompleted) return false;
            CitySnapshot snapshot = city.CreateSnapshot();
            // JsonUtility may run off the main thread; the snapshot is a private copy nothing else touches.
            _background = Task.Run(() =>
            {
                try
                {
                    WriteSnapshot(snapshot);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Could not save XCity in the background: {ex.Message}");
                }
            });
            return true;
        }

        private bool WriteSnapshot(CitySnapshot snapshot)
        {
            try
            {
                File.WriteAllText(_temporary, JsonUtility.ToJson(snapshot));
                if (File.Exists(_path))
                    File.Replace(_temporary, _path, _primaryValid ? _backup : null);
                else
                    File.Move(_temporary, _path);
                _primaryValid = true;
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                Debug.LogWarning($"Could not save XCity: {ex.Message}");
                return false;
            }
        }
    }
}
