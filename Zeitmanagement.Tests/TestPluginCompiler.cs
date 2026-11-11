// Target: .NET Framework + C# 7.3
using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.CSharp;
using TimeTrack.Plugins;
using Xunit;

namespace Zeitmanagement.Tests
{
    /// <summary>
    /// Übersetzt Test-Plugins zur Laufzeit (CodeDom, C# 5) in einen Plugin-Ordner. Jeder Ordner bekommt einen eindeutigen
    /// Namen, weil eine einmal geladene Assembly im Testprozess bleibt.
    /// </summary>
    internal static class TestPluginCompiler
    {
        public static string UniqueFolder()
        {
            return "TestPlugin" + Guid.NewGuid().ToString("N").Substring(0, 12);
        }

        /// <summary>Übersetzt <paramref name="source"/> nach <c>&lt;root&gt;\&lt;folder&gt;\&lt;folder&gt;.dll</c>; liefert den Ordnernamen.</summary>
        public static string Compile(string root, string folder, string source)
        {
            var dir = Path.Combine(root, folder);
            Directory.CreateDirectory(dir);
            using (var provider = new CSharpCodeProvider())
            {
                var parameters = new CompilerParameters
                {
                    OutputAssembly = Path.Combine(dir, folder + ".dll"),
                    GenerateInMemory = false,
                    GenerateExecutable = false
                };
                parameters.ReferencedAssemblies.Add("System.dll");
                parameters.ReferencedAssemblies.Add("System.Core.dll");
                parameters.ReferencedAssemblies.Add(typeof(TimeTrackPlugin).Assembly.Location);
                parameters.ReferencedAssemblies.Add(typeof(FrameworkElement).Assembly.Location);   // PresentationFramework
                parameters.ReferencedAssemblies.Add(typeof(UIElement).Assembly.Location);          // PresentationCore
                parameters.ReferencedAssemblies.Add(typeof(DependencyObject).Assembly.Location);   // WindowsBase
                parameters.ReferencedAssemblies.Add(typeof(System.Windows.Markup.IQueryAmbient).Assembly.Location); // System.Xaml
                var result = provider.CompileAssemblyFromSource(parameters, source);
                Assert.False(result.Errors.HasErrors, string.Join("\n", result.Errors.Cast<CompilerError>().Select(e => e.ToString())));
            }
            return folder;
        }
    }
}
