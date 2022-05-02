﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Editor.UnitTests.Workspaces;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Snippets;
using Microsoft.CodeAnalysis.Test.Utilities;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.Editor.UnitTests.Snippets
{
    [UseExportProvider]
    public class RoslynLSPSnippetConvertTests
    {
        [Fact, Trait(Traits.Feature, Traits.Features.RoslynLSPSnippetConverter)]
        public Task TestExtendSnippetTextChangeForwards()
        {
            var markup =
@"[|if ({|placeholder:true|})
{
}|] $$";

            var expectedLSPSnippet =
@"if (${1:true})
{
} $0";
            MarkupTestFile.GetPositionAndSpans(markup, out var outString, out var cursorPosition, out IDictionary<string, ImmutableArray<TextSpan>> dictionary);
            var stringSpan = dictionary[""].First();
            var textChange = new TextChange(new TextSpan(stringSpan.Start, 0), outString[..stringSpan.Length]);
            var placeholders = dictionary["placeholder"].Select(span => span.Start).ToImmutableArray();
            return TestAsync(markup, expectedLSPSnippet, cursorPosition, ImmutableArray.Create(new SnippetPlaceholder("true", placeholders)), textChange);
        }

        [Fact, Trait(Traits.Feature, Traits.Features.RoslynLSPSnippetConverter)]
        public Task TestExtendSnippetTextChangeBackwards()
        {
            var markup =
@"$$ [|if ({|placeholder:true|})
{
}|]";

            var expectedLSPSnippet =
@"$0if (${1:true})
{
}";
            MarkupTestFile.GetPositionAndSpans(markup, out var outString, out var cursorPosition, out IDictionary<string, ImmutableArray<TextSpan>> dictionary);
            var stringSpan = dictionary[""].First();
            var textChange = new TextChange(new TextSpan(stringSpan.Start, 0), outString.Substring(stringSpan.Start, stringSpan.Length - 1));
            var placeholders = dictionary["placeholder"].Select(span => span.Start).ToImmutableArray();
            return TestAsync(markup, expectedLSPSnippet, cursorPosition, ImmutableArray.Create(new SnippetPlaceholder("true", placeholders)), textChange);
        }

        protected static TestWorkspace CreateWorkspaceFromCode(string code)
         => TestWorkspace.CreateCSharp(code);

        private static async Task TestAsync(string markup, string expectedLSPSnippet, int? cursorPosition, ImmutableArray<SnippetPlaceholder> placeholders, TextChange textChange)
        {
            using var workspace = CreateWorkspaceFromCode(markup);
            var document = workspace.CurrentSolution.GetDocument(workspace.Documents.First().Id);
            var lspSnippetString = await RoslynLSPSnippetConverter.GenerateLSPSnippetAsync(document, cursorPosition!.Value, placeholders, textChange).ConfigureAwait(false);

            AssertEx.EqualOrDiff(expectedLSPSnippet, lspSnippetString);
        }
    }
}
