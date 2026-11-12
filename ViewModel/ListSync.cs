using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Zeitmanagement.ViewModel
{
    /// <summary>
    /// Gleicht die Einträge einer Auswahlliste an neue Werte an, ohne sie zu leeren. Ein ComboBox verliert beim
    /// Leeren seiner Liste die Auswahl; so bleibt der gewählte Eintrag unberührt, solange er noch vorkommt.
    /// </summary>
    internal static class ListSync
    {
        public static void Sync(ObservableCollection<string> list, IList<string> wanted)
        {
            for (var i = list.Count - 1; i >= 0; i--)
            {
                if (!wanted.Contains(list[i])) list.RemoveAt(i);
            }

            for (var i = 0; i < wanted.Count; i++)
            {
                if (i < list.Count && list[i] == wanted[i]) continue;

                var at = list.IndexOf(wanted[i]);
                if (at > i) list.Move(at, i);
                else list.Insert(i, wanted[i]);
            }
        }
    }
}
