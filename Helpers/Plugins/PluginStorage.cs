// Target: .NET Framework + C# 7.3
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using TimeTrack.Plugins;

namespace Zeitmanagement.Helpers.Plugins
{
    /// <summary>
    /// Die Ablage eines Plugins in <c>plugin_records</c>, abgegrenzt über die Plugin-ID. Geheimnisse liegen in der
    /// Sammlung <see cref="SecretCollection"/>, mit DPAPI für den angemeldeten Windows-Benutzer verschlüsselt.
    /// </summary>
    /// <remarks>Nicht thread-sicher: Der <see cref="PluginHost"/> ruft sie nur auf dem UI-Thread auf.</remarks>
    internal sealed class PluginStorage : IPluginStorage
    {
        /// <summary>Plugin-ID, unter der TimeTrack selbst Angaben zu Plugins ablegt (z. B. welche aktiviert sind).</summary>
        public const string HostId = "$host";

        /// <summary>Sammlung der verschlüsselten Geheimnisse eines Plugins.</summary>
        public const string SecretCollection = "$secret";

        private const string SecretPrefix = "dpapi:";
        private const int MaxNameLength = 200;

        private static readonly Regex PluginIdPattern = new Regex(@"^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant);

        private readonly TimeTracker _db;
        private readonly string _pluginId;

        public PluginStorage(TimeTracker db, string pluginId)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            if (!IsValidPluginId(pluginId))
                throw new ArgumentException($"Ungültige Plugin-ID '{pluginId}'.", nameof(pluginId));
            _pluginId = pluginId;
        }

        /// <summary>Kleinbuchstaben, Ziffern, Punkt, Binde- und Unterstrich, höchstens 64 Zeichen.</summary>
        public static bool IsValidPluginId(string pluginId)
        {
            return pluginId != null && PluginIdPattern.IsMatch(pluginId);
        }

        public string Get(string collection, string key)
        {
            CheckCollection(collection);
            CheckKey(key);
            return _db.GetPluginRecord(_pluginId, collection, key);
        }

        public void Set(string collection, string key, string value)
        {
            CheckCollection(collection);
            CheckKey(key);
            if (value == null)
                _db.DeletePluginRecord(_pluginId, collection, key);
            else
                _db.SetPluginRecord(_pluginId, collection, key, value);
        }

        public bool Delete(string collection, string key)
        {
            CheckCollection(collection);
            CheckKey(key);
            return _db.DeletePluginRecord(_pluginId, collection, key);
        }

        public IReadOnlyDictionary<string, string> List(string collection)
        {
            CheckCollection(collection);
            return _db.ListPluginRecords(_pluginId, collection);
        }

        public void SetSecret(string key, string value)
        {
            CheckKey(key);
            if (string.IsNullOrEmpty(value))
            {
                _db.DeletePluginRecord(_pluginId, SecretCollection, key);
                return;
            }

            var cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy(key), DataProtectionScope.CurrentUser);
            _db.SetPluginRecord(_pluginId, SecretCollection, key, SecretPrefix + Convert.ToBase64String(cipher));
        }

        public string GetSecret(string key)
        {
            CheckKey(key);
            var stored = _db.GetPluginRecord(_pluginId, SecretCollection, key);
            if (stored == null || !stored.StartsWith(SecretPrefix, StringComparison.Ordinal))
                return null;

            try
            {
                var cipher = Convert.FromBase64String(stored.Substring(SecretPrefix.Length));
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(cipher, Entropy(key), DataProtectionScope.CurrentUser));
            }
            catch (CryptographicException)
            {
                // Anderer Rechner oder anderer Benutzer (z. B. nach dem Wiederherstellen einer Sicherung): nicht lesbar.
                return null;
            }
            catch (FormatException)
            {
                return null;
            }
        }

        /// <summary>Bindet das Geheimnis an Plugin und Schlüssel, damit es nicht unter einem anderen Namen lesbar ist.</summary>
        private byte[] Entropy(string key)
        {
            return Encoding.UTF8.GetBytes("TimeTrack.Plugin|" + _pluginId + "|" + key);
        }

        private static void CheckCollection(string collection)
        {
            CheckName(collection, nameof(collection));
            if (collection.StartsWith("$", StringComparison.Ordinal))
                throw new ArgumentException("Sammlungen, die mit '$' beginnen, sind TimeTrack vorbehalten.", nameof(collection));
        }

        private static void CheckKey(string key)
        {
            CheckName(key, nameof(key));
        }

        private static void CheckName(string value, string parameter)
        {
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException("Darf nicht leer sein.", parameter);
            if (value.Length > MaxNameLength)
                throw new ArgumentException($"Höchstens {MaxNameLength} Zeichen.", parameter);
        }
    }
}
