﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Xml;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.Ast;
using ICSharpCode.ILSpy;
using ICSharpCode.ILSpy.TextView;
using Mono.Cecil;

namespace Terraria.ModLoader.Setup
{
    public class DecompileTask : Task
    {
        private class EmbeddedAssemblyResolver : BaseAssemblyResolver
        {
            private Dictionary<string, AssemblyDefinition> cache = new Dictionary<string, AssemblyDefinition>();
            public ModuleDefinition baseModule;

            public override AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters) {
                lock (this) {
                    AssemblyDefinition assemblyDefinition;
                    if (cache.TryGetValue(name.FullName, out assemblyDefinition))
                        return assemblyDefinition;

                    //ignore references to other mscorlib versions, they are unneeded and produce namespace conflicts
                    if (name.Name == "mscorlib" && name.Version.Major != 4)
                        goto skip;

                    //look in the base module's embedded resources
                    if (baseModule != null) {
                        var resName = name.Name + ".dll";
                        var res =
                            baseModule.Resources.OfType<EmbeddedResource>()
                                .SingleOrDefault(r => r.Name.EndsWith(resName));
                        if (res != null)
                            assemblyDefinition = AssemblyDefinition.ReadAssembly(res.GetResourceStream(), parameters);
                    }

                    if (assemblyDefinition == null)
                        assemblyDefinition = base.Resolve(name, parameters);

                skip:
                    cache[name.FullName] = assemblyDefinition;
                    return assemblyDefinition;
                }
            }
        }

		public static ProgramSetting<bool> SingleDecompileThread = new ProgramSetting<bool>("SingleDecompileThread");
		public static ProgramSetting<string> SteamDir = new ProgramSetting<string>("SteamDir");

		public static string TerrariaPath { get { return Path.Combine(SteamDir.Get(), "Terraria.exe"); } }
		public static string TerrariaServerPath { get { return Path.Combine(SteamDir.Get(), "TerrariaServer.exe"); } }

	    public static bool SelectTerrariaDialog() {
		    while (true) {
				var dialog = new OpenFileDialog {
					InitialDirectory = Path.GetFullPath(Directory.Exists(SteamDir.Get()) ? SteamDir.Get() : Program.baseDir),
					Filter = "Terraria|Terraria.exe",
					Title = "Select Terraria.exe"
				};

				if (dialog.ShowDialog() != DialogResult.OK)
					return false;

			    string err = null;
			    if (Path.GetFileName(dialog.FileName) != "Terraria.exe")
				    err = "File must be named Terraria.exe";
				else if (!File.Exists(Path.Combine(Path.GetDirectoryName(dialog.FileName), "TerrariaServer.exe")))
					err = "TerrariaServer.exe does not exist in the same directory";

			    if (err != null) {
				    if (MessageBox.Show(err, "Invalid Selection", MessageBoxButtons.RetryCancel, MessageBoxIcon.Error) == DialogResult.Cancel)
					    return false;
			    }
			    else {
					SteamDir.Set(Path.GetDirectoryName(dialog.FileName));
				    return true;
			    }
		    }
	    }

        private static readonly CSharpLanguage lang = new CSharpLanguage();
        private static readonly Guid clientGuid = new Guid("3996D5FA-6E59-4FE4-9F2B-40EEEF9645D5");
        private static readonly Guid serverGuid = new Guid("85BF1171-A0DC-4696-BFA4-D6E9DC4E0830");
		public static readonly Version version = new Version(1, 3, 0, 7);

        public string srcDir;
        private ModuleDefinition clientModule;
        private ModuleDefinition serverModule;

        public string FullSrcDir { get { return Path.Combine(Program.baseDir, srcDir); } }

        public DecompileTask(ITaskInterface taskInterface, string srcDir) : base(taskInterface) {
            this.srcDir = srcDir;
        }

	    public override bool ConfigurationDialog() {
		    if (File.Exists(TerrariaPath) && File.Exists(TerrariaServerPath))
			    return true;

		    return (bool)taskInterface.Invoke(new Func<bool>(SelectTerrariaDialog));
	    }

	    public override bool StartupWarning() {
            return MessageBox.Show(
                    "Decompilation may take a long time (1-3 hours) and consume a lot of RAM (2GB will not be enough)",
                    "Ready to Decompile", MessageBoxButtons.OKCancel, MessageBoxIcon.Information)
                == DialogResult.OK;
        }

        public override void Run() {
            taskInterface.SetStatus("Deleting Old Src");

            if (Directory.Exists(FullSrcDir))
                Directory.Delete(FullSrcDir, true);

            var resolver = new EmbeddedAssemblyResolver();
            var readParams = new ReaderParameters() { AssemblyResolver = resolver };

            taskInterface.CancellationToken().ThrowIfCancellationRequested();
            taskInterface.SetStatus("Loading Terraria.exe");
			clientModule = ModuleDefinition.ReadModule(TerrariaPath, readParams);

            taskInterface.CancellationToken().ThrowIfCancellationRequested();
            taskInterface.SetStatus("Loading TerrariaServer.exe");
            serverModule = ModuleDefinition.ReadModule(TerrariaServerPath, readParams);

            resolver.baseModule = clientModule;

			VersionCheck(clientModule.Assembly);
			VersionCheck(serverModule.Assembly);

            var options = new DecompilationOptions {
                FullDecompilation = true,
                CancellationToken = taskInterface.CancellationToken(),
                SaveAsProjectDirectory = FullSrcDir
            };

            var clientSources = GetCodeFiles(clientModule, options).ToList();
            var serverSources = GetCodeFiles(serverModule, options).ToList();
            var clientResources = GetResourceFiles(clientModule, options).ToList();
            var serverResources = GetResourceFiles(serverModule, options).ToList();

            var sources = CombineFiles(clientSources, serverSources, src => src.Key);
            var resources = CombineFiles(clientResources, serverResources, res => res.Item1);

            var items = new List<WorkItem>();
            
            items.AddRange(sources.Select(src => new WorkItem(
                "Decompiling: "+src.Key, () => DecompileSourceFile(src, options))));

            items.AddRange(resources.Select(res => new WorkItem(
                "Extracting: " + res.Item1, () => ExtractResource(res, options))));
            
            items.Add(new WorkItem("Writing Assembly Info",
                () => WriteAssemblyInfo(clientModule, options)));
            
            items.Add(new WorkItem("Writing Terraria"+lang.ProjectFileExtension,
                () => WriteProjectFile(clientModule, clientGuid, clientSources, clientResources, options)));

            items.Add(new WorkItem("Writing TerrariaServer"+lang.ProjectFileExtension,
                () => WriteProjectFile(serverModule, serverGuid, serverSources, serverResources, options)));
			
            items.Add(new WorkItem("Writing Terraria"+lang.ProjectFileExtension+".user",
				() => WriteProjectUserFile(clientModule, SteamDir.Get(), options)));

            items.Add(new WorkItem("Writing TerrariaServer"+lang.ProjectFileExtension+".user",
				() => WriteProjectUserFile(serverModule, SteamDir.Get(), options)));
            
            ExecuteParallel(items, maxDegree: SingleDecompileThread.Get() ? 1 : 0);
        }

	    private void VersionCheck(AssemblyDefinition assembly) {
			if (assembly.Name.Version != version)
				throw new Exception(string.Format("{0} version {1}. Expected {2}", 
					assembly.Name.Name, assembly.Name.Version, version));
	    }

#region ReflectedMethods
        private static readonly MethodInfo _IncludeTypeWhenDecompilingProject = typeof(CSharpLanguage)
            .GetMethod("IncludeTypeWhenDecompilingProject", BindingFlags.NonPublic | BindingFlags.Instance);

        public static bool IncludeTypeWhenDecompilingProject(TypeDefinition type, DecompilationOptions options) {
            return (bool)_IncludeTypeWhenDecompilingProject.Invoke(lang, new object[] { type, options });
        }

        private static readonly MethodInfo _WriteProjectFile = typeof(CSharpLanguage)
            .GetMethod("WriteProjectFile", BindingFlags.NonPublic | BindingFlags.Instance);

        public static void WriteProjectFile(TextWriter writer, IEnumerable<Tuple<string, string>> files, ModuleDefinition module) {
            _WriteProjectFile.Invoke(lang, new object[] { writer, files, module });
        }

        private static readonly MethodInfo _CleanUpName = typeof(DecompilerTextView)
            .GetMethod("CleanUpName", BindingFlags.NonPublic | BindingFlags.Static);

        public static string CleanUpName(string name) {
            return (string)_CleanUpName.Invoke(null, new object[] { name });
        }
#endregion

        //from ICSharpCode.ILSpy.CSharpLanguage
        private static IEnumerable<IGrouping<string, TypeDefinition>> GetCodeFiles(ModuleDefinition module, DecompilationOptions options) {
            return module.Types.Where(t => IncludeTypeWhenDecompilingProject(t, options))
                .GroupBy(type => {
                    var file = CleanUpName(type.Name) + lang.FileExtension;
                    return string.IsNullOrEmpty(type.Namespace) ? file : Path.Combine(CleanUpName(type.Namespace), file);
                }, StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<Tuple<string, EmbeddedResource>> GetResourceFiles(ModuleDefinition module, DecompilationOptions options) {
            return module.Resources.OfType<EmbeddedResource>().Select(res => {
                var path = res.Name;
                path = path.Replace("Terraria.Libraries.", "Terraria.Libraries\\");
                if (path.EndsWith(".dll")) {
                    var asmRef = module.AssemblyReferences.SingleOrDefault(r => path.EndsWith(r.Name + ".dll"));
                    if (asmRef != null)
                        path = path.Substring(0, path.Length - asmRef.Name.Length - 5) +
                        Path.DirectorySeparatorChar + asmRef.Name + ".dll";
                }
                return Tuple.Create(path, res);
            });
        }

        private static List<T> CombineFiles<T, K>(IEnumerable<T> client, IEnumerable<T> server, Func<T, K> key) {
            var list = client.ToList();
            var set = new HashSet<K>(list.Select(key));
            list.AddRange(server.Where(src => !set.Contains(key(src))));
            return list;
        }

        private static void ExtractResource(Tuple<string, EmbeddedResource> res, DecompilationOptions options) {
            var path = Path.Combine(options.SaveAsProjectDirectory, res.Item1);
            CreateParentDirectory(path);

            var s = res.Item2.GetResourceStream();
            s.Position = 0;
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
			    s.CopyTo(fs);
        }

        private static void DecompileSourceFile(IGrouping<string, TypeDefinition> src, DecompilationOptions options) {
            var path = Path.Combine(options.SaveAsProjectDirectory, src.Key);
            CreateParentDirectory(path);

            using (var w = new StreamWriter(path)) {
                var builder = new AstBuilder(
                    new DecompilerContext(src.First().Module) {
                        CancellationToken = options.CancellationToken,
                        Settings = options.DecompilerSettings
                    });

                foreach (var type in src)
                    builder.AddType(type);

                builder.GenerateCode(new PlainTextOutput(w));
            }
        }

        private static void WriteAssemblyInfo(ModuleDefinition module, DecompilationOptions options) {
            var path = Path.Combine(options.SaveAsProjectDirectory, Path.Combine("Properties", "AssemblyInfo" + lang.FileExtension));
            CreateParentDirectory(path);

            using (var w = new StreamWriter(path)) {
                var builder = new AstBuilder(
                    new DecompilerContext(module) {
                        CancellationToken = options.CancellationToken,
                        Settings = options.DecompilerSettings
                    });

                builder.AddAssembly(module, true);
                builder.GenerateCode(new PlainTextOutput(w));
            }
        }

        private static void WriteProjectFile(ModuleDefinition module, Guid guid,
                IEnumerable<IGrouping<string, TypeDefinition>> sources, 
                IEnumerable<Tuple<string, EmbeddedResource>> resources,
                DecompilationOptions options) {

            //flatten the file list
            var files = sources.Select(src => Tuple.Create("Compile", src.Key))
                .Concat(resources.Select(res => Tuple.Create("EmbeddedResource", res.Item1)))
                .Concat(new[] { Tuple.Create("Compile", Path.Combine("Properties", "AssemblyInfo" + lang.FileExtension)) });

            //fix the guid and add a value to the CommandLineArguments field so the method doesn't crash
            var claField = typeof(App).GetField("CommandLineArguments", BindingFlags.Static | BindingFlags.NonPublic);
            var claType = typeof(App).Assembly.GetType("ICSharpCode.ILSpy.CommandLineArguments");
            var claConstructor = claType.GetConstructors()[0];
            var claInst = claConstructor.Invoke(new object[] {Enumerable.Empty<string>()});
            var guidField = claType.GetField("FixedGuid");
            guidField.SetValue(claInst, guid);
            claField.SetValue(null, claInst);

            var path = Path.Combine(options.SaveAsProjectDirectory,
                Path.GetFileNameWithoutExtension(module.Name) + lang.ProjectFileExtension);
            CreateParentDirectory(path);

            using (var w = new StreamWriter(path))
                WriteProjectFile(w, files, module);
            using (var w = new StreamWriter(path, true))
                w.Write(Environment.NewLine);
        }

        private static void WriteProjectUserFile(ModuleDefinition module, string debugWorkingDir, DecompilationOptions options) {
            var path = Path.Combine(options.SaveAsProjectDirectory,
                Path.GetFileNameWithoutExtension(module.Name) + lang.ProjectFileExtension + ".user");
            CreateParentDirectory(path);

            using (var w = new StreamWriter(path))
                using (var xml = new XmlTextWriter(w)) {
                    xml.Formatting = Formatting.Indented;
                    xml.WriteStartDocument();
                    xml.WriteStartElement("Project", "http://schemas.microsoft.com/developer/msbuild/2003");
                    xml.WriteAttributeString("ToolsVersion", "4.0");
                    xml.WriteStartElement("PropertyGroup");
                    xml.WriteAttributeString("Condition", "'$(Configuration)' == 'Debug'");
                    xml.WriteStartElement("StartWorkingDirectory");
                    xml.WriteValue(debugWorkingDir);
                    xml.WriteEndElement();
                    xml.WriteEndElement();
                    xml.WriteEndDocument();
                }
        }
    }
}