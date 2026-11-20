using System;
using System.Collections.Generic;

namespace Zeitmanagement.Helpers
{
    /// <summary>
    /// Verlauf besuchter Seiten mit Zurück/Vor, wie im Browser. Unabhängig von WPF, damit testbar.
    ///
    /// Ein neuer Eintrag verwirft die Vorwärts-Einträge. Ein Eintrag, der dem aktuellen gleich ist
    /// (laut <c>areEqual</c>), wird nicht doppelt abgelegt. Der Verlauf ist auf <see cref="Capacity"/>
    /// Einträge begrenzt; die ältesten fallen dann heraus.
    /// </summary>
    internal sealed class NavigationHistory<T>
    {
        public const int DefaultCapacity = 50;

        private readonly List<T> _items = new List<T>();
        private readonly Func<T, T, bool> _areEqual;
        private int _index = -1;

        public NavigationHistory(Func<T, T, bool> areEqual, int capacity = DefaultCapacity)
        {
            _areEqual = areEqual ?? throw new ArgumentNullException(nameof(areEqual));
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            Capacity = capacity;
        }

        public int Capacity { get; }

        /// <summary>Anzahl der gespeicherten Einträge.</summary>
        public int Count => _items.Count;

        /// <summary>Der aktuelle Eintrag (bei leerem Verlauf der Standardwert von T).</summary>
        public T Current => _index >= 0 ? _items[_index] : default(T);

        public bool CanGoBack => _index > 0;

        public bool CanGoForward => _index >= 0 && _index < _items.Count - 1;

        /// <summary>Legt einen neuen Eintrag als aktuellen ab und verwirft die Vorwärts-Einträge.</summary>
        public void Push(T item)
        {
            if (_index >= 0 && _areEqual(_items[_index], item))
                return;

            int keep = _index + 1;
            if (keep < _items.Count)
                _items.RemoveRange(keep, _items.Count - keep);

            _items.Add(item);
            _index = _items.Count - 1;

            while (_items.Count > Capacity)
            {
                _items.RemoveAt(0);
                _index--;
            }
        }

        /// <summary>Geht einen Eintrag zurück und liefert ihn. Vorher <see cref="CanGoBack"/> prüfen.</summary>
        public T GoBack()
        {
            if (!CanGoBack)
                throw new InvalidOperationException("Es gibt keinen früheren Eintrag.");
            return _items[--_index];
        }

        /// <summary>Geht einen Eintrag vor und liefert ihn. Vorher <see cref="CanGoForward"/> prüfen.</summary>
        public T GoForward()
        {
            if (!CanGoForward)
                throw new InvalidOperationException("Es gibt keinen späteren Eintrag.");
            return _items[++_index];
        }
    }
}
