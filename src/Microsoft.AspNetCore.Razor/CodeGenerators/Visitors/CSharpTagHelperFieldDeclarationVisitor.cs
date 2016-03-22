// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Razor.Chunks;
using Microsoft.Extensions.Internal;

namespace Microsoft.AspNetCore.Razor.CodeGenerators.Visitors
{
    public class CSharpTagHelperFieldDeclarationVisitor : CodeVisitor<CSharpCodeWriter>
    {
        private const string _preAllocatedAttributeVariablePrefix = "__tagHelperAttribute_";
        private readonly HashSet<string> _declaredTagHelpers;
        private readonly Dictionary<TagHelperAttributeKey, string> _preAllocatedAttributes;
        private readonly GeneratedTagHelperContext _tagHelperContext;
        private bool _foundTagHelpers;

        public CSharpTagHelperFieldDeclarationVisitor(
            CSharpCodeWriter writer,
            CodeGeneratorContext context)
            : base(writer, context)
        {
            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            _declaredTagHelpers = new HashSet<string>(StringComparer.Ordinal);
            _tagHelperContext = Context.Host.GeneratedClassContext.GeneratedTagHelperContext;
            _preAllocatedAttributes = new Dictionary<TagHelperAttributeKey, string>();
        }

        protected override void Visit(TagHelperChunk chunk)
        {
            // We only want to setup tag helper manager fields if there are tag helpers, and only once
            if (!_foundTagHelpers)
            {
                _foundTagHelpers = true;

                // We want to hide declared TagHelper fields so they cannot be stepped over via a debugger.
                Writer.WriteLineHiddenDirective();

                // Runtime fields aren't useful during design time.
                if (!Context.Host.DesignTimeMode)
                {
                    // Need to disable the warning "X is assigned to but never used." for the value buffer since
                    // whether it's used depends on how a TagHelper is used.
                    Writer.WritePragma("warning disable 0414");
                    WritePrivateField(
                        _tagHelperContext.TagHelperContentTypeName,
                        CSharpTagHelperCodeRenderer.StringValueBufferVariableName,
                        value: null);
                    Writer.WritePragma("warning restore 0414");

                    WritePrivateField(
                        _tagHelperContext.ExecutionContextTypeName,
                        CSharpTagHelperCodeRenderer.ExecutionContextVariableName,
                        value: null);

                    WritePrivateField(
                        _tagHelperContext.RunnerTypeName,
                        CSharpTagHelperCodeRenderer.RunnerVariableName,
                        value: null);

                    Writer
                        .Write("private global::")
                        .Write(_tagHelperContext.ScopeManagerTypeName)
                        .Write(" ")
                        .WriteStartAssignment(CSharpTagHelperCodeRenderer.ScopeManagerVariableName)
                        .WriteStartNewObject("global::" + _tagHelperContext.ScopeManagerTypeName)
                        .WriteEndMethodInvocation();
                }
            }

            foreach (var descriptor in chunk.Descriptors)
            {
                if (!_declaredTagHelpers.Contains(descriptor.TypeName))
                {
                    _declaredTagHelpers.Add(descriptor.TypeName);

                    WritePrivateField(
                        descriptor.TypeName,
                        CSharpTagHelperCodeRenderer.GetVariableName(descriptor),
                        value: null);
                }
            }

            if (!Context.Host.DesignTimeMode)
            {
                PreAllocateUnboundTagHelperAttributes(chunk);
            }

            // We need to dive deeper to ensure we pick up any nested tag helpers.
            Accept(chunk.Children);
        }

        private void PreAllocateUnboundTagHelperAttributes(TagHelperChunk chunk)
        {
            var boundAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < chunk.Attributes.Count; i++)
            {
                var attribute = chunk.Attributes[i];
                var hasAssociatedDescriptors = chunk.Descriptors.Any(descriptor =>
                    descriptor.Attributes.Any(attributeDescriptor => attributeDescriptor.IsNameMatch(attribute.Key)));

                // If there's no descriptors associated or we're hitting a bound attribute a second time.
                if (!hasAssociatedDescriptors || !boundAttributes.Add(attribute.Key))
                {
                    string preAllocatedAttributeVariableName = null;

                    if (attribute.Value == null)
                    {
                        var preAllocatedAttributeKey = new TagHelperAttributeKey(attribute.Key, value: null);
                        if (TryCachePreallocatedVariableName(preAllocatedAttributeKey, out preAllocatedAttributeVariableName))
                        {
                            Writer
                                .Write("private static readonly global::")
                                .Write(_tagHelperContext.TagHelperAttributeTypeName)
                                .Write(" ")
                                .Write(preAllocatedAttributeVariableName)
                                .Write(" = ")
                                .WriteStartNewObject("global::" + _tagHelperContext.TagHelperAttributeTypeName)
                                .WriteStringLiteral(attribute.Key)
                                .WriteEndMethodInvocation();
                        }
                    }
                    else
                    {
                        string plainText;
                        if (CSharpTagHelperCodeRenderer.TryGetPlainTextValue(attribute.Value, out plainText))
                        {
                            var preAllocatedAttributeKey = new TagHelperAttributeKey(attribute.Key, plainText);
                            if (TryCachePreallocatedVariableName(preAllocatedAttributeKey, out preAllocatedAttributeVariableName))
                            {
                                Writer
                                    .Write("private static readonly global::")
                                    .Write(_tagHelperContext.TagHelperAttributeTypeName)
                                    .Write(" ")
                                    .Write(preAllocatedAttributeVariableName)
                                    .Write(" = ")
                                    .WriteStartNewObject("global::" + _tagHelperContext.TagHelperAttributeTypeName)
                                    .WriteStringLiteral(attribute.Key)
                                    .WriteParameterSeparator()
                                    .WriteStartNewObject("global::" + _tagHelperContext.EncodedHtmlStringTypeName)
                                    .WriteStringLiteral(plainText)
                                    .WriteEndMethodInvocation(endLine: false)
                                    .WriteEndMethodInvocation();
                            }
                        }
                    }

                    if (preAllocatedAttributeVariableName != null)
                    {
                        chunk.Attributes[i] = new KeyValuePair<string, Chunk>(
                            attribute.Key,
                            new PreallocatedTagHelperAttributeChunk
                            {
                                AttributeVariableAccessor = preAllocatedAttributeVariableName
                            });
                    }
                }
            }
        }

        public override void Accept(Chunk chunk)
        {
            if (chunk == null)
            {
                throw new ArgumentNullException(nameof(chunk));
            }

            var parentChunk = chunk as ParentChunk;

            // If we're any ParentChunk other than TagHelperChunk then we want to dive into its Children
            // to search for more TagHelperChunk chunks. This if-statement enables us to not override
            // each of the special ParentChunk types and then dive into their children.
            if (parentChunk != null && !(parentChunk is TagHelperChunk))
            {
                Accept(parentChunk.Children);
            }
            else
            {
                // If we're a TagHelperChunk or any other non ParentChunk we ".Accept" it. This ensures
                // that our overridden Visit(TagHelperChunk) method gets called and is not skipped over.
                // If we're a non ParentChunk or a TagHelperChunk then we want to just invoke the Visit
                // method for that given chunk (base.Accept indirectly calls the Visit method).
                base.Accept(chunk);
            }
        }

        private bool TryCachePreallocatedVariableName(TagHelperAttributeKey key, out string preAllocatedAttributeVariableName)
        {
            if (!_preAllocatedAttributes.TryGetValue(key, out preAllocatedAttributeVariableName))
            {
                preAllocatedAttributeVariableName = _preAllocatedAttributeVariablePrefix + _preAllocatedAttributes.Count;
                _preAllocatedAttributes[key] = preAllocatedAttributeVariableName;
                return true;
            }

            return false;
        }

        private void WritePrivateField(string type, string name, string value)
        {
            Writer
                .Write("private global::")
                .WriteVariableDeclaration(type, name, value);
        }

        private struct TagHelperAttributeKey : IEquatable<TagHelperAttributeKey>
        {
            public TagHelperAttributeKey(string name, string value)
            {
                Name = name;
                Value = value;
            }

            public string Name { get; }

            public string Value { get; }

            public override int GetHashCode()
            {
                var hashCodeCombiner = HashCodeCombiner.Start();
                hashCodeCombiner.Add(Name, StringComparer.Ordinal);
                hashCodeCombiner.Add(Value, StringComparer.Ordinal);

                return hashCodeCombiner.CombinedHash;
            }

            public override bool Equals(object obj)
            {
                var other = obj as TagHelperAttributeKey?;

                if (other != null)
                {
                    return Equals(other.Value);
                }

                return false;
            }

            public bool Equals(TagHelperAttributeKey other)
            {
                return string.Equals(Name, other.Name, StringComparison.Ordinal) &&
                    string.Equals(Value, other.Value, StringComparison.Ordinal);
            }
        }
    }
}