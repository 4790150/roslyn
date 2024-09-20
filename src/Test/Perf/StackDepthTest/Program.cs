// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace OverflowSensitivity
{
    public class Program
    {
        [DllImport("kernel32.dll")]
        private static extern ErrorModes SetErrorMode(ErrorModes uMode);

        [Flags]
        private enum ErrorModes : uint
        {
            SYSTEM_DEFAULT = 0x0,
            SEM_FAILCRITICALERRORS = 0x0001,
            SEM_NOALIGNMENTFAULTEXCEPT = 0x0004,
            SEM_NOGPFAULTERRORBOX = 0x0002,
            SEM_NOOPENFILEERRORBOX = 0x8000
        }

        public static int Main(string[] args)
        {
            // Prevent the "This program has stopped working" messages.
            SetErrorMode(ErrorModes.SEM_NOGPFAULTERRORBOX);

            if (args.Length != 1)
            {
                Console.WriteLine("You must pass an integer argument in to this program.");
                return -1;
            }

            Console.WriteLine($"Running in {IntPtr.Size * 8}-bit mode");


        const string programText =
@"using System;
namespace TopLevel
{
    class Base {}
    class Foo : Base {
        public static bool IsWin() {
            return true;
        }

        public static bool IsWin(int i) {
            return i > 0;
        }

        public static void Main()
        {
            Foo.IsWin(true);
        }
    }
}";


            SyntaxTree tree = CSharpSyntaxTree.ParseText(programText);
            CompilationUnitSyntax root = tree.GetCompilationUnitRoot();

            var compilation = CSharpCompilation.Create("HelloWorld")
                .AddReferences(MetadataReference.CreateFromFile(
                    typeof(string).Assembly.Location))
                .AddSyntaxTrees(tree);

            using (var ms = new MemoryStream())
            {
                EmitResult result = compilation.Emit(ms);

                if (result.Success)
                {
                    ms.Seek(0, SeekOrigin.Begin);
                    Assembly assembly = Assembly.Load(ms.ToArray());
                    MethodInfo method = assembly.EntryPoint;
                    method.Invoke(null, null);
                }
                else
                {
                    foreach (var diagnostic in result.Diagnostics)
                    {
                        Console.WriteLine(diagnostic.ToString());
                    }
                }
            }

            SemanticModel model = compilation.GetSemanticModel(tree);

            forNodes(model, root);

            if (int.TryParse(args[0], out var i))
            {
                CompileCode(MakeCode(i));
                return 0;
            }
            else
            {
                Console.WriteLine($"Could not parse {args[0]}");
                return -1;
            }
        }

        private class Base
        {
            public void Func() { }
            public void Func(int i, string s) { }
        }

        private static void forNodes(SemanticModel model, SyntaxNode node)
        {
            foreach (var child in node.ChildNodes())
            {
                var symbolInfo = model.GetSymbolInfo(child);
                var symbol = model.GetDeclaredSymbol(child);
                if (symbolInfo.Symbol != null)
                {
                    Console.WriteLine("node={0},     symbol={1},  isDeclared={2}", child, symbolInfo.Symbol, symbol != null);
                }

                if (symbol != null)
                {
                    var t3 = symbol.GetType();
                    Console.WriteLine("node={0},     symbol={1},  isDeclared={2}", child, symbolInfo.Symbol??symbol, symbol != null);
                }

                forNodes(model, child);
            }
        }

private static string MakeCode(int depth)
        {
            var builder = new StringBuilder();
            builder.AppendLine(
    @"class C {
    C M(string x) { return this; }
    void M2() {
        new C()
");
            for (int i = 0; i < depth; i++)
            {
                builder.AppendLine(@"            .M(""test"")");
            }
            builder.AppendLine(
           @"            .M(""test"");
    }
}");
            return builder.ToString();
        }
        private static void CompileCode(string stringText)
        {
            var parseOptions = new CSharpParseOptions(kind: SourceCodeKind.Regular, documentationMode: DocumentationMode.None);
            var options = new CSharpCompilationOptions(outputKind: OutputKind.DynamicallyLinkedLibrary, concurrentBuild: false);
            var tree = SyntaxFactory.ParseSyntaxTree(SourceText.From(stringText, encoding: null, SourceHashAlgorithm.Sha256), parseOptions);
            var reference = MetadataReference.CreateFromFile(@"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.5.2\mscorlib.dll");
            var comp = CSharpCompilation.Create("assemblyName", new SyntaxTree[] { tree }, references: new MetadataReference[] { reference }, options: options);
            var diag = comp.GetDiagnostics();
            if (!diag.IsDefaultOrEmpty)
            {
                throw new Exception(diag[0].ToString());
            }
        }
    }
}
