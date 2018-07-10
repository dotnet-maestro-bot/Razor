// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;

namespace Microsoft.AspNetCore.Razor.Language.Legacy
{
    internal class ClassifiedSpanSerializer
    {
        internal static string Serialize(SyntaxTreeNode node)
        {
            using (var writer = new StringWriter())
            {
                var visitor = new ClassifiedSpanWriter(writer);
                visitor.Visit(node);

                return writer.ToString();
            }
        }
    }
}
