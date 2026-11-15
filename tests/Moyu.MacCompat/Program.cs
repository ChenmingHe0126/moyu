using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Moyu.MacCompat
{
  /// <summary>
  /// dotnet run -- path/to/Moyu.gha "path/to/Rhino 8.app" [allowed.txt]
  /// Resolves every type, method and field reference in the plug-in against the .app's assemblies (then this .NET's
  /// own framework for what the .app does not carry) and lists what is missing, with the methods that use it.
  /// A member listed in allowed.txt (one full name per line, # comments) is reported but does not fail the check:
  /// it is only reached on Windows.
  /// </summary>
  static class Program
  {
    static int Main(string[] args)
    {
      if (args.Length < 2)
      {
        Console.WriteLine("usage: Moyu.MacCompat <Moyu.gha> <Rhino 8.app folder> [allowed.txt]");
        return 2;
      }
      string gha = Path.GetFullPath(args[0]), app = Path.GetFullPath(args[1]);
      var allowed = args.Length > 2 && File.Exists(args[2])
        ? new HashSet<string>(File.ReadAllLines(args[2]).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")))
        : new HashSet<string>();

      var resolver = new AppResolver(app);
      Console.WriteLine($"Rhino app: {app} ({resolver.Count} assemblies)");
      using (var module = ModuleDefinition.ReadModule(gha, new ReaderParameters { AssemblyResolver = resolver }))
      {
        Console.WriteLine($"Plug-in:   {module.Assembly.FullName}");
        bool versionsOk = References(module, resolver);
        var missing = Scan(module);
        PInvokes(module);

        int failing = 0;
        Console.WriteLine();
        if (missing.Count == 0) Console.WriteLine("Every type, method and field the plug-in uses exists in Rhino for Mac.");
        foreach (var group in missing.OrderBy(m => m.Key))
        {
          bool ok = allowed.Contains(group.Key);
          if (!ok) failing++;
          Console.WriteLine($"{(ok ? "ALLOWED (Windows only)" : "MISSING")}: {group.Key}");
          foreach (var user in group.Value.Take(6)) Console.WriteLine($"    used in {user}");
          if (group.Value.Count > 6) Console.WriteLine($"    … and {group.Value.Count - 6} more");
        }
        Console.WriteLine();
        Console.WriteLine(failing == 0 && versionsOk ? "MAC CHECK PASSED" : $"MAC CHECK FAILED: {failing} missing, versions {(versionsOk ? "ok" : "too old")}");
        return failing == 0 && versionsOk ? 0 : 1;
      }
    }

    /// <summary>Each assembly the plug-in references, where Rhino for Mac has it and in which version.</summary>
    static bool References(ModuleDefinition module, AppResolver resolver)
    {
      bool ok = true;
      Console.WriteLine();
      Console.WriteLine("Referenced assemblies:");
      foreach (var r in module.AssemblyReferences.OrderBy(r => r.Name))
      {
        var found = resolver.Find(r.Name, out string where);
        if (found == null)
        {
          ok = false;
          Console.WriteLine($"  {r.Name} {r.Version}: NOT FOUND");
          continue;
        }
        bool older = found.Name.Version < r.Version;
        // An app-local assembly older than the reference can still bind (Rhino resolves plug-in references by name), but
        // a framework assembly cannot.
        if (older && where == "framework") ok = false;
        Console.WriteLine($"  {r.Name} {r.Version} -> {found.Name.Version} ({where}){(older ? "  OLDER THAN REFERENCED" : "")}");
      }
      return ok;
    }

    static void PInvokes(ModuleDefinition module)
    {
      var natives = module.Types.SelectMany(AllTypes).SelectMany(t => t.Methods)
        .Where(m => m.HasPInvokeInfo).GroupBy(m => m.PInvokeInfo.Module.Name).OrderBy(g => g.Key);
      Console.WriteLine();
      Console.WriteLine("Native libraries called (Windows ones must only be reached on Windows):");
      foreach (var g in natives) Console.WriteLine($"  {g.Key}: {string.Join(", ", g.Select(m => m.PInvokeInfo.EntryPoint ?? m.Name).Distinct())}");
    }

    static IEnumerable<TypeDefinition> AllTypes(TypeDefinition t) => new[] { t }.Concat(t.NestedTypes.SelectMany(AllTypes));

    /// <summary>Missing member → the plug-in methods (or types) that use it.</summary>
    static Dictionary<string, List<string>> Scan(ModuleDefinition module)
    {
      var missing = new Dictionary<string, List<string>>();
      var seen = new Dictionary<string, bool>();

      void Note(string member, string user)
      {
        if (!missing.TryGetValue(member, out var users)) missing[member] = users = new List<string>();
        if (!users.Contains(user)) users.Add(user);
      }

      bool TypeOk(TypeReference t, string user)
      {
        switch (t)
        {
          case null: return true;
          case GenericParameter _: return true;
          case TypeSpecification spec when !(spec is GenericInstanceType):
            return TypeOk(spec.ElementType, user);
          case GenericInstanceType g:
            bool all = TypeOk(g.ElementType, user);
            foreach (var a in g.GenericArguments) all &= TypeOk(a, user);
            return all;
        }
        if (t.Module == module && t is TypeDefinition) return true;
        string key = t.FullName;
        if (!seen.TryGetValue("T:" + key, out bool ok))
        {
          ok = Safe(() => t.Resolve()) != null;
          seen["T:" + key] = ok;
        }
        if (!ok) Note($"type {key} [{Scope(t)}]", user);
        return ok;
      }

      void MethodOk(MethodReference m, string user)
      {
        if (m is GenericInstanceMethod gim)
        {
          foreach (var a in gim.GenericArguments) TypeOk(a, user);
          m = gim.ElementMethod;
        }
        if (m.DeclaringType is ArrayType) return; // Get/Set/Address on multi-dimensional arrays are runtime-provided.
        if (!TypeOk(m.DeclaringType, user)) return;
        if (m is MethodDefinition md && md.Module == module) return;
        string key = m.FullName;
        if (!seen.TryGetValue("M:" + key, out bool ok))
        {
          ok = Safe(() => m.Resolve()) != null;
          seen["M:" + key] = ok;
        }
        if (!ok) Note($"method {key} [{Scope(m.DeclaringType)}]", user);
      }

      void FieldOk(FieldReference f, string user)
      {
        if (!TypeOk(f.DeclaringType, user)) return;
        if (f is FieldDefinition fd && fd.Module == module) return;
        string key = f.FullName;
        if (!seen.TryGetValue("F:" + key, out bool ok))
        {
          ok = Safe(() => f.Resolve()) != null;
          seen["F:" + key] = ok;
        }
        if (!ok) Note($"field {key} [{Scope(f.DeclaringType)}]", user);
      }

      void AttributesOk(ICustomAttributeProvider p, string user)
      {
        if (!p.HasCustomAttributes) return;
        foreach (var a in p.CustomAttributes) MethodOk(a.Constructor, user);
      }

      foreach (var type in module.Types.SelectMany(AllTypes))
      {
        string tname = type.FullName;
        TypeOk(type.BaseType, tname);
        foreach (var i in type.Interfaces) TypeOk(i.InterfaceType, tname);
        AttributesOk(type, tname);
        foreach (var f in type.Fields) TypeOk(f.FieldType, $"{tname}.{f.Name}");
        foreach (var p in type.Properties) AttributesOk(p, $"{tname}.{p.Name}");
        foreach (var method in type.Methods)
        {
          string user = $"{tname}::{method.Name}";
          AttributesOk(method, user);
          TypeOk(method.ReturnType, user);
          foreach (var p in method.Parameters) TypeOk(p.ParameterType, user);
          foreach (var o in method.Overrides) MethodOk(o, user);
          if (!method.HasBody) continue;
          foreach (var v in method.Body.Variables) TypeOk(v.VariableType, user);
          foreach (var h in method.Body.ExceptionHandlers) TypeOk(h.CatchType, user);
          foreach (var ins in method.Body.Instructions)
          {
            switch (ins.Operand)
            {
              case MethodReference mr: MethodOk(mr, user); break;
              case FieldReference fr: FieldOk(fr, user); break;
              case TypeReference tr: TypeOk(tr, user); break;
              case CallSite cs:
                TypeOk(cs.ReturnType, user);
                foreach (var p in cs.Parameters) TypeOk(p.ParameterType, user);
                break;
            }
          }
        }
      }
      return missing;
    }

    static string Scope(TypeReference t)
    {
      while (t.DeclaringType != null) t = t.DeclaringType;
      return t.Scope?.Name ?? "?";
    }

    static T Safe<T>(Func<T> f) where T : class
    {
      try { return f(); }
      catch (AssemblyResolutionException) { return null; }
    }
  }

  /// <summary>Finds assemblies in the Rhino .app (Rhino's core folder first), then in this .NET's framework folder.</summary>
  sealed class AppResolver : IAssemblyResolver
  {
    readonly Dictionary<string, string> _app = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    readonly string _framework = RuntimeEnvironment.GetRuntimeDirectory();
    readonly Dictionary<string, AssemblyDefinition> _cache = new Dictionary<string, AssemblyDefinition>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> _where = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public AppResolver(string app)
    {
      // Several copies of one assembly can sit in a .app (Rhino's core, each plug-in's folder, reference assemblies for
      // compiling scripts): prefer Rhino's own folders, then plug-in folders, then ref/ folders, then the shallower path.
      string refDir = Path.DirectorySeparatorChar + "ref" + Path.DirectorySeparatorChar;
      var files = Directory.EnumerateFiles(app, "*.dll", SearchOption.AllDirectories)
        .OrderBy(p => p.Contains(refDir, StringComparison.OrdinalIgnoreCase) ? 2 : p.Contains("PlugIns", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
        .ThenBy(p => p.Count(c => c == Path.DirectorySeparatorChar));
      foreach (var file in files)
      {
        string name = Path.GetFileNameWithoutExtension(file);
        if (!_app.ContainsKey(name)) _app[name] = file;
      }
    }

    public int Count => _app.Count;

    public AssemblyDefinition Find(string name, out string where)
    {
      var a = Load(name);
      where = a != null && _where.TryGetValue(name, out var w) ? w : null;
      return a;
    }

    AssemblyDefinition Load(string name)
    {
      if (_cache.TryGetValue(name, out var cached)) return cached;
      string path = null, where = null;
      if (_app.TryGetValue(name, out var inApp)) { path = inApp; where = Relative(inApp); }
      else
      {
        string fw = Path.Combine(_framework, name + ".dll");
        if (File.Exists(fw)) { path = fw; where = "framework"; }
      }
      AssemblyDefinition def = null;
      if (path != null)
      {
        try { def = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = this }); }
        catch (BadImageFormatException) { def = null; }
      }
      _cache[name] = def;
      if (where != null) _where[name] = where;
      return def;
    }

    string Relative(string path)
    {
      int i = path.IndexOf(".app" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
      return i >= 0 ? path.Substring(i + 5) : path;
    }

    public AssemblyDefinition Resolve(AssemblyNameReference name) => Load(name.Name) ?? throw new AssemblyResolutionException(name);

    public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters) => Resolve(name);

    public void Dispose()
    {
      foreach (var a in _cache.Values) a?.Dispose();
      _cache.Clear();
    }
  }
}
