// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Microsoft.AspNetCore.Razor.Language.Legacy
{
    internal class ClassifiedSpanWriter
    {
        private readonly TextWriter _writer;

        public ClassifiedSpanWriter(TextWriter writer)
        {
            _writer = writer;
        }

        public virtual void Visit(SyntaxTreeNode node)
        {
            var classifiedSpans = GetClassifiedSpans(node);
            foreach (var span in classifiedSpans)
            {
                VisitClassifiedSpan(span);
                WriteNewLine();
            }
        }

        public virtual void VisitClassifiedSpan(ClassifiedSpanInternal span)
        {
            WriteClassifiedSpan(span);
        }

        protected void WriteClassifiedSpan(ClassifiedSpanInternal span)
        {
            Write($"{span.SpanKind} span at {span.Span} (Accepts:{span.AcceptedCharacters})");
            WriteSeparator();
            Write($"Parent: {span.BlockKind} block at {span.BlockSpan}");
        }

        protected void WriteSeparator()
        {
            Write(" - ");
        }

        protected void WriteNewLine()
        {
            _writer.WriteLine();
        }

        protected void Write(object value)
        {
            _writer.Write(value);
        }

        internal static IReadOnlyList<ClassifiedSpanInternal> GetClassifiedSpans(SyntaxTreeNode root)
        {
            var spans = Flatten(root);

            var result = new ClassifiedSpanInternal[spans.Count];
            for (var i = 0; i < spans.Count; i++)
            {
                var span = spans[i];
                result[i] = new ClassifiedSpanInternal(
                    new SourceSpan(
                        span.Start.FilePath,
                        span.Start.AbsoluteIndex,
                        span.Start.LineIndex,
                        span.Start.CharacterIndex,
                        span.Length),
                    new SourceSpan(
                        span.Parent.Start.FilePath,
                        span.Parent.Start.AbsoluteIndex,
                        span.Parent.Start.LineIndex,
                        span.Parent.Start.CharacterIndex,
                        span.Parent.Length),
                    span.Kind,
                    span.Parent.Type,
                    span.EditHandler.AcceptedCharacters);
            }

            return result;
        }

        private static List<Span> Flatten(SyntaxTreeNode root)
        {
            var result = new List<Span>();
            AppendFlattenedSpans(root, result);
            return result;

            void AppendFlattenedSpans(SyntaxTreeNode node, List<Span> foundSpans)
            {
                if (node is Span spanNode)
                {
                    foundSpans.Add(spanNode);
                }
                else
                {
                    if (node is TagHelperBlock tagHelperNode)
                    {
                        // These aren't in document order, sort them first and then dig in
                        var attributeNodes = tagHelperNode.Attributes.Select(kvp => kvp.Value).Where(att => att != null).ToList();
                        attributeNodes.Sort((x, y) => x.Start.AbsoluteIndex.CompareTo(y.Start.AbsoluteIndex));

                        foreach (var attribute in attributeNodes)
                        {
                            AppendFlattenedSpans(attribute, foundSpans);
                        }
                    }

                    if (node is Block blockNode)
                    {
                        foreach (var child in blockNode.Children)
                        {
                            AppendFlattenedSpans(child, foundSpans);
                        }
                    }
                }
            }
        }
    }
}
