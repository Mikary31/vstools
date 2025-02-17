/****************************************************************************
**
** Copyright (C) 2022 The Qt Company Ltd.
** Contact: https://www.qt.io/licensing/
**
** This file is part of the Qt VS Tools.
**
** $QT_BEGIN_LICENSE:GPL-EXCEPT$
** Commercial License Usage
** Licensees holding valid commercial Qt licenses may use this file in
** accordance with the commercial license agreement provided with the
** Software or, alternatively, in accordance with the terms contained in
** a written agreement between you and The Qt Company. For licensing terms
** and conditions see https://www.qt.io/terms-conditions. For further
** information use the contact form at https://www.qt.io/contact-us.
**
** GNU General Public License Usage
** Alternatively, this file may be used under the terms of the GNU
** General Public License version 3 as published by the Free Software
** Foundation with exceptions as appearing in the file LICENSE.GPL3-EXCEPT
** included in the packaging of this file. Please review the following
** information to ensure the GNU General Public License requirements will
** be met: https://www.gnu.org/licenses/gpl-3.0.html.
**
** $QT_END_LICENSE$
**
****************************************************************************/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.VCProjectEngine;
#if VS2017
using Microsoft.Win32;
#endif
using EnvDTE;

using Process = System.Diagnostics.Process;

namespace QtVsTools.Core
{
    using Common;
    using static SyntaxAnalysis.RegExpr;

    public static class HelperFunctions
    {
        static LazyFactory StaticLazy { get; } = new LazyFactory();

        static readonly HashSet<string> _sources = new HashSet<string>(new[] { ".c", ".cpp", ".cxx" },
            StringComparer.OrdinalIgnoreCase);
        public static bool IsSourceFile(string fileName)
        {
            return _sources.Contains(Path.GetExtension(fileName));
        }

        static readonly HashSet<string> _headers = new HashSet<string>(new[] { ".h", ".hpp", ".hxx" },
            StringComparer.OrdinalIgnoreCase);
        public static bool IsHeaderFile(string fileName)
        {
            return _headers.Contains(Path.GetExtension(fileName));
        }

        public static bool IsUicFile(string fileName)
        {
            return ".ui".Equals(Path.GetExtension(fileName), StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsMocFile(string fileName)
        {
            return ".moc".Equals(Path.GetExtension(fileName), StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsQrcFile(string fileName)
        {
            return ".qrc".Equals(Path.GetExtension(fileName), StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsWinRCFile(string fileName)
        {
            return ".rc".Equals(Path.GetExtension(fileName), StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsTranslationFile(string fileName)
        {
            return ".ts".Equals(Path.GetExtension(fileName), StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsQmlFile(string fileName)
        {
            return ".qml".Equals(Path.GetExtension(fileName), StringComparison.OrdinalIgnoreCase);
        }

        public static void SetDebuggingEnvironment(Project prj)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            SetDebuggingEnvironment(prj, string.Empty);
        }

        public static void SetDebuggingEnvironment(Project prj, string solutionConfig)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            SetDebuggingEnvironment(prj, "PATH=$(QTDIR)\\bin;$(PATH)", false, solutionConfig);
        }

        public static void SetDebuggingEnvironment(Project prj, string envpath, bool overwrite)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            SetDebuggingEnvironment(prj, envpath, overwrite, string.Empty);
        }

        public static void SetDebuggingEnvironment(Project prj, string envpath, bool overwrite, string solutionConfig)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (QtProject.GetFormatVersion(prj) >= Resources.qtMinFormatVersion_Settings)
                return;

            // Get platform name from given solution configuration
            // or if not available take the active configuration
            var activePlatformName = string.Empty;
            if (string.IsNullOrEmpty(solutionConfig)) {
                // First get active configuration cause not given as parameter
                try {
                    var activeConf = prj.ConfigurationManager.ActiveConfiguration;
                    activePlatformName = activeConf.PlatformName;
                } catch {
                    Messages.Print("Could not get the active platform name.");
                }
            } else {
                activePlatformName = solutionConfig.Split('|')[1];
            }

            var vcprj = prj.Object as VCProject;
            foreach (VCConfiguration conf in vcprj.Configurations as IVCCollection) {
                // Set environment only for active (or given) platform
                var currentPlatform = conf.Platform as VCPlatform;
                if (currentPlatform == null || currentPlatform.Name != activePlatformName)
                    continue;

                var de = conf.DebugSettings as VCDebugSettings;
                if (de == null)
                    continue;

                // See: https://connect.microsoft.com/VisualStudio/feedback/details/619702
                // Project | Properties | Configuration Properties | Debugging | Environment
                //
                // Issue: Substitution of ";" to "%3b"
                // Answer: This behavior currently is by design as ';' is a special MSBuild
                // character and needs to be escaped. In the Project Properties we show this
                // escaped value, but it should be the original when we use it.
                envpath = envpath.Replace("%3b", ";");
                de.Environment = de.Environment.Replace("%3b", ";");

                var index = envpath.LastIndexOf(";$(PATH)", StringComparison.Ordinal);
                var withoutPath = (index >= 0 ? envpath.Remove(index) : envpath);

                if (overwrite || string.IsNullOrEmpty(de.Environment))
                    de.Environment = envpath;
                else if (!de.Environment.Contains(envpath) && !de.Environment.Contains(withoutPath)) {
                    var m = Regex.Match(de.Environment, "PATH\\s*=\\s*");
                    if (m.Success) {
                        de.Environment = Regex.Replace(de.Environment, "PATH\\s*=\\s*", withoutPath + ";");
                        if (!de.Environment.Contains("$(PATH)") && !de.Environment.Contains("%PATH%")) {
                            if (!de.Environment.EndsWith(";", StringComparison.Ordinal))
                                de.Environment = de.Environment + ";";
                            de.Environment += "$(PATH)";
                        }
                    } else {
                        if (!string.IsNullOrEmpty(de.Environment))
                            de.Environment += "\n";
                        de.Environment += envpath;
                    }
                }
            }
        }

        public static Project ProjectFromSolution(DTE dteObject, string fullName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            fullName = new FileInfo(fullName).FullName;
            foreach (var p in ProjectsInSolution(dteObject)) {
                if (p.FullName.Equals(fullName, StringComparison.OrdinalIgnoreCase))
                    return p;
            }
            return null;
        }

        /// <summary>
        /// Returns the normalized file path of a given file.
        /// </summary>
        /// <param name="name">file name</param>
        public static string NormalizeFilePath(string name)
        {
            var fi = new FileInfo(name);
            return fi.FullName;
        }

        public static string NormalizeRelativeFilePath(string path)
        {
            if (path == null)
                return ".\\";

            path = path.Trim();
            path = HelperFunctions.ToNativeSeparator(path);

            var tmp = string.Empty;
            while (tmp != path) {
                tmp = path;
                path = path.Replace("\\\\", "\\");
            }

            path = path.Replace("\"", "");

            if (path != "." && !IsAbsoluteFilePath(path)
                && !path.StartsWith(".\\", StringComparison.OrdinalIgnoreCase)
                && !path.StartsWith("$", StringComparison.OrdinalIgnoreCase)) {
                path = ".\\" + path;
            }
            if (path.EndsWith("\\", StringComparison.OrdinalIgnoreCase))
                path = path.Substring(0, path.Length - 1);

            return path;
        }

        public static bool IsAbsoluteFilePath(string path)
        {
            path = path.Trim();
            if (path.Length >= 2 && path[1] == ':')
                return true;
            return path.StartsWith("\\", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Reads lines from a .pro file that is opened with a StreamReader
        /// and concatenates strings that end with a backslash.
        /// </summary>
        /// <param name="streamReader"></param>
        /// <returns>the composite string</returns>
        private static string ReadProFileLine(StreamReader streamReader)
        {
            var line = streamReader.ReadLine();
            if (line == null)
                return null;

            line = line.TrimEnd(' ', '\t');
            while (line.EndsWith("\\", StringComparison.OrdinalIgnoreCase)) {
                line = line.Remove(line.Length - 1);
                var appendix = streamReader.ReadLine();
                if (appendix != null)
                    line += appendix.TrimEnd(' ', '\t');
            }
            return line;
        }

        /// <summary>
        /// Reads a .pro file and returns true if it is a subdirs template.
        /// </summary>
        /// <param name="profile">full name of .pro file to read</param>
        /// <returns>true if this is a subdirs file</returns>
        public static bool IsSubDirsFile(string profile)
        {
            StreamReader sr = null;
            try {
                sr = new StreamReader(profile);

                var line = string.Empty;
                while ((line = ReadProFileLine(sr)) != null) {
                    line = line.Replace(" ", string.Empty).Replace("\t", string.Empty);
                    if (line.StartsWith("TEMPLATE", StringComparison.Ordinal))
                        return line.StartsWith("TEMPLATE=subdirs", StringComparison.Ordinal);
                }
            } catch (Exception e) {
                Messages.DisplayErrorMessage(e);
            } finally {
                if (sr != null)
                    sr.Dispose();
            }
            return false;
        }

        /// <summary>
        /// Returns the relative path between a given file and a path.
        /// </summary>
        /// <param name="path">absolute path</param>
        /// <param name="file">absolute file name</param>
        public static string GetRelativePath(string path, string file)
        {
            if (file == null || path == null)
                return "";
            var fi = new FileInfo(file);
            var di = new DirectoryInfo(path);

            var fiArray = fi.FullName.Split(Path.DirectorySeparatorChar);
            var dir = di.FullName;
            while (dir.EndsWith("\\", StringComparison.OrdinalIgnoreCase))
                dir = dir.Remove(dir.Length - 1, 1);
            var diArray = dir.Split(Path.DirectorySeparatorChar);

            var minLen = fiArray.Length < diArray.Length ? fiArray.Length : diArray.Length;
            int i = 0, j = 0, commonParts = 0;

            while (i < minLen && fiArray[i].ToLower() == diArray[i].ToLower()) {
                commonParts++;
                i++;
            }

            if (commonParts < 1)
                return fi.FullName;

            var result = string.Empty;

            for (j = i; j < fiArray.Length; j++) {
                if (j == i)
                    result = fiArray[j];
                else
                    result += Path.DirectorySeparatorChar + fiArray[j];
            }
            while (i < diArray.Length) {
                result = "..\\" + result;
                i++;
            }
            //MessageBox.Show(path + "\n" + file + "\n" + result);
            if (result.StartsWith("..\\", StringComparison.Ordinal))
                return result;
            return ".\\" + result;
        }

        /// <summary>
        /// Since VS2010 it is possible to have VCCustomBuildTools without commandlines
        /// for certain filetypes. We are not interested in them and thus try to read the
        /// tool's commandline. If this causes an exception, we ignore it.
        /// There does not seem to be another way for checking which kind of tool it is.
        /// </summary>
        /// <param name="config">File configuration</param>
        /// <returns></returns>
        public static VCCustomBuildTool GetCustomBuildTool(VCFileConfiguration config)
        {
            if (config.File is VCFile file
                && file.ItemType == "CustomBuild"
                && config.Tool is VCCustomBuildTool tool) {
                    try {
                        _ = tool.CommandLine;
                    } catch {
                        return null;
                    }
                    return tool;
            }
            return null;
        }

        /// <summary>
        /// Since VS2010 we have to ensure, that a custom build tool is present
        /// if we want to use it. In order to do so, the ProjectItem's ItemType
        /// has to be "CustomBuild"
        /// </summary>
        /// <param name="projectItem">Project Item which needs to have custom build tool</param>
        public static void EnsureCustomBuildToolAvailable(ProjectItem projectItem)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            foreach (Property prop in projectItem.Properties) {
                if (prop.Name == "ItemType") {
                    if ((string)prop.Value != "CustomBuild")
                        prop.Value = "CustomBuild";
                    break;
                }
            }
        }

        public static string GetQtDirFromQMakeProject(Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var vcProject = project.Object as VCProject;
            if (vcProject == null)
                return null;

            try {
                foreach (VCConfiguration projectConfig in vcProject.Configurations as IVCCollection) {
                    var compiler = CompilerToolWrapper.Create(projectConfig);
                    if (compiler != null) {
                        var additionalIncludeDirectories = compiler.AdditionalIncludeDirectories;
                        if (additionalIncludeDirectories != null) {
                            foreach (var dir in additionalIncludeDirectories) {
                                var subdir = Path.GetFileName(dir);
                                if (subdir != "QtCore" && subdir != "QtGui")    // looking for Qt include directories
                                    continue;
                                var dirName = Path.GetDirectoryName(dir);    // cd ..
                                dirName = Path.GetDirectoryName(dirName);       // cd ..
                                if (!Path.IsPathRooted(dirName)) {
                                    var projectDir = Path.GetDirectoryName(project.FullName);
                                    dirName = Path.Combine(projectDir, dirName);
                                    dirName = Path.GetFullPath(dirName);
                                }
                                return dirName;
                            }
                        }
                    }

                    var linker = (VCLinkerTool)((IVCCollection)projectConfig.Tools).Item("VCLinkerTool");
                    if (linker != null) {
                        var linkerWrapper = new LinkerToolWrapper(linker);
                        var linkerPaths = linkerWrapper.AdditionalDependencies;
                        if (linkerPaths != null) {
                            foreach (var library in linkerPaths) {
                                var idx = library.IndexOf("\\lib\\qtmain.lib", StringComparison.OrdinalIgnoreCase);
                                if (idx == -1)
                                    idx = library.IndexOf("\\lib\\qtmaind.lib", StringComparison.OrdinalIgnoreCase);
                                if (idx == -1)
                                    idx = library.IndexOf("\\lib\\qtcore5.lib", StringComparison.OrdinalIgnoreCase);
                                if (idx == -1)
                                    idx = library.IndexOf("\\lib\\qtcored5.lib", StringComparison.OrdinalIgnoreCase);
                                if (idx == -1)
                                    continue;

                                var dirName = Path.GetDirectoryName(library);
                                dirName = Path.GetDirectoryName(dirName);   // cd ..
                                if (!Path.IsPathRooted(dirName)) {
                                    var projectDir = Path.GetDirectoryName(project.FullName);
                                    dirName = Path.Combine(projectDir, dirName);
                                    dirName = Path.GetFullPath(dirName);
                                }

                                return dirName;
                            }
                        }

                        linkerPaths = linkerWrapper.AdditionalLibraryDirectories;
                        if (linkerPaths != null) {
                            foreach (var libDir in linkerPaths) {
                                var dirName = libDir;
                                if (!Path.IsPathRooted(dirName)) {
                                    var projectDir = Path.GetDirectoryName(project.FullName);
                                    dirName = Path.Combine(projectDir, dirName);
                                    dirName = Path.GetFullPath(dirName);
                                }

                                if (File.Exists(dirName + "\\qtmain.lib") ||
                                    File.Exists(dirName + "\\qtmaind.lib") ||
                                    File.Exists(dirName + "\\QtCore5.lib") ||
                                    File.Exists(dirName + "\\QtCored5.lib")) {
                                    return Path.GetDirectoryName(dirName);
                                }
                            }
                        }
                    }
                }
            } catch { }

            return null;
        }

        /// <summary>
        /// Return true if the project is a VS tools project; false otherwise.
        /// </summary>
        /// <param name="proj">project</param>
        public static bool IsVsToolsProject(Project proj)
        {
            ThreadHelper.ThrowIfNotOnUIThread(); // C++ Project Type GUID
            if (proj == null || proj.Kind != "{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}")
                return false;
            return IsVsToolsProject(proj.Object as VCProject);
        }

        /// <summary>
        /// Return true if the project is a VS tools project; false otherwise.
        /// </summary>
        /// <param name="proj">project</param>
        public static bool IsVsToolsProject(VCProject proj)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!IsQtProject(proj))
                return false;

            if (QtProject.GetFormatVersion(proj) >= Resources.qtMinFormatVersion_Settings)
                return true;

            var envPro = proj.Object as Project;
            if (envPro.Globals == null || envPro.Globals.VariableNames == null)
                return false;

            foreach (var global in envPro.Globals.VariableNames as string[]) {
                if (global.StartsWith("Qt5Version", StringComparison.Ordinal)
                    && envPro.Globals.get_VariablePersists(global)) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Return true if the project is a Qt project; false otherwise.
        /// </summary>
        /// <param name="proj">project</param>
        public static bool IsQtProject(Project proj)
        {
            ThreadHelper.ThrowIfNotOnUIThread(); //C++ Project Type GUID
            if (proj == null || proj.Kind != "{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}")
                return false;
            return IsQtProject(proj.Object as VCProject);
        }

        /// <summary>
        /// Return true if the project is a Qt project; false otherwise.
        /// </summary>
        /// <param name="proj">project</param>
        public static bool IsQtProject(VCProject proj)
        {
            if (proj == null)
                return false;
            var keyword = proj.keyword;
            if (string.IsNullOrEmpty(keyword))
                return false;
            return keyword.StartsWith(Resources.qtProjectKeyword, StringComparison.Ordinal)
                || keyword.StartsWith(Resources.qtProjectV2Keyword, StringComparison.Ordinal);
        }

        /// <summary>
        /// Deletes the file's directory if it is empty (not deleting the file itself so it must
        /// have been deleted before) and every empty parent directory until the first, non-empty
        /// directory is found.
        /// </summary>
        /// <param term='file'>Start point of the deletion</param>
        public static void DeleteEmptyParentDirs(VCFile file)
        {
            var dir = file.FullPath.Remove(file.FullPath.LastIndexOf(Path.DirectorySeparatorChar));
            DeleteEmptyParentDirs(dir);
        }

        /// <summary>
        /// Deletes the directory if it is empty and every empty parent directory until the first,
        /// non-empty directory is found.
        /// </summary>
        /// <param term='file'>Start point of the deletion</param>
        public static void DeleteEmptyParentDirs(string directory)
        {
            var dirInfo = new DirectoryInfo(directory);
            while (dirInfo.Exists && dirInfo.GetFileSystemInfos().Length == 0) {
                var tmp = dirInfo;
                dirInfo = dirInfo.Parent;
                tmp.Delete();
            }
        }

        public static bool HasQObjectDeclaration(VCFile file)
        {
            return CxxFileContainsNotCommented(file,
                new[]
                {
                    "Q_OBJECT",
                    "Q_GADGET",
                    "Q_NAMESPACE"
                },
                StringComparison.Ordinal, true);
        }

        public static bool CxxFileContainsNotCommented(VCFile file, string str,
            StringComparison comparisonType, bool suppressStrings)
        {
            return CxxFileContainsNotCommented(file, new[] { str }, comparisonType, suppressStrings);
        }

        public static bool CxxFileContainsNotCommented(VCFile file, string[] searchStrings,
            StringComparison comparisonType, bool suppressStrings)
        {
            // Small optimization, we first read the whole content as a string and look for the
            // search strings. Once we found at least one, ...
            bool found = false;
            var content = string.Empty;
            try {
                using (StreamReader sr = new StreamReader(file.FullPath))
                    content = sr.ReadToEnd();

                foreach (var key in searchStrings) {
                    if (content.IndexOf(key, comparisonType) >= 0) {
                        found = true;
                        break;
                    }
                }
            } catch { }

            if (!found)
                return false;

            // ... we will start parsing the file again to see if the actual string is commented
            // or not. The combination of string.IndexOf(...) and string.Split(...) seems to be
            // way faster then reading the file line by line.
            found = false;
            CxxStreamReader cxxSr = null;
            try {
                cxxSr = new CxxStreamReader(content.Split(new[] { "\n", "\r\n" },
                    StringSplitOptions.RemoveEmptyEntries));
                string strLine;
                while (!found && (strLine = cxxSr.ReadLine(suppressStrings)) != null) {
                    foreach (var str in searchStrings) {
                        if (strLine.IndexOf(str, comparisonType) != -1) {
                            found = true;
                            break;
                        }
                    }
                }
                cxxSr.Close();
            } catch (Exception) {
                if (cxxSr != null)
                    cxxSr.Close();
            }
            return found;
        }

        public static void SetEnvironmentVariableEx(string environmentVariable, string variableValue)
        {
            try {
                Environment.SetEnvironmentVariable(environmentVariable, variableValue);
            } catch {
                throw new QtVSException(SR.GetString("HelperFunctions_CannotWriteEnvQTDIR"));
            }
        }

        /// <summary>
        /// Converts all directory separators of the path to the alternate character
        /// directory separator. For instance, FromNativeSeparators("c:\\winnt\\system32")
        /// returns "c:/winnt/system32".
        /// </summary>
        /// <param name="path">The path to convert.</param>
        /// <returns>Returns path using '/' as file separator.</returns>
        public static string FromNativeSeparators(string path)
        {
            return path.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        /// <summary>
        /// Converts all alternate directory separators characters of the path to the native
        /// directory separator. For instance, ToNativeSeparators("c:/winnt/system32")
        /// returns "c:\\winnt\\system32".
        /// </summary>
        /// <param name="path">The path to convert.</param>
        /// <returns>Returns path using '\' as file separator.</returns>
        public static string ToNativeSeparator(string path)
        {
            return path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        }

        public static string ChangePathFormat(string path)
        {
            return path.Replace('\\', '/');
        }

        public static string RemoveFileNameExtension(FileInfo fi)
        {
            var lastIndex = fi.Name.LastIndexOf(fi.Extension, StringComparison.Ordinal);
            return fi.Name.Remove(lastIndex, fi.Extension.Length);
        }

        public static bool IsInFilter(VCFile vcfile, FakeFilter filter)
        {
            var item = (VCProjectItem)vcfile;

            while ((item.Parent != null) && (item.Kind != "VCProject")) {
                item = (VCProjectItem)item.Parent;

                if (item.Kind == "VCFilter") {
                    var f = (VCFilter)item;
                    if (f.UniqueIdentifier != null
                        && f.UniqueIdentifier.ToLower() == filter.UniqueIdentifier.ToLower())
                        return true;
                }
            }
            return false;
        }

        public static void CollapseFilter(UIHierarchyItem item, UIHierarchy hierarchy, string nodeToCollapseFilter)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (string.IsNullOrEmpty(nodeToCollapseFilter))
                return;

            foreach (UIHierarchyItem innerItem in item.UIHierarchyItems) {
                if (innerItem.Name == nodeToCollapseFilter)
                    CollapseFilter(innerItem, hierarchy);
                else if (innerItem.UIHierarchyItems.Count > 0)
                    CollapseFilter(innerItem, hierarchy, nodeToCollapseFilter);
            }
        }

        public static void CollapseFilter(UIHierarchyItem item, UIHierarchy hierarchy)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var subItems = item.UIHierarchyItems;
            if (subItems != null) {
                foreach (UIHierarchyItem innerItem in subItems) {
                    if (innerItem.UIHierarchyItems.Count > 0) {
                        CollapseFilter(innerItem, hierarchy);

                        if (innerItem.UIHierarchyItems.Expanded) {
                            innerItem.UIHierarchyItems.Expanded = false;
                            if (innerItem.UIHierarchyItems.Expanded) {
                                innerItem.Select(vsUISelectionType.vsUISelectionTypeSelect);
                                hierarchy.DoDefaultAction();
                            }
                        }
                    }
                }
            }
            if (item.UIHierarchyItems.Expanded) {
                item.UIHierarchyItems.Expanded = false;
                if (item.UIHierarchyItems.Expanded) {
                    item.Select(vsUISelectionType.vsUISelectionTypeSelect);
                    hierarchy.DoDefaultAction();
                }
            }
        }

        // returns true if some exception occurs
        public static bool IsGenerated(VCFile vcfile)
        {
            try {
                return IsInFilter(vcfile, Filters.GeneratedFiles());
            } catch (Exception e) {
                MessageBox.Show(e.ToString());
                return true;
            }
        }

        // returns false if some exception occurs
        public static bool IsResource(VCFile vcfile)
        {
            try {
                return IsInFilter(vcfile, Filters.ResourceFiles());
            } catch (Exception) {
                return false;
            }
        }

        public static List<string> GetProjectFiles(Project pro, FilesToList filter)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            VCProject vcpro;
            try {
                vcpro = (VCProject)pro.Object;
            } catch (Exception e) {
                Messages.DisplayErrorMessage(e);
                return null;
            }

            var fileList = new List<string>();
            var configurationName = pro.ConfigurationManager.ActiveConfiguration.ConfigurationName;

            foreach (VCFile vcfile in (IVCCollection)vcpro.Files) {
                // Why project files are also returned?
                if (vcfile.ItemName.EndsWith(".vcxproj.filters", StringComparison.Ordinal))
                    continue;
                var excluded = false;
                var fileConfigurations = (IVCCollection)vcfile.FileConfigurations;
                foreach (VCFileConfiguration config in fileConfigurations) {
                    if (config.ExcludedFromBuild && config.MatchName(configurationName, false)) {
                        excluded = true;
                        break;
                    }
                }

                if (excluded)
                    continue;

                // can be in any filter
                if (IsTranslationFile(vcfile.Name) && (filter == FilesToList.FL_Translation))
                    fileList.Add(FromNativeSeparators(vcfile.RelativePath));

                // can also be in any filter
                if (IsWinRCFile(vcfile.Name) && (filter == FilesToList.FL_WinResource))
                    fileList.Add(FromNativeSeparators(vcfile.RelativePath));

                if (IsGenerated(vcfile)) {
                    if (filter == FilesToList.FL_Generated)
                        fileList.Add(FromNativeSeparators(vcfile.RelativePath));
                    continue;
                }

                if (IsResource(vcfile)) {
                    if (filter == FilesToList.FL_Resources)
                        fileList.Add(FromNativeSeparators(vcfile.RelativePath));
                    continue;
                }

                switch (filter) {
                case FilesToList.FL_UiFiles: // form files
                    if (IsUicFile(vcfile.Name))
                        fileList.Add(FromNativeSeparators(vcfile.RelativePath));
                    break;
                case FilesToList.FL_HFiles:
                    if (IsHeaderFile(vcfile.Name))
                        fileList.Add(FromNativeSeparators(vcfile.RelativePath));
                    break;
                case FilesToList.FL_CppFiles:
                    if (IsSourceFile(vcfile.Name))
                        fileList.Add(FromNativeSeparators(vcfile.RelativePath));
                    break;
                case FilesToList.FL_QmlFiles:
                    if (IsQmlFile(vcfile.Name))
                        fileList.Add(FromNativeSeparators(vcfile.RelativePath));
                    break;
                }
            }

            return fileList;
        }

        /// <summary>
        /// Removes a file reference from the project and moves the file to the "Deleted" folder.
        /// </summary>
        /// <param name="vcpro"></param>
        /// <param name="fileName"></param>
        public static void RemoveFileInProject(VCProject vcpro, string fileName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            fileName = new FileInfo(fileName).FullName;
            foreach (VCFile vcfile in (IVCCollection)vcpro.Files) {
                if (vcfile.FullPath.Equals(fileName, StringComparison.OrdinalIgnoreCase)) {
                    vcpro.RemoveFile(vcfile);
                    QtProject.Create(vcpro)?.MoveFileToDeletedFolder(vcfile);
                }
            }
        }

        public static Project GetSelectedProject(DTE dteObject)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (dteObject == null)
                return null;

            Array prjs = null;
            try {
                prjs = (Array)dteObject.ActiveSolutionProjects;
            } catch {
                // When VS2010 is started from the command line,
                // we may catch a "Unspecified error" here.
            }
            if (prjs == null || prjs.Length < 1)
                return null;

            // don't handle multiple selection... use the first one
            if (prjs.GetValue(0) is Project project)
                return project;
            return null;
        }

        public static Project GetActiveDocumentProject(DTE dteObject)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return dteObject?.ActiveDocument?.ProjectItem?.ContainingProject;
        }

        public static Project GetSingleProjectInSolution(DTE dteObject)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var projectList = ProjectsInSolution(dteObject);
            if (projectList.Count != 1)
                return null; // no way to know which one to select

            return projectList[0];
        }

        /// <summary>
        /// Returns the the current selected Qt Project. If not project
        /// is selected or if the selected project is not a Qt project
        /// this function returns null.
        /// </summary>
        public static Project GetSelectedQtProject(DTE dteObject)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // can happen sometimes shortly after starting VS
            if (ProjectsInSolution(dteObject).Count == 0)
                return null;

            var pro = GetSelectedProject(dteObject);
            if (pro == null) {
                if ((pro = GetSingleProjectInSolution(dteObject)) == null)
                    pro = GetActiveDocumentProject(dteObject);
            }
            return IsVsToolsProject(pro) ? pro : null;
        }

        public static VCFile[] GetSelectedFiles(DTE dteObject)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (GetSelectedQtProject(dteObject) == null)
                return null;

            if (dteObject.SelectedItems.Count <= 0)
                return null;

            var items = dteObject.SelectedItems;

            var files = new VCFile[items.Count + 1];
            for (var i = 1; i <= items.Count; ++i) {
                var item = items.Item(i);
                if (item.ProjectItem == null)
                    continue;

                VCProjectItem vcitem;
                try {
                    vcitem = (VCProjectItem)item.ProjectItem.Object;
                } catch (Exception) {
                    return null;
                }

                if (vcitem.Kind == "VCFile")
                    files[i - 1] = (VCFile)vcitem;
            }
            files[items.Count] = null;
            return files;
        }

        public static Image GetSharedImage(string name)
        {
            Image image = null;
            var a = Assembly.GetExecutingAssembly();
            using (var imgStream = a.GetManifestResourceStream(name)) {
                if (imgStream != null)
                    image = Image.FromStream(imgStream);
            }
            return image;
        }

        public static RccOptions ParseRccOptions(string cmdLine, VCFile qrcFile)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var pro = VCProjectToProject((VCProject)qrcFile.project);

            var rccOpts = new RccOptions(pro, qrcFile);

            if (cmdLine.Length > 0) {
                var cmdSplit = cmdLine.Split(' ', '\t');
                for (var i = 0; i < cmdSplit.Length; ++i) {
                    var lowercmdSplit = cmdSplit[i].ToLower();
                    if (lowercmdSplit.Equals("-threshold")) {
                        rccOpts.CompressFiles = true;
                        rccOpts.CompressThreshold = int.Parse(cmdSplit[i + 1]);
                    } else if (lowercmdSplit.Equals("-compress")) {
                        rccOpts.CompressFiles = true;
                        rccOpts.CompressLevel = int.Parse(cmdSplit[i + 1]);
                    }
                }
            }
            return rccOpts;
        }

        public static Project VCProjectToProject(VCProject vcproj)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return (Project)vcproj.Object;
        }

        public static List<Project> ProjectsInSolution(DTE dteObject)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (dteObject == null)
                return new List<Project>();

            var projects = new List<Project>();
            var solution = dteObject.Solution;
            if (solution != null) {
                var c = solution.Count;
                for (var i = 1; i <= c; ++i) {
                    try {
                        var prj = solution.Projects.Item(i);
                        if (prj == null)
                            continue;
                        addSubProjects(prj, ref projects);
                    } catch {
                        // Ignore this exception... maybe the next project is ok.
                        // This happens for example for Intel VTune projects.
                    }
                }
            }
            return projects;
        }

        private static void addSubProjects(Project prj, ref List<Project> projects)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // If the actual object of the project is null then the project was probably unloaded.
            if (prj.Object == null)
                return;

            if (prj.ConfigurationManager != null &&
                // Is this a Visual C++ project?
                prj.Kind == "{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}") {
                projects.Add(prj);
            } else {
                // In this case, prj is a solution folder
                addSubProjects(prj.ProjectItems, ref projects);
            }
        }

        private static void addSubProjects(ProjectItems subItems, ref List<Project> projects)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (subItems == null)
                return;

            foreach (ProjectItem item in subItems) {
                Project subprj = null;
                try {
                    subprj = item.SubProject;
                } catch {
                    // The property "SubProject" might not be implemented.
                    // This is the case for Intel Fortran projects. (QTBUG-11567)
                }
                if (subprj != null)
                    addSubProjects(subprj, ref projects);
            }
        }

        public static int GetMaximumCommandLineLength()
        {
            var epsilon = 10;       // just to be sure :)
            var os = Environment.OSVersion;
            if (os.Version.Major >= 6 ||
                (os.Version.Major == 5 && os.Version.Minor >= 1))
                return 8191 - epsilon;    // Windows XP and above
            return 2047 - epsilon;
        }

        /// <summary>
        /// Translates the machine type given as command line argument to the linker
        /// to the internal enum type VCProjectEngine.machineTypeOption.
        /// </summary>
        public static machineTypeOption TranslateMachineType(string cmdLineMachine)
        {
            switch (cmdLineMachine.ToUpper()) {
            case "AM33":
                return machineTypeOption.machineAM33;
            case "X64":
                return machineTypeOption.machineAMD64;
            case "ARM":
                return machineTypeOption.machineARM;
            case "EBC":
                return machineTypeOption.machineEBC;
            case "IA-64":
                return machineTypeOption.machineIA64;
            case "M32R":
                return machineTypeOption.machineM32R;
            case "MIPS":
                return machineTypeOption.machineMIPS;
            case "MIPS16":
                return machineTypeOption.machineMIPS16;
            case "MIPSFPU":
                return machineTypeOption.machineMIPSFPU;
            case "MIPSFPU16":
                return machineTypeOption.machineMIPSFPU16;
            case "MIPS41XX":
                return machineTypeOption.machineMIPSR41XX;
            case "SH3":
                return machineTypeOption.machineSH3;
            case "SH3DSP":
                return machineTypeOption.machineSH3DSP;
            case "SH4":
                return machineTypeOption.machineSH4;
            case "SH5":
                return machineTypeOption.machineSH5;
            case "THUMB":
                return machineTypeOption.machineTHUMB;
            case "X86":
                return machineTypeOption.machineX86;
            default:
                return machineTypeOption.machineNotSet;
            }
        }

        public static bool ArraysEqual(Array array1, Array array2)
        {
            if (array1 == array2)
                return true;

            if (array1 == null || array2 == null)
                return false;

            if (array1.Length != array2.Length)
                return false;

            for (var i = 0; i < array1.Length; i++) {
                if (!Equals(array1.GetValue(i), array2.GetValue(i)))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// This method copies the specified directory and all its child directories and files to
        /// the specified destination. The destination directory is created if it does not exist.
        /// </summary>
        public static void CopyDirectory(string directory, string targetPath)
        {
            var sourceDir = new DirectoryInfo(directory);
            if (!sourceDir.Exists)
                return;

            try {
                if (!Directory.Exists(targetPath))
                    Directory.CreateDirectory(targetPath);

                var files = sourceDir.GetFiles();
                foreach (var file in files) {
                    try {
                        file.CopyTo(Path.Combine(targetPath, file.Name), true);
                    } catch { }
                }
            } catch { }

            var subDirs = sourceDir.GetDirectories();
            foreach (var subDir in subDirs)
                CopyDirectory(subDir.FullName, Path.Combine(targetPath, subDir.Name));
        }

        /// <summary>
        /// Performs an in-place expansion of MS Build properties in the form $(PropertyName)
        /// and project item metadata in the form %(MetadataName).<para/>
        /// Returns: 'true' if expansion was successful, 'false' otherwise<para/>
        /// <paramref name="stringToExpand"/>: The string containing properties and/or metadata to
        /// expand. This string is passed by ref and expansion is performed in-place.<para/>
        /// <paramref name="project"/>: Current project.<para/>
        /// <paramref name="configName"/>: Name of selected configuration (e.g. "Debug").<para/>
        /// <paramref name="platformName"/>: Name of selected platform (e.g. "x64").<para/>
        /// <paramref name="filePath"/>(optional): Evaluation context.<para/>
        /// </summary>
        public static bool ExpandString(
            ref string stringToExpand,
            EnvDTE.Project project,
            string configName,
            string platformName,
            string filePath = null)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (project == null
                || string.IsNullOrEmpty(configName)
                || string.IsNullOrEmpty(platformName))
                return false;

            var vcProject = project.Object as VCProject;
            if (filePath == null) {
                var vcConfig = (from VCConfiguration _config
                                in (IVCCollection)vcProject.Configurations
                                where _config.Name == configName + "|" + platformName
                                select _config).FirstOrDefault();
                return ExpandString(ref stringToExpand, vcConfig);
            } else {
                var vcFile = (from VCFile _file in (IVCCollection)vcProject.Files
                              where _file.FullPath == filePath
                              select _file).FirstOrDefault();
                if (vcFile == null)
                    return false;

                var vcFileConfig = (from VCFileConfiguration _config
                                    in (IVCCollection)vcFile.FileConfigurations
                                    where _config.Name == configName + "|" + platformName
                                    select _config).FirstOrDefault();
                return ExpandString(ref stringToExpand, vcFileConfig);
            }
        }

        /// <summary>
        /// Performs an in-place expansion of MS Build properties in the form $(PropertyName)
        /// and project item metadata in the form %(MetadataName).<para/>
        /// Returns: 'true' if expansion was successful, 'false' otherwise<para/>
        /// <paramref name="stringToExpand"/>: The string containing properties and/or metadata to
        /// expand. This string is passed by ref and expansion is performed in-place.<para/>
        /// <paramref name="config"/>: Either a VCConfiguration or VCFileConfiguration object to
        /// use as provider of property expansion (through Evaluate()). Cannot be null.<para/>
        /// </summary>
        public static bool ExpandString(
            ref string stringToExpand,
            object config)
        {
            if (config == null)
                return false;

            /* try property expansion through VCConfiguration.Evaluate()
             * or VCFileConfiguration.Evaluate() */
            string expanded = stringToExpand;
            VCProject vcProj = null;
            VCFile vcFile = null;
            string configName = "", platformName = "";
            if (config is VCConfiguration vcConfig) {
                vcProj = vcConfig.project as VCProject;
                configName = vcConfig.ConfigurationName;
                if (vcConfig.Platform is VCPlatform vcPlatform)
                    platformName = vcPlatform.Name;
                try {
                    expanded = vcConfig.Evaluate(expanded);
                } catch { }
            } else {
                var vcFileConfig = config as VCFileConfiguration;
                if (vcFileConfig == null)
                    return false;
                vcFile = vcFileConfig.File as VCFile;
                if (vcFile != null)
                    vcProj = vcFile.project as VCProject;
                if (vcFileConfig.ProjectConfiguration is VCConfiguration vcProjConfig) {
                    configName = vcProjConfig.ConfigurationName;
                    if (vcProjConfig.Platform is VCPlatform vcPlatform)
                        platformName = vcPlatform.Name;
                }
                try {
                    expanded = vcFileConfig.Evaluate(expanded);
                } catch { }
            }

            /* fail-safe */
            foreach (Match propNameMatch in Regex.Matches(expanded, @"\$\(([^\)]+)\)")) {
                string propName = propNameMatch.Groups[1].Value;
                string propValue = "";
                switch (propName) {
                case "Configuration":
                case "ConfigurationName":
                    if (string.IsNullOrEmpty(configName))
                        return false;
                    propValue = configName;
                    break;
                case "Platform":
                case "PlatformName":
                    if (string.IsNullOrEmpty(platformName))
                        return false;
                    propValue = platformName;
                    break;
                default:
                    return false;
                }
                expanded = expanded.Replace(string.Format("$({0})", propName), propValue);
            }

            /* because item metadata is not expanded in Evaluate() */
            foreach (Match metaNameMatch in Regex.Matches(expanded, @"\%\(([^\)]+)\)")) {
                string metaName = metaNameMatch.Groups[1].Value;
                string metaValue = "";
                switch (metaName) {
                case "FullPath":
                    if (vcFile == null)
                        return false;
                    metaValue = vcFile.FullPath;
                    break;
                case "RootDir":
                    if (vcFile == null)
                        return false;
                    metaValue = Path.GetPathRoot(vcFile.FullPath);
                    break;
                case "Filename":
                    if (vcFile == null)
                        return false;
                    metaValue = Path.GetFileNameWithoutExtension(vcFile.FullPath);
                    break;
                case "Extension":
                    if (vcFile == null)
                        return false;
                    metaValue = Path.GetExtension(vcFile.FullPath);
                    break;
                case "RelativeDir":
                    if (vcProj == null || vcFile == null)
                        return false;
                    metaValue = Path.GetDirectoryName(GetRelativePath(
                        Path.GetDirectoryName(vcProj.ProjectFile),
                        vcFile.FullPath));
                    if (!metaValue.EndsWith("\\"))
                        metaValue += "\\";
                    if (metaValue.StartsWith(".\\"))
                        metaValue = metaValue.Substring(2);
                    break;
                case "Directory":
                    if (vcFile == null)
                        return false;
                    metaValue = Path.GetDirectoryName(GetRelativePath(
                        Path.GetPathRoot(vcFile.FullPath),
                        vcFile.FullPath));
                    if (!metaValue.EndsWith("\\"))
                        metaValue += "\\";
                    if (metaValue.StartsWith(".\\"))
                        metaValue = metaValue.Substring(2);
                    break;
                case "Identity":
                    if (vcProj == null || vcFile == null)
                        return false;
                    metaValue = GetRelativePath(
                        Path.GetDirectoryName(vcProj.ProjectFile),
                        vcFile.FullPath);
                    if (metaValue.StartsWith(".\\"))
                        metaValue = metaValue.Substring(2);
                    break;
                case "RecursiveDir":
                case "ModifiedTime":
                case "CreatedTime":
                case "AccessedTime":
                    return false;
                default:
                    var vcFileConfig = config as VCFileConfiguration;
                    if (vcFileConfig == null)
                        return false;
                    var propStoreTool = vcFileConfig.Tool as IVCRulePropertyStorage;
                    if (propStoreTool == null)
                        return false;
                    try {
                        metaValue = propStoreTool.GetEvaluatedPropertyValue(metaName);
                    } catch {
                        return false;
                    }
                    break;
                }
                expanded = expanded.Replace(string.Format("%({0})", metaName), metaValue);
            }

            stringToExpand = expanded;
            return true;
        }

#if VS2017
        private static string GetRegistrySoftwareString(string subKeyName, string valueName)
        {
            var keyName = new StringBuilder();
            keyName.Append(@"SOFTWARE\");
            if (System.Environment.Is64BitOperatingSystem && IntPtr.Size == 4)
                keyName.Append(@"WOW6432Node\");
            keyName.Append(subKeyName);
            try {
                using (var key = Registry.LocalMachine.OpenSubKey(keyName.ToString(), false)) {
                    if (key == null)
                        return ""; //key not found
                    RegistryValueKind valueKind = key.GetValueKind(valueName);
                    if (valueKind != RegistryValueKind.String
                        && valueKind != RegistryValueKind.ExpandString) {
                        return ""; //wrong value kind
                    }
                    Object objValue = key.GetValue(valueName);
                    if (objValue == null)
                        return ""; //error getting value
                    return objValue.ToString();
                }
            } catch {
                return "";
            }
        }
#endif

        public static string GetWindows10SDKVersion()
        {
#if VS2019 || VS2022
            // In Visual Studio 2019: WindowsTargetPlatformVersion=10.0
            // will be treated as "use latest installed Windows 10 SDK".
            // https://developercommunity.visualstudio.com/comments/407752/view.html
            return "10.0";
#else
            string versionWin10SDK = HelperFunctions.GetRegistrySoftwareString(
                @"Microsoft\Microsoft SDKs\Windows\v10.0", "ProductVersion");
            if (string.IsNullOrEmpty(versionWin10SDK))
                return versionWin10SDK;
            while (versionWin10SDK.Split(new char[] { '.' }).Length < 4)
                versionWin10SDK = versionWin10SDK + ".0";
            return versionWin10SDK;
#endif
        }

        static string _VCPath;
        public static string VCPath
        {
            set => _VCPath = value;
            get
            {
                if (!string.IsNullOrEmpty(_VCPath))
                    return _VCPath;
                else
                    return GetVCPathFromRegistry();
            }
        }

        private static string GetVCPathFromRegistry()
        {
#if VS2022
            Debug.Assert(false, "VCPath for VS2022 is not available through the registry");
            string vcPath = string.Empty;
#elif VS2019
            Debug.Assert(false, "VCPath for VS2019 is not available through the registry");
            string vcPath = string.Empty;
#elif VS2017
            string vsPath = GetRegistrySoftwareString(@"Microsoft\VisualStudio\SxS\VS7", "15.0");
            if (string.IsNullOrEmpty(vsPath))
                return "";
            string vcPath = Path.Combine(vsPath, "VC");
#endif
            return vcPath;
        }

        static Parser EnvVarParser => StaticLazy.Get(() => EnvVarParser, () =>
        {
            Token tokenName = new Token("name", (~Chars["=\r\n"]).Repeat(atLeast: 1));
            Token tokenValuePart = new Token("value_part", (~Chars[";\r\n"]).Repeat(atLeast: 1));
            Token tokenValue = new Token("value", (tokenValuePart | Chars[';']).Repeat())
            {
                new Rule<List<string>>
                {
                    Capture(_ => new List<string>()),
                    Update("value_part", (List<string> parts, string part) => parts.Add(part))
                }
            };
            Token tokenEnvVar = new Token("env_var", tokenName & "=" & tokenValue & LineBreak)
            {
                new Rule<KeyValuePair<string, List<string>>>
                {
                    Create("name", (string name)
                        => new KeyValuePair<string, List<string>>(name, null)),
                    Transform("value", (KeyValuePair<string, List<string>> prop, List<string> value)
                        => new KeyValuePair<string, List<string>>(prop.Key, value))
                }
            };
            return tokenEnvVar.Render();
        });

        public static bool SetVCVars(VersionInformation VersionInfo, ProcessStartInfo startInfo)
        {
            if (VersionInfo == null) {
                VersionInfo = QtVersionManager.The().GetVersionInfo(
                    QtVersionManager.The().GetDefaultVersion());
            }

            if (string.IsNullOrEmpty(VCPath))
                return false;

            // Select vcvars script according to host and target platforms
            bool osIs64Bit = System.Environment.Is64BitOperatingSystem;
            string comspecPath = Environment.GetEnvironmentVariable("COMSPEC");
            string vcVarsCmd = "";
            switch (VersionInfo.platform()) {
            case Platform.x86:
                vcVarsCmd = Path.Combine(VCPath, osIs64Bit
                        ? @"Auxiliary\Build\vcvarsamd64_x86.bat"
                        : @"Auxiliary\Build\vcvars32.bat");
                break;
            case Platform.x64:
                vcVarsCmd = Path.Combine(VCPath, osIs64Bit
                        ? @"Auxiliary\Build\vcvars64.bat"
                        : @"Auxiliary\Build\vcvarsx86_amd64.bat");
                break;
            case Platform.arm64:
                vcVarsCmd = Path.Combine(VCPath, osIs64Bit
                        ? @"Auxiliary\Build\vcvarsamd64_arm64.bat"
                        : @"Auxiliary\Build\vcvarsx86_arm64.bat");
                if (!File.Exists(vcVarsCmd)) {
                    vcVarsCmd = Path.Combine(VCPath, osIs64Bit
                            ? @"Auxiliary\Build\vcvars64.bat"
                            : @"Auxiliary\Build\vcvarsx86_amd64.bat");
                }
                break;
            }

            Messages.Print($"vcvars: {vcVarsCmd}");
            if (!File.Exists(vcVarsCmd)) {
                Messages.Print($"vcvars: NOT FOUND");
                return false;
            }

            // Run vcvars and print environment variables
            StringBuilder stdOut = new StringBuilder();
            string command =
                string.Format("/c \"{0}\" && set", vcVarsCmd);
            var vcVarsStartInfo = new ProcessStartInfo(comspecPath, command);
            vcVarsStartInfo.CreateNoWindow = true;
            vcVarsStartInfo.UseShellExecute = false;
            vcVarsStartInfo.RedirectStandardError = true;
            vcVarsStartInfo.RedirectStandardOutput = true;
            var process = Process.Start(vcVarsStartInfo);
            process.OutputDataReceived += (object sender, DataReceivedEventArgs e) =>
            {
                if (string.IsNullOrEmpty(e.Data))
                    return;
                e.Data.TrimEnd('\r', '\n');
                if (!string.IsNullOrEmpty(e.Data))
                    stdOut.Append($"{e.Data}\r\n");
            };
            process.BeginOutputReadLine();
            process.WaitForExit();
            bool ok = (process.ExitCode == 0);
            process.Close();
            if (!ok)
                return false;

            // Parse command output: copy environment variables to startInfo
            var envVars = EnvVarParser.Parse(stdOut.ToString())
                .GetValues<KeyValuePair<string, List<string>>>("env_var")
                .ToDictionary(envVar => envVar.Key, envVar => envVar.Value,
                    StringComparer.InvariantCultureIgnoreCase);
            foreach (var vcVar in envVars)
                startInfo.Environment[vcVar.Key] = string.Join(";", vcVar.Value);

            // Warn if cl.exe is not in PATH
            string clPath = envVars["PATH"]
                .Select(path => Path.Combine(path, "cl.exe"))
                .Where(pathToCl => File.Exists(pathToCl))
                .FirstOrDefault();
            Messages.Print($"cl: {clPath ?? "NOT FOUND"}");

            return true;
        }

        /// <summary>
        /// Rooted canonical path is the absolute path for the specified path string
        /// (cf. Path.GetFullPath()) without a trailing path separator.
        /// </summary>
        static string RootedCanonicalPath(string path)
        {
            try {
                return Path
                .GetFullPath(path)
                .TrimEnd(new char[] {
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar
                });
            } catch {
                return "";
            }
        }

        /// <summary>
        /// If the given path is relative and a sub-path of the current directory, returns
        /// a "relative canonical path", containing only the steps beyond the current directory.
        /// Otherwise, returns the absolute ("rooted") canonical path.
        /// </summary>
        public static string CanonicalPath(string path)
        {
            string canonicalPath = RootedCanonicalPath(path);
            if (!Path.IsPathRooted(path)) {
                string currentCanonical = RootedCanonicalPath(".");
                if (canonicalPath.StartsWith(currentCanonical,
                    StringComparison.InvariantCultureIgnoreCase)) {
                    return canonicalPath
                    .Substring(currentCanonical.Length)
                    .TrimStart(new char[] {
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar
                    });
                } else {
                    return canonicalPath;
                }
            } else {
                return canonicalPath;
            }
        }

        public static bool PathIsRelativeTo(string path, string subPath)
        {
            return CanonicalPath(path).EndsWith(CanonicalPath(subPath),
                StringComparison.InvariantCultureIgnoreCase);
        }

        public static string Unquote(string text)
        {
            text = text.Trim();
            if (string.IsNullOrEmpty(text)
                || text.Length < 3
                || !text.StartsWith("\"")
                || !text.EndsWith("\"")) {
                return text;
            }
            return text.Substring(1, text.Length - 2);
        }

        public static string NewProjectGuid()
        {
            return string.Format("{{{0}}}", Guid.NewGuid().ToString().ToUpper());
        }

        public static string SafePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            path = path.Replace("\"", "");
            if (!path.Contains(' '))
                return path;
            if (path.EndsWith("\\"))
                path += Path.DirectorySeparatorChar;
            return string.Format("\"{0}\"", path);
        }
    }
}
