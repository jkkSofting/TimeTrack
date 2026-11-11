// Target: .NET Framework + C# 7.3
using System;
using System.IO;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Pdf;

namespace Zeitmanagement.Helpers
{
    /// <summary>
    /// Macht aus einem <see cref="ExportReport"/> die PDF-Datei. Das Layout liefert <see cref="PdfReportLayout"/>; hier wird nur
    /// gerendert (Schriften eingebettet, Unicode) und gespeichert. Der Aufruf ist unabhängig vom UI-Thread.
    /// </summary>
    public static class PdfExporter
    {
        public static byte[] ToBytes(ExportReport report, ExportMeta meta)
        {
            return Render(PdfReportLayout.Build(report, meta));
        }

        /// <summary>
        /// Schreibt die Datei erst, wenn das PDF vollständig gerendert ist, und dann über eine temporäre Datei
        /// (<see cref="SafeFile"/>): Ein Fehler beim Rendern oder Schreiben hinterlässt keine halbe Datei und lässt eine vorhandene
        /// unangetastet. Eine gesperrte Zieldatei (im PDF-Programm geöffnet) meldet sich als <see cref="IOException"/>.
        /// </summary>
        public static void WriteFile(string path, ExportReport report, ExportMeta meta)
        {
            SafeFile.WriteAllBytes(path, ToBytes(report, meta));
        }

        public static byte[] Render(Document document)
        {
            // unicode: true bettet die Schrift als Unicode-TrueType ein (Umlaute, Anführungszeichen, Euro …).
            var renderer = new PdfDocumentRenderer(true) { Document = document };
            renderer.RenderDocument();
            renderer.PdfDocument.Info.Creator = "TimeTrack";

            using (var stream = new MemoryStream())
            {
                renderer.PdfDocument.Save(stream, false);
                return stream.ToArray();
            }
        }
    }
}
