using Mono.Cecil;

namespace Il2CppDumper
{
    public class MyAssemblyResolver : DefaultAssemblyResolver
    {
        public void Register(AssemblyDefinition assembly)
        {
            RegisterAssembly(assembly);
        }

        public override AssemblyDefinition Resolve(AssemblyNameReference name)
        {
            try
            {
                return base.Resolve(name);
            }
            catch (AssemblyResolutionException)
            {
                var asm = AssemblyDefinition.CreateAssembly(
                    new AssemblyNameDefinition(name.Name, name.Version),
                    name.Name + ".dll",
                    ModuleKind.Dll);
                RegisterAssembly(asm);
                return asm;
            }
        }
    }
}
