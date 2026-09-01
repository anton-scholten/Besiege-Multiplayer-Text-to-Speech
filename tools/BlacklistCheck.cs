// Walks a built assembly for references the Besiege mod loader refuses, and
// reports them with the method they appear in.
//
// The lists here are read straight out of
// InternalModding.Assemblies.AssemblyScanner in Assembly-CSharp: a prefix test
// over (namespace + "." + typeName), a set of exact type names exempted from
// it, and four individually forbidden methods. There is also a separate,
// dedicated P/Invoke refusal ("You are not allowed to use PInvoke!"), which is
// why DECtalk and every other native library is unreachable from a mod.
//
// Compiled and run by tools/build.sh. Not part of the mod.

using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

public static class BlacklistCheck
{
    private static readonly string[] Blacklist = new string[]
    {
        "System.IO", "System.Net", "System.Xml", "System.Reflection",
        "System.Runtime.InteropServices", "System.Diagnostics", "System.Security",
        "Mono.CSharp", "Mono.Cecil", "System.CodeDom.Compiler",
        "CSharpCompiler", "IKVM", "Microsoft", "Mono.CompilerServices",
        "UnityEngine.WWW", "UnityEngine.MasterServer", "PlayFab",
        "Steamworks", "GameGrind", "InternalModding", "BesiegeDlc",
    };

    private static readonly string[] Exempt = new string[]
    {
        "System.IO.Stream", "System.IO.TextWriter", "System.IO.TextReader",
        "System.IO.BinaryWriter", "System.IO.BinaryReader", "System.IO.MemoryStream",
        "System.IO.Path", "System.IO.SeekOrigin", "System.Diagnostics.Stopwatch",
        "System.Security.Cryptography", "Mono.CSharp.Tuple`2", "Mono.CSharp.Tuple`3",
    };

    private static readonly string[] ForbiddenMethods = new string[]
    {
        "XmlSaver.Save", "LevelXMLSaver.Create",
        "UnityEngine.AssetBundle.LoadFromFile", "UnityEngine.AssetBundle.LoadFromFileAsync",
    };

    private static readonly List<string> problems = new List<string>();

    public static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: blacklist <assembly.dll>");
            return 2;
        }

        AssemblyDefinition asm = AssemblyDefinition.ReadAssembly(args[0]);

        CheckEntryPoint(asm);

        foreach (ModuleDefinition module in asm.Modules)
        {
            foreach (TypeDefinition type in AllTypes(module.Types))
            {
                foreach (FieldDefinition f in type.Fields)
                    CheckType(f.FieldType, type.FullName + "::" + f.Name, "field type");

                foreach (MethodDefinition m in type.Methods)
                {
                    string where = type.FullName + "::" + m.Name;

                    if (m.IsPInvokeImpl || (m.PInvokeInfo != null))
                        problems.Add(where + ": P/Invoke -- the loader refuses this outright");

                    CheckType(m.ReturnType, where, "return type");
                    foreach (ParameterDefinition p in m.Parameters)
                        CheckType(p.ParameterType, where, "parameter");

                    if (!m.HasBody) continue;

                    foreach (VariableDefinition v in m.Body.Variables)
                        CheckType(v.VariableType, where, "local");

                    foreach (Instruction i in m.Body.Instructions)
                    {
                        MemberReference member = i.Operand as MemberReference;
                        if (member == null) continue;

                        MethodReference called = member as MethodReference;
                        if (called != null)
                        {
                            CheckType(called.DeclaringType, where, "calls");
                            CheckForbiddenMethod(called, where);
                            continue;
                        }

                        FieldReference field = member as FieldReference;
                        if (field != null)
                        {
                            CheckType(field.DeclaringType, where, "reads");
                            continue;
                        }

                        TypeReference typeRef = member as TypeReference;
                        if (typeRef != null) CheckType(typeRef, where, "references");
                    }
                }
            }
        }

        if (problems.Count == 0)
        {
            Console.WriteLine("   blacklist: clean");
            return 0;
        }

        Console.Error.WriteLine("   blacklist: " + problems.Count + " problem(s)");
        problems.Sort();
        string last = null;
        foreach (string p in problems)
        {
            if (p == last) continue;      // the same call in a loop, once
            last = p;
            Console.Error.WriteLine("     " + p);
        }
        Console.Error.WriteLine();
        Console.Error.WriteLine("   The loader would refuse this assembly and the mod would simply");
        Console.Error.WriteLine("   not appear in game, with the reason only in Player.log.");
        return 1;
    }

    /// <summary>
    /// The loader collects every type extending Modding.ModEntryPoint, throws
    /// "Too many types extending ModEntryPoint!" if there is more than one,
    /// and builds the one it finds with Activator.CreateInstance -- so it
    /// needs a public parameterless constructor. None of that is visible at
    /// compile time, and with no entry point at all the mod loads and silently
    /// does nothing.
    /// </summary>
    private static void CheckEntryPoint(AssemblyDefinition asm)
    {
        List<TypeDefinition> found = new List<TypeDefinition>();

        foreach (ModuleDefinition module in asm.Modules)
        {
            foreach (TypeDefinition t in AllTypes(module.Types))
            {
                if (t.BaseType == null) continue;
                if (t.BaseType.FullName != "Modding.ModEntryPoint") continue;
                found.Add(t);
            }
        }

        if (found.Count == 0)
        {
            problems.Add("no type extends Modding.ModEntryPoint -- "
                         + "the mod would load and do nothing");
            return;
        }
        if (found.Count > 1)
        {
            string names = "";
            for (int i = 0; i < found.Count; i++)
            {
                if (i > 0) names += ", ";
                names += found[i].FullName;
            }
            problems.Add("more than one type extends Modding.ModEntryPoint ("
                         + names + ") -- the loader throws on this");
            return;
        }

        TypeDefinition entry = found[0];
        bool constructible = false;
        foreach (MethodDefinition m in entry.Methods)
        {
            if (m.IsConstructor && m.IsPublic && m.Parameters.Count == 0)
                constructible = true;
        }
        if (!constructible)
        {
            problems.Add(entry.FullName + ": no public parameterless constructor, "
                         + "so Activator.CreateInstance would fail");
            return;
        }

        Console.WriteLine("   entry point: " + entry.FullName);
    }

    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
    {
        foreach (TypeDefinition t in types)
        {
            yield return t;
            foreach (TypeDefinition nested in AllTypes(t.NestedTypes))
                yield return nested;
        }
    }

    private static void CheckType(TypeReference type, string where, string how)
    {
        if (type == null) return;

        // Unwrap arrays, byrefs, pointers and generic instances so the check
        // sees List<System.IO.File> as well as System.IO.File.
        GenericInstanceType generic = type as GenericInstanceType;
        if (generic != null)
        {
            CheckType(generic.ElementType, where, how);
            foreach (TypeReference arg in generic.GenericArguments)
                CheckType(arg, where, how);
            return;
        }

        TypeSpecification spec = type as TypeSpecification;
        if (spec != null) { CheckType(spec.ElementType, where, how); return; }

        string full = type.FullName;
        if (string.IsNullOrEmpty(full)) return;

        // The scanner tests (namespace + "." + typeName), and a nested type is
        // spelled with '/' in Cecil's FullName; normalise so a nested type
        // under a blacklisted namespace is still caught.
        string normalised = full.Replace('/', '.');

        for (int i = 0; i < Exempt.Length; i++)
        {
            if (normalised == Exempt[i]) return;
        }

        for (int i = 0; i < Blacklist.Length; i++)
        {
            if (!normalised.StartsWith(Blacklist[i], StringComparison.Ordinal)) continue;
            problems.Add(where + ": " + how + " " + normalised
                         + "  (blacklisted prefix '" + Blacklist[i] + "')");
            return;
        }
    }

    private static void CheckForbiddenMethod(MethodReference method, string where)
    {
        if (method.DeclaringType == null) return;
        string name = method.DeclaringType.FullName.Replace('/', '.') + "." + method.Name;

        for (int i = 0; i < ForbiddenMethods.Length; i++)
        {
            if (name.EndsWith(ForbiddenMethods[i], StringComparison.Ordinal))
                problems.Add(where + ": calls forbidden method " + ForbiddenMethods[i]);
        }
    }
}
