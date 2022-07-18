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
using System.Linq;
using System.Windows.Forms;
using System.IO;
using EnvDTE;

namespace QtVsTools.Legacy
{
    using Core;

    public partial class FormProjectQtSettings : Form
    {
        private Project project;
        private QtProject qtProject;
        private ProjectQtSettings qtSettings;
        private string majorVersion;

        private struct ModuleMapItem
        {
            public readonly CheckBox checkbox;
            public readonly int moduleId;
            public bool initialValue;

            public ModuleMapItem(CheckBox cb, int mid)
            {
                checkbox = cb;
                moduleId = mid;
                initialValue = false;
            }
        }

        private readonly List<ModuleMapItem> moduleMap = new List<ModuleMapItem>();

        public FormProjectQtSettings(uint qtMajor)
        {
            InitializeComponent();
            okButton.Text = "OK";
            cancelButton.Text = "Cancel";
            tabControl1.TabPages[0].Text = "Properties";
            tabControl1.TabPages[1].Text = "Qt Modules";

            majorVersion = qtMajor.ToString();
            var modules = QtModules.Instance.GetAvailableModules(qtMajor)
                .Where(x => x.Selectable)
                .OrderBy(x => x.Name);
            foreach (var module in modules) {
                var checkBox = new CheckBox
                {
                    Margin = new Padding(6),
                    Location = new System.Drawing.Point(150, 150),
                    Name = module.LibraryPrefix,
                    UseVisualStyleBackColor = true,
                    AutoSize = true,
                };
                flowLayoutPanel1.Controls.Add(checkBox);
                checkBox.Text = module.Name;
                AddMapping(checkBox, module.Id);
            }

            KeyPress += FormProjectQtSettings_KeyPress;

            Shown += FormProjectQtSettings_Shown;
        }

        private void FormProjectQtSettings_Shown(object sender, EventArgs e)
        {
            Text = "Qt Project Settings";
        }

        private void AddMapping(CheckBox checkbox, int moduleId)
        {
            moduleMap.Add(new ModuleMapItem(checkbox, moduleId));
        }

        public void SetProject(Project pro)
        {
            project = pro;
            qtProject = QtProject.Create(project);
            InitModules();
            qtSettings = new ProjectQtSettings(project);
            OptionsPropertyGrid.SelectedObject = qtSettings;
        }

        private void FormProjectQtSettings_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == 27) {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }

        private void OkButton_Click(object sender, EventArgs e)
        {
            // Disable the buttons since some operations are quite expensive (e.g. changing
            // the Qt version) and take some to finish. Keeping the buttons enables allows to hit
            // the buttons several times resulting in successive executions of these operations.
            okButton.Enabled = false;
            cancelButton.Enabled = false;

            qtSettings.SaveSettings();
            SaveModules();
            okButton.DialogResult = DialogResult.OK;
            Close();
        }

        private void InitModules()
        {
            var versionManager = QtVersionManager.The();
            var qtVersion = qtProject.GetQtVersion();
            var install_path = versionManager.GetInstallPath(qtVersion) ?? string.Empty;

            for (var i = 0; i < moduleMap.Count; ++i) {
                var item = moduleMap[i];
                item.initialValue = qtProject.HasModule(item.moduleId);
                item.checkbox.Checked = item.initialValue;
                moduleMap[i] = item;

                // Disable if module not installed
                var versionInfo = versionManager.GetVersionInfo(qtVersion);
                var info = QtModules.Instance.Module(item.moduleId, versionInfo.qtMajor);
                if (info != null && versionInfo != null) {
                    var libraryPrefix = info.LibraryPrefix;
                    if (libraryPrefix.StartsWith("Qt", StringComparison.Ordinal))
                        libraryPrefix = "Qt" + majorVersion + libraryPrefix.Substring(2);
                    var full_path = Path.Combine(install_path, "lib",
                        string.Format("{0}{1}.lib", libraryPrefix, versionInfo.LibInfix()));
                    var fi = new System.IO.FileInfo(full_path);
                    item.checkbox.Enabled = fi.Exists;
                    if (fi.Exists == false) {
                        // Don't disable item if qtVersion not available
                        if (qtVersion != null)
                            item.checkbox.Checked = false;
                    }
                } else {
                    item.checkbox.Checked = false;
                }
            }
        }

        private void SaveModules()
        {
            qtProject = QtProject.Create(project);
            for (var i = 0; i < moduleMap.Count; ++i) {
                var item = moduleMap[i];
                var isModuleChecked = item.checkbox.Checked;
                if (isModuleChecked != item.initialValue) {
                    if (isModuleChecked)
                        qtProject.AddModule(item.moduleId);
                    else
                        qtProject.RemoveModule(item.moduleId);
                }
            }
        }

    }
}
