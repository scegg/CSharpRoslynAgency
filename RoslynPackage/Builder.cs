﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace SecretNest.CSharpRoslynAgency
{
    /// <summary>
    /// Contains the builder for building C# code using Roslyn.
    /// </summary>
    public class Builder
    {
        MetadataReferenceResolver metadataReferenceResolver;
        /// <summary>
        /// Initializes an instance of the Builder.
        /// </summary>
        public Builder()
        {
            metadataReferenceResolver = new MetadataReferenceResolver() { GetMissingAssemblyCallback = GetMissingAssembly };
        }

        /// <summary>
        /// Occurs when a missing assembly / module needs to be resolved. Property MissingAssemblyImage in parameter e should be set before returning if the assembly / module is resolved.
        /// </summary>
        public event EventHandler<MissingAssemblyResolvingEventArgs> MissingAssemblyResolving;

        /// <summary>
        /// Occurs when a missing assembly / module needs to be resolved before querying from cache. Property MissingAssemblyImage in parameter e should be set before returning if the assembly / module is resolved.
        /// </summary>
        public event EventHandler<MissingAssemblyResolvingEventArgs> MissingAssemblyResolvingBeforeCache;

        //long EightBytesToLong(byte[] data)
        //{
        //    return BitConverter.ToInt64(data, 0);
        //}

        //bool ByteArrayCompare(byte[] a, byte[] b)
        //{
        //    if (a == null)
        //    {
        //        if (b == null)
        //            return true;
        //        else
        //            return false;
        //    }
        //    else
        //    {
        //        if (b == null)
        //            return false;
        //    }
        //    if (a.Length != b.Length)
        //        return false;
        //    for (int i = 0; i < a.Length; i++)
        //    {
        //        if (a[i] != b[i])
        //            return false;
        //    }
        //    return true;
        //}

        PortableExecutableReference GetFromEvent(EventHandler<MissingAssemblyResolvingEventArgs> handler, string display, string fullName, AssemblyReference assemblyReference)
        {
            if (handler != null)
            {
                var e = new MissingAssemblyResolvingEventArgs(display, assemblyReference);
                handler(this, e);
                if (e.MissingAssemblyImage != null)
                {
                    LoadIntoAssemblyCache(fullName, e.MissingAssemblyImage);
                    var result = assemblyReference.BuildPortableExecutableReference(e.MissingAssemblyImage);
#if DEBUG
                    Console.WriteLine("Got Assembly: " + fullName);
#endif
                    //metadataReferenceCache[fullName] = result;
                    return result;
                }
            }
            return null;
        }

        PortableExecutableReference GetMissingAssembly(string display, AssemblyReference assemblyReference)
        {
            string fullName = assemblyReference.Name.FullName;

            var beforeCache = GetFromEvent(MissingAssemblyResolvingBeforeCache, display, fullName, assemblyReference);
            if (beforeCache != null)
            {
                return beforeCache;
            }

//            if (metadataReferenceCache.TryGetValue(fullName, out var match))
//            {
//#if DEBUG
//                Console.WriteLine("Got Assembly From Cache: " + fullName);
//#endif
//                return match;
//            }

            if (assemblyImageCache.TryGetValue(fullName, out var image))
            {
                var result = assemblyReference.BuildPortableExecutableReference(image);
                //metadataReferenceCache[fullName] = result;
                return result;
            }

            return GetFromEvent(MissingAssemblyResolving, display, fullName, assemblyReference);
        }

        Dictionary<string, byte[]> assemblyImageCache = new Dictionary<string, byte[]>();

        /// <summary>
        /// Clears the assembly cache.
        /// </summary>
        public void ClearAssemblyCache()
        {
            //metadataReferenceCache.Clear();
            assemblyImageCache.Clear();
        }

        /// <summary>
        /// Loads image into assembly cache.
        /// </summary>
        /// <param name="fullName">Full name of the assembly / module</param>
        /// <param name="image">PE format image of the assembly / module</param>
        public void LoadIntoAssemblyCache(string fullName, byte[] image)
        {
            assemblyImageCache[fullName] = image;
        }

        /// <summary>
        /// Returns names of all cached assemblies and modules.
        /// </summary>
        /// <returns>Names of all cached assemblies and modules</returns>
        public IEnumerable<string> GetAllCachedAssemblyNames()
        {
            return assemblyImageCache.Keys;
        }

        /// <summary>
        /// Removes an cached assembly / module.
        /// </summary>
        /// <param name="fullName">Full name of the assembly / module</param>
        /// <returns>Whether the element is successfully found and removed.</returns>
        public bool RemoveFromAssemblyCache(string fullName)
        {
            return assemblyImageCache.Remove(fullName);
        }

        /// <summary>
        /// Checks whether the assembly / module specified is cached.
        /// </summary>
        /// <param name="fullName">Full name of the assembly / module</param>
        /// <returns>Whether the assembly / module specified is cached.</returns>
        public bool CheckExistedInAssemblyCache(string fullName)
        {
            return assemblyImageCache.ContainsKey(fullName);
        }

        /// <summary>
        /// Builds source code.
        /// </summary>
        /// <param name="assemblyName">Name of the creating assembly</param>
        /// <param name="sourceCode">Source code. Each element represents a source code file.</param>
        /// <param name="references">Assembly references for building this assembly</param>
        /// <param name="assemblyImage">Image of the created assembly</param>
        /// <param name="errors">Building errors</param>
        /// <returns>Whether the building procedure is succeeded</returns>
        public bool Build(string assemblyName, IEnumerable<string> sourceCode, IEnumerable<AssemblyReference> references, out byte[] assemblyImage, out BuildingError[] errors)
        {
            List<SyntaxTree> syntaxTrees = new List<SyntaxTree>();
            foreach (var code in sourceCode)
                syntaxTrees.Add(CSharpSyntaxTree.ParseText(code));
            List<MetadataReference> metadataReferences = new List<MetadataReference>();
            foreach (var name in references)
            {
                var item = GetMissingAssembly(name.Name.FullName, name);

                if (item != null)
                {
                    metadataReferences.Add(item);
                }
            }
            CSharpCompilation compilation = CSharpCompilation.Create(
                assemblyName,
                syntaxTrees: syntaxTrees,
                references: metadataReferences,
                options: new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    metadataReferenceResolver: metadataReferenceResolver));
#if DEBUG
            Console.WriteLine("Begin Emit");
#endif
            using (MemoryStream ms = new MemoryStream())
            {
                EmitResult result = compilation.Emit(ms);
                if (!result.Success)
                {
                    IEnumerable<Diagnostic> failures = result.Diagnostics.Where(diagnostic =>
                            diagnostic.IsWarningAsError ||
                            diagnostic.Severity == DiagnosticSeverity.Error);
                    errors = failures.Select(i => new BuildingError(i.Id, i.GetMessage(), i.Location.ToString())).ToArray();
                    assemblyImage = null;
                    return false;
                }
                else
                {
                    ms.Seek(0, SeekOrigin.Begin);
                    assemblyImage = ms.ToArray();
                    errors = null;
                    return true;
                }
            }
        }
    }
}
