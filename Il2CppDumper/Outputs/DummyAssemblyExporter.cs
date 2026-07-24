using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;

namespace Il2CppDumper
{
    public static class DummyAssemblyExporter
    {
        public static void Export(Il2CppExecutor il2CppExecutor, string outputDir, bool addToken)
        {
            Directory.SetCurrentDirectory(outputDir);
            if (Directory.Exists("DummyDll"))
                Directory.Delete("DummyDll", true);
            Directory.CreateDirectory("DummyDll");
            Directory.SetCurrentDirectory("DummyDll");
            var dummy = new DummyAssemblyGenerator(il2CppExecutor, addToken);
            foreach (var assembly in dummy.Assemblies)
            {
                try
                {
                    using var stream = new MemoryStream();
                    assembly.Write(stream);
                    File.WriteAllBytes(assembly.MainModule.Name, stream.ToArray());
                }
                catch
                {
                    try
                    {
                        StripBadCustomAttributes(assembly);
                        using var stream = new MemoryStream();
                        assembly.Write(stream);
                        File.WriteAllBytes(assembly.MainModule.Name, stream.ToArray());
                        Console.WriteLine($"WARNING: {assembly.MainModule.Name} written with some custom attributes stripped.");
                    }
                    catch
                    {
                        try
                        {
                            StripUnresolvableConstants(assembly);
                            using var stream = new MemoryStream();
                            assembly.Write(stream);
                            File.WriteAllBytes(assembly.MainModule.Name, stream.ToArray());
                            Console.WriteLine($"WARNING: {assembly.MainModule.Name} written with attributes and some constants stripped.");
                        }
                        catch (Exception e3)
                        {
                            Console.WriteLine($"WARNING: Failed to write {assembly.MainModule.Name}: {e3.Message}");
                        }
                    }
                }
            }
        }

        private static void StripBadCustomAttributes(AssemblyDefinition assembly)
        {
            foreach (var type in assembly.MainModule.Types.SelectMany(GetAllTypes))
            {
                StripArgAttrs(type.CustomAttributes);
                foreach (var field in type.Fields)
                    StripArgAttrs(field.CustomAttributes);
                foreach (var method in type.Methods)
                {
                    StripArgAttrs(method.CustomAttributes);
                    foreach (var param in method.Parameters)
                        StripArgAttrs(param.CustomAttributes);
                }
                foreach (var prop in type.Properties)
                    StripArgAttrs(prop.CustomAttributes);
                foreach (var evt in type.Events)
                    StripArgAttrs(evt.CustomAttributes);
            }
        }

        private static void StripArgAttrs(Mono.Collections.Generic.Collection<CustomAttribute> attrs)
        {
            for (int i = attrs.Count - 1; i >= 0; i--)
            {
                var a = attrs[i];
                if (HasNonPrimitiveArg(a))
                    attrs.RemoveAt(i);
            }
        }

        private static bool HasNonPrimitiveArg(CustomAttribute attr)
        {
            foreach (var arg in attr.ConstructorArguments)
                if (IsNonPrimitive(arg)) return true;
            foreach (var named in attr.Fields)
                if (IsNonPrimitive(named.Argument)) return true;
            foreach (var named in attr.Properties)
                if (IsNonPrimitive(named.Argument)) return true;
            return false;
        }

        private static bool IsNonPrimitive(CustomAttributeArgument arg)
        {
            var t = arg.Type;
            if (t == null) return false;
            if (t.IsPrimitive || t.FullName == "System.String" || t.FullName == "System.Type") return false;
            if (t.FullName == "System.Object")
            {
                if (arg.Value is CustomAttributeArgument inner)
                    return IsNonPrimitive(inner);
                return false;
            }
            if (t is ArrayType at)
            {
                var et = at.ElementType;
                if (et.IsPrimitive || et.FullName == "System.String" || et.FullName == "System.Type") return false;
            }
            return true;
        }

        private static void StripUnresolvableConstants(AssemblyDefinition assembly)
        {
            foreach (var type in assembly.MainModule.Types.SelectMany(GetAllTypes))
            {
                foreach (var field in type.Fields)
                {
                    if (field.HasConstant)
                    {
                        try
                        {
                            if (field.FieldType.Resolve() == null)
                                field.Constant = null;
                        }
                        catch { field.Constant = null; }
                    }
                }
                foreach (var method in type.Methods)
                {
                    foreach (var param in method.Parameters)
                    {
                        if (param.HasConstant)
                        {
                            try
                            {
                                if (param.ParameterType.Resolve() == null)
                                    param.Constant = null;
                            }
                            catch { param.Constant = null; }
                        }
                    }
                }
                type.CustomAttributes.Clear();
                foreach (var field in type.Fields)
                    field.CustomAttributes.Clear();
                foreach (var method in type.Methods)
                {
                    method.CustomAttributes.Clear();
                    foreach (var param in method.Parameters)
                        param.CustomAttributes.Clear();
                }
                foreach (var prop in type.Properties)
                    prop.CustomAttributes.Clear();
                foreach (var evt in type.Events)
                    evt.CustomAttributes.Clear();
            }
        }

        private static IEnumerable<TypeDefinition> GetAllTypes(TypeDefinition type)
        {
            yield return type;
            foreach (var nested in type.NestedTypes)
                foreach (var t in GetAllTypes(nested))
                    yield return t;
        }
    }
}
