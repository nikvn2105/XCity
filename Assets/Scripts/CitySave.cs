using System;
using System.IO;
using UnityEngine;

namespace XCity
{
    internal sealed class CitySave
    {
        private readonly string _path;
        private readonly string _backup;
        private readonly string _temporary;
        private bool _primaryValid;

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
                if (CitySimulation.TryRestore(snapshot, out var city)) return city;
                Debug.LogWarning($"Invalid XCity save: {Path.GetFileName(path)}");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                Debug.LogWarning($"Could not read XCity save: {ex.Message}");
            }
            return null;
        }

        public bool Write(CitySimulation city)
        {
            try
            {
                File.WriteAllText(_temporary, JsonUtility.ToJson(city.CreateSnapshot()));
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
