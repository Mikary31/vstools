/****************************************************************************
**
** Copyright (C) 2016 The Qt Company Ltd.
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

using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Controls;

namespace QtVsTools.Wizards.ProjectWizard
{
    using QtVsTools.Common;
    using Wizards.Common;

    using static QtVsTools.Common.EnumExt;

    public class LibraryWizard : ProjectTemplateWizard
    {
        LazyFactory Lazy { get; } = new LazyFactory();

        protected override Options TemplateType => Options.GUISystem
            | (WizardData.CreateStaticLibrary ? Options.StaticLibrary : Options.DynamicLibrary);

        enum NewLibClass
        {
            [String("classname")] ClassName,
            [String("sourcefilename")] SourceFileName,
            [String("headerfilename")] HeaderFileName,
            [String("include")] Include,
            [String("saveglobal")] GlobalHeader,
            [String("pro_lib_define")] LibDefine,
            [String("pro_lib_export")] LibExport,
        }

        protected override WizardData WizardData => Lazy.Get(() =>
            WizardData, () => new WizardData
            {
                DefaultModules = new List<string> { "QtCore" }
            });

        readonly List<string> LibExtraDefines = new List<string>();
        protected override IEnumerable<string> ExtraDefines => LibExtraDefines;

        protected override WizardWindow WizardWindow => Lazy.Get(() =>
            WizardWindow, () => new WizardWindow(title: "Qt Class Library Wizard")
            {
                new WizardIntroPage {
                    Data = WizardData,
                    Header = @"Welcome to the Qt Class Library Wizard",
                    Message = @"This wizard generates a Qt Class Library project. The "
                        + @"resulting library is linked dynamically with Qt."
                        + System.Environment.NewLine + System.Environment.NewLine
                        + @"To continue, click Next.",
                    PreviousButtonEnabled = false,
                    NextButtonEnabled = true,
                    FinishButtonEnabled = false,
                    CancelButtonEnabled = true
                },
                new ConfigPage {
                    Data = WizardData,
                    Header = @"Welcome to the Qt Class Library Wizard",
                    Message =
                            @"Setup the configurations you want to include in your project. "
                            + @"The recommended settings for this project are selected by default.",
                    PreviousButtonEnabled = true,
                    NextButtonEnabled = true,
                    FinishButtonEnabled = false,
                    CancelButtonEnabled = true
                },
                new LibraryClassPage {
                    Data = WizardData,
                    Header = @"Welcome to the Qt Class Library Wizard",
                    Message = @"This wizard generates a Qt Class Library project. The "
                        + @"resulting library is linked dynamically with Qt.",
                    PreviousButtonEnabled = true,
                    NextButtonEnabled = false,
                    FinishButtonEnabled = true,
                    CancelButtonEnabled = true
                }
            });

        protected override void BeforeWizardRun()
        {
            var safeprojectname = Parameter[NewProject.SafeName];
            safeprojectname = Regex.Replace(safeprojectname, @"[^a-zA-Z0-9_]", string.Empty);
            safeprojectname = Regex.Replace(safeprojectname, @"^[\d-]*\s*", string.Empty);
            var result = new Util.ClassNameValidationRule().Validate(safeprojectname, null);
            if (result != ValidationResult.ValidResult)
                safeprojectname = @"QtClassLibrary";

            WizardData.ClassName = safeprojectname;
            WizardData.ClassHeaderFile = safeprojectname + @".h";
            WizardData.ClassSourceFile = safeprojectname + @".cpp";
        }

        protected override void BeforeTemplateExpansion()
        {
            Parameter[NewLibClass.ClassName] = WizardData.ClassName;
            Parameter[NewLibClass.HeaderFileName] = WizardData.ClassHeaderFile;
            Parameter[NewLibClass.SourceFileName] = WizardData.ClassSourceFile;

            var include = new StringBuilder();
            if (UsePrecompiledHeaders)
                include.AppendLine(string.Format("#include \"{0}\"", PrecompiledHeader.Include));
            include.AppendLine(string.Format("#include \"{0}\"", WizardData.ClassHeaderFile));
            Parameter[NewLibClass.Include] = FormatParam(include);

            var safeprojectname = Parameter[NewProject.SafeName];
            Parameter[NewLibClass.GlobalHeader] = safeprojectname.ToLower();
            Parameter[NewLibClass.LibDefine] = safeprojectname.ToUpper() + "_LIB";
            Parameter[NewLibClass.LibExport] = safeprojectname.ToUpper() + "_EXPORT";

            LibExtraDefines.Add(Parameter[NewLibClass.LibDefine]);
            if (WizardData.CreateStaticLibrary)
                LibExtraDefines.Add("BUILD_STATIC");
        }
    }
}
